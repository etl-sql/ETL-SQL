using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Data;
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
    private readonly string _sessionId;
    private readonly string _dbPath;
    private readonly string _entropy;
    private SqliteConnection? _connection;

    public SqliteSessionMetadataStore(string sessionId, string sessionRoot, string machineKeyEntropy)
    {
        _sessionId = sessionId;
        var resolvedRoot = Path.GetFullPath(sessionRoot);
        var candidatePath = Path.Combine(resolvedRoot, sessionId, "metadata.db");
        if (!SafePath.TryResolveWithinRoot(resolvedRoot, candidatePath, out _dbPath))
            throw new ArgumentException("Session metadata path escapes the configured session root.", nameof(sessionId));
        _entropy = machineKeyEntropy;
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
            CREATE TABLE IF NOT EXISTS connections (
                name TEXT PRIMARY KEY,
                info_json TEXT
            );
            CREATE TABLE IF NOT EXISTS docker_state (
                key TEXT PRIMARY KEY,
                value_json TEXT
            );
        ";
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task SaveVariablesAsync(IDictionary<string, object?> variables, IDictionary<string, VariableMetadata> metadata)
    {
        using var transaction = _connection!.BeginTransaction();
        foreach (var kvp in variables)
        {
            var meta = metadata.TryGetValue(kvp.Key, out var m) ? m : new VariableMetadata();
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = "INSERT OR REPLACE INTO variables (name, value_json, metadata_json) VALUES (@name, @val, @meta)";
            cmd.Parameters.AddWithValue("@name", kvp.Key);
            cmd.Parameters.AddWithValue("@val", JsonSerializer.Serialize(kvp.Value));
            cmd.Parameters.AddWithValue("@meta", JsonSerializer.Serialize(meta));
            await cmd.ExecuteNonQueryAsync();
        }
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
            var valJson = reader.GetString(1);
            var metaJson = reader.GetString(2);

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
            cmd.Parameters.AddWithValue("@json", JsonSerializer.Serialize(entry));
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
            var entry = JsonSerializer.Deserialize<LineageEntry>(reader.GetString(0));
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
            cmd.Parameters.AddWithValue("@schema", JsonSerializer.Serialize(table.Schema));
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
                chunkCmd.Parameters.AddWithValue("@chunk", chunk);
                await chunkCmd.ExecuteNonQueryAsync();
            }
        }
        await transaction.CommitAsync();
    }

    public async Task<IEnumerable<SavedTempTable>> LoadAllTempTablesAsync()
    {
        var result = new List<SavedTempTable>();
        var tableRows = new List<(string Name, string SchemaJson)>();

        using (var cmd = _connection!.CreateCommand())
        {
            cmd.CommandText = "SELECT name, schema_json FROM temp_tables";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                tableRows.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        foreach (var row in tableRows)
        {
            var name = row.Name;
            var schema = JsonSerializer.Deserialize<List<ColumnDefinition>>(row.SchemaJson) ?? new();

            var chunks = new List<string>();
            using (var chunkCmd = _connection!.CreateCommand())
            {
                chunkCmd.CommandText = "SELECT chunk_name FROM temp_table_chunks WHERE table_name = @name";
                chunkCmd.Parameters.AddWithValue("@name", name);
                using var chunkReader = await chunkCmd.ExecuteReaderAsync();
                while (await chunkReader.ReadAsync())
                {
                    chunks.Add(chunkReader.GetString(0));
                }
            }

            result.Add(new SavedTempTable(name, schema, chunks));
        }

        return result;
    }

    public async Task SaveConnectionsAsync(IEnumerable<ETL_SQL.Core.Data.ConnectionInfo> connections)
    {
        using var transaction = _connection!.BeginTransaction();

        using var clearCmd = _connection.CreateCommand();
        clearCmd.Transaction = transaction;
        clearCmd.CommandText = "DELETE FROM connections";
        await clearCmd.ExecuteNonQueryAsync();

        foreach (var conn in connections)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = "INSERT INTO connections (name, info_json) VALUES (@name, @json)";
            cmd.Parameters.AddWithValue("@name", conn.Name);

            var json = JsonSerializer.Serialize(conn);
            var protectedJson = ETL_SQL.Common.CryptoUtils.Protect(json, _entropy);
            cmd.Parameters.AddWithValue("@json", protectedJson);

            await cmd.ExecuteNonQueryAsync();
        }
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
            var json = ETL_SQL.Common.CryptoUtils.Unprotect(protectedJson, _entropy);
            var info = JsonSerializer.Deserialize<ETL_SQL.Core.Data.ConnectionInfo>(json);
            if (info != null)
            {
                result.Add(info);
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
        cmd1.Parameters.AddWithValue("@val", JsonSerializer.Serialize(lastConn));
        await cmd1.ExecuteNonQueryAsync();

        using var cmd2 = _connection.CreateCommand();
        cmd2.Transaction = transaction;
        cmd2.CommandText = "INSERT OR REPLACE INTO docker_state (key, value_json) VALUES ('conn_strings', @val)";

        var connJson = JsonSerializer.Serialize(connStrings);
        var protectedConnJson = ETL_SQL.Common.CryptoUtils.Protect(connJson, _entropy);
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
            if (key == "last_conn") lastConn = JsonSerializer.Deserialize<string?>(val);
            else if (key == "conn_strings")
            {
                var unprotectedJson = ETL_SQL.Common.CryptoUtils.Unprotect(val, _entropy);
                connStrings = JsonSerializer.Deserialize<Dictionary<string, string>>(unprotectedJson) ?? new();
            }
        }

        return (lastConn, connStrings);
    }

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
}
