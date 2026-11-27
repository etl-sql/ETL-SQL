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
        Assert.Equal(2, page.Total);
        Assert.Equal(1, page.Page);
        Assert.Equal(StudioController.DefaultCatalogPageSize, page.PageSize);
        Assert.Equal(1, page.TotalPages);
        Assert.IsType<NotFoundObjectResult>((await controller.GetReportSummary(5, CancellationToken.None)).Result);
        Assert.IsType<NotFoundObjectResult>((await controller.GetReportSummary(2, CancellationToken.None)).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.SearchCatalog(CancellationToken.None, new string('x', 161))).Result);
    }

    [Fact]
    public async Task LargeCatalogFixture_AppliesTenantAndFolderVisibilityBeforePagingAndCounting()
    {
        await using var db = new PortalDbContext(
            new DbContextOptionsBuilder<PortalDbContext>().UseSqlite("Data Source=:memory:").Options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();

        await AssertLargeCatalogPagingAsync(db);
    }

    internal static async Task AssertLargeCatalogPagingAsync(PortalDbContext db)
    {
        // Declared large-catalog fixture: 465 total documents (140 visible to caller, 325 hidden).
        // Hidden restricted-folder documents sort first alphabetically (/00-Restricted) so any
        // post-paging filter would produce empty/partial early pages or inflated totals.
        const int visibleOwnedCount = 65;
        const int visibleGroupAuthorCount = 60;
        const int visibleDirectAuthorCount = 10;
        const int visibleAuthoredReadUpgradeCount = 5;
        const int expectedVisibleTotal = visibleOwnedCount + visibleGroupAuthorCount
            + visibleDirectAuthorCount + visibleAuthoredReadUpgradeCount; // 140

        const int hiddenRestrictedCount = 120;
        const int hiddenReadOnlyCount = 80;
        const int hiddenAuthoredWithoutGrantCount = 20;
        const int hiddenForeignTenantCount = 90;
        const int hiddenDeletedCount = 15;

        var config = new PortalConfig { SharedTenancy = new SharedTenancyConfig { Enabled = true } };
        var tenant = TenantContext.FromVerifiedCredential("tenant-alpha");
        var tenantScope = new DatasetTenantScope(config, tenant);

        db.Users.AddRange(
            new PortalUser { Id = 1, UserName = "caller", TenantId = "tenant-alpha" },
            new PortalUser { Id = 2, UserName = "other", TenantId = "tenant-alpha" });
        db.Groups.AddRange(
            new Group { Id = 10, Name = "Authors", TenantId = "tenant-alpha" },
            new Group { Id = 11, Name = "Readers", TenantId = "tenant-alpha" });
        db.UserGroups.AddRange(
            new UserGroup { UserId = 1, GroupId = 10, TenantId = "tenant-alpha" },
            new UserGroup { UserId = 1, GroupId = 11, TenantId = "tenant-alpha" });

        db.Folders.AddRange(
            new Folder { Id = 1, Name = "00-Restricted", Path = "/00-Restricted", OwnerId = 2, TenantId = "tenant-alpha" },
            new Folder { Id = 2, Name = "Alpha-Owned", Path = "/Alpha-Owned", OwnerId = 1, TenantId = "tenant-alpha" },
            new Folder { Id = 3, Name = "Beta-Author", Path = "/Beta-Author", OwnerId = 2, TenantId = "tenant-alpha" },
            new Folder { Id = 4, Name = "ReadOnly", Path = "/ReadOnly", OwnerId = 2, TenantId = "tenant-alpha" },
            new Folder { Id = 5, Name = "Restricted-Direct", Path = "/Restricted-Direct", OwnerId = 2, TenantId = "tenant-alpha" },
            new Folder { Id = 6, Name = "00-Foreign", Path = "/00-Foreign", OwnerId = 1, TenantId = "tenant-beta" });

        db.FolderAcls.AddRange(
            new FolderAcl { FolderId = 3, GroupId = 10, Permission = FolderPermission.Author },
            new FolderAcl { FolderId = 4, GroupId = 11, Permission = FolderPermission.Read });

        var nextId = 1;
        var allReports = new List<Report>();

        // 120 hidden in /00-Restricted (no ACL)
        for (var i = 0; i < hiddenRestrictedCount; i++)
            allReports.Add(new Report { Id = nextId++, Name = $"Hidden-{i:D4}", FolderId = 1, TenantId = "tenant-alpha", CreatedBy = 2 });

        // 20 hidden in /00-Restricted (authored by caller, but no folder/report grant)
        for (var i = 0; i < hiddenAuthoredWithoutGrantCount; i++)
            allReports.Add(new Report { Id = nextId++, Name = $"OrphanAuthored-{i:D4}", FolderId = 1, TenantId = "tenant-alpha", CreatedBy = 1 });

        // 90 hidden in tenant-beta (even though caller is owner and creator)
        for (var i = 0; i < hiddenForeignTenantCount; i++)
            allReports.Add(new Report { Id = nextId++, Name = $"ForeignDoc-{i:D4}", FolderId = 6, TenantId = "tenant-beta", CreatedBy = 1 });

        // 65 visible in /Alpha-Owned (folder owner -> Manage)
        for (var i = 0; i < visibleOwnedCount; i++)
            allReports.Add(new Report { Id = nextId++, Name = i == 0 ? "Metric_50%Report" : $"Owned-{i:D4}", FolderId = 2, TenantId = "tenant-alpha", CreatedBy = 2 });

        // 15 soft-deleted in /Alpha-Owned
        for (var i = 0; i < hiddenDeletedCount; i++)
            allReports.Add(new Report { Id = nextId++, Name = $"Deleted-{i:D4}", FolderId = 2, TenantId = "tenant-alpha", CreatedBy = 1, IsDeleted = true });

        // 60 visible in /Beta-Author (group Author ACL)
        for (var i = 0; i < visibleGroupAuthorCount; i++)
            allReports.Add(new Report { Id = nextId++, Name = $"AuthorDoc-{i:D4}", FolderId = 3, TenantId = "tenant-alpha", CreatedBy = 2 });

        // 80 hidden in /ReadOnly (group Read ACL only, created by other)
        for (var i = 0; i < hiddenReadOnlyCount; i++)
            allReports.Add(new Report { Id = nextId++, Name = $"ReadOnlyDoc-{i:D4}", FolderId = 4, TenantId = "tenant-alpha", CreatedBy = 2 });

        // 5 visible in /ReadOnly (group Read ACL upgraded to Manage because caller is author)
        for (var i = 0; i < visibleAuthoredReadUpgradeCount; i++)
            allReports.Add(new Report { Id = nextId++, Name = $"UpgradedAuthor-{i:D4}", FolderId = 4, TenantId = "tenant-alpha", CreatedBy = 1 });

        // 10 visible in /Restricted-Direct (direct ReportAcl.Author)
        var directReportIds = new List<int>();
        for (var i = 0; i < visibleDirectAuthorCount; i++)
        {
            var id = nextId++;
            directReportIds.Add(id);
            allReports.Add(new Report { Id = id, Name = $"DirectGrant-{i:D4}", FolderId = 5, TenantId = "tenant-alpha", CreatedBy = 2 });
        }

        db.Reports.AddRange(allReports);
        db.ReportAcls.AddRange(directReportIds.Select(id => new ReportAcl
        {
            ReportId = id,
            UserId = 1,
            Permission = FolderPermission.Author
        }));
        await db.SaveChangesAsync();

        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "1"),
            new Claim(ClaimTypes.Role, "Publisher")], "test"));
        var http = new DefaultHttpContext { User = principal };
        var catalog = new PortalTenantCatalogScope(db, tenantScope);
        var permissions = new FolderPermissionService(db, catalog);
        var controller = new StudioController(db, permissions, new StudioAuthorizationService(config),
            new InMemoryArtifactStorage(), config, new PortalScriptSourceControlService(config),
            new AuditService(db, new HttpContextAccessor { HttpContext = http }, tenantScope), tenantScope, catalog)
        { ControllerContext = new ControllerContext { HttpContext = http } };

        // 1. Page size cap (200 -> 50) and full first page despite 140 restricted docs sorting first.
        var page1 = Assert.IsType<StudioCatalogPageDto>(
            Assert.IsType<OkObjectResult>((await controller.SearchCatalog(CancellationToken.None, null, page: 1, pageSize: 200)).Result).Value);
        Assert.Equal(expectedVisibleTotal, page1.Total);
        Assert.Equal(1, page1.Page);
        Assert.Equal(StudioController.MaxCatalogPageSize, page1.PageSize);
        Assert.Equal(3, page1.TotalPages);
        Assert.Equal(50, page1.Items.Count);

        // 2. Walk all pages and verify page boundaries, zero duplicates, and stable ordering.
        var page2 = Assert.IsType<StudioCatalogPageDto>(
            Assert.IsType<OkObjectResult>((await controller.SearchCatalog(CancellationToken.None, null, page: 2, pageSize: 50)).Result).Value);
        var page3 = Assert.IsType<StudioCatalogPageDto>(
            Assert.IsType<OkObjectResult>((await controller.SearchCatalog(CancellationToken.None, null, page: 3, pageSize: 50)).Result).Value);
        Assert.Equal(50, page2.Items.Count);
        Assert.Equal(40, page3.Items.Count);

        var collected = page1.Items.Concat(page2.Items).Concat(page3.Items).ToList();
        Assert.Equal(expectedVisibleTotal, collected.Count);
        Assert.Equal(expectedVisibleTotal, collected.Select(item => item.Id).Distinct().Count());
        Assert.DoesNotContain(collected, item => item.FolderPath is "/00-Restricted" or "/00-Foreign");
        Assert.DoesNotContain(collected, item => item.Name.StartsWith("ReadOnlyDoc-", StringComparison.Ordinal));
        Assert.DoesNotContain(collected, item => item.Name.StartsWith("Deleted-", StringComparison.Ordinal));

        var expectedOrder = collected
            .OrderBy(item => item.FolderPath, StringComparer.Ordinal)
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .ThenBy(item => item.Id)
            .Select(item => item.Id)
            .ToArray();
        Assert.Equal(expectedOrder, collected.Select(item => item.Id).ToArray());

        // 3. Out-of-bounds page clamping (page 0 -> 1, page 99 -> 3, pageSize 0 -> 1).
        var clampedLow = Assert.IsType<StudioCatalogPageDto>(
            Assert.IsType<OkObjectResult>((await controller.SearchCatalog(CancellationToken.None, null, page: 0, pageSize: 0)).Result).Value);
        Assert.Equal(1, clampedLow.Page);
        Assert.Equal(1, clampedLow.PageSize);
        Assert.Equal(expectedVisibleTotal, clampedLow.TotalPages);
        Assert.Single(clampedLow.Items);

        var clampedHigh = Assert.IsType<StudioCatalogPageDto>(
            Assert.IsType<OkObjectResult>((await controller.SearchCatalog(CancellationToken.None, null, page: 99, pageSize: 50)).Result).Value);
        Assert.Equal(3, clampedHigh.Page);
        Assert.Equal(page3.Items.Select(item => item.Id), clampedHigh.Items.Select(item => item.Id));

        // 4. Search filtering respects visibility before counting and escapes LIKE wildcards.
        var restrictedSearch = Assert.IsType<StudioCatalogPageDto>(
            Assert.IsType<OkObjectResult>((await controller.SearchCatalog(CancellationToken.None, "Restricted", page: 1, pageSize: 25)).Result).Value);
        Assert.Equal(visibleDirectAuthorCount, restrictedSearch.Total);
        Assert.Equal(1, restrictedSearch.TotalPages);
        Assert.All(restrictedSearch.Items, item => Assert.Equal("/Restricted-Direct", item.FolderPath));

        var foreignSearch = Assert.IsType<StudioCatalogPageDto>(
            Assert.IsType<OkObjectResult>((await controller.SearchCatalog(CancellationToken.None, "ForeignDoc", page: 1, pageSize: 25)).Result).Value);
        Assert.Equal(0, foreignSearch.Total);
        Assert.Equal(0, foreignSearch.TotalPages);
        Assert.Empty(foreignSearch.Items);

        var wildcardSearch = Assert.IsType<StudioCatalogPageDto>(
            Assert.IsType<OkObjectResult>((await controller.SearchCatalog(CancellationToken.None, "50%", page: 1, pageSize: 25)).Result).Value);
        Assert.Equal(1, wildcardSearch.Total);
        Assert.Equal("Metric_50%Report", Assert.Single(wildcardSearch.Items).Name);
    }
}
