using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core.Portability;
using ETL_SQL.Orchestrator.Storage;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ETL_SQL.Portal.Tests.Data;

public class SharedBackupSurfaceInventoryTests
{
    [Fact]
    public void PortalDbContextTablesMustBeClassified()
    {
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        using var db = new PortalDbContext(options);
        var model = db.Model;

        var tables = model.GetEntityTypes()
            .Select(t => t.GetTableName())
            .Where(t => t != null)
            .Select(t => t!)
            .Distinct()
            .ToList();

        var classifiedSurfaces = SharedBackupSurfaceInventory.Surfaces
            .Where(s => s.Host == SurfaceHost.Portal)
            .Select(s => s.PhysicalName)
            .ToHashSet();

        var unclassified = tables.Where(t => !classifiedSurfaces.Contains(t)).ToList();

        if (unclassified.Any())
        {
            Assert.Fail($"Unclassified Portal tables: {string.Join(", ", unclassified)}");
        }
    }

    [Fact]
    public void OrchestratorTablesMustBeClassified()
    {
        var tables = new[] {
            "ThrottleSlots", "SandboxAdmissions", "SandboxAdmissionPools", "SandboxAdmissionTenantCapacity",
            "TenantMeteringLedger", "Jobs", "Schedules", "Notifications", "JobSchedules", "JobNotifications",
            "OrchestratorObjectAcls", "JobHistory", "JobColumnMetrics", "JobDataQualityFailures", "JobStatementMetrics",
            "TenantUsageRecords", "SharedTenantControlPlanes", "SharedTenantLifecycleOperations",
            "SharedTenantLifecycleFencedJobs", "BundleVersions", "BundleFiles", "BundleDependencies",
            "LineageHistory", "Nodes", "WriteEpochs", "ClusterLocks", "JobState", "HostMetrics",
            "JobHistoryDaily", "HostMetricsDaily"
        };

        var classifiedSurfaces = SharedBackupSurfaceInventory.Surfaces
            .Where(s => s.Host == SurfaceHost.Orchestrator)
            .Select(s => s.PhysicalName)
            .ToHashSet();

        var unclassified = tables.Where(t => !classifiedSurfaces.Contains(t)).ToList();
        if (unclassified.Any())
        {
            Assert.Fail($"Unclassified Orchestrator tables: {string.Join(", ", unclassified)}");
        }
    }

    [Fact]
    public void ArtifactAreasMustBeClassified()
    {
        var areas = new[] { "scripts", "datasets", "snapshots", "maps", "keys", "scratch/spill", "checkpoints", "temporary decrypted content" };

        var classifiedSurfaces = SharedBackupSurfaceInventory.Surfaces
            .Where(s => s.Host == SurfaceHost.Artifact)
            .Select(s => s.PhysicalName)
            .ToHashSet();

        var unclassified = areas.Where(t => !classifiedSurfaces.Contains(t)).ToList();
        if (unclassified.Any())
        {
            Assert.Fail($"Unclassified Artifact areas: {string.Join(", ", unclassified)}");
        }
    }
    [Fact]
    public void UniqueSurfaceIdsAndNoDuplicatePhysicalSurfaces()
    {
        var duplicatesById = SharedBackupSurfaceInventory.Surfaces
            .GroupBy(s => s.SurfaceId)
            .Where(g => g.Count() > 1)
            .ToList();

        Assert.Empty(duplicatesById);

        var duplicatesByPhysical = SharedBackupSurfaceInventory.Surfaces
            .GroupBy(s => new { s.Host, s.PhysicalName })
            .Where(g => g.Count() > 1)
            .ToList();

        Assert.Empty(duplicatesByPhysical);
    }

    [Fact]
    public void TenantOwnedRequiredSurfacesHaveDeclaredDeletionAction()
    {
        var requiredSurfaces = SharedBackupSurfaceInventory.Surfaces
            .Where(s => s.BackupDisposition == SurfaceBackupDisposition.Required)
            .ToList();

        foreach (var surface in requiredSurfaces)
        {
            Assert.NotEqual(SurfaceDeletionDisposition.Retain, surface.DeletionDisposition);
        }
    }

