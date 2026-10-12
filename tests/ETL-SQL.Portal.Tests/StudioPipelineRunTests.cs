using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Core.Data;
using ETL_SQL.Portal.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// A Portal joined to a real Orchestrator the way production joins them, with the three people a
/// pipeline run involves: an administrator, an Orchestrator manager who is also an author, and an
/// ordinary author who holds neither role.
/// </summary>
public sealed class StudioPipelineRunFixture : IAsyncLifetime
{
    public const string Password = "Pipeline@Tests99!";

    public OrchestratorWebFactory Orchestrator { get; } = new(requireFederatedIdentity: true);
    public PortalWebFactory Portal { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;
    public string AdminToken { get; private set; } = "";
    public string AuthorToken { get; private set; } = "";
    public int AuthorId { get; private set; }
    public string AuthorPrincipalKey { get; private set; } = "";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task InitializeAsync()
    {
        Portal = new FederatedPortalFactory(Orchestrator);
        Client = Portal.CreateClient();

        var initial = await LoginAsync(Client, "admin", "Admin@12345!");
        using (var change = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(new { currentPassword = "Admin@12345!", newPassword = Password })
        })
        {
            change.Headers.Authorization = new AuthenticationHeaderValue("Bearer", initial);
            Assert.Equal(HttpStatusCode.NoContent, (await Client.SendAsync(change)).StatusCode);
        }
        AdminToken = await LoginAsync(Client, "admin", Password);

        (AuthorId, AuthorPrincipalKey) = await CreateUserAsync(Portal, "pipeline_author", "Publisher");
        AuthorToken = await LoginAsync(Client, "pipeline_author", Password);
    }

    public Task DisposeAsync()
    {
        Client?.Dispose();
        Portal?.Dispose();
        Orchestrator.Dispose();
        return Task.CompletedTask;
    }

    public static async Task<(int Id, string PrincipalKey)> CreateUserAsync(
        PortalWebFactory portal, string userName, params string[] roles)
    {
        using var scope = portal.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
        var user = new PortalUser
        {
            UserName = userName,
            Email = $"{userName}@test.local",
            IsActive = true,
            MustChangePassword = false,
            Provider = "Local"
        };
        var created = await users.CreateAsync(user, Password);
        Assert.True(created.Succeeded, string.Join(", ", created.Errors.Select(error => error.Description)));
        foreach (var role in roles)
            Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        var stored = await scope.ServiceProvider.GetRequiredService<PortalDbContext>().Users
            .AsNoTracking().SingleAsync(item => item.Id == user.Id);
        Assert.False(string.IsNullOrWhiteSpace(stored.PrincipalKey));
        return (stored.Id, stored.PrincipalKey!);
    }

    public static async Task<string> LoginAsync(HttpClient client, string username, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>(Json))!["token"]!.GetValue<string>();
    }

    private sealed class FederatedPortalFactory(OrchestratorWebFactory orchestrator) : PortalWebFactory
    {
        protected override void CustomizePortalConfig(PortalConfig config)
        {
            config.Orchestrator.ApiUrl = "http://orchestrator.test";
            config.Orchestrator.ApiKey = "test-orch-key-12345";
            config.Orchestrator.IdentitySigningSecret = OrchestratorWebFactory.IdentitySecret;
        }

        protected override void CustomizeServices(IServiceCollection services) =>
            services.AddHttpClient<ETL_SQL.Portal.Services.OrchestratorProxyService>()
                .ConfigurePrimaryHttpMessageHandler(() => orchestrator.Server.CreateHandler());
    }
}

