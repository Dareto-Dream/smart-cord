namespace SmartCord.Pixoo.Screens;

public sealed class GpuScreen : IPixooScreen
{
    public string Id => "gpu";
    public string Label => "GPU dashboard";

    public bool IsAvailable(PixooContext ctx) => ctx.Gpu is not null;

    public void Render(PixooCanvas c, PixooContext ctx)
    {
        var g = ctx.Gpu!;
        ScreenChrome.Frame(c, g.ShortName, Color.FromArgb(118, 185, 0), ScreenChrome.Heat(g.TempC));

        // big utilisation
        ScreenChrome.BigValue(c, 2, 12, g.UtilPct.ToString(), "% GPU", ScreenChrome.Load(g.UtilPct));
        c.ProgressBar(2, 25, 60, 4, g.UtilPct / 100.0, ScreenChrome.Load(g.UtilPct), ScreenChrome.Track, ScreenChrome.Dim);

        ScreenChrome.Gauge(c, 32, "VRAM",
            $"{g.MemUsedMb / 1024.0:0.0}/{g.MemTotalMb / 1024.0:0.0}G",
            g.MemFraction,
            ScreenChrome.Load(g.MemFraction * 100));

        var power = g.PowerW > 0 ? $"{g.PowerW:0}W" : "--";
        ScreenChrome.Footer(c, ctx.Now, $"{g.TempC}° {power}");
    }
}
