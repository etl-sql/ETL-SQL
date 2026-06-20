using System;
using System.IO;
using System.Linq;
using ETL_SQL.Core.Common;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Context;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace ETL_SQL.Common
{
    /// <summary>
    /// Default implementation of ILogger that handles multiple Serilog sinks (App, Script, Test),
    /// Console output, and UI callbacks.
    /// </summary>
    public class LoggerService : ILogger, ILoggerService, IDisposable
    {
        private Serilog.Core.Logger? _appLogger;
        private Serilog.Core.Logger? _scriptLogger;
        private Serilog.Core.Logger? _testLogger;
        private ILoggerFactory? _msLoggerFactory;
        private Microsoft.Extensions.Logging.ILogger? _msLogger;
        private readonly AsyncLocal<string?> _sessionId = new();
        private readonly AsyncLocal<IDisposable?> _sessionContext = new();

        public bool IsSilent { get; set; }
        public bool IsVerbose { get; set; }
        public bool IsFileLogging { get; set; }
        public bool SuppressConsole { get; set; }
        public bool IsJsonMode { get; set; }
        public event Action<string, string?, ConsoleColor>? OnMessage;

        public bool IsDebugEnabled => IsVerbose;
        public bool IsVerboseEnabled => IsVerbose;

        /// <summary>
        /// When set, enriches every Serilog log event with a SessionId property and
        /// prefixes console output with [sid:{value}] for correlation across concurrent sessions.
        /// </summary>
        public string? SessionId
        {
            get => _sessionId.Value;
            set
            {
                _sessionId.Value = value;
                _sessionContext.Value?.Dispose();
                _sessionContext.Value = value != null
                    ? LogContext.PushProperty("SessionId", value)
                    : null;
            }
        }

        public void Log(LogLevel level, string message, Exception? ex = null)
        {
            if (IsSilent && level != LogLevel.Error) return;
            if (level == LogLevel.Debug && !IsVerbose) return;

            WriteCore(level, "{Message}", ex, message);
        }

        // ── Structured-template overloads ─────────────────────────────────────
        // Pass the raw template + args to Serilog so named properties are
        // preserved in file/Seq sinks for structured querying.

        public void Debug(string template, params object?[] args)
        {
            if (IsSilent || !IsVerbose) return;
            WriteCore(LogLevel.Debug, template, null, args);
        }

        public void Info(string template, params object?[] args)
        {
            if (IsSilent) return;
            WriteCore(LogLevel.Info, template, null, args);
        }

        public void Warning(string template, params object?[] args)
        {
            if (IsSilent) return;
            WriteCore(LogLevel.Warning, template, null, args);
        }

        public void Error(string template, Exception? ex, params object?[] args)
        {
            WriteCore(LogLevel.Error, template, ex, args);
        }

        // ── Shared write path ──────────────────────────────────────────────────
        private void WriteCore(LogLevel level, string template, Exception? ex, params object?[] args)
        {
            var serilogLevel = level switch
            {
                LogLevel.Error => LogEventLevel.Error,
                LogLevel.Warning => LogEventLevel.Warning,
                LogLevel.Debug => LogEventLevel.Debug,
                _ => LogEventLevel.Information
            };

            var color = level switch
            {
                LogLevel.Error => ConsoleColor.Red,
                LogLevel.Warning => ConsoleColor.Yellow,
                LogLevel.Debug => ConsoleColor.DarkGray,
                _ => ConsoleColor.White
            };

            // 1. Serilog structured write — named properties preserved in file/Seq sinks
            var safeTemplate = SecretRedactor.Redact(template) ?? string.Empty;
            var safeArgs = args.Select(RedactLogArgument).ToArray();
            var safeException = SecretRedactor.RedactException(ex);
            _appLogger?.Write(serilogLevel, safeException, safeTemplate, safeArgs);
            _scriptLogger?.Write(serilogLevel, safeException, safeTemplate, safeArgs);
            _testLogger?.Write(serilogLevel, safeException, safeTemplate, safeArgs);

            // 2. MEL Bridge
            if (_msLogger != null)
            {
                var msLevel = level switch
                {
                    LogLevel.Error => Microsoft.Extensions.Logging.LogLevel.Error,
                    LogLevel.Warning => Microsoft.Extensions.Logging.LogLevel.Warning,
                    LogLevel.Debug => Microsoft.Extensions.Logging.LogLevel.Debug,
                    _ => Microsoft.Extensions.Logging.LogLevel.Information
                };
                _msLogger.Log(msLevel, safeException, ILogger.FormatArgs(safeTemplate, safeArgs));
            }

            // 3. Console — format template and prefix SessionId when set
            var consoleMessage = ILogger.FormatArgs(safeTemplate, safeArgs);
            if (_sessionId.Value != null) consoleMessage = $"[{_sessionId.Value}] {consoleMessage}";
            if (safeException != null) consoleMessage += $"{Environment.NewLine}Exception: {safeException.Message}";

            if (((!IsSilent || level == LogLevel.Error) && !SuppressConsole) || (level == LogLevel.Error && !IsJsonMode))
            {
                if (color != ConsoleColor.White) Console.ForegroundColor = color;
                Console.WriteLine(consoleMessage);
                if (color != ConsoleColor.White) Console.ResetColor();
            }

            // 4. UI Callback - pass current SessionId for filtering
            OnMessage?.Invoke(consoleMessage, _sessionId.Value, color);

        }

        private static object RedactLogArgument(object? arg)
        {
            if (arg is null) return "<null>";
            if (arg is Exception ex) return (object?)SecretRedactor.RedactException(ex) ?? "<null>";
            if (arg is string text) return SecretRedactor.Redact(text) ?? string.Empty;
            return arg;
        }

        public void InitializeAppLogger(string logDirectory, int retentionDays = 30, int fileSizeLimitMb = 10)
        {
            logDirectory = ResolvePath(logDirectory);
            Directory.CreateDirectory(logDirectory);
            PurgeOldLogs(logDirectory, retentionDays, "*.log");

            _appLogger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .Enrich.FromLogContext()
                .WriteTo.File(
                    path: Path.Combine(logDirectory, "etlsql-.log"),
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: fileSizeLimitMb > 0 ? (long?)fileSizeLimitMb * 1024 * 1024 : null,
                    rollOnFileSizeLimit: true,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}]{SessionId: [sid=]:l} {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            if (_msLoggerFactory == null)
            {
                _msLoggerFactory = new SerilogLoggerFactory(_appLogger, dispose: false);
                _msLogger = _msLoggerFactory.CreateLogger("ETL_SQL.Common.Logger");
            }
        }

        public void InitializeScriptLogger(string sourceScript, string logDirectory, int retentionDays = 30, int fileSizeLimitMb = 10)
        {
            IsFileLogging = true;
            logDirectory = ResolvePath(logDirectory);
            Directory.CreateDirectory(logDirectory);
            PurgeOldLogs(logDirectory, retentionDays, "*.log");

            string scriptName = Path.GetFileNameWithoutExtension(sourceScript);
            string dateStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            _scriptLogger?.Dispose();
            _scriptLogger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .Enrich.FromLogContext()
                .WriteTo.File(
                    path: Path.Combine(logDirectory, $"{scriptName}_{dateStamp}-.log"),
                    rollingInterval: RollingInterval.Infinite,
                    fileSizeLimitBytes: fileSizeLimitMb > 0 ? (long?)fileSizeLimitMb * 1024 * 1024 : null,
                    rollOnFileSizeLimit: true,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}]{SessionId: [sid=]:l} {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            Log(LogLevel.Info, "--- ETL-SQL Script Log Started ---");
        }

        public void InitializeTestLogger(string logDirectory, int retentionDays = 30, int fileSizeLimitMb = 50)
        {
            logDirectory = ResolvePath(logDirectory);
            Directory.CreateDirectory(logDirectory);

            _testLogger?.Dispose();
            _testLogger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .Enrich.FromLogContext()
                .WriteTo.File(
                    path: Path.Combine(logDirectory, "test-.log"),
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: fileSizeLimitMb > 0 ? (long?)fileSizeLimitMb * 1024L * 1024L : null,
                    rollOnFileSizeLimit: true,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}]{SessionId: [sid=]:l} {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            Log(LogLevel.Info, "=== ETL-SQL Test Logger Initialized ===");
        }

        private string ResolvePath(string path)
        {
            return Path.IsPathRooted(path) ? path : Path.GetFullPath(path);
        }

        private void PurgeOldLogs(string directory, int days, string pattern)
        {
            if (days <= 0) return;
            var cutoff = DateTime.Now.AddDays(-days);
            try
            {
                foreach (var file in Directory.EnumerateFiles(directory, pattern))
                {
                    if (File.GetLastWriteTime(file) < cutoff)
                        File.Delete(file);
                }
            }
            catch { }
        }

        public void Dispose()
        {
            _sessionContext.Value?.Dispose();
            _testLogger?.Dispose();
            _scriptLogger?.Dispose();
            _appLogger?.Dispose();
            _msLoggerFactory?.Dispose();
        }
    }
}
