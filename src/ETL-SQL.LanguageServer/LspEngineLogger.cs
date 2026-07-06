using System;
using ETL_SQL.Common;
using Microsoft.Extensions.Logging;

namespace ETL_SQL.LSP
{
    /// <summary>
    /// Adapts the engine's internal custom <see cref="ETL_SQL.Common.ILogger"/> to Microsoft's
    /// <see cref="Microsoft.Extensions.Logging.ILogger"/>, routing all engine logs to the LSP client output channel.
    /// </summary>
    public class LspEngineLogger : ETL_SQL.Common.ILogger
    {
        private readonly Microsoft.Extensions.Logging.ILogger _logger;

        public LspEngineLogger(Microsoft.Extensions.Logging.ILogger logger)
        {
            _logger = logger;
        }

        public string? SessionId { get; set; }
        public bool IsDebugEnabled => true;
        public bool IsVerboseEnabled => true;
        public bool IsVerbose { get; set; }
        public bool SuppressConsole { get; set; }
        public bool IsJsonMode { get; set; }
        public event Action<string, string?, ConsoleColor>? OnMessage;

        public void Log(ETL_SQL.Common.LogLevel level, string message, Exception? ex = null)
        {
            var msLevel = level switch
            {
                ETL_SQL.Common.LogLevel.Debug => Microsoft.Extensions.Logging.LogLevel.Debug,
                ETL_SQL.Common.LogLevel.Info => Microsoft.Extensions.Logging.LogLevel.Information,
                ETL_SQL.Common.LogLevel.Warning => Microsoft.Extensions.Logging.LogLevel.Warning,
                ETL_SQL.Common.LogLevel.Error => Microsoft.Extensions.Logging.LogLevel.Error,
                _ => Microsoft.Extensions.Logging.LogLevel.Information
            };

            var safeMessage = ETL_SQL.Core.Common.SecretRedactor.Redact(message) ?? string.Empty;
            _logger.Log(msLevel, ex, "{Message}", safeMessage);

            OnMessage?.Invoke(safeMessage, SessionId, ConsoleColor.White);
        }
    }
}
