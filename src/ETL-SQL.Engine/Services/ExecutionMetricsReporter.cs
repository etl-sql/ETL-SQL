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
        private int _lastPartitionsCount;
        private long _startRows;

        public List<ExecutionMetrics> ProfileMetrics => _context.ProfileMetrics;

        /// <summary>
        /// Captures baseline metrics before a statement begins execution.
        /// </summary>
        public void ReportPreExecutionMetrics(Statement s)
        {
            if (!_context.IsProfiling) return;
            _lastMemoryUsage = GC.GetTotalMemory(false);
            _lastSpilledBytes = _context.TotalSpilledBytes;
            _lastPartitionsCount = _context.PartitionsCount;
            _startRows = _context.RowsProcessed;
        }

        /// <summary>
        /// Captures and logs metrics after a statement completes execution.
        /// </summary>
        public void ReportPostExecutionMetrics(Statement s, long ms)
        {
            if (!_context.IsProfiling) return;
            
            var currentMemory = GC.GetTotalMemory(false);
            var rowsProcessed = _context.RowsProcessed - _startRows;
            
            // Note: We still update the context's LastStatementRowsProcessed so @@ROWCOUNT works
            if (_context is Evaluator eval)
            {
                eval.LastStatementRowsProcessed = rowsProcessed;
            }

            _context.ProfileMetrics.Add(new ExecutionMetrics
            {
                Sql = s.ToSql(),
                DurationMs = ms,
                MemoryDeltaBytes = currentMemory - _lastMemoryUsage,
                RowsProcessed = rowsProcessed,
                IndexName = _context.LastIndexUsedName,
                Timestamp = DateTime.Now,
                SpilledBytes = _context.TotalSpilledBytes - _lastSpilledBytes,
                PartitionsCount = _context.PartitionsCount - _lastPartitionsCount,
                RecursiveDepth = _context.CurrentRecursiveDepth
            });
        }

        /// <summary>
        /// Analyzes the executed statement and provides performance optimization tips.
        /// </summary>
        public void ProvideTips(Statement s)
        {
            if (s is SelectStatement sel && sel.Joins?.Count > 1 && string.IsNullOrEmpty(_context.LastIndexUsedName))
            {
                _context.Logger.Warning("Performance Tip: Multi-join query detected without index usage. Consider adding indexes to JOIN columns.");
            }
        }

        public void Clear() => _context.ProfileMetrics.Clear();
    }
}
