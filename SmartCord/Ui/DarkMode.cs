using System.Runtime.InteropServices;

namespace SmartCord.Ui;

/// <summary>
/// Coaxes Win32 common controls (combo drop-downs, scrollbars, context menus)
/// into dark mode. .NET 8 WinForms has no first-class switch for this, so we lean
/// on the same undocumented uxtheme ordinals every WinForms dark-mode shim uses —
/// all wrapped in try/catch so an older Windows just falls back to light chrome.
/// </summary>
public static class DarkMode
{
    private static bool _applied;

    public static void TryEnableAppDarkMode()
    {
        if (_applied)
        {
            return;
        }
        _applied = true;

        try
        {
            // 1903+: 2 == ForceDark
            SetPreferredAppMode(2);
            FlushMenuThemes();
        }
        catch
        {
            // pre-1903 or the ordinals moved — nothing to do.
        }
    }

    /// <summary>Apply a dark visual style to one control's window handle.</summary>
    public static void ApplyToControl(IntPtr handle, string style = "DarkMode_Explorer")
    {
        try
        {
            SetWindowTheme(handle, style, null);
        }
        catch
        {
            // no themed control services — ignore.
        }
    }

    public static bool TryGetComboListHandle(IntPtr comboHandle, out IntPtr listHandle)
    {
        listHandle = IntPtr.Zero;
        try
        {
            var info = new COMBOBOXINFO { cbSize = Marshal.SizeOf<COMBOBOXINFO>() };
            if (GetComboBoxInfo(comboHandle, ref info))
            {
                listHandle = info.hwndList;
                return listHandle != IntPtr.Zero;
            }
        }
        catch
        {
            // ignore
        }
        return false;
    }

    [DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true)]
    private static extern int SetPreferredAppMode(int mode);

    [DllImport("uxtheme.dll", EntryPoint = "#136")]
    private static extern void FlushMenuThemes();

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? pszSubAppName, string? pszSubIdList);

    [DllImport("user32.dll")]
    private static extern bool GetComboBoxInfo(IntPtr hwndCombo, ref COMBOBOXINFO pcbi);

    [StructLayout(LayoutKind.Sequential)]
    private struct COMBOBOXINFO
    {
        public int cbSize;
        public Rectangle rcItem;
        public Rectangle rcButton;
        public int stateButton;
        public IntPtr hwndCombo;
        public IntPtr hwndItem;
        public IntPtr hwndList;
    }
}
