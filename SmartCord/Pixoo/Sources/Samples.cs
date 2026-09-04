namespace SmartCord.Pixoo.Sources;

public sealed record GpuSample(
    string Name,
    int UtilPct,
    int MemUtilPct,
    int MemUsedMb,
    int MemTotalMb,
    int TempC,
    double PowerW,
    int ClockMhz,
    IReadOnlyList<GpuProc> Processes,
    DateTimeOffset At)
{
    public double MemFraction => MemTotalMb > 0 ? (double)MemUsedMb / MemTotalMb : 0;
    public string ShortName
    {
        get
        {
            // "NVIDIA GeForce RTX 5080 Laptop GPU" -> "RTX 5080"
            var parts = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var rtx = Array.FindIndex(parts, p => p is "RTX" or "GTX" or "GT");
            if (rtx >= 0 && rtx + 1 < parts.Length)
            {
                return $"{parts[rtx]} {parts[rtx + 1]}";
            }
            return parts.Length > 0 ? parts[^1] : Name;
        }
    }
}

public sealed record GpuProc(int Pid, string Name, int MemMb);

public sealed record SystemSample(
    double CpuPct,
    long RamUsedMb,
    long RamTotalMb,
    TimeSpan Uptime,
    DateTimeOffset At)
{
    public double RamFraction => RamTotalMb > 0 ? (double)RamUsedMb / RamTotalMb : 0;
}

public sealed record NowPlayingSample(
    string Title,
    string Artist,
    string Album,
    double PositionSeconds,
    double DurationSeconds,
    bool IsPlaying,
    IReadOnlyList<double> Levels,
    string Accent,
    int ScrobblesToday,
    int ScrobblesTotal,
    int MinutesToday,
    DateTimeOffset At,
    string Source = "Spectralis")
{
    public double Progress => DurationSeconds > 0 ? Math.Clamp(PositionSeconds / DurationSeconds, 0, 1) : 0;
    public bool HasTrack => !string.IsNullOrWhiteSpace(Title);
}

public sealed record ComputeSample(
    string ProcessName,
    int Pid,
    TimeSpan Elapsed,
    int GpuMemMb,
    int GpuUtilPct,
    DateTimeOffset At)
{
    /// <summary>Best-guess label from the process name.</summary>
    public string Kind => ProcessName.ToLowerInvariant() switch
    {
        var n when n.Contains("ollama") || n.Contains("llama") || n.Contains("koboldcpp") || n.Contains("lmstudio") => "INFERENCE",
        var n when n.Contains("comfy") || n.Contains("a1111") || n.Contains("forge") || n.Contains("sd") => "DIFFUSION",
        _ => "TRAINING",
    };
}
