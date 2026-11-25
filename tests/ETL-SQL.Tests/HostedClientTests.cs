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
        Assert.Equal("Hosted: Dev Portal (tenant-alpha) · alice", sessions.FormatStatusLabel());
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
    public async Task RunCoordinator_Dropped202_RecoversExactRunIdWithSameKey_AndScopesPendingToCaller()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero));
        var currentUser = "alice";
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
                // Simulate dropped 202 response after Portal accepted run #42
                throw new HttpRequestException("Simulated connection reset after Portal accepted run");
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

        var outcome = await coordinator.SubmitNewRunAsync(pipeline);
        Assert.Equal(HostedRunOutcomeKind.Completed, outcome.Kind);
        Assert.Equal(42L, outcome.FinalRun?.RunId);
        Assert.Equal(1, postRunsCount);
        Assert.True(Guid.TryParse(capturedKey, out _));
        Assert.Null(coordinator.GetPendingSubmissionForCurrentCaller());
    }

    [Fact]
    public async Task TuiHostedController_DirtyBufferCancel_PreservesLocalBuffer_AndBlocksPartialExecution()
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
    }

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
