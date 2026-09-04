namespace SmartCord.Pixoo.Screens;

/// <summary>Shared 64×64 furniture so every screen reads as the same family.</summary>
internal static class ScreenChrome
{
    public static readonly Color White = Color.White;
    public static readonly Color Grey = Color.FromArgb(150, 154, 165);
    public static readonly Color Dim = Color.FromArgb(92, 95, 104);
    public static readonly Color Track = Color.FromArgb(28, 30, 36);
    public static readonly Color Bg = Color.FromArgb(8, 8, 10);
    public static readonly Color Ok = Color.FromArgb(35, 165, 90);
    public static readonly Color Warn = Color.FromArgb(230, 170, 40);
    public static readonly Color Hot = Color.FromArgb(220, 70, 60);

    public static void Frame(PixooCanvas c, string title, Color accent, Color? dot = null)
    {
        c.Fill(Bg);
        c.Rect(0, 0, 64, 8, accent, fill: true);
        c.DrawTextClipped(2, 2, dot is null ? 60 : 52, title.ToUpperInvariant(), White);
        if (dot is { } d)
        {
            c.Rect(59, 2, 4, 4, d, fill: true);
        }
    }

    public static void Footer(PixooCanvas c, DateTime now, string? left = null)
    {
        c.HLine(0, 55, 64, Dim);
        if (!string.IsNullOrWhiteSpace(left))
        {
            c.DrawTextClipped(2, 58, 44, left!, Dim);
        }
        c.DrawTextRight(62, 58, now.ToString("HH:mm"), Grey);
    }

    /// <summary>A big scale-2 number with a small unit, left-aligned at (x, y).</summary>
    public static void BigValue(PixooCanvas c, int x, int y, string value, string unit, Color color)
    {
        var end = c.DrawText(x, y, value, color, 2);
        if (!string.IsNullOrEmpty(unit))
        {
            c.DrawText(end + 1, y + 5, unit, Grey);
        }
    }

    /// <summary>Labelled horizontal gauge: LABEL on the left, value on the right, bar under it.</summary>
    public static void Gauge(PixooCanvas c, int y, string label, string value, double fraction, Color fill)
    {
        c.DrawText(2, y, label, Grey);
        c.DrawTextRight(62, y, value, White);
        c.ProgressBar(2, y + 7, 60, 4, fraction, fill, Track, Dim);
    }

    public static Color Heat(int tempC) => tempC switch
    {
        >= 80 => Hot,
        >= 68 => Warn,
        _ => Ok,
    };

    public static Color Load(double pct) => pct switch
    {
        >= 90 => Hot,
        >= 60 => Warn,
        _ => Ok,
    };
}
