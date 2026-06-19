using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Core.Common;
using ETL_SQL.ReportPortal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.ReportPortal.Services;

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
        var endpoint = GetEndpointOrThrow();
        var now = clock.GetUtcNow().UtcDateTime;
        var batchSize = Math.Max(1, config.Audit.TransportBatchSize);
        var lockUntil = now.AddSeconds(Math.Max(1, config.Audit.TransportLockSeconds));

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();

        var pendingCount = await db.AuditOutboxMessages.CountAsync(x => x.Status == "Pending", ct);
        if (pendingCount > Math.Max(1, config.Audit.OutboxBackpressureLimit))
        {
            log.LogWarning(
                "Audit outbox pending backlog {PendingCount} exceeds configured limit {Limit}",
                pendingCount, config.Audit.OutboxBackpressureLimit);
        }

        var rows = await db.AuditOutboxMessages
            .Where(x => x.Status == "Pending"
                && (x.NextAttemptAt == null || x.NextAttemptAt <= now)
                && (x.LockedUntil == null || x.LockedUntil <= now))
            .OrderBy(x => x.Id)
            .Take(batchSize)
            .ToListAsync(ct);

        if (rows.Count == 0)
            return 0;

        foreach (var row in rows)
        {
            row.LockedUntil = lockUntil;
            row.UpdatedAt = now;
        }
        await db.SaveChangesAsync(ct);

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
        return rows.Count;
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
