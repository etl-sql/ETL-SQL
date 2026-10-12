using System;
using System.Data.Common;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Orchestrator.Storage;
using Microsoft.Data.Sqlite;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Orchestration;

public class OrchestratorSchemaMigrationTests
{
    // Unquoted identifiers match the pre-snake_case store, including PostgreSQL folding.
    internal const string LegacyHistorySql = """
        CREATE TABLE JobHistory (Id INTEGER PRIMARY KEY, JobId TEXT, JobName TEXT,
            StartTime TEXT, EndTime TEXT, Status TEXT, RowsProcessed INTEGER);
        INSERT INTO JobHistory VALUES (7, 'job-1', 'retained-job', '2026-10-01',
            '2026-10-02', 'SUCCESS', 42);
        """;

    internal const string LegacyJobsSql = """
        CREATE TABLE Jobs (
            Id TEXT PRIMARY KEY, Name TEXT NOT NULL, Script TEXT NOT NULL, Interval INTEGER NOT NULL,
            Unit TEXT NOT NULL, AtTime TEXT, LastRun TEXT, NextRun TEXT, IsEnabled INTEGER NOT NULL,
            MaxRetries INTEGER NOT NULL, RetryDelaySeconds INTEGER NOT NULL, Version INTEGER NOT NULL,
            LeaseFenceToken INTEGER NOT NULL, JobType TEXT NOT NULL, TargetPath TEXT, DisplayName TEXT,
            Description TEXT, Options TEXT, CreatedBy TEXT, ModifiedBy TEXT, TenantId TEXT NOT NULL,
            ScriptHash TEXT, HashPolicy TEXT, LeaseOwner TEXT, LeaseExpiresAt TEXT,
            UNIQUE (TenantId, Name));
        INSERT INTO Jobs (Id, Name, Script, Interval, Unit, IsEnabled, MaxRetries, RetryDelaySeconds,
            Version, LeaseFenceToken, JobType, TenantId, ScriptHash, HashPolicy)
        VALUES ('job-1', 'retained-job', 'SELECT 1;', 2, 'HOUR', 1, 3, 45, 7, 9, 'Script',
            'tenant-a', 'retained-hash', 'Strict');
        """;

    internal static async Task VerifyLegacyStoreAsync(IOrchestratorStoreDialect dialect)
    {
        var store = new RelationalJobHistoryStore(dialect);
        await store.InitializeAsync();
        var job = await store.GetJobAsync("tenant-a", "retained-job");
        Assert.NotNull(job);
        Assert.Equal("job-1", job.Id.Value);
        Assert.Equal("SELECT 1;", job.Script);
        Assert.Equal(2, job.Interval);
        Assert.Equal(3, job.MaxRetries);
        Assert.Equal(7L, job.Version);
        Assert.Equal("retained-hash", job.ScriptHash);
        Assert.Equal("Strict", job.HashPolicy);
        Assert.Null(await store.GetJobAsync("tenant-b", "retained-job"));
    }

    [Fact]
    public async Task StoreBootstrap_PreservesLegacyJobAndTenantBinding()
    {
        var connectionString = $"Data Source=migration-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, LegacyJobsSql);
        await VerifyLegacyStoreAsync(new SqliteOrchestratorDialect(connectionString));
    }

