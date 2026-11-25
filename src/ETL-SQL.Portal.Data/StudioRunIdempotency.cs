namespace ETL_SQL.Portal.Data;

/// <summary>
/// Durable idempotency ledger for <c>POST /api/studio/pipelines/{id}/runs</c> and
/// <c>GET /api/studio/pipelines/{id}/runs/by-request/{key}</c> (ST55 §3.4 / ST56 Slice 2).
/// Scoped to (TenantId, UserId, PipelineId, IdempotencyKey) with a 24-hour recovery window.
/// </summary>
public sealed class StudioRunIdempotency
{
    public long Id { get; set; }
    public string TenantId { get; set; } = "portal-host";
    public int UserId { get; set; }
    public int PipelineId { get; set; }
    public string IdempotencyKey { get; set; } = "";
    public long RequestedVersion { get; set; }
    public string ScriptHash { get; set; } = "";
    public string JobName { get; set; } = "";
    public string State { get; set; } = "Pending";
    public long? RunId { get; set; }
    public string OwnerInstanceId { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
