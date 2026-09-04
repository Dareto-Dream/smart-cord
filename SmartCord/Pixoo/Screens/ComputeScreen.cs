namespace SmartCord.Pixoo.Screens;

/// <summary>Only in rotation while an AI process (see <c>Pixoo.Compute.ProcessNames</c>) is on the GPU.</summary>
public sealed class ComputeScreen : IPixooScreen
{
    public string Id => "compute";
    public string Label => "AI compute (when training/inference)";

    public bool IsAvailable(PixooContext ctx) => ctx.Compute is not null;

    public int DwellSeconds => 10;

    public void Render(PixooCanvas c, PixooContext ctx)
    {
        var m = ctx.Compute!;
        var gpu = ctx.Gpu;
        var accent = Color.FromArgb(200, 90, 220);

        ScreenChrome.Frame(c, m.Kind, accent, ScreenChrome.Ok);

        c.DrawTextClipped(2, 11, 60, m.ProcessName, ScreenChrome.White);
        c.DrawTextClipped(2, 19, 60, "up " + Elapsed(m.Elapsed), ScreenChrome.Grey);

        var util = gpu?.UtilPct ?? m.GpuUtilPct;
        ScreenChrome.Gauge(c, 27, "GPU", $"{util}%", util / 100.0, ScreenChrome.Load(util));

        var vramText = $"{m.GpuMemMb / 1024.0:0.0}G";
        var vramFrac = gpu is { MemTotalMb: > 0 } ? (double)m.GpuMemMb / gpu.MemTotalMb : 0;
        ScreenChrome.Gauge(c, 40, "VRAM", vramText, vramFrac, accent);

        ScreenChrome.Footer(c, ctx.Now, gpu is not null ? $"{gpu.TempC}° {gpu.PowerW:0}W" : $"CPU {ctx.System.CpuPct:0}%");
    }

    private static string Elapsed(TimeSpan t)
    {
        if (t <= TimeSpan.Zero)
        {
            return "just now";
        }
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m" : $"{t.Minutes}m {t.Seconds}s";
    }
}
