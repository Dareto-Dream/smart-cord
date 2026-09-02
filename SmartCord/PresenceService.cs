using DiscordRPC;
using Microsoft.Extensions.Logging;

namespace SmartCord;

public sealed class PresenceService : IDisposable
{
    private readonly ILogger<PresenceService> _logger;
    private readonly object _gate = new();
    private SmartCordSettings _settings;
    private DiscordRpcClient? _client;
    private DateTime _startedAt = DateTime.UtcNow;
    private string _lastModeKey = "";
    private bool _enabled = true;
    private bool _connected;

    public PresenceService(SmartCordSettings settings, ILogger<PresenceService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_settings.Discord.ClientId);
    public bool IsEnabled => _enabled;
    public bool IsConnected => _connected;

    public string LastStatus { get; private set; } = "Not connected";

    /// <summary>The most recent payload handed to Discord — for the UI's live preview.</summary>
    public PresencePayload? LastPayload { get; private set; }

    /// <summary>When the current card's elapsed timer started (UTC).</summary>
    public DateTime CurrentStartedAt => _startedAt;

    public void UpdateSettings(SmartCordSettings settings)
    {
        var clientIdChanged = !string.Equals(
            _settings.Discord.ClientId, settings.Discord.ClientId, StringComparison.Ordinal);
        _settings = settings;

        if (clientIdChanged)
        {
            _logger.LogInformation("Discord client id changed; reconnecting");
            DisposeClient();
        }
    }

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;

