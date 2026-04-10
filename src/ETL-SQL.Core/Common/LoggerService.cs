using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace ETL_SQL.Common
{
    /// <summary>
    /// Default implementation of ILogger that handles multiple Serilog sinks (App, Script, Test),
    /// Console output, and UI callbacks.
    /// </summary>
    public class LoggerService : ILogger, IDisposable
    {
        private Serilog.Core.Logger? _appLogger;
        private Serilog.Core.Logger? _scriptLogger;
        private Serilog.Core.Logger? _testLogger;
        private ILoggerFactory? _msLoggerFactory;
        private Microsoft.Extensions.Logging.ILogger? _msLogger;

        public bool IsSilent { get; set; }
        public bool IsVerbose { get; set; }
        public bool IsFileLogging { get; set; }
        public bool SuppressConsole { get; set; }
        public bool IsJsonMode { get; set; }
        public event Action<string, ConsoleColor>? OnMessage;

        public bool IsDebugEnabled => IsVerbose;
        public bool IsVerboseEnabled => IsVerbose;

        public void Log(LogLevel level, string message, Exception? ex = null)
        {
            if (IsSilent && level != LogLevel.Error) return;
            if (level == LogLevel.Debug && !IsVerbose) return;

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

            string formattedMessage = message;
            if (ex != null) formattedMessage += $"{Environment.NewLine}Exception: {ex.Message}";

            // 1. Serilog - Sinks
            _appLogger?.Write(serilogLevel, ex, message);
            _scriptLogger?.Write(serilogLevel, ex, message);
            _testLogger?.Write(serilogLevel, ex, message);

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
                _msLogger.Log(msLevel, ex, message);
            }

            // 3. Console
            if (!IsSilent && !SuppressConsole)
            {
                if (color != ConsoleColor.White) Console.ForegroundColor = color;
                Console.WriteLine(formattedMessage);
                if (color != ConsoleColor.White) Console.ResetColor();
            }

            // 4. UI Callback
            OnMessage?.Invoke(formattedMessage, color);
            
            // 5. Legacy Bridge - Ensure static Logger.OnMessage is also called
            Logger.OnMessage?.Invoke(formattedMessage, color);
        }

        public void InitializeAppLogger(string logDirectory, int retentionDays = 30, int fileSizeLimitMb = 10)
        {
            logDirectory = ResolvePath(logDirectory);
            Directory.CreateDirectory(logDirectory);
            PurgeOldLogs(logDirectory, retentionDays, "*.log");

            _appLogger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(
                    path: Path.Combine(logDirectory, "etlsql-.log"),
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: fileSizeLimitMb > 0 ? (long?)fileSizeLimitMb * 1024 * 1024 : null,
                    rollOnFileSizeLimit: true,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
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
                .WriteTo.File(
                    path: Path.Combine(logDirectory, $"{scriptName}_{dateStamp}-.log"),
                    rollingInterval: RollingInterval.Infinite,
                    fileSizeLimitBytes: fileSizeLimitMb > 0 ? (long?)fileSizeLimitMb * 1024 * 1024 : null,
                    rollOnFileSizeLimit: true,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
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
                .WriteTo.File(
                    path: Path.Combine(logDirectory, "test-.log"),
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: fileSizeLimitMb > 0 ? (long?)fileSizeLimitMb * 1024L * 1024L : null,
                    rollOnFileSizeLimit: true,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            Log(LogLevel.Info, "=== ETL-SQL Test Logger Initialized ===");
        }

        private string ResolvePath(string path)
        {
            // Simplified resolution for now, can bridge to Logger.ResolveRootPath if needed
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
            _testLogger?.Dispose();
            _scriptLogger?.Dispose();
            _appLogger?.Dispose();
            _msLoggerFactory?.Dispose();
        }
    }
}
