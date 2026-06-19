using System.Net;
using System.Net.Http.Json;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Models;
using ETL_SQL.ReportPortal.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ETL_SQL.ReportPortal.Tests;

/// <summary>
/// P1.2 certification for federated OIDC: the authorization-code login bridges to the portal's own
/// JWT/refresh session, provisions and syncs the user, and leaves local login working. The external
/// provider is replaced with <see cref="StubOidcAuthenticationService"/> so the controller/bridge —
/// cookie flow, state verification, provisioning, token issuance, refresh, logout — is certified
/// without a live IdP. The token crypto/validation itself is covered by OidcAuthenticationServiceTests.
/// </summary>
[Trait("Category", "Portal")]
public sealed class OidcAuthTests : IClassFixture<OidcAuthTests.OidcPortalWebFactory>
{
    private readonly OidcPortalWebFactory _factory;

    public OidcAuthTests(OidcPortalWebFactory factory) => _factory = factory;

    private HttpClient NoRedirectClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task Login_WhenOidcEnabled_RedirectsToProvider_AndSetsFlowCookie()
    {
        var client = NoRedirectClient();

        var res = await client.GetAsync("/api/auth/oidc/login");

        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Equal(StubOidcAuthenticationService.AuthorizationUrl, res.Headers.Location!.ToString());
        Assert.Contains(res.Headers.GetValues("Set-Cookie"), c => c.StartsWith("ETLSQL_OIDC_FLOW="));
    }

    [Fact]
    public async Task Login_WhenOidcDisabled_Returns404()
    {
        using var plain = new PortalWebFactory();
        var client = plain.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var res = await client.GetAsync("/api/auth/oidc/login");

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Callback_ValidState_ProvisionsUser_IssuesSession_AndRefreshWorks()
    {
        var username = "oidc_" + Guid.NewGuid().ToString("N")[..8];
        _factory.Stub.Identity = new OidcIdentity("sub-" + username, username, $"{username}@example.com", []);

        var client = NoRedirectClient();
        await client.GetAsync("/api/auth/oidc/login"); // sets the flow cookie on this client

        var callback = await client.GetAsync(
            $"/api/auth/oidc/callback?code=test-code&state={StubOidcAuthenticationService.State}");

        // Success renders a hand-off page (tokens in a JSON data-island), never the URL.
        Assert.Equal(HttpStatusCode.OK, callback.StatusCode);
        Assert.Equal("text/html", callback.Content.Headers.ContentType!.MediaType);
        var (accessToken, refreshToken) = await ParseHandoffTokensAsync(callback);
        Assert.False(string.IsNullOrEmpty(accessToken));
        Assert.False(string.IsNullOrEmpty(refreshToken));

        // User was provisioned as an OIDC account.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var user = await db.Users.FirstOrDefaultAsync(u => u.UserName == username);
            Assert.NotNull(user);
            Assert.Equal("OIDC", user!.Provider);
            Assert.Equal($"{username}@example.com", user.Email);
        }

        // The issued refresh token rotates into a fresh session.
        var refreshRes = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(refreshToken));
        Assert.Equal(HttpStatusCode.OK, refreshRes.StatusCode);
        var refreshed = await refreshRes.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.False(string.IsNullOrEmpty(refreshed!.Token));

