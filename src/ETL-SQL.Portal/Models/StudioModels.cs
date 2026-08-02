namespace ETL_SQL.Portal.Models;

public sealed record StudioSessionDto(
    string Mode,
    IReadOnlyList<string> Capabilities,
    bool SourceControlEnabled);

public sealed record StudioReportDto(
    int Id,
    int FolderId,
    string FolderPath,
    string Name,
    string? Description,
    DateTime UpdatedAt,
    long Version);

public sealed record StudioFolderDto(int Id, string Path, string Name);

public sealed record CreateStudioReportRequest(
    int FolderId,
    string Name,
    string ScriptText,
    string? Description = null);
