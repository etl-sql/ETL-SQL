using ETL_SQL.Core.Data;

namespace ETL_SQL.ReportPortal.Models;

public record DatasetDto(
    int      Id,
    string   Name,
    string   FolderPath,
    string   AccessLevel,
    long     RowCount,
    bool     IsStale,
    DateTime? LastRefresh,
    string?  Ttl,
    string?  RefreshInterval,
    bool     IsEncrypted,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string?  OwningReportName,
    int?     OwningReportId);

public record DatasetColumnDto(string Name, string Type);

public record DatasetPreviewDto(
    IEnumerable<DatasetColumnDto> Columns,
    long                          RowCount);

public record UpdateDatasetRequest(string? AccessLevel, string? Ttl);

public record DatasetAclEntryDto(int GroupId, string GroupName, string Permission);

public record GrantDatasetPermissionRequest(int GroupId, string Permission);

public record DatasetRefreshStatusDto(
    string    Status,       // Idle | InProgress
    string?   JobId,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    string?   Error,
    DateTime? LastRefresh,
    bool      IsStale);

// ── Dataset Viewer ─────────────────────────────────────────────────────────────

// Op values: contains | starts_with | eq | neq | gt | lt | gte | lte | between | in | is_null | not_null
public record DatasetColumnFilterDto(string Col, string Op, string? Val, string? Val2);

public record DatasetRowsDto(
    IEnumerable<DatasetColumnDto>            Columns,
    IEnumerable<Dictionary<string, object?>> Rows,
    long TotalCount,
    long FilteredCount,
    int  Page,
    int  PageSize);

public record DatasetColumnStatsDto(
    string  Name,
    long    NullCount,
    object? Min,
    object? Max,
    double? Avg);

public record DatasetColumnValuesDto(IEnumerable<object?> Values, long TotalDistinct);
