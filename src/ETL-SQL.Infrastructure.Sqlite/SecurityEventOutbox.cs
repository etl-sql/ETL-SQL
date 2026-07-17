using System.Globalization;
using System.Text;
using ETL_SQL.Core.Common;
using Microsoft.Data.Sqlite;

namespace ETL_SQL.Core.Governance;

/// <summary>
/// Process-independent SQLite outbox for sanitized security events. A unique event ID provides
/// idempotent append, and leased claims recover automatically when a process exits mid-delivery.
/// </summary>
public sealed class SecurityEventOutbox : ISecurityEventOutbox
{
    private readonly SecurityEventOutboxOptions _options;
    private readonly string _connectionString;
    private readonly Func<double> _jitter;

    public string DatabasePath => _options.DatabasePath;

    public SecurityEventOutbox(
        SecurityEventOutboxOptions options,
        Func<double>? jitter = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!Path.IsPathFullyQualified(options.DatabasePath))
            throw new ArgumentException("Security event outbox path must be fully qualified.", nameof(options));
        if (options.MaxBytes <= 0 || options.MaxPendingEvents <= 0
            || options.MaxDeliveryAttempts <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Outbox limits must be positive.");
        if (options.InitialRetryDelay <= TimeSpan.Zero
            || options.MaxRetryDelay < options.InitialRetryDelay)
            throw new ArgumentOutOfRangeException(nameof(options), "Retry delays are invalid.");

        _options = options with { DatabasePath = Path.GetFullPath(options.DatabasePath) };
        _jitter = jitter ?? Random.Shared.NextDouble;
        var directory = Path.GetDirectoryName(_options.DatabasePath)
            ?? throw new ArgumentException("Security event outbox path has no parent directory.", nameof(options));
        Directory.CreateDirectory(directory);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _options.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true
        }.ToString();
        Initialize();
    }

    public void Emit(SecurityEvent securityEvent)
    {
        ArgumentNullException.ThrowIfNull(securityEvent);
        var sanitized = SecurityEventSanitizer.Sanitize(securityEvent);
        var payload = SecurityEventContract.Serialize(sanitized);
        var payloadBytes = Encoding.UTF8.GetByteCount(payload);
        var now = DateTimeOffset.UtcNow;

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: false);
        if (Exists(connection, transaction, sanitized.EventId))
        {
            transaction.Commit();
            return;
        }

        var (count, bytes) = CurrentSize(connection, transaction);
        if (count >= _options.MaxPendingEvents || bytes + payloadBytes > _options.MaxBytes)
            throw new SecurityEventOutboxFullException(
                $"Security event outbox reached its configured capacity ({count} events, {bytes} bytes).");

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO security_events
                (event_id, payload_json, payload_bytes, severity, status, attempts, created_utc, updated_utc)
            VALUES
                ($eventId, $payload, $payloadBytes, $severity, 'Pending', 0, $now, $now);
            """;
        command.Parameters.AddWithValue("$eventId", sanitized.EventId.ToString("D"));
        command.Parameters.AddWithValue("$payload", payload);
        command.Parameters.AddWithValue("$payloadBytes", payloadBytes);
        command.Parameters.AddWithValue("$severity", (int)sanitized.Severity);
        command.Parameters.AddWithValue("$now", Format(now));
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    public IReadOnlyList<SecurityEventOutboxItem> ClaimBatch(
        int batchSize,
        DateTimeOffset nowUtc,
        TimeSpan leaseDuration)
    {
        if (batchSize <= 0) throw new ArgumentOutOfRangeException(nameof(batchSize));
        if (leaseDuration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        nowUtc = nowUtc.ToUniversalTime();

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: false);
        var rows = new List<(string Id, string Payload, int Attempts, string Created)>();
        using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = """
                SELECT event_id, payload_json, attempts, created_utc
                FROM security_events
                WHERE status = 'Pending'
                  AND (next_attempt_utc IS NULL OR next_attempt_utc <= $now)
                  AND (locked_until_utc IS NULL OR locked_until_utc <= $now)
                ORDER BY created_utc, event_id
                LIMIT $batchSize;
                """;
            select.Parameters.AddWithValue("$now", Format(nowUtc));
            select.Parameters.AddWithValue("$batchSize", batchSize);
            using var reader = select.ExecuteReader();
            while (reader.Read())
                rows.Add((reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3)));
        }

        if (rows.Count > 0)
        {
            using var claim = connection.CreateCommand();
            claim.Transaction = transaction;
            claim.CommandText = $"""
                UPDATE security_events
                SET locked_until_utc = $lockedUntil, updated_utc = $now
                WHERE event_id IN ({string.Join(",", rows.Select((_, index) => $"$id{index}"))});
                """;
            claim.Parameters.AddWithValue("$lockedUntil", Format(nowUtc.Add(leaseDuration)));
            claim.Parameters.AddWithValue("$now", Format(nowUtc));
            for (var index = 0; index < rows.Count; index++)
                claim.Parameters.AddWithValue($"$id{index}", rows[index].Id);
            claim.ExecuteNonQuery();
        }
        transaction.Commit();

        var claimed = new List<SecurityEventOutboxItem>(rows.Count);
        foreach (var row in rows)
        {
            try
            {
                claimed.Add(new SecurityEventOutboxItem(
                    SecurityEventContract.Deserialize(row.Payload), row.Attempts, Parse(row.Created)));
            }
            catch
            {
                MarkCorrupt(row.Id, nowUtc);
            }
        }
        return claimed;
    }

    public void MarkDelivered(IEnumerable<Guid> eventIds, DateTimeOffset deliveredAtUtc)
    {
        UpdateMany(eventIds, (connection, transaction, ids) =>
        {
            using var command = CreateIdCommand(connection, transaction, ids, """
                UPDATE security_events
                SET status = 'Delivered', delivered_utc = $now, locked_until_utc = NULL,
                    next_attempt_utc = NULL, last_error = NULL, updated_utc = $now
                WHERE event_id IN ({0});
                """);
            command.Parameters.AddWithValue("$now", Format(deliveredAtUtc));
            command.ExecuteNonQuery();
        });
    }

    public void MarkDeliveryFailed(
        IEnumerable<Guid> eventIds,
        string error,
        DateTimeOffset failedAtUtc)
    {
        var safeError = SecurityEventSanitizer.Sanitize(SecurityEventContract.Create(
            SecurityEventSeverity.Error, SecurityEventType.PolicyAvailabilityFailure,
            "outbox", "outbox", "<collector>", SecurityEventDecision.Failed, error)).Reason;
        UpdateMany(eventIds, (connection, transaction, ids) =>
        {
            foreach (var id in ids)
            {
                var attempts = GetAttempts(connection, transaction, id) + 1;
                var terminal = attempts >= _options.MaxDeliveryAttempts;
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    UPDATE security_events
                    SET attempts = $attempts, status = $status, next_attempt_utc = $nextAttempt,
                        locked_until_utc = NULL, last_error = $error, updated_utc = $now
                    WHERE event_id = $eventId;
                    """;
                command.Parameters.AddWithValue("$attempts", attempts);
                command.Parameters.AddWithValue("$status", terminal ? "Failed" : "Pending");
                command.Parameters.AddWithValue("$nextAttempt",
                    terminal ? DBNull.Value : Format(failedAtUtc.Add(RetryDelay(attempts))));
                command.Parameters.AddWithValue("$error", safeError);
                command.Parameters.AddWithValue("$now", Format(failedAtUtc));
                command.Parameters.AddWithValue("$eventId", id);
                command.ExecuteNonQuery();
            }
        });
    }

    public int PruneDelivered(DateTimeOffset deliveredBeforeUtc)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: false);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM security_events
            WHERE status IN ('Delivered', 'Filtered') AND delivered_utc < $cutoff;
            """;
        command.Parameters.AddWithValue("$cutoff", Format(deliveredBeforeUtc));
        var removed = command.ExecuteNonQuery();
        transaction.Commit();
        return removed;
    }

    public int ApplyForwardingFilter(
        SecurityEventSeverity minimumSeverity,
        DateTimeOffset filteredAtUtc)
    {
        filteredAtUtc = filteredAtUtc.ToUniversalTime();
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: false);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE security_events
            SET status = 'Filtered', delivered_utc = $now, locked_until_utc = NULL,
                next_attempt_utc = NULL, last_error = NULL, updated_utc = $now
            WHERE status = 'Pending'
              AND severity < $minimumSeverity
              AND (locked_until_utc IS NULL OR locked_until_utc <= $now);
            """;
        command.Parameters.AddWithValue("$minimumSeverity", (int)minimumSeverity);
        command.Parameters.AddWithValue("$now", Format(filteredAtUtc));
        var filtered = command.ExecuteNonQuery();
        transaction.Commit();
        return filtered;
    }

    public SecurityEventOutboxHealth GetHealth()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                SUM(CASE WHEN status = 'Pending' THEN 1 ELSE 0 END),
                SUM(CASE WHEN status = 'Failed' THEN 1 ELSE 0 END),
                SUM(CASE WHEN status = 'Filtered' THEN 1 ELSE 0 END),
                COALESCE(SUM(payload_bytes), 0),
                MIN(CASE WHEN status = 'Pending' THEN created_utc END),
                MAX(CASE WHEN status = 'Delivered' THEN delivered_utc END)
            FROM security_events;
            """;
        using var reader = command.ExecuteReader();
        reader.Read();
        return new SecurityEventOutboxHealth(
            reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
            reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
            reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
            reader.GetInt64(3),
            reader.IsDBNull(4) ? null : Parse(reader.GetString(4)),
            reader.IsDBNull(5) ? null : Parse(reader.GetString(5)));
    }

    private void Initialize()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = FULL;
            CREATE TABLE IF NOT EXISTS security_events (
                event_id TEXT PRIMARY KEY,
                payload_json TEXT NOT NULL,
                payload_bytes INTEGER NOT NULL,
                severity INTEGER NOT NULL,
                status TEXT NOT NULL,
                attempts INTEGER NOT NULL,
                next_attempt_utc TEXT NULL,
                locked_until_utc TEXT NULL,
                last_error TEXT NULL,
                created_utc TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                delivered_utc TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_security_events_delivery
                ON security_events(status, next_attempt_utc, locked_until_utc, created_utc);
            """;
        command.ExecuteNonQuery();

        using var columns = connection.CreateCommand();
        columns.CommandText = "PRAGMA table_info(security_events);";
        using var reader = columns.ExecuteReader();
        var hasSeverity = false;
        while (reader.Read())
            hasSeverity |= string.Equals(reader.GetString(1), "severity", StringComparison.OrdinalIgnoreCase);
        reader.Close();
        if (!hasSeverity)
        {
            using var transaction = connection.BeginTransaction(deferred: false);
            using var migrate = connection.CreateCommand();
            migrate.Transaction = transaction;
            migrate.CommandText =
                "ALTER TABLE security_events ADD COLUMN severity INTEGER NOT NULL DEFAULT 0;";
            migrate.ExecuteNonQuery();

            using var existing = connection.CreateCommand();
            existing.Transaction = transaction;
            existing.CommandText = "SELECT event_id, payload_json FROM security_events;";
            using var existingReader = existing.ExecuteReader();
            var severities = new List<(string EventId, SecurityEventSeverity Severity)>();
            while (existingReader.Read())
            {
                var securityEvent = SecurityEventContract.Deserialize(existingReader.GetString(1));
                severities.Add((existingReader.GetString(0), securityEvent.Severity));
            }
            existingReader.Close();

            foreach (var (eventId, severity) in severities)
            {
                using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText =
                    "UPDATE security_events SET severity = $severity WHERE event_id = $eventId;";
                update.Parameters.AddWithValue("$severity", (int)severity);
                update.Parameters.AddWithValue("$eventId", eventId);
                update.ExecuteNonQuery();
            }
            transaction.Commit();
        }
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout = 5000;";
        command.ExecuteNonQuery();
        return connection;
    }

    private static bool Exists(SqliteConnection connection, SqliteTransaction transaction, Guid eventId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM security_events WHERE event_id = $eventId LIMIT 1;";
        command.Parameters.AddWithValue("$eventId", eventId.ToString("D"));
        return command.ExecuteScalar() is not null;
    }

    private static (int Count, long Bytes) CurrentSize(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COALESCE(SUM(CASE WHEN status = 'Pending' THEN 1 ELSE 0 END), 0),
                   COALESCE(SUM(payload_bytes), 0)
            FROM security_events;
            """;
        using var reader = command.ExecuteReader();
        reader.Read();
        return (reader.GetInt32(0), reader.GetInt64(1));
    }

    private void UpdateMany(
        IEnumerable<Guid> eventIds,
        Action<SqliteConnection, SqliteTransaction, string[]> update)
    {
        ArgumentNullException.ThrowIfNull(eventIds);
        var ids = eventIds.Distinct().Select(id => id.ToString("D")).ToArray();
        if (ids.Length == 0) return;
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: false);
        update(connection, transaction, ids);
        transaction.Commit();
    }

    private static SqliteCommand CreateIdCommand(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<string> ids,
        string commandTemplate)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        var parameters = string.Join(",", ids.Select((_, index) => $"$id{index}"));
        command.CommandText = string.Format(CultureInfo.InvariantCulture, commandTemplate, parameters);
        for (var index = 0; index < ids.Count; index++)
            command.Parameters.AddWithValue($"$id{index}", ids[index]);
        return command;
    }

    private static int GetAttempts(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string eventId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT attempts FROM security_events WHERE event_id = $eventId;";
        command.Parameters.AddWithValue("$eventId", eventId);
        return command.ExecuteScalar() is long attempts ? checked((int)attempts) : 0;
    }

    private void MarkCorrupt(string eventId, DateTimeOffset failedAtUtc)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: false);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE security_events
            SET status = 'Failed', attempts = $attempts, next_attempt_utc = NULL,
                locked_until_utc = NULL, last_error = $error, updated_utc = $now
            WHERE event_id = $eventId;
            """;
        command.Parameters.AddWithValue("$attempts", _options.MaxDeliveryAttempts);
        command.Parameters.AddWithValue("$error", "Stored security event payload is invalid.");
        command.Parameters.AddWithValue("$now", Format(failedAtUtc));
        command.Parameters.AddWithValue("$eventId", eventId);
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private TimeSpan RetryDelay(int attempts)
    {
        var exponential = _options.InitialRetryDelay.TotalMilliseconds
            * Math.Pow(2, Math.Max(0, attempts - 1));
        var capped = Math.Min(_options.MaxRetryDelay.TotalMilliseconds, exponential);
        var jitterFactor = 0.8 + Math.Clamp(_jitter(), 0, 1) * 0.4;
        return TimeSpan.FromMilliseconds(capped * jitterFactor);
    }

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}

public sealed class SqliteSecurityEventOutboxFactory : ISecurityEventOutboxFactory
{
    public ISecurityEventOutbox Create(SecurityEventOutboxOptions options, Func<double>? jitter = null) =>
        new SecurityEventOutbox(options, jitter);
}
