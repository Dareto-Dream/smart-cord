using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SmartCord.Integrations;
using SmartCord.LocalApi;
using SmartCord.Pixoo;
using SmartCord.Signals;

namespace SmartCord;

public enum PresenceMode
{
    AutoDetect,
    ManualPreset,
    Custom
}

/// <summary>
/// The headless brain of SmartCord: owns the presence service, poll loop, project
/// list, persisted state, idle / lock handling and settings hot-reload. The tray
/// shell and the main window are both just views bound to this — they read state
/// here, call the operations, and refresh on <see cref="Changed"/>.
/// </summary>
public sealed class SmartCordController : IDisposable
{
    private readonly string _workspaceDirectory;
    private readonly SettingsProvider _settingsProvider;
    private readonly ILogger<SmartCordController> _logger;
    private readonly HttpClient _httpClient = new();
    private readonly ProjectApiClient _projectApiClient;
    private readonly PresenceService _presenceService;
    private readonly StateStore _stateStore;
    private readonly AutoStartManager _autoStart;
    private readonly IdleDetector _idleDetector = new();
    private readonly ICodingSignalSource _foregroundWindow = new ForegroundWindowSource();
    private readonly MediaSessionSource _media = new();
    private readonly ActivityLog _activityLog;
    private readonly SecretStore _secretStore;
    private readonly IntegrationManager _integrations;
    private readonly PixooService _pixoo;
    private readonly LocalStatusServer _localApi;
    private readonly System.Windows.Forms.Timer _pollTimer = new();
    private readonly Control _uiSync = new();

    private SmartCordSettings _settings;
    private ProcessActivityDetector _activityDetector;
    private StatusPreset _fallbackPreset;
    private List<ProjectItem> _projects = [];
    private ProjectItem? _activeProject;
    private StatusPreset? _manualPreset;
    private CustomPresenceSettings _customPresence = new();
    private bool _useCustomPresence;
    private bool _minimizeToTray = true;
    private bool _codingDrivesPresence = true;
    private bool _sessionLocked;
    private bool _projectsRestored;
    private bool _streaming;
    private DateTime? _lastWeeklyRecapShown;
    private int? _savedActiveProjectId;

    public SmartCordController(SettingsProvider settingsProvider, ILoggerFactory loggerFactory)
    {
        _workspaceDirectory = Directory.GetCurrentDirectory();
        _settingsProvider = settingsProvider;
        _settings = settingsProvider.Current;
        _logger = loggerFactory.CreateLogger<SmartCordController>();

        _projectApiClient = new ProjectApiClient(_httpClient, _settings);
        _activityDetector = new ProcessActivityDetector(_settings.StatusPresets);
        _presenceService = new PresenceService(_settings, loggerFactory.CreateLogger<PresenceService>());
        _stateStore = new StateStore(loggerFactory.CreateLogger<StateStore>());
        _autoStart = new AutoStartManager(loggerFactory.CreateLogger<AutoStartManager>());
        _secretStore = new SecretStore(loggerFactory.CreateLogger<SecretStore>());
        _integrations = new IntegrationManager(_secretStore, _settings, loggerFactory);
        _pixoo = new PixooService(this, _settings, loggerFactory);
        _localApi = new LocalStatusServer(this, _settings, loggerFactory.CreateLogger<LocalStatusServer>());
        _activityLog = new ActivityLog(loggerFactory.CreateLogger<ActivityLog>());
        _fallbackPreset = ResolveFallbackPreset(_settings);

        _ = _uiSync.Handle; // force handle creation on the UI thread for cross-thread marshalling

        RestoreState();

        _integrations.Changed += OnIntegrationsChanged;
        _settingsProvider.Changed += OnSettingsChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;

        _pollTimer.Interval = PollIntervalMs(_settings);
        _pollTimer.Tick += async (_, _) => await RefreshPresenceAsync();
        _pollTimer.Start();
    }

    // ── events ──────────────────────────────────────────────────────────

    /// <summary>Raised (on the UI thread) whenever observable state changes.</summary>
    public event Action? Changed;

    /// <summary>Raised when something worth a tray balloon happens: (title, message, isError).</summary>
    public event Action<string, string, bool>? Notification;

