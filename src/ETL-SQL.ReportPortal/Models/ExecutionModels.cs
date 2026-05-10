namespace ETL_SQL.ReportPortal.Models;

public record ExecuteRequest(Dictionary<string, string>? Parameters);

public record JobStatusResponse(
    string    JobId,
    string    Status,
    DateTime  CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    string?   ManifestPath,
    string?   Error);

public record RefreshResponse(string JobId, bool AlreadyRunning);

public record SnapshotResponse(
    int       ReportId,
    string    ManifestPath,
    DateTime  BuiltAt,
    bool      IsStale,
    object?   Manifest);

public record ParameterUpdateRequest(string Name, string Value, bool IsInteraction = false);

public record BatchParameterRequest(List<ParameterUpdateRequest> Params, bool IsInteraction = false);

public record DrillRequest(string VisualName, string Direction, string? ClickedValue, int TargetDepth = 0);