        // The issued access token authorizes a protected endpoint, and logout invalidates the session.
        using var logoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logoutReq.Headers.Authorization = new("Bearer", accessToken);
        logoutReq.Content = JsonContent.Create(new RefreshRequest(refreshed.RefreshToken));
        var logoutRes = await client.SendAsync(logoutReq);
        Assert.Equal(HttpStatusCode.NoContent, logoutRes.StatusCode);
    }

    [Fact]
    public async Task Callback_InvalidState_RedirectsToError()
    {
        var client = NoRedirectClient();
        await client.GetAsync("/api/auth/oidc/login");

        var callback = await client.GetAsync("/api/auth/oidc/callback?code=test-code&state=not-the-real-state");

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Contains("error=sso_failed", callback.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Callback_DisabledUser_RedirectsToAccountDisabled()
    {
        var username = "oidcoff_" + Guid.NewGuid().ToString("N")[..8];
        _factory.Stub.Identity = new OidcIdentity("sub-" + username, username, $"{username}@example.com", []);

        // First login provisions the account.
        var client = NoRedirectClient();
        await client.GetAsync("/api/auth/oidc/login");
        await client.GetAsync($"/api/auth/oidc/callback?code=c&state={StubOidcAuthenticationService.State}");

        // Administrator disables it.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var user = await db.Users.SingleAsync(u => u.UserName == username);
            user.IsActive = false;
            await db.SaveChangesAsync();
        }

        // A fresh federated login must not resurrect a portal-disabled account.
        var client2 = NoRedirectClient();
        await client2.GetAsync("/api/auth/oidc/login");
        var callback = await client2.GetAsync($"/api/auth/oidc/callback?code=c&state={StubOidcAuthenticationService.State}");

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Contains("error=account_disabled", callback.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Callback_RefusesToAttachToNonOidcAccount()
    {
        // Provider-confusion guard: an IdP identity whose username matches the local 'admin' account
        // must NOT mint a session for it. The federated login is refused and audited.
        _factory.Stub.Identity = new OidcIdentity("attacker-sub", "admin", "admin@evil.test", []);

        var client = NoRedirectClient();
        await client.GetAsync("/api/auth/oidc/login");
        var callback = await client.GetAsync($"/api/auth/oidc/callback?code=c&state={StubOidcAuthenticationService.State}");

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Contains("error=sso_failed", callback.Headers.Location!.ToString());

        // The local admin account is untouched (still Provider=Local), and the refusal is audited.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var admin = await db.Users.SingleAsync(u => u.UserName == "admin");
        Assert.Equal("Local", admin.Provider);
        Assert.True(await db.AuditLogs.AnyAsync(a =>
            a.Action == "LOGIN_FAILED" && a.UserId == admin.Id && a.Detail!.Contains("OIDC login refused")));
    }

    [Fact]
    public async Task Callback_SyncsGroupClaims_AddingAndRemoving()
    {
        var username = "oidcgrp_" + Guid.NewGuid().ToString("N")[..8];
        int groupId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var group = new Group { Name = "Analysts_" + username, Provider = "OIDC", AdGroup = "analysts" };
            db.Groups.Add(group);
            await db.SaveChangesAsync();
            groupId = group.Id;
        }

        // First login: claim the group → membership added.
        _factory.Stub.Identity = new OidcIdentity("sub-" + username, username, null, ["analysts"]);
        var c1 = NoRedirectClient();
        await c1.GetAsync("/api/auth/oidc/login");
        await c1.GetAsync($"/api/auth/oidc/callback?code=c&state={StubOidcAuthenticationService.State}");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var user = await db.Users.SingleAsync(u => u.UserName == username);
            Assert.True(await db.UserGroups.AnyAsync(ug => ug.UserId == user.Id && ug.GroupId == groupId));
        }

        // Second login: claim dropped → membership removed (deterministic stale removal).
        _factory.Stub.Identity = new OidcIdentity("sub-" + username, username, null, []);
        var c2 = NoRedirectClient();
        await c2.GetAsync("/api/auth/oidc/login");
        await c2.GetAsync($"/api/auth/oidc/callback?code=c&state={StubOidcAuthenticationService.State}");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var user = await db.Users.SingleAsync(u => u.UserName == username);
            Assert.False(await db.UserGroups.AnyAsync(ug => ug.UserId == user.Id && ug.GroupId == groupId));
        }
    }

    [Fact]
    public async Task Providers_ReflectsOidcEnabled()
    {
        var res = await _factory.CreateClient().GetFromJsonAsync<ProvidersDto>("/api/auth/providers");

        Assert.True(res!.Local);
        Assert.True(res.OidcEnabled);
        Assert.Equal("/api/auth/oidc/login", res.OidcLoginUrl);
    }

    [Fact]
    public async Task Providers_WhenOidcDisabled_ReportsDisabled()
    {
        using var plain = new PortalWebFactory();
        var res = await plain.CreateClient().GetFromJsonAsync<ProvidersDto>("/api/auth/providers");

        Assert.True(res!.Local);
        Assert.False(res.OidcEnabled);
        Assert.Null(res.OidcLoginUrl);
    }

    private sealed record ProvidersDto(bool Local, bool OidcEnabled, string? OidcLoginUrl);

    [Fact]
    public async Task LocalLogin_StillWorks_WhenOidcEnabled()
    {
        var client = _factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest("admin", "Admin@12345!"));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    private static async Task<(string AccessToken, string RefreshToken)> ParseHandoffTokensAsync(HttpResponseMessage res)
    {
        var html = await res.Content.ReadAsStringAsync();
        const string open = "<script type=\"application/json\" id=\"sso-data\">";
        var start = html.IndexOf(open, StringComparison.Ordinal) + open.Length;
        var end = html.IndexOf("</script>", start, StringComparison.Ordinal);
        using var doc = System.Text.Json.JsonDocument.Parse(html[start..end]);
        var root = doc.RootElement;
        return (root.GetProperty("token").GetString()!, root.GetProperty("refreshToken").GetString()!);
    }

    public sealed class OidcPortalWebFactory : PortalWebFactory
    {
        public StubOidcAuthenticationService Stub { get; } = new();

        protected override void CustomizePortalConfig(PortalConfig config)
        {
            config.Identity = new IdentityConfig
            {
                Provider = "Local",
                Oidc = new OidcIdentityConfig
                {
                    Enabled = true,
                    Authority = "https://idp.example.test",
                    ClientId = "etl-portal",
                    ClientSecret = "test-client-secret",
                    PostLoginRedirectPath = "/index.html"
                }
            };
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IOidcAuthenticationService>();
                services.AddSingleton<IOidcAuthenticationService>(Stub);
            });
        }
    }

    public sealed class StubOidcAuthenticationService : IOidcAuthenticationService
    {
        public const string AuthorizationUrl = "https://idp.example.test/authorize?client_id=etl-portal";
        public const string State = "test-state-value";
        public const string Nonce = "test-nonce-value";
        public const string Verifier = "test-code-verifier";

        public OidcIdentity Identity { get; set; } = new("sub", "user", "user@example.com", []);

        public bool Enabled => true;

        public Task<OidcAuthorizationRequest> BuildAuthorizationRequestAsync(string redirectUri, CancellationToken ct = default) =>
            Task.FromResult(new OidcAuthorizationRequest(AuthorizationUrl, State, Nonce, Verifier));

        public Task<OidcIdentity> CompleteAsync(
            string code, string codeVerifier, string redirectUri, string expectedNonce, CancellationToken ct = default)
        {
            Assert.Equal(Nonce, expectedNonce);   // controller must pass the flow nonce through
            Assert.Equal(Verifier, codeVerifier); // and the PKCE verifier from the flow cookie
            return Task.FromResult(Identity);
        }
    }
}
