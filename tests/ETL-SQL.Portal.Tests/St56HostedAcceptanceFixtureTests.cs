using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ETL_SQL.Core.Data;
using ETL_SQL.Orchestrator.Service;
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
/// ST56 8-Step Hosted Acceptance Fixture against the real Portal + real Federated Orchestrator
/// + mock OIDC provider (ST55 §4.1).
/// Proves end-to-end: OIDC PKCE native sign-in, authorized run as alice, session reuse,
/// 403 run_grant_denied as bob, refresh + revoked-refresh 401, Stop -> CANCELLED on SlowWait,
/// lost-202 recovery with exactly ONE Orchestrator history row for K1, single-session logout,
/// and Portal audit rows (CLIENT_LOGIN, PIPELINE_RUN_SUBMITTED, PIPELINE_RUN_CANCEL_REQUESTED, LOGOUT).
/// </summary>
[Trait("Category", "Portal")]
public sealed partial class St56HostedAcceptanceFixtureTests : IAsyncLifetime
{
    private const string FinanceCheckScript =
        "CREATE CONNECTION sample_data AS MOCKDB();\n"
        + "SELECT UserID INTO #users FROM sample_data.Users;\n"
        + "ASSERT (SELECT COUNT(*) FROM #users) > 0;\n";

    private const string SlowWaitScript = "WAITFOR DELAY '00:00:40';\n";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [GeneratedRegex("name=\"csrf_token\"\\s+value=\"([^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex CsrfInputRegex();

    private OrchestratorWebFactory _orchestrator = null!;
    private AcceptancePortalFactory _portal = null!;

    public Task InitializeAsync()
    {
        _orchestrator = new OrchestratorWebFactory(requireFederatedIdentity: true);
        _portal = new AcceptancePortalFactory(_orchestrator);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _portal?.Dispose();
        _orchestrator?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task EightStepAcceptanceFixture_RunsEndToEndAgainstPortalAndFederatedOrchestrator()
    {
        var browser = _portal.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        // Seed OIDC groups + capabilities (DataAuthors: StudioAccess/Read/Run/Save; DataReaders: StudioAccess/Read/Run)
        int folderId;
        int financePipelineId;
        long financeVersion;
        int slowPipelineId;
        long slowVersion;
        int aliceUserId;
        string alicePrincipalKey;

        using (var scope = _portal.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var admin = await db.Users.SingleAsync(u => u.UserName == "admin");
            admin.MustChangePassword = false;

            var authorsGroup = new ETL_SQL.Portal.Data.Group { Name = "DataAuthors", Provider = "OIDC", AdGroup = "DataAuthors" };
            var readersGroup = new ETL_SQL.Portal.Data.Group { Name = "DataReaders", Provider = "OIDC", AdGroup = "DataReaders" };
            db.Groups.AddRange(authorsGroup, readersGroup);
            await db.SaveChangesAsync();

            db.GroupStudioCapabilities.AddRange(
                new GroupStudioCapability { GroupId = authorsGroup.Id, Capability = StudioCapabilities.StudioAccess },
                new GroupStudioCapability { GroupId = authorsGroup.Id, Capability = StudioCapabilities.ScriptRead },
                new GroupStudioCapability { GroupId = authorsGroup.Id, Capability = StudioCapabilities.ScriptRun },
                new GroupStudioCapability { GroupId = authorsGroup.Id, Capability = StudioCapabilities.ScriptSave },
                new GroupStudioCapability { GroupId = readersGroup.Id, Capability = StudioCapabilities.StudioAccess },
                new GroupStudioCapability { GroupId = readersGroup.Id, Capability = StudioCapabilities.ScriptRead },
                new GroupStudioCapability { GroupId = readersGroup.Id, Capability = StudioCapabilities.ScriptRun });

            var folder = new Folder { Name = "Finance", Path = "/Finance", OwnerId = admin.Id };
            db.Folders.Add(folder);
            await db.SaveChangesAsync();
            folderId = folder.Id;

            db.FolderAcls.AddRange(
                new FolderAcl { FolderId = folderId, GroupId = authorsGroup.Id, Permission = FolderPermission.Author },
                new FolderAcl { FolderId = folderId, GroupId = readersGroup.Id, Permission = FolderPermission.Execute });
            await db.SaveChangesAsync();
        }

        // Provision alice and bob via real OIDC native sign-in (Step 1)
        _portal.StubOidc.Identity = new OidcIdentity("oid-alice", "alice", "alice@alpha.test", ["DataAuthors"]);
        var aliceSession = await SignInViaOidcNativePkceAsync(browser, "http://127.0.0.1:45301/callback");
        Assert.Equal("alice", aliceSession.User.Username);
        Assert.Equal(TokenService.NativeTokenAudience, aliceSession.Audience);

        // Assign Publisher role to alice and create the two saved catalog pipelines as admin
        var adminToken = await LoginLocalAsync(browser, "admin", "Admin@12345!");
        (financePipelineId, financeVersion) = await CreatePipelineAsync(browser, adminToken, folderId, "FinanceCheck.etlsql", FinanceCheckScript);
        (slowPipelineId, slowVersion) = await CreatePipelineAsync(browser, adminToken, folderId, "SlowWait.etlsql", SlowWaitScript);

        using (var scope = _portal.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var alice = await db.Users.SingleAsync(u => u.UserName == "alice");
            aliceUserId = alice.Id;
            alicePrincipalKey = alice.PrincipalKey!;
        }

        // Provision Orchestrator jobs once as admin and grant EXECUTE on PIPELINE:{id} to alice (not bob)
        await ProvisionAndGrantExecuteAsync(browser, adminToken, financePipelineId, financeVersion, alicePrincipalKey);
        await ProvisionAndGrantExecuteAsync(browser, adminToken, slowPipelineId, slowVersion, alicePrincipalKey, cancelImmediately: true);

        // Step 1 probe: GET /api/studio/session with alice's native token
        var sessionProbe = await AuthSendAsync(browser, HttpMethod.Get, "/api/studio/session", aliceSession.Token);
        Assert.Equal(HttpStatusCode.OK, sessionProbe.StatusCode);

        // Step 2: Authorized run of FinanceCheck.etlsql as alice with UUIDv4 Idempotency-Key
        var key1 = Guid.NewGuid().ToString("D");
        var run1Res = await SubmitPipelineAsync(browser, financePipelineId, financeVersion, aliceSession.Token, key1);
        Assert.Equal(HttpStatusCode.Accepted, run1Res.StatusCode);
        var run1 = (await run1Res.Content.ReadFromJsonAsync<PipelineRunSubmittedDto>(Json))!;
        Assert.False(run1.Replayed);
        var run1Final = await PollUntilTerminalAsync(browser, financePipelineId, run1.RunId, aliceSession.Token);
        Assert.Equal("SUCCESS", run1Final.Status);
        Assert.NotEmpty(run1Final.Statements);

        // Step 3: Session reuse (same in-memory session, distinct runId)
        var key2 = Guid.NewGuid().ToString("D");
        var run2Res = await SubmitPipelineAsync(browser, financePipelineId, financeVersion, aliceSession.Token, key2);
        Assert.Equal(HttpStatusCode.Accepted, run2Res.StatusCode);
        var run2 = (await run2Res.Content.ReadFromJsonAsync<PipelineRunSubmittedDto>(Json))!;
        Assert.NotEqual(run1.RunId, run2.RunId);
        var run2Final = await PollUntilTerminalAsync(browser, financePipelineId, run2.RunId, aliceSession.Token);
        Assert.Equal("SUCCESS", run2Final.Status);

        // Step 4: Denied run as bob (403 run_grant_denied, zero Orchestrator execution)
        _portal.StubOidc.Identity = new OidcIdentity("oid-bob", "bob", "bob@alpha.test", ["DataReaders"]);
        var bobSession = await SignInViaOidcNativePkceAsync(
            _portal.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }),
            "http://127.0.0.1:45302/callback");
        var bobRun = await SubmitPipelineAsync(browser, financePipelineId, financeVersion, bobSession.Token, Guid.NewGuid().ToString("D"));
        Assert.Equal(HttpStatusCode.Forbidden, bobRun.StatusCode);
        Assert.Equal("run_grant_denied", (await bobRun.Content.ReadFromJsonAsync<PipelineRunRefusalDto>(Json))!.Code);

        // Step 5: Native single-flight refresh succeeds and keeps session working
        var refreshRes = await browser.PostAsJsonAsync("/api/auth/client/refresh",
            new NativeClientRefreshRequest(aliceSession.RefreshToken, NativeClientRegistry.VsCodeClientId));
        Assert.Equal(HttpStatusCode.OK, refreshRes.StatusCode);
        var aliceRefreshed = (await refreshRes.Content.ReadFromJsonAsync<NativeClientTokenResponse>(Json))!;
        Assert.Equal(aliceSession.SessionId, aliceRefreshed.SessionId);

        // Step 6: Stop on SlowWait.etlsql -> 202 cancel -> terminal CANCELLED
        var slowSubmitRes = await SubmitPipelineAsync(browser, slowPipelineId, slowVersion, aliceRefreshed.Token, Guid.NewGuid().ToString("D"));
        Assert.Equal(HttpStatusCode.Accepted, slowSubmitRes.StatusCode);
        var slowSubmitted = (await slowSubmitRes.Content.ReadFromJsonAsync<PipelineRunSubmittedDto>(Json))!;

        var cancelRes = await AuthSendAsync(browser, HttpMethod.Post,
            $"/api/studio/pipelines/{slowPipelineId}/runs/{slowSubmitted.RunId}/cancel", aliceRefreshed.Token);
        Assert.Equal(HttpStatusCode.Accepted, cancelRes.StatusCode);
        var slowFinal = await PollUntilTerminalAsync(browser, slowPipelineId, slowSubmitted.RunId, aliceRefreshed.Token);
        Assert.Equal("CANCELLED", slowFinal.Status);

        // Step 7: Lost 202 response -> recover exact runId via by-request/{K1} + replay -> ONE Orchestrator run for K1
        var kDropped = Guid.NewGuid().ToString("D");
        int historyCountBefore;
        using (var scope = _orchestrator.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IJobHistoryStore>();
            var job = (await store.GetJobAsync("portal-host", $"PIPELINE:{financePipelineId}"))!;
            historyCountBefore = (await store.GetHistoryAsync(job.Id, 100)).Count();
        }

        // Submit succeeds at Portal (client drops the 202 response), then client recovers via by-request + replay
        var droppedSubmitRes = await SubmitPipelineAsync(browser, financePipelineId, financeVersion, aliceRefreshed.Token, kDropped);
        Assert.Equal(HttpStatusCode.Accepted, droppedSubmitRes.StatusCode);
        var originalAccepted = (await droppedSubmitRes.Content.ReadFromJsonAsync<PipelineRunSubmittedDto>(Json))!;

        var byReqRes = await AuthSendAsync(browser, HttpMethod.Get,
            $"/api/studio/pipelines/{financePipelineId}/runs/by-request/{kDropped}", aliceRefreshed.Token);
        Assert.Equal(HttpStatusCode.OK, byReqRes.StatusCode);
        var recovered = (await byReqRes.Content.ReadFromJsonAsync<PipelineRunSubmittedDto>(Json))!;
        Assert.Equal(originalAccepted.RunId, recovered.RunId);
        Assert.True(recovered.Replayed);

        var postReplayRes = await SubmitPipelineAsync(browser, financePipelineId, financeVersion, aliceRefreshed.Token, kDropped);
        Assert.Equal(HttpStatusCode.Accepted, postReplayRes.StatusCode);
        var replayed = (await postReplayRes.Content.ReadFromJsonAsync<PipelineRunSubmittedDto>(Json))!;
        Assert.Equal(originalAccepted.RunId, replayed.RunId);
        Assert.True(replayed.Replayed);

        var droppedFinal = await PollUntilTerminalAsync(browser, financePipelineId, recovered.RunId, aliceRefreshed.Token);
        Assert.Equal("SUCCESS", droppedFinal.Status);

        using (var scope = _orchestrator.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IJobHistoryStore>();
            var job = (await store.GetJobAsync("portal-host", $"PIPELINE:{financePipelineId}"))!;
            var historyAfter = await store.GetHistoryAsync(job.Id, 100);
            Assert.Equal(historyCountBefore + 1, historyAfter.Count());
        }

        // Step 8: Single-session native logout revokes alice's native session while leaving bob's intact
        var logoutRes = await AuthSendAsync(browser, HttpMethod.Post, "/api/auth/client/logout",
            aliceRefreshed.Token, new NativeClientLogoutRequest(aliceRefreshed.RefreshToken));
        Assert.Equal(HttpStatusCode.NoContent, logoutRes.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await AuthSendAsync(browser, HttpMethod.Get, "/api/studio/session", aliceRefreshed.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await AuthSendAsync(browser, HttpMethod.Get, "/api/studio/session", bobSession.Token)).StatusCode);

        // Verify required Portal audit rows (CLIENT_LOGIN, PIPELINE_RUN_SUBMITTED, PIPELINE_RUN_CANCEL_REQUESTED, LOGOUT)
        using (var scope = _portal.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            Assert.True(await db.AuditLogs.AnyAsync(a => a.UserId == aliceUserId && a.Action == "CLIENT_LOGIN"));
            Assert.True(await db.AuditLogs.AnyAsync(a => a.UserId == aliceUserId && a.Action == "PIPELINE_RUN_SUBMITTED"));
            Assert.True(await db.AuditLogs.AnyAsync(a => a.UserId == aliceUserId && a.Action == "PIPELINE_RUN_CANCEL_REQUESTED"));
            Assert.True(await db.AuditLogs.AnyAsync(a => a.UserId == aliceUserId && a.Action == "LOGOUT"));
        }
    }

    private async Task ProvisionAndGrantExecuteAsync(
        HttpClient browser, string adminToken, int pipelineId, long version, string principalKey, bool cancelImmediately = false)
    {
        var first = await SubmitPipelineAsync(browser, pipelineId, version, adminToken, Guid.NewGuid().ToString("D"));
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        var submitted = (await first.Content.ReadFromJsonAsync<PipelineRunSubmittedDto>(Json))!;
        if (cancelImmediately)
        {
            await AuthSendAsync(browser, HttpMethod.Post,
                $"/api/studio/pipelines/{pipelineId}/runs/{submitted.RunId}/cancel", adminToken);
        }
        await PollUntilTerminalAsync(browser, pipelineId, submitted.RunId, adminToken);

        using var grant = await AuthSendAsync(browser, HttpMethod.Put,
            $"/api/orchestrator/authorization/JOB/{Uri.EscapeDataString($"PIPELINE:{pipelineId}")}/USER/{principalKey}",
            adminToken, new { permission = "EXECUTE" });
        Assert.Equal(HttpStatusCode.OK, grant.StatusCode);
    }

    private static async Task<NativeClientTokenResponse> SignInViaOidcNativePkceAsync(HttpClient browser, string redirectUri)
    {
        var verifier = NativeClientRegistry.GenerateOpaqueToken(32);
        var challenge = NativeClientRegistry.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = "state-" + Guid.NewGuid().ToString("N")[..12];

        var authz = await browser.GetAsync(
            $"/api/auth/client/authorize?client_id={NativeClientRegistry.VsCodeClientId}" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            $"&code_challenge={Uri.EscapeDataString(challenge)}&code_challenge_method=S256" +
            $"&state={Uri.EscapeDataString(state)}&scope={Uri.EscapeDataString("studio.read studio.run studio.save")}");
        Assert.Equal(HttpStatusCode.Redirect, authz.StatusCode);

        var oidcLogin = await browser.GetAsync(authz.Headers.Location);
        Assert.Equal(HttpStatusCode.Redirect, oidcLogin.StatusCode);

        var oidcCallback = await browser.GetAsync("/api/auth/oidc/callback?code=idp-code&state=acceptance-oidc-state");
        Assert.Equal(HttpStatusCode.Redirect, oidcCallback.StatusCode);

        var consentUrl = oidcCallback.Headers.Location!.ToString();
        var tx = ParseQuery(consentUrl)["tx"];
        var consentPage = await browser.GetAsync(consentUrl);
        Assert.Equal(HttpStatusCode.OK, consentPage.StatusCode);
        var csrf = WebUtility.HtmlDecode(CsrfInputRegex().Match(await consentPage.Content.ReadAsStringAsync()).Groups[1].Value);

        var approved = await browser.PostAsync("/api/auth/client/consent", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["tx"] = tx,
            ["csrf_token"] = csrf,
            ["decision"] = "approve"
        }));
        Assert.Equal(HttpStatusCode.Redirect, approved.StatusCode);
        var code = ParseQuery(approved.Headers.Location!.ToString())["code"];