    [Fact]
    public async Task Migration_RollsBackEarlierRenamesWhenLaterDdlFails()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await ExecuteAsync(connection, LegacyHistorySql);
        var dialect = new Mock<IOrchestratorStoreDialect>();
        dialect.Setup(value => value.GetTableNamesAsync(connection, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>(StringComparer.Ordinal) { "JobHistory" });
        // Force failure after the table and its first column have already been renamed.
        dialect.Setup(value => value.GetExactColumnNamesAsync(connection, "JobHistory", It.IsAny<CancellationToken>()))
            .ReturnsAsync(["JobId", "MissingColumn"]);
        await Assert.ThrowsAsync<SqliteException>(() => OrchestratorSchemaMigration.MigrateLegacyTableAsync(
            dialect.Object, connection, "JobHistory", "job_history"));
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT JobId FROM JobHistory WHERE Id = 7;";
        Assert.Equal("job-1", await command.ExecuteScalarAsync());
        var tables = await new SqliteOrchestratorDialect("Data Source=:memory:").GetTableNamesAsync(connection);
        Assert.Contains("JobHistory", tables);
        Assert.DoesNotContain("job_history", tables);
        Assert.DoesNotContain("_tmp_snake_job_history", tables);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Migration_PreservesLegacyRowsAndIsRepeatable(bool folded)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await ExecuteAsync(connection, folded
            ? LegacyHistorySql.Replace("JobHistory", "jobhistory").Replace("JobId", "jobid")
                .Replace("JobName", "jobname").Replace("StartTime", "starttime")
                .Replace("EndTime", "endtime").Replace("RowsProcessed", "rowsprocessed")
            : LegacyHistorySql);
        await VerifyHistoryMigrationAsync(new SqliteOrchestratorDialect(":memory:"), connection);
    }

    internal static async Task VerifyHistoryMigrationAsync(IOrchestratorStoreDialect dialect, DbConnection connection)
    {
        await OrchestratorSchemaMigration.MigrateLegacyTableAsync(dialect, connection, "JobHistory", "job_history");
        await OrchestratorSchemaMigration.MigrateLegacyTableAsync(dialect, connection, "JobHistory", "job_history");
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, job_id, job_name, start_time, end_time, status, rows_processed FROM job_history;";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(7, Convert.ToInt32(reader.GetValue(0)));
        Assert.Equal("job-1", reader.GetString(1));
        Assert.Equal("retained-job", reader.GetString(2));
        Assert.Equal("2026-10-01", reader.GetString(3));
        Assert.Equal("2026-10-02", reader.GetString(4));
        Assert.Equal("SUCCESS", reader.GetString(5));
        Assert.Equal(42, Convert.ToInt32(reader.GetValue(6)));
        Assert.False(await reader.ReadAsync());
    }

    [Fact]
    public async Task Migration_ColumnCollisionFailsWithoutChangingLegacyData()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await ExecuteAsync(connection, "CREATE TABLE JobHistory (JobId TEXT, job_id TEXT); INSERT INTO JobHistory VALUES ('old', 'new');");
        await Assert.ThrowsAnyAsync<Exception>(() => OrchestratorSchemaMigration.MigrateLegacyTableAsync(
            new SqliteOrchestratorDialect(":memory:"), connection, "JobHistory", "job_history"));
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT JobId, job_id FROM JobHistory;";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("old", reader.GetString(0));
        Assert.Equal("new", reader.GetString(1));
    }

    [Fact]
    public async Task Migration_TableCollisionMustNotHideLegacyRows()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await ExecuteAsync(connection, LegacyHistorySql + "CREATE TABLE job_history (id INTEGER);");
        await Assert.ThrowsAsync<InvalidOperationException>(() => OrchestratorSchemaMigration.MigrateLegacyTableAsync(
            new SqliteOrchestratorDialect(":memory:"), connection, "JobHistory", "job_history"));
    }

    [Fact]
    public async Task Migration_PropagatesCancellation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => OrchestratorSchemaMigration.MigrateLegacyTableAsync(
            new SqliteOrchestratorDialect(":memory:"), connection, "JobHistory", "job_history", cancellation.Token));
    }

    [Fact]
    public async Task Migration_RefusesOrphanFromAnInterruptedLegacyMigration()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await ExecuteAsync(connection, LegacyHistorySql.Replace("JobHistory", "_tmp_snake_job_history"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => OrchestratorSchemaMigration.MigrateLegacyTableAsync(
            new SqliteOrchestratorDialect("Data Source=:memory:"), connection, "JobHistory", "job_history"));
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT RowsProcessed FROM _tmp_snake_job_history WHERE Id = 7;";
        Assert.Equal(42L, await command.ExecuteScalarAsync());
    }

    private static async Task ExecuteAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
