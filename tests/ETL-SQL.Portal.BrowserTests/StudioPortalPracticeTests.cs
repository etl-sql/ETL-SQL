using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Tests;
using ETL_SQL.TestSupport;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// The beginner ETL exercise in Portal Studio: a validation that fails, its repair, and the rows
/// and cleanup that follow. The learner is a Publisher who owns one folder and holds EXECUTE on the
/// pipeline's Orchestrator job; an operator provisions that job. Every run is a saved revision
/// submitted to the Orchestrator, which executes it in process (see the practice-execution decision).
///
/// <para>A Portal run returns a statement log, not rows, so the script proves its own output: each
/// expected row, the row count and the cleanup are assertions that fail the run when they are wrong.</para>
/// </summary>
[Trait("Category", "Browser")]
[Collection(PortalJourneyCollection.Name)]
public sealed class StudioPortalPracticeTests(PortalJourneyFixture fixture)
{
    public bool RequireSandboxEvidence { get; init; }
    private const string LearnerPassword = "Learner@Journey99!";
    private const string Tables = "'#practice_stage', '#practice_transform', '#practice_load'";
    private const string RepairHint = "Expected three users: repair the transformation filter.";

    private const string Exercise = $"""
        CREATE CONNECTION sample_data AS MOCKDB();
        SELECT UserID INTO #practice_stage FROM sample_data.Users WHERE UserID BETWEEN 1 AND 3;
        SELECT UserID, UserID * 10 AS Score INTO #practice_transform FROM #practice_stage WHERE UserID < 3;
        ASSERT (SELECT COUNT(*) FROM #practice_transform) = 3, '{RepairHint}';
        SELECT UserID, Score INTO #practice_load FROM #practice_transform;
        ASSERT (SELECT COUNT(*) FROM #practice_load) = 3, 'Load must contain exactly three rows.';
        ASSERT (SELECT COUNT(*) FROM #practice_load WHERE UserID = 1 AND Score = 10) = 1, 'Expected (1, 10) once.';
        ASSERT (SELECT COUNT(*) FROM #practice_load WHERE UserID = 2 AND Score = 20) = 1, 'Expected (2, 20) once.';
        ASSERT (SELECT COUNT(*) FROM #practice_load WHERE UserID = 3 AND Score = 30) = 1, 'Expected (3, 30) once.';
        ASSERT (SELECT COUNT(*) FROM eng.columns WHERE table_name IN ({Tables})) > 0, 'The practice tables must be visible before they are dropped.';
        DROP TABLE #practice_load;
        DROP TABLE #practice_transform;
        DROP TABLE #practice_stage;
        ASSERT (SELECT COUNT(*) FROM eng.columns WHERE table_name IN ({Tables})) = 0, 'Cleanup must drop every practice table.';

        """;

    // A run of its own: nothing a failed attempt staged may still be there.
    private const string NothingLeftBehind =
        $"ASSERT (SELECT COUNT(*) FROM eng.columns WHERE table_name IN ({Tables})) = 0, 'A new run must not see an earlier run''s tables.';\n";

    private static readonly OrchestratorCaller Operator =
        new("user", "practice-operator", "operator", ["OrchestratorManager"], [], "portal-host");

