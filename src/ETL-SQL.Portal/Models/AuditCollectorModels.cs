namespace ETL_SQL.Portal.Models;

/// <param name="CollectorEndpoint">
/// Scheme, host and path only. A configured endpoint can carry a token in its query string, so the
/// query is never reported.
/// </param>
public sealed record AuditCollectorHealthDto(
    bool CollectorConfigured,
    string? CollectorEndpoint,
    bool BearerTokenConfigured,
    bool RemoteDeliveryRequired,
    int Pending,
    int Failed,
    int Delivered,
    long PendingBytes,
    DateTime? OldestPendingUtc,
    int? OldestPendingAgeSeconds,
    DateTime? LastAttemptUtc,
    DateTime? LastSuccessUtc,
    string? LastError,
    AuditCollectorThresholdsDto Thresholds,
    AuditCollectorFailClosedDto FailClosed);

/// <summary>The limits the fail-closed policy compares against, so a reading can be interpreted.</summary>
public sealed record AuditCollectorThresholdsDto(
    int FailClosedMaxPendingBacklog,
    int FailClosedMaxBacklogSeconds,
    long OutboxMaxBytes,
    int TransportMaxAttempts,
    int OutboxBackpressureLimit);

/// <param name="Tripped">Whether the next security-sensitive mutation would be refused.</param>
/// <param name="Reason">The gate's own message when tripped.</param>
public sealed record AuditCollectorFailClosedDto(
    bool Tripped,
    string? Reason,
    string Explanation);

/// <param name="Delivered">Whether the collector accepted the probe.</param>
/// <param name="Error">Redacted failure detail; never the request body or credentials.</param>
public sealed record AuditCollectorTestDeliveryDto(
    bool Delivered,
    int? StatusCode,
    string? Error,
    string? Endpoint,
    int ElapsedMs);
