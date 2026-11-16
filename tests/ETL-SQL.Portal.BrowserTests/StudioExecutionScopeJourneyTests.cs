using System.Text.Json;
using ETL_SQL.Portal.Data;
using ETL_SQL.WorkstationEditor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

[Trait("Category", "Browser")]
[Collection(StudioAuthoringCollection.Name)]
public sealed class StudioExecutionScopeJourneyTests(StudioAuthoringFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScopeSelectionAndMutatingPrefixConsentAgreeWithTheHost(bool desktop)
    {
        using var workspace = new StudioTempWorkspace();
        await using var session = await fixture.NewSessionAsync();
        await using var host = await OpenAsync(session.Page, workspace, desktop);
        var page = session.Page;
        var requests = new List<JsonElement>();
        page.Request += (_, request) =>
        {
            if (request.Url.EndsWith("/api/designer/run", StringComparison.Ordinal) && request.PostData is { } text)
                requests.Add(JsonDocument.Parse(text).RootElement.Clone());
        };

        await ReplaceAsync(page, "");
        await page.Locator("[data-action='run-selected']").ClickAsync();
        await page.Locator(".etlsql-script-results [data-tab='messages']").ClickAsync();
        await Expect(page.Locator(".etlsql-script-results")).ToContainTextAsync("No query or statement selected");
        Assert.Empty(requests);

        await ReplaceAsync(page, "SELECT 13 AS Chosen;\nSELECT 99 AS NotRun;");
        await page.EvaluateAsync("() => window.__STUDIO__.state.editorInstance.revealLines(1, 1)");
        var selectedResponse = page.WaitForResponseAsync(r => r.Url.EndsWith("/api/designer/run", StringComparison.Ordinal));
        await page.Locator("[data-action='run-selected']").ClickAsync();
        var selected = await (await selectedResponse).JsonAsync();
        Assert.Equal(13m, selected!.Value.GetProperty("rows")[0].GetProperty("Chosen").GetDecimal());
        Assert.DoesNotContain("99", requests[^1].GetProperty("selection").GetString());
        await DoneAsync(page);

        await ReplaceAsync(page, "SELECT FROM WHERE (");
        await page.Locator("[data-activity='engine']").ClickAsync();
        await Expect(page.Locator("[data-sidebar-content]")).ToContainTextAsync("Could not read");
        Assert.Single(requests);

        const string script = "CREATE CONNECTION m AS MOCKDB();\nEXECUTE m BEGIN CREATE TABLE scratch (Id INT); END;\nSELECT 7 AS Scoped INTO #stage;\nSELECT * FROM #stage;";
        await ReplaceAsync(page, script);
        await page.EvaluateAsync("() => window.__STUDIO__.state.editorInstance.revealLines(4, 4)");
        await page.Locator("[data-engine-refresh]").ClickAsync();
        await Expect(page.Locator("[data-allow-mutating-prefix]")).ToBeVisibleAsync();
        await Expect(page.Locator("[data-sidebar-content]")).ToContainTextAsync("#stage");
        await page.Locator("[data-explain-statement]").ClickAsync();
        await Expect(page.Locator("[data-plan-host]")).ToContainTextAsync("Refused: Mutating prefix");
        Assert.Single(requests);

        await page.Locator("[data-allow-mutating-prefix]").CheckAsync();
        var plannedResponse = page.WaitForResponseAsync(r => r.Url.EndsWith("/api/designer/run", StringComparison.Ordinal));
        await page.Locator("[data-explain-statement]").ClickAsync();
        var response = await plannedResponse;
        Assert.True(requests[^1].GetProperty("confirmDestructive").GetBoolean());
        Assert.Contains("CREATE TABLE scratch", requests[^1].GetProperty("selection").GetString());
        Assert.Contains("EXPLAIN SELECT", requests[^1].GetProperty("selection").GetString());
        if (desktop)
        {
            Assert.True(response.Ok);
            var payload = await response.JsonAsync();
            Assert.True(payload!.Value.GetProperty("success").GetBoolean(), payload.ToString());
            await Expect(page.Locator("[data-plan-host]")).ToContainTextAsync("operator");
        }
        else
        {
            Assert.Equal(400, response.Status);
            await Expect(page.Locator("[data-plan-host]")).ToContainTextAsync("not allowed in an interactive run");
        }
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AResponseLostAfterRealExecutionKeepsUnknownOutcomeAndDocumentOwnership(bool desktop)
    {
        using var workspace = new StudioTempWorkspace();
        await using var session = await fixture.NewSessionAsync();
        await using var host = await OpenAsync(session.Page, workspace, desktop);
        var page = session.Page;
        var serverFinished = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var loseResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await page.RouteAsync("**/api/designer/run", async route =>
        {
            var response = await route.FetchAsync();
            var result = await response.JsonAsync();
            if (!response.Ok || (result!.Value.TryGetProperty("success", out var success) && !success.GetBoolean()))
            {
                serverFinished.TrySetException(new InvalidOperationException(result.ToString()));
                await route.AbortAsync();
                return;
            }
            serverFinished.TrySetResult(result.Value.GetProperty("runId").GetString()!);
            await loseResponse.Task;
            await route.AbortAsync();
        });
        await ReplaceAsync(page, "SELECT 17 AS ServerCompleted;");
        await page.Locator("[data-action='run']").ClickAsync();
        var runId = await serverFinished.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await page.Locator("[data-studio-new-tab]").ClickAsync();
        await page.Locator("[data-new-type='dashboard']").ClickAsync();
        if (!desktop)
        {
            await page.Locator("[data-catalog-report-name]").FillAsync("Other document");
            await page.Locator("[data-catalog-create-confirm]").ClickAsync();
        }
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.documents.length === 2 && window.__STUDIO__.state.activeDocId === window.__STUDIO__.state.documents[1].id");
        loseResponse.TrySetResult();
        await DoneAsync(page);
        var trace = await page.EvaluateAsync<string>("() => JSON.stringify(window.__STUDIO__.state.documents[0].studioContext.resultsTrace)");
        Assert.Contains("outcome is unknown", trace);
        Assert.Contains(runId, trace);
        Assert.Contains("Unknown", trace);
        Assert.DoesNotContain("Run completed", trace);
        var secondTrace = await page.EvaluateAsync<string>("() => JSON.stringify(window.__STUDIO__.state.documents[1].studioContext.resultsTrace || [])");
        Assert.DoesNotContain(runId, secondTrace);
        await Expect(page.Locator(".etlsql-script-results")).Not.ToContainTextAsync(runId);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task DesktopStopCancelsAnExecutingServerRunAndRetainsUnconfirmedClientOutcome()
    {
        using var workspace = new StudioTempWorkspace();
        await using var session = await fixture.NewSessionAsync();
        await using var host = await OpenAsync(session.Page, workspace, true);
        var page = session.Page;
        var lifecycle = host!.Services.GetRequiredService<StudioHostLifecycleService>();
        await ReplaceAsync(page, "WAITFOR DELAY '00:00:40';\nSELECT 99 AS MustNotFinish;");
        await page.Locator("[data-action='run']").ClickAsync();
        await ETL_SQL.TestSupport.LoadAwareWait.UntilAsync("desktop run started", _ => Task.FromResult(lifecycle.ActiveRuns), count => count == 1, TimeSpan.FromSeconds(10));
        var id = await page.EvaluateAsync<string>("() => window.__STUDIO__.state.documents[0].studioContext.currentRunId");
        await page.Locator("[data-action='stop']").ClickAsync();
        await DoneAsync(page);
        await ETL_SQL.TestSupport.LoadAwareWait.UntilAsync("desktop run cancelled", _ => Task.FromResult(lifecycle.ActiveRuns), count => count == 0, TimeSpan.FromSeconds(10));
        var trace = await page.EvaluateAsync<string>("() => JSON.stringify(window.__STUDIO__.state.documents[0].studioContext.resultsTrace)");
        Assert.Contains("Cancellation is unconfirmed", trace);
        Assert.Contains(id, trace);
        Assert.DoesNotContain("server-confirmed", trace);
        var historyPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ETL-SQL", "workstation-editor", "run-history.jsonl");
        var history = await File.ReadAllTextAsync(historyPath, CancellationToken.None);
        Assert.Contains($"\"runId\":\"{id}\"", history);
        Assert.Empty(session.PageErrors);
    }

    internal async Task<Microsoft.AspNetCore.Builder.WebApplication?> OpenAsync(IPage page, StudioTempWorkspace workspace, bool desktop)
    {
        if (desktop)
        {
            var file = Path.Combine(workspace.Root, "scope.etlsql");
            await File.WriteAllTextAsync(file, "SELECT 1;");
            var host = WorkstationEditorApp.Create([], new WorkstationEditorOptions(workspace.Root, file, 0, false, "scope-token", StudioMode: true));
            await host.StartAsync();
            await page.GotoWithDiagnosticsAsync($"{WorkstationEditorApp.GetListeningUrl(host)}/studio?token=scope-token");
            await page.WaitForFunctionAsync("() => Boolean(window.__STUDIO__?.state.editorInstance)");
            await page.Locator("[data-projection='split']").ClickAsync();
            return host;
        }
        await fixture.SignInAsync(page);
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var admin = await db.Users.SingleAsync(u => u.UserName == PortalBrowserFixture.AdminUsername);
        var folder = new Folder { Name = "Scope " + Guid.NewGuid().ToString("N"), Path = "/Scope-" + Guid.NewGuid().ToString("N"), OwnerId = admin.Id };
        db.Folders.Add(folder);
        await db.SaveChangesAsync();
        var report = await page.EvaluateAsync<JsonElement>("async data => { const {studioApi} = await import('/js/api.js'); return studioApi.createReport(data); }",
            new { folderId = folder.Id, name = "Scope", scriptText = "SELECT 1 AS A INTO #t; CREATE VISUAL T AS TABLE (SOURCE = #t); CREATE PAGE Main AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = T));" });
        var reportId = report.GetProperty("id").GetInt32();
        await page.GotoWithDiagnosticsAsync($"/studio.html?reportId={reportId}");
        await page.WaitForFunctionAsync("id => Boolean(window.__STUDIO__?.state.editorInstance) && window.__STUDIO__.state.documents.some(d => Number(d.reportId) === id)", reportId);
        await page.Locator("[data-projection='split']").ClickAsync();
        return null;
    }

    internal static Task ReplaceAsync(IPage page, string text) => page.EvaluateAsync("text => window.__STUDIO__.state.editorInstance.replaceAll(text)", text);
    internal static Task DoneAsync(IPage page) => page.WaitForFunctionAsync("() => !window.__STUDIO__.state.documents[0].studioContext.runActive");
}
