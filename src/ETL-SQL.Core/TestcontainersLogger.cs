using System;
using Microsoft.Extensions.Logging;
using ETL_SQL.Common;

namespace ETL_SQL.Core
{
    public class TestcontainersLogger : ILogger
    {
        private readonly string _categoryName;

        public TestcontainersLogger(string categoryName)
        {
            _categoryName = categoryName;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            var color = logLevel switch
            {
                LogLevel.Error or LogLevel.Critical => ConsoleColor.Red,
                LogLevel.Warning => ConsoleColor.Yellow,
                LogLevel.Information => ConsoleColor.Cyan,
                _ => ConsoleColor.Gray
            };

            // Route to our central logger
            ETL_SQL.Common.Logger.WriteLine($"[{_categoryName}] {message}", color);
        }
    }

    public class TestcontainersLoggerProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new TestcontainersLogger(categoryName);
        public void Dispose() { }
    }
}
