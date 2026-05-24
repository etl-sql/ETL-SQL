using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Core;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.ReportPortal.Tests;

/// <summary>
/// End-to-end integration tests for the Report Portal.
/// Tests run against a real in-process Kestrel + temp SQLite database.
/// Coverage: auth → user → folder → report publish → subscription CRUD → audit log.
/// </summary>
[Trait("Category", "Portal")]
public class PortalIntegrationTests : IClassFixture<PortalWebFactory>
{
    private readonly HttpClient _client;
    private readonly PortalWebFactory _factory;
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    // Shared across all test instances so password-change and lockout don't accumulate.
    private static string?  _adminToken;
    private static readonly SemaphoreSlim _tokenLock = new(1, 1);

    public PortalIntegrationTests(PortalWebFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    // ── 1. Health endpoint ─────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Smoke.Portal")]
    public async Task Health_ReturnsOkWithStatus()
    {
        var res = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await res.Content.ReadFromJsonAsync<JsonObject>(_json);
        Assert.NotNull(body);
        var status = body!["status"]?.GetValue<string>();
        Assert.True(status is "Healthy" or "Degraded",
            $"Expected Healthy or Degraded, got {status}");
    }

    // ── 2. Auth flow ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_WithDefaultAdminCredentials_ReturnsJwt()
    {
        var token = await GetAdminTokenAsync();
        Assert.False(string.IsNullOrWhiteSpace(token), "Expected a JWT token");
    }

    [Fact]
    public async Task Login_WithNonExistentUser_Returns401()
    {
        // Use a username that doesn't exist to avoid affecting the admin lockout counter.
        var res = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            username = "no_such_user_xyz",
            password = "wrongpassword"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Login_WithDeactivatedUser_Returns401()
    {
        var adminToken = await GetAdminTokenAsync();

        // Create a fresh user for this test to avoid side-effects on shared state.
        var username = $"inactive_{Guid.NewGuid():N}"[..18];
        var createRes = await AuthPost(adminToken, "/api/admin/users", new
        {
            username,
            email    = $"{username}@test.local",
            password = "Active@Test1!",
            role     = "Viewer"
        });
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var created = await createRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var userId  = created!["id"]!.GetValue<int>();

        // Deactivate the user via admin PUT.
        var deactivateRes = await AuthPut(adminToken, $"/api/admin/users/{userId}",
            new { isActive = false });
        Assert.Equal(HttpStatusCode.NoContent, deactivateRes.StatusCode);

        // Login attempt on inactive account must return 401 (not 500 or 403).
        var loginRes = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            username,
            password = "Active@Test1!"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, loginRes.StatusCode);
    }

    [Fact]
    public async Task Login_AfterExcessiveFailures_Returns429()
    {
        var adminToken = await GetAdminTokenAsync();

        var username = $"lockout_{Guid.NewGuid():N}"[..17];
        var createRes = await AuthPost(adminToken, "/api/admin/users", new
        {
            username,
            email    = $"{username}@test.local",
            password = "Lockout@Test1!",
            role     = "Viewer"
        });
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);

        // Exhaust the lockout threshold (default 5 failed attempts).
        // Use wrong password — separate user so admin counter stays clean.
        for (int i = 0; i < 6; i++)
        {
            await _client.PostAsJsonAsync("/api/auth/login", new
            {
                username,
                password = "WrongPassword@1!"
            });
        }

        var lockedRes = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            username,
            password = "WrongPassword@1!"
        });
        Assert.Equal(429, (int)lockedRes.StatusCode);
    }

    [Fact]
    public async Task MustChangePassword_BlocksApiUntilChanged()
    {
        var token = await GetAdminTokenAsync();

        // Should be able to reach /api/folders now (admin password already changed in GetAdminTokenAsync)
        var foldersRes = await AuthGet(token, "/api/folders");
        Assert.Equal(HttpStatusCode.OK, foldersRes.StatusCode);

        // MustChangePassword middleware: create a fresh test user with MustChangePassword=true
        var newUser = $"mcp_user_{Guid.NewGuid():N}"[..14];
        var createRes = await AuthPost(token, "/api/admin/users", new
        {
            username = newUser,
            email    = $"{newUser}@test.local",
            password = "MustChange@1!",
            role     = "Viewer"
        });
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);

        // Log in as the new user (MustChangePassword=true by default for admin-created users)
        var loginRes = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            username = newUser,
            password = "MustChange@1!"
        });
        Assert.Equal(HttpStatusCode.OK, loginRes.StatusCode);
        var loginBody = await loginRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var userToken = loginBody!["token"]!.GetValue<string>();

        // Blocked before password change
        var blockedRes = await AuthGet(userToken, "/api/folders");
        Assert.Equal(HttpStatusCode.Forbidden, blockedRes.StatusCode);
        var blockBody = await blockedRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        Assert.NotNull(blockBody!["redirect"]);

        // Change password
        using var cpReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password");
        cpReq.Headers.Authorization = new("Bearer", userToken);
        cpReq.Content = JsonContent.Create(new
        {
            currentPassword = "MustChange@1!",
            newPassword     = "Changed@9999!"
        });
        var cpRes = await _client.SendAsync(cpReq);
        Assert.Equal(HttpStatusCode.NoContent, cpRes.StatusCode);

        // Re-login to get a fresh token (post-password-change)
        var reloginRes = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            username = newUser,
            password = "Changed@9999!"
        });
        Assert.Equal(HttpStatusCode.OK, reloginRes.StatusCode);
        var reloginBody = await reloginRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var freshToken  = reloginBody!["token"]!.GetValue<string>();

        // Should be able to reach /api/folders with fresh token
        var foldersRes2 = await AuthGet(freshToken, "/api/folders");
        Assert.Equal(HttpStatusCode.OK, foldersRes2.StatusCode);
    }

    // ── 3. Admin — user & folder CRUD ─────────────────────────────────────────

    [Fact]
    public async Task Admin_CreateUserAndFolder_PersistCorrectly()
    {
        var token = await GetAdminTokenAsync();

        // Create a viewer user
        var createUserRes = await AuthPost(token, "/api/admin/users", new
        {
            username = $"viewer_{Guid.NewGuid():N}",
            email    = "viewer@test.local",
            password = "Viewer@1234!",
            role     = "Viewer"
        });
        Assert.Equal(HttpStatusCode.Created, createUserRes.StatusCode);

        // Create a folder
        var folderRes = await AuthPost(token, "/api/folders", new
        {
            name     = "Test Folder",
            parentId = (int?)null
        });
        Assert.Equal(HttpStatusCode.Created, folderRes.StatusCode);
        var folder = await folderRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var folderId = folder!["id"]!.GetValue<int>();
        Assert.True(folderId > 0);
    }

    // ── 4. Report publish ──────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Smoke.Portal")]
    public async Task Report_PublishAndGet_RoundTrips()
    {
        var token = await GetAdminTokenAsync();

        // Create folder
        var folderRes = await AuthPost(token, "/api/folders", new { name = "Rpt Folder", parentId = (int?)null });
        var folder    = await folderRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var folderId  = folder!["id"]!.GetValue<int>();

        // Write a dummy .rptsql file in the temp script root
        var scriptPath = Path.Combine(_factory.TempDir, "scripts", "dummy_report.rptsql");
        await File.WriteAllTextAsync(scriptPath,
            "/* @owner: Finance BI; @contact: finance-bi@example.com; @tags: revenue,monthly; @category: Finance; @certification: trusted */\n" +
            "-- dummy report\n");

        var publishRes = await AuthPost(token, "/api/reports", new
        {
            folderId    = folderId,
            name        = "Dummy Report",
            description = "Integration test report",
            scriptPath  = scriptPath
        });
        Assert.Equal(HttpStatusCode.Created, publishRes.StatusCode);
        var report   = await publishRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var reportId = report!["id"]!.GetValue<int>();
        Assert.True(reportId > 0);
        Assert.Equal("Finance BI", report["owner"]!.GetValue<string>());
        Assert.Equal("finance-bi@example.com", report["contact"]!.GetValue<string>());
        Assert.Equal("revenue,monthly", report["tags"]!.GetValue<string>());
        Assert.Equal("Finance", report["category"]!.GetValue<string>());
        Assert.Equal("trusted", report["certification"]!.GetValue<string>());
        Assert.Equal("Finance BI", report["metadata"]!["owner"]!.GetValue<string>());

        // Verify GET returns it
        var getRes = await AuthGet(token, $"/api/folders/{folderId}/reports");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var reports = await getRes.Content.ReadFromJsonAsync<JsonArray>(_json);
        var listed = reports!.Single(r => r!["id"]!.GetValue<int>() == reportId)!.AsObject();
        Assert.Equal("Finance BI", listed["owner"]!.GetValue<string>());
        Assert.Equal("revenue,monthly", listed["tags"]!.GetValue<string>());
    }

    [Fact]
    [Trait("Category", "Smoke.Portal")]
    public async Task CatalogSearch_FindsReportsByMetadataAndFoldersByPath()
    {
        var token = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var folderRes = await AuthPost(token, "/api/folders", new { name = $"Search {suffix}", parentId = (int?)null });
        Assert.Equal(HttpStatusCode.Created, folderRes.StatusCode);
        var folder = await folderRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var folderId = folder!["id"]!.GetValue<int>();

        var scriptPath = Path.Combine(_factory.TempDir, "scripts", $"search_{suffix}.rptsql");
        await File.WriteAllTextAsync(scriptPath,
            $"/* @owner: Search Team; @tags: alpha-{suffix},quarterly; @category: Discovery */\n" +
            "SET REPORT TITLE = 'Catalog Search';\n");

        var publishRes = await AuthPost(token, "/api/reports", new
        {
            folderId,
            name = $"Searchable Report {suffix}",
            description = "Catalog search integration report",
            scriptPath
        });
        Assert.Equal(HttpStatusCode.Created, publishRes.StatusCode);

        var reportSearch = await AuthGet(token, $"/api/catalog/search?q=alpha-{suffix}");
        Assert.Equal(HttpStatusCode.OK, reportSearch.StatusCode);
        var reportResults = await reportSearch.Content.ReadFromJsonAsync<JsonArray>(_json);
        var reportHit = Assert.Single(reportResults!, r => r!["type"]!.GetValue<string>() == "Report");
        Assert.Equal($"Searchable Report {suffix}", reportHit!["name"]!.GetValue<string>());
        Assert.Equal("Discovery", reportHit["category"]!.GetValue<string>());

        var folderSearch = await AuthGet(token, $"/api/catalog/search?q=Search%20{suffix}");
        Assert.Equal(HttpStatusCode.OK, folderSearch.StatusCode);
        var folderResults = await folderSearch.Content.ReadFromJsonAsync<JsonArray>(_json);
        Assert.Contains(folderResults!, r =>
            r!["type"]!.GetValue<string>() == "Folder" &&
            r["path"]!.GetValue<string>() == $"/Search {suffix}");
    }

    [Fact]
    [Trait("Category", "Smoke.Portal")]
    public async Task CatalogLineageEndpoints_ReturnHistoryWithReportContext()
    {
        var token = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var folderRes = await AuthPost(token, "/api/folders", new { name = $"Lineage {suffix}", parentId = (int?)null });
        Assert.Equal(HttpStatusCode.Created, folderRes.StatusCode);
        var folder = await folderRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var folderId = folder!["id"]!.GetValue<int>();

        var scriptPath = Path.Combine(_factory.TempDir, "scripts", $"lineage_{suffix}.rptsql");
        await File.WriteAllTextAsync(scriptPath, "SET REPORT TITLE = 'Lineage Catalog';\n");

        var publishRes = await AuthPost(token, "/api/reports", new
        {
            folderId,
            name = $"Lineage Report {suffix}",
            description = "Lineage catalog integration report",
            scriptPath
        });
        Assert.Equal(HttpStatusCode.Created, publishRes.StatusCode);
        var report = await publishRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var reportId = report!["id"]!.GetValue<int>();

        using (var scope = _factory.Services.CreateScope())
        {
            var catalog = scope.ServiceProvider.GetRequiredService<ILineageCatalogStore>();
            var stageEntry = new LineageEntry("#stage", "SELECT")
            {
                TargetColumn = "OrderId",
                SourceTables = new List<string> { $"sales.Orders_{suffix}" },
                Metadata = new Dictionary<string, string> { ["pii"] = "true", ["owner"] = "SalesOps" },
                SourceFile = scriptPath,
                Line = 3
            };
            var visualEntry = new LineageEntry("report:SalesCard", "CREATE VISUAL")
            {
                SourceTables = new List<string> { "#stage" },
                Metadata = new Dictionary<string, string> { ["owner"] = "SalesOps" },
                SourceFile = scriptPath,
                Line = 8
            };

            await catalog.SaveLineageAsync(
                new[] { stageEntry, visualEntry },
                $"report:{reportId}:manual-session",
                scriptPath,
                DateTime.UtcNow);
        }

        var sourceRes = await AuthGet(token, $"/api/catalog/lineage/source?name={Uri.EscapeDataString($"sales.Orders_{suffix}")}");
        Assert.Equal(HttpStatusCode.OK, sourceRes.StatusCode);
        var sourceRows = await sourceRes.Content.ReadFromJsonAsync<JsonArray>(_json);
        var sourceHit = Assert.Single(sourceRows!);
        Assert.Equal("#stage", sourceHit!["targetTable"]!.GetValue<string>());
        Assert.Equal($"Lineage Report {suffix}", sourceHit["reportName"]!.GetValue<string>());
        Assert.Equal($"/Lineage {suffix}", sourceHit["folderPath"]!.GetValue<string>());

        var tableRes = await AuthGet(token, $"/api/catalog/lineage/table?name={Uri.EscapeDataString("report:SalesCard")}");
        Assert.Equal(HttpStatusCode.OK, tableRes.StatusCode);
        var tableRows = await tableRes.Content.ReadFromJsonAsync<JsonArray>(_json);
        Assert.Contains(tableRows!, r =>
            r!["reportId"]!.GetValue<int>() == reportId &&
            r["sourceTables"]!.AsArray().Any(s => s!.GetValue<string>() == "#stage"));

        var tagRes = await AuthGet(token, "/api/catalog/lineage/tag?key=pii&value=true");
        Assert.Equal(HttpStatusCode.OK, tagRes.StatusCode);
        var tagRows = await tagRes.Content.ReadFromJsonAsync<JsonArray>(_json);
        Assert.Contains(tagRows!, r =>
            r!["targetTable"]!.GetValue<string>() == "#stage" &&
            r["tags"]!["owner"]!.GetValue<string>() == "SalesOps");

        var sourceFileRes = await AuthGet(token, $"/api/catalog/lineage/source-file?path={Uri.EscapeDataString(scriptPath)}");
        Assert.Equal(HttpStatusCode.OK, sourceFileRes.StatusCode);
        var sourceFileRows = await sourceFileRes.Content.ReadFromJsonAsync<JsonArray>(_json);
        Assert.True(sourceFileRows!.Count >= 2);
    }

    [Fact]
    [Trait("Category", "Smoke.Portal")]
    public async Task GoldenWorkflow_PublishExecuteInteractAndExport_RoundTrips()
    {
        var token = await GetAdminTokenAsync();

        var folderRes = await AuthPost(token, "/api/folders", new { name = "Golden Workflow", parentId = (int?)null });
        Assert.Equal(HttpStatusCode.Created, folderRes.StatusCode);
        var folder = await folderRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var folderId = folder!["id"]!.GetValue<int>();

        var scriptPath = Path.Combine(_factory.TempDir, "scripts", "golden_workflow.rptsql");
        var exportPath = Path.Combine(_factory.TempDir, "scripts", "golden_orders_export.csv");
        File.Copy(GetGoldenWorkflowPath(), scriptPath, overwrite: true);

        var publishRes = await AuthPost(token, "/api/reports", new
        {
            folderId,
            name = "Golden Workflow",
            description = "End-to-end smoke target",
            scriptPath
        });
        Assert.Equal(HttpStatusCode.Created, publishRes.StatusCode);
        var report = await publishRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var reportId = report!["id"]!.GetValue<int>();

        var executeRes = await AuthPost(token, $"/api/reports/{reportId}/execute", new
        {
            parameters = new Dictionary<string, string>
            {
                ["Region"] = "All",
                ["MinMargin"] = "0",
                ["ShowIssues"] = "1",
                ["ExportPath"] = exportPath
            }
        });
        Assert.Equal(HttpStatusCode.Accepted, executeRes.StatusCode);
        var executeBody = await executeRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var jobId = executeBody!["jobId"]!.GetValue<string>();

        var job = await WaitForJobAsync(token, jobId);
        Assert.Equal("Completed", job["status"]!.GetValue<string>());

        var snapshotRes = await AuthGet(token, $"/api/reports/{reportId}/snapshot?includeManifest=true");
        Assert.Equal(HttpStatusCode.OK, snapshotRes.StatusCode);
        var snapshot = await snapshotRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var manifest = snapshot!["manifest"]!.AsObject();
        Assert.Equal("Golden Sales Operations Workflow", manifest["title"]!.GetValue<string>());
        Assert.Equal(3, manifest["pages"]!.AsArray().Count);

        var interactRes = await AuthPost(token, $"/api/reports/{reportId}/parameters", new
        {
            @params = new[]
            {
                new { name = "Region", value = "EMEA" },
                new { name = "MinMargin", value = "500" },
                new { name = "ExportPath", value = exportPath }
            }
        });
        Assert.Equal(HttpStatusCode.OK, interactRes.StatusCode);
        var filteredManifest = await interactRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var orderDetail = filteredManifest!["visuals"]!.AsArray()
            .Select(v => v!.AsObject())
            .Single(v => v["name"]!.GetValue<string>() == "OrderDetail");
        Assert.NotEmpty(orderDetail["rows"]!.AsArray());

        var exportRes = await AuthGet(token, $"/api/reports/{reportId}/export/csv?visual=OrderDetail");
        Assert.Equal(HttpStatusCode.OK, exportRes.StatusCode);
        Assert.StartsWith("text/csv", exportRes.Content.Headers.ContentType?.MediaType);
        var csv = await exportRes.Content.ReadAsStringAsync();
        Assert.Contains("OrderId,OrderDate,Region,CustomerID,ProductID,Quantity,UnitPrice,Revenue,Cost,Margin,Status", csv);
        Assert.Contains("EMEA", csv);
    }

    [Fact]
    [Trait("Category", "Smoke.Portal")]
    public async Task Report_ExecuteAndSnapshot_RoundTrips()
    {
        var token = await GetAdminTokenAsync();

        var folderRes = await AuthPost(token, "/api/folders", new { name = "Exec Folder", parentId = (int?)null });
        Assert.Equal(HttpStatusCode.Created, folderRes.StatusCode);
        var folder = await folderRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var folderId = folder!["id"]!.GetValue<int>();

        var visualName = $"ExecLineage_{Guid.NewGuid():N}";
        var scriptPath = Path.Combine(_factory.TempDir, "scripts", "execute_report.rptsql");
        await File.WriteAllTextAsync(scriptPath, $@"
CREATE VISUAL {visualName} AS CARD (
    SOURCE = (SELECT 42 AS Answer),
    MAPPINGS (VALUE = Answer)
);
");

        var publishRes = await AuthPost(token, "/api/reports", new
        {
            folderId,
            name = "Executable Report",
            description = "Smoke test report",
            scriptPath
        });
        Assert.Equal(HttpStatusCode.Created, publishRes.StatusCode);
        var report = await publishRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var reportId = report!["id"]!.GetValue<int>();

        var executeRes = await AuthPost(token, $"/api/reports/{reportId}/execute", new { parameters = new Dictionary<string, string>() });
        Assert.Equal(HttpStatusCode.Accepted, executeRes.StatusCode);
        var executeBody = await executeRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var jobId = executeBody!["jobId"]!.GetValue<string>();

        JsonObject? job = null;
        for (var i = 0; i < 50; i++)
        {
            var jobRes = await AuthGet(token, $"/api/jobs/{jobId}");
            Assert.Equal(HttpStatusCode.OK, jobRes.StatusCode);
            job = await jobRes.Content.ReadFromJsonAsync<JsonObject>(_json);
            var status = job!["status"]!.GetValue<string>();
            if (status is "Completed" or "Failed" or "Cancelled")
                break;

            await Task.Delay(100);
        }

        Assert.NotNull(job);
        Assert.Equal("Completed", job!["status"]!.GetValue<string>());

        var snapshotRes = await AuthGet(token, $"/api/reports/{reportId}/snapshot?includeManifest=true");
        Assert.Equal(HttpStatusCode.OK, snapshotRes.StatusCode);
        var snapshot = await snapshotRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        Assert.NotNull(snapshot!["manifest"]);

        var listRes = await AuthGet(token, $"/api/folders/{folderId}/reports");
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
        var reports = await listRes.Content.ReadFromJsonAsync<JsonArray>(_json);
        var listed = reports!.Single(r => r!["id"]!.GetValue<int>() == reportId)!.AsObject();
        Assert.Equal("Completed", listed["lastRefreshStatus"]!.GetValue<string>());
        Assert.NotNull(listed["snapshotBuiltAt"]);
        Assert.NotNull(listed["lastViewedAt"]);
        Assert.True(listed["lastRefreshDurationMs"]!.GetValue<long>() >= 0);

        using (var scope = _factory.Services.CreateScope())
        {
            var catalog = scope.ServiceProvider.GetRequiredService<ILineageCatalogStore>();
            var lineage = (await catalog.GetHistoryForTableAsync($"report:{visualName}", 20)).ToList();
            Assert.Contains(lineage, e =>
                e.Operation == "CREATE VISUAL" &&
                e.JobName == $"report:{reportId}:{jobId}" &&
                e.ScriptPath == scriptPath);
        }

        var recentRes = await AuthGet(token, "/api/catalog/recent?limit=5");
        Assert.Equal(HttpStatusCode.OK, recentRes.StatusCode);
        var recent = await recentRes.Content.ReadFromJsonAsync<JsonArray>(_json);
        var recentHit = recent!.Single(r => r!["id"]!.GetValue<int>() == reportId)!.AsObject();
        Assert.Equal("Executable Report", recentHit["name"]!.GetValue<string>());
        Assert.True(recentHit["hasSnapshot"]!.GetValue<bool>());
        Assert.False(recentHit["isStale"]!.GetValue<bool>());

        var favoriteRes = await AuthPost(token, $"/api/reports/{reportId}/favorite", new { });
        Assert.Equal(HttpStatusCode.NoContent, favoriteRes.StatusCode);
        var favoriteReportRes = await AuthGet(token, $"/api/reports/{reportId}");
        Assert.Equal(HttpStatusCode.OK, favoriteReportRes.StatusCode);
        var favoriteReport = await favoriteReportRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        Assert.True(favoriteReport!["isFavorite"]!.GetValue<bool>());

        var favoritesRes = await AuthGet(token, "/api/catalog/favorites?limit=5");
        Assert.Equal(HttpStatusCode.OK, favoritesRes.StatusCode);
        var favorites = await favoritesRes.Content.ReadFromJsonAsync<JsonArray>(_json);
        var favoriteHit = favorites!.Single(r => r!["id"]!.GetValue<int>() == reportId)!.AsObject();
        Assert.True(favoriteHit["isFavorite"]!.GetValue<bool>());

        var unfavoriteRes = await AuthDelete(token, $"/api/reports/{reportId}/favorite");
        Assert.Equal(HttpStatusCode.NoContent, unfavoriteRes.StatusCode);
    }

    [Fact]
    [Trait("Category", "Smoke.Portal")]
    public async Task ReportShareLinks_ResolveOnlyWhenCallerHasPermission()
    {
        var token = await GetAdminTokenAsync();

        var folderRes = await AuthPost(token, "/api/folders", new { name = "Share Folder", parentId = (int?)null });
        Assert.Equal(HttpStatusCode.Created, folderRes.StatusCode);
        var folder = await folderRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var folderId = folder!["id"]!.GetValue<int>();

        var scriptPath = Path.Combine(_factory.TempDir, "scripts", $"share_{Guid.NewGuid():N}.rptsql");
        await File.WriteAllTextAsync(scriptPath, "SET REPORT TITLE = 'Shared';");

        var publishRes = await AuthPost(token, "/api/reports", new
        {
            folderId,
            name = "Shared Report",
            description = "Share-link smoke test",
            scriptPath
        });
        Assert.Equal(HttpStatusCode.Created, publishRes.StatusCode);
        var report = await publishRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var reportId = report!["id"]!.GetValue<int>();

        var createShareRes = await AuthPost(token, $"/api/reports/{reportId}/share-links", new
        {
            expiresAt = DateTime.UtcNow.AddDays(1)
        });
        Assert.Equal(HttpStatusCode.Created, createShareRes.StatusCode);
        var share = await createShareRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var shareToken = share!["token"]!.GetValue<string>();
        Assert.Contains($"/api/share/{shareToken}", share["url"]!.GetValue<string>());

        var adminResolve = await AuthGet(token, $"/api/share/{shareToken}");
        Assert.Equal(HttpStatusCode.OK, adminResolve.StatusCode);
        var resolved = await adminResolve.Content.ReadFromJsonAsync<JsonObject>(_json);
        Assert.Equal(reportId, resolved!["reportId"]!.GetValue<int>());

        var viewerToken = await GetFreshViewerTokenAsync();
        var viewerResolve = await AuthGet(viewerToken, $"/api/share/{shareToken}");
        Assert.Equal(HttpStatusCode.Forbidden, viewerResolve.StatusCode);

        var listRes = await AuthGet(token, $"/api/reports/{reportId}/share-links");
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
        var links = await listRes.Content.ReadFromJsonAsync<JsonArray>(_json);
        Assert.Contains(links!, l => l!["token"]!.GetValue<string>() == shareToken);

        var revokeRes = await AuthDelete(token, $"/api/reports/{reportId}/share-links/{shareToken}");
        Assert.Equal(HttpStatusCode.NoContent, revokeRes.StatusCode);

        var revokedResolve = await AuthGet(token, $"/api/share/{shareToken}");
        Assert.Equal(HttpStatusCode.NotFound, revokedResolve.StatusCode);
    }

    [Fact]
    [Trait("Category", "Smoke.Portal")]
    public async Task ReportDependencies_ReturnsDatasetsJobsAndSources()
    {
        var token = await GetAdminTokenAsync();

        var folderRes = await AuthPost(token, "/api/folders", new { name = "Dependency Folder", parentId = (int?)null });
        Assert.Equal(HttpStatusCode.Created, folderRes.StatusCode);
        var folder = await folderRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var folderId = folder!["id"]!.GetValue<int>();

        var scriptPath = Path.Combine(_factory.TempDir, "scripts", $"dependency_{Guid.NewGuid():N}.rptsql");
        await File.WriteAllTextAsync(scriptPath, @"
SELECT OrderId /* @owner: SalesOps; */
INTO #stage
FROM sales.Orders;

CREATE VISUAL Total AS CARD (
    SOURCE = (SELECT 42 AS Answer),
    MAPPINGS (VALUE = Answer)
);
");

        var publishRes = await AuthPost(token, "/api/reports", new
        {
            folderId,
            name = "Dependency Report",
            description = "Dependency smoke test",
            scriptPath
        });
        Assert.Equal(HttpStatusCode.Created, publishRes.StatusCode);
        var report = await publishRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var reportId = report!["id"]!.GetValue<int>();

        var manifestPath = Path.Combine(_factory.TempDir, "snapshots", $"dependency_{Guid.NewGuid():N}.snapshot.json");
        await File.WriteAllTextAsync(manifestPath, """
{
  "source": "dependency.rptsql",
  "builtAt": "2026-05-16T00:00:00Z",
  "visuals": [],
  "pages": [],
  "datasets": [
    { "tempTableName": "#stage", "refreshInterval": "Hourly", "ttl": "2h", "rowCount": 42 }
  ],
  "parameters": {},
  "parameterMetadata": {}
}
""");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            db.ReportSnapshots.Add(new ReportSnapshot
            {
                ReportId = reportId,
                ManifestPath = manifestPath,
                BuiltAt = DateTime.UtcNow,
                BuiltBy = 1
            });
            db.Datasets.Add(new Dataset
            {
                Name = "Sales Summary",
                FolderPath = "/Dependency",
                ParquetFilePath = Path.Combine(_factory.TempDir, "datasets", "sales-summary.parquet"),
                OwningReportId = reportId,
                SourceQuery = "SELECT * FROM erp.InvoiceLines",
                AccessLevel = DatasetAccessLevel.Private,
                RowCount = 25,
                RefreshInterval = "Hourly",
                LastRefresh = DateTime.UtcNow
            });
            db.DatasetJobs.Add(new DatasetJob
            {
                ReportId = reportId,
                OrchestratorJobName = "refresh_sales_summary",
                RefreshInterval = "Hourly",
                LastRefreshedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var res = await AuthGet(token, $"/api/reports/{reportId}/dependencies");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonObject>(_json);

        Assert.Equal("Dependency Report", body!["report"]!["name"]!.GetValue<string>());
        Assert.Equal("#stage", body["manifestDatasets"]![0]!["tempTableName"]!.GetValue<string>());
        Assert.Equal("Sales Summary", body["registeredDatasets"]![0]!["name"]!.GetValue<string>());
        Assert.Equal("refresh_sales_summary", body["refreshJobs"]![0]!["orchestratorJobName"]!.GetValue<string>());
        Assert.Contains(body["sources"]!.AsArray(), n => n!["name"]!.GetValue<string>() == "erp.InvoiceLines");
        Assert.Contains(body["lineageEntries"]!.AsArray(), n =>
            n!["target"]!.GetValue<string>() == "#stage" &&
            n["targetColumn"]!.GetValue<string>() == "OrderId" &&
            n["tags"]!["owner"]!.GetValue<string>() == "SalesOps");
    }

    [Fact]
    [Trait("Category", "Smoke.Portal")]
    public async Task ReportHistory_ReturnsSnapshotsHashesAndChanges()
    {
        var token = await GetAdminTokenAsync();

        var folderRes = await AuthPost(token, "/api/folders", new { name = "History Folder", parentId = (int?)null });
        Assert.Equal(HttpStatusCode.Created, folderRes.StatusCode);
        var folder = await folderRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var folderId = folder!["id"]!.GetValue<int>();

        var scriptPath = Path.Combine(_factory.TempDir, "scripts", $"history_{Guid.NewGuid():N}.rptsql");
        await File.WriteAllTextAsync(scriptPath, "SET REPORT TITLE = 'History';");

        var publishRes = await AuthPost(token, "/api/reports", new
        {
            folderId,
            name = "History Report",
            description = "History smoke test",
            scriptPath
        });
        Assert.Equal(HttpStatusCode.Created, publishRes.StatusCode);
        var report = await publishRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var reportId = report!["id"]!.GetValue<int>();

        string publishedHash;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var entity = await db.Reports.FindAsync(reportId);
            publishedHash = entity!.PublishedScriptHash!;
            db.ReportSnapshots.Add(new ReportSnapshot
            {
                ReportId = reportId,
                ManifestPath = Path.Combine(_factory.TempDir, "snapshots", "history.snapshot.json"),
                BuiltAt = DateTime.UtcNow,
                BuiltBy = 1,
                ScriptHashAtRunTime = publishedHash,
                HashMatched = true,
                ParametersJson = "{\"@Region\":\"NA\"}"
            });
            await db.SaveChangesAsync();
        }

        await File.AppendAllTextAsync(scriptPath, Environment.NewLine + "SET REPORT DESCRIPTION = 'Changed';");

        var res = await AuthGet(token, $"/api/reports/{reportId}/history");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonObject>(_json);

        Assert.Equal("History Report", body!["report"]!["name"]!.GetValue<string>());
        Assert.Equal(publishedHash, body["publishedScriptHash"]!.GetValue<string>());
        Assert.True(body["scriptChanged"]!.GetValue<bool>());
        Assert.NotEqual(publishedHash, body["currentScriptHash"]!.GetValue<string>());
        Assert.Equal(publishedHash, body["snapshots"]![0]!["scriptHashAtRunTime"]!.GetValue<string>());
        Assert.Contains(body["changes"]!.AsArray(), n => n!["action"]!.GetValue<string>() == "PUBLISH_REPORT");
    }

    [Fact]
    [Trait("Category", "Smoke.Portal")]
    public async Task Report_FailedExecution_SurfacesCatalogFailureStatus()
    {
        var token = await GetAdminTokenAsync();

        var folderRes = await AuthPost(token, "/api/folders", new { name = "Failed Exec Folder", parentId = (int?)null });
        Assert.Equal(HttpStatusCode.Created, folderRes.StatusCode);
        var folder = await folderRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var folderId = folder!["id"]!.GetValue<int>();

        var scriptPath = Path.Combine(_factory.TempDir, "scripts", "failed_execute_report.rptsql");
        await File.WriteAllTextAsync(scriptPath, "SET REPORT TITLE = 'Missing at run time';");

        var publishRes = await AuthPost(token, "/api/reports", new
        {
            folderId,
            name = "Failing Report",
            description = "Smoke test failure status",
            scriptPath
        });
        Assert.Equal(HttpStatusCode.Created, publishRes.StatusCode);
        var report = await publishRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var reportId = report!["id"]!.GetValue<int>();
        File.Delete(scriptPath);

        var executeRes = await AuthPost(token, $"/api/reports/{reportId}/execute", new { parameters = new Dictionary<string, string>() });
        Assert.Equal(HttpStatusCode.Accepted, executeRes.StatusCode);
        var executeBody = await executeRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var jobId = executeBody!["jobId"]!.GetValue<string>();

        var job = await WaitForJobAsync(token, jobId);
        Assert.Equal("Failed", job["status"]!.GetValue<string>());

        var listRes = await AuthGet(token, $"/api/folders/{folderId}/reports");
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
        var reports = await listRes.Content.ReadFromJsonAsync<JsonArray>(_json);
        var listed = reports!.Single(r => r!["id"]!.GetValue<int>() == reportId)!.AsObject();
        Assert.Equal("Failed", listed["lastRefreshStatus"]!.GetValue<string>());
        Assert.False(string.IsNullOrWhiteSpace(listed["lastRefreshError"]!.GetValue<string>()));
        Assert.True(listed["lastRefreshDurationMs"]!.GetValue<long>() >= 0);
    }

    [Fact]
    [Trait("Category", "Smoke.Security")]
    public async Task Report_PublishRejectsSiblingScriptRootBypass()
    {
        var token = await GetAdminTokenAsync();

        var folderRes = await AuthPost(token, "/api/folders", new { name = "Sibling Publish", parentId = (int?)null });
        var folder    = await folderRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var folderId  = folder!["id"]!.GetValue<int>();

        var siblingRoot = Path.Combine(_factory.TempDir, "scripts2");
        Directory.CreateDirectory(siblingRoot);
        var siblingScript = Path.Combine(siblingRoot, "outside.rptsql");
        await File.WriteAllTextAsync(siblingScript, "-- outside script root\n");

        var publishRes = await AuthPost(token, "/api/reports", new
        {
            folderId,
            name        = "Outside Report",
            description = "Should be rejected",
            scriptPath  = siblingScript
        });

        Assert.Equal(HttpStatusCode.BadRequest, publishRes.StatusCode);
    }

    [Fact]
    [Trait("Category", "Smoke.Security")]
    public async Task Report_UpdateRejectsSiblingScriptRootBypass()
    {
        var token = await GetAdminTokenAsync();

        var folderRes = await AuthPost(token, "/api/folders", new { name = "Sibling Update", parentId = (int?)null });
        var folder    = await folderRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var folderId  = folder!["id"]!.GetValue<int>();

        var scriptPath = Path.Combine(_factory.TempDir, "scripts", "inside_update.rptsql");
        await File.WriteAllTextAsync(scriptPath, "-- inside script root\n");

        var publishRes = await AuthPost(token, "/api/reports", new
        {
            folderId,
            name        = "Inside Report",
            description = "",
            scriptPath
        });
        Assert.Equal(HttpStatusCode.Created, publishRes.StatusCode);
        var report   = await publishRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var reportId = report!["id"]!.GetValue<int>();

        var siblingRoot = Path.Combine(_factory.TempDir, "scripts2");
        Directory.CreateDirectory(siblingRoot);
        var siblingScript = Path.Combine(siblingRoot, "outside_update.rptsql");
        await File.WriteAllTextAsync(siblingScript, "-- outside script root\n");

        var updateRes = await AuthPut(token, $"/api/reports/{reportId}", new
        {
            scriptPath = siblingScript
        });

        Assert.Equal(HttpStatusCode.BadRequest, updateRes.StatusCode);
    }

    [Fact]
    [Trait("Category", "Smoke.Portal")]
    public async Task Report_PublishAndUpdateValidateScriptBeforeAccepting()
    {
        var token = await GetAdminTokenAsync();

        var folderRes = await AuthPost(token, "/api/folders", new { name = "Validate Publish", parentId = (int?)null });
        var folder    = await folderRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var folderId  = folder!["id"]!.GetValue<int>();

        var invalidPath = Path.Combine(_factory.TempDir, "scripts", $"invalid_{Guid.NewGuid():N}.rptsql");
        await File.WriteAllTextAsync(invalidPath, "SET REPORT TITLE = 'unterminated;");

        var validateRes = await AuthPost(token, "/api/reports/validate", new { scriptPath = invalidPath });
        Assert.Equal(HttpStatusCode.BadRequest, validateRes.StatusCode);
        var validation = await validateRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        Assert.False(validation!["isValid"]!.GetValue<bool>());

        var rejectedPublish = await AuthPost(token, "/api/reports", new
        {
            folderId,
            name = "Invalid Report",
            description = "",
            scriptPath = invalidPath
        });
        Assert.Equal(HttpStatusCode.BadRequest, rejectedPublish.StatusCode);

        var validPath = Path.Combine(_factory.TempDir, "scripts", $"valid_{Guid.NewGuid():N}.rptsql");
        await File.WriteAllTextAsync(validPath, "SET REPORT TITLE = 'Valid';");
        var publishRes = await AuthPost(token, "/api/reports", new
        {
            folderId,
            name = "Valid Report",
            description = "",
            scriptPath = validPath
        });
        Assert.Equal(HttpStatusCode.Created, publishRes.StatusCode);
        var report   = await publishRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var reportId = report!["id"]!.GetValue<int>();

        var updateRes = await AuthPut(token, $"/api/reports/{reportId}", new { scriptPath = invalidPath });
        Assert.Equal(HttpStatusCode.BadRequest, updateRes.StatusCode);

        var afterRes = await AuthGet(token, $"/api/reports/{reportId}");
        Assert.Equal(HttpStatusCode.OK, afterRes.StatusCode);
        var after = await afterRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        Assert.Equal(validPath, after!["scriptPath"]!.GetValue<string>());
    }

    [Fact]
    [Trait("Category", "Smoke.Security")]
    public async Task Report_ParametersRejectsPersistedSiblingScriptRootBypass()
    {
        var token = await GetAdminTokenAsync();

        var folderRes = await AuthPost(token, "/api/folders", new { name = "Tampered Script", parentId = (int?)null });
        var folder    = await folderRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var folderId  = folder!["id"]!.GetValue<int>();

        var scriptPath = Path.Combine(_factory.TempDir, "scripts", "inside_params.rptsql");
        await File.WriteAllTextAsync(scriptPath, "DECLARE @Region STRING INPUT = 'All';");

        var publishRes = await AuthPost(token, "/api/reports", new
        {
            folderId,
            name = "Inside Params",
            description = "",
            scriptPath
        });
        Assert.Equal(HttpStatusCode.Created, publishRes.StatusCode);
        var report   = await publishRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var reportId = report!["id"]!.GetValue<int>();

        var siblingRoot = Path.Combine(_factory.TempDir, "scripts2");
        Directory.CreateDirectory(siblingRoot);
        var siblingScript = Path.Combine(siblingRoot, "outside_params.rptsql");
        await File.WriteAllTextAsync(siblingScript, "DECLARE @Region STRING INPUT = 'All';");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var entity = await db.Reports.FindAsync(reportId);
            Assert.NotNull(entity);
            entity!.ScriptPath = siblingScript;
            await db.SaveChangesAsync();
        }

        var parametersRes = await AuthGet(token, $"/api/reports/{reportId}/parameters");

        Assert.Equal(HttpStatusCode.Forbidden, parametersRes.StatusCode);
    }

    [Fact]
    [Trait("Category", "Smoke.Security")]
    public async Task SnapshotEndpointsRejectPersistedSiblingSnapshotRootBypass()
    {
        var token = await GetAdminTokenAsync();

        var folderRes = await AuthPost(token, "/api/folders", new { name = "Tampered Snapshot", parentId = (int?)null });
        var folder    = await folderRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var folderId  = folder!["id"]!.GetValue<int>();

        var scriptPath = Path.Combine(_factory.TempDir, "scripts", "inside_snapshot.rptsql");
        await File.WriteAllTextAsync(scriptPath, "-- snapshot tamper test\n");

        var publishRes = await AuthPost(token, "/api/reports", new
        {
            folderId,
            name = "Snapshot Tamper",
            description = "",
            scriptPath
        });
        Assert.Equal(HttpStatusCode.Created, publishRes.StatusCode);
        var report   = await publishRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var reportId = report!["id"]!.GetValue<int>();

        var siblingRoot = Path.Combine(_factory.TempDir, "snapshots2");
        Directory.CreateDirectory(siblingRoot);
        var siblingSnapshot = Path.Combine(siblingRoot, "outside.snapshot.json");
        await File.WriteAllTextAsync(siblingSnapshot, "{}");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            db.ReportSnapshots.Add(new ReportSnapshot
            {
                ReportId = reportId,
                ManifestPath = siblingSnapshot,
                BuiltAt = DateTime.UtcNow,
                BuiltBy = 1
            });
            await db.SaveChangesAsync();
        }

        var snapshotRes = await AuthGet(token, $"/api/reports/{reportId}/snapshot?includeManifest=true");
        var manifestRes = await AuthGet(token, $"/api/reports/{reportId}/snapshot/manifest");
        var exportRes   = await AuthGet(token, $"/api/reports/{reportId}/export/csv");

        Assert.Equal(HttpStatusCode.Forbidden, snapshotRes.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, manifestRes.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, exportRes.StatusCode);
    }

    [Fact]
    [Trait("Category", "Smoke.Security")]
    public async Task DatasetRegistry_RejectsSiblingDatasetRootBypass()
    {
        var siblingRoot = Path.Combine(_factory.TempDir, "datasets2");
        Directory.CreateDirectory(siblingRoot);
        var siblingDataset = Path.Combine(siblingRoot, "outside.parquet");

        using var scope = _factory.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IDatasetRegistry>();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            registry.RegisterOrUpdate(new DatasetMetadata
            {
                Name = "#outside",
                FolderPath = "/reports",
                ParquetFilePath = siblingDataset,
                SourceQuery = "SELECT 1"
            }));

        Assert.Contains("DatasetRootPath", ex.Message);

        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        Assert.False(await db.Datasets.AnyAsync(d => d.Name == "#outside"));
    }

    [Fact]
    [Trait("Category", "Smoke.Security")]
    public async Task DatasetRegistry_StoresResolvedPathInsideDatasetRoot()
    {
        using var scope = _factory.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IDatasetRegistry>();

        await registry.RegisterOrUpdate(new DatasetMetadata
        {
            Name = "#inside",
            FolderPath = "/reports",
            ParquetFilePath = "inside.parquet",
            SourceQuery = "SELECT 1"
        });

        var metadata = await registry.Lookup("#inside", "/reports", "Admin");

        Assert.NotNull(metadata);
        Assert.Equal(Path.Combine(_factory.TempDir, "datasets", "inside.parquet"), metadata!.ParquetFilePath);
    }

    [Fact]
    [Trait("Category", "Smoke.Security")]
    public async Task DatasetRegistry_FiltersPrivateDatasetsByOwnerAclAndAdmin()
    {
        using var scope = _factory.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IDatasetRegistry>();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var owner = new PortalUser { UserName = $"ds_owner_{suffix}", Email = $"ds_owner_{suffix}@test.local" };
        var viewer = new PortalUser { UserName = $"ds_viewer_{suffix}", Email = $"ds_viewer_{suffix}@test.local" };
        var outsider = new PortalUser { UserName = $"ds_outsider_{suffix}", Email = $"ds_outsider_{suffix}@test.local" };
        db.Users.AddRange(owner, viewer, outsider);
        await db.SaveChangesAsync();

        var folder = new Folder { Name = $"Datasets {suffix}", Path = $"/datasets-{suffix}", OwnerId = owner.Id };
        db.Folders.Add(folder);
        await db.SaveChangesAsync();

        var ownerReport = new Report
        {
            FolderId = folder.Id,
            Name = $"Owner Report {suffix}",
            ScriptPath = Path.Combine(_factory.TempDir, "scripts", $"owner_{suffix}.rptsql"),
            CreatedBy = owner.Id
        };
        var otherReport = new Report
        {
            FolderId = folder.Id,
            Name = $"Other Report {suffix}",
            ScriptPath = Path.Combine(_factory.TempDir, "scripts", $"other_{suffix}.rptsql"),
            CreatedBy = outsider.Id
        };
        db.Reports.AddRange(ownerReport, otherReport);
        await db.SaveChangesAsync();

        await registry.RegisterOrUpdate(new DatasetMetadata
        {
            Name = $"#public_{suffix}",
            FolderPath = folder.Path,
            ParquetFilePath = $"public_{suffix}.parquet",
            SourceQuery = "SELECT 1",
            AccessLevel = DatasetAccessLevel.Public,
            OwningReportId = otherReport.Id
        });
        await registry.RegisterOrUpdate(new DatasetMetadata
        {
            Name = $"#owner_{suffix}",
            FolderPath = folder.Path,
            ParquetFilePath = $"owner_{suffix}.parquet",
            SourceQuery = "SELECT 1",
            AccessLevel = DatasetAccessLevel.Private,
            OwningReportId = ownerReport.Id
        });
        await registry.RegisterOrUpdate(new DatasetMetadata
        {
            Name = $"#acl_{suffix}",
            FolderPath = folder.Path,
            ParquetFilePath = $"acl_{suffix}.parquet",
            SourceQuery = "SELECT 1",
            AccessLevel = DatasetAccessLevel.Private,
            OwningReportId = otherReport.Id
        });
        await registry.RegisterOrUpdate(new DatasetMetadata
        {
            Name = $"#other_{suffix}",
            FolderPath = folder.Path,
            ParquetFilePath = $"other_{suffix}.parquet",
            SourceQuery = "SELECT 1",
            AccessLevel = DatasetAccessLevel.Private,
            OwningReportId = otherReport.Id
        });

        var group = new Group { Name = $"dataset-viewers-{suffix}" };
        db.Groups.Add(group);
        await db.SaveChangesAsync();
        db.UserGroups.Add(new UserGroup { UserId = viewer.Id, GroupId = group.Id });
        var aclDataset = await db.Datasets.SingleAsync(d => d.Name == $"#acl_{suffix}" && d.FolderPath == folder.Path);
        db.DatasetAcls.Add(new DatasetAcl
        {
            DatasetId = aclDataset.Id,
            GroupId = group.Id,
            Permission = DatasetPermission.Viewer
        });
        await db.SaveChangesAsync();

        var anonymousList = (await registry.ListAll("")).Select(d => d.Name).ToHashSet();
        Assert.Contains($"#public_{suffix}", anonymousList);
        Assert.DoesNotContain($"#owner_{suffix}", anonymousList);
        Assert.DoesNotContain($"#acl_{suffix}", anonymousList);
        Assert.DoesNotContain($"#other_{suffix}", anonymousList);

        var ownerList = (await registry.ListAll($"UserId={owner.Id}")).Select(d => d.Name).ToHashSet();
        Assert.Contains($"#public_{suffix}", ownerList);
        Assert.Contains($"#owner_{suffix}", ownerList);
        Assert.DoesNotContain($"#acl_{suffix}", ownerList);
        Assert.DoesNotContain($"#other_{suffix}", ownerList);

        var viewerList = (await registry.ListAll($"UserId={viewer.Id}")).Select(d => d.Name).ToHashSet();
        Assert.Contains($"#public_{suffix}", viewerList);
        Assert.Contains($"#acl_{suffix}", viewerList);
        Assert.DoesNotContain($"#owner_{suffix}", viewerList);
        Assert.DoesNotContain($"#other_{suffix}", viewerList);

        Assert.Null(await registry.Lookup($"#owner_{suffix}", folder.Path, $"UserId={outsider.Id}"));
        Assert.NotNull(await registry.Lookup($"#owner_{suffix}", folder.Path, $"UserId={owner.Id}"));
        Assert.Equal(4, (await registry.ListAll("Admin")).Count(d => d.FolderPath == folder.Path));
    }

    [Fact]
    [Trait("Category", "Smoke.Portal")]
    public async Task AdminEffectivePermissions_ReturnsUserFolderAndReportAccess()
    {
        var token = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        int userId;
        int folderId;
        int reportId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();

            var user = new PortalUser { UserName = $"perm_user_{suffix}", Email = $"perm_user_{suffix}@test.local" };
            var group = new Group { Name = $"Perm Group {suffix}" };
            db.Users.Add(user);
            db.Groups.Add(group);
            await db.SaveChangesAsync();

            db.UserGroups.Add(new UserGroup { UserId = user.Id, GroupId = group.Id });
            var folder = new Folder { Name = $"Perm Folder {suffix}", Path = $"/perm-{suffix}", OwnerId = user.Id };
            db.Folders.Add(folder);
            await db.SaveChangesAsync();

            db.FolderAcls.Add(new FolderAcl
            {
                FolderId = folder.Id,
                GroupId = group.Id,
                Permission = FolderPermission.Execute
            });
            var report = new Report
            {
                FolderId = folder.Id,
                Name = $"Perm Report {suffix}",
                ScriptPath = Path.Combine(_factory.TempDir, "scripts", $"perm_{suffix}.rptsql"),
                ScriptLastModified = DateTime.UtcNow,
                CreatedBy = user.Id
            };
            db.Reports.Add(report);
            await db.SaveChangesAsync();

            userId = user.Id;
            folderId = folder.Id;
            reportId = report.Id;
        }

        var userRes = await AuthGet(token, $"/api/admin/permissions/effective/user/{userId}");
        Assert.Equal(HttpStatusCode.OK, userRes.StatusCode);
        var userBody = await userRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        Assert.Equal($"perm_user_{suffix}", userBody!["username"]!.GetValue<string>());
        var folderHit = userBody["folders"]!.AsArray().Single(f => f!["resourceId"]!.GetValue<int>() == folderId)!.AsObject();
        Assert.Equal("Execute", folderHit["permission"]!.GetValue<string>());
        var reportHit = userBody["reports"]!.AsArray().Single(r => r!["resourceId"]!.GetValue<int>() == reportId)!.AsObject();
        Assert.Equal("Execute", reportHit["permission"]!.GetValue<string>());

        var folderRes = await AuthGet(token, $"/api/admin/permissions/effective/folder/{folderId}");
        Assert.Equal(HttpStatusCode.OK, folderRes.StatusCode);
        var folderBody = await folderRes.Content.ReadFromJsonAsync<JsonArray>(_json);
        var folderUser = folderBody!.Single(u => u!["userId"]!.GetValue<int>() == userId)!.AsObject();
        Assert.Equal("Execute", folderUser["permission"]!.GetValue<string>());

        var reportRes = await AuthGet(token, $"/api/admin/permissions/effective/report/{reportId}");
        Assert.Equal(HttpStatusCode.OK, reportRes.StatusCode);
        var reportBody = await reportRes.Content.ReadFromJsonAsync<JsonArray>(_json);
        var reportUser = reportBody!.Single(u => u!["userId"]!.GetValue<int>() == userId)!.AsObject();
        Assert.Equal("Execute", reportUser["permission"]!.GetValue<string>());
    }

    [Fact]
    [Trait("Category", "Smoke.Portal")]
    public async Task AdminUsageMetrics_ReturnsViewsRefreshAndSubscriptionFailures()
    {
        var token = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        int reportId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var user = new PortalUser { UserName = $"metric_user_{suffix}", Email = $"metric_user_{suffix}@test.local" };
            db.Users.Add(user);
            await db.SaveChangesAsync();

            var folder = new Folder { Name = $"Metrics {suffix}", Path = $"/metrics-{suffix}", OwnerId = user.Id };
            db.Folders.Add(folder);
            await db.SaveChangesAsync();

            var report = new Report
            {
                FolderId = folder.Id,
                Name = $"Metric Report {suffix}",
                ScriptPath = Path.Combine(_factory.TempDir, "scripts", $"metric_{suffix}.rptsql"),
                ScriptLastModified = DateTime.UtcNow,
                CreatedBy = user.Id,
                LastRefreshStatus = "Failed",
                LastRefreshDurationMs = 1250,
                LastRefreshError = "Refresh failed"
            };
            db.Reports.Add(report);
            await db.SaveChangesAsync();

            db.AuditLogs.AddRange(
                new AuditLog { UserId = user.Id, Action = "VIEW_SNAPSHOT", ResourceType = "Report", ResourceId = report.Id.ToString(), Timestamp = DateTime.UtcNow.AddMinutes(-5) },
                new AuditLog { UserId = user.Id, Action = "VIEW_SNAPSHOT", ResourceType = "Report", ResourceId = report.Id.ToString(), Timestamp = DateTime.UtcNow.AddMinutes(-1) });
            db.Subscriptions.Add(new Subscription
            {
                ReportId = report.Id,
                UserId = user.Id,
                Format = SubscriptionFormat.PDF,
                SmtpAlias = "smtp",
                Recipients = user.Email!,
                FailCount = 3
            });
            await db.SaveChangesAsync();
            reportId = report.Id;
        }

        var res = await AuthGet(token, "/api/admin/metrics/usage?days=7");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonObject>(_json);
        Assert.True(body!["totalViews"]!.GetValue<int>() >= 2);
        Assert.True(body["uniqueViewers"]!.GetValue<int>() >= 1);
        Assert.True(body["refreshFailureCount"]!.GetValue<int>() >= 1);
        Assert.True(body["subscriptionDeliveryFailureCount"]!.GetValue<int>() >= 3);

        var reportMetric = body["reports"]!.AsArray().Single(r => r!["reportId"]!.GetValue<int>() == reportId)!.AsObject();
        Assert.Equal(2, reportMetric["viewCount"]!.GetValue<int>());
        Assert.Equal(1, reportMetric["uniqueViewers"]!.GetValue<int>());
        Assert.Equal("Failed", reportMetric["lastRefreshStatus"]!.GetValue<string>());
        Assert.Equal(1250, reportMetric["lastRefreshDurationMs"]!.GetValue<long>());
        Assert.Equal(3, reportMetric["subscriptionFailureCount"]!.GetValue<int>());
    }

    // ── 5. Subscription CRUD ──────────────────────────────────────────────────

    [Fact]
    public async Task Subscription_CreateAndDelete_RegistersAndRemovesJob()
    {
        var token = await GetAdminTokenAsync();

        // Publish a report
        var folderRes = await AuthPost(token, "/api/folders", new { name = "Sub Folder", parentId = (int?)null });
        var folder    = await folderRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var folderId  = folder!["id"]!.GetValue<int>();

        var scriptPath = Path.Combine(_factory.TempDir, "scripts", "sub_report.rptsql");
        await File.WriteAllTextAsync(scriptPath, "-- sub report\n");

        var reportRes = await AuthPost(token, "/api/reports", new
        {
            folderId, name = "Sub Report", description = "", scriptPath
        });
        var report   = await reportRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var reportId = report!["id"]!.GetValue<int>();

        // Create SMTP connection (required for non-Link format)
        var smtpRes = await AuthPost(token, "/api/admin/smtp", new
        {
            alias       = $"test-smtp-{Guid.NewGuid():N}"[..16],
            host        = "smtp.test.local",
            port        = 587,
            username    = "user@test.local",
            password    = "smtppassword",
            fromAddress = "noreply@test.local",
            useSsl      = true
        });
        Assert.Equal(HttpStatusCode.OK, smtpRes.StatusCode);
        var smtpBody = await smtpRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var smtpAlias = smtpBody!["alias"]!.GetValue<string>();

        // Create subscription (Link format — no attachment export needed)
        var subRes = await AuthPost(token, "/api/subscriptions", new
        {
            reportId       = reportId,
            schedule       = "Daily",
            format         = "Link",
            smtpAlias      = smtpAlias,
            recipientEmail = "subscriber@test.local",
            atTime         = "08:00"
        });
        Assert.Equal(HttpStatusCode.Created, subRes.StatusCode);
        var sub   = await subRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var subId = sub!["id"]!.GetValue<int>();
        Assert.True(subId > 0);

        // Verify GET
        var getRes = await AuthGet(token, $"/api/subscriptions/{subId}");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);

        // Delete
        var delRes = await AuthDelete(token, $"/api/subscriptions/{subId}");
        Assert.Equal(HttpStatusCode.NoContent, delRes.StatusCode);

        // Confirm gone
        var gone = await AuthGet(token, $"/api/subscriptions/{subId}");
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    [Trait("Category", "Smoke.Security")]
    public async Task Subscription_DeleteRejectsPersistedSiblingScriptRootBypass()
    {
        var token = await GetAdminTokenAsync();

        var folderRes = await AuthPost(token, "/api/folders", new { name = "Sub Tamper", parentId = (int?)null });
        var folder    = await folderRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var folderId  = folder!["id"]!.GetValue<int>();

        var scriptPath = Path.Combine(_factory.TempDir, "scripts", "sub_tamper_report.rptsql");
        await File.WriteAllTextAsync(scriptPath, "-- sub tamper report\n");

        var reportRes = await AuthPost(token, "/api/reports", new
        {
            folderId,
            name = "Sub Tamper Report",
            description = "",
            scriptPath
        });
        Assert.Equal(HttpStatusCode.Created, reportRes.StatusCode);
        var report   = await reportRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var reportId = report!["id"]!.GetValue<int>();

        var subRes = await AuthPost(token, "/api/subscriptions", new
        {
            reportId,
            schedule = "Daily",
            format = "Link",
            recipientEmail = "subscriber@test.local",
            atTime = "08:00"
        });
        Assert.Equal(HttpStatusCode.Created, subRes.StatusCode);
        var sub   = await subRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var subId = sub!["id"]!.GetValue<int>();

        var siblingRoot = Path.Combine(_factory.TempDir, "scripts2");
        Directory.CreateDirectory(siblingRoot);
        var siblingScript = Path.Combine(siblingRoot, "outside_subscription.etlsql");
        await File.WriteAllTextAsync(siblingScript, "PRINT 'outside';");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var entity = await db.Subscriptions.FindAsync(subId);
            Assert.NotNull(entity);
            entity!.ScriptPath = siblingScript;
            await db.SaveChangesAsync();
        }

        var delRes = await AuthDelete(token, $"/api/subscriptions/{subId}");

        Assert.Equal(HttpStatusCode.Forbidden, delRes.StatusCode);
        Assert.True(File.Exists(siblingScript));

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            Assert.True(await db.Subscriptions.AnyAsync(s => s.Id == subId));
        }
    }

    // ── 6. Audit log ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AuditLog_RecordsLoginAndAdminActions()
    {
        var token = await GetAdminTokenAsync();

        var auditRes = await AuthGet(token, "/api/admin/audit?pageSize=200");
        Assert.Equal(HttpStatusCode.OK, auditRes.StatusCode);

        var body    = await auditRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var items   = body!["items"]!.AsArray();
        Assert.True(items.Count > 0, "Expected at least one audit log entry");
        Assert.Contains(items, item => item!["action"]!.GetValue<string>() == "LOGIN");
    }

    [Fact]
    public async Task AuditLog_CsvExport_ReturnsCsvFile()
    {
        var token = await GetAdminTokenAsync();

        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/admin/audit/export/csv");
        req.Headers.Authorization = new("Bearer", token);
        var res = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.StartsWith("text/csv", res.Content.Headers.ContentType?.MediaType);
        var csv = await res.Content.ReadAsStringAsync();
        Assert.Contains("Id,Timestamp,UserId", csv);
    }

    // ── 7. Custom maps ───────────────────────────────────────────────────────

    [Fact]
    public async Task CustomMap_RequiresAuthentication()
    {
        var res = await _client.GetAsync("/api/maps/custom?path=test.geojson");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task CustomMap_ReadsOnlyFromMapRoot()
    {
        var token = await GetAdminTokenAsync();
        var mapPath = Path.Combine(_factory.TempDir, "maps", "test.geojson");
        await File.WriteAllTextAsync(mapPath, """{"type":"FeatureCollection","features":[]}""");

        var res = await AuthGet(token, "/api/maps/custom?path=test.geojson");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("application/geo+json", res.Content.Headers.ContentType?.MediaType);
        var json = await res.Content.ReadAsStringAsync();
        Assert.Contains("FeatureCollection", json);
    }

    [Fact]
    public async Task CustomMap_DoesNotReadFromScriptRoot()
    {
        var token = await GetAdminTokenAsync();
        var scriptMapPath = Path.Combine(_factory.TempDir, "scripts", "script-root-map.geojson");
        await File.WriteAllTextAsync(scriptMapPath, """{"type":"FeatureCollection","features":[]}""");

        var res = await AuthGet(token, "/api/maps/custom?path=script-root-map.geojson");

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task CustomMap_RejectsTraversalAndUnsupportedExtensions()
    {
        var token = await GetAdminTokenAsync();

        var traversal = await AuthGet(token, "/api/maps/custom?path=../portal.db");
        Assert.Equal(HttpStatusCode.BadRequest, traversal.StatusCode);

        var geoJsonTraversal = await AuthGet(token, "/api/maps/custom?path=../outside.geojson");
        Assert.Equal(HttpStatusCode.Forbidden, geoJsonTraversal.StatusCode);

        var unsupported = await AuthGet(token, "/api/maps/custom?path=notes.txt");
        Assert.Equal(HttpStatusCode.BadRequest, unsupported.StatusCode);
    }

    [Fact]
    [Trait("Category", "Smoke.Security")]
    public async Task CustomMap_RejectsSiblingMapRootBypass()
    {
        var token = await GetAdminTokenAsync();
        var siblingRoot = Path.Combine(_factory.TempDir, "maps2");
        Directory.CreateDirectory(siblingRoot);
        await File.WriteAllTextAsync(
            Path.Combine(siblingRoot, "outside.geojson"),
            """{"type":"FeatureCollection","features":[]}""");

        var res = await AuthGet(token, "/api/maps/custom?path=../maps2/outside.geojson");

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    // ── 8. Operational hardening ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Smoke.Portal")]
    public async Task Snapshot_FailedRefresh_KeepsLastGoodSnapshot()
    {
        var token = await GetAdminTokenAsync();

        var folderRes = await AuthPost(token, "/api/folders", new { name = "Resilience Folder", parentId = (int?)null });
        var folder    = await folderRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var folderId  = folder!["id"]!.GetValue<int>();

        var scriptPath = Path.Combine(_factory.TempDir, "scripts", "resilience_report.rptsql");
        await File.WriteAllTextAsync(scriptPath, @"
CREATE VISUAL Answer AS CARD (
    SOURCE = (SELECT 42 AS Value),
    MAPPINGS (VALUE = Value)
);
");

        var publishRes = await AuthPost(token, "/api/reports", new
        {
            folderId, name = "Resilience Report", description = "", scriptPath
        });
        Assert.Equal(HttpStatusCode.Created, publishRes.StatusCode);
        var report   = await publishRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var reportId = report!["id"]!.GetValue<int>();

        // First execution — should succeed and produce a snapshot
        var exec1     = await AuthPost(token, $"/api/reports/{reportId}/execute", new { parameters = new Dictionary<string, string>() });
        Assert.Equal(HttpStatusCode.Accepted, exec1.StatusCode);
        var exec1Body = await exec1.Content.ReadFromJsonAsync<JsonObject>(_json);
        var job1      = await WaitForJobAsync(token, exec1Body!["jobId"]!.GetValue<string>());
        Assert.Equal("Completed", job1["status"]!.GetValue<string>());

        // Verify snapshot exists
        var snap1 = await AuthGet(token, $"/api/reports/{reportId}/snapshot?includeManifest=false");
        Assert.Equal(HttpStatusCode.OK, snap1.StatusCode);

        // Delete the script — next run will fail
        File.Delete(scriptPath);

        var exec2     = await AuthPost(token, $"/api/reports/{reportId}/execute", new { parameters = new Dictionary<string, string>() });
        Assert.Equal(HttpStatusCode.Accepted, exec2.StatusCode);
        var exec2Body = await exec2.Content.ReadFromJsonAsync<JsonObject>(_json);
        var job2      = await WaitForJobAsync(token, exec2Body!["jobId"]!.GetValue<string>());
        Assert.Equal("Failed", job2["status"]!.GetValue<string>());

        // Old snapshot must still be accessible despite the failed refresh
        var snap2 = await AuthGet(token, $"/api/reports/{reportId}/snapshot?includeManifest=false");
        Assert.Equal(HttpStatusCode.OK, snap2.StatusCode);
        var snapBody = await snap2.Content.ReadFromJsonAsync<JsonObject>(_json);
        Assert.NotNull(snapBody!["builtAt"]);

        // Catalog listing should surface Failed status and preserve snapshotBuiltAt
        var listRes = await AuthGet(token, $"/api/folders/{folderId}/reports");
        var reports = await listRes.Content.ReadFromJsonAsync<JsonArray>(_json);
        var listed  = reports!.Single(r => r!["id"]!.GetValue<int>() == reportId)!.AsObject();
        Assert.Equal("Failed", listed["lastRefreshStatus"]!.GetValue<string>());
        Assert.NotNull(listed["snapshotBuiltAt"]);
    }

    [Fact]
    [Trait("Category", "Smoke.Portal")]
    public async Task AuditLog_RecordsViewSnapshotExportAndSubscriptionEvents()
    {
        var token = await GetAdminTokenAsync();

        var folderRes = await AuthPost(token, "/api/folders", new { name = "Audit Events Folder", parentId = (int?)null });
        var folder    = await folderRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var folderId  = folder!["id"]!.GetValue<int>();

        var scriptPath = Path.Combine(_factory.TempDir, "scripts", "audit_events_report.rptsql");
        await File.WriteAllTextAsync(scriptPath, @"
CREATE VISUAL Summary AS TABLE (
    SOURCE = (SELECT 1 AS Id, 'Alpha' AS Name),
    MAPPINGS (Id = Id, Name = Name)
);
");

        var publishRes = await AuthPost(token, "/api/reports", new
        {
            folderId, name = "Audit Events Report", description = "", scriptPath
        });
        Assert.Equal(HttpStatusCode.Created, publishRes.StatusCode);
        var report   = await publishRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var reportId = report!["id"]!.GetValue<int>();

        // Execute to get a snapshot (required for CSV export)
        var execRes = await AuthPost(token, $"/api/reports/{reportId}/execute", new { parameters = new Dictionary<string, string>() });
        Assert.Equal(HttpStatusCode.Accepted, execRes.StatusCode);
        var execBody = await execRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var job      = await WaitForJobAsync(token, execBody!["jobId"]!.GetValue<string>());
        Assert.Equal("Completed", job["status"]!.GetValue<string>());

        // Trigger VIEW_SNAPSHOT
        var snapRes = await AuthGet(token, $"/api/reports/{reportId}/snapshot?includeManifest=false");
        Assert.Equal(HttpStatusCode.OK, snapRes.StatusCode);

        // Trigger EXPORT_CSV
        var csvRes = await AuthGet(token, $"/api/reports/{reportId}/export/csv");
        Assert.Equal(HttpStatusCode.OK, csvRes.StatusCode);

        // Trigger CREATE_SUBSCRIPTION and DELETE_SUBSCRIPTION
        var smtpRes = await AuthPost(token, "/api/admin/smtp", new
        {
            alias       = $"audit-smtp-{Guid.NewGuid():N}"[..16],
            host        = "smtp.test.local",
            port        = 587,
            username    = "user@test.local",
            password    = "smtppassword",
            fromAddress = "noreply@test.local",
            useSsl      = true
        });
        Assert.Equal(HttpStatusCode.OK, smtpRes.StatusCode);
        var smtpBody  = await smtpRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var smtpAlias = smtpBody!["alias"]!.GetValue<string>();

        var subRes = await AuthPost(token, "/api/subscriptions", new
        {
            reportId, schedule = "Daily", format = "Link", smtpAlias, recipientEmail = "audit@test.local", atTime = "09:00"
        });
        Assert.Equal(HttpStatusCode.Created, subRes.StatusCode);
        var subBody = await subRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var subId   = subBody!["id"]!.GetValue<int>();

        var delSubRes = await AuthDelete(token, $"/api/subscriptions/{subId}");
        Assert.Equal(HttpStatusCode.NoContent, delSubRes.StatusCode);

        // Verify all expected audit events exist
        var auditRes = await AuthGet(token, "/api/admin/audit?pageSize=500");
        Assert.Equal(HttpStatusCode.OK, auditRes.StatusCode);
        var auditBody = await auditRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var items     = auditBody!["items"]!.AsArray()
            .Select(i => i!["action"]!.GetValue<string>())
            .ToHashSet();

        Assert.Contains("VIEW_SNAPSHOT",       items);
        Assert.Contains("EXPORT_CSV",          items);
        Assert.Contains("CREATE_SUBSCRIPTION", items);
        Assert.Contains("DELETE_SUBSCRIPTION", items);
    }

    [Fact]
    [Trait("Category", "Smoke.Portal")]
    public async Task Subscription_WithParameters_PersistsAndRoundTrips()
    {
        var token = await GetAdminTokenAsync();

        var folderRes = await AuthPost(token, "/api/folders", new { name = "Parameterized Sub Folder", parentId = (int?)null });
        var folder    = await folderRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var folderId  = folder!["id"]!.GetValue<int>();

        var scriptPath = Path.Combine(_factory.TempDir, "scripts", "param_sub_report.rptsql");
        await File.WriteAllTextAsync(scriptPath, "DECLARE @Region STRING INPUT = 'All';");

        var publishRes = await AuthPost(token, "/api/reports", new
        {
            folderId, name = "Param Sub Report", description = "", scriptPath
        });
        Assert.Equal(HttpStatusCode.Created, publishRes.StatusCode);
        var report   = await publishRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var reportId = report!["id"]!.GetValue<int>();

        var smtpRes = await AuthPost(token, "/api/admin/smtp", new
        {
            alias       = $"param-smtp-{Guid.NewGuid():N}"[..16],
            host        = "smtp.test.local",
            port        = 587,
            username    = "user@test.local",
            password    = "smtppassword",
            fromAddress = "noreply@test.local",
            useSsl      = true
        });
        Assert.Equal(HttpStatusCode.OK, smtpRes.StatusCode);
        var smtpBody  = await smtpRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var smtpAlias = smtpBody!["alias"]!.GetValue<string>();

        var subRes = await AuthPost(token, "/api/subscriptions", new
        {
            reportId,
            schedule       = "Daily",
            format         = "Link",
            smtpAlias,
            recipientEmail = "regional@test.local",
            atTime         = "07:00",
            parameters     = new Dictionary<string, string> { ["Region"] = "EMEA" }
        });
        Assert.Equal(HttpStatusCode.Created, subRes.StatusCode);
        var subBody = await subRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var subId   = subBody!["id"]!.GetValue<int>();

        // Verify the GET round-trip preserves parameters
        var getRes  = await AuthGet(token, $"/api/subscriptions/{subId}");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var getBody = await getRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var param   = getBody!["parameters"]!.AsObject()["Region"]!.GetValue<string>();
        Assert.Equal("EMEA", param);

        // Update the parameter value
        var updateRes = await AuthPut(token, $"/api/subscriptions/{subId}", new
        {
            parameters = new Dictionary<string, string> { ["Region"] = "NA" }
        });
        Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);
        var updatedBody  = await updateRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var updatedParam = updatedBody!["parameters"]!.AsObject()["Region"]!.GetValue<string>();
        Assert.Equal("NA", updatedParam);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a valid admin JWT, changing the default password once on first call.
    /// Shared across all test instances via a static field to prevent re-login accumulation.
    /// </summary>
    private async Task<string> GetAdminTokenAsync()
    {
        await _tokenLock.WaitAsync();
        try
        {
            if (_adminToken is not null) return _adminToken;

            // Login with the initial seeded password
            var loginRes = await _client.PostAsJsonAsync("/api/auth/login", new
            {
                username = "admin",
                password = "Admin@12345!"
            });
            loginRes.EnsureSuccessStatusCode();
            var body  = await loginRes.Content.ReadFromJsonAsync<JsonObject>(_json);
            var token = body!["token"]!.GetValue<string>();

            // Change password once so subsequent tests don't trigger lockout
            using var cpReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password");
            cpReq.Headers.Authorization = new("Bearer", token);
            cpReq.Content = JsonContent.Create(new
            {
                currentPassword = "Admin@12345!",
                newPassword     = "Admin@Tests99!"
            });
            var cpRes = await _client.SendAsync(cpReq);
            cpRes.EnsureSuccessStatusCode();

            // Re-login to get a fresh token that works without MustChangePassword block
            var reloginRes = await _client.PostAsJsonAsync("/api/auth/login", new
            {
                username = "admin",
                password = "Admin@Tests99!"
            });
            reloginRes.EnsureSuccessStatusCode();
            var reloginBody = await reloginRes.Content.ReadFromJsonAsync<JsonObject>(_json);
            _adminToken = reloginBody!["token"]!.GetValue<string>();

            return _adminToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task<string> GetFreshViewerTokenAsync()
    {
        var adminToken = await GetAdminTokenAsync();
        var username = $"share_viewer_{Guid.NewGuid():N}"[..20];
        const string initialPassword = "Viewer@Test1!";
        const string changedPassword = "Viewer@Test2!";

        var createRes = await AuthPost(adminToken, "/api/admin/users", new
        {
            username,
            email = $"{username}@test.local",
            password = initialPassword,
            role = "Viewer"
        });
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);

        var loginRes = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            username,
            password = initialPassword
        });
        Assert.Equal(HttpStatusCode.OK, loginRes.StatusCode);
        var loginBody = await loginRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var token = loginBody!["token"]!.GetValue<string>();

        using var changePassword = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password");
        changePassword.Headers.Authorization = new("Bearer", token);
        changePassword.Content = JsonContent.Create(new
        {
            currentPassword = initialPassword,
            newPassword = changedPassword
        });
        var changeRes = await _client.SendAsync(changePassword);
        Assert.Equal(HttpStatusCode.NoContent, changeRes.StatusCode);

        var reloginRes = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            username,
            password = changedPassword
        });
        Assert.Equal(HttpStatusCode.OK, reloginRes.StatusCode);
        var reloginBody = await reloginRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        return reloginBody!["token"]!.GetValue<string>();
    }

    private Task<HttpResponseMessage> AuthGet(string token, string url)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new("Bearer", token);
        return _client.SendAsync(req);
    }

    private Task<HttpResponseMessage> AuthPost(string token, string url, object body)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new("Bearer", token);
        req.Content = JsonContent.Create(body);
        return _client.SendAsync(req);
    }

    private Task<HttpResponseMessage> AuthPut(string token, string url, object body)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, url);
        req.Headers.Authorization = new("Bearer", token);
        req.Content = JsonContent.Create(body);
        return _client.SendAsync(req);
    }

    private Task<HttpResponseMessage> AuthDelete(string token, string url)
    {
        var req = new HttpRequestMessage(HttpMethod.Delete, url);
        req.Headers.Authorization = new("Bearer", token);
        return _client.SendAsync(req);
    }

    private async Task<JsonObject> WaitForJobAsync(string token, string jobId)
    {
        JsonObject? job = null;
        for (var i = 0; i < 50; i++)
        {
            var jobRes = await AuthGet(token, $"/api/jobs/{jobId}");
            Assert.Equal(HttpStatusCode.OK, jobRes.StatusCode);
            job = await jobRes.Content.ReadFromJsonAsync<JsonObject>(_json);
            var status = job!["status"]!.GetValue<string>();
            if (status is "Completed" or "Failed" or "Cancelled")
                return job;

            await Task.Delay(100);
        }

        Assert.NotNull(job);
        return job!;
    }

    private static string GetGoldenWorkflowPath()
    {
        var candidates = new[]
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory
        };

        foreach (var start in candidates)
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, "samples", "golden_workflow", "golden_workflow.rptsql");
                if (File.Exists(candidate))
                    return candidate;

                dir = dir.Parent;
            }
        }

        throw new FileNotFoundException("Could not locate samples/golden_workflow/golden_workflow.rptsql.");
    }
}
