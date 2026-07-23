namespace ETL_SQL.Portal.Models;

public record CatalogSearchResultDto(
    string Type,
    int Id,
    string Name,
    string Path,
    int? FolderId,
    string? Description,
    string? Tags,
    string? Category,
    string? Owner,
    string? Certification,
    DateTime? SnapshotBuiltAt,
    DateTime? LastViewedAt,
    string? LastRefreshStatus,
    string? LastRefreshError,
    long? LastRefreshDurationMs,
    bool? HasSnapshot,
    bool? IsStale,
    bool? ScriptChanged,
    bool? IsFavorite,
    string? MatchReason = null,
    double? Score = null);

public record CatalogLineageHistoryDto(
    long Id,
    DateTime RunAt,
    string? JobName,
    string? ScriptPath,
    string TargetTable,
    string? TargetColumn,
    IReadOnlyList<string> SourceTables,
    string Operation,
    IReadOnlyDictionary<string, string> Tags,
    string? SourceFile,
    int Line,
    IReadOnlyList<string> SourceColumns,
    string? TransformationKind,
    string? TransformationExpression,
    IReadOnlyList<string>? FunctionsApplied,
    string? DerivedFromDescriptions,
    int? ReportId,
    string? ReportName,
    string? FolderPath);

public record DownstreamReportDto(
    int? ReportId,
    string? ReportName,
    string? FolderPath,
    int RunCount,
    DateTime LastSeen);

public record StewardshipSummaryDto(
    int TotalAssets,
    int SensitiveAssets,
    int MissingMetadataAssets,
    int StaleAssets,
    int StewardQueueAssets);

public record StewardshipFacetDto(
    string Value,
    int Count);

public record StewardshipCatalogDto(
    StewardshipSummaryDto Summary,
    IReadOnlyList<StewardshipFacetDto> Stewards,
    IReadOnlyList<StewardshipFacetDto> Domains,
    IReadOnlyList<StewardshipFacetDto> Classifications,
    IReadOnlyList<StewardshipFacetDto> Qualities,
    IReadOnlyList<StewardshipAssetDto> Items);

public record StewardshipAssetDto(
    string TargetTable,
    string? TargetColumn,
    DateTime RunAt,
    string? JobName,
    string? ScriptPath,
    IReadOnlyList<string> SourceTables,
    IReadOnlyDictionary<string, string> Tags,
    IReadOnlyList<string> MissingTags,
    bool IsSensitive,
    bool IsRestricted,
    bool IsStale,
    string StaleReason,
    string? Owner,
    string? Steward,
    string? Contact,
    string? Domain,
    string? Classification,
    string? Quality,
    string? Freshness);

public record LineageImpactRequestDto(
    string Kind,
    string Name,
    string? Column,
    string Direction,
    int Depth,
    int Limit);

public record LineageImpactSummaryDto(
    int Tables,
    int Columns,
    int Reports,
    int Datasets,
    int Subscriptions,
    int Jobs,
    int Stewards);

public record LineageImpactItemDto(
    string Type,
    string Name,
    string? Detail,
    DateTime? LastSeen,
    long? Count);

public record LineageImpactDto(
    LineageImpactRequestDto Request,
    LineageImpactSummaryDto Summary,
    IReadOnlyList<LineageImpactItemDto> Tables,
    IReadOnlyList<LineageImpactItemDto> Columns,
    IReadOnlyList<LineageImpactItemDto> Reports,
    IReadOnlyList<LineageImpactItemDto> Datasets,
    IReadOnlyList<LineageImpactItemDto> Subscriptions,
    IReadOnlyList<LineageImpactItemDto> Jobs,
    IReadOnlyList<LineageImpactItemDto> Stewards);

public record ConsumerHomeDto(
    IReadOnlyList<CatalogSearchResultDto> Favorites,
    IReadOnlyList<CatalogSearchResultDto> Recent,
    IReadOnlyList<CatalogSearchResultDto> Featured,
    IReadOnlyList<CatalogSearchResultDto> Popular);

public record ReportAccessInfoDto(
    int? ReportId,
    string? ReportName,
    string? FolderPath,
    string? Owner,
    string? Contact,
    string? Description,
    bool CanRequestAccess,
    string Status = "Restricted",
    int? ExistingRequestId = null,
    string? ExistingRequestStatus = null);

public record RequestReportAccessDto(
    string? Reason);

public record PendingAccessRequestDto(
    int Id,
    int ReportId,
    string ReportTitle,
    int RequesterUserId,
    string RequesterUserName,
    string? RequesterEmail,
    string? Reason,
    string Status,
    DateTime CreatedAt);

public record ApproveReportAccessRequestDto(
    ETL_SQL.Portal.Data.FolderPermission? Permission = ETL_SQL.Portal.Data.FolderPermission.Read,
    string? DecisionReason = null);

public record DenyReportAccessRequestDto(
    string? DecisionReason = null);
