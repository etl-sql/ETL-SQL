using System;

namespace ETL_SQL.Core;

public class ExecutionMetrics
{
    public string Sql { get; set; } = string.Empty;
    public long DurationMs { get; set; }
    public long MemoryDeltaBytes { get; set; }
    public long RowsProcessed { get; set; }
    public long SpilledBytes { get; set; }
    public long SubqueryCacheHits { get; set; }
    public long SubqueryCacheMisses { get; set; }
    public long SubquerySpilledBytes { get; set; }
    public int PartitionsCount { get; set; }
    public int RecursiveDepth { get; set; }
    public string? IndexName { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public long QueueWaitMs { get; set; }
    public long LockWaitMs { get; set; }

    // ── Large-dataset execution ───────────────────────────────────────────────────────────────
    // These counters have existed since the spill/partition work but never reached the profile, so
    // the surface an operator profiles with was still describing the pre-spill engine.

    /// <summary>Bytes read back from spill files — the other half of <see cref="SpilledBytes"/>.</summary>
    public long SpillReadBytes { get; set; }

    /// <summary>Spill extents created. A high count with low bytes means fragmentation, not volume.</summary>
    public int SpillExtentCount { get; set; }

    /// <summary>Partition passes performed. More than one means the data did not fit the budget.</summary>
    public int PartitionPassCount { get; set; }

    /// <summary>Distinct groups produced by aggregation — the cardinality that drives memory.</summary>
    public long AggregateGroupsCount { get; set; }

    /// <summary>Aggregate output rows over input rows.</summary>
    public double AggregateExpansionRatio { get; set; }

    /// <summary>Times a sort spilled to disk.</summary>
    public int SortSpillCount { get; set; }

    /// <summary>
    /// CPU milliseconds consumed while this statement ran. Distinguishes "slow because it was
    /// working" from "slow because it was waiting" — the latter shows duration without CPU.
    /// </summary>
    public long CpuTimeMs { get; set; }

    // ── Data quality ──────────────────────────────────────────────────────────────────────────
    // What a rule costs, per statement. The run-level tallies in DataQualityReport answer "what did
    // the rules find"; these answer "what did they cost me here", which is the question asked when
    // a load has slowed down and rules are the thing that changed.

    /// <summary>Rows this statement put through rule evaluation.</summary>
    public long DataQualityRowsValidated { get; set; }

    /// <summary>Rows this statement diverted to a quarantine target.</summary>
    public long DataQualityRowsQuarantined { get; set; }

    /// <summary>Rows this statement recorded as warnings.</summary>
    public long DataQualityRowsWarned { get; set; }

    /// <summary>
    /// Wall-clock milliseconds spent inside rule evaluation and capture for this statement.
    /// Measured only while profiling is on — which is the default, so this is normally populated;
    /// <c>SET PROFILE OFF</c> removes both the measurement and its per-row cost.
    /// </summary>
    public long DataQualityValidationMs { get; set; }
}
