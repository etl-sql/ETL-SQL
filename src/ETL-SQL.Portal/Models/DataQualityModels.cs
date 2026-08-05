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
    string ReplayStatement,
    /// <summary>
    /// Whether Portal can read this target's rows. False means the queue shows the target as
    /// view-only rather than offering a row editor that cannot work — see
    /// <see cref="ETL_SQL.Portal.Services.QuarantineTargetReadability"/>.
    /// </summary>
    bool RowsReadable,
    string? RowsUnavailableReason,
    /// <summary>The statement a steward can run themselves against a view-only target.</summary>
    string ReviewStatement);

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

/// <summary>
/// One rule's failure count for a run.
///
/// <para>Normally read from the structured per-rule rows the engine writes, which carry the target
/// table, the action the rule took, and the rule's owner. Runs that predate that capture have only
/// the compact <c>column:rule=count;…</c> history string, which cannot express those three — such a
/// row is marked <see cref="CountsOnly"/> so a blank owner reads as "not recorded by this run"
/// rather than as "nobody owns this rule".</para>
/// </summary>
public sealed record DataQualityRuleFailureDto(
    string Column,
    string Rule,
    long Count,
    string? TargetTable = null,
    string? Action = null,
    string? Owner = null,
    /// <summary>
    /// True when this row came from the legacy display string rather than structured capture, so
    /// <see cref="TargetTable"/>, <see cref="Action"/> and <see cref="Owner"/> are unavailable
    /// rather than empty. Counts-only rows are never merged with structured ones: two rules that
    /// differ only in a field one side cannot see are not the same rule.
    /// </summary>
    bool CountsOnly = false);

/// <summary>One rule protecting a script output column, including rules that have not failed.</summary>
public sealed record DataQualityRuleDefinitionDto(
    string TargetTable,
    string? TargetColumn,
    string RuleTag,
    string Rule,
    string Action,
    string? SourceFile,
    int Line,
    string? JobName = null);

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
