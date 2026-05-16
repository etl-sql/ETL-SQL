using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
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
[Trait("Category", "Integration")]
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

        var scriptPath = Path.Combine(_factory.TempDir, "scripts", "execute_report.rptsql");
        await File.WriteAllTextAsync(scriptPath, @"
CREATE VISUAL Total AS CARD (
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

        var recentRes = await AuthGet(token, "/api/catalog/recent?limit=5");
        Assert.Equal(HttpStatusCode.OK, recentRes.StatusCode);
        var recent = await recentRes.Content.ReadFromJsonAsync<JsonArray>(_json);
        var recentHit = recent!.Single(r => r!["id"]!.GetValue<int>() == reportId)!.AsObject();
        Assert.Equal("Executable Report", recentHit["name"]!.GetValue<string>());
        Assert.True(recentHit["hasSnapshot"]!.GetValue<bool>());
        Assert.False(recentHit["isStale"]!.GetValue<bool>());
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
