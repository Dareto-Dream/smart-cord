using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace SmartCord.Pixoo.Sources;

/// <summary>
/// Pulls the current track from Spectralis's local OBS overlay server
/// (<c>http://127.0.0.1:{port}/obs/{token}/state</c>) and listening stats from its
/// <c>scrobble-history.json</c>. Port + token are auto-discovered from
/// <c>%AppData%\Spectralis\settings-avalonia.json</c>.
/// </summary>
public sealed class SpectralisSampler
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Spectralis", "settings-avalonia.json");

    private static readonly string ScrobblePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Spectralis", "scrobble-history.json");

    private readonly HttpClient _http;
    private readonly ILogger _logger;

    private DateTime _scrobbleReadAt = DateTime.MinValue;
    private DateTime _scrobbleFileStamp = DateTime.MinValue;
    private (int today, int total, int minutes) _scrobbleStats;

    public SpectralisSampler(HttpClient http, ILogger logger)
    {
        _http = http;
        _logger = logger;
    }

    public NowPlayingSample? Latest { get; private set; }

    public async Task RefreshAsync(SpectralisSettings settings, CancellationToken ct)
    {
        if (!settings.Enabled)
        {
            Latest = null;
            return;
        }

        var (port, token) = ResolveEndpoint(settings);
        if (port == 0 || string.IsNullOrWhiteSpace(token))
        {
            Latest = null;
            return;
        }

        RefreshScrobbleStats();

        try
        {
            var url = $"http://{settings.Host}:{port}/obs/{token}/state";
            using var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                Latest = null;
                return;
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;
            var track = root.GetProperty("track");
            var playback = root.GetProperty("playback");

            var levels = Array.Empty<double>();
            if (root.TryGetProperty("visualizer", out var viz) &&
                viz.TryGetProperty("levels", out var lv) && lv.ValueKind == JsonValueKind.Array)
            {
                levels = lv.EnumerateArray().Select(e => e.GetDouble()).ToArray();
            }

            var accent = "#F59E0B";
            if (root.TryGetProperty("theme", out var theme) && theme.TryGetProperty("accent", out var ac))
            {
                accent = ac.GetString() ?? accent;
            }

            Latest = new NowPlayingSample(
                Title: Str(track, "title"),
                Artist: Str(track, "artist"),
                Album: Str(track, "album"),
                PositionSeconds: Num(playback, "positionSeconds"),
                DurationSeconds: Num(track, "durationSeconds"),
                IsPlaying: playback.TryGetProperty("isPlaying", out var p) && p.GetBoolean(),
                Levels: levels,
                Accent: accent,
                ScrobblesToday: _scrobbleStats.today,
                ScrobblesTotal: _scrobbleStats.total,
                MinutesToday: _scrobbleStats.minutes,
                At: DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Spectralis overlay poll failed");
            Latest = null;
        }
    }

    private (int port, string token) ResolveEndpoint(SpectralisSettings settings)
    {
        var port = settings.PortOverride;
        var token = settings.TokenOverride;

        if ((port == 0 || string.IsNullOrWhiteSpace(token)) && File.Exists(SettingsPath))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
                var root = doc.RootElement;
                if (root.TryGetProperty("EnableObsOverlay", out var en) && !en.GetBoolean())
                {
                    return (0, "");
                }
                if (port == 0 && root.TryGetProperty("ObsOverlayPort", out var pp) && pp.TryGetInt32(out var pv))
                {
                    port = pv;
                }
                if (string.IsNullOrWhiteSpace(token) && root.TryGetProperty("ObsOverlayToken", out var tk))
                {
                    token = tk.GetString() ?? "";
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not read Spectralis settings file");
            }
        }

        return (port, token);
    }

    private void RefreshScrobbleStats()
    {
        if (DateTime.UtcNow - _scrobbleReadAt < TimeSpan.FromSeconds(20) || !File.Exists(ScrobblePath))
        {
            return;
        }
        _scrobbleReadAt = DateTime.UtcNow;

        try
        {
            var stamp = File.GetLastWriteTimeUtc(ScrobblePath);
            if (stamp == _scrobbleFileStamp && _scrobbleStats != default)
            {
                // recompute "today" anyway — the day may have rolled over
            }
            _scrobbleFileStamp = stamp;

            using var doc = JsonDocument.Parse(File.ReadAllText(ScrobblePath));
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            var midnight = new DateTimeOffset(DateTime.Today).ToUnixTimeSeconds();
            var today = 0;
            var minutesToday = 0.0;
            var total = 0;
            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                total++;
                var ts = entry.TryGetProperty("Timestamp", out var t) && t.TryGetInt64(out var tv) ? tv : 0;
                if (ts >= midnight)
                {
                    today++;
                    if (entry.TryGetProperty("Duration", out var d) && d.TryGetDouble(out var dv))
                    {
                        minutesToday += dv / 60.0;
                    }
                }
            }
            _scrobbleStats = (today, total, (int)Math.Round(minutesToday));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read scrobble history");
        }
    }

    private static string Str(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) ? v.GetString() ?? "" : "";

    private static double Num(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.TryGetDouble(out var d) ? d : 0;
}
