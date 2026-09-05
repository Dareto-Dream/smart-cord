using System.Diagnostics;

namespace SmartCord;

public sealed class ProcessActivityDetector(IReadOnlyList<StatusPreset> presets)
{
    public StatusPreset Detect(StatusPreset fallback)
    {
        var processNames = Process.GetProcesses()
            .Select(process => SafeProcessName(process))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var preset in presets.Where(preset => preset.ProcessNames.Count > 0))
        {
            if (preset.ProcessNames.Any(processNames.Contains))
            {
                return preset;
            }
        }

        return fallback;
    }

    private static string SafeProcessName(Process process)
    {
        try
        {
            return process.ProcessName;
        }
        catch
        {
            return "";
        }
        finally
        {
            process.Dispose();
        }
    }
}
