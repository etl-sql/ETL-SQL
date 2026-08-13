using System.Security.Claims;
using ETL_SQL.Core;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Orchestrator.Storage;
using ETL_SQL.Portal.Controllers;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Models;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Smoke.Security")]
public sealed class SharedStewardshipTenantIsolationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"shared-stewardship-{Guid.NewGuid():N}");

    [Fact]
    public async Task EqualGovernanceKeysScansAndForeignIdsRemainTenantPartitioned()
    {
        Directory.CreateDirectory(_root);
        await using var db = NewDb();
        var lineage = new SQLiteJobHistoryStore(Path.Combine(_root, "lineage.db"));
        var config = new PortalConfig
        {
            SharedTenancy = new SharedTenancyConfig { Enabled = true }
        };
        var alphaScope = Scope(config, "tenant-alpha");
        var betaScope = Scope(config, "tenant-beta");
        var alphaLineage = new PortalTenantLineageCatalog(lineage, alphaScope, config);
        var betaLineage = new PortalTenantLineageCatalog(lineage, betaScope, config);
        await alphaLineage.SaveLineageAsync([Entry("alpha")], "same-job", null, DateTime.UtcNow);
        await betaLineage.SaveLineageAsync([Entry("beta")], "same-job", null, DateTime.UtcNow);

        var alphaService = new GovernanceService(db, alphaLineage, alphaScope);
        var betaService = new GovernanceService(db, betaLineage, betaScope);
        Assert.Equal("tenant-alpha", (await alphaService.GetSettingsAsync()).TenantId);
        Assert.Equal("tenant-beta", (await betaService.GetSettingsAsync()).TenantId);
        await alphaService.ScanAsync("manual", 1);
        await betaService.ScanAsync("manual", 2);

        var alphaFinding = await db.StewardshipFindings.SingleAsync(x => x.TenantId == "tenant-alpha");
        var betaFinding = await db.StewardshipFindings.SingleAsync(x => x.TenantId == "tenant-beta");
        db.StewardshipResolutionCategories.AddRange(
            new StewardshipResolutionCategory { TenantId = "tenant-alpha", Value = "same", Label = "Alpha" },
            new StewardshipResolutionCategory { TenantId = "tenant-beta", Value = "same", Label = "Beta" });
        db.StewardshipGlossaryTerms.AddRange(
            Term("tenant-alpha", "Customer", "Alpha definition"),
            Term("tenant-beta", "Customer", "Beta definition"));
        await db.SaveChangesAsync();

        var alphaController = Controller(db, alphaService, alphaScope);
        var findings = Assert.IsType<OkObjectResult>(await alphaController.GetFindings());
        var findingRows = Assert.IsAssignableFrom<IEnumerable<StewardshipFindingDto>>(findings.Value);
        Assert.Equal(alphaFinding.Id, Assert.Single(findingRows).Id);
        Assert.IsType<NotFoundObjectResult>(await alphaController.DecideFinding(
            betaFinding.Id,
            new DecideFindingRequest("reopen", null, "foreign attempt", ""),
            default));

        var categories = Assert.IsType<OkObjectResult>(await alphaController.GetCategories());
        var categoryRows = Assert.IsAssignableFrom<IEnumerable<GovernanceCategoryDto>>(categories.Value);
        Assert.Equal("Alpha", Assert.Single(categoryRows).Label);
        var glossary = Assert.IsType<OkObjectResult>(await alphaController.GetGlossary());
        var glossaryRows = Assert.IsAssignableFrom<IEnumerable<StewardshipGlossaryTermDto>>(glossary.Value);
        Assert.Equal("Alpha definition", Assert.Single(glossaryRows).Description);

        var alphaDashboard = await alphaService.GetDashboardAsync(null);
        var betaDashboard = await betaService.GetDashboardAsync(null);
        Assert.All(alphaDashboard.Assets, item => Assert.Equal("alpha", item.Owner));
        Assert.All(betaDashboard.Assets, item => Assert.Equal("beta", item.Owner));
        Assert.Equal(2, await db.StewardshipSettings.CountAsync());
        Assert.Equal(2, await db.StewardshipScans.CountAsync());
    }

    private static LineageEntry Entry(string owner) => new("same.table", "SELECT")
    {
        Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["owner"] = owner
        }
    };

    private static StewardshipGlossaryTerm Term(string tenant, string name, string description) => new()
    {
        TenantId = tenant,
        Term = name,
        DataType = "string",
        Aliases = name,
        Description = description
    };

    private static DatasetTenantScope Scope(PortalConfig config, string tenant) =>
        new(config, TenantContext.FromVerifiedCredential(tenant));

    private static GovernanceController Controller(
        PortalDbContext db, GovernanceService service, DatasetTenantScope scope)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "1"), new Claim(ClaimTypes.Name, "steward")],
                "Test"))
        };
        var controller = new GovernanceController(
            db, service, new AuditService(db, new HttpContextAccessor { HttpContext = http }), scope);
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return controller;
    }

    private PortalDbContext NewDb()
    {
        var db = new PortalDbContext(new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_root, "portal.db")}").Options);
        db.Database.EnsureCreated();
        return db;
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
    }
}
