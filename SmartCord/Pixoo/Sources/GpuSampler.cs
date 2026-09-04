using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace SmartCord.Pixoo.Sources;

/// <summary>
/// Shells out to <c>nvidia-smi</c> for GPU telemetry and the compute-process
/// list. Marks itself unavailable (so the GPU / Compute screens drop out of the
/// rotation) if nvidia-smi isn't on PATH or keeps failing.
/// </summary>
public sealed class GpuSampler
{
    private readonly ILogger _logger;
    private int _consecutiveFailures;

    public GpuSampler(ILogger logger) => _logger = logger;

    public GpuSample? Latest { get; private set; }
    public ComputeSample? Compute { get; private set; }
    public bool Unavailable => _consecutiveFailures >= 3;

    public async Task RefreshAsync(IReadOnlyCollection<string> aiProcessNames, CancellationToken ct)
    {
        if (Unavailable)
        {
            return;
        }

        try
        {
            var gpuCsv = await RunAsync(
                "--query-gpu=name,utilization.gpu,utilization.memory,memory.used,memory.total,temperature.gpu,power.draw,clocks.sm --format=csv,noheader,nounits",
                ct);
            if (gpuCsv is null)
            {
                Fail();
                return;
            }

            var f = FirstRow(gpuCsv);
            if (f.Length < 8)
            {
                Fail();
                return;
            }

            var procsCsv = await RunAsync(
                "--query-compute-apps=pid,process_name,used_memory --format=csv,noheader,nounits", ct);
            var procs = ParseProcs(procsCsv);

            var sample = new GpuSample(
                Name: f[0],
                UtilPct: Int(f[1]),
                MemUtilPct: Int(f[2]),
                MemUsedMb: Int(f[3]),
                MemTotalMb: Int(f[4]),
                TempC: Int(f[5]),
                PowerW: Dbl(f[6]),
                ClockMhz: Int(f[7]),
                Processes: procs,
                At: DateTimeOffset.UtcNow);

            Latest = sample;
            Compute = DeriveCompute(sample, aiProcessNames);
            _consecutiveFailures = 0;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "nvidia-smi sample failed");
            Fail();
        }
    }

    private void Fail()
    {
        _consecutiveFailures++;
        if (Unavailable)
        {
            _logger.LogInformation("nvidia-smi unavailable — GPU / compute screens disabled");
        }
    }

    private static ComputeSample? DeriveCompute(GpuSample gpu, IReadOnlyCollection<string> aiNames)
    {
        GpuProc? match = null;
        foreach (var proc in gpu.Processes)
        {
            var name = Path.GetFileNameWithoutExtension(proc.Name);
            if (aiNames.Any(n => name.Contains(n, StringComparison.OrdinalIgnoreCase)) &&
                (match is null || proc.MemMb > match.MemMb))
            {
                match = proc;
            }
        }
        if (match is null)
        {
            return null;
        }

        var elapsed = TimeSpan.Zero;
        try
        {
            elapsed = DateTime.Now - Process.GetProcessById(match.Pid).StartTime;
        }
        catch
        {
            // process gone or access denied — leave elapsed at zero
        }

        return new ComputeSample(
            ProcessName: Path.GetFileNameWithoutExtension(match.Name),
            Pid: match.Pid,
            Elapsed: elapsed,
            GpuMemMb: match.MemMb,
            GpuUtilPct: gpu.UtilPct,
            At: DateTimeOffset.UtcNow);
    }

    private static IReadOnlyList<GpuProc> ParseProcs(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return [];
        }

        var list = new List<GpuProc>();
        foreach (var line in csv.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length >= 3 && int.TryParse(parts[0], out var pid))
            {
                list.Add(new GpuProc(pid, parts[1], Int(parts[2])));
            }
        }
        return list;
    }

    private static async Task<string?> RunAsync(string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("nvidia-smi", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        if (process is null)
        {
            return null;
        }

        var output = await process.StandardOutput.ReadToEndAsync(ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(); } catch { /* ignore */ }
            return null;
        }

        return process.ExitCode == 0 ? output : null;
    }

    private static string[] FirstRow(string csv)
    {
        var line = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return line.Split(',', StringSplitOptions.TrimEntries);
    }

    private static int Int(string s) => int.TryParse(s.Split('.')[0], out var v) ? v : 0;
    private static double Dbl(string s) => double.TryParse(s, out var v) ? v : 0;
}
