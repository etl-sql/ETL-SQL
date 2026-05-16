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

public record ValidateReportScriptRequest(string ScriptPath);

public record ReportScriptValidationDto(
    bool IsValid,
    string ScriptPath,
    string? Hash,
    DateTime? LastModified,
    IReadOnlyDictionary<string, string> Metadata,
    IReadOnlyList<ReportParameterDto> Parameters,
    IReadOnlyList<string> Errors);

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
    bool IsFavorite,
    bool IsStale,
    bool ScriptChanged);

public record ReportDependencyDto(
    ReportDependencyReportDto Report,
    ReportDependencySnapshotDto? Snapshot,
    IReadOnlyList<ReportDependencyManifestDatasetDto> ManifestDatasets,
    IReadOnlyList<ReportDependencyDatasetDto> RegisteredDatasets,
    IReadOnlyList<ReportDependencyRefreshJobDto> RefreshJobs,
    IReadOnlyList<ReportDependencySourceDto> Sources);

public record ReportDependencyReportDto(
    int Id,
    string Name,
    string FolderPath,
    string ScriptPath);

public record ReportDependencySnapshotDto(
    int Id,
    string ManifestPath,
    DateTime BuiltAt);

public record ReportDependencyManifestDatasetDto(
    string TempTableName,
    string? RefreshInterval,
    string? Ttl,
    DateTime? LastRefresh,
    long RowCount);

public record ReportDependencyDatasetDto(
    int Id,
    string Name,
    string FolderPath,
    string AccessLevel,
    long RowCount,
    DateTime? LastRefresh,
    string? RefreshInterval,
    IReadOnlyList<ReportDependencySourceDto> Sources);

public record ReportDependencyRefreshJobDto(
    int Id,
    string OrchestratorJobName,
    string RefreshInterval,
    DateTime? LastRefreshedAt);

public record ReportDependencySourceDto(
    string Name,
    string? Connection,
    string? ObjectName,
    string Kind);

public record ReportHistoryDto(
    ReportDependencyReportDto Report,
    string? PublishedScriptHash,
    string? CurrentScriptHash,
    bool ScriptChanged,
    IReadOnlyList<ReportHistorySnapshotDto> Snapshots,
    IReadOnlyList<ReportHistoryChangeDto> Changes);

public record ReportHistorySnapshotDto(
    int Id,
    DateTime BuiltAt,
    int BuiltBy,
    string ManifestPath,
    string? ScriptHashAtRunTime,
    bool? HashMatched,
    string? ParametersJson);

public record ReportHistoryChangeDto(
    int Id,
    string Action,
    DateTime Timestamp,
    int? UserId,
    string? Detail);
