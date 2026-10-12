using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Core.Data;
using ETL_SQL.Portal.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ETL_SQL.Portal.Tests;

/// <summary>A clock the test moves. The scheduler asks it when a schedule link is due.</summary>
public sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    private long ticks = start.UtcTicks;

    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref ticks), TimeSpan.Zero);

    public void Set(DateTimeOffset value) => Interlocked.Exchange(ref ticks, value.UtcTicks);
}

/// <summary>
/// The pipeline-run fixture with the Orchestrator's scheduler on a <see cref="ManualClock"/>. The
/// scheduler loop itself keeps ticking once a real second, so moving the clock is what makes an
/// occurrence due.
/// </summary>
public sealed class StudioPipelineScheduleFixture : StudioPipelineRunFixture
{
    public static readonly DateTimeOffset Start = new(2030, 1, 1, 10, 0, 0, TimeSpan.Zero);

    public StudioPipelineScheduleFixture() : this(new ManualClock(Start)) { }

    private StudioPipelineScheduleFixture(ManualClock clock) : base(new ClockedOrchestratorFactory(clock)) => Clock = clock;

    public ManualClock Clock { get; }

    private sealed class ClockedOrchestratorFactory(ManualClock clock) : OrchestratorWebFactory(requireFederatedIdentity: true)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock);
            });
        }
    }
}

