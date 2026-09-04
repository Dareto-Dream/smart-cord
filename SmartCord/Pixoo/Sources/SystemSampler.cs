using System.Runtime.InteropServices;

namespace SmartCord.Pixoo.Sources;

/// <summary>CPU load, physical memory and uptime via plain Win32 — no perf-counter warm-up.</summary>
public sealed class SystemSampler
{
    private ulong _lastIdle, _lastKernel, _lastUser;
    private bool _primed;

    public SystemSample Latest { get; private set; } =
        new(0, 0, 0, TimeSpan.Zero, DateTimeOffset.MinValue);

    public void Refresh()
    {
        var cpu = SampleCpu();

        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        long usedMb = 0, totalMb = 0;
        if (GlobalMemoryStatusEx(ref mem))
        {
            totalMb = (long)(mem.ullTotalPhys / (1024 * 1024));
            usedMb = (long)((mem.ullTotalPhys - mem.ullAvailPhys) / (1024 * 1024));
        }

        Latest = new SystemSample(
            CpuPct: cpu,
            RamUsedMb: usedMb,
            RamTotalMb: totalMb,
            Uptime: TimeSpan.FromMilliseconds(GetTickCount64()),
            At: DateTimeOffset.UtcNow);
    }

    private double SampleCpu()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return Latest.CpuPct;
        }

        var i = ToUlong(idle);
        var k = ToUlong(kernel);
        var u = ToUlong(user);

        if (!_primed)
        {
            (_lastIdle, _lastKernel, _lastUser, _primed) = (i, k, u, true);
            return 0;
        }

        var idleDelta = (double)(i - _lastIdle);
        var kernelDelta = (double)(k - _lastKernel);
        var userDelta = (double)(u - _lastUser);
        (_lastIdle, _lastKernel, _lastUser) = (i, k, u);

        var total = kernelDelta + userDelta; // kernel time already includes idle
        return total <= 0 ? Latest.CpuPct : Math.Clamp((total - idleDelta) / total * 100, 0, 100);
    }

    private static ulong ToUlong(FILETIME ft) => ((ulong)(uint)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public int dwLowDateTime;
        public int dwHighDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("kernel32.dll")]
    private static extern ulong GetTickCount64();
}
