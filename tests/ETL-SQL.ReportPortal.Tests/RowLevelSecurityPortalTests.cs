using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.ReportPortal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.ReportPortal.Tests;

/// <summary>
/// End-to-end row-level-security behavior over the full HTTP path: identity-sensitive reports are
/// never persisted as a shared snapshot (closing the cross-viewer leak), and admin impersonation
/// runs under the target's identity while auditing the real actor. Engine-level filtering
/// (HAS_GROUP restricting rows) is covered by RowLevelSecurityIdentityTests.
/// </summary>
[Trait("Category", "Portal")]
public class RowLevelSecurityPortalTests : IClassFixture<PortalWebFactory>
{
    private readonly HttpClient _client;
    private readonly PortalWebFactory _factory;
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private static string? _adminToken;
    private static readonly SemaphoreSlim _tokenLock = new(1, 1);

    public RowLevelSecurityPortalTests(PortalWebFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task IdentitySensitiveReport_PersistsNoSharedSnapshot_PlainReportDoes()
    {
        var token = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var folderId = await CreateFolderAsync(token, $"RLS Folder {suffix}");

        // Identity-sensitive: references HAS_GROUP, so it must never be cached as a shared snapshot.
        var sensitiveId = await CreateReportAsync(token, folderId, $"rls_sensitive_{suffix}",
            $"CREATE VISUAL Sens_{suffix} AS CARD (SOURCE = (SELECT 42 AS Answer, HAS_GROUP('Region:East') AS Allowed), MAPPINGS (VALUE = Answer));");

        // Control: no identity reference, so it caches a shared snapshot as usual.
        var plainId = await CreateReportAsync(token, folderId, $"rls_plain_{suffix}",
            $"CREATE VISUAL Plain_{suffix} AS CARD (SOURCE = (SELECT 42 AS Answer), MAPPINGS (VALUE = Answer));");

        await ExecuteAndAwaitAsync(token, sensitiveId);
        await ExecuteAndAwaitAsync(token, plainId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();

        var sensitiveSnapshots = await db.ReportSnapshots.CountAsync(s => s.ReportId == sensitiveId);
        var plainSnapshots = await db.ReportSnapshots.CountAsync(s => s.ReportId == plainId);

        Assert.Equal(0, sensitiveSnapshots);
        Assert.True(plainSnapshots >= 1, "A non-identity report should persist a shared snapshot.");
    }

    [Fact]
    public async Task AdminImpersonation_Runs_AuditsRealActor_AndSharesNoSnapshot()
    {
        var token = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var folderId = await CreateFolderAsync(token, $"RLS Imp Folder {suffix}");

        int targetUserId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var target = new PortalUser
            {
                UserName = $"rls_target_{suffix}",
                NormalizedUserName = $"RLS_TARGET_{suffix}".ToUpperInvariant(),
                Email = $"rls_target_{suffix}@test.local"
            };
            db.Users.Add(target);
            var group = new Group { Name = $"Region:East {suffix}" };
            db.Groups.Add(group);
            await db.SaveChangesAsync();
            db.UserGroups.Add(new UserGroup { UserId = target.Id, GroupId = group.Id });
            await db.SaveChangesAsync();
            targetUserId = target.Id;
        }

        var reportId = await CreateReportAsync(token, folderId, $"rls_imp_{suffix}",
            $"CREATE VISUAL Imp_{suffix} AS CARD (SOURCE = (SELECT 42 AS Answer, HAS_GROUP('Region:East {suffix}') AS Allowed), MAPPINGS (VALUE = Answer));");

        var execRes = await AuthPost(token, $"/api/reports/{reportId}/execute-as/{targetUserId}",
            new { parameters = new Dictionary<string, string>() });
        Assert.Equal(HttpStatusCode.Accepted, execRes.StatusCode);
        var body = await execRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var job = await WaitForJobAsync(token, body!["jobId"]!.GetValue<string>());
        Assert.Equal("Completed", job["status"]!.GetValue<string>());

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<PortalDbContext>();

        // Audited as the real admin acting as the target; impersonated run left no shared snapshot.
        var audit = await verifyDb.AuditLogs
            .Where(a => a.Action == "EXECUTE_REPORT_AS" && a.ResourceId == reportId.ToString())
            .OrderByDescending(a => a.Id)
            .FirstOrDefaultAsync();
        Assert.NotNull(audit);
        Assert.Contains(targetUserId.ToString(), audit!.Detail);

        Assert.Equal(0, await verifyDb.ReportSnapshots.CountAsync(s => s.ReportId == reportId));
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private async Task<int> CreateFolderAsync(string token, string name)
    {
        var res = await AuthPost(token, "/api/folders", new { name, parentId = (int?)null });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonObject>(_json);
        return body!["id"]!.GetValue<int>();
    }

    private async Task<int> CreateReportAsync(string token, int folderId, string fileStem, string script)
    {
        var scriptPath = Path.Combine(_factory.TempDir, "scripts", $"{fileStem}.rptsql");
        await File.WriteAllTextAsync(scriptPath, script);
        var res = await AuthPost(token, "/api/reports", new
        {
            folderId,
            name = fileStem,
            description = "RLS test",
            scriptPath
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonObject>(_json);
        return body!["id"]!.GetValue<int>();
    }

    private async Task ExecuteAndAwaitAsync(string token, int reportId)
    {
        var res = await AuthPost(token, $"/api/reports/{reportId}/execute",
            new { parameters = new Dictionary<string, string>() });
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonObject>(_json);
        var job = await WaitForJobAsync(token, body!["jobId"]!.GetValue<string>());
        Assert.Equal("Completed", job["status"]!.GetValue<string>());
    }

    private async Task<JsonObject> WaitForJobAsync(string token, string jobId)
    {
        for (var i = 0; i < 300; i++)
        {
            var res = await AuthGet(token, $"/api/jobs/{jobId}");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var job = await res.Content.ReadFromJsonAsync<JsonObject>(_json);
            var status = job!["status"]!.GetValue<string>();
            if (status is "Completed" or "Failed" or "Cancelled")
                return job;
            await Task.Delay(200);
        }
        throw new Xunit.Sdk.XunitException($"Job {jobId} did not reach a terminal state.");
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
        return await _client.SendAsync(req);
    }

    private async Task<string> GetAdminTokenAsync()
    {
        await _tokenLock.WaitAsync();
        try
        {
            if (_adminToken is not null) return _adminToken;

            // The seeded admin has MustChangePassword=true; the first-login token is restricted
            // (no effective role authority), so change the password once and re-login for a full token.
            var loginRes = await _client.PostAsJsonAsync("/api/auth/login",
                new { username = "admin", password = "Admin@12345!" });
            loginRes.EnsureSuccessStatusCode();
            var body = await loginRes.Content.ReadFromJsonAsync<JsonObject>(_json);
            var firstToken = body!["token"]!.GetValue<string>();

            using var cpReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password");
            cpReq.Headers.Authorization = new("Bearer", firstToken);
            cpReq.Content = JsonContent.Create(new
            {
                currentPassword = "Admin@12345!",
                newPassword = "Admin@Tests99!"
            });
            (await _client.SendAsync(cpReq)).EnsureSuccessStatusCode();

            var reloginRes = await _client.PostAsJsonAsync("/api/auth/login",
                new { username = "admin", password = "Admin@Tests99!" });
            reloginRes.EnsureSuccessStatusCode();
            var reloginBody = await reloginRes.Content.ReadFromJsonAsync<JsonObject>(_json);
            return _adminToken = reloginBody!["token"]!.GetValue<string>();
        }
        finally
        {
            _tokenLock.Release();
        }
    }
}
