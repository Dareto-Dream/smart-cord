using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace SmartCord;

/// <summary>
/// Toggles a HKCU\...\Run entry so SmartCord launches at login. Per-user, no
/// elevation needed.
/// </summary>
public sealed class AutoStartManager(ILogger<AutoStartManager> logger)
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SmartCord";

    public bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                return key?.GetValue(ValueName) is string existing &&
                       existing.TrimStart('"').StartsWith(ExecutablePath, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not read auto-start state");
                return false;
            }
        }
    }

    public void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

            if (enabled)
            {
                key.SetValue(ValueName, $"\"{ExecutablePath}\" --tray");
                logger.LogInformation("Auto-start enabled");
            }
            else if (key.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                logger.LogInformation("Auto-start disabled");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not update auto-start state");
        }
    }

    private static string ExecutablePath =>
        Environment.ProcessPath ?? Path.ChangeExtension(typeof(AutoStartManager).Assembly.Location, ".exe");
}
