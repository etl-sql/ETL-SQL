using System.Text.Json.Serialization;

namespace ETL_SQL.Portal.Models;

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
    string? Certification = null,
    string? CreatedByUsername = null,
    /// <summary><c>Pipeline</c> publishes an <c>.etlsql</c> catalog pipeline; absent means a report.</summary>
    string? Kind = null);

public record UpdateReportRequest(
    string? Name,
    string? Description,
    int? FolderId,
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
    IReadOnlyList<string> Errors,
    ReportValidationImpactDto? Impact = null);

public record ReportValidationImpactDto(
    IReadOnlyList<ReportValidationImpactSourceDto> Sources,
    int ReportCount,
    int DatasetCount,
    int SubscriptionCount,
    int JobCount);

public record ReportValidationImpactSourceDto(
    string Source,
    LineageImpactSummaryDto Summary);

public record CreateReportShareLinkRequest(string? Name, DateTime? ExpiresAt);

public record ReportShareLinkDto(
    int Id,
    int ReportId,
    string ReportName,
    string FolderPath,
    string Name,
    string? Token,
    string? Url,
    int CreatedBy,
    DateTime CreatedAt,
    DateTime? ExpiresAt,
    DateTime? RevokedAt);

public record ReportShareResolutionDto(
    int ReportId,
    string ReportName,
    string FolderPath,
    string Url,
    DateTime? ExpiresAt);

public record CreateReportEmbedTokenRequest(string? Name, DateTime? ExpiresAt);

public record ReportEmbedTokenDto(
    int Id,
    int ReportId,
    string ReportName,
    string Name,
    string? Token,
    string? Url,
    int CreatedBy,
    DateTime CreatedAt,
    DateTime? ExpiresAt,
    DateTime? RevokedAt);

public record AnonymousReportAccessDto(
    string Type,
    int Id,
    int ReportId,
    string ReportName,
    string FolderPath,
    string? Name,
    int CreatedBy,
    string? CreatorUsername,
    bool CreatorIsActive,
    DateTime CreatedAt,
    DateTime? ExpiresAt,
    DateTime? RevokedAt,
    string Status);

// COMPAT_BREAK: 0.22 — saved views accept only the shared resolved-state envelope.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record CreateSavedReportViewRequest(
    string Name,
    string? StateJson = null,
    string? ScriptHash = null,
    bool IsDefault = false);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record UpdateSavedReportViewRequest(
    string? Name,
    bool? IsDefault,
    string? StateJson = null,
    string? ScriptHash = null);

/// <summary>
/// Body for saving the caller's default view as the shared resolved-state envelope.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record SaveDefaultReportViewRequest(
    ETL_SQL.Core.Reporting.ResolvedReportState? State = null);

public record SavedReportViewDto(
    int Id,
    int ReportId,
    string Name,
    string? StateJson,
    string? ScriptHash,
    bool IsDefault,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    ETL_SQL.Core.Reporting.ResolvedReportState? State = null,
    string? DriftWarning = null);

// COMPAT_BREAK: 0.22 — retired inline delivery fields are rejected, including explicit nulls.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record CreateReportAlertRequest(
    string Name,
    string VisualName,
    string Operator,
    decimal Threshold,
    string? DisplayName,
    string? Description,
    Dictionary<string, string>? Options);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record UpdateReportAlertRequest(
    string? Name,
    string? VisualName,
    string? Operator,
    decimal? Threshold,
    string? DisplayName,
    string? Description,
    Dictionary<string, string>? Options,
    bool? IsActive);

public record AlterAlertNotificationRequest(string OrchestratorAlias, string NotificationName);

public record AlertNotificationDto(
    int Id,
    string OrchestratorAlias,
    string NotificationName,
    DateTime CreatedAt);

public record ReportAlertDto(
    int Id,
    int ReportId,
    string Name,
    string? DisplayName,
    string? Description,
    Dictionary<string, string>? Options,
    string VisualName,
    string Operator,
    decimal Threshold,
    bool IsActive,
    IReadOnlyList<AlertNotificationDto> Notifications,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string? LastState,
    DateTime? LastEvaluatedAt,
    DateTime? LastCheckedAt,
    DateTime? LastTriggeredAt,
    DateTime? LastNotifiedAt);

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
    bool ScriptChanged,
    long Version = 1,
    // The report's rows depend on who is looking: it is viewed live and has no snapshot. Set on
    // the single-report read only; lists would have to read every script to answer.
    bool PerViewer = false);

public record ReportDependencyDto(
    ReportDependencyReportDto Report,
    ReportDependencySnapshotDto? Snapshot,
    IReadOnlyList<ReportDependencyManifestDatasetDto> ManifestDatasets,
    IReadOnlyList<ReportDependencyDatasetDto> RegisteredDatasets,
    IReadOnlyList<ReportDependencyRefreshJobDto> RefreshJobs,
    IReadOnlyList<ReportDependencyAlertDto> Alerts,
    IReadOnlyList<ReportDependencySourceDto> Sources,
    IReadOnlyList<ReportDependencyLineageDto> LineageEntries);

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
    string? OrchestratorAlias,
    string RefreshInterval,
    DateTime? LastRefreshedAt);

public record ReportDependencyAlertDto(
    int Id,
    string Name,
    string VisualName,
    string Operator,
    decimal Threshold,
    bool IsActive,
    string? LastState,
    DateTime? LastEvaluatedAt,
    IReadOnlyList<ReportDependencyAlertNotificationDto> Notifications);

public record ReportDependencyAlertNotificationDto(
    string OrchestratorAlias,
    string NotificationName);

public record ReportDependencySourceDto(
    string Name,
    string? Connection,
    string? ObjectName,
    string Kind);

public record ReportDependencyLineageDto(
    string Target,
    string? TargetColumn,
    string Operation,
    IReadOnlyList<string> Sources,
    IReadOnlyList<string> SourceColumns,
    IReadOnlyDictionary<string, string> Tags,
    int Line,
    string? TransformationKind,
    string? TransformationExpression,
    IReadOnlyList<string>? FunctionsApplied,
    string? DerivedFromDescriptions);

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
