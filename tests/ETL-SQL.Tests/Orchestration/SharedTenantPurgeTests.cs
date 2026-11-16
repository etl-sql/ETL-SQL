using ETL_SQL.Core.Data;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Orchestrator.Storage;
using Microsoft.Data.Sqlite;

namespace ETL_SQL.Tests.Orchestration;

/// <summary>
/// Deleting a Shared tenant purges everything partitioned by its tenant id, and nothing of another
/// tenant's. Rows the purge leaves behind outlive the tenant and resurface for a re-provisioned
/// tenant of the same id.
/// </summary>
public sealed class SharedTenantPurgeTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-01T12:00:00Z");
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"tenant-purge-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public async Task Delete_PurgesTheTenantsLineageBundlesAndFences_AndLeavesOtherTenantsAlone()
    {
        var store = new SQLiteJobHistoryStore(_path);
        await store.InitializeAsync();
        var doomed = TenantContext.FromVerifiedCredential("tenant-a");
        var neighbour = TenantContext.FromVerifiedCredential("tenant-b");
        await store.ApplySharedTenantLifecycleAsync(doomed, Command("provision-a", SharedTenantLifecycleKind.Provision));

        foreach (var tenant in new[] { doomed, neighbour })
        {
            await store.SaveLineageAsync(tenant,
                [new LineageEntry("warehouse.orders", "INSERT") { SourceTables = ["stage.orders"] }],
                "load", null, Now.UtcDateTime);
            await SeedAsync(tenant.Tenant.Value);
        }

        var result = await store.ApplySharedTenantLifecycleAsync(doomed, Command("delete-a", SharedTenantLifecycleKind.Delete));
        Assert.Equal("Completed", result.Status);

        foreach (var table in PartitionedTables)
        {
            Assert.True(await CountAsync(table, "tenant-a") == 0, $"{table} still holds the deleted tenant's rows.");
            Assert.True(await CountAsync(table, "tenant-b") > 0, $"{table} lost the neighbouring tenant's rows.");
        }
    }

    private static readonly string[] PartitionedTables =
    [
        "lineage_history", "bundle_versions", "bundle_files", "bundle_dependencies",
        "tenant_portability_cutovers", "tenant_portability_fenced_jobs", "shared_tenant_lifecycle_fenced_jobs"
    ];

    private static SharedTenantLifecycleCommand Command(string operationId, SharedTenantLifecycleKind kind) =>
        new(operationId, kind, "platform-operator", $"change-{operationId}", "0.22.0", 1, 128, 1, Now);

    private async Task SeedAsync(string tenant)
    {
        await using var connection = new SqliteConnection($"Data Source={_path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO bundle_versions (tenant_id, bundle_name, version, entry_path, content_hash, published_at)
                 VALUES ($t, 'b', 1, 'main.etlsql', 'h', '2026-11-01T00:00:00Z');
            INSERT INTO bundle_files (tenant_id, bundle_name, version, virtual_path, content, content_hash, size_bytes, content_type)
                 VALUES ($t, 'b', 1, 'main.etlsql', 'SELECT 1;', 'h', 9, 'text/plain');
            INSERT INTO bundle_dependencies (tenant_id, bundle_name, version, from_path, to_path)
                 VALUES ($t, 'b', 1, 'main.etlsql', 'lib.etlsql');
            INSERT INTO tenant_portability_cutovers (tenant_id, operation_id, authority, fence_epoch,
                        source_schedules_enabled, target_schedules_enabled, source_active_executions, updated_at_utc)
                 VALUES ($t, 'cutover-' || $t, 'source', 1, 0, 1, 0, '2026-11-01T00:00:00Z');
            INSERT INTO tenant_portability_fenced_jobs (tenant_id, operation_id, job_name)
                 VALUES ($t, 'cutover-' || $t, 'load');
            INSERT INTO shared_tenant_lifecycle_fenced_jobs (operation_id, tenant_id, job_name)
                 VALUES ('fence-' || $t, $t, 'load');";
        command.Parameters.AddWithValue("$t", tenant);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> CountAsync(string table, string tenant)
    {
        await using var connection = new SqliteConnection($"Data Source={_path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table} WHERE tenant_id = $t;";
        command.Parameters.AddWithValue("$t", tenant);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
