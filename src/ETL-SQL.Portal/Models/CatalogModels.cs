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
    bool? IsFavorite);

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
