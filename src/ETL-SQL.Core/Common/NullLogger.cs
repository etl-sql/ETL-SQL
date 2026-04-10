using System;

namespace ETL_SQL.Common
{
    /// <summary>
    /// A no-op logger for use in unit tests and scenarios where logging is not required.
    /// </summary>
    public class NullLogger : ILogger
    {
        public static readonly NullLogger Instance = new();

        public void Log(LogLevel level, string message, Exception? ex = null) { }
        
        public bool IsDebugEnabled => false;
        public bool IsVerboseEnabled => false;
        public bool IsVerbose { get; set; } = false;
        public bool SuppressConsole { get; set; } = false;
        public bool IsJsonMode { get; set; } = false;
        public event Action<string, ConsoleColor>? OnMessage;
    }
}
