using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Models;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Portal")]
public sealed partial class NativeClientAuthTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [GeneratedRegex("name=\"csrf_token\"\\s+value=\"([^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex CsrfInputRegex();

    [Fact]
    public async Task DedicatedLoginAndConsent_IssuesBoundedNativeSession_AndAuditsClientLogin()
    {
        using var factory = new PortalWebFactory();
        await MarkAdminReadyAsync(factory);
        var browser = NoRedirectClient(factory);
        var (verifier, challenge) = CreatePkcePair();
        const string redirectUri = "http://127.0.0.1:45123/callback";
        const string state = "state-happy-path-123456";

        var callbackUrl = await CompleteDedicatedBrowserFlowAsync(
            browser, "admin", "Admin@12345!", NativeClientRegistry.VsCodeClientId, redirectUri, challenge, state, "studio.read studio.run");

        Assert.StartsWith(redirectUri + "?", callbackUrl, StringComparison.Ordinal);
        var query = ParseQuery(callbackUrl);
        Assert.Equal(state, query["state"]);
        var code = query["code"];
        Assert.False(string.IsNullOrWhiteSpace(code));
        Assert.DoesNotContain("token", callbackUrl, StringComparison.OrdinalIgnoreCase);

        var exchangeRes = await browser.PostAsJsonAsync("/api/auth/client/exchange", new NativeClientExchangeRequest(
            code, verifier, redirectUri, NativeClientRegistry.VsCodeClientId));
        Assert.Equal(HttpStatusCode.OK, exchangeRes.StatusCode);

        var session = await exchangeRes.Content.ReadFromJsonAsync<NativeClientTokenResponse>(Json);
        Assert.NotNull(session);
        Assert.Equal(TokenService.NativeTokenAudience, session!.Audience);
        Assert.Equal(NativeClientRegistry.VsCodeClientId, session.ClientId);
        Assert.Equal("portal-host", session.TenantId);
        Assert.Equal(["studio.read", "studio.run"], session.Scopes);
        Assert.Equal("admin", session.User.Username);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(session.Token);
        Assert.Contains(TokenService.NativeTokenAudience, jwt.Audiences);
        Assert.Equal(TokenService.NativeClientIdentityType, jwt.Claims.Single(c => c.Type == TokenService.IdentityTypeClaim).Value);
        Assert.Equal(session.SessionId, jwt.Claims.Single(c => c.Type == TokenService.NativeSessionIdClaim).Value);
        Assert.Equal(NativeClientRegistry.VsCodeClientId, jwt.Claims.Single(c => c.Type == TokenService.NativeClientIdClaim).Value);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "CLIENT_LOGIN" && a.ResourceId == session.SessionId));
    }

    [Fact]
    public async Task DedicatedLogin_MustChangePasswordUser_IsRefusedWithForbidden()
    {
        using var factory = new PortalWebFactory();
        var browser = NoRedirectClient(factory);
        var (_, challenge) = CreatePkcePair();
        const string redirectUri = "http://127.0.0.1:45122/callback";

        var authorize = await browser.GetAsync(BuildAuthorizeUrl(
            NativeClientRegistry.VsCodeClientId, redirectUri, challenge, "state-must-change-123456"));
        Assert.Equal(HttpStatusCode.Redirect, authorize.StatusCode);
        var tx = ParseQuery(authorize.Headers.Location!.ToString())["tx"];
        var loginPage = await browser.GetAsync(authorize.Headers.Location);
        var csrf = ExtractCsrfToken(await loginPage.Content.ReadAsStringAsync());

        var submit = await browser.PostAsync("/api/auth/client/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["tx"] = tx,
            ["csrf_token"] = csrf,
            ["username"] = "admin",
            ["password"] = "Admin@12345!"
        }));

        Assert.Equal(HttpStatusCode.Forbidden, submit.StatusCode);
        Assert.Contains("temporary", await submit.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Consent_Deny_RedirectsWithAccessDenied_AndIssuesNoCode()
    {
        using var factory = new PortalWebFactory();
        await MarkAdminReadyAsync(factory);
        var browser = NoRedirectClient(factory);
        var (_, challenge) = CreatePkcePair();
        const string redirectUri = "http://127.0.0.1:45124/callback";
        const string state = "state-deny-flow-123456";

        var callbackUrl = await CompleteDedicatedBrowserFlowAsync(
            browser, "admin", "Admin@12345!", NativeClientRegistry.VsCodeClientId, redirectUri, challenge, state,
            decision: "deny");

        var query = ParseQuery(callbackUrl);
        Assert.Equal("access_denied", query["error"]);
        Assert.Equal(state, query["state"]);
        Assert.False(query.ContainsKey("code"));
    }

    [Fact]
    public async Task Consent_RequiresAuthenticationAndValidCsrfAndSameOrigin()
    {
        using var factory = new PortalWebFactory();
        await MarkAdminReadyAsync(factory);
        var browser = NoRedirectClient(factory);
        var (_, challenge) = CreatePkcePair();
        const string redirectUri = "http://127.0.0.1:45125/callback";

        var authorize = await browser.GetAsync(BuildAuthorizeUrl(
            NativeClientRegistry.VsCodeClientId, redirectUri, challenge, "state-csrf-check-123456"));
        Assert.Equal(HttpStatusCode.Redirect, authorize.StatusCode);
        var tx = ParseQuery(authorize.Headers.Location!.ToString())["tx"];

        // Skipping browser login cannot access consent.
        var unauthConsent = await browser.GetAsync($"/api/auth/client/consent?tx={Uri.EscapeDataString(tx)}");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthConsent.StatusCode);

        // Complete login so transaction enters PendingConsent.
        var loginPage = await browser.GetAsync(authorize.Headers.Location);
        var loginCsrf = ExtractCsrfToken(await loginPage.Content.ReadAsStringAsync());
        var loginSubmit = await browser.PostAsync("/api/auth/client/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["tx"] = tx,
            ["csrf_token"] = loginCsrf,
            ["username"] = "admin",
            ["password"] = "Admin@12345!"
        }));
        Assert.Equal(HttpStatusCode.Redirect, loginSubmit.StatusCode);

        var consentPage = await browser.GetAsync(loginSubmit.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, consentPage.StatusCode);
        var consentCsrf = ExtractCsrfToken(await consentPage.Content.ReadAsStringAsync());

        // Wrong CSRF token is refused.
        var badCsrf = await browser.PostAsync("/api/auth/client/consent", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["tx"] = tx,
            ["csrf_token"] = "wrong-csrf-token",
            ["decision"] = "approve"
        }));
        Assert.Equal(HttpStatusCode.Forbidden, badCsrf.StatusCode);

        // Cross-origin Origin header is refused.
        using var crossOriginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/client/consent")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["tx"] = tx,
                ["csrf_token"] = consentCsrf,
                ["decision"] = "approve"
            })
        };
        crossOriginReq.Headers.TryAddWithoutValidation("Origin", "https://evil.example");
        var crossOriginRes = await browser.SendAsync(crossOriginReq);
        Assert.Equal(HttpStatusCode.Forbidden, crossOriginRes.StatusCode);
    }

    [Theory]
    [InlineData("https://evil.example/callback")]
    [InlineData("http://localhost:45123/callback")]
    [InlineData("http://127.0.0.2:45123/callback")]
    [InlineData("http://127.0.0.1/callback")]
    [InlineData("http://127.0.0.1:45123/other")]
    [InlineData("http://127.0.0.1:45123/callback?foo=bar")]
    [InlineData("http://127.0.0.1:45123/callback#frag")]
    public async Task Authorize_RejectsInvalidRedirectUris_WithoutRedirecting(string badRedirect)
    {
        using var factory = new PortalWebFactory();
        var browser = NoRedirectClient(factory);
        var (_, challenge) = CreatePkcePair();

        var res = await browser.GetAsync(BuildAuthorizeUrl(
            NativeClientRegistry.VsCodeClientId, badRedirect, challenge, "state-bad-redirect-123456"));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Null(res.Headers.Location);
    }

    [Fact]
    public async Task Authorize_AcceptsIpv6Loopback_AndRejectsPlainPkceOrUnknownClientOrScope()
    {
        using var factory = new PortalWebFactory();
        var browser = NoRedirectClient(factory);
        var (_, challenge) = CreatePkcePair();

        var ipv6 = await browser.GetAsync(BuildAuthorizeUrl(
            NativeClientRegistry.VsCodeClientId, "http://[::1]:45199/callback", challenge, "state-ipv6-12345678"));
        Assert.Equal(HttpStatusCode.Redirect, ipv6.StatusCode);

        var plainPkce = await browser.GetAsync(
            $"/api/auth/client/authorize?client_id={NativeClientRegistry.VsCodeClientId}" +
            $"&redirect_uri={Uri.EscapeDataString("http://127.0.0.1:45199/callback")}" +
            $"&code_challenge={Uri.EscapeDataString(challenge)}&code_challenge_method=plain&state=state-plain-123456");
        Assert.Equal(HttpStatusCode.BadRequest, plainPkce.StatusCode);

        var unknownClient = await browser.GetAsync(BuildAuthorizeUrl(
            "unregistered-client", "http://127.0.0.1:45199/callback", challenge, "state-client-123456"));
        Assert.Equal(HttpStatusCode.BadRequest, unknownClient.StatusCode);

        var badScope = await browser.GetAsync(BuildAuthorizeUrl(
            NativeClientRegistry.VsCodeClientId, "http://127.0.0.1:45199/callback", challenge, "state-scope-123456", "admin.all"));
        Assert.Equal(HttpStatusCode.BadRequest, badScope.StatusCode);
    }

    [Fact]
    public async Task NativeLdapLoginRefreshesRolesAndGroupsBeforeIssuingTokens()
    {
        using var factory = new LdapAuthTests.LdapPortalWebFactory();
        using var browser = NoRedirectClient(factory);
        const string username = "native-ldap-user";
        const string adGroup = "GG-Portal-Publishers";
        factory.MockLdap.AuthenticateFunc = (_, _) => new LdapUserResult
        {
            Username = username,
            Groups = [adGroup]
        };
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            db.Groups.Add(new ETL_SQL.Portal.Data.Group { Name = adGroup, Provider = "LDAP" });
            await db.SaveChangesAsync();
        }

        var browserSignIn = await browser.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, "test-password"));
        Assert.Equal(HttpStatusCode.OK, browserSignIn.StatusCode);
        var previousSession = await browserSignIn.Content.ReadFromJsonAsync<LoginResponse>(Json);
        Assert.NotNull(previousSession);
        Assert.True((await AuthGet(browser, previousSession.Token, "/api/studio/session")).IsSuccessStatusCode);

        factory.MockLdap.AuthenticateFunc = (_, _) => new LdapUserResult
        {
            Username = username,
            Groups = []
        };
        var (verifier, challenge) = CreatePkcePair();
        const string redirectUri = "http://127.0.0.1:45204/callback";
        var callback = await CompleteDedicatedBrowserFlowAsync(browser, username, "test-password",
            NativeClientRegistry.VsCodeClientId, redirectUri, challenge, "state-ldap-sync-123456");
        var response = await browser.PostAsJsonAsync("/api/auth/client/exchange", new NativeClientExchangeRequest(
            ParseQuery(callback)["code"], verifier, redirectUri, NativeClientRegistry.VsCodeClientId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var issued = await response.Content.ReadFromJsonAsync<NativeClientTokenResponse>(Json);
        Assert.NotNull(issued);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(issued.Token);
        Assert.DoesNotContain(jwt.Claims, claim => claim.Value == "Publisher");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var userId = await db.Users.Where(user => user.UserName == username).Select(user => user.Id).SingleAsync();
            Assert.False(await db.UserGroups.AnyAsync(membership => membership.UserId == userId));
        }

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await AuthGet(browser, previousSession.Token, "/api/studio/session")).StatusCode);
    }

    [Fact]
    public async Task Exchange_RejectsInvalidVerifierRedirectClientAndOrigin_AndRevokesOnCodeReplay()
    {
        using var factory = new PortalWebFactory();
        await MarkAdminReadyAsync(factory);
        var browser = NoRedirectClient(factory);
        var (verifier, challenge) = CreatePkcePair();
        const string redirectUri = "http://127.0.0.1:45200/callback";

        var callbackUrl = await CompleteDedicatedBrowserFlowAsync(
            browser, "admin", "Admin@12345!", NativeClientRegistry.VsCodeClientId, redirectUri, challenge, "state-exchange-checks-123");
        var code = ParseQuery(callbackUrl)["code"];

        // Wrong PKCE verifier
        var wrongVerifier = await browser.PostAsJsonAsync("/api/auth/client/exchange", new NativeClientExchangeRequest(
            code, verifier[..^1] + (verifier[^1] == 'A' ? 'B' : 'A'), redirectUri, NativeClientRegistry.VsCodeClientId));
        Assert.Equal(HttpStatusCode.BadRequest, wrongVerifier.StatusCode);

        // Wrong redirect_uri (different loopback port)
        var wrongRedirect = await browser.PostAsJsonAsync("/api/auth/client/exchange", new NativeClientExchangeRequest(
            code, verifier, "http://127.0.0.1:45201/callback", NativeClientRegistry.VsCodeClientId));
        Assert.Equal(HttpStatusCode.BadRequest, wrongRedirect.StatusCode);

        // Wrong client_id
        var wrongClient = await browser.PostAsJsonAsync("/api/auth/client/exchange", new NativeClientExchangeRequest(
            code, verifier, redirectUri, NativeClientRegistry.TuiClientId));
        Assert.Equal(HttpStatusCode.BadRequest, wrongClient.StatusCode);

        // Different Portal origin (cross-Portal replay attempt)
        using var otherPortalClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://other-portal.example")
        });
        var wrongOrigin = await otherPortalClient.PostAsJsonAsync("/api/auth/client/exchange", new NativeClientExchangeRequest(
            code, verifier, redirectUri, NativeClientRegistry.VsCodeClientId));
        Assert.Equal(HttpStatusCode.BadRequest, wrongOrigin.StatusCode);

        // Valid exchange succeeds once
        var okRes = await browser.PostAsJsonAsync("/api/auth/client/exchange", new NativeClientExchangeRequest(
            code, verifier, redirectUri, NativeClientRegistry.VsCodeClientId));
        Assert.Equal(HttpStatusCode.OK, okRes.StatusCode);
        var issued = await okRes.Content.ReadFromJsonAsync<NativeClientTokenResponse>(Json);
        Assert.NotNull(issued);

        // Session works before replay
        Assert.Equal(HttpStatusCode.OK, (await AuthGet(browser, issued!.Token, "/api/studio/session")).StatusCode);

        var replayFromWrongOrigin = await otherPortalClient.PostAsJsonAsync("/api/auth/client/exchange",
            new NativeClientExchangeRequest(code, verifier, redirectUri, NativeClientRegistry.VsCodeClientId));
        Assert.Equal(HttpStatusCode.BadRequest, replayFromWrongOrigin.StatusCode);
        var replayWithoutProof = await browser.PostAsJsonAsync("/api/auth/client/exchange",
            new NativeClientExchangeRequest(code, CreatePkcePair().Verifier, redirectUri, NativeClientRegistry.VsCodeClientId));
        Assert.Equal(HttpStatusCode.BadRequest, replayWithoutProof.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await AuthGet(browser, issued.Token, "/api/studio/session")).StatusCode);

        // Code replay is refused AND revokes the issued session + refresh family
        var replay = await browser.PostAsJsonAsync("/api/auth/client/exchange", new NativeClientExchangeRequest(
            code, verifier, redirectUri, NativeClientRegistry.VsCodeClientId));
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await AuthGet(browser, issued.Token, "/api/studio/session")).StatusCode);
        var refreshAfterReplay = await browser.PostAsJsonAsync("/api/auth/client/refresh", new NativeClientRefreshRequest(issued.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, refreshAfterReplay.StatusCode);
    }

    [Fact]
    public async Task NativeScopeAllowlist_EnforcedBeforeAndAfterRefresh_AndRefreshRoutesAreIsolated()
    {
        using var factory = new PortalWebFactory();
        await MarkAdminReadyAsync(factory);
        var browser = NoRedirectClient(factory);

        // Also obtain a browser session for the same admin user to test refresh isolation.
        var browserLogin = await browser.PostAsJsonAsync("/api/auth/login", new LoginRequest("admin", "Admin@12345!"));
        Assert.Equal(HttpStatusCode.OK, browserLogin.StatusCode);
        var browserTokens = await browserLogin.Content.ReadFromJsonAsync<LoginResponse>(Json);

        // Authorize a narrow studio.read-only native session for the Admin user.
        var native = await AcquireDedicatedNativeSessionAsync(
            browser, "admin", "Admin@12345!", NativeClientRegistry.VsCodeClientId, "http://127.0.0.1:45210/callback", "studio.read");

        async Task AssertScopeEnforcementAsync(string accessToken)
        {
            Assert.Equal(HttpStatusCode.OK, (await AuthGet(browser, accessToken, "/api/studio/session")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await AuthGet(browser, accessToken, "/api/studio/catalog")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await AuthGet(browser, accessToken, "/api/studio/pipelines/runnable")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await AuthGet(browser, accessToken, "/api/studio/pipelines/1/revision")).StatusCode);

            // Narrow scope blocks studio.run and studio.save
            var runDenied = await AuthPost(browser, accessToken, "/api/studio/pipelines/1/runs", new { version = 1 });
            Assert.Equal(HttpStatusCode.Forbidden, runDenied.StatusCode);
            Assert.Contains("native_client_scope_denied", await runDenied.Content.ReadAsStringAsync());

            var saveDenied = await AuthPost(browser, accessToken, "/api/designer/save", new { });
            Assert.Equal(HttpStatusCode.Forbidden, saveDenied.StatusCode);
            Assert.Contains("native_client_scope_denied", await saveDenied.Content.ReadAsStringAsync());

            // Admin routes, direct orchestrator assertion exchange, raw designer run, and browser APIs are always denied
            Assert.Equal(HttpStatusCode.Forbidden, (await AuthGet(browser, accessToken, "/api/admin/users")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await AuthPost(browser, accessToken, "/api/auth/orchestrator-assertion", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await AuthPost(browser, accessToken, "/api/designer/run", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await AuthGet(browser, accessToken, "/api/folders")).StatusCode);
        }

        await AssertScopeEnforcementAsync(native.Token);

        // Browser refresh route must reject a native refresh token; native refresh route must reject a browser refresh token.
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await browser.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(native.RefreshToken))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await browser.PostAsJsonAsync("/api/auth/client/refresh", new NativeClientRefreshRequest(browserTokens!.RefreshToken))).StatusCode);

        // Native refresh rotates token and preserves session ID and narrow scope ceiling.
        var refreshRes = await browser.PostAsJsonAsync("/api/auth/client/refresh", new NativeClientRefreshRequest(
            native.RefreshToken, NativeClientRegistry.VsCodeClientId));
        Assert.Equal(HttpStatusCode.OK, refreshRes.StatusCode);
        var refreshed = await refreshRes.Content.ReadFromJsonAsync<NativeClientTokenResponse>(Json);
        Assert.NotNull(refreshed);
        Assert.Equal(native.SessionId, refreshed!.SessionId);
        Assert.Equal(["studio.read"], refreshed.Scopes);
        Assert.NotEqual(native.RefreshToken, refreshed.RefreshToken);

        await AssertScopeEnforcementAsync(refreshed.Token);

        // Replaying the rotated native refresh token revokes ONLY this native session family, not the browser session.
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await browser.PostAsJsonAsync("/api/auth/client/refresh", new NativeClientRefreshRequest(native.RefreshToken))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await browser.PostAsJsonAsync("/api/auth/client/refresh", new NativeClientRefreshRequest(refreshed.RefreshToken))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await AuthGet(browser, refreshed.Token, "/api/studio/session")).StatusCode);

        // Browser session remains completely usable.
        Assert.Equal(HttpStatusCode.OK, (await AuthGet(browser, browserTokens.Token, "/api/folders")).StatusCode);
    }

    [Fact]
    public async Task SingleSessionNativeLogout_IsolatesBrowserAndOtherNativeSessions_AndIsIdempotent()
    {
        using var factory = new PortalWebFactory();
        await MarkAdminReadyAsync(factory);
        var client = NoRedirectClient(factory);

        var browserLogin = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("admin", "Admin@12345!"));
        var browserSession = await browserLogin.Content.ReadFromJsonAsync<LoginResponse>(Json);

        var nativeOne = await AcquireDedicatedNativeSessionAsync(
            NoRedirectClient(factory), "admin", "Admin@12345!", NativeClientRegistry.VsCodeClientId, "http://127.0.0.1:45220/callback");
        var nativeTwo = await AcquireDedicatedNativeSessionAsync(
            NoRedirectClient(factory), "admin", "Admin@12345!", NativeClientRegistry.TuiClientId, "http://127.0.0.1:45221/callback");

        // Browser token cannot call native logout.
        var browserLogoutNative = await AuthPost(client, browserSession!.Token, "/api/auth/client/logout", new NativeClientLogoutRequest());
        Assert.Equal(HttpStatusCode.Forbidden, browserLogoutNative.StatusCode);

        // Logout native session #1.
        var logoutOne = await AuthPost(client, nativeOne.Token, "/api/auth/client/logout", new NativeClientLogoutRequest(nativeOne.RefreshToken));
        Assert.Equal(HttpStatusCode.NoContent, logoutOne.StatusCode);

        // Repeating logout with the revoked session's refresh token is safe/idempotent.
        var repeatLogout = await client.PostAsJsonAsync("/api/auth/client/logout", new NativeClientLogoutRequest(nativeOne.RefreshToken));
        Assert.Equal(HttpStatusCode.NoContent, repeatLogout.StatusCode);

        // Native session #1 access and refresh are dead.
        Assert.Equal(HttpStatusCode.Unauthorized, (await AuthGet(client, nativeOne.Token, "/api/studio/session")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/client/refresh", new NativeClientRefreshRequest(nativeOne.RefreshToken))).StatusCode);

        // Browser session and native session #2 remain fully usable.
        Assert.Equal(HttpStatusCode.OK, (await AuthGet(client, browserSession.Token, "/api/folders")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await AuthGet(client, nativeTwo.Token, "/api/studio/session")).StatusCode);

        var nativeTwoRefresh = await client.PostAsJsonAsync("/api/auth/client/refresh", new NativeClientRefreshRequest(nativeTwo.RefreshToken));
        Assert.Equal(HttpStatusCode.OK, nativeTwoRefresh.StatusCode);
        var nativeTwoRefreshed = await nativeTwoRefresh.Content.ReadFromJsonAsync<NativeClientTokenResponse>(Json);
        Assert.Equal(HttpStatusCode.OK, (await AuthGet(client, nativeTwoRefreshed!.Token, "/api/studio/session")).StatusCode);

        // Browser all-device logout revokes both browser and remaining native sessions.
        var allDeviceLogout = await AuthPost(client, browserSession.Token, "/api/auth/logout", new RefreshRequest(browserSession.RefreshToken));
        Assert.Equal(HttpStatusCode.NoContent, allDeviceLogout.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await AuthGet(client, browserSession.Token, "/api/folders")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await AuthGet(client, nativeTwoRefreshed.Token, "/api/studio/session")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/client/refresh", new NativeClientRefreshRequest(nativeTwoRefreshed.RefreshToken))).StatusCode);
    }

    [Fact]
    public async Task SharedMultiTenantOidc_BindsCodeRefreshAndAccessTokenToVerifiedTenant()
    {
        using var factory = new SharedNativeOidcPortalFactory();
        await factory.SeedAsync();

        var alpha = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://alpha.portal.test")
        });
        var beta = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://beta.portal.test")
        });

        var (verifier, challenge) = CreatePkcePair();
        const string redirectUri = "http://127.0.0.1:45230/callback";
        const string state = "state-shared-tenant-123456";

        // Start native authorize on alpha.portal.test -> OIDC -> callback -> consent -> approve
        var authorize = await alpha.GetAsync(BuildAuthorizeUrl(
            NativeClientRegistry.VsCodeClientId, redirectUri, challenge, state, "studio.read"));
        Assert.Equal(HttpStatusCode.Redirect, authorize.StatusCode);
        Assert.StartsWith("/api/auth/oidc/login?native_tx=", authorize.Headers.Location!.ToString(), StringComparison.Ordinal);

        var oidcLogin = await alpha.GetAsync(authorize.Headers.Location);
        Assert.Equal(HttpStatusCode.Redirect, oidcLogin.StatusCode);

        var oidcCallback = await alpha.GetAsync("/api/auth/oidc/callback?code=idp-code&state=shared-state");
        Assert.Equal(HttpStatusCode.Redirect, oidcCallback.StatusCode);
        Assert.StartsWith("/api/auth/client/consent?tx=", oidcCallback.Headers.Location!.ToString(), StringComparison.Ordinal);

        var tx = ParseQuery(oidcCallback.Headers.Location.ToString())["tx"];
        var consentPage = await alpha.GetAsync(oidcCallback.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, consentPage.StatusCode);
        var csrf = ExtractCsrfToken(await consentPage.Content.ReadAsStringAsync());

        var consentSubmit = await alpha.PostAsync("/api/auth/client/consent", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["tx"] = tx,
            ["csrf_token"] = csrf,
            ["decision"] = "approve"
        }));
        Assert.Equal(HttpStatusCode.Redirect, consentSubmit.StatusCode);
        var code = ParseQuery(consentSubmit.Headers.Location!.ToString())["code"];

        // OIDC native login must NOT mint a browser refresh token.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            Assert.Empty(await db.RefreshTokens.ToListAsync());
        }

        // Cross-tenant exchange on beta.portal.test must fail.
        var crossTenantExchange = await beta.PostAsJsonAsync("/api/auth/client/exchange", new NativeClientExchangeRequest(
            code, verifier, redirectUri, NativeClientRegistry.VsCodeClientId));
        Assert.Equal(HttpStatusCode.BadRequest, crossTenantExchange.StatusCode);

        // Exchange on alpha.portal.test succeeds and binds tenant-alpha.
        var exchangeRes = await alpha.PostAsJsonAsync("/api/auth/client/exchange", new NativeClientExchangeRequest(
            code, verifier, redirectUri, NativeClientRegistry.VsCodeClientId));
        Assert.Equal(HttpStatusCode.OK, exchangeRes.StatusCode);
        var session = await exchangeRes.Content.ReadFromJsonAsync<NativeClientTokenResponse>(Json);
        Assert.NotNull(session);
        Assert.Equal("tenant-alpha", session!.TenantId);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(session.Token);
        Assert.Equal("tenant-alpha", jwt.Claims.Single(c => c.Type == TokenService.TenantClaim).Value);
        Assert.Equal(HttpStatusCode.OK, (await AuthGet(alpha, session.Token, "/api/studio/session")).StatusCode);

        var crossTenantReplay = await beta.PostAsJsonAsync("/api/auth/client/exchange", new NativeClientExchangeRequest(
            code, verifier, redirectUri, NativeClientRegistry.VsCodeClientId));
        Assert.Equal(HttpStatusCode.BadRequest, crossTenantReplay.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await AuthGet(alpha, session.Token, "/api/studio/session")).StatusCode);

        // Cross-tenant refresh on beta.portal.test must fail.
        var crossTenantRefresh = await beta.PostAsJsonAsync("/api/auth/client/refresh", new NativeClientRefreshRequest(session.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, crossTenantRefresh.StatusCode);

        // Forged native JWT with tenant-beta must fail validation.
        PortalUser alphaUser;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            alphaUser = await db.Users.SingleAsync(u => u.TenantId == "tenant-alpha" && u.ExternalSubject == "equal-subject");
        }
        var forgedWrongTenantJwt = new TokenService(factory.Services.GetRequiredService<PortalConfig>())
            .GenerateNativeClientJwt(
                alphaUser,
                [],
                session.SessionId,
                NativeClientRegistry.VsCodeClientId,
                [NativeClientScopes.StudioRead],
                tenantContext: TenantContext.FromVerifiedCredential("tenant-beta"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await AuthGet(alpha, forgedWrongTenantJwt, "/api/studio/session")).StatusCode);

        // Refresh on alpha.portal.test succeeds and preserves tenant-alpha.
        var alphaRefresh = await alpha.PostAsJsonAsync("/api/auth/client/refresh", new NativeClientRefreshRequest(session.RefreshToken));
        Assert.Equal(HttpStatusCode.OK, alphaRefresh.StatusCode);
        var refreshed = await alphaRefresh.Content.ReadFromJsonAsync<NativeClientTokenResponse>(Json);
        Assert.Equal("tenant-alpha", refreshed!.TenantId);

        // A different Portal node can revoke this session without access to this process's memory.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            await db.NativeClientSessions.Where(s => s.Id == session.SessionId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.RevokedAt, DateTime.UtcNow));
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await AuthGet(alpha, session.Token, "/api/studio/session")).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentUseOfTheSameNativeCredentialRevokesItsSession(bool refresh)
    {
        using var factory = new PortalWebFactory();
        await MarkAdminReadyAsync(factory);
        using var browser = NoRedirectClient(factory);
        var (verifier, challenge) = CreatePkcePair();
        const string redirect = "http://127.0.0.1:45220/callback";
        var callback = await CompleteDedicatedBrowserFlowAsync(browser, "admin", "Admin@12345!",
            NativeClientRegistry.VsCodeClientId, redirect, challenge, "state-concurrent-reuse-123");
        var exchange = new NativeClientExchangeRequest(
            ParseQuery(callback)["code"], verifier, redirect, NativeClientRegistry.VsCodeClientId);
        var route = "/api/auth/client/exchange";
        object payload = exchange;
        if (refresh)
        {
            var first = await browser.PostAsJsonAsync(route, exchange);
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            var issued = await first.Content.ReadFromJsonAsync<NativeClientTokenResponse>(Json);
            Assert.NotNull(issued);
            route = "/api/auth/client/refresh";
            payload = new NativeClientRefreshRequest(issued.RefreshToken);
        }

        var responses = await Task.WhenAll(browser.PostAsJsonAsync(route, payload), browser.PostAsJsonAsync(route, payload));
        var winner = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        var loser = Assert.Single(responses, r => r.StatusCode != HttpStatusCode.OK);
        Assert.Equal(refresh ? HttpStatusCode.Unauthorized : HttpStatusCode.BadRequest, loser.StatusCode);
        var session = await winner.Content.ReadFromJsonAsync<NativeClientTokenResponse>(Json);
        Assert.NotNull(session);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await AuthGet(browser, session.Token, "/api/studio/session")).StatusCode);
    }

    private static HttpClient NoRedirectClient(PortalWebFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task MarkAdminReadyAsync(PortalWebFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var admin = await db.Users.SingleAsync(u => u.UserName == "admin");
        admin.MustChangePassword = false;
        await db.SaveChangesAsync();
    }

    private static (string Verifier, string Challenge) CreatePkcePair()
    {
        var verifier = NativeClientRegistry.GenerateOpaqueToken(32);
        var challenge = NativeClientRegistry.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return (verifier, challenge);
    }

    private static string BuildAuthorizeUrl(
        string clientId,
        string redirectUri,
        string challenge,
        string state,
        string? scope = null)
    {
        var url = $"/api/auth/client/authorize?client_id={Uri.EscapeDataString(clientId)}" +
                  $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                  $"&code_challenge={Uri.EscapeDataString(challenge)}" +
                  $"&code_challenge_method=S256" +
                  $"&state={Uri.EscapeDataString(state)}";
        if (!string.IsNullOrEmpty(scope))
            url += $"&scope={Uri.EscapeDataString(scope)}";
        return url;
    }

    private static async Task<string> CompleteDedicatedBrowserFlowAsync(
        HttpClient browser,
        string username,
        string password,
        string clientId,
        string redirectUri,
        string challenge,
        string state,
        string? scope = null,
        string decision = "approve")
    {
        var authorize = await browser.GetAsync(BuildAuthorizeUrl(clientId, redirectUri, challenge, state, scope));
        Assert.Equal(HttpStatusCode.Redirect, authorize.StatusCode);

        var loginPage = await browser.GetAsync(authorize.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, loginPage.StatusCode);
        var loginHtml = await loginPage.Content.ReadAsStringAsync();
        var tx = ParseQuery(authorize.Headers.Location!.ToString())["tx"];
        var loginCsrf = ExtractCsrfToken(loginHtml);

        var loginSubmit = await browser.PostAsync("/api/auth/client/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["tx"] = tx,
            ["csrf_token"] = loginCsrf,
            ["username"] = username,
            ["password"] = password
        }));
        Assert.Equal(HttpStatusCode.Redirect, loginSubmit.StatusCode);

        var consentPage = await browser.GetAsync(loginSubmit.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, consentPage.StatusCode);
        var consentHtml = await consentPage.Content.ReadAsStringAsync();
        var consentCsrf = ExtractCsrfToken(consentHtml);

        var consentSubmit = await browser.PostAsync("/api/auth/client/consent", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["tx"] = tx,
            ["csrf_token"] = consentCsrf,
            ["decision"] = decision
        }));
        Assert.Equal(HttpStatusCode.Redirect, consentSubmit.StatusCode);
        return consentSubmit.Headers.Location!.ToString();
    }

    private static async Task<NativeClientTokenResponse> AcquireDedicatedNativeSessionAsync(
        HttpClient browser,
        string username,
        string password,
        string clientId,
        string redirectUri,
        string? scope = null)
    {
        var (verifier, challenge) = CreatePkcePair();
        var state = "state-" + Guid.NewGuid().ToString("N")[..12];
        var callbackUrl = await CompleteDedicatedBrowserFlowAsync(
            browser, username, password, clientId, redirectUri, challenge, state, scope);
        var code = ParseQuery(callbackUrl)["code"];
        var exchangeRes = await browser.PostAsJsonAsync("/api/auth/client/exchange", new NativeClientExchangeRequest(
            code, verifier, redirectUri, clientId));
        Assert.Equal(HttpStatusCode.OK, exchangeRes.StatusCode);
        return (await exchangeRes.Content.ReadFromJsonAsync<NativeClientTokenResponse>(Json))!;
    }

    private static string ExtractCsrfToken(string html)
    {
        var match = CsrfInputRegex().Match(html);
        Assert.True(match.Success, "Expected hidden csrf_token input in HTML form.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static Dictionary<string, string> ParseQuery(string urlOrRelative)
    {
        var idx = urlOrRelative.IndexOf('?');
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (idx < 0) return result;
        foreach (var pair in urlOrRelative[(idx + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq < 0) continue;
            result[Uri.UnescapeDataString(pair[..eq])] = Uri.UnescapeDataString(pair[(eq + 1)..]);
        }
        return result;
    }

    private static Task<HttpResponseMessage> AuthGet(HttpClient client, string token, string url)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(req);
    }

    private static Task<HttpResponseMessage> AuthPost(HttpClient client, string token, string url, object body)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(req);
    }

    public sealed class SharedNativeOidcPortalFactory : PortalWebFactory
    {
        private bool _seeded;

        protected override void CustomizeConfiguration(Dictionary<string, string?> settings)
        {
            settings["Portal:SharedTenancy:Enabled"] = "true";
            settings["Portal:Identity:Oidc:Enabled"] = "false";
        }

        protected override void CustomizePortalConfig(PortalConfig config)
        {
            config.SharedTenancy.Enabled = true;
            config.Identity.Oidc.Enabled = false;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISharedOidcAuthenticationService>();
                services.AddSingleton<ISharedOidcAuthenticationService>(new StubSharedOidcService());
            });
        }

        public async Task SeedAsync()
        {
            if (_seeded) return;
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            db.SharedIdentityAuthorities.AddRange(
                Authority("alpha", "tenant-alpha", "alpha.portal.test", "https://alpha.idp.test"),
                Authority("beta", "tenant-beta", "beta.portal.test", "https://beta.idp.test"));
            db.Groups.AddRange(
                new ETL_SQL.Portal.Data.Group { TenantId = "tenant-alpha", Name = "Analysts", Provider = OidcUserProvisioningService.ProviderName },
                new ETL_SQL.Portal.Data.Group { TenantId = "tenant-beta", Name = "Analysts", Provider = OidcUserProvisioningService.ProviderName });
            await db.SaveChangesAsync();
            _seeded = true;
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

        private sealed class StubSharedOidcService : ISharedOidcAuthenticationService
        {
            public Task<OidcAuthorizationRequest> BuildAuthorizationRequestAsync(
                SharedIdentityAuthorityBinding authority,
                string redirectUri,
                CancellationToken ct = default) =>
                Task.FromResult(new OidcAuthorizationRequest(
                    $"{authority.Issuer}/authorize?state=shared-state",
                    "shared-state",
                    "shared-nonce",
                    "shared-verifier"));

            public Task<OidcIdentity> CompleteAsync(
                SharedIdentityAuthorityBinding authority,
                string code,
                string codeVerifier,
                string redirectUri,
                string expectedNonce,
                CancellationToken ct = default) =>
                Task.FromResult(new OidcIdentity(
                    "equal-subject",
                    "equal-user",
                    $"equal@{authority.LoginDomain}",
                    ["Analysts"],
                    authority.Issuer));
        }
    }
}
