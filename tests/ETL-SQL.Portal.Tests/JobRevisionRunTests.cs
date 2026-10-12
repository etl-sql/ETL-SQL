using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// A job as a grantable run slot: a caller with EXECUTE submits a script revision for one run and
/// follows that run by its id. The job's stored definition is the operator's, and a submitted
/// revision must never rewrite it.
/// </summary>
[Trait("Category", "Portal")]
public sealed class JobRevisionRunTests : IDisposable
{
    private const string Provisioned =
        "CREATE CONNECTION sample_data AS MOCKDB();\nSELECT UserID INTO #users FROM sample_data.Users;\n";
    private const string Revised = Provisioned + "ASSERT (SELECT COUNT(*) FROM #users) > 0;\n";
    private const string Failing = Provisioned + "ASSERT (SELECT COUNT(*) FROM #users) = -1;\n";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly OrchestratorWebFactory factory = new(requireFederatedIdentity: true);
    private readonly HttpClient client;

    public JobRevisionRunTests() => client = factory.CreateClient();

    private static readonly OrchestratorCaller Operator =
        new("user", "operator-key", "operator", ["OrchestratorManager"], []);
    private static readonly OrchestratorCaller Author = new("user", "author-key", "author", ["Publisher"], []);

    [Fact]
    public async Task ARevisionRunsThroughTheJobAndLeavesItsStoredScriptAlone()
    {
        var first = await SubmitAsync("PIPELINE:1", Operator, Provisioned, provision: true);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        await FollowAsync((await Body(first))["runId"]!.GetValue<long>(), Operator);

        var second = await SubmitAsync("PIPELINE:1", Operator, Revised);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        var accepted = await Body(second);
        Assert.Equal(Sha(Revised), accepted["scriptHash"]!.GetValue<string>());

        var run = await FollowAsync(accepted["runId"]!.GetValue<long>(), Operator);
        Assert.Equal("SUCCESS", run["status"]!.GetValue<string>());
        // The hash recorded on the run is the hash of what ran, not of what the job stores.
        Assert.Equal(Sha(Revised), run["scriptHash"]!.GetValue<string>());
        Assert.Equal("PIPELINE:1", run["jobName"]!.GetValue<string>());

        var stored = await StoredJobAsync("PIPELINE:1");
        Assert.Equal(Provisioned, stored.Script);
        Assert.Equal("user:operator-key", stored.CreatedBy);

        // A run slot is never due on its own, and running a revision does not re-arm it: the
        // scheduler ticks every second here, and the only runs are the two that were submitted.
        Assert.True(stored.NextRun > DateTime.Now.AddYears(100), $"next run was {stored.NextRun:O}");
        await Task.Delay(2500); // flaky-delay-ok: negative check; spans two scheduler ticks so a wrongly due run would have started. Load can only hide a failure, not cause one
        using var scope = factory.Services.CreateScope();
        var history = await scope.ServiceProvider.GetRequiredService<IJobHistoryStore>().GetHistoryAsync(stored.Id, 10);
        Assert.Equal(2, history.Count());
    }

    [Fact]
    public async Task AFailingRevisionIsReportedAsAFailureWithItsError()
    {
        var submitted = await SubmitAsync("PIPELINE:2", Operator, Failing, provision: true);
        var run = await FollowAsync((await Body(submitted))["runId"]!.GetValue<long>(), Operator);

        Assert.Equal("FAILURE", run["status"]!.GetValue<string>());
        Assert.False(string.IsNullOrWhiteSpace(run["errorMessage"]?.GetValue<string>()));
    }

