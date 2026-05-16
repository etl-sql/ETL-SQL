namespace ETL_SQL.ReportPortal.Models;

public record UploadScriptRequest(string Filename, string ContentBase64);

public record UploadScriptResponse(string Path);

public record PublishReportRequest(
    int FolderId,
    string Name,
    string ScriptPath,
    string? Description,
    string? Owner = null,
    string? Contact = null,
    string? Tags = null,
    string? Category = null,
    string? Domain = null,
    string? Steward = null,
    string? Certification = null);

public record UpdateReportRequest(
    string? Name,
    string? Description,
    int?    FolderId,
    string? ScriptPath,
    string? Owner = null,
    string? Contact = null,
    string? Tags = null,
    string? Category = null,
    string? Domain = null,
    string? Steward = null,
    string? Certification = null);

public record ReportDto(
    int Id,
    int FolderId,
    string FolderPath,
    string Name,
    string? Description,
    string? Owner,
    string? Contact,
    string? Tags,
    string? Category,
    string? Domain,
    string? Steward,
    string? Certification,
    IReadOnlyDictionary<string, string> Metadata,
    string ScriptPath,
    DateTime ScriptLastModified,
    bool HasSnapshot,
    DateTime? SnapshotBuiltAt,
    DateTime? LastViewedAt,
    DateTime? LastRefreshStartedAt,
    DateTime? LastRefreshCompletedAt,
    string? LastRefreshStatus,
    string? LastRefreshError,
    long? LastRefreshDurationMs,
    bool IsStale,
    bool ScriptChanged);
