using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.ReportPortal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.ReportPortal.Tests;

[Trait("Category", "Portal")]
[Trait("CompatBreak", "0.11")]
public class AnonymousReportAccessTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task AnonymousCapabilities_DefaultExpiryEntropyAuditInventoryAndPermissionRevocation()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var user = await CreateReadyUserAsync(client, adminToken, $"cap_{suffix}", "Publisher");
        var groupId = await CreateGroupAsync(client, adminToken, $"cap_group_{suffix}");
        // Versioned membership/ACL/user mutations return 200 with the resource's bumped version.
        Assert.Equal(
            HttpStatusCode.OK,
            (await AuthPost(client, adminToken, $"/api/admin/groups/{groupId}/members",
                new { userId = user.UserId })).StatusCode);

        var folderResponse = await AuthPost(client, adminToken, "/api/folders", new
        {
            name = $"cap_folder_{suffix}",
            parentId = (int?)null
        });
        Assert.Equal(HttpStatusCode.Created, folderResponse.StatusCode);
        var folderId = (await folderResponse.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();
        Assert.Equal(
            HttpStatusCode.OK,
            (await AuthPost(client, adminToken, $"/api/folders/{folderId}/acl",
                new { groupId, permission = FolderPermission.Manage })).StatusCode);

        var creatorToken = (await LoginAsync(client, user.Username, "Ready@Test2!")).AccessToken;
        var scriptPath = Path.Combine(factory.TempDir, "scripts", $"anonymous-{suffix}.rptsql");
        await File.WriteAllTextAsync(scriptPath, "SET REPORT TITLE = 'Anonymous Access';");
        var reportResponse = await AuthPost(client, creatorToken, "/api/reports", new
        {
            folderId,
            name = $"Anonymous Report {suffix}",
            scriptPath
        });
        Assert.Equal(HttpStatusCode.Created, reportResponse.StatusCode);
        var reportId = (await reportResponse.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();

        var shareResponse = await AuthPost(
            client, creatorToken, $"/api/reports/{reportId}/share-links", new { });
        Assert.Equal(HttpStatusCode.Created, shareResponse.StatusCode);
        var share = await shareResponse.Content.ReadFromJsonAsync<JsonObject>(Json);

        var embedResponse = await AuthPost(
            client, creatorToken, $"/api/reports/{reportId}/embed-tokens",
            new { name = "Operations wallboard" });
        Assert.Equal(HttpStatusCode.Created, embedResponse.StatusCode);
        var embed = await embedResponse.Content.ReadFromJsonAsync<JsonObject>(Json);

        var shareToken = share!["token"]!.GetValue<string>();
        var embedToken = embed!["token"]!.GetValue<string>();
        Assert.True(Base64UrlDecodedLength(shareToken) >= 32);
        Assert.True(Base64UrlDecodedLength(embedToken) >= 32);

        var shareExpiry = share["expiresAt"]!.GetValue<DateTime>();
        var embedExpiry = embed["expiresAt"]!.GetValue<DateTime>();
        Assert.InRange(shareExpiry, DateTime.UtcNow.AddDays(6.9), DateTime.UtcNow.AddDays(7.1));
        Assert.InRange(embedExpiry, DateTime.UtcNow.AddDays(6.9), DateTime.UtcNow.AddDays(7.1));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/share/{shareToken}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/embed/{embedToken}")).StatusCode);

        var inventoryResponse = await AuthGet(client, adminToken, "/api/admin/anonymous-report-access");
        Assert.Equal(HttpStatusCode.OK, inventoryResponse.StatusCode);
        var inventory = await inventoryResponse.Content.ReadFromJsonAsync<JsonArray>(Json);
        Assert.Contains(inventory!, item =>
            item!["type"]!.GetValue<string>() == "ShareLink"
            && item["reportId"]!.GetValue<int>() == reportId
            && item["status"]!.GetValue<string>() == "Active");
        Assert.Contains(inventory!, item =>
            item!["type"]!.GetValue<string>() == "EmbedToken"
            && item["reportId"]!.GetValue<int>() == reportId
            && item["status"]!.GetValue<string>() == "Active");

        Assert.Equal(
            HttpStatusCode.OK,
            (await AuthPost(client, adminToken, $"/api/admin/groups/{groupId}/members/bulk-remove",
                new { userIds = new[] { user.UserId } })).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/share/{shareToken}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/embed/{embedToken}")).StatusCode);

        inventoryResponse = await AuthGet(client, adminToken, "/api/admin/anonymous-report-access");
        inventory = await inventoryResponse.Content.ReadFromJsonAsync<JsonArray>(Json);
        Assert.Contains(inventory!, item =>
            item!["type"]!.GetValue<string>() == "ShareLink"
            && item["reportId"]!.GetValue<int>() == reportId
            && item["status"]!.GetValue<string>() == "PermissionLost");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var audits = await db.AuditLogs
            .Where(entry => entry.ResourceId == reportId.ToString()
                && (entry.Action == "ANONYMOUS_SHARE_LINK_VIEW"
                    || entry.Action == "ANONYMOUS_EMBED_TOKEN_VIEW"))
            .ToListAsync();
        Assert.Equal(2, audits.Count);
        Assert.All(audits, entry =>
        {
            Assert.Null(entry.UserId);
            Assert.DoesNotContain(shareToken, entry.Detail ?? "", StringComparison.Ordinal);
            Assert.DoesNotContain(embedToken, entry.Detail ?? "", StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task CreatorDemotion_ExplicitlyRevokesOutstandingCapabilities()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = await CreateReadyUserAsync(client, adminToken, $"admincap_{suffix}", "Admin");

        var folderResponse = await AuthPost(client, adminToken, "/api/folders", new
        {
            name = $"admin_cap_folder_{suffix}",
            parentId = (int?)null
        });
        var folderId = (await folderResponse.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();
        var scriptPath = Path.Combine(factory.TempDir, "scripts", $"admin-cap-{suffix}.rptsql");
        await File.WriteAllTextAsync(scriptPath, "SET REPORT TITLE = 'Admin Capability';");
        var reportResponse = await AuthPost(client, user.AccessToken, "/api/reports", new
        {
            folderId,
            name = $"Admin Capability {suffix}",
            scriptPath
        });
        var reportId = (await reportResponse.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();
        var shareResponse = await AuthPost(
            client, user.AccessToken, $"/api/reports/{reportId}/share-links", new { });
        var share = await shareResponse.Content.ReadFromJsonAsync<JsonObject>(Json);
        var shareToken = share!["token"]!.GetValue<string>();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/share/{shareToken}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await AuthPut(client, adminToken, $"/api/admin/users/{user.UserId}",
                new { role = "Viewer" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/share/{shareToken}")).StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        Assert.NotNull((await db.ReportShareLinks.SingleAsync(link => link.Token == shareToken)).RevokedAt);
    }

    private static int Base64UrlDecodedLength(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');
        return Convert.FromBase64String(padded).Length;
    }

    private static async Task<int> CreateGroupAsync(HttpClient client, string adminToken, string name)
    {
        var response = await AuthPost(client, adminToken, "/api/admin/groups", new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();
    }

    private static async Task<(int UserId, string Username, string AccessToken)> CreateReadyUserAsync(
        HttpClient client,
        string adminToken,
        string username,
        string role = "Viewer")
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
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await AuthPost(client, initial.AccessToken, "/api/auth/change-password",
                new { currentPassword = "Initial@Test1!", newPassword = "Ready@Test2!" })).StatusCode);
        var ready = await LoginAsync(client, username, "Ready@Test2!");
        return (userId, username, ready.AccessToken);
    }

    private static async Task<string> GetAdminTokenAsync(HttpClient client)
    {
        var initial = await LoginAsync(client, "admin", "Admin@12345!");
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await AuthPost(client, initial.AccessToken, "/api/auth/change-password",
                new { currentPassword = "Admin@12345!", newPassword = "Admin@Tests99!" })).StatusCode);
        return (await LoginAsync(client, "admin", "Admin@Tests99!")).AccessToken;
    }

    private static async Task<(string AccessToken, string RefreshToken)> LoginAsync(
        HttpClient client,
        string username,
        string password)
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

    private static Task<HttpResponseMessage> AuthPut(HttpClient client, string token, string url, object body) =>
        SendAsync(client, HttpMethod.Put, token, url, body);

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string token,
        string url,
        object? body)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        await IfMatchVersioning.StampAsync(client, request, token);
        return await client.SendAsync(request);
    }
}
