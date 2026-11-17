using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Profiling;
using ETL_SQL.Core.Quality;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Proves OR04: Triage, OrchestratorPollerService, Subscriptions, and SHARED:/SECRET: governance
/// federation operate strictly over HTTP via <see cref="OrchestratorProxyService"/> when Portal
/// and Orchestrator run on separate hosts without sharing a local SQLite database file.
/// </summary>
public sealed class OrchestratorRemoteBridgeTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task RemoteTriage_ReadsBoardAndRunDetailOverHttp_WithoutLocalSqlite()
    {
        using var orchestrator = new RemoteOrchestratorFactory();
        using var portal = new SeparateHostPortalFactory(orchestrator);
        using var client = portal.CreateClient();
        var adminToken = await LoginAdminAsync(client);

        var runName = $"remote_fail_{Guid.NewGuid():N}"[..20];
        var missedName = $"remote_missed_{Guid.NewGuid():N}"[..20];
        long runId;
        using (var scope = orchestrator.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IJobHistoryStore>();
            await store.SaveJobAsync(new JobDefinition(
                missedName, "SELECT 1;", 1, "HOUR", null,
                DateTime.Now.AddHours(-2), DateTime.Now.AddMinutes(-30), true,
                DisplayName: "Remote Missed Job", TenantId: "portal-host"));
            await store.SaveJobAsync(new JobDefinition(
                runName, "SELECT 1;", 1, "DAY", null,
                null, null, true, TenantId: "portal-host"));
            var savedRunJob = await store.GetJobAsync("portal-host", runName);
            Assert.NotNull(savedRunJob);

            runId = await store.LogJobStartAsync(savedRunJob.Id);
            await store.SaveJobStatementMetricsAsync(runId,
            [
                new StatementMetricsPayload
                {
                    Statement = "SELECT 1",
                    DurationMs = 45,
                    RowsProcessed = 10,
                    QueueWaitMs = 2,
                    Failed = true
                }
            ]);
            await store.SaveJobDataQualityFailuresAsync(runId,
            [
                new DataQualityRuleFailureMetric("wh.Users", "Email", "NOT_NULL", "QUARANTINE", 4, null)
            ]);
            await store.LogJobEndAsync(runId, "FAILED", "Remote timeout from gateway");
        }

        // Confirm Portal's local SQLite Orchestrator DB does not exist.
        Assert.False(File.Exists(portal.NonExistentLocalOrchDbPath));

        using (var boardRes = await Send(client, HttpMethod.Get, "/api/orchestrator/triage", adminToken))
        {
            Assert.Equal(HttpStatusCode.OK, boardRes.StatusCode);
            var board = (await boardRes.Content.ReadFromJsonAsync<JsonObject>(Json))!;
            Assert.Contains(board["incidents"]!.AsArray(), item => item!["jobNames"]!.AsArray().Any(n => n!.GetValue<string>() == runName));
            Assert.Contains(board["missed"]!.AsArray(), item => item!["jobName"]!.GetValue<string>() == missedName);
        }

        using (var detailRes = await Send(client, HttpMethod.Get, $"/api/orchestrator/triage/runs/{runId}", adminToken))
        {
            Assert.Equal(HttpStatusCode.OK, detailRes.StatusCode);
            var detail = (await detailRes.Content.ReadFromJsonAsync<JsonObject>(Json))!;
            Assert.Equal(runId, detail["run"]!["id"]!.GetValue<long>());
            Assert.Equal(runName, detail["run"]!["jobName"]!.GetValue<string>());
            Assert.Single(detail["statements"]!.AsArray());
            Assert.Single(detail["qualityFailures"]!.AsArray());
        }

        Assert.False(File.Exists(portal.NonExistentLocalOrchDbPath));
    }

    [Fact]
    public async Task RemotePoller_DetectsCompletedJobOverHttp_AndEnqueuesReportRefresh()
    {
        using var orchestrator = new RemoteOrchestratorFactory();
        using var portal = new SeparateHostPortalFactory(orchestrator);
        using var client = portal.CreateClient();
        _ = await LoginAdminAsync(client);

        const string jobName = "remote_refresh_job";
        var scriptPath = Path.Combine(portal.TempDir, "scripts", "remote_refresh.rptsql");
        await File.WriteAllTextAsync(scriptPath, "SET REPORT TITLE = 'Remote Refresh';");

        int reportId;
        await using (var portalScope = portal.Services.CreateAsyncScope())
        {
            var db = portalScope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var adminId = await db.Users.Where(u => u.UserName == "admin").Select(u => u.Id).SingleAsync();
            var folder = new Folder { Name = "RemoteRefresh", Path = "/RemoteRefresh", OwnerId = adminId };
            db.Folders.Add(folder);
            await db.SaveChangesAsync();

            var report = new Report
            {
                TenantId = "portal-host",
                FolderId = folder.Id,
                Name = "Remote Refresh Report",
                ScriptPath = scriptPath,
                CreatedBy = adminId
            };
            db.Reports.Add(report);
            await db.SaveChangesAsync();
            reportId = report.Id;

            db.ReportJobLinks.Add(new ReportJobLink
            {
                ReportId = reportId,
                OrchestratorAlias = "default",
                JobName = jobName
            });
            await db.SaveChangesAsync();
        }

        using (var orchScope = orchestrator.Services.CreateScope())
        {
            var store = orchScope.ServiceProvider.GetRequiredService<IJobHistoryStore>();
            await store.SaveJobAsync(new JobDefinition(jobName, "SELECT 1;", 1, "HOUR", null, null, null, true, TenantId: "portal-host"));
            var job = await store.GetJobAsync("portal-host", jobName);
            Assert.NotNull(job);
            var runId = await store.LogJobStartAsync(job.Id);
            await store.LogJobEndAsync(runId, "SUCCESS");
        }

        Assert.False(File.Exists(portal.NonExistentLocalOrchDbPath));
        var poller = ActivatorUtilities.CreateInstance<OrchestratorPollerService>(portal.Services);
        await poller.PollAsync(CancellationToken.None);

        await using (var portalScope = portal.Services.CreateAsyncScope())
        {
            var db = portalScope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var link = await db.ReportJobLinks.AsNoTracking().SingleAsync(l => l.ReportId == reportId);
            Assert.NotNull(link.LastRefreshedAt);
            Assert.Single(await db.PortalExecutionJobs.AsNoTracking().Where(j => j.ReportId == reportId).ToListAsync());
        }
        Assert.False(File.Exists(portal.NonExistentLocalOrchDbPath));
    }

    [Fact]
    public async Task RemoteSubscriptions_CreateUpdateHistoryDelete_RouteThroughHttp()
    {
        using var orchestrator = new RemoteOrchestratorFactory();
        using var portal = new SeparateHostPortalFactory(orchestrator);
        using var client = portal.CreateClient();
        var adminToken = await LoginAdminAsync(client);

        // Configure SMTP shared connection + secret so subscription creation can validate SmtpAlias.
        Assert.Equal(HttpStatusCode.NoContent,
            (await Send(client, HttpMethod.Put, "/api/admin/secrets/smtp_pass", adminToken, new { value = "SmtpSecret!1" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await Send(client, HttpMethod.Put, "/api/admin/connections/corp_smtp", adminToken, new
            {
                connectorType = "SMTP",
                target = "smtp.corp.local:587",
                options = new Dictionary<string, string> { ["HOST"] = "smtp.corp.local", ["PASSWORD"] = "SECRET:smtp_pass" }
            })).StatusCode);

        int reportId;
        var scriptPath = Path.Combine(portal.TempDir, "scripts", "sub_remote.rptsql");
        await File.WriteAllTextAsync(scriptPath, "SET REPORT TITLE = 'Sub Report';");
        await using (var portalScope = portal.Services.CreateAsyncScope())
        {
            var db = portalScope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var adminId = await db.Users.Where(u => u.UserName == "admin").Select(u => u.Id).SingleAsync();
            var folder = new Folder { Name = "SubRemote", Path = "/SubRemote", OwnerId = adminId };
            db.Folders.Add(folder);
            await db.SaveChangesAsync();

            var report = new Report
            {
                TenantId = "portal-host",
                FolderId = folder.Id,
                Name = "SalesDigest",
                ScriptPath = scriptPath,
                CreatedBy = adminId
            };
            db.Reports.Add(report);
            await db.SaveChangesAsync();
            reportId = report.Id;
        }

        int subId;
        using (var createRes = await Send(client, HttpMethod.Post, "/api/subscriptions", adminToken, new
        {
            reportId,
            name = "Morning Digest",
            schedule = "Daily",
            atTime = "08:30",
            format = "PDF",
            smtpAlias = "corp_smtp",
            recipientEmail = "digest@example.test"
        }))
        {
            Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
            var created = (await createRes.Content.ReadFromJsonAsync<JsonObject>(Json))!;
            subId = created["id"]!.GetValue<int>();
        }

        var jobName = $"SUB:{subId}:SalesDigest";
        Assert.False(File.Exists(portal.NonExistentLocalOrchDbPath));

        long runId;
        using (var orchScope = orchestrator.Services.CreateScope())
        {
            var store = orchScope.ServiceProvider.GetRequiredService<IJobHistoryStore>();
            var catalog = orchScope.ServiceProvider.GetRequiredService<IJobCatalogStore>();
            var job = await store.GetJobAsync("portal-host", jobName);
            Assert.NotNull(job);
            Assert.True(job.IsEnabled);
            Assert.NotNull(await catalog.GetScheduleAsync("portal-host", $"SUBSCHED:{subId}"));
            Assert.NotNull(await catalog.GetNotificationAsync("portal-host", $"SUBNOTIFY:{subId}"));

            runId = await store.LogJobStartAsync(job.Id);
            await store.LogJobEndAsync(runId, "SUCCESS");
        }

        // History returns over HTTP via /api/portal/subscriptions/{id}/history.
        using (var histRes = await Send(client, HttpMethod.Get, $"/api/subscriptions/{subId}/history", adminToken))
        {
            Assert.Equal(HttpStatusCode.OK, histRes.StatusCode);
            var rows = (await histRes.Content.ReadFromJsonAsync<JsonArray>(Json))!;
            Assert.Contains(rows, item => item!["id"]!.GetValue<long>() == runId);
        }

        // Update subscription disables Orchestrator job over HTTP.
        long version;
        using (var getSub = await Send(client, HttpMethod.Get, $"/api/subscriptions/{subId}", adminToken))
            version = (await getSub.Content.ReadFromJsonAsync<JsonObject>(Json))!["version"]!.GetValue<long>();

        using (var updReq = Request(HttpMethod.Put, $"/api/subscriptions/{subId}", adminToken, new { isActive = false, schedule = "Weekly" }))
        {
            updReq.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
            using var updRes = await client.SendAsync(updReq);
            Assert.Equal(HttpStatusCode.OK, updRes.StatusCode);
            version = (await updRes.Content.ReadFromJsonAsync<JsonObject>(Json))!["version"]!.GetValue<long>();
        }

        using (var orchScope = orchestrator.Services.CreateScope())
        {
            var job = await orchScope.ServiceProvider.GetRequiredService<IJobHistoryStore>().GetJobAsync("portal-host", jobName);
            Assert.NotNull(job);
            Assert.False(job.IsEnabled);
        }

        // Delete removes the job and schedule from Orchestrator over HTTP.
        using (var delReq = Request(HttpMethod.Delete, $"/api/subscriptions/{subId}", adminToken))
        {
            delReq.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
            using var delRes = await client.SendAsync(delReq);
            Assert.Equal(HttpStatusCode.NoContent, delRes.StatusCode);
        }

        using (var orchScope = orchestrator.Services.CreateScope())
        {
            Assert.Null(await orchScope.ServiceProvider.GetRequiredService<IJobHistoryStore>().GetJobAsync("portal-host", jobName));
        }
        Assert.False(File.Exists(portal.NonExistentLocalOrchDbPath));
    }

    [Fact]
    public async Task RemoteGovernanceSync_PropagatesSecretsConnectionsAndAclsToOrchestratorWorker()
    {
        using var orchestrator = new RemoteOrchestratorFactory();
        using var portal = new SeparateHostPortalFactory(orchestrator);
        using var client = portal.CreateClient();
        var adminToken = await LoginAdminAsync(client);

        int groupId;
        using (var grpRes = await Send(client, HttpMethod.Post, "/api/admin/groups", adminToken, new { name = "FinanceAnalysts" }))
        {
            Assert.Equal(HttpStatusCode.Created, grpRes.StatusCode);
            groupId = (await grpRes.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();
        }

        // 1. Admin creates secret + SHARED: connection + group ACL on Portal; worker receives them immediately.
        Assert.Equal(HttpStatusCode.NoContent,
            (await Send(client, HttpMethod.Put, "/api/admin/secrets/wh_api_key", adminToken, new { value = "TopSecret#2026" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await Send(client, HttpMethod.Put, "/api/admin/connections/warehouse_sync", adminToken, new
            {
                connectorType = "Postgres",
                target = "Host=pg.internal;Database=wh",
                options = new Dictionary<string, string> { ["USERNAME"] = "svc_etl", ["PASSWORD"] = "SECRET:wh_api_key" }
            })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await Send(client, HttpMethod.Post, $"/api/admin/connections/warehouse_sync/acl", adminToken, new { groupId })).StatusCode);

        using (var orchScope = orchestrator.Services.CreateScope())
        {
            var secrets = orchScope.ServiceProvider.GetRequiredService<ISecretProvider>();
            var resolvedSecret = await secrets.ResolveAsync("wh_api_key");
            Assert.Equal("TopSecret#2026", resolvedSecret.Value);

            var catalog = orchScope.ServiceProvider.GetRequiredService<IConnectionCatalogProvider>();
            var conn = await catalog.ResolveAsync("warehouse_sync", new ExecutionIdentity
            {
                EffectiveUser = "user:analyst-1",
                RealUser = "analyst-1",
                IsAdmin = false,
                TenantId = "portal-host",
                Groups = ["FinanceAnalysts"]
            });
            Assert.Equal("Postgres", conn.ConnectorType);
            Assert.Equal("SECRET:wh_api_key", conn.Options["PASSWORD"]);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => catalog.ResolveAsync("warehouse_sync", new ExecutionIdentity
            {
                EffectiveUser = "user:outsider",
                RealUser = "outsider",
                IsAdmin = false,
                TenantId = "portal-host",
                Groups = ["OtherGroup"]
            }));
        }

        // 2. Manual full resync endpoint synchronizes secrets & connections cleanly.
        using (var syncRes = await Send(client, HttpMethod.Post, "/api/orchestrator/sync-governance", adminToken))
        {
            Assert.Equal(HttpStatusCode.OK, syncRes.StatusCode);
            var summary = (await syncRes.Content.ReadFromJsonAsync<JsonObject>(Json))!;
            Assert.True(summary["secretsSynced"]!.GetValue<int>() >= 1);
            Assert.True(summary["connectionsSynced"]!.GetValue<int>() >= 1);
            Assert.Empty(summary["warnings"]!.AsArray());
        }

        // 3. Disabling connection + deleting secret in Portal propagates to Orchestrator worker.
        Assert.Equal(HttpStatusCode.NoContent,
            (await Send(client, HttpMethod.Post, "/api/admin/connections/warehouse_sync/disable", adminToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await Send(client, HttpMethod.Delete, "/api/admin/secrets/wh_api_key", adminToken)).StatusCode);

        using (var orchScope = orchestrator.Services.CreateScope())
        {
            var secrets = (ISecretLifecycleProvider)orchScope.ServiceProvider.GetRequiredService<ISecretProvider>();
            Assert.Equal(SecretLifecycleStatus.NotFound, await secrets.GetStatusAsync("wh_api_key"));

            var catalog = (IWritableConnectionCatalogProvider)orchScope.ServiceProvider.GetRequiredService<IConnectionCatalogProvider>();
            Assert.Equal(SecretLifecycleStatus.Disabled, await catalog.GetStatusAsync("warehouse_sync"));
        }
    }

    [Fact]
    public async Task RemoteFleetAndRunWaterfallEndpoints_ReturnNodeMetricsPoolStatesAndStatementWaterfall()
    {
        using var orchestrator = new RemoteOrchestratorFactory();
        using var portal = new SeparateHostPortalFactory(orchestrator);
        using var client = portal.CreateClient();
        var adminToken = await LoginAdminAsync(client);

        long runId;
        using (var orchScope = orchestrator.Services.CreateScope())
        {
            var store = orchScope.ServiceProvider.GetRequiredService<IJobHistoryStore>();
            var job = new JobDefinition(
                "fleet_waterfall_job", "SELECT 1;", 1, "HOUR", null, null, null,
                CreatedBy: "user:admin", TenantId: "portal-host");
            await store.SaveJobAsync(job);
            var savedJob = await store.GetJobAsync("portal-host", job.Name);
            runId = await store.LogJobStartAsync(savedJob!.Id);
            await store.SaveJobStatementMetricsAsync(runId,
            [
                new StatementMetricsPayload { Statement = "SELECT 1 AS FirstStep;", DurationMs = 120, RowsProcessed = 10, Failed = false },
                new StatementMetricsPayload { Statement = "SELECT 2 AS SlowStep;", DurationMs = 840, RowsProcessed = 250, Failed = false }
            ]);
            await store.LogJobEndAsync(runId, "SUCCESS", rowsProcessed: 260);

            if (orchScope.ServiceProvider.GetService<INodeRegistryStore>() is { } nodeRegistry)
            {
                await nodeRegistry.RegisterOrRenewNodeAsync(
                    "orch-test-node:101",
                    "Orchestrator",
                    TimeSpan.FromMinutes(2),
                    JsonSerializer.Serialize(new
                    {
                        machine = "TEST-HOST",
                        pid = 101,
                        capacity = new
                        {
                            WorkingSetBytes = 100_000_000L,
                            GcHeapBytes = 50_000_000L,
                            TotalAvailableMemoryBytes = 8_000_000_000L,
                            MemoryLoadPercent = 35.5,
                            ProcessCpuPercent = 12.0,
                            HostCpuPercent = 24.0,
                            ProcessorCount = 4,
                            IsOverloaded = false
                        }
                    }));
            }
        }

        // 1. Run waterfall endpoint returns statements via Portal relay.
        using (var runRes = await Send(client, HttpMethod.Get, $"/api/orchestrator/runs/{runId}", adminToken))
        {
            Assert.Equal(HttpStatusCode.OK, runRes.StatusCode);
            var payload = (await runRes.Content.ReadFromJsonAsync<JsonObject>(Json))!;
            Assert.Equal(runId, payload["runId"]!.GetValue<long>());
            Assert.Equal("fleet_waterfall_job", payload["jobName"]!.GetValue<string>());
            var statements = payload["statements"]!.AsArray();
            Assert.Equal(2, statements.Count);
            Assert.Equal("SELECT 2 AS SlowStep;", statements[1]!["statement"]!.GetValue<string>());
            Assert.Equal(840, statements[1]!["durationMs"]!.GetValue<long>());
        }

        // 2. Cancelling a finished run relays 409 Conflict instead of claiming cancellation succeeded.
        using (var cancelRes = await Send(client, HttpMethod.Post, $"/api/orchestrator/runs/{runId}/cancel", adminToken))
        {
            Assert.Equal(HttpStatusCode.Conflict, cancelRes.StatusCode);
        }

        // 3. Fleet endpoint returns NodeHeartbeatService metrics & RelationalSandboxAdmissionLedger pool states.
        using (var fleetRes = await Send(client, HttpMethod.Get, "/api/orchestrator/fleet", adminToken))
        {
            Assert.Equal(HttpStatusCode.OK, fleetRes.StatusCode);
            var fleet = (await fleetRes.Content.ReadFromJsonAsync<JsonObject>(Json))!;
            var nodes = fleet["nodes"]!.AsArray();
            Assert.NotEmpty(nodes);
            var node = Assert.Single(nodes, item => item!["nodeId"]!.GetValue<string>() == "orch-test-node:101")!;
            Assert.Equal("TEST-HOST", node["machine"]!.GetValue<string>());
            Assert.Equal(35.5, node["capacity"]!["memoryLoadPercent"]!.GetValue<double>(), 1);
            Assert.NotNull(fleet["sandboxPools"]);
        }
    }

    private sealed class RemoteOrchestratorFactory() : OrchestratorWebFactory(requireFederatedIdentity: true)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISecretProvider>();
                services.AddSingleton<ISecretProvider>(new OsSecretStoreProvider(
                    Path.Combine(TempDir, "secrets"),
                    LocalVaultProtectionScope.CurrentUser));
            });
        }
    }

    private sealed class SeparateHostPortalFactory(OrchestratorWebFactory orchestrator) : PortalWebFactory
    {
        public string NonExistentLocalOrchDbPath => Path.Combine(TempDir, "no-shared-sqlite-orch.db");

        protected override void CustomizePortalConfig(PortalConfig config)
        {
            config.Orchestrator.DatabasePath = NonExistentLocalOrchDbPath;
            config.Orchestrator.ApiUrl = "http://orchestrator.test";
            config.Orchestrator.ApiKey = "test-orch-key-12345";
            config.Orchestrator.IdentitySigningSecret = OrchestratorWebFactory.IdentitySecret;
        }

        protected override void CustomizeServices(IServiceCollection services)
        {
            services.AddHttpClient<OrchestratorProxyService>()
                .ConfigurePrimaryHttpMessageHandler(() => orchestrator.Server.CreateHandler());
        }
    }

    private static async Task<string> LoginAdminAsync(HttpClient client)
    {
        const string password = "RemoteBridge@Tests99!";
        using var initialRes = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = "Admin@12345!" });
        Assert.Equal(HttpStatusCode.OK, initialRes.StatusCode);
        var initial = (await initialRes.Content.ReadFromJsonAsync<JsonObject>(Json))!["token"]!.GetValue<string>();
        using (var change = Request(HttpMethod.Post, "/api/auth/change-password", initial,
            new { currentPassword = "Admin@12345!", newPassword = password }))
        {
            Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(change)).StatusCode);
        }
        using var finalRes = await client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password });
        Assert.Equal(HttpStatusCode.OK, finalRes.StatusCode);
        return (await finalRes.Content.ReadFromJsonAsync<JsonObject>(Json))!["token"]!.GetValue<string>();
    }

    private static async Task<HttpResponseMessage> Send(
        HttpClient client, HttpMethod method, string path, string token, object? body = null)
    {
        using var req = Request(method, path, token, body);
        return await client.SendAsync(req);
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string token, object? body = null)
    {
        var req = new HttpRequestMessage(method, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) req.Content = JsonContent.Create(body);
        return req;
    }
}
