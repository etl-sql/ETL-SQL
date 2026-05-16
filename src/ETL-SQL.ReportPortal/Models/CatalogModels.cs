namespace ETL_SQL.ReportPortal.Models;

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
    long? LastRefreshDurationMs);
