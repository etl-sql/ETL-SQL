using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace ETL_SQL.Core.Profiling;

/// <summary>
/// One statement's measurements, as carried between a job process and the scheduler.
///
/// <para><b>Defined once, on purpose.</b> There are three execution paths — the in-process
/// <c>ScriptExecutorAdapter</c> (the default), the one-shot <c>--json</c> process, and the warm
/// runner with its own line protocol — and the latter two each described the run payload
/// separately, so every field added had to be written twice. This is the shared shape all of them
/// use, so it is written once.</para>
///
/// <para>Property names match <c>eng.profile</c>'s columns so an operator's query reads the same
/// whether they are looking at a live session or durable history.</para>
///
/// <para><see cref="Statement"/> is always normalized (see
/// <see cref="StatementTextNormalizer"/>): raw statement text carries literal values, and this
/// payload crosses into a store read by a different principal than the one who ran the script.</para>
/// </summary>
public sealed class StatementMetricsPayload
{
    [JsonPropertyName("statement")]
    public string Statement { get; set; } = string.Empty;

    [JsonPropertyName("duration_ms")]
    public long DurationMs { get; set; }

    [JsonPropertyName("rows_processed")]
    public long RowsProcessed { get; set; }

    [JsonPropertyName("cpu_time_ms")]
    public long CpuTimeMs { get; set; }

    [JsonPropertyName("spilled_bytes")]
    public long SpilledBytes { get; set; }

    [JsonPropertyName("spill_read_bytes")]
    public long SpillReadBytes { get; set; }

    [JsonPropertyName("partitions")]
    public int Partitions { get; set; }

    [JsonPropertyName("queue_wait_ms")]
    public long QueueWaitMs { get; set; }

    [JsonPropertyName("lock_wait_ms")]
    public long LockWaitMs { get; set; }

    [JsonPropertyName("index_used")]
    public string? IndexUsed { get; set; }

    [JsonPropertyName("dq_rows_validated")]
    public long DataQualityRowsValidated { get; set; }

    [JsonPropertyName("dq_rows_quarantined")]
    public long DataQualityRowsQuarantined { get; set; }

    [JsonPropertyName("dq_rows_warned")]
    public long DataQualityRowsWarned { get; set; }

    [JsonPropertyName("dq_validation_ms")]
    public long DataQualityValidationMs { get; set; }

    /// <summary>
    /// Whether this statement is the one that failed the run. Nothing in the engine records a
    /// per-statement failure today, so this is set by the caller that knows the run's outcome; it
    /// exists here because the selection rule must never discard a failed statement to make room.
    /// </summary>
    [JsonPropertyName("failed")]
    public bool Failed { get; set; }

    /// <summary>Projects engine measurements into the wire shape, normalizing the statement text.</summary>
    public static StatementMetricsPayload From(ExecutionMetrics metrics, bool failed = false) => new()
    {
        Statement = StatementTextNormalizer.Normalize(metrics.Sql),
        DurationMs = metrics.DurationMs,
        RowsProcessed = metrics.RowsProcessed,
        CpuTimeMs = metrics.CpuTimeMs,
        SpilledBytes = metrics.SpilledBytes,
        SpillReadBytes = metrics.SpillReadBytes,
        Partitions = metrics.PartitionsCount,
        QueueWaitMs = metrics.QueueWaitMs,
        LockWaitMs = metrics.LockWaitMs,
        IndexUsed = metrics.IndexName,
        DataQualityRowsValidated = metrics.DataQualityRowsValidated,
        DataQualityRowsQuarantined = metrics.DataQualityRowsQuarantined,
        DataQualityRowsWarned = metrics.DataQualityRowsWarned,
        DataQualityValidationMs = metrics.DataQualityValidationMs,
        Failed = failed
    };

    /// <summary>Default cap. A triage question is answered by the slow statements and the failure.</summary>
    public const int DefaultMaxStatements = 25;

    /// <summary>
    /// Reduces a run's statements to what is worth carrying.
    ///
    /// <para>The envelope is parsed as a single line and buffered whole in the scheduler, so a
    /// 500-statement script must not ship 500 entries. Every failed statement is kept — that is the
    /// one an operator opens the run to find — and the remaining budget goes to the slowest, which
    /// is what "why was this slow" asks. Original order is preserved in the result so the output
    /// still reads as a timeline rather than a leaderboard.</para>
    /// </summary>
    public static IReadOnlyList<StatementMetricsPayload> Cap(
        IEnumerable<StatementMetricsPayload> statements,
        int maxStatements = DefaultMaxStatements)
    {
        var ordered = statements as IList<StatementMetricsPayload> ?? statements.ToList();
        if (maxStatements <= 0) return [];
        if (ordered.Count <= maxStatements) return [.. ordered];

        // Reference identity: two statements can be measurement-identical and must not collapse.
        var keep = new HashSet<StatementMetricsPayload>(ReferenceEqualityComparer.Instance);

        foreach (var statement in ordered)
            if (statement.Failed) keep.Add(statement);

        // A run with more failures than the budget keeps the failures: dropping one would hide the
        // very thing being looked for, and an oversized envelope is the lesser problem.
        foreach (var statement in ordered.Where(s => !s.Failed).OrderByDescending(s => s.DurationMs))
        {
            if (keep.Count >= maxStatements) break;
            keep.Add(statement);
        }

        return [.. ordered.Where(keep.Contains)];
    }
}
