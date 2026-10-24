using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// The job detail panel on the production Orchestrator page: each section belongs to the job that
/// was opened, an unreadable section is not drawn as "none", and writes are confirmed before they
/// are announced.
/// </summary>
[Trait("Category", "Browser")]
[Collection(SandboxStoryCollection.Name)]
public sealed class OrchestratorJobDetailSandboxTests(SandboxStoryFixture fixture) : IAsyncLifetime
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
    public async Task OpeningAJobShowsItsAttachmentsStateHistoryAndAccess()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenJobAsync(page, "daily_sales");
        await Expect(page.Locator("#detailJobName")).ToHaveTextAsync("daily_sales");
        await Expect(page.Locator("#metaVersion")).ToHaveTextAsync("v3");
        await Expect(page.Locator("#detailLinkedSchedulesWrap")).ToContainTextAsync("nightly_utc");
        await Expect(page.Locator("#detailLinkedNotificationsWrap")).ToContainTextAsync("ops_mail");
        await Expect(page.Locator("#detailLinkedNotificationsWrap .badge")).ToHaveTextAsync("Failure");
        await Expect(page.Locator("#watermarksWrap")).ToContainTextAsync("last_order_id");
        await Expect(page.Locator("#historyTableWrap tbody tr")).ToHaveCountAsync(2);
        await Expect(page.Locator("[data-resume-id='42']")).ToHaveTextAsync("Resume · load_orders");
        await Expect(page.Locator("#historyTableWrap")).ToContainTextAsync("Only failed or cancelled runs can resume.");
        // The access panel renders its grants and its controls; it used to render an exception message.
        await Expect(page.Locator("#accessPanelWrap")).ToContainTextAsync("finance-ops");
        await Expect(page.Locator("#accessPanelWrap #aclGrantBtn")).ToBeVisibleAsync();
        await Expect(page.Locator("#accessPanelWrap #aclOwnerBtn")).ToBeVisibleAsync();
        await Expect(page.Locator("#accessPanelWrap")).Not.ToContainTextAsync("is not a function");
        await Expect(page.Locator("tr.job-row.selected")).ToHaveAttributeAsync("data-name", "daily_sales");
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData("detail-unavailable", "Orchestrator job details are temporarily unavailable")]
    [InlineData("detail-malformed", "Invalid")]
    public async Task AnUnreadableSectionIsNotDrawnAsEmpty(string scenario, string reason)
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenAsync(page, scenario);
        await page.Locator("tr.job-row[data-name='daily_sales'] td").First.ClickAsync();
        foreach (var section in new[] { "#detailLinkedSchedulesWrap", "#detailLinkedNotificationsWrap", "#watermarksWrap", "#historyTableWrap" })
            await Expect(page.Locator($"{section} [role='alert']")).ToContainTextAsync(reason);
        await Expect(page.Locator("#accessPanelWrap")).ToContainTextAsync("Access could not be loaded");
        await Expect(page.Locator("#detailPanel")).Not.ToContainTextAsync("No shared schedules attached");
        await Expect(page.Locator("#detailPanel")).Not.ToContainTextAsync("No notification endpoints attached");
        await Expect(page.Locator("#detailPanel")).Not.ToContainTextAsync("No execution history recorded yet");
        await Expect(page.Locator("#accessPanelWrap #aclGrantBtn")).ToHaveCountAsync(0);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task ARefusedGrantListExplainsTheRefusalAndOffersNoControls()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenAsync(page, "detail-denied");
        await page.Locator("tr.job-row[data-name='daily_sales'] td").First.ClickAsync();
        await Expect(page.Locator("#accessPanelWrap")).ToContainTextAsync("not permitted to administer its access");
        await Expect(page.Locator("#accessPanelWrap button")).ToHaveCountAsync(0);
        await Expect(page.Locator("#detailLinkedSchedulesWrap")).ToContainTextAsync("nightly_utc");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task ALateDetailResponseIsNotDrawnUnderAnotherJob()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenAsync(page);
        await page.EvaluateAsync("() => window.__orchestratorMock.pauseDetail('daily_sales')");
        await page.Locator("tr.job-row[data-name='daily_sales'] td").First.ClickAsync();
        await page.WaitForFunctionAsync("() => window.__orchestratorMock.pendingDetailReads >= 5");
        await page.Locator("tr.job-row[data-name='hourly_orders'] td").First.ClickAsync();
        await Expect(page.Locator("#detailLinkedSchedulesWrap")).ToContainTextAsync("No shared schedules attached.");
        await Expect(page.Locator("#historyTableWrap")).ToContainTextAsync("No execution history recorded yet.");
        await page.EvaluateAsync("() => window.__orchestratorMock.releaseDetail()");
        await page.WaitForFunctionAsync("() => window.__orchestratorMock.pendingDetailReads === 0");
        // Drain the released responses' continuations before reading what they left on screen.
        await page.EvaluateAsync("async () => { await new Promise(resolve => setTimeout(resolve, 50)); }");
        await Expect(page.Locator("#detailJobName")).ToHaveTextAsync("hourly_orders");
        await Expect(page.Locator("#detailPanel")).Not.ToContainTextAsync("nightly_utc");
        await Expect(page.Locator("#detailPanel")).Not.ToContainTextAsync("last_order_id");
        await Expect(page.Locator("#detailPanel")).Not.ToContainTextAsync("finance-ops");
        await Expect(page.Locator("[data-resume-id]")).ToHaveCountAsync(0);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task DetachingAndAttachingAScheduleAreConfirmedBeforeTheyAreAnnounced()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenJobAsync(page, "daily_sales");
        await page.EvaluateAsync("() => window.__orchestratorMock.setFailure('failed')");
        await page.Locator("[data-detach-schedule='nightly_utc']").ClickAsync();
        await page.Locator(".etlsql-feedback-btn-danger").ClickAsync();
        await WaitForMessageAsync(page, "Orchestrator service unavailable.");
        await Expect(page.Locator("[data-detach-schedule='nightly_utc']")).ToBeVisibleAsync();
        Assert.DoesNotContain(await MessagesAsync(page), message => message.Contains("detached", StringComparison.Ordinal));

        await page.EvaluateAsync("() => window.__orchestratorMock.setFailure('healthy')");
        await page.Locator("[data-detach-schedule='nightly_utc']").ClickAsync();
        await page.Locator(".etlsql-feedback-btn-danger").ClickAsync();
        await WaitForMessageAsync(page, "Schedule 'nightly_utc' detached from 'daily_sales'.");
        await Expect(page.Locator("#detailLinkedSchedulesWrap")).ToContainTextAsync("No shared schedules attached.");

        await page.Locator("#attachScheduleBtn").ClickAsync();
        await Expect(page.Locator("#as-select option")).ToHaveCountAsync(3);
        await page.Locator("#as-submitBtn").ClickAsync();
        await Expect(page.Locator("#as-error")).ToHaveTextAsync("Choose an entry to attach.");
        await page.Locator("#as-select").SelectOptionAsync("hourly_us");
        await page.Locator("#as-submitBtn").ClickAsync();
        await WaitForMessageAsync(page, "Schedule 'hourly_us' attached to 'daily_sales'.");
        await Expect(page.Locator("#attachScheduleModal")).ToBeHiddenAsync();
        await Expect(page.Locator("[data-detach-schedule='hourly_us']")).ToBeVisibleAsync();
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AnAttachDialogWhoseCatalogCannotBeReadOffersNothingToAttach()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenJobAsync(page, "daily_sales");
        await page.EvaluateAsync("() => window.__orchestratorMock.setFailure('catalog-unavailable')");
        await page.Locator("#attachNotificationBtn").ClickAsync();
        await Expect(page.Locator("#an-error")).ToContainTextAsync("Notifications are temporarily unavailable");
        await Expect(page.Locator("#an-submitBtn")).ToBeDisabledAsync();
        await Expect(page.Locator("#an-select")).ToBeDisabledAsync();
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task ANotificationIsAttachedForTheChosenTriggerAndDetachedForThatTrigger()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenJobAsync(page, "daily_sales");
        await page.Locator("#attachNotificationBtn").ClickAsync();
        await page.Locator("#an-select").SelectOptionAsync("oncall_chat");
        var trigger = await page.Locator("#an-trigger").InputValueAsync();
        await page.Locator("#an-submitBtn").ClickAsync();
        await WaitForMessageAsync(page, "Notification 'oncall_chat' attached to 'daily_sales'.");
        Assert.Equal(trigger, await page.EvaluateAsync<string>(
            "() => window.__orchestratorMock.calls.find(call => call.method === 'POST' && call.path.endsWith('/notifications/oncall_chat')).body.trigger"));
        await page.Locator("[data-detach-notification='ops_mail']").ClickAsync();
        await page.Locator(".etlsql-feedback-btn-danger").ClickAsync();
        await WaitForMessageAsync(page, "Notification 'ops_mail' detached from 'daily_sales'.");
        await Expect(page.Locator("[data-detach-notification='ops_mail']")).ToHaveCountAsync(0);
        await Expect(page.Locator("[data-detach-notification='oncall_chat']")).ToBeVisibleAsync();
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task WatermarksAreEditedAddedAndCleared()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenJobAsync(page, "daily_sales");
        await page.Locator("[data-state-action='edit'][data-key='last_order_id']").ClickAsync();
        await Expect(page.Locator("#es-key")).ToBeDisabledAsync();
        await Expect(page.Locator("#es-val")).ToHaveValueAsync("100520");
        await page.Locator("#es-val").FillAsync("200000");
        await page.Locator("#es-submitBtn").ClickAsync();
        await WaitForMessageAsync(page, "Watermark 'last_order_id' saved.");
        await Expect(page.Locator("#editStateModal")).ToBeHiddenAsync();
        await Expect(page.Locator("#watermarksWrap")).ToContainTextAsync("200000");
        Assert.Equal("200000", await page.EvaluateAsync<string>(
            "() => window.__orchestratorMock.calls.find(call => call.method === 'PUT' && call.path.endsWith('/state/last_order_id')).body.value"));

        await page.Locator("#addStateBtn").ClickAsync();
        await page.Locator("#es-submitBtn").ClickAsync();
        await Expect(page.Locator("#es-error")).ToHaveTextAsync("Key is required.");
        await page.Locator("#es-key").FillAsync("cursor");
        await page.Locator("#es-val").FillAsync("abc");
        await page.Locator("#es-submitBtn").ClickAsync();
        await WaitForMessageAsync(page, "Watermark 'cursor' saved.");
        await Expect(page.Locator("[data-state-action='reset'][data-key='cursor']")).ToBeVisibleAsync();

        await page.Locator("[data-state-action='reset'][data-key='last_order_id']").ClickAsync();
        await page.Locator(".etlsql-feedback-btn-danger").ClickAsync();
        await WaitForMessageAsync(page, "Watermark 'last_order_id' cleared.");
        await Expect(page.Locator("[data-key='last_order_id']")).ToHaveCountAsync(0);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task GrantsAndOwnershipCanBeAdministeredFromThePanel()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenJobAsync(page, "daily_sales");
        await page.Locator("#aclGrantBtn").ClickAsync();
        await WaitForMessageAsync(page, "Enter the principal key to grant access to.");
        await page.Locator("#aclPrincipalKind").SelectOptionAsync("USER");
        await page.Locator("#aclPrincipalKey").FillAsync("3f2a9c1d");
        await page.Locator("#aclPermission").SelectOptionAsync("MANAGE");
        await page.Locator("#aclGrantBtn").ClickAsync();
        await WaitForMessageAsync(page, "MANAGE granted to '3f2a9c1d'.");
        await Expect(page.Locator("#accessPanelWrap tbody tr")).ToHaveCountAsync(2);
        Assert.Equal("MANAGE", await page.EvaluateAsync<string>(
            "() => window.__orchestratorMock.calls.find(call => call.method === 'PUT' && call.path.endsWith('/authorization/Job/daily_sales/USER/3f2a9c1d')).body.permission"));

        await page.Locator("[data-revoke-kind='GROUP'][data-revoke-key='finance-ops']").ClickAsync();
        await page.Locator(".etlsql-feedback-btn-danger").ClickAsync();
        await WaitForMessageAsync(page, "Access revoked for 'finance-ops'.");
        await Expect(page.Locator("#accessPanelWrap tbody tr")).ToHaveCountAsync(1);
        await Expect(page.Locator("#accessPanelWrap")).Not.ToContainTextAsync("finance-ops");

        await page.Locator("#aclOwnerKey").FillAsync("new-owner");
        await page.Locator("#aclOwnerBtn").ClickAsync();
        await WaitForMessageAsync(page, "Owner of 'daily_sales' updated.");
        await Expect(page.Locator("#metaOwner")).ToHaveTextAsync("User new-owner");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task ASecondScriptSaveSendsTheVersionTheFirstSaveProduced()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenJobAsync(page, "daily_sales");
        await Expect(page.Locator("#saveScriptBtn")).ToBeHiddenAsync();
        var editor = page.Locator("#detailScriptEditor .cm-content");
        await editor.ClickAsync();
        await page.Keyboard.TypeAsync("-- first ");
        await page.Locator("#saveScriptBtn").ClickAsync();
        await WaitForMessageAsync(page, "Script saved.");
        await Expect(page.Locator("#metaVersion")).ToHaveTextAsync("v4");
        await Expect(page.Locator("#saveScriptBtn")).ToBeHiddenAsync();

        await editor.ClickAsync();
        await page.Keyboard.TypeAsync("-- second ");
        await page.Locator("#saveScriptBtn").ClickAsync();
        await Expect(page.Locator("#metaVersion")).ToHaveTextAsync("v5");
        Assert.Equal(new[] { "\"3\"", "\"4\"" }, await page.EvaluateAsync<string[]>(
            "() => window.__orchestratorMock.calls.filter(call => call.method === 'PUT' && call.path === '/api/orchestrator/jobs/daily_sales').map(call => call.ifMatch)"));
        Assert.Contains("-- first", await page.EvaluateAsync<string>(
            "() => window.__orchestratorMock.calls.find(call => call.method === 'PUT' && call.path === '/api/orchestrator/jobs/daily_sales').body.scriptText"));
        Assert.DoesNotContain(await MessagesAsync(page), message => message.StartsWith("Error", StringComparison.Ordinal));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task ARejectedScriptSaveKeepsTheDraftAndItsSaveButton()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenJobAsync(page, "daily_sales");
        await page.Locator("#detailScriptEditor .cm-content").ClickAsync();
        await page.Keyboard.TypeAsync("-- draft ");
        await page.EvaluateAsync("() => window.__orchestratorMock.setFailure('conflict')");
        await page.Locator("#saveScriptBtn").ClickAsync();
        await WaitForMessageAsync(page, "The resource changed after it was read");
        await Expect(page.Locator("#saveScriptBtn")).ToBeVisibleAsync();
        await Expect(page.Locator("#saveScriptBtn")).ToBeEnabledAsync();
        await Expect(page.Locator("#detailScriptEditor .cm-content")).ToContainTextAsync("-- draft");
        await Expect(page.Locator("#metaVersion")).ToHaveTextAsync("v3");
        Assert.DoesNotContain("Script saved.", await MessagesAsync(page));
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData("healthy", "Run #42 queued to resume.")]
    [InlineData("failed", "Resume failed: the checkpoint session expired.")]
    public async Task ResumingARunIsAnnouncedOnlyWhenItWasQueued(string scenario, string message)
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenJobAsync(page, "daily_sales");
        await page.EvaluateAsync("scenario => window.__orchestratorMock.setFailure(scenario)", scenario);
        await page.Locator("[data-resume-id='42']").ClickAsync();
        await page.Locator(".etlsql-feedback-btn-primary").ClickAsync();
        await WaitForMessageAsync(page, message);
        if (scenario != "healthy")
        {
            Assert.DoesNotContain(await MessagesAsync(page), text => text.Contains("queued", StringComparison.Ordinal));
            await Expect(page.Locator("[data-resume-id='42']")).ToBeEnabledAsync();
        }
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task TheQualityTabReportsThisJobsLatestRunAndKeepsSourcesApart()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenJobAsync(page, "daily_sales");
        await page.Locator("#detailTabQuality").ClickAsync();
        var quality = page.Locator("#qualityDetailsWrap");
        await Expect(quality).ToContainTextAsync("Rows quarantined");
        await Expect(quality.Locator(".orch-meta-card", new() { HasText = "Rows quarantined" }).Locator(".orch-meta-value")).ToHaveTextAsync("3");
        await Expect(quality.Locator(".orch-meta-card", new() { HasText = "Rows warned" }).Locator(".orch-meta-value")).ToHaveTextAsync("7");
        await Expect(quality).ToContainTextAsync("NOT NULL");
        await Expect(quality).ToContainTextAsync("orders.amount");
        await Expect(quality).Not.ToContainTextAsync("99");
        await Expect(quality).Not.ToContainTextAsync("Stewardship");

        await page.EvaluateAsync("() => window.__orchestratorMock.setFailure('quality-unavailable')");
        await page.Locator("tr.job-row[data-name='hourly_orders'] td").First.ClickAsync();
        await Expect(page.Locator("#detailJobName")).ToHaveTextAsync("hourly_orders");
        await Expect(quality.Locator("[role='alert']")).ToContainTextAsync("Quality status is unavailable.");
        await Expect(quality).ToContainTextAsync("No data quality rule violations recorded for this job.");
        await Expect(quality.Locator(".orch-meta-value")).ToHaveCountAsync(0);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task DependencyAndAuditTabsLoadForTheOpenJob()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenJobAsync(page, "daily_sales");
        await page.Locator("#detailTabDeps").ClickAsync();
        await Expect(page.Locator("#dependencyDetailsWrap")).ToContainTextAsync("Upstream providers (1)");
        await Expect(page.Locator("#dependencyDetailsWrap")).ToContainTextAsync("hourly_orders");
        await page.Locator("#detailTabAudit").ClickAsync();
        await Expect(page.Locator("#auditLogWrap")).ToContainTextAsync("JobScriptEdited");
        await Expect(page.Locator("#auditLogWrap")).ToContainTextAsync("edited daily_sales");
        await Expect(page.Locator("#detailTabAudit")).ToHaveAttributeAsync("aria-selected", "true");
        await page.Locator("#closeDetailBtn").ClickAsync();
        await Expect(page.Locator("tr.job-row.selected")).ToHaveCountAsync(0);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task LeavingThePageDiscardsPendingDetailReads()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenAsync(page);
        await page.EvaluateAsync("() => window.__orchestratorMock.pauseDetail('daily_sales')");
        await page.Locator("tr.job-row[data-name='daily_sales'] td").First.ClickAsync();
        await page.WaitForFunctionAsync("() => window.__orchestratorMock.pendingDetailReads >= 5");
        await page.EvaluateAsync("() => { window.dispatchEvent(new Event('pagehide')); window.__orchestratorMock.releaseDetail(); }");
        await page.EvaluateAsync("async () => { await new Promise(resolve => setTimeout(resolve, 50)); }");
        await Expect(page.Locator("#detailPanel")).Not.ToContainTextAsync("nightly_utc");
        await Expect(page.Locator("#detailPanel")).Not.ToContainTextAsync("finance-ops");
        Assert.Empty(await MessagesAsync(page));
        Assert.Empty(session.PageErrors);
    }

    private async Task OpenAsync(IPage page, string scenario = "healthy")
    {
        await page.GotoWithDiagnosticsAsync($"{baseUrl}/tools/ui-sandbox/orchestrator-page-frame.html?fixture={scenario}");
        await Expect(page.Locator("body")).ToHaveAttributeAsync("data-orchestrator-sandbox-ready", "true");
        await Expect(page.Locator("[data-action='toggle'][data-name='daily_sales']")).ToBeVisibleAsync();
    }

    private async Task OpenJobAsync(IPage page, string name)
    {
        await OpenAsync(page);
        await page.Locator($"tr.job-row[data-name='{name}'] td").First.ClickAsync();
        await Expect(page.Locator("#detailPanel")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("\\bopen\\b"));
        await Expect(page.Locator("#accessPanelWrap table")).ToBeVisibleAsync();
        await Expect(page.Locator("#historyTableWrap .spinner")).ToHaveCountAsync(0);
    }

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
