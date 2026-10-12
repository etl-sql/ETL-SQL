using System.Net;
using System.Net.Http.Json;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Tests;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// An ordinary author runs a saved pipeline from Portal Studio. The author is a Publisher who owns
/// one folder and holds no administrative or Orchestrator role, so every run is decided twice: by the
/// Portal for the document and by the Orchestrator for the job. An operator, acting on the
/// Orchestrator directly, provisions the job and grants EXECUTE.
/// </summary>
[Trait("Category", "Browser")]
[Collection(PortalJourneyCollection.Name)]
public sealed class StudioPipelineRunJourneyTests(PortalJourneyFixture fixture)
{
    private const string AuthorPassword = "Author@Journey99!";
    private const string Passing =
        "CREATE CONNECTION sample_data AS MOCKDB();\nSELECT UserID INTO #users FROM sample_data.Users;\n"
        + "ASSERT (SELECT COUNT(*) FROM #users) > 0;\n";
    private const string Failing =
        "CREATE CONNECTION sample_data AS MOCKDB();\nSELECT UserID INTO #users FROM sample_data.Users;\n"
        + "ASSERT (SELECT COUNT(*) FROM #users) = -1;\n";

    private static readonly OrchestratorCaller Operator =
        new("user", "journey-operator", "operator", ["OrchestratorManager"], [], "portal-host");

