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
    public class ExecutionMetricsReporter(Evaluator evaluator)
    {
        private readonly Evaluator _evaluator = evaluator;
        private long _lastMemoryUsage;
        private long _lastSpilledBytes;
        private int _lastPartitionsCount;

        /// <summary>
        /// Captures baseline metrics before a statement begins execution.
        /// </summary>
        public void ReportPreExecutionMetrics(Statement s)
        {
            if (!_evaluator.IsProfiling) return;
            _lastMemoryUsage = GC.GetTotalMemory(false);
            _lastSpilledBytes = _evaluator.TotalSpilledBytes;
            _lastPartitionsCount = _evaluator.PartitionsCount;
        }

        /// <summary>
        /// Captures and logs metrics after a statement completes execution.
        /// </summary>
        public void ReportPostExecutionMetrics(Statement s, long ms)
        {
            if (!_evaluator.IsProfiling) return;
            var currentMemory = GC.GetTotalMemory(false);
            _evaluator.ProfileMetrics.Add(new ExecutionMetrics
            {
                Sql = s.ToSql(),
                DurationMs = ms,
                MemoryDeltaBytes = currentMemory - _lastMemoryUsage,
                RowsProcessed = _evaluator.LastStatementRowsProcessed,
                IndexName = _evaluator.LastIndexUsedName,
                Timestamp = DateTime.Now,
                SpilledBytes = _evaluator.TotalSpilledBytes - _lastSpilledBytes,
                PartitionsCount = _evaluator.PartitionsCount - _lastPartitionsCount,
                RecursiveDepth = _evaluator.MaxRecursiveDepth
            });
        }

        /// <summary>
        /// Analyzes the executed statement and provides performance optimization tips.
        /// </summary>
        public void ProvideTips(Statement s)
        {
            if (s is SelectStatement sel && sel.Joins?.Count > 1 && string.IsNullOrEmpty(_evaluator.LastIndexUsedName))
            {
                _evaluator.Log("Performance Tip: Multi-join query detected without index usage. Consider adding indexes to JOIN columns.", ConsoleColor.Yellow);
            }
        }
    }
}
