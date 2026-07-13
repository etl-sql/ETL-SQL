using ETL_SQL.Core.Governance;
using Microsoft.Data.Sqlite;

namespace ETL_SQL.Tests.Core;

public sealed class SecurityEventOutboxTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"security_outbox_{Guid.NewGuid():N}");

    [Fact]
    public void Emit_IsAtomicDeduplicatedAndSanitizedBeforePersistence()
    {
        var outbox = CreateOutbox();
        var securityEvent = Event(Guid.Parse("91729d30-e3eb-4840-b8fa-6fc2285b2a95")) with
        {
            SanitizedTarget = "https://api.example.test/data?token=raw-query-secret",
            Reason = "PASSWORD=raw-password at C:\\private\\payroll.csv"
        };

        outbox.Emit(securityEvent);
        outbox.Emit(securityEvent);

        var health = outbox.GetHealth();
        var claimed = outbox.ClaimBatch(10, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        var stored = Assert.Single(claimed).Event;
        Assert.Equal(1, health.PendingCount);
        Assert.Equal("https://api.example.test", stored.SanitizedTarget);
        Assert.DoesNotContain("raw-query-secret", SecurityEventContract.Serialize(stored),
            StringComparison.Ordinal);
        Assert.DoesNotContain("raw-password", SecurityEventContract.Serialize(stored),
            StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\private", SecurityEventContract.Serialize(stored),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_RejectsNewEventWhenConfiguredCapacityIsReached()
    {
        var outbox = CreateOutbox(maxEvents: 1);
        outbox.Emit(Event(Guid.NewGuid()));

        Assert.Throws<SecurityEventOutboxFullException>(() => outbox.Emit(Event(Guid.NewGuid())));
        Assert.Equal(1, outbox.GetHealth().PendingCount);
    }

    [Fact]
    public void Emit_RejectsAtomicallyWhenPayloadExceedsByteCapacity()
    {
        var outbox = CreateOutbox(maxBytes: 1);

        Assert.Throws<SecurityEventOutboxFullException>(() =>
            outbox.Emit(Event(Guid.NewGuid())));
        Assert.Equal(0, outbox.GetHealth().PendingCount);
        Assert.Equal(0, outbox.GetHealth().StoredBytes);
    }

    [Fact]
    public void ClaimBatch_ExcludesActiveLeaseAndRecoversExpiredLeaseAfterReopen()
    {
        var now = new DateTimeOffset(2026, 7, 12, 18, 0, 0, TimeSpan.Zero);
        var eventId = Guid.NewGuid();
        var outbox = CreateOutbox();
        outbox.Emit(Event(eventId));

        Assert.Single(outbox.ClaimBatch(10, now, TimeSpan.FromMinutes(2)));
        Assert.Empty(outbox.ClaimBatch(10, now.AddMinutes(1), TimeSpan.FromMinutes(2)));

        var reopened = CreateOutbox();
        var recovered = Assert.Single(reopened.ClaimBatch(10, now.AddMinutes(2), TimeSpan.FromMinutes(2)));
        Assert.Equal(eventId, recovered.Event.EventId);
    }

    [Fact]
    public void DeliveryFailure_UsesJitteredRetryThenBecomesTerminal()
    {
        var now = new DateTimeOffset(2026, 7, 12, 18, 0, 0, TimeSpan.Zero);
        var eventId = Guid.NewGuid();
        var outbox = CreateOutbox(maxAttempts: 2, retryDelay: TimeSpan.FromSeconds(10));
        outbox.Emit(Event(eventId));
        Assert.Single(outbox.ClaimBatch(1, now, TimeSpan.FromMinutes(1)));

        outbox.MarkDeliveryFailed([eventId], "Bearer raw-token", now);

        Assert.Empty(outbox.ClaimBatch(1, now.AddSeconds(9), TimeSpan.FromMinutes(1)));
        var retry = Assert.Single(outbox.ClaimBatch(1, now.AddSeconds(10), TimeSpan.FromMinutes(1)));
        Assert.Equal(1, retry.Attempts);

        outbox.MarkDeliveryFailed([eventId], "PASSWORD=still-secret", now.AddSeconds(10));

        Assert.Empty(outbox.ClaimBatch(1, now.AddDays(1), TimeSpan.FromMinutes(1)));
        var health = outbox.GetHealth();
        Assert.Equal(0, health.PendingCount);
        Assert.Equal(1, health.FailedCount);
    }

    [Fact]
    public void DeliveredEventsUpdateHealthAndCanBePruned()
    {
        var now = new DateTimeOffset(2026, 7, 12, 18, 0, 0, TimeSpan.Zero);
        var eventId = Guid.NewGuid();
        var outbox = CreateOutbox();
        outbox.Emit(Event(eventId));
        Assert.Single(outbox.ClaimBatch(1, now, TimeSpan.FromMinutes(1)));

        outbox.MarkDelivered([eventId], now);

        var health = outbox.GetHealth();
        Assert.Equal(0, health.PendingCount);
        Assert.Equal(now, health.LastDeliveredUtc);
        Assert.Equal(1, outbox.PruneDelivered(now.AddSeconds(1)));
        Assert.Equal(0, outbox.GetHealth().StoredBytes);
    }

    [Fact]
    public void ExistingOutboxSchema_IsMigratedBeforeSeverityFiltering()
    {
        Directory.CreateDirectory(_root);
        var databasePath = Path.Combine(_root, "security-events.db");
        var legacyEvent = Event(Guid.NewGuid());
        var legacyPayload = SecurityEventContract.Serialize(legacyEvent);
        using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE security_events (
                    event_id TEXT PRIMARY KEY, payload_json TEXT NOT NULL,
                    payload_bytes INTEGER NOT NULL, status TEXT NOT NULL,
                    attempts INTEGER NOT NULL, next_attempt_utc TEXT NULL,
                    locked_until_utc TEXT NULL, last_error TEXT NULL,
                    created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL,
                    delivered_utc TEXT NULL);
                INSERT INTO security_events
                    (event_id, payload_json, payload_bytes, status, attempts, created_utc, updated_utc)
                VALUES ($eventId, $payload, $payloadBytes, 'Pending', 0, $now, $now);
                """;
            command.Parameters.AddWithValue("$eventId", legacyEvent.EventId.ToString("D"));
            command.Parameters.AddWithValue("$payload", legacyPayload);
            command.Parameters.AddWithValue("$payloadBytes", System.Text.Encoding.UTF8.GetByteCount(legacyPayload));
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }

        var outbox = CreateOutbox();

        Assert.Equal(0, outbox.ApplyForwardingFilter(
            SecurityEventSeverity.Warning, DateTimeOffset.UtcNow));
        var claimed = Assert.Single(outbox.ClaimBatch(10, DateTimeOffset.UtcNow.AddMinutes(1),
            TimeSpan.FromMinutes(1)));
        Assert.Equal(legacyEvent.EventId, claimed.Event.EventId);
    }

    [Fact]
    public void FilteredEvent_DoesNotReportAsSuccessfulDelivery()
    {
        var outbox = CreateOutbox();
        outbox.Emit(Event(Guid.NewGuid()) with { Severity = SecurityEventSeverity.Information });

        Assert.Equal(1, outbox.ApplyForwardingFilter(
            SecurityEventSeverity.Warning, DateTimeOffset.UtcNow));

        var health = outbox.GetHealth();
        Assert.Equal(1, health.FilteredCount);
        Assert.Null(health.LastDeliveredUtc);
    }

    [Fact]
    public void ClaimBatch_QuarantinesCorruptPayloadAndContinuesWithValidEvents()
    {
        var corruptId = Guid.NewGuid();
        var validId = Guid.NewGuid();
        var outbox = CreateOutbox();
        outbox.Emit(Event(corruptId));
        outbox.Emit(Event(validId));
        using (var connection = new SqliteConnection($"Data Source={outbox.DatabasePath}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "UPDATE security_events SET payload_json = '{invalid' WHERE event_id = $eventId;";
            command.Parameters.AddWithValue("$eventId", corruptId.ToString("D"));
            command.ExecuteNonQuery();
        }

        var claimed = outbox.ClaimBatch(10, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));

        Assert.Equal(validId, Assert.Single(claimed).Event.EventId);
        var health = outbox.GetHealth();
        Assert.Equal(1, health.FailedCount);
        Assert.Equal(1, health.PendingCount);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private SecurityEventOutbox CreateOutbox(
        int maxEvents = 100,
        int maxAttempts = 3,
        TimeSpan? retryDelay = null,
        long maxBytes = 1024 * 1024) =>
        new(new SecurityEventOutboxOptions
        {
            DatabasePath = Path.Combine(_root, "security-events.db"),
            MaxBytes = maxBytes,
            MaxPendingEvents = maxEvents,
            MaxDeliveryAttempts = maxAttempts,
            InitialRetryDelay = retryDelay ?? TimeSpan.FromSeconds(30),
            MaxRetryDelay = TimeSpan.FromMinutes(5)
        }, jitter: () => 0.5);

    private static SecurityEvent Event(Guid eventId) => SecurityEventContract.Create(
        SecurityEventSeverity.Error,
        SecurityEventType.OperationDenied,
        "user:42",
        "service:runner",
        "<target>",
        SecurityEventDecision.Denied,
        "Operation denied.",
        eventId: eventId);
}
