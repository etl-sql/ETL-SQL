using System;
using System.Collections.Generic;
using System.Diagnostics;
using ETL_SQL.Common;
using ETL_SQL.Core;

namespace ETL_SQL.Engine.Services;
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
    private long _lastQueueWaitMs;
    private long _lastLockWaitMs;
    private long _lastDqValidationTicks;
    private long _lastDqRowsValidated;
    private long _lastDqRowsQuarantined;
    private long _lastDqRowsWarned;
    private long _lastSpillReadBytes;
    private int _lastSpillExtentCount;
    private int _lastPartitionPassCount;
    private long _lastAggregateGroupsCount;
    private int _lastSortSpillCount;
    private TimeSpan _lastCpuTime;

    // Cached: Process.GetCurrentProcess() allocates, and this is read twice per statement.
    private static readonly System.Diagnostics.Process CurrentProcess =
        System.Diagnostics.Process.GetCurrentProcess();

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
        _lastQueueWaitMs = _context.Telemetry.QueueWaitMs;
        _lastLockWaitMs = _context.Telemetry.LockWaitMs;
        _lastDqValidationTicks = _context.Telemetry.DataQualityValidationTicks;
        _lastDqRowsValidated = _context.DataQuality.RowsValidated;
        _lastDqRowsQuarantined = _context.DataQuality.RowsQuarantined;
        _lastDqRowsWarned = _context.DataQuality.RowsWarned;
        _lastSpillReadBytes = _context.Telemetry.SpillReadBytes;
        _lastSpillExtentCount = _context.Telemetry.SpillExtentCount;
        _lastPartitionPassCount = _context.Telemetry.PartitionPassCount;
        _lastAggregateGroupsCount = _context.Telemetry.AggregateGroupsCount;
        _lastSortSpillCount = _context.Telemetry.SortSpillCount;
        _lastCpuTime = SafeCpuTime();
    }

    /// <summary>
    /// Process CPU time, or <see cref="TimeSpan.Zero"/> where the platform refuses it. A profiling
    /// counter must never be the reason a statement fails.
    /// </summary>
    private static TimeSpan SafeCpuTime()
    {
        try { return CurrentProcess.TotalProcessorTime; }
        catch { return TimeSpan.Zero; }
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
            RecursiveDepth = _context.EngineContext.CurrentRecursiveDepth,
            QueueWaitMs = _context.Telemetry.QueueWaitMs - _lastQueueWaitMs,
            LockWaitMs = _context.Telemetry.LockWaitMs - _lastLockWaitMs,
            DataQualityRowsValidated = _context.DataQuality.RowsValidated - _lastDqRowsValidated,
            DataQualityRowsQuarantined = _context.DataQuality.RowsQuarantined - _lastDqRowsQuarantined,
            DataQualityRowsWarned = _context.DataQuality.RowsWarned - _lastDqRowsWarned,
            DataQualityValidationMs =
                (_context.Telemetry.DataQualityValidationTicks - _lastDqValidationTicks)
                    * 1000 / Stopwatch.Frequency,
            SpillReadBytes = _context.Telemetry.SpillReadBytes - _lastSpillReadBytes,
            SpillExtentCount = _context.Telemetry.SpillExtentCount - _lastSpillExtentCount,
            PartitionPassCount = _context.Telemetry.PartitionPassCount - _lastPartitionPassCount,
            AggregateGroupsCount = _context.Telemetry.AggregateGroupsCount - _lastAggregateGroupsCount,
            // A ratio, not a counter — the current value is the statement's own, not a running sum.
            AggregateExpansionRatio = _context.Telemetry.AggregateExpansionRatio,
            SortSpillCount = _context.Telemetry.SortSpillCount - _lastSortSpillCount,
            CpuTimeMs = (long)(SafeCpuTime() - _lastCpuTime).TotalMilliseconds
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
