using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// A report that filters rows by its viewer, read back as a viewer. It keeps no snapshot, so it is
/// answered from the caller's own session under the caller's identity: two readers of one report
/// get disjoint rows, a row belonging to neither group reaches neither, and nothing is stored.
/// <c>RowLevelSecurityPortalTests</c> covers the job path; these cover what a reader receives.
/// </summary>
[Trait("Category", "Portal")]
public sealed class RowLevelSecurityLiveViewTests : IClassFixture<PortalWebFactory>
{
    private readonly HttpClient _client;
    private readonly PortalWebFactory _factory;
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private string? _adminToken;

    public RowLevelSecurityLiveViewTests(PortalWebFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // West belongs to neither group: a row no reader of this report may see.
    private static string Script(string north, string south) => $"""
        CREATE TABLE #sales (SaleID INT, Region VARCHAR(20), Total DECIMAL(10,2));
        INSERT INTO #sales VALUES (1, 'North', 100), (2, 'North', 150), (3, 'South', 80),
          (4, 'South', 60), (5, 'West', 200), (6, 'West', 40);
        CREATE VISUAL Detail AS TABLE (
          SOURCE = (SELECT SaleID, Region, Total FROM #sales
                    WHERE (Region = 'North' AND HAS_GROUP('{north}'))
                       OR (Region = 'South' AND HAS_GROUP('{south}')))
        );
        CREATE VISUAL Stamp AS TABLE (SOURCE = (SELECT CAST(NEWID() AS VARCHAR(40)) AS Token));
        CREATE PAGE [Main] AS DASHBOARD (LAYOUT (STRUCTURE = 'A B', MAP ('A' = Detail, 'B' = Stamp)));
        """;

    [Fact]
    public async Task EachReaderGetsOnlyTheirRows_WithReadAlone_AndNothingIsStored()
    {
        var seed = await SeedAsync(FolderPermission.Read);

        Assert.Equal(new[] { 1, 2 }, await SaleIdsAsync(seed.NorthToken, seed.ReportId));
        Assert.Equal(new[] { 3, 4 }, await SaleIdsAsync(seed.SouthToken, seed.ReportId));
        // Asked again after the other reader, the first still gets only their own.
        Assert.Equal(new[] { 1, 2 }, await SaleIdsAsync(seed.NorthToken, seed.ReportId));

        Assert.Equal(HttpStatusCode.NotFound,
            (await AuthGet(seed.NorthToken, $"/api/reports/{seed.ReportId}/snapshot")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await AuthGet(seed.NorthToken, $"/api/reports/{seed.ReportId}/snapshot/manifest")).StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        Assert.Equal(0, await db.ReportSnapshots.CountAsync(s => s.ReportId == seed.ReportId));
        Assert.True(await db.AuditLogs.AnyAsync(
            a => a.Action == "VIEW_REPORT_LIVE" && a.ResourceId == seed.ReportId.ToString()));
    }

    [Fact]
    public async Task AReaderRemovedFromTheirGroupGetsNoRowsOnTheNextRequest()
    {
        var seed = await SeedAsync(FolderPermission.Read);
        Assert.Equal(new[] { 1, 2 }, await SaleIdsAsync(seed.NorthToken, seed.ReportId));

        // A second group keeps their folder grant; only the region membership goes.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var keep = new Group { Name = $"keep_{Guid.NewGuid():N}"[..20] };
            db.Groups.Add(keep);
            await db.SaveChangesAsync();
            db.UserGroups.Add(new UserGroup { UserId = seed.NorthUserId, GroupId = keep.Id });
            db.FolderAcls.Add(new FolderAcl { FolderId = seed.FolderId, GroupId = keep.Id, Permission = FolderPermission.Read });
            db.UserGroups.Remove(await db.UserGroups.SingleAsync(
                ug => ug.UserId == seed.NorthUserId && ug.GroupId == seed.NorthGroupId));
            await db.SaveChangesAsync();
        }

        Assert.Empty(await SaleIdsAsync(seed.NorthToken, seed.ReportId));
    }

    [Fact]
    public async Task AReaderWithNoGrantIsRefused()
    {
        var seed = await SeedAsync(FolderPermission.Read);
        var outsiderName = $"outsider_{Guid.NewGuid():N}"[..20];
        await CreatePortalUserAsync(outsiderName, "Out@Test1!", "Viewer");
        var outsider = await LoginWithRequiredChangeAsync(outsiderName, "Out@Test1!", "Out@Test2!");

        Assert.Equal(HttpStatusCode.Forbidden,
            (await AuthGet(outsider, $"/api/reports/{seed.ReportId}/manifest")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await AuthPost(outsider, $"/api/reports/{seed.ReportId}/live/reset", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await AuthGet(outsider, $"/api/reports/{seed.ReportId}/export/csv")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await AuthPost(outsider, $"/api/reports/{seed.ReportId}/parameters", new { @params = Array.Empty<object>() })).StatusCode);
    }

    [Theory]
    [InlineData(true, new[] { 1, 2, 3, 4 })]
    [InlineData(false, new int[0])]
    public async Task AnAdministratorFollowsTheBypassSetting(bool bypass, int[] expected)
    {
        var seed = await SeedAsync(FolderPermission.Read);
        var config = _factory.Services.GetRequiredService<PortalConfig>();
        var original = config.Security.AdminBypassRowLevelSecurity;
        config.Security.AdminBypassRowLevelSecurity = bypass;
        try
        {
            Assert.Equal(expected, await SaleIdsAsync(await GetAdminTokenAsync(), seed.ReportId));
        }
        finally
        {
            config.Security.AdminBypassRowLevelSecurity = original;
        }
    }

    [Fact]
    public async Task ResetRunsTheReportAgainForTheCallerOnly()
    {
        var seed = await SeedAsync(FolderPermission.Read);
        var first = await TokenAsync(seed.NorthToken, seed.ReportId);
        var southFirst = await TokenAsync(seed.SouthToken, seed.ReportId);
        // Without a reset the reader keeps the result of their one run.
        Assert.Equal(first, await TokenAsync(seed.NorthToken, seed.ReportId));

        var reset = await AuthPost(seed.NorthToken, $"/api/reports/{seed.ReportId}/live/reset", new { });

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.NotEqual(first, await TokenAsync(seed.NorthToken, seed.ReportId));
        Assert.Equal(new[] { 1, 2 }, await SaleIdsAsync(seed.NorthToken, seed.ReportId));
        Assert.Equal(southFirst, await TokenAsync(seed.SouthToken, seed.ReportId));
    }

    /// <summary>
    /// The author tightens the filter and saves. A reader who already has the report open must get
    /// the new filter on their next request: no snapshot run exists to end their session.
    /// </summary>
    [Fact]
    public async Task ARepublishedScriptAppliesToAReaderWhoAlreadyHasItOpen()
    {
        var seed = await SeedAsync(FolderPermission.Read);
        Assert.Equal(new[] { 1, 2 }, await SaleIdsAsync(seed.NorthToken, seed.ReportId));

        // North's group no longer unlocks anything.
        await File.WriteAllTextAsync(seed.ScriptPath, Script($"nobody_{Guid.NewGuid():N}", seed.SouthGroup));
        File.SetLastWriteTimeUtc(seed.ScriptPath, DateTime.UtcNow.AddSeconds(5));

        Assert.Empty(await SaleIdsAsync(seed.NorthToken, seed.ReportId));
        Assert.Equal(new[] { 3, 4 }, await SaleIdsAsync(seed.SouthToken, seed.ReportId));
    }

    /// <summary>
    /// For a service account, reading does not include running. A token with only the read scope
    /// could not run a report before these reports were shown live, and still cannot.
    /// </summary>
    [Theory]
    [InlineData(new[] { "portal.read" }, HttpStatusCode.Forbidden)]
    [InlineData(new[] { "portal.read", "reports.execute" }, HttpStatusCode.OK)]
    public async Task AServiceAccountNeedsTheExecuteScopeToRunTheReport(string[] scopes, HttpStatusCode expected)
    {
        var seed = await SeedAsync(FolderPermission.Read);
        // Owned by the north reader, so it acts with that reader's Read grant.
        var token = await ServiceTokenAsync(scopes, seed.NorthUserId);

        Assert.Equal(expected, (await AuthGet(token, $"/api/reports/{seed.ReportId}/manifest")).StatusCode);
        Assert.Equal(expected, (await AuthGet(token, $"/api/reports/{seed.ReportId}/export/csv")).StatusCode);
    }

    [Fact]
    public async Task ASessionWithNoIdentityReturnsNoRows()
    {
        var seed = await SeedAsync(FolderPermission.Read);
        var sessions = _factory.Services.GetRequiredService<ETL_SQL.Portal.Services.SessionCache>();

        var manifest = await sessions.GetOrCreate(seed.ReportId, userId: int.MaxValue, seed.ScriptPath, executionIdentity: null)
            .GetManifestAsync();

        var detail = manifest.Visuals.Single(visual => visual.Name == "Detail");
        Assert.Empty(detail.Rows);
    }

    [Fact]
    public async Task AnOrdinaryReportIsStillServedFromItsSnapshot_EvenWithItsScriptGone()
    {
        var admin = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var folderId = await CreateFolderAsync($"Plain snap {suffix}");
        var stem = $"plain_snap_{suffix}";
        var reportId = await CreateReportAsync(folderId, stem,
            "CREATE VISUAL P AS TABLE (SOURCE = (SELECT 4242 AS Answer));");

        var accepted = await AuthPost(admin, $"/api/reports/{reportId}/execute", new { parameters = new Dictionary<string, string>() });
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var jobId = (await accepted.Content.ReadFromJsonAsync<JsonObject>(_json))!["jobId"]!.GetValue<string>();
        for (var attempt = 0; ; attempt++)
        {
            var job = await (await AuthGet(admin, $"/api/jobs/{jobId}")).Content.ReadFromJsonAsync<JsonObject>(_json);
            var status = job!["status"]!.GetValue<string>();
            if (status == "Completed") break;
            Assert.True(status is not ("Failed" or "Cancelled") && attempt < 300, $"The run ended as {status}.");
            await Task.Delay(200);
        }

        var report = await (await AuthGet(admin, $"/api/reports/{reportId}")).Content.ReadFromJsonAsync<JsonObject>(_json);
        Assert.False(report!["perViewer"]!.GetValue<bool>());
        Assert.True(report["hasSnapshot"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.OK, (await AuthGet(admin, $"/api/reports/{reportId}/manifest")).StatusCode);
        Assert.Contains("4242", await (await AuthGet(admin, $"/api/reports/{reportId}/export/csv")).Content.ReadAsStringAsync());
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            Assert.Equal(1, await db.ReportSnapshots.CountAsync(s => s.ReportId == reportId));
            Assert.False(await db.AuditLogs.AnyAsync(a => a.Action == "VIEW_REPORT_LIVE" && a.ResourceId == reportId.ToString()));
        }

        // A script that is gone cannot be run for anyone; the snapshot is still what there is to show.
        File.Delete(Path.Combine(_factory.TempDir, "scripts", $"{stem}.rptsql"));
        Assert.Equal(HttpStatusCode.OK, (await AuthGet(admin, $"/api/reports/{reportId}/manifest")).StatusCode);
        Assert.Contains("4242", await (await AuthGet(admin, $"/api/reports/{reportId}/export/csv")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AnOrdinaryReportStillHasNoManifestUntilItHasASnapshot()
    {
        var admin = await GetAdminTokenAsync();
        var folderId = await CreateFolderAsync($"Plain {Guid.NewGuid():N}"[..20]);
        var reportId = await CreateReportAsync(folderId, $"plain_{Guid.NewGuid():N}"[..20],
            "CREATE VISUAL P AS CARD (SOURCE = (SELECT 42 AS Answer), MAPPINGS (VALUE = Answer));");

        Assert.Equal(HttpStatusCode.NotFound, (await AuthGet(admin, $"/api/reports/{reportId}/manifest")).StatusCode);
    }

    [Fact]
    public async Task TheReportSaysItIsPerViewer()
    {
        var seed = await SeedAsync(FolderPermission.Read);

        var report = await (await AuthGet(seed.NorthToken, $"/api/reports/{seed.ReportId}"))
            .Content.ReadFromJsonAsync<JsonObject>(_json);

        Assert.True(report!["perViewer"]!.GetValue<bool>());
        Assert.False(report["hasSnapshot"]!.GetValue<bool>());
    }

    [Fact]
    public async Task EachReadersCsvHoldsOnlyTheirRows()
    {
        var seed = await SeedAsync(FolderPermission.Read);

        var north = await (await AuthGet(seed.NorthToken, $"/api/reports/{seed.ReportId}/export/csv")).Content.ReadAsStringAsync();
        var south = await (await AuthGet(seed.SouthToken, $"/api/reports/{seed.ReportId}/export/csv")).Content.ReadAsStringAsync();

        Assert.Contains("North", north);
        Assert.DoesNotContain("South", north);
        Assert.DoesNotContain("West", north);
        Assert.Contains("South", south);
        Assert.DoesNotContain("North", south);
        Assert.DoesNotContain("West", south);
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private sealed record Seed(
        int ReportId, int FolderId, string NorthToken, string SouthToken, int NorthUserId, int NorthGroupId,
        string ScriptPath, string SouthGroup);

    /// <summary>
    /// One identity-filtered report in its own folder, and two Viewers, each in one region group
    /// that holds <paramref name="permission"/> on the folder.
    /// </summary>
    private async Task<Seed> SeedAsync(FolderPermission permission)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var folderId = await CreateFolderAsync($"Live {suffix}");
        var northName = $"north_{suffix}";
        var southName = $"south_{suffix}";
        await CreatePortalUserAsync(northName, "North@Test1!", "Viewer");
        await CreatePortalUserAsync(southName, "South@Test1!", "Viewer");

        int northUserId, northGroupId;
        string northGroup = $"region_north_{suffix}", southGroup = $"region_south_{suffix}";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var north = await db.Users.SingleAsync(u => u.UserName == northName);
            var south = await db.Users.SingleAsync(u => u.UserName == southName);
            var northRow = new Group { Name = northGroup };
            var southRow = new Group { Name = southGroup };
            db.Groups.AddRange(northRow, southRow);
            await db.SaveChangesAsync();
            db.UserGroups.AddRange(
                new UserGroup { UserId = north.Id, GroupId = northRow.Id },
                new UserGroup { UserId = south.Id, GroupId = southRow.Id });
            db.FolderAcls.AddRange(
                new FolderAcl { FolderId = folderId, GroupId = northRow.Id, Permission = permission },
                new FolderAcl { FolderId = folderId, GroupId = southRow.Id, Permission = permission });
            await db.SaveChangesAsync();
            northUserId = north.Id;
            northGroupId = northRow.Id;
        }

        var reportId = await CreateReportAsync(folderId, $"rls_live_{suffix}", Script(northGroup, southGroup));
        return new Seed(
            reportId, folderId,
            await LoginWithRequiredChangeAsync(northName, "North@Test1!", "North@Test2!"),
            await LoginWithRequiredChangeAsync(southName, "South@Test1!", "South@Test2!"),
            northUserId, northGroupId,
            Path.Combine(_factory.TempDir, "scripts", $"rls_live_{suffix}.rptsql"), southGroup);
    }

    /// <summary>The value the caller's last run of the report produced; a new run produces a new one.</summary>
    private async Task<string> TokenAsync(string token, int reportId)
    {
        var response = await AuthGet(token, $"/api/reports/{reportId}/manifest");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {body}");
        var stamp = JsonNode.Parse(body)!["visuals"]!.AsArray()
            .Single(visual => visual!["name"]!.GetValue<string>() == "Stamp")!;
        return stamp["rows"]!.AsArray().Single()![0]!.ToString();
    }

    /// <summary>A token for a new service account owned by this user, holding exactly these scopes.</summary>
    private async Task<string> ServiceTokenAsync(string[] scopes, int ownerId)
    {
        var admin = await GetAdminTokenAsync();
        var created = await AuthPost(admin, "/api/admin/service-accounts", new
        {
            name = $"live-{Guid.NewGuid().ToString("N")[..6]}",
            ownerUserId = ownerId,
            scopes,
            roles = new[] { "Viewer" },
            expiresAt = DateTime.UtcNow.AddHours(1)
        });
        var body = await created.Content.ReadFromJsonAsync<JsonObject>(_json);
        Assert.True(created.IsSuccessStatusCode, body?.ToJsonString());
        var exchange = await _client.PostAsJsonAsync("/api/auth/service-token", new
        {
            clientId = body!["account"]!["clientId"]!.GetValue<string>(),
            clientSecret = body["clientSecret"]!.GetValue<string>()
        });
        Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);
        return (await exchange.Content.ReadFromJsonAsync<JsonObject>(_json))!["accessToken"]!.GetValue<string>();
    }

    /// <summary>The SaleID values the caller's Detail table holds, sorted.</summary>
    private async Task<int[]> SaleIdsAsync(string token, int reportId)
    {
        var response = await AuthGet(token, $"/api/reports/{reportId}/manifest");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {body}");
        var detail = JsonNode.Parse(body)!["visuals"]!.AsArray()
            .Single(visual => visual!["name"]!.GetValue<string>() == "Detail")!;
        var column = detail["columns"]!.AsArray().Select(name => name!.GetValue<string>()).ToList()
            .FindIndex(name => name.Equals("SaleID", StringComparison.OrdinalIgnoreCase));
        return [.. detail["rows"]!.AsArray().Select(row => (int)Convert.ToDecimal(row![column]!.ToString())).Order()];
    }

    private async Task CreatePortalUserAsync(string username, string password, string role)
    {
        var res = await AuthPost(await GetAdminTokenAsync(), "/api/admin/users",
            new { username, email = $"{username}@test.local", password, role });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
    }

    // Admin-created users have MustChangePassword=true; the first-login token is restricted, so
    // change the password once and re-login for a full-authority token.
    private async Task<string> LoginWithRequiredChangeAsync(string username, string password, string newPassword)
    {
        var loginRes = await _client.PostAsJsonAsync("/api/auth/login", new { username, password });
        loginRes.EnsureSuccessStatusCode();
        var firstToken = (await loginRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["token"]!.GetValue<string>();

        using var cpReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password");
        cpReq.Headers.Authorization = new("Bearer", firstToken);
        cpReq.Content = JsonContent.Create(new { currentPassword = password, newPassword });
        (await _client.SendAsync(cpReq)).EnsureSuccessStatusCode();

        var reloginRes = await _client.PostAsJsonAsync("/api/auth/login", new { username, password = newPassword });
        reloginRes.EnsureSuccessStatusCode();
        return (await reloginRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["token"]!.GetValue<string>();
    }

    private async Task<int> CreateFolderAsync(string name)
    {
        var res = await AuthPost(await GetAdminTokenAsync(), "/api/folders", new { name, parentId = (int?)null });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<JsonObject>(_json))!["id"]!.GetValue<int>();
    }

    private async Task<int> CreateReportAsync(int folderId, string fileStem, string script)
    {
        var scriptPath = Path.Combine(_factory.TempDir, "scripts", $"{fileStem}.rptsql");
        await File.WriteAllTextAsync(scriptPath, script);
        var res = await AuthPost(await GetAdminTokenAsync(), "/api/reports",
            new { folderId, name = fileStem, description = "RLS live view test", scriptPath });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<JsonObject>(_json))!["id"]!.GetValue<int>();
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

    // This class has its own Portal, so the seeded administrator's forced change happens once here.
    private async Task<string> GetAdminTokenAsync()
    {
        if (_adminToken is not null) return _adminToken;
        var login = await _client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "Admin@12345!" });
        if (login.IsSuccessStatusCode)
        {
            var first = (await login.Content.ReadFromJsonAsync<JsonObject>(_json))!["token"]!.GetValue<string>();
            using var change = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password");
            change.Headers.Authorization = new("Bearer", first);
            change.Content = JsonContent.Create(new { currentPassword = "Admin@12345!", newPassword = "Admin@Tests99!" });
            (await _client.SendAsync(change)).EnsureSuccessStatusCode();
        }
        var relogin = await _client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "Admin@Tests99!" });
        relogin.EnsureSuccessStatusCode();
        return _adminToken = (await relogin.Content.ReadFromJsonAsync<JsonObject>(_json))!["token"]!.GetValue<string>();
    }
}
