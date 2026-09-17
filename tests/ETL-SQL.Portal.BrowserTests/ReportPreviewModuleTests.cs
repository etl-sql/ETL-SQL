using Microsoft.Playwright;

namespace ETL_SQL.Portal.BrowserTests;

[Trait("Category", "Browser")]
[Collection(StudioSurfaceCollection.Name)]
public sealed class ReportPreviewModuleTests(StudioSurfaceFixture fixture)
{
    [Fact]
    public async Task DesignerPreview_DynamicallyLoadsRuntimeModules()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync("/designer-preview.html");

        // Use the same message protocol as the designer, through the real Portal static host.
        const string sendManifest = """
            value => window.postMessage({ type: 'reportManifest', manifest: {
                title: 'Module preview', parameters: {}, pages: [],
                visuals: [{ name: 'Revenue', visualType: 'CARD', columns: ['Value'], rows: [[value]] }]
            } }, '*')
            """;
        await page.EvaluateAsync(sendManifest, "120");
        await Assertions.Expect(page.Locator("#root")).ToContainTextAsync("120");

        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
        Assert.Empty(session.FailedRequests);
    }
}
