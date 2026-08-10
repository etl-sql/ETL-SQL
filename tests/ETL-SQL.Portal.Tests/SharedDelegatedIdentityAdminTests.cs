using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Portal")]
public sealed class SharedDelegatedIdentityAdminTests
{
    [Fact]
    public async Task TenantAdminsCannotEnumerateOrMutateTheOtherIdentityPartition()
    {
        using var factory = new SharedAdminFactory();
        var seeded = await SeedAsync(factory);
        using var alpha = Client(factory, seeded.AlphaToken);
        using var beta = Client(factory, seeded.BetaToken);

        var alphaUsers = await alpha.GetStringAsync("/api/admin/users");
        var betaUsers = await beta.GetStringAsync("/api/admin/users");
        Assert.Contains("alpha-admin", alphaUsers);
        Assert.DoesNotContain("beta-admin", alphaUsers);
        Assert.Contains("beta-admin", betaUsers);
        Assert.DoesNotContain("alpha-admin", betaUsers);

        var alphaGroups = await alpha.GetStringAsync("/api/admin/groups");
        Assert.Contains("Equal Group", alphaGroups);
        Assert.DoesNotContain("beta-only", alphaGroups);
        Assert.Contains("beta-only", await beta.GetStringAsync("/api/admin/groups"));

        Assert.Equal(HttpStatusCode.NotFound,
            (await alpha.GetAsync($"/api/admin/users/{seeded.BetaUserId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await PutAsync(alpha, $"/api/admin/service-accounts/{seeded.BetaServiceId}", new
            {
                isEnabled = false,
                expiresAt = DateTime.UtcNow.AddHours(1),
                scopes = new[] { ServiceAccountScopes.PortalRead }
            }, version: 1)).StatusCode);

        var crossMembership = new HttpRequestMessage(
            HttpMethod.Post, $"/api/admin/groups/{seeded.AlphaGroupId}/members")
        {
            Content = JsonContent.Create(new { userId = seeded.BetaUserId })
        };
        crossMembership.Headers.IfMatch.Add(new EntityTagHeaderValue("\"1\""));
        Assert.Equal(HttpStatusCode.NotFound, (await alpha.SendAsync(crossMembership)).StatusCode);

        var alphaSessions = await alpha.GetStringAsync("/api/admin/sessions");
        Assert.Contains(seeded.AlphaRefreshId.ToString(), alphaSessions);
        Assert.DoesNotContain($"\"id\":{seeded.BetaRefreshId}", alphaSessions);
        var alphaServices = await alpha.GetStringAsync("/api/admin/service-accounts");
        Assert.Contains("equal-agent", alphaServices);
        Assert.DoesNotContain("beta-only-agent", alphaServices);
        var alphaAuthorities = await alpha.GetStringAsync("/api/admin/identity/authorities");
        Assert.Contains("alpha.portal.test", alphaAuthorities);
        Assert.DoesNotContain("beta.portal.test", alphaAuthorities);
        Assert.Equal(HttpStatusCode.NotFound,
            (await alpha.PostAsync("/api/admin/identity/authorities/beta-authority/disable", null)).StatusCode);

        var alphaCreate = await alpha.PostAsJsonAsync("/api/admin/users", NewUser("equal-local"));
        var betaCreate = await beta.PostAsJsonAsync("/api/admin/users", NewUser("equal-local"));
        Assert.Equal(HttpStatusCode.Created, alphaCreate.StatusCode);
        Assert.Equal(HttpStatusCode.Created, betaCreate.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var equalUsers = await db.Users.Where(user => user.UserName == "equal-local").ToListAsync();
        Assert.Equal(2, equalUsers.Count);
        Assert.Equal(["tenant-alpha", "tenant-beta"], equalUsers.OrderBy(x => x.TenantId).Select(x => x.TenantId));
        Assert.False(await db.UserGroups.AnyAsync(membership =>
            membership.GroupId == seeded.AlphaGroupId && membership.UserId == seeded.BetaUserId));
    }

    private static object NewUser(string username) => new
    {
        username,
        email = username + "@example.test",
        password = "Valid@12345!",
        role = "Viewer",
        firstName = "Equal",
        lastName = "User",
        provider = "Local"
    };

    private static HttpClient Client(SharedAdminFactory factory, string token)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://shared.portal.test")
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static Task<HttpResponseMessage> PutAsync(
        HttpClient client, string path, object body, long version)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, path) { Content = JsonContent.Create(body) };
        request.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{version}\""));
        return client.SendAsync(request);
    }

    private static async Task<Seeded> SeedAsync(SharedAdminFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var adminRole = await db.Roles.SingleAsync(role => role.Name == "Admin");
        var alpha = User("tenant-alpha", "alpha-admin", "alpha-stamp");
        var beta = User("tenant-beta", "beta-admin", "beta-stamp");
        db.Users.AddRange(alpha, beta);
        await db.SaveChangesAsync();
        db.UserRoles.AddRange(
            new Microsoft.AspNetCore.Identity.IdentityUserRole<int> { UserId = alpha.Id, RoleId = adminRole.Id },
            new Microsoft.AspNetCore.Identity.IdentityUserRole<int> { UserId = beta.Id, RoleId = adminRole.Id });
        var alphaGroup = new Group { TenantId = "tenant-alpha", Name = "Equal Group" };
        var betaGroup = new Group { TenantId = "tenant-beta", Name = "Equal Group", Description = "beta-only" };
        db.Groups.AddRange(alphaGroup, betaGroup);
        var alphaRefresh = new RefreshToken
        {
            TenantId = "tenant-alpha", UserId = alpha.Id, Token = "alpha-refresh-hash",
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };
        var betaRefresh = new RefreshToken
        {
            TenantId = "tenant-beta", UserId = beta.Id, Token = "beta-refresh-hash",
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };
        db.RefreshTokens.AddRange(alphaRefresh, betaRefresh);
        var alphaService = Service("tenant-alpha", alpha.Id, "equal-agent", "alpha-client");
        var betaService = Service("tenant-beta", beta.Id, "beta-only-agent", "beta-client");
        db.ServiceAccounts.AddRange(alphaService, betaService);
        db.SharedIdentityAuthorities.AddRange(
            Authority("alpha-authority", "tenant-alpha", "alpha.portal.test", "https://alpha.idp.test"),
            Authority("beta-authority", "tenant-beta", "beta.portal.test", "https://beta.idp.test"));
        await db.SaveChangesAsync();

        var config = scope.ServiceProvider.GetRequiredService<PortalConfig>();
        var tokens = new TokenService(config);
        return new Seeded(
            alpha.Id,
            beta.Id,
            alphaGroup.Id,
            betaService.Id,
            alphaRefresh.Id,
            betaRefresh.Id,
            tokens.GenerateJwt(alpha, ["Admin"], tenantContext: TenantContext.FromVerifiedCredential("tenant-alpha")),
            tokens.GenerateJwt(beta, ["Admin"], tenantContext: TenantContext.FromVerifiedCredential("tenant-beta")));
    }

    private static PortalUser User(string tenant, string username, string stamp) => new()
    {
        TenantId = tenant,
        UserName = username,
        NormalizedUserName = username.ToUpperInvariant(),
        Email = username + "@example.test",
        NormalizedEmail = (username + "@example.test").ToUpperInvariant(),
        SecurityStamp = stamp,
        IsActive = true
    };

    private static ServiceAccount Service(string tenant, int ownerId, string name, string clientId) => new()
    {
        TenantId = tenant,
        OwnerUserId = ownerId,
        Name = name,
        NormalizedName = name.ToUpperInvariant(),
        ClientId = clientId,
        SecretHash = "not-used",
        Scopes = ServiceAccountScopes.PortalRead
    };

    private static SharedIdentityAuthority Authority(string id, string tenant, string host, string issuer) => new()
    {
        AuthorityId = id,
        TenantId = tenant,
        PortalHost = host,
        LoginDomain = tenant + ".example.test",
        Issuer = issuer,
        ClientId = tenant + "-client",
        Enabled = true,
        Version = 1,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    private sealed record Seeded(
        int AlphaUserId,
        int BetaUserId,
        int AlphaGroupId,
        string BetaServiceId,
        int AlphaRefreshId,
        int BetaRefreshId,
        string AlphaToken,
        string BetaToken);

    private sealed class SharedAdminFactory : PortalWebFactory
    {
        protected override void CustomizeConfiguration(Dictionary<string, string?> settings) =>
            settings["Portal:SharedTenancy:Enabled"] = "true";

        protected override void CustomizePortalConfig(PortalConfig config) =>
            config.SharedTenancy.Enabled = true;
    }
}
