using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ETL_SQL.ReportPortal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.ReportPortal.Tests;

/// <summary>
/// P1.6 audit contract: security-sensitive mutations commit their audit row in the same unit of
/// work (no successful mutation without a durable, correlated audit event), and a rejected
/// mutation leaves no audit row behind.
/// </summary>
[Trait("Category", "Portal")]
public sealed class AuditContractTests
{
    [Fact]
    public async Task SecuritySensitiveMutation_AuditsAtomically_WithCorrelationId()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var folderResponse = await SendAsync(client, HttpMethod.Post, "/api/folders", adminToken,
            new { name = $"audit_{suffix}", parentId = (int?)null });
        Assert.Equal(HttpStatusCode.Created, folderResponse.StatusCode);
        var folderId = (await folderResponse.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<int>();
        var groupResponse = await SendAsync(client, HttpMethod.Post, "/api/admin/groups", adminToken,
            new { name = $"audit_grp_{suffix}" });
        Assert.Equal(HttpStatusCode.Created, groupResponse.StatusCode);
        var groupId = (await groupResponse.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<int>();

        // ACL grant with the current version succeeds and must carry a correlated audit row.
        var granted = await SendAsync(client, HttpMethod.Post, $"/api/folders/{folderId}/acl",
            adminToken, new { groupId, permission = 0 }, version: 1);
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var row = await db.AuditLogs.SingleAsync(a =>
                a.Action == "GRANT_PERMISSION" && a.ResourceId == folderId.ToString());
            Assert.False(string.IsNullOrWhiteSpace(row.CorrelationId),
                "expected the audit row to carry the request's correlation id");
        }

        // A stale-version retry is rejected — and must leave no second audit row behind.
        var stale = await SendAsync(client, HttpMethod.Post, $"/api/folders/{folderId}/acl",
            adminToken, new { groupId, permission = 2 }, version: 1);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            Assert.Equal(1, await db.AuditLogs.CountAsync(a =>
                a.Action == "GRANT_PERMISSION" && a.ResourceId == folderId.ToString()));
        }
    }

    private static async Task<string> GetAdminTokenAsync(HttpClient client)
    {
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = "Admin@12345!" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = (await login.Content.ReadFromJsonAsync<JsonObject>())!["token"]!.GetValue<string>();
        var change = await SendAsync(client, HttpMethod.Post, "/api/auth/change-password", token,
            new { currentPassword = "Admin@12345!", newPassword = "Admin@Tests99!" });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        var relogin = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = "Admin@Tests99!" });
        return (await relogin.Content.ReadFromJsonAsync<JsonObject>())!["token"]!.GetValue<string>();
    }

    private static Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string url, string token, object? body, long? version = null)
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
