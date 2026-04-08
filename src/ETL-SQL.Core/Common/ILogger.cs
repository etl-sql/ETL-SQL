using System;

namespace ETL_SQL.Common
{
    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }

    public interface ILogger
    {
        void Log(LogLevel level, string message, Exception? ex = null);
        void Debug(string message) => Log(LogLevel.Debug, message);
        void Info(string message) => Log(LogLevel.Info, message);
        void Warning(string message) => Log(LogLevel.Warning, message);
        void Error(string message, Exception? ex = null) => Log(LogLevel.Error, message, ex);
        
        bool IsDebugEnabled { get; }
        bool IsVerboseEnabled { get; }
        bool IsVerbose { get; set; }
        event Action<string, ConsoleColor>? OnMessage;
    }
}
