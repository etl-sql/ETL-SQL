using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// The report viewer on the production catalog page, against the sandbox service fixture: opening
/// a report, running it and waiting for the result, and the structure, export, dependency, and
/// history dialogs.
/// </summary>
[Trait("Category", "Browser")]
[Collection(SandboxStoryCollection.Name)]
public sealed class ReportViewerSandboxTests(SandboxStoryFixture fixture) : IAsyncLifetime
{
    private WebApplication? host;
    private string baseUrl = "";

    public async Task InitializeAsync()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ETL-SQL.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        host = builder.Build();
        host.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(directory.FullName),
            ServeUnknownFileTypes = true
        });
        await host.StartAsync();
        baseUrl = host.Urls.First().TrimEnd('/');
    }

    public async Task DisposeAsync()
    {
        if (host is null) return;
        await host.StopAsync(TimeSpan.FromSeconds(10));
        await host.DisposeAsync();
    }

    [Fact]
    public async Task AReportWithASnapshotOpensItsViewerAndFrame()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenAsync(page, "healthy", "#report-7");
        await Expect(page.Locator(".viewer-header-info h2")).ToHaveTextAsync("Regional sales");
        await Expect(page.Locator(".viewer-meta-ready")).ToHaveTextAsync("Ready");
        await Expect(page.Locator("#reportFrame iframe")).ToHaveAttributeAsync("title", "Report viewer");
        foreach (var id in new[] { "refreshBtn", "structureBtn", "dependenciesBtn", "historyBtn", "exportBtn", "subBtn", "backBtn" })
            await Expect(page.Locator($"#{id}")).ToBeEnabledAsync();
        // The frame's document addresses this report and does not start in preview mode.
        var document = await page.Locator("#reportFrame iframe").GetAttributeAsync("srcdoc");
        Assert.Contains("window.__API_BASE__ = '/api/reports/7'", document);
        Assert.Contains("window.__IS_PREVIEW__ = false", document);
    }

    [Fact]
    public async Task AReportThatCannotBeLoadedSaysWhy()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenAsync(page, "report-unavailable", "#report-7");
        await Expect(page.Locator("#mainContent [role='alert']")).ToHaveTextAsync("Failed to load report: The catalog is offline.");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AFirstRunNeedsItsParametersThenShowsTheNewSnapshot()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenAsync(page, "healthy", "#report-8");
        await Expect(page.Locator(".viewer-body h2")).ToHaveTextAsync("Set parameters, then run");
        await page.Locator("#execBtn").ClickAsync();
        await Expect(page.Locator("#runParameterError")).ToHaveTextAsync("Product requires a value.");
        Assert.Equal(0, await ExecuteCallsAsync(page));

        await page.Locator("#runParameterFields .param-input").FillAsync("Widgets");
        await page.Locator("#execBtn").ClickAsync();
        await Expect(page.Locator("#pollBanner")).ToBeVisibleAsync();
        await Expect(page.Locator("#reportFrame iframe")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        Assert.Equal("Widgets", await page.EvaluateAsync<string>(
            "() => window.__readerMock.calls.find(call => call.path === '/api/reports/8/execute').body.parameters.Product"));
        Assert.Equal(1, await ExecuteCallsAsync(page));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AFirstRunWhoseParametersCannotBeReadCannotStart()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenAsync(page, "parameters-unavailable", "#report-8");
        await Expect(page.Locator("#mainContent [role='alert']").First).ToContainTextAsync("Parameter preflight failed: The report script could not be inspected.");
        await Expect(page.Locator("#execBtn")).ToBeDisabledAsync();
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData("Failed", "Report execution failed: Division by zero in visual Sales.")]
    [InlineData("Cancelled", "Report execution was cancelled.")]
    public async Task ARunThatDoesNotCompleteShowsItsReasonAndCanBeRetried(string state, string reason)
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenAsync(page, "healthy", "#report-7");
        await page.EvaluateAsync("state => window.__readerMock.setRun(state)", state);
        await page.Locator("#refreshBtn").ClickAsync();
        await Expect(page.Locator(".viewer-body [role='alert'] p")).ToHaveTextAsync(reason, new() { Timeout = 15_000 });
        await Expect(page.Locator("#subBtn")).ToBeEnabledAsync();
        await page.EvaluateAsync("() => window.__readerMock.setRun('Completed')");
        await page.Locator("#retryRunBtn").ClickAsync();
        await Expect(page.Locator("#reportFrame iframe")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        Assert.Equal(2, await ExecuteCallsAsync(page));
    }

    [Fact]
    public async Task ARunThatIsRefusedIsReportedWithoutWaiting()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenAsync(page, "healthy", "#report-7");
        await page.EvaluateAsync("() => window.__readerMock.setFailure('run-rejected')");
        await page.Locator("#refreshBtn").ClickAsync();
        await Expect(page.Locator(".viewer-body [role='alert'] p")).ToHaveTextAsync("Failed to start job: Report execution capacity is exhausted.");
        Assert.Equal(0, await page.EvaluateAsync<int>("() => window.__readerMock.polls"));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task LosingContactWithARunIsReportedInsteadOfWaitingOutTheTimeout()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenAsync(page, "healthy", "#report-7");
        await page.EvaluateAsync("() => window.__readerMock.setRun('unreachable')");
        await page.Locator("#refreshBtn").ClickAsync();
        await Expect(page.Locator(".viewer-body [role='alert'] p")).ToContainTextAsync(
            "Lost contact with the run: The job service is restarting.", new() { Timeout = 20_000 });
        Assert.Equal(5, await page.EvaluateAsync<int>("() => window.__readerMock.polls"));
    }

    [Fact]
    public async Task LeavingTheViewerEndsTheWaitSoALaterResultCannotPullTheReaderBack()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenAsync(page, "healthy", "#report-7");
        await page.EvaluateAsync("() => window.__readerMock.setRun('Running')");
        await page.Locator("#refreshBtn").ClickAsync();
        await page.WaitForFunctionAsync("() => window.__readerMock.polls >= 1", null, new() { Timeout = 15_000 });
        await page.Locator("#navSubscriptions").ClickAsync();
        await Expect(page.Locator("#mainContent .subs-table-wrap")).ToHaveAttributeAsync("data-subscriptions-state", "ready");
        var pollsAtDeparture = await page.EvaluateAsync<int>("() => window.__readerMock.polls");
        await page.EvaluateAsync("() => window.__readerMock.setRun('Completed')");
        // Two poll intervals: long enough for a wait that was still running to notice the completion.
        await page.EvaluateAsync("async () => { await new Promise(resolve => setTimeout(resolve, 3200)); }");
        await Expect(page.Locator("#mainContent h2")).ToHaveTextAsync("My Subscriptions");
        await Expect(page.Locator("#reportFrame")).ToHaveCountAsync(0);
        Assert.InRange(await page.EvaluateAsync<int>("() => window.__readerMock.polls"), pollsAtDeparture, pollsAtDeparture + 1);
    }

    [Fact]
    public async Task HistoryAndStructureDialogsLoadForTheOpenReport()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenAsync(page, "healthy", "#report-7");
        await page.Locator("#historyBtn").ClickAsync();
        await Expect(page.Locator("#historyBody .viewer-meta-warning")).ToHaveTextAsync("Script changed");
        await Expect(page.Locator("#historyBody .change-history-table tbody tr")).ToHaveCountAsync(1);
        // A change's detail is text, whatever it contains.
        await Expect(page.Locator("#historyBody .history-detail-cell")).ToHaveTextAsync("<script>document.title = 1</script>");
        await page.Locator("#historyCloseBtn").ClickAsync();
        await Expect(page.Locator("#historyModal")).ToBeHiddenAsync();

        await page.Locator("#structureBtn").ClickAsync();
        await Expect(page.Locator("#structureModal")).ToBeVisibleAsync();
        await Expect(page.Locator("#structureDag .loading-state")).ToHaveCountAsync(0);
        await page.Locator("#structureCloseBtn").ClickAsync();
        await Expect(page.Locator("#structureModal")).ToBeHiddenAsync();
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task ADialogThatCannotBeReadSaysWhy()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenAsync(page, "healthy", "#report-7");
        await page.EvaluateAsync("() => window.__readerMock.setFailure('dialogs-unavailable')");
        await page.Locator("#historyBtn").ClickAsync();
        await Expect(page.Locator("#historyBody [role='alert']")).ToHaveTextAsync("Failed to load history: History store is offline.");
        await page.Locator("#historyCloseBtn").ClickAsync();
        await page.Locator("#structureBtn").ClickAsync();
        await Expect(page.Locator("#structureDag [role='alert']")).ToHaveTextAsync("Failed to load structure: The script could not be parsed.");
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData("healthy", false)]
    [InlineData("downstream-partial", true)]
    public async Task DependenciesListDownstreamReportsAndSayWhenALookupFailed(string scenario, bool incomplete)
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenAsync(page, "healthy", "#report-7");
        await page.EvaluateAsync("scenario => window.__readerMock.setFailure(scenario)", scenario);
        await page.Locator("#dependenciesBtn").ClickAsync();
        var body = page.Locator("#dependenciesBody");
        await Expect(body).ToContainTextAsync("orders");
        await Expect(body).ToContainTextAsync("customers");
        await Expect(body.Locator("[data-downstream-report-id='9']")).ToBeVisibleAsync();
        await Expect(body.Locator(".status-banner-warning")).ToHaveCountAsync(incomplete ? 1 : 0);
        if (incomplete)
            await Expect(body.Locator(".status-banner-warning")).ToContainTextAsync("could not be read for 1 of 2 sources");
        // Both sources were looked up, including the one named only by objectName.
        Assert.Equal(new[] { "customers", "orders" }, await page.EvaluateAsync<string[]>(
            "() => window.__readerMock.calls.filter(call => call.path === '/api/catalog/lineage/downstream').map(call => call.query.get('table')).sort()"));
        await body.Locator("[data-downstream-report-id='9']").ClickAsync();
        await Expect(page.Locator(".viewer-header-info h2")).ToHaveTextAsync("Inventory aging");
        await Expect(page.Locator("#dependenciesModal")).ToBeHiddenAsync();
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData("healthy", null)]
    [InlineData("export-failed", "The export renderer is unavailable.")]
    public async Task AnExportDownloadsTheChosenFormatOrReportsTheFailure(string scenario, string? failure)
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenAsync(page, "healthy", "#report-7");
        await page.EvaluateAsync("scenario => window.__readerMock.setFailure(scenario)", scenario);
        await page.Locator("#exportBtn").ClickAsync();
        await Expect(page.Locator("#exportModal")).ToBeVisibleAsync();
        if (failure is null)
        {
            var download = await page.RunAndWaitForDownloadAsync(() => page.Locator("#exportCsvBtn").ClickAsync());
            Assert.Equal("regional.csv", download.SuggestedFilename);
        }
        else
        {
            await page.Locator("#exportCsvBtn").ClickAsync();
            await page.WaitForFunctionAsync("text => window.__readerMessages.some(item => item.message === text && item.tone === 'error')", failure);
        }
        await Expect(page.Locator("#exportModal")).ToBeHiddenAsync();
        Assert.Equal(1, await page.EvaluateAsync<int>("() => window.__readerMock.calls.filter(call => call.path === '/api/reports/7/export/csv').length"));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task BackFromALinkedReportReturnsToTheLibrary()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenAsync(page, "healthy", "#report-7");
        await page.Locator("#backBtn").ClickAsync();
        await Expect(page.Locator("#reportFrame")).ToHaveCountAsync(0);
        await Expect(page.Locator("#navReportHome")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("\\bactive\\b"));
    }

    private async Task OpenAsync(IPage page, string scenario, string hash)
    {
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/reader-page-frame.html?fixture={scenario}{hash}");
        await Expect(page.Locator("body")).ToHaveAttributeAsync("data-reader-sandbox-ready", "true");
        await Expect(page.Locator("#mainContent .loading-state")).ToHaveCountAsync(0);
    }

    private static Task<int> ExecuteCallsAsync(IPage page) => page.EvaluateAsync<int>(
        "() => window.__readerMock.calls.filter(call => call.path.endsWith('/execute')).length");
}
