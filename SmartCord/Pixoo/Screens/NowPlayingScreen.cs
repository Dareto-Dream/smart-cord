using System.Globalization;

namespace SmartCord.Pixoo.Screens;

/// <summary>
/// Spectralis "now playing" — track, progress, a mini spectrum from the overlay's
/// visualizer levels, and today's scrobble tally. When nothing's playing it falls
/// back to a listening-stats card.
/// </summary>
public sealed class NowPlayingScreen : IPixooScreen
{
    private int _scroll;
    private string _scrollKey = "";

    public string Id => "nowplaying";
    public string Label => "Spectralis now playing";

    public bool IsAvailable(PixooContext ctx) =>
        ctx.NowPlaying is { } n && (n.HasTrack || n.ScrobblesTotal > 0);

    public void Render(PixooCanvas c, PixooContext ctx)
    {
        var n = ctx.NowPlaying!;
        var accent = ParseHex(n.Accent, Color.FromArgb(245, 158, 11));

        if (n.HasTrack && n.IsPlaying)
        {
            RenderPlaying(c, ctx, n, accent);
        }
        else if (n.HasTrack)
        {
            RenderPaused(c, ctx, n, accent);
        }
        else
        {
            RenderStats(c, ctx, n, accent);
        }
    }

    private void RenderPlaying(PixooCanvas c, PixooContext ctx, Sources.NowPlayingSample n, Color accent)
    {
        ScreenChrome.Frame(c, "NOW PLAYING", accent, ScreenChrome.Ok);

        ScrollingTitle(c, 11, n.Title, ScreenChrome.White);
        c.DrawTextClipped(2, 19, 60, n.Artist, ScreenChrome.Grey);

        c.ProgressBar(2, 28, 60, 3, n.Progress, accent, ScreenChrome.Track);
        c.DrawText(2, 33, Mmss(n.PositionSeconds), ScreenChrome.Dim);
        c.DrawTextRight(62, 33, Mmss(n.DurationSeconds), ScreenChrome.Dim);

        Spectrum(c, 41, 12, n.Levels, accent);

        ScreenChrome.Footer(c, ctx.Now, $"{n.ScrobblesToday} today");
    }

    private void RenderPaused(PixooCanvas c, PixooContext ctx, Sources.NowPlayingSample n, Color accent)
    {
        ScreenChrome.Frame(c, "PAUSED", ScreenChrome.Dim, ScreenChrome.Warn);
        ScrollingTitle(c, 13, n.Title, ScreenChrome.Grey);
        c.DrawTextClipped(2, 22, 60, n.Artist, ScreenChrome.Dim);
        c.ProgressBar(2, 32, 60, 3, n.Progress, ScreenChrome.Dim, ScreenChrome.Track);
        StatsBlock(c, 40, n);
        ScreenChrome.Footer(c, ctx.Now);
    }

    private static void RenderStats(PixooCanvas c, PixooContext ctx, Sources.NowPlayingSample n, Color accent)
    {
        ScreenChrome.Frame(c, "LISTENING", accent);
        c.DrawTextCenter(15, "TODAY", ScreenChrome.Grey, 2);
        ScreenChrome.BigValue(c, 2, 28, n.ScrobblesToday.ToString(), "tracks", accent);
        c.DrawText(2, 42, $"{n.MinutesToday} min listened", ScreenChrome.Grey);
        ScreenChrome.Footer(c, ctx.Now, $"{n.ScrobblesTotal} total");
    }

    private static void StatsBlock(PixooCanvas c, int y, Sources.NowPlayingSample n)
    {
        c.DrawText(2, y, $"{n.ScrobblesToday} today", ScreenChrome.Grey);
        c.DrawTextRight(62, y, $"{n.MinutesToday}m", ScreenChrome.Grey);
    }

    private void ScrollingTitle(PixooCanvas c, int y, string title, Color color)
    {
        var width = PixooCanvas.TextWidth(title);
        if (width <= 62)
        {
            _scroll = 0;
            c.DrawText(2, y, title, color);
            return;
        }

        if (_scrollKey != title)
        {
            _scrollKey = title;
            _scroll = 0;
        }

        var span = width + 12; // trailing gap before the loop repeats
        var x = 2 - _scroll;
        c.DrawText(x, y, title, color);
        c.DrawText(x + span, y, title, color);
        // repaint the header bar edge the marquee just drew under is fine — text starts at y=11
        _scroll = (_scroll + 1) % span;
    }

    private static void Spectrum(PixooCanvas c, int baseY, int maxH, IReadOnlyList<double> levels, Color color)
    {
        if (levels.Count == 0)
        {
            return;
        }
        const int bars = 31;
        for (var i = 0; i < bars; i++)
        {
            var src = (int)((double)i / bars * levels.Count);
            var v = Math.Clamp(levels[src], 0, 1);
            var h = Math.Max(1, (int)Math.Round(v * maxH));
            var x = 1 + i * 2;
            c.VLine(x, baseY + (maxH - h), h, color);
        }
    }

    private static string Mmss(double seconds)
    {
        if (seconds < 0 || double.IsNaN(seconds))
        {
            seconds = 0;
        }
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

    private static Color ParseHex(string hex, Color fallback)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 6 &&
            int.TryParse(hex.AsSpan(0, 2), NumberStyles.HexNumber, null, out var r) &&
            int.TryParse(hex.AsSpan(2, 2), NumberStyles.HexNumber, null, out var g) &&
            int.TryParse(hex.AsSpan(4, 2), NumberStyles.HexNumber, null, out var b))
        {
            return Color.FromArgb(r, g, b);
        }
        return fallback;
    }
}
