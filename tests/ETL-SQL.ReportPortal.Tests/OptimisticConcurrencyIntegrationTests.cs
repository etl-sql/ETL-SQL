using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace ETL_SQL.ReportPortal.Tests;

[Trait("Category", "Portal")]
[Trait("CompatBreak", "0.12")]
public sealed class OptimisticConcurrencyIntegrationTests : IClassFixture<PortalWebFactory>
{
    private static readonly SemaphoreSlim loginLock = new(1, 1);
    private static string? adminToken;
    private readonly HttpClient client;

    public OptimisticConcurrencyIntegrationTests(PortalWebFactory factory) =>
        client = factory.CreateClient();

    [Fact]
    public async Task FolderUpdate_RequiresVersion_AndReturnsCurrentStateOnConflict()
    {
        var token = await LoginAsAdminAsync();
        var created = await SendAsync(
            HttpMethod.Post, "/api/folders", token, new { name = "Concurrency", parentId = (int?)null });
        var folder = await created.Content.ReadFromJsonAsync<JsonObject>();
        var id = folder!["id"]!.GetValue<int>();

        var missing = await SendAsync(
            HttpMethod.Put, $"/api/folders/{id}", token, new { name = "Missing" });
        Assert.Equal(HttpStatusCode.PreconditionRequired, missing.StatusCode);

        var updated = await SendAsync(
            HttpMethod.Put, $"/api/folders/{id}", token, new { name = "Current" }, version: 1);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal("\"2\"", updated.Headers.ETag?.Tag);

        var stale = await SendAsync(
            HttpMethod.Put, $"/api/folders/{id}", token, new { name = "Stale" }, version: 1);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var conflict = await stale.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal("Current", conflict!["current"]!["name"]!.GetValue<string>());
        Assert.Equal(2, conflict["current"]!["version"]!.GetValue<long>());
    }

    [Fact]
    public async Task BulkUserStatus_ReturnsPerItemConflictAndSuccess()
    {
        var token = await LoginAsAdminAsync();
        var firstId = await CreateUserAsync(token, "bulk_concurrency_a");
        var secondId = await CreateUserAsync(token, "bulk_concurrency_b");

        var advance = await SendAsync(
            HttpMethod.Put,
            $"/api/admin/users/{firstId}",
            token,
            new { email = "advanced@example.test" },
            version: 1);
        Assert.Equal(HttpStatusCode.OK, advance.StatusCode);

        var bulk = await SendAsync(
            HttpMethod.Post,
            "/api/admin/users/bulk-status",
            token,
            new
            {
                users = new[]
                {
                    new { id = firstId, version = 1 },
                    new { id = secondId, version = 1 }
                },
                isActive = false
            });
        Assert.Equal(HttpStatusCode.OK, bulk.StatusCode);

        var body = await bulk.Content.ReadFromJsonAsync<JsonObject>();
        var results = body!["results"]!.AsArray();
        Assert.Contains(results, item =>
            item!["id"]!.GetValue<int>() == firstId &&
            item["status"]!.GetValue<string>() == "Conflict" &&
            item["currentVersion"]!.GetValue<long>() == 2);
        Assert.Contains(results, item =>
            item!["id"]!.GetValue<int>() == secondId &&
            item["status"]!.GetValue<string>() == "Updated");
    }

    private async Task<int> CreateUserAsync(string token, string username)
    {
        var response = await SendAsync(
            HttpMethod.Post,
            "/api/admin/users",
            token,
            new
            {
                username,
                email = $"{username}@example.test",
                password = "Tests@12345!",
                role = "Viewer",
                firstName = "Concurrency",
                lastName = "Test"
            });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<int>();
    }

    private async Task<string> LoginAsAdminAsync()
    {
        await loginLock.WaitAsync();
        try
        {
            if (adminToken is not null)
                return adminToken;

            var login = await client.PostAsJsonAsync("/api/auth/login", new
            {
                username = "admin",
                password = "Admin@12345!"
            });
            login.EnsureSuccessStatusCode();
            var initial = await login.Content.ReadFromJsonAsync<JsonObject>();
            var token = initial!["token"]!.GetValue<string>();
            var changed = await SendAsync(
                HttpMethod.Post,
                "/api/auth/change-password",
                token,
                new { currentPassword = "Admin@12345!", newPassword = "Admin@Tests99!" });
            changed.EnsureSuccessStatusCode();

            login = await client.PostAsJsonAsync("/api/auth/login", new
            {
                username = "admin",
                password = "Admin@Tests99!"
            });
            initial = await login.Content.ReadFromJsonAsync<JsonObject>();
            adminToken = initial!["token"]!.GetValue<string>();
            return adminToken;
        }
        finally
        {
            loginLock.Release();
        }
    }

    private Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string url,
        string token,
        object? body = null,
        long? version = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new("Bearer", token);
        if (version.HasValue)
            request.Headers.TryAddWithoutValidation("If-Match", $"\"{version.Value}\"");
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return client.SendAsync(request);
    }
}
