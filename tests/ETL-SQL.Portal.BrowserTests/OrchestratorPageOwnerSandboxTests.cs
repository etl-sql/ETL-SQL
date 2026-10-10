using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// The Orchestrator page owner: who is let in and what they are offered, the create-job dialog,
/// today's schedule strip, and the service Stop control.
/// </summary>
[Trait("Category", "Browser")]
[Collection(SandboxStoryCollection.Name)]
public sealed class OrchestratorPageOwnerSandboxTests(SandboxStoryFixture fixture) : IAsyncLifetime
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
    public async Task AViewerIsOfferedNoAuthoringOrServiceControls()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenAsync(page, role: "OrchestratorViewer");
        await Expect(page.Locator("#statusLabel")).ToHaveTextAsync("Online");
        foreach (var control in new[] { "#newJobBtn", "#newScheduleBtn", "#newNotificationBtn", "#stopBtn" })
            await Expect(page.Locator(control)).ToBeHiddenAsync();
        await Expect(page.Locator("#restartBtn")).ToHaveCountAsync(0);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AManagerMayAuthorButOnlyAnAdministratorIsOfferedStop()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenAsync(page, role: "OrchestratorManager");
        await Expect(page.Locator("#statusLabel")).ToHaveTextAsync("Online");
        await Expect(page.Locator("#newJobBtn")).ToBeVisibleAsync();
        await Expect(page.Locator("#stopBtn")).ToBeHiddenAsync();
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task SomeoneWithoutAnOrchestratorRoleIsSentAwayBeforeThePageLoadsAnything()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        // The exit target does not exist in the sandbox; answer it so the navigation can be observed.
        await page.RouteAsync("**/index.html", route => route.FulfillAsync(new() { ContentType = "text/html", Body = "<title>catalog</title>" }));
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/orchestrator-page-frame.html?fixture=healthy&role=Viewer");
        await page.WaitForURLAsync("**/index.html");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task TodaysStripPlacesEachDueJobAndOpensItsDetail()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenAsync(page);
        var run = page.Locator("#ganttChart .orch-day-run");
        await Expect(run).ToHaveCountAsync(1);
        await Expect(run).ToHaveAttributeAsync("data-job", "daily_sales");
        // 18:30 of a 24-hour day is 77.08% along the track.
        Assert.Equal("77.08%", await run.EvaluateAsync<string>("element => element.style.left"));
        await Expect(page.Locator("#ganttChart .orch-day-name")).ToHaveTextAsync("daily_sales");
        await run.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Expect(page.Locator("#detailJobName")).ToHaveTextAsync("daily_sales");
        await page.Locator("#closeDetailBtn").ClickAsync();

        await page.Locator("#timelineModeCalendarBtn").ClickAsync();
        await Expect(page.Locator("#timelineGanttWrap")).ToBeHiddenAsync();
        await Expect(page.Locator("#timelineCalendarWrap .orch-calendar-item")).ToHaveCountAsync(1);
        await Expect(page.Locator("#timelineTitle")).ToHaveTextAsync("7-Day Run Calendar");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task CreatingAJobOffersTheServersScriptsAndBundlesAndSendsTheDraftOnce()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenAsync(page);
        await page.Locator("#newJobBtn").ClickAsync();
        await Expect(page.Locator("#cj-script-select option")).ToHaveCountAsync(3);
        await Expect(page.Locator("#cj-shared-schedule-select option")).ToHaveCountAsync(3);
        // Two published versions of one bundle are one choice, labelled with the newest.
        await Expect(page.Locator("#cj-bundle-select option")).ToHaveCountAsync(2);
        await Expect(page.Locator("#cj-bundle-select option").Nth(1)).ToHaveTextAsync("sales_bundle (latest: v2)");
        await page.Locator("#cj-saveBtn").ClickAsync();
        await Expect(page.Locator("#cj-error")).ToHaveTextAsync("Job identifier is required.");

        await page.Locator("#cj-name").FillAsync("weekly_close");
        await page.Locator("#cj-script-select").SelectOptionAsync("etl/daily_sales.etlsql");
        await page.EvaluateAsync("() => window.__orchestratorMock.pauseWrites()");
        await page.Locator("#cj-saveBtn").ClickAsync();
        await page.WaitForFunctionAsync("() => window.__orchestratorMock.pendingWrites === 1");
        await page.Locator("#cj-saveBtn").DispatchEventAsync("click");
        await page.EvaluateAsync("() => window.__orchestratorMock.releaseWrites()");
        await WaitForMessageAsync(page, "Job 'weekly_close' created.");
        await Expect(page.Locator("#createJobModal")).ToBeHiddenAsync();
        await Expect(page.Locator("tr.job-row[data-name='weekly_close']")).ToBeVisibleAsync();
        Assert.Equal(1, await page.EvaluateAsync<int>(
            "() => window.__orchestratorMock.calls.filter(call => call.method === 'POST' && call.path === '/api/orchestrator/jobs').length"));
        Assert.Equal("weekly_close|etl/daily_sales.etlsql|Create", await page.EvaluateAsync<string>(
            "() => { const body = window.__orchestratorMock.calls.find(call => call.method === 'POST' && call.path === '/api/orchestrator/jobs').body;"
            + " return [body.name, body.targetPath, body.mode].join('|'); }"));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task ARejectedCreateKeepsTheDraftAndAnUnreadablePickerSaysSo()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenAsync(page, "pickers-unavailable");
        await page.Locator("#newJobBtn").ClickAsync();
        await Expect(page.Locator("#cj-error")).ToContainTextAsync("Server scripts could not be loaded: Script unavailable.");
        await Expect(page.Locator("#cj-script-select")).ToBeDisabledAsync();
        await Expect(page.Locator("#cj-bundle-select")).ToBeEnabledAsync();

        await page.Locator("#cj-name").FillAsync("daily_sales");
        await page.Locator("#cj-script-path").FillAsync("etl/typed.etlsql");
        await page.Locator("#cj-saveBtn").ClickAsync();
        await Expect(page.Locator("#cj-error")).ToHaveTextAsync("Job 'daily_sales' already exists.");
        await Expect(page.Locator("#createJobModal")).ToBeVisibleAsync();
        await Expect(page.Locator("#cj-script-path")).ToHaveValueAsync("etl/typed.etlsql");
        Assert.Empty(await MessagesAsync(page));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AJobCreatedWithoutItsSharedScheduleSaysBothThings()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenAsync(page);
        await page.Locator("#newJobBtn").ClickAsync();
        await page.Locator("#cj-name").FillAsync("month_end");
        await page.Locator("#cj-script-path").FillAsync("etl/month_end.etlsql");
        await page.Locator("#cj-sched-mode-shared").CheckAsync();
        await page.Locator("#cj-saveBtn").ClickAsync();
        await Expect(page.Locator("#cj-error")).ToHaveTextAsync("Choose the shared schedule to attach.");
        await page.Locator("#cj-shared-schedule-select").SelectOptionAsync("nightly_utc");
        await page.EvaluateAsync("() => window.__orchestratorMock.setFailure('attach-refused')");
        await page.Locator("#cj-saveBtn").ClickAsync();
        await WaitForMessageAsync(page, "Job 'month_end' was created, but schedule 'nightly_utc' was not attached");
        await Expect(page.Locator("tr.job-row[data-name='month_end']")).ToBeVisibleAsync();
        Assert.DoesNotContain("Job 'month_end' created.", await MessagesAsync(page));
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData("healthy", "Stop signal sent to the Orchestrator service.")]
    [InlineData("failed", "The Orchestrator service refused the stop request.")]
    [InlineData("denied", "Unexpected response (403)")]
    public async Task StoppingTheServiceIsConfirmedFirstAndAnnouncedOnlyWhenAccepted(string scenario, string message)
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenAsync(page);
        await Expect(page.Locator("#stopBtn")).ToBeVisibleAsync();
        await page.EvaluateAsync("scenario => window.__orchestratorMock.setFailure(scenario)", scenario);
        await page.Locator("#stopBtn").ClickAsync();
        Assert.Equal(0, await StopCallsAsync(page));
        await page.Locator(".etlsql-feedback-btn-danger").ClickAsync();
        await WaitForMessageAsync(page, message);
        Assert.Equal(1, await StopCallsAsync(page));
        if (scenario != "healthy")
            Assert.DoesNotContain(await MessagesAsync(page), text => text.StartsWith("Stop signal sent", StringComparison.Ordinal));
        Assert.Empty(session.PageErrors);
    }

    private async Task OpenAsync(IPage page, string scenario = "healthy", string role = "Admin")
    {
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/orchestrator-page-frame.html?fixture={scenario}&role={role}");
        await Expect(page.Locator("body")).ToHaveAttributeAsync("data-orchestrator-sandbox-ready", "true");
        await Expect(page.Locator("[data-action='toggle'][data-name='daily_sales']")).ToBeVisibleAsync();
    }

    private static Task<int> StopCallsAsync(IPage page) => page.EvaluateAsync<int>(
        "() => window.__orchestratorMock.calls.filter(call => call.path === '/api/orchestrator/service/stop').length");

    private static Task<string[]> MessagesAsync(IPage page) =>
        page.EvaluateAsync<string[]>("() => window.__orchestratorMessages.map(item => item.message)");

    /// <summary>Waits for a notice; a timeout names the notices that did arrive instead of only the wait.</summary>
    private static async Task WaitForMessageAsync(IPage page, string text)
    {
        try
        {
            await page.WaitForFunctionAsync("text => window.__orchestratorMessages.some(item => item.message.includes(text))", text);
        }
        catch (TimeoutException)
        {
            Assert.Fail($"No notice containing '{text}' arrived. Notices: [{string.Join(" | ", await MessagesAsync(page))}]");
        }
    }
}
