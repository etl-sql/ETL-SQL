using System.Security.Claims;
using System.Text.Json;
using ETL_SQL.Core.Common;
using ETL_SQL.ReportPortal.Data;

namespace ETL_SQL.ReportPortal.Services;

public class AuditService(PortalDbContext db, IHttpContextAccessor httpContext)
{
    private static readonly JsonSerializerOptions PayloadJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Stages an audit row in the operation's own unit of work WITHOUT saving, so it commits —
    /// or fails — atomically with the mutation it records. Security-sensitive mutations must
    /// call this before their final <c>SaveChangesAsync</c> (or Identity operation, which saves
    /// through the same scoped context): the operation then cannot succeed without its durable
    /// audit event, and a failed/conflicted save discards the staged row with the mutation.
    /// </summary>
    public void Stage(int? userId, string action,
        string? resourceType = null, string? resourceId = null, string? detail = null,
        string? correlationId = null, string? actorType = null, string? actorId = null,
        string? effectiveScopes = null)
    {
        var occurredAt = DateTime.UtcNow;
        var safeResourceId = SecretRedactor.Redact(resourceId);
        var safeDetail = SecretRedactor.Redact(detail);
        var effectiveCorrelationId = correlationId ?? httpContext.HttpContext?.TraceIdentifier;
        var principal = httpContext.HttpContext?.User;
        var effectiveActorType = actorType ??
            (principal?.FindFirstValue(TokenService.IdentityTypeClaim) == TokenService.ServiceIdentityType
                ? "ServiceAccount" : "User");
        var effectiveActorId = actorId ??
            (effectiveActorType == "ServiceAccount"
                ? principal?.FindFirstValue(TokenService.ServiceAccountIdClaim)
                : userId?.ToString());
        var resolvedEffectiveScopes = effectiveScopes ?? (effectiveActorType == "ServiceAccount"
            ? string.Join(' ', (principal?.FindAll(TokenService.ScopeClaim) ?? [])
                .Select(claim => claim.Value)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(scope => scope, StringComparer.Ordinal))
            : null);

        var auditLog = new AuditLog
        {
            UserId = userId,
            ActorType = effectiveActorType,
            ActorId = effectiveActorId,
            EffectiveScopes = resolvedEffectiveScopes,
            Action = action,
            ResourceType = resourceType,
            ResourceId = safeResourceId,
            Timestamp = occurredAt,
            Detail = safeDetail,
            CorrelationId = effectiveCorrelationId
        };

        db.AuditLogs.Add(auditLog);
        db.AuditOutboxMessages.Add(new AuditOutboxMessage
        {
            AuditLog = auditLog,
            UserId = userId,
            ActorType = effectiveActorType,
            ActorId = effectiveActorId,
            EffectiveScopes = resolvedEffectiveScopes,
            Action = action,
            ResourceType = resourceType,
            ResourceId = safeResourceId,
            CorrelationId = effectiveCorrelationId,
            OccurredAt = occurredAt,
            PayloadJson = JsonSerializer.Serialize(new
            {
                userId,
                actorType = effectiveActorType,
                actorId = effectiveActorId,
                effectiveScopes = resolvedEffectiveScopes,
                action,
                resourceType,
                resourceId = safeResourceId,
                detail = safeDetail,
                correlationId = effectiveCorrelationId,
                occurredAt
            }, PayloadJsonOptions),
            Status = "Pending",
            CreatedAt = occurredAt,
            UpdatedAt = occurredAt
        });
    }

    /// <summary>
    /// Stages and immediately saves an audit row (its own commit). Appropriate for events that
    /// are not the durable record of a mutation: views, exports, logins, denials, and
    /// best-effort background bookkeeping. For security-sensitive mutations use
    /// <see cref="Stage"/> so the audit row shares the operation's commit.
    /// </summary>
    public async Task LogAsync(int? userId, string action,
        string? resourceType = null, string? resourceId = null, string? detail = null,
        string? correlationId = null, string? actorType = null, string? actorId = null,
        string? effectiveScopes = null)
    {
        Stage(userId, action, resourceType, resourceId, detail, correlationId, actorType, actorId,
            effectiveScopes);
        await db.SaveChangesAsync();
    }
}
