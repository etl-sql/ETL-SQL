using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Hosted;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using ETL_SQL.TUI.UI;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;
using Xunit.Abstractions;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Opt-in live ST57 acceptance test: exercises the shared .NET hosted client
/// (<see cref="HostedClientSessionService"/>, <see cref="HostedPipelineClient"/>,
/// <see cref="HostedRunCoordinator"/>) and <see cref="TuiHostedExecutionController"/>
/// with client_id=etlsql-tui against a real loopback Kestrel Portal, real Federated
/// Orchestrator, and mock OIDC provider.
/// Enable via ETLSQL_ST57_LIVE=1.
/// </summary>
[Trait("Category", "Integration")]
public sealed class St57TuiHostedLiveTests(ITestOutputHelper output)
{
    private const string FinanceCheckScript =
        "CREATE CONNECTION sample_data AS MOCKDB();\n"
        + "SELECT UserID INTO #users FROM sample_data.Users;\n"
        + "ASSERT (SELECT COUNT(*) FROM #users) > 0;\n";

    private const string SlowWaitScript = "WAITFOR DELAY '00:00:40';\n";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [St57LiveFact]
    public async Task LiveTuiHostedFlow_PassesEndToEndAgainstKestrelPortalAndFederatedOrchestrator()
    {
        using var orchestrator = new OrchestratorWebFactory(requireFederatedIdentity: true);
        using var portal = new LiveKestrelPortalFactory(orchestrator);
        portal.Start();

        var seeded = await SeedAcceptanceTopologyAsync(portal);

        await using var proxy = new LoopbackFaultProxy(portal.ServerAddress, portal);
        proxy.Start();

        var tempRegistryPath = Path.Combine(Path.GetTempPath(), $"etlsql_st57_servers_{Guid.NewGuid():N}.json");
        var clock = new AdvanceableClock(DateTimeOffset.UtcNow);
        var logs = new List<string>();
        var statuses = new List<string>();
        var chooserQueue = new Queue<int>();
        var capturedSecrets = new HashSet<string>(StringComparer.Ordinal);
        var browserLaunches = 0;

        try
        {
            var registry = new HostedServerRegistryStore(tempRegistryPath);
            var serverEntry = registry.AddOrUpdate("dev", "Loopback Portal", proxy.ProxyBaseUrl, "Dev");

            var authenticator = new LoopbackPkceAuthenticator(async (authorizeUri, ct) =>
            {
                Interlocked.Increment(ref browserLaunches);
                await DriveRealPortalOidcConsentFlowAsync(authorizeUri, code => capturedSecrets.Add(code), ct);
                return true;
            });

            await using var sessions = new HostedClientSessionService(
                clientId: HostedClientIds.Tui,
                requestedScope: HostedClientScopes.TuiDefault,
                serverRegistry: registry,
                authenticator: authenticator,
                clock: clock);
            var client = new HostedPipelineClient(sessions);
            var coordinator = new HostedRunCoordinator(
                sessions,
                client,
                clock,
                initialPollDelay: TimeSpan.FromMilliseconds(150),
                maxPollDelay: TimeSpan.FromMilliseconds(500));

            await using var controller = new TuiHostedExecutionController(
                (msg, _) => logs.Add(msg),
                msg => statuses.Add(msg),
                (_, _) => Task.FromResult(chooserQueue.Count > 0 ? chooserQueue.Dequeue() : -1),
                (_, _, _) => Task.FromResult<string?>(null),
                sessions,
                client,
                coordinator);

            void CaptureActiveTokens()
            {
                foreach (var s in sessions.GetActiveSecrets())
                    capturedSecrets.Add(s);
            }

            // 1. Offline startup defaults to Local (Offline)
            Assert.False(controller.IsHosted);
            Assert.Equal("Local (Offline)", controller.StatusLabel);
            Assert.Null(sessions.Session);

            // 2. Initial sign-in as alice via real authorize -> mock OIDC -> consent -> loopback PKCE -> exchange -> /api/studio/session
            portal.StubOidc.Identity = new OidcIdentity("oid-alice", "alice", "alice@alpha.test", ["DataAuthors"]);
            Assert.True(await controller.ConnectToServerAsync(serverEntry), string.Join("\n", logs.Concat(proxy.SanitizedTrace)));
            CaptureActiveTokens();
            Assert.Equal(1, browserLaunches);
            Assert.True(controller.IsHosted);
            Assert.Contains("alice", controller.StatusLabel);
            Assert.Contains("portal-host", controller.StatusLabel);

            // 3. Authorized run of FinanceCheck.etlsql as alice (discovers via /api/studio/pipelines/runnable & /{id}/revision)
            var work1 = await controller.PrepareHostedRunAsync("FinanceCheck.etlsql", isDirty: false);
            Assert.NotNull(work1);
            var outcome1 = await work1!(CancellationToken.None);
            Assert.Equal(HostedRunOutcomeKind.Completed, outcome1.Kind);
            Assert.Equal("SUCCESS", outcome1.FinalRun?.Status);
            Assert.NotEmpty(outcome1.FinalRun!.Statements);
            var run1Id = outcome1.FinalRun.RunId;

            // 4. Session reuse (0 extra browser launches, second distinct runId)
            var work2 = await controller.PrepareHostedRunAsync("FinanceCheck.etlsql", isDirty: false);
            Assert.NotNull(work2);
            var outcome2 = await work2!(CancellationToken.None);
            Assert.Equal(HostedRunOutcomeKind.Completed, outcome2.Kind);
            Assert.Equal("SUCCESS", outcome2.FinalRun?.Status);
            Assert.NotEqual(run1Id, outcome2.FinalRun!.RunId);
            Assert.Equal(1, browserLaunches);

            // 5. Denied run as bob: Portal FolderPermission.Execute ONLY (NOT Author), NO Orchestrator EXECUTE grant
            var histBeforeBob = await GetHistoryCountAsync(orchestrator, seeded.FinancePipelineId);
            portal.StubOidc.Identity = new OidcIdentity("oid-bob", "bob", "bob@alpha.test", ["DataReaders"]);
            Assert.True(await controller.ConnectToServerAsync(serverEntry), string.Join("\n", logs.Concat(proxy.SanitizedTrace)));
            CaptureActiveTokens();
            Assert.Contains("bob", controller.StatusLabel);

            // Execute-only discovery & revision lookup succeed for bob, then POST /runs returns 403 run_grant_denied
            var bobWork = await controller.PrepareHostedRunAsync("FinanceCheck.etlsql", isDirty: false);
            Assert.NotNull(bobWork);
            var bobOutcome = await bobWork!(CancellationToken.None);
            Assert.Equal(HostedRunOutcomeKind.Refused, bobOutcome.Kind);
            Assert.True(bobOutcome.Refusal?.Code == "run_grant_denied",
                $"Expected run_grant_denied, got code={bobOutcome.Refusal?.Code} msg={bobOutcome.Message} logs={string.Join(" | ", logs.TakeLast(5))}");
            Assert.Equal(histBeforeBob, await GetHistoryCountAsync(orchestrator, seeded.FinancePipelineId));

            // 6. Silent single-flight refresh as alice (advance clock past token expiry -> 0 extra browser launches)
            portal.StubOidc.Identity = new OidcIdentity("oid-alice", "alice", "alice@alpha.test", ["DataAuthors"]);
            Assert.True(await controller.ConnectToServerAsync(serverEntry), string.Join("\n", logs.Concat(proxy.SanitizedTrace)));
            CaptureActiveTokens();
            var launchesBeforeRefresh = browserLaunches;
            clock.Advance(TimeSpan.FromHours(2));
            var work3 = await controller.PrepareHostedRunAsync("FinanceCheck.etlsql", isDirty: false);
            Assert.NotNull(work3);
            var outcome3 = await work3!(CancellationToken.None);
            CaptureActiveTokens();
            Assert.Equal(HostedRunOutcomeKind.Completed, outcome3.Kind);
            Assert.Equal("SUCCESS", outcome3.FinalRun?.Status);
            Assert.Equal(launchesBeforeRefresh, browserLaunches);

            // 7. Stop on SlowWait.etlsql -> Cancellation requested immediately -> polls to terminal CANCELLED
            var slowWork = await controller.PrepareHostedRunAsync("SlowWait.etlsql", isDirty: false);
            Assert.NotNull(slowWork);
            var slowTask = slowWork!(CancellationToken.None);
            for (var i = 0; i < 100 && coordinator.ActiveRun is null; i++)
                await Task.Delay(50);
            Assert.NotNull(coordinator.ActiveRun);

            var stopMsg = await controller.RequestStopAsync();
            Assert.Contains("Cancellation requested", stopMsg);
            var slowOutcome = await slowTask;
            Assert.Equal(HostedRunOutcomeKind.Completed, slowOutcome.Kind);
            Assert.Equal("CANCELLED", slowOutcome.FinalRun?.Status);

            // 8. Dropped 202 response -> recovers exact runId with original Idempotency-Key -> exactly ONE Orchestrator run
            var histBeforeDrop1 = await GetHistoryCountAsync(orchestrator, seeded.FinancePipelineId);
            proxy.ArmDropNext202(revokeAliceSessionAfterDrop: false);
            var dropWork1 = await controller.PrepareHostedRunAsync("FinanceCheck.etlsql", isDirty: false);
            Assert.NotNull(dropWork1);
            var dropOutcome1 = await dropWork1!(CancellationToken.None);
            Assert.Equal(HostedRunOutcomeKind.Completed, dropOutcome1.Kind);
            Assert.Equal("SUCCESS", dropOutcome1.FinalRun?.Status);
            Assert.Equal(histBeforeDrop1 + 1, await GetHistoryCountAsync(orchestrator, seeded.FinancePipelineId));
            Assert.Contains(logs, l => l.Contains("Recovered accepted run", StringComparison.Ordinal));

            // 9. Revoked native session during recovery with dirty buffer -> preserves dirty buffer + pending key ->
            //    refuses to expose under bob -> resumes only under alice (same host, tenant, user) with ONE run
            var histBeforeDrop2 = await GetHistoryCountAsync(orchestrator, seeded.FinancePipelineId);
            proxy.ArmDropNext202(revokeAliceSessionAfterDrop: true);
            chooserQueue.Enqueue(0); // Dirty buffer prompt -> "Run Last Saved Revision"
            var dropWork2 = await controller.PrepareHostedRunAsync("FinanceCheck.etlsql", isDirty: true);
            Assert.NotNull(dropWork2);
            var dropOutcome2 = await dropWork2!(CancellationToken.None);
            Assert.Equal(HostedRunOutcomeKind.SessionExpired, dropOutcome2.Kind);
            Assert.True(controller.IsHosted);
            Assert.Null(sessions.Session);

            // Sign in as bob -> alice's pending submission MUST NOT be visible or resumable
            portal.StubOidc.Identity = new OidcIdentity("oid-bob", "bob", "bob@alpha.test", ["DataReaders"]);
            Assert.True(await controller.ConnectToServerAsync(serverEntry), string.Join("\n", logs.Concat(proxy.SanitizedTrace)));
            CaptureActiveTokens();
            Assert.Null(coordinator.GetPendingSubmissionForCurrentCaller());

            // Sign back in as alice (same origin, tenant, user) -> alice's pending key is preserved and recovers
            portal.StubOidc.Identity = new OidcIdentity("oid-alice", "alice", "alice@alpha.test", ["DataAuthors"]);
            Assert.True(await controller.ConnectToServerAsync(serverEntry), string.Join("\n", logs.Concat(proxy.SanitizedTrace)));
            CaptureActiveTokens();
            var pendingForAlice = coordinator.GetPendingSubmissionForCurrentCaller();
            Assert.NotNull(pendingForAlice);
            var recoveredOutcome = await controller.CheckPendingSubmissionAsync();
            Assert.NotNull(recoveredOutcome);
            Assert.Equal(HostedRunOutcomeKind.Completed, recoveredOutcome!.Kind);
            Assert.Equal("SUCCESS", recoveredOutcome.FinalRun?.Status);
            Assert.Equal(histBeforeDrop2 + 1, await GetHistoryCountAsync(orchestrator, seeded.FinancePipelineId));

            // 10. Disconnect -> revokes native session -> returns to Local (Offline) -> fresh instance defaults to Local
            await controller.DisconnectAsync();
            Assert.False(controller.IsHosted);
            Assert.Equal("Local (Offline)", controller.StatusLabel);
            Assert.Null(sessions.Session);

            await using var freshSessions = new HostedClientSessionService(serverRegistry: registry);
            Assert.False(freshSessions.IsHosted);
            Assert.Null(freshSessions.Session);

            // 11. Verify Portal audit rows from the live run
            using (var scope = portal.BrowserServices.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
                var alice = await db.Users.SingleAsync(u => u.UserName == "alice");
                Assert.True(await db.AuditLogs.AnyAsync(a => a.UserId == alice.Id && a.Action == "CLIENT_LOGIN"));
                Assert.True(await db.AuditLogs.AnyAsync(a => a.UserId == alice.Id && a.Action == "PIPELINE_RUN_SUBMITTED"));
                Assert.True(await db.AuditLogs.AnyAsync(a => a.UserId == alice.Id && a.Action == "PIPELINE_RUN_CANCEL_REQUESTED"));
                Assert.True(await db.AuditLogs.AnyAsync(a => a.UserId == alice.Id && a.Action == "LOGOUT"));
            }

            // 12. Stop Portal and verify secret hygiene (zero tokens/codes in logs, statuses, or hosted-servers.json)
            await portal.StopKestrelAsync();
            Assert.True(capturedSecrets.Count >= 10, $"Expected captured secrets to audit, got {capturedSecrets.Count}");
            var combined = string.Join('\n', logs.Concat(statuses)) + "\n" + File.ReadAllText(tempRegistryPath);
            foreach (var secret in capturedSecrets)
                Assert.DoesNotContain(secret, combined, StringComparison.Ordinal);

            output.WriteLine($"ST57_LIVE_SECRETS_CHECKED={capturedSecrets.Count}");
            foreach (var line in proxy.SanitizedTrace)
                output.WriteLine(line);
        }
        finally
        {
            try { File.Delete(tempRegistryPath); } catch { }
        }
    }

