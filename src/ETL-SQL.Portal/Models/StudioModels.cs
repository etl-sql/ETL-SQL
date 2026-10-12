namespace ETL_SQL.Portal.Models;

public sealed record StudioSessionDto(
    string Mode,
    IReadOnlyList<string> Capabilities,
    bool SourceControlEnabled,
    bool DraftRecovery);

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