    [Fact]
    public async Task ALearnerFailsRepairsLoadsAndCleansThePracticePipeline()
    {
        var learner = await CreateLearnerAsync();
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await SignInAsync(page, learner.UserName);
        await page.GotoAsync("/studio.html");

        var pipelineId = await CreatePipelineInStudioAsync(page, learner);
        var job = $"PIPELINE:{pipelineId}";
        await OperatorProvisionsAsync(job);
        await OperatorGrantsAsync(job, learner.PrincipalKey, "EXECUTE");

        // The deliberate failure: the filter drops user 3, the ASSERT stops the run before the load,
        // and Studio shows it as a failure with the message that says what to repair.
        var exercise = RequireSandboxEvidence
            ? $"ASSERT @@CURRENT_USER = 'user:{learner.PrincipalKey}', 'Sandbox must carry the learner identity.';\n" + Exercise
            : Exercise;
        await TypeScriptAsync(page, pipelineId, exercise);
        await SaveAsync(page, pipelineId);
        var failed = await RunAsync(page, job, pipelineId, "Failed");
        Assert.Equal("FAILURE", failed.Status);
        Assert.True(failed.ErrorMessage?.Contains(RepairHint, StringComparison.Ordinal) == true, failed.ErrorMessage);
        Assert.Equal(await SavedHashAsync(page, pipelineId), failed.ScriptHashAtRunTime);
        await Expect(page.Locator(".etlsql-script-results")).ToContainTextAsync(RepairHint);
        // The log points at the validation that failed, not at the statement before it.
        await Expect(page.Locator(".etlsql-script-results")).ToContainTextAsync("Failed: ASSERT ((SELECT COUNT(*) FROM #practice_transform)");
        await Expect(page.Locator(".etlsql-script-results")).Not.ToContainTextAsync("Failed: SELECT");
        await Expect(page.Locator(".etlsql-script-results")).ToContainTextAsync($"Run {failed.Id} finished with status FAILURE");

        // The failed attempt left nothing for the next run to trip over.
        await TypeScriptAsync(page, pipelineId, NothingLeftBehind);
        await SaveAsync(page, pipelineId);
        Assert.Equal("SUCCESS", (await RunAsync(page, job, pipelineId, "Complete")).Status);

        // The repair keeps the validation and fixes the filter. Twice, to the same result.
        var repaired = exercise.Replace("WHERE UserID < 3", "WHERE UserID <= 3", StringComparison.Ordinal);
        Assert.Contains(RepairHint, repaired);
        await TypeScriptAsync(page, pipelineId, repaired);
        await SaveAsync(page, pipelineId);
        var savedHash = await SavedHashAsync(page, pipelineId);
        Assert.Equal(Sha(repaired), savedHash);
        var first = await RunAsync(page, job, pipelineId, "Complete");
        var second = await RunAsync(page, job, pipelineId, "Complete");
        Assert.NotEqual(first.Id, second.Id);
        Assert.All(new[] { first, second }, run =>
        {
            Assert.Equal("SUCCESS", run.Status);
            Assert.Null(run.ErrorMessage);
            Assert.Equal(savedHash, run.ScriptHashAtRunTime);
            Assert.NotNull(run.EndTime);
        });
        await Expect(page.Locator(".etlsql-script-results")).ToContainTextAsync($"Run {second.Id} finished with status SUCCESS");

        // The learner's document survives its runs: reopened, it is the repaired script.
        await page.GotoAsync("/studio.html");
        var open = page.Locator($"[data-open-report='{pipelineId}'][data-open-proj='code']");
        await Expect(open).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await open.ClickAsync();
        await page.WaitForFunctionAsync(
            "id => { const s = window.__STUDIO__?.state; return s?.editorInstance && s.documents.some(d => d.reportId === id); }",
            pipelineId, new PageWaitForFunctionOptions { Timeout = 20_000 });
        Assert.Equal(Normalize(repaired), Normalize(await page.EvaluateAsync<string>(
            "() => window.__STUDIO__.state.editorInstance.getValue()")));

        // Nothing ran in the Portal's own process, and the job ran only what the learner submitted.
        var history = await HistoryAsync(job);
        Assert.Equal(5, history.Count);
        if (RequireSandboxEvidence)
        {
            // An edited saved revision is still confined: it cannot create an outbound connection.
            var denied = "CREATE CONNECTION outside AS MSSQL(SERVER = '169.254.169.254', DATABASE = 'practice');\nSELECT * FROM outside.Users;\n";
            await TypeScriptAsync(page, pipelineId, denied);
            await SaveAsync(page, pipelineId);
            var refusal = await RunAsync(page, job, pipelineId, "Failed");
            Assert.Equal("FAILURE", refusal.Status);
            Assert.NotNull(refusal.ErrorMessage);
            await TypeScriptAsync(page, pipelineId, repaired);
            await SaveAsync(page, pipelineId);
        }
        Assert.Empty(session.PageErrors);
    }

