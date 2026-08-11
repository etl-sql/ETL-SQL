using System;
using System.IO;
using System.Threading.Tasks;
using ETL_SQL.Core.Execution;
using Microsoft.Data.Sqlite;

namespace ETL_SQL.Infrastructure.Sqlite;

public class SqliteOperationLedger : IOperationLedger, IDisposable
{
    private readonly string _dbPath;
    private SqliteConnection? _connection;

    public SqliteOperationLedger(string directory, string sessionId)
    {
        _dbPath = Path.Combine(directory, sessionId, "ledger.db");
    }

    public async Task InitializeAsync()
    {
        var dir = Path.GetDirectoryName(_dbPath);
        if (dir != null && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var builder = new SqliteConnectionStringBuilder { DataSource = _dbPath };
        _connection = new SqliteConnection(builder.ConnectionString);
        await _connection.OpenAsync();

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS operations (
                operation_id TEXT PRIMARY KEY,
                operation_type TEXT,
                status INTEGER,
                exit_code INTEGER,
                error_message TEXT,
                payload TEXT,
                started_at TEXT,
                completed_at TEXT
            );
        ";
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task RecordStartAsync(string operationId, string operationType, string payload)
    {
        if (_connection == null) await InitializeAsync();

        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO operations (operation_id, operation_type, status, payload, started_at)
            VALUES (@id, @type, @status, @payload, @startedAt)
            ON CONFLICT(operation_id) DO UPDATE SET
                status = @status,
                started_at = @startedAt;
        ";
        cmd.Parameters.AddWithValue("@id", operationId);
        cmd.Parameters.AddWithValue("@type", operationType);
        cmd.Parameters.AddWithValue("@status", (int)OperationStatus.Started);
        cmd.Parameters.AddWithValue("@payload", payload);
        cmd.Parameters.AddWithValue("@startedAt", DateTime.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task RecordCompletionAsync(string operationId, int exitCode, string? error)
    {
        if (_connection == null) await InitializeAsync();

        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = @"
            UPDATE operations 
            SET status = @status,
                exit_code = @exitCode,
                error_message = @error,
                completed_at = @completedAt
            WHERE operation_id = @id;
        ";
        cmd.Parameters.AddWithValue("@id", operationId);
        cmd.Parameters.AddWithValue("@status", exitCode == 0 ? (int)OperationStatus.Completed : (int)OperationStatus.Failed);
        cmd.Parameters.AddWithValue("@exitCode", exitCode);
        cmd.Parameters.AddWithValue("@error", error ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@completedAt", DateTime.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<OperationState?> GetStateAsync(string operationId)
    {
        if (_connection == null) await InitializeAsync();

        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT operation_type, status, exit_code, error_message FROM operations WHERE operation_id = @id";
        cmd.Parameters.AddWithValue("@id", operationId);
        using var reader = await cmd.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            return new OperationState
            {
                OperationId = operationId,
                OperationType = reader.GetString(0),
                Status = (OperationStatus)reader.GetInt32(1),
                ExitCode = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                ErrorMessage = reader.IsDBNull(3) ? null : reader.GetString(3)
            };
        }
        return null;
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }
}

public class SqliteOperationLedgerFactory : IOperationLedgerFactory
{
    public IOperationLedger Create(string sessionRoot, string sessionId)
    {
        return new SqliteOperationLedger(sessionRoot, sessionId);
    }
}
