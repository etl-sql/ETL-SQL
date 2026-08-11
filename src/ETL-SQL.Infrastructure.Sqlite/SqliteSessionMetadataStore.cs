using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Security;
using ETL_SQL.Data;
using Microsoft.Data.Sqlite;

namespace ETL_SQL.Core.Execution;
/// <summary>
/// A high-performance session metadata store powered by SQLite.
/// Replaces ephemeral JSON snapshots with structured, queryable storage.
/// Data is machine-locked using the provided MachineKey entropy.
/// </summary>
public class SqliteSessionMetadataStore : ISessionMetadataStore
{
    private const int MaxSqliteParametersPerCommand = 900;
    private readonly string _sessionId;
    private readonly string _dbPath;
    private readonly string _entropy;
    private readonly IKeyMaterialProvider? _keyProvider;
    private readonly string _keyScope;
    private SqliteConnection? _connection;

    public SqliteSessionMetadataStore(
        string sessionId,
        string sessionRoot,
        string machineKeyEntropy,
        IKeyMaterialProvider? keyProvider = null,
        string keyScope = "engine-host")
    {
        _sessionId = sessionId;
        var resolvedRoot = Path.GetFullPath(sessionRoot);
        var candidatePath = Path.Combine(resolvedRoot, sessionId, "metadata.db");
        if (!SafePath.TryResolveWithinRoot(resolvedRoot, candidatePath, out _dbPath))
            throw new ArgumentException("Session metadata path escapes the configured session root.", nameof(sessionId));
        _entropy = machineKeyEntropy;
        _keyProvider = keyProvider;
        _keyScope = keyScope;
    }

    public async Task InitializeAsync()
    {
        var dir = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrEmpty(dir))
        {
            await Task.Run(() => Directory.CreateDirectory(dir));
        }

        var builder = new SqliteConnectionStringBuilder { DataSource = _dbPath };
        _connection = new SqliteConnection(builder.ConnectionString);
        await _connection.OpenAsync();

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS variables (
                name TEXT PRIMARY KEY,
                value_json TEXT,
                metadata_json TEXT
            );
            CREATE TABLE IF NOT EXISTS lineage (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                entry_json TEXT
            );
            CREATE TABLE IF NOT EXISTS temp_tables (
                name TEXT PRIMARY KEY,
                schema_json TEXT
            );
            CREATE TABLE IF NOT EXISTS temp_table_chunks (
                table_name TEXT,
                chunk_name TEXT,
                FOREIGN KEY(table_name) REFERENCES temp_tables(name)
            );
            CREATE INDEX IF NOT EXISTS idx_temp_table_chunks_table_name
                ON temp_table_chunks(table_name);
            CREATE TABLE IF NOT EXISTS connections (
                name TEXT PRIMARY KEY,
                info_json TEXT
            );
            CREATE TABLE IF NOT EXISTS docker_state (
                key TEXT PRIMARY KEY,
                value_json TEXT
            );
            CREATE TABLE IF NOT EXISTS tools (
                alias TEXT PRIMARY KEY,
                definition_json TEXT
            );
        ";
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task SaveVariablesAsync(IDictionary<string, object?> variables, IDictionary<string, VariableMetadata> metadata)
    {
        var rows = new List<object?[]>();
        foreach (var kvp in variables)
        {
            var meta = metadata.TryGetValue(kvp.Key, out var m) ? m : new VariableMetadata();
            rows.Add(new object?[]
            {
                kvp.Key,
                await ProtectCheckpointAsync(JsonSerializer.Serialize(kvp.Value)),
                await ProtectCheckpointAsync(JsonSerializer.Serialize(meta))
            });
        }

        using var transaction = _connection!.BeginTransaction();
        await ExecuteBatchedInsertAsync(
            transaction,
            "INSERT OR REPLACE INTO variables (name, value_json, metadata_json) VALUES ",
            rows);
        await transaction.CommitAsync();
    }

    public async Task<(Dictionary<string, object?>, Dictionary<string, VariableMetadata>)> LoadVariablesAsync()
    {
        var variables = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var metadata = new Dictionary<string, VariableMetadata>(StringComparer.OrdinalIgnoreCase);

        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT name, value_json, metadata_json FROM variables";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(0);
            var valJson = await UnprotectCheckpointAsync(reader.GetString(1));
            var metaJson = await UnprotectCheckpointAsync(reader.GetString(2));

            var rawValue = JsonSerializer.Deserialize<object?>(valJson);
            variables[name] = UnmarshalJsonValue(rawValue);
            metadata[name] = JsonSerializer.Deserialize<VariableMetadata>(metaJson) ?? new VariableMetadata();
        }

