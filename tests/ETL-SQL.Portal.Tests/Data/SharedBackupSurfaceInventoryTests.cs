using ETL_SQL.Core.Portability;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;
using ETL_SQL.Orchestrator.Storage;
using System.Linq;

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
}
