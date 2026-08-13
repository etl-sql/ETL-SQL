using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json.Nodes;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Portal.Controllers;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Models;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Portal")]
public class PortalConsumerUxTests : IClassFixture<PortalWebFactory>
{
    private readonly PortalWebFactory _factory;
    private readonly HttpClient _client;

    public PortalConsumerUxTests(PortalWebFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task EnsureAuthenticatedAsync()
    {
        if (_client.DefaultRequestHeaders.Authorization != null) return;

        var loginRes = await _client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "Admin@12345!" });
        if (loginRes.IsSuccessStatusCode)
        {
            var body = await loginRes.Content.ReadFromJsonAsync<JsonObject>();
            var token = body?["token"]?.ToString();

            using var cpReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password");
            cpReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            cpReq.Content = JsonContent.Create(new { currentPassword = "Admin@12345!", newPassword = "Admin@Tests99!" });
            await _client.SendAsync(cpReq);

            var reloginRes = await _client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "Admin@Tests99!" });
            var reloginBody = await reloginRes.Content.ReadFromJsonAsync<JsonObject>();
            token = reloginBody?["token"]?.ToString() ?? token;

            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        else
        {
            var reloginRes = await _client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "Admin@Tests99!" });
            reloginRes.EnsureSuccessStatusCode();
            var reloginBody = await reloginRes.Content.ReadFromJsonAsync<JsonObject>();
            var token = reloginBody?["token"]?.ToString();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    [Fact]
    public async Task Search_WithFuzzyTokens_ReturnsSuccess()
    {
        await EnsureAuthenticatedAsync();
        var response = await _client.GetAsync("/api/catalog/search?q=Sales");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var items = await response.Content.ReadFromJsonAsync<List<CatalogSearchResultDto>>();
        Assert.NotNull(items);
    }

    [Fact]
    public async Task ConsumerHome_ReturnsAllDashboardCategories()
    {
        await EnsureAuthenticatedAsync();
        var response = await _client.GetAsync("/api/catalog/consumer-home");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var home = await response.Content.ReadFromJsonAsync<ConsumerHomeDto>();
        Assert.NotNull(home);
        Assert.NotNull(home.Favorites);
        Assert.NotNull(home.Recent);
        Assert.NotNull(home.Featured);
        Assert.NotNull(home.Popular);
    }

    [Fact]
    public async Task GetAccessInfo_ReturnsReportMetadataOrNotFound()
    {
        await EnsureAuthenticatedAsync();
        var response = await _client.GetAsync("/api/reports/99999/access-info");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RequestAccess_ReturnsReportNotFoundForInvalidId()
    {
        await EnsureAuthenticatedAsync();
        var response = await _client.PostAsJsonAsync("/api/reports/99999/request-access", new RequestReportAccessDto("Need access"));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AccessInfo_ForRestrictedReport_DoesNotLeakMetadata()
    {
        await using var db = await CreateDbAsync();
        var report = await SeedReportAsync(db);
        var controller = CreateReportsController(db, userId: 2);

        var result = Assert.IsType<OkObjectResult>(await controller.GetAccessInfo(report.Id));
        var info = Assert.IsType<ReportAccessInfoDto>(result.Value);

        Assert.Null(info.ReportId);
        Assert.Null(info.ReportName);
        Assert.Null(info.FolderPath);
        Assert.Null(info.Description);
        Assert.True(info.CanRequestAccess);
        Assert.Equal("Restricted", info.Status);
    }

    [Fact]
    public async Task RequestAccess_DeduplicatesPendingRequest()
    {
        await using var db = await CreateDbAsync();
        var report = await SeedReportAsync(db);
        var controller = CreateReportsController(db, userId: 2);

        var first = Assert.IsType<OkObjectResult>(
            await controller.RequestAccess(report.Id, new RequestReportAccessDto("Need Q3 sales")));
        var second = Assert.IsType<OkObjectResult>(
            await controller.RequestAccess(report.Id, new RequestReportAccessDto("Need Q3 sales again")));

        Assert.Equal(1, await db.ReportAccessRequests.CountAsync(r => r.ReportId == report.Id && r.RequesterUserId == 2));
        Assert.Contains("Access request submitted", first.Value!.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("already pending", second.Value!.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Search_ReturnsMatchReason()
    {
        await using var db = await CreateDbAsync();
        await SeedReportAsync(db, name: "RPT_2026_SALES_Q3_FINAL", tags: "#sales,#inventory");
        var controller = CreateCatalogController(db, userId: 1, isAdmin: true);

        var result = Assert.IsType<OkObjectResult>(await controller.Search("Slaes"));
        var items = Assert.IsAssignableFrom<IEnumerable<CatalogSearchResultDto>>(result.Value);
        var report = Assert.Single(items, i => i.Type == "Report");

        Assert.True(report.Score > 0);
        Assert.False(string.IsNullOrWhiteSpace(report.MatchReason));
    }

    [Fact]
    public async Task Recent_IsScopedToCurrentUser()
    {
        await using var db = await CreateDbAsync();
        var first = await SeedReportAsync(db, name: "User One Report");
        var second = await SeedReportAsync(db, name: "User Two Report");
        db.AuditLogs.AddRange(
            new AuditLog { UserId = 1, Action = "VIEW_SNAPSHOT", ResourceType = "Report", ResourceId = first.Id.ToString(), Timestamp = DateTime.UtcNow.AddMinutes(-1) },
            new AuditLog { UserId = 2, Action = "VIEW_SNAPSHOT", ResourceType = "Report", ResourceId = second.Id.ToString(), Timestamp = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var controller = CreateCatalogController(db, userId: 1, isAdmin: true);

        var result = Assert.IsType<OkObjectResult>(await controller.Recent());
        var items = Assert.IsAssignableFrom<IEnumerable<CatalogSearchResultDto>>(result.Value).ToList();

        Assert.Contains(items, item => item.Id == first.Id);
        Assert.DoesNotContain(items, item => item.Id == second.Id);
    }

    private static async Task<PortalDbContext> CreateDbAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"portal-consumer-ux-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlite($"Data Source={path}")
            .Options;
        var db = new PortalDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Users.AddRange(
            new PortalUser { Id = 1, UserName = "admin", NormalizedUserName = "ADMIN", Email = "admin@example.invalid" },
            new PortalUser { Id = 2, UserName = "consumer", NormalizedUserName = "CONSUMER", Email = "consumer@example.invalid" });
        await db.SaveChangesAsync();
        return db;
    }

    private static async Task<Report> SeedReportAsync(
        PortalDbContext db,
        string name = "Restricted Sales",
        string? tags = "#sales")
    {
        var folder = new Folder { Name = $"Folder {Guid.NewGuid():N}", Path = $"/Secure/{Guid.NewGuid():N}", OwnerId = 1 };
        db.Folders.Add(folder);
        await db.SaveChangesAsync();
        var report = new Report
        {
            FolderId = folder.Id,
            Name = name,
            Description = "Executive restricted sales report",
            Owner = "Finance",
            Contact = "finance@example.invalid",
            Tags = tags,
            ScriptPath = "sales.rptsql",
            ScriptLastModified = DateTime.UtcNow,
            CreatedBy = 1
        };
        db.Reports.Add(report);
        await db.SaveChangesAsync();
        return report;
    }

    [Fact]
    public async Task ApproveAccessRequest_GrantsReportAcl_And_UpdatesStatus()
    {
        await EnsureAuthenticatedAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<AuditService>();

        var folder = new Folder { Name = "Secured", Path = "/Secured", OwnerId = 1 };
        db.Folders.Add(folder);
        await db.SaveChangesAsync();

        var report = new Report { FolderId = folder.Id, Name = "Secured Sales", ScriptPath = "sales.rptsql", CreatedBy = 1 };
        db.Reports.Add(report);
        await db.SaveChangesAsync();

        var requester = new PortalUser { UserName = "consumer1", Email = "consumer1@test.com" };
        db.Users.Add(requester);
        await db.SaveChangesAsync();

        var req = new ReportAccessRequest { ReportId = report.Id, RequesterUserId = requester.Id, Status = "Pending", Reason = "Need Q3 data" };
        db.ReportAccessRequests.Add(req);
        await db.SaveChangesAsync();

        var ownerCtrl = CreateReportsController(db, userId: 1, isAdmin: true);
        var pendingRes = await ownerCtrl.GetPendingAccessRequests();
        var pendingOk = Assert.IsType<OkObjectResult>(pendingRes);
        var pendingList = Assert.IsAssignableFrom<IEnumerable<PendingAccessRequestDto>>(pendingOk.Value);
        Assert.Contains(pendingList, p => p.Id == req.Id);

        var approveRes = await ownerCtrl.ApproveAccessRequest(req.Id, new ApproveReportAccessRequestDto(FolderPermission.Read, "Approved for audit"));
        Assert.IsType<OkObjectResult>(approveRes);

        var updatedReq = await db.ReportAccessRequests.FindAsync(req.Id);
        Assert.Equal("Approved", updatedReq?.Status);

        var acl = await db.ReportAcls.FirstOrDefaultAsync(a => a.ReportId == report.Id && a.UserId == requester.Id);
        Assert.NotNull(acl);
        Assert.Equal(FolderPermission.Read, acl!.Permission);

        var consumerCtrl = CreateReportsController(db, userId: requester.Id, isAdmin: false);
        var accessInfo = await consumerCtrl.GetAccessInfo(report.Id);
        var accessOk = Assert.IsType<OkObjectResult>(accessInfo);
        var accessDto = Assert.IsType<ReportAccessInfoDto>(accessOk.Value);
        Assert.Equal("HasAccess", accessDto.Status);
    }

    [Fact]
    public async Task RequestDataRefresh_StaleReport_ReturnsRequestedOrStarted()
    {
        await using var db = await CreateDbAsync();

        var folder = new Folder { Name = "Ops", Path = "/Ops", OwnerId = 1 };
        db.Folders.Add(folder);
        await db.SaveChangesAsync();

        var report = new Report { FolderId = folder.Id, Name = "Stale Ops", ScriptPath = "ops.rptsql", CreatedBy = 1 };
        db.Reports.Add(report);
        await db.SaveChangesAsync();
        db.ReportAcls.Add(new ReportAcl { ReportId = report.Id, UserId = 2, Permission = FolderPermission.Read });
        await db.SaveChangesAsync();

        var userCtrl = CreateExecutionController(db, userId: 2, isAdmin: false);
        var res = await userCtrl.RequestDataRefresh(report.Id);
        var okRes = Assert.IsType<OkObjectResult>(res);
        Assert.NotNull(okRes.Value);
    }

    [Fact]
    public async Task ReportAcl_AllowsFavoritingWithoutFolderAccess()
    {
        await using var db = await CreateDbAsync();
        var report = await SeedReportAsync(db);
        db.ReportAcls.Add(new ReportAcl
        {
            ReportId = report.Id,
            UserId = 2,
            Permission = FolderPermission.Read
        });
        await db.SaveChangesAsync();

        var controller = CreateReportsController(db, userId: 2);
        Assert.IsType<NoContentResult>(await controller.AddFavorite(report.Id));
        Assert.True(await db.ReportFavorites.AnyAsync(
            favorite => favorite.ReportId == report.Id && favorite.UserId == 2));
    }

    [Fact]
    public async Task ReportAcl_KeepsCreatorShareLinkResolvableWithoutFolderAccess()
    {
        await using var db = await CreateDbAsync();
        var report = await SeedReportAsync(db);
        db.ReportAcls.Add(new ReportAcl
        {
            ReportId = report.Id,
            UserId = 2,
            Permission = FolderPermission.Execute
        });
        await db.SaveChangesAsync();

        var controller = CreateReportsController(db, userId: 2);
        Assert.IsType<CreatedAtActionResult>(await controller.CreateShareLink(report.Id, null));

        var link = await db.ReportShareLinks.SingleAsync(value => value.ReportId == report.Id);
        Assert.IsType<OkObjectResult>(await controller.ResolveShareLink(link.Token));
    }

    [Fact]
    public async Task SaveDefaultView_PersistsDefaultView_PerUserAndReport()
    {
        await EnsureAuthenticatedAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();

        var folder = new Folder { Name = "Sales", Path = "/Sales", OwnerId = 1 };
        db.Folders.Add(folder);
        await db.SaveChangesAsync();

        var report = new Report { FolderId = folder.Id, Name = "Regional Sales", ScriptPath = "regional.rptsql", CreatedBy = 1 };
        db.Reports.Add(report);
        await db.SaveChangesAsync();

        var userCtrl = CreateReportsController(db, userId: 1, isAdmin: true);
        var paramsDict = new Dictionary<string, string> { ["@region"] = "Midwest", ["@year"] = "2026" };

        var saveRes = await userCtrl.SaveDefaultView(report.Id, paramsDict);
        Assert.IsType<OkObjectResult>(saveRes);

        var secondSaveRes = await userCtrl.SaveDefaultView(report.Id, new Dictionary<string, string> { ["@region"] = "West" });
        Assert.IsType<OkObjectResult>(secondSaveRes);

        var getRes = await userCtrl.GetDefaultView(report.Id);
        var getOk = Assert.IsType<OkObjectResult>(getRes);
        Assert.NotNull(getOk.Value);
        Assert.Equal(1, await db.SavedReportViews.CountAsync(v => v.ReportId == report.Id && v.UserId == 1 && v.Name == "My Default View"));
    }

    private static ReportsController CreateReportsController(PortalDbContext db, int userId, bool isAdmin = false)
    {
        var context = new DefaultHttpContext
        {
            User = Principal(userId, isAdmin),
            TraceIdentifier = $"test-{Guid.NewGuid():N}"
        };
        var audit = new AuditService(db, new HttpContextAccessor { HttpContext = context });
        var controller = new ReportsController(
            db,
            audit,
            new PortalConfig(),
            TenantLineage(),
            new FolderPermissionService(db),
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!);
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        return controller;
    }

    private static ExecutionController CreateExecutionController(PortalDbContext db, int userId, bool isAdmin = false)
    {
        var context = new DefaultHttpContext
        {
            User = Principal(userId, isAdmin),
            TraceIdentifier = $"test-{Guid.NewGuid():N}"
        };
        var audit = new AuditService(db, new HttpContextAccessor { HttpContext = context });
        var controller = new ExecutionController(
            db,
            null!,
            null!,
            audit,
            new PortalConfig(),
            new FolderPermissionService(db),
            null!,
            null!,
            null!);
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        return controller;
    }

    private static CatalogController CreateCatalogController(PortalDbContext db, int userId, bool isAdmin = false)
    {
        var context = new DefaultHttpContext
        {
            User = Principal(userId, isAdmin),
            TraceIdentifier = $"test-{Guid.NewGuid():N}"
        };
        var config = new PortalConfig();
        var tenantScope = new DatasetTenantScope(config);
        var controller = new CatalogController(
            db,
            new PortalTenantLineageCatalog(new EmptyLineageCatalogStore(), tenantScope, config),
            tenantScope);
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        return controller;
    }

    private static ClaimsPrincipal Principal(int userId, bool isAdmin = false)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        if (isAdmin)
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static PortalTenantLineageCatalog TenantLineage()
    {
        var config = new PortalConfig();
        return new PortalTenantLineageCatalog(
            new EmptyLineageCatalogStore(), new DatasetTenantScope(config), config);
    }

    private sealed class EmptyLineageCatalogStore : ILineageCatalogStore
    {
        public Task SaveLineageAsync(IEnumerable<LineageEntry> entries, string? jobName, string? scriptPath, DateTime runAt) => Task.CompletedTask;
        public Task<IEnumerable<LineageHistoryEntry>> GetHistoryForTableAsync(string tableName, int limit = 100) => Empty();
        public Task<IEnumerable<LineageHistoryEntry>> GetHistoryForTagAsync(string tagKey, string? tagValue = null, int limit = 100) => Empty();
        public Task<IEnumerable<LineageMissingMetadataEntry>> GetMissingMetadataAsync(IReadOnlyCollection<string> requiredTags, int limit = 100) =>
            Task.FromResult<IEnumerable<LineageMissingMetadataEntry>>([]);
        public Task<IEnumerable<LineageHistoryEntry>> GetRecentLineageAsync(int limit = 1000) => Empty();
        public Task<IEnumerable<LineageHistoryEntry>> GetHistoryForJobAsync(string jobName, int limit = 100) => Empty();
        public Task<IEnumerable<LineageHistoryEntry>> GetHistoryForSourceAsync(string sourceName, int limit = 100) => Empty();
        public Task<IEnumerable<LineageHistoryEntry>> GetHistoryForSourceFileAsync(string sourceFile, int limit = 100) => Empty();

        private static Task<IEnumerable<LineageHistoryEntry>> Empty() =>
            Task.FromResult<IEnumerable<LineageHistoryEntry>>([]);
    }
}
