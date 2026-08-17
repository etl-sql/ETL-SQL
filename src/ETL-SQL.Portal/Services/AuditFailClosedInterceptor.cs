using System.Security.Claims;
using System.Text.Json;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

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
        if (TryGetAuditedContext(eventData, out var db, out var tenantId))
            AuditDeliveryGate.EnsureDeliverable(db, config.Audit, clock, tenantId);
        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (TryGetAuditedContext(eventData, out var db, out var tenantId))
            await AuditDeliveryGate.EnsureDeliverableAsync(
                db, config.Audit, clock, tenantId, cancellationToken);
        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <summary>Returns true only when fail-closed delivery is required and this save is staging a
    /// new audit event — the single signal that a security-sensitive mutation is committing.</summary>
    private bool TryGetAuditedContext(
        DbContextEventData eventData,
        out PortalDbContext db,
        out string tenantId)
    {
        db = null!;
        tenantId = string.Empty;
        var required = config.Audit.ResolveRequireRemoteDelivery(
            ETL_SQL.Core.Governance.EnterprisePolicyRuntime.Current.IsEnrolled);
        if (!required || eventData.Context is not PortalDbContext context)
            return false;

        var stagedOutbox = context.ChangeTracker
            .Entries<AuditOutboxMessage>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity)
            .ToArray();
        if (stagedOutbox.Length > 0)
        {
            var auditTenants = stagedOutbox
                .Select(e => e.TenantId)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (auditTenants.Length != 1 || string.IsNullOrWhiteSpace(auditTenants[0]))
                throw new InvalidOperationException(
                    "A single audited persistence operation must target exactly one tenant partition.");
            if (stagedOutbox.Any(e => e.AuditLog is not null
                && !string.Equals(e.AuditLog.TenantId, auditTenants[0], StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    "An audit outbox event cannot reference an audit row from another tenant partition.");
            }
            tenantId = auditTenants[0];
        }
        else
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
            var tenantIds = context.ChangeTracker.Entries()
                .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .Select(e => e.Entity.GetType().GetProperty("TenantId")?.GetValue(e.Entity) as string)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (tenantIds.Length > 1)
                throw new InvalidOperationException(
                    "A single audited persistence operation cannot mutate multiple tenant partitions.");
            var requestTenant = httpContext?.HttpContext?.RequestServices
                .GetService<TenantContext>()?.Tenant.Value;
            tenantId = tenantIds.SingleOrDefault()
                ?? requestTenant
                ?? (string.IsNullOrWhiteSpace(config.TenantId) ? "portal-host" : config.TenantId);
            StageFallbackAudit(context, mutatedTypes, tenantId);
        }

        db = context;
        return true;
    }

    private void StageFallbackAudit(
        PortalDbContext db, IReadOnlyList<string> mutatedTypes, string tenantId)
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
            TenantId = tenantId,
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
            TenantId = tenantId,
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
                tenantId,
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
