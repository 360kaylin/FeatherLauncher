using FeatherLauncher.Core.Services;
using Microsoft.Extensions.Logging;

namespace FeatherLauncher.Infrastructure.Logging;

public sealed class RedactingFileLoggerProvider(IAppPaths paths, ILogRedactor redactor) : ILoggerProvider
{
    private readonly object gate = new();
    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, paths, redactor, gate);
    public void Dispose() { }
    private sealed class FileLogger(string category, IAppPaths paths, ILogRedactor redactor, object gate) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { if (!IsEnabled(level)) return; paths.EnsureCreated(); var text = redactor.Redact(formatter(state, exception) + (exception is null ? "" : $" ({exception.GetType().Name})")); lock (gate) File.AppendAllText(Path.Combine(paths.Logs, $"launcher-{DateTime.UtcNow:yyyyMMdd}.log"), $"{DateTime.UtcNow:O} [{level}] {category}: {text}{Environment.NewLine}"); }
    }
}
