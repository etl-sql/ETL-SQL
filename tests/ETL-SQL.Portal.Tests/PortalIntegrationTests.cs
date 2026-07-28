using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Storage;
using ETL_SQL.Portal;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Models;
using ETL_SQL.Portal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// End-to-end integration tests for the Portal.
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
    private static string? _adminToken;
    private static readonly SemaphoreSlim _tokenLock = new(1, 1);

    public PortalIntegrationTests(PortalWebFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
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

    [Fact]
    [Trait("Category", "Smoke.Portal")]
    public async Task Healthz_ReturnsOkWithLightweightDependencyChecks()
    {
        var res = await _client.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await res.Content.ReadFromJsonAsync<JsonObject>(_json);
        Assert.NotNull(body);
        Assert.Equal("Healthy", body!["status"]?.GetValue<string>());
        Assert.Equal("Standalone", body["mode"]?.GetValue<string>());
        var checks = body["checks"]!.AsObject();
        Assert.Equal("ok", checks["database"]?.GetValue<string>());
        Assert.Equal("ok", checks["storage"]?.GetValue<string>());
        Assert.Equal("ok", checks["lease"]?.GetValue<string>());
        Assert.Equal("ok", checks["topology"]?.GetValue<string>());
        Assert.Empty(body["findings"]!.AsArray());
    }

    [Fact]
    [Trait("Category", "Smoke.Portal")]
    public async Task Healthz_FailsClosedWhenHaTopologyUsesStandaloneState()
    {
        using var factory = new HaExpectedWithStandaloneStateFactory();
        using var client = factory.CreateClient();

        var res = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonObject>(_json);
        Assert.NotNull(body);
        Assert.Equal("Unhealthy", body!["status"]?.GetValue<string>());
        Assert.Equal("HighAvailability", body["mode"]?.GetValue<string>());
        var checks = body["checks"]!.AsObject();
        Assert.NotEqual("ok", checks["topology"]?.GetValue<string>());
        var findings = body["findings"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();
        Assert.Contains("ha-requires-portal-postgres", findings);
        Assert.Contains("ha-requires-orchestrator-postgres", findings);
        Assert.Contains("ha-requires-shared-key-ring", findings);
    }

    [Fact]
    public async Task DocumentationHub_SearchesMarkdownLibrary()
    {
        var indexResponse = await _client.GetAsync("/api/docs/index");
        Assert.Equal(HttpStatusCode.OK, indexResponse.StatusCode);
        var index = await indexResponse.Content.ReadFromJsonAsync<JsonArray>(_json);
        Assert.NotNull(index);
        Assert.Contains(index!, item => item!["path"]!.GetValue<string>() == "README.md");

        var searchResponse = await _client.GetAsync("/api/docs/search?q=portal");
        Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);
        var results = await searchResponse.Content.ReadFromJsonAsync<JsonArray>(_json);
        Assert.NotNull(results);
        Assert.NotEmpty(results!);

        var docResponse = await _client.GetAsync("/api/docs/document?path=README.md");
        Assert.Equal(HttpStatusCode.OK, docResponse.StatusCode);
        var doc = await docResponse.Content.ReadFromJsonAsync<JsonObject>(_json);
        Assert.Equal("README.md", doc!["path"]!.GetValue<string>());
        Assert.Contains("# ETL-SQL Documentation", doc["markdown"]!.GetValue<string>());

        var traversal = await _client.GetAsync("/api/docs/document?path=../appsettings.json");
        Assert.Equal(HttpStatusCode.NotFound, traversal.StatusCode);
    }

    [Fact]
    [Trait("Category", "Smoke.Portal")]
    public async Task DesignerAnalyze_ReturnsRealLinterDiagnostics()
    {
        var token = await GetAdminTokenAsync();

        var res = await AuthPost(token, "/api/designer/analyze", new { script = "SELECT * FROM #stage;" });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonObject>(_json);
        var diagnostics = body!["diagnostics"]!.AsArray();
        var selectStar = diagnostics
            .Select(d => d!.AsObject())
            .Single(d => d["code"]!.GetValue<string>() == "AvoidSelectStar");
        Assert.Equal("ETL-SQL Linter", selectStar["source"]!.GetValue<string>());
    }

    [Fact]
    public async Task DesignerComplete_ReturnsLanguageSuggestions()
    {
        var token = await GetAdminTokenAsync();

        var res = await AuthPost(token, "/api/designer/complete", new
        {
            script = "SEL",
            line = 0,
            column = 3
        });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonObject>(_json);
        var items = body!["items"]!.AsArray();
        Assert.Contains(items, item =>
            item!["label"]!.GetValue<string>() == "SELECT" &&
            item["kind"]!.GetValue<string>() == "keyword");
    }

    [Fact]
    public async Task DocumentationHub_RespectsModuleFlag()
    {
        using var factory = new DocumentationDisabledFactory();
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/docs")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/docs.html")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/docs/index")).StatusCode);
    }

    [Fact]
    public async Task ArtifactStorage_RejectsStaleWriterThroughPortalDi()
    {
        using var scope = _factory.Services.CreateScope();
        var epochs = scope.ServiceProvider.GetRequiredService<IWriteEpochStore>();
        var storage = scope.ServiceProvider.GetRequiredService<IArtifactStorage>();

        Assert.True(await epochs.TryClaimWriteEpochAsync(
            "artifact", "Scripts/fenced.rptsql", long.MaxValue));

        await Assert.ThrowsAsync<FencedWriteException>(() =>
            storage.WriteAllTextAsync(ArtifactArea.Scripts, "fenced.rptsql", "SET REPORT TITLE = 'Stale';"));
    }

    [Fact]
    public async Task Health_EmitsLoadBalancerAffinityCookie()
    {
        var res = await _client.GetAsync("/health");

        Assert.True(res.Headers.TryGetValues("Set-Cookie", out var values));
        Assert.Contains(values, value =>
            value.StartsWith("ETLSQL_PORTAL_AFFINITY=", StringComparison.Ordinal)
            && value.Contains("HttpOnly", StringComparison.OrdinalIgnoreCase)
            && value.Contains("Path=/", StringComparison.OrdinalIgnoreCase));
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
            email = $"{username}@test.local",
            password = "Active@Test1!",
            role = "Viewer"
        });
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var created = await createRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var userId = created!["id"]!.GetValue<int>();

        var activeLoginRes = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            username,
            password = "Active@Test1!"
        });
        Assert.Equal(HttpStatusCode.OK, activeLoginRes.StatusCode);
        var activeLogin = await activeLoginRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var accessToken = activeLogin!["token"]!.GetValue<string>();
        var refreshToken = activeLogin!["refreshToken"]!.GetValue<string>();

        // Deactivate the user via admin PUT (versioned mutations return 200 with the new version).
        var deactivateRes = await AuthPut(adminToken, $"/api/admin/users/{userId}",
            new { isActive = false });
        Assert.Equal(HttpStatusCode.OK, deactivateRes.StatusCode);

        // Login attempt on inactive account must return 401 (not 500 or 403).
        var loginRes = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            username,
            password = "Active@Test1!"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, loginRes.StatusCode);

        var refreshRes = await _client.PostAsJsonAsync("/api/auth/refresh", new
        {
            refreshToken
        });
        Assert.Equal(HttpStatusCode.Unauthorized, refreshRes.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await AuthGet(accessToken, "/api/folders")).StatusCode);
    }

    [Fact]
    public async Task Login_AfterExcessiveFailures_Returns429()
    {
        var adminToken = await GetAdminTokenAsync();

        var username = $"lockout_{Guid.NewGuid():N}"[..17];
        var createRes = await AuthPost(adminToken, "/api/admin/users", new
        {
            username,
            email = $"{username}@test.local",
            password = "Lockout@Test1!",
            role = "Viewer"
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
            email = $"{newUser}@test.local",
            password = "MustChange@1!",
            role = "Viewer"
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
            newPassword = "Changed@9999!"
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
        var freshToken = reloginBody!["token"]!.GetValue<string>();

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
            email = "viewer@test.local",
            password = "Viewer@1234!",
            role = "Viewer"
        });
        Assert.Equal(HttpStatusCode.Created, createUserRes.StatusCode);

        // Create a folder
        var folderRes = await AuthPost(token, "/api/folders", new
        {
            name = "Test Folder",
            parentId = (int?)null
        });
        Assert.Equal(HttpStatusCode.Created, folderRes.StatusCode);
        var folder = await folderRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var folderId = folder!["id"]!.GetValue<int>();
        Assert.True(folderId > 0);
    }

    [Fact]
    public async Task AdminCatalogs_FilterPageAndBulkMutateUsersGroupsMembersAndSubscriptions()
    {
        var token = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var usernames = Enumerable.Range(1, 3).Select(i => $"catalog_{suffix}_{i}").ToList();
        var userIds = new List<int>();

        foreach (var username in usernames)
        {
            var createRes = await AuthPost(token, "/api/admin/users", new
            {
                username,
                email = $"{username}@test.local",
                password = "Catalog@Tests99!",
                role = "Viewer"
            });
            Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
            var user = await createRes.Content.ReadFromJsonAsync<JsonObject>(_json);
            userIds.Add(user!["id"]!.GetValue<int>());
        }

        var userPage1 = await AuthGet(token, $"/api/admin/users/catalog?q=catalog_{suffix}&page=1&pageSize=2");
        var userPage1Body = await userPage1.Content.ReadFromJsonAsync<PagedResult<UserDto>>(_json);
        Assert.Equal(HttpStatusCode.OK, userPage1.StatusCode);
        Assert.Equal(3, userPage1Body!.Total);
        Assert.Equal(2, userPage1Body.Items.Count);

        var userPage2 = await AuthGet(token, $"/api/admin/users/catalog?q=catalog_{suffix}&page=2&pageSize=2");
        var userPage2Body = await userPage2.Content.ReadFromJsonAsync<PagedResult<UserDto>>(_json);
        Assert.Single(userPage2Body!.Items);

        // Bulk operations carry per-item {id, version} so stale items conflict individually.
        var disableRes = await AuthPost(token, "/api/admin/users/bulk-status", new
        {
            users = userIds.Take(2).Select(id => new { id, version = 1 }).ToArray(),
            isActive = false
        });
        Assert.Equal(HttpStatusCode.OK, disableRes.StatusCode);
        var inactiveUsers = await AuthGet(token, $"/api/admin/users/catalog?q=catalog_{suffix}&status=inactive");
        var inactiveUsersBody = await inactiveUsers.Content.ReadFromJsonAsync<PagedResult<UserDto>>(_json);
        Assert.Equal(2, inactiveUsersBody!.Total);

        var groupName = $"Catalog Group {suffix}";
        var groupRes = await AuthPost(token, "/api/admin/groups", new { name = groupName, description = "Catalog test group" });
        Assert.Equal(HttpStatusCode.Created, groupRes.StatusCode);
        var group = await groupRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var groupId = group!["id"]!.GetValue<int>();

        var addMembersRes = await AuthPost(token, $"/api/admin/groups/{groupId}/members/bulk-add", new { userIds });
        Assert.Equal(HttpStatusCode.OK, addMembersRes.StatusCode);
        var memberPage = await AuthGet(token, $"/api/admin/groups/{groupId}/members/catalog?q=catalog_{suffix}&pageSize=2");
        var memberPageBody = await memberPage.Content.ReadFromJsonAsync<PagedResult<GroupMemberDto>>(_json);
        Assert.Equal(3, memberPageBody!.Total);
        Assert.Equal(2, memberPageBody.Items.Count);

        var removeMembersRes = await AuthPost(token, $"/api/admin/groups/{groupId}/members/bulk-remove", new
        {
            userIds = userIds.Take(2).ToArray()
        });
        Assert.Equal(HttpStatusCode.OK, removeMembersRes.StatusCode);
        var remainingMembers = await AuthGet(token, $"/api/admin/groups/{groupId}/members/catalog");
        var remainingMembersBody = await remainingMembers.Content.ReadFromJsonAsync<PagedResult<GroupMemberDto>>(_json);
        Assert.Single(remainingMembersBody!.Items);

        var groupCatalog = await AuthGet(token, $"/api/admin/groups/catalog?q={Uri.EscapeDataString(suffix)}");
        var groupCatalogBody = await groupCatalog.Content.ReadFromJsonAsync<PagedResult<GroupDto>>(_json);
        Assert.Contains(groupCatalogBody!.Items, item => item.Id == groupId);

        var emptyGroupIds = new List<int>();
        foreach (var index in Enumerable.Range(1, 2))
        {
            var emptyGroupRes = await AuthPost(token, "/api/admin/groups", new
            {
                name = $"Empty Catalog Group {suffix} {index}",
                description = ""
            });
            Assert.Equal(HttpStatusCode.Created, emptyGroupRes.StatusCode);
            var emptyGroup = await emptyGroupRes.Content.ReadFromJsonAsync<JsonObject>(_json);
            emptyGroupIds.Add(emptyGroup!["id"]!.GetValue<int>());
        }
        var deleteGroupsRes = await AuthPost(token, "/api/admin/groups/bulk-delete", new
        {
            groups = emptyGroupIds.Select(id => new { id, version = 1 }).ToArray()
        });
        Assert.Equal(HttpStatusCode.OK, deleteGroupsRes.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AuthGet(token, $"/api/admin/groups/{emptyGroupIds[0]}")).StatusCode);

        var folderRes = await AuthPost(token, "/api/folders", new { name = $"Catalog Subs {suffix}", parentId = (int?)null });
        Assert.Equal(HttpStatusCode.Created, folderRes.StatusCode);
        var folder = await folderRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var scriptPath = Path.Combine(_factory.TempDir, "scripts", $"catalog_subs_{suffix}.rptsql");
        await File.WriteAllTextAsync(scriptPath, "CREATE VISUAL Answer AS CARD (SOURCE = (SELECT 1 AS Value), MAPPINGS (VALUE = Value));");
        var reportRes = await AuthPost(token, "/api/reports", new
        {
            folderId = folder!["id"]!.GetValue<int>(),
            name = $"Catalog Subscription Report {suffix}",
            description = "",
            scriptPath
        });
        Assert.Equal(HttpStatusCode.Created, reportRes.StatusCode);
        var report = await reportRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var reportId = report!["id"]!.GetValue<int>();

        var subscriptionIds = new List<int>();
        foreach (var index in Enumerable.Range(1, 2))
        {
            var subRes = await AuthPost(token, "/api/subscriptions", new
            {
                reportId,
                name = $"Catalog Subscription {suffix} {index}",
                schedule = "Daily",
                format = "Link",
                recipientEmail = $"catalog-sub-{suffix}-{index}@test.local",
                atTime = "08:00"
            });
            Assert.Equal(HttpStatusCode.Created, subRes.StatusCode);
            var sub = await subRes.Content.ReadFromJsonAsync<JsonObject>(_json);
            subscriptionIds.Add(sub!["id"]!.GetValue<int>());
        }

        var subscriptionCatalog = await AuthGet(token, $"/api/admin/subscriptions/catalog?q={Uri.EscapeDataString(suffix)}&pageSize=1");
        var subscriptionCatalogBody = await subscriptionCatalog.Content.ReadFromJsonAsync<PagedResult<SubscriptionDto>>(_json);
        Assert.Equal(2, subscriptionCatalogBody!.Total);
        Assert.Single(subscriptionCatalogBody.Items);

        var pauseRes = await AuthPost(token, "/api/admin/subscriptions/bulk-status", new
        {
            subscriptions = subscriptionIds.Select(id => new { id, version = 1 }).ToArray(),
            isActive = false
        });
        Assert.Equal(HttpStatusCode.OK, pauseRes.StatusCode);
        var pausedCatalog = await AuthGet(token, $"/api/admin/subscriptions/catalog?q={Uri.EscapeDataString(suffix)}&status=paused");
        var pausedCatalogBody = await pausedCatalog.Content.ReadFromJsonAsync<PagedResult<SubscriptionDto>>(_json);
        Assert.Equal(2, pausedCatalogBody!.Total);
    }

    // ── 4. Report publish ──────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Smoke.Portal")]
    public async Task Report_PublishAndGet_RoundTrips()
    {
        var token = await GetAdminTokenAsync();

        // Create folder
        var folderRes = await AuthPost(token, "/api/folders", new { name = "Rpt Folder", parentId = (int?)null });
        var folder = await folderRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var folderId = folder!["id"]!.GetValue<int>();

        // Write a dummy .rptsql file in the temp script root
        var scriptPath = Path.Combine(_factory.TempDir, "scripts", "dummy_report.rptsql");
        await File.WriteAllTextAsync(scriptPath,
            "/* @owner: Finance BI; @contact: finance-bi@example.com; @tags: revenue,monthly; @category: Finance; @certification: trusted */\n" +
            "-- dummy report\n");

        var publishRes = await AuthPost(token, "/api/reports", new
        {
            folderId = folderId,
            name = "Dummy Report",
            description = "Integration test report",
            scriptPath = scriptPath
        });
        Assert.Equal(HttpStatusCode.Created, publishRes.StatusCode);
        var report = await publishRes.Content.ReadFromJsonAsync<JsonObject>(_json);
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
        await File.WriteAllTextAsync(scriptPath,
            $"SELECT OrderId INTO #stage_{suffix} FROM sales.Orders_{suffix};\n" +
            "SET REPORT TITLE = 'Lineage Catalog';\n");

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
        var stageName = $"#stage_{suffix}";
        var visualTarget = $"report:SalesCard_{suffix}";
        var stewardQueueTarget = $"warehouse.Customer_{suffix}";
        var cycleA = $"cycle.A_{suffix}";
        var cycleB = $"cycle.B_{suffix}";

        using (var scope = _factory.Services.CreateScope())
        {
            var catalog = scope.ServiceProvider.GetRequiredService<ILineageCatalogStore>();
            var stageEntry = new LineageEntry(stageName, "SELECT")
            {
                TargetColumn = "OrderId",
                SourceTables = new List<string> { $"sales.Orders_{suffix}" },
                SourceColumns = new List<string> { "order_id" },
                TransformationKind = TransformationKind.Cast,
                TransformationExpression = "CAST(order_id AS INT)",
                FunctionsApplied = new List<string> { "CAST" },
                DerivedFromDescriptions = "order_id: ERP order key",
                Metadata = new Dictionary<string, string> { ["pii"] = "true", ["owner"] = "SalesOps" },
                SourceFile = scriptPath,
                Line = 3
            };
            var visualEntry = new LineageEntry(visualTarget, "CREATE VISUAL")
            {
                SourceTables = new List<string> { stageName },
                Metadata = new Dictionary<string, string> { ["owner"] = "SalesOps" },
                SourceFile = scriptPath,
                Line = 8
            };
            var oldPiiEntry = new LineageEntry("#old_stage", "SELECT")
            {
                SourceTables = new List<string> { $"legacy.Orders_{suffix}" },
                Metadata = new Dictionary<string, string> { ["pii"] = "true" },
                SourceFile = scriptPath,
                Line = 11
            };
            var stewardQueueEntry = new LineageEntry(stewardQueueTarget, "SELECT")
            {
                SourceTables = new List<string> { $"crm.Customer_{suffix}" },
                Metadata = new Dictionary<string, string>
                {
                    ["owner"] = "MarketingOps",
                    ["steward"] = "DataSteward",
                    ["domain"] = "crm",
                    ["classification"] = "restricted",
                    ["pii"] = "true"
                },
                SourceFile = scriptPath,
                Line = 15
            };
            var cycleEntryA = new LineageEntry(cycleA, "SELECT")
            {
                SourceTables = new List<string> { cycleB },
                Metadata = new Dictionary<string, string> { ["steward"] = "CycleSteward" },
                SourceFile = scriptPath,
                Line = 20
            };
            var cycleEntryB = new LineageEntry(cycleB, "SELECT")
            {
                SourceTables = new List<string> { cycleA },
                Metadata = new Dictionary<string, string> { ["steward"] = "CycleSteward" },
                SourceFile = scriptPath,
                Line = 21
            };

            await catalog.SaveLineageAsync(
                new[] { stageEntry, visualEntry, stewardQueueEntry, cycleEntryA, cycleEntryB },
                $"report:{reportId}:manual-session",
                scriptPath,
                DateTime.UtcNow);
            await catalog.SaveLineageAsync(
                new[] { oldPiiEntry },
                $"report:{reportId}:old-session",
                scriptPath,
                DateTime.UtcNow.AddDays(-10));

            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var alert = new ReportAlert
            {
                ReportId = reportId,
                OwnerId = 1,
                Name = $"LineageAlert_{suffix}",
                VisualName = $"SalesCard_{suffix}",
                Operator = ">",
                Threshold = 100
            };
            alert.Notifications.Add(new AlertNotification
            {
                OrchestratorAlias = "lineage_orch",
                NotificationName = $"NotifySales_{suffix}"
            });
            db.ReportAlerts.Add(alert);
            await db.SaveChangesAsync();

            var notifier = scope.ServiceProvider.GetRequiredService<LineageStewardNotificationService>();
            await notifier.NotifyAsync(1, reportId, $"report:{reportId}:manual-session", scriptPath, [stewardQueueEntry], CancellationToken.None);
        }

        var sourceRes = await AuthGet(token, $"/api/catalog/lineage/source?name={Uri.EscapeDataString($"sales.Orders_{suffix}")}");
        Assert.Equal(HttpStatusCode.OK, sourceRes.StatusCode);
        var sourceRows = await sourceRes.Content.ReadFromJsonAsync<JsonArray>(_json);
        var sourceHit = Assert.Single(sourceRows!);
        Assert.Equal(stageName, sourceHit!["targetTable"]!.GetValue<string>());
        Assert.Equal($"Lineage Report {suffix}", sourceHit["reportName"]!.GetValue<string>());
        Assert.Equal($"/Lineage {suffix}", sourceHit["folderPath"]!.GetValue<string>());

        var tableRes = await AuthGet(token, $"/api/catalog/lineage/table?name={Uri.EscapeDataString(visualTarget)}");
        Assert.Equal(HttpStatusCode.OK, tableRes.StatusCode);
        var tableRows = await tableRes.Content.ReadFromJsonAsync<JsonArray>(_json);
        Assert.Contains(tableRows!, r =>
            r!["reportId"]!.GetValue<int>() == reportId &&
            r["sourceTables"]!.AsArray().Any(s => s!.GetValue<string>() == stageName));

        var columnRes = await AuthGet(token, $"/api/catalog/lineage/table?name={Uri.EscapeDataString(stageName)}&column=OrderId");
        Assert.Equal(HttpStatusCode.OK, columnRes.StatusCode);
        var columnRows = await columnRes.Content.ReadFromJsonAsync<JsonArray>(_json);
        var columnHit = Assert.Single(columnRows!);
        Assert.Equal("OrderId", columnHit!["targetColumn"]!.GetValue<string>());
        // Rich persisted fields surface through the catalog lineage DTO.
        Assert.Equal("CAST(order_id AS INT)", columnHit["transformationExpression"]!.GetValue<string>());
        Assert.Equal("Cast", columnHit["transformationKind"]!.GetValue<string>());
        Assert.Equal("order_id: ERP order key", columnHit["derivedFromDescriptions"]!.GetValue<string>());
        Assert.Contains(columnHit["sourceColumns"]!.AsArray(), n => n!.GetValue<string>() == "order_id");

        var tagRes = await AuthGet(token, "/api/catalog/lineage/tag?key=pii&value=true");
        Assert.Equal(HttpStatusCode.OK, tagRes.StatusCode);
        var tagRows = await tagRes.Content.ReadFromJsonAsync<JsonArray>(_json);
        Assert.Contains(tagRows!, r =>
            r!["targetTable"]!.GetValue<string>() == stageName &&
            r["tags"]!["owner"]!.GetValue<string>() == "SalesOps");

        var from = Uri.EscapeDataString(DateTime.UtcNow.AddDays(-1).ToString("O"));
        var recentTagRes = await AuthGet(token, $"/api/catalog/lineage/tag?key=pii&value=true&from={from}");
        Assert.Equal(HttpStatusCode.OK, recentTagRes.StatusCode);
        var recentTagRows = await recentTagRes.Content.ReadFromJsonAsync<JsonArray>(_json);
        Assert.DoesNotContain(recentTagRows!, r => r!["targetTable"]!.GetValue<string>() == "#old_stage");

        var sourceFileRes = await AuthGet(token, $"/api/catalog/lineage/source-file?path={Uri.EscapeDataString(scriptPath)}");
        Assert.Equal(HttpStatusCode.OK, sourceFileRes.StatusCode);
        var sourceFileRows = await sourceFileRes.Content.ReadFromJsonAsync<JsonArray>(_json);
        Assert.True(sourceFileRows!.Count >= 2);

        var missingRes = await AuthGet(token, $"/api/catalog/stewardship?view=missing&q={Uri.EscapeDataString(suffix)}");
        Assert.Equal(HttpStatusCode.OK, missingRes.StatusCode);
        var missing = await missingRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        Assert.True(missing!["summary"]!["missingMetadataAssets"]!.GetValue<int>() >= 1);
        Assert.Contains(missing["items"]!.AsArray(), r =>
            r!["targetTable"]!.GetValue<string>() == stageName &&
            r["missingTags"]!.AsArray().Any(t => t!.GetValue<string>() == "steward"));

        var sensitiveRes = await AuthGet(token, $"/api/catalog/stewardship?view=sensitive&q={Uri.EscapeDataString(suffix)}");
        Assert.Equal(HttpStatusCode.OK, sensitiveRes.StatusCode);
        var sensitive = await sensitiveRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        Assert.Contains(sensitive!["items"]!.AsArray(), r =>
            r!["targetTable"]!.GetValue<string>() == stewardQueueTarget &&
            r["isRestricted"]!.GetValue<bool>());

        var staleRes = await AuthGet(token, $"/api/catalog/stewardship?view=stale&q=old_stage&staleAfterDays=1");
        Assert.Equal(HttpStatusCode.OK, staleRes.StatusCode);
        var stale = await staleRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        Assert.Contains(stale!["items"]!.AsArray(), r =>
            r!["targetTable"]!.GetValue<string>() == "#old_stage" &&
            r["isStale"]!.GetValue<bool>());

        var queueRes = await AuthGet(token, "/api/catalog/stewardship?view=queue&steward=DataSteward");
        Assert.Equal(HttpStatusCode.OK, queueRes.StatusCode);
        var queue = await queueRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        Assert.True(queue!["summary"]!["stewardQueueAssets"]!.GetValue<int>() >= 1);
        Assert.Contains(queue["items"]!.AsArray(), r =>
            r!["targetTable"]!.GetValue<string>() == stewardQueueTarget &&
            r["steward"]!.GetValue<string>() == "DataSteward");

        var impactRes = await AuthGet(token, $"/api/catalog/impact?kind=table&name={Uri.EscapeDataString($"sales.Orders_{suffix}")}&direction=downstream&depth=4");
        Assert.Equal(HttpStatusCode.OK, impactRes.StatusCode);
        var impact = await impactRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        Assert.True(impact!["summary"]!["tables"]!.GetValue<int>() >= 2);
        Assert.Contains(impact["tables"]!.AsArray(), r =>
            r!["name"]!.GetValue<string>() == visualTarget);
        Assert.Contains(impact["reports"]!.AsArray(), r =>
            r!["name"]!.GetValue<string>() == $"Lineage Report {suffix}");
        Assert.True(impact["summary"]!["alerts"]!.GetValue<int>() >= 1);
        Assert.Contains(impact["alerts"]!.AsArray(), r =>
            r!["name"]!.GetValue<string>() == $"LineageAlert_{suffix}" &&
            r["detail"]!.GetValue<string>().Contains($"lineage_orch.NotifySales_{suffix}", StringComparison.Ordinal));

        var reportImpactRes = await AuthGet(token, $"/api/catalog/impact?kind=report&name={Uri.EscapeDataString($"Lineage Report {suffix}")}&direction=both&depth=4");
        Assert.Equal(HttpStatusCode.OK, reportImpactRes.StatusCode);
        var reportImpact = await reportImpactRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        Assert.Contains(reportImpact!["tables"]!.AsArray(), r =>
            r!["name"]!.GetValue<string>() == $"sales.Orders_{suffix}");
        Assert.Contains(reportImpact["alerts"]!.AsArray(), r =>
            r!["name"]!.GetValue<string>() == $"LineageAlert_{suffix}");

        var stewardImpactRes = await AuthGet(token, "/api/catalog/impact?kind=steward&name=DataSteward&direction=both&depth=2");
        Assert.Equal(HttpStatusCode.OK, stewardImpactRes.StatusCode);
        var stewardImpact = await stewardImpactRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        Assert.Contains(stewardImpact!["stewards"]!.AsArray(), r =>
            r!["type"]!.GetValue<string>() == "Steward" &&
            r["name"]!.GetValue<string>() == "DataSteward");

        var cycleImpactRes = await AuthGet(token, $"/api/catalog/impact?kind=table&name={Uri.EscapeDataString(cycleA)}&direction=both&depth=8");
        Assert.Equal(HttpStatusCode.OK, cycleImpactRes.StatusCode);
        var cycleImpact = await cycleImpactRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        Assert.True(cycleImpact!["summary"]!["tables"]!.GetValue<int>() <= 2);
        Assert.Contains(cycleImpact["tables"]!.AsArray(), r =>
            r!["name"]!.GetValue<string>() == cycleA);
        Assert.Contains(cycleImpact["tables"]!.AsArray(), r =>
            r!["name"]!.GetValue<string>() == cycleB);

        var validationRes = await AuthPost(token, "/api/reports/validate", new { scriptPath });
        Assert.Equal(HttpStatusCode.OK, validationRes.StatusCode);
        var validation = await validationRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        Assert.True(validation!["isValid"]!.GetValue<bool>());
        Assert.NotNull(validation["impact"]);
        Assert.True(validation["impact"]!["reportCount"]!.GetValue<int>() >= 1);
        Assert.Contains(validation["impact"]!["sources"]!.AsArray(), r =>
            r!["source"]!.GetValue<string>() == $"sales.Orders_{suffix}");

        var privateDatasetName = $"#private_impact_{suffix}";
        using (var scope = _factory.Services.CreateScope())
        {
            var registry = scope.ServiceProvider.GetRequiredService<IDatasetRegistry>();
            await registry.RegisterOrUpdate(new DatasetMetadata
            {
                Name = privateDatasetName,
                FolderPath = "/private-impact",
                ParquetFilePath = $"private_impact_{suffix}.parquet",
                SourceQuery = "SELECT 1",
                AccessLevel = DatasetAccessLevel.Private
            });
        }

        var viewerToken = await GetFreshViewerTokenAsync();
        var privateDatasetImpactRes = await AuthGet(viewerToken, $"/api/catalog/impact?kind=dataset&name={Uri.EscapeDataString(privateDatasetName)}");
        Assert.Equal(HttpStatusCode.OK, privateDatasetImpactRes.StatusCode);
        var privateDatasetImpact = await privateDatasetImpactRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        Assert.Empty(privateDatasetImpact!["datasets"]!.AsArray());

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            Assert.True(await db.AuditLogs.AnyAsync(a =>
                a.Action == "STEWARD_LINEAGE_IMPACT" &&
                a.ResourceType == "Steward" &&
                a.ResourceId == "DataSteward" &&
                a.Detail != null &&
                a.Detail.Contains(stewardQueueTarget)));
            Assert.True(await db.AuditOutboxMessages.AnyAsync(a =>
                a.Action == "STEWARD_LINEAGE_IMPACT" &&
                a.ResourceType == "Steward" &&
                a.ResourceId == "DataSteward"));
        }
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
    public async Task DataQuality_QuarantineQueue_ReturnsReplayManifests()
    {
        var token = await GetAdminTokenAsync();
        var store = _factory.Services.GetRequiredService<IJobHistoryStore>();
        await store.SetJobStateAsync(
            "nightly_import",
            "dq:quarantine-manifest:q_users",
            JsonSerializer.Serialize(new QuarantineReplayManifest(
                "nightly_import",
                "loads/users.etlsql",
                "import_users",
                "#src",
                "q_users",
                true,
                null,
                ["Id", "Email"],
                "schema-a",
                DateTimeOffset.UtcNow)));
        await store.SetJobStateAsync(
            "nightly_import",
            "dq:quarantine-manifest:q_joined",
            JsonSerializer.Serialize(new QuarantineReplayManifest(
                "nightly_import",
                "loads/users.etlsql",
                "import_joined",
                "#src,#dim",
                "q_joined",
                false,
                "quarantine source spans a join; replay requires a single-table input in this version",
                ["Id", "Email", "Region"],
                "schema-b",
                DateTimeOffset.UtcNow.AddMinutes(-1))));

        var res = await AuthGet(token, "/api/data-quality/quarantine?replayable=true&q=users");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var items = await res.Content.ReadFromJsonAsync<List<QuarantineQueueItemDto>>(_json);
        var item = Assert.Single(items!);
        Assert.Equal("nightly_import", item.JobName);
        Assert.Equal("q_users", item.QuarantineTarget);
        Assert.True(item.IsReplayable);
        Assert.Equal("REPLAY QUARANTINE q_users;", item.ReplayStatement);
        Assert.Contains("Email", item.InputColumns);
    }

    [Theory]
    // A durable table on a named connection, and a #temp target — the two shapes real capture
    // targets take. Neither is readable from the Portal process, and the #temp case is the
    // dangerous one: the preview session auto-creates the table empty, so a steward who is
    // offered a row editor sees "no rows" and reads it as "nothing was quarantined".
    [InlineData("warehouse.dbo.q_unreadable", "warehouse")]
    [InlineData("#q_scratch", "temp table")]
    public async Task DataQuality_Queue_MarksTargetsPortalCannotReadAsViewOnly(string target, string expectedInReason)
    {
        var token = await GetAdminTokenAsync();
        var store = _factory.Services.GetRequiredService<IJobHistoryStore>();
        var job = $"view_only_{Guid.NewGuid():N}";
        await store.SetJobStateAsync(
            job,
            $"dq:quarantine-manifest:{target}",
            JsonSerializer.Serialize(new QuarantineReplayManifest(
                job, "loads/x.etlsql", "sec", "#src", target,
                true, null, ["Id"], "schema-v", DateTimeOffset.UtcNow)));

        var res = await AuthGet(token, $"/api/data-quality/quarantine?jobName={job}");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var item = Assert.Single((await res.Content.ReadFromJsonAsync<List<QuarantineQueueItemDto>>(_json))!);
        Assert.False(item.RowsReadable);
        Assert.Contains(expectedInReason, item.RowsUnavailableReason);
        // Replay is unaffected — it runs in the orchestrator, which does have the connection.
        Assert.True(item.IsReplayable);
        Assert.Equal($"SELECT * FROM {target} WHERE __dq_status = 'quarantined';", item.ReviewStatement);
    }

    [Fact]
    public async Task DataQuality_QuarantineRows_DeclinesTargetPortalCannotRead()
    {
        var token = await GetAdminTokenAsync();
        var store = _factory.Services.GetRequiredService<IJobHistoryStore>();
        await store.SetJobStateAsync(
            "unreadable_job",
            "dq:quarantine-manifest:warehouse.dbo.q_rows",
            JsonSerializer.Serialize(new QuarantineReplayManifest(
                "unreadable_job", "loads/x.etlsql", "sec", "#src", "warehouse.dbo.q_rows",
                true, null, ["Id"], "schema-v", DateTimeOffset.UtcNow)));

        var res = await AuthGet(
            token,
            "/api/data-quality/quarantine/rows?quarantineTarget=warehouse.dbo.q_rows&jobName=unreadable_job");

        // Declined up front with a reason, rather than executed and surfaced as a 502 carrying a
        // raw engine diagnostic.
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("warehouse", body);
        Assert.Contains("SELECT * FROM warehouse.dbo.q_rows", body);
    }

    [Fact]
    public async Task DataQuality_ReplayQuarantine_SubmitsReplayJob()
    {
        var token = await GetAdminTokenAsync();
        var store = _factory.Services.GetRequiredService<IJobHistoryStore>();
        await store.SetJobStateAsync(
            "nightly_replay",
            "dq:quarantine-manifest:q_replayable",
            JsonSerializer.Serialize(new QuarantineReplayManifest(
                "nightly_replay",
                "loads/replay.etlsql",
                "replayable_section",
                "#src",
                "q_replayable",
                true,
                null,
                ["Id"],
                "schema-replay",
                DateTimeOffset.UtcNow)));

        var res = await AuthPost(token, "/api/data-quality/quarantine/replay", new
        {
            quarantineTarget = "q_replayable",
            jobName = "nightly_replay"
        });

        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<ReplayQuarantineResponse>(_json);
        Assert.False(string.IsNullOrWhiteSpace(body!.JobId));
        Assert.Equal("REPLAY QUARANTINE q_replayable;", body.ReplayStatement);
    }

    [Fact]
    public async Task DataQuality_ReplayQuarantine_RejectsBlockedManifest()
    {
        var token = await GetAdminTokenAsync();
        var store = _factory.Services.GetRequiredService<IJobHistoryStore>();
        await store.SetJobStateAsync(
            "nightly_blocked",
            "dq:quarantine-manifest:q_blocked",
            JsonSerializer.Serialize(new QuarantineReplayManifest(
                "nightly_blocked",
                "loads/replay.etlsql",
                "blocked_section",
                "#src,#dim",
                "q_blocked",
                false,
                "quarantine source spans a join; replay requires a single-table input in this version",
                ["Id"],
                "schema-blocked",
                DateTimeOffset.UtcNow)));

        var res = await AuthPost(token, "/api/data-quality/quarantine/replay", new
        {
            quarantineTarget = "q_blocked",
            jobName = "nightly_blocked"
        });

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task DataQuality_UpdateDisposition_SubmitsGuardedReleaseUpdate()
    {
        var token = await GetAdminTokenAsync();
        var store = _factory.Services.GetRequiredService<IJobHistoryStore>();
        await store.SetJobStateAsync(
            "nightly_disposition",
            "dq:quarantine-manifest:q_disposition",
            JsonSerializer.Serialize(new QuarantineReplayManifest(
                "nightly_disposition",
                "loads/replay.etlsql",
                "disposition_section",
                "#src",
                "q_disposition",
                true,
                null,
                ["Id", "Email"],
                "schema-disposition",
                DateTimeOffset.UtcNow)));

        var res = await AuthPost(token, "/api/data-quality/quarantine/disposition", new
        {
            quarantineTarget = "q_disposition",
            jobName = "nightly_disposition",
            rowIds = new[] { "row-1" },
            disposition = "released",
            changes = new Dictionary<string, string?> { ["Email"] = "fixed@example.test" }
        });

        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<QuarantineDispositionResponse>(_json);
        Assert.False(string.IsNullOrWhiteSpace(body!.JobId));
        Assert.Equal(
            "UPDATE q_disposition SET Email = 'fixed@example.test', __dq_status = 'released' WHERE __dq_row_id IN ('row-1') AND __dq_status = 'quarantined';",
            body.DispositionStatement);
    }

    [Fact]
    public async Task DataQuality_UpdateDisposition_RejectsReleaseForNonReplayableManifest()
    {
        var token = await GetAdminTokenAsync();
        var store = _factory.Services.GetRequiredService<IJobHistoryStore>();
        await store.SetJobStateAsync(
            "nightly_non_replayable",
            "dq:quarantine-manifest:q_non_replayable",
            JsonSerializer.Serialize(new QuarantineReplayManifest(
                "nightly_non_replayable",
                "loads/replay.etlsql",
                "blocked_section",
                "#src,#dim",
                "q_non_replayable",
                false,
                "quarantine source spans a join; replay requires a single-table input in this version",
                ["Id"],
                "schema-non-replayable",
                DateTimeOffset.UtcNow)));

        var res = await AuthPost(token, "/api/data-quality/quarantine/disposition", new
        {
            quarantineTarget = "q_non_replayable",
            jobName = "nightly_non_replayable",
            rowIds = new[] { "row-1" },
            disposition = "released"
        });

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task DataQuality_QuarantineRows_RejectsUnsupportedStatus()
    {
        var token = await GetAdminTokenAsync();

        var res = await AuthGet(token, "/api/data-quality/quarantine/rows?quarantineTarget=q_any&status=deleted");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task DataQuality_QuarantineRows_RejectsTamperedTarget()
    {
        var token = await GetAdminTokenAsync();
        var store = _factory.Services.GetRequiredService<IJobHistoryStore>();
        await store.SetJobStateAsync(
            "nightly_tampered_rows",
            "dq:quarantine-manifest:q_tampered",
            JsonSerializer.Serialize(new QuarantineReplayManifest(
                "nightly_tampered_rows",
                "loads/replay.etlsql",
                "tampered_section",
                "#src",
                "q_tampered; DROP TABLE users",
                true,
                null,
                ["Id"],
                "schema-tampered",
                DateTimeOffset.UtcNow)));

        var res = await AuthGet(token, "/api/data-quality/quarantine/rows?quarantineTarget=q_tampered%3B%20DROP%20TABLE%20users&jobName=nightly_tampered_rows");

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task DataQuality_Disposition_IsAuditedWithActorAndReason()
    {
        var token = await GetAdminTokenAsync();
        var store = _factory.Services.GetRequiredService<IJobHistoryStore>();
        await store.SetJobStateAsync(
            "audited_job",
            "dq:quarantine-manifest:q_audited",
            JsonSerializer.Serialize(new QuarantineReplayManifest(
                "audited_job", "loads/x.etlsql", "sec", "#src", "q_audited",
                true, null, ["Id"], "schema-a", DateTimeOffset.UtcNow)));

        var res = await AuthPost(token, "/api/data-quality/quarantine/disposition", new
        {
            quarantineTarget = "q_audited",
            jobName = "audited_job",
            rowIds = new[] { "row-abc", "row-def" },
            disposition = "discarded",
            note = "Duplicate feed replayed by the vendor on 2026-07-25"
        });
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var entry = await db.AuditLogs
            .Where(a => a.Action == "DATA_QUALITY_DISCARD" && a.ResourceId == "q_audited")
            .OrderByDescending(a => a.Timestamp)
            .FirstOrDefaultAsync();

        Assert.NotNull(entry);
        Assert.NotNull(entry!.UserId);                              // who
        Assert.Contains("Duplicate feed replayed", entry.Detail!);  // why
        Assert.Contains("row-abc", entry.Detail!);                  // which evidence
        Assert.Contains("rows=2", entry.Detail!);
    }

    [Fact]
    public async Task DataQuality_Trend_SurfacesPersistedRunMetrics()
    {
        var token = await GetAdminTokenAsync();
        var store = _factory.Services.GetRequiredService<IJobHistoryStore>();
        const string job = "trend_import";

        // Three completed runs, quality degrading: 1%, 2%, then 20%.
        foreach (var (rows, quarantined, failures) in new[]
        {
            (1000L, 10L, "Email:MATCHES ^[^@]+@[^@]+$=10"),
            (1000L, 20L, "Email:MATCHES ^[^@]+@[^@]+$=20"),
            (1000L, 200L, "Email:MATCHES ^[^@]+@[^@]+$=180;Age:>= 0=20")
        })
        {
            var id = await store.LogJobStartAsync(job);
            await store.LogJobEndAsync(id, "SUCCESS", rowsProcessed: rows,
                rowsQuarantined: quarantined, rowsWarned: 0, dataQualityFailures: failures);
            await Task.Delay(5);
        }

        var res = await AuthGet(token, $"/api/data-quality/trend?jobName={job}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var trend = await res.Content.ReadFromJsonAsync<DataQualityTrendDto>(_json);

        Assert.Equal(3, trend!.RunCount);
        Assert.Equal(230, trend.TotalRowsQuarantined);
        Assert.Equal(0.2m, trend.LatestQuarantineRate);
        // Latest 20% against a 1.5% mean of the two earlier runs — a clear degradation signal.
        Assert.NotNull(trend.QuarantineRateDelta);
        Assert.True(trend.QuarantineRateDelta > 0.18m, "expected the latest run to read as degrading");

        // Rule text contains ':' and '=' (a MATCHES regex), so the payload parser must not split on them.
        var top = trend.TopRuleFailures[0];
        Assert.Equal("Email", top.Column);
        Assert.Equal("MATCHES ^[^@]+@[^@]+$", top.Rule);
        Assert.Equal(210, top.Count);
    }

    [Fact]
    public async Task DataQuality_Trend_UnknownJob_ReturnsEmptyRatherThanError()
    {
        var token = await GetAdminTokenAsync();

        var res = await AuthGet(token, "/api/data-quality/trend?jobName=never_ran_at_all");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var trend = await res.Content.ReadFromJsonAsync<DataQualityTrendDto>(_json);
        Assert.Equal(0, trend!.RunCount);
        Assert.Empty(trend.Runs);
    }

    [Fact]
    public async Task DataQuality_Endpoints_AreDeniedToNonStewards()
    {
        // Quarantine remediation reads raw failing source rows (whatever PII the source carried),
        // edits them, and enqueues jobs that re-run production loads. A plain report Viewer must
        // not be able to do any of that.
        var viewerToken = await GetFreshViewerTokenAsync();

        var queue = await AuthGet(viewerToken, "/api/data-quality/quarantine");
        Assert.Equal(HttpStatusCode.Forbidden, queue.StatusCode);

        var rows = await AuthGet(viewerToken, "/api/data-quality/quarantine/rows?quarantineTarget=q_any");
        Assert.Equal(HttpStatusCode.Forbidden, rows.StatusCode);

        var replay = await AuthPost(viewerToken, "/api/data-quality/quarantine/replay", new
        {
            quarantineTarget = "q_any"
        });
        Assert.Equal(HttpStatusCode.Forbidden, replay.StatusCode);

        var disposition = await AuthPost(viewerToken, "/api/data-quality/quarantine/disposition", new
        {
            quarantineTarget = "q_any",
            rowIds = new[] { "abc" },
            disposition = "released"
        });
        Assert.Equal(HttpStatusCode.Forbidden, disposition.StatusCode);

        var trend = await AuthGet(viewerToken, "/api/data-quality/trend?jobName=any");
        Assert.Equal(HttpStatusCode.Forbidden, trend.StatusCode);
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
        for (var i = 0; i < 300; i++)
        {
            var jobRes = await AuthGet(token, $"/api/jobs/{jobId}");
            Assert.Equal(HttpStatusCode.OK, jobRes.StatusCode);
            job = await jobRes.Content.ReadFromJsonAsync<JsonObject>(_json);
            var status = job!["status"]!.GetValue<string>();
            if (status is "Completed" or "Failed" or "Cancelled")
                break;

            await Task.Delay(200);
        }

        Assert.NotNull(job);
        var jobStatus = job!["status"]!.GetValue<string>();
        var jobError = job["error"]?.GetValue<string>() ?? "(no error)";
        Assert.True(jobStatus == "Completed", $"Expected Completed but job ended with {jobStatus}: {jobError}");

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
    public async Task ReportShareLinks_ResolveAnonymouslyWhileCreatorRemainsAuthorized()
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

        var adminResolve = await _client.GetAsync($"/api/share/{shareToken}");
        Assert.Equal(HttpStatusCode.OK, adminResolve.StatusCode);
        var resolved = await adminResolve.Content.ReadFromJsonAsync<JsonObject>(_json);
        Assert.Equal(reportId, resolved!["reportId"]!.GetValue<int>());

        var anonymousResolve = await _client.GetAsync($"/api/share/{shareToken}");
        Assert.Equal(HttpStatusCode.OK, anonymousResolve.StatusCode);

        var listRes = await AuthGet(token, $"/api/reports/{reportId}/share-links");
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
        var links = await listRes.Content.ReadFromJsonAsync<JsonArray>(_json);
        Assert.Contains(links!, l => l!["token"]!.GetValue<string>() == shareToken);

        var revokeRes = await AuthDelete(token, $"/api/reports/{reportId}/share-links/{shareToken}");
        Assert.Equal(HttpStatusCode.NoContent, revokeRes.StatusCode);

        var revokedResolve = await _client.GetAsync($"/api/share/{shareToken}");
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
            var adminId = await db.Users
                .Where(u => u.UserName == "admin")
                .Select(u => u.Id)
                .SingleAsync();
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
            db.ReportJobLinks.Add(new ReportJobLink
            {
                ReportId = reportId,
                OrchestratorAlias = "prod_orch",
                JobName = "refresh_sales_summary_v2",
                LastRefreshedAt = DateTime.UtcNow
            });
            var alert = new ReportAlert
            {
                ReportId = reportId,
                OwnerId = adminId,
                Name = "RevenueDrop",
                VisualName = "Total",
                Operator = "<",
                Threshold = 10,
                IsActive = true,
                LastState = "OK",
                LastEvaluatedAt = DateTime.UtcNow
            };
            alert.Notifications.Add(new AlertNotification
            {
                OrchestratorAlias = "prod_orch",
                NotificationName = "OpsPager"
            });
            db.ReportAlerts.Add(alert);
            await db.SaveChangesAsync();
        }

        var res = await AuthGet(token, $"/api/reports/{reportId}/dependencies");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonObject>(_json);

        Assert.Equal("Dependency Report", body!["report"]!["name"]!.GetValue<string>());
        Assert.Equal("#stage", body["manifestDatasets"]![0]!["tempTableName"]!.GetValue<string>());
        Assert.Equal("Sales Summary", body["registeredDatasets"]![0]!["name"]!.GetValue<string>());
        Assert.Contains(body["refreshJobs"]!.AsArray(), n =>
            n!["orchestratorJobName"]!.GetValue<string>() == "refresh_sales_summary_v2" &&
            n["orchestratorAlias"]!.GetValue<string>() == "prod_orch");
        Assert.Equal("RevenueDrop", body["alerts"]![0]!["name"]!.GetValue<string>());
        Assert.Equal("Total", body["alerts"]![0]!["visualName"]!.GetValue<string>());
        Assert.Equal("prod_orch", body["alerts"]![0]!["notifications"]![0]!["orchestratorAlias"]!.GetValue<string>());
        Assert.Equal("OpsPager", body["alerts"]![0]!["notifications"]![0]!["notificationName"]!.GetValue<string>());
        Assert.Contains(body["sources"]!.AsArray(), n => n!["name"]!.GetValue<string>() == "erp.InvoiceLines");
        Assert.Contains(body["lineageEntries"]!.AsArray(), n =>
            n!["target"]!.GetValue<string>() == "#stage" &&
            n["targetColumn"]!.GetValue<string>() == "OrderId" &&
            n["tags"]!["owner"]!.GetValue<string>() == "SalesOps");
    }

    [Fact]
    [Trait("Category", "Smoke.Portal")]
    public async Task Structure_BridgesDatasetReferenceAcrossScripts()
    {
        var token = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var dsRef = $"&sales_snap_{suffix}";

        var folderRes = await AuthPost(token, "/api/folders", new { name = $"XScript {suffix}", parentId = (int?)null });
        Assert.Equal(HttpStatusCode.Created, folderRes.StatusCode);
        var folderId = (await folderRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["id"]!.GetValue<int>();

        // Report script — references a dataset built by a *separate* script.
        var scriptPath = Path.Combine(_factory.TempDir, "scripts", $"xscript_{suffix}.rptsql");
        await File.WriteAllTextAsync(scriptPath,
            $"USE DATASET {dsRef};\n" +
            $"SELECT * INTO #sales FROM {dsRef};\n" +
            "CREATE VISUAL salesBar AS BAR (\n" +
            "    SOURCE = #sales,\n" +
            "    MAPPINGS (X = Date, Y = total, SERIES = Vendor)\n" +
            ");\n" +
            "CREATE PAGE Main AS DASHBOARD( STRUCTURE = 'A', MAP ( 'A' = salesBar ) );\n");

        var publishRes = await AuthPost(token, "/api/reports", new
        {
            folderId,
            name = $"XScript Report {suffix}",
            description = "cross-script lineage",
            scriptPath
        });
        Assert.Equal(HttpStatusCode.Created, publishRes.StatusCode);
        var reportId = (await publishRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["id"]!.GetValue<int>();

        // Register the dataset (its SourceQuery) + persist its build-run lineage
        // (the inherited description/pii that the SQL text alone can't supply).
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            db.Datasets.Add(new Dataset
            {
                Name = dsRef,
                FolderPath = $"/XScript {suffix}",
                ParquetFilePath = Path.Combine(_factory.TempDir, "datasets", $"{suffix}.parquet"),
                OwningReportId = reportId,
                SourceQuery = "SELECT Date, Vendor, SUM(Amount) AS total FROM edw.Sales",
                AccessLevel = DatasetAccessLevel.Private,
            });
            await db.SaveChangesAsync();

            var catalog = scope.ServiceProvider.GetRequiredService<ILineageCatalogStore>();
            var totalEntry = new LineageEntry($"dataset:{dsRef}", "CREATE DATASET")
            {
                TargetColumn = "total",
                SourceTables = new List<string> { "Sales" },
                SourceColumns = new List<string> { "Amount" },
                TransformationKind = TransformationKind.Aggregation,
                TransformationExpression = "SUM(Amount)",
                FunctionsApplied = new List<string> { "SUM" },
                DerivedFromDescriptions = "Amount: Sales amounts",
                Metadata = new Dictionary<string, string> { ["d"] = "Sales amounts", ["pii"] = "true" },
            };
            await catalog.SaveLineageAsync(new[] { totalEntry }, $"dataset:{dsRef}:build", "build_sales_snap.rptsql", DateTime.UtcNow);
        }

        var res = await AuthGet(token, $"/api/reports/{reportId}/structure");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var dag = await res.Content.ReadFromJsonAsync<JsonObject>(_json);
        var nodes = dag!["nodes"]!.AsArray();

        string Norm(string s) => s.TrimStart('&', '#');

        // The dataset-reference node resolved to its build lineage across the boundary.
        var dsNode = Assert.Single(nodes, n =>
            n!["type"]!.GetValue<string>() == "dataset" &&
            string.Equals(Norm(n["label"]!.GetValue<string>()), Norm(dsRef), StringComparison.OrdinalIgnoreCase));
        var total = dsNode!["meta"]!["columnLineage"]!["total"]!;
        Assert.Equal("SUM(Amount)", total["transform"]!.GetValue<string>());
        Assert.Equal("Sales amounts", total["description"]!.GetValue<string>());
        Assert.Equal("true", total["tags"]!["pii"]!.GetValue<string>());
        // Source resolves to the fully-qualified EDW table from the dataset's SourceQuery.
        Assert.Contains(total["sources"]!.AsArray(), s =>
            s!["table"]!.GetValue<string>().EndsWith("Sales", StringComparison.OrdinalIgnoreCase) &&
            s["column"]!.GetValue<string>() == "Amount");

        // The SELECT * consumer (#sales) pass-throughs back to the dataset, so the
        // visual's Y=total stays connected to the dataset across the boundary.
        var sales = Assert.Single(nodes, n => n!["label"]!.GetValue<string>() == "#sales");
        Assert.True(sales!["meta"] is not null, $"#sales node had no meta. Node = {sales.ToJsonString()}");
        var salesCl = sales["meta"]!["columnLineage"];
        Assert.True(salesCl is not null && salesCl["total"] is not null,
            $"#sales has no pass-through 'total'. meta = {sales["meta"]!.ToJsonString()}");
        Assert.Contains(salesCl!["total"]!["sources"]!.AsArray(), s =>
            string.Equals(Norm(s!["table"]!.GetValue<string>()), Norm(dsRef), StringComparison.OrdinalIgnoreCase) &&
            s["column"]!.GetValue<string>() == "total");
    }

    [Fact]
    [Trait("Category", "Smoke.Portal")]
    public async Task Structure_ExpandsRawSelectStarFromPersistedCatalogLineage()
    {
        var token = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var source = $"edw.Sales_{suffix}";

        var folderRes = await AuthPost(token, "/api/folders", new { name = $"RawStar {suffix}", parentId = (int?)null });
        Assert.Equal(HttpStatusCode.Created, folderRes.StatusCode);
        var folderId = (await folderRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["id"]!.GetValue<int>();

        var scriptPath = Path.Combine(_factory.TempDir, "scripts", $"rawstar_{suffix}.rptsql");
        await File.WriteAllTextAsync(scriptPath,
            $"SELECT * INTO #sales FROM {source};\n" +
            "CREATE VISUAL salesTable AS TABLE (\n" +
            "    SOURCE = #sales,\n" +
            "    MAPPINGS (Date = Date, Amount = Amount)\n" +
            ");\n" +
            "CREATE PAGE Main AS DASHBOARD( STRUCTURE = 'A', MAP ( 'A' = salesTable ) );\n");

        var publishRes = await AuthPost(token, "/api/reports", new
        {
            folderId,
            name = $"Raw Star Report {suffix}",
            description = "raw select star lineage",
            scriptPath
        });
        Assert.Equal(HttpStatusCode.Created, publishRes.StatusCode);
        var reportId = (await publishRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["id"]!.GetValue<int>();

        using (var scope = _factory.Services.CreateScope())
        {
            var catalog = scope.ServiceProvider.GetRequiredService<ILineageCatalogStore>();
            await catalog.SaveLineageAsync(new[]
            {
                new LineageEntry(source, "DB_CATALOG")
                {
                    TargetColumn = "Date",
                    Metadata = new Dictionary<string, string> { ["d"] = "Transaction date", ["db_type"] = "date" },
                },
                new LineageEntry(source, "DB_CATALOG")
                {
                    TargetColumn = "Amount",
                    Metadata = new Dictionary<string, string> { ["d"] = "Sales amount", ["db_type"] = "decimal" },
                }
            }, $"report:{reportId}:catalog-import", scriptPath, DateTime.UtcNow);
        }

        var res = await AuthGet(token, $"/api/reports/{reportId}/structure");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var nodes = (await res.Content.ReadFromJsonAsync<JsonObject>(_json))!["nodes"]!.AsArray();

        var sourceNode = Assert.Single(nodes, n => n!["label"]!.GetValue<string>() == source);
        var sourceCols = sourceNode!["meta"]!["columnLineage"]!;
        Assert.Equal("Sales amount", sourceCols["Amount"]!["description"]!.GetValue<string>());
        Assert.Equal("decimal", sourceCols["Amount"]!["tags"]!["db_type"]!.GetValue<string>());

        var sales = Assert.Single(nodes, n => n!["label"]!.GetValue<string>() == "#sales");
        var salesCols = sales!["meta"]!["columnLineage"]!;
        Assert.Contains(salesCols["Amount"]!["sources"]!.AsArray(), s =>
            s!["table"]!.GetValue<string>() == source &&
            s["column"]!.GetValue<string>() == "Amount");
    }

    [Fact]
    [Trait("Category", "Smoke.Portal")]
    public async Task Structure_UsesPageLayoutForVisualEdges_WhenVisualsDeclaredBeforePages()
    {
        var token = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var folderRes = await AuthPost(token, "/api/folders", new { name = $"Layout {suffix}", parentId = (int?)null });
        Assert.Equal(HttpStatusCode.Created, folderRes.StatusCode);
        var folderId = (await folderRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["id"]!.GetValue<int>();

        var scriptPath = Path.Combine(_factory.TempDir, "scripts", $"layout_{suffix}.rptsql");
        await File.WriteAllTextAsync(scriptPath, @"
CREATE VISUAL SalesCard AS CARD (
    SOURCE = (SELECT 42 AS Total),
    MAPPINGS (VALUE = Total)
);

CREATE PAGE Main AS DASHBOARD(
    STRUCTURE = 'A',
    MAP ('A' = SalesCard)
);
");

        var publishRes = await AuthPost(token, "/api/reports", new
        {
            folderId,
            name = $"Layout Report {suffix}",
            description = "visual before page",
            scriptPath
        });
        Assert.Equal(HttpStatusCode.Created, publishRes.StatusCode);
        var reportId = (await publishRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["id"]!.GetValue<int>();

        var res = await AuthGet(token, $"/api/reports/{reportId}/structure");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var dag = await res.Content.ReadFromJsonAsync<JsonObject>(_json);
        var nodes = dag!["nodes"]!.AsArray();
        var edges = dag["edges"]!.AsArray();

        var visual = Assert.Single(nodes, n => n!["id"]!.GetValue<string>() == "vis:SalesCard");
        Assert.Equal("Main", visual!["meta"]!["page"]!.GetValue<string>());
        Assert.Contains(visual["meta"]!["pages"]!.AsArray(), p => p!.GetValue<string>() == "Main");
        Assert.Contains(edges, e =>
            e!["source"]!.GetValue<string>() == "page:Main" &&
            e["target"]!.GetValue<string>() == "vis:SalesCard");
    }

    [Fact]
    [Trait("Category", "Smoke.Portal")]
    public async Task Structure_ReportColumnTags_OverrideDescriptionButPreserveDatasetHistory()
    {
        var token = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var dsRef = $"&sales_snap_{suffix}";

        var folderRes = await AuthPost(token, "/api/folders", new { name = $"Tagged {suffix}", parentId = (int?)null });
        Assert.Equal(HttpStatusCode.Created, folderRes.StatusCode);
        var folderId = (await folderRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["id"]!.GetValue<int>();

        // Report explicitly selects the dataset's columns and adds inline tags — the
        // 'total' tag deliberately overrides the description inherited from EDW.
        var scriptPath = Path.Combine(_factory.TempDir, "scripts", $"tagged_{suffix}.rptsql");
        await File.WriteAllTextAsync(scriptPath,
            $"USE DATASET {dsRef};\n" +
            "SELECT\n" +
            "  Date /*@d:date of the transaction;@source:Sales;*/\n" +
            "  ,Vendor /*@d:vendor name;@notes:Does not include the vendor id;*/\n" +
            "  ,total /*@d:Total sum of Amount;*/\n" +
            "INTO #sales\n" +
            $"FROM {dsRef} /*@d:This is the dataset created in a separate script;*/;\n" +
            "CREATE VISUAL salesBar AS BAR (\n" +
            "    SOURCE = #sales,\n" +
            "    MAPPINGS (X = Date, Y = total, SERIES = Vendor)\n" +
            ");\n" +
            "CREATE PAGE Main AS DASHBOARD( STRUCTURE = 'A', MAP ( 'A' = salesBar ) );\n");

        var publishRes = await AuthPost(token, "/api/reports", new
        {
            folderId,
            name = $"Tagged Report {suffix}",
            description = "report-side column tags",
            scriptPath
        });
        Assert.Equal(HttpStatusCode.Created, publishRes.StatusCode);
        var reportId = (await publishRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["id"]!.GetValue<int>();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            db.Datasets.Add(new Dataset
            {
                Name = dsRef,
                FolderPath = $"/Tagged {suffix}",
                ParquetFilePath = Path.Combine(_factory.TempDir, "datasets", $"{suffix}.parquet"),
                OwningReportId = reportId,
                SourceQuery = "SELECT Date, Vendor, SUM(Amount) AS total FROM edw.Sales",
                AccessLevel = DatasetAccessLevel.Private,
            });
            await db.SaveChangesAsync();

            var catalog = scope.ServiceProvider.GetRequiredService<ILineageCatalogStore>();
            await catalog.SaveLineageAsync(new[]
            {
                new LineageEntry($"dataset:{dsRef}", "CREATE DATASET")
                {
                    TargetColumn             = "total",
                    SourceTables             = new List<string> { "Sales" },
                    SourceColumns            = new List<string> { "Amount" },
                    TransformationKind       = TransformationKind.Aggregation,
                    TransformationExpression = "SUM(Amount)",
                    DerivedFromDescriptions  = "Amount: Sales amounts",
                    Metadata                 = new Dictionary<string, string> { ["d"] = "Sales amounts", ["pii"] = "true" },
                }
            }, $"dataset:{dsRef}:build", "build_sales_snap.rptsql", DateTime.UtcNow);
        }

        var res = await AuthGet(token, $"/api/reports/{reportId}/structure");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var nodes = (await res.Content.ReadFromJsonAsync<JsonObject>(_json))!["nodes"]!.AsArray();
        string Norm(string s) => s.TrimStart('&', '#');
        string? Tag(JsonNode? colLin, string key)
        {
            var t = colLin?["tags"]?.AsObject();
            if (t is null) return null;
            foreach (var kv in t) if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)) return kv.Value?.GetValue<string>();
            return null;
        }

        // ── #sales: the report's inline tags made it through ──────────────────
        var sales = Assert.Single(nodes, n => n!["label"]!.GetValue<string>() == "#sales");
        var salesCols = sales!["meta"]!["columnLineage"]!;

        // total: the report description OVERRIDES the inherited EDW one here.
        Assert.Contains("Total sum of Amount", salesCols["total"]!["description"]!.GetValue<string>());
        Assert.Contains(salesCols["total"]!["sources"]!.AsArray(), s =>
            string.Equals(Norm(s!["table"]!.GetValue<string>()), Norm(dsRef), StringComparison.OrdinalIgnoreCase) &&
            s["column"]!.GetValue<string>() == "total");
        // Other columns' tags flow too.
        Assert.Equal("Sales", Tag(salesCols["Date"], "source"));
        Assert.Contains("date of the transaction", salesCols["Date"]!["description"]!.GetValue<string>());
        Assert.Contains("vendor id", Tag(salesCols["Vendor"], "notes") ?? "");

        // ── &sales_snap: the EDW description + pii are PRESERVED one hop up ────
        var dsNode = Assert.Single(nodes, n =>
            n!["type"]!.GetValue<string>() == "dataset" &&
            string.Equals(Norm(n["label"]!.GetValue<string>()), Norm(dsRef), StringComparison.OrdinalIgnoreCase));
        var dsTotal = dsNode!["meta"]!["columnLineage"]!["total"]!;
        Assert.Equal("SUM(Amount)", dsTotal["transform"]!.GetValue<string>());
        Assert.Equal("Sales amounts", dsTotal["description"]!.GetValue<string>());
        Assert.Equal("true", dsTotal["tags"]!["pii"]!.GetValue<string>());
        Assert.Contains(dsTotal["sources"]!.AsArray(), s =>
            s!["table"]!.GetValue<string>().EndsWith("Sales", StringComparison.OrdinalIgnoreCase) &&
            s["column"]!.GetValue<string>() == "Amount");
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
        var folder = await folderRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var folderId = folder!["id"]!.GetValue<int>();

        var siblingRoot = Path.Combine(_factory.TempDir, "scripts2");
        Directory.CreateDirectory(siblingRoot);
        var siblingScript = Path.Combine(siblingRoot, "outside.rptsql");
        await File.WriteAllTextAsync(siblingScript, "-- outside script root\n");

        var publishRes = await AuthPost(token, "/api/reports", new
        {
            folderId,
            name = "Outside Report",
            description = "Should be rejected",
            scriptPath = siblingScript
        });

        Assert.Equal(HttpStatusCode.BadRequest, publishRes.StatusCode);
    }

    [Fact]
    [Trait("Category", "Smoke.Security")]
    public async Task Report_UpdateRejectsSiblingScriptRootBypass()
    {
        var token = await GetAdminTokenAsync();

        var folderRes = await AuthPost(token, "/api/folders", new { name = "Sibling Update", parentId = (int?)null });
        var folder = await folderRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var folderId = folder!["id"]!.GetValue<int>();

        var scriptPath = Path.Combine(_factory.TempDir, "scripts", "inside_update.rptsql");
        await File.WriteAllTextAsync(scriptPath, "-- inside script root\n");

        var publishRes = await AuthPost(token, "/api/reports", new
        {
            folderId,
            name = "Inside Report",
            description = "",
            scriptPath
        });
        Assert.Equal(HttpStatusCode.Created, publishRes.StatusCode);
        var report = await publishRes.Content.ReadFromJsonAsync<JsonObject>(_json);
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
        var folder = await folderRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var folderId = folder!["id"]!.GetValue<int>();

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
        var report = await publishRes.Content.ReadFromJsonAsync<JsonObject>(_json);
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
        var folder = await folderRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var folderId = folder!["id"]!.GetValue<int>();

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
        var report = await publishRes.Content.ReadFromJsonAsync<JsonObject>(_json);
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
        var folder = await folderRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var folderId = folder!["id"]!.GetValue<int>();

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
        var report = await publishRes.Content.ReadFromJsonAsync<JsonObject>(_json);
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
        var exportRes = await AuthGet(token, $"/api/reports/{reportId}/export/csv");

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

        var metadata = await registry.Lookup("#inside", "Admin");

        Assert.NotNull(metadata);
        Assert.Equal(Path.Combine(_factory.TempDir, "datasets", "inside.parquet"), metadata!.ParquetFilePath);
    }

    [Fact]
    [Trait("Category", "Smoke.Security")]
    public async Task DatasetRegistry_PublishAuthorization_RequiresManageAndDefinesOwnership()
    {
        using var scope = _factory.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IDatasetRegistry>();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var owner = new PortalUser
        {
            UserName = $"publish_owner_{suffix}",
            Email = $"publish_owner_{suffix}@test.local"
        };
        var publisher = new PortalUser
        {
            UserName = $"publish_user_{suffix}",
            Email = $"publish_user_{suffix}@test.local"
        };
        db.Users.AddRange(owner, publisher);
        await db.SaveChangesAsync();

        var folder = new Folder
        {
            Name = $"Publish {suffix}",
            Path = $"/publish-{suffix}",
            OwnerId = owner.Id
        };
        var group = new Group { Name = $"publishers-{suffix}" };
        db.Folders.Add(folder);
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        db.UserGroups.Add(new UserGroup { UserId = publisher.Id, GroupId = group.Id });
        db.FolderAcls.Add(new FolderAcl
        {
            FolderId = folder.Id,
            GroupId = group.Id,
            Permission = FolderPermission.Read
        });
        await db.SaveChangesAsync();

        Assert.Null(await registry.AuthorizePublishAsync("/missing", $"UserId={publisher.Id}"));
        Assert.Null(await registry.AuthorizePublishAsync(folder.Path, $"UserId={publisher.Id}"));

        var acl = await db.FolderAcls.SingleAsync(a =>
            a.FolderId == folder.Id && a.GroupId == group.Id);
        acl.Permission = FolderPermission.Manage;
        await db.SaveChangesAsync();

        var interactive = await registry.AuthorizePublishAsync(
            folder.Path,
            $"UserId={publisher.Id}");
        Assert.NotNull(interactive);
        Assert.Equal(folder.Id, interactive!.FolderId);
        Assert.Equal(publisher.Id, interactive.OwnerUserId);

        var interactiveAdmin = await registry.AuthorizePublishAsync(
            folder.Path,
            $"UserId={publisher.Id};IsAdmin=true");
        Assert.NotNull(interactiveAdmin);
        Assert.Equal(publisher.Id, interactiveAdmin!.OwnerUserId);

        var system = await registry.AuthorizePublishAsync(folder.Path, "IsAdmin=true");
        Assert.NotNull(system);
        Assert.Equal(owner.Id, system!.OwnerUserId);

        await registry.AuditPublishAsync(
            publisher.Id,
            "&authorized",
            folder.Path,
            succeeded: true);
        await registry.AuditPublishAsync(
            publisher.Id,
            "&denied",
            folder.Path,
            succeeded: false,
            "target folder was not found or caller lacks Manage permission");

        Assert.True(await db.AuditLogs.AnyAsync(a =>
            a.Action == "PUBLISH_DATASET" &&
            a.ResourceId == "&authorized" &&
            a.Detail == $"Published to {folder.Path}"));
        var failedAudit = await db.AuditLogs.SingleAsync(a =>
            a.Action == "PUBLISH_DATASET_FAILED" &&
            a.ResourceId == "&denied");
        Assert.NotNull(failedAudit.Detail);
        Assert.DoesNotContain("password", failedAudit.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", failedAudit.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Smoke.Portal")]
    public async Task DatasetRegistry_ResolvesByGlobalNameRegardlessOfFolder()
    {
        // 1a: datasets resolve by globally unique name, folder-independent. A dataset created
        // "in" folder A must be discoverable by a consumer running anywhere else, and moving it
        // to a new folder must not change its on-disk parquet path (filename is keyed on the
        // stable Id, not folder|name).
        using var scope = _factory.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IDatasetRegistry>();

        var name = $"#xfolder_{Guid.NewGuid():N}".Substring(0, 14);

        var id = await registry.RegisterOrUpdate(new DatasetMetadata
        {
            Name = name,
            FolderPath = "/folder-a",
            ParquetFilePath = "xfolder.parquet",
            SourceQuery = "SELECT 1",
            AccessLevel = DatasetAccessLevel.Public
        });
        Assert.True(id > 0);

        // Lookup takes no folder — resolution is by name alone.
        var resolved = await registry.Lookup(name, "Admin");
        Assert.NotNull(resolved);
        Assert.Equal("/folder-a", resolved!.FolderPath);
        Assert.Equal(id, resolved.Id);

        var pathBefore = registry.BuildDatasetFilePath(id, name);

        // "Move" the dataset to another folder (same global name → same Id).
        var idAfterMove = await registry.RegisterOrUpdate(new DatasetMetadata
        {
            Name = name,
            FolderPath = "/folder-b",
            ParquetFilePath = "xfolder.parquet",
            SourceQuery = "SELECT 1",
            AccessLevel = DatasetAccessLevel.Public
        });

        Assert.Equal(id, idAfterMove);                                   // stable identity across the move
        Assert.Equal(pathBefore, registry.BuildDatasetFilePath(idAfterMove, name)); // file not rewritten
        var afterMove = await registry.Lookup(name, "Admin");
        Assert.Equal("/folder-b", afterMove!.FolderPath);               // folder is mutable display metadata
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
            FolderPath = $"/unfiled-{suffix}",   // no matching Folder → no folder link
            ParquetFilePath = $"public_{suffix}.parquet",
            SourceQuery = "SELECT 1",
            AccessLevel = DatasetAccessLevel.Public
            // No OwningReportId and no resolvable folder → PUBLIC falls back to "any authenticated caller".
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

        // Unauthenticated caller: a no-folder PUBLIC dataset now requires authentication (1b), so it
        // is no longer visible anonymously; PRIVATE datasets remain hidden.
        var anonymousList = (await registry.ListAll("")).Select(d => d.Name).ToHashSet();
        Assert.DoesNotContain($"#public_{suffix}", anonymousList);
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

        Assert.Null(await registry.Lookup($"#owner_{suffix}", $"UserId={outsider.Id}"));
        Assert.NotNull(await registry.Lookup($"#owner_{suffix}", $"UserId={owner.Id}"));
        Assert.Equal(4, (await registry.ListAll("Admin")).Count(d => d.Name.EndsWith(suffix)));
    }

    [Fact]
    [Trait("Category", "Smoke.Security")]
    public async Task DatasetRegistry_PublicGatedByFolderReadPermission()
    {
        // 1b: PUBLIC = any authenticated user with Read on the dataset's folder. The dataset's
        // FolderId is resolved from its owning report's folder; access reuses FolderPermissionService.
        using var scope = _factory.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IDatasetRegistry>();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var granted = new PortalUser { UserName = $"fg_granted_{suffix}", Email = $"fg_granted_{suffix}@test.local" };
        var denied = new PortalUser { UserName = $"fg_denied_{suffix}", Email = $"fg_denied_{suffix}@test.local" };
        db.Users.AddRange(granted, denied);
        await db.SaveChangesAsync();

        var folder = new Folder { Name = $"FG {suffix}", Path = $"/fg-{suffix}", OwnerId = granted.Id };
        db.Folders.Add(folder);
        await db.SaveChangesAsync();

        // Grant Read on the folder to a group that only `granted` belongs to.
        var group = new Group { Name = $"fg-readers-{suffix}" };
        db.Groups.Add(group);
        await db.SaveChangesAsync();
        db.UserGroups.Add(new UserGroup { UserId = granted.Id, GroupId = group.Id });
        db.FolderAcls.Add(new FolderAcl { FolderId = folder.Id, GroupId = group.Id, Permission = FolderPermission.Read });

        var report = new Report
        {
            FolderId = folder.Id,
            Name = $"FG Report {suffix}",
            ScriptPath = Path.Combine(_factory.TempDir, "scripts", $"fg_{suffix}.rptsql"),
            CreatedBy = granted.Id
        };
        db.Reports.Add(report);
        await db.SaveChangesAsync();

        var name = $"#fg_{suffix}";
        await registry.RegisterOrUpdate(new DatasetMetadata
        {
            Name = name,
            FolderPath = folder.Path,
            ParquetFilePath = $"fg_{suffix}.parquet",
            SourceQuery = "SELECT 1",
            AccessLevel = DatasetAccessLevel.Public,
            OwningReportId = report.Id
        });

        // FolderId is resolved from the owning report's folder.
        var stored = await db.Datasets.SingleAsync(d => d.Name == name);
        Assert.Equal(folder.Id, stored.FolderId);

        // Folder Read → visible; no folder permission → denied; admin always; unauthenticated denied.
        Assert.NotNull(await registry.Lookup(name, $"UserId={granted.Id}"));
        Assert.Null(await registry.Lookup(name, $"UserId={denied.Id}"));
        Assert.NotNull(await registry.Lookup(name, "Admin"));
        Assert.Null(await registry.Lookup(name, ""));
    }

    [Fact]
    [Trait("Category", "Smoke.Security")]
    public async Task DatasetRegistry_SeparatesRefreshFromEditPermission()
    {
        // Refresh can be delegated independently. Editors and owners retain refresh rights,
        // while a Refresh grant cannot edit metadata or source definitions.
        using var scope = _factory.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IDatasetRegistry>();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var owner = new PortalUser { UserName = $"ed_owner_{suffix}", Email = $"ed_owner_{suffix}@test.local" };
        var refresher = new PortalUser { UserName = $"ed_refresh_{suffix}", Email = $"ed_refresh_{suffix}@test.local" };
        var editor = new PortalUser { UserName = $"ed_editor_{suffix}", Email = $"ed_editor_{suffix}@test.local" };
        var viewer = new PortalUser { UserName = $"ed_viewer_{suffix}", Email = $"ed_viewer_{suffix}@test.local" };
        var outsider = new PortalUser { UserName = $"ed_out_{suffix}", Email = $"ed_out_{suffix}@test.local" };
        db.Users.AddRange(owner, refresher, editor, viewer, outsider);
        await db.SaveChangesAsync();

        var folder = new Folder { Name = $"ED {suffix}", Path = $"/ed-{suffix}", OwnerId = owner.Id };
        db.Folders.Add(folder);
        await db.SaveChangesAsync();

        var report = new Report
        {
            FolderId = folder.Id,
            Name = $"ED Report {suffix}",
            ScriptPath = Path.Combine(_factory.TempDir, "scripts", $"ed_{suffix}.rptsql"),
            CreatedBy = owner.Id
        };
        db.Reports.Add(report);
        await db.SaveChangesAsync();

        var name = $"#ed_{suffix}";
        await registry.RegisterOrUpdate(new DatasetMetadata
        {
            Name = name,
            FolderPath = folder.Path,
            ParquetFilePath = $"ed_{suffix}.parquet",
            SourceQuery = "SELECT 1",
            AccessLevel = DatasetAccessLevel.Private,
            OwningReportId = report.Id
        });

        var refreshGroup = new Group { Name = $"ed-refreshers-{suffix}" };
        var editorGroup = new Group { Name = $"ed-editors-{suffix}" };
        var viewerGroup = new Group { Name = $"ed-viewers-{suffix}" };
        db.Groups.AddRange(refreshGroup, editorGroup, viewerGroup);
        await db.SaveChangesAsync();
        db.UserGroups.AddRange(
            new UserGroup { UserId = refresher.Id, GroupId = refreshGroup.Id },
            new UserGroup { UserId = editor.Id, GroupId = editorGroup.Id },
            new UserGroup { UserId = viewer.Id, GroupId = viewerGroup.Id });
        var ds = await db.Datasets.SingleAsync(d => d.Name == name);
        db.DatasetAcls.AddRange(
            new DatasetAcl { DatasetId = ds.Id, GroupId = refreshGroup.Id, Permission = DatasetPermission.Refresh },
            new DatasetAcl { DatasetId = ds.Id, GroupId = editorGroup.Id, Permission = DatasetPermission.Editor },
            new DatasetAcl { DatasetId = ds.Id, GroupId = viewerGroup.Id, Permission = DatasetPermission.Viewer });
        await db.SaveChangesAsync();

        Assert.True(await registry.CanRefreshAsync(name, "Admin"));
        Assert.True(await registry.CanRefreshAsync(name, $"UserId={owner.Id}"));
        Assert.True(await registry.CanRefreshAsync(name, $"UserId={refresher.Id}"));
        Assert.True(await registry.CanRefreshAsync(name, $"UserId={editor.Id}"));
        Assert.False(await registry.CanRefreshAsync(name, $"UserId={viewer.Id}"));
        Assert.False(await registry.CanRefreshAsync(name, $"UserId={outsider.Id}"));
        Assert.False(await registry.CanRefreshAsync(name, ""));
        Assert.False(await registry.CanRefreshAsync($"#nonexistent_{suffix}", "Admin"));

        Assert.True(await registry.CanEditAsync(name, "Admin"));
        Assert.True(await registry.CanEditAsync(name, $"UserId={owner.Id}"));
        Assert.False(await registry.CanEditAsync(name, $"UserId={refresher.Id}"));
        Assert.True(await registry.CanEditAsync(name, $"UserId={editor.Id}"));
        Assert.False(await registry.CanEditAsync(name, $"UserId={viewer.Id}"));
        Assert.False(await registry.CanEditAsync(name, $"UserId={outsider.Id}"));
        Assert.False(await registry.CanEditAsync(name, ""));
        Assert.False(await registry.CanEditAsync($"#nonexistent_{suffix}", "Admin"));
    }

    [Fact]
    [Trait("Category", "Smoke.Security")]
    public async Task DatasetRegistry_PublishedDataset_OwnedByCreatedBy()
    {
        // 2b: a published dataset has no owning report — its owner is Dataset.CreatedBy (the publisher),
        // and its folder is resolved from the target folder's logical Path.
        using var scope = _factory.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IDatasetRegistry>();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var publisher = new PortalUser { UserName = $"pub_{suffix}", Email = $"pub_{suffix}@test.local" };
        var outsider = new PortalUser { UserName = $"out_{suffix}", Email = $"out_{suffix}@test.local" };
        db.Users.AddRange(publisher, outsider);
        await db.SaveChangesAsync();

        var folder = new Folder { Name = $"Pub {suffix}", Path = $"/pub-{suffix}", OwnerId = publisher.Id };
        db.Folders.Add(folder);
        await db.SaveChangesAsync();

        var name = $"#published_{suffix}";
        await registry.RegisterOrUpdate(new DatasetMetadata
        {
            Name = name,
            FolderPath = folder.Path,
            ParquetFilePath = $"published_{suffix}.parquet",
            SourceQuery = "",
            AccessLevel = DatasetAccessLevel.Private,
            CreatedBy = publisher.Id,
            LastRefresh = DateTime.UtcNow
        });

        var stored = await db.Datasets.SingleAsync(d => d.Name == name);
        Assert.Equal(publisher.Id, stored.CreatedBy);
        Assert.Equal(folder.Id, stored.FolderId);   // resolved from the target folder's logical Path

        // Publisher is the owner: can read + edit. Outsider: denied.
        Assert.NotNull(await registry.Lookup(name, $"UserId={publisher.Id}"));
        Assert.Null(await registry.Lookup(name, $"UserId={outsider.Id}"));
        Assert.True(await registry.CanEditAsync(name, $"UserId={publisher.Id}"));
        Assert.False(await registry.CanEditAsync(name, $"UserId={outsider.Id}"));
    }

    [Fact]
    [Trait("Category", "Smoke.Portal")]
    public async Task ReadOnlyReportAccess_AllowsSnapshotAndExportButFiltersPrivateDatasets()
    {
        var adminToken = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var groupRes = await AuthPost(adminToken, "/api/admin/groups", new
        {
            name = $"readonly-report-{suffix}",
            description = "Read-only report access edge case"
        });
        Assert.Equal(HttpStatusCode.Created, groupRes.StatusCode);
        var group = await groupRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var groupId = group!["id"]!.GetValue<int>();

        var username = $"report_ro_{suffix}";
        const string initialPassword = "Viewer@Test1!";
        const string changedPassword = "Viewer@Test2!";
        var userRes = await AuthPost(adminToken, "/api/admin/users", new
        {
            username,
            email = $"{username}@test.local",
            password = initialPassword,
            role = "Viewer"
        });
        Assert.Equal(HttpStatusCode.Created, userRes.StatusCode);
        var user = await userRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var userId = user!["id"]!.GetValue<int>();

        var memberRes = await AuthPost(adminToken, $"/api/admin/groups/{groupId}/members", new { userId });
        Assert.Equal(HttpStatusCode.OK, memberRes.StatusCode);

        var folderName = $"Permission Edge {suffix}";
        var folderRes = await AuthPost(adminToken, "/api/folders", new { name = folderName, parentId = (int?)null });
        Assert.Equal(HttpStatusCode.Created, folderRes.StatusCode);
        var folder = await folderRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var folderId = folder!["id"]!.GetValue<int>();
        var folderPath = folder["path"]!.GetValue<string>();

        var scriptPath = Path.Combine(_factory.TempDir, "scripts", $"permission_edge_{suffix}.rptsql");
        await File.WriteAllTextAsync(scriptPath, @"
CREATE VISUAL EdgeRows AS TABLE (
    SOURCE = (SELECT 1 AS Id, 'Allowed' AS Name),
    MAPPINGS (Id = Id, Name = Name)
);
");

        var publishRes = await AuthPost(adminToken, "/api/reports", new
        {
            folderId,
            name = $"Permission Edge Report {suffix}",
            description = "",
            scriptPath
        });
        Assert.Equal(HttpStatusCode.Created, publishRes.StatusCode);
        var report = await publishRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var reportId = report!["id"]!.GetValue<int>();

        var executeRes = await AuthPost(adminToken, $"/api/reports/{reportId}/execute", new { parameters = new Dictionary<string, string>() });
        Assert.Equal(HttpStatusCode.Accepted, executeRes.StatusCode);
        var executeBody = await executeRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var job = await WaitForJobAsync(adminToken, executeBody!["jobId"]!.GetValue<string>());
        Assert.Equal("Completed", job["status"]!.GetValue<string>());

        using (var scope = _factory.Services.CreateScope())
        {
            var registry = scope.ServiceProvider.GetRequiredService<IDatasetRegistry>();
            await registry.RegisterOrUpdate(new DatasetMetadata
            {
                Name = $"#public_edge_{suffix}",
                FolderPath = folderPath,
                ParquetFilePath = $"public_edge_{suffix}.parquet",
                SourceQuery = "SELECT PublicId FROM finance.PublicOrders",
                AccessLevel = DatasetAccessLevel.Public,
                OwningReportId = reportId
            });
            await registry.RegisterOrUpdate(new DatasetMetadata
            {
                Name = $"#private_edge_{suffix}",
                FolderPath = folderPath,
                ParquetFilePath = $"private_edge_{suffix}.parquet",
                SourceQuery = "SELECT PrivateId FROM finance.PrivateOrders",
                AccessLevel = DatasetAccessLevel.Private,
                OwningReportId = reportId
            });
        }

        var grantReadRes = await AuthPost(adminToken, $"/api/folders/{folderId}/acl", new
        {
            groupId,
            permission = 0
        });
        Assert.Equal(HttpStatusCode.OK, grantReadRes.StatusCode);

        var loginRes = await _client.PostAsJsonAsync("/api/auth/login", new { username, password = initialPassword });
        Assert.Equal(HttpStatusCode.OK, loginRes.StatusCode);
        var loginBody = await loginRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var firstToken = loginBody!["token"]!.GetValue<string>();

        using (var changePassword = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password"))
        {
            changePassword.Headers.Authorization = new("Bearer", firstToken);
            changePassword.Content = JsonContent.Create(new
            {
                currentPassword = initialPassword,
                newPassword = changedPassword
            });
            var changeRes = await _client.SendAsync(changePassword);
            Assert.Equal(HttpStatusCode.NoContent, changeRes.StatusCode);
        }

        var reloginRes = await _client.PostAsJsonAsync("/api/auth/login", new { username, password = changedPassword });
        Assert.Equal(HttpStatusCode.OK, reloginRes.StatusCode);
        var reloginBody = await reloginRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var viewerToken = reloginBody!["token"]!.GetValue<string>();

        var viewerReportRes = await AuthGet(viewerToken, $"/api/reports/{reportId}");
        var viewerSnapshotRes = await AuthGet(viewerToken, $"/api/reports/{reportId}/snapshot?includeManifest=true");
        var viewerExportRes = await AuthGet(viewerToken, $"/api/reports/{reportId}/export/csv?visual=EdgeRows");
        var viewerExecuteRes = await AuthPost(viewerToken, $"/api/reports/{reportId}/execute", new { parameters = new Dictionary<string, string>() });
        var viewerRefreshRes = await AuthPost(viewerToken, $"/api/reports/{reportId}/refresh", new { });

        Assert.Equal(HttpStatusCode.OK, viewerReportRes.StatusCode);
        Assert.Equal(HttpStatusCode.OK, viewerSnapshotRes.StatusCode);
        Assert.Equal(HttpStatusCode.OK, viewerExportRes.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, viewerExecuteRes.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, viewerRefreshRes.StatusCode);

        var adminDependenciesRes = await AuthGet(adminToken, $"/api/reports/{reportId}/dependencies");
        Assert.Equal(HttpStatusCode.OK, adminDependenciesRes.StatusCode);
        var adminDependencies = await adminDependenciesRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var adminDatasetNames = adminDependencies!["registeredDatasets"]!.AsArray()
            .Select(d => d!["name"]!.GetValue<string>())
            .ToHashSet();
        Assert.Contains($"#public_edge_{suffix}", adminDatasetNames);
        Assert.Contains($"#private_edge_{suffix}", adminDatasetNames);

        var viewerDependenciesRes = await AuthGet(viewerToken, $"/api/reports/{reportId}/dependencies");
        Assert.Equal(HttpStatusCode.OK, viewerDependenciesRes.StatusCode);
        var viewerDependencies = await viewerDependenciesRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var viewerDependencyNames = viewerDependencies!["registeredDatasets"]!.AsArray()
            .Select(d => d!["name"]!.GetValue<string>())
            .ToHashSet();
        Assert.Contains($"#public_edge_{suffix}", viewerDependencyNames);
        Assert.DoesNotContain($"#private_edge_{suffix}", viewerDependencyNames);

        var datasetsRes = await AuthGet(viewerToken, "/api/datasets");
        Assert.Equal(HttpStatusCode.OK, datasetsRes.StatusCode);
        var datasets = await datasetsRes.Content.ReadFromJsonAsync<JsonArray>(_json);
        var publicDataset = datasets!.Single(d => d!["name"]!.GetValue<string>() == $"#public_edge_{suffix}")!.AsObject();
        Assert.DoesNotContain(datasets!, d => d!["name"]!.GetValue<string>() == $"#private_edge_{suffix}");

        var publicDatasetId = publicDataset["id"]!.GetValue<int>();
        var privateDatasetId = adminDependencies["registeredDatasets"]!.AsArray()
            .Single(d => d!["name"]!.GetValue<string>() == $"#private_edge_{suffix}")!["id"]!.GetValue<int>();

        var publicDatasetRes = await AuthGet(viewerToken, $"/api/datasets/{publicDatasetId}");
        var privateDatasetRes = await AuthGet(viewerToken, $"/api/datasets/{privateDatasetId}");
        Assert.Equal(HttpStatusCode.OK, publicDatasetRes.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, privateDatasetRes.StatusCode);
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
        var folder = await folderRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var folderId = folder!["id"]!.GetValue<int>();

        var scriptPath = Path.Combine(_factory.TempDir, "scripts", "sub_report.rptsql");
        await File.WriteAllTextAsync(scriptPath, "-- sub report\n");

        var reportRes = await AuthPost(token, "/api/reports", new
        {
            folderId,
            name = "Sub Report",
            description = "",
            scriptPath
        });
        var report = await reportRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var reportId = report!["id"]!.GetValue<int>();

        // Create SMTP connection (required for non-Link format)
        var smtpAlias = await CreateSmtpConnectionAsync(token, "test-smtp");

        // Create subscription (Link format — no attachment export needed)
        var subRes = await AuthPost(token, "/api/subscriptions", new
        {
            reportId = reportId,
            schedule = "Daily",
            format = "Link",
            smtpAlias = smtpAlias,
            recipientEmail = "subscriber@test.local",
            atTime = "08:00"
        });
        Assert.Equal(HttpStatusCode.Created, subRes.StatusCode);
        var sub = await subRes.Content.ReadFromJsonAsync<JsonObject>(_json);
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
        var folder = await folderRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var folderId = folder!["id"]!.GetValue<int>();

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
        var report = await reportRes.Content.ReadFromJsonAsync<JsonObject>(_json);
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
        var sub = await subRes.Content.ReadFromJsonAsync<JsonObject>(_json);
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

        var body = await auditRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var items = body!["items"]!.AsArray();
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

    // ── 7b. Script storage (IArtifactStorage Scripts area) ────────────────────

    [Fact]
    public async Task UploadScript_LandsInScriptRoot_AndIsListed()
    {
        var token = await GetAdminTokenAsync();
        var name = $"uploaded-{Guid.NewGuid():N}.rptsql";
        var content = "-- uploaded report\nSELECT 1;";
        var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(content));

        var up = await AuthPost(token, "/api/scripts/upload",
            new { filename = name, contentBase64 = b64 });
        Assert.Equal(HttpStatusCode.OK, up.StatusCode);

        // Written through the guarded Scripts area → present at the configured script root.
        Assert.True(File.Exists(Path.Combine(_factory.TempDir, "scripts", name)));

        // …and enumerated by the migrated available-scripts listing.
        var listed = await AuthGet(token, "/api/reports/available-scripts");
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        var files = await listed.Content.ReadFromJsonAsync<List<string>>(_json);
        Assert.Contains(name, files!);
    }

    [Fact]
    public async Task UploadScript_RejectsPathSeparatorsAndNonRptsql()
    {
        var token = await GetAdminTokenAsync();
        var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("x"));

        var traversal = await AuthPost(token, "/api/scripts/upload",
            new { filename = "../evil.rptsql", contentBase64 = b64 });
        Assert.Equal(HttpStatusCode.BadRequest, traversal.StatusCode);

        var wrongType = await AuthPost(token, "/api/scripts/upload",
            new { filename = "notes.txt", contentBase64 = b64 });
        Assert.Equal(HttpStatusCode.BadRequest, wrongType.StatusCode);
    }

    // ── 8. Operational hardening ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Smoke.Portal")]
    public async Task Snapshot_FailedRefresh_KeepsLastGoodSnapshot()
    {
        var token = await GetAdminTokenAsync();

        var folderRes = await AuthPost(token, "/api/folders", new { name = "Resilience Folder", parentId = (int?)null });
        var folder = await folderRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var folderId = folder!["id"]!.GetValue<int>();

        var scriptPath = Path.Combine(_factory.TempDir, "scripts", "resilience_report.rptsql");
        await File.WriteAllTextAsync(scriptPath, @"
CREATE VISUAL Answer AS CARD (
    SOURCE = (SELECT 42 AS Value),
    MAPPINGS (VALUE = Value)
);
");

        var publishRes = await AuthPost(token, "/api/reports", new
        {
            folderId,
            name = "Resilience Report",
            description = "",
            scriptPath
        });
        Assert.Equal(HttpStatusCode.Created, publishRes.StatusCode);
        var report = await publishRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var reportId = report!["id"]!.GetValue<int>();

        // First execution — should succeed and produce a snapshot
        var exec1 = await AuthPost(token, $"/api/reports/{reportId}/execute", new { parameters = new Dictionary<string, string>() });
        Assert.Equal(HttpStatusCode.Accepted, exec1.StatusCode);
        var exec1Body = await exec1.Content.ReadFromJsonAsync<JsonObject>(_json);
        var job1 = await WaitForJobAsync(token, exec1Body!["jobId"]!.GetValue<string>());
        Assert.Equal("Completed", job1["status"]!.GetValue<string>());

        // Verify snapshot exists
        var snap1 = await AuthGet(token, $"/api/reports/{reportId}/snapshot?includeManifest=false");
        Assert.Equal(HttpStatusCode.OK, snap1.StatusCode);

        // Delete the script — next run will fail
        File.Delete(scriptPath);

        var exec2 = await AuthPost(token, $"/api/reports/{reportId}/execute", new { parameters = new Dictionary<string, string>() });
        Assert.Equal(HttpStatusCode.Accepted, exec2.StatusCode);
        var exec2Body = await exec2.Content.ReadFromJsonAsync<JsonObject>(_json);
        var job2 = await WaitForJobAsync(token, exec2Body!["jobId"]!.GetValue<string>());
        Assert.Equal("Failed", job2["status"]!.GetValue<string>());

        // Old snapshot must still be accessible despite the failed refresh
        var snap2 = await AuthGet(token, $"/api/reports/{reportId}/snapshot?includeManifest=false");
        Assert.Equal(HttpStatusCode.OK, snap2.StatusCode);
        var snapBody = await snap2.Content.ReadFromJsonAsync<JsonObject>(_json);
        Assert.NotNull(snapBody!["builtAt"]);

        // Catalog listing should surface Failed status and preserve snapshotBuiltAt
        var listRes = await AuthGet(token, $"/api/folders/{folderId}/reports");
        var reports = await listRes.Content.ReadFromJsonAsync<JsonArray>(_json);
        var listed = reports!.Single(r => r!["id"]!.GetValue<int>() == reportId)!.AsObject();
        Assert.Equal("Failed", listed["lastRefreshStatus"]!.GetValue<string>());
        Assert.NotNull(listed["snapshotBuiltAt"]);
    }

    [Fact]
    [Trait("Category", "Smoke.Portal")]
    public async Task Snapshot_ConcurrentRefreshReadsAndExports_ReturnConsistentResponses()
    {
        var token = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var folderRes = await AuthPost(token, "/api/folders", new { name = $"Concurrent Refresh {suffix}", parentId = (int?)null });
        Assert.Equal(HttpStatusCode.Created, folderRes.StatusCode);
        var folder = await folderRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var folderId = folder!["id"]!.GetValue<int>();

        var scriptPath = Path.Combine(_factory.TempDir, "scripts", $"concurrent_refresh_{suffix}.rptsql");
        await File.WriteAllTextAsync(scriptPath, @"
CREATE VISUAL Answer AS TABLE (
    SOURCE = (SELECT 42 AS Value),
    MAPPINGS (Value = Value)
);
");

        var publishRes = await AuthPost(token, "/api/reports", new
        {
            folderId,
            name = $"Concurrent Refresh Report {suffix}",
            description = "",
            scriptPath
        });
        Assert.Equal(HttpStatusCode.Created, publishRes.StatusCode);
        var report = await publishRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var reportId = report!["id"]!.GetValue<int>();

        var execRes = await AuthPost(token, $"/api/reports/{reportId}/execute", new { parameters = new Dictionary<string, string>() });
        Assert.Equal(HttpStatusCode.Accepted, execRes.StatusCode);
        var execBody = await execRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var execJob = await WaitForJobAsync(token, execBody!["jobId"]!.GetValue<string>());
        Assert.Equal("Completed", execJob["status"]!.GetValue<string>());

        var baselineSnapshotRes = await AuthGet(token, $"/api/reports/{reportId}/snapshot?includeManifest=true");
        Assert.Equal(HttpStatusCode.OK, baselineSnapshotRes.StatusCode);
        var baselineSnapshot = await baselineSnapshotRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var baselineBuiltAt = baselineSnapshot!["builtAt"]!.GetValue<DateTime>();

        await File.WriteAllTextAsync(scriptPath, @"
WAITFOR DELAY '00:00:01';
CREATE VISUAL Answer AS TABLE (
    SOURCE = (SELECT 43 AS Value),
    MAPPINGS (Value = Value)
);
");

        var refreshRes = await AuthPost(token, $"/api/reports/{reportId}/refresh", new { });
        Assert.Equal(HttpStatusCode.Accepted, refreshRes.StatusCode);
        var refreshBody = await refreshRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var refreshJobId = refreshBody!["jobId"]!.GetValue<string>();

        await WaitForRunningOrCompletedJobAsync(token, refreshJobId);

        var readTasks = new[]
        {
            AuthGet(token, $"/api/reports/{reportId}/snapshot?includeManifest=true"),
            AuthGet(token, $"/api/reports/{reportId}/snapshot/manifest"),
            AuthGet(token, $"/api/reports/{reportId}/history"),
            AuthGet(token, $"/api/reports/{reportId}"),
            AuthGet(token, $"/api/folders/{folderId}/reports"),
            AuthGet(token, $"/api/reports/{reportId}/export/csv"),
            AuthGet(token, $"/api/reports/{reportId}/export/xlsx"),
            AuthGet(token, $"/api/reports/{reportId}/export/pdf")
        };
        var duplicateRefreshTask = AuthPost(token, $"/api/reports/{reportId}/refresh", new { });

        var responses = await Task.WhenAll(readTasks.Append(duplicateRefreshTask));

        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        var duringRefreshSnapshot = await responses[0].Content.ReadFromJsonAsync<JsonObject>(_json);
        Assert.NotNull(duringRefreshSnapshot!["manifest"]);
        Assert.True(duringRefreshSnapshot["builtAt"]!.GetValue<DateTime>() >= baselineBuiltAt);

        Assert.Equal(HttpStatusCode.OK, responses[1].StatusCode);
        Assert.Equal(HttpStatusCode.OK, responses[2].StatusCode);
        Assert.Equal(HttpStatusCode.OK, responses[3].StatusCode);
        Assert.Equal(HttpStatusCode.OK, responses[4].StatusCode);
        Assert.Equal(HttpStatusCode.OK, responses[5].StatusCode);
        Assert.Equal("text/csv", responses[5].Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.OK, responses[6].StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", responses[6].Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.OK, responses[7].StatusCode);
        Assert.Equal("application/pdf", responses[7].Content.Headers.ContentType?.MediaType);

        Assert.Equal(HttpStatusCode.Accepted, responses[8].StatusCode);
        var duplicateRefresh = await responses[8].Content.ReadFromJsonAsync<JsonObject>(_json);
        Assert.Equal(refreshJobId, duplicateRefresh!["jobId"]!.GetValue<string>());
        Assert.True(duplicateRefresh["alreadyRunning"]!.GetValue<bool>());

        var refreshJob = await WaitForJobAsync(token, refreshJobId);
        Assert.Equal("Completed", refreshJob["status"]!.GetValue<string>());
    }

    [Fact]
    [Trait("Category", "Smoke.Portal")]
    public async Task AuditLog_RecordsViewSnapshotExportAndSubscriptionEvents()
    {
        var token = await GetAdminTokenAsync();

        var folderRes = await AuthPost(token, "/api/folders", new { name = "Audit Events Folder", parentId = (int?)null });
        var folder = await folderRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var folderId = folder!["id"]!.GetValue<int>();

        var scriptPath = Path.Combine(_factory.TempDir, "scripts", "audit_events_report.rptsql");
        await File.WriteAllTextAsync(scriptPath, @"
CREATE VISUAL Summary AS TABLE (
    SOURCE = (SELECT 1 AS Id, 'Alpha' AS Name),
    MAPPINGS (Id = Id, Name = Name)
);
");

        var publishRes = await AuthPost(token, "/api/reports", new
        {
            folderId,
            name = "Audit Events Report",
            description = "",
            scriptPath
        });
        Assert.Equal(HttpStatusCode.Created, publishRes.StatusCode);
        var report = await publishRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var reportId = report!["id"]!.GetValue<int>();

        // Execute to get a snapshot (required for CSV export)
        var execRes = await AuthPost(token, $"/api/reports/{reportId}/execute", new { parameters = new Dictionary<string, string>() });
        Assert.Equal(HttpStatusCode.Accepted, execRes.StatusCode);
        var execBody = await execRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var job = await WaitForJobAsync(token, execBody!["jobId"]!.GetValue<string>());
        Assert.Equal("Completed", job["status"]!.GetValue<string>());

        // Trigger VIEW_SNAPSHOT
        var snapRes = await AuthGet(token, $"/api/reports/{reportId}/snapshot?includeManifest=false");
        Assert.Equal(HttpStatusCode.OK, snapRes.StatusCode);

        // Trigger EXPORT_CSV
        var csvRes = await AuthGet(token, $"/api/reports/{reportId}/export/csv");
        Assert.Equal(HttpStatusCode.OK, csvRes.StatusCode);

        // Trigger CREATE_SUBSCRIPTION and DELETE_SUBSCRIPTION
        var smtpAlias = await CreateSmtpConnectionAsync(token, "audit-smtp");

        var subRes = await AuthPost(token, "/api/subscriptions", new
        {
            reportId,
            schedule = "Daily",
            format = "Link",
            smtpAlias,
            recipientEmail = "audit@test.local",
            atTime = "09:00"
        });
        Assert.Equal(HttpStatusCode.Created, subRes.StatusCode);
        var subBody = await subRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var subId = subBody!["id"]!.GetValue<int>();

        var delSubRes = await AuthDelete(token, $"/api/subscriptions/{subId}");
        Assert.Equal(HttpStatusCode.NoContent, delSubRes.StatusCode);

        // Verify all expected audit events exist
        var auditRes = await AuthGet(token, "/api/admin/audit?pageSize=500");
        Assert.Equal(HttpStatusCode.OK, auditRes.StatusCode);
        var auditBody = await auditRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var items = auditBody!["items"]!.AsArray()
            .Select(i => i!["action"]!.GetValue<string>())
            .ToHashSet();

        Assert.Contains("VIEW_SNAPSHOT", items);
        Assert.Contains("EXPORT_CSV", items);
        Assert.Contains("CREATE_SUBSCRIPTION", items);
        Assert.Contains("DELETE_SUBSCRIPTION", items);
    }

    [Fact]
    [Trait("Category", "Smoke.Portal")]
    public async Task Subscription_WithParameters_PersistsAndRoundTrips()
    {
        var token = await GetAdminTokenAsync();

        var folderRes = await AuthPost(token, "/api/folders", new { name = "Parameterized Sub Folder", parentId = (int?)null });
        var folder = await folderRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var folderId = folder!["id"]!.GetValue<int>();

        var scriptPath = Path.Combine(_factory.TempDir, "scripts", "param_sub_report.rptsql");
        await File.WriteAllTextAsync(scriptPath, "DECLARE @Region STRING INPUT = 'All';");

        var publishRes = await AuthPost(token, "/api/reports", new
        {
            folderId,
            name = "Param Sub Report",
            description = "",
            scriptPath
        });
        Assert.Equal(HttpStatusCode.Created, publishRes.StatusCode);
        var report = await publishRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var reportId = report!["id"]!.GetValue<int>();

        var smtpAlias = await CreateSmtpConnectionAsync(token, "param-smtp");

        var subRes = await AuthPost(token, "/api/subscriptions", new
        {
            reportId,
            schedule = "Daily",
            format = "Link",
            smtpAlias,
            recipientEmail = "regional@test.local",
            atTime = "07:00",
            parameters = new Dictionary<string, string> { ["Region"] = "EMEA" }
        });
        Assert.Equal(HttpStatusCode.Created, subRes.StatusCode);
        var subBody = await subRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var subId = subBody!["id"]!.GetValue<int>();

        // Verify the GET round-trip preserves parameters
        var getRes = await AuthGet(token, $"/api/subscriptions/{subId}");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var getBody = await getRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var param = getBody!["parameters"]!.AsObject()["Region"]!.GetValue<string>();
        Assert.Equal("EMEA", param);

        // Update the parameter value
        var updateRes = await AuthPut(token, $"/api/subscriptions/{subId}", new
        {
            parameters = new Dictionary<string, string> { ["Region"] = "NA" }
        });
        Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);
        var updatedBody = await updateRes.Content.ReadFromJsonAsync<JsonObject>(_json);
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
            var body = await loginRes.Content.ReadFromJsonAsync<JsonObject>(_json);
            var token = body!["token"]!.GetValue<string>();

            // Change password once so subsequent tests don't trigger lockout
            using var cpReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password");
            cpReq.Headers.Authorization = new("Bearer", token);
            cpReq.Content = JsonContent.Create(new
            {
                currentPassword = "Admin@12345!",
                newPassword = "Admin@Tests99!"
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

    private async Task<HttpResponseMessage> AuthPost(string token, string url, object body)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new("Bearer", token);
        req.Content = JsonContent.Create(body);
        await IfMatchVersioning.StampAsync(_client, req, await GetAdminTokenAsync());
        return await _client.SendAsync(req);
    }

    /// <summary>
    /// Registers an SMTP connection in the governed catalog and returns its alias. Two steps rather
    /// than one: the catalog stores <c>SECRET:</c> references and rejects literal credentials, so
    /// the value goes to the Portal secret store first and the connection references it by name.
    /// </summary>
    private async Task<string> CreateSmtpConnectionAsync(string token, string prefix)
    {
        var alias = $"{prefix}-{Guid.NewGuid():N}"[..16];
        var secretName = alias.Replace('-', '_') + "_password";

        Assert.True((await AuthPut(token, $"/api/admin/secrets/{secretName}",
            new { value = "smtppassword" })).IsSuccessStatusCode);

        var res = await AuthPut(token, $"/api/admin/connections/{alias}", new
        {
            connectorType = "SMTP",
            options = new Dictionary<string, string>
            {
                ["HOST"] = "smtp.test.local",
                ["PORT"] = "587",
                ["USERNAME"] = "user@test.local",
                ["PASSWORD"] = $"SECRET:{secretName}",
                ["DEFAULT_FROM"] = "noreply@test.local",
                ["USE_SSL"] = "true"
            },
            sensitiveFields = new[] { "PASSWORD" }
        });
        Assert.True(res.IsSuccessStatusCode, $"connection register failed: {res.StatusCode}");
        return alias;
    }

    private async Task<HttpResponseMessage> AuthPut(string token, string url, object body)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, url);
        req.Headers.Authorization = new("Bearer", token);
        req.Content = JsonContent.Create(body);
        await IfMatchVersioning.StampAsync(_client, req, await GetAdminTokenAsync());
        return await _client.SendAsync(req);
    }

    private async Task<HttpResponseMessage> AuthDelete(string token, string url)
    {
        var req = new HttpRequestMessage(HttpMethod.Delete, url);
        req.Headers.Authorization = new("Bearer", token);
        await IfMatchVersioning.StampAsync(_client, req, await GetAdminTokenAsync());
        return await _client.SendAsync(req);
    }

    private async Task<JsonObject> WaitForJobAsync(string token, string jobId)
    {
        JsonObject? job = null;
        for (var i = 0; i < 300; i++)
        {
            var jobRes = await AuthGet(token, $"/api/jobs/{jobId}");
            Assert.Equal(HttpStatusCode.OK, jobRes.StatusCode);
            job = await jobRes.Content.ReadFromJsonAsync<JsonObject>(_json);
            var status = job!["status"]!.GetValue<string>();
            if (status is "Completed" or "Failed" or "Cancelled")
                return job;

            await Task.Delay(200);
        }

        Assert.NotNull(job);
        return job!;
    }

    private async Task<JsonObject> WaitForRunningOrCompletedJobAsync(string token, string jobId)
    {
        JsonObject? job = null;
        for (var i = 0; i < 300; i++)
        {
            var jobRes = await AuthGet(token, $"/api/jobs/{jobId}");
            Assert.Equal(HttpStatusCode.OK, jobRes.StatusCode);
            job = await jobRes.Content.ReadFromJsonAsync<JsonObject>(_json);
            var status = job!["status"]!.GetValue<string>();
            if (status is "Running" or "Completed" or "Failed" or "Cancelled")
                return job;

            await Task.Delay(50);
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

    [Fact]
    public async Task Report_InteractiveParameters_PersistsLineage_WhenConfigEnabled()
    {
        // Arrange
        var config = _factory.Services.GetRequiredService<ETL_SQL.Portal.PortalConfig>();
        config.Resources.PersistAdHocInteractions = true;

        var token = await GetAdminTokenAsync();

        var folderRes = await AuthPost(token, "/api/folders", new { name = "Interactive Folder", parentId = (int?)null });
        Assert.Equal(HttpStatusCode.Created, folderRes.StatusCode);
        var folder = await folderRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var folderId = folder!["id"]!.GetValue<int>();

        var visualName = $"IntVisual_{Guid.NewGuid():N}";
        var scriptPath = Path.Combine(_factory.TempDir, "scripts", "interactive_report.rptsql");
        await File.WriteAllTextAsync(scriptPath, $@"
DECLARE @Region VARCHAR(50) = 'EMEA';
CREATE VISUAL {visualName} AS CARD (
    SOURCE = (SELECT @Region AS Region),
    MAPPINGS (VALUE = Region)
);
");

        var publishRes = await AuthPost(token, "/api/reports", new
        {
            folderId,
            name = "Interactive Report",
            scriptPath
        });
        Assert.Equal(HttpStatusCode.Created, publishRes.StatusCode);
        var report = await publishRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var reportId = report!["id"]!.GetValue<int>();

        // Execute once to create snapshot and initialize session
        var executeRes = await AuthPost(token, $"/api/reports/{reportId}/execute", new { parameters = new Dictionary<string, string>() });
        Assert.Equal(HttpStatusCode.Accepted, executeRes.StatusCode);
        var executeBody = await executeRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var jobId = executeBody!["jobId"]!.GetValue<string>();
        await WaitForJobAsync(token, jobId);

        // Act - Interaction with parameters
        var interactRes = await AuthPost(token, $"/api/reports/{reportId}/parameters", new
        {
            @params = new[]
            {
                new { name = "Region", value = "APAC", isInteraction = true }
            },
            isInteraction = true
        });
        Assert.Equal(HttpStatusCode.OK, interactRes.StatusCode);

        // Assert - Verify that lineage was persisted under interactive job run
        using (var scope = _factory.Services.CreateScope())
        {
            var catalog = scope.ServiceProvider.GetRequiredService<ILineageCatalogStore>();
            var lineage = (await catalog.GetHistoryForJobAsync($"report:{reportId}:interaction", 20)).ToList();
            Assert.NotEmpty(lineage);
            Assert.Contains(lineage, e =>
                e.TargetTable == $"report:{visualName}" &&
                e.Operation == "CREATE VISUAL" &&
                e.ScriptPath == scriptPath);
        }
    }

    private sealed class HaExpectedWithStandaloneStateFactory : PortalWebFactory
    {
        protected override void CustomizePortalConfig(PortalConfig config)
        {
            config.Topology.ExpectedMode = "HighAvailability";
        }

        protected override void CustomizeConfiguration(Dictionary<string, string?> settings)
        {
            settings["Portal:Topology:ExpectedMode"] = "HighAvailability";
        }
    }

    private sealed class DocumentationDisabledFactory : PortalWebFactory
    {
        protected override void CustomizePortalConfig(PortalConfig config)
        {
            config.Modules.Documentation = false;
        }

        protected override void CustomizeConfiguration(Dictionary<string, string?> settings)
        {
            settings["Portal:Modules:Documentation"] = "false";
        }
    }
}