/// <summary>
/// ST04: a saved catalog pipeline runs through the Orchestrator, never in the Portal. The Portal
/// decides the document question (is it a pipeline, may this account execute it, is this the saved
/// revision); the Orchestrator decides whether the account may run its job.
/// </summary>
[Trait("Category", "Portal")]
public sealed class StudioPipelineRunTests(StudioPipelineRunFixture fixture) : IClassFixture<StudioPipelineRunFixture>
{
    private const string Script =
        "CREATE CONNECTION sample_data AS MOCKDB();\nSELECT UserID INTO #users FROM sample_data.Users;\n"
        + "ASSERT (SELECT COUNT(*) FROM #users) > 0;\n";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task ASavedPipelineRunsThroughTheOrchestratorAndNotInThePortal()
    {
        var (id, version) = await CreatePipelineAsync(fixture.AdminToken, await CreateFolderAsync(ownerId: null));

        var submitted = await Submit(id, version, fixture.AdminToken);
        Assert.Equal(HttpStatusCode.Accepted, submitted.StatusCode);
        var accepted = await Body(submitted);
        var runId = accepted["runId"]!.GetValue<long>();
        Assert.Equal(Sha(Script), accepted["scriptHash"]!.GetValue<string>());
        Assert.Equal(version, accepted["version"]!.GetValue<long>());

        var run = await FollowAsync(id, runId, fixture.AdminToken);
        Assert.Equal("SUCCESS", run["status"]!.GetValue<string>());
        // What ran is the saved revision: the Orchestrator hashed the text it executed.
        Assert.Equal(Sha(Script), run["scriptHash"]!.GetValue<string>());

        // The run is the Orchestrator's, recorded against this pipeline's job in the Portal's tenant.
        using (var scope = fixture.Orchestrator.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IJobHistoryStore>();
            var job = await store.GetJobAsync("portal-host", $"PIPELINE:{id}");
            Assert.NotNull(job);
            Assert.Contains(await store.GetHistoryAsync(job.Id, 10), entry => entry.Id == runId);
        }
        // The Portal holds its own job store in these tests. Nothing ran there.
        using (var scope = fixture.Portal.Services.CreateScope())
        {
            var local = scope.ServiceProvider.GetRequiredService<IJobHistoryStore>();
            Assert.Empty(await local.GetHistoryAsync(limit: 10));
            Assert.Null(await local.GetJobAsync("portal-host", $"PIPELINE:{id}"));
        }

        using (var scope = fixture.Portal.Services.CreateScope())
        {
            var audited = await scope.ServiceProvider.GetRequiredService<PortalDbContext>().AuditLogs
                .SingleAsync(entry => entry.Action == "PIPELINE_RUN_SUBMITTED" && entry.ResourceId == id.ToString());
            Assert.Contains($"version={version}", audited.Detail);
            Assert.Contains($"run={runId}", audited.Detail);
            Assert.Contains(Sha(Script), audited.Detail);
            Assert.NotNull(audited.UserId);
        }
    }

    [Fact]
    public async Task AFailedRunIsFollowedToItsFailureWithItsOwnBoundedLog()
    {
        var folderId = await CreateFolderAsync(ownerId: null);
        var (passing, passingVersion) = await CreatePipelineAsync(fixture.AdminToken, folderId);
        var (failing, failingVersion) = await CreatePipelineAsync(fixture.AdminToken, folderId);
        var broken = Script.Replace("> 0", "= -1", StringComparison.Ordinal);
        failingVersion = await SaveAsync(failing, failingVersion, broken, fixture.AdminToken);

        var passed = await FollowAsync(passing,
            (await Body(await Submit(passing, passingVersion, fixture.AdminToken)))["runId"]!.GetValue<long>(), fixture.AdminToken);
        var failedRunId = (await Body(await Submit(failing, failingVersion, fixture.AdminToken)))["runId"]!.GetValue<long>();
        var failed = await FollowAsync(failing, failedRunId, fixture.AdminToken);

        Assert.Equal("FAILURE", failed["status"]!.GetValue<string>());
        Assert.True(failed["isTerminal"]!.GetValue<bool>());
        Assert.NotNull(failed["endTime"]);
        Assert.Contains("Assertion failed", failed["errorMessage"]!.GetValue<string>());
        Assert.Equal(Sha(broken), failed["scriptHash"]!.GetValue<string>());

        // Each run carries its own statements: the failing run's log is not the passing run's.
        var passedLog = Statements(passed);
        var failedLog = Statements(failed);
        Assert.NotEmpty(passedLog);
        Assert.NotEmpty(failedLog);
        // Statement text is normalized by the Orchestrator: literals are replaced before it is kept.
        Assert.Contains(passedLog, statement => statement.Contains("> ?", StringComparison.Ordinal));
        Assert.DoesNotContain(failedLog, statement => statement.Contains("> ?", StringComparison.Ordinal));
        Assert.Contains(failedLog, statement => statement.StartsWith("SELECT UserID INTO #users", StringComparison.Ordinal));
        Assert.InRange(failedLog.Count, 1, 200);
        Assert.False(failed["logTruncated"]!.GetValue<bool>());

        // Read again after the fact, the run is still the same run with the same outcome.
        var again = await Body(await Send(HttpMethod.Get, $"/api/studio/pipelines/{failing}/runs/{failedRunId}", fixture.AdminToken));
        Assert.Equal("FAILURE", again["status"]!.GetValue<string>());
        Assert.Equal(failedRunId, again["runId"]!.GetValue<long>());

        static List<string> Statements(JsonObject run) =>
            [.. run["statements"]!.AsArray().Select(item => item!["statement"]!.GetValue<string>())];
    }

