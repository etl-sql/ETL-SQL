namespace ETL_SQL.ReportPortal.Models;

public record PublishReportRequest(
    int FolderId,
    string Name,
    string ScriptPath,
    string? Description);

public record UpdateReportRequest(
    string? Name,
    string? Description,
    int? FolderId);

public record ReportDto(
    int Id,
    int FolderId,
    string FolderPath,
    string Name,
    string? Description,
    string ScriptPath,
    DateTime ScriptLastModified,
    bool HasSnapshot,
    DateTime? SnapshotBuiltAt,
    bool IsStale,
    bool ScriptChanged);
