namespace ETL_SQL.Portal.Models;

public sealed record StudioSessionDto(
    string Mode,
    IReadOnlyList<string> Capabilities,
    bool SourceControlEnabled,
    bool DraftRecovery,
    bool PrivateDrafts = false);

/// <summary>One of the caller's own private drafts, without its text.</summary>
public sealed record StudioPrivateDraftSummaryDto(
    int Id,
    string Name,
    string Kind,
    long Version,
    DateTime UpdatedAt,
    DateTime ExpiresAt);

/// <summary>A private draft and its saved text. <see cref="Version"/> is what the next save must name.</summary>
public sealed record StudioPrivateDraftDto(
    int Id,
    string Name,
    string Kind,
    long Version,
    string Content,
    DateTime UpdatedAt,
    DateTime ExpiresAt);

public sealed record CreateStudioPrivateDraftRequest(string? Name, string? Content, string? Kind = null);

/// <param name="BaseVersion">The saved version the edits started from; a stale one is refused.</param>
public sealed record SaveStudioPrivateDraftRequest(string? Content, long BaseVersion, string? Name = null);

/// <summary>Publishes a private draft as a catalog document in <see cref="FolderId"/>.</summary>
/// <param name="Version">The saved version the author is looking at; a stale one is refused.</param>
/// <param name="Name">The catalog name; the draft's own name when omitted.</param>
public sealed record PublishStudioPrivateDraftRequest(int FolderId, long Version, string? Name = null, string? Description = null);

/// <summary>One kept save of a private draft.</summary>
public sealed record StudioPrivateDraftRevisionDto(long Version, DateTime SavedAt, string ContentHash, string? Content = null);

/// <summary>A refusal the author can act on. <see cref="Code"/> is stable.</summary>
public sealed record StudioPrivateDraftRefusalDto(string Code, string Error, long? CurrentVersion = null);

/// <summary>An author's unsaved edits to one report, and the version they started from.</summary>
public sealed record StudioRecoveryDraftDto(
    string Content,
    long? BaseVersion,
    string? BaseSourceRevision,
    DateTime UpdatedAt);

public sealed record SaveStudioRecoveryDraftRequest(
    string? Content,
    long? BaseVersion,
    string? BaseSourceRevision);

public sealed record StudioReportDto(
    int Id,
    int FolderId,
    string FolderPath,
    string Name,
    string? Description,
    DateTime UpdatedAt,
    long Version,
    string Kind = "Report");

public sealed record StudioFolderDto(int Id, string Path, string Name);

/// <summary>Asks the Orchestrator to run one saved pipeline revision.</summary>
/// <param name="Version">The saved version the author is looking at; a stale one is refused.</param>
public sealed record SubmitPipelineRunRequest(long Version);

/// <summary>A run the Orchestrator accepted. <see cref="RunId"/> is the only handle to follow it by.</summary>
public sealed record PipelineRunSubmittedDto(
    long RunId,
    int PipelineId,
    long Version,
    string ScriptHash,
    string JobName);

/// <summary>
/// A refusal the author can act on. <see cref="Code"/> is stable and names whose decision it was:
/// the Portal's (document), the Orchestrator's (job grant), or no Orchestrator at all.
/// </summary>
public sealed record PipelineRunRefusalDto(string Code, string Error, long? CurrentVersion = null);

/// <summary>
/// The Orchestrator accepted a request to stop a run. This is not the outcome: the run reports
/// <c>CANCELLED</c> when it has stopped, or another status if it finished first.
/// </summary>
public sealed record PipelineRunCancelRequestedDto(long RunId, int PipelineId);

/// <summary>Asks the Orchestrator to run one saved pipeline revision on a cron schedule.</summary>
/// <param name="Version">The saved version to pin; a stale one is refused.</param>
public sealed record SchedulePipelineRequest(long Version, string Cron, string? TimeZone = null);

/// <summary>
/// A pipeline's schedule in the Orchestrator. The schedule runs the revision that was pinned when it
/// was set, not the latest save: <see cref="ScheduledRevisionIsSaved"/> is false once they differ.
/// </summary>
public sealed record PipelineScheduleDto(
    int PipelineId,
    bool Scheduled,
    string? Cron,
    string? TimeZone,
    bool Enabled,
    DateTime? NextRun,
    DateTime? LastRun,
    string? ScheduledScriptHash,
    bool ScheduledRevisionIsSaved,
    long SavedVersion);

public sealed record PipelineRunStatementDto(string Statement, long DurationMs, long RowsProcessed, bool Failed);

/// <summary>One run's status and bounded statement log, as the Orchestrator recorded it.</summary>
public sealed record PipelineRunDto(
    long RunId,
    int PipelineId,
    string Status,
    bool IsTerminal,
    DateTime StartTime,
    DateTime? EndTime,
    string? ErrorMessage,
    long RowsProcessed,
    string? ScriptHash,
    bool LogTruncated,
    IReadOnlyList<PipelineRunStatementDto> Statements);

public sealed record CreateStudioReportRequest(
    int FolderId,
    string Name,
    string ScriptText,
    string? Description = null,
    string? Kind = null);