        if (!enabled)
        {
            lock (_gate)
            {
                _client?.ClearPresence();
            }
            LastStatus = "Rich Presence disabled";
            _logger.LogInformation("Rich Presence disabled");
        }
    }

    /// <summary>Drop the current card without disabling the service (session lock, hard idle).</summary>
    public void Clear(string reason)
    {
        _lastModeKey = "";
        lock (_gate)
        {
            _client?.ClearPresence();
        }
        LastStatus = reason;
        _logger.LogInformation("Presence cleared: {Reason}", reason);
    }

    /// <summary>Called on the poll timer — keeps the RPC connection alive.</summary>
    public void Tick()
    {
        if (!_enabled || !IsConfigured)
        {
            return;
        }

        lock (_gate)
        {
            if (_client is { IsDisposed: true })
            {
                _logger.LogWarning("RPC client was disposed; rebuilding");
                _client = null;
                _connected = false;
            }
        }

        EnsureClient();
    }

    public void Update(StatusPreset preset, ProjectItem? project)
    {
        Update(BuildPayload(preset, project));
    }

    /// <summary>Maps a preset + active project to the card SmartCord would push, without sending it.</summary>
    public PresencePayload BuildPayload(StatusPreset preset, ProjectItem? project)
    {
        var projectName = string.IsNullOrWhiteSpace(project?.Title)
            ? _settings.Presence.FallbackState
            : project.Title;

        return new PresencePayload
        {
            ModeKey = preset.Key,
            Label = preset.Label,
            Details = preset.Details,
            State = projectName,
            LargeImageKey = FirstNonEmpty(preset.LargeImageKey, _settings.Presence.DefaultLargeImageKey),
            LargeImageText = preset.Label,
            SmallImageKey = FirstNonEmpty(preset.SmallImageKey, _settings.Presence.DefaultSmallImageKey),
            SmallImageText = "SmartCord",
            PrimaryButtonLabel = "View Project",
            PrimaryButtonUrl = BuildProjectUrl(project),
            SecondaryButtonLabel = "API",
            SecondaryButtonUrl = _settings.Api.ButtonUrl
        };
    }

    public void Update(PresencePayload payload)
    {
        // Record intent even when we won't push, so the UI preview stays honest.
        LastPayload = payload;

        if (!_enabled)
        {
            return;
        }

        if (!IsConfigured)
        {
            LastStatus = "Missing Discord Client ID";
            return;
        }

        EnsureClient();

        if (_client is null)
        {
            return;
        }

        if (!string.Equals(_lastModeKey, payload.ModeKey, StringComparison.OrdinalIgnoreCase))
        {
            _startedAt = DateTime.UtcNow;
            _lastModeKey = payload.ModeKey;
        }

        var presence = new RichPresence
        {
            Details = Truncate(FirstNonEmpty(payload.Details, "Coding"), 128),
            State = Truncate(FirstNonEmpty(payload.State, _settings.Presence.FallbackState), 128),
            Timestamps = new Timestamps(_startedAt),
            Assets = BuildAssets(payload),
            Buttons = BuildButtons(payload)
        };

        lock (_gate)
        {
            _client.SetPresence(presence);
        }

        LastStatus = $"{payload.Label}: {presence.Details} / {presence.State}";
    }

    private void EnsureClient()
    {
        lock (_gate)
        {
            if (_client is { IsDisposed: false })
            {
                return;
            }

            // autoEvents:true spins a background thread that pumps OnReady/OnError,
            // so LastStatus tracks the real connection state and reconnects work.
            _client = new DiscordRpcClient(_settings.Discord.ClientId, autoEvents: true)
            {
                Logger = new DiscordRpcLogBridge(_logger),
                SkipIdenticalPresence = true
            };

            _client.OnReady += (_, args) =>
            {
                _connected = true;
                LastStatus = $"Connected as {args.User.Username}";
                _logger.LogInformation("Discord RPC ready as {User}", args.User.Username);
            };
            _client.OnClose += (_, args) =>
            {
                _connected = false;
                LastStatus = $"Disconnected: {args.Reason}";
                _logger.LogWarning("Discord RPC closed: {Reason}", args.Reason);
            };
            _client.OnError += (_, args) =>
            {
                LastStatus = args.Message;
                _logger.LogWarning("Discord RPC error: {Message}", args.Message);
            };
            _client.OnConnectionFailed += (_, _) =>
            {
                _connected = false;
                _logger.LogDebug("Discord RPC connection attempt failed (is Discord running?)");
            };

            // The library retries the pipe on its own timer once initialized, so a
            // Discord restart heals without us re-initializing.
            _client.Initialize();
            _logger.LogInformation("Discord RPC client initialized for {ClientId}", _settings.Discord.ClientId);
        }
    }

    private void DisposeClient()
    {
        lock (_gate)
        {
            _client?.Dispose();
            _client = null;
            _connected = false;
        }
    }

    private string BuildProjectUrl(ProjectItem? project)
    {
        if (project is null)
        {
            return _settings.Api.ButtonUrl;
        }

        var slug = Slugify(project.Title);
        return _settings.Frontend.ProjectUrlTemplate
            .Replace("{id}", project.Id.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{slug}", slug, StringComparison.OrdinalIgnoreCase);
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
    }

    private static Assets BuildAssets(PresencePayload payload)
    {
        return new Assets
        {
            LargeImageKey = AssetKey(payload.LargeImageKey),
            LargeImageText = Truncate(payload.LargeImageText, 128),
            SmallImageKey = AssetKey(payload.SmallImageKey),
            SmallImageText = Truncate(payload.SmallImageText, 128)
        };
    }

    private static DiscordRPC.Button[] BuildButtons(PresencePayload payload)
    {
        var buttons = new List<DiscordRPC.Button>();

        AddButton(buttons, payload.PrimaryButtonLabel, payload.PrimaryButtonUrl);
        AddButton(buttons, payload.SecondaryButtonLabel, payload.SecondaryButtonUrl);

        return buttons.ToArray();
    }

    private static void AddButton(List<DiscordRPC.Button> buttons, string label, string url)
    {
        if (buttons.Count >= 2 || string.IsNullOrWhiteSpace(label) || !IsHttpUrl(url))
        {
            return;
        }

        buttons.Add(new DiscordRPC.Button
        {
            Label = Truncate(label.Trim(), 32),
            Url = url.Trim()
        });
    }

    private static bool IsHttpUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static string AssetKey(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "" : value.Trim().ToLowerInvariant();
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }

    private static string Slugify(string value)
    {
        return ImageAssetDownloader.NormalizeAssetKey(value);
    }

    public void Dispose()
    {
        DisposeClient();
    }

    /// <summary>Routes the DiscordRPC library's own logging into our file log.</summary>
    private sealed class DiscordRpcLogBridge(ILogger logger) : DiscordRPC.Logging.ILogger
    {
        public DiscordRPC.Logging.LogLevel Level { get; set; } = DiscordRPC.Logging.LogLevel.Warning;

        public void Trace(string message, params object[] args) => Write(LogLevel.Trace, message, args);
        public void Info(string message, params object[] args) => Write(LogLevel.Debug, message, args);
        public void Warning(string message, params object[] args) => Write(LogLevel.Debug, message, args);
        public void Error(string message, params object[] args) => Write(LogLevel.Warning, message, args);

        private void Write(LogLevel level, string message, object[] args)
        {
            var text = args is { Length: > 0 } ? SafeFormat(message, args) : message;
            logger.Log(level, "[DiscordRPC] {Message}", text);
        }

        private static string SafeFormat(string message, object[] args)
        {
            try
            {
                return string.Format(message, args);
            }
            catch (FormatException)
            {
                return message;
            }
        }
    }
}
