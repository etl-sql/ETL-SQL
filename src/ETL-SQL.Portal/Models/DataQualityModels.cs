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

/// <summary>
/// A durable record of one data-quality submission (a replay or a disposition), written when the
/// job is handed to the channel and updated when its outcome is observed.
///
/// <para>It exists because the outcome was otherwise known only to the browser that submitted it.
/// A steward who closed the tab never learned whether the replay landed, and a second steward
/// looking at the same quarantine target could not tell that one was already in flight — so the
/// obvious next step was to submit another replay of the same production load.</para>
/// </summary>
/// <param name="Status">
/// Channel status, or <c>Unknown</c> when the submission outlived the process that was running it.
/// Deliberately not <c>Failed</c>: an in-process channel keeps job state in memory and answers
/// "not found" after a restart, and reporting that as failure would tell a steward their replay
/// did not happen when it may well have completed.
/// </param>
public sealed record QuarantineSubmissionRecord(
    string JobId,
    string Kind,
    string JobName,
    string QuarantineTarget,
    DateTimeOffset SubmittedAtUtc,
    int? SubmittedByUserId,
    string Status,
    DateTimeOffset StatusUpdatedAtUtc,
    string? Disposition = null,
    int? RowCount = null,
    string? Error = null);

/// <summary>Status of one submitted data-quality job, as the steward's view polls it.</summary>
public sealed record QuarantineSubmissionStatusDto(
    string JobId,
    string Kind,
    string QuarantineTarget,
    string Status,
    bool IsTerminal,
    DateTimeOffset SubmittedAtUtc,
    DateTimeOffset StatusUpdatedAtUtc,
    string? Error = null,
    /// <summary>Why the status cannot be determined, when it cannot. Never invented detail.</summary>
    string? UnknownReason = null);

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
