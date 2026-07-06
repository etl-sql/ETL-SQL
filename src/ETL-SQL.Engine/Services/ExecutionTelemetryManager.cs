using System.Collections.Generic;
using ETL_SQL.Common;
using ETL_SQL.Core;

namespace ETL_SQL.Engine.Services;

public class ExecutionTelemetryManager : ITelemetryContext
{
    private long _rowsProcessed = 0;
    public long RowsProcessed
    {
        get => System.Threading.Interlocked.Read(ref _rowsProcessed);
        set => System.Threading.Interlocked.Exchange(ref _rowsProcessed, value);
    }

    private long _lastStatementRowsProcessed = 0;
    public long LastStatementRowsProcessed
    {
        get => System.Threading.Interlocked.Read(ref _lastStatementRowsProcessed);
        set => System.Threading.Interlocked.Exchange(ref _lastStatementRowsProcessed, value);
    }

    private long _totalSpilledBytes = 0;
    public long TotalSpilledBytes
    {
        get => System.Threading.Interlocked.Read(ref _totalSpilledBytes);
        set => System.Threading.Interlocked.Exchange(ref _totalSpilledBytes, value);
    }

    private long _spillReadBytes;
    public long SpillReadBytes
    {
        get => System.Threading.Interlocked.Read(ref _spillReadBytes);
        set => System.Threading.Interlocked.Exchange(ref _spillReadBytes, value);
    }

    private int _spillExtentCount;
    public int SpillExtentCount
    {
        get => System.Threading.Volatile.Read(ref _spillExtentCount);
        set => System.Threading.Interlocked.Exchange(ref _spillExtentCount, value);
    }

    public bool TelemetryEnabled { get; set; } = true;

    public int PartitionsCount { get; set; } = 0;

    private int _partitionPassCount;
    public int PartitionPassCount
    {
        get => System.Threading.Volatile.Read(ref _partitionPassCount);
        set => System.Threading.Interlocked.Exchange(ref _partitionPassCount, value);
    }

    private long _aggregateGroupsCount = 0;
    public long AggregateGroupsCount
    {
        get => System.Threading.Interlocked.Read(ref _aggregateGroupsCount);
        set => System.Threading.Interlocked.Exchange(ref _aggregateGroupsCount, value);
    }

    public double AggregateExpansionRatio { get; set; } = 1.0;

    public long LastExecutionTimeMs { get; set; }

    private long _subqueryCacheHits = 0;
    public long SubqueryCacheHits
    {
        get => System.Threading.Interlocked.Read(ref _subqueryCacheHits);
        set => System.Threading.Interlocked.Exchange(ref _subqueryCacheHits, value);
    }

    private long _subqueryCacheMisses = 0;
    public long SubqueryCacheMisses
    {
        get => System.Threading.Interlocked.Read(ref _subqueryCacheMisses);
        set => System.Threading.Interlocked.Exchange(ref _subqueryCacheMisses, value);
    }

    public int SubquerySpillCount { get; set; } = 0;
    public long SubquerySpilledBytes { get; set; } = 0;

    public int SortSpillCount { get; set; } = 0;
    public int FetchStatus { get; set; } = 0;

    public bool IsProfiling { get; set; } = true;
    public long QueueWaitMs { get; set; } = 0;
    public long LockWaitMs { get; set; } = 0;

    public List<ExecutionMetrics> ProfileMetrics { get; } = new();

    public ETL_SQL.Core.Common.ExecutionTree ExecutionTree { get; } = new();

    public void Clear()
    {
        RowsProcessed = 0;
        LastStatementRowsProcessed = 0;
        TotalSpilledBytes = 0;
        SpillReadBytes = 0;
        SpillExtentCount = 0;
        PartitionsCount = 0;
        PartitionPassCount = 0;
        AggregateGroupsCount = 0;
        AggregateExpansionRatio = 1.0;
        LastExecutionTimeMs = 0;
        SubqueryCacheHits = 0;
        SubqueryCacheMisses = 0;
        SubquerySpillCount = 0;
        SubquerySpilledBytes = 0;
        SortSpillCount = 0;
        FetchStatus = 0;
        QueueWaitMs = 0;
        LockWaitMs = 0;
        ProfileMetrics.Clear();
        ExecutionTree.Clear();
    }
}