    private static async Task DriveRealPortalOidcConsentFlowAsync(
        Uri authorizeUri,
        Action<string> onCapturedCode,
        CancellationToken ct)
    {
        var cookies = new CookieContainer();
        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = true,
            CookieContainer = cookies
        };
        using var browser = new HttpClient(handler);

        // 1. GET /api/auth/client/authorize -> 302 to /api/auth/oidc/login or /api/auth/client/consent
        var currentUri = authorizeUri;
        HttpResponseMessage resp;
        while (true)
        {
            resp = await browser.GetAsync(currentUri, ct);
            if ((int)resp.StatusCode is >= 300 and < 400 && resp.Headers.Location is not null)
            {
                var next = resp.Headers.Location.IsAbsoluteUri
                    ? resp.Headers.Location
                    : new Uri(currentUri, resp.Headers.Location);
                resp.Dispose();

                // Mock OIDC IdP authorize URL -> jump directly to Portal /api/auth/oidc/callback
                if (next.Host.Equals("idp.acceptance.test", StringComparison.OrdinalIgnoreCase))
                {
                    var origin = $"{authorizeUri.Scheme}://{authorizeUri.Authority}";
                    currentUri = new Uri($"{origin}/api/auth/oidc/callback?code=mock-oidc-code&state=acceptance-oidc-state");
                    continue;
                }

                currentUri = next;
                continue;
            }
            break;
        }

