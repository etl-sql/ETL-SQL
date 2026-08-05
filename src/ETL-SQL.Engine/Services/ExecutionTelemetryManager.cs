using System.Collections.Generic;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Planning;

namespace ETL_SQL.Engine.Services;

public class ExecutionTelemetryManager : ITelemetryContext
{
    private readonly object _planDecisionLock = new();
    private readonly List<PlanDecision> _planDecisions = new();

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

    /// <summary>
    /// Stopwatch ticks spent in data-quality rule evaluation and capture. Accumulated only while
    /// profiling is on — the row pipeline should not pay for a measurement nobody asked for.
    /// </summary>
    public long DataQualityValidationTicks { get; set; } = 0;

    public List<ExecutionMetrics> ProfileMetrics { get; } = new();

    public ETL_SQL.Core.Common.ExecutionTree ExecutionTree { get; } = new();

    public int MaxPlanDecisions { get; set; } = 1024;

    public IReadOnlyList<PlanDecision> PlanDecisions
    {
        get
        {
            lock (_planDecisionLock)
                return _planDecisions.ToArray();
        }
    }

    public void RecordPlanDecision(PlanDecision decision)
    {
        if (!TelemetryEnabled || MaxPlanDecisions <= 0) return;

        var sanitized = SanitizeDecision(decision);
        lock (_planDecisionLock)
        {
            while (_planDecisions.Count >= MaxPlanDecisions)
                _planDecisions.RemoveAt(0);
            _planDecisions.Add(sanitized);
        }
    }

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
        DataQualityValidationTicks = 0;
        ProfileMetrics.Clear();
        ExecutionTree.Clear();
        lock (_planDecisionLock)
            _planDecisions.Clear();
    }

    private static PlanDecision SanitizeDecision(PlanDecision decision)
    {
        var attributes = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in decision.Attributes)
        {
            attributes[Normalize(key)] = SecretRedactor.MaskIfSensitive(key, Normalize(value));
        }

        return decision with
        {
            QueryId = Normalize(decision.QueryId),
            OperatorId = Normalize(decision.OperatorId),
            CandidatePath = Normalize(decision.CandidatePath),
            ReasonCode = Normalize(decision.ReasonCode),
            Message = SecretRedactor.Redact(Normalize(decision.Message)) ?? string.Empty,
            Attributes = attributes
        };
    }

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace('\r', ' ').Replace('\n', ' ').Trim();
}
