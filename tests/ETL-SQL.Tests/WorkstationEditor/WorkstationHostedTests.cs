using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ETL_SQL.Core.Hosted;
using ETL_SQL.WorkstationEditor;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.WorkstationEditor;

public sealed class WorkstationHostedTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task HostedStatus_DefaultsToLocalOffline_EvenWhenServersAreRegistered()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "etl-sql-st58-unit", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var registryPath = Path.Combine(tempDir, "hosted-servers.json");
        try
        {
            var registry = new HostedServerRegistryStore(registryPath);
            registry.AddOrUpdate("prod", "Prod Portal", "https://portal.corp.example", "Prod");

            await using var app = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
                tempDir, null, 0, false, "studio-token", StudioMode: true, HostedServerRegistryPath: registryPath));
            await app.StartAsync();

            using var client = new HttpClient { BaseAddress = new Uri(WorkstationEditorApp.GetListeningUrl(app)) };

            // Unauthenticated request must be rejected by the existing loopback token gate.
            using var unauth = await client.GetAsync("/api/hosted/status");
            Assert.Equal(HttpStatusCode.Unauthorized, unauth.StatusCode);

            // Authenticated request defaults to Local (Offline) with registered servers listed.
            client.DefaultRequestHeaders.Add("X-ETLSQL-EDITOR-TOKEN", "studio-token");
            var status = await client.GetFromJsonAsync<HostedStatusDto>("/api/hosted/status", Json);
            Assert.NotNull(status);
            Assert.Equal("local", status!.Mode);
            Assert.Equal("local", status.TargetKey);
            Assert.Equal("Local (Offline)", status.StatusLabel);
            Assert.False(status.SignedIn);
            Assert.Null(status.Server);
            Assert.Null(status.User);
            Assert.Single(status.Servers);
            Assert.Equal("prod", status.Servers[0].Alias);

            // Verify client ID and scopes on the underlying HostedClientSessionService.
            var sessionService = app.Services.GetRequiredService<HostedClientSessionService>();
            Assert.Equal(HostedClientIds.DesktopStudio, sessionService.ClientId);
            Assert.Equal(HostedClientScopes.TuiDefault, sessionService.RequestedScope);
            Assert.DoesNotContain(HostedClientScopes.StudioSave, sessionService.RequestedScope, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task HostedServers_RejectsNonLoopbackHttp_AndSupportsCrud()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "etl-sql-st58-unit", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var registryPath = Path.Combine(tempDir, "hosted-servers.json");
        try
        {
            await using var app = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
                tempDir, null, 0, false, "studio-token", StudioMode: true, HostedServerRegistryPath: registryPath));
            await app.StartAsync();

            using var client = new HttpClient { BaseAddress = new Uri(WorkstationEditorApp.GetListeningUrl(app)) };
            client.DefaultRequestHeaders.Add("X-ETLSQL-EDITOR-TOKEN", "studio-token");

            // Plain http:// to non-loopback host must be refused.
            using var badResp = await client.PostAsJsonAsync("/api/hosted/servers", new
            {
                alias = "bad",
                label = "Insecure",
                origin = "http://portal.corp.example"
            });
            Assert.Equal(HttpStatusCode.BadRequest, badResp.StatusCode);

            // Valid https:// origin succeeds and persists to user-level registry file.
            using var addResp = await client.PostAsJsonAsync("/api/hosted/servers", new
            {
                alias = "dev",
                label = "Dev Portal",
                origin = "https://dev.portal.example/",
                environment = "Dev"
            });
            Assert.Equal(HttpStatusCode.OK, addResp.StatusCode);
            var afterAdd = await addResp.Content.ReadFromJsonAsync<HostedStatusDto>(Json);
            Assert.NotNull(afterAdd);
            Assert.Single(afterAdd!.Servers);
            Assert.Equal("https://dev.portal.example", afterAdd.Servers[0].Origin);

            // Local offline run works even with a server registered and unreachable.
            using var localRunResp = await client.PostAsJsonAsync("/api/designer/run", new
            {
                script = "SELECT 42 AS Answer;"
            });
            Assert.Equal(HttpStatusCode.OK, localRunResp.StatusCode);
            var localRun = await localRunResp.Content.ReadFromJsonAsync<RunResponse>(Json);
            Assert.NotNull(localRun);
            Assert.True(localRun!.Success);
            Assert.Equal(1, localRun.RowCount);

            // Delete removes server from registry.
            using var delResp = await client.DeleteAsync("/api/hosted/servers/dev");
            Assert.Equal(HttpStatusCode.OK, delResp.StatusCode);
            var afterDel = await delResp.Content.ReadFromJsonAsync<HostedStatusDto>(Json);
            Assert.Empty(afterDel!.Servers);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task HostedRuns_WhenSignedOut_RefusesAndNeverFallsBackToLocal()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "etl-sql-st58-unit", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var registryPath = Path.Combine(tempDir, "hosted-servers.json");
        try
        {
            await using var app = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
                tempDir, null, 0, false, "studio-token", StudioMode: true, HostedServerRegistryPath: registryPath));
            await app.StartAsync();

            using var client = new HttpClient { BaseAddress = new Uri(WorkstationEditorApp.GetListeningUrl(app)) };
            client.DefaultRequestHeaders.Add("X-ETLSQL-EDITOR-TOKEN", "studio-token");

            using var runResp = await client.PostAsJsonAsync("/api/hosted/runs", new HostedStartRunRequest(
                PipelineId: 1,
                ExpectedVersion: 1,
                ExpectedOrigin: "https://portal.corp.example",
                ExpectedTenantId: "tenant-alpha",
                ExpectedUserId: 10,
                DocumentId: "doc-primary",
                DocumentPath: "FinanceCheck.etlsql"));
            Assert.Equal(HttpStatusCode.Unauthorized, runResp.StatusCode);
            var body = await runResp.Content.ReadAsStringAsync();
            Assert.Contains("session_expired", body, StringComparison.Ordinal);

            using var recoverResp = await client.PostAsJsonAsync("/api/hosted/runs/recover", new HostedRecoverRunRequest("check"));
            Assert.Equal(HttpStatusCode.Unauthorized, recoverResp.StatusCode);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task HostedRuns_StaleTargetOrRevision_StartsZeroRunsOnNewTarget()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "etl-sql-st58-unit", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var registryPath = Path.Combine(tempDir, "hosted-servers.json");
        try
        {
            const string originA = "https://portal-a.test";
            const string originB = "https://portal-b.test";
            var registry = new HostedServerRegistryStore(registryPath);
            registry.AddOrUpdate("a", "Portal A", originA, "Dev");
            registry.AddOrUpdate("b", "Portal B", originB, "Prod");

            var portalHandler = new StubHostedPortalHandler();
            using var portalHttp = new HttpClient(portalHandler);
            var authenticator = new LoopbackPkceAuthenticator(async (authorizeUri, ct) =>
            {
                var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(authorizeUri.Query);
                var redirect = query["redirect_uri"].ToString();
                var state = query["state"].ToString();
                using var cb = new HttpClient();
                await cb.GetAsync($"{redirect}?code=code-123&state={Uri.EscapeDataString(state)}", ct);
                return true;
            });

            await using var app = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
                tempDir, null, 0, false, "studio-token", StudioMode: true, HostedServerRegistryPath: registryPath),
                services =>
                {
                    services.AddSingleton(registry);
                    services.AddSingleton(authenticator);
                    services.AddSingleton(new HostedClientSessionService(
                        clientId: HostedClientIds.DesktopStudio,
                        requestedScope: HostedClientScopes.TuiDefault,
                        serverRegistry: registry,
                        authenticator: authenticator,
                        httpClient: portalHttp));
                });
            await app.StartAsync();

            using var client = new HttpClient { BaseAddress = new Uri(WorkstationEditorApp.GetListeningUrl(app)) };
            client.DefaultRequestHeaders.Add("X-ETLSQL-EDITOR-TOKEN", "studio-token");

            // Connect to Portal A (pipeline ID 7, v1).
            portalHandler.ActiveTenantId = "tenant-a";
            portalHandler.ActiveUserId = 10;
            portalHandler.ActiveUsername = "alice";
            using var connectA = await client.PostAsJsonAsync("/api/hosted/connect", new HostedConnectRequest("a"));
            Assert.Equal(HttpStatusCode.OK, connectA.StatusCode);

            // Switch to Portal B, which ALSO has a pipeline with ID 7 (v1).
            portalHandler.ActiveTenantId = "tenant-b";
            using var connectB = await client.PostAsJsonAsync("/api/hosted/connect", new HostedConnectRequest("b"));
            Assert.Equal(HttpStatusCode.OK, connectB.StatusCode);

            // Submitting Portal A's stale selection against Portal B MUST fail with 409 target_changed and start ZERO runs.
            using var staleTargetResp = await client.PostAsJsonAsync("/api/hosted/runs", new HostedStartRunRequest(
                PipelineId: 7,
                ExpectedVersion: 1,
                ExpectedOrigin: originA,
                ExpectedTenantId: "tenant-a",
                ExpectedUserId: 10,
                DocumentId: "doc-1",
                DocumentPath: "SharedId.etlsql"));
            Assert.Equal(HttpStatusCode.Conflict, staleTargetResp.StatusCode);
            var staleTargetBody = await staleTargetResp.Content.ReadAsStringAsync();
            Assert.Contains("target_changed", staleTargetBody, StringComparison.Ordinal);
            Assert.Equal(0, portalHandler.SubmitCountForOrigin(originB));

            // Stale revision on Portal B (client expected v1, Portal B is now v2) MUST fail with 409 revision_changed and start ZERO runs.
            portalHandler.PipelineVersion = 2;
            using var staleRevResp = await client.PostAsJsonAsync("/api/hosted/runs", new HostedStartRunRequest(
                PipelineId: 7,
                ExpectedVersion: 1,
                ExpectedOrigin: originB,
                ExpectedTenantId: "tenant-b",
                ExpectedUserId: 10,
                DocumentId: "doc-1",
                DocumentPath: "SharedId.etlsql"));
            Assert.Equal(HttpStatusCode.Conflict, staleRevResp.StatusCode);
            var staleRevBody = await staleRevResp.Content.ReadAsStringAsync();
            Assert.Contains("revision_changed", staleRevBody, StringComparison.Ordinal);
            Assert.Equal(0, portalHandler.SubmitCountForOrigin(originB));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task HostedOperations_EnforceOwnershipOnReadAndStop_AndBlockDelayedStopOnLaterRun()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "etl-sql-st58-unit", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var registryPath = Path.Combine(tempDir, "hosted-servers.json");
        try
        {
            const string origin = "https://portal-a.test";
            var registry = new HostedServerRegistryStore(registryPath);
            registry.AddOrUpdate("a", "Portal A", origin, "Dev");

            var portalHandler = new StubHostedPortalHandler();
            using var portalHttp = new HttpClient(portalHandler);
            var authenticator = new LoopbackPkceAuthenticator(async (authorizeUri, ct) =>
            {
                var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(authorizeUri.Query);
                var redirect = query["redirect_uri"].ToString();
                var state = query["state"].ToString();
                using var cb = new HttpClient();
                await cb.GetAsync($"{redirect}?code=code-123&state={Uri.EscapeDataString(state)}", ct);
                return true;
            });

            await using var app = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
                tempDir, null, 0, false, "studio-token", StudioMode: true, HostedServerRegistryPath: registryPath),
                services =>
                {
                    services.AddSingleton(registry);
                    services.AddSingleton(authenticator);
                    var sessionSvc = new HostedClientSessionService(
                        clientId: HostedClientIds.DesktopStudio,
                        requestedScope: HostedClientScopes.TuiDefault,
                        serverRegistry: registry,
                        authenticator: authenticator,
                        httpClient: portalHttp);
                    services.AddSingleton(sessionSvc);
                    services.AddSingleton(provider => new HostedRunCoordinator(
                        provider.GetRequiredService<HostedClientSessionService>(),
                        provider.GetRequiredService<HostedPipelineClient>(),
                        provider.GetRequiredService<TimeProvider>(),
                        initialPollDelay: TimeSpan.FromMilliseconds(40),
                        maxPollDelay: TimeSpan.FromMilliseconds(80)));
                });
            await app.StartAsync();

            using var client = new HttpClient { BaseAddress = new Uri(WorkstationEditorApp.GetListeningUrl(app)) };
            client.DefaultRequestHeaders.Add("X-ETLSQL-EDITOR-TOKEN", "studio-token");

            // 1. Sign in as alice and run operation 1 to completion.
            portalHandler.ActiveTenantId = "tenant-a";
            portalHandler.ActiveUserId = 10;
            portalHandler.ActiveUsername = "alice";
            portalHandler.CompleteImmediately = true;
            using var connectAlice = await client.PostAsJsonAsync("/api/hosted/connect", new HostedConnectRequest("a"));
            Assert.Equal(HttpStatusCode.OK, connectAlice.StatusCode);

            using var startOp1Resp = await client.PostAsJsonAsync("/api/hosted/runs", new HostedStartRunRequest(
                PipelineId: 7, ExpectedVersion: 1, ExpectedOrigin: origin, ExpectedTenantId: "tenant-a", ExpectedUserId: 10));
            Assert.Equal(HttpStatusCode.Accepted, startOp1Resp.StatusCode);
            var op1 = (await startOp1Resp.Content.ReadFromJsonAsync<HostedRunOperationDto>(Json))!;

            // Wait for op1 to reach terminal completion.
            HostedRunOperationDto op1State = op1;
            for (var i = 0; i < 40 && !op1State.IsTerminal; i++)
            {
                await Task.Delay(50);
                op1State = (await client.GetFromJsonAsync<HostedRunOperationDto>($"/api/hosted/runs/{op1.OperationId}", Json))!;
            }
            Assert.True(op1State.IsTerminal);

            // 2. Switch to bob -> reading or stopping alice's operation MUST return 403 Forbidden.
            portalHandler.ActiveUserId = 20;
            portalHandler.ActiveUsername = "bob";
            using var connectBob = await client.PostAsJsonAsync("/api/hosted/connect", new HostedConnectRequest("a"));
            Assert.Equal(HttpStatusCode.OK, connectBob.StatusCode);

            using var bobReadResp = await client.GetAsync($"/api/hosted/runs/{op1.OperationId}");
            Assert.Equal(HttpStatusCode.Forbidden, bobReadResp.StatusCode);

            using var bobStopResp = await client.PostAsJsonAsync("/api/hosted/runs/stop", new HostedStopRunRequest(op1.OperationId));
            Assert.Equal(HttpStatusCode.Forbidden, bobStopResp.StatusCode);

            // 3. Switch back to alice, start a long-running op2, and send a delayed Stop for op1.
            //    Delayed Stop MUST return 409 operation_not_active and MUST NOT cancel op2.
            portalHandler.ActiveUserId = 10;
            portalHandler.ActiveUsername = "alice";
            portalHandler.CompleteImmediately = false;
            using var reconnectAlice = await client.PostAsJsonAsync("/api/hosted/connect", new HostedConnectRequest("a"));
            Assert.Equal(HttpStatusCode.OK, reconnectAlice.StatusCode);

            using var startOp2Resp = await client.PostAsJsonAsync("/api/hosted/runs", new HostedStartRunRequest(
                PipelineId: 7, ExpectedVersion: 1, ExpectedOrigin: origin, ExpectedTenantId: "tenant-a", ExpectedUserId: 10));
            Assert.Equal(HttpStatusCode.Accepted, startOp2Resp.StatusCode);
            var op2 = (await startOp2Resp.Content.ReadFromJsonAsync<HostedRunOperationDto>(Json))!;

            // Wait until op2 is actively running.
            for (var i = 0; i < 40; i++)
            {
                var snap = await client.GetFromJsonAsync<HostedRunOperationDto>($"/api/hosted/runs/{op2.OperationId}", Json);
                if (snap?.State == "running") break;
                await Task.Delay(50);
            }

            using var delayedStopResp = await client.PostAsJsonAsync("/api/hosted/runs/stop", new HostedStopRunRequest(op1.OperationId));
            Assert.Equal(HttpStatusCode.Conflict, delayedStopResp.StatusCode);
            Assert.Contains("operation_not_active", await delayedStopResp.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            Assert.Equal(0, portalHandler.CancelCalls);

            // Explicit Stop naming op2 succeeds and cancels op2 cleanly.
            using var validStopResp = await client.PostAsJsonAsync("/api/hosted/runs/stop", new HostedStopRunRequest(op2.OperationId));
            Assert.Equal(HttpStatusCode.OK, validStopResp.StatusCode);
            Assert.Equal(1, portalHandler.CancelCalls);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    private sealed class StubHostedPortalHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, int> _submitCounts = new(StringComparer.OrdinalIgnoreCase);
        private long _nextRunId = 100;
        private volatile bool _cancelled;

        public string ActiveTenantId { get; set; } = "tenant-a";
        public int ActiveUserId { get; set; } = 10;
        public string ActiveUsername { get; set; } = "alice";
        public long PipelineVersion { get; set; } = 1;
        public bool CompleteImmediately { get; set; } = true;
        public int CancelCalls { get; private set; }

        public int SubmitCountForOrigin(string origin) =>
            _submitCounts.TryGetValue(origin, out var count) ? count : 0;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var origin = uri.GetLeftPart(UriPartial.Authority);
            var path = uri.AbsolutePath;

            if (path == "/api/auth/providers")
                return JsonResp(HttpStatusCode.OK, new { localEnabled = false, oidcEnabled = true, oidcDisplayName = "Stub", ldapEnabled = false });

            if (path == "/api/auth/client/exchange")
            {
                return JsonResp(HttpStatusCode.OK, new
                {
                    token = $"tok-{ActiveUserId}-{Guid.NewGuid():N}",
                    refreshToken = $"ref-{ActiveUserId}-{Guid.NewGuid():N}",
                    expiresAt = DateTime.UtcNow.AddHours(1),
                    sessionId = $"sess-{ActiveUserId}-{Guid.NewGuid():N}",
                    clientId = HostedClientIds.DesktopStudio,
                    audience = "etl-sql-portal-native-api",
                    tenantId = ActiveTenantId,
                    scopes = new[] { HostedClientScopes.StudioRead, HostedClientScopes.StudioRun },
                    user = new { id = ActiveUserId, username = ActiveUsername, email = $"{ActiveUsername}@test.local" }
                });
            }

            if (path == "/api/auth/client/logout")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));

            if (path == "/api/studio/session")
                return JsonResp(HttpStatusCode.OK, new
                {
                    mode = "CatalogOnly",
                    capabilities = new[] { "StudioAccess", "ScriptRead", "ScriptRun" },
                    sourceControlEnabled = false,
                    draftRecovery = false,
                    privateDrafts = false,
                    maxScriptCharacters = 100000
                });

            if (path == "/api/studio/pipelines/7/revision")
                return JsonResp(HttpStatusCode.OK, new
                {
                    id = 7,
                    name = "SharedId.etlsql",
                    folderPath = "/Finance",
                    version = PipelineVersion,
                    canAuthor = true
                });

            if (request.Method == HttpMethod.Post && path == "/api/studio/pipelines/7/runs")
            {
                _submitCounts[origin] = SubmitCountForOrigin(origin) + 1;
                _cancelled = false;
                var runId = Interlocked.Increment(ref _nextRunId);
                return JsonResp(HttpStatusCode.Accepted, new
                {
                    runId,
                    pipelineId = 7,
                    version = PipelineVersion,
                    scriptHash = "sha256:abc123",
                    jobName = "PIPELINE:7",
                    replayed = false
                });
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/cancel", StringComparison.Ordinal))
            {
                CancelCalls++;
                _cancelled = true;
                var runId = long.Parse(path.Split('/')[^2]);
                return JsonResp(HttpStatusCode.Accepted, new { runId, pipelineId = 7 });
            }

            if (request.Method == HttpMethod.Get && path.StartsWith("/api/studio/pipelines/7/runs/", StringComparison.Ordinal))
            {
                var runId = long.Parse(path.Split('/')[^1]);
                var isTerminal = CompleteImmediately || _cancelled;
                var status = _cancelled ? "CANCELLED" : (isTerminal ? "SUCCESS" : "RUNNING");
                return JsonResp(HttpStatusCode.OK, new
                {
                    runId,
                    pipelineId = 7,
                    jobName = "PIPELINE:7",
                    status,
                    isTerminal,
                    startTime = DateTime.UtcNow,
                    endTime = isTerminal ? DateTime.UtcNow : (DateTime?)null,
                    errorMessage = (string?)null,
                    rowsProcessed = isTerminal ? 5 : 0,
                    scriptHash = "sha256:abc123",
                    logTruncated = false,
                    statements = Array.Empty<object>()
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static Task<HttpResponseMessage> JsonResp(HttpStatusCode code, object payload) =>
            Task.FromResult(new HttpResponseMessage(code)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload, Json), Encoding.UTF8, "application/json")
            });
    }
}