    [Fact]
    public void ProtectedAndEphemeralMaterialCannotBeRestorable()
    {
        var nonRestorable = SharedBackupSurfaceInventory.Surfaces
            .Where(s => s.BackupDisposition == SurfaceBackupDisposition.Excluded &&
                        (s.ExclusionReason.Contains("Protected", System.StringComparison.OrdinalIgnoreCase) ||
                         s.ExclusionReason.Contains("Ephemeral", System.StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var surface in nonRestorable)
        {
            Assert.Equal(SurfaceRestoreDisposition.Exclude, surface.RestoreDisposition);
        }
    }

    // ── Reconciliation against the real schema ────────────────────────────────
    //
    // Everything above checks the inventory against itself — duplicates, classification, disposition
    // consistency — and against a hand-maintained list of table names. None of it ever opened a
    // database, which is how the inventory came to declare tenant columns on tables that did not have
    // them. Backup, restore, portability and tenant-deletion evidence all rest on these declarations,
    // so they are reconciled here against a schema the store actually creates.

    private static async Task<Dictionary<string, HashSet<string>>> ReadOrchestratorSchemaAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"etlsql-surface-recon-{Guid.NewGuid():N}.db");
        try
        {
            var store = new SQLiteJobHistoryStore(path);
            await store.InitializeAsync();

            // The orchestrator database is written by several components, not one. The admission and
            // metering ledgers create their tables lazily on first use, so a harmless read is what
            // materializes them — without this the reconciliation would silently skip exactly the
            // surfaces whose tenant partitioning matters most for metering and capacity.
            var dialect = new SqliteOrchestratorDialect($"Data Source={path}");
            await new RelationalSandboxAdmissionLedger(dialect).ReadAsync("reconciliation-probe");
            await new RelationalTenantMeteringLedger(dialect).ListAsync(
                ETL_SQL.Core.Multitenancy.TenantContext.FromVerifiedCredential("reconciliation-probe"));

            var schema = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();

            var tables = new List<string>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';";
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
            }

            foreach (var table in tables)
            {
                var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using var command = connection.CreateCommand();
                command.CommandText = $"PRAGMA table_info({table});";
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync()) columns.Add(reader.GetString(1));
                schema[table] = columns;
            }
            return schema;
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                try { if (File.Exists(path + suffix)) File.Delete(path + suffix); } catch (IOException) { }
            }
        }
    }

    [Fact]
    public async Task EveryDeclaredOrchestratorSurfaceExistsWithItsAuthoritativeRoot()
    {
        var schema = await ReadOrchestratorSchemaAsync();
        var problems = new List<string>();

        foreach (var surface in SharedBackupSurfaceInventory.Surfaces
            .Where(s => s.Host == SurfaceHost.Orchestrator))
        {
            // Only partitioned surfaces are reconciled. A Global surface has no tenant root to check,
            // and several are created by components a test would otherwise have to stand up whole
            // (the throttle, for one) to observe nothing about tenancy.
            var requiresRoot = surface.PartitionMode is SurfacePartitionMode.DirectTenantColumn
                or SurfacePartitionMode.TenantRootJoin
                or SurfacePartitionMode.Tombstone;
            if (!requiresRoot) continue;

            if (!schema.TryGetValue(surface.PhysicalName, out var columns))
            {
                problems.Add(
                    $"{surface.SurfaceId}: declares tenant-partitioned table '{surface.PhysicalName}', " +
                    "which the orchestrator schema does not contain.");
                continue;
            }

            // A declared root has to be a real column. DirectTenantColumn means the tenant is on the
            // row; TenantRootJoin means this column reaches the row that carries it. Either way the
            // partition is unenforceable if the column is not there.
            if (string.IsNullOrWhiteSpace(surface.AuthoritativeRoot))
                problems.Add($"{surface.SurfaceId}: {surface.PartitionMode} declares no authoritative root.");
            else if (!columns.Contains(surface.AuthoritativeRoot))
                problems.Add(
                    $"{surface.SurfaceId}: declares root '{surface.AuthoritativeRoot}' on '{surface.PhysicalName}', " +
                    $"which has columns [{string.Join(", ", columns.OrderBy(c => c))}].");
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public async Task EveryOrchestratorTableTheStoreCreatesIsClassified()
    {
        var schema = await ReadOrchestratorSchemaAsync();
        var classified = SharedBackupSurfaceInventory.Surfaces
            .Where(s => s.Host == SurfaceHost.Orchestrator)
            .Select(s => s.PhysicalName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Read from the database rather than from a list kept by hand, so adding a table to the store
        // and forgetting its backup/restore/deletion disposition fails here instead of being noticed
        // when a restore turns out to be incomplete.
        var unclassified = schema.Keys.Where(t => !classified.Contains(t)).OrderBy(t => t).ToList();

        Assert.True(
            unclassified.Count == 0,
            "Orchestrator tables with no SharedBackupSurfaceInventory entry: " + string.Join(", ", unclassified));
    }
}
