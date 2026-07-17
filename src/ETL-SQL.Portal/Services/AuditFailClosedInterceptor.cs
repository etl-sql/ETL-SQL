using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Security.Claims;
using System.Text.Json;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Enforces the fail-closed mutation policy (P1.12) at the single choke point every
/// security-sensitive mutation passes through: the audit row is staged into the same EF unit of
/// work as the mutation (see <see cref="AuditService.Stage"/>), so blocking the save when a new
/// <see cref="AuditOutboxMessage"/> is being added blocks the mutation itself — it cannot be
/// bypassed by skipping a controller-level check.
///
/// No-op unless <see cref="AuditConfig.RequireRemoteDelivery"/> is set, so the local zero-trust
/// default (best-effort forwarding) is unchanged.
/// </summary>
public sealed class AuditFailClosedInterceptor(
    PortalConfig config,
    TimeProvider clock,
    IHttpContextAccessor? httpContext = null) : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions PayloadJsonOptions = new(JsonSerializerDefaults.Web);

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (TryGetAuditedContext(eventData, out var db))
            AuditDeliveryGate.EnsureDeliverable(db, config.Audit, clock);
        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (TryGetAuditedContext(eventData, out var db))
            await AuditDeliveryGate.EnsureDeliverableAsync(db, config.Audit, clock, cancellationToken);
        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <summary>Returns true only when fail-closed delivery is required and this save is staging a
    /// new audit event — the single signal that a security-sensitive mutation is committing.</summary>
    private bool TryGetAuditedContext(DbContextEventData eventData, out PortalDbContext db)
    {
        db = null!;
        var required = config.Audit.ResolveRequireRemoteDelivery(
            ETL_SQL.Core.Governance.EnterprisePolicyRuntime.Current.IsEnrolled);
        if (!required || eventData.Context is not PortalDbContext context)
            return false;

        var stagingAudit = context.ChangeTracker
            .Entries<AuditOutboxMessage>()
            .Any(e => e.State == EntityState.Added);
        if (!stagingAudit)
        {
            var mutatedTypes = context.ChangeTracker.Entries()
                .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .Where(e => e.Entity is not AuditLog and not AuditOutboxMessage)
                .Select(e => e.Metadata.ClrType.Name)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            if (mutatedTypes.Length == 0)
                return false;
            StageFallbackAudit(context, mutatedTypes);
        }

        db = context;
        return true;
    }

    private void StageFallbackAudit(PortalDbContext db, IReadOnlyList<string> mutatedTypes)
    {
        var occurredAt = clock.GetUtcNow().UtcDateTime;
        var request = httpContext?.HttpContext;
        var userId = int.TryParse(request?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedUserId)
            ? parsedUserId
            : (int?)null;
        var actorType = request?.User.FindFirstValue(TokenService.IdentityTypeClaim)
            == TokenService.ServiceIdentityType ? "ServiceAccount" : "User";
        var actorId = actorType == "ServiceAccount"
            ? request?.User.FindFirstValue(TokenService.ServiceAccountIdClaim)
            : userId?.ToString();
        var resourceType = string.Join(',', mutatedTypes);
        const string action = "PORTAL_MUTATION";
        const string detail = "A mutation reached the persistence boundary without an explicit audit event.";
        var correlationId = request?.TraceIdentifier;

        var auditLog = new AuditLog
        {
            UserId = userId,
            ActorType = actorType,
            ActorId = actorId,
            Action = action,
            ResourceType = resourceType,
            Timestamp = occurredAt,
            Detail = detail,
            CorrelationId = correlationId
        };
        db.AuditLogs.Add(auditLog);
        db.AuditOutboxMessages.Add(new AuditOutboxMessage
        {
            AuditLog = auditLog,
            UserId = userId,
            ActorType = actorType,
            ActorId = actorId,
            Action = action,
            ResourceType = resourceType,
            CorrelationId = correlationId,
            OccurredAt = occurredAt,
            PayloadJson = JsonSerializer.Serialize(new
            {
                userId,
                actorType,
                actorId,
                action,
                resourceType,
                detail,
                correlationId,
                occurredAt
            }, PayloadJsonOptions),
            Status = "Pending",
            CreatedAt = occurredAt,
            UpdatedAt = occurredAt
        });
    }
}