/// <summary>
/// ST07: scheduling a saved pipeline is the Run handoff with a cadence. The saved revision is pinned
/// into the pipeline's Orchestrator job, a cron schedule is attached, and the occurrence fires when
/// the clock reaches it — running the pinned revision, not whatever was saved afterwards.
/// </summary>
[Trait("Category", "Portal")]
public sealed class StudioPipelineScheduleTests(StudioPipelineScheduleFixture fixture)
    : IClassFixture<StudioPipelineScheduleFixture>
{
    private const string Script =
        "CREATE CONNECTION sample_data AS MOCKDB();\nSELECT UserID INTO #users FROM sample_data.Users;\n"
        + "ASSERT (SELECT COUNT(*) FROM #users) > 0;\n";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task AScheduledPipelineRunsItsPinnedRevisionWhenTheClockReachesTheOccurrence()
    {
        fixture.Clock.Set(StudioPipelineScheduleFixture.Start);
        var (id, version) = await CreatePipelineAsync(fixture.AdminToken);
        var job = $"PIPELINE:{id}";

        var set = await Send(HttpMethod.Put, $"/api/studio/pipelines/{id}/schedule", fixture.AdminToken,
            new { version, cron = "0 2 * * *", timeZone = "UTC" });
        Assert.True(set.IsSuccessStatusCode, $"schedule: {(int)set.StatusCode} {await set.Content.ReadAsStringAsync()}");
        var schedule = await Body(set);
        Assert.True(schedule["scheduled"]!.GetValue<bool>());
        Assert.Equal("0 2 * * *", schedule["cron"]!.GetValue<string>());
        Assert.Equal(Sha(Script), schedule["scheduledScriptHash"]!.GetValue<string>());
        Assert.True(schedule["scheduledRevisionIsSaved"]!.GetValue<bool>());
        // Armed at the next occurrence after "now", not fired on the spot.
        Assert.Equal(new DateTime(2030, 1, 2, 2, 0, 0, DateTimeKind.Utc), NextRun(schedule));

        // The scheduler ticks every real second. Nothing is due at 10:00, so nothing runs.
        await Task.Delay(2500);
        Assert.Empty(await HistoryAsync(job));

        // A later save does not change what the schedule runs, and Studio is told they differ.
        var edited = Script + "ASSERT (SELECT COUNT(*) FROM #users) >= 1;\n";
        var saved = await SaveAsync(id, version, edited);
        var afterSave = await Body(await Send(HttpMethod.Get, $"/api/studio/pipelines/{id}/schedule", fixture.AdminToken));
        Assert.False(afterSave["scheduledRevisionIsSaved"]!.GetValue<bool>());
        Assert.Equal(Sha(Script), afterSave["scheduledScriptHash"]!.GetValue<string>());
        Assert.Equal(saved, afterSave["savedVersion"]!.GetValue<long>());

        // A run on request executes the new save, and consumes nothing of the schedule.
        var manual = await Body(await Send(HttpMethod.Post, $"/api/studio/pipelines/{id}/runs", fixture.AdminToken, new { version = saved }));
        var manualRunId = manual["runId"]!.GetValue<long>();
        Assert.Equal(Sha(edited), (await FollowAsync(id, manualRunId))["scriptHash"]!.GetValue<string>());
        var afterManual = await Body(await Send(HttpMethod.Get, $"/api/studio/pipelines/{id}/schedule", fixture.AdminToken));
        Assert.Equal(new DateTime(2030, 1, 2, 2, 0, 0, DateTimeKind.Utc), NextRun(afterManual));
        Assert.Equal(Sha(Script), afterManual["scheduledScriptHash"]!.GetValue<string>());

        // The clock reaches the occurrence.
        fixture.Clock.Set(new DateTimeOffset(2030, 1, 2, 2, 0, 5, TimeSpan.Zero));
        var scheduled = await WaitForRunAsync(job, entry => entry.Id != manualRunId && entry.EndTime is not null);
        Assert.Equal("SUCCESS", scheduled.Status);
        // The pinned revision ran, not the later save.
        Assert.Equal(Sha(Script), scheduled.ScriptHashAtRunTime);
        Assert.True(scheduled.HashMatched);

        // Its output is readable through the same route as any other run of this pipeline.
        var run = await Body(await Send(HttpMethod.Get, $"/api/studio/pipelines/{id}/runs/{scheduled.Id}", fixture.AdminToken));
        Assert.Equal("SUCCESS", run["status"]!.GetValue<string>());
        var statements = run["statements"]!.AsArray().Select(item => item!["statement"]!.GetValue<string>()).ToList();
        Assert.Contains(statements, statement => statement.StartsWith("SELECT UserID INTO #users", StringComparison.Ordinal));
        Assert.DoesNotContain(statements, statement => statement.Contains(">= ?", StringComparison.Ordinal));

        // One occurrence, one run: the link moved to the next day and does not fire again.
        var afterRun = await WaitForScheduleAsync(id, item => NextRun(item) == new DateTime(2030, 1, 3, 2, 0, 0, DateTimeKind.Utc));
        Assert.NotNull(afterRun["lastRun"]);
        await Task.Delay(2500);
        Assert.Equal(2, (await HistoryAsync(job)).Count);

        // Unscheduled, the job is a run slot again and a later occurrence does not fire.
        var removed = await Send(HttpMethod.Delete, $"/api/studio/pipelines/{id}/schedule", fixture.AdminToken);
        Assert.True(removed.IsSuccessStatusCode);
        Assert.False((await Body(removed))["scheduled"]!.GetValue<bool>());
        fixture.Clock.Set(new DateTimeOffset(2030, 1, 5, 2, 0, 5, TimeSpan.Zero));
        await Task.Delay(2500);
        Assert.Equal(2, (await HistoryAsync(job)).Count);
        using (var scope = fixture.Orchestrator.Services.CreateScope())
        {
            var stored = await scope.ServiceProvider.GetRequiredService<IJobHistoryStore>().GetJobAsync("portal-host", job);
            Assert.True(stored!.NextRun > DateTime.Now.AddYears(100), $"next run was {stored.NextRun:O}");
        }

        using (var scope = fixture.Portal.Services.CreateScope())
        {
            var audited = await scope.ServiceProvider.GetRequiredService<PortalDbContext>().AuditLogs
                .Where(entry => entry.ResourceId == id.ToString()).Select(entry => entry.Action).ToListAsync();
            Assert.Contains("PIPELINE_SCHEDULED", audited);
            Assert.Contains("PIPELINE_UNSCHEDULED", audited);
        }
    }

    [Fact]
    public async Task SchedulingNeedsManageOnTheJobAndASavedVersion()
    {
        fixture.Clock.Set(StudioPipelineScheduleFixture.Start);
        // The author owns the folder, so every refusal below is about the schedule, not the document.
        var (id, version) = await CreatePipelineAsync(fixture.AuthorToken, fixture.AuthorId);
        var job = $"PIPELINE:{id}";

        var unprovisioned = await Send(HttpMethod.Put, $"/api/studio/pipelines/{id}/schedule", fixture.AuthorToken,
            new { version, cron = "0 2 * * *" });
        Assert.Equal(HttpStatusCode.Forbidden, unprovisioned.StatusCode);
        Assert.Equal("job_not_provisioned", (await Body(unprovisioned))["code"]!.GetValue<string>());

        // Provisioned by an administrator's run, with EXECUTE granted to the author.
        var first = await Body(await Send(HttpMethod.Post, $"/api/studio/pipelines/{id}/runs", fixture.AdminToken, new { version }));
        await FollowAsync(id, first["runId"]!.GetValue<long>());
        using (var grant = await Send(HttpMethod.Put,
            $"/api/orchestrator/authorization/JOB/{Uri.EscapeDataString(job)}/USER/{fixture.AuthorPrincipalKey}",
            fixture.AdminToken, new { permission = "EXECUTE" }))
            Assert.Equal(HttpStatusCode.OK, grant.StatusCode);

        // EXECUTE runs it. It does not decide what runs unattended.
        var executeOnly = await Send(HttpMethod.Put, $"/api/studio/pipelines/{id}/schedule", fixture.AuthorToken,
            new { version, cron = "0 2 * * *" });
        Assert.Equal(HttpStatusCode.Forbidden, executeOnly.StatusCode);
        Assert.Equal("schedule_grant_denied", (await Body(executeOnly))["code"]!.GetValue<string>());
        Assert.False((await Body(await Send(HttpMethod.Get, $"/api/studio/pipelines/{id}/schedule", fixture.AuthorToken)))
            ["scheduled"]!.GetValue<bool>());

        using (var grant = await Send(HttpMethod.Put,
            $"/api/orchestrator/authorization/JOB/{Uri.EscapeDataString(job)}/USER/{fixture.AuthorPrincipalKey}",
            fixture.AdminToken, new { permission = "MANAGE" }))
            Assert.Equal(HttpStatusCode.OK, grant.StatusCode);

        var stale = await Send(HttpMethod.Put, $"/api/studio/pipelines/{id}/schedule", fixture.AuthorToken,
            new { version = version + 7, cron = "0 2 * * *" });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("stale_revision", (await Body(stale))["code"]!.GetValue<string>());

        var nonsense = await Send(HttpMethod.Put, $"/api/studio/pipelines/{id}/schedule", fixture.AuthorToken,
            new { version, cron = "every now and then" });
        Assert.Equal(HttpStatusCode.BadRequest, nonsense.StatusCode);
        Assert.Equal("invalid_schedule", (await Body(nonsense))["code"]!.GetValue<string>());

        var managed = await Send(HttpMethod.Put, $"/api/studio/pipelines/{id}/schedule", fixture.AuthorToken,
            new { version, cron = "0 6 * * 1", timeZone = "UTC" });
        Assert.True(managed.IsSuccessStatusCode, $"schedule: {(int)managed.StatusCode} {await managed.Content.ReadAsStringAsync()}");
        Assert.True((await Body(managed))["scheduled"]!.GetValue<bool>());

        Assert.True((await Send(HttpMethod.Delete, $"/api/studio/pipelines/{id}/schedule", fixture.AuthorToken)).IsSuccessStatusCode);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static DateTime? NextRun(JsonObject schedule) =>
        schedule["nextRun"] is { } value ? value.GetValue<DateTime>().ToUniversalTime() : null;

    private async Task<JobHistoryEntry> WaitForRunAsync(string job, Func<JobHistoryEntry, bool> matches)
    {
        for (var attempt = 0; attempt < 400; attempt++)
        {
            var found = (await HistoryAsync(job)).FirstOrDefault(matches);
            if (found is not null) return found;
            await Task.Delay(50);
        }
        throw new TimeoutException($"No scheduled run of '{job}' appeared.");
    }

    private async Task<JsonObject> WaitForScheduleAsync(int id, Func<JsonObject, bool> matches)
    {
        JsonObject schedule = [];
        for (var attempt = 0; attempt < 200; attempt++)
        {
            schedule = await Body(await Send(HttpMethod.Get, $"/api/studio/pipelines/{id}/schedule", fixture.AdminToken));
            if (matches(schedule)) return schedule;
            await Task.Delay(50);
        }
        throw new TimeoutException($"The schedule did not reach the expected state: {schedule.ToJsonString()}");
    }

    private async Task<List<JobHistoryEntry>> HistoryAsync(string job)
    {
        using var scope = fixture.Orchestrator.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IJobHistoryStore>();
        var stored = await store.GetJobAsync("portal-host", job);
        return stored is null ? [] : [.. await store.GetHistoryAsync(stored.Id, 50)];
    }

    private async Task<JsonObject> FollowAsync(int id, long runId)
    {
        for (var attempt = 0; attempt < 400; attempt++)
        {
            var run = await Body(await Send(HttpMethod.Get, $"/api/studio/pipelines/{id}/runs/{runId}", fixture.AdminToken));
            if (run["isTerminal"]!.GetValue<bool>())
            {
                // The job's start guard clears just after the run is recorded.
                await Task.Delay(250);
                return run;
            }
            await Task.Delay(25);
        }
        throw new TimeoutException($"Run {runId} did not reach a terminal status.");
    }

    private async Task<(int Id, long Version)> CreatePipelineAsync(string token, int? ownerId = null)
    {
        int folderId;
        using (var scope = fixture.Portal.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var owner = ownerId ?? await db.Users.Where(user => user.UserName == "admin").Select(user => user.Id).SingleAsync();
            var suffix = Guid.NewGuid().ToString("N")[..8];
            var folder = new Folder { Name = $"Scheduled {suffix}", Path = $"/Scheduled-{suffix}", OwnerId = owner };
            db.Folders.Add(folder);
            await db.SaveChangesAsync();
            folderId = folder.Id;
        }
        var created = await Send(HttpMethod.Post, "/api/studio/reports", token,
            new { folderId, name = $"Nightly users {Guid.NewGuid():N}"[..24], scriptText = Script, kind = "Pipeline" });
        Assert.True(created.IsSuccessStatusCode, $"create: {(int)created.StatusCode} {await created.Content.ReadAsStringAsync()}");
        var pipeline = await Body(created);
        return (pipeline["id"]!.GetValue<int>(), pipeline["version"]!.GetValue<long>());
    }

    private async Task<long> SaveAsync(int id, long version, string text)
    {
        Assert.True((await Send(HttpMethod.Post, "/api/designer/lease", fixture.AdminToken, new { reportId = id, force = false })).IsSuccessStatusCode);
        using var save = new HttpRequestMessage(HttpMethod.Post, "/api/designer/save")
        {
            Content = JsonContent.Create(new { reportId = id, scriptText = text })
        };
        save.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.AdminToken);
        save.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        var saved = await fixture.Client.SendAsync(save);
        Assert.True(saved.IsSuccessStatusCode, $"save: {(int)saved.StatusCode} {await saved.Content.ReadAsStringAsync()}");
        return (await Body(await Send(HttpMethod.Get, $"/api/reports/{id}/script-content", fixture.AdminToken)))["version"]!.GetValue<long>();
    }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, string token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await fixture.Client.SendAsync(request);
    }

    private static string Sha(string text) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static async Task<JsonObject> Body(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonObject>(Json))!;
}
