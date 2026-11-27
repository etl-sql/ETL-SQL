using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ETL_SQL.Core.Hosted;
using ETL_SQL.TUI.UI;
using Xunit;

namespace ETL_SQL.Tests;

public sealed class HostedClientTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("https://portal.company.internal", true, "https://portal.company.internal")]
    [InlineData("https://PORTAL.Company.Internal:443/", true, "https://portal.company.internal")]
    [InlineData("https://portal.company.internal:8443", true, "https://portal.company.internal:8443")]
    [InlineData("http://localhost:5001", true, "http://localhost:5001")]
    [InlineData("http://127.0.0.1:5001/", true, "http://127.0.0.1:5001")]
    [InlineData("http://[::1]:5001", true, "http://[::1]:5001")]
    [InlineData("http://portal.company.internal", false, "")]
    [InlineData("https://user:pass@portal.company.internal", false, "")]
    [InlineData("https://portal.company.internal/studio", false, "")]
    [InlineData("https://portal.company.internal?x=1", false, "")]
    [InlineData("ftp://portal.company.internal", false, "")]
    [InlineData("", false, "")]
    public void OriginValidator_EnforcesHttpsAndLoopbackOnlyHttp(string raw, bool expectedOk, string expectedOrigin)
    {
        var ok = HostedOriginValidator.TryNormalizeTrustedOrigin(raw, out var origin, out _, out var error);
        Assert.Equal(expectedOk, ok);
        if (expectedOk)
        {
            Assert.Equal(expectedOrigin, origin);
            Assert.Null(error);
        }
        else
        {
            Assert.False(string.IsNullOrWhiteSpace(error));
        }
    }

    [Fact]
    public void ServerRegistry_PersistsTrustedAliasesOnly_NeverTokens()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"etlsql_hosted_servers_{Guid.NewGuid():N}.json");
        try
        {
            var store = new HostedServerRegistryStore(tempFile);
            Assert.Empty(store.Load());

            store.AddOrUpdate("prod", "Production Portal", "https://portal.company.internal:443/", "Prod");
            store.AddOrUpdate("dev", "Local Dev", "http://127.0.0.1:5001");

            var loaded = store.Load();
            Assert.Equal(2, loaded.Count);
            Assert.Equal("https://portal.company.internal", store.FindByAlias("PROD")?.Origin);
            Assert.Equal("http://127.0.0.1:5001", store.FindByAlias("dev")?.Origin);

            var rawJson = File.ReadAllText(tempFile);
            Assert.DoesNotContain("token", rawJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("password", rawJson, StringComparison.OrdinalIgnoreCase);

            Assert.True(store.Remove("dev"));
            Assert.Single(store.Load());
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }

    [Fact]
    public async Task SessionService_DefaultsToLocal_CoalescesSingleFlightRefresh_AndStaysHostedOnExpiry()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero));
        var refreshCalls = 0;
        var failNextRefresh = false;

        using var handler = new StubHttpHandler(async req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path == "/api/auth/providers")
                return JsonResponse(HttpStatusCode.OK, new { localEnabled = true, oidcEnabled = true, oidcDisplayName = "OIDC", ldapEnabled = false });

            if (path == "/api/auth/client/exchange")
                return JsonResponse(HttpStatusCode.OK, new
                {
                    token = "tok-initial-12345678",
                    refreshToken = "ref-initial-12345678",
                    expiresAt = clock.GetUtcNow().AddMinutes(10).UtcDateTime,
                    sessionId = "sess-1",
                    clientId = HostedClientIds.Tui,
                    audience = "etl-sql-portal-native-api",
                    tenantId = "tenant-alpha",
                    scopes = new[] { "studio.read", "studio.run" },
                    user = new { id = 7, username = "alice", role = "Publisher", email = "alice@alpha.test", provider = "OIDC" }
                });

            if (path == "/api/studio/session")
                return JsonResponse(HttpStatusCode.OK, new
                {
                    mode = "CatalogOnly",
                    capabilities = new[] { "StudioAccess", "ScriptRead", "ScriptRun" },
                    sourceControlEnabled = false,
                    draftRecovery = false,
                    privateDrafts = false,
                    maxScriptCharacters = 100000
                });

            if (path == "/api/auth/client/refresh")
            {
                Interlocked.Increment(ref refreshCalls);
                await Task.Delay(50);
                if (failNextRefresh)
                    return JsonResponse(HttpStatusCode.Unauthorized, new { error = "refresh_revoked" });

                return JsonResponse(HttpStatusCode.OK, new
                {
                    token = "tok-refreshed-87654321",
                    refreshToken = "ref-rotated-87654321",
                    expiresAt = clock.GetUtcNow().AddMinutes(15).UtcDateTime,
                    sessionId = "sess-1",
                    clientId = HostedClientIds.Tui,
                    audience = "etl-sql-portal-native-api",
                    tenantId = "tenant-alpha",
                    scopes = new[] { "studio.read", "studio.run" },
                    user = new { id = 7, username = "alice", role = "Publisher", email = "alice@alpha.test", provider = "OIDC" }
                });
            }

            if (path == "/api/studio/pipelines/runnable")
                return JsonResponse(HttpStatusCode.OK, new { items = Array.Empty<object>(), nextAfterId = (int?)null });

            if (path == "/api/auth/client/logout")
                return new HttpResponseMessage(HttpStatusCode.NoContent);

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var http = new HttpClient(handler);
        var authenticator = new LoopbackPkceAuthenticator(async (authorizeUri, ct) =>
        {
            var q = ParseQuery(authorizeUri.Query);
            using var cb = new HttpClient();
            await cb.GetAsync($"{q["redirect_uri"]}?code=auth-code-12345678&state={Uri.EscapeDataString(q["state"])}", ct);
            return true;
        });

        await using var sessions = new HostedClientSessionService(
            clientId: HostedClientIds.Tui,
            requestedScope: HostedClientScopes.TuiDefault,
            authenticator: authenticator,
            httpClient: http,
            clock: clock);

        // Startup defaults to Local (Offline)
        Assert.False(sessions.IsHosted);
        Assert.Equal("Local (Offline)", sessions.FormatStatusLabel());

        var server = new HostedServerEntry("dev", "Dev Portal", "http://127.0.0.1:5001");
        var session = await sessions.SignInAsync(server);
        Assert.True(sessions.IsHosted);
        Assert.Equal("Hosted: Dev Portal (127.0.0.1:5001 / tenant-alpha) · alice", sessions.FormatStatusLabel());
        Assert.Equal("tok-initial-12345678", session.Token);

        // Advance past expiry and fire two concurrent requests -> must coalesce to 1 refresh call
        clock.Advance(TimeSpan.FromMinutes(10));
        var client = new HostedPipelineClient(sessions);
        await Task.WhenAll(client.GetRunnablePipelinesAsync(), client.GetRunnablePipelinesAsync());
        Assert.Equal(1, refreshCalls);
        Assert.Equal("tok-refreshed-87654321", sessions.Session?.Token);

        // Revoked refresh -> clears session, throws HostedSessionExpiredException, stays Hosted (no Local fallback)
        failNextRefresh = true;
        clock.Advance(TimeSpan.FromMinutes(20));
        await Assert.ThrowsAsync<HostedSessionExpiredException>(() => client.GetRunnablePipelinesAsync());
        Assert.Null(sessions.Session);
        Assert.True(sessions.IsHosted);
        Assert.Equal("Hosted: Dev Portal (Sign in required)", sessions.FormatStatusLabel());

        // Explicit disconnect returns to Local (Offline)
        await sessions.DisconnectAsync();
        Assert.False(sessions.IsHosted);
        Assert.Equal("Local (Offline)", sessions.FormatStatusLabel());
    }

    [Fact]
    public async Task RunCoordinator_Dropped202_RecoversOriginalRunAndPreventsConcurrentSubmission()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero));
        const string currentUser = "alice";
        var dispatchStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDispatch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? capturedKey = null;
        var postRunsCount = 0;

        using var handler = new StubHttpHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path == "/api/auth/providers")
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, new { localEnabled = true, oidcEnabled = true, oidcDisplayName = "OIDC", ldapEnabled = false }));

            if (path == "/api/auth/client/exchange")
            {
                var id = currentUser == "alice" ? 7 : 8;
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, new
                {
                    token = $"tok-{currentUser}-12345678",
                    refreshToken = $"ref-{currentUser}-12345678",
                    expiresAt = clock.GetUtcNow().AddMinutes(10).UtcDateTime,
                    sessionId = $"sess-{currentUser}",
                    clientId = HostedClientIds.Tui,
                    audience = "etl-sql-portal-native-api",
                    tenantId = "tenant-alpha",
                    scopes = new[] { "studio.read", "studio.run" },
                    user = new { id, username = currentUser, role = "Publisher", email = $"{currentUser}@alpha.test", provider = "OIDC" }
                }));
            }

            if (path == "/api/studio/session")
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, new
                {
                    mode = "CatalogOnly",
                    capabilities = new[] { "StudioAccess", "ScriptRead", "ScriptRun" },
                    sourceControlEnabled = false,
                    draftRecovery = false,
                    privateDrafts = false,
                    maxScriptCharacters = 100000
                }));

            if (path == "/api/studio/pipelines/1/runs" && req.Method == HttpMethod.Post)
            {
                postRunsCount++;
                capturedKey = req.Headers.GetValues("Idempotency-Key").Single();
                dispatchStarted.TrySetResult();
                return DropResponseAsync();
            }

            if (path == $"/api/studio/pipelines/1/runs/by-request/{capturedKey}")
            {
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, new
                {
                    runId = 42L,
                    pipelineId = 1,
                    version = 1L,
                    scriptHash = "sha256:abc123",
                    jobName = "PIPELINE:1",
                    replayed = true
                }));
            }

            if (path == "/api/studio/pipelines/1/runs/42")
            {
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, new
                {
                    runId = 42L,
                    pipelineId = 1,
                    jobName = "PIPELINE:1",
                    status = "SUCCESS",
                    isTerminal = true,
                    startTime = clock.GetUtcNow().UtcDateTime,
                    endTime = clock.GetUtcNow().UtcDateTime,
                    errorMessage = (string?)null,
                    rowsProcessed = 5L,
                    scriptHash = "sha256:abc123",
                    logTruncated = false,
                    statements = new[] { new { statement = "SELECT 1;", durationMs = 12L, rowsProcessed = 5L, failed = false } }
                }));
            }

            if (path == "/api/auth/client/logout")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        using var http = new HttpClient(handler);
        var authenticator = new LoopbackPkceAuthenticator(async (authorizeUri, ct) =>
        {
            var q = ParseQuery(authorizeUri.Query);
            using var cb = new HttpClient();
            await cb.GetAsync($"{q["redirect_uri"]}?code=code-12345678&state={Uri.EscapeDataString(q["state"])}", ct);
            return true;
        });

        await using var sessions = new HostedClientSessionService(
            authenticator: authenticator, httpClient: http, clock: clock);
        var client = new HostedPipelineClient(sessions);
        var coordinator = new HostedRunCoordinator(sessions, client, clock,
            initialPollDelay: TimeSpan.FromMilliseconds(10), maxPollDelay: TimeSpan.FromMilliseconds(20));

        await sessions.SignInAsync(new HostedServerEntry("dev", "Dev", "http://127.0.0.1:5001"));
        var pipeline = new RunnablePipelineDto(1, "FinanceCheck.etlsql", "/Finance", 1, CanAuthor: false);

        var firstRun = coordinator.SubmitNewRunAsync(pipeline);
        await dispatchStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            var concurrent = await coordinator.SubmitNewRunAsync(pipeline);
            Assert.Equal(HostedRunOutcomeKind.Error, concurrent.Kind);
            Assert.Equal(1, postRunsCount);
        }
        finally { releaseDispatch.TrySetResult(); }

        var outcome = await firstRun;
        Assert.Equal(HostedRunOutcomeKind.Completed, outcome.Kind);
        Assert.Equal(42L, outcome.FinalRun?.RunId);
        Assert.Equal(1, postRunsCount);
        Assert.True(Guid.TryParse(capturedKey, out _));
        Assert.Null(coordinator.GetPendingSubmissionForCurrentCaller());

        async Task<HttpResponseMessage> DropResponseAsync()
        {
            await releaseDispatch.Task;
            throw new HttpRequestException("Simulated connection reset after Portal accepted run");
        }
    }

    [Fact]
    public async Task TuiHostedController_PreservesDirtyBufferAndDiscardsWorkPreparedForAnotherTarget()
    {
        var logs = new List<string>();
        var statuses = new List<string>();
        var chooserChoices = new Queue<int>();
        var clock = new TestClock(new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero));

        using var handler = new StubHttpHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path == "/api/auth/providers")
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, new { localEnabled = true, oidcEnabled = true, oidcDisplayName = "OIDC", ldapEnabled = false }));
            if (path == "/api/auth/client/exchange")
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, new
                {
                    token = "tok-tui-12345678",
                    refreshToken = "ref-tui-12345678",
                    expiresAt = clock.GetUtcNow().AddMinutes(10).UtcDateTime,
                    sessionId = "sess-tui",
                    clientId = HostedClientIds.Tui,
                    audience = "etl-sql-portal-native-api",
                    tenantId = "tenant-alpha",
                    scopes = new[] { "studio.read", "studio.run" },
                    user = new { id = 7, username = "alice", role = "Publisher", email = "alice@alpha.test", provider = "OIDC" }
                }));
            if (path == "/api/studio/session")
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, new
                {
                    mode = "CatalogOnly",
                    capabilities = new[] { "StudioAccess", "ScriptRead", "ScriptRun" },
                    sourceControlEnabled = false,
                    draftRecovery = false,
                    privateDrafts = false,
                    maxScriptCharacters = 100000
                }));
            if (path == "/api/studio/pipelines/runnable")
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, new
                {
                    items = new[] { new { id = 1, name = "FinanceCheck.etlsql", folderPath = "/Finance", version = 3L, canAuthor = false } },
                    nextAfterId = (int?)null
                }));
            if (path == "/api/studio/pipelines/1/revision")
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, new
                {
                    id = 1,
                    name = "FinanceCheck.etlsql",
                    folderPath = "/Finance",
                    version = 3L,
                    canAuthor = false
                }));
            if (path == "/api/auth/client/logout")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        using var http = new HttpClient(handler);
        var authenticator = new LoopbackPkceAuthenticator(async (authorizeUri, ct) =>
        {
            var q = ParseQuery(authorizeUri.Query);
            using var cb = new HttpClient();
            await cb.GetAsync($"{q["redirect_uri"]}?code=code-tui-12345678&state={Uri.EscapeDataString(q["state"])}", ct);
            return true;
        });

        await using var sessions = new HostedClientSessionService(
            authenticator: authenticator, httpClient: http, clock: clock);
        await using var controller = new TuiHostedExecutionController(
            (m, _) => logs.Add(m),
            s => statuses.Add(s),
            (_, _) => Task.FromResult(chooserChoices.Count > 0 ? chooserChoices.Dequeue() : -1),
            (_, _, _) => Task.FromResult<string?>(null),
            sessions);

        Assert.False(controller.IsHosted);
        Assert.True(await controller.ConnectToServerAsync(new HostedServerEntry("dev", "Dev", "http://127.0.0.1:5001")));
        Assert.True(controller.IsHosted);

        // Dirty buffer -> user picks Cancel (index 1) -> returns null, no submission made
        chooserChoices.Enqueue(1);
        var work = await controller.PrepareHostedRunAsync("FinanceCheck.etlsql", isDirty: true);
        Assert.Null(work);
        Assert.Contains(statuses, s => s.Contains("preserved", StringComparison.OrdinalIgnoreCase));

        var prepared = await controller.PrepareHostedRunAsync("FinanceCheck.etlsql", isDirty: false);
        Assert.NotNull(prepared);
        Assert.True(await controller.ConnectToServerAsync(new HostedServerEntry("other", "Other", "http://127.0.0.1:5002")));
        var staleOutcome = await prepared(CancellationToken.None);
        Assert.Equal(HostedRunOutcomeKind.SessionExpired, staleOutcome.Kind);
        Assert.Null(controller.Coordinator.GetPendingSubmissionForCurrentCaller());
    }

    [Fact]
    public async Task DisconnectDuringSignInDiscardsAndRevokesTheLateSession()
    {
        var studioRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var studioResponse = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var logoutCalls = 0;
        using var handler = new StubHttpHandler(async request =>
        {
            switch (request.RequestUri!.AbsolutePath)
            {
                case "/api/auth/providers":
                    return JsonResponse(HttpStatusCode.OK, new { oidcEnabled = true });
                case "/api/auth/client/exchange":
                    return RaceToken("dev");
                case "/api/studio/session":
                    studioRequested.TrySetResult();
                    return await studioResponse.Task;
                case "/api/auth/client/logout":
                    Interlocked.Increment(ref logoutCalls);
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                default:
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        });
        using var http = new HttpClient(handler);
        await using var sessions = new HostedClientSessionService(httpClient: http, authenticator: RaceBrowser());
        var signIn = sessions.SignInAsync(new HostedServerEntry("dev", "Dev", "http://127.0.0.1:5001"));
        await studioRequested.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await sessions.DisconnectAsync();
        studioResponse.SetResult(JsonResponse(HttpStatusCode.OK, new { mode = "CatalogOnly" }));

        await Assert.ThrowsAsync<HostedSessionExpiredException>(() => signIn);
        Assert.Null(sessions.Session);
        Assert.False(sessions.IsHosted);
        Assert.Equal(1, logoutCalls);
    }

    [Fact]
    public async Task LateUnauthorizedResponseIsDiscardedAfterSwitchAndNeverRefreshesTheNewSession()
    {
        var oldRequestSent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldResponse = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var unexpectedCalls = 0;
        using var handler = new StubHttpHandler(async request =>
        {
            var uri = request.RequestUri!;
            switch (uri.AbsolutePath)
            {
                case "/api/auth/providers":
                    return JsonResponse(HttpStatusCode.OK, new { oidcEnabled = true });
                case "/api/auth/client/exchange":
                    return RaceToken(uri.Port == 5001 ? "dev" : "other");
                case "/api/studio/session":
                    return JsonResponse(HttpStatusCode.OK, new { mode = "CatalogOnly" });
                case "/api/auth/client/logout":
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                case "/api/studio/pipelines/runnable" when uri.Port == 5001:
                    oldRequestSent.TrySetResult();
                    return await oldResponse.Task;
                default:
                    Interlocked.Increment(ref unexpectedCalls);
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        });
        using var http = new HttpClient(handler);
        await using var sessions = new HostedClientSessionService(httpClient: http, authenticator: RaceBrowser());
        var initial = await sessions.SignInAsync(new HostedServerEntry("dev", "Dev", "http://127.0.0.1:5001"));
        var client = new HostedPipelineClient(sessions);
        var oldList = client.GetRunnablePipelinesAsync(expectedSession: initial);
        await oldRequestSent.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var current = await sessions.SignInAsync(new HostedServerEntry("other", "Other", "http://127.0.0.1:5002"));
        oldResponse.SetResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));

        await Assert.ThrowsAsync<HostedSessionExpiredException>(() => oldList);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sessions.AuthorizedSendAsync(_ => new HttpRequestMessage(HttpMethod.Get, initial.Server.Origin), expectedSession: current));
        Assert.Equal(0, unexpectedCalls);
        Assert.Same(current, sessions.Session);
        Assert.DoesNotContain(current.Token, current.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(current.RefreshToken, System.Text.Json.JsonSerializer.Serialize(current), StringComparison.Ordinal);
    }

    private static LoopbackPkceAuthenticator RaceBrowser() => new(async (authorizeUri, ct) =>
    {
        var query = ParseQuery(authorizeUri.Query);
        using var http = new HttpClient();
        using var response = await http.GetAsync($"{query["redirect_uri"]}?code=race-auth-code&state={Uri.EscapeDataString(query["state"])}", ct);
        return response.IsSuccessStatusCode;
    });

    private static HttpResponseMessage RaceToken(string session) => JsonResponse(HttpStatusCode.OK, new
    {
        token = $"test-access-token-{session}",
        refreshToken = $"test-refresh-token-{session}",
        expiresAt = DateTime.UtcNow.AddMinutes(10),
        sessionId = session,
        clientId = HostedClientIds.Tui,
        audience = "etl-sql-portal-native-api",
        tenantId = "tenant-alpha",
        scopes = new[] { "studio.read", "studio.run" },
        user = new { id = 7, username = "alice" }
    });

    [Fact]
    public void SecretRedactor_StripsTokensAndVerifiers()
    {
        var raw = "Error with tok-secret-12345678 and ref-secret-87654321";
        var redacted = HostedSecretRedactor.Redact(raw, ["tok-secret-12345678", "ref-secret-87654321"]);
        Assert.DoesNotContain("tok-secret-12345678", redacted);
        Assert.DoesNotContain("ref-secret-87654321", redacted);
        Assert.Contains("[REDACTED]", redacted);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, object payload) =>
        new(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, Json), Encoding.UTF8, "application/json")
        };

    private static Dictionary<string, string> ParseQuery(string rawQuery)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in rawQuery.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = pair.IndexOf('=');
            if (idx > 0)
                result[Uri.UnescapeDataString(pair[..idx])] = Uri.UnescapeDataString(pair[(idx + 1)..]);
        }
        return result;
    }

    [Fact]
    public async Task Coordinator_StopBoundToRunA_DoesNotCancelRunB_WhenRunAFinishesAndRunBStarts()
    {
        var runAGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runBGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runBRunning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nextRunId = 100L;
        var cancelledRunIds = new List<long>();

        using var http = new HttpClient(new StubHttpHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path == "/api/auth/providers")
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, new { localEnabled = true, oidcEnabled = false, ldapEnabled = false }));
            if (path == "/api/auth/client/exchange")
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, new
                {
                    token = "tok-race-a",
                    refreshToken = "ref-race-a",
                    expiresAt = DateTime.UtcNow.AddHours(1),
                    sessionId = "sess-race",
                    clientId = HostedClientIds.DesktopStudio,
                    audience = "etl-sql-portal-native-api",
                    tenantId = "tenant-alpha",
                    scopes = new[] { "studio.read", "studio.run" },
                    user = new { id = 10, username = "alice", email = "alice@example.test" }
                }));
            if (path == "/api/studio/session")
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, new
                {
                    mode = "CatalogOnly",
                    capabilities = new[] { "StudioAccess", "ScriptRun" },
                    sourceControlEnabled = false,
                    draftRecovery = false,
                    privateDrafts = false,
                    maxScriptCharacters = 100000
                }));
            if (req.Method == HttpMethod.Post && path == "/api/studio/pipelines/7/runs")
            {
                var runId = Interlocked.Increment(ref nextRunId);
                return Task.FromResult(JsonResponse(HttpStatusCode.Accepted, new
                {
                    runId,
                    pipelineId = 7,
                    version = 1,
                    scriptHash = "sha256:race",
                    jobName = "PIPELINE:7",
                    replayed = false
                }));
            }
            if (req.Method == HttpMethod.Get && path == "/api/studio/pipelines/7/runs/101")
            {
                var done = runAGate.Task.IsCompleted;
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, new
                {
                    runId = 101L,
                    pipelineId = 7,
                    jobName = "PIPELINE:7",
                    status = done ? "SUCCESS" : "RUNNING",
                    isTerminal = done,
                    startTime = DateTime.UtcNow,
                    endTime = done ? DateTime.UtcNow : (DateTime?)null,
                    errorMessage = (string?)null,
                    rowsProcessed = done ? 1 : 0,
                    scriptHash = "sha256:race",
                    logTruncated = false,
                    statements = Array.Empty<object>()
                }));
            }
            if (req.Method == HttpMethod.Get && path == "/api/studio/pipelines/7/runs/102")
            {
                runBRunning.TrySetResult();
                var done = runBGate.Task.IsCompleted;
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, new
                {
                    runId = 102L,
                    pipelineId = 7,
                    jobName = "PIPELINE:7",
                    status = done ? "SUCCESS" : "RUNNING",
                    isTerminal = done,
                    startTime = DateTime.UtcNow,
                    endTime = done ? DateTime.UtcNow : (DateTime?)null,
                    errorMessage = (string?)null,
                    rowsProcessed = done ? 2 : 0,
                    scriptHash = "sha256:race",
                    logTruncated = false,
                    statements = Array.Empty<object>()
                }));
            }
            if (req.Method == HttpMethod.Post && path.EndsWith("/cancel", StringComparison.Ordinal))
            {
                var runId = long.Parse(path.Split('/')[^2]);
                lock (cancelledRunIds) cancelledRunIds.Add(runId);
                return Task.FromResult(JsonResponse(HttpStatusCode.Accepted, new { runId, pipelineId = 7 }));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }));

        var auth = new LoopbackPkceAuthenticator(async (authorizeUri, ct) =>
        {
            var q = ParseQuery(authorizeUri.Query);
            using var cb = new HttpClient();
            await cb.GetAsync($"{q["redirect_uri"]}?code=code-race&state={Uri.EscapeDataString(q["state"])}", ct);
            return true;
        });

        var tempReg = Path.Combine(Path.GetTempPath(), $"etlsql_race_{Guid.NewGuid():N}.json");
        try
        {
            var registry = new HostedServerRegistryStore(tempReg);
            var server = registry.AddOrUpdate("dev", "Dev", "https://portal.race.test");
            await using var sessions = new HostedClientSessionService(
                HostedClientIds.DesktopStudio, HostedClientScopes.TuiDefault, registry, auth, http);
            var session = await sessions.SignInAsync(server);
            var client = new HostedPipelineClient(sessions);
            var coordinator = new HostedRunCoordinator(
                sessions, client, initialPollDelay: TimeSpan.FromMilliseconds(20), maxPollDelay: TimeSpan.FromMilliseconds(40));

            var pipeline = new RunnablePipelineDto(7, "RaceCheck.etlsql", "/Finance", 1, true);

            // Start run A (runId = 101) and capture its exact stop target.
            long? runAId = null;
            var runATask = coordinator.SubmitNewRunAsync(pipeline, new HostedRunCallbacks(OnAccepted: r => runAId = r.RunId));
            for (var i = 0; i < 40 && runAId is null; i++) await Task.Delay(20);
            Assert.Equal(101L, runAId);
            var stopTargetForRunA = new HostedRunStopTarget(7, 101L, session.Server.Origin, session.TenantId, session.User.Id);

            // Simulate the race: run A finishes and run B (runId = 102) starts before Stop(run A) reaches the coordinator.
            runAGate.TrySetResult();
            var outcomeA = await runATask;
            Assert.Equal(HostedRunOutcomeKind.Completed, outcomeA.Kind);

            var runBTask = coordinator.SubmitNewRunAsync(pipeline);
            await runBRunning.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(102L, coordinator.ActiveRun?.RunId);

            // Stop bound to run A must NOT cancel run B.
            var stopMsg = await coordinator.RequestStopAsync(expectedTarget: stopTargetForRunA, expectedSession: session);
            Assert.Contains("no longer active", stopMsg, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(cancelledRunIds);
            Assert.False(coordinator.ActiveRun!.CancelRequested);

            // Run B continues and completes cleanly.
            runBGate.TrySetResult();
            var outcomeB = await runBTask;
            Assert.Equal(HostedRunOutcomeKind.Completed, outcomeB.Kind);
            Assert.Equal("SUCCESS", outcomeB.FinalRun?.Status);
            Assert.Equal(102L, outcomeB.FinalRun?.RunId);
        }
        finally
        {
            try { File.Delete(tempReg); } catch { }
        }
    }

    private sealed class StubHttpHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handler(request);
    }

    private sealed class TestClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now += delta;
    }
}
