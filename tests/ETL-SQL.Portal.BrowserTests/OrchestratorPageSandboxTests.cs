using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

[Trait("Category", "Browser")]
[Collection(SandboxStoryCollection.Name)]
public sealed class OrchestratorPageSandboxTests(SandboxStoryFixture fixture) : IAsyncLifetime
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
    public async Task ToggleSendsTheLoadedVersionAndOnlyConfirmsAnAcceptedWrite()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenAsync(page);
        await page.Locator("[data-action='toggle'][data-name='daily_sales']").ClickAsync();
        await page.WaitForFunctionAsync("() => window.__orchestratorMessages.some(item => item.message === \"Job 'daily_sales' disabled.\")");
        Assert.Equal("\"3\"", await page.EvaluateAsync<string>("() => window.__orchestratorMock.calls.find(call => call.method === 'PUT').ifMatch"));
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData("denied", "Job management is denied.")]
    [InlineData("failed", "Service rejected the write.")]
    [InlineData("conflict", "The resource changed")]
    [InlineData("unexpected", "Unexpected response")]
    public async Task RejectedToggleDoesNotAnnounceSuccess(string scenario, string message)
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenAsync(page, scenario);
        await page.Locator("[data-action='toggle'][data-name='daily_sales']").ClickAsync();
        await page.WaitForFunctionAsync("text => window.__orchestratorMessages.some(item => item.message.includes(text))", message);
        Assert.False(await page.EvaluateAsync<bool>("() => window.__orchestratorMessages.some(item => item.message === \"Job 'daily_sales' disabled.\")"));
        await Expect(page.Locator("[data-action='toggle'][data-name='daily_sales']")).ToBeEnabledAsync();
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task RunConfirmationKeepsTheSubmittedJobName()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenAsync(page);
        await page.Locator("[data-action='trigger'][data-name='daily_sales']").ClickAsync();
        await page.Locator("#runJobSubmitBtn").ClickAsync();
        await page.WaitForFunctionAsync("() => window.__orchestratorMessages.some(item => item.message === \"Job 'daily_sales' triggered.\")");
        await Expect(page.Locator("#runJobModal")).ToBeHiddenAsync();
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task DeleteUsesTheLoadedVersion()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenAsync(page);
        await page.Locator("[data-action='delete'][data-name='daily_sales']").ClickAsync();
        await page.Locator(".etlsql-feedback-btn-danger").ClickAsync();
        await page.WaitForFunctionAsync("() => window.__orchestratorMessages.some(item => item.message === \"Job 'daily_sales' deleted.\")");
        await Expect(page.Locator("[data-action='toggle'][data-name='daily_sales']")).ToHaveCountAsync(0);
        Assert.Equal("\"3\"", await page.EvaluateAsync<string>("() => window.__orchestratorMock.calls.find(call => call.method === 'DELETE').ifMatch"));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task RepaintingTheJobListDoesNotSubmitADuplicatePendingToggle()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenAsync(page);
        await page.EvaluateAsync("() => window.__orchestratorMock.pauseWrites()");
        await page.Locator("[data-action='toggle'][data-name='daily_sales']").ClickAsync();
        await page.WaitForFunctionAsync("() => window.__orchestratorMock.pendingWrites === 1");
        await page.Locator("#refreshJobsBtn").ClickAsync();
        var toggle = page.Locator("[data-action='toggle'][data-name='daily_sales']");
        await Expect(toggle).ToBeEnabledAsync();
        await toggle.ClickAsync();
        Assert.Equal(1, await page.EvaluateAsync<int>("() => window.__orchestratorMock.calls.filter(call => call.method === 'PUT').length"));
        await page.EvaluateAsync("() => window.__orchestratorMock.releaseWrites()");
        await page.WaitForFunctionAsync("() => window.__orchestratorMessages.some(item => item.message === \"Job 'daily_sales' disabled.\")");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task RunOverridesAreValidatedAndSubmittedOnce()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenAsync(page);
        await page.Locator("[data-action='trigger'][data-name='daily_sales']").ClickAsync();
        await page.Locator("#runAddOverrideBtn").ClickAsync();
        await page.Locator(".run-override-name").FillAsync("bad name");
        await page.Locator(".run-override-value").FillAsync("2026-10-01");
        await page.Locator("#runJobSubmitBtn").ClickAsync();
        await Expect(page.Locator("#runJobError")).ToContainTextAsync("not a variable name");
        Assert.Equal(0, await page.EvaluateAsync<int>("() => window.__orchestratorMock.calls.filter(call => call.path.endsWith('/trigger')).length"));
        await page.Locator(".run-override-name").FillAsync("start_date");
        await page.EvaluateAsync("() => window.__orchestratorMock.pauseWrites()");
        await page.Locator("#runJobSubmitBtn").ClickAsync();
        await page.WaitForFunctionAsync("() => window.__orchestratorMock.pendingWrites === 1");
        await page.Locator("#runJobSubmitBtn").DispatchEventAsync("click");
        Assert.Equal(1, await page.EvaluateAsync<int>("() => window.__orchestratorMock.calls.filter(call => call.path.endsWith('/trigger')).length"));
        Assert.Equal("2026-10-01", await page.EvaluateAsync<string>("() => window.__orchestratorMock.calls.find(call => call.path.endsWith('/trigger')).body.variables['@start_date']"));
        await page.EvaluateAsync("() => window.__orchestratorMock.releaseWrites()");
        await Expect(page.Locator("#runJobModal")).ToBeHiddenAsync();
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task OldRunCompletionCannotCloseANewerRunDialog()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenAsync(page);
        await page.Locator("[data-action='trigger'][data-name='daily_sales']").ClickAsync();
        await page.EvaluateAsync("() => window.__orchestratorMock.pauseWrites()");
        await page.Locator("#runJobSubmitBtn").ClickAsync();
        await page.WaitForFunctionAsync("() => window.__orchestratorMock.pendingWrites === 1");
        await page.Locator("#runJobCancelBtn").ClickAsync();
        await page.Locator("[data-action='trigger'][data-name='hourly_orders']").ClickAsync();
        await Expect(page.Locator("#runJobSubmitBtn")).ToBeDisabledAsync();
        await page.EvaluateAsync("() => window.__orchestratorMock.releaseWrites()");
        await Expect(page.Locator("#runJobSubmitBtn")).ToBeEnabledAsync();
        await Expect(page.Locator("#runJobModal")).ToBeVisibleAsync();
        await Expect(page.Locator("#runJobTitle")).ToHaveTextAsync("Run hourly_orders");
        Assert.Empty(await page.EvaluateAsync<string[]>("() => window.__orchestratorMessages.map(item => item.message)"));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task DisposalPreventsLateWriteFeedbackAndReload()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenAsync(page);
        await page.EvaluateAsync("() => window.__orchestratorMock.pauseWrites()");
        await page.Locator("[data-action='toggle'][data-name='daily_sales']").ClickAsync();
        await page.WaitForFunctionAsync("() => window.__orchestratorMock.pendingWrites === 1");
        await page.EvaluateAsync("() => { window.dispatchEvent(new Event('pagehide')); window.__readsAtDisposal = window.__orchestratorMock.calls.length; window.__orchestratorMock.releaseWrites(); }");
        await page.WaitForFunctionAsync("() => window.__orchestratorMock.pendingWrites === 0");
        // Drain the mutation continuation, including the response-status check.
        await page.EvaluateAsync("async () => { await new Promise(resolve => setTimeout(resolve, 0)); }");
        Assert.Empty(await page.EvaluateAsync<string[]>("() => window.__orchestratorMessages.map(item => item.message)"));
        Assert.Equal(await page.EvaluateAsync<int>("() => window.__readsAtDisposal"),
            await page.EvaluateAsync<int>("() => window.__orchestratorMock.calls.length"));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task SandboxPrototypeMountsTheProductionPage()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenAsync(page);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(Path.GetTempPath(), "etlsql-orchestrator-prototype.png"),
            FullPage = true
        });
        Assert.Empty(session.PageErrors);
    }

    private async Task OpenAsync(IPage page, string scenario = "healthy")
    {
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/orchestrator-page-frame.html?fixture={scenario}");
        await Expect(page.Locator("body")).ToHaveAttributeAsync("data-orchestrator-sandbox-ready", "true");
        await Expect(page.Locator("[data-action='toggle'][data-name='daily_sales']")).ToBeVisibleAsync();
    }
}
