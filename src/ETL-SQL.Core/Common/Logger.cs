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
    /// Central logging façade for the ETL-SQL engine.
    /// Maintains backward-compatible static API while delegating to Serilog for
    /// rolling-file output, retention cleanup, and structured logging.
    /// </summary>
    public static class Logger
    {
        // ─── Public state (backward-compatible) ─────────────────────────────
        public static bool IsSilent       { get; set; } = false;
        public static bool IsVerbose      { get; set; } = false;
        public static bool IsFileLogging  { get; set; } = false;
        public static bool SuppressConsole { get; set; } = false;

        /// <summary>
        /// The active ILogger instance to which all static calls are delegated.
        /// Defaults to a ConsoleLogger if not set.
        /// </summary>
        private static ILogger? _instance;
        public static ILogger Instance 
        { 
            get => _instance ??= new EngineLogger("Global");
            set => _instance = value;
        }

        /// <summary>
        /// Callback invoked for every message (e.g. UI panels that mirror log output).
        /// </summary>
        public static Action<string, ConsoleColor>? OnMessage { get; set; }

        [ThreadStatic]
        private static bool ExecutingInInstance;

        // ─── Internal Serilog loggers ───────────────────────────────────────
        private static Serilog.Core.Logger? _appLogger;      // application-level logger
        private static Serilog.Core.Logger? _scriptLogger;   // per-script-run logger
        private static Serilog.Core.Logger? _testLogger;     // unit/integration test logger

        // MEL bridge kept for injected ILogger<T> consumers
        private static ILoggerFactory? _msLoggerFactory;
        private static Microsoft.Extensions.Logging.ILogger? _msLogger;

        // ─── Application logger wiring (called from DependencyInjectionSetup) ─
        public static ILoggerFactory? Factory
        {
            get => _msLoggerFactory;
            set
            {
                _msLoggerFactory = value;
                if (value != null)
                    _msLogger = value.CreateLogger("ETL_SQL.Common.Logger");
            }
        }

        public static void InitializeAppLogger(string logDirectory, int retentionDays = 30, int fileSizeLimitMb = 10)
        {
            if (Instance is LoggerService service)
            {
                service.InitializeAppLogger(logDirectory, retentionDays, fileSizeLimitMb);
            }
        }

        public static void InitializeScriptLogger(string sourceScript, string logDirectory, int retentionDays = 30, int fileSizeLimitMb = 10)
        {
            if (Instance is LoggerService service)
            {
                service.InitializeScriptLogger(sourceScript, logDirectory, retentionDays, fileSizeLimitMb);
            }
        }

        public static void InitializeTestLogger(string logDirectory, int retentionDays = 30, int fileSizeLimitMb = 50)
        {
            if (Instance is LoggerService service)
            {
                service.InitializeTestLogger(logDirectory, retentionDays, fileSizeLimitMb);
            }
        }

        /// <summary>
        /// Resolves a path relative to the solution root (detected by presence of ETL-SQL.slnx).
        /// If the path is already absolute, it is returned as-is.
        /// </summary>
        /// <param name="path">The relative or absolute path to resolve.</param>
        /// <returns>The fully qualified absolute path.</returns>
        public static string ResolveRootPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            if (Path.IsPathRooted(path)) return path;

            // If the path starts with . (current or parent), resolve relative to CWD immediately to avoid "smart" root searching.
            if (path.StartsWith(".") || path.StartsWith("/") || path.StartsWith("\\"))
                return Path.GetFullPath(path);

            // Try searching up from BaseDirectory first (most reliable for deployed apps)
            var root = FindMarker(AppContext.BaseDirectory, "ETL-SQL.slnx");
            
            // Try searching up from CurrentDirectory as a second chance (common in dev/CLI)
            if (root == null)
                root = FindMarker(Environment.CurrentDirectory, "ETL-SQL.slnx");

            // Third chance: search up from the executing assembly location if different
            if (root == null)
            {
                var assemblyLoc = Path.GetDirectoryName(typeof(Logger).Assembly.Location);
                if (!string.IsNullOrEmpty(assemblyLoc))
                    root = FindMarker(assemblyLoc, "ETL-SQL.slnx");
            }

            if (root != null)
                return Path.GetFullPath(Path.Combine(root, path));

            // Fallback: search for other common markers like .git
            root = FindMarker(AppContext.BaseDirectory, ".git") 
                ?? FindMarker(Environment.CurrentDirectory, ".git")
                ?? FindMarker(Path.GetDirectoryName(typeof(Logger).Assembly.Location) ?? "", ".git");

            if (root != null)
                return Path.GetFullPath(Path.Combine(root, path));

            // Absolute fallback: just resolve relative to current directory
            return Path.GetFullPath(path);
        }

        /// <summary>
        /// Searches upwards from a directory to find a file system marker.
        /// </summary>
        /// <param name="startDir">The directory to start the search from.</param>
        /// <param name="marker">The file or directory name to look for.</param>
        /// <returns>The path to the directory containing the marker, or null if not found.</returns>
        private static string? FindMarker(string startDir, string marker)
        {
            if (string.IsNullOrEmpty(startDir)) return null;
            var current = new DirectoryInfo(startDir);
            while (current != null)
            {
                try
                {
                    if (current.GetFileSystemInfos(marker).Any())
                        return current.FullName;
                }
                catch { /* Ignore access denied etc */ }
                current = current.Parent;
            }
            return null;
        }

        /// <summary>
        /// Legacy compatibility shim. Prefer InitializeScriptLogger.
        /// </summary>
        public static void InitializeLogFile(string sourceScript, string overridePath)
            => InitializeScriptLogger(sourceScript, string.IsNullOrWhiteSpace(overridePath) ? "logs/scripts" : overridePath);

        // ─── Logging entry points (backward-compatible) ─────────────────────

        /// <summary>
        /// Writes a verbose message to the log if IsVerbose is true.
        /// </summary>
        /// <param name="message">The message to log.</param>
        /// <param name="color">The console color to use.</param>
        public static void Verbose(string message, ConsoleColor color = ConsoleColor.DarkGray)
        {
            if (IsVerbose)
                WriteInternal(LogEventLevel.Debug, $"[VERBOSE] {message}", color);
        }

        /// <summary>
        /// Writes a message to the log with a specific console color.
        /// </summary>
        /// <param name="message">The message to log.</param>
        /// <param name="color">The console color to use.</param>
        public static void WriteLine(string message, ConsoleColor color = ConsoleColor.White)
            => WriteInternal(MapLevel(color), message, color);

        // ─── Private helpers ────────────────────────────────────────────────

        /// <summary>
        /// Internal method that handles routing messages to all active collectors (Serilog, MEL, Console, UI Callback).
        /// </summary>
        /// <param name="serilogLevel">The severity level for Serilog.</param>
        /// <param name="message">The plaintext message.</param>
        /// <param name="color">The console color.</param>
        private static void WriteInternal(LogEventLevel serilogLevel, string message, ConsoleColor color)
        {
            if (ExecutingInInstance) return;

            ExecutingInInstance = true;
            try
            {
                var level = serilogLevel switch
                {
                    LogEventLevel.Error => LogLevel.Error,
                    LogEventLevel.Warning => LogLevel.Warning,
                    LogEventLevel.Debug => LogLevel.Debug,
                    _ => LogLevel.Info
                };
                Instance.Log(level, message);
            }
            finally { ExecutingInInstance = false; }
        }

        /// <summary>
        /// Maps a ConsoleColor to a Serilog LogEventLevel.
        /// </summary>
        private static LogEventLevel MapLevel(ConsoleColor color) => color switch
        {
            ConsoleColor.Red      => LogEventLevel.Error,
            ConsoleColor.Yellow   => LogEventLevel.Warning,
            ConsoleColor.DarkGray => LogEventLevel.Debug,
            _                     => LogEventLevel.Information,
        };

        /// <summary>Delete log files older than <paramref name="days"/> from <paramref name="directory"/>.</summary>
        private static void PurgeOldLogs(string directory, int days, string pattern)
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
            catch
            {
                // Best-effort; don't crash the engine if cleanup fails
            }
        }

        /// <summary>Flush and close all open log files. Call on app shutdown.</summary>
        public static void CloseAndFlush()
        {
            if (Instance is IDisposable disposable) disposable.Dispose();
        }
    }
}
