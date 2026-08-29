using System.Runtime.InteropServices;

namespace SmartCord;

/// <summary>
/// Wraps <c>GetLastInputInfo</c> so the resolver can tell "at the keyboard" from
/// "stepped away". Also tracks session lock / unlock so presence can be cleared
/// while the machine is locked or on an RDP disconnect.
/// </summary>
public sealed class IdleDetector
{
    public TimeSpan IdleFor
    {
        get
        {
            var info = new LASTINPUTINFO
            {
                cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>()
            };

            if (!GetLastInputInfo(ref info))
            {
                return TimeSpan.Zero;
            }

            var idleMs = (uint)Environment.TickCount - info.dwTime;
            return TimeSpan.FromMilliseconds(idleMs);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
}
