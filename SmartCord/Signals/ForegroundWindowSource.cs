using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using SmartCord.Integrations;

namespace SmartCord.Signals;

/// <summary>Emits a coding signal from something concrete: which source it read, when.</summary>
public interface ICodingSignalSource
{
    string Name { get; }
    CodingSnapshot? Read();
}

/// <summary>
/// Reads the actually-focused window — not just "is an editor process running
/// somewhere" like <see cref="ProcessActivityDetector"/> — and turns a recognized
/// IDE's title bar into a <see cref="CodingSnapshot"/>. Sits ahead of the blind
/// process-scan fallback in the auto-detect resolver: "VS Code is focused, editing
/// Foo.cs" beats "VS Code is running, who knows where it's minimized to."
///
/// Title parsing is best-effort — editors don't expose a stable API for this, so
/// we're scraping "{file} - {folder} - {app}"-shaped title bars. Folder is a
/// display name from the title, not a filesystem path, so this can't shell out to
/// git for branch/diff info; that needs a real path source (see plan.md Tier 3,
/// the VS Code companion extension idea).
/// </summary>
public sealed class ForegroundWindowSource : ICodingSignalSource
{
    private static readonly Dictionary<string, string> EditorDisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Code"] = "VS Code",
        ["Cursor"] = "Cursor",
        ["devenv"] = "Visual Studio",
        ["rider64"] = "Rider",
        ["idea64"] = "IntelliJ IDEA",
        ["pycharm64"] = "PyCharm",
        ["webstorm64"] = "WebStorm",
        ["clion64"] = "CLion",
        ["sublime_text"] = "Sublime Text",
    };

    private static readonly Dictionary<string, string> LanguageByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"] = "C#", [".ts"] = "TypeScript", [".tsx"] = "TypeScript", [".js"] = "JavaScript",
        [".jsx"] = "JavaScript", [".py"] = "Python", [".rs"] = "Rust", [".go"] = "Go",
        [".java"] = "Java", [".kt"] = "Kotlin", [".cpp"] = "C++", [".cc"] = "C++", [".h"] = "C++",
        [".hpp"] = "C++", [".c"] = "C", [".rb"] = "Ruby", [".php"] = "PHP", [".swift"] = "Swift",
        [".html"] = "HTML", [".css"] = "CSS", [".json"] = "JSON", [".md"] = "Markdown",
        [".sql"] = "SQL", [".sh"] = "Shell", [".ps1"] = "PowerShell", [".lua"] = "Lua",
    };

    public string Name => "ForegroundWindow";

    public CodingSnapshot? Read()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0)
        {
            return null;
        }

        string processName;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            processName = process.ProcessName;
        }
        catch
        {
            return null; // process exited between the two calls, or access denied
        }

        if (!EditorDisplayNames.TryGetValue(processName, out var displayName))
        {
            return null;
        }

        var title = GetWindowTitle(hwnd);
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var (file, folder) = ParseTitle(title);
        var language = file is null ? null : LanguageByExtension.GetValueOrDefault(Path.GetExtension(file));

        return new CodingSnapshot(
            Source: displayName,
            Project: folder,
            Language: language,
            Editor: displayName,
            SecondsToday: 0,
            CapturedAtUtc: DateTimeOffset.UtcNow,
            ActiveNow: true);
    }

    /// <summary>
    /// VS Code / Visual Studio / JetBrains all title roughly as
    /// "{file} - {folder} - {app}" (JetBrains uses an en dash). Best-effort split;
    /// the last segment (the app name) is dropped, the rest is a file/folder guess.
    /// </summary>
    private static (string? File, string? Folder) ParseTitle(string title)
    {
        var parts = title.Split([" - ", " – "], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            return (null, null); // just the app name, or nothing recognizable
        }

        var meaningful = parts[..^1];
        return meaningful.Length switch
        {
            >= 2 => (meaningful[0], meaningful[1]),
            1 when LooksLikeFileName(meaningful[0]) => (meaningful[0], null),
            1 => (null, meaningful[0]),
            _ => (null, null),
        };
    }

    private static bool LooksLikeFileName(string value) =>
        Path.HasExtension(value) && value.IndexOfAny(['\\', '/']) < 0;

    private static string GetWindowTitle(IntPtr hwnd)
    {
        var length = GetWindowTextLength(hwnd);
        if (length <= 0)
        {
            return "";
        }

        var buffer = new StringBuilder(length + 1);
        GetWindowText(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