    // ── observable state ────────────────────────────────────────────────

    public SmartCordSettings Settings => _settings;
    public string WorkspaceDirectory => _workspaceDirectory;
    public IReadOnlyList<ProjectItem> Projects => _projects;
    public ProjectItem? ActiveProject => _activeProject;
    public StatusPreset? ManualPreset => _manualPreset;
    public CustomPresenceSettings CustomPresence => _customPresence;
    public bool SessionLocked => _sessionLocked;

    public bool IsEnabled => _presenceService.IsEnabled;
    public bool IsConnected => _presenceService.IsConnected;
    public bool IsConfigured => _presenceService.IsConfigured;
    public string LastStatus => _presenceService.LastStatus;
    public PresencePayload? CurrentPayload => _presenceService.LastPayload;
    public DateTime CurrentStartedAtUtc => _presenceService.CurrentStartedAt;

    public TimeSpan IdleFor => _idleDetector.IdleFor;

    public IntegrationManager Integrations => _integrations;

    public PixooService Pixoo => _pixoo;

    public LocalStatusServer LocalApi => _localApi;

    /// <summary>Whatever Windows' own media session broker reports is playing, if anything.</summary>
    public MediaSnapshot? NowPlaying => _media.Latest;

    /// <summary>True while streaming/do-not-disturb mode is active — overrides every other presence mode.</summary>
    public bool IsStreaming => _streaming;

    /// <summary>When true (default), a live WakaTime/Hackatime heartbeat overrides auto-detect.</summary>
    public bool CodingActivityDrivesPresence
    {
        get => _codingDrivesPresence;
        set
        {
            _codingDrivesPresence = value;
            PersistState();
            _ = RefreshPresenceAsync();
        }
    }

    /// <summary>The current coding snapshot if it's actually driving presence, else null.</summary>
    public CodingSnapshot? DrivingCodingActivity =>
        _codingDrivesPresence && !_useCustomPresence && _manualPreset is null && !_sessionLocked
            ? ResolveCodingSnapshot()
            : null;

    /// <summary>
    /// A live WakaTime/Hackatime heartbeat is authoritative — it knows about coding
    /// happening on this machine (or any other one on the account) regardless of
    /// local input. Failing that, once idle/away kicks in there's nothing to show;
    /// otherwise ask what's actually focused before falling back to the blind
    /// process-scan preset in <see cref="ResolveAutoPreset"/>.
    /// </summary>
    private CodingSnapshot? ResolveCodingSnapshot()
    {
        if (_integrations.Current is { } snapshot)
        {
            return snapshot;
        }

        if (_idleDetector.IdleFor >= TimeSpan.FromMinutes(Math.Max(1, _settings.Presence.IdleAfterMinutes)))
        {
            return null;
        }

        return _foregroundWindow.Read();
    }

    /// <summary>Human text for the Dashboard "what's showing" line.</summary>
    public string DetectedActivityText
    {
        get
        {
            if (DrivingCodingActivity is { } snap)
            {
                return $"{snap.DetailsLine} — {snap.StateLine}  (via {snap.Source})";
            }
            var preset = ResolveAutoPreset();
            return $"{preset.Label} — {preset.Details}";
        }
    }

