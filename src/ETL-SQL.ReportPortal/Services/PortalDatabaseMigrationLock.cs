using System.Data;
using ETL_SQL.ReportPortal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.ReportPortal.Services;

/// <summary>
/// Serializes Portal EF migrations at the database session boundary so HA nodes that boot together
/// cannot run the same DDL concurrently.
/// </summary>
internal static class PortalDatabaseMigrationLock
{
    internal const long PostgresAdvisoryLockKey = 772153482002;

    private static readonly SemaphoreSlim SqliteMigrationGate = new(1, 1);

    public static async Task RunExclusiveAsync(
        PortalDbContext db,
        ILogger logger,
        Func<Task> criticalSection,
        CancellationToken ct = default)
    {
        if (IsPostgres(db))
        {
            await RunWithPostgresAdvisoryLockAsync(db, logger, criticalSection, ct);
            return;
        }

        await SqliteMigrationGate.WaitAsync(ct);
        try
        {
            await criticalSection();
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
        CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
            await connection.OpenAsync(ct);

        try
        {
            await ExecuteAdvisoryCommandAsync(db, "SELECT pg_advisory_lock(@key);", ct);
            logger.LogInformation(
                "Acquired Portal database migration advisory lock {LockKey}.",
                PostgresAdvisoryLockKey);

            await criticalSection();
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
}
