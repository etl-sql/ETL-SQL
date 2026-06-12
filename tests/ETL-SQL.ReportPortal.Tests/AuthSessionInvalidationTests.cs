using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.ReportPortal.Tests;

[Trait("Category", "Portal")]
[Trait("CompatBreak", "0.11")]
public class AuthSessionInvalidationTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task RoleDemotion_InvalidatesIssuedAccessAndRefreshTokens()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var (userId, _, accessToken, refreshToken) = await CreateReadyUserAsync(
            client, adminToken, role: "Admin");

        Assert.Equal(HttpStatusCode.OK, (await AuthGet(client, accessToken, "/api/admin/users")).StatusCode);

        var demote = await AuthPut(
            client, adminToken, $"/api/admin/users/{userId}", new { role = "Viewer" });
        Assert.Equal(HttpStatusCode.OK, demote.StatusCode);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await AuthGet(client, accessToken, "/api/admin/users")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken })).StatusCode);
    }

    [Fact]
    public async Task PasswordChange_InvalidatesPreviousAccessAndRefreshTokens()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var (_, _, accessToken, refreshToken) = await CreateReadyUserAsync(
            client, adminToken, role: "Viewer");

        var change = await AuthPost(client, accessToken, "/api/auth/change-password", new
        {
            currentPassword = "Ready@Test2!",
            newPassword = "Changed@Test3!"
        });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await AuthGet(client, accessToken, "/api/folders")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken })).StatusCode);
    }

    [Fact]
    public async Task GroupAndAclChanges_InvalidateExistingSession()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var (userId, username, accessToken, _) = await CreateReadyUserAsync(
            client, adminToken, role: "Viewer");

        var groupResponse = await AuthPost(client, adminToken, "/api/admin/groups", new
        {
            name = $"Session Group {Guid.NewGuid():N}"[..24]
        });
        Assert.Equal(HttpStatusCode.Created, groupResponse.StatusCode);
        var group = await groupResponse.Content.ReadFromJsonAsync<JsonObject>(Json);
        var groupId = group!["id"]!.GetValue<int>();

        var addMember = await AuthPost(
            client, adminToken, $"/api/admin/groups/{groupId}/members", new { userId });
        Assert.Equal(HttpStatusCode.OK, addMember.StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await AuthGet(client, accessToken, "/api/folders")).StatusCode);

        var relogin = await LoginAsync(client, username, "Ready@Test2!");
        var folderResponse = await AuthPost(client, adminToken, "/api/folders", new
        {
            name = $"SessionFolder{Guid.NewGuid():N}"[..24],
            parentId = (int?)null
        });
        Assert.Equal(HttpStatusCode.Created, folderResponse.StatusCode);
        var folder = await folderResponse.Content.ReadFromJsonAsync<JsonObject>(Json);
        var folderId = folder!["id"]!.GetValue<int>();

        var grant = await AuthPost(client, adminToken, $"/api/folders/{folderId}/acl", new
        {
            groupId,
            permission = FolderPermission.Read
        });
        Assert.Equal(HttpStatusCode.OK, grant.StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await AuthGet(client, relogin.AccessToken, "/api/folders")).StatusCode);
    }

    [Fact]
    public async Task RefreshTokens_AreHashedAtRestAndRotateOnUse()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var (userId, _, _, refreshToken) = await CreateReadyUserAsync(
            client, adminToken, role: "Viewer");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var stored = await db.RefreshTokens
                .Where(token => token.UserId == userId && token.RevokedAt == null)
                .Select(token => token.Token)
                .SingleAsync();
            Assert.NotEqual(refreshToken, stored);
            Assert.Equal(TokenService.HashRefreshToken(refreshToken), stored);
        }

        var refreshResponse = await client.PostAsJsonAsync(
            "/api/auth/refresh", new { refreshToken });
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        var refreshed = await refreshResponse.Content.ReadFromJsonAsync<JsonObject>(Json);
        var rotatedRefresh = refreshed!["refreshToken"]!.GetValue<string>();

        // Rotation works: the successor refreshes successfully and rotates again.
        var secondResponse = await client.PostAsJsonAsync(
            "/api/auth/refresh", new { refreshToken = rotatedRefresh });
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var second = await secondResponse.Content.ReadFromJsonAsync<JsonObject>(Json);
        var secondRotated = second!["refreshToken"]!.GetValue<string>();

        // Replaying the original (already-rotated) token is a theft signal: it is rejected
        // AND the whole token family is revoked, so the latest successor stops working too.
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken })).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = secondRotated })).StatusCode);
    }

    [Fact]
    public async Task Logout_InvalidatesAllIssuedTokensForCurrentUser()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var (_, _, accessToken, refreshToken) = await CreateReadyUserAsync(
            client, adminToken, role: "Viewer");

        var logout = await AuthPost(
            client, accessToken, "/api/auth/logout", new { refreshToken });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await AuthGet(client, accessToken, "/api/folders")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken })).StatusCode);
    }

    private static async Task<(int UserId, string Username, string AccessToken, string RefreshToken)> CreateReadyUserAsync(
        HttpClient client,
        string adminToken,
        string role)
    {
        var username = $"session_{Guid.NewGuid():N}"[..20];
        var create = await AuthPost(client, adminToken, "/api/admin/users", new
        {
            username,
            email = $"{username}@test.local",
            password = "Initial@Test1!",
            role
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<JsonObject>(Json);
        var userId = created!["id"]!.GetValue<int>();

        var initial = await LoginAsync(client, username, "Initial@Test1!");
        var change = await AuthPost(client, initial.AccessToken, "/api/auth/change-password", new
        {
            currentPassword = "Initial@Test1!",
            newPassword = "Ready@Test2!"
        });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);

        var ready = await LoginAsync(client, username, "Ready@Test2!");
        return (userId, username, ready.AccessToken, ready.RefreshToken);
    }

    private static async Task<string> GetAdminTokenAsync(HttpClient client)
    {
        var initial = await LoginAsync(client, "admin", "Admin@12345!");
        var change = await AuthPost(client, initial.AccessToken, "/api/auth/change-password", new
        {
            currentPassword = "Admin@12345!",
            newPassword = "Admin@Tests99!"
        });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
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
        return (
            body!["token"]!.GetValue<string>(),
            body["refreshToken"]!.GetValue<string>());
    }

    private static Task<HttpResponseMessage> AuthGet(HttpClient client, string token, string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new("Bearer", token);
        return client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> AuthPost(
        HttpClient client,
        string token,
        string url,
        object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new("Bearer", token);
        request.Content = JsonContent.Create(body);
        await IfMatchVersioning.StampAsync(client, request, token);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> AuthPut(
        HttpClient client,
        string token,
        string url,
        object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, url);
        request.Headers.Authorization = new("Bearer", token);
        request.Content = JsonContent.Create(body);
        await IfMatchVersioning.StampAsync(client, request, token);
        return await client.SendAsync(request);
    }
}