    public PresenceMode Mode => _useCustomPresence
        ? PresenceMode.Custom
        : _manualPreset is not null
            ? PresenceMode.ManualPreset
            : PresenceMode.AutoDetect;

    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        set
        {
            _minimizeToTray = value;
            PersistState();
            RaiseChanged();
        }
    }

    public bool AutoStartEnabled
    {
        get => _autoStart.IsEnabled;
        set
        {
            _autoStart.SetEnabled(value);
            RaiseChanged();
        }
    }

    /// <summary>The preset auto-detect resolves to right now (for the "detected" readout).</summary>
    public StatusPreset ResolveDetectedPreset() => ResolveAutoPreset();

    // ── kick-off ────────────────────────────────────────────────────────

    public void Start()
    {
        if (!_presenceService.IsConfigured)
        {
            Notification?.Invoke(
                "SmartCord needs a Client ID",
                "Set Discord:ClientId in appsettings.json, .env (SMARTCORD_Discord__ClientId), or an env var.",
                true);
        }

        _integrations.Start();
        _pixoo.Start();
        _localApi.Start();
        _ = RefreshProjectsAsync();
        _ = RefreshPresenceAsync();
    }

    private void OnIntegrationsChanged()
    {
        _ = RefreshPresenceAsync();
    }

    private static int PollIntervalMs(SmartCordSettings settings) =>
        Math.Max(3, settings.Presence.PollSeconds) * 1000;

    private static StatusPreset ResolveFallbackPreset(SmartCordSettings settings) =>
        settings.StatusPresets.FirstOrDefault(preset => preset.Key == "idle")
        ?? settings.StatusPresets.First();

    // ── operations ──────────────────────────────────────────────────────

    public void SetEnabled(bool enabled)
    {
        _presenceService.SetEnabled(enabled);
        PersistState();
        _ = RefreshPresenceAsync();
    }

    public void SetAutoDetect()
    {
        _manualPreset = null;
        _useCustomPresence = false;
        PersistState();
        _ = RefreshPresenceAsync();
    }

    public void SetManualPreset(StatusPreset preset)
    {
        _manualPreset = preset;
        _useCustomPresence = false;
        PersistState();
        _ = RefreshPresenceAsync();
    }

    public void SetActiveProject(ProjectItem? project)
    {
        _activeProject = project;
        PersistState();
        _ = RefreshPresenceAsync();
    }

    public void ApplyCustomPresence(CustomPresenceSettings custom)
    {
        _customPresence = custom;
        _manualPreset = null;
        _useCustomPresence = true;
        PersistState();
        _ = RefreshPresenceAsync();
    }

    /// <summary>
    /// One-click "I'm live, don't ping me": overrides whatever mode is active with a
    /// fixed streaming card and blanks the Pixoo, without touching the underlying
    /// mode/preset/project selection — turning it back off returns to exactly
    /// whatever was showing before.
    /// </summary>
    public void SetStreamingMode(bool enabled)
    {
        _streaming = enabled;
        PersistState();
        _pixoo.SetForceBlank(enabled);
        _ = RefreshPresenceAsync();
    }

    /// <summary>Fires the weekly recap toast immediately, bypassing the day/time gate — for testing or "show me now."</summary>
    public void ShowWeeklyRecapNow() => ShowWeeklyRecap();

    public async Task RefreshProjectsAsync()
    {
        try
        {
            _projects = await _projectApiClient.GetProjectsAsync(CancellationToken.None);

            if (!_projectsRestored)
            {
                if (_savedActiveProjectId is not null)
                {
                    _activeProject = _projects.FirstOrDefault(p => p.Id == _savedActiveProjectId);
                }
                _projectsRestored = true;
            }

            _activeProject ??= _projects.FirstOrDefault(project =>
                project.Progress.Contains("progress", StringComparison.OrdinalIgnoreCase) ||
                project.Metadata.Status.Contains("progress", StringComparison.OrdinalIgnoreCase));
            _activeProject ??= _projects.FirstOrDefault();
            await RefreshPresenceAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Project refresh failed");
            Notification?.Invoke("Project refresh failed", ex.Message, true);
            RaiseChanged();
        }
    }

    public async Task<DownloadResult> DownloadIconsAsync()
    {
        try
        {
            var result = await ImageAssetDownloader.DownloadAsync(_settings, _workspaceDirectory, CancellationToken.None);
            Notification?.Invoke("Icons downloaded", $"{result.DownloadedCount} files ready in icons.", false);
            RaiseChanged();
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Icon download failed");
            Notification?.Invoke("Icon download failed", ex.Message, true);
            throw;
        }
    }

    public Task ForceRefreshAsync() => RefreshPresenceAsync();

    public string IconsDirectory => Path.Combine(_workspaceDirectory, "icons");

    // ── state persistence ───────────────────────────────────────────────

    private void RestoreState()
    {
        var state = _stateStore.Load();

        _presenceService.SetEnabled(state.PresenceEnabled);
        _useCustomPresence = state.UseCustomPresence;
        _customPresence = state.CustomPresence ?? new CustomPresenceSettings();
        _minimizeToTray = state.MinimizeToTray;
        _codingDrivesPresence = state.CodingActivityDrivesPresence;
        _savedActiveProjectId = state.ActiveProjectId;
        _streaming = state.StreamingMode;
        _lastWeeklyRecapShown = state.LastWeeklyRecapShown;

        if (!string.IsNullOrWhiteSpace(state.ManualPresetKey))
        {
            _manualPreset = _settings.StatusPresets
                .FirstOrDefault(p => string.Equals(p.Key, state.ManualPresetKey, StringComparison.OrdinalIgnoreCase));
        }

        // Active project id is re-linked once the project list loads.
    }

    private void PersistState()
    {
        _stateStore.Save(new AppState
        {
            PresenceEnabled = _presenceService.IsEnabled,
            ManualPresetKey = _manualPreset?.Key,
            ActiveProjectId = _activeProject?.Id,
            UseCustomPresence = _useCustomPresence,
            CustomPresence = _customPresence,
            MinimizeToTray = _minimizeToTray,
            CodingActivityDrivesPresence = _codingDrivesPresence,
            StreamingMode = _streaming,
            LastWeeklyRecapShown = _lastWeeklyRecapShown
        });
    }

    // ── settings hot-reload ─────────────────────────────────────────────

    private void OnSettingsChanged(SmartCordSettings updated)
    {
        if (_uiSync.InvokeRequired)
        {
            _uiSync.BeginInvoke(() => OnSettingsChanged(updated));
            return;
        }

        _settings = updated;
        _activityDetector = new ProcessActivityDetector(updated.StatusPresets);
        _fallbackPreset = ResolveFallbackPreset(updated);
        _presenceService.UpdateSettings(updated);

        if (_manualPreset is not null)
        {
            _manualPreset = updated.StatusPresets
                .FirstOrDefault(p => string.Equals(p.Key, _manualPreset.Key, StringComparison.OrdinalIgnoreCase));
        }

        _pollTimer.Interval = PollIntervalMs(updated);
        _integrations.UpdateSettings(updated);
        _pixoo.UpdateSettings(updated);
        _localApi.UpdateSettings(updated);
        _ = RefreshPresenceAsync();
        _logger.LogInformation("Applied reloaded settings");
    }

    // ── session lock / unlock ───────────────────────────────────────────

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (_uiSync.InvokeRequired)
        {
            _uiSync.BeginInvoke(() => OnSessionSwitch(sender, e));
            return;
        }

        switch (e.Reason)
        {
            case SessionSwitchReason.SessionLock:
            case SessionSwitchReason.RemoteDisconnect:
            case SessionSwitchReason.ConsoleDisconnect:
                _sessionLocked = true;
                _logger.LogInformation("Session locked/disconnected ({Reason})", e.Reason);
                if (_settings.Presence.ClearOnLock)
                {
                    _presenceService.Clear("Session locked");
                }
                RaiseChanged();
                break;

            case SessionSwitchReason.SessionUnlock:
            case SessionSwitchReason.RemoteConnect:
            case SessionSwitchReason.ConsoleConnect:
                _sessionLocked = false;
                _logger.LogInformation("Session resumed ({Reason})", e.Reason);
                _ = RefreshPresenceAsync();
                break;
        }
    }

    // ── presence resolution ─────────────────────────────────────────────

    private async Task RefreshPresenceAsync()
    {
        _presenceService.Tick();
        await _media.RefreshAsync();
        CheckWeeklyRecap();

        if (_streaming)
        {
            _presenceService.Update(StreamingPayload());
            RaiseChanged();
            return;
        }

        if (_sessionLocked && _settings.Presence.ClearOnLock)
        {
            RaiseChanged();
            return;
        }

        if (_useCustomPresence)
        {
            var payload = _customPresence.ToPayload(_settings);
            if (string.IsNullOrWhiteSpace(payload.PrimaryButtonUrl))
            {
                payload.PrimaryButtonUrl = _activeProject is null
                    ? _settings.Api.ButtonUrl
                    : _settings.Frontend.ProjectUrlTemplate
                        .Replace("{id}", _activeProject.Id.ToString(), StringComparison.OrdinalIgnoreCase)
                        .Replace("{slug}", ImageAssetDownloader.NormalizeAssetKey(_activeProject.Title), StringComparison.OrdinalIgnoreCase);
            }

            _presenceService.Update(payload);
            RaiseChanged();
            return;
        }

        if (_manualPreset is null && _codingDrivesPresence && ResolveCodingSnapshot() is { } snapshot)
        {
            _activityLog.Record(snapshot.Project, snapshot.SecondsToday);
            _presenceService.Update(BuildCodingPayload(snapshot));
            RaiseChanged();
            return;
        }

        if (_manualPreset is null && _codingDrivesPresence && !_sessionLocked && ResolveListeningSnapshot() is { } listening)
        {
            _presenceService.Update(BuildListeningPayload(listening));
            RaiseChanged();
            return;
        }

        var preset = _manualPreset ?? ResolveAutoPreset();
        _presenceService.Update(preset, _activeProject);
        RaiseChanged();
    }

    /// <summary>
    /// "Listening" sits between actively coding and fully away: past the idle
    /// threshold (so it's not fighting WakaTime/foreground-window detection) but
    /// short of away, with music actually playing via Windows' media session broker.
    /// </summary>
    private MediaSnapshot? ResolveListeningSnapshot()
    {
        var idle = _idleDetector.IdleFor;
        var idleAfter = TimeSpan.FromMinutes(Math.Max(1, _settings.Presence.IdleAfterMinutes));
        var awayAfter = TimeSpan.FromMinutes(Math.Max(1, _settings.Presence.AwayAfterMinutes));

        if (idle < idleAfter || idle >= awayAfter)
        {
            return null;
        }

        return _media.Latest is { IsPlaying: true, HasTrack: true } snapshot ? snapshot : null;
    }

    private PresencePayload BuildListeningPayload(MediaSnapshot media) => new()
    {
        ModeKey = "listening:" + media.Title,
        Label = "Listening",
        Details = media.Title,
        State = string.IsNullOrWhiteSpace(media.Artist) ? "Listening" : $"by {media.Artist}",
        LargeImageKey = "spotify",
        LargeImageText = string.IsNullOrWhiteSpace(media.Album) ? "Now Playing" : media.Album,
        SmallImageKey = _settings.Presence.DefaultSmallImageKey,
        SmallImageText = "SmartCord",
        PrimaryButtonLabel = _activeProject is null ? "View Projects" : "View Project",
        PrimaryButtonUrl = ActiveProjectUrl(),
        SecondaryButtonLabel = "API",
        SecondaryButtonUrl = _settings.Api.ButtonUrl,
    };

    private PresencePayload StreamingPayload() => new()
    {
        ModeKey = "streaming",
        Label = "Streaming",
        Details = "🔴 Streaming",
        State = "Do not disturb",
        LargeImageKey = _settings.Presence.DefaultLargeImageKey,
        LargeImageText = "Streaming",
        SmallImageKey = _settings.Presence.DefaultSmallImageKey,
        SmallImageText = "DND",
        PrimaryButtonLabel = _activeProject is null ? "View Projects" : "View Project",
        PrimaryButtonUrl = ActiveProjectUrl(),
        SecondaryButtonLabel = "API",
        SecondaryButtonUrl = _settings.Api.ButtonUrl,
    };

    // ── weekly recap ────────────────────────────────────────────────────

    private void CheckWeeklyRecap()
    {
        var now = DateTime.Now;
        if (now.DayOfWeek != DayOfWeek.Sunday || now.TimeOfDay < TimeSpan.FromHours(18))
        {
            return;
        }
        if (_lastWeeklyRecapShown?.Date == now.Date)
        {
            return;
        }
        ShowWeeklyRecap();
    }

    private void ShowWeeklyRecap()
    {
        _lastWeeklyRecapShown = DateTime.Now;
        PersistState();

        var recap = _activityLog.BuildRecap(DateTime.Now);
        if (!recap.HasData)
        {
            Notification?.Invoke("This week", "No coding activity logged yet — check back after a few sessions.", false);
            return;
        }

        var message = recap.TopProject is null
            ? $"{CodingSnapshot.HumanizeDuration(recap.TotalSeconds)} logged across {recap.ProjectCount} project(s)."
            : $"{CodingSnapshot.HumanizeDuration(recap.TotalSeconds)} logged · most on {recap.TopProject} " +
              $"({CodingSnapshot.HumanizeDuration(recap.TopProjectSeconds)}).";

        Notification?.Invoke("Your week", message, false);
    }

    private PresencePayload BuildCodingPayload(CodingSnapshot snapshot)
    {
        var largeKey = string.IsNullOrWhiteSpace(snapshot.Language)
            ? _settings.Presence.DefaultLargeImageKey
            : ImageAssetDownloader.NormalizeAssetKey(snapshot.Language);

        var dashboardUrl = snapshot.Source switch
        {
            "WakaTime" => "https://wakatime.com/dashboard",
            "Hackatime" => "https://hackatime.hackclub.com",
            _ => "", // foreground-window-detected activity has no dashboard to link to
        };

        return new PresencePayload
        {
            ModeKey = "coding:" + snapshot.Source,
            Label = snapshot.Source,
            Details = snapshot.DetailsLine,
            State = snapshot.StateLine,
            LargeImageKey = largeKey,
            LargeImageText = snapshot.Language ?? snapshot.Source,
            SmallImageKey = _settings.Presence.DefaultSmallImageKey,
            SmallImageText = $"via {snapshot.Source}",
            PrimaryButtonLabel = _activeProject is null ? "View Projects" : "View Project",
            PrimaryButtonUrl = ActiveProjectUrl(),
            SecondaryButtonLabel = snapshot.Source,
            SecondaryButtonUrl = dashboardUrl,
        };
    }

    private string ActiveProjectUrl() =>
        _activeProject is null
            ? _settings.Api.ButtonUrl
            : _settings.Frontend.ProjectUrlTemplate
                .Replace("{id}", _activeProject.Id.ToString(), StringComparison.OrdinalIgnoreCase)
                .Replace("{slug}", ImageAssetDownloader.NormalizeAssetKey(_activeProject.Title), StringComparison.OrdinalIgnoreCase);

    private StatusPreset ResolveAutoPreset()
    {
        var idle = _idleDetector.IdleFor;

        if (idle >= TimeSpan.FromMinutes(Math.Max(1, _settings.Presence.AwayAfterMinutes)))
        {
            return AwayPreset();
        }

        if (idle >= TimeSpan.FromMinutes(Math.Max(1, _settings.Presence.IdleAfterMinutes)))
        {
            return _settings.StatusPresets.FirstOrDefault(p => p.Key == "idle") ?? _fallbackPreset;
        }

        return _activityDetector.Detect(_fallbackPreset);
    }

    private StatusPreset AwayPreset() =>
        _settings.StatusPresets.FirstOrDefault(p => string.Equals(p.Key, "away", StringComparison.OrdinalIgnoreCase))
        ?? new StatusPreset
        {
            Key = "away",
            Label = "Away",
            Details = "Away from keyboard",
            LargeImageKey = _settings.Presence.DefaultLargeImageKey,
            SmallImageKey = _settings.Presence.DefaultSmallImageKey
        };

    private void RaiseChanged()
    {
        if (_uiSync.IsDisposed)
        {
            return;
        }

        if (_uiSync.InvokeRequired)
        {
            try
            {
                _uiSync.BeginInvoke(RaiseChanged);
            }
            catch (ObjectDisposedException)
            {
                // shutting down
            }
            return;
        }

        _pixoo.OnStateChanged();
        Changed?.Invoke();
    }

    public void Dispose()
    {
        _pollTimer.Stop();
        _integrations.Changed -= OnIntegrationsChanged;
        _settingsProvider.Changed -= OnSettingsChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        PersistState();
        _pollTimer.Dispose();
        _pixoo.Dispose();
        _localApi.Dispose();
        _integrations.Dispose();
        _presenceService.Dispose();
        _httpClient.Dispose();
        _uiSync.Dispose();
    }
}
