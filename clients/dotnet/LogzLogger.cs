#pragma warning disable
using System.Collections.Concurrent;

public class LogzLogger : ILogger
{
    private readonly string _categoryName;

    public LogzLogger(string categoryName)
    {
        _categoryName = categoryName;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var message = formatter(state, exception);
        if (string.IsNullOrEmpty(message) && exception == null)
        {
            return;
        }

        var logMessage = $"[{logLevel}] {_categoryName}: {message}";

        if (exception != null)
        {
            logMessage += $" Exception: {exception.GetType().Name}: {exception.Message}";
            if (exception.StackTrace != null)
            {
                logMessage += $" StackTrace: {exception.StackTrace}";
            }
            if (exception.InnerException != null)
            {
                logMessage += $" Inner: {exception.InnerException.GetType().Name}: {exception.InnerException.Message}";
            }
        }

        // Shorten long category names for cleaner logs
        var source = _categoryName.Length > 30
            ? _categoryName.Substring(_categoryName.LastIndexOf('.') + 1)
            : _categoryName;

        Logz.Log(source, logMessage);
    }
}

public class LogzLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, LogzLogger> _loggers = new();

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, name => new LogzLogger(name));
    }

    public void Dispose()
    {
        _loggers.Clear();
    }
}

public static class LogzLoggerExtensions
{
    public static ILoggingBuilder AddLogz(this ILoggingBuilder builder)
    {
        builder.AddProvider(new LogzLoggerProvider());
        return builder;
    }
}