        return (variables, metadata);
    }

    private object? UnmarshalJsonValue(object? value)
    {
        if (value is JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => element.GetRawText() // Fallback for objects/arrays
            };
        }
        return value;
    }

    public async Task SaveLineageAsync(IEnumerable<LineageEntry> entries)
    {
        using var transaction = _connection!.BeginTransaction();

        using var clearCmd = _connection.CreateCommand();
        clearCmd.Transaction = transaction;
        clearCmd.CommandText = "DELETE FROM lineage";
        await clearCmd.ExecuteNonQueryAsync();

        foreach (var entry in entries)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = "INSERT INTO lineage (entry_json) VALUES (@json)";
            cmd.Parameters.AddWithValue("@json", await ProtectCheckpointAsync(JsonSerializer.Serialize(entry)));
            await cmd.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
    }

    public async Task<IEnumerable<LineageEntry>> LoadLineageAsync()
    {
        var result = new List<LineageEntry>();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT entry_json FROM lineage";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var entry = JsonSerializer.Deserialize<LineageEntry>(await UnprotectCheckpointAsync(reader.GetString(0)));
            if (entry != null) result.Add(entry);
        }
        return result;
    }

    public async Task SaveTempTablesAsync(IEnumerable<SavedTempTable> tables)
    {
        using var transaction = _connection!.BeginTransaction();
        foreach (var table in tables)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = "INSERT OR REPLACE INTO temp_tables (name, schema_json) VALUES (@name, @schema)";
            cmd.Parameters.AddWithValue("@name", table.TableName);
            cmd.Parameters.AddWithValue("@schema", await ProtectCheckpointAsync(JsonSerializer.Serialize(table.Schema)));
            await cmd.ExecuteNonQueryAsync();

            using var clearChunks = _connection.CreateCommand();
            clearChunks.Transaction = transaction;
            clearChunks.CommandText = "DELETE FROM temp_table_chunks WHERE table_name = @name";
            clearChunks.Parameters.AddWithValue("@name", table.TableName);
            await clearChunks.ExecuteNonQueryAsync();

            foreach (var chunk in table.ChunkNames)
            {
                using var chunkCmd = _connection.CreateCommand();
                chunkCmd.Transaction = transaction;
                chunkCmd.CommandText = "INSERT INTO temp_table_chunks (table_name, chunk_name) VALUES (@name, @chunk)";
                chunkCmd.Parameters.AddWithValue("@name", table.TableName);
                chunkCmd.Parameters.AddWithValue("@chunk", await ProtectCheckpointAsync(chunk));
                await chunkCmd.ExecuteNonQueryAsync();
            }
        }
        await transaction.CommitAsync();
    }

    public async Task<IEnumerable<SavedTempTable>> LoadAllTempTablesAsync()
    {
        var tables = new Dictionary<string, TempTableLoadRow>(StringComparer.OrdinalIgnoreCase);

        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = @"
            SELECT t.name, t.schema_json, c.chunk_name
            FROM temp_tables t
            LEFT JOIN temp_table_chunks c ON c.table_name = t.name
            ORDER BY t.name, c.rowid";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(0);
            if (!tables.TryGetValue(name, out var table))
            {
                var schema = JsonSerializer.Deserialize<List<ColumnDefinition>>(
                    await UnprotectCheckpointAsync(reader.GetString(1))) ?? new();
                table = new TempTableLoadRow(schema);
                tables[name] = table;
            }

            if (!reader.IsDBNull(2))
                table.ChunkNames.Add(await UnprotectCheckpointAsync(reader.GetString(2)));
        }

        return tables
            .Select(kvp => new SavedTempTable(kvp.Key, kvp.Value.Schema, kvp.Value.ChunkNames))
            .ToList();
    }

    public async Task SaveConnectionsAsync(IEnumerable<ETL_SQL.Core.Data.ConnectionInfo> connections)
    {
        var rows = new List<object?[]>();
        foreach (var conn in connections)
        {
            var json = JsonSerializer.Serialize(conn);
            var protectedJson = _keyProvider is null
                ? ETL_SQL.Common.CryptoUtils.Protect(json, _entropy)
                : await ProtectCheckpointAsync(json);
            rows.Add(new object?[]
            {
                conn.Name,
                protectedJson
            });
        }

        using var transaction = _connection!.BeginTransaction();

        using var clearCmd = _connection.CreateCommand();
        clearCmd.Transaction = transaction;
        clearCmd.CommandText = "DELETE FROM connections";
        await clearCmd.ExecuteNonQueryAsync();

        await ExecuteBatchedInsertAsync(
            transaction,
            "INSERT INTO connections (name, info_json) VALUES ",
            rows);
        await transaction.CommitAsync();
    }

    public async Task<IEnumerable<ETL_SQL.Core.Data.ConnectionInfo>> LoadConnectionsAsync()
    {
        var result = new List<ETL_SQL.Core.Data.ConnectionInfo>();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT info_json FROM connections";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var protectedJson = reader.GetString(0);
            var json = protectedJson.StartsWith(KeyMaterialEnvelope.Prefix, StringComparison.Ordinal)
                ? await UnprotectCheckpointAsync(protectedJson)
                : ETL_SQL.Common.CryptoUtils.Unprotect(protectedJson, _entropy);
            var info = JsonSerializer.Deserialize<ETL_SQL.Core.Data.ConnectionInfo>(json);
            if (info != null)
            {
                result.Add(info);
            }
        }
        return result;
    }

    public async Task SaveToolDefinitionsAsync(IEnumerable<ETL_SQL.Core.CreateToolStatement> toolDefinitions)
    {
        var rows = new List<object?[]>();
        foreach (var def in toolDefinitions)
        {
            var json = JsonSerializer.Serialize(def);
            var protectedJson = _keyProvider is null
                ? ETL_SQL.Common.CryptoUtils.Protect(json, _entropy)
                : await ProtectCheckpointAsync(json);
            rows.Add(new object?[]
            {
                def.ToolName,
                protectedJson
            });
        }

        using var transaction = _connection!.BeginTransaction();

        using var clearCmd = _connection.CreateCommand();
        clearCmd.Transaction = transaction;
        clearCmd.CommandText = "DELETE FROM tools";
        await clearCmd.ExecuteNonQueryAsync();

        await ExecuteBatchedInsertAsync(
            transaction,
            "INSERT INTO tools (alias, definition_json) VALUES ",
            rows);
        await transaction.CommitAsync();
    }

    public async Task<IEnumerable<ETL_SQL.Core.CreateToolStatement>> LoadToolDefinitionsAsync()
    {
        var result = new List<ETL_SQL.Core.CreateToolStatement>();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT definition_json FROM tools";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var protectedJson = reader.GetString(0);
            var json = protectedJson.StartsWith(KeyMaterialEnvelope.Prefix, StringComparison.Ordinal)
                ? await UnprotectCheckpointAsync(protectedJson)
                : ETL_SQL.Common.CryptoUtils.Unprotect(protectedJson, _entropy);
            var def = JsonSerializer.Deserialize<ETL_SQL.Core.CreateToolStatement>(json);
            if (def != null)
            {
                result.Add(def);
            }
        }
        return result;
    }

    public async Task SaveDockerStateAsync(string? lastConn, IDictionary<string, string> connStrings)
    {
        using var transaction = _connection!.BeginTransaction();

        using var cmd1 = _connection.CreateCommand();
        cmd1.Transaction = transaction;
        cmd1.CommandText = "INSERT OR REPLACE INTO docker_state (key, value_json) VALUES ('last_conn', @val)";
        var lastJson = JsonSerializer.Serialize(lastConn);
        cmd1.Parameters.AddWithValue("@val", _keyProvider is null
            ? lastJson
            : await ProtectCheckpointAsync(lastJson));
        await cmd1.ExecuteNonQueryAsync();

        using var cmd2 = _connection.CreateCommand();
        cmd2.Transaction = transaction;
        cmd2.CommandText = "INSERT OR REPLACE INTO docker_state (key, value_json) VALUES ('conn_strings', @val)";

        var connJson = JsonSerializer.Serialize(connStrings);
        var protectedConnJson = _keyProvider is null
            ? ETL_SQL.Common.CryptoUtils.Protect(connJson, _entropy)
            : await ProtectCheckpointAsync(connJson);
        cmd2.Parameters.AddWithValue("@val", protectedConnJson);
        await cmd2.ExecuteNonQueryAsync();

        await transaction.CommitAsync();
    }

    public async Task<(string? LastConn, Dictionary<string, string> ConnStrings)> LoadDockerStateAsync()
    {
        string? lastConn = null;
        var connStrings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT key, value_json FROM docker_state";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var key = reader.GetString(0);
            var val = reader.GetString(1);
            if (key == "last_conn")
                lastConn = JsonSerializer.Deserialize<string?>(await UnprotectCheckpointAsync(val));
            else if (key == "conn_strings")
            {
                var unprotectedJson = val.StartsWith(KeyMaterialEnvelope.Prefix, StringComparison.Ordinal)
                    ? await UnprotectCheckpointAsync(val)
                    : ETL_SQL.Common.CryptoUtils.Unprotect(val, _entropy);
                connStrings = JsonSerializer.Deserialize<Dictionary<string, string>>(unprotectedJson) ?? new();
            }
        }

        return (lastConn, connStrings);
    }

    private Task<string> ProtectCheckpointAsync(string value) => _keyProvider is null
        ? Task.FromResult(value)
        : KeyMaterialEnvelope.ProtectAsync(
            value, _keyProvider, new KeyMaterialRequest(_keyScope, KeyPurpose.Checkpoint));

    private Task<string> UnprotectCheckpointAsync(string value) =>
        value.StartsWith(KeyMaterialEnvelope.Prefix, StringComparison.Ordinal)
            ? _keyProvider is null
                ? throw new InvalidOperationException(
                    "Checkpoint metadata requires the configured key-material provider.")
                : KeyMaterialEnvelope.UnprotectAsync(
                    value, _keyProvider, _keyScope, KeyPurpose.Checkpoint)
            : Task.FromResult(value);

    public void Dispose()
    {
        if (_connection != null)
        {
            // ClearPool releases the file handle from the connection pool so that
            // the caller can delete the session directory immediately after disposal.
            SqliteConnection.ClearPool(_connection);
            _connection.Dispose();
            _connection = null;
        }
    }

    private sealed record TempTableLoadRow(List<ColumnDefinition> Schema)
    {
        public List<string> ChunkNames { get; } = [];
    }

    private async Task ExecuteBatchedInsertAsync(
        SqliteTransaction transaction,
        string insertPrefix,
        IReadOnlyList<object?[]> rows)
    {
        if (rows.Count == 0)
            return;

        var valuesPerRow = rows[0].Length;
        var rowsPerBatch = Math.Max(1, MaxSqliteParametersPerCommand / valuesPerRow);

        for (var offset = 0; offset < rows.Count; offset += rowsPerBatch)
        {
            var batchSize = Math.Min(rowsPerBatch, rows.Count - offset);
            using var cmd = _connection!.CreateCommand();
            cmd.Transaction = transaction;

            var valueClauses = new List<string>(capacity: batchSize);
            for (var rowIndex = 0; rowIndex < batchSize; rowIndex++)
            {
                var parameterNames = new List<string>(capacity: valuesPerRow);
                var row = rows[offset + rowIndex];
                for (var valueIndex = 0; valueIndex < valuesPerRow; valueIndex++)
                {
                    var parameterName = $"@p{rowIndex}_{valueIndex}";
                    parameterNames.Add(parameterName);
                    cmd.Parameters.AddWithValue(parameterName, row[valueIndex] ?? DBNull.Value);
                }

                valueClauses.Add($"({string.Join(", ", parameterNames)})");
            }

            cmd.CommandText = insertPrefix + string.Join(", ", valueClauses);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}

public sealed class SqliteSessionMetadataStoreFactory(
    IKeyMaterialProvider? keyProvider = null,
    KeyMaterialHostScope? hostScope = null) : ISessionMetadataStoreFactory
{
    public ISessionMetadataStore Create(
        string sessionId,
        string sessionRoot,
        string machineKeyEntropy,
        string? keyScope = null)
    {
        if (hostScope?.RequireExplicitScope == true && string.IsNullOrWhiteSpace(keyScope))
            throw new UnauthorizedAccessException(
                "Shared checkpoint persistence requires an explicit server-derived tenant scope.");
        var resolvedScope = string.IsNullOrWhiteSpace(keyScope)
            ? hostScope?.Value ?? "engine-host"
            : ETL_SQL.Core.Multitenancy.TenantId.FromTrustedSource(keyScope).Value;
        return new SqliteSessionMetadataStore(
            sessionId,
            sessionRoot,
            machineKeyEntropy,
            keyProvider,
            resolvedScope);
    }
}
