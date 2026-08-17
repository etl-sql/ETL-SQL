using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Portal")]
public sealed class SharedOidcAuthTests : IClassFixture<SharedOidcAuthTests.SharedOidcPortalFactory>
{
    private readonly SharedOidcPortalFactory _factory;

    public SharedOidcAuthTests(SharedOidcPortalFactory factory) => _factory = factory;

    [Fact]
    public async Task RoutedAuthoritiesProvisionEqualIdentitiesIntoSeparateTenantPartitions()
    {
        await _factory.SeedAsync();
        var alpha = Client("alpha.portal.test");
        var beta = Client("beta.portal.test");

        var alphaSession = await SignInAsync(alpha);
        var betaSession = await SignInAsync(beta);

        Assert.Equal("tenant-alpha", TenantClaim(alphaSession.AccessToken));
        Assert.Equal("tenant-beta", TenantClaim(betaSession.AccessToken));
        var alphaRefresh = await alpha.PostAsJsonAsync(
            "/api/auth/refresh", new { refreshToken = alphaSession.RefreshToken });
        var betaRefresh = await beta.PostAsJsonAsync(
            "/api/auth/refresh", new { refreshToken = betaSession.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, alphaRefresh.StatusCode);
        Assert.Equal(HttpStatusCode.OK, betaRefresh.StatusCode);
        Assert.Equal("tenant-alpha", TenantClaim(await AccessTokenAsync(alphaRefresh)));
        Assert.Equal("tenant-beta", TenantClaim(await AccessTokenAsync(betaRefresh)));
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var users = await db.Users.Where(x => x.ExternalSubject == "equal-subject").OrderBy(x => x.TenantId).ToListAsync();
        Assert.Equal(2, users.Count);
        Assert.Equal(["tenant-alpha", "tenant-beta"], users.Select(x => x.TenantId));
        Assert.All(users, x => Assert.Equal("equal-user", x.UserName));
        Assert.Equal("https://alpha.idp.test", users[0].ExternalIssuer);
        Assert.Equal("https://beta.idp.test", users[1].ExternalIssuer);

        var memberships = await db.UserGroups.Where(x => users.Select(u => u.Id).Contains(x.UserId)).ToListAsync();
        Assert.Equal(2, memberships.Count);
        Assert.All(memberships, membership =>
        {
            var user = users.Single(x => x.Id == membership.UserId);
            Assert.Equal(user.TenantId, membership.TenantId);
        });
        var refreshTokens = await db.RefreshTokens.Where(x => users.Select(u => u.Id).Contains(x.UserId)).ToListAsync();
        Assert.True(refreshTokens.Count >= 4);
        Assert.All(refreshTokens, token =>
            Assert.Equal(users.Single(x => x.Id == token.UserId).TenantId, token.TenantId));
    }

    [Fact]
    public async Task ServiceCredentialAndSignedRuntimeStateRemainTenantBound()
    {
        await _factory.SeedAsync();
        var client = Client("alpha.portal.test");
        await SignInAsync(client);
        string secret = ServiceAccountCredentials.NewSecret();
        ServiceAccount account;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var user = await db.Users.SingleAsync(x =>
                x.TenantId == "tenant-alpha" && x.ExternalSubject == "equal-subject");
            account = new ServiceAccount
            {
                TenantId = "tenant-alpha",
                Name = "shared-agent",
                NormalizedName = "SHARED-AGENT",
                ClientId = ServiceAccountCredentials.NewClientId(),
                OwnerUserId = user.Id,
                Scopes = ServiceAccountScopes.PortalRead
            };
            account.SecretHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher<ServiceAccount>>()
                .HashPassword(account, secret);
            db.ServiceAccounts.Add(account);
            await db.SaveChangesAsync();
        }

        var exchange = await client.PostAsJsonAsync(
            "/api/auth/service-token", new { clientId = account.ClientId, clientSecret = secret });
        Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);
        var serviceToken = await AccessTokenAsync(exchange);
        Assert.Equal("tenant-alpha", TenantClaim(serviceToken));
        Assert.Equal(HttpStatusCode.OK, (await GetWithTokenAsync(client, serviceToken)).StatusCode);

