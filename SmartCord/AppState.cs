using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace SmartCord;

/// <summary>
/// The bits of runtime state that should survive a restart. Written to
/// <see cref="AppPaths.StateFile"/> whenever the user changes something in the tray.
/// </summary>
public sealed class AppState
{
    public bool PresenceEnabled { get; set; } = true;

    /// <summary>Key of the manually pinned <see cref="StatusPreset"/>, or null for auto-detect.</summary>
    public string? ManualPresetKey { get; set; }

    public int? ActiveProjectId { get; set; }

    public bool UseCustomPresence { get; set; }

    public CustomPresenceSettings CustomPresence { get; set; } = new();

    /// <summary>Closing the main window hides it to the tray instead of exiting.</summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>A live WakaTime/Hackatime heartbeat overrides auto-detect.</summary>
    public bool CodingActivityDrivesPresence { get; set; } = true;
}

public sealed class StateStore(ILogger<StateStore> logger)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly object _gate = new();

    public AppState Load()
    {
        try
        {
            if (File.Exists(AppPaths.StateFile))
            {
                var json = File.ReadAllText(AppPaths.StateFile);
                var state = JsonSerializer.Deserialize<AppState>(json, Options);
                if (state is not null)
                {
                    logger.LogInformation("Restored state from {File}", AppPaths.StateFile);
                    return state;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read state file; starting fresh");
        }

        return new AppState();
    }

    public void Save(AppState state)
    {
        try
        {
            AppPaths.EnsureCreated();
            var json = JsonSerializer.Serialize(state, Options);

            lock (_gate)
            {
                var temp = AppPaths.StateFile + ".tmp";
                File.WriteAllText(temp, json);
                File.Move(temp, AppPaths.StateFile, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist state");
        }
    }
}
