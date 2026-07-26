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
    string ReplayStatement);

public sealed record ReplayQuarantineRequest(
    string QuarantineTarget,
    string? JobName = null);

public sealed record ReplayQuarantineResponse(
    string JobId,
    string ReplayStatement);
