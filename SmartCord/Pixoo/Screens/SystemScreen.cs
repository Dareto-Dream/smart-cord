namespace SmartCord.Pixoo.Screens;

public sealed class SystemScreen : IPixooScreen
{
    public string Id => "system";
    public string Label => "System (CPU / RAM)";

    public bool IsAvailable(PixooContext ctx) => ctx.System.RamTotalMb > 0;

    public void Render(PixooCanvas c, PixooContext ctx)
    {
        var s = ctx.System;
        ScreenChrome.Frame(c, "SYSTEM", Color.FromArgb(70, 130, 180));

        ScreenChrome.BigValue(c, 2, 12, ((int)Math.Round(s.CpuPct)).ToString(), "% CPU", ScreenChrome.Load(s.CpuPct));
        c.ProgressBar(2, 25, 60, 4, s.CpuPct / 100.0, ScreenChrome.Load(s.CpuPct), ScreenChrome.Track, ScreenChrome.Dim);

        ScreenChrome.Gauge(c, 32, "RAM",
            $"{s.RamUsedMb / 1024.0:0.0}/{s.RamTotalMb / 1024.0:0.0}G",
            s.RamFraction,
            ScreenChrome.Load(s.RamFraction * 100));

        ScreenChrome.Footer(c, ctx.Now, "up " + Uptime(s.Uptime));
    }

    private static string Uptime(TimeSpan up)
    {
        if (up.TotalDays >= 1)
        {
            return $"{(int)up.TotalDays}d {up.Hours}h";
        }
        return up.TotalHours >= 1 ? $"{(int)up.TotalHours}h {up.Minutes}m" : $"{up.Minutes}m";
    }
}