        var config = _factory.Services.GetRequiredService<PortalConfig>();
        var wrongTenantToken = new TokenService(config).GenerateServiceJwt(
            account, [], [ServiceAccountScopes.PortalRead],
            tenantContext: TenantContext.FromVerifiedCredential("tenant-beta"));
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await GetWithTokenAsync(client, wrongTenantToken)).StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var stored = await db.ServiceAccounts.SingleAsync(x => x.Id == account.Id);
            stored.TenantId = "tenant-beta";
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync(
                "/api/auth/service-token", new { clientId = account.ClientId, clientSecret = secret })).StatusCode);
    }

    [Fact]
    public async Task RefreshAndUserJwtRejectPersistedTenantMismatch()
    {
        await _factory.SeedAsync();
        var client = Client("alpha.portal.test");
        var session = await SignInAsync(client);
        PortalUser user;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            user = await db.Users.SingleAsync(x =>
                x.TenantId == "tenant-alpha" && x.ExternalSubject == "equal-subject");
            var hash = TokenService.HashRefreshToken(session.RefreshToken);
            var refresh = await db.RefreshTokens.SingleAsync(x => x.Token == hash);
            refresh.TenantId = "tenant-beta";
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync(
                "/api/auth/refresh", new { refreshToken = session.RefreshToken })).StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var hash = TokenService.HashRefreshToken(session.RefreshToken);
            var refresh = await db.RefreshTokens.SingleAsync(x => x.Token == hash);
            refresh.TenantId = "tenant-alpha";
            await db.SaveChangesAsync();
        }

        var config = _factory.Services.GetRequiredService<PortalConfig>();
        var wrongTenantToken = new TokenService(config).GenerateJwt(
            user, [], tenantContext: TenantContext.FromVerifiedCredential("tenant-beta"));
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await GetWithTokenAsync(client, wrongTenantToken)).StatusCode);
    }

    [Fact]
    public async Task UnregisteredHostCannotStartSharedLogin()
    {
        await _factory.SeedAsync();
        var response = await Client("unknown.portal.test").GetAsync("/api/auth/oidc/login");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private HttpClient Client(string host) => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri($"https://{host}")
    });

    private static async Task<(string AccessToken, string RefreshToken)> SignInAsync(HttpClient client)
    {
        var login = await client.GetAsync("/api/auth/oidc/login");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var callback = await client.GetAsync("/api/auth/oidc/callback?code=test-code&state=shared-state");
        Assert.Equal(HttpStatusCode.OK, callback.StatusCode);
        var html = await callback.Content.ReadAsStringAsync();
        const string prefix = "<script type=\"application/json\" id=\"sso-data\">";
        var start = html.IndexOf(prefix, StringComparison.Ordinal) + prefix.Length;
        var end = html.IndexOf("</script>", start, StringComparison.Ordinal);
        using var json = System.Text.Json.JsonDocument.Parse(html[start..end]);
        return (
            json.RootElement.GetProperty("token").GetString()!,
            json.RootElement.GetProperty("refreshToken").GetString()!);
    }

    private static async Task<string> AccessTokenAsync(HttpResponseMessage response)
    {
        using var json = await System.Text.Json.JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());
        return json.RootElement.TryGetProperty("token", out var token)
            ? token.GetString()!
            : json.RootElement.GetProperty("accessToken").GetString()!;
    }

    private static Task<HttpResponseMessage> GetWithTokenAsync(HttpClient client, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/folders");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(request);
    }

    private static string TenantClaim(string token) =>
        new JwtSecurityTokenHandler().ReadJwtToken(token).Claims.Single(x => x.Type == TokenService.TenantClaim).Value;

    public sealed class SharedOidcPortalFactory : PortalWebFactory
    {
        private int _seeded;

        protected override void CustomizeConfiguration(Dictionary<string, string?> settings) =>
            settings["Portal:SharedTenancy:Enabled"] = "true";

        protected override void CustomizePortalConfig(PortalConfig config) =>
            config.SharedTenancy.Enabled = true;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISharedOidcAuthenticationService>();
                services.AddSingleton<ISharedOidcAuthenticationService, StubSharedOidcAuthenticationService>();
            });
        }

        public async Task SeedAsync()
        {
            if (Interlocked.Exchange(ref _seeded, 1) != 0) return;
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            db.SharedIdentityAuthorities.AddRange(
                Authority("alpha", "tenant-alpha", "alpha.portal.test", "https://alpha.idp.test"),
                Authority("beta", "tenant-beta", "beta.portal.test", "https://beta.idp.test"));
            db.Groups.AddRange(
                new Group { TenantId = "tenant-alpha", Name = "Equal Group", Provider = "OIDC", AdGroup = "analysts" },
                new Group { TenantId = "tenant-beta", Name = "Equal Group", Provider = "OIDC", AdGroup = "analysts" });
            await db.SaveChangesAsync();
        }

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
    }

    private sealed class StubSharedOidcAuthenticationService : ISharedOidcAuthenticationService
    {
        public Task<OidcAuthorizationRequest> BuildAuthorizationRequestAsync(
            SharedIdentityAuthorityBinding authority,
            string redirectUri,
            CancellationToken ct = default) => Task.FromResult(new OidcAuthorizationRequest(
                $"https://login.example.test/authorize?client_id={authority.ClientId}",
                "shared-state", "shared-nonce", "shared-verifier"));

        public Task<OidcIdentity> CompleteAsync(
            SharedIdentityAuthorityBinding authority,
            string code,
            string codeVerifier,
            string redirectUri,
            string expectedNonce,
            CancellationToken ct = default)
        {
            Assert.Equal("shared-verifier", codeVerifier);
            Assert.Equal("shared-nonce", expectedNonce);
            return Task.FromResult(new OidcIdentity(
                "equal-subject", "equal-user", "equal@example.test", ["analysts"], authority.Issuer));
        }
    }
}
