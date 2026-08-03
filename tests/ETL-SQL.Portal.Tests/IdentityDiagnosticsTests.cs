using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Identity diagnostics exist so an operator can see why federated login misbehaves without reading
/// logs, and so one question gets asked before it matters rather than after: if the identity
/// provider goes away, can anyone still administer this Portal?
/// </summary>
[Trait("Category", "Portal")]
public sealed class IdentityDiagnosticsTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task ReportsProviderConfigurationWithoutEverReturningASecret()
    {
        using var factory = new OidcConfiguredFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);

        var diagnostics = await DiagnosticsAsync(client, adminToken);
        var oidc = diagnostics["oidc"]!.AsObject();

        Assert.True(oidc["enabled"]!.GetValue<bool>());
        Assert.Equal("etl-portal", oidc["clientId"]!.GetValue<string>());
        Assert.True(oidc["clientSecretConfigured"]!.GetValue<bool>());

        // The configured secret must appear as a flag and nowhere as a value — the whole response is
        // checked, not just the field it would obviously live in.
        Assert.DoesNotContain("super-secret-value", diagnostics.ToJsonString(), StringComparison.Ordinal);

        // The provider is unreachable in a test, and saying so plainly is the point of the probe.
        Assert.False(oidc["discoveryReachable"]!.GetValue<bool>());
        Assert.NotNull(oidc["discoveryError"]?.GetValue<string>());

        var ldap = diagnostics["ldap"]!.AsObject();
        Assert.False(ldap["enabled"]!.GetValue<bool>());
        Assert.False(ldap["servicePasswordConfigured"]!.GetValue<bool>());
    }

    [Fact]
    public async Task NamesTheClaimValueEachGroupExpects_AndTestsAMappingWithoutASignIn()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        // One group matched by its AD name, one by its portal name — the two halves of the rule
        // OidcUserProvisioningService applies.
        await SeedProviderGroupAsync(factory, $"Finance {suffix}", adGroup: $"CN=Finance-{suffix}");
        await SeedProviderGroupAsync(factory, $"Ops {suffix}", adGroup: null);

        var diagnostics = await DiagnosticsAsync(client, adminToken);
        var mappings = diagnostics["groupMappings"]!.AsArray()
            .ToDictionary(m => m!["groupName"]!.GetValue<string>(), m => m!["claimValue"]!.GetValue<string>());

        Assert.Equal($"CN=Finance-{suffix}", mappings[$"Finance {suffix}"]);
        Assert.Equal($"Ops {suffix}", mappings[$"Ops {suffix}"]);

        // A mapping can be checked before a user discovers it is wrong by not having access.
        var response = await AuthPost(client, adminToken, "/api/admin/identity/diagnostics/group-mapping-test",
            new { claimValues = new[] { $"CN=Finance-{suffix}", "CN=NoSuchGroup" } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonObject>(Json);

        Assert.Equal([$"Finance {suffix}"],
            result!["matched"]!.AsArray().Select(m => m!["groupName"]!.GetValue<string>()));
        // An unmatched claim is the usual cause of sign-in working and authorization silently not.
        Assert.Equal(["CN=NoSuchGroup"], result["unmatched"]!.AsArray().Select(v => v!.GetValue<string>()));
    }

    [Fact]
    public async Task ReportsBreakGlassReadiness_AndWithdrawsItWhenTheLastLocalAdminGoes()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);

        // The seeded admin is local, so the deployment starts able to recover.
        var before = await DiagnosticsAsync(client, adminToken);
        var breakGlass = before["breakGlass"]!.AsObject();
        Assert.True(breakGlass["ready"]!.GetValue<bool>());
        Assert.Contains("admin", breakGlass["localAdministrators"]!.AsArray().Select(a => a!.GetValue<string>()));

        // Federate the only administrator, as an estate that moves everything to its IdP would.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var admin = await db.Users.SingleAsync(user => user.UserName == "admin");
            admin.Provider = "OIDC";
            await db.SaveChangesAsync();
        }

        var after = await DiagnosticsAsync(client, adminToken);
        var afterBreakGlass = after["breakGlass"]!.AsObject();
        Assert.False(afterBreakGlass["ready"]!.GetValue<bool>());
        Assert.Empty(afterBreakGlass["localAdministrators"]!.AsArray());
        // The explanation has to say what the consequence is, not just report a false.
        Assert.Contains("nobody can sign in",
            afterBreakGlass["explanation"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CountsFederatedUsersLandingInNoMappedGroup()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            db.Users.Add(new PortalUser
            {
                UserName = $"fed_{suffix}",
                NormalizedUserName = $"FED_{suffix}".ToUpperInvariant(),
                Provider = "OIDC",
                ExternalSubject = $"sub-{suffix}"
            });
            await db.SaveChangesAsync();
        }

        var diagnostics = await DiagnosticsAsync(client, adminToken);
        var sync = diagnostics["syncHealth"]!.AsObject();

        Assert.Equal(1, sync["federatedUsers"]!.GetValue<int>());
        // Signing in fine and belonging to nothing is authorization broken quietly, which is exactly
        // the state this count exists to make visible.
        Assert.Equal(1, sync["federatedUsersWithNoMappedGroup"]!.GetValue<int>());
    }

    [Fact]
    public async Task IsAdministratorOnly()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await CreateViewerAsync(client, adminToken, $"idp_deny_{suffix}");
        var viewerToken = await LoginAsync(client, $"idp_deny_{suffix}", "Ready@Test2!");

        Assert.Equal(HttpStatusCode.Forbidden,
            (await AuthGet(client, viewerToken, "/api/admin/identity/diagnostics")).StatusCode);
    }

    /// <summary>A portal with OIDC configured (and unreachable, as it is in a test).</summary>
    private sealed class OidcConfiguredFactory : PortalWebFactory
    {
        protected override void CustomizePortalConfig(PortalConfig config)
        {
            config.Identity.Provider = "OIDC";
            config.Identity.Oidc.Enabled = true;
            config.Identity.Oidc.Authority = "https://identity.example.invalid/realms/etl";
            config.Identity.Oidc.ClientId = "etl-portal";
            config.Identity.Oidc.ClientSecret = "super-secret-value";
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static async Task SeedProviderGroupAsync(PortalWebFactory factory, string name, string? adGroup)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        db.Groups.Add(new Group { Name = name, Provider = "OIDC", AdGroup = adGroup });
        await db.SaveChangesAsync();
    }

    private static async Task<JsonObject> DiagnosticsAsync(HttpClient client, string adminToken)
    {
        var response = await AuthGet(client, adminToken, "/api/admin/identity/diagnostics");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>(Json))!;
    }

    private static async Task CreateViewerAsync(HttpClient client, string adminToken, string username)
    {
        var create = await AuthPost(client, adminToken, "/api/admin/users", new
        {
            username,
            email = $"{username}@test.local",
            password = "Initial@Test1!",
            role = "Viewer"
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var initial = await LoginAsync(client, username, "Initial@Test1!");
        Assert.Equal(HttpStatusCode.NoContent,
            (await AuthPost(client, initial, "/api/auth/change-password",
                new { currentPassword = "Initial@Test1!", newPassword = "Ready@Test2!" })).StatusCode);
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
