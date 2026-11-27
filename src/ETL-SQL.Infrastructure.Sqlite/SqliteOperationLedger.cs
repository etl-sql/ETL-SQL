using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Execution;
using Microsoft.Data.Sqlite;

namespace ETL_SQL.Infrastructure.Sqlite;

public class SqliteOperationLedger : IOperationLedger, IDisposable
{
    private readonly string _dbPath;
    private SqliteConnection? _connection;

    public SqliteOperationLedger(string directory, string sessionId)
    {
        var resolvedRoot = Path.GetFullPath(directory);
        var candidatePath = Path.Combine(resolvedRoot, sessionId, "ledger.db");
        if (!SafePath.TryResolveWithinRoot(resolvedRoot, candidatePath, out _dbPath))
            throw new ArgumentException("Session ledger path escapes the configured session root.", nameof(sessionId));
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var dir = Path.GetDirectoryName(_dbPath);
        if (dir != null && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var builder = new SqliteConnectionStringBuilder { DataSource = _dbPath };
        _connection = new SqliteConnection(builder.ConnectionString);
        await _connection.OpenAsync(cancellationToken);

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
            CREATE TABLE IF NOT EXISTS tool_approvals (
                tool_alias TEXT PRIMARY KEY COLLATE NOCASE,
                approval TEXT NOT NULL
            );
        ";
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> TryBindToolApprovalAsync(string alias, string? approval, CancellationToken cancellationToken)
    {
        if (_connection == null) await InitializeAsync(cancellationToken);

        // Record a legacy launch too, so a concurrent attempt cannot adopt a pinned contract
        // while an unpinned launch for the same alias is in flight.
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            INSERT INTO tool_approvals (tool_alias, approval)
            SELECT @alias, @approval
            WHERE @legacy OR NOT EXISTS (
                SELECT 1 FROM operations WHERE operation_type = 'ToolExecution'
                    AND payload = @alias COLLATE NOCASE
            )
            ON CONFLICT(tool_alias) DO NOTHING;
            """;
        var binding = approval ?? "legacy";
        cmd.Parameters.AddWithValue("@alias", alias);
        cmd.Parameters.AddWithValue("@approval", binding);
        cmd.Parameters.AddWithValue("@legacy", approval is null);
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        // Bindings are insert-only. Rotation and retries cannot replace the winner.
        cmd.CommandText = "SELECT approval FROM tool_approvals WHERE tool_alias = @alias";
        return (await cmd.ExecuteScalarAsync(cancellationToken) as string) == binding;
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

public sealed class SqliteToolApprovalStore : IToolApprovalStore
{
    public async Task<bool> TryBindAsync(string sessionRoot, string sessionId, string alias, string? approval,
        CancellationToken cancellationToken)
    {
        using var ledger = new SqliteOperationLedger(sessionRoot, sessionId);
        return await ledger.TryBindToolApprovalAsync(alias, approval, cancellationToken);
    }
}

public class SqliteOperationLedgerFactory : IOperationLedgerFactory
{
    public IOperationLedger Create(string sessionRoot, string sessionId)
    {
        return new SqliteOperationLedger(sessionRoot, sessionId);
    }
}
