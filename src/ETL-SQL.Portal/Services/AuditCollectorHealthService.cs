using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Models;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// The operator's view of durable remote audit delivery: what is queued, how old it is, whether
/// anything has terminally failed, and — the question that matters during an incident — whether the
/// fail-closed policy is about to start refusing mutations.
///
/// Those signals were already emitted through health, Prometheus, and fleet status, which is fine
/// for a dashboard and no use to someone deciding whether to raise the backlog threshold or fix the
/// collector. Fail-closed state is evaluated by calling <see cref="AuditDeliveryGate"/> itself
/// rather than re-deriving the thresholds, so what this reports is exactly what would happen to the
/// next mutation.
/// </summary>
public sealed class AuditCollectorHealthService(
    PortalDbContext db,
    PortalConfig config,
    TimeProvider clock)
{
    public async Task<AuditCollectorHealthDto> BuildAsync(CancellationToken ct = default)
    {
        var audit = config.Audit;
        var pending = db.AuditOutboxMessages.Where(message => message.Status == "Pending");

        var pendingCount = await pending.CountAsync(ct);
        var oldestPending = await pending
            .OrderBy(message => message.OccurredAt)
            .Select(message => (DateTime?)message.OccurredAt)
            .FirstOrDefaultAsync(ct);

        var lastAttempt = await db.AuditOutboxMessages
            .Where(message => message.NextAttemptAt != null)
            .MaxAsync(message => (DateTime?)message.UpdatedAt, ct);
        var lastSuccess = await db.AuditOutboxMessages
            .Where(message => message.DeliveredAt != null)
            .MaxAsync(message => message.DeliveredAt, ct);
        var lastError = await db.AuditOutboxMessages
            .Where(message => message.LastError != null)
            .OrderByDescending(message => message.UpdatedAt)
            .Select(message => message.LastError)
            .FirstOrDefaultAsync(ct);

        var now = clock.GetUtcNow().UtcDateTime;
        var required = audit.ResolveRequireRemoteDelivery(
            ETL_SQL.Core.Governance.EnterprisePolicyRuntime.Current.IsEnrolled);

        return new AuditCollectorHealthDto(
            CollectorConfigured: !string.IsNullOrWhiteSpace(audit.TransportEndpoint),
            // Host and path only. The configured endpoint can carry a token in its query string.
            CollectorEndpoint: DescribeEndpoint(audit.TransportEndpoint),
            BearerTokenConfigured: !string.IsNullOrWhiteSpace(audit.TransportBearerToken),
            RemoteDeliveryRequired: required,
            Pending: pendingCount,
            Failed: await db.AuditOutboxMessages.CountAsync(message => message.Status == "Failed", ct),
            Delivered: await db.AuditOutboxMessages.CountAsync(message => message.Status == "Delivered", ct),
            PendingBytes: await pending.SumAsync(message => (long)message.PayloadJson.Length, ct),
            OldestPendingUtc: oldestPending,
            OldestPendingAgeSeconds: oldestPending is DateTime oldest
                ? (int)Math.Max(0, (now - oldest).TotalSeconds)
                : null,
            LastAttemptUtc: lastAttempt,
            LastSuccessUtc: lastSuccess,
            LastError: lastError,
            Thresholds: new AuditCollectorThresholdsDto(
                audit.FailClosedMaxPendingBacklog,
                audit.FailClosedMaxBacklogSeconds,
                audit.OutboxMaxBytes,
                audit.TransportMaxAttempts,
                audit.OutboxBackpressureLimit),
            FailClosed: await EvaluateFailClosedAsync(required, ct));
    }

    /// <summary>
    /// Asks the gate itself whether the next mutation would be refused, instead of re-deriving its
    /// thresholds here. A second copy of that rule would eventually disagree with the one that
    /// actually blocks writes, and an operator would be reading a reassurance that is not true.
    /// </summary>
    private async Task<AuditCollectorFailClosedDto> EvaluateFailClosedAsync(bool required, CancellationToken ct)
    {
        if (!required)
        {
            return new AuditCollectorFailClosedDto(false, null,
                "Remote delivery is not required, so mutations are never blocked on the collector.");
        }

        try
        {
            await AuditDeliveryGate.EnsureDeliverableAsync(db, config.Audit, clock, ct);
            return new AuditCollectorFailClosedDto(false, null,
                "Remote delivery is required and currently healthy; mutations proceed.");
        }
        catch (AuditDeliveryUnavailableException ex)
        {
            return new AuditCollectorFailClosedDto(true, ex.Message,
                "Security-sensitive mutations are being refused with HTTP 503 until delivery recovers.");
        }
    }

    private static string? DescribeEndpoint(string? endpoint) =>
        Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            ? $"{uri.Scheme}://{uri.Host}{(uri.IsDefaultPort ? "" : $":{uri.Port}")}{uri.AbsolutePath}"
            : null;
}
