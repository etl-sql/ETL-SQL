using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// End-to-end cover for the narrow hole opened in the <c>/api/admin</c> deny: a service identity
/// holding <c>admin.identity</c> can administer users and groups, and nothing else.
/// </summary>
[Trait("Category", "Portal")]
public sealed class AdminIdentityScopeIntegrationTests
{
    [Fact]
    public async Task IdentityScopeAdministersUsersButReachesNothingElseUnderAdmin()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var ownerId = await GetAdminIdAsync(factory);

        var token = await IssueIdentityTokenAsync(client, adminToken, ownerId, "identity-admin");

        // Reaches identity administration.
        var users = await SendAsync(client, HttpMethod.Get, "/api/admin/users", token);
        Assert.Equal(HttpStatusCode.OK, users.StatusCode);
        var groups = await SendAsync(client, HttpMethod.Get, "/api/admin/groups", token);
        Assert.Equal(HttpStatusCode.OK, groups.StatusCode);
        var sessions = await SendAsync(client, HttpMethod.Get, "/api/admin/sessions", token);
        Assert.Equal(HttpStatusCode.OK, sessions.StatusCode);

        // Reaches nothing else, including the capabilities the scope deliberately withholds.
        foreach (var path in new[]
                 {
                     "/api/admin/support-bundle",
                     "/api/admin/configuration/export",
                     "/api/admin/audit",
                     "/api/admin/settings/branding",
                     "/api/admin/service-accounts",
                     "/api/admin/metrics/usage"
                 })
        {
            var denied = await SendAsync(client, HttpMethod.Get, path, token);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }

