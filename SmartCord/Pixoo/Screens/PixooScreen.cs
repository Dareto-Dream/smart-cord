using SmartCord.Pixoo.Sources;

namespace SmartCord.Pixoo.Screens;

/// <summary>Everything a screen might want to draw, sampled once per render cycle.</summary>
public sealed record PixooContext(
    SmartCordController Controller,
    SmartCordSettings Settings,
    GpuSample? Gpu,
    SystemSample System,
    NowPlayingSample? NowPlaying,
    ComputeSample? Compute,
    DateTime Now);

/// <summary>One 64×64 page in the Pixoo rotation.</summary>
public interface IPixooScreen
{
    /// <summary>Stable id used in settings + the UI checklist.</summary>
    string Id { get; }

    /// <summary>Human label for the UI.</summary>
    string Label { get; }

    /// <summary>Should this screen be in the rotation right now? (GPU present, music playing, …)</summary>
    bool IsAvailable(PixooContext ctx);

    /// <summary>Seconds to dwell on this screen before advancing (0 = use the global default).</summary>
    int DwellSeconds => 0;

    void Render(PixooCanvas canvas, PixooContext ctx);
}
