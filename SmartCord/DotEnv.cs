namespace SmartCord;

/// <summary>
/// Minimal .env reader. SmartCord is a user-facing app, so we don't bake secrets
/// into the build — anything sensitive (tokens, self-hosted base URLs, an ingest
/// token) goes in a gitignored .env file that gets loaded into the process
/// environment before configuration binds. Keys use the standard
/// <c>SMARTCORD_Section__Key</c> shape so they flow straight through
/// <see cref="Microsoft.Extensions.Configuration.EnvironmentVariablesExtensions"/>.
/// </summary>
public static class DotEnv
{
    /// <summary>
    /// Loads the first <c>.env</c> found walking up from the executable directory,
    /// then the working directory. Existing environment variables are never
    /// overwritten, so a real machine env var always wins over the file.
    /// </summary>
    public static void Load()
    {
        foreach (var path in CandidatePaths())
        {
            if (File.Exists(path))
            {
                LoadFile(path);
                return;
            }
        }
    }

    private static IEnumerable<string> CandidatePaths()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            var candidate = Path.Combine(dir, ".env");
            if (seen.Add(candidate))
            {
                yield return candidate;
            }

            var parent = Directory.GetParent(dir)?.FullName;
            if (string.Equals(parent, dir, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            dir = parent!;
        }

        var cwd = Path.Combine(Directory.GetCurrentDirectory(), ".env");
        if (seen.Add(cwd))
        {
            yield return cwd;
        }
    }

    private static void LoadFile(string path)
    {
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("export ", StringComparison.Ordinal))
            {
                line = line["export ".Length..].TrimStart();
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();

            if ((value.StartsWith('"') && value.EndsWith('"') && value.Length >= 2) ||
                (value.StartsWith('\'') && value.EndsWith('\'') && value.Length >= 2))
            {
                value = value[1..^1];
            }

            if (key.Length == 0 || Environment.GetEnvironmentVariable(key) is not null)
            {
                continue;
            }

            Environment.SetEnvironmentVariable(key, value);
        }
    }
}
