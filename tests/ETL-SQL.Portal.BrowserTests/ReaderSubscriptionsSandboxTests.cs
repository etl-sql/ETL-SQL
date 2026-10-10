using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// A reader's subscriptions on the production catalog page, against the sandbox service fixture:
/// the list and its actions, the Subscribe dialog a report opens, and what each does when the
/// service does not answer.
/// </summary>
[Trait("Category", "Browser")]
[Collection(SandboxStoryCollection.Name)]
public sealed class ReaderSubscriptionsSandboxTests(SandboxStoryFixture fixture) : IAsyncLifetime
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
    public async Task TheListShowsEachSubscriptionAsText()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenListAsync(page);
        await Expect(page.Locator("#mainContent tbody tr")).ToHaveCountAsync(3);
        await Expect(page.Locator("#mainContent .badge-ok")).ToHaveTextAsync("2 active");
        await Expect(page.Locator("tr[data-subscription-id='11'] .sub-name")).ToHaveTextAsync("Morning sales");
        await Expect(page.Locator("tr[data-subscription-id='12'] .sub-warning")).ToHaveTextAsync("2 failed sends");
        // A report name is data: markup in it is shown, not interpreted.
        await Expect(page.Locator("tr[data-subscription-id='13'] .sub-name")).ToHaveTextAsync("Inventory <b>aging</b>");
        await Expect(page.Locator("tr[data-subscription-id='13'] b")).ToHaveCountAsync(0);
        await Expect(page.Locator("tr[data-subscription-id='13']")).ToContainTextAsync("Portal link");
        await Expect(page.Locator("#navSubscriptions")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("\\bactive\\b"));
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData("list-unavailable", "The subscription store is offline.")]
    [InlineData("list-malformed", "The subscription response is unavailable or incomplete.")]
    public async Task AnUnreadableListSaysWhyAndRecovers(string scenario, string reason)
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenAsync(page, scenario, "#subscriptions");
        var alert = page.Locator("#mainContent [role='alert']");
        await Expect(alert).ToContainTextAsync(reason);
        await Expect(page.Locator("#mainContent")).Not.ToContainTextAsync("No subscriptions yet");
        await page.EvaluateAsync("() => window.__readerMock.setFailure('healthy')");
        await alert.GetByRole(AriaRole.Button, new() { Name = "Try again" }).ClickAsync();
        await Expect(page.Locator("#mainContent tbody tr")).ToHaveCountAsync(3);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AFailedRefreshDropsRowsItCanNoLongerVouchFor()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenListAsync(page);
        await page.EvaluateAsync("() => window.__readerMock.setFailure('list-unavailable')");
        await page.Locator("#refreshSubscriptionsBtn").ClickAsync();
        await Expect(page.Locator("#mainContent [role='alert']")).ToContainTextAsync("The subscription store is offline.");
        await Expect(page.Locator("#mainContent tbody tr")).ToHaveCountAsync(0);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task PausingSendsTheLoadedVersionOnceAndIsAnnouncedAfterItsReceipt()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenListAsync(page);
        await page.EvaluateAsync("() => window.__readerMock.pauseWrites()");
        var toggle = page.Locator("tr[data-subscription-id='11'] [data-action='toggle']");
        await toggle.ClickAsync();
        await page.WaitForFunctionAsync("() => window.__readerMock.pendingWrites === 1");
        await Expect(toggle).ToBeDisabledAsync();
        await Expect(page.Locator("tr[data-subscription-id='11'] [data-action='delete']")).ToBeDisabledAsync();
        await Expect(page.Locator("tr[data-subscription-id='12'] [data-action='toggle']")).ToBeEnabledAsync();
        Assert.Empty(await MessagesAsync(page));
        await page.EvaluateAsync("() => window.__readerMock.releaseWrites()");
        await WaitForMessageAsync(page, "No deliveries will be sent while it is paused.");
        await Expect(toggle).ToHaveTextAsync("Resume");
        await Expect(page.Locator("tr[data-subscription-id='11'] .chip")).ToHaveTextAsync("Paused");
        Assert.Equal("\"2\"|False", await page.EvaluateAsync<string>(
            "() => { const calls = window.__readerMock.calls.filter(call => call.method === 'PUT'); return calls.length === 1 ? calls[0].ifMatch + '|' + (calls[0].body.isActive ? 'True' : 'False') : 'calls=' + calls.length; }"));
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData("failed", "Subscription change outcome is unconfirmed")]
    [InlineData("conflict", "The resource changed after it was read")]
    [InlineData("unconfirmed", "Subscription change outcome is unconfirmed")]
    public async Task AChangeThatWasNotConfirmedIsNotAnnouncedAsDone(string scenario, string reason)
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenListAsync(page);
        await page.EvaluateAsync("scenario => window.__readerMock.setFailure(scenario)", scenario);
        await page.Locator("tr[data-subscription-id='11'] [data-action='toggle']").ClickAsync();
        await WaitForMessageAsync(page, reason);
        Assert.All(await TonesAsync(page), tone => Assert.Equal("error", tone));
        await Expect(page.Locator("tr[data-subscription-id='11'] [data-action='toggle']")).ToBeEnabledAsync();
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task DeletingAsksFirstAndRemovesTheRowAfterTheServiceConfirms()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenListAsync(page);
        await page.Locator("tr[data-subscription-id='12'] [data-action='delete']").ClickAsync();
        Assert.Equal(0, await page.EvaluateAsync<int>("() => window.__readerMock.calls.filter(call => call.method === 'DELETE').length"));
        await page.Locator(".etlsql-feedback-btn-danger").ClickAsync();
        await WaitForMessageAsync(page, "Future deliveries are cancelled.");
        await Expect(page.Locator("#mainContent tbody tr")).ToHaveCountAsync(2);
        Assert.Equal("\"5\"", await page.EvaluateAsync<string>("() => window.__readerMock.calls.find(call => call.method === 'DELETE').ifMatch"));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task EditingParametersLoadsDefinitionsAndSavesTheDraftAgainstTheLoadedVersion()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenListAsync(page);
        await page.Locator("tr[data-subscription-id='11'] [data-action='edit-params']").ClickAsync();
        await Expect(page.Locator("#editParamsTitle")).ToHaveTextAsync("Edit Parameters — Morning sales");
        var inputs = page.Locator("#editParamsFields .param-input");
        await Expect(inputs).ToHaveCountAsync(2);
        await Expect(inputs.Nth(0)).ToHaveValueAsync("North");
        await Expect(inputs.Nth(1)).ToHaveValueAsync("D-1");
        await inputs.Nth(0).FillAsync("");
        await page.Locator("#editParamsSaveBtn").ClickAsync();
        await Expect(page.Locator("#editParamsError")).ToHaveTextAsync("Region requires a value.");
        Assert.Equal(0, await page.EvaluateAsync<int>("() => window.__readerMock.calls.filter(call => call.method === 'PUT').length"));

        await inputs.Nth(0).FillAsync("East");
        await page.Locator("#editParamsFields .reldate-quickpicks button", new() { HasText = "M-1" }).ClickAsync();
        await page.Locator("#editParamsSaveBtn").ClickAsync();
        await WaitForMessageAsync(page, "Future deliveries will use the new parameter values.");
        await Expect(page.Locator("#editParamsModal")).ToBeHiddenAsync();
        await Expect(page.Locator("tr[data-subscription-id='11']")).ToContainTextAsync("Region=East, AsOf=M-1");
        Assert.Equal("\"2\"|East|M-1", await page.EvaluateAsync<string>(
            "() => { const call = window.__readerMock.calls.find(item => item.method === 'PUT'); return [call.ifMatch, call.body.parameters.Region, call.body.parameters.AsOf].join('|'); }"));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task ParameterDefinitionsThatCannotBeReadAreNotReplacedByFreeTextFields()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenListAsync(page);
        await page.EvaluateAsync("() => window.__readerMock.setFailure('parameters-unavailable')");
        await page.Locator("tr[data-subscription-id='11'] [data-action='edit-params']").ClickAsync();
        await Expect(page.Locator("#editParamsError")).ToHaveTextAsync("The report script could not be inspected.");
        await Expect(page.Locator("#editParamsFields .param-input")).ToHaveCountAsync(0);
        await Expect(page.Locator("#editParamsSaveBtn")).ToBeDisabledAsync();
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData("healthy", "No delivery attempts have been recorded for this subscription.")]
    [InlineData("history-unavailable", "Scheduler trigger history is temporarily unavailable")]
    public async Task DeliveryHistoryKeepsAnEmptyHistoryApartFromAnUnavailableOne(string scenario, string text)
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenListAsync(page);
        await page.EvaluateAsync("scenario => window.__readerMock.setFailure(scenario)", scenario);
        await page.Locator("tr[data-subscription-id='11'] [data-action='history']").ClickAsync();
        await Expect(page.Locator("#subscriptionHistoryTitle")).ToHaveTextAsync("Morning sales Delivery History");
        await Expect(page.Locator("#subscriptionHistoryBody")).ToContainTextAsync(text, new() { IgnoreCase = true });
        await Expect(page.Locator("#subscriptionHistoryBody [role='alert']")).ToHaveCountAsync(scenario == "healthy" ? 0 : 1);
        await page.Locator("#subscriptionHistoryCloseBtn").ClickAsync();
        await Expect(page.Locator("#subscriptionHistoryModal")).ToBeHiddenAsync();
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task SubscribingRequiresTheReportsParametersAndSendsTheDraftOnce()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenSubscribeAsync(page);
        await Expect(page.Locator("#sub-smtp option")).ToHaveCountAsync(2);
        await Expect(page.Locator("#sub-params-fields .param-input")).ToHaveCountAsync(2);
        await page.Locator("#sub-saveBtn").ClickAsync();
        await Expect(page.Locator("#sub-error")).ToHaveTextAsync("Region requires a value.");
        Assert.Equal(0, await CreateCallsAsync(page));

        await page.Locator("#sub-params-fields .param-input").First.FillAsync("West");
        await page.Locator("#sub-name").FillAsync("West weekly");
        await page.EvaluateAsync("() => window.__readerMock.pauseWrites()");
        await page.Locator("#sub-saveBtn").ClickAsync();
        await page.WaitForFunctionAsync("() => window.__readerMock.pendingWrites === 1");
        await Expect(page.Locator("#sub-saveBtn")).ToBeDisabledAsync();
        await page.Locator("#sub-saveBtn").DispatchEventAsync("click");
        await page.EvaluateAsync("() => window.__readerMock.releaseWrites()");
        await WaitForMessageAsync(page, "Future deliveries are now scheduled.");
        await Expect(page.Locator("#subscribeModal")).ToBeHiddenAsync();
        Assert.Equal(1, await CreateCallsAsync(page));
        Assert.Equal("7|West weekly|PDF|smtp_ops|West|D-1", await page.EvaluateAsync<string>(
            "() => { const body = window.__readerMock.calls.find(call => call.method === 'POST').body;"
            + " return [body.reportId, body.name, body.format, body.smtpAlias, body.parameters.Region, body.parameters.AsOf].join('|'); }"));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AReportWhoseParametersCannotBeReadCannotBeSubscribedBlind()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenSubscribeAsync(page, "parameters-unavailable");
        await Expect(page.Locator("#sub-error")).ToContainTextAsync("This report's parameters could not be loaded: The report script could not be inspected.");
        await Expect(page.Locator("#sub-saveBtn")).ToBeDisabledAsync();
        await Expect(page.Locator("#sub-params-fields .param-input")).ToHaveCountAsync(0);
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData("smtp-unavailable", "Delivery connections could not be loaded: Connection catalog is offline.")]
    [InlineData("no-smtp", "No delivery connection is configured")]
    public async Task WithoutADeliveryConnectionOnlyALinkCanBeScheduled(string scenario, string reason)
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenSubscribeAsync(page, scenario);
        await Expect(page.Locator("#sub-error")).ToContainTextAsync(reason);
        await Expect(page.Locator("#sub-saveBtn")).ToBeDisabledAsync();
        await page.Locator("#sub-format").SelectOptionAsync("Link");
        await Expect(page.Locator("#sub-smtp-group")).ToBeHiddenAsync();
        await Expect(page.Locator("#sub-saveBtn")).ToBeEnabledAsync();
        await page.Locator("#sub-params-fields .param-input").First.FillAsync("West");
        await page.Locator("#sub-saveBtn").ClickAsync();
        await WaitForMessageAsync(page, "Future deliveries are now scheduled.");
        Assert.True(await page.EvaluateAsync<bool>("() => window.__readerMock.calls.find(call => call.method === 'POST').body.smtpAlias === null"));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task ARejectedSubscriptionKeepsItsDraft()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenSubscribeAsync(page);
        await page.Locator("#sub-params-fields .param-input").First.FillAsync("West");
        await page.EvaluateAsync("() => window.__readerMock.setFailure('rejected')");
        await page.Locator("#sub-saveBtn").ClickAsync();
        await Expect(page.Locator("#sub-error")).ToHaveTextAsync("No recipient email. Supply one or add an email to your profile.");
        await Expect(page.Locator("#subscribeModal")).ToBeVisibleAsync();
        await Expect(page.Locator("#sub-params-fields .param-input").First).ToHaveValueAsync("West");
        await Expect(page.Locator("#sub-saveBtn")).ToBeEnabledAsync();
        Assert.Empty(await MessagesAsync(page));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task ALateListResponseDoesNotRepaintAnotherView()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenAsync(page, "healthy", "");
        await page.EvaluateAsync("() => window.__readerMock.pauseReads('/api/subscriptions')");
        await page.Locator("#navSubscriptions").ClickAsync();
        await page.WaitForFunctionAsync("() => window.__readerMock.pendingReads === 1");
        await page.Locator("#navFavorites").ClickAsync();
        await Expect(page.Locator("#navFavorites")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("\\bactive\\b"));
        // Let the favourites view finish its own read, so any later change is the subscription list's doing.
        await Expect(page.Locator("#mainContent .loading-state")).ToHaveCountAsync(0);
        var before = await page.Locator("#mainContent").InnerHTMLAsync();
        await page.EvaluateAsync("() => window.__readerMock.releaseReads()");
        await page.EvaluateAsync("async () => { await new Promise(resolve => setTimeout(resolve, 50)); }");
        await Expect(page.Locator("#mainContent")).Not.ToContainTextAsync("My Subscriptions");
        await Expect(page.Locator("tr[data-subscription-id]")).ToHaveCountAsync(0);
        Assert.Equal(before, await page.Locator("#mainContent").InnerHTMLAsync());
    }

    private async Task OpenAsync(IPage page, string scenario, string hash)
    {
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/reader-page-frame.html?fixture={scenario}{hash}");
        await Expect(page.Locator("body")).ToHaveAttributeAsync("data-reader-sandbox-ready", "true");
    }

    private async Task OpenListAsync(IPage page)
    {
        await OpenAsync(page, "healthy", "#subscriptions");
        await Expect(page.Locator("#mainContent .subs-table-wrap")).ToHaveAttributeAsync("data-subscriptions-state", "ready");
    }

    private async Task OpenSubscribeAsync(IPage page, string scenario = "healthy")
    {
        await OpenAsync(page, scenario, "#report-7");
        await page.Locator("#subBtn").ClickAsync();
        await Expect(page.Locator("#subscribeModal")).ToBeVisibleAsync();
        // Both dialog reads have settled once the connection picker is no longer loading.
        await Expect(page.Locator("#sub-smtp option", new() { HasText = "Loading connections" })).ToHaveCountAsync(0);
    }

    private static Task<int> CreateCallsAsync(IPage page) => page.EvaluateAsync<int>(
        "() => window.__readerMock.calls.filter(call => call.method === 'POST' && call.path === '/api/subscriptions').length");

    private static Task<string[]> MessagesAsync(IPage page) =>
        page.EvaluateAsync<string[]>("() => window.__readerMessages.map(item => item.message)");

    private static Task<string[]> TonesAsync(IPage page) =>
        page.EvaluateAsync<string[]>("() => window.__readerMessages.map(item => item.tone)");

    /// <summary>Waits for a notice; a timeout names the notices that did arrive instead of only the wait.</summary>
    private static async Task WaitForMessageAsync(IPage page, string text)
    {
        try
        {
            await page.WaitForFunctionAsync("text => window.__readerMessages.some(item => item.message.includes(text))", text);
        }
        catch (TimeoutException)
        {
            Assert.Fail($"No notice containing '{text}' arrived. Notices: [{string.Join(" | ", await MessagesAsync(page))}]");
        }
    }
}
