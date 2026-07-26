using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Pins the rule that report authorship <em>upgrades</em> an existing grant rather than acting as
/// standing permission. v0.17.0 briefly short-circuited on <c>CreatedBy == userId</c> in four
/// places, which meant removing a user from every group revoked nothing they had authored. Each
/// assertion here corresponds to one of those surfaces.
/// </summary>
[Trait("Category", "Portal")]
public class ReportAuthorshipRevocationTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // FolderPermission { Read = 0, Execute = 1, Manage = 2 }
    private const int Read = 0;
    private const int Manage = 2;

    [Fact]
    public async Task RemovingAuthorFromGroup_RevokesAccess_Visibility_AndApprovalAuthority()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        // An author whose only access to the folder is a group ACL.
        var author = await CreateReadyUserAsync(client, adminToken, $"author_{suffix}", "Publisher");
        var requester = await CreateReadyUserAsync(client, adminToken, $"req_{suffix}", "Viewer");
        var groupId = await CreateGroupAsync(client, adminToken, $"author_group_{suffix}");
        Assert.Equal(HttpStatusCode.OK,
            (await AuthPost(client, adminToken, $"/api/admin/groups/{groupId}/members",
                new { userId = author.UserId })).StatusCode);

        var folderResponse = await AuthPost(client, adminToken, "/api/folders",
            new { name = $"author_folder_{suffix}", parentId = (int?)null });
        Assert.Equal(HttpStatusCode.Created, folderResponse.StatusCode);
        var folderId = (await folderResponse.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();
        Assert.Equal(HttpStatusCode.OK,
            (await AuthPost(client, adminToken, $"/api/folders/{folderId}/acl",
                new { groupId, permission = Manage })).StatusCode);

        var authorToken = (await LoginAsync(client, author.Username, "Ready@Test2!")).AccessToken;
        var reportName = $"Authored Report {suffix}";
        var scriptPath = Path.Combine(factory.TempDir, "scripts", $"authored-{suffix}.rptsql");
        Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
        await File.WriteAllTextAsync(scriptPath, "SET REPORT TITLE = 'Authored';");
        var reportResponse = await AuthPost(client, authorToken, "/api/reports",
            new { folderId, name = reportName, scriptPath });
        Assert.Equal(HttpStatusCode.Created, reportResponse.StatusCode);
        var reportId = (await reportResponse.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();

        // Someone else asks for access, so there is a pending decision to make.
        var requesterToken = (await LoginAsync(client, requester.Username, "Ready@Test2!")).AccessToken;
        Assert.Equal(HttpStatusCode.OK,
            (await AuthPost(client, requesterToken, $"/api/reports/{reportId}/request-access",
                new { reason = "Need this for reporting" })).StatusCode);
        var requestId = await PendingRequestIdAsync(client, authorToken, reportId);

        // Baseline: while the group ACL stands, the author has access, visibility, and authority.
        Assert.Equal(HttpStatusCode.OK, (await AuthGet(client, authorToken, $"/api/reports/{reportId}")).StatusCode);
        Assert.True(await CatalogContainsAsync(client, authorToken, reportName, reportId),
            "The author should see their report in catalog search while they still hold folder access.");

        // Revoke the only grant the author had.
        Assert.Equal(HttpStatusCode.OK,
            (await AuthPost(client, adminToken, $"/api/admin/groups/{groupId}/members/bulk-remove",
                new { userIds = new[] { author.UserId } })).StatusCode);

        // A membership change invalidates outstanding tokens, so the author must re-authenticate.
        // The interesting assertion is what the *fresh* session is allowed to do.
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await AuthGet(client, authorToken, $"/api/reports/{reportId}")).StatusCode);
        authorToken = (await LoginAsync(client, author.Username, "Ready@Test2!")).AccessToken;

        // 1. Interactive access — FolderPermissionService.GetEffectiveReportPermissionAsync.
        var afterAccess = (await AuthGet(client, authorToken, $"/api/reports/{reportId}")).StatusCode;
        Assert.True(afterAccess is HttpStatusCode.Forbidden or HttpStatusCode.NotFound,
            $"Authorship must not survive losing every grant; got {afterAccess}.");

        // 2. Catalog visibility — CatalogController.VisibleReportsQuery.
        Assert.False(await CatalogContainsAsync(client, authorToken, reportName, reportId),
            "A report the author can no longer open must not remain visible in catalog search.");

        // 3. Approval authority — deciding a request grants access to someone else.
        var approve = await AuthPost(client, authorToken,
            $"/api/reports/access-requests/{requestId}/approve", new { permission = Read });
        Assert.Equal(HttpStatusCode.Forbidden, approve.StatusCode);

        var deny = await AuthPost(client, authorToken,
            $"/api/reports/access-requests/{requestId}/deny", new { decisionReason = "no" });
        Assert.Equal(HttpStatusCode.Forbidden, deny.StatusCode);

        // 4. An admin retains authority, so the request is still actionable by someone.
        Assert.Equal(HttpStatusCode.OK,
            (await AuthPost(client, adminToken,
                $"/api/reports/access-requests/{requestId}/deny", new { decisionReason = "handled" })).StatusCode);
    }

    [Fact]
    public async Task AuthorRetainingAReadGrant_IsUpgradedToManage()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        // The other half of the rule: authorship still *upgrades* a surviving grant. The author
        // keeps only a Read-level group after revocation, and authorship must lift that to Manage —
        // otherwise the fix would strip authors of control over their own reports.
        var author = await CreateReadyUserAsync(client, adminToken, $"keep_{suffix}", "Publisher");
        var requester = await CreateReadyUserAsync(client, adminToken, $"keepreq_{suffix}", "Viewer");
        var manageGroupId = await CreateGroupAsync(client, adminToken, $"keep_manage_{suffix}");
        var readGroupId = await CreateGroupAsync(client, adminToken, $"keep_read_{suffix}");
        foreach (var gid in new[] { manageGroupId, readGroupId })
        {
            Assert.Equal(HttpStatusCode.OK,
                (await AuthPost(client, adminToken, $"/api/admin/groups/{gid}/members",
                    new { userId = author.UserId })).StatusCode);
        }

        var folderResponse = await AuthPost(client, adminToken, "/api/folders",
            new { name = $"keep_folder_{suffix}", parentId = (int?)null });
        var folderId = (await folderResponse.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();
        Assert.Equal(HttpStatusCode.OK,
            (await AuthPost(client, adminToken, $"/api/folders/{folderId}/acl",
                new { groupId = manageGroupId, permission = Manage })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await AuthPost(client, adminToken, $"/api/folders/{folderId}/acl",
                new { groupId = readGroupId, permission = Read })).StatusCode);

        var authorToken = (await LoginAsync(client, author.Username, "Ready@Test2!")).AccessToken;
        var scriptPath = Path.Combine(factory.TempDir, "scripts", $"keep-{suffix}.rptsql");
        Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
        await File.WriteAllTextAsync(scriptPath, "SET REPORT TITLE = 'Keep';");
        var reportResponse = await AuthPost(client, authorToken, "/api/reports",
            new { folderId, name = $"Kept Report {suffix}", scriptPath });
        Assert.Equal(HttpStatusCode.Created, reportResponse.StatusCode);
        var reportId = (await reportResponse.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();

        var requesterToken = (await LoginAsync(client, requester.Username, "Ready@Test2!")).AccessToken;
        Assert.Equal(HttpStatusCode.OK,
            (await AuthPost(client, requesterToken, $"/api/reports/{reportId}/request-access",
                new { reason = "Please" })).StatusCode);
        var requestId = await PendingRequestIdAsync(client, authorToken, reportId);

        // Drop the Manage group; only the Read group remains.
        Assert.Equal(HttpStatusCode.OK,
            (await AuthPost(client, adminToken, $"/api/admin/groups/{manageGroupId}/members/bulk-remove",
                new { userIds = new[] { author.UserId } })).StatusCode);
        authorToken = (await LoginAsync(client, author.Username, "Ready@Test2!")).AccessToken;

        Assert.Equal(HttpStatusCode.OK, (await AuthGet(client, authorToken, $"/api/reports/{reportId}")).StatusCode);

        // Approval needs Manage. A plain Read grant would not be enough, so success here is
        // authorship doing its remaining job: upgrading a grant the user still holds.
        Assert.Equal(HttpStatusCode.OK,
            (await AuthPost(client, authorToken,
                $"/api/reports/access-requests/{requestId}/approve", new { permission = Read })).StatusCode);
    }

    private static async Task<bool> CatalogContainsAsync(
        HttpClient client, string token, string reportName, int reportId)
    {
        var response = await AuthGet(client, token, $"/api/catalog/search?q={Uri.EscapeDataString(reportName)}");
        if (response.StatusCode != HttpStatusCode.OK) return false;
        var body = await response.Content.ReadAsStringAsync();
        var node = JsonNode.Parse(body);
        var items = node as JsonArray ?? node?["results"] as JsonArray ?? node?["items"] as JsonArray;
        if (items is null) return body.Contains($"\"id\":{reportId}", StringComparison.Ordinal);
        return items.Any(item => item?["id"]?.GetValue<int>() == reportId);
    }

    private static async Task<int> PendingRequestIdAsync(HttpClient client, string token, int reportId)
    {
        var response = await AuthGet(client, token, "/api/reports/access-requests/pending");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await response.Content.ReadFromJsonAsync<JsonArray>(Json);
        var match = items!.FirstOrDefault(item => item?["reportId"]?.GetValue<int>() == reportId);
        Assert.NotNull(match);
        return match!["id"]!.GetValue<int>();
    }

    private static async Task<int> CreateGroupAsync(HttpClient client, string adminToken, string name)
    {
        var response = await AuthPost(client, adminToken, "/api/admin/groups", new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();
    }

    private static async Task<(int UserId, string Username, string AccessToken)> CreateReadyUserAsync(
        HttpClient client, string adminToken, string username, string role = "Viewer")
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
            (await AuthPost(client, initial.AccessToken, "/api/auth/change-password",
                new { currentPassword = "Initial@Test1!", newPassword = "Ready@Test2!" })).StatusCode);
        var ready = await LoginAsync(client, username, "Ready@Test2!");
        return (userId, username, ready.AccessToken);
    }

    private static async Task<string> GetAdminTokenAsync(HttpClient client)
    {
        var initial = await LoginAsync(client, "admin", "Admin@12345!");
        Assert.Equal(HttpStatusCode.NoContent,
            (await AuthPost(client, initial.AccessToken, "/api/auth/change-password",
                new { currentPassword = "Admin@12345!", newPassword = "Admin@Tests99!" })).StatusCode);
        return (await LoginAsync(client, "admin", "Admin@Tests99!")).AccessToken;
    }

    private static async Task<(string AccessToken, string RefreshToken)> LoginAsync(
        HttpClient client, string username, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(Json);
        return (body!["token"]!.GetValue<string>(), body["refreshToken"]!.GetValue<string>());
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
        // Membership/ACL/user mutations are version-guarded and answer 428 without a precondition.
        await IfMatchVersioning.StampAsync(client, request, token);
        return await client.SendAsync(request);
    }
}
