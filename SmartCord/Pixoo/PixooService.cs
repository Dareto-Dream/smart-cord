using Microsoft.Extensions.Logging;
using SmartCord.Pixoo.Screens;
using SmartCord.Pixoo.Sources;

namespace SmartCord.Pixoo;

/// <summary>
/// Drives a Pixoo64 with a rotating set of 64×64 screens (SmartCord status,
/// Spectralis now-playing, GPU, system, AI compute). Samplers refresh on their
/// own cadences; a 1 s tick advances the rotation, renders the current screen and
/// pushes it if the frame changed.
/// </summary>
public sealed class PixooService : IDisposable
{
    private static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(90);

    private readonly SmartCordController _controller;
    private readonly ILogger<PixooService> _logger;
    private readonly PixooClient _client;
    private readonly PixooCanvas _canvas = new();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(4) };
    private readonly SemaphoreSlim _pushGate = new(1, 1);
    private readonly System.Threading.Timer _tick;
    private readonly CancellationTokenSource _cts = new();

    private readonly GpuSampler _gpu;
    private readonly SystemSampler _system = new();
    private readonly SpectralisSampler _spectralis;
    private readonly IReadOnlyList<IPixooScreen> _allScreens;

    private SmartCordSettings _settings;
    private long _lastFingerprint;
    private DateTimeOffset _lastPushUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastGpuSample = DateTimeOffset.MinValue;
    private DateTimeOffset _lastSysSample = DateTimeOffset.MinValue;
    private DateTimeOffset _lastSpectralisSample = DateTimeOffset.MinValue;
    private DateTime _lastAdvance = DateTime.MinValue;
    private int _rotationIndex;
    private string? _pinnedId;
    private string _currentScreenId = "status";
    private bool _initialised;
    private bool _disposed;
    private int _appliedBrightness = -1;

    public PixooService(SmartCordController controller, SmartCordSettings settings, ILoggerFactory loggerFactory)
    {
        _controller = controller;
        _settings = settings;
        _logger = loggerFactory.CreateLogger<PixooService>();
        _client = new PixooClient(settings.Pixoo.Host, loggerFactory.CreateLogger("Pixoo"));
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SmartCord/1.0");

        _gpu = new GpuSampler(loggerFactory.CreateLogger("Pixoo.Gpu"));
        _spectralis = new SpectralisSampler(_http, loggerFactory.CreateLogger("Pixoo.Spectralis"));
        _allScreens = [new StatusScreen(), new NowPlayingScreen(), new ComputeScreen(), new GpuScreen(), new SystemScreen()];

        _tick = new System.Threading.Timer(_ => _ = TickAsync(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public event Action? Changed;

    public bool Enabled => _settings.Pixoo.Enabled;
    public string Host => _settings.Pixoo.Host;
    public string LastResult { get; private set; } = "Not started";
    public PixooCanvas Canvas => _canvas;
    public string CurrentScreenId => _currentScreenId;
    public string? PinnedId => _pinnedId;

    /// <summary>id / label / enabled-in-settings / available-right-now — for the UI checklist.</summary>
    public IReadOnlyList<(string Id, string Label, bool Enabled, bool Available)> ScreenStatus()
    {
        var ctx = BuildContext();
        var enabled = _settings.Pixoo.Screens;
        return _allScreens
            .Select(s => (s.Id, s.Label, enabled.Contains(s.Id, StringComparer.OrdinalIgnoreCase), s.IsAvailable(ctx)))
            .ToList();
    }

    public void Start()
    {
        Reschedule();
        if (Enabled)
        {
            _ = RenderAndPushAsync(force: true);
        }
    }

    public void UpdateSettings(SmartCordSettings settings)
    {
        var wasEnabled = _settings.Pixoo.Enabled;
        _settings = settings;
        _client.Host = settings.Pixoo.Host;
        _initialised = false;
        Reschedule();

        if (settings.Pixoo.Enabled)
        {
            _ = RenderAndPushAsync(force: true);
        }
        else if (wasEnabled)
        {
            LastResult = "Disabled";
            Changed?.Invoke();
        }
    }

    public void OnStateChanged()
    {
        if (!_disposed && Enabled && DateTimeOffset.UtcNow - _lastPushUtc >= TimeSpan.FromSeconds(2))
        {
            _ = RenderAndPushAsync(force: false);
        }
    }

    public Task PushNowAsync() => RenderAndPushAsync(force: true);

    public void NextScreen() => Step(+1);
    public void PrevScreen() => Step(-1);
    public void ClearPin()
    {
        _pinnedId = null;
        _lastAdvance = DateTime.Now;
        _ = RenderAndPushAsync(force: true);
    }

    private void Step(int direction)
    {
        var available = AvailableScreens(BuildContext());
        if (available.Count == 0)
        {
            return;
        }
        var current = available.FindIndex(s => s.Id == _currentScreenId);
        if (current < 0)
        {
            current = 0;
        }
        var next = ((current + direction) % available.Count + available.Count) % available.Count;
        _pinnedId = available[next].Id;
        _ = RenderAndPushAsync(force: true);
    }

    public async Task<string> TestPatternAsync()
    {
        try
        {
            await EnsureInitialisedAsync(_cts.Token);
            for (var frame = 0; frame < 24; frame++)
            {
                PixooDashboard.RenderTestPattern(_canvas, frame * 12);
                await _client.PushFrameAsync(_canvas.ToBase64(), _cts.Token);
                await Task.Delay(120, _cts.Token);
            }
            _lastFingerprint = 0;
            LastResult = $"Test pattern sent {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            LastResult = $"Test failed: {ex.Message}";
        }
        Changed?.Invoke();
        return LastResult;
    }

    public async Task BlankAsync()
    {
        try
        {
            await EnsureInitialisedAsync(_cts.Token);
            _canvas.Clear();
            await _client.PushFrameAsync(_canvas.ToBase64(), _cts.Token);
            _lastFingerprint = _canvas.Fingerprint();
            _lastPushUtc = DateTimeOffset.UtcNow;
            LastResult = $"Blanked {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            LastResult = $"Blank failed: {ex.Message}";
        }
        Changed?.Invoke();
    }

    // ── rotation + rendering ───────────────────────────────────────────

    private void Reschedule()
    {
        _tick.Change(TimeSpan.FromSeconds(Enabled ? 2 : 3600), TimeSpan.FromSeconds(1));
    }

    private async Task TickAsync()
    {
        if (_disposed || !Enabled)
        {
            return;
        }

        await RefreshSamplersAsync();
        await RenderAndPushAsync(force: false);
    }

    private async Task RefreshSamplersAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var wantGpu = _settings.Pixoo.Screens.Any(s => s is "gpu" or "compute");
        var wantSpectralis = _settings.Pixoo.Spectralis.Enabled && _settings.Pixoo.Screens.Contains("nowplaying");

        if (now - _lastSysSample >= TimeSpan.FromSeconds(2))
        {
            _lastSysSample = now;
            try { _system.Refresh(); } catch (Exception ex) { _logger.LogDebug(ex, "system sample"); }
        }

        if (wantGpu && now - _lastGpuSample >= TimeSpan.FromSeconds(3))
        {
            _lastGpuSample = now;
            await _gpu.RefreshAsync(_settings.Pixoo.Compute.ProcessNames, _cts.Token);
        }

        if (wantSpectralis && now - _lastSpectralisSample >= TimeSpan.FromSeconds(3))
        {
            _lastSpectralisSample = now;
            await _spectralis.RefreshAsync(_settings.Pixoo.Spectralis, _cts.Token);
        }
    }

    private PixooContext BuildContext() => new(
        _controller, _settings,
        _gpu.Latest, _system.Latest, _spectralis.Latest, _gpu.Compute,
        DateTime.Now);

    private List<IPixooScreen> AvailableScreens(PixooContext ctx)
    {
        var order = _settings.Pixoo.Screens;
        var wanted = order
            .Select(id => _allScreens.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase)))
            .Where(s => s is not null)
            .Cast<IPixooScreen>()
            .Where(s => s.IsAvailable(ctx))
            .ToList();

        if (wanted.Count == 0)
        {
            wanted.Add(_allScreens.First(s => s.Id == "status"));
        }
        return wanted;
    }

    private IPixooScreen PickScreen(PixooContext ctx, List<IPixooScreen> available)
    {
        if (_pinnedId is not null)
        {
            var pinned = available.FirstOrDefault(s => s.Id == _pinnedId);
            if (pinned is not null)
            {
                _currentScreenId = pinned.Id;
                return pinned;
            }
        }

        var dwellSeconds = available.ElementAtOrDefault(_rotationIndex)?.DwellSeconds is > 0 and var d
            ? d
            : Math.Clamp(_settings.Pixoo.DwellSeconds, 3, 120);

        if (DateTime.Now - _lastAdvance >= TimeSpan.FromSeconds(dwellSeconds))
        {
            _rotationIndex++;
            _lastAdvance = DateTime.Now;
        }
        if (_rotationIndex >= available.Count || _rotationIndex < 0)
        {
            _rotationIndex = 0;
        }

        var screen = available[_rotationIndex];
        _currentScreenId = screen.Id;
        return screen;
    }

    private async Task RenderAndPushAsync(bool force)
    {
        if (_disposed || !Enabled || !await _pushGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            await EnsureInitialisedAsync(_cts.Token);

            var ctx = BuildContext();
            var available = AvailableScreens(ctx);
            var screen = PickScreen(ctx, available);
            screen.Render(_canvas, ctx);

            var fingerprint = _canvas.Fingerprint();
            var stale = DateTimeOffset.UtcNow - _lastPushUtc >= KeepAlive;
            var rateLimited = DateTimeOffset.UtcNow - _lastPushUtc < TimeSpan.FromSeconds(Math.Clamp(_settings.Pixoo.PushSeconds, 1, 60));

            if (!force && ((fingerprint == _lastFingerprint && !stale) || rateLimited))
            {
                return;
            }

            await _client.PushFrameAsync(_canvas.ToBase64(), _cts.Token);
            _lastFingerprint = fingerprint;
            _lastPushUtc = DateTimeOffset.UtcNow;
            LastResult = $"{screen.Id} · pushed {DateTime.Now:HH:mm:ss}";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            LastResult = $"Push failed: {ex.Message}";
            _logger.LogWarning(ex, "Pixoo push failed ({Host})", Host);
            _initialised = false;
        }
        finally
        {
            _pushGate.Release();
            Changed?.Invoke();
        }
    }

    private async Task EnsureInitialisedAsync(CancellationToken ct)
    {
        if (_initialised && _appliedBrightness == _settings.Pixoo.Brightness)
        {
            return;
        }
        await _client.ResetGifIdAsync(ct);
        await _client.SetBrightnessAsync(_settings.Pixoo.Brightness, ct);
        _appliedBrightness = _settings.Pixoo.Brightness;
        _initialised = true;
    }

    public void Dispose()
    {
        _disposed = true;
        _cts.Cancel();
        _tick.Dispose();
        _client.Dispose();
        _http.Dispose();
        _cts.Dispose();
        _pushGate.Dispose();
    }
}