    [Fact]
    public async Task AnOrdinaryAuthorRunsASavedPipelineAndFollowsThatRun()
    {
        var author = await CreateAuthorAsync();
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await SignInAsync(page, author.UserName);
        await page.GotoAsync("/studio.html");

        // Create and save, as the author.
        var pipelineId = await CreatePipelineInStudioAsync(page, author);
        var job = $"PIPELINE:{pipelineId}";
        await SetAndSaveAsync(page, pipelineId, Passing);

        // No job is provisioned and the author may not create one. Run says so; nothing runs.
        await RunAsync(page);
        await WaitForDoneAsync(page, pipelineId, "Not run");
        Assert.Contains("No Orchestrator job is provisioned", await MessagesAsync(page, pipelineId));
        Assert.Null(await StoredJobAsync(job));

        // The operator provisions the job. The author still holds no grant on it.
        await OperatorProvisionsAsync(job);
        await RunAsync(page);
        await WaitForDoneAsync(page, pipelineId, "Not run");
        Assert.Contains("holds no EXECUTE grant", await MessagesAsync(page, pipelineId));

        // With EXECUTE the saved revision runs, and Studio follows the run it was given.
        await OperatorGrantsAsync(job, author.PrincipalKey, "EXECUTE");
        var before = await HistoryAsync(job);
        await RunAsync(page);
        await WaitForDoneAsync(page, pipelineId, "Complete");
        var runId = await CurrentRunIdAsync(page, pipelineId);
        var after = await HistoryAsync(job);
        var run = Assert.Single(after, entry => before.All(old => old.Id != entry.Id));
        Assert.Equal(run.Id, runId);
        Assert.Equal("SUCCESS", run.Status);
        var log = await MessagesAsync(page, pipelineId);
        Assert.Contains($"Run {runId} accepted by the Orchestrator", log);
        Assert.Contains($"Run {runId} finished with status SUCCESS", log);
        // The panel shows it, not only the stored trace.
        await Expect(page.Locator(".etlsql-script-results")).ToContainTextAsync($"Run {runId} finished with status SUCCESS");

        // Unsaved edits are refused: Run means the saved revision.
        await page.EvaluateAsync("body => window.__STUDIO__.state.editorInstance.setValue(body)", Failing);
        await page.WaitForFunctionAsync(
            "id => window.__STUDIO__.state.documents.find(d => d.reportId === id)?.isDirty === true", pipelineId);
        await RunAsync(page);
        await WaitForStatusTextAsync(page, pipelineId, "unsaved edits");
        Assert.Equal(after.Count, (await HistoryAsync(job)).Count);

        // Saved, the failing revision runs and its failure is shown as a failure, with its run id.
        await SaveAsync(page, pipelineId);
        await RunAsync(page);
        await WaitForDoneAsync(page, pipelineId, "Failed");
        var failedRunId = await CurrentRunIdAsync(page, pipelineId);
        Assert.NotEqual(runId, failedRunId);
        var failedLog = await MessagesAsync(page, pipelineId);
        Assert.Contains($"Run {failedRunId} finished with status FAILURE", failedLog);
        Assert.DoesNotContain($"Run {runId} ", failedLog);
        Assert.Equal("FAILURE", (await HistoryAsync(job)).Single(entry => entry.Id == failedRunId).Status);

        // Reopened in a fresh page, the same document runs again from the catalog.
        await page.GotoAsync("/studio.html");
        var open = page.Locator($"[data-open-report='{pipelineId}'][data-open-proj='code']");
        await Expect(open).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await open.ClickAsync();
        await page.WaitForFunctionAsync(
            "id => { const s = window.__STUDIO__?.state; return s?.editorInstance && s.documents.some(d => d.reportId === id); }",
            pipelineId, new PageWaitForFunctionOptions { Timeout = 20_000 });
        await SetAndSaveAsync(page, pipelineId, Passing);
        await RunAsync(page);
        await WaitForDoneAsync(page, pipelineId, "Complete");

        // And stops a run of that same pipeline, holding nothing more than EXECUTE on its job.
        await SetAndSaveAsync(page, pipelineId, "WAITFOR DELAY '00:00:40';\n");
        await RunAsync(page);
        await WaitForStatusTextAsync(page, pipelineId, "accepted by the Orchestrator");
        var stoppedRunId = await CurrentRunIdAsync(page, pipelineId);
        await page.Locator("[data-action='stop']").ClickAsync();
        await WaitForDoneAsync(page, pipelineId, "Cancelled");
        Assert.Equal("CANCELLED", (await HistoryAsync(job)).Single(entry => entry.Id == stoppedRunId).Status);

        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task StopReachesTheRunAndStudioSaysOnlyWhatWasConfirmed()
    {
        var author = await CreateAuthorAsync();
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await SignInAsync(page, author.UserName);
        await page.GotoAsync("/studio.html");
        var pipelineId = await CreatePipelineInStudioAsync(page, author);
        var job = $"PIPELINE:{pipelineId}";
        await OperatorProvisionsAsync(job);
        await OperatorGrantsAsync(job, author.PrincipalKey, "EXECUTE");

        // Confirmed: the Orchestrator stops the run and records it as cancelled.
        await SetAndSaveAsync(page, pipelineId, "WAITFOR DELAY '00:00:40';\n");
        await RunAsync(page);
        await WaitForStatusTextAsync(page, pipelineId, "accepted by the Orchestrator");
        var runId = await CurrentRunIdAsync(page, pipelineId);
        await page.Locator("[data-action='stop']").ClickAsync();
        await WaitForDoneAsync(page, pipelineId, "Cancelled");
        var log = await MessagesAsync(page, pipelineId);
        Assert.Contains($"Cancellation confirmed by the Orchestrator: run {runId} was stopped.", log);
        // The run's identity and outcome stay on screen after it is stopped.
        Assert.Contains($"Run {runId} accepted by the Orchestrator", log);
        Assert.Contains($"Run {runId} finished with status CANCELLED", log);
        Assert.Equal("CANCELLED", (await HistoryAsync(job)).Single(entry => entry.Id == runId).Status);
        Assert.Equal(runId, await CurrentRunIdAsync(page, pipelineId));

        // Unconfirmed: the stop request never reaches the Portal. Studio says so, keeps following
        // the run, and reports how it really ended.
        await page.RouteAsync("**/api/studio/pipelines/*/runs/*/cancel", route => route.AbortAsync());
        await SetAndSaveAsync(page, pipelineId, "WAITFOR DELAY '00:00:04';\n");
        await RunAsync(page);
        await WaitForStatusTextAsync(page, pipelineId, "accepted by the Orchestrator");
        var survivor = await CurrentRunIdAsync(page, pipelineId);
        Assert.NotEqual(runId, survivor);
        await page.Locator("[data-action='stop']").ClickAsync();
        await WaitForStatusTextAsync(page, pipelineId, $"Stop was not confirmed for run {survivor}");
        Assert.Contains("may still be running", await MessagesAsync(page, pipelineId));
        await WaitForDoneAsync(page, pipelineId, "Complete");
        Assert.Contains(
            $"Stop was requested, but run {survivor} had already finished as SUCCESS. It was not cancelled.",
            await MessagesAsync(page, pipelineId));
        Assert.Equal("SUCCESS", (await HistoryAsync(job)).Single(entry => entry.Id == survivor).Status);

        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AnAuthorSchedulesTheSavedRevisionThroughTheOrchestrator()
    {
        var author = await CreateAuthorAsync();
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await SignInAsync(page, author.UserName);
        await page.GotoAsync("/studio.html");
        var pipelineId = await CreatePipelineInStudioAsync(page, author);
        var job = $"PIPELINE:{pipelineId}";
        await SetAndSaveAsync(page, pipelineId, Passing);
        await OperatorProvisionsAsync(job);
        await OperatorGrantsAsync(job, author.PrincipalKey, "EXECUTE");

        await page.Locator("[data-activity='governance']").ClickAsync();
        var panel = page.Locator("[data-gov-hosted-schedule]");
        await panel.WaitForAsync(new() { Timeout = 15_000 });
        var status = page.Locator("[data-gov-hosted-schedule-status]");
        await Expect(status).ToContainTextAsync("Not scheduled", new() { Timeout = 15_000 });
        // The statement-writing form is not offered: a catalog pipeline has no path a job could name.
        Assert.Equal(0, await page.Locator("[data-gov-schedule-apply]").CountAsync());

        // EXECUTE runs the job. It does not decide what runs unattended.
        await page.Locator("[data-gov-hosted-schedule-cron]").FillAsync("0 2 * * *");
        await page.Locator("[data-gov-hosted-schedule-zone]").FillAsync("UTC");
        await page.Locator("[data-gov-hosted-schedule-apply]").ClickAsync();
        await Expect(page.Locator("[data-gov-hosted-schedule-error]")).ToContainTextAsync("MANAGE grant", new() { Timeout = 15_000 });
        Assert.Null(await StoredScheduleAsync(job));

        await OperatorGrantsAsync(job, author.PrincipalKey, "MANAGE");
        await page.Locator("[data-gov-hosted-schedule-cron]").FillAsync("0 2 * * *");
        await page.Locator("[data-gov-hosted-schedule-zone]").FillAsync("UTC");
        await page.Locator("[data-gov-hosted-schedule-apply]").ClickAsync();
        await Expect(status).ToContainTextAsync("Runs on 0 2 * * * (UTC)", new() { Timeout = 15_000 });

        // The Orchestrator holds it: a schedule on the job, and the saved revision pinned with a
        // blocking hash. Nothing was written into the script.
        var schedule = await StoredScheduleAsync(job);
        Assert.Equal("0 2 * * *", schedule!.Cron);
        var stored = await StoredJobAsync(job);
        Assert.Equal(Passing, stored!.Script.Replace("\r\n", "\n"));
        Assert.Equal("Block", stored.HashPolicy);
        Assert.Equal(Passing, (await page.EvaluateAsync<string>(
            "() => window.__STUDIO__.state.editorInstance.getValue()")).Replace("\r\n", "\n"));

        // Saved again, the schedule still runs what it pinned, and the panel says so.
        await SetAndSaveAsync(page, pipelineId, Failing);
        // Close and reopen the rail so it reads the schedule against the new saved version.
        await page.Locator("[data-activity='governance']").ClickAsync();
        await Expect(panel).ToBeHiddenAsync(new() { Timeout = 15_000 });
        await page.Locator("[data-activity='governance']").ClickAsync();
        await Expect(page.Locator("[data-gov-hosted-schedule-stale]")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        Assert.Equal(Passing, (await StoredJobAsync(job))!.Script.Replace("\r\n", "\n"));

        await page.Locator("[data-gov-hosted-schedule-remove]").ClickAsync();
        await Expect(status).ToContainTextAsync("Not scheduled", new() { Timeout = 15_000 });
        Assert.Null(await StoredScheduleAsync(job));

        Assert.Empty(session.PageErrors);
    }

    private async Task<ScheduleDefinition?> StoredScheduleAsync(string job)
    {
        using var scope = fixture.Orchestrator.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IJobCatalogStore>().GetScheduleAsync("portal-host", job);
    }

    // ── Studio ───────────────────────────────────────────────────────────────

    /// <summary>Creates a pipeline from Studio Home in the author's folder and returns its id.</summary>
    private static async Task<int> CreatePipelineInStudioAsync(IPage page, Author author)
    {
        var card = page.Locator("[data-create-from-home='etl']:not([data-seed-sample])");
        await Expect(card).Not.ToHaveAttributeAsync("aria-disabled", "true", new() { Timeout = 20_000 });
        await card.ClickAsync();
        var name = $"Load users {Guid.NewGuid():N}"[..20];
        await page.Locator("[data-catalog-report-name]").FillAsync(name);
        await page.Locator("[data-catalog-report-folder]").SelectOptionAsync(author.FolderId.ToString());
        await page.Locator("[data-catalog-create-confirm]").ClickAsync();
        await page.WaitForFunctionAsync(
            "name => (window.__STUDIO__?.state?.documents || []).some(d => d.name === name + '.etlsql' && d.reportId) && window.__STUDIO__.state.editorInstance",
            name, new PageWaitForFunctionOptions { Timeout = 20_000 });
        return await page.EvaluateAsync<int>(
            "name => window.__STUDIO__.state.documents.find(d => d.name === name + '.etlsql').reportId", name);
    }

    private static Task RunAsync(IPage page) => page.Locator("[data-action='run']").ClickAsync();

    private static async Task SetAndSaveAsync(IPage page, int pipelineId, string body)
    {
        await page.EvaluateAsync("body => window.__STUDIO__.state.editorInstance.setValue(body)", body);
        await page.WaitForFunctionAsync(
            "id => window.__STUDIO__.state.documents.find(d => d.reportId === id)?.isDirty === true", pipelineId);
        await SaveAsync(page, pipelineId);
    }

    private static async Task SaveAsync(IPage page, int pipelineId)
    {
        await page.Locator("[data-action='save']").ClickAsync();
        await page.WaitForFunctionAsync(
            "id => window.__STUDIO__.state.documents.find(d => d.reportId === id)?.isDirty === false", pipelineId,
            new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    private const string Trace =
        "(window.__STUDIO__.state.documents.find(d => d.reportId === id)?.studioContext?.resultsTrace || [])";

    /// <summary>Waits for the run on this document to end with the given outcome label.</summary>
    private static async Task WaitForDoneAsync(IPage page, int pipelineId, string outcome)
    {
        try
        {
            await page.WaitForFunctionAsync(
                $"([id, outcome]) => !window.__STUDIO__.state.documents.find(d => d.reportId === id)?.studioContext?.runActive"
                + $" && {Trace}.some(e => e.type === 'done' && e.status === outcome)",
                new object[] { pipelineId, outcome }, new PageWaitForFunctionOptions { Timeout = 30_000 });
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException(
                $"The run did not end as '{outcome}'. Messages: {await MessagesAsync(page, pipelineId)}", ex);
        }
    }

    private static Task WaitForStatusTextAsync(IPage page, int pipelineId, string text) =>
        page.WaitForFunctionAsync(
            $"([id, text]) => {Trace}.some(e => e.type === 'message' && String(e.text).includes(text))",
            new object[] { pipelineId, text }, new PageWaitForFunctionOptions { Timeout = 15_000 });

    private static Task<string> MessagesAsync(IPage page, int pipelineId) =>
        page.EvaluateAsync<string>(
            $"id => {Trace}.filter(e => e.type === 'message').map(e => String(e.text)).join('\\n')", pipelineId);

    private static async Task<long> CurrentRunIdAsync(IPage page, int pipelineId) =>
        long.Parse(await page.EvaluateAsync<string>(
            "id => String(window.__STUDIO__.state.documents.find(d => d.reportId === id).studioContext.currentRunId)", pipelineId));

    private static async Task SignInAsync(IPage page, string userName)
    {
        await page.GotoAsync("/login.html");
        await page.FillAsync("#username", userName);
        await page.FillAsync("#password", AuthorPassword);
        await page.ClickAsync("#loginBtn");
        await page.WaitForURLAsync("**/index.html", new PageWaitForURLOptions { Timeout = 20_000 });
    }

    // ── The people involved ──────────────────────────────────────────────────

    private sealed record Author(string UserName, string PrincipalKey, int FolderId);

    /// <summary>A Publisher who owns one folder. No administrative or Orchestrator role.</summary>
    private async Task<Author> CreateAuthorAsync()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new PortalUser
        {
            UserName = $"author_{suffix}",
            Email = $"author_{suffix}@test.local",
            IsActive = true,
            MustChangePassword = false,
            Provider = "Local"
        };
        var created = await users.CreateAsync(user, AuthorPassword);
        Assert.True(created.Succeeded, string.Join(", ", created.Errors.Select(error => error.Description)));
        Assert.True((await users.AddToRoleAsync(user, "Publisher")).Succeeded);

        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var folder = new Folder { Name = $"Author {suffix}", Path = $"/Author-{suffix}", OwnerId = user.Id };
        db.Folders.Add(folder);
        await db.SaveChangesAsync();
        var key = await db.Users.AsNoTracking().Where(item => item.Id == user.Id).Select(item => item.PrincipalKey).SingleAsync();
        Assert.False(string.IsNullOrWhiteSpace(key));
        return new Author(user.UserName, key!, folder.Id);
    }

    private async Task OperatorProvisionsAsync(string job)
    {
        using var client = fixture.Orchestrator.CreateClient();
        using var request = OperatorRequest(HttpMethod.Post, $"/api/scheduled-jobs/{Uri.EscapeDataString(job)}/runs",
            new { scriptText = "SELECT 1 AS Provisioned;", provisionIfMissing = true });
        Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(request)).StatusCode);
        // The job's start guard clears just after its run is recorded.
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var history = await HistoryAsync(job);
            if (history.Count > 0 && history.All(entry => entry.EndTime is not null)) break;
            await Task.Delay(25);
        }
        await Task.Delay(250);
    }

    private async Task OperatorGrantsAsync(string job, string principalKey, string permission)
    {
        using var client = fixture.Orchestrator.CreateClient();
        using var request = OperatorRequest(HttpMethod.Put,
            $"/api/authorization/JOB/{Uri.EscapeDataString(job)}/USER/{principalKey}", new { permission });
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);
    }

    private static HttpRequestMessage OperatorRequest(HttpMethod method, string path, object body)
    {
        var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Orchestrator-Key", "test-orch-key-12345");
        request.Headers.Add(OrchestratorIdentityAssertion.HeaderName,
            OrchestratorIdentityAssertion.Create(Operator, OrchestratorWebFactory.IdentitySecret));
        return request;
    }

    private async Task<JobDefinition?> StoredJobAsync(string job)
    {
        using var scope = fixture.Orchestrator.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IJobHistoryStore>().GetJobAsync("portal-host", job);
    }

    private async Task<List<JobHistoryEntry>> HistoryAsync(string job)
    {
        using var scope = fixture.Orchestrator.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IJobHistoryStore>();
        var stored = await store.GetJobAsync("portal-host", job);
        return stored is null ? [] : [.. await store.GetHistoryAsync(stored.Id, 50)];
    }
}
