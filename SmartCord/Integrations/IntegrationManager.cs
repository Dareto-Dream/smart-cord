using Microsoft.Extensions.Logging;

namespace SmartCord.Integrations;

/// <summary>
/// Owns the time-tracking sources (WakaTime, Hackatime), polls them on a timer,
/// and exposes the single best "what am I coding right now" snapshot for the
/// presence resolver and the Integrations page.
/// </summary>
public sealed class IntegrationManager : IDisposable
{
    private readonly SecretStore _secrets;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly ILogger<IntegrationManager> _logger;
    private readonly System.Threading.Timer _pollTimer;
    private readonly SemaphoreSlim _pollGate = new(1, 1);
    private readonly CancellationTokenSource _cts = new();

    private SmartCordSettings _settings;

    public IntegrationManager(SecretStore secrets, SmartCordSettings settings, ILoggerFactory loggerFactory)
    {
        _secrets = secrets;
        _settings = settings;
        _logger = loggerFactory.CreateLogger<IntegrationManager>();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SmartCord/1.0");

        WakaTime = new WakaTimeSource(secrets, _http, loggerFactory.CreateLogger("WakaTime"), settings.Integrations.WakaTimeApiUrl);
        Hackatime = new HackatimeSource(secrets, _http, loggerFactory.CreateLogger("Hackatime"));
        Sources = [WakaTime, Hackatime];

        SeedFromEnvironment();

        _pollTimer = new System.Threading.Timer(_ => _ = RefreshAllAsync(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public WakaTimeSource WakaTime { get; }
    public HackatimeSource Hackatime { get; }
    public IReadOnlyList<CodingActivitySource> Sources { get; }

    public event Action? Changed;

    public bool AnyConnected => Sources.Any(s => s.IsConnected);

    /// <summary>The snapshot that should drive presence, or null if nothing is active.</summary>
    public CodingSnapshot? Current
    {
        get
        {
            var active = Sources
                .Select(s => s.Latest)
                .Where(s => s is { ActiveNow: true })
                .Cast<CodingSnapshot>()
                .ToList();
            if (active.Count == 0)
            {
                return null;
            }

            var preferred = _settings.Integrations.PreferredSource;
            if (!string.IsNullOrWhiteSpace(preferred))
            {
                var match = active.FirstOrDefault(s =>
                    string.Equals(s.Source, preferred, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                {
                    return match;
                }
            }

            return active.OrderByDescending(s => s.SecondsToday).ThenByDescending(s => s.CapturedAtUtc).First();
        }
    }

    public void Start()
    {
        RescheduleTimer();
        _ = RefreshAllAsync();
    }

    public void UpdateSettings(SmartCordSettings settings)
    {
        _settings = settings;
        WakaTime.SetApiBase(settings.Integrations.WakaTimeApiUrl);
        RescheduleTimer();
    }

    public async Task RefreshAllAsync()
    {
        if (!await _pollGate.WaitAsync(0))
        {
            return; // a poll is already in flight
        }

        try
        {
            foreach (var source in Sources)
            {
                await source.RefreshAsync(_cts.Token);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Integration poll failed");
        }
        finally
        {
            _pollGate.Release();
            Changed?.Invoke();
        }
    }

    private void RescheduleTimer()
    {
        var seconds = Math.Clamp(_settings.Integrations.PollSeconds, 30, 900);
        _pollTimer.Change(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(seconds));
    }

    private void SeedFromEnvironment()
    {
        SeedClient(WakaTime, "SMARTCORD_WAKATIME_CLIENT_ID", "SMARTCORD_WAKATIME_CLIENT_SECRET");
        SeedClient(Hackatime, "SMARTCORD_HACKATIME_CLIENT_ID", "SMARTCORD_HACKATIME_CLIENT_SECRET");
        // mantle's .env names, so a shared .env just works
        SeedClient(Hackatime, "HACKATIME_UID", "HACKATIME_SECRET_KEY");
        SeedKey(WakaTime, "SMARTCORD_WAKATIME_API_KEY", "WAKATIME_API_KEY");
        SeedKey(Hackatime, "SMARTCORD_HACKATIME_API_KEY", "HACKATIME_API_KEY", "HACKATIME_ACCOUNT_TOKEN");
    }

    private void SeedClient(CodingActivitySource source, string idVar, string secretVar)
    {
        if (source.Connection != ConnectionKind.None)
        {
            return;
        }
        var id = Environment.GetEnvironmentVariable(idVar);
        var secret = Environment.GetEnvironmentVariable(secretVar);
        if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(secret))
        {
            source.SaveOAuthClient(id, secret);
            _logger.LogInformation("Seeded {Source} OAuth client from environment (needs one-click connect)", source.DisplayName);
        }
    }

    private void SeedKey(CodingActivitySource source, params string[] vars)
    {
        if (source.Connection != ConnectionKind.None)
        {
            return;
        }
        foreach (var name in vars)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                source.SetApiKey(value);
                _logger.LogInformation("Seeded {Source} API key from environment", source.DisplayName);
                return;
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _pollTimer.Dispose();
        _http.Dispose();
        _cts.Dispose();
        _pollGate.Dispose();
    }
}