    [Fact]
    public async Task RunRefusesAStaleVersionAndRunsTheNewlySavedOne()
    {
        var (id, version) = await CreatePipelineAsync(fixture.AdminToken, await CreateFolderAsync(ownerId: null));
        var edited = Script + "ASSERT (SELECT COUNT(*) FROM #users) >= 1;\n";
        var saved = await SaveAsync(id, version, edited, fixture.AdminToken);
        Assert.Equal(version + 1, saved);

        var stale = await Submit(id, version, fixture.AdminToken);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var refusal = await Body(stale);
        Assert.Equal("stale_revision", refusal["code"]!.GetValue<string>());
        Assert.Equal(saved, refusal["currentVersion"]!.GetValue<long>());
        using (var scope = fixture.Orchestrator.Services.CreateScope())
            Assert.Null(await scope.ServiceProvider.GetRequiredService<IJobHistoryStore>()
                .GetJobAsync("portal-host", $"PIPELINE:{id}"));

        var current = await Submit(id, saved, fixture.AdminToken);
        Assert.Equal(HttpStatusCode.Accepted, current.StatusCode);
        Assert.Equal(Sha(edited), (await Body(current))["scriptHash"]!.GetValue<string>());
    }

    [Fact]
    public async Task TheOrchestratorDecidesWhetherAnAuthorMayRunTheJob()
    {
        // The author owns the folder, so the Portal's document check passes at every step below.
        var (id, version) = await CreatePipelineAsync(fixture.AuthorToken, await CreateFolderAsync(fixture.AuthorId));

        var unprovisioned = await Submit(id, version, fixture.AuthorToken);
        Assert.Equal(HttpStatusCode.Forbidden, unprovisioned.StatusCode);
        Assert.Equal("job_not_provisioned", (await Body(unprovisioned))["code"]!.GetValue<string>());

        // An administrator's first run provisions the slot. The author still holds no grant on it.
        var first = await Submit(id, version, fixture.AdminToken);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        await FollowAsync(id, (await Body(first))["runId"]!.GetValue<long>(), fixture.AdminToken);

        var ungranted = await SubmitWhenIdle(id, version, fixture.AuthorToken);
        Assert.Equal(HttpStatusCode.Forbidden, ungranted.StatusCode);
        Assert.Equal("run_grant_denied", (await Body(ungranted))["code"]!.GetValue<string>());

        using (var grant = await Send(HttpMethod.Put,
            $"/api/orchestrator/authorization/JOB/{Uri.EscapeDataString($"PIPELINE:{id}")}/USER/{fixture.AuthorPrincipalKey}",
            fixture.AdminToken, new { permission = "EXECUTE" }))
            Assert.Equal(HttpStatusCode.OK, grant.StatusCode);

        var granted = await SubmitWhenIdle(id, version, fixture.AuthorToken);
        Assert.Equal(HttpStatusCode.Accepted, granted.StatusCode);
        var run = await FollowAsync(id, (await Body(granted))["runId"]!.GetValue<long>(), fixture.AuthorToken);
        Assert.Equal("SUCCESS", run["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task AnAccountWithoutExecuteOnTheDocumentIsRefusedByThePortal()
    {
        var (id, version) = await CreatePipelineAsync(fixture.AdminToken, await CreateFolderAsync(ownerId: null));

        var refused = await Submit(id, version, fixture.AuthorToken);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("document_execute_denied", (await Body(refused))["code"]!.GetValue<string>());
        using var scope = fixture.Orchestrator.Services.CreateScope();
        Assert.Null(await scope.ServiceProvider.GetRequiredService<IJobHistoryStore>()
            .GetJobAsync("portal-host", $"PIPELINE:{id}"));
    }

    [Fact]
    public async Task AReportIsNotAPipeline()
    {
        var folderId = await CreateFolderAsync(ownerId: null);
        var created = await Send(HttpMethod.Post, "/api/studio/reports", fixture.AdminToken,
            new { folderId, name = $"Report {Guid.NewGuid():N}"[..20], scriptText = "SELECT 1 AS Value;\n", kind = "Report" });
        Assert.True(created.IsSuccessStatusCode);
        var report = await Body(created);

        var refused = await Submit(report["id"]!.GetValue<int>(), report["version"]!.GetValue<long>(), fixture.AdminToken);
        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
        Assert.Equal("pipeline_not_found", (await Body(refused))["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task AnotherPipelinesRunIsNotShown()
    {
        var folderId = await CreateFolderAsync(ownerId: null);
        var (first, firstVersion) = await CreatePipelineAsync(fixture.AdminToken, folderId);
        var (second, _) = await CreatePipelineAsync(fixture.AdminToken, folderId);
        var runId = (await Body(await Submit(first, firstVersion, fixture.AdminToken)))["runId"]!.GetValue<long>();
        await FollowAsync(first, runId, fixture.AdminToken);

        // The administrator may read the run. It is still not the second pipeline's run.
        var crossed = await Send(HttpMethod.Get, $"/api/studio/pipelines/{second}/runs/{runId}", fixture.AdminToken);
        Assert.Equal(HttpStatusCode.NotFound, crossed.StatusCode);
        Assert.Equal("run_not_found", (await Body(crossed))["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task StopReachesTheSubmittedRunAndTheOrchestratorConfirmsIt()
    {
        var folderId = await CreateFolderAsync(ownerId: null);
        var (id, version) = await CreatePipelineAsync(fixture.AdminToken, folderId, Slow);
        var (other, _) = await CreatePipelineAsync(fixture.AdminToken, folderId);
        var started = DateTime.UtcNow;
        var runId = (await Body(await Submit(id, version, fixture.AdminToken)))["runId"]!.GetValue<long>();

        // Not through another pipeline's route, even for someone who may stop it.
        var crossed = await Send(HttpMethod.Post, $"/api/studio/pipelines/{other}/runs/{runId}/cancel", fixture.AdminToken);
        Assert.Equal(HttpStatusCode.NotFound, crossed.StatusCode);
        Assert.Equal("run_not_found", (await Body(crossed))["code"]!.GetValue<string>());
        Assert.False((await Body(await Send(HttpMethod.Get, $"/api/studio/pipelines/{id}/runs/{runId}", fixture.AdminToken)))
            ["isTerminal"]!.GetValue<bool>());

        var stop = await Send(HttpMethod.Post, $"/api/studio/pipelines/{id}/runs/{runId}/cancel", fixture.AdminToken);
        Assert.Equal(HttpStatusCode.Accepted, stop.StatusCode);
        Assert.Equal(runId, (await Body(stop))["runId"]!.GetValue<long>());

        // Accepted is a request. Confirmation is the run's own recorded status.
        var run = await FollowAsync(id, runId, fixture.AdminToken);
        Assert.Equal("CANCELLED", run["status"]!.GetValue<string>());
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(20), "the run was not interrupted");
        using (var scope = fixture.Orchestrator.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IJobHistoryStore>();
            Assert.Equal("CANCELLED", (await store.GetHistoryEntryAsync(runId))!.Status);
        }
        using (var scope = fixture.Portal.Services.CreateScope())
        {
            var audited = await scope.ServiceProvider.GetRequiredService<PortalDbContext>().AuditLogs
                .SingleAsync(entry => entry.Action == "PIPELINE_RUN_CANCEL_REQUESTED" && entry.ResourceId == id.ToString());
            Assert.Contains($"run={runId}", audited.Detail);
        }

        // Stopping it again reports how it ended and changes nothing.
        var again = await Send(HttpMethod.Post, $"/api/studio/pipelines/{id}/runs/{runId}/cancel", fixture.AdminToken);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("run_already_finished", (await Body(again))["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task AnAuthorStopsTheirOwnRunButNotAColleagues()
    {
        var folderId = await CreateFolderAsync(fixture.AuthorId);
        var (id, version) = await CreatePipelineAsync(fixture.AuthorToken, folderId, Slow);
        // The administrator provisions the slot with a run of their own and stops it.
        var provisioning = (await Body(await Submit(id, version, fixture.AdminToken)))["runId"]!.GetValue<long>();
        Assert.Equal(HttpStatusCode.Accepted,
            (await Send(HttpMethod.Post, $"/api/studio/pipelines/{id}/runs/{provisioning}/cancel", fixture.AdminToken)).StatusCode);
        await FollowAsync(id, provisioning, fixture.AdminToken);
        using (var grant = await Send(HttpMethod.Put,
            $"/api/orchestrator/authorization/JOB/{Uri.EscapeDataString($"PIPELINE:{id}")}/USER/{fixture.AuthorPrincipalKey}",
            fixture.AdminToken, new { permission = "EXECUTE" }))
            Assert.Equal(HttpStatusCode.OK, grant.StatusCode);

        // The administrator's run: the author holds EXECUTE on the job and still may not stop it.
        var adminRun = (await Body(await SubmitWhenIdle(id, version, fixture.AdminToken)))["runId"]!.GetValue<long>();
        var denied = await Send(HttpMethod.Post, $"/api/studio/pipelines/{id}/runs/{adminRun}/cancel", fixture.AuthorToken);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal("cancel_denied", (await Body(denied))["code"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.Accepted,
            (await Send(HttpMethod.Post, $"/api/studio/pipelines/{id}/runs/{adminRun}/cancel", fixture.AdminToken)).StatusCode);
        await FollowAsync(id, adminRun, fixture.AdminToken);

        // Their own run, with nothing more than EXECUTE.
        var ownRun = (await Body(await SubmitWhenIdle(id, version, fixture.AuthorToken)))["runId"]!.GetValue<long>();
        Assert.Equal(HttpStatusCode.Accepted,
            (await Send(HttpMethod.Post, $"/api/studio/pipelines/{id}/runs/{ownRun}/cancel", fixture.AuthorToken)).StatusCode);
        Assert.Equal("CANCELLED", (await FollowAsync(id, ownRun, fixture.AuthorToken))["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task WithNoOrchestratorConnectedRunSaysSoAndRunsNothing()
    {
        using var portal = new PortalWebFactory();
        using var client = portal.CreateClient();
        var initial = await StudioPipelineRunFixture.LoginAsync(client, "admin", "Admin@12345!");
        using (var change = Request(HttpMethod.Post, "/api/auth/change-password", initial,
            new { currentPassword = "Admin@12345!", newPassword = StudioPipelineRunFixture.Password }))
            Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(change)).StatusCode);
        var admin = await StudioPipelineRunFixture.LoginAsync(client, "admin", StudioPipelineRunFixture.Password);
        var folderId = await CreateFolderAsync(ownerId: null, portal);
        JsonObject pipeline;
        using (var create = Request(HttpMethod.Post, "/api/studio/reports", admin,
            new { folderId, name = "Unconnected", scriptText = Script, kind = "Pipeline" }))
            pipeline = await Body(await client.SendAsync(create));

        using var submit = Request(HttpMethod.Post, $"/api/studio/pipelines/{pipeline["id"]!.GetValue<int>()}/runs", admin,
            new { version = pipeline["version"]!.GetValue<long>() });
        var response = await client.SendAsync(submit);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var refusal = await Body(response);
        Assert.Equal("orchestrator_not_connected", refusal["code"]!.GetValue<string>());
        Assert.Contains("No Orchestrator is connected", refusal["error"]!.GetValue<string>());
        // This Portal has an in-process job channel. A pipeline must not fall back to it.
        using var scope = portal.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<IJobHistoryStore>().GetHistoryAsync(limit: 10));
        Assert.False(await scope.ServiceProvider.GetRequiredService<PortalDbContext>().AuditLogs
            .AnyAsync(entry => entry.Action == "PIPELINE_RUN_SUBMITTED"));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private Task<HttpResponseMessage> Submit(int id, long version, string token) =>
        Send(HttpMethod.Post, $"/api/studio/pipelines/{id}/runs", token, new { version });

    /// <summary>The job's start guard clears just after its run is recorded as finished.</summary>
    private async Task<HttpResponseMessage> SubmitWhenIdle(int id, long version, string token)
    {
        for (var attempt = 0; ; attempt++)
        {
            var response = await Submit(id, version, token);
            if (response.StatusCode != HttpStatusCode.Conflict || attempt >= 200) return response;
            await Task.Delay(25);
        }
    }

    private async Task<JsonObject> FollowAsync(int id, long runId, string token)
    {
        for (var attempt = 0; attempt < 400; attempt++)
        {
            var response = await Send(HttpMethod.Get, $"/api/studio/pipelines/{id}/runs/{runId}", token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var run = await Body(response);
            Assert.Equal(runId, run["runId"]!.GetValue<long>());
            if (run["isTerminal"]!.GetValue<bool>()) return run;
            await Task.Delay(25);
        }
        throw new TimeoutException($"Run {runId} did not reach a terminal status.");
    }

    private const string Slow = "WAITFOR DELAY '00:00:30';\n";

    private async Task<(int Id, long Version)> CreatePipelineAsync(string token, int folderId, string script = Script)
    {
        var created = await Send(HttpMethod.Post, "/api/studio/reports", token,
            new { folderId, name = $"Load users {Guid.NewGuid():N}"[..24], scriptText = script, kind = "Pipeline" });
        Assert.True(created.IsSuccessStatusCode, $"create: {(int)created.StatusCode} {await created.Content.ReadAsStringAsync()}");
        var pipeline = await Body(created);
        return (pipeline["id"]!.GetValue<int>(), pipeline["version"]!.GetValue<long>());
    }

    private async Task<long> SaveAsync(int id, long version, string text, string token)
    {
        Assert.True((await Send(HttpMethod.Post, "/api/designer/lease", token, new { reportId = id, force = false })).IsSuccessStatusCode);
        using var save = Request(HttpMethod.Post, "/api/designer/save", token, new { reportId = id, scriptText = text });
        save.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        var saved = await fixture.Client.SendAsync(save);
        Assert.True(saved.IsSuccessStatusCode, $"save: {(int)saved.StatusCode} {await saved.Content.ReadAsStringAsync()}");
        var content = await Body(await Send(HttpMethod.Get, $"/api/reports/{id}/script-content", token));
        return content["version"]!.GetValue<long>();
    }

    /// <summary>A folder owned by <paramref name="ownerId"/>, or by the administrator.</summary>
    private Task<int> CreateFolderAsync(int? ownerId) => CreateFolderAsync(ownerId, fixture.Portal);

    private static async Task<int> CreateFolderAsync(int? ownerId, PortalWebFactory portal)
    {
        using var scope = portal.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var owner = ownerId ?? await db.Users.Where(user => user.UserName == "admin").Select(user => user.Id).SingleAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var folder = new Folder { Name = $"Pipelines {suffix}", Path = $"/Pipelines-{suffix}", OwnerId = owner };
        db.Folders.Add(folder);
        await db.SaveChangesAsync();
        return folder.Id;
    }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, string token, object? body = null)
    {
        using var request = Request(method, path, token, body);
        return await fixture.Client.SendAsync(request);
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }

    private static string Sha(string text) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static async Task<JsonObject> Body(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonObject>(Json))!;
}
