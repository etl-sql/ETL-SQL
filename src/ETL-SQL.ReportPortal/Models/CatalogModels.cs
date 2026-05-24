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
    int? ReportId,
    string? ReportName,
    string? FolderPath);
