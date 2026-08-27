namespace ETL_SQL.Portal.Models;

public record ExecuteRequest(Dictionary<string, string>? Parameters);

public record JobStatusResponse(
    string JobId,
    string Status,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    string? ManifestPath,
    string? Error,
    long RowsProcessed,
    long PeakMemoryBytes,
    double CpuTimeSeconds);

public record RefreshResponse(string JobId, bool AlreadyRunning);

public record SnapshotResponse(
    int ReportId,
    string ManifestPath,
    DateTime BuiltAt,
    bool IsStale,
    object? Manifest);

public record ParameterUpdateRequest(string Name, string Value, bool IsInteraction = false);

public record BatchParameterRequest(List<ParameterUpdateRequest> Params, bool IsInteraction = false, string? PageName = null);

public record DrillRequest(string VisualName, string Direction, string? ClickedValue, int TargetDepth = 0);

public record RefreshVisualsRequest(List<string> Visuals);

public record BookmarkApplyRequest(string BookmarkName);

public record NativeChartLayoutRequest(string VisualName, string Tier);
