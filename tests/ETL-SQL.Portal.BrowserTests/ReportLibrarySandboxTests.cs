using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// The report library and page routing on the production catalog page, against the sandbox service
/// fixture: the folder tree, folder lists, home, search, favorites, and which view owns the page.
/// </summary>
[Trait("Category", "Browser")]
[Collection(SandboxStoryCollection.Name)]
public sealed class ReportLibrarySandboxTests(SandboxStoryFixture fixture) : IAsyncLifetime
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
    public async Task TheFolderTreeShowsSubfoldersAndOpensThemByMouseAndKeyboard()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenAsync(page);
        await Expect(page.Locator("#folderTree .folder-item")).ToHaveCountAsync(3);
        // A subfolder is nested inside its parent's group; it used to be left out of the tree entirely.
        var quarterly = page.Locator("#folderTree .folder-children .folder-item[data-id='2']");
        await Expect(quarterly).ToHaveTextAsync("Quarterly");
        await Expect(page.Locator("#folderTree .folder-item[data-id='1']")).ToHaveAttributeAsync("aria-expanded", "true");

        await quarterly.ClickAsync();
        await Expect(page.Locator(".library-title h2")).ToHaveTextAsync("Quarterly");
        await Expect(page.Locator(".report-card")).ToHaveCountAsync(1);
        await Expect(quarterly).ToHaveAttributeAsync("aria-selected", "true");

        await page.Locator("#folderTree .folder-item[data-id='1']").FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Expect(page.Locator(".library-title h2")).ToHaveTextAsync("Finance");
        await Expect(page.Locator(".report-card")).ToHaveCountAsync(3);
        await Expect(quarterly).ToHaveAttributeAsync("aria-selected", "false");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AFolderListFiltersAndSortsWithoutLosingTheSearchBox()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenFolderAsync(page);
        await Expect(page.Locator("#reportCount")).ToHaveTextAsync("3 of 3 reports");
        // A report name is text, whatever it contains.
        await Expect(page.Locator(".report-card[data-report-id='10'] h3")).ToHaveTextAsync("Accounts <i>aging</i>");
        await Expect(page.Locator(".report-card[data-report-id='8'] .badge-error")).ToHaveTextAsync("Failed");
        await Expect(page.Locator(".report-card[data-report-id='7'] .badge-stale")).ToHaveTextAsync("Stale");

        var search = page.Locator("#reportSearchInput");
        await search.FocusAsync();
        await page.Keyboard.TypeAsync("receivables");
        // Typing a whole word proves the box kept focus between keystrokes.
        await Expect(search).ToHaveValueAsync("receivables");
        await Expect(search).ToBeFocusedAsync();
        await Expect(page.Locator(".report-card")).ToHaveCountAsync(1);
        await Expect(page.Locator("#reportCount")).ToHaveTextAsync("1 of 3 reports");
        await search.FillAsync("nothing matches this");
        await Expect(page.Locator("#reportGridHost")).ToContainTextAsync("No matching reports");
        await search.FillAsync("");

        await page.Locator("#reportSortSelect").SelectOptionAsync("stale");
        await Expect(page.Locator(".report-card").First).ToHaveAttributeAsync("data-report-id", "7");
        await page.Locator("#reportSortSelect").SelectOptionAsync("name");
        await Expect(page.Locator(".report-card").First).ToHaveAttributeAsync("data-report-id", "10");
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData("folder-unavailable", "1", "Reports did not load", "Folder permissions are being rebuilt.")]
    [InlineData("home-unavailable", null, "Report home did not load", "The catalog index is offline.")]
    public async Task AViewThatCannotBeReadShowsTheReasonAndRetries(string scenario, string? folder, string title, string reason)
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenAsync(page, scenario);
        if (folder is not null) await page.Locator($"#folderTree .folder-item[data-id='{folder}']").ClickAsync();
        var alert = page.Locator("#mainContent [role='alert']");
        await Expect(alert.Locator("h2")).ToHaveTextAsync(title);
        await Expect(alert.Locator("p")).ToHaveTextAsync(reason);
        await page.EvaluateAsync("() => window.__readerMock.setFailure('healthy')");
        await alert.GetByRole(AriaRole.Button, new() { Name = "Retry" }).ClickAsync();
        await Expect(alert).ToHaveCountAsync(0);
        await Expect(page.Locator(folder is null ? "#consumerHomeTitle" : ".report-card").First).ToBeVisibleAsync();
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AFolderTreeThatCannotBeReadSaysSoAndReloads()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenAsync(page, "folders-unavailable");
        await Expect(page.Locator("#folderTree [role='alert']")).ToContainTextAsync("Folders did not load: The folder index is rebuilding.");
        await page.EvaluateAsync("() => window.__readerMock.setFailure('healthy')");
        await page.Locator("#folderTree").GetByRole(AriaRole.Button, new() { Name = "Retry" }).ClickAsync();
        await Expect(page.Locator("#folderTree .folder-item")).ToHaveCountAsync(3);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task HomeSearchFindsFoldersAndReportsAndClears()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenAsync(page);
        await Expect(page.Locator("#consumer-favorites")).ToBeVisibleAsync();
        await Expect(page.Locator("#consumerHomeSections .consumer-report-card")).ToHaveCountAsync(2);
        await page.Locator("#globalReportSearchInput").FillAsync("aging");
        await page.Locator("#globalReportSearchInput").PressAsync("Enter");
        var results = page.Locator("#globalSearchResults");
        await Expect(results.Locator("h3").First).ToHaveTextAsync("2 matches for “aging”");
        await Expect(results.Locator(".consumer-report-card")).ToHaveCountAsync(2);
        await Expect(page.Locator("#consumerHomeSections")).ToBeHiddenAsync();

        await page.Locator("#globalReportSearchInput").FillAsync("quarter");
        await page.Locator("#globalReportSearchInput").PressAsync("Enter");
        await Expect(results.Locator(".consumer-folder-result")).ToHaveCountAsync(1);
        await results.Locator(".consumer-folder-result").ClickAsync();
        await Expect(page.Locator(".library-title h2")).ToHaveTextAsync("Quarterly");

        await page.Locator("#navReportHome").ClickAsync();
        await page.Locator("#globalReportSearchInput").FillAsync("sales");
        await page.Locator("#globalReportSearchInput").PressAsync("Enter");
        await page.Locator("#clearGlobalSearchBtn").ClickAsync();
        await Expect(results.Locator("*")).ToHaveCountAsync(0);
        await Expect(page.Locator("#consumerHomeSections")).ToBeVisibleAsync();
        await Expect(page.Locator("#globalReportSearchInput")).ToBeFocusedAsync();
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task ASearchThatCannotCompleteShowsTheReason()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenAsync(page, "search-unavailable");
        await page.Locator("#globalReportSearchInput").FillAsync("sales");
        await page.Locator("#globalReportSearchInput").PressAsync("Enter");
        await Expect(page.Locator("#globalSearchResults [role='alert'] p")).ToHaveTextAsync("The search index is offline.");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AFavoriteIsSetOnceAndShownAfterTheServiceAccepts()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenFolderAsync(page);
        var star = page.Locator(".report-card[data-report-id='7'] .favorite-btn");
        await Expect(star).ToHaveAttributeAsync("aria-pressed", "false");
        await page.EvaluateAsync("() => window.__readerMock.pauseWrites()");
        await star.ClickAsync();
        await page.WaitForFunctionAsync("() => window.__readerMock.pendingWrites === 1");
        await Expect(star).ToBeDisabledAsync();
        await Expect(star).ToHaveAttributeAsync("aria-pressed", "false");
        await star.DispatchEventAsync("click");
        await page.EvaluateAsync("() => window.__readerMock.releaseWrites()");
        await Expect(star).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(star).ToBeEnabledAsync();
        Assert.Equal(1, await page.EvaluateAsync<int>("() => window.__readerMock.calls.filter(call => call.path === '/api/reports/7/favorite').length"));
        // Starring a report does not open it.
        await Expect(page.Locator(".report-card")).ToHaveCountAsync(3);

        await page.Locator("#navFavorites").ClickAsync();
        await Expect(page.Locator(".catalog-report-row")).ToHaveCountAsync(2);
        await page.Locator(".catalog-report-row[data-report-id='7'] .favorite-btn").ClickAsync();
        await Expect(page.Locator(".catalog-report-row")).ToHaveCountAsync(1);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task ARefusedFavoriteIsReportedAndLeftAsItWas()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenFolderAsync(page);
        await page.EvaluateAsync("() => window.__readerMock.setFailure('favorite-failed')");
        var star = page.Locator(".report-card[data-report-id='7'] .favorite-btn");
        await star.ClickAsync();
        await page.WaitForFunctionAsync("() => window.__readerMessages.some(item => item.message === 'Favorites are read-only during maintenance.' && item.tone === 'error')");
        await Expect(star).ToHaveAttributeAsync("aria-pressed", "false");
        await Expect(star).ToBeEnabledAsync();
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task BackFromAReportReturnsToTheFolderItWasOpenedFromAndBrowserBackRestoresIt()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenFolderAsync(page);
        await page.Locator(".report-card[data-report-id='7'] .report-card-link").ClickAsync();
        await Expect(page.Locator(".viewer-header-info h2")).ToHaveTextAsync("Regional sales");
        await page.Locator("#backBtn").ClickAsync();
        await Expect(page.Locator(".library-title h2")).ToHaveTextAsync("Finance");
        await Expect(page.Locator("#folderTree .folder-item[data-id='1']")).ToHaveAttributeAsync("aria-selected", "true");

        await page.Locator("#navRecent").ClickAsync();
        await page.Locator(".catalog-report-row[data-report-id='9'] .report-card-link").ClickAsync();
        await Expect(page.Locator(".viewer-header-info h2")).ToHaveTextAsync("Inventory aging");
        // A report opened from a catalog list returns to the folder it lives in.
        await page.Locator("#backBtn").ClickAsync();
        await Expect(page.Locator(".library-title h2")).ToHaveTextAsync("/Finance");
        await Expect(page.Locator(".report-card")).ToHaveCountAsync(1);

        await page.GoBackAsync();
        await Expect(page.Locator(".viewer-header-info h2")).ToHaveTextAsync("Inventory aging");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task ALateFolderResponseDoesNotRepaintTheViewThatReplacedIt()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenAsync(page);
        await page.EvaluateAsync("() => window.__readerMock.pauseReads('/api/folders/1/reports')");
        await page.Locator("#folderTree .folder-item[data-id='1']").ClickAsync();
        await page.WaitForFunctionAsync("() => window.__readerMock.pendingReads === 1");
        await page.Locator("#navSubscriptions").ClickAsync();
        await Expect(page.Locator("#mainContent .subs-table-wrap")).ToHaveAttributeAsync("data-subscriptions-state", "ready");
        await page.EvaluateAsync("() => window.__readerMock.releaseReads()");
        await page.EvaluateAsync("async () => { await new Promise(resolve => setTimeout(resolve, 80)); }");
        await Expect(page.Locator("#mainContent h2")).ToHaveTextAsync("My Subscriptions");
        await Expect(page.Locator(".report-card")).ToHaveCountAsync(0);
        await Expect(page.Locator("#folderTree .folder-item.active")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task AReaderIsOfferedLineageOnlyAndARefusedGovernanceRouteFallsBackToIt()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenAsync(page, "healthy", "#governance/quarantine");
        await page.WaitForFunctionAsync("() => location.hash === '#governance/lineage'");
        await Expect(page.Locator("#governanceSidebarSection")).ToBeVisibleAsync();
        await Expect(page.Locator("#reportsSidebarSection")).ToBeHiddenAsync();
        await Expect(page.Locator("#govNavLineage")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("\\bactive\\b"));
        foreach (var hidden in new[] { "govNavOverview", "govNavWorkqueue", "govNavExceptions", "govNavGlossary", "govNavQuality", "govNavSettings", "govNavQuarantine" })
            await Expect(page.Locator($"#{hidden}")).ToBeHiddenAsync();
    }

    private async Task OpenAsync(IPage page, string scenario = "healthy", string hash = "")
    {
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/reader-page-frame.html?fixture={scenario}{hash}");
        await Expect(page.Locator("body")).ToHaveAttributeAsync("data-reader-sandbox-ready", "true");
        await Expect(page.Locator("#mainContent .loading-state")).ToHaveCountAsync(0);
    }

    private async Task OpenFolderAsync(IPage page)
    {
        await OpenAsync(page);
        await page.Locator("#folderTree .folder-item[data-id='1']").ClickAsync();
        await Expect(page.Locator(".report-card")).ToHaveCountAsync(3);
    }
}
