using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;
using ETL_SQL.Orchestrator.Service;
using ETL_SQL.Orchestrator.Storage;
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
    public async Task OidcPublisher_MissingOrganizationDatasetClassificationIsRejectedBeforeCatalogMutation()
    {
        var username = "oidcpub_" + Guid.NewGuid().ToString("N")[..8];
        _factory.Stub.Identity = new OidcIdentity("sub-" + username, username, $"{username}@example.com", []);

        var bootstrapClient = NoRedirectClient();
        await bootstrapClient.GetAsync("/api/auth/oidc/login");
        var bootstrapCallback = await bootstrapClient.GetAsync(
            $"/api/auth/oidc/callback?code=bootstrap&state={StubOidcAuthenticationService.State}");
        Assert.Equal(HttpStatusCode.OK, bootstrapCallback.StatusCode);

        int folderId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var user = await db.Users.SingleAsync(candidate => candidate.UserName == username);
            var publisherRole = await db.Roles.SingleAsync(role => role.Name == "Publisher");
            db.UserRoles.Add(new Microsoft.AspNetCore.Identity.IdentityUserRole<int>
            { UserId = user.Id, RoleId = publisherRole.Id });
            var folder = new Folder { Name = "OIDC governed", Path = "/OIDC-governed-" + username, OwnerId = user.Id };
            db.Folders.Add(folder);
            await db.SaveChangesAsync();
            folderId = folder.Id;

            var authority = scope.ServiceProvider.GetRequiredService<PolicyAuthorityService>();
            await authority.PublishAsync(new OrganizationPolicyDocument
            {
                Metadata = new MetadataGovernancePolicySection
                {
                    RequiredTags = [new OrganizationRequiredTagRule { Tag = "@classification", Scopes = ["DATASET"] }]
                }
            }, "default", "default", "oidc-metadata-v1", "security", "data-office",
                DateTimeOffset.UtcNow.AddHours(1));
        }

        var scriptName = $"oidc_missing_classification_{Guid.NewGuid():N}.rptsql";
        await File.WriteAllTextAsync(Path.Combine(_factory.TempDir, "scripts", scriptName), """
            CREATE TABLE #customers (Email STRING);
            CREATE DATASET &customers AS (
              SELECT Email FROM #customers
            );
            """);

        var publisherClient = NoRedirectClient();
        await publisherClient.GetAsync("/api/auth/oidc/login");
        var publisherCallback = await publisherClient.GetAsync(
            $"/api/auth/oidc/callback?code=publisher&state={StubOidcAuthenticationService.State}");
        var (accessToken, _) = await ParseHandoffTokensAsync(publisherCallback);
        using var publishRequest = new HttpRequestMessage(HttpMethod.Post, "/api/reports");
        publishRequest.Headers.Authorization = new("Bearer", accessToken);
        publishRequest.Content = JsonContent.Create(new PublishReportRequest(
            folderId, "Governed customer report", scriptName, null));

        var response = await publisherClient.SendAsync(publishRequest);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("organization_metadata_policy", await response.Content.ReadAsStringAsync());
        using var verifyScope = _factory.Services.CreateScope();
        Assert.False(await verifyScope.ServiceProvider.GetRequiredService<PortalDbContext>().Reports
            .AnyAsync(report => report.Name == "Governed customer report"));
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
    public async Task Callback_UsernameChangeAtIdp_KeepsSameAccount()
    {
        // Federated accounts are keyed on the immutable subject, so an IdP username change updates the
        // existing account rather than creating a duplicate or detaching the user from their data.
        var subject = "stable-sub-" + Guid.NewGuid().ToString("N")[..8];
        var first = "oidcname1_" + Guid.NewGuid().ToString("N")[..6];
        var second = "oidcname2_" + Guid.NewGuid().ToString("N")[..6];

        _factory.Stub.Identity = new OidcIdentity(subject, first, null, []);
        var c1 = NoRedirectClient();
        await c1.GetAsync("/api/auth/oidc/login");
        await c1.GetAsync($"/api/auth/oidc/callback?code=c&state={StubOidcAuthenticationService.State}");

        int userId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var u = await db.Users.SingleAsync(x => x.ExternalSubject == subject);
            userId = u.Id;
            Assert.Equal(first, u.UserName);
        }

        _factory.Stub.Identity = new OidcIdentity(subject, second, null, []);
        var c2 = NoRedirectClient();
        await c2.GetAsync("/api/auth/oidc/login");
        await c2.GetAsync($"/api/auth/oidc/callback?code=c&state={StubOidcAuthenticationService.State}");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var users = await db.Users.Where(x => x.ExternalSubject == subject).ToListAsync();
            Assert.Single(users);              // no duplicate account
            Assert.Equal(userId, users[0].Id); // same identity
            Assert.Equal(second, users[0].UserName); // username adopted
        }
    }

    [Fact]
    public async Task Callback_RefusesWhenUsernameTakenByDifferentSubject()
    {
        var shared = "oidcshared_" + Guid.NewGuid().ToString("N")[..8];

        // Subject A provisions the username.
        _factory.Stub.Identity = new OidcIdentity("subA-" + shared, shared, null, []);
        var cA = NoRedirectClient();
        await cA.GetAsync("/api/auth/oidc/login");
        await cA.GetAsync($"/api/auth/oidc/callback?code=c&state={StubOidcAuthenticationService.State}");

        // Subject B claims the same username — must be refused (no takeover, no new account).
        _factory.Stub.Identity = new OidcIdentity("subB-" + shared, shared, null, []);
        var cB = NoRedirectClient();
        await cB.GetAsync("/api/auth/oidc/login");
        var callback = await cB.GetAsync($"/api/auth/oidc/callback?code=c&state={StubOidcAuthenticationService.State}");

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Contains("error=sso_failed", callback.Headers.Location!.ToString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        Assert.False(await db.Users.AnyAsync(x => x.ExternalSubject == "subB-" + shared));
        Assert.Equal("subA-" + shared, (await db.Users.SingleAsync(x => x.UserName == shared)).ExternalSubject);
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
    public async Task DynamicOidcGroupClaims_DriveOrchestratorAssertionGroupGrants_AndRevokeOnClaimLoss()
    {
        var username = "oidc_grant_user_" + Guid.NewGuid().ToString("N")[..8];
        var groupKey = PortalPrincipalKey.New();
        var jobId = "job_" + Guid.NewGuid().ToString("N")[..8];

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var group = new Group
            {
                Name = "data_engineers_" + username,
                Provider = "OIDC",
                AdGroup = "data_engineers",
                PrincipalKey = groupKey
            };
            db.Groups.Add(group);
            await db.SaveChangesAsync();
        }

        // Set up an Orchestrator authorization store with a GROUP grant targeting groupKey
        var dbPath = Path.Combine(Path.GetTempPath(), $"orch_oidc_acl_{Guid.NewGuid():N}.db");
        try
        {
            var grantStore = new SQLiteJobHistoryStore(dbPath);
            await grantStore.SaveObjectGrantAsync(new OrchestratorObjectGrant(
                jobId, OrchestratorObjectKind.Job, OrchestratorPrincipalKind.Group, groupKey,
                OrchestratorObjectPermission.Read, "admin:1"));
            var authorizer = new OrchestratorObjectAuthorizationService(grantStore);

            // First login: user presents claim ["data_engineers"]
            _factory.Stub.Identity = new OidcIdentity("sub-" + username, username, $"{username}@example.com", ["data_engineers"]);
            var client1 = NoRedirectClient();
            await client1.GetAsync("/api/auth/oidc/login");
            var callback1 = await client1.GetAsync($"/api/auth/oidc/callback?code=c&state={StubOidcAuthenticationService.State}");
            var (token1, _) = await ParseHandoffTokensAsync(callback1);

            // Exchange Portal session for Orchestrator assertion
            var exchangeReq1 = new HttpRequestMessage(HttpMethod.Post, "/api/auth/orchestrator-assertion");
            exchangeReq1.Headers.Authorization = new("Bearer", token1);
            var exchangeRes1 = await _factory.CreateClient().SendAsync(exchangeReq1);
            Assert.Equal(HttpStatusCode.OK, exchangeRes1.StatusCode);
            var assertionBody1 = await exchangeRes1.Content.ReadFromJsonAsync<JsonElement>();
            var assertion1 = assertionBody1.GetProperty("assertion").GetString()!;

            // Validate assertion has groupKey in caller.GroupIds
            Assert.True(OrchestratorIdentityAssertion.TryValidate(
                assertion1, "test-orchestrator-signing-secret-key-12345", out var caller1, out var err1), err1);
            Assert.NotNull(caller1);
            Assert.Contains(groupKey, caller1!.GroupIds);

            // Assert Orchestrator authorizer approves Read for caller1 via the GROUP grant
            Assert.True(await authorizer.CanAsync(
                caller1, OrchestratorObjectKind.Job, jobId, caller1.TenantId, OrchestratorObjectPermission.Read, "admin:1"));
            // Manage is NOT granted by Read grant
            Assert.False(await authorizer.CanAsync(
                caller1, OrchestratorObjectKind.Job, jobId, caller1.TenantId, OrchestratorObjectPermission.Manage, "admin:1"));

            // Second login: user no longer has "data_engineers" in claims
            _factory.Stub.Identity = new OidcIdentity("sub-" + username, username, $"{username}@example.com", []);
            var client2 = NoRedirectClient();
            await client2.GetAsync("/api/auth/oidc/login");
            var callback2 = await client2.GetAsync($"/api/auth/oidc/callback?code=c&state={StubOidcAuthenticationService.State}");
            var (token2, _) = await ParseHandoffTokensAsync(callback2);

            // Exchange second Portal session for Orchestrator assertion
            var exchangeReq2 = new HttpRequestMessage(HttpMethod.Post, "/api/auth/orchestrator-assertion");
            exchangeReq2.Headers.Authorization = new("Bearer", token2);
            var exchangeRes2 = await _factory.CreateClient().SendAsync(exchangeReq2);
            Assert.Equal(HttpStatusCode.OK, exchangeRes2.StatusCode);
            var assertionBody2 = await exchangeRes2.Content.ReadFromJsonAsync<JsonElement>();
            var assertion2 = assertionBody2.GetProperty("assertion").GetString()!;

            // Validate assertion now has EMPTY groupIds
            Assert.True(OrchestratorIdentityAssertion.TryValidate(
                assertion2, "test-orchestrator-signing-secret-key-12345", out var caller2, out var err2), err2);
            Assert.NotNull(caller2);
            Assert.DoesNotContain(groupKey, caller2!.GroupIds);

            // Orchestrator authorizer now DENIES Read for caller2
            Assert.False(await authorizer.CanAsync(
                caller2, OrchestratorObjectKind.Job, jobId, caller2.TenantId, OrchestratorObjectPermission.Read, "admin:1"));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var s in new[] { "", "-wal", "-shm" })
            {
                var p = dbPath + s;
                if (File.Exists(p)) try { File.Delete(p); } catch { }
            }
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

    // ── P2.1 operational diagnostics ──────────────────────────────────────────────

    [Fact]
    public async Task Diagnostics_ReturnsRedactedConfig_AndProbesDiscovery()
    {
        // Mint an isolated admin token (MustChangePassword=false) so the shared first-run admin —
        // which other tests log in as — is left untouched.
        string jwt;
        using (var scope = _factory.Services.CreateScope())
        {
            var userMgr = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<PortalUser>>();
            var tokens = scope.ServiceProvider.GetRequiredService<TokenService>();
            var admin = new PortalUser
            {
                UserName = "oidcdiag_" + Guid.NewGuid().ToString("N")[..8],
                Email = "diag@example.test",
                IsActive = true,
                MustChangePassword = false
            };
            Assert.True((await userMgr.CreateAsync(admin, "Diag@12345!")).Succeeded);
            await userMgr.AddToRoleAsync(admin, "Admin");
            jwt = tokens.GenerateJwt(admin, await userMgr.GetRolesAsync(admin));
        }

        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/auth/oidc/diagnostics");
        req.Headers.Authorization = new("Bearer", jwt);
        var res = await _factory.CreateClient().SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.DoesNotContain("test-client-secret", body); // secret is never returned
        using var json = System.Text.Json.JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.True(root.GetProperty("enabled").GetBoolean());
        Assert.True(root.GetProperty("clientSecretConfigured").GetBoolean());
        Assert.Equal("etl-portal", root.GetProperty("clientId").GetString());
        // The discovery probe ran; the fake authority is unreachable, so it reports reachable=false.
        Assert.False(root.GetProperty("discovery").GetProperty("reachable").GetBoolean());
    }

    [Fact]
    public async Task Diagnostics_RequiresAdmin()
    {
        var res = await NoRedirectClient().GetAsync("/api/auth/oidc/diagnostics");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    // ── P2.2 recovery: unavailable IdP, session revocation on claim change ─────────

    [Fact]
    public async Task Callback_WhenProviderUnavailable_RedirectsToError_AndAudits()
    {
        _factory.Stub.Identity = new OidcIdentity("sub-x", "oidc_unavail", null, []);
        _factory.Stub.ThrowOnComplete = true;
        try
        {
            var client = NoRedirectClient();
            await client.GetAsync("/api/auth/oidc/login");
            var callback = await client.GetAsync($"/api/auth/oidc/callback?code=c&state={StubOidcAuthenticationService.State}");

            Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
            Assert.Contains("error=sso_failed", callback.Headers.Location!.ToString());

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            Assert.True(await db.AuditLogs.AnyAsync(a =>
                a.Action == "LOGIN_FAILED" && a.Detail!.Contains("OIDC authentication failed")));
        }
        finally
        {
            _factory.Stub.ThrowOnComplete = false;
        }
    }

    [Fact]
    public async Task OldSession_IsRevoked_AfterGroupClaimChange()
    {
        var username = "oidcrevoke_" + Guid.NewGuid().ToString("N")[..8];
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            db.Groups.Add(new Group { Name = "Revoke_" + username, Provider = "OIDC", AdGroup = "revgrp" });
            await db.SaveChangesAsync();
        }

        // Login while claiming the group.
        _factory.Stub.Identity = new OidcIdentity("sub-" + username, username, null, ["revgrp"]);
        var c1 = NoRedirectClient();
        await c1.GetAsync("/api/auth/oidc/login");
        var cb = await c1.GetAsync($"/api/auth/oidc/callback?code=c&state={StubOidcAuthenticationService.State}");
        var (accessToken, refreshToken) = await ParseHandoffTokensAsync(cb);

        // The session is live (a protected endpoint authenticates).
        Assert.NotEqual(HttpStatusCode.Unauthorized, (await GetWithBearerAsync("/api/folders", accessToken)).StatusCode);

        // A later login with the group claim dropped rotates the security stamp + revokes tokens.
        _factory.Stub.Identity = new OidcIdentity("sub-" + username, username, null, []);
        var c2 = NoRedirectClient();
        await c2.GetAsync("/api/auth/oidc/login");
        await c2.GetAsync($"/api/auth/oidc/callback?code=c&state={StubOidcAuthenticationService.State}");

        // No privilege retention: the old access token and refresh token are both rejected.
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetWithBearerAsync("/api/folders", accessToken)).StatusCode);
        var refreshRes = await _factory.CreateClient().PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(refreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, refreshRes.StatusCode);
    }

    private async Task<HttpResponseMessage> GetWithBearerAsync(string path, string token)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.Authorization = new("Bearer", token);
        return await _factory.CreateClient().SendAsync(req);
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
            config.Orchestrator.IdentitySigningSecret = "test-orchestrator-signing-secret-key-12345";
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IOidcAuthenticationService>();
                services.AddSingleton<IOidcAuthenticationService>(Stub);
                services.RemoveAll<IPolicyEnvelopeSigner>();
                services.AddSingleton<IPolicyEnvelopeSigner>(new RsaPolicyEnvelopeSigner(RSA.Create(2048)));
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

        /// <summary>When set, CompleteAsync throws — simulates an unavailable IdP / failed token exchange.</summary>
        public bool ThrowOnComplete { get; set; }

        public bool Enabled => true;

        public Task<OidcAuthorizationRequest> BuildAuthorizationRequestAsync(string redirectUri, CancellationToken ct = default) =>
            Task.FromResult(new OidcAuthorizationRequest(AuthorizationUrl, State, Nonce, Verifier));

        public Task<OidcIdentity> CompleteAsync(
            string code, string codeVerifier, string redirectUri, string expectedNonce, CancellationToken ct = default)
        {
            if (ThrowOnComplete)
                throw new OidcAuthenticationException("Identity provider is unavailable.");
            Assert.Equal(Nonce, expectedNonce);   // controller must pass the flow nonce through
            Assert.Equal(Verifier, codeVerifier); // and the PKCE verifier from the flow cookie
            return Task.FromResult(Identity);
        }
    }
}
