using System.Security.Claims;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Core.Storage;
using ETL_SQL.Portal;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ETL_SQL.Review;

public sealed class PortalReproductions
{
    [Fact]
    public async Task SharedStudioSaveMustUseTheTenantRelativeScriptKey()
    {
        var config = new PortalConfig
        {
            ScriptRootPath = Path.Combine(Path.GetTempPath(), "etlsql-review-virtual-root"),
            SharedTenancy = new SharedTenancyConfig { Enabled = true }
        };
        var tenant = TenantContext.FromVerifiedCredential("tenant-alpha");
        var tenantScope = new DatasetTenantScope(config, tenant);
        await using var db = new PortalDbContext(new DbContextOptionsBuilder<PortalDbContext>().UseSqlite("Data Source=:memory:").Options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();
        var user = new PortalUser { Id = 1, UserName = "review", TenantId = tenant.Tenant.Value };
        var folder = new Folder { Id = 1, TenantId = tenant.Tenant.Value, Name = "Review", Path = "/review", OwnerId = 1 };
        var report = new Report
        {
            Id = 1, TenantId = tenant.Tenant.Value, FolderId = 1, Folder = folder, Name = "Review",
            CreatedBy = 1, Kind = CatalogDocumentKind.Pipeline,
            ScriptPath = Path.Combine(config.ScriptRootPath, tenant.Tenant.Value, "studio", "item.etlsql")
        };
        db.Users.Add(user);
        db.Folders.Add(folder);
        db.Reports.Add(report);
        await db.SaveChangesAsync();
        var backend = new InMemoryArtifactStorage();
        var storage = new TenantArtifactStorageFactory(backend).ForTenant(tenant);
        await storage.WriteAllTextAsync(ArtifactArea.Scripts, "studio/item.etlsql", "SELECT 1;");
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "1"), new Claim(ClaimTypes.Role, "Admin")
        }, "review"));
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } };
        var catalog = new PortalTenantCatalogScope(db, tenantScope);
        var save = new ReportScriptSaveService(db, new FolderPermissionService(db, catalog), storage,
            config, new PortalScriptSourceControlService(config), new AuditService(db, accessor, tenantScope), catalog);
        var result = await save.SaveAsync(report.Id, "SELECT 2;", report.Version, principal, 1);
        Assert.Equal(ReportScriptSaveStatus.Saved, result.Status);
        Assert.Equal("SELECT 2;", await storage.ReadAllTextAsync(ArtifactArea.Scripts, "studio/item.etlsql"));
    }
}