        var shutdown = await SendAsync(client, HttpMethod.Post, "/api/admin/service/shutdown", token);
        Assert.Equal(HttpStatusCode.Forbidden, shutdown.StatusCode);
    }

    /// <summary>
    /// The escalation question. A token that could mint an Admin could grant itself — through a new
    /// account — every capability this scope withholds, which would make the narrow scope
    /// meaningless.
    /// </summary>
    [Fact]
    public async Task ServiceTokenCannotCreateAnAdminButCanCreateAnOrdinaryUser()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var ownerId = await GetAdminIdAsync(factory);
        var token = await IssueIdentityTokenAsync(client, adminToken, ownerId, "identity-escalation");
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var elevated = await SendAsync(client, HttpMethod.Post, "/api/admin/users", token, new
        {
            username = $"escalated_{suffix}",
            email = $"escalated_{suffix}@corp.local",
            role = "Admin",
            password = "Str0ng@Passw0rd!"
        });
        Assert.Equal(HttpStatusCode.Forbidden, elevated.StatusCode);
        Assert.Contains("admin_elevation_requires_interactive_user",
            await elevated.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // The account must not exist: the guard runs before any mutation.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            Assert.False(await db.Users.AnyAsync(user => user.UserName == $"escalated_{suffix}"));
        }

        // Provisioning ordinary users is the whole point of the scope and still works.
        var ordinary = await SendAsync(client, HttpMethod.Post, "/api/admin/users", token, new
        {
            username = $"ordinary_{suffix}",
            email = $"ordinary_{suffix}@corp.local",
            role = "Viewer",
            password = "Str0ng@Passw0rd!"
        });
        Assert.Equal(HttpStatusCode.Created, ordinary.StatusCode);
    }

    /// <summary>Demotion is deliberately still permitted — revoking an admin should not need a browser.</summary>
    [Fact]
    public async Task ServiceTokenCannotPromoteAnExistingUserToAdmin()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var ownerId = await GetAdminIdAsync(factory);
        var token = await IssueIdentityTokenAsync(client, adminToken, ownerId, "identity-promote");
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var created = await SendAsync(client, HttpMethod.Post, "/api/admin/users", token, new
        {
            username = $"promote_{suffix}",
            email = $"promote_{suffix}@corp.local",
            role = "Viewer",
            password = "Str0ng@Passw0rd!"
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var userId = (await created.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<int>();

        var promote = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{userId}")
        {
            Content = JsonContent.Create(new { role = "Admin" })
        };
        promote.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(promote);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var isAdmin = await db.UserRoles
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .AnyAsync(entry => entry.UserId == userId && entry.Name == "Admin");
        Assert.False(isAdmin);
    }

    /// <summary>
    /// A mutation made by automation must be tellable from the same-named human who owns the token,
    /// or the audit log cannot answer "who did this".
    /// </summary>
    [Fact]
    public async Task MutationsAreAttributedToTheServiceIdentityNotTheOwningHuman()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var ownerId = await GetAdminIdAsync(factory);
        var (accountId, token) = await IssueIdentityTokenWithIdAsync(client, adminToken, ownerId, "identity-audit");
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var created = await SendAsync(client, HttpMethod.Post, "/api/admin/users", token, new
        {
            username = $"audited_{suffix}",
            email = $"audited_{suffix}@corp.local",
            role = "Viewer",
            password = "Str0ng@Passw0rd!"
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var entry = await db.AuditLogs
            .Where(log => log.Action == "CREATE_USER" && log.ActorId == accountId)
            .OrderByDescending(log => log.Id)
            .FirstAsync();

        Assert.Equal("ServiceAccount", entry.ActorType);
        Assert.Equal(accountId, entry.ActorId);
        Assert.Contains("admin.identity", entry.EffectiveScopes ?? "", StringComparison.Ordinal);
        // The owning human's id is still recorded, but it is not what identifies the actor.
        Assert.Equal(ownerId, entry.UserId);
    }

    /// <summary>
    /// The Admin role is only safe on a token because admin.identity confines it to the identity
    /// allowlist. Granting the role without the scope would restore unbounded administrative reach.
    /// </summary>
    [Fact]
    public async Task AdminRoleIsRefusedOnAServiceAccountWithoutTheIdentityScope()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var ownerId = await GetAdminIdAsync(factory);

        var response = await SendAsync(client, HttpMethod.Post, "/api/admin/service-accounts", adminToken,
            new
            {
                name = $"unscoped-admin-{Guid.NewGuid().ToString("N")[..6]}",
                ownerUserId = ownerId,
                scopes = new[] { "portal.read" },
                roles = new[] { "Admin" },
                expiresAt = DateTime.UtcNow.AddHours(1)
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("admin.identity", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdminRoleIsAcceptedWhenPairedWithTheIdentityScope()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var ownerId = await GetAdminIdAsync(factory);

        var response = await SendAsync(client, HttpMethod.Post, "/api/admin/service-accounts", adminToken,
            new
            {
                name = $"scoped-admin-{Guid.NewGuid().ToString("N")[..6]}",
                ownerUserId = ownerId,
                scopes = new[] { "admin.identity" },
                roles = new[] { "Admin" },
                expiresAt = DateTime.UtcNow.AddHours(1)
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // ── Harness ──────────────────────────────────────────────────────────────────

    private static async Task<string> IssueIdentityTokenAsync(
        HttpClient client, string adminToken, int ownerId, string name) =>
        (await IssueIdentityTokenWithIdAsync(client, adminToken, ownerId, name)).Token;

    private static async Task<(string AccountId, string Token)> IssueIdentityTokenWithIdAsync(
        HttpClient client, string adminToken, int ownerId, string name)
    {
        var response = await SendAsync(client, HttpMethod.Post, "/api/admin/service-accounts", adminToken,
            new
            {
                name = $"{name}-{Guid.NewGuid().ToString("N")[..6]}",
                ownerUserId = ownerId,
                scopes = new[] { "admin.identity" },
                roles = new[] { "Admin" },
                expiresAt = DateTime.UtcNow.AddHours(1)
            });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
        var account = body["account"]!.AsObject();
        var clientId = account["clientId"]!.GetValue<string>();
        var secret = body["clientSecret"]!.GetValue<string>();

        var exchange = await client.PostAsJsonAsync("/api/auth/service-token",
            new { clientId, clientSecret = secret });
        Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);
        var token = (await exchange.Content.ReadFromJsonAsync<JsonObject>())!["accessToken"]!.GetValue<string>();

        return (account["id"]!.GetValue<string>(), token);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string path, string token, object? payload = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (payload is not null) request.Content = JsonContent.Create(payload);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
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
            new { currentPassword = "Admin@12345!", newPassword = "Admin@Identity99!" });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        var relogin = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = "Admin@Identity99!" });
        Assert.Equal(HttpStatusCode.OK, relogin.StatusCode);
        return (await relogin.Content.ReadFromJsonAsync<JsonObject>())!["token"]!.GetValue<string>();
    }
}
