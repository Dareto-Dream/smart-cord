namespace SmartCord.Pixoo;

/// <summary>What SmartCord wants shown on the Pixoo — filled from the controller,
/// rendered by <see cref="PixooDashboard"/>.</summary>
public sealed record PixooDashboardModel
{
    public bool Coding { get; init; }
    public string Headline { get; init; } = "SMARTCORD";
    public Color Accent { get; init; } = Color.FromArgb(88, 101, 242);

    public string? Language { get; init; }
    public string? Project { get; init; }
    public string Detail { get; init; } = "";

    public bool ShowProgress { get; init; }
    public double ProgressFraction { get; init; }
    public string ProgressText { get; init; } = "";

    public bool DiscordConnected { get; init; }
    public string DiscordText { get; init; } = "off";
    public Color StatusDot { get; init; } = Color.FromArgb(120, 120, 120);

    public DateTime Clock { get; init; } = DateTime.Now;
}

/// <summary>Paints a <see cref="PixooDashboardModel"/> onto a 64×64 canvas.</summary>
public static class PixooDashboard
{
    private static readonly Color White = Color.White;
    private static readonly Color Grey = Color.FromArgb(150, 154, 165);
    private static readonly Color Dim = Color.FromArgb(90, 93, 102);
    private static readonly Color Bg = Color.FromArgb(8, 8, 10);

    public static void Render(PixooCanvas c, PixooDashboardModel m)
    {
        c.Fill(Bg);

        // ── header bar ─────────────────────────────────────────────────
        c.Rect(0, 0, 64, 8, m.Accent, fill: true);
        c.DrawTextClipped(2, 2, 52, m.Headline.ToUpperInvariant(), White);
        c.Rect(59, 2, 4, 4, m.StatusDot, fill: true);

        if (m.Coding)
        {
            RenderCoding(c, m);
        }
        else
        {
            RenderStatus(c, m);
        }

        // ── footer: discord + clock ───────────────────────────────────
        c.HLine(0, 47, 64, Dim);

        c.Rect(2, 50, 3, 3, m.DiscordConnected ? Color.FromArgb(35, 165, 90) : Color.FromArgb(180, 60, 60), fill: true);
        c.DrawTextClipped(8, 50, 54, m.DiscordText, m.DiscordConnected ? Grey : Dim);

        c.DrawTextRight(62, 57, m.Clock.ToString("HH:mm"), Grey);
    }

    private static void RenderCoding(PixooCanvas c, PixooDashboardModel m)
    {
        var lang = string.IsNullOrWhiteSpace(m.Language) ? "CODE" : m.Language!.ToUpperInvariant();
        var scale = PixooCanvas.TextWidth(lang, 2) <= 62 ? 2 : 1;
        c.DrawText(2, 11, PixooCanvas.Fit(lang, 62, scale), White, scale);

        var y = scale == 2 ? 24 : 19;

        if (!string.IsNullOrWhiteSpace(m.Project))
        {
            c.DrawTextClipped(2, y, 60, m.Project!, m.Accent);
            y += 7;
        }

        if (!string.IsNullOrWhiteSpace(m.Detail))
        {
            c.DrawTextClipped(2, y, 60, m.Detail, Grey);
        }

        if (m.ShowProgress)
        {
            c.ProgressBar(2, 41, 60, 4, m.ProgressFraction, m.Accent, Color.FromArgb(28, 30, 36), Dim);
        }
    }

    private static void RenderStatus(PixooCanvas c, PixooDashboardModel m)
    {
        var head = m.Headline.ToUpperInvariant();
        var scale = PixooCanvas.TextWidth(head, 2) <= 62 ? 2 : 1;
        c.DrawTextCenter(15, head, White, scale);

        var y = scale == 2 ? 28 : 24;

        if (!string.IsNullOrWhiteSpace(m.Detail))
        {
            var lines = c.DrawTextWrapped(2, y, 60, 7, 2, m.Detail, Grey);
            y += lines * 7 + 2;
        }

        if (!string.IsNullOrWhiteSpace(m.Project) && y <= 40)
        {
            c.DrawTextClipped(2, y, 60, m.Project!, m.Accent);
        }
    }

    // ── a simple animated test pattern ────────────────────────────────

    public static void RenderTestPattern(PixooCanvas c, int tick)
    {
        c.Clear();
        for (var y = 0; y < 64; y++)
        {
            for (var x = 0; x < 64; x++)
            {
                var r = (x * 4 + tick) & 0xff;
                var g = (y * 4 + tick) & 0xff;
                var b = ((x + y) * 2 - tick) & 0xff;
                c.SetPixel(x, y, Color.FromArgb(r, g, b));
            }
        }
        c.Rect(8, 24, 48, 16, Color.Black, fill: true);
        c.Rect(8, 24, 48, 16, Color.White, fill: false);
        c.DrawTextCenter(29, "SMARTCORD", Color.White);
    }
}
