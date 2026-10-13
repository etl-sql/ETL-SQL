using System.Security.Claims;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Core.Storage;
using ETL_SQL.Portal.Controllers;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Models;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Portal")]
public sealed class StudioCatalogPagingTests
{
    [Fact]
    public async Task SearchAndSummaryPreserveTenantAndRankedAuthorPermissions()
    {
        var config = new PortalConfig { SharedTenancy = new SharedTenancyConfig { Enabled = true } };
        var tenant = TenantContext.FromVerifiedCredential("tenant-alpha");
        var tenantScope = new DatasetTenantScope(config, tenant);
        await using var db = new PortalDbContext(new DbContextOptionsBuilder<PortalDbContext>().UseSqlite("Data Source=:memory:").Options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();
        db.Users.AddRange(new PortalUser { Id = 1, UserName = "author", TenantId = "tenant-alpha" },
            new PortalUser { Id = 2, UserName = "owner", TenantId = "tenant-alpha" });
        var folder = new Folder { Id = 1, Name = "Local", Path = "/Local", OwnerId = 2, TenantId = "tenant-alpha" };
        var foreign = new Folder { Id = 2, Name = "Foreign", Path = "/Foreign", OwnerId = 1, TenantId = "tenant-beta" };
        db.Folders.AddRange(folder, foreign);
        var reports = Enumerable.Range(1, 5).Select(id => new Report
        {
            Id = id,
            Name = $"Document{id}",
            FolderId = id == 5 ? 2 : 1,
            TenantId = id == 5 ? "tenant-beta" : "tenant-alpha",
            CreatedBy = id is 3 or 4 ? 1 : 2
        }).ToArray();
        db.Reports.AddRange(reports);
        db.ReportAcls.AddRange(
            new ReportAcl { ReportId = 1, UserId = 1, Permission = FolderPermission.Author },
            new ReportAcl { ReportId = 2, UserId = 1, Permission = FolderPermission.Read },
            new ReportAcl { ReportId = 3, UserId = 1, Permission = FolderPermission.Read });
        await db.SaveChangesAsync();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "1"), new Claim(ClaimTypes.Role, "Publisher")], "test"));
        var http = new DefaultHttpContext { User = principal };
        var catalog = new PortalTenantCatalogScope(db, tenantScope);
        var permissions = new FolderPermissionService(db, catalog);
        var bulk = await permissions.GetEffectiveReportPermissionsAsync(reports, principal);
        foreach (var report in reports)
            Assert.Equal(await permissions.GetEffectiveReportPermissionAsync(report, principal), bulk[report.Id]);
        Assert.Equal(FolderPermission.Author, bulk[1]);
        Assert.Equal(FolderPermission.Read, bulk[2]);
        Assert.Equal(FolderPermission.Manage, bulk[3]);
        Assert.Null(bulk[4]);
        Assert.Null(bulk[5]);
        var controller = new StudioController(db, permissions, new StudioAuthorizationService(config),
            new InMemoryArtifactStorage(), config, new PortalScriptSourceControlService(config),
            new AuditService(db, new HttpContextAccessor { HttpContext = http }, tenantScope), tenantScope, catalog)
        { ControllerContext = new ControllerContext { HttpContext = http } };
        var response = await controller.SearchCatalog(CancellationToken.None, "Document");
        var page = Assert.IsType<StudioCatalogPageDto>(Assert.IsType<OkObjectResult>(response.Result).Value);
        Assert.Equal(new[] { 1, 3 }, page.Items.Select(report => report.Id));
        Assert.Null(page.NextAfterId);
        Assert.IsType<NotFoundObjectResult>((await controller.GetReportSummary(5, CancellationToken.None)).Result);
        Assert.IsType<NotFoundObjectResult>((await controller.GetReportSummary(2, CancellationToken.None)).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.SearchCatalog(CancellationToken.None, new string('x', 161))).Result);
    }
}
