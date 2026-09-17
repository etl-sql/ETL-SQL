using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Portal.Services;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Studio capabilities used to be resolvable only from <c>Portal:Studio:RoleCapabilities</c>
/// configuration, so changing who may publish or push meant editing a config file and restarting,
/// and could not be said for anything narrower than a whole role. These tests cover granting them to
/// a group and to a service account instead.
///
/// Every test here runs with the role mapping <b>empty</b>, so nothing is inherited from a role and
/// any capability observed can only have come from the grant under test.
/// </summary>
[Trait("Category", "Portal")]
public sealed class StudioCapabilityAssignmentTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Studio on, but no role grants anything — capabilities must be assigned explicitly.</summary>
    private sealed class NoRoleCapabilitiesFactory : PortalWebFactory
    {
        protected override void CustomizePortalConfig(PortalConfig config)
        {
            config.Studio.Mode = StudioDeploymentMode.SourceControlled;
            config.Studio.RoleCapabilities = [];
        }
    }

    [Fact]
    public async Task GroupGrant_EnablesAGatedRoute_AndRevokingItTakesTheRouteAway()
    {
        using var factory = new NoRoleCapabilitiesFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var user = await CreateUserAsync(client, adminToken, $"cap_grp_{suffix}", "Publisher");
        var groupId = await CreateGroupAsync(client, adminToken, $"cap_grp_group_{suffix}");
        Assert.Equal(HttpStatusCode.OK,
            (await AuthPost(client, adminToken, $"/api/admin/groups/{groupId}/members",
                new { userId = user })).StatusCode);

        // The designer needs StudioAccess (controller) and ScriptPreview (action). With no role
        // mapping the Publisher role alone grants neither.
        var token = await LoginAsync(client, $"cap_grp_{suffix}", "Ready@Test2!");
        Assert.Equal(HttpStatusCode.Forbidden, (await AnalyzeAsync(client, token)).StatusCode);

        var granted = await SetCapabilitiesAsync(client, adminToken, groupId,
            [StudioCapabilities.StudioAccess, StudioCapabilities.ScriptPreview]);
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);

        // Capabilities ride on the token, so the grant reaches the user at their next sign-in.
        token = await LoginAsync(client, $"cap_grp_{suffix}", "Ready@Test2!");
        Assert.Equal(HttpStatusCode.OK, (await AnalyzeAsync(client, token)).StatusCode);

        // Revoking must not leave the outstanding session holding authority just withdrawn.
        Assert.Equal(HttpStatusCode.OK,
            (await SetCapabilitiesAsync(client, adminToken, groupId, [])).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await AnalyzeAsync(client, token)).StatusCode);

        token = await LoginAsync(client, $"cap_grp_{suffix}", "Ready@Test2!");
        Assert.Equal(HttpStatusCode.Forbidden, (await AnalyzeAsync(client, token)).StatusCode);
    }

    [Fact]
    public async Task UnknownCapabilityName_IsRejectedRatherThanStoredAndIgnored()
    {
        using var factory = new NoRoleCapabilitiesFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var groupId = await CreateGroupAsync(client, adminToken, $"cap_bad_{Guid.NewGuid():N}"[..24]);

        // A typo that stored silently would read as a successful grant that does nothing at all.
        var response = await SetCapabilitiesAsync(client, adminToken, groupId, ["ScriptPreveiw"]);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.Contains("ScriptPreveiw", body!["unknown"]!.AsArray().Select(v => v!.GetValue<string>()));
        Assert.Contains(StudioCapabilities.ScriptPreview,
            body["allowed"]!.AsArray().Select(v => v!.GetValue<string>()));

        var listed = await AuthGet(client, adminToken, $"/api/admin/groups/{groupId}/studio-capabilities");
        var current = await listed.Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.Empty(current!["capabilities"]!.AsArray());
    }

    [Fact]
    public async Task ServiceAccountCapabilities_AreCappedByItsOwners()
    {
        using var factory = new NoRoleCapabilitiesFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var ownerId = await CreateUserAsync(client, adminToken, $"cap_owner_{suffix}", "Publisher");
        var groupId = await CreateGroupAsync(client, adminToken, $"cap_owner_group_{suffix}");
        Assert.Equal(HttpStatusCode.OK,
            (await AuthPost(client, adminToken, $"/api/admin/groups/{groupId}/members",
                new { userId = ownerId })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await SetCapabilitiesAsync(client, adminToken, groupId, [StudioCapabilities.ScriptPreview])).StatusCode);

        // The account asks for more than its owner holds. An account that could exceed its owner
        // would be a way to keep authority the owner had taken away from them.
        var create = await AuthPost(client, adminToken, "/api/admin/service-accounts", new
        {
            name = $"cap_account_{suffix}",
            ownerUserId = ownerId,
            scopes = new[] { ServiceAccountScopes.PortalRead },
            roles = new[] { "Publisher" },
            studioCapabilities = new[] { StudioCapabilities.ScriptPreview, StudioCapabilities.SourcePush }
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<JsonObject>(Json);
        var clientId = created!["account"]!["clientId"]!.GetValue<string>();
        var clientSecret = created["clientSecret"]!.GetValue<string>();

        var exchange = await client.PostAsJsonAsync("/api/auth/service-token", new { clientId, clientSecret });
        Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);
        var accessToken = (await exchange.Content.ReadFromJsonAsync<JsonObject>(Json))!["accessToken"]!.GetValue<string>();

        var capabilities = CapabilityClaims(accessToken);
        Assert.Contains(StudioCapabilities.ScriptPreview, capabilities);
        Assert.DoesNotContain(StudioCapabilities.SourcePush, capabilities);
    }

    [Fact]
    public async Task CustomRoleUser_WithStudioAccessAndScriptRead_CanAccessStudioReportsAndScriptContent()
    {
        using var factory = new NoRoleCapabilitiesFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        // Create a non-privileged user with role "Viewer" (not Admin and not Publisher)
        var userId = await CreateUserAsync(client, adminToken, $"learner_{suffix}", "Viewer");
        var groupId = await CreateGroupAsync(client, adminToken, $"learner_group_{suffix}");
        Assert.Equal(HttpStatusCode.OK,
            (await AuthPost(client, adminToken, $"/api/admin/groups/{groupId}/members", new { userId })).StatusCode);

        // Grant StudioAccess and ScriptRead capabilities to the group
        var grantRes = await SetCapabilitiesAsync(client, adminToken, groupId,
            [StudioCapabilities.StudioAccess, StudioCapabilities.ScriptRead]);
        Assert.Equal(HttpStatusCode.OK, grantRes.StatusCode);

        // Under NoRoleCapabilitiesFactory, admin has no capabilities from role mappings.
        // Grant ReportPublish to an admin group so admin can publish the seed report.
        var adminGroupId = await CreateGroupAsync(client, adminToken, $"admin_grp_{suffix}");
        Assert.Equal(HttpStatusCode.OK,
            (await SetCapabilitiesAsync(client, adminToken, adminGroupId,
                [StudioCapabilities.StudioAccess, StudioCapabilities.ReportPublish, StudioCapabilities.ScriptSave])).StatusCode);
        var usersRes = await AuthGet(client, adminToken, "/api/admin/users");
        var usersObj = await usersRes.Content.ReadFromJsonAsync<JsonObject>(Json);
        var adminUserId = usersObj!["items"]!.AsArray().First(u => u!["userName"]!.GetValue<string>() == "admin")!["id"]!.GetValue<int>();
        Assert.Equal(HttpStatusCode.OK,
            (await AuthPost(client, adminToken, $"/api/admin/groups/{adminGroupId}/members", new { userId = adminUserId })).StatusCode);
        adminToken = await LoginAsync(client, "admin", "Admin@Tests99!");

        // Publish a report as Admin and grant the learner Author permission on the folder
        var folderRes = await AuthPost(client, adminToken, "/api/folders", new { name = $"learn_folder_{suffix}" });
        Assert.Equal(HttpStatusCode.Created, folderRes.StatusCode);
        var folderId = (await folderRes.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();

        var scriptName = $"learn_{suffix}.rptsql";
        var scriptPath = Path.Combine(factory.TempDir, "scripts", scriptName);
        await File.WriteAllTextAsync(scriptPath, "SET REPORT TITLE = 'Learn';");

        var reportRes = await AuthPost(client, adminToken, "/api/reports",
            new { folderId, name = $"Learn Report {suffix}", scriptPath = scriptName });
        Assert.Equal(HttpStatusCode.Created, reportRes.StatusCode);
        var reportId = (await reportRes.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();

        // Grant Author permission (2) on the folder to the learner's group
        var aclRes = await AuthPost(client, adminToken, $"/api/folders/{folderId}/acl", new { groupId, permission = 2 });
        Assert.Equal(HttpStatusCode.OK, aclRes.StatusCode);

        // Login as the learner user
        var userToken = await LoginAsync(client, $"learner_{suffix}", "Ready@Test2!");

        // 1. Can access GET /api/studio/reports
        var studioReportsRes = await AuthGet(client, userToken, "/api/studio/reports");
        Assert.Equal(HttpStatusCode.OK, studioReportsRes.StatusCode);
        var reports = await studioReportsRes.Content.ReadFromJsonAsync<JsonArray>(Json);
        Assert.NotNull(reports);
        Assert.Contains(reports!, r => r!["id"]!.GetValue<int>() == reportId);

        // 2. Can access GET /api/reports/{id}/script-content
        var scriptContentRes = await AuthGet(client, userToken, $"/api/reports/{reportId}/script-content");
        Assert.Equal(HttpStatusCode.OK, scriptContentRes.StatusCode);
        var scriptContent = await scriptContentRes.Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.NotNull(scriptContent);
        Assert.Equal("SET REPORT TITLE = 'Learn';", scriptContent!["scriptText"]!.GetValue<string>());
    }

    [Fact]
    public async Task Learner_WithStudioAccessAndScriptPreview_CanRunPreviewWithoutAdminOrPublisherRole()
    {
        using var factory = new NoRoleCapabilitiesFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        // Create learner user with non-privileged role
        var userId = await CreateUserAsync(client, adminToken, $"preview_user_{suffix}", "Viewer");
        var groupId = await CreateGroupAsync(client, adminToken, $"preview_group_{suffix}");
        Assert.Equal(HttpStatusCode.OK,
            (await AuthPost(client, adminToken, $"/api/admin/groups/{groupId}/members", new { userId })).StatusCode);

        // Grant StudioAccess and ScriptPreview
        Assert.Equal(HttpStatusCode.OK,
            (await SetCapabilitiesAsync(client, adminToken, groupId,
                [StudioCapabilities.StudioAccess, StudioCapabilities.ScriptPreview])).StatusCode);

        var token = await LoginAsync(client, $"preview_user_{suffix}", "Ready@Test2!");

        // Call /api/designer/preview
        var previewRes = await AuthPost(client, token, "/api/designer/preview", new
        {
            script = "SET REPORT TITLE = 'Preview Test';",
            runEveryPage = true
        });
        Assert.Equal(HttpStatusCode.OK, previewRes.StatusCode);
        var previewBody = await previewRes.Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.NotNull(previewBody);
    }

    [Fact]
    public async Task User_WithoutReportPublish_IsRejectedByCreateReportEndpoint()
    {
        using var factory = new NoRoleCapabilitiesFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        // Create user with non-privileged role
        var userId = await CreateUserAsync(client, adminToken, $"draft_only_{suffix}", "Viewer");
        var groupId = await CreateGroupAsync(client, adminToken, $"draft_only_group_{suffix}");
        Assert.Equal(HttpStatusCode.OK,
            (await AuthPost(client, adminToken, $"/api/admin/groups/{groupId}/members", new { userId })).StatusCode);

        // Grant StudioAccess and ScriptSave BUT NOT ReportPublish
        Assert.Equal(HttpStatusCode.OK,
            (await SetCapabilitiesAsync(client, adminToken, groupId,
                [StudioCapabilities.StudioAccess, StudioCapabilities.ScriptSave])).StatusCode);

        // Create a folder and grant Manage permission (permission = 3) to the user's group
        var folderRes = await AuthPost(client, adminToken, "/api/folders", new { name = $"draft_folder_{suffix}" });
        Assert.Equal(HttpStatusCode.Created, folderRes.StatusCode);
        var folderId = (await folderRes.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();

        var aclRes = await AuthPost(client, adminToken, $"/api/folders/{folderId}/acl", new { groupId, permission = 3 });
        Assert.Equal(HttpStatusCode.OK, aclRes.StatusCode);

        var token = await LoginAsync(client, $"draft_only_{suffix}", "Ready@Test2!");

        // Attempt to create/publish a catalog report via POST /api/studio/reports
        var createRes = await AuthPost(client, token, "/api/studio/reports", new
        {
            folderId,
            name = $"Draft Report {suffix}",
            scriptText = "SET REPORT TITLE = 'Blocked';"
        });

        // Must be rejected with 403 Forbidden because user lacks ReportPublish capability
        Assert.Equal(HttpStatusCode.Forbidden, createRes.StatusCode);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Reads the <c>studio_capability</c> claims straight out of the issued JWT.</summary>
    private static IReadOnlyList<string> CapabilityClaims(string jwt)
    {
        var payload = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
        var claims = JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)))!.AsObject();
        if (!claims.TryGetPropertyValue(StudioAuthorizationService.CapabilityClaim, out var value) || value is null)
            return [];
        return value is JsonArray array
            ? [.. array.Select(item => item!.GetValue<string>())]
            : [value.GetValue<string>()];
    }

    private static Task<HttpResponseMessage> AnalyzeAsync(HttpClient client, string token) =>
        AuthPost(client, token, "/api/designer/analyze", new { script = "SELECT 1 INTO #a;" });

    private static Task<HttpResponseMessage> SetCapabilitiesAsync(
        HttpClient client, string adminToken, int groupId, string[] capabilities) =>
        SendAsync(client, HttpMethod.Put, adminToken,
            $"/api/admin/groups/{groupId}/studio-capabilities", new { capabilities });

    private static async Task<int> CreateGroupAsync(HttpClient client, string adminToken, string name)
    {
        var response = await AuthPost(client, adminToken, "/api/admin/groups", new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();
    }

    private static async Task<int> CreateUserAsync(
        HttpClient client, string adminToken, string username, string role)
    {
        var create = await AuthPost(client, adminToken, "/api/admin/users", new
        {
            username,
            email = $"{username}@test.local",
            password = "Initial@Test1!",
            role
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var userId = (await create.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();
        var initial = await LoginAsync(client, username, "Initial@Test1!");
        Assert.Equal(HttpStatusCode.NoContent,
            (await AuthPost(client, initial, "/api/auth/change-password",
                new { currentPassword = "Initial@Test1!", newPassword = "Ready@Test2!" })).StatusCode);
        return userId;
    }

    private static async Task<string> GetAdminTokenAsync(HttpClient client)
    {
        var initial = await LoginAsync(client, "admin", "Admin@12345!");
        Assert.Equal(HttpStatusCode.NoContent,
            (await AuthPost(client, initial, "/api/auth/change-password",
                new { currentPassword = "Admin@12345!", newPassword = "Admin@Tests99!" })).StatusCode);
        return await LoginAsync(client, "admin", "Admin@Tests99!");
    }

    private static async Task<string> LoginAsync(HttpClient client, string username, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>(Json))!["token"]!.GetValue<string>();
    }

    private static Task<HttpResponseMessage> AuthGet(HttpClient client, string token, string url) =>
        SendAsync(client, HttpMethod.Get, token, url, null);

    private static Task<HttpResponseMessage> AuthPost(HttpClient client, string token, string url, object body) =>
        SendAsync(client, HttpMethod.Post, token, url, body);

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string token, string url, object? body)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        await IfMatchVersioning.StampAsync(client, request, token);
        return await client.SendAsync(request);
    }
}
