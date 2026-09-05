using Windows.Media.Control;

namespace SmartCord.Signals;

/// <summary>One poll's worth of "what's playing" from Windows' own media session broker.</summary>
public sealed record MediaSnapshot(
    string Title,
    string Artist,
    string Album,
    double PositionSeconds,
    double DurationSeconds,
    bool IsPlaying,
    DateTimeOffset CapturedAtUtc)
{
    public bool HasTrack => !string.IsNullOrWhiteSpace(Title);
}

/// <summary>
/// Reads whatever's currently playing via
/// <see cref="GlobalSystemMediaTransportControlsSessionManager"/> — the same broker
/// Windows' own volume-mixer "now playing" flyout reads from. Works for Spotify's
/// desktop app, a browser tab, VLC, anything that registers System Media Transport
/// Controls, with no API keys or OAuth. Tradeoff versus a real Spotify integration:
/// only sees local playback, and a couple of apps (rare) never register a session.
/// </summary>
public sealed class MediaSessionSource
{
    public MediaSnapshot? Latest { get; private set; }

    public async Task RefreshAsync()
    {
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var session = manager.GetCurrentSession();
            if (session is null)
            {
                Latest = null;
                return;
            }

            var props = await session.TryGetMediaPropertiesAsync();
            var timeline = session.GetTimelineProperties();
            var playback = session.GetPlaybackInfo();

            Latest = new MediaSnapshot(
                Title: props.Title ?? "",
                Artist: props.Artist ?? "",
                Album: props.AlbumTitle ?? "",
                PositionSeconds: Math.Max(0, timeline.Position.TotalSeconds),
                DurationSeconds: Math.Max(0, timeline.EndTime.TotalSeconds),
                IsPlaying: playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                CapturedAtUtc: DateTimeOffset.UtcNow);
        }
        catch
        {
            // No session broker, nothing playing, or the active app never
            // registered SMTC — all the same "nothing to show" from here.
            Latest = null;
        }
    }
}