    [Fact]
    public async Task ExecuteRunsARevisionAndReadDoesNot()
    {
        await FollowAsync(
            (await Body(await SubmitAsync("PIPELINE:3", Operator, Provisioned, provision: true)))["runId"]!.GetValue<long>(),
            Operator);
        var reader = new OrchestratorCaller("user", "reader-key", "reader", ["Publisher"], []);
        await GrantAsync("PIPELINE:3", "author-key", "EXECUTE");
        await GrantAsync("PIPELINE:3", "reader-key", "READ");

        var refused = await SubmitAsync("PIPELINE:3", reader, Revised);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("run_grant_denied", (await Body(refused))["code"]!.GetValue<string>());

        var allowed = await SubmitAsync("PIPELINE:3", Author, Revised);
        Assert.Equal(HttpStatusCode.Accepted, allowed.StatusCode);
        var runId = (await Body(allowed))["runId"]!.GetValue<long>();
        Assert.Equal("SUCCESS", (await FollowAsync(runId, Author))["status"]!.GetValue<string>());

        // READ follows a run it could not start; no grant at all does not.
        using (var read = Request(HttpMethod.Get, $"/api/job-runs/{runId}", reader))
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(read)).StatusCode);
        var stranger = new OrchestratorCaller("user", "stranger-key", "stranger", ["Publisher"], []);
        using (var read = Request(HttpMethod.Get, $"/api/job-runs/{runId}", stranger))
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(read)).StatusCode);
    }

    [Fact]
    public async Task ACallerWhoMayNotCreateCannotProvisionTheSlot()
    {
        var asked = await SubmitAsync("PIPELINE:4", Author, Provisioned, provision: true);
        Assert.Equal(HttpStatusCode.Forbidden, asked.StatusCode);
        Assert.Equal("job_not_provisioned", (await Body(asked))["code"]!.GetValue<string>());

        var plain = await SubmitAsync("PIPELINE:4", Operator, Provisioned);
        Assert.Equal(HttpStatusCode.NotFound, plain.StatusCode);
        Assert.Equal("job_not_provisioned", (await Body(plain))["code"]!.GetValue<string>());

        using var scope = factory.Services.CreateScope();
        Assert.Null(await scope.ServiceProvider.GetRequiredService<IJobHistoryStore>().GetJobAsync(null, "PIPELINE:4"));
    }

    [Fact]
    public async Task AnOperatorsOwnJobRunsOnlyItsStoredScript()
    {
        // EXECUTE on a hash-pinned job is leave to start the pinned script, not to replace it.
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IJobHistoryStore>().SaveJobAsync(new JobDefinition(
                "nightly-load", Provisioned, 100, "DAY", null, null, DateTime.Now.AddDays(100),
                ScriptHash: Sha(Provisioned), HashPolicy: "Block", CreatedBy: "user:operator-key"));
        await GrantAsync("nightly-load", "author-key", "EXECUTE");

        foreach (var caller in new[] { Author, Operator })
        {
            var refused = await SubmitAsync("nightly-load", caller, Revised);
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Equal("not_a_run_slot", (await Body(refused))["code"]!.GetValue<string>());
        }

        // The same answer for a name nobody holds, and no job is provisioned under it.
        var absent = await SubmitAsync("weekly-load", Operator, Provisioned, provision: true);
        Assert.Equal(HttpStatusCode.Forbidden, absent.StatusCode);
        Assert.Equal("not_a_run_slot", (await Body(absent))["code"]!.GetValue<string>());
        using var check = factory.Services.CreateScope();
        var store = check.ServiceProvider.GetRequiredService<IJobHistoryStore>();
        Assert.Null(await store.GetJobAsync(null, "weekly-load"));
        Assert.Empty(await store.GetHistoryAsync((await StoredJobAsync("nightly-load")).Id));
    }

    [Fact]
    public async Task ASandboxedOrchestratorRefusesTextItCannotRunAsItsSubmitter()
    {
        var sandbox = new RecordingSandboxExecutor();
        using var sandboxed = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<ETL_SQL.Orchestrator.Execution.ISandboxScheduledJobExecutor>(sandbox)));
        using var sandboxedClient = sandboxed.CreateClient();
        using (var scope = sandboxed.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IJobHistoryStore>().SaveJobAsync(new JobDefinition(
                "PIPELINE:10", Provisioned, 1, "DAY", null, null, ETL_SQL.Orchestrator.Scheduling.SchedulerService.ManualOnlyNextRun,
                CreatedBy: "user:operator-key"));

        using var request = Request(HttpMethod.Post, "/api/scheduled-jobs/PIPELINE%3A10/runs", Operator,
            new { scriptText = Revised, provisionIfMissing = false });
        var refused = await sandboxedClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotImplemented, refused.StatusCode);
        Assert.Equal("sandbox_identity_unsupported", (await Body(refused))["code"]!.GetValue<string>());
        Assert.Equal(0, sandbox.Calls);
    }

    private sealed class RecordingSandboxExecutor : ETL_SQL.Orchestrator.Execution.ISandboxScheduledJobExecutor
    {
        private int calls;
        public int Calls => Volatile.Read(ref calls);

        public Task<ETL_SQL.Core.ScriptExecutionResult> ExecuteAsync(
            JobDefinition job, long historyId, int attempt, string sessionId, bool resumeFromCheckpoint,
            long queueWaitMs, IReadOnlyDictionary<string, string>? variableOverrides, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            throw new InvalidOperationException("Submitted text must not reach the sandbox executor.");
        }
    }

    [Fact]
    public async Task AnotherTenantCanNeitherRunNorReadTheRun()
    {
        var owner = Operator with { TenantId = "tenant-a" };
        var submitted = await SubmitAsync("PIPELINE:5", owner, Provisioned, provision: true);
        var runId = (await Body(submitted))["runId"]!.GetValue<long>();
        await FollowAsync(runId, owner);

        var other = new OrchestratorCaller("user", "admin-key", "admin", ["Admin"], [], "tenant-b");
        using (var read = Request(HttpMethod.Get, $"/api/job-runs/{runId}", other))
            Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(read)).StatusCode);
        // To tenant-b the name is free, so without the provisioning flag there is nothing to run.
        Assert.Equal(HttpStatusCode.NotFound, (await SubmitAsync("PIPELINE:5", other, Revised)).StatusCode);
    }

    [Fact]
    public async Task RepeatedFailingRevisionsDoNotQuarantineTheJob()
    {
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IJobHistoryStore>().SaveJobAsync(new JobDefinition(
                "PIPELINE:6", Provisioned, 100, "DAY", null, null, DateTime.Now.AddDays(100),
                CreatedBy: "user:operator-key"));

        for (var attempt = 0; attempt < 6; attempt++)
        {
            var submitted = await SubmitAsync("PIPELINE:6", Operator, Failing);
            Assert.Equal(HttpStatusCode.Accepted, submitted.StatusCode);
            await FollowAsync((await Body(submitted))["runId"]!.GetValue<long>(), Operator);
        }

        Assert.True((await StoredJobAsync("PIPELINE:6")).IsEnabled);
    }

    [Fact]
    public async Task TheSubmitterCancelsItsOwnRunAndItIsRecordedAsCancelled()
    {
        await ProvisionAsync("PIPELINE:7");
        await GrantAsync("PIPELINE:7", "author-key", "EXECUTE");
        var started = DateTime.UtcNow;
        var runId = (await Body(await SubmitAsync("PIPELINE:7", Author, Slow)))["runId"]!.GetValue<long>();

        using (var cancel = Request(HttpMethod.Post, $"/api/job-runs/{runId}/cancel", Author))
        {
            var response = await client.SendAsync(cancel);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            Assert.Equal(runId, (await Body(response))["runId"]!.GetValue<long>());
        }

        var run = await FollowAsync(runId, Author);
        // Cancelled, not failed: the script did nothing wrong, and it stopped long before its wait.
        Assert.Equal("CANCELLED", run["status"]!.GetValue<string>());
        Assert.NotNull(run["endTime"]);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(20), "the run was not interrupted");

        // Asked again, the submitter is told how it ended, not that the run is someone else's.
        using var again = Request(HttpMethod.Post, $"/api/job-runs/{runId}/cancel", Author);
        var finished = await client.SendAsync(again);
        Assert.Equal(HttpStatusCode.Conflict, finished.StatusCode);
        Assert.Equal("CANCELLED", (await Body(finished))["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task ExecuteDoesNotCancelSomeoneElsesRunButManageDoes()
    {
        await ProvisionAsync("PIPELINE:8");
        var colleague = new OrchestratorCaller("user", "colleague-key", "colleague", ["Publisher"], []);
        await GrantAsync("PIPELINE:8", "author-key", "EXECUTE");
        await GrantAsync("PIPELINE:8", "colleague-key", "EXECUTE");
        var runId = (await Body(await SubmitAsync("PIPELINE:8", Author, Slow)))["runId"]!.GetValue<long>();

        using (var cancel = Request(HttpMethod.Post, $"/api/job-runs/{runId}/cancel", colleague))
        {
            var refused = await client.SendAsync(cancel);
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Equal("cancel_denied", (await Body(refused))["code"]!.GetValue<string>());
        }
        using (var read = Request(HttpMethod.Get, $"/api/job-runs/{runId}", Author))
            Assert.False((await Body(await client.SendAsync(read)))["isTerminal"]!.GetValue<bool>());

        var tenantB = new OrchestratorCaller("user", "admin-key", "admin", ["Admin"], [], "tenant-b");
        using (var cancel = Request(HttpMethod.Post, $"/api/job-runs/{runId}/cancel", tenantB))
            Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(cancel)).StatusCode);

        using (var cancel = Request(HttpMethod.Post, $"/api/job-runs/{runId}/cancel", Operator))
            Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(cancel)).StatusCode);
        Assert.Equal("CANCELLED", (await FollowAsync(runId, Operator))["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task CancellingAFinishedRunReportsHowItFinished()
    {
        var runId = (await Body(await SubmitAsync("PIPELINE:9", Operator, Revised, provision: true)))["runId"]!.GetValue<long>();
        await FollowAsync(runId, Operator);

        using var cancel = Request(HttpMethod.Post, $"/api/job-runs/{runId}/cancel", Operator);
        var response = await client.SendAsync(cancel);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var refusal = await Body(response);
        Assert.Equal("run_already_finished", refusal["code"]!.GetValue<string>());
        Assert.Equal("SUCCESS", refusal["status"]!.GetValue<string>());
        // Asking to cancel a finished run does not rewrite how it finished.
        using var read = Request(HttpMethod.Get, $"/api/job-runs/{runId}", Operator);
        Assert.Equal("SUCCESS", (await Body(await client.SendAsync(read)))["status"]!.GetValue<string>());
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private const string Slow = "WAITFOR DELAY '00:00:30';\n";

    private async Task ProvisionAsync(string job) =>
        await FollowAsync(
            (await Body(await SubmitAsync(job, Operator, Provisioned, provision: true)))["runId"]!.GetValue<long>(),
            Operator);

    /// <summary>
    /// Submits one revision. A job's start guard clears just after its run is recorded as finished,
    /// so a submission that follows a run immediately waits out that window instead of racing it.
    /// </summary>
    private async Task<HttpResponseMessage> SubmitAsync(
        string job, OrchestratorCaller caller, string script, bool provision = false)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = Request(HttpMethod.Post, $"/api/scheduled-jobs/{Uri.EscapeDataString(job)}/runs", caller,
                new { scriptText = script, provisionIfMissing = provision });
            var response = await client.SendAsync(request);
            if (response.StatusCode != HttpStatusCode.Conflict || attempt >= 200) return response;
            await Task.Delay(25);
        }
    }

    private async Task<JsonObject> FollowAsync(long runId, OrchestratorCaller caller)
    {
        for (var attempt = 0; attempt < 400; attempt++)
        {
            using var request = Request(HttpMethod.Get, $"/api/job-runs/{runId}", caller);
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var run = await Body(response);
            Assert.Equal(runId, run["runId"]!.GetValue<long>());
            if (run["isTerminal"]!.GetValue<bool>()) return run;
            await Task.Delay(25);
        }
        throw new TimeoutException($"Run {runId} did not reach a terminal status.");
    }

    private async Task GrantAsync(string job, string principalKey, string permission)
    {
        using var grant = Request(HttpMethod.Put,
            $"/api/authorization/JOB/{Uri.EscapeDataString(job)}/USER/{principalKey}", Operator, new { permission });
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(grant)).StatusCode);
    }

    private async Task<JobDefinition> StoredJobAsync(string name)
    {
        using var scope = factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<IJobHistoryStore>().GetJobAsync(null, name))!;
    }

    private static string Sha(string text) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static async Task<JsonObject> Body(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonObject>(Json))!;

    private static HttpRequestMessage Request(
        HttpMethod method, string path, OrchestratorCaller caller, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Orchestrator-Key", "test-orch-key-12345");
        request.Headers.Add(
            OrchestratorIdentityAssertion.HeaderName,
            OrchestratorIdentityAssertion.Create(caller, OrchestratorWebFactory.IdentitySecret));
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
    }
}
