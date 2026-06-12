using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Core.Common;

namespace ETL_SQL.Engine
{
    public partial class Evaluator
    {
        [System.Obsolete("Use Telemetry.TotalSpilledBytes")]
        public long TotalSpilledBytes => Telemetry.TotalSpilledBytes;

        public bool IsProfiling { get => Telemetry.IsProfiling; set => Telemetry.IsProfiling = value; }
        public long RowsProcessed => Telemetry.RowsProcessed;
        public int PartitionsCount => Telemetry.PartitionsCount;
        public double AggregateExpansionRatio => Telemetry.AggregateExpansionRatio;
        public bool TelemetryEnabled { get => _options.TelemetryEnabled; set { _options.TelemetryEnabled = value; Telemetry.TelemetryEnabled = value; } }
        public long AggregateGroupsCount => Telemetry.AggregateGroupsCount;
        public List<ExecutionMetrics> ProfileMetrics => Telemetry.ProfileMetrics;
        public List<LogEntry> Messages { get; } = new();
        public int MaxMessages { get; set; } = 1000;
        public ITelemetryContext Telemetry => _registry.TelemetryManager;

        public ErrorInfo? LastError { get; set; }
        public ErrorInfo? ActiveException { get; set; }
        public int PreviousErrorNumber { get; set; } = 0;

        public long MemoryUsageBytes => _spillCoordinator.MemoryUsageBytes;
        public Task<bool> SpillAsync() => _spillCoordinator.SpillAsync();
        public string SpillToken => $"Session_{SessionId}";

        public void Log(string message, ConsoleColor color = ConsoleColor.White, bool forwardToLogger = true)
        {
            var scrubbed = Scrub(message);

            if (forwardToLogger)
            {
                _logger.WriteLine(scrubbed, color);

                // If output is redirected, the OnMessage event (subscribed in constructor) 
                // will handle adding to the Messages list to avoid double-capture.
                if (RedirectOutput) return;
            }

            lock (_messagesLock)
            {
                Messages.Add(new LogEntry(scrubbed, color, DateTime.Now));
                if (Messages.Count > MaxMessages)
                {
                    Messages.RemoveAt(0);
                    if (Messages.Count > 0 && !Messages[0].Message.StartsWith("[TRUNCATED]"))
                    {
                        var first = Messages[0];
                        Messages[0] = first with { Message = "[TRUNCATED] " + first.Message };
                    }
                }
            }
        }

        public static string Scrub(string message)
        {
            if (string.IsNullOrEmpty(message)) return message;
            // Scrub standard connection string passwords, tokens, etc.
            var res = System.Text.RegularExpressions.Regex.Replace(message, @"(?i)(password|pwd|token|secret)\s*=\s*[^\s;]+", "$1=********");
            // Scrub ETL-SQL encrypted constants
            res = System.Text.RegularExpressions.Regex.Replace(res, @"ENC:[a-zA-Z0-9+/=]+", "ENC:********");
            return res;
        }
    }
}
