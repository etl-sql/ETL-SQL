using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// The schedule and notification catalogs on the production Orchestrator page, driven against the
/// sandbox service fixture: unavailable reads, confirmed writes, and dialogs that outlive their request.
/// </summary>
[Trait("Category", "Browser")]
[Collection(SandboxStoryCollection.Name)]
public sealed class OrchestratorCatalogSandboxTests(SandboxStoryFixture fixture) : IAsyncLifetime
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
    public async Task CatalogCountsAreReadWithThePageAndUncountedLinksAreNotShownAsZero()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenAsync(page);
        // The badges are filled before either catalog tab has been opened.
        await Expect(page.Locator("#schedulesNavCount")).ToHaveTextAsync("2");
        await Expect(page.Locator("#notificationsNavCount")).ToHaveTextAsync("2");
        await page.Locator("#orchNavSchedules").ClickAsync();
        await Expect(page.Locator("#schedulesTableWrap tbody tr")).ToHaveCountAsync(2);
        await Expect(page.Locator("#schedulesTableWrap")).ToContainTextAsync("0 2 * * *");
        await Expect(page.Locator("#schedulesTableWrap th", new() { HasText = "Linked Jobs" })).ToHaveCountAsync(0);
        await Expect(page.Locator("#schedulesTableWrap")).Not.ToContainTextAsync("0 jobs");
        await page.Locator("#orchNavNotifications").ClickAsync();
        await Expect(page.Locator("#notificationsTableWrap tbody tr")).ToHaveCountAsync(2);
        await Expect(page.Locator("#notificationsTableWrap")).Not.ToContainTextAsync("0 jobs");
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData("catalog-unavailable", "Schedules are temporarily unavailable")]
    [InlineData("catalog-denied", "Schedules are not available to this account")]
    [InlineData("catalog-malformed", "Invalid schedule catalog response")]
    public async Task AFailedCatalogReadIsUnavailableNotEmptyAndRecoversOnRefresh(string scenario, string reason)
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenAsync(page, scenario);
        await page.Locator("#orchNavSchedules").ClickAsync();
        var alert = page.Locator("#schedulesTableWrap [role='alert']");
        await Expect(alert).ToContainTextAsync(reason);
        await Expect(page.Locator("#schedulesNavCount")).ToHaveTextAsync("—");
        await Expect(page.Locator("#schedulesTableWrap")).Not.ToContainTextAsync("No shared schedules defined yet");
        await Expect(page.Locator("#notificationsNavCount")).ToHaveTextAsync("—");

        await page.EvaluateAsync("() => window.__orchestratorMock.setFailure('healthy')");
        await page.Locator("#refreshSchedulesBtn").ClickAsync();
        await Expect(page.Locator("#schedulesTableWrap tbody tr")).ToHaveCountAsync(2);
        await Expect(page.Locator("#schedulesNavCount")).ToHaveTextAsync("2");
        await Expect(alert).ToHaveCountAsync(0);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AFailedRefreshClearsRowsThatWereLoadedEarlier()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenAsync(page);
        await page.Locator("#orchNavNotifications").ClickAsync();
        await Expect(page.Locator("#notificationsTableWrap tbody tr")).ToHaveCountAsync(2);
        await page.EvaluateAsync("() => window.__orchestratorMock.setFailure('catalog-unavailable')");
        await page.Locator("#refreshNotificationsBtn").ClickAsync();
        await Expect(page.Locator("#notificationsTableWrap [role='alert']")).ToContainTextAsync("Notifications are temporarily unavailable");
        await Expect(page.Locator("#notificationsTableWrap tbody tr")).ToHaveCountAsync(0);
        await Expect(page.Locator("#notificationsNavCount")).ToHaveTextAsync("—");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task TogglingAScheduleIsAnnouncedOnlyAfterTheServiceConfirmsIt()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenSchedulesAsync(page);
        await page.EvaluateAsync("() => window.__orchestratorMock.pauseWrites()");
        var toggle = page.Locator("[data-action='toggle-schedule'][data-name='nightly_utc']");
        await toggle.ClickAsync();
        await page.WaitForFunctionAsync("() => window.__orchestratorMock.pendingWrites === 1");
        await Expect(toggle).ToBeDisabledAsync();
        Assert.Empty(await MessagesAsync(page));
        // A repaint while the write is pending keeps the row locked, so it cannot be sent twice.
        await page.Locator("#refreshSchedulesBtn").ClickAsync();
        await Expect(page.Locator("[data-action='delete-schedule'][data-name='nightly_utc']")).ToBeDisabledAsync();
        await page.EvaluateAsync("() => window.__orchestratorMock.releaseWrites()");
        await WaitForMessageAsync(page, "Schedule 'nightly_utc' disabled.");
        await Expect(toggle).ToHaveTextAsync("Enable");
        await Expect(toggle).ToBeEnabledAsync();
        Assert.Equal(1, await WriteCountAsync(page, "PUT", "/api/orchestrator/schedules/nightly_utc"));
        Assert.False(await page.EvaluateAsync<bool>(
            "() => window.__orchestratorMock.calls.find(call => call.method === 'PUT' && call.path.endsWith('/schedules/nightly_utc')).body.isEnabled"));
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData("denied", "Unexpected response (403)")]
    [InlineData("failed", "Orchestrator service unavailable.")]
    [InlineData("unexpected", "Unexpected response (206)")]
    public async Task ARejectedScheduleWriteIsNotAnnouncedAsDone(string scenario, string reason)
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenSchedulesAsync(page);
        await page.EvaluateAsync("scenario => window.__orchestratorMock.setFailure(scenario)", scenario);
        var toggle = page.Locator("[data-action='toggle-schedule'][data-name='nightly_utc']");
        await toggle.ClickAsync();
        await WaitForMessageAsync(page, reason);
        Assert.DoesNotContain(await MessagesAsync(page), message => message.Contains("disabled.", StringComparison.Ordinal));
        await Expect(toggle).ToHaveTextAsync("Disable");
        await Expect(toggle).ToBeEnabledAsync();
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task ARefusedDeleteShowsTheServicesReason()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenSchedulesAsync(page);
        await page.EvaluateAsync("() => window.__orchestratorMock.setFailure('in-use')");
        await page.Locator("[data-action='delete-schedule'][data-name='nightly_utc']").ClickAsync();
        await page.Locator(".etlsql-feedback-btn-danger").ClickAsync();
        await WaitForMessageAsync(page, "Cannot delete schedule 'nightly_utc' because it is attached to 1 job(s).");
        await Expect(page.Locator("#schedulesTableWrap tbody tr")).ToHaveCountAsync(2);
        Assert.DoesNotContain(await MessagesAsync(page), message => message.Contains("deleted.", StringComparison.Ordinal));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task DeletingANotificationRemovesItAfterConfirmation()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenAsync(page);
        await page.Locator("#orchNavNotifications").ClickAsync();
        await page.Locator("[data-action='delete-notification'][data-name='ops_mail']").ClickAsync();
        await page.Locator(".etlsql-feedback-btn-danger").ClickAsync();
        await WaitForMessageAsync(page, "Notification 'ops_mail' deleted.");
        await Expect(page.Locator("#notificationsTableWrap tbody tr")).ToHaveCountAsync(1);
        await Expect(page.Locator("#notificationsNavCount")).ToHaveTextAsync("1");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task CreatingAScheduleValidatesThenSendsTheDraftOnce()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenSchedulesAsync(page);
        await page.Locator("#newScheduleBtn").ClickAsync();
        await page.Locator("#cs-saveBtn").ClickAsync();
        await Expect(page.Locator("#cs-error")).ToHaveTextAsync("Name is required.");
        Assert.Equal(0, await WriteCountAsync(page, "POST", "/api/orchestrator/schedules"));

        await page.Locator("#cs-name").FillAsync("weekly_close");
        await page.Locator("#cs-cron").FillAsync("0 6 * * 1");
        await page.EvaluateAsync("() => window.__orchestratorMock.pauseWrites()");
        await page.Locator("#cs-saveBtn").ClickAsync();
        await page.WaitForFunctionAsync("() => window.__orchestratorMock.pendingWrites === 1");
        await page.Locator("#cs-saveBtn").DispatchEventAsync("click");
        await page.EvaluateAsync("() => window.__orchestratorMock.releaseWrites()");
        await WaitForMessageAsync(page, "Schedule 'weekly_close' created.");
        await Expect(page.Locator("#scheduleModal")).ToBeHiddenAsync();
        await Expect(page.Locator("#schedulesTableWrap tbody tr")).ToHaveCountAsync(3);
        Assert.Equal(1, await WriteCountAsync(page, "POST", "/api/orchestrator/schedules"));
        Assert.Equal("weekly_close|0 6 * * 1|UTC|True", await page.EvaluateAsync<string>(
            "() => { const body = window.__orchestratorMock.calls.find(call => call.method === 'POST' && call.path === '/api/orchestrator/schedules').body;"
            + " return [body.name, body.cron, body.timeZone, body.isEnabled ? 'True' : 'False'].join('|'); }"));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task ARejectedSaveKeepsTheDraftAndShowsTheReasonInTheDialog()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenSchedulesAsync(page);
        await page.Locator("#newScheduleBtn").ClickAsync();
        await page.Locator("#cs-name").FillAsync("nightly_utc");
        await page.Locator("#cs-saveBtn").ClickAsync();
        await Expect(page.Locator("#cs-error")).ToHaveTextAsync("'nightly_utc' already exists.");
        await Expect(page.Locator("#scheduleModal")).ToBeVisibleAsync();
        await Expect(page.Locator("#cs-name")).ToHaveValueAsync("nightly_utc");
        await Expect(page.Locator("#cs-saveBtn")).ToBeEnabledAsync();
        Assert.Empty(await MessagesAsync(page));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task ALateSaveCannotCloseADialogOpenedForAnotherSchedule()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenSchedulesAsync(page);
        await page.Locator("[data-action='edit-schedule'][data-name='nightly_utc']").ClickAsync();
        await Expect(page.Locator("#cs-name")).ToBeDisabledAsync();
        await page.Locator("#cs-cron").FillAsync("30 3 * * *");
        await page.EvaluateAsync("() => window.__orchestratorMock.pauseWrites()");
        await page.Locator("#cs-saveBtn").ClickAsync();
        await page.WaitForFunctionAsync("() => window.__orchestratorMock.pendingWrites === 1");
        await page.Locator("#cs-cancelBtn").ClickAsync();
        await page.Locator("[data-action='edit-schedule'][data-name='hourly_us']").ClickAsync();
        await page.Locator("#cs-description").FillAsync("Draft that must survive");
        await page.EvaluateAsync("() => window.__orchestratorMock.releaseWrites()");
        await WaitForMessageAsync(page, "Schedule 'nightly_utc' updated.");
        // Save stays locked until the earlier request has been fully handled; only then is it
        // meaningful that the newer dialog is still open.
        await Expect(page.Locator("#cs-saveBtn")).ToBeEnabledAsync();

        await Expect(page.Locator("#scheduleModal")).ToBeVisibleAsync();
        await Expect(page.Locator("#scheduleModalTitle")).ToHaveTextAsync("Edit Schedule 'hourly_us'");
        await Expect(page.Locator("#cs-description")).ToHaveValueAsync("Draft that must survive");
        Assert.Equal("30 3 * * *", await page.EvaluateAsync<string>(
            "() => window.__orchestratorMock.calls.find(call => call.method === 'PUT' && call.path.endsWith('/schedules/nightly_utc')).body.cron"));
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData("delivered", "Test notification sent to 'ops_mail'.", true)]
    [InlineData("skipped", "Nothing was sent to 'ops_mail'. Notification endpoint is disabled.", false)]
    [InlineData("failed", "Delivery to 'ops_mail' failed. SMTP relay refused the message.", false)]
    public async Task ATestDispatchReportsDeliveryOnlyWhenTheMessageWasDelivered(string outcome, string text, bool delivered)
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenAsync(page);
        await page.Locator("#orchNavNotifications").ClickAsync();
        await page.EvaluateAsync("outcome => window.__orchestratorMock.setDispatch(outcome)", outcome);
        await page.Locator("[data-action='dispatch-notification'][data-name='ops_mail']").ClickAsync();
        await Expect(page.Locator("#dispatchModalTitle")).ToHaveTextAsync("Test Dispatch to 'ops_mail'");
        await page.Locator("#disp-sendBtn").ClickAsync();
        if (delivered)
        {
            await WaitForMessageAsync(page, text);
            await Expect(page.Locator("#dispatchModal")).ToBeHiddenAsync();
        }
        else
        {
            await Expect(page.Locator("#disp-error")).ToHaveTextAsync(text);
            await Expect(page.Locator("#dispatchModal")).ToBeVisibleAsync();
            Assert.DoesNotContain(await MessagesAsync(page), message => message.Contains("sent to", StringComparison.Ordinal));
        }
        Assert.Equal(1, await WriteCountAsync(page, "POST", "/api/orchestrator/notifications/ops_mail/dispatch"));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task LeavingThePageDiscardsPendingCatalogWork()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenSchedulesAsync(page);
        await page.EvaluateAsync("() => window.__orchestratorMock.pauseWrites()");
        await page.Locator("[data-action='toggle-schedule'][data-name='nightly_utc']").ClickAsync();
        await page.WaitForFunctionAsync("() => window.__orchestratorMock.pendingWrites === 1");
        await page.EvaluateAsync("() => { window.dispatchEvent(new Event('pagehide')); window.__callsAtDisposal = window.__orchestratorMock.calls.length; window.__orchestratorMock.releaseWrites(); }");
        await page.WaitForFunctionAsync("() => window.__orchestratorMock.pendingWrites === 0");
        // Drain the write continuation, including its status check and the reload it would start.
        await page.EvaluateAsync("async () => { await new Promise(resolve => setTimeout(resolve, 0)); }");
        Assert.Empty(await MessagesAsync(page));
        Assert.Equal(await page.EvaluateAsync<int>("() => window.__callsAtDisposal"),
            await page.EvaluateAsync<int>("() => window.__orchestratorMock.calls.length"));
        await page.Locator("#newScheduleBtn").DispatchEventAsync("click");
        await Expect(page.Locator("#scheduleModal")).ToBeHiddenAsync();
        Assert.Empty(session.PageErrors);
    }

    private async Task OpenAsync(IPage page, string scenario = "healthy")
    {
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/orchestrator-page-frame.html?fixture={scenario}");
        await Expect(page.Locator("body")).ToHaveAttributeAsync("data-orchestrator-sandbox-ready", "true");
        await Expect(page.Locator("[data-action='toggle'][data-name='daily_sales']")).ToBeVisibleAsync();
    }

    private async Task OpenSchedulesAsync(IPage page)
    {
        await OpenAsync(page);
        await page.Locator("#orchNavSchedules").ClickAsync();
        await Expect(page.Locator("#schedulesTableWrap tbody tr")).ToHaveCountAsync(2);
        // Opening the tab starts a second read; let it settle so later repaints belong to the test.
        await page.WaitForFunctionAsync(
            "() => window.__orchestratorMock.calls.filter(call => call.method === 'GET' && call.path === '/api/orchestrator/schedules').length >= 2");
        await Expect(page.Locator("[data-action='toggle-schedule'][data-name='nightly_utc']")).ToBeEnabledAsync();
    }

    private static Task<string[]> MessagesAsync(IPage page) =>
        page.EvaluateAsync<string[]>("() => window.__orchestratorMessages.map(item => item.message)");

    private static Task WaitForMessageAsync(IPage page, string text) =>
        page.WaitForFunctionAsync("text => window.__orchestratorMessages.some(item => item.message.includes(text))", text);

    private static Task<int> WriteCountAsync(IPage page, string method, string path) =>
        page.EvaluateAsync<int>(
            "([method, path]) => window.__orchestratorMock.calls.filter(call => call.method === method && call.path === path).length",
            new[] { method, path });
}
