using System;
using System.Collections.Generic;
using System.Diagnostics;
using ETL_SQL.Common;
using ETL_SQL.Core;

namespace ETL_SQL.Engine.Services
{
    /// <summary>
    /// Handles the collection and reporting of execution metrics and performance tips.
    /// Provides feedback to the user about query performance and resource usage.
    /// </summary>
    public class ExecutionMetricsReporter(IExecutionContext context)
    {
        private readonly IExecutionContext _context = context;

        private long _lastMemoryUsage;
        private long _lastSpilledBytes;
        private long _lastSubqHits;
        private long _lastSubqMisses;
        private long _lastSubqSpilledBytes;
        private int _lastPartitionsCount;
        private long _startRows;

        public List<ExecutionMetrics> ProfileMetrics => _context.Telemetry.ProfileMetrics;

        /// <summary>
        /// Captures baseline metrics before a statement begins execution.
        /// </summary>
        public void ReportPreExecutionMetrics(Statement s)
        {
            if (!_context.Telemetry.IsProfiling) return;
            _lastMemoryUsage = GC.GetTotalMemory(false);
            _lastSpilledBytes = _context.Telemetry.TotalSpilledBytes;
            _lastSubqHits = _context.Telemetry.SubqueryCacheHits;
            _lastSubqMisses = _context.Telemetry.SubqueryCacheMisses;
            _lastSubqSpilledBytes = _context.Telemetry.SubquerySpilledBytes;
            _lastPartitionsCount = _context.Telemetry.PartitionsCount;
            _startRows = _context.Telemetry.RowsProcessed;
        }

        /// <summary>
        /// Captures and logs metrics after a statement completes execution.
        /// </summary>
        public void ReportPostExecutionMetrics(Statement s, long ms)
        {
            if (!_context.Telemetry.IsProfiling) return;

            var currentMemory = GC.GetTotalMemory(false);
            var rowsProcessed = _context.Telemetry.RowsProcessed - _startRows;

            // Note: We still update the context's LastStatementRowsProcessed so @@ROWCOUNT works
            _context.Telemetry.LastStatementRowsProcessed = rowsProcessed;

            _context.Telemetry.ProfileMetrics.Add(new ExecutionMetrics
            {
                Sql = s.ToSql(),
                DurationMs = ms,
                MemoryDeltaBytes = currentMemory - _lastMemoryUsage,
                RowsProcessed = rowsProcessed,
                IndexName = _context.DataContext.LastIndexUsedName,
                Timestamp = DateTime.Now,
                SpilledBytes = _context.Telemetry.TotalSpilledBytes - _lastSpilledBytes,
                SubqueryCacheHits = _context.Telemetry.SubqueryCacheHits - _lastSubqHits,
                SubqueryCacheMisses = _context.Telemetry.SubqueryCacheMisses - _lastSubqMisses,
                SubquerySpilledBytes = _context.Telemetry.SubquerySpilledBytes - _lastSubqSpilledBytes,
                PartitionsCount = _context.Telemetry.PartitionsCount - _lastPartitionsCount,
                RecursiveDepth = _context.EngineContext.CurrentRecursiveDepth
            });
        }

        /// <summary>
        /// Analyzes the executed statement and provides performance optimization tips.
        /// </summary>
        public void ProvideTips(Statement s)
        {
            if (s is SelectStatement sel && sel.Joins?.Count > 1 && string.IsNullOrEmpty(_context.DataContext.LastIndexUsedName))
            {
                _context.LoggingContext.Logger.Warning("Performance Tip: Multi-join query detected without index usage. Consider adding indexes to JOIN columns.");
            }
        }

        public void Clear() => _context.Telemetry.ProfileMetrics.Clear();
    }
}
