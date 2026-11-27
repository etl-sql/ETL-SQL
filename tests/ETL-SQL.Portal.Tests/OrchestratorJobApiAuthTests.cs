using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Observability;
using ETL_SQL.Orchestrator.Channels;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.Orchestrator.Service;
using ETL_SQL.Orchestrator.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Auth coverage for the ad-hoc job API. Proves that submitting, cancelling, and inspecting jobs all
/// require a valid X-Orchestrator-Key, while liveness/readiness probes stay open. The factory always
/// configures an API key, so the "no key" cases exercise the deny path.
///
/// <para><b>The factory defaults to legacy mode</b> — API key only, no caller assertion — so the
/// cases above prove exactly one of the two authorization models. That gap once hid a live break:
/// the Portal's job channel sent the key alone, which a federated Orchestrator refuses, and no test
/// noticed because none of them ran federated. The federated cases below exist so a new test added
/// here cannot quietly repeat that; end-to-end coverage of the channel itself lives in
/// <see cref="OrchestratorJobChannelIdentityTests"/>.</para>
/// </summary>
[Trait("Category", "Portal")]
public class OrchestratorJobApiAuthTests : IDisposable
{
    private const string ValidKey = "test-orch-key-12345";
    private readonly OrchestratorWebFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private static HttpRequestMessage Request(
        HttpMethod method,
        string uri,
        string? apiKey,
        object? body = null,
        long? version = null)
    {
        var req = new HttpRequestMessage(method, uri);
        if (apiKey != null) req.Headers.Add("X-Orchestrator-Key", apiKey);
        if (version.HasValue) req.Headers.TryAddWithoutValidation("If-Match", $"\"{version.Value}\"");
        if (body != null) req.Content = JsonContent.Create(body);
        return req;
    }

