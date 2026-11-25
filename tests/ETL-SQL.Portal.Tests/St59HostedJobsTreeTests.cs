using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Core.Data;
using ETL_SQL.Portal.Controllers;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Middleware;
using ETL_SQL.Portal.Models;
using ETL_SQL.Portal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Focused Portal authorization and live loopback Portal + Federated Orchestrator acceptance tests
/// for the ST59 Hosted Jobs &amp; Recent Runs contract (/api/studio/hosted-jobs/*).
/// </summary>
public sealed class St59HostedJobsTreeTests
{
    private const string FinanceScript =
        "CREATE CONNECTION sample_data AS MOCKDB();\n"
        + "SELECT UserID INTO #users FROM sample_data.Users;\n"
        + "ASSERT (SELECT COUNT(*) FROM #users) > 0;\n";

    private const string SlowWaitScript = "WAITFOR DELAY '00:00:40';\n";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void NativeClientAllowlist_ScopesHostedJobsRoutesMinimallyAndDeniesAdmin()
    {
        Assert.Equal(NativeClientScopes.StudioRead, NativeClientScopeMiddleware.RequiredScope("GET", "/api/studio/hosted-jobs"));
        Assert.Equal(NativeClientScopes.StudioRead, NativeClientScopeMiddleware.RequiredScope("GET", "/api/studio/hosted-jobs/Nightly/runs"));
        Assert.Equal(NativeClientScopes.StudioRead, NativeClientScopeMiddleware.RequiredScope("GET", "/api/studio/hosted-jobs/PIPELINE%3A12/runs/99"));
        Assert.Equal(NativeClientScopes.StudioRun, NativeClientScopeMiddleware.RequiredScope("POST", "/api/studio/hosted-jobs/Nightly/trigger"));
        Assert.Equal(NativeClientScopes.StudioRun, NativeClientScopeMiddleware.RequiredScope("POST", "/api/studio/hosted-jobs/PIPELINE%3A12/runs/99/cancel"));

        // Non-allowlisted methods and admin/orchestrator routes remain denied by default.
        Assert.Null(NativeClientScopeMiddleware.RequiredScope("DELETE", "/api/studio/hosted-jobs/Nightly"));
        Assert.Null(NativeClientScopeMiddleware.RequiredScope("PUT", "/api/studio/hosted-jobs/Nightly"));
        Assert.Null(NativeClientScopeMiddleware.RequiredScope("GET", "/api/orchestrator/jobs"));
        Assert.Null(NativeClientScopeMiddleware.RequiredScope("GET", "/api/admin/users"));
    }

