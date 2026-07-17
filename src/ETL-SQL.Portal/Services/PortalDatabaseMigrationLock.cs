using System.Data;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Serializes Portal EF migrations at the database session boundary so HA nodes that boot together
/// cannot run the same DDL concurrently.
/// </summary>
internal static class PortalDatabaseMigrationLock
{
    internal const long PostgresAdvisoryLockKey = 772153482002;

    private static readonly SemaphoreSlim SqliteMigrationGate = new(1, 1);
    private static readonly object StatusSync = new();

    internal static PortalMigrationStatus CurrentStatus { get; private set; } = PortalMigrationStatus.Idle;

    internal static void ReportProgress(string stage, int? pendingMigrations = null) =>
        UpdateStatus(CurrentStatus with
        {
            State = stage,
            PendingMigrations = pendingMigrations ?? CurrentStatus.PendingMigrations,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });

    public static async Task RunExclusiveAsync(
        PortalDbContext db,
        ILogger logger,
        Func<Task> criticalSection,
        string? ownerNodeId = null,
        CancellationToken ct = default)
    {
        var provider = IsPostgres(db) ? "Postgres" : "Sqlite";
        var now = DateTimeOffset.UtcNow;
        UpdateStatus(new PortalMigrationStatus(
            "Waiting", ownerNodeId, provider, IsPostgres(db) ? "PostgresAdvisoryLock" : "ProcessSemaphore",
            IsPostgres(db) ? PostgresAdvisoryLockKey : null,
            now, null, null, now, null, null));
        if (IsPostgres(db))
        {
            await RunWithPostgresAdvisoryLockAsync(db, logger, criticalSection, ownerNodeId, ct);
            return;
        }

        await SqliteMigrationGate.WaitAsync(ct);
        try
        {
            UpdateStatus(CurrentStatus with
            {
                State = "Acquired",
                AcquiredAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
            await criticalSection();
            UpdateStatus(CurrentStatus with
            {
                State = "Succeeded",
                CompletedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            UpdateStatus(CurrentStatus with
            {
                State = "Failed",
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Error = ex.Message,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
            throw;
        }
        finally
        {
            SqliteMigrationGate.Release();
        }
    }

    internal static bool IsPostgres(PortalDbContext db) =>
        (db.Database.ProviderName ?? string.Empty)
            .Contains("Npgsql", StringComparison.OrdinalIgnoreCase);

    private static async Task RunWithPostgresAdvisoryLockAsync(
        PortalDbContext db,
        ILogger logger,
        Func<Task> criticalSection,
        string? ownerNodeId,
        CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
            await connection.OpenAsync(ct);

        try
        {
            await ExecuteAdvisoryCommandAsync(db, "SELECT pg_advisory_lock(@key);", ct);
            UpdateStatus(CurrentStatus with
            {
                State = "Acquired",
                OwnerNodeId = ownerNodeId,
                AcquiredAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
            logger.LogInformation(
                "Acquired Portal database migration advisory lock {LockKey}.",
                PostgresAdvisoryLockKey);

            await criticalSection();
            UpdateStatus(CurrentStatus with
            {
                State = "Succeeded",
                CompletedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            UpdateStatus(CurrentStatus with
            {
                State = "Failed",
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Error = ex.Message,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
            throw;
        }
        finally
        {
            try
            {
                await ExecuteAdvisoryCommandAsync(db, "SELECT pg_advisory_unlock(@key);", CancellationToken.None);
                logger.LogInformation(
                    "Released Portal database migration advisory lock {LockKey}.",
                    PostgresAdvisoryLockKey);
            }
            finally
            {
                if (openedHere)
                    await connection.CloseAsync();
            }
        }
    }

    private static async Task ExecuteAdvisoryCommandAsync(PortalDbContext db, string sql, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@key";
        parameter.Value = PostgresAdvisoryLockKey;
        command.Parameters.Add(parameter);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static void UpdateStatus(PortalMigrationStatus status)
    {
        lock (StatusSync) CurrentStatus = status;
    }
}

internal sealed record PortalMigrationStatus(
    string State,
    string? OwnerNodeId,
    string? Provider,
    string? LockKind,
    long? LockKey,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? AcquiredAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    int? PendingMigrations,
    string? Error)
{
    public static PortalMigrationStatus Idle { get; } = new(
        "Idle", null, null, null, null, null, null, null, DateTimeOffset.UtcNow, null, null);
}
