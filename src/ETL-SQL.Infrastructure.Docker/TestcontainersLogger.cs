using System;
using Microsoft.Extensions.Logging;

namespace ETL_SQL.Core;

public class TestcontainersLogger : Microsoft.Extensions.Logging.ILogger
{
    private readonly string _categoryName;
    private readonly ETL_SQL.Common.ILogger _logger;

    public TestcontainersLogger(string categoryName, ETL_SQL.Common.ILogger logger)
    {
        _categoryName = categoryName;
        _logger = logger;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

    public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        var color = logLevel switch
        {
            Microsoft.Extensions.Logging.LogLevel.Error or Microsoft.Extensions.Logging.LogLevel.Critical => ConsoleColor.Red,
            Microsoft.Extensions.Logging.LogLevel.Warning => ConsoleColor.Yellow,
            Microsoft.Extensions.Logging.LogLevel.Information => ConsoleColor.Cyan,
            _ => ConsoleColor.Gray
        };

        // Route to our central logger
        _logger.WriteLine($"[{_categoryName}] {message}", color);
    }
}

public class TestcontainersLoggerProvider : Microsoft.Extensions.Logging.ILoggerProvider
{
    private readonly ETL_SQL.Common.ILogger _logger;

    public TestcontainersLoggerProvider(ETL_SQL.Common.ILogger logger)
    {
        _logger = logger;
    }

    public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) => new TestcontainersLogger(categoryName, _logger);
    public void Dispose() { }
}
