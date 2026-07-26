namespace ETL_SQL.Core.Quality;

/// <summary>
/// The <c>__dq_*</c> columns appended to every captured quarantine/warn row. The schema is frozen
/// on a target's first write, so every column the v2 remediation workflow needs must exist from
/// v1 — including <see cref="OriginRowId"/>, which v1 always writes as NULL (design decision 11).
/// </summary>
public static class DataQualityColumns
{
    /// <summary>The rule text that failed, e.g. "MATCHES ^[^@]+@[^@]+$".</summary>
    public const string Rule = "__dq_rule";
    /// <summary>The output column the failing rule was declared on.</summary>
    public const string Column = "__dq_column";
    /// <summary>The projected value that failed (masked when the column is <c>@pii</c>-tagged).</summary>
    public const string Value = "__dq_value";
    /// <summary>Human-readable failure reason.</summary>
    public const string Reason = "__dq_reason";
    /// <summary>UTC capture timestamp.</summary>
    public const string Timestamp = "__dq_ts";
    /// <summary>Identifier of the run that captured the row.</summary>
    public const string RunId = "__dq_run_id";
    /// <summary>Stable job or script identity used to scope retention on shared capture targets.</summary>
    public const string CaptureScope = "__dq_capture_scope";
    /// <summary>Disposition: 'quarantined' (v2 lifecycle) or 'warned' (immutable).</summary>
    public const string Status = "__dq_status";
    /// <summary>Deterministic hash of the captured row content + run id — the replay identity key.</summary>
    public const string RowId = "__dq_row_id";
    /// <summary>
    /// Reserved for v2 replay linkage; always NULL in v1. Present from day one because the target
    /// schema is frozen on first write — adding it later would break v1-created tables.
    /// </summary>
    public const string OriginRowId = "__dq_origin_row_id";
    /// <summary>Warn tables only: always 1, confirming the row still reached the main target.</summary>
    public const string TargetWritten = "__dq_target_written";

    public const string QuarantinedStatus = "quarantined";
    public const string WarnedStatus = "warned";
    public const string ReleasedStatus = "released";
    public const string ReplayingStatus = "replaying";
    public const string ReplayedStatus = "replayed";
    public const string DiscardedStatus = "discarded";

    /// <summary>True when <paramref name="name"/> is one of the engine-owned DQ columns.</summary>
    public static bool IsDataQualityColumn(string name) =>
        name.StartsWith("__dq_", System.StringComparison.OrdinalIgnoreCase);
}
