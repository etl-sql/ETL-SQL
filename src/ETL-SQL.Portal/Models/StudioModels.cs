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

public sealed record CreateStudioReportRequest(
    int FolderId,
    string Name,
    string ScriptText,
    string? Description = null,
    string? Kind = null);
