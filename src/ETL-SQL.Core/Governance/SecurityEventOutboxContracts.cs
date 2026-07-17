namespace ETL_SQL.Core.Governance;

public sealed record SecurityEventOutboxOptions
{
    public required string DatabasePath { get; init; }
    public long MaxBytes { get; init; } = 64 * 1024 * 1024;
    public int MaxPendingEvents { get; init; } = 100_000;
    public int MaxDeliveryAttempts { get; init; } = 10;
    public TimeSpan InitialRetryDelay { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromHours(1);
}

public sealed record SecurityEventOutboxItem(
    SecurityEvent Event,
    int Attempts,
    DateTimeOffset CreatedAtUtc);

public sealed record SecurityEventOutboxHealth(
    int PendingCount,
    int FailedCount,
    int FilteredCount,
    long StoredBytes,
    DateTimeOffset? OldestPendingUtc,
    DateTimeOffset? LastDeliveredUtc);

public sealed class SecurityEventOutboxFullException(string message) : IOException(message);

public interface ISecurityEventOutbox : ISecurityEventSink
{
    string DatabasePath { get; }
    IReadOnlyList<SecurityEventOutboxItem> ClaimBatch(int batchSize, DateTimeOffset nowUtc, TimeSpan leaseDuration);
    void MarkDelivered(IEnumerable<Guid> eventIds, DateTimeOffset deliveredAtUtc);
    void MarkDeliveryFailed(IEnumerable<Guid> eventIds, string error, DateTimeOffset failedAtUtc);
    int PruneDelivered(DateTimeOffset deliveredBeforeUtc);
    int ApplyForwardingFilter(SecurityEventSeverity minimumSeverity, DateTimeOffset filteredAtUtc);
    SecurityEventOutboxHealth GetHealth();
}

public interface ISecurityEventOutboxFactory
{
    ISecurityEventOutbox Create(SecurityEventOutboxOptions options, Func<double>? jitter = null);
}
