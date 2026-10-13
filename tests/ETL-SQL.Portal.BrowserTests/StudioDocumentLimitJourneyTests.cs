using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

[Trait("Category", "Browser")]
[Trait("CompatBreak", "0.21.0")]
[Collection(StudioAuthoringCollection.Name)]
public sealed class StudioDocumentLimitJourneyTests(StudioAuthoringFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OversizedTextIsKeptWhileEditorServicesPauseAndResume(bool desktop)
    {
        using var workspace = new StudioTempWorkspace();
        await using var session = await fixture.NewSessionAsync();
        await using var host = await new StudioExecutionScopeJourneyTests(fixture).OpenAsync(session.Page, workspace, desktop);
        var page = session.Page;
        var oversized = "--" + new string('é', 199_999);
        var oversizedAnalysisRequests = 0;
        page.Request += (_, request) =>
        {
            if (request.Url.Contains("/api/designer/analyze", StringComparison.Ordinal)
                && request.PostData?.Contains(oversized, StringComparison.Ordinal) == true) oversizedAnalysisRequests++;
        };
        await StudioExecutionScopeJourneyTests.ReplaceAsync(page, oversized);
        await page.EvaluateAsync("() => window.__STUDIO__.state.editorInstance.analyze()");
        await page.Locator(".etlsql-script-results [data-tab='messages']").ClickAsync();
        await Expect(page.Locator(".etlsql-script-results")).ToContainTextAsync("Editor services are paused above");
        Assert.Equal(oversized, await page.EvaluateAsync<string>("() => window.__STUDIO__.state.editorInstance.getValue()"));
        Assert.Equal(0, oversizedAnalysisRequests);
        var response = page.WaitForResponseAsync(response => response.Url.Contains("/api/designer/analyze", StringComparison.Ordinal) && response.Status == 200);
        await StudioExecutionScopeJourneyTests.ReplaceAsync(page, StudioDeliveryJourneyTests.LargeDocument);
        await page.EvaluateAsync("() => window.__STUDIO__.state.editorInstance.analyze()");
        await response;
        await Expect(page.Locator(".etlsql-script-results")).Not.ToContainTextAsync("Editor services are paused above");
        Assert.Equal(StudioDeliveryJourneyTests.LargeDocument, await page.EvaluateAsync<string>("() => window.__STUDIO__.state.editorInstance.getValue()"));

        if (!desktop)
        {
            var atLimit = "--" + new string('é', 199_998);
            var statuses = await page.EvaluateAsync<int[]>("""
                async scripts => {
                    const { apiFetch } = await import('/js/api.js');
                    const statuses = [];
                    for (const script of scripts) {
                        const response = await apiFetch('/api/designer/analyze', {
                            method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ script })
                        });
                        statuses.push(response.status);
                    }
                    return statuses;
                }
                """, new[] { atLimit, oversized });
            Assert.Equal(new[] { 200, 413 }, statuses);
            Assert.Equal(StudioDeliveryJourneyTests.LargeDocument, await page.EvaluateAsync<string>("() => window.__STUDIO__.state.editorInstance.getValue()"));
            await StudioExecutionScopeJourneyTests.ReplaceAsync(page, oversized);
            var refusedSave = page.WaitForResponseAsync(response => response.Url.Contains("/api/designer/save", StringComparison.Ordinal));
            await page.Locator("[data-action='save']").ClickAsync();
            Assert.Equal(413, (await refusedSave).Status);
            Assert.Equal(oversized, await page.EvaluateAsync<string>("() => window.__STUDIO__.state.editorInstance.getValue()"));
            Assert.True(await page.EvaluateAsync<bool>("() => window.__STUDIO__.state.documents[0].isDirty"));
        }
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task PortalRateLimitedAnalysisExplainsStaleDiagnosticsAndRetriesTheKeptBuffer()
    {
        using var workspace = new StudioTempWorkspace();
        await using var session = await fixture.NewSessionAsync();
        await using var host = await new StudioExecutionScopeJourneyTests(fixture).OpenAsync(session.Page, workspace, false);
        var page = session.Page;
        var calls = 0;
        string? retriedBody = null;
        await page.RouteAsync("**/api/designer/analyze", async route =>
        {
            if (Interlocked.Increment(ref calls) == 1)
                await route.FulfillAsync(new RouteFulfillOptions
                {
                    Status = 429,
                    ContentType = "application/json",
                    Headers = new Dictionary<string, string> { ["Retry-After"] = "2" },
                    Body = "{\"error\":\"controlled rate limit\"}"
                });
            else
            {
                retriedBody = route.Request.PostData;
                await route.ContinueAsync();
            }
        });
        const string script = "-- keep this buffer during rate limiting\nSELECT 73;";
        await StudioExecutionScopeJourneyTests.ReplaceAsync(page, script);
        await page.EvaluateAsync("() => window.__STUDIO__.state.editorInstance.analyze()");
        await page.Locator(".etlsql-script-results [data-tab='messages']").ClickAsync();
        await Expect(page.Locator(".etlsql-script-results")).ToContainTextAsync("Previous diagnostics are stale");
        await Expect(page.Locator(".cm-lintRange-error").First).ToBeVisibleAsync();
        var currentScript = script + "\n-- typed during permitted retry";
        await StudioExecutionScopeJourneyTests.ReplaceAsync(page, currentScript);
        await Expect(page.Locator(".etlsql-script-results")).Not.ToContainTextAsync("Previous diagnostics are stale",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        Assert.True(calls >= 2);
        await Expect(page.Locator(".cm-lintRange-error")).ToHaveCountAsync(0);
        Assert.Contains("typed during permitted retry", retriedBody);
        Assert.Equal(currentScript, await page.EvaluateAsync<string>("() => window.__STUDIO__.state.editorInstance.getValue()"));
        Assert.Empty(session.PageErrors);
    }
}
