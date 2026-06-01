using System;

namespace ETL_SQL.Common
{
    /// <summary>
    /// A lightweight standalone implementation of ILogger used as a fallback or for
    /// simple "Global" logging before DI is fully initialized.
    /// </summary>
    public class EngineLogger : ILogger
    {
        private readonly string _category;

        public EngineLogger(string category = "Engine")
        {
            _category = category;
        }

        public string? SessionId { get; set; }
        public bool IsDebugEnabled => true; // Fallback assumes enabled or handled by global flags
        public bool IsVerboseEnabled => true;
        public bool IsVerbose { get; set; }
        public bool SuppressConsole { get; set; }
        public bool IsJsonMode { get; set; }
        public event Action<string, string?, ConsoleColor>? OnMessage;

        public void Log(LogLevel level, string message, Exception? ex = null)
        {
            var color = level switch
            {
                LogLevel.Error => ConsoleColor.Red,
                LogLevel.Warning => ConsoleColor.Yellow,
                LogLevel.Debug => ConsoleColor.DarkGray,
                _ => ConsoleColor.White
            };
            WriteToConsole(level.ToString().ToUpper(), message, color, ex);
        }

        public void WriteLine(string message, ConsoleColor color = ConsoleColor.White)
        {
            WriteToConsole("INFO", message, color);
        }

        private void WriteToConsole(string level, string message, ConsoleColor color, Exception? ex = null)
        {
            var sid = SessionId != null ? $" [{SessionId}]" : "";
            string formattedMessage = $"[{level}] [{_category}]{sid} {message}";
            if (ex != null) formattedMessage += $"{Environment.NewLine}Exception: {ex.Message}";
            
            OnMessage?.Invoke(formattedMessage, SessionId, color);
        }
    }
}
