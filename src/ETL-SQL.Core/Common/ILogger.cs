using System;
using System.Linq;
using System.Text.RegularExpressions;

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

        // ── Single-message overloads (existing API) ──────────────────────────
        void Debug(string message) => Log(LogLevel.Debug, message);
        void Info(string message) => Log(LogLevel.Info, message);
        void Warning(string message) => Log(LogLevel.Warning, message);
        void Error(string message, Exception? ex = null) => Log(LogLevel.Error, message, ex);
        void WriteLine(string message, ConsoleColor color = ConsoleColor.White)
        {
            var level = color switch
            {
                ConsoleColor.Red => LogLevel.Error,
                ConsoleColor.Yellow => LogLevel.Warning,
                _ => LogLevel.Info
            };
            Log(level, message);
        }

        // ── Structured-template overloads ─────────────────────────────────────
        // Call sites: _logger.Debug("Executing {Sql}", sql)
        // Non-Serilog loggers fall back to FormatArgs; LoggerService overrides to
        // pass the raw template + args to Serilog for true structured properties.
        void Debug(string template, params object?[] args) => Log(LogLevel.Debug, FormatArgs(template, args));
        void Info(string template, params object?[] args) => Log(LogLevel.Info, FormatArgs(template, args));
        void Warning(string template, params object?[] args) => Log(LogLevel.Warning, FormatArgs(template, args));
        void Error(string template, Exception? ex, params object?[] args) => Log(LogLevel.Error, FormatArgs(template, args), ex);

        // ── Session correlation ───────────────────────────────────────────────
        string? SessionId { get; set; }

        bool IsDebugEnabled { get; }
        bool IsVerboseEnabled { get; }
        bool IsVerbose { get; set; }
        bool SuppressConsole { get; set; }
        bool IsJsonMode { get; set; }
        event Action<string, ConsoleColor>? OnMessage;

        // ── Formatting helper for non-Serilog loggers ─────────────────────────
        // Converts Serilog-style {Name} tokens to positional {0}, {1}… for string.Format.
        static string FormatArgs(string template, object?[] args)
        {
            if (args.Length == 0) return template;
            int i = 0;
            var positional = Regex.Replace(
                template,
                @"\{[A-Za-z_][A-Za-z0-9_]*(?::[^}]*)?\}",
                _ => $"{{{i++}}}");
            return string.Format(positional, args.Select(a => (object)(a ?? "<null>")).ToArray());
        }
    }
}
