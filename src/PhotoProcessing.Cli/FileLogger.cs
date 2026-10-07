using Microsoft.Extensions.Logging;

namespace PhotoProcessing.Cli;

/// <summary>Appends log lines to logs/photoedit-YYYYMMDD.log, so a hidden watcher still leaves a trail.</summary>
public sealed class FileLoggerProvider(string directory) : ILoggerProvider
{
    private readonly Lock _lock = new();

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose() { }

    private void Write(string line)
    {
        lock (_lock)
        {
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, $"photoedit-{DateTime.Now:yyyyMMdd}.log"), line + Environment.NewLine);
        }
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var shortCategory = category[(category.LastIndexOf('.') + 1)..];
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {logLevel.ToString()[..4].ToUpperInvariant()} {shortCategory}: {formatter(state, exception)}";
            if (exception is not null) line += Environment.NewLine + exception;
            provider.Write(line);
        }
    }
}
