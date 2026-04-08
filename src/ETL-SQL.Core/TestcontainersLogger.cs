using System;
using Microsoft.Extensions.Logging;
using ETL_SQL.Common;

namespace ETL_SQL.Core
{
    public class TestcontainersLogger : Microsoft.Extensions.Logging.ILogger
    {
        private readonly string _categoryName;

        public TestcontainersLogger(string categoryName)
        {
            _categoryName = categoryName;
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
            ETL_SQL.Common.Logger.WriteLine($"[{_categoryName}] {message}", color);
        }
    }

    public class TestcontainersLoggerProvider : Microsoft.Extensions.Logging.ILoggerProvider
    {
        public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) => new TestcontainersLogger(categoryName);
        public void Dispose() { }
    }
}