    // ── Studio ───────────────────────────────────────────────────────────────

    /// <summary>Creates a pipeline from Studio Home in the learner's folder and returns its id.</summary>
    private static async Task<int> CreatePipelineInStudioAsync(IPage page, Learner learner)
    {
        var card = page.Locator("[data-create-from-home='etl']:not([data-seed-sample])");
        await Expect(card).Not.ToHaveAttributeAsync("aria-disabled", "true", new() { Timeout = 20_000 });
        await card.ClickAsync();
        var name = $"Practice {Guid.NewGuid():N}"[..20];
        await page.Locator("[data-catalog-report-name]").FillAsync(name);
        await page.Locator("[data-catalog-report-folder]").SelectOptionAsync(learner.FolderId.ToString());
        await page.Locator("[data-catalog-create-confirm]").ClickAsync();
        await page.WaitForFunctionAsync(
            "name => (window.__STUDIO__?.state?.documents || []).some(d => d.name === name + '.etlsql' && d.reportId) && window.__STUDIO__.state.editorInstance",
            name, new PageWaitForFunctionOptions { Timeout = 20_000 });
        return await page.EvaluateAsync<int>(
            "name => window.__STUDIO__.state.documents.find(d => d.name === name + '.etlsql').reportId", name);
    }

    /// <summary>Replaces the whole script through the editor, with the keyboard.</summary>
    private static async Task TypeScriptAsync(IPage page, int pipelineId, string script)
    {
        await page.Locator(".cm-content").First.ClickAsync();
        await page.Keyboard.PressAsync("ControlOrMeta+a");
        await page.Keyboard.InsertTextAsync(script);
        await page.WaitForFunctionAsync(
            "([id, text]) => window.__STUDIO__.state.documents.find(d => d.reportId === id)?.isDirty === true"
            + " && window.__STUDIO__.state.editorInstance.getValue().replace(/\\r\\n/g, '\\n') === text",
            new object[] { pipelineId, Normalize(script) }, new PageWaitForFunctionOptions { Timeout = 15_000 });
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

    /// <summary>
    /// Presses Run, waits for Studio to report the outcome, and returns the one run the Orchestrator
    /// recorded for it, checked against the run id Studio followed.
    /// </summary>
    private async Task<JobHistoryEntry> RunAsync(IPage page, string job, int pipelineId, string outcome)
    {
        var before = await HistoryAsync(job);
        await page.Locator("[data-action='run']").ClickAsync();
        try
        {
            await page.WaitForFunctionAsync(
                $"([id, outcome, known]) => !window.__STUDIO__.state.documents.find(d => d.reportId === id)?.studioContext?.runActive"
                + $" && !known.includes(String(window.__STUDIO__.state.documents.find(d => d.reportId === id)?.studioContext?.currentRunId))"
                + $" && {Trace}.some(e => e.type === 'done' && e.status === outcome)",
                new object[] { pipelineId, outcome, before.Select(entry => entry.Id.ToString()).ToArray() },
                new PageWaitForFunctionOptions { Timeout = 30_000 });
        }
        catch (TimeoutException ex)
        {
            var messages = await page.EvaluateAsync<string>(
                $"id => {Trace}.filter(e => e.type === 'message').map(e => String(e.text)).join('\\n')", pipelineId);
            throw new TimeoutException($"The run did not end as '{outcome}'. Messages: {messages}", ex);
        }
        var runId = long.Parse(await page.EvaluateAsync<string>(
            "id => String(window.__STUDIO__.state.documents.find(d => d.reportId === id).studioContext.currentRunId)", pipelineId));
        var run = Assert.Single(await HistoryAsync(job), entry => before.All(old => old.Id != entry.Id));
        Assert.Equal(run.Id, runId);
        return run;
    }

    /// <summary>The hash of the script text the Portal holds for the document, as the learner reads it back.</summary>
    private static async Task<string> SavedHashAsync(IPage page, int pipelineId) =>
        Sha(await page.EvaluateAsync<string>(
            """
            async id => {
                const { auth } = await import('/js/api.js');
                const response = await fetch(`/api/reports/${id}/script-content`, {
                    headers: { Authorization: `Bearer ${auth.getToken()}` }
                });
                if (!response.ok) throw new Error(`script-content returned ${response.status}`);
                const payload = await response.json();
                return payload.scriptText ?? payload.script ?? payload.content ?? '';
            }
            """, pipelineId));

    private static string Sha(string text) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static async Task SignInAsync(IPage page, string userName)
    {
        await page.GotoAsync("/login.html");
        await page.FillAsync("#username", userName);
        await page.FillAsync("#password", LearnerPassword);
        await page.ClickAsync("#loginBtn");
        await page.WaitForURLAsync("**/index.html", new PageWaitForURLOptions { Timeout = 20_000 });
    }

    // ── The people involved ──────────────────────────────────────────────────

    private sealed record Learner(string UserName, string PrincipalKey, int FolderId);

    /// <summary>A Publisher who owns one folder. No administrative or Orchestrator role.</summary>
    private async Task<Learner> CreateLearnerAsync()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new PortalUser
        {
            UserName = $"learner_{suffix}",
            Email = $"learner_{suffix}@test.local",
            IsActive = true,
            MustChangePassword = false,
            Provider = "Local"
        };
        var created = await users.CreateAsync(user, LearnerPassword);
        Assert.True(created.Succeeded, string.Join(", ", created.Errors.Select(error => error.Description)));
        Assert.True((await users.AddToRoleAsync(user, "Publisher")).Succeeded);
        Assert.Equal("Publisher", Assert.Single(await users.GetRolesAsync(user)));

        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var folder = new Folder { Name = $"Practice {suffix}", Path = $"/Practice-{suffix}", OwnerId = user.Id };
        db.Folders.Add(folder);
        await db.SaveChangesAsync();
        var key = await db.Users.AsNoTracking().Where(item => item.Id == user.Id).Select(item => item.PrincipalKey).SingleAsync();
        Assert.False(string.IsNullOrWhiteSpace(key));
        return new Learner(user.UserName, key!, folder.Id);
    }

    private async Task OperatorProvisionsAsync(string job)
    {
        using var client = fixture.Orchestrator.CreateClient();
        using var request = OperatorRequest(HttpMethod.Post, $"/api/scheduled-jobs/{Uri.EscapeDataString(job)}/runs",
            new { scriptText = "SELECT 1 AS Provisioned;", provisionIfMissing = true });
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var accepted = await response.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>();
        var runId = accepted!["runId"]!.GetValue<long>();
        await LoadAwareWait.UntilAsync("provisioning run releases the run-slot guard", async ct =>
        {
            using var read = OperatorRequest(HttpMethod.Get, $"/api/job-runs/{runId}", new { });
            var state = await (await client.SendAsync(read, ct)).Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>(ct);
            return state!["isTerminal"]!.GetValue<bool>();
        }, ready => ready, TimeSpan.FromSeconds(30));
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

    private async Task<List<JobHistoryEntry>> HistoryAsync(string job)
    {
        using var scope = fixture.Orchestrator.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IJobHistoryStore>();
        var stored = await store.GetJobAsync("portal-host", job);
        return stored is null ? [] : [.. await store.GetHistoryAsync(stored.Id, 50)];
    }
}
