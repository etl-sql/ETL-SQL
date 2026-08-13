using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Observability;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

public sealed class AuditOutboxTransportService(
    IServiceScopeFactory scopeFactory,
    PortalConfig config,
    HttpClient http,
    TimeProvider clock,
    ILogger<AuditOutboxTransportService> log) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(config.Audit.TransportEndpoint))
            return;

        var interval = TimeSpan.FromSeconds(Math.Max(1, config.Audit.TransportIntervalSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PruneAsync(stoppingToken);
                await DrainOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Audit outbox transport sweep failed; will retry next interval");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public async Task<int> DrainOnceAsync(CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var activity = BackgroundServiceObservability.StartRun("portal", "audit-outbox-transport", "drain");
        try
        {
            var endpoint = GetEndpointOrThrow();
            var now = clock.GetUtcNow().UtcDateTime;
            var batchSize = Math.Max(1, config.Audit.TransportBatchSize);

            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();

            var pendingCount = await db.AuditOutboxMessages.CountAsync(x => x.Status == "Pending", ct);
            if (pendingCount > Math.Max(1, config.Audit.OutboxBackpressureLimit))
            {
                log.LogWarning(
                    "Audit outbox pending backlog {PendingCount} exceeds configured limit {Limit}",
                    pendingCount, config.Audit.OutboxBackpressureLimit);
            }

            var rows = await ClaimBatchAsync(db, now, batchSize, ct);

            if (rows.Count == 0)
            {
                CompleteTransport(activity, sw, "drain", "empty", 0);
                return 0;
            }

            var response = await PostBatchAsync(endpoint, rows, ct);
            now = clock.GetUtcNow().UtcDateTime;

            if (response.IsSuccessStatusCode)
            {
                foreach (var row in rows)
                {
                    row.Status = "Delivered";
                    row.DeliveredAt = now;
                    row.LockedUntil = null;
                    row.LastError = null;
                    row.UpdatedAt = now;
                }
            }
            else
            {
                var error = SecretRedactor.Redact($"{(int)response.StatusCode} {response.ReasonPhrase}") ?? "Transport failed";
                ApplyFailure(rows, now, error);
            }

            await db.SaveChangesAsync(ct);
            CompleteTransport(activity, sw, "drain", response.IsSuccessStatusCode ? "delivered" : "failed", rows.Count);
            return rows.Count;
        }
        catch
        {
            CompleteTransport(activity, sw, "drain", "failure", 0);
            throw;
        }
    }

    /// <summary>
    /// Local outbox disk-size safeguards and retention (P2.1). Purges Delivered rows past their
    /// retention window, then — only when remote delivery is NOT mandatory — sheds the oldest rows
    /// to keep the queue under <see cref="AuditConfig.OutboxMaxBytes"/> during an extended collector
    /// outage. When delivery IS mandatory the queue is never silently trimmed: the fail-closed gate
    /// (<see cref="AuditDeliveryGate"/>) blocks new mutations instead, so no audit event is dropped.
    /// Returns the number of rows removed.
    /// </summary>
    public async Task<int> PruneAsync(CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var activity = BackgroundServiceObservability.StartRun("portal", "audit-outbox-transport", "prune");
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var now = clock.GetUtcNow().UtcDateTime;
            var removed = 0;

            var retentionCutoff = now.AddMinutes(-Math.Max(1, config.Audit.OutboxDeliveredRetentionMinutes));
            var purgedDelivered = await db.AuditOutboxMessages
                .Where(x => x.Status == "Delivered" && x.DeliveredAt != null && x.DeliveredAt < retentionCutoff)
                .ExecuteDeleteAsync(ct);
            removed += purgedDelivered;
            if (purgedDelivered > 0)
                log.LogInformation(
                    "Purged {Count} delivered audit outbox rows older than {Minutes}m",
                    purgedDelivered, config.Audit.OutboxDeliveredRetentionMinutes);

            if (config.Audit.OutboxMaxBytes <= 0)
            {
                CompleteTransport(activity, sw, "prune", "success", removed);
                return removed;
            }

            var totalBytes = await db.AuditOutboxMessages.SumAsync(x => (long)x.PayloadJson.Length, ct);
            if (totalBytes < config.Audit.OutboxMaxBytes)
            {
                CompleteTransport(activity, sw, "prune", "success", removed);
                return removed;
            }

            if (config.Audit.ResolveRequireRemoteDelivery(
                    ETL_SQL.Core.Governance.EnterprisePolicyRuntime.Current.IsEnrolled))
            {
                // Mandatory delivery: do not drop events — the fail-closed gate already stops new
                // mutations. Surface the saturation for operators.
                log.LogError(
                    "Audit outbox (~{Bytes} bytes) has reached the size cap ({Cap} bytes) while remote " +
                    "delivery is required; mutations are failing closed until the collector drains.",
                    totalBytes, config.Audit.OutboxMaxBytes);
                CompleteTransport(activity, sw, "prune", "saturated", removed);
                return removed;
            }

            // Best-effort forwarding: shed the oldest rows (Delivered first, then Pending) to bound
            // local disk use. Dropped Pending rows lose remote forwarding only — the durable AuditLog
            // record remains.
            log.LogWarning(
                "Audit outbox (~{Bytes} bytes) exceeds the size cap ({Cap} bytes); shedding oldest rows " +
                "to bound local disk use (remote delivery is not required).",
                totalBytes, config.Audit.OutboxMaxBytes);

            var shed = await ShedToCapAsync(db, totalBytes, ct);
            removed += shed;
            CompleteTransport(activity, sw, "prune", "success", removed);
            return removed;
        }
        catch
        {
            CompleteTransport(activity, sw, "prune", "failure", 0);
            throw;
        }
    }

    private static void CompleteTransport(System.Diagnostics.Activity? activity, System.Diagnostics.Stopwatch sw,
        string operation, string status, long rowsProcessed)
    {
        sw.Stop();
        BackgroundServiceObservability.SetRowsProcessed(activity, rowsProcessed);
        BackgroundServiceObservability.CompleteRun(
            activity,
            "portal",
            "audit-outbox-transport",
            operation,
            status,
            sw.ElapsedMilliseconds);
    }

    private async Task<int> ShedToCapAsync(PortalDbContext db, long totalBytes, CancellationToken ct)
    {
        var cap = config.Audit.OutboxMaxBytes;
        var shed = 0;
        const int chunk = 500;

        while (totalBytes >= cap)
        {
            // Prefer already-delivered rows, then the oldest remaining, so a sustained outage keeps
            // the most recent events for the collector when it returns.
            var batch = await db.AuditOutboxMessages
                .OrderBy(x => x.Status == "Delivered" ? 0 : 1)
                .ThenBy(x => x.Id)
                .Take(chunk)
                .Select(x => new { x.Id, Len = (long)x.PayloadJson.Length })
                .ToListAsync(ct);
            if (batch.Count == 0)
                break;

            var ids = batch.Select(b => b.Id).ToList();
            await db.AuditOutboxMessages.Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync(ct);
            totalBytes -= batch.Sum(b => b.Len);
            shed += batch.Count;
        }

        if (shed > 0)
            log.LogWarning("Shed {Count} audit outbox rows to satisfy the {Cap}-byte size cap", shed, cap);
        return shed;
    }

    private async Task<List<AuditOutboxMessage>> ClaimBatchAsync(
        PortalDbContext db,
        DateTime now,
        int batchSize,
        CancellationToken ct)
    {
        var candidateIds = await db.AuditOutboxMessages
            .Where(x => x.Status == "Pending"
                && (x.NextAttemptAt == null || x.NextAttemptAt <= now)
                && (x.LockedUntil == null || x.LockedUntil <= now))
            .OrderBy(x => x.Id)
            .Take(batchSize)
            .Select(x => x.Id)
            .ToListAsync(ct);

        if (candidateIds.Count == 0)
            return [];

        var lockUntil = CreateUniqueLockUntil(now);
        var claimed = await db.AuditOutboxMessages
            .Where(x => candidateIds.Contains(x.Id)
                && x.Status == "Pending"
                && (x.NextAttemptAt == null || x.NextAttemptAt <= now)
                && (x.LockedUntil == null || x.LockedUntil <= now))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.LockedUntil, (DateTime?)lockUntil)
                .SetProperty(x => x.UpdatedAt, now), ct);

        if (claimed == 0)
            return [];

        return await db.AuditOutboxMessages
            .Where(x => candidateIds.Contains(x.Id)
                && x.Status == "Pending"
                && x.LockedUntil == lockUntil)
            .OrderBy(x => x.Id)
            .ToListAsync(ct);
    }

    private DateTime CreateUniqueLockUntil(DateTime now)
    {
        var lockSeconds = Math.Max(1, config.Audit.TransportLockSeconds);
        var jitterTicks = RandomNumberGenerator.GetInt32(1, 1_000_000);
        return now.AddSeconds(lockSeconds).AddTicks(jitterTicks);
    }

    /// <summary>
    /// Posts a synthetic event to the configured collector and reports the outcome, without touching
    /// the outbox.
    ///
    /// It goes through <see cref="PostBatchAsync"/> — the same endpoint resolution, the same
    /// authentication, the same body shape as a real delivery. A probe that took its own path would
    /// prove the probe works, not the delivery. The event carries no real audit content, so a
    /// misconfigured endpoint receives nothing of consequence.
    /// </summary>
    public async Task<ETL_SQL.Portal.Models.AuditCollectorTestDeliveryDto> SendTestDeliveryAsync(
        CancellationToken ct = default)
    {
        var started = clock.GetTimestamp();
        int Elapsed() => (int)clock.GetElapsedTime(started).TotalMilliseconds;

        if (string.IsNullOrWhiteSpace(config.Audit.TransportEndpoint))
        {
            return new(false, null, "No collector endpoint is configured.", null, Elapsed());
        }

        Uri endpoint;
        try
        {
            endpoint = GetEndpointOrThrow();
        }
        catch (InvalidOperationException ex)
        {
            return new(false, null, ex.Message, null, Elapsed());
        }

        var probe = new AuditOutboxMessage
        {
            Action = "AUDIT_COLLECTOR_TEST_DELIVERY",
            ResourceType = "AuditCollector",
            OccurredAt = clock.GetUtcNow().UtcDateTime,
            PayloadJson = """{"probe":true,"note":"Synthetic delivery test; carries no audit content."}"""
        };

        var described = $"{endpoint.Scheme}://{endpoint.Host}{(endpoint.IsDefaultPort ? "" : $":{endpoint.Port}")}{endpoint.AbsolutePath}";
        try
        {
            using var response = await PostBatchAsync(endpoint, [probe], ct);
            return new(
                response.IsSuccessStatusCode,
                (int)response.StatusCode,
                response.IsSuccessStatusCode ? null : $"Collector returned {(int)response.StatusCode}.",
                described,
                Elapsed());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            log.LogWarning(ex, "Audit collector test delivery failed.");
            // Redacted: a transport failure can echo a URL carrying a token.
            return new(false, null, ETL_SQL.Core.Common.SecretRedactor.Redact(ex.Message), described, Elapsed());
        }
    }

    private Uri GetEndpointOrThrow()
    {
        if (!Uri.TryCreate(config.Audit.TransportEndpoint, UriKind.Absolute, out var endpoint))
            throw new InvalidOperationException("Portal:Audit:TransportEndpoint must be an absolute HTTPS URI.");
        if (!string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Portal:Audit:TransportEndpoint must use HTTPS.");
        return endpoint;
    }

    private async Task<HttpResponseMessage> PostBatchAsync(Uri endpoint, IReadOnlyList<AuditOutboxMessage> rows, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        if (!string.IsNullOrWhiteSpace(config.Audit.TransportBearerToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.Audit.TransportBearerToken);

        var body = new
        {
            events = rows.Select(row => new
            {
                row.TenantId,
                row.EventId,
                row.AuditLogId,
                row.UserId,
                row.Action,
                row.ResourceType,
                row.ResourceId,
                row.CorrelationId,
                row.OccurredAt,
                payload = JsonNode.Parse(row.PayloadJson)
            }).ToArray()
        };
        request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        return await http.SendAsync(request, ct);
    }

    private void ApplyFailure(IReadOnlyList<AuditOutboxMessage> rows, DateTime now, string error)
    {
        var maxAttempts = Math.Max(1, config.Audit.TransportMaxAttempts);
        foreach (var row in rows)
        {
            row.Attempts++;
            row.LockedUntil = null;
            row.LastError = error;
            row.UpdatedAt = now;

            if (row.Attempts >= maxAttempts)
            {
                row.Status = "Failed";
                row.NextAttemptAt = null;
                continue;
            }

            var delaySeconds = Math.Min(3600, Math.Pow(2, row.Attempts - 1) * 30);
            row.NextAttemptAt = now.AddSeconds(delaySeconds);
        }
    }
}
