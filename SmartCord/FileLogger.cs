using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;

namespace SmartCord;

/// <summary>
/// Tiny rolling-file logger. One file per day under
/// <see cref="AppPaths.LogDirectory"/>, old files pruned to <see cref="RetainDays"/>.
/// Deliberately dependency-free — SmartCord only needs "write a line to a file",
/// not a logging framework.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private const int RetainDays = 14;

    private readonly BlockingCollection<string> _queue = new(2048);
    private readonly Thread _worker;
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();

    public FileLoggerProvider()
    {
        AppPaths.EnsureCreated();
        PruneOldFiles();

        _worker = new Thread(DrainLoop)
        {
            IsBackground = true,
            Name = "SmartCord.FileLog"
        };
        _worker.Start();
    }

    public static string CurrentLogFile =>
        Path.Combine(AppPaths.LogDirectory, $"smartcord-{DateTime.Now:yyyy-MM-dd}.log");

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new FileLogger(name, Enqueue));

    private void Enqueue(string line)
    {
        // Drop rather than block the UI thread if the writer falls behind.
        _queue.TryAdd(line);
    }

    private void DrainLoop()
    {
        foreach (var line in _queue.GetConsumingEnumerable())
        {
            try
            {
                File.AppendAllText(CurrentLogFile, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // Logging must never take the app down.
            }
        }
    }

    private static void PruneOldFiles()
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-RetainDays);
            foreach (var file in Directory.EnumerateFiles(AppPaths.LogDirectory, "smartcord-*.log"))
            {
                if (File.GetLastWriteTime(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch
        {
            // best effort
        }
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _worker.Join(TimeSpan.FromSeconds(2));
        _queue.Dispose();
    }

    private sealed class FileLogger(string category, Action<string> write) : ILogger
    {
        private readonly string _shortCategory = category.Contains('.')
            ? category[(category.LastIndexOf('.') + 1)..]
            : category;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var sb = new StringBuilder()
                .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                .Append(" [").Append(Level(logLevel)).Append("] ")
                .Append(_shortCategory).Append(": ")
                .Append(formatter(state, exception));

            if (exception is not null)
            {
                sb.Append(Environment.NewLine).Append(exception);
            }

            write(sb.ToString());
        }

        private static string Level(LogLevel level) => level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "???"
        };
    }
}