    [Fact]
    public async Task PostJob_WithoutApiKey_IsUnauthorized()
    {
        var client = _factory.CreateClient();
        using var req = Request(HttpMethod.Post, "/jobs", apiKey: null, body: new { ScriptText = "PRINT 'hi';" });
        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task PostJob_WithWrongApiKey_IsUnauthorized()
    {
        var client = _factory.CreateClient();
        using var req = Request(HttpMethod.Post, "/jobs", apiKey: "wrong-key", body: new { ScriptText = "PRINT 'hi';" });
        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task PostJob_WithValidApiKey_IsAccepted()
    {
        var client = _factory.CreateClient();
        using var req = Request(HttpMethod.Post, "/jobs", apiKey: ValidKey, body: new { ScriptText = "PRINT 'hi';" });
        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
    }

    [Fact]
    public async Task CancelJob_WithoutApiKey_IsUnauthorized()
    {
        var client = _factory.CreateClient();
        using var req = Request(HttpMethod.Delete, "/jobs/deadbeef", apiKey: null);
        var res = await client.SendAsync(req);
        // Auth is checked before the job-exists lookup, so this is 401, not 404.
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task GetJobStatus_WithoutApiKey_IsUnauthorized()
    {
        var client = _factory.CreateClient();
        using var req = Request(HttpMethod.Get, "/jobs/deadbeef", apiKey: null);
        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    // ── Federated mode ────────────────────────────────────────────────────────
    //
    // The same three verbs, against an Orchestrator that requires a signed caller as well as the
    // service key. A valid key on its own is no longer enough, which is the difference the legacy
    // cases above cannot see.

    [Theory]
    [InlineData("POST", "/jobs")]
    [InlineData("DELETE", "/jobs/deadbeef")]
    [InlineData("GET", "/jobs/deadbeef")]
    public async Task Federated_ValidApiKeyWithoutAnAssertion_IsUnauthorized(string method, string uri)
    {
        using var factory = new OrchestratorWebFactory(requireFederatedIdentity: true);
        using var client = factory.CreateClient();

        using var req = Request(
            new HttpMethod(method), uri, ValidKey,
            body: method == "POST" ? new { ScriptText = "PRINT 'hi';" } : null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(req)).StatusCode);
    }

    [Fact]
    public async Task Federated_ValidApiKeyWithAnAssertion_IsAccepted()
    {
        using var factory = new OrchestratorWebFactory(requireFederatedIdentity: true);
        using var client = factory.CreateClient();

        using var req = Request(HttpMethod.Post, "/jobs", ValidKey, body: new { ScriptText = "PRINT 'hi';" });
        req.Headers.Add(
            OrchestratorIdentityAssertion.HeaderName,
            OrchestratorIdentityAssertion.Create(
                new OrchestratorCaller("user", "1", "Ada Lovelace", ["OrchestratorManager"], []),
                OrchestratorWebFactory.IdentitySecret));

        Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(req)).StatusCode);
    }

    [Fact]
    public async Task ScheduledToolUseRequiresOwnerApprovalForCurrentVersionAndLiveMembership()
    {
        const string tenant = "tenant-a";
        const string name = "approved-native-workflow";
        const string alias = "scheduled_tool";
        using var factory = new OrchestratorWebFactory(requireFederatedIdentity: true);
        var catalog = new LocalToolCatalogProvider(Path.Combine(factory.TempDir, "scheduled-tools"),
            ToolCatalogOwnershipMode.PersonalWorkstation, validateOwnership: false, requireUseGrants: true);
        var image = "alpine@sha256:" + new string('d', 64);
        await catalog.StoreAsync(new ToolDefinition(alias, "CONTAINER",
            new Dictionary<string, string> { ["COMMAND"] = "echo", ["IMAGE"] = image }, false));
        await catalog.SetUseGrantAsync(alias, new(tenant, ToolPrincipalKind.Group, "finance-key"), allow: true);

        var lookup = new CurrentToolOwnerLookup(
            new ToolExecutionPrincipal(tenant, ToolPrincipalKind.User, "alice", ["finance-key"], true));
        using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Governance:Docker:AllowedImages:0"] = image }));
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IToolCatalogProvider>(catalog);
                services.AddSingleton<IScheduledToolPrincipalLookup>(lookup);
            });
        });
        using var client = host.CreateClient();
        var store = host.Services.GetRequiredService<IJobHistoryStore>();
        await store.SaveJobAsync(new JobDefinition(name, $"SET WHAT_IF ON; EXECUTE TOOL '{alias}';",
            1, "HOUR", null, null, DateTime.Now.AddHours(1), CreatedBy: "user:alice", TenantId: tenant));
        var job = (await store.GetJobAsync(tenant, name))!;
        var approvals = Assert.IsAssignableFrom<IJobToolApprovalStore>(store);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));

        async Task<HttpStatusCode> ApproveAsync(string subject, long version, bool approved = true,
            string jobName = name, string subjectType = "user", string[]? scopes = null)
        {
            using var req = Request(HttpMethod.Put, $"/api/scheduled-jobs/{jobName}/tool-approval", ValidKey,
                new { approved }, version);
            req.Headers.Add(OrchestratorIdentityAssertion.HeaderName, OrchestratorIdentityAssertion.Create(
                new OrchestratorCaller(subjectType, subject, subject, ["Admin", "OrchestratorManager"], [], tenant, scopes),
                OrchestratorWebFactory.IdentitySecret));
            using var response = await client.SendAsync(req, timeout.Token);
            return response.StatusCode;
        }

        Assert.Equal(HttpStatusCode.Forbidden, await ApproveAsync("other-admin", job.Version));
        Assert.Equal(HttpStatusCode.Conflict, await ApproveAsync("alice", job.Version + 1));
        Assert.Equal(HttpStatusCode.NoContent, await ApproveAsync("alice", job.Version));
        Assert.True(await approvals.HasToolApprovalAsync(job, timeout.Token));

        const string serviceJobName = "service-owned-workflow";
        await store.SaveJobAsync(new JobDefinition(serviceJobName, job.Script, 1, "HOUR", null, null,
            DateTime.Now.AddHours(1), CreatedBy: "service:job-bot", TenantId: tenant));
        var serviceJob = (await store.GetJobAsync(tenant, serviceJobName))!;
        Assert.Equal(HttpStatusCode.Forbidden, await ApproveAsync("job-bot", serviceJob.Version,
            jobName: serviceJobName, subjectType: "service", scopes: ["orchestrator.execute"]));
        Assert.Equal(HttpStatusCode.NoContent, await ApproveAsync("job-bot", serviceJob.Version,
            jobName: serviceJobName, subjectType: "service", scopes: ["orchestrator.publish"]));

        async Task<JobHistoryEntry> RunNextOccurrenceAsync()
        {
            var baseline = (await store.GetHistoryAsync(job.Id, 1)).FirstOrDefault()?.Id;
            // Legacy interval jobs and their due query use local wall-clock time.
            await store.UpdateJobLastRunAsync(job.Id, DateTime.Now, DateTime.Now.AddSeconds(-1));
            while (true)
            {
                var run = (await store.GetHistoryAsync(job.Id, 1)).FirstOrDefault();
                var current = await store.GetJobByIdAsync(job.Id);
                if (run is not null && run.Id != baseline && run.EndTime is not null
                    && current?.NextRun > DateTime.Now)
                    return run;

                await Task.Delay(50, timeout.Token);
            }
        }

        var allowed = await RunNextOccurrenceAsync();
        Assert.True(allowed.Status == "SUCCESS", allowed.ErrorMessage);

        lookup.Principal = lookup.Principal with { GroupIds = [] };
        var removed = await RunNextOccurrenceAsync();
        Assert.Equal("FAILURE", removed.Status);
        Assert.Contains("unavailable to this caller", removed.ErrorMessage);

        lookup.Principal = lookup.Principal with { GroupIds = ["finance-key"] };
        job = (await store.GetJobByIdAsync(job.Id))!;
        Assert.True(await store.TrySaveJobAsync(job with { Script = job.Script + " PRINT 'edited';" }, job.Version));
        Assert.False(await approvals.HasToolApprovalAsync(job, timeout.Token));
        var edited = await RunNextOccurrenceAsync();
        Assert.Equal("FAILURE", edited.Status);
        Assert.Contains("unavailable to this caller", edited.ErrorMessage);

        job = (await store.GetJobByIdAsync(job.Id))!;
        Assert.Equal(HttpStatusCode.NoContent, await ApproveAsync("alice", job.Version));
        Assert.True(await approvals.HasToolApprovalAsync(job, timeout.Token));
        Assert.Equal(HttpStatusCode.NoContent, await ApproveAsync("alice", job.Version, approved: false));
        Assert.False(await approvals.HasToolApprovalAsync(job, timeout.Token));
    }

    private sealed class CurrentToolOwnerLookup(ToolExecutionPrincipal principal) : IScheduledToolPrincipalLookup
    {
        public ToolExecutionPrincipal Principal { get; set; } = principal;

        public Task<ToolExecutionPrincipal?> ResolveAsync(string tenantId, string ownerKey, CancellationToken cancellationToken) =>
            Task.FromResult<ToolExecutionPrincipal?>(tenantId == Principal.TenantId && ownerKey == "user:alice"
                ? Principal : null);
    }

    [Fact]
    public async Task Federated_ToolUseFollowsSignedGroupAndCurrentCatalogGrant()
    {
        using var factory = new OrchestratorWebFactory(requireFederatedIdentity: true);
        var catalog = new LocalToolCatalogProvider(
            Path.Combine(factory.TempDir, "tools"), ToolCatalogOwnershipMode.PersonalWorkstation,
            validateOwnership: false, requireUseGrants: true);
        const string alias = "mask_sales";
        var approvedImage = "alpine@sha256:" + new string('c', 64);
        var definition = new ToolDefinition(alias, "CONTAINER",
            new Dictionary<string, string>
            {
                ["COMMAND"] = "echo",
                ["IMAGE"] = approvedImage
            }, false);
        const string reconcileAlias = "reconcile_sales";
        await catalog.StoreAsync(definition);
        await catalog.StoreAsync(definition with { Name = reconcileAlias });
        var grant = new ToolUseGrant("tenant-a", ToolPrincipalKind.Group, "finance-id");
        await catalog.SetUseGrantAsync(alias, grant, allow: true);
        await catalog.SetUseGrantAsync(reconcileAlias, new("tenant-a", ToolPrincipalKind.Group, "operations-id"), allow: true);

        using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Governance:Docker:AllowedImages:0"] = approvedImage
                }));
            builder.ConfigureServices(services => services.AddSingleton<IToolCatalogProvider>(catalog));
        });
        using var client = host.CreateClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        async Task<JobStatusResponse> SubmitAsync(string tenant, string groupId, string subjectId, string toolAlias)
        {
            string assertion = OrchestratorIdentityAssertion.Create(
                new OrchestratorCaller("user", subjectId, subjectId, ["OrchestratorManager"], [groupId],
                    tenant, GroupNames: ["Finance"]),
                OrchestratorWebFactory.IdentitySecret);

            using var submit = Request(HttpMethod.Post, "/jobs", ValidKey,
                new { ScriptText = $"SET WHAT_IF ON; EXECUTE TOOL '{toolAlias}';" });
            submit.Headers.Add(OrchestratorIdentityAssertion.HeaderName, assertion);
            using var accepted = await client.SendAsync(submit, timeout.Token);
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
            Assert.NotNull(accepted.Headers.Location);

            while (true)
            {
                using var poll = Request(HttpMethod.Get, accepted.Headers.Location.ToString(), ValidKey);
                poll.Headers.Add(OrchestratorIdentityAssertion.HeaderName, assertion);
                using var result = await client.SendAsync(poll, timeout.Token);
                result.EnsureSuccessStatusCode();
                var status = await result.Content.ReadFromJsonAsync<JobStatusResponse>(timeout.Token);
                Assert.NotNull(status);
                if (status.Status is not (JobRunStatus.Queued or JobRunStatus.Running))
                    return status;

                await Task.Delay(25, timeout.Token);
            }
        }

        var allowed = await SubmitAsync("tenant-a", "finance-id", "alice", alias);
        Assert.True(allowed.Status == JobRunStatus.Completed, allowed.ErrorMessage);
        var operations = await SubmitAsync("tenant-a", "operations-id", "bob", reconcileAlias);
        Assert.True(operations.Status == JobRunStatus.Completed, operations.ErrorMessage);
        var otherTool = await SubmitAsync("tenant-a", "finance-id", "alice", reconcileAlias);
        Assert.Equal(JobRunStatus.Failed, otherTool.Status);
        Assert.Contains("unavailable to this caller", otherTool.ErrorMessage);
        var otherGroup = await SubmitAsync("tenant-a", "operations-id", "bob", alias);
        Assert.Equal(JobRunStatus.Failed, otherGroup.Status);
        Assert.Contains("unavailable to this caller", otherGroup.ErrorMessage);
        var otherTenant = await SubmitAsync("tenant-b", "finance-id", "alice", alias);
        Assert.Equal(JobRunStatus.Failed, otherTenant.Status);
        Assert.Equal(otherGroup.ErrorMessage, otherTenant.ErrorMessage);

        Assert.True(await catalog.SetUseGrantAsync(alias, grant, allow: false));
        var revoked = await SubmitAsync("tenant-a", "finance-id", "alice", alias);
        Assert.Equal(JobRunStatus.Failed, revoked.Status);
        Assert.Equal(otherGroup.ErrorMessage, revoked.ErrorMessage);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/ready")]
    [InlineData("/metrics")]
    [InlineData("/metrics/prometheus")]
    public async Task Probes_StayOpenWithoutApiKey(string path)
    {
        var client = _factory.CreateClient();
        using var req = Request(HttpMethod.Get, path, apiKey: null);
        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.True(res.Headers.Contains("X-Correlation-ID"));
    }

    // HA fault PostgresOutageBrief: while the job store cannot be read, readiness fails closed with
    // 503 and no store detail, and liveness (/health) stays 200 so supervisors do not restart the node.
    [Fact]
    public async Task Readiness_FailsClosed_WhenTheJobStoreIsUnavailable()
    {
        using var failing = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IOrchestratorReadinessProbe>();
            services.AddSingleton<IOrchestratorReadinessProbe>(new UnavailableStoreProbe());
        }));
        var client = failing.CreateClient();

        var ready = await client.GetAsync("/health/ready");
        var body = await ready.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Contains("job-store-unavailable", body);
        Assert.DoesNotContain("10.0.0.5", body);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task Readiness_FailsClosed_WhenTheJobStoreHangs()
    {
        using var hanging = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IOrchestratorReadinessProbe>();
            services.AddSingleton<IOrchestratorReadinessProbe>(new HangingStoreProbe());
        }));
        var client = hanging.CreateClient();
        var watch = Stopwatch.StartNew();
        var ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        // Generous hang guard: the deadline is 3 s; only an unbounded wait fails this.
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(60), $"readiness took {watch.Elapsed}"); // flaky-time-bound-ok: 20x hang guard over the 3 s deadline
    }

    private sealed class UnavailableStoreProbe : IOrchestratorReadinessProbe
    {
        public Task ProbeAsync(CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException("connection to 10.0.0.5:5432 refused"));
    }

    private sealed class HangingStoreProbe : IOrchestratorReadinessProbe
    {
        public Task ProbeAsync(CancellationToken cancellationToken) => Task.Delay(Timeout.Infinite, cancellationToken);
    }

    [Fact]
    public async Task PrometheusMetrics_UsesTextFormat()
    {
        var client = _factory.CreateClient();
        using (var scope = _factory.Services.CreateScope())
        {
            var history = scope.ServiceProvider.GetRequiredService<IJobHistoryStore>();
            var hostMetrics = scope.ServiceProvider.GetRequiredService<IHostMetricsStore>();
            await history.InitializeAsync();
            var started = DateTime.UtcNow.AddMinutes(-10);
            var entryId = await history.LogJobStartAsync("secret-job-name");
            await history.LogJobEndAsync(
                entryId,
                "FAILED",
                "secret failure should not be in labels",
                rowsProcessed: 123,
                peakMemoryBytes: 456,
                cpuTimeSeconds: 1.5);
            await hostMetrics.AppendHostMetricAsync(new HostMetricSample(
                Environment.MachineName,
                started,
                MemoryLoadPercent: 55.5,
                ProcessCpuPercent: 12.5,
                HostCpuPercent: 33.5,
                StateDiskFreeBytes: 1000,
                SpillDiskFreeBytes: 2000));
        }

        using var req = Request(HttpMethod.Get, "/metrics/prometheus", apiKey: null);
        var res = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.StartsWith("text/plain", res.Content.Headers.ContentType?.MediaType);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("# HELP etlsql_orchestrator_jobs_active", body);
        Assert.Contains("# HELP etlsql_orchestrator_jobs_completed_1h", body);
        Assert.Contains("etlsql_orchestrator_jobs_completed_1h", body);
        Assert.Contains("etlsql_orchestrator_jobs_failed_1h", body);
        Assert.Contains("etlsql_orchestrator_rows_processed_1h", body);
        Assert.Contains("etlsql_orchestrator_memory_load_percent", body);
        Assert.Contains("etlsql_orchestrator_state_disk_free_bytes", body);
        Assert.Contains("etlsql_orchestrator_database_reachable", body);
        Assert.Contains("etlsql_runtime_process_working_set_bytes", body);
        Assert.Contains("etlsql_runtime_gc_heap_bytes", body);
        Assert.Contains("etlsql_runtime_gc_collections_gen2_total", body);
        Assert.Contains("component=\"orchestrator\"", body);
        Assert.Contains($"node=\"{Environment.MachineName}\"", body);
        Assert.DoesNotContain("secret-job-name", body);
        Assert.DoesNotContain("secret failure", body);
    }

    [Fact]
    public void OrchestratorObservability_EmitsAdHocJobSpanAndMetrics()
    {
        var stoppedActivities = new List<Activity>();
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == OrchestratorObservability.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => stoppedActivities.Add(activity)
        };
        ActivitySource.AddActivityListener(activityListener);

        var measurements = new List<(string Name, double Value, Dictionary<string, object?> Tags)>();
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == OrchestratorObservability.MeterName)
                    l.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            measurements.Add((instrument.Name, value, ToDictionary(tags))));
        meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            measurements.Add((instrument.Name, value, ToDictionary(tags))));
        meterListener.Start();

        var activity = OrchestratorObservability.StartAdHocJobActivity("job-observe", "corr-observe");
        OrchestratorObservability.CompleteAdHocJobActivity(
            activity,
            "job-observe",
            JobRunStatus.Completed,
            durationMs: 25,
            rowsProcessed: 12,
            peakMemoryBytes: 4096,
            cpuTimeSeconds: 0.5);
        activity?.Dispose();

        var span = Assert.Single(stoppedActivities);
        Assert.Equal("orchestrator.job", span.OperationName);
        Assert.Equal("job-observe", Tag(span, ObservabilityConventions.Tags.JobId));
        Assert.Equal("corr-observe", Tag(span, ObservabilityConventions.Tags.CorrelationId));
        Assert.Equal("Completed", Tag(span, ObservabilityConventions.Tags.Status));
        Assert.Contains(measurements, m => m.Name == "etlsql.orchestrator.job.completed"
            && HasTag(m.Tags, ObservabilityConventions.Tags.Component, "orchestrator")
            && HasTag(m.Tags, ObservabilityConventions.Tags.Status, "Completed")
            && HasTag(m.Tags, ObservabilityConventions.Tags.WorkloadKind, "ad-hoc"));
        Assert.Contains(measurements, m => m.Name == "etlsql.orchestrator.job.rows_processed" && m.Value == 12);
        Assert.DoesNotContain(measurements, m => m.Tags.ContainsKey(ObservabilityConventions.Tags.JobId));
    }

    [Fact]
    [Trait("CompatBreak", "0.12")]
    public async Task ScheduledJobUpdate_RequiresVersion_AndReturnsCurrentStateOnConflict()
    {
        var client = _factory.CreateClient();
        using var create = Request(
            HttpMethod.Post,
            "/api/scheduled-jobs",
            ValidKey,
            new
            {
                Name = "versioned-job",
                ScriptText = "PRINT 'hi';",
                Interval = 1,
                Unit = "HOUR"
            });
        Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(create)).StatusCode);

        using var missing = Request(
            HttpMethod.Put,
            "/api/scheduled-jobs/versioned-job",
            ValidKey,
            new { Interval = 2 });
        Assert.Equal(HttpStatusCode.PreconditionRequired, (await client.SendAsync(missing)).StatusCode);

        using var update = Request(
            HttpMethod.Put,
            "/api/scheduled-jobs/versioned-job",
            ValidKey,
            new { Interval = 2 },
            version: 1);
        var updated = await client.SendAsync(update);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var updatedBody = await updated.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>();
        Assert.Equal(2, updatedBody!["version"]!.GetValue<long>());

        using var stale = Request(
            HttpMethod.Put,
            "/api/scheduled-jobs/versioned-job",
            ValidKey,
            new { Interval = 3 },
            version: 1);
        var conflict = await client.SendAsync(stale);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var conflictBody = await conflict.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>();
        Assert.Equal(2, conflictBody!["current"]!["version"]!.GetValue<long>());
        Assert.Equal(2, conflictBody["current"]!["interval"]!.GetValue<int>());
    }

    [Fact]
    public async Task GetEffectivePermissions_WithoutApiKey_IsUnauthorized()
    {
        var client = _factory.CreateClient();
        using var req = Request(HttpMethod.Get, "/api/effective-permissions", apiKey: null);
        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task GetEffectivePermissions_WithValidKey_ReturnsPermissionsList()
    {
        var client = _factory.CreateClient();
        using var req = Request(HttpMethod.Get, "/api/effective-permissions", apiKey: ValidKey);
        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var list = await res.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonArray>();
        Assert.NotNull(list);
        Assert.NotEmpty(list!);
    }

    [Fact]
    public async Task GetCapabilities_WithValidKey_ReturnsCapabilitiesList()
    {
        var client = _factory.CreateClient();
        using var req = Request(HttpMethod.Get, "/api/capabilities", apiKey: ValidKey);
        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var list = await res.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonArray>();
        Assert.NotNull(list);
    }

    [Fact]
    public async Task GetTenantContext_WithValidKey_ReturnsContext()
    {
        var client = _factory.CreateClient();
        using var req = Request(HttpMethod.Get, "/api/tenant-context", apiKey: ValidKey);
        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var obj = await res.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>();
        Assert.NotNull(obj);
        Assert.NotNull(obj!["tenantId"]);
    }

    private static string? Tag(Activity activity, string key) =>
        activity.Tags.FirstOrDefault(t => t.Key == key).Value;

    private static Dictionary<string, object?> ToDictionary(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var result = new Dictionary<string, object?>();
        foreach (var tag in tags)
            result[tag.Key] = tag.Value;
        return result;
    }

    private static bool HasTag(Dictionary<string, object?> tags, string key, object value) =>
        tags.TryGetValue(key, out var actual) && Equals(actual, value);
}