        // 2. Consent page HTML -> extract csrf_token + tx and POST approval with Origin header
        using (resp)
        {
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var html = await resp.Content.ReadAsStringAsync(ct);
            var csrfMatch = Regex.Match(html, "name=\"csrf_token\"\\s+value=\"([^\"]+)\"");
            Assert.True(csrfMatch.Success, "Consent page did not contain csrf_token.");
            var csrf = WebUtility.HtmlDecode(csrfMatch.Groups[1].Value);
            var txMatch = Regex.Match(currentUri.Query, "[?&]tx=([^&]+)");
            Assert.True(txMatch.Success, $"Consent URL did not contain tx query param: {currentUri}");
            var tx = Uri.UnescapeDataString(txMatch.Groups[1].Value);
            var origin = $"{authorizeUri.Scheme}://{authorizeUri.Authority}";

            using var approveReq = new HttpRequestMessage(HttpMethod.Post, $"{origin}/api/auth/client/consent")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["tx"] = tx,
                    ["csrf_token"] = csrf,
                    ["decision"] = "approve"
                })
            };
            approveReq.Headers.TryAddWithoutValidation("Origin", origin);
            using var approveResp = await browser.SendAsync(approveReq, ct);
            Assert.True((int)approveResp.StatusCode is >= 300 and < 400 && approveResp.Headers.Location is not null,
                $"Expected redirect from consent approval, got {(int)approveResp.StatusCode}");

            var callbackUri = approveResp.Headers.Location!;
            var codeMatch = Regex.Match(callbackUri.Query, "[?&]code=([^&]+)");
            if (codeMatch.Success)
                onCapturedCode(Uri.UnescapeDataString(codeMatch.Groups[1].Value));

            // 3. Deliver callback to the loopback HttpListener
            using var cbResp = await browser.GetAsync(callbackUri, ct);
            Assert.Equal(HttpStatusCode.OK, cbResp.StatusCode);
        }
    }

    private static async Task<(int FinancePipelineId, int SlowPipelineId)> SeedAcceptanceTopologyAsync(LiveKestrelPortalFactory portal)
    {
        int folderId;
        using (var scope = portal.BrowserServices.CreateScope())
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

            // DataReaders has FolderPermission.Execute ONLY (not Author) to prove Execute-only discovery + 403 run_grant_denied.
            db.FolderAcls.AddRange(
                new FolderAcl { FolderId = folderId, GroupId = authorsGroup.Id, Permission = FolderPermission.Author },
                new FolderAcl { FolderId = folderId, GroupId = readersGroup.Id, Permission = FolderPermission.Execute });

            var alice = new PortalUser
            {
                TenantId = "portal-host",
                UserName = "alice",
                NormalizedUserName = "ALICE",
                Email = "alice@alpha.test",
                NormalizedEmail = "ALICE@ALPHA.TEST",
                IsActive = true,
                MustChangePassword = false,
                Provider = "OIDC",
                ExternalSubject = "oid-alice",
                SecurityStamp = Guid.NewGuid().ToString("N")
            };
            db.Users.Add(alice);
            await db.SaveChangesAsync();
            db.UserGroups.Add(new UserGroup { UserId = alice.Id, GroupId = authorsGroup.Id });
            await db.SaveChangesAsync();
        }

        using var http = new HttpClient { BaseAddress = new Uri(portal.ServerAddress) };
        var loginResp = await http.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "Admin@12345!" });
        Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);
        var adminToken = (await loginResp.Content.ReadFromJsonAsync<JsonObject>(Json))!["token"]!.GetValue<string>();

        var (financeId, financeVersion) = await CreatePipelineAsync(http, adminToken, folderId, "FinanceCheck.etlsql", FinanceCheckScript);
        var (slowId, slowVersion) = await CreatePipelineAsync(http, adminToken, folderId, "SlowWait.etlsql", SlowWaitScript);

        string alicePrincipalKey;
        using (var scope = portal.BrowserServices.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            alicePrincipalKey = (await db.Users.SingleAsync(u => u.UserName == "alice")).PrincipalKey!;
        }

        await ProvisionAndGrantAsync(http, adminToken, financeId, financeVersion, alicePrincipalKey, cancelImmediately: false);
        await ProvisionAndGrantAsync(http, adminToken, slowId, slowVersion, alicePrincipalKey, cancelImmediately: true);

        return (financeId, slowId);
    }

    private static async Task<(int Id, long Version)> CreatePipelineAsync(
        HttpClient http, string token, int folderId, string name, string script)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/studio/reports")
        {
            Content = JsonContent.Create(new { folderId, name, scriptText = script, kind = "Pipeline" })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await http.SendAsync(req);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = (await resp.Content.ReadFromJsonAsync<JsonObject>(Json))!;
        return (body["id"]!.GetValue<int>(), body["version"]!.GetValue<long>());
    }

    private static async Task ProvisionAndGrantAsync(
        HttpClient http, string token, int pipelineId, long version, string principalKey, bool cancelImmediately)
    {
        using var submitReq = new HttpRequestMessage(HttpMethod.Post, $"/api/studio/pipelines/{pipelineId}/runs")
        {
            Content = JsonContent.Create(new { version })
        };
        submitReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var submitResp = await http.SendAsync(submitReq);
        Assert.Equal(HttpStatusCode.Accepted, submitResp.StatusCode);
        var submitted = (await submitResp.Content.ReadFromJsonAsync<PipelineRunSubmittedDto>(Json))!;

        if (cancelImmediately)
        {
            using var cancelReq = new HttpRequestMessage(HttpMethod.Post, $"/api/studio/pipelines/{pipelineId}/runs/{submitted.RunId}/cancel");
            cancelReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var _ = await http.SendAsync(cancelReq);
        }

        for (var i = 0; i < 60; i++)
        {
            using var pollReq = new HttpRequestMessage(HttpMethod.Get, $"/api/studio/pipelines/{pipelineId}/runs/{submitted.RunId}");
            pollReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var pollResp = await http.SendAsync(pollReq);
            if (pollResp.IsSuccessStatusCode)
            {
                var run = await pollResp.Content.ReadFromJsonAsync<PipelineRunDto>(Json);
                if (run is not null && run.IsTerminal)
                    break;
            }
            await Task.Delay(100);
        }

        using var grantReq = new HttpRequestMessage(HttpMethod.Put,
            $"/api/orchestrator/authorization/JOB/{Uri.EscapeDataString($"PIPELINE:{pipelineId}")}/USER/{principalKey}")
        {
            Content = JsonContent.Create(new { permission = "EXECUTE" })
        };
        grantReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var grantResp = await http.SendAsync(grantReq);
        Assert.True(grantResp.IsSuccessStatusCode, $"Failed to grant EXECUTE on PIPELINE:{pipelineId}: {(int)grantResp.StatusCode}");
    }

    private static async Task<int> GetHistoryCountAsync(OrchestratorWebFactory orchestrator, int pipelineId)
    {
        using var scope = orchestrator.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IJobHistoryStore>();
        var job = await store.GetJobAsync("portal-host", $"PIPELINE:{pipelineId}");
        return job is null ? 0 : (await store.GetHistoryAsync(job.Id, 200)).Count();
    }

    private sealed class AdvanceableClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now += delta;
    }

    private sealed class LiveKestrelPortalFactory(OrchestratorWebFactory orchestrator) : PortalWebFactory
    {
        public string ServerAddress { get; private set; } = string.Empty;
        public IServiceProvider BrowserServices => Services;
        public MutableStubOidc StubOidc { get; } = new();

        public void Start()
        {
            _ = orchestrator.Server;
            UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
            using var client = CreateClient();
            ServerAddress = client.BaseAddress!.AbsoluteUri.TrimEnd('/');
        }

        public async Task StopKestrelAsync()
        {
            using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await Services.GetRequiredService<IServer>().StopAsync(stopTimeout.Token);
        }

        protected override void CustomizeConfiguration(Dictionary<string, string?> settings)
        {
            settings["Portal:Identity:Oidc:Enabled"] = "true";
            settings["Portal:Identity:Oidc:Authority"] = "https://idp.acceptance.test";
            settings["Portal:Identity:Oidc:ClientId"] = "portal-acceptance";
        }

        protected override void CustomizePortalConfig(ETL_SQL.Portal.PortalConfig config)
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

    private sealed class MutableStubOidc : IOidcAuthenticationService
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

    private sealed class LoopbackFaultProxy : IAsyncDisposable
    {
        private readonly string _upstreamOrigin;
        private readonly LiveKestrelPortalFactory _portal;
        private readonly HttpListener _proxyListener = new();
        private readonly HttpClient _upstream = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        private readonly CancellationTokenSource _cts = new();
        private readonly List<string> _trace = [];
        private readonly object _traceLock = new();

        private volatile bool _dropNext202;
        private volatile bool _revokeAliceSessionAfterDrop;

        public string ProxyBaseUrl { get; }
        public IReadOnlyList<string> SanitizedTrace { get { lock (_traceLock) return _trace.ToArray(); } }

        public LoopbackFaultProxy(string upstreamOrigin, LiveKestrelPortalFactory portal)
        {
            _upstreamOrigin = upstreamOrigin.TrimEnd('/');
            _portal = portal;
            ProxyBaseUrl = $"http://127.0.0.1:{GetFreePort()}";
            _proxyListener.Prefixes.Add(ProxyBaseUrl + "/");
        }

        public void ArmDropNext202(bool revokeAliceSessionAfterDrop)
        {
            _dropNext202 = true;
            _revokeAliceSessionAfterDrop = revokeAliceSessionAfterDrop;
        }

        public void Start()
        {
            _proxyListener.Start();
            _ = Task.Run(ServeLoopAsync);
        }

        private async Task ServeLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try { ctx = await _proxyListener.GetContextAsync(); }
                catch { break; }
                _ = Task.Run(() => HandleProxyAsync(ctx));
            }
        }

        private async Task HandleProxyAsync(HttpListenerContext ctx)
        {
            var path = ctx.Request.Url?.AbsolutePath ?? "/";
            var method = ctx.Request.HttpMethod;
            try
            {
                var targetUrl = _upstreamOrigin + ctx.Request.RawUrl;
                using var req = new HttpRequestMessage(new HttpMethod(method), targetUrl);
                if (ctx.Request.HasEntityBody)
                {
                    using var ms = new MemoryStream();
                    await ctx.Request.InputStream.CopyToAsync(ms);
                    req.Content = new ByteArrayContent(ms.ToArray());
                    if (!string.IsNullOrEmpty(ctx.Request.ContentType))
                        req.Content.Headers.TryAddWithoutValidation("Content-Type", ctx.Request.ContentType);
                }

                foreach (string? name in ctx.Request.Headers.AllKeys)
                {
                    if (string.IsNullOrEmpty(name)) continue;
                    if (name.Equals("Host", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var value = ctx.Request.Headers[name]!;
                    if (name.Equals("Origin", StringComparison.OrdinalIgnoreCase)
                        && value.Equals(ProxyBaseUrl, StringComparison.OrdinalIgnoreCase))
                        value = _upstreamOrigin;
                    req.Headers.TryAddWithoutValidation(name, value);
                }

                using var resp = await _upstream.SendAsync(req);

                if (_dropNext202
                    && method == "POST"
                    && path.EndsWith("/runs", StringComparison.OrdinalIgnoreCase)
                    && resp.StatusCode == HttpStatusCode.Accepted)
                {
                    _dropNext202 = false;
                    if (_revokeAliceSessionAfterDrop)
                    {
                        _revokeAliceSessionAfterDrop = false;
                        await RevokeAliceNativeSessionsAsync();
                    }
                    RecordTrace($"{method} {path} -> <DROPPED_202>");
                    ctx.Response.Abort();
                    return;
                }

                RecordTrace($"{method} {path} -> {(int)resp.StatusCode}");
                ctx.Response.StatusCode = (int)resp.StatusCode;
                foreach (var header in resp.Headers)
                {
                    if (header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (var val in header.Value)
                        ctx.Response.Headers.Add(header.Key, val);
                }
                foreach (var header in resp.Content.Headers)
                {
                    if (header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
                    if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                        ctx.Response.ContentType = string.Join(", ", header.Value);
                    else
                        foreach (var val in header.Value)
                            ctx.Response.Headers.Add(header.Key, val);
                }

                var bodyBytes = await resp.Content.ReadAsByteArrayAsync();
                ctx.Response.ContentLength64 = bodyBytes.Length;
                await ctx.Response.OutputStream.WriteAsync(bodyBytes);
                ctx.Response.Close();
            }
            catch
            {
                try { ctx.Response.Abort(); } catch { }
            }
        }

        private async Task RevokeAliceNativeSessionsAsync()
        {
            using var scope = _portal.BrowserServices.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var alice = await db.Users.SingleAsync(u => u.UserName == "alice");
            var now = DateTime.UtcNow;
            await db.NativeClientSessions
                .Where(s => s.UserId == alice.Id && s.RevokedAt == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.RevokedAt, now)
                    .SetProperty(x => x.RevokedReason, "st57_revoked_during_recovery"));
            await db.NativeClientRefreshTokens
                .Where(t => t.UserId == alice.Id && t.RevokedAt == null)
                .ExecuteUpdateAsync(t => t.SetProperty(x => x.RevokedAt, now));
        }

        private void RecordTrace(string line)
        {
            lock (_traceLock) _trace.Add(line);
        }

        private static int GetFreePort()
        {
            using var tcp = new TcpListener(IPAddress.Loopback, 0);
            tcp.Start();
            var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
            tcp.Stop();
            return port;
        }

        public ValueTask DisposeAsync()
        {
            _cts.Cancel();
            try { _proxyListener.Stop(); } catch { }
            try { _proxyListener.Close(); } catch { }
            _upstream.Dispose();
            _cts.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class St57LiveFactAttribute : FactAttribute
    {
        public St57LiveFactAttribute()
        {
            if (!string.Equals(Environment.GetEnvironmentVariable("ETLSQL_ST57_LIVE"), "1", StringComparison.Ordinal))
                Skip = "Opt-in ST57 live test. Set ETLSQL_ST57_LIVE=1 to run against loopback Kestrel Portal and Federated Orchestrator.";
        }
    }
}
