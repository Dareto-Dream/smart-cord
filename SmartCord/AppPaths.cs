namespace SmartCord;

/// <summary>
/// Central place for the per-user data directory. Everything the agent writes at
/// runtime (state, logs, secrets) lives under %AppData%\SmartCord so it survives
/// reinstalls and never lands next to the executable.
/// </summary>
public static class AppPaths
{
    public static string RootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SmartCord");

    public static string LogDirectory { get; } = Path.Combine(RootDirectory, "logs");

    public static string StateFile { get; } = Path.Combine(RootDirectory, "state.json");

    /// <summary>DPAPI-encrypted blob for OAuth refresh tokens / API keys (Tier 2).</summary>
    public static string SecretsFile { get; } = Path.Combine(RootDirectory, "secrets.dat");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(LogDirectory);
    }
}
