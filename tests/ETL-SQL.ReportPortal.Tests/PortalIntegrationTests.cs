using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

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
    public async Task Report_PublishAndGet_RoundTrips()
    {
        var token = await GetAdminTokenAsync();

        // Create folder
        var folderRes = await AuthPost(token, "/api/folders", new { name = "Rpt Folder", parentId = (int?)null });
        var folder    = await folderRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var folderId  = folder!["id"]!.GetValue<int>();

        // Write a dummy .rptsql file in the temp script root
        var scriptPath = Path.Combine(_factory.TempDir, "scripts", "dummy_report.rptsql");
        await File.WriteAllTextAsync(scriptPath, "-- dummy report\n");

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

        // Verify GET returns it
        var getRes = await AuthGet(token, $"/api/folders/{folderId}/reports");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var reports = await getRes.Content.ReadFromJsonAsync<JsonArray>(_json);
        Assert.Contains(reports!, r => r!["id"]!.GetValue<int>() == reportId);
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

    private Task<HttpResponseMessage> AuthDelete(string token, string url)
    {
        var req = new HttpRequestMessage(HttpMethod.Delete, url);
        req.Headers.Authorization = new("Bearer", token);
        return _client.SendAsync(req);
    }
}
