namespace ETL_SQL.Portal.Models;

public sealed record QuarantineQueueItemDto(
    string JobName,
    string? ScriptPath,
    string? SectionLabel,
    string SourceTable,
    string QuarantineTarget,
    bool IsReplayable,
    string? NonReplayableReason,
    IReadOnlyList<string> InputColumns,
    string InputSchemaFingerprint,
    DateTimeOffset UpdatedAtUtc,
    string ReplayMode,
    string? ProbeSourceTable,
    string? JoinBuildTable,
    bool? JoinObservedN1,
    string? JoinNonReplayableReason,
    string ReplayStatement);

public sealed record ReplayQuarantineRequest(
    string QuarantineTarget,
    string? JobName = null);

public sealed record ReplayQuarantineResponse(
    string JobId,
    string ReplayStatement);

public sealed record QuarantineDispositionRequest(
    string QuarantineTarget,
    IReadOnlyList<string> RowIds,
    string Disposition,
    string? JobName = null,
    IReadOnlyDictionary<string, string?>? Changes = null);

public sealed record QuarantineDispositionResponse(
    string JobId,
    string DispositionStatement);

public sealed record QuarantineRowsResponse(
    string QuarantineTarget,
    string Status,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    bool Capped);