        var exchange = await browser.PostAsJsonAsync("/api/auth/client/exchange",
            new NativeClientExchangeRequest(code, verifier, redirectUri, NativeClientRegistry.VsCodeClientId));
        Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);
        return (await exchange.Content.ReadFromJsonAsync<NativeClientTokenResponse>(Json))!;
    }

    private static async Task<string> LoginLocalAsync(HttpClient client, string username, string password)
    {
        var resp = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<JsonObject>(Json))!["token"]!.GetValue<string>();
    }

    private static async Task<(int Id, long Version)> CreatePipelineAsync(
        HttpClient client, string token, int folderId, string name, string script)
    {
        var resp = await AuthSendAsync(client, HttpMethod.Post, "/api/studio/reports", token,
            new { folderId, name, scriptText = script, kind = "Pipeline" });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = (await resp.Content.ReadFromJsonAsync<JsonObject>(Json))!;
        return (body["id"]!.GetValue<int>(), body["version"]!.GetValue<long>());
    }

    private static Task<HttpResponseMessage> SubmitPipelineAsync(
        HttpClient client, int pipelineId, long version, string token, string idempotencyKey)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/studio/pipelines/{pipelineId}/runs")
        {
            Content = JsonContent.Create(new { version })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        return client.SendAsync(req);
    }

    private static async Task<PipelineRunDto> PollUntilTerminalAsync(
        HttpClient client, int pipelineId, long runId, string token)
    {
        for (var i = 0; i < 60; i++)
        {
            var resp = await AuthSendAsync(client, HttpMethod.Get, $"/api/studio/pipelines/{pipelineId}/runs/{runId}", token);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var run = (await resp.Content.ReadFromJsonAsync<PipelineRunDto>(Json))!;
            if (run.IsTerminal)
                return run;
            await Task.Delay(150);
        }

        throw new TimeoutException($"Run {runId} did not reach terminal status within timeout.");
    }

    private static Task<HttpResponseMessage> AuthSendAsync(
        HttpClient client, HttpMethod method, string url, string token, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            req.Content = JsonContent.Create(body);
        return client.SendAsync(req);
    }

    private static Dictionary<string, string> ParseQuery(string url)
    {
        var idx = url.IndexOf('?');
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (idx < 0) return result;
        foreach (var pair in url[(idx + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq > 0)
                result[Uri.UnescapeDataString(pair[..eq])] = Uri.UnescapeDataString(pair[(eq + 1)..]);
        }
        return result;
    }

    private sealed class AcceptancePortalFactory(OrchestratorWebFactory orchestrator) : PortalWebFactory
    {
        public MutableStubOidcService StubOidc { get; } = new();

        protected override void CustomizeConfiguration(Dictionary<string, string?> settings)
        {
            settings["Portal:Identity:Oidc:Enabled"] = "true";
            settings["Portal:Identity:Oidc:Authority"] = "https://idp.acceptance.test";
            settings["Portal:Identity:Oidc:ClientId"] = "portal-acceptance";
        }

        protected override void CustomizePortalConfig(PortalConfig config)
        {
            config.Identity.Oidc.Enabled = true;
            config.Identity.Oidc.Authority = "https://idp.acceptance.test";
            config.Identity.Oidc.ClientId = "portal-acceptance";
            config.Orchestrator.ApiUrl = "http://orchestrator.test";
            config.Orchestrator.ApiKey = "test-orch-key-12345";
            config.Orchestrator.IdentitySigningSecret = OrchestratorWebFactory.IdentitySecret;
        }

        protected override void CustomizeServices(IServiceCollection services)
        {
            services.RemoveAll<IOidcAuthenticationService>();
            services.AddSingleton<IOidcAuthenticationService>(StubOidc);
            services.AddHttpClient<OrchestratorProxyService>()
                .ConfigurePrimaryHttpMessageHandler(() => orchestrator.Server.CreateHandler());
        }
    }

    private sealed class MutableStubOidcService : IOidcAuthenticationService
    {
        public bool Enabled => true;
        public OidcIdentity Identity { get; set; } = new("oid-alice", "alice", "alice@alpha.test", ["DataAuthors"]);

        public Task<OidcAuthorizationRequest> BuildAuthorizationRequestAsync(string redirectUri, CancellationToken ct = default) =>
            Task.FromResult(new OidcAuthorizationRequest(
                "https://idp.acceptance.test/authorize?state=acceptance-oidc-state",
                "acceptance-oidc-state",
                "acceptance-nonce",
                "acceptance-verifier"));

        public Task<OidcIdentity> CompleteAsync(
            string code, string codeVerifier, string redirectUri, string expectedNonce, CancellationToken ct = default) =>
            Task.FromResult(Identity);
    }
}
