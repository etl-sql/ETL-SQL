using ETL_SQL.ReportPortal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.ReportPortal.Services;

/// <summary>
/// Thrown when a security-sensitive mutation cannot proceed because required remote audit delivery
/// is unavailable. Surfaced to clients as HTTP 503 by the fail-closed middleware in
/// <c>Program.cs</c>. The message is operator-facing (no secret material) and explains which
/// fail-closed condition tripped so the outage can be triaged.
/// </summary>
public sealed class AuditDeliveryUnavailableException(string message) : Exception(message);

/// <summary>
/// Evaluates whether durable remote audit delivery is healthy enough to permit new mutations.
/// This is the enforcement side of the governance <c>Audit:RemoteDeliveryRequired</c> policy
/// (P1.12): when delivery is required and the local outbox shows the collector is unreachable, a
/// mutation must fail closed rather than commit a security event that can never be forwarded.
///
/// The check is centralized in <see cref="AuditFailClosedInterceptor"/>, which invokes it whenever
/// a mutation stages a new <see cref="AuditOutboxMessage"/> in the same unit of work. A single
/// queued event during a brief outage is allowed; the gate trips only once the backlog, its age, or
/// its size shows the collector has been down long enough to risk losing the audit trail.
/// </summary>
public static class AuditDeliveryGate
{
    private readonly record struct OutboxBacklog(int Failed, int Pending, DateTime? OldestPending, long PendingBytes);

    /// <summary>
    /// Throws <see cref="AuditDeliveryUnavailableException"/> if the outbox indicates remote audit
    /// delivery is unavailable. Reads the persisted backlog only (the row being staged in this unit
    /// of work is not yet committed, so the first event of an outage is never blocked).
    /// </summary>
    public static async Task EnsureDeliverableAsync(
        PortalDbContext db,
        AuditConfig config,
        TimeProvider clock,
        CancellationToken ct = default)
    {
        var pending = db.AuditOutboxMessages.Where(x => x.Status == "Pending");
        var backlog = new OutboxBacklog(
            Failed: await db.AuditOutboxMessages.CountAsync(x => x.Status == "Failed", ct),
            Pending: config.FailClosedMaxPendingBacklog > 0 ? await pending.CountAsync(ct) : 0,
            OldestPending: config.FailClosedMaxBacklogSeconds > 0
                ? await pending.OrderBy(x => x.OccurredAt).Select(x => (DateTime?)x.OccurredAt).FirstOrDefaultAsync(ct)
                : null,
            PendingBytes: config.OutboxMaxBytes > 0 ? await pending.SumAsync(x => (long)x.PayloadJson.Length, ct) : 0);

        Evaluate(backlog, config, clock);
    }

    /// <summary>Synchronous counterpart of <see cref="EnsureDeliverableAsync"/> for the rarely used
    /// synchronous SaveChanges path; avoids blocking on async work.</summary>
    public static void EnsureDeliverable(PortalDbContext db, AuditConfig config, TimeProvider clock)
    {
        var pending = db.AuditOutboxMessages.Where(x => x.Status == "Pending");
        var backlog = new OutboxBacklog(
            Failed: db.AuditOutboxMessages.Count(x => x.Status == "Failed"),
            Pending: config.FailClosedMaxPendingBacklog > 0 ? pending.Count() : 0,
            OldestPending: config.FailClosedMaxBacklogSeconds > 0
                ? pending.OrderBy(x => x.OccurredAt).Select(x => (DateTime?)x.OccurredAt).FirstOrDefault()
                : null,
            PendingBytes: config.OutboxMaxBytes > 0 ? pending.Sum(x => (long)x.PayloadJson.Length) : 0);

        Evaluate(backlog, config, clock);
    }

    private static void Evaluate(OutboxBacklog backlog, AuditConfig config, TimeProvider clock)
    {
        // A terminally failed delivery means retries were exhausted: the audit trail is already
        // broken, so refuse further mutations until an operator clears the backlog.
        if (backlog.Failed > 0)
            throw new AuditDeliveryUnavailableException(
                $"Remote audit delivery is required but {backlog.Failed} audit event(s) have failed delivery; " +
                "mutations are blocked until the audit collector is reachable and the backlog is cleared.");

        var maxBacklog = config.FailClosedMaxPendingBacklog;
        if (maxBacklog > 0 && backlog.Pending >= maxBacklog)
            throw new AuditDeliveryUnavailableException(
                $"Remote audit delivery is required but the undelivered audit backlog ({backlog.Pending}) " +
                $"has reached the fail-closed limit ({maxBacklog}); mutations are blocked until the " +
                "audit collector drains the queue.");

        var maxAgeSeconds = config.FailClosedMaxBacklogSeconds;
        if (maxAgeSeconds > 0 && backlog.OldestPending is { } oldestAt)
        {
            var ageSeconds = (clock.GetUtcNow().UtcDateTime - oldestAt).TotalSeconds;
            if (ageSeconds > maxAgeSeconds)
                throw new AuditDeliveryUnavailableException(
                    $"Remote audit delivery is required but the oldest undelivered audit event is " +
                    $"{(int)ageSeconds}s old (limit {maxAgeSeconds}s); mutations are blocked until the " +
                    "audit collector is reachable.");
        }

        if (config.OutboxMaxBytes > 0 && backlog.PendingBytes >= config.OutboxMaxBytes)
            throw new AuditDeliveryUnavailableException(
                $"Remote audit delivery is required but the local audit outbox (~{backlog.PendingBytes} bytes) " +
                $"has reached the configured size cap ({config.OutboxMaxBytes} bytes); mutations are " +
                "blocked until the audit collector drains the queue.");
    }
}
