using System.Text.Json;
using ETL_SQL.Core.Governance;
using Microsoft.Data.Sqlite;

namespace ETL_SQL.Infrastructure.Sqlite;

/// <summary>Incremental, indexed receipts. One store is owned by one Gateway daemon.</summary>
public sealed class SqliteGatewayOutcomeStore : IGatewayOutcomeStore, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FileStream _ownership;
    private readonly GatewayOutcomeRetentionOptions _options;
    private readonly TimeProvider _time;
    private int _transitions;

    public SqliteGatewayOutcomeStore(string path, GatewayOutcomeRetentionOptions? options = null, TimeProvider? timeProvider = null)
    {
        if (!Path.IsPathFullyQualified(path))
            throw new ArgumentException("The Gateway outcome store path must be absolute.", nameof(path));
        _options = options ?? new();
        _options.Validate();
        _time = timeProvider ?? TimeProvider.System;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        try
        {
            _ownership = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            _connection.Open();
            Execute("""
                PRAGMA auto_vacuum=INCREMENTAL;
                PRAGMA journal_mode=WAL;
                PRAGMA synchronous=FULL;
                CREATE TABLE IF NOT EXISTS Outcomes (
                    TenantId TEXT NOT NULL, OperationId TEXT NOT NULL,
                    State INTEGER NOT NULL, Effect INTEGER NOT NULL,
                    UpdatedUtc INTEGER NOT NULL, Compacted INTEGER NOT NULL DEFAULT 0,
                    Payload TEXT NOT NULL, PRIMARY KEY (TenantId, OperationId));
                CREATE INDEX IF NOT EXISTS IX_Outcomes_ReadRetention ON Outcomes(UpdatedUtc) WHERE Effect=0;
                CREATE INDEX IF NOT EXISTS IX_Outcomes_DetailRetention ON Outcomes(UpdatedUtc) WHERE Effect=1 AND Compacted=0 AND State IN (1,2);
                CREATE INDEX IF NOT EXISTS IX_Outcomes_Interrupted ON Outcomes(State) WHERE Effect=1 AND State=0;
                CREATE INDEX IF NOT EXISTS IX_Outcomes_Ambiguous ON Outcomes(TenantId, Effect, State);
                CREATE TABLE IF NOT EXISTS StoreMetadata (Name TEXT PRIMARY KEY);
                UPDATE Outcomes SET State=3, Payload=json_set(Payload, '$.State', 3) WHERE Effect=1 AND State=0;
                """);
            Maintain();
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or GatewayProtocolException)
        {
            _connection.Dispose();
            _ownership?.Dispose();
            throw Refused();
        }
    }

    /// <summary>Streams the old JSON array once. The import marker and receipts commit together.</summary>
    public async Task ImportLegacyAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            using var check = _connection.CreateCommand();
            check.CommandText = "SELECT COUNT(*) FROM StoreMetadata WHERE Name='legacy-imported'";
            if (Convert.ToInt64(await check.ExecuteScalarAsync(cancellationToken)) != 0) return;
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var transaction = _connection.BeginTransaction();
            await foreach (var item in JsonSerializer.DeserializeAsyncEnumerable<GatewayOperationOutcome>(stream, cancellationToken: cancellationToken))
            {
                if (item is null || string.IsNullOrWhiteSpace(item.TenantId) || string.IsNullOrWhiteSpace(item.OperationId))
                    throw new JsonException("Invalid outcome record.");
                var restored = item.Effect == GatewayOperationEffect.Mutating && item.State == GatewayOutcomeState.InFlight
                    ? item with { State = GatewayOutcomeState.Ambiguous } : item;
                SaveCore(restored, transaction, insertOnly: true);
            }
            using var marker = _connection.CreateCommand();
            marker.Transaction = transaction;
            marker.CommandText = "INSERT INTO StoreMetadata(Name) VALUES ('legacy-imported')";
            await marker.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (FileNotFoundException)
        {
            // A new Gateway has no legacy ledger. Access errors must never be treated as absence.
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or JsonException)
        {
            throw Refused();
        }
    }

    public GatewayOperationOutcome? Find(string tenantId, string operationId)
    {
        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT Payload FROM Outcomes WHERE TenantId=$tenant AND OperationId=$operation";
            command.Parameters.AddWithValue("$tenant", tenantId);
            command.Parameters.AddWithValue("$operation", operationId);
            return command.ExecuteScalar() is string json
                ? JsonSerializer.Deserialize<GatewayOperationOutcome>(json) ?? throw Refused() : null;
        }
        catch (Exception ex) when (ex is SqliteException or JsonException) { throw Refused(); }
    }

    public void Save(GatewayOperationOutcome outcome)
    {
        try
        {
            SaveCore(outcome);
            if (++_transitions >= _options.MaintenanceEveryTransitions)
            {
                Maintain();
                _transitions = 0;
            }
        }
        catch (SqliteException) { throw Refused(); }
    }

    private void SaveCore(GatewayOperationOutcome outcome, SqliteTransaction? transaction = null, bool insertOnly = false)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Outcomes(TenantId, OperationId, State, Effect, UpdatedUtc, Payload)
            VALUES ($tenant, $operation, $state, $effect, $updated, $payload)
            """ + (insertOnly ? " ON CONFLICT(TenantId, OperationId) DO NOTHING" : """
             ON CONFLICT(TenantId, OperationId) DO UPDATE SET
                State=excluded.State, Effect=excluded.Effect, UpdatedUtc=excluded.UpdatedUtc,
                Payload=excluded.Payload, Compacted=0
            """);
        command.Parameters.AddWithValue("$tenant", outcome.TenantId);
        command.Parameters.AddWithValue("$operation", outcome.OperationId);
        command.Parameters.AddWithValue("$state", (int)outcome.State);
        command.Parameters.AddWithValue("$effect", (int)outcome.Effect);
        command.Parameters.AddWithValue("$updated", _time.GetUtcNow().UtcTicks);
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(outcome));
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<GatewayOperationOutcome> ListAmbiguousMutating(string tenantId)
    {
        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT Payload FROM Outcomes WHERE TenantId=$tenant AND Effect=1 AND State=3";
            command.Parameters.AddWithValue("$tenant", tenantId);
            using var reader = command.ExecuteReader();
            var outcomes = new List<GatewayOperationOutcome>();
            while (reader.Read()) outcomes.Add(JsonSerializer.Deserialize<GatewayOperationOutcome>(reader.GetString(0))!);
            return outcomes;
        }
        catch (Exception ex) when (ex is SqliteException or JsonException) { throw Refused(); }
    }

    /// <summary>Bounded maintenance. Mutating identities, decisions and triage metadata are never deleted.</summary>
    public void Maintain()
    {
        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                DELETE FROM Outcomes WHERE rowid IN (
                    SELECT rowid FROM Outcomes WHERE Effect=0 AND UpdatedUtc < $readCutoff LIMIT $batch);
                UPDATE Outcomes SET Payload=json_set(Payload, '$.Detail', NULL), Compacted=1 WHERE rowid IN (
                    SELECT rowid FROM Outcomes WHERE Effect=1 AND Compacted=0 AND UpdatedUtc < $detailCutoff AND State IN (1,2) LIMIT $batch);
                """;
            command.Parameters.AddWithValue("$readCutoff", _time.GetUtcNow().AddDays(-_options.ReadOnlyRetentionDays).UtcTicks);
            command.Parameters.AddWithValue("$detailCutoff", _time.GetUtcNow().AddDays(-_options.ReceiptDetailRetentionDays).UtcTicks);
            command.Parameters.AddWithValue("$batch", _options.MaintenanceBatchSize);
            command.ExecuteNonQuery();
            if (_options.IncrementalVacuumPages > 0)
                Execute($"PRAGMA incremental_vacuum({_options.IncrementalVacuumPages});");
            Execute("PRAGMA wal_checkpoint(PASSIVE);");
        }
        catch (SqliteException) { throw Refused(); }
    }

    private void Execute(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static GatewayProtocolException Refused() => new(
        "The durable Gateway outcome store could not be read or updated; execution is refused to prevent unsafe replay.");

    public void Dispose()
    {
        _connection.Dispose();
        _ownership.Dispose();
    }
}
