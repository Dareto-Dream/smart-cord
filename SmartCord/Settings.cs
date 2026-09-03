using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace SmartCord;

public sealed class SmartCordSettings
{
    public DiscordSettings Discord { get; set; } = new();
    public ApiSettings Api { get; set; } = new();
    public FrontendSettings Frontend { get; set; } = new();
    public PresenceSettings Presence { get; set; } = new();
    public IntegrationSettings Integrations { get; set; } = new();
    public PixooSettings Pixoo { get; set; } = new();
    public LocalApiSettings LocalApi { get; set; } = new();
    public List<StatusPreset> StatusPresets { get; set; } = [];
}

public sealed class LocalApiSettings
{
    /// <summary>Serve current presence as JSON + a browser-source overlay on loopback, for OBS.</summary>
    public bool Enabled { get; set; }

    public int Port { get; set; } = 5340;
}

public sealed class PixooSettings
{
    /// <summary>Push a status dashboard to a Divoom Pixoo64 on the LAN.</summary>
    public bool Enabled { get; set; }

    public string Host { get; set; } = "";

    /// <summary>0-100.</summary>
    public int Brightness { get; set; } = 60;

    /// <summary>Minimum seconds between frame pushes to the device.</summary>
    public int PushSeconds { get; set; } = 3;

    /// <summary>How long to dwell on each screen before rotating.</summary>
    public int DwellSeconds { get; set; } = 8;

    /// <summary>Daily coding-hours target for the status screen's progress bar.</summary>
    public double DailyTargetHours { get; set; } = 6;

    /// <summary>Screen ids to include in the rotation, in order. Unknown ids are ignored.</summary>
    public List<string> Screens { get; set; } = ["status", "nowplaying", "compute", "gpu", "system"];

    public SpectralisSettings Spectralis { get; set; } = new();

    public ComputeSettings Compute { get; set; } = new();
}

public sealed class SpectralisSettings
{
    public bool Enabled { get; set; } = true;

    public string Host { get; set; } = "127.0.0.1";

    /// <summary>Overrides the port read from Spectralis's settings file (0 = auto).</summary>
    public int PortOverride { get; set; }

    /// <summary>Overrides the OBS overlay token read from Spectralis's settings file.</summary>
    public string TokenOverride { get; set; } = "";
}

public sealed class ComputeSettings
{
    /// <summary>Process-name fragments that count as "AI work" when they show up on the GPU.</summary>
    public List<string> ProcessNames { get; set; } =
        ["python", "pt_main", "ollama", "llama", "koboldcpp", "comfyui", "comfy", "vllm", "text-generation", "lmstudio", "jupyter"];
}

public sealed class IntegrationSettings
{
    /// <summary>WakaTime API base — override for a self-hosted Wakapi instance.</summary>
    public string WakaTimeApiUrl { get; set; } = "https://wakatime.com/api/v1";

    /// <summary>How often to poll the connected time-tracking sources.</summary>
    public int PollSeconds { get; set; } = 120;

    /// <summary>"wakatime", "hackatime", or empty to pick whichever is active / has more hours.</summary>
    public string PreferredSource { get; set; } = "";
}

public sealed class DiscordSettings
{
    public string ClientId { get; set; } = "";
    public string ApplicationName { get; set; } = "I shouldve gone to bed";
}

public sealed class ApiSettings
{
    /// <summary>Your own backend, if you have one. Blank means no project list / no
    /// auto-fetched buttons -- everything else still works. Set via .env, not here.</summary>
    public string ProjectsUrl { get; set; } = "";
    public string ButtonUrl { get; set; } = "";
}

public sealed class FrontendSettings
{
    /// <summary>{id} gets swapped for the project id. Blank just means the presence
    /// card's project button doesn't show up.</summary>
    public string ProjectUrlTemplate { get; set; } = "";
}

public sealed class PresenceSettings
{
    public string FallbackState { get; set; } = "Avoiding sleep with code";
    public int PollSeconds { get; set; } = 10;
    public string DefaultLargeImageKey { get; set; } = "vscode";
    public string DefaultSmallImageKey { get; set; } = "terminal";

    /// <summary>Minutes of no input before the card switches to the idle preset.</summary>
    public int IdleAfterMinutes { get; set; } = 10;

    /// <summary>Minutes of no input before the card switches to "Away".</summary>
    public int AwayAfterMinutes { get; set; } = 15;

    /// <summary>Clear Discord presence entirely while the session is locked / disconnected.</summary>
    public bool ClearOnLock { get; set; } = true;
}

public sealed class StatusPreset
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Details { get; set; } = "";
    public string LargeImageKey { get; set; } = "";
    public string SmallImageKey { get; set; } = "";
    public List<string> ProcessNames { get; set; } = [];
}

/// <summary>
/// Holds the live <see cref="SmartCordSettings"/> and reloads it when
/// appsettings.json changes on disk, raising <see cref="Changed"/> so the tray can
/// re-apply. Poor man's <c>IOptionsMonitor</c> without dragging in the DI host.
/// </summary>
public sealed class SettingsProvider : IDisposable
{
    private readonly IConfigurationRoot _configuration;
    private readonly ILogger<SettingsProvider>? _logger;
    private readonly object _gate = new();
    private IDisposable? _reloadRegistration;

    public SettingsProvider(ILogger<SettingsProvider>? logger = null)
    {
        _logger = logger;
        _configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables("SMARTCORD_")
            .Build();

        Current = Bind(_configuration);

        _reloadRegistration = ChangeToken.OnChange(
            _configuration.GetReloadToken,
            OnConfigurationReloaded);
    }

    public SmartCordSettings Current { get; private set; }

    public event Action<SmartCordSettings>? Changed;

    private void OnConfigurationReloaded()
    {
        SmartCordSettings updated;
        lock (_gate)
        {
            updated = Bind(_configuration);
            Current = updated;
        }

        _logger?.LogInformation("Reloaded settings after appsettings change");
        Changed?.Invoke(updated);
    }

    private static SmartCordSettings Bind(IConfiguration configuration)
    {
        var settings = new SmartCordSettings();
        configuration.Bind(settings);

        if (settings.StatusPresets.Count == 0)
        {
            settings.StatusPresets.Add(new StatusPreset
            {
                Key = "idle",
                Label = "Idle",
                Details = "Thinking about code",
                LargeImageKey = settings.Presence.DefaultLargeImageKey,
                SmallImageKey = settings.Presence.DefaultSmallImageKey
            });
        }

        return settings;
    }

    public void Dispose()
    {
        _reloadRegistration?.Dispose();
        _reloadRegistration = null;
        (_configuration as IDisposable)?.Dispose();
    }
}
