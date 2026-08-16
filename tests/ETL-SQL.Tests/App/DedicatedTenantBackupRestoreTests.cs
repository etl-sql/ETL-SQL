using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.Orchestrator.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ETL_SQL.Tests.CliCommands;

public sealed class DedicatedTenantBackupRestoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"etlsql_dedicated_recovery_{Guid.NewGuid():N}");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public async Task DedicatedRoundTripRequiresTenantAuthorityRestoresKeysAndFencesWork()
    {
        var tenantRoot = await SeedBoundaryAsync("tenant-alpha");
        var output = Path.Combine(_root, "backup");

        var backupExit = await BackupRestoreService.BackupAsync(new CliContext
        {
            BackupTenantRoot = tenantRoot,
            BackupOutputDir = output
        }, NullLogger.Instance);

        Assert.Equal(0, backupExit);
        var dataArchive = Assert.Single(Directory.GetFiles(output, "etl-sql-backup-*.zip"));
        var keysArchive = Assert.Single(Directory.GetFiles(output, "etl-sql-keys-*.zip"));

        Assert.Equal(1, await BackupRestoreService.RestoreAsync(new CliContext
        {
            RestoreFrom = dataArchive,
            RestoreKeys = keysArchive,
            RestoreValidateOnly = true
        }, NullLogger.Instance));
        Assert.Equal(1, await BackupRestoreService.RestoreAsync(new CliContext
        {
            RestoreFrom = dataArchive,
            RestoreKeys = keysArchive,
            RestoreExpectedTenant = "tenant-beta",
            RestoreValidateOnly = true
        }, NullLogger.Instance));

        var restoreRoot = Path.Combine(_root, "restored-alpha");
        var reportPath = Path.Combine(_root, "restore-report.json");
        Assert.Equal(0, await BackupRestoreService.RestoreAsync(new CliContext
        {
            RestoreFrom = dataArchive,
            RestoreKeys = keysArchive,
            RestoreExpectedTenant = "tenant-alpha",
            RestoreTo = restoreRoot,
            RestoreReport = reportPath
        }, NullLogger.Instance));

        Assert.True(File.Exists(Path.Combine(restoreRoot, "Reports", "job.etlsql")));
        Assert.True(File.Exists(Path.Combine(restoreRoot, "data", "datasets", "cache.bin")));
        Assert.True(File.Exists(Path.Combine(restoreRoot, ".portal-keys", "key.xml")));
        Assert.Equal("tenant-alpha",
            (string?)JsonNode.Parse(await File.ReadAllTextAsync(reportPath))?["tenantId"]);

        await using var restored = new SqliteConnection(
            $"Data Source={Path.Combine(restoreRoot, "etlsql.db")}");
        await restored.OpenAsync();
        await using var job = restored.CreateCommand();
        job.CommandText = "SELECT IsEnabled, LeaseOwner, LeaseExpiresAt, LeaseFenceToken, TenantId FROM Jobs WHERE Name = 'nightly';";
        await using var reader = await job.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(0L, reader.GetInt64(0));
        Assert.True(reader.IsDBNull(1));
        Assert.True(reader.IsDBNull(2));
        Assert.True(reader.GetInt64(3) >= 2);
        Assert.Equal("tenant-alpha", reader.GetString(4));
        await reader.DisposeAsync();

        await using var admissions = restored.CreateCommand();
        admissions.CommandText =
            "SELECT AdmissionId, State, LeaseOwner, LeaseExpiresUtc, ReconciliationReason FROM SandboxAdmissions ORDER BY AdmissionId;";
        await using var admissionReader = await admissions.ExecuteReaderAsync();
        Assert.True(await admissionReader.ReadAsync());
        Assert.Equal("active-at-backup", admissionReader.GetString(0));
        Assert.Equal("Retained", admissionReader.GetString(1));
        Assert.True(admissionReader.IsDBNull(2));
        Assert.True(admissionReader.IsDBNull(3));
        Assert.Contains("reconciliation", admissionReader.GetString(4), StringComparison.OrdinalIgnoreCase);
        Assert.True(await admissionReader.ReadAsync());
        Assert.Equal("queued-at-backup", admissionReader.GetString(0));
        Assert.Equal("Cancelled", admissionReader.GetString(1));
    }

    [Fact]
    public async Task DedicatedBackupRefusesExplicitForeignTenantRows()
    {
        var tenantRoot = await SeedBoundaryAsync("tenant-alpha");
        var database = Path.Combine(tenantRoot, "databases", "orchestrator.db");
        await using (var connection = new SqliteConnection($"Data Source={database}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE Jobs SET TenantId = 'tenant-beta' WHERE Name = 'nightly';";
            await command.ExecuteNonQueryAsync();
        }
        SqliteConnection.ClearAllPools();

        var output = Path.Combine(_root, "foreign-backup");
        var exit = await BackupRestoreService.BackupAsync(new CliContext
        {
            BackupTenantRoot = tenantRoot,
            BackupOutputDir = output
        }, NullLogger.Instance);

        Assert.Equal(1, exit);
        Assert.Empty(Directory.GetFiles(output, "*.zip"));
    }

    private async Task<string> SeedBoundaryAsync(string tenant)
    {
        var tenantRoot = Path.Combine(_root, tenant);
        Directory.CreateDirectory(Path.Combine(tenantRoot, "config"));
        Directory.CreateDirectory(Path.Combine(tenantRoot, "databases"));
        Directory.CreateDirectory(Path.Combine(tenantRoot, "artifacts", "scripts"));
        Directory.CreateDirectory(Path.Combine(tenantRoot, "artifacts", "datasets"));
        Directory.CreateDirectory(Path.Combine(tenantRoot, "artifacts", "snapshots"));
        Directory.CreateDirectory(Path.Combine(tenantRoot, "keys", "portal"));

        await File.WriteAllTextAsync(Path.Combine(tenantRoot, "tenant-manifest.json"), $$"""
        { "schemaVersion": "etl-sql.saas-tenant-boundary/v1", "tenantId": "{{tenant}}" }
        """);
        await File.WriteAllTextAsync(Path.Combine(tenantRoot, "config", "appsettings.tenant.json"), $$"""
        {
          "SaasTenant": { "TenantId": "{{tenant}}", "AuthorityMode": "HostFixed" },
          "Portal": {
            "DatabasePath": "../databases/portal.db",
            "ScriptRootPath": "../artifacts/scripts",
            "DatasetRootPath": "../artifacts/datasets",
            "SnapshotDirectory": "../artifacts/snapshots",
            "Storage": { "KeyRingPath": "../keys/portal" }
          },
          "Orchestrator": { "DatabasePath": "../databases/orchestrator.db" }
        }
        """);
        await File.WriteAllTextAsync(Path.Combine(tenantRoot, "artifacts", "scripts", "job.etlsql"), "SELECT 1;");
        await File.WriteAllTextAsync(Path.Combine(tenantRoot, "artifacts", "datasets", "cache.bin"), "encrypted");
        await File.WriteAllTextAsync(Path.Combine(tenantRoot, "keys", "portal", "key.xml"), "<key />");

        await using (var portal = new SqliteConnection(
            $"Data Source={Path.Combine(tenantRoot, "databases", "portal.db")}"))
        {
            await portal.OpenAsync();
            await using var create = portal.CreateCommand();
            create.CommandText = "CREATE TABLE Marker (Id INTEGER PRIMARY KEY);";
            await create.ExecuteNonQueryAsync();
        }

        var store = new SQLiteJobHistoryStore(
            Path.Combine(tenantRoot, "databases", "orchestrator.db"));
        await store.InitializeAsync();
        await store.SaveJobAsync(new JobDefinition(
            "nightly", "SELECT 1;", 1, "DAY", null, null, DateTime.UtcNow,
            TenantId: tenant));
        // Resolved in the job's own tenant — this fixture is about the tenant boundary, so a lookup
        // in the unbound scope would be asking about a different object.
        var nightly = (await store.GetJobAsync(tenant, "nightly"))!;
        Assert.True(await store.AcquireJobLeaseAsync(
            nightly.Id, "source-node", TimeSpan.FromMinutes(5)) > 0);
        var factory = new OrchestratorStoreFactory(new ConfigurationBuilder().Build());
        var ledger = factory.CreateSandboxAdmissionLedger(
            Path.Combine(tenantRoot, "databases", "orchestrator.db"));
        var policy = new ResolvedSandboxAdmissionPolicy
        {
            PoolId = "dedicated-tenant-alpha",
            TenantWeight = 1,
            MaxConcurrentAttempts = 1,
            MaxQueuedAttempts = 2
        };
        var tenantContext = TenantContext.FromHostConfiguration(tenant);
        Assert.True(await ledger.EnqueueAsync("active-at-backup", tenantContext, policy));
        Assert.True(await ledger.TryActivateAsync(
            "active-at-backup", "source-worker", 1, TimeSpan.FromMinutes(5)) > 0);
        Assert.True(await ledger.EnqueueAsync("queued-at-backup", tenantContext, policy));
        SqliteConnection.ClearAllPools();
        return tenantRoot;
    }
}
