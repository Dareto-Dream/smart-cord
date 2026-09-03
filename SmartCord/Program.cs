using Microsoft.Extensions.Logging;

namespace SmartCord;

static class Program
{
    private const string SingleInstanceMutexName = "SmartCord.SingleInstance.9f2c";

    [STAThread]
    static void Main(string[] args)
    {
        DotEnv.Load();
        AppPaths.EnsureCreated();

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddProvider(new FileLoggerProvider());
        });
        var logger = loggerFactory.CreateLogger("Program");

        using var settingsProvider = new SettingsProvider(loggerFactory.CreateLogger<SettingsProvider>());

        if (args.Any(arg => string.Equals(arg, "--download-icons", StringComparison.OrdinalIgnoreCase)))
        {
            ImageAssetDownloader.DownloadAsync(settingsProvider.Current, Directory.GetCurrentDirectory(), CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            return;
        }

        using var singleInstance = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            logger.LogWarning("Another SmartCord instance is already running; exiting");
            MessageBox.Show(
                "SmartCord is already running (check the system tray).",
                "SmartCord",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            logger.LogCritical(e.ExceptionObject as Exception, "Unhandled exception");
        Application.ThreadException += (_, e) =>
            logger.LogError(e.Exception, "Unhandled UI thread exception");

        logger.LogInformation("SmartCord starting");

        var startHidden = args.Any(arg =>
            string.Equals(arg, "--tray", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "--minimized", StringComparison.OrdinalIgnoreCase));

        ApplicationConfiguration.Initialize();
        Ui.DarkMode.TryEnableAppDarkMode();
        Application.Run(new TrayApplicationContext(settingsProvider, loggerFactory, startHidden));

        logger.LogInformation("SmartCord exiting");
    }
}
