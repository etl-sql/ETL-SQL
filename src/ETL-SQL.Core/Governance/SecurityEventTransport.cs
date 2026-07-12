using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ETL_SQL.Core.Governance;

public sealed record SecurityEventTransportOptions
{
    public required Uri CollectorEndpoint { get; init; }
    public int BatchSize { get; init; } = 100;
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromMinutes(2);
}

public sealed record SecurityEventDeliveryResult(
    int Claimed,
    int Delivered,
    int Failed,
    string? Error = null);

/// <summary>
/// HTTPS transport for the local security-event outbox. Event IDs are both the collector's
/// idempotency keys and the acknowledgement unit; a successful HTTP status alone never deletes an
/// event unless the response explicitly acknowledges its ID.
/// </summary>
public sealed class SecurityEventTransport
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SecurityEventOutbox _outbox;
    private readonly HttpClient _http;
    private readonly SecurityEventTransportOptions _options;

    public SecurityEventTransport(
        SecurityEventOutbox outbox,
        HttpClient http,
        SecurityEventTransportOptions options)
    {
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (!string.Equals(options.CollectorEndpoint.Scheme, Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(options.CollectorEndpoint.UserInfo))
            throw new ArgumentException(
                "Security event collector endpoint must be HTTPS without embedded credentials.",
                nameof(options));
        if (options.BatchSize <= 0 || options.LeaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "Transport limits must be positive.");
    }

    public async Task<SecurityEventDeliveryResult> DrainOnceAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var batch = _outbox.ClaimBatch(_options.BatchSize, nowUtc, _options.LeaseDuration);
        if (batch.Count == 0) return new(0, 0, 0);

        var eventIds = batch.Select(item => item.Event.EventId).ToArray();
        var batchId = BatchId(eventIds);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _options.CollectorEndpoint);
            request.Headers.TryAddWithoutValidation("Idempotency-Key", batchId);
            request.Headers.TryAddWithoutValidation("X-ETL-SQL-Security-Event-Schema",
                SecurityEventContract.CurrentSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
            request.Content = JsonContent.Create(new
            {
                schemaVersion = SecurityEventContract.CurrentSchemaVersion,
                batchId,
                events = batch.Select(item => item.Event).ToArray()
            }, options: JsonOptions);

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var error = $"Collector returned HTTP {(int)response.StatusCode}.";
                _outbox.MarkDeliveryFailed(eventIds, error, nowUtc);
                return new(batch.Count, 0, batch.Count, error);
            }

            var acknowledgement = await response.Content
                .ReadFromJsonAsync<CollectorAcknowledgement>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            var acknowledged = acknowledgement?.AcknowledgedEventIds
                .Where(eventIds.Contains)
                .Distinct()
                .ToArray() ?? [];
            var unacknowledged = eventIds.Except(acknowledged).ToArray();
            if (acknowledged.Length > 0)
                _outbox.MarkDelivered(acknowledged, nowUtc);
            if (unacknowledged.Length > 0)
                _outbox.MarkDeliveryFailed(unacknowledged,
                    "Collector response did not acknowledge the event ID.", nowUtc);

            return new(batch.Count, acknowledged.Length, unacknowledged.Length,
                unacknowledged.Length == 0 ? null : "Collector acknowledgement was incomplete.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var error = ETL_SQL.Core.Common.SecretRedactor.Redact(ex.Message)
                ?? "Security event collector request failed.";
            _outbox.MarkDeliveryFailed(eventIds, error, nowUtc);
            return new(batch.Count, 0, batch.Count, error);
        }
    }

    internal static string BatchId(IEnumerable<Guid> eventIds)
    {
        var canonical = string.Join('\n', eventIds.Order().Select(id => id.ToString("D")));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    private sealed record CollectorAcknowledgement(Guid[] AcknowledgedEventIds);
}
