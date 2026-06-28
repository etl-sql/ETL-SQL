using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ETL_SQL.ReportPortal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.ReportPortal.Tests;

[Trait("Category", "Portal")]
public sealed class ServiceAccountIntegrationTests
{
    [Fact]
    public async Task ProvisionAndExchange_ExposeSecretOnce_EnforceScopes_AndAttributeAudit()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var ownerId = await GetAdminIdAsync(factory);

        var created = await SendAsync(client, HttpMethod.Post, "/api/admin/service-accounts", adminToken,
            new
            {
                name = "report-reader",
                description = "integration test",
                ownerUserId = ownerId,
                scopes = new[] { "portal.read" },
                roles = Array.Empty<string>(),
                expiresAt = DateTime.UtcNow.AddHours(1)
            });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = (await created.Content.ReadFromJsonAsync<JsonObject>())!;
        var secret = body["clientSecret"]!.GetValue<string>();
        var account = body["account"]!.AsObject();
        var accountId = account["id"]!.GetValue<string>();
        var clientId = account["clientId"]!.GetValue<string>();
        Assert.StartsWith("sas_", secret, StringComparison.Ordinal);

        var listed = await SendAsync(client, HttpMethod.Get, "/api/admin/service-accounts", adminToken);
        var listText = await listed.Content.ReadAsStringAsync();
        Assert.DoesNotContain(secret, listText, StringComparison.Ordinal);
        Assert.DoesNotContain("secretHash", listText, StringComparison.OrdinalIgnoreCase);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var stored = await db.ServiceAccounts.SingleAsync(value => value.Id == accountId);
            Assert.NotEqual(secret, stored.SecretHash);
            Assert.DoesNotContain(secret, stored.SecretHash, StringComparison.Ordinal);
        }

        var serviceToken = await ExchangeAsync(client, clientId, secret);
        var folders = await SendAsync(client, HttpMethod.Get, "/api/folders", serviceToken);
        Assert.Equal(HttpStatusCode.OK, folders.StatusCode);
        var adminDenied = await SendAsync(client, HttpMethod.Get, "/api/admin/service-accounts", serviceToken);
        Assert.Equal(HttpStatusCode.Forbidden, adminDenied.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var audit = await db.AuditLogs.SingleAsync(value =>
                value.Action == "SERVICE_ACCOUNT_TOKEN_ISSUED" && value.ActorId == accountId);
            Assert.Equal("ServiceAccount", audit.ActorType);
            Assert.Equal("portal.read", audit.EffectiveScopes);
            Assert.False(string.IsNullOrWhiteSpace(audit.CorrelationId));
            Assert.DoesNotContain(secret, audit.Detail ?? "", StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task RotateExpireAndRevoke_InvalidateCredentialsAndIssuedTokens()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var ownerId = await GetAdminIdAsync(factory);
        var provisioned = await CreateAsync(client, adminToken, ownerId, "rotating-reader");
        var originalToken = await ExchangeAsync(client, provisioned.ClientId, provisioned.Secret);

        var rotated = await SendAsync(client, HttpMethod.Post,
            $"/api/admin/service-accounts/{provisioned.Id}/rotate-secret", adminToken, version: 1);
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        var newSecret = (await rotated.Content.ReadFromJsonAsync<JsonObject>())!["clientSecret"]!.GetValue<string>();
        Assert.NotEqual(provisioned.Secret, newSecret);
        var staleRotation = await SendAsync(client, HttpMethod.Post,
            $"/api/admin/service-accounts/{provisioned.Id}/rotate-secret", adminToken, version: 1);
        Assert.Equal(HttpStatusCode.Conflict, staleRotation.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/service-token",
                new { clientId = provisioned.ClientId, clientSecret = provisioned.Secret })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await SendAsync(client, HttpMethod.Get, "/api/folders", originalToken)).StatusCode);
        var rotatedToken = await ExchangeAsync(client, provisioned.ClientId, newSecret);

        var disabled = await SendAsync(client, HttpMethod.Put,
            $"/api/admin/service-accounts/{provisioned.Id}", adminToken,
            new { isEnabled = false, expiresAt = DateTime.UtcNow.AddHours(1), scopes = new[] { "portal.read" } },
            version: 2);
        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/service-token",
                new { clientId = provisioned.ClientId, clientSecret = newSecret })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await SendAsync(client, HttpMethod.Get, "/api/folders", rotatedToken)).StatusCode);

        var enabled = await SendAsync(client, HttpMethod.Put,
            $"/api/admin/service-accounts/{provisioned.Id}", adminToken,
            new { isEnabled = true, expiresAt = DateTime.UtcNow.AddHours(1), scopes = new[] { "portal.read" } },
            version: 3);
        Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);
        rotatedToken = await ExchangeAsync(client, provisioned.ClientId, newSecret);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var row = await db.ServiceAccounts.SingleAsync(value => value.Id == provisioned.Id);
            row.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/service-token",
                new { clientId = provisioned.ClientId, clientSecret = newSecret })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await SendAsync(client, HttpMethod.Get, "/api/folders", rotatedToken)).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var row = await db.ServiceAccounts.SingleAsync(value => value.Id == provisioned.Id);
            row.ExpiresAt = DateTime.UtcNow.AddHours(1);
            await db.SaveChangesAsync();
        }
        var currentToken = await ExchangeAsync(client, provisioned.ClientId, newSecret);
        var revoked = await SendAsync(client, HttpMethod.Post,
            $"/api/admin/service-accounts/{provisioned.Id}/revoke", adminToken, version: 4);
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await SendAsync(client, HttpMethod.Get, "/api/folders", currentToken)).StatusCode);
    }

    private static async Task<(string Id, string ClientId, string Secret)> CreateAsync(
        HttpClient client, string adminToken, int ownerId, string name)
    {
        var response = await SendAsync(client, HttpMethod.Post, "/api/admin/service-accounts", adminToken,
            new
            {
                name,
                ownerUserId = ownerId,
                scopes = new[] { "portal.read" },
                roles = Array.Empty<string>(),
                expiresAt = DateTime.UtcNow.AddHours(1)
            });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
        var account = body["account"]!.AsObject();
        return (account["id"]!.GetValue<string>(), account["clientId"]!.GetValue<string>(),
            body["clientSecret"]!.GetValue<string>());
    }

    private static async Task<string> ExchangeAsync(HttpClient client, string clientId, string secret)
    {
        var response = await client.PostAsJsonAsync("/api/auth/service-token",
            new { clientId, clientSecret = secret });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!["accessToken"]!.GetValue<string>();
    }

    private static async Task<int> GetAdminIdAsync(PortalWebFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        return await db.Users.Where(value => value.UserName == "admin").Select(value => value.Id).SingleAsync();
    }

    private static async Task<string> GetAdminTokenAsync(HttpClient client)
    {
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = "Admin@12345!" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = (await login.Content.ReadFromJsonAsync<JsonObject>())!["token"]!.GetValue<string>();
        var change = await SendAsync(client, HttpMethod.Post, "/api/auth/change-password", token,
            new { currentPassword = "Admin@12345!", newPassword = "Admin@Service99!" });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        var relogin = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = "Admin@Service99!" });
        Assert.Equal(HttpStatusCode.OK, relogin.StatusCode);
        return (await relogin.Content.ReadFromJsonAsync<JsonObject>())!["token"]!.GetValue<string>();
    }

    private static Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string url, string token, object? body = null, long? version = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new("Bearer", token);
        if (version.HasValue) request.Headers.TryAddWithoutValidation("If-Match", $"\"{version.Value}\"");
        if (body is not null) request.Content = JsonContent.Create(body);
        return client.SendAsync(request);
    }
}
