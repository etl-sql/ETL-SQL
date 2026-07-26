namespace ETL_SQL.Portal.Models;

public sealed record QuarantineQueueItemDto(
    string JobName,
    string? ScriptPath,
    string? SectionLabel,
    string SourceTable,
    string QuarantineTarget,
    bool IsReplayable,
    string? NonReplayableReason,
    IReadOnlyList<string> InputColumns,
    string InputSchemaFingerprint,
    DateTimeOffset UpdatedAtUtc,
    string ReplayMode,
    string? ProbeSourceTable,
    string? JoinBuildTable,
    bool? JoinObservedN1,
    string? JoinNonReplayableReason,
    string ReplayStatement);

public sealed record ReplayQuarantineRequest(
    string QuarantineTarget,
    string? JobName = null);

public sealed record ReplayQuarantineResponse(
    string JobId,
    string ReplayStatement);

public sealed record QuarantineDispositionRequest(
    string QuarantineTarget,
    IReadOnlyList<string> RowIds,
    string Disposition,
    string? JobName = null,
    IReadOnlyDictionary<string, string?>? Changes = null,
    /// <summary>
    /// Why the steward made this call. Recorded in the audit trail, not in the quarantine table —
    /// the capture schema is frozen on first write, and an audit row cannot be edited afterwards
    /// the way a note column could.
    /// </summary>
    string? Note = null);

public sealed record QuarantineDispositionResponse(
    string JobId,
    string DispositionStatement);

/// <summary>One completed run's data-quality outcome, for the steward trend view.</summary>
public sealed record DataQualityRunDto(
    long HistoryId,
    string JobName,
    DateTime StartTime,
    DateTime? EndTime,
    string Status,
    long RowsProcessed,
    long RowsQuarantined,
    long RowsWarned,
    /// <summary>Quarantined rows as a fraction of rows processed (0..1); null when nothing was processed.</summary>
    decimal? QuarantineRate,
    /// <summary>Warned rows as a fraction of rows processed (0..1); null when nothing was processed.</summary>
    decimal? WarnRate,
    IReadOnlyList<DataQualityRuleFailureDto> RuleFailures);

/// <summary>A per-rule failure count parsed from the run's compact history payload.</summary>
public sealed record DataQualityRuleFailureDto(string Column, string Rule, long Count);

/// <summary>
/// Quality trend for one job: the most recent runs plus the aggregate a steward triages on —
/// which rules fire most, and whether the rate is moving.
/// </summary>
public sealed record DataQualityTrendDto(
    string JobName,
    int RunCount,
    long TotalRowsProcessed,
    long TotalRowsQuarantined,
    long TotalRowsWarned,
    decimal? AverageQuarantineRate,
    decimal? LatestQuarantineRate,
    /// <summary>Latest rate minus the mean of the preceding runs; positive means quality is degrading.</summary>
    decimal? QuarantineRateDelta,
    IReadOnlyList<DataQualityRuleFailureDto> TopRuleFailures,
    IReadOnlyList<DataQualityRunDto> Runs);

public sealed record QuarantineRowsResponse(
    string QuarantineTarget,
    string Status,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    bool Capped);