    [Fact]
    public async Task HostedJobsTree_LivePortalAndFederatedOrchestrator_EnforcesAuthorizationTenantIsolationRevocationAndExactCancel()
    {
        using var orchestrator = new OrchestratorWebFactory(requireFederatedIdentity: true);
        using var portal = new St59PortalFactory(orchestrator);
        using var http = portal.CreateClient();

        // 1. Seed groups, folders, users (alice = authorized, bob = Execute-only on folder without Orchestrator EXECUTE),
        //    and a second-tenant job in tenant-beta to verify tenant isolation.
        int folderId;
        int aliceId;
        string alicePrincipalKey;
        int bobId;
        string bobPrincipalKey;
        using (var scope = portal.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var admin = await db.Users.SingleAsync(u => u.UserName == "admin");
            admin.MustChangePassword = false;

            var authorsGroup = new Group { Name = "DataAuthors", Provider = "Local" };
            var readersGroup = new Group { Name = "DataReaders", Provider = "Local" };
            db.Groups.AddRange(authorsGroup, readersGroup);
            await db.SaveChangesAsync();

            db.GroupStudioCapabilities.AddRange(
                new GroupStudioCapability { GroupId = authorsGroup.Id, Capability = StudioCapabilities.StudioAccess },
                new GroupStudioCapability { GroupId = authorsGroup.Id, Capability = StudioCapabilities.ScriptRead },
                new GroupStudioCapability { GroupId = authorsGroup.Id, Capability = StudioCapabilities.ScriptRun },
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

            var alice = new PortalUser
            {
                TenantId = "portal-host",
                UserName = "alice",
                NormalizedUserName = "ALICE",
                Email = "alice@alpha.test",
                NormalizedEmail = "ALICE@ALPHA.TEST",
                IsActive = true,
                MustChangePassword = false,
                Provider = "Local",
                SecurityStamp = Guid.NewGuid().ToString("N")
            };
            var bob = new PortalUser
            {
                TenantId = "portal-host",
                UserName = "bob",
                NormalizedUserName = "BOB",
                Email = "bob@alpha.test",
                NormalizedEmail = "BOB@ALPHA.TEST",
                IsActive = true,
                MustChangePassword = false,
                Provider = "Local",
                SecurityStamp = Guid.NewGuid().ToString("N")
            };
            db.Users.AddRange(alice, bob);
            await db.SaveChangesAsync();
            aliceId = alice.Id;
            alicePrincipalKey = alice.PrincipalKey!;
            bobId = bob.Id;
            bobPrincipalKey = bob.PrincipalKey!;

            db.UserGroups.AddRange(
                new UserGroup { UserId = alice.Id, GroupId = authorsGroup.Id },
                new UserGroup { UserId = bob.Id, GroupId = readersGroup.Id });
            await db.SaveChangesAsync();
        }

        // Seed a job in tenant-beta directly in the Orchestrator to prove tenant isolation.
        using (var orchScope = orchestrator.Services.CreateScope())
        {
            var store = orchScope.ServiceProvider.GetRequiredService<IJobHistoryStore>();
            await store.SaveJobAsync(new JobDefinition(
                Name: "BetaOnlyJob",
                Script: "SELECT 1;",
                Interval: 1,
                Unit: "Days",
                AtTime: null,
                LastRun: null,
                NextRun: DateTime.UtcNow.AddHours(1),
                IsEnabled: true,
                DisplayName: "Beta Only Job",
                TenantId: "tenant-beta"));
        }

        // Login as admin to create pipelines and provision/grant EXECUTE for alice.
        var loginResp = await http.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "Admin@12345!" });
        Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);
        var adminToken = (await loginResp.Content.ReadFromJsonAsync<JsonObject>(Json))!["token"]!.GetValue<string>();

        var (financeId, financeVersion) = await CreatePipelineAsync(http, adminToken, folderId, "FinanceCheck.etlsql", FinanceScript);
        var (slowId, slowVersion) = await CreatePipelineAsync(http, adminToken, folderId, "SlowWait.etlsql", SlowWaitScript);
        await ProvisionAndGrantAsync(http, adminToken, financeId, financeVersion, alicePrincipalKey, cancelImmediately: false);
        await ProvisionAndGrantAsync(http, adminToken, slowId, slowVersion, alicePrincipalKey, cancelImmediately: true);

        // Mint real native client tokens for alice (studio.read + studio.run), aliceReadOnly (studio.read only), and bob (studio.read + studio.run).
        var aliceToken = await IssueNativeTokenAsync(portal, aliceId, [NativeClientScopes.StudioRead, NativeClientScopes.StudioRun]);
        var aliceReadOnlyToken = await IssueNativeTokenAsync(portal, aliceId, [NativeClientScopes.StudioRead]);
        var bobToken = await IssueNativeTokenAsync(portal, bobId, [NativeClientScopes.StudioRead, NativeClientScopes.StudioRun]);

        // 2. Native scope ceiling: studio.read-only token can list hosted jobs, but is denied on POST /cancel and /api/orchestrator/jobs.
        using (var readJobsReq = AuthedGet("/api/studio/hosted-jobs", aliceReadOnlyToken))
        using (var readJobsResp = await http.SendAsync(readJobsReq))
        {
            Assert.Equal(HttpStatusCode.OK, readJobsResp.StatusCode);
            var rawJson = await readJobsResp.Content.ReadAsStringAsync();
            // Zero script bodies or tenant-beta jobs in response!
            Assert.DoesNotContain("CREATE CONNECTION", rawJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("BetaOnlyJob", rawJson, StringComparison.OrdinalIgnoreCase);
            var page = JsonSerializer.Deserialize<HostedJobsPageDto>(rawJson, Json)!;
            Assert.True(page.OrchestratorOnline);
            Assert.Contains(page.Items, i => i.PipelineId == financeId && i.PortalPath.StartsWith("/", StringComparison.Ordinal) && !i.CanRun);
        }

        using (var deniedAdminReq = AuthedGet("/api/orchestrator/jobs", aliceToken))
        using (var deniedAdminResp = await http.SendAsync(deniedAdminReq))
            Assert.Equal(HttpStatusCode.Forbidden, deniedAdminResp.StatusCode);

        // 3. Authorized user (alice): list jobs, list runs, read statement logs.
        long financeRunId;
        using (var aliceJobsReq = AuthedGet("/api/studio/hosted-jobs", aliceToken))
        using (var aliceJobsResp = await http.SendAsync(aliceJobsReq))
        {
            Assert.Equal(HttpStatusCode.OK, aliceJobsResp.StatusCode);
            var page = (await aliceJobsResp.Content.ReadFromJsonAsync<HostedJobsPageDto>(Json))!;
            var fin = Assert.Single(page.Items, i => i.PipelineId == financeId);
            Assert.True(fin.CanRun);
            Assert.Equal("SUCCESS", fin.LastStatus);
            Assert.NotNull(fin.LastRunId);
            financeRunId = fin.LastRunId!.Value;
        }

        using (var runsReq = AuthedGet($"/api/studio/hosted-jobs/{Uri.EscapeDataString($"PIPELINE:{financeId}")}/runs", aliceToken))
        using (var runsResp = await http.SendAsync(runsReq))
        {
            Assert.Equal(HttpStatusCode.OK, runsResp.StatusCode);
            var runs = (await runsResp.Content.ReadFromJsonAsync<HostedJobRecentRunDto[]>(Json))!;
            Assert.NotEmpty(runs);
            Assert.Contains(runs, r => r.RunId == financeRunId && r.Status == "SUCCESS");
        }

        using (var detailReq = AuthedGet($"/api/studio/hosted-jobs/{Uri.EscapeDataString($"PIPELINE:{financeId}")}/runs/{financeRunId}", aliceToken))
        using (var detailResp = await http.SendAsync(detailReq))
        {
            Assert.Equal(HttpStatusCode.OK, detailResp.StatusCode);
            var detail = (await detailResp.Content.ReadFromJsonAsync<HostedJobRunDetailDto>(Json))!;
            Assert.Equal(financeRunId, detail.RunId);
            Assert.NotEmpty(detail.Statements);
        }

        // Tenant isolation: alice cannot read BetaOnlyJob runs.
        using (var betaReq = AuthedGet("/api/studio/hosted-jobs/BetaOnlyJob/runs", aliceToken))
        using (var betaResp = await http.SendAsync(betaReq))
        {
            if (betaResp.StatusCode == HttpStatusCode.OK)
            {
                var betaRuns = await betaResp.Content.ReadFromJsonAsync<HostedJobRecentRunDto[]>(Json);
                Assert.Empty(betaRuns!);
            }
            else
            {
                Assert.True(betaResp.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden);
            }
        }

        // 4. Denied user (bob): has no Orchestrator grant -> reading runs and cancelling are refused (403).
        using (var bobRunsReq = AuthedGet($"/api/studio/hosted-jobs/{Uri.EscapeDataString($"PIPELINE:{financeId}")}/runs", bobToken))
        using (var bobRunsResp = await http.SendAsync(bobRunsReq))
            Assert.Equal(HttpStatusCode.Forbidden, bobRunsResp.StatusCode);

        // 5. Exact selected run cancellation: start SlowWait as alice, cancel that exact runId via hosted-jobs endpoint,
        //    and verify wrong jobName returns 404 without cancelling it.
        long slowRunId;
        using (var submitSlowReq = new HttpRequestMessage(HttpMethod.Post, $"/api/studio/pipelines/{slowId}/runs")
        {
            Content = JsonContent.Create(new { version = slowVersion })
        })
        {
            submitSlowReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", aliceToken);
            submitSlowReq.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("D"));
            using var submitSlowResp = await http.SendAsync(submitSlowReq);
            Assert.Equal(HttpStatusCode.Accepted, submitSlowResp.StatusCode);
            slowRunId = (await submitSlowResp.Content.ReadFromJsonAsync<PipelineRunSubmittedDto>(Json))!.RunId;
        }

        var slowJobPath = $"/api/studio/hosted-jobs/{Uri.EscapeDataString($"PIPELINE:{slowId}")}";
        using (var grant = new HttpRequestMessage(HttpMethod.Put,
            $"/api/orchestrator/authorization/JOB/{Uri.EscapeDataString($"PIPELINE:{slowId}")}/USER/{bobPrincipalKey}")
        {
            Content = JsonContent.Create(new { permission = "READ" })
        })
        {
            grant.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            using var response = await http.SendAsync(grant);
            Assert.True(response.IsSuccessStatusCode);
        }

        using (var jobsReq = AuthedGet("/api/studio/hosted-jobs", bobToken))
        using (var jobsResp = await http.SendAsync(jobsReq))
        {
            Assert.Equal(HttpStatusCode.OK, jobsResp.StatusCode);
            var page = (await jobsResp.Content.ReadFromJsonAsync<HostedJobsPageDto>(Json))!;
            var slow = Assert.Single(page.Items, item => item.PipelineId == slowId);
            Assert.Equal(slowRunId, slow.ActiveRunId);
            Assert.False(slow.CanRun);
            Assert.False(slow.CanStop);
        }

        using (var runsReq = AuthedGet($"{slowJobPath}/runs", aliceToken))
        using (var runsResp = await http.SendAsync(runsReq))
        {
            Assert.Equal(HttpStatusCode.OK, runsResp.StatusCode);
            var runs = (await runsResp.Content.ReadFromJsonAsync<HostedJobRecentRunDto[]>(Json))!;
            Assert.True(Assert.Single(runs, run => run.RunId == slowRunId).CanStop);
        }

        using (var runsReq = AuthedGet($"{slowJobPath}/runs", bobToken))
        using (var runsResp = await http.SendAsync(runsReq))
        {
            Assert.Equal(HttpStatusCode.OK, runsResp.StatusCode);
            var runs = (await runsResp.Content.ReadFromJsonAsync<HostedJobRecentRunDto[]>(Json))!;
            Assert.False(Assert.Single(runs, run => run.RunId == slowRunId).CanStop);
        }

        using (var bobCancel = AuthedPost($"{slowJobPath}/runs/{slowRunId}/cancel", bobToken))
        using (var response = await http.SendAsync(bobCancel))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // A different job at the same run ID cannot signal this run.
        using (var wrongJobCancelReq = AuthedPost($"/api/studio/hosted-jobs/{Uri.EscapeDataString($"PIPELINE:{financeId}")}/runs/{slowRunId}/cancel", aliceToken))
        using (var wrongJobCancelResp = await http.SendAsync(wrongJobCancelReq))
            Assert.Equal(HttpStatusCode.NotFound, wrongJobCancelResp.StatusCode);

        // Exact job + exact runId cancels cleanly.
        using (var exactCancelReq = AuthedPost($"/api/studio/hosted-jobs/{Uri.EscapeDataString($"PIPELINE:{slowId}")}/runs/{slowRunId}/cancel", aliceToken))
        using (var exactCancelResp = await http.SendAsync(exactCancelReq))
            Assert.Equal(HttpStatusCode.Accepted, exactCancelResp.StatusCode);

        // Poll until terminal CANCELLED.
        string? finalStatus = null;
        for (var i = 0; i < 60; i++)
        {
            using var pollReq = AuthedGet($"/api/studio/hosted-jobs/{Uri.EscapeDataString($"PIPELINE:{slowId}")}/runs/{slowRunId}", aliceToken);
            using var pollResp = await http.SendAsync(pollReq);
            if (pollResp.IsSuccessStatusCode)
            {
                var detail = await pollResp.Content.ReadFromJsonAsync<HostedJobRunDetailDto>(Json);
                if (detail is not null && detail.IsTerminal)
                {
                    finalStatus = detail.Status;
                    break;
                }
            }
            await Task.Delay(100);
        }
        Assert.Equal("CANCELLED", finalStatus);

        // 6. Grant revocation: revoke alice's Orchestrator EXECUTE grant -> subsequent Run is refused (403).
        using (var revokeReq = new HttpRequestMessage(HttpMethod.Delete,
            $"/api/orchestrator/authorization/JOB/{Uri.EscapeDataString($"PIPELINE:{financeId}")}/USER/{alicePrincipalKey}"))
        {
            revokeReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            using var revokeResp = await http.SendAsync(revokeReq);
            Assert.True(revokeResp.IsSuccessStatusCode, $"Failed to revoke EXECUTE grant: {(int)revokeResp.StatusCode}");
        }

        using (var afterRevokeReq = new HttpRequestMessage(HttpMethod.Post, $"/api/studio/pipelines/{financeId}/runs")
        {
            Content = JsonContent.Create(new { version = financeVersion })
        })
        {
            afterRevokeReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", aliceToken);
            afterRevokeReq.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("D"));
            using var afterRevokeResp = await http.SendAsync(afterRevokeReq);
            Assert.Equal(HttpStatusCode.Forbidden, afterRevokeResp.StatusCode);
        }
    }

    [Fact]
    public async Task HostedJobsTree_LegacyOrchestratorDoesNotAuthorizeThePortalCaller()
    {
        using var orchestrator = new OrchestratorWebFactory();
        using var portal = new St59PortalFactory(orchestrator);
        using var http = portal.CreateClient();

        using (var scope = portal.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var admin = await db.Users.SingleAsync(u => u.UserName == "admin");
            admin.MustChangePassword = false;
            await db.SaveChangesAsync();
        }

        using var login = await http.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "Admin@12345!" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = (await login.Content.ReadFromJsonAsync<JsonObject>(Json))!["token"]!.GetValue<string>();

        using var list = AuthedGet("/api/studio/hosted-jobs", token);
        using var listResult = await http.SendAsync(list);
        Assert.Equal(HttpStatusCode.OK, listResult.StatusCode);
        var page = (await listResult.Content.ReadFromJsonAsync<HostedJobsPageDto>(Json))!;
        Assert.False(page.OrchestratorOnline);
        Assert.Empty(page.Items);

        using var trigger = AuthedPost("/api/studio/hosted-jobs/LegacyJob/trigger", token);
        using var triggerResult = await http.SendAsync(trigger);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, triggerResult.StatusCode);
        var refusal = await triggerResult.Content.ReadFromJsonAsync<PipelineRunRefusalDto>(Json);
        Assert.Equal("federated_orchestrator_required", refusal?.Code);
    }

    private static HttpRequestMessage AuthedGet(string path, string token)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    }

    private static HttpRequestMessage AuthedPost(string path, string token)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    }

    private static async Task<string> IssueNativeTokenAsync(St59PortalFactory portal, int userId, string[] scopes)
    {
        using var scope = portal.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<TokenService>();
        var user = await db.Users.SingleAsync(u => u.Id == userId);

        var sessionId = Guid.NewGuid().ToString("N");
        db.NativeClientSessions.Add(new NativeClientSession
        {
            Id = sessionId,
            TenantId = user.TenantId,
            UserId = user.Id,
            ClientId = "etlsql-vscode",
            Scopes = string.Join(' ', scopes),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        });
        await db.SaveChangesAsync();

        var groupIds = await db.UserGroups.Where(ug => ug.UserId == user.Id).Select(ug => ug.GroupId).ToListAsync();
        var capabilities = await db.GroupStudioCapabilities
            .Where(c => groupIds.Contains(c.GroupId))
            .Select(c => c.Capability)
            .Distinct()
            .ToArrayAsync();

        return tokens.GenerateNativeClientJwt(user, ["Publisher"], sessionId, "etlsql-vscode", scopes, capabilities);
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

    private sealed class St59PortalFactory(OrchestratorWebFactory orchestrator) : PortalWebFactory
    {
        protected override void CustomizePortalConfig(PortalConfig config)
        {
            config.Orchestrator.ApiUrl = "http://orchestrator.test";
            config.Orchestrator.ApiKey = "test-orch-key-12345";
            config.Orchestrator.IdentitySigningSecret = OrchestratorWebFactory.IdentitySecret;
        }

        protected override void CustomizeServices(IServiceCollection services)
        {
            _ = orchestrator.Server;
            services.AddHttpClient<OrchestratorProxyService>()
                .ConfigurePrimaryHttpMessageHandler(() => orchestrator.Server.CreateHandler());
        }
    }
}
