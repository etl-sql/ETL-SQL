using ETL_SQL.Core.Data;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// The Orchestrator page's catalogs, create dialog, detail panel, and ownership dialog, driven
/// through the native Portal against the signed Orchestrator API. Each result is read back from the
/// Orchestrator's own store, so a control that only looked as if it worked would fail here.
/// </summary>
public sealed partial class ScheduledJobActivityBrowserTests
{
    private IJobCatalogStore Catalog => Store;

    [Fact]
    public async Task SchedulesNotificationsAndANewJobPersistInTheOrchestrator()
    {
        await using var session = await browser.NewSessionAsync();
        var page = session.Page;
        await browser.SignInAsync(page);
        await OpenAsync(page);

        await page.Locator("#orchNavSchedules").ClickAsync();
        await page.Locator("#newScheduleBtn").ClickAsync();
        await page.Locator("#cs-name").FillAsync("native_nightly");
        await page.Locator("#cs-cron").FillAsync("0 2 * * *");
        await page.Locator("#cs-saveBtn").ClickAsync();
        await Expect(page.Locator("#scheduleModal")).ToBeHiddenAsync();
        await Expect(page.Locator("tr[data-schedule-name='native_nightly']")).ToContainTextAsync("0 2 * * *");
        var schedule = Assert.IsType<ScheduleDefinition>(await Catalog.GetScheduleAsync(Tenant, "native_nightly"));
        Assert.Equal("0 2 * * *", schedule.Cron);
        Assert.True(schedule.IsEnabled);

        var toggle = page.Locator("[data-action='toggle-schedule'][data-name='native_nightly']");
        await toggle.ClickAsync();
        await Expect(toggle).ToHaveTextAsync("Enable");
        Assert.False((await Catalog.GetScheduleAsync(Tenant, "native_nightly"))!.IsEnabled);

        await page.Locator("#orchNavNotifications").ClickAsync();
        await page.Locator("#newNotificationBtn").ClickAsync();
        await page.Locator("#cn-name").FillAsync("native_ops");
        await page.Locator("#cn-connection").FillAsync("smtp_ops");
        await page.Locator("#cn-recipient").FillAsync("ops@example.test");
        await page.Locator("#cn-saveBtn").ClickAsync();
        await Expect(page.Locator("#notificationModal")).ToBeHiddenAsync();
        await Expect(page.Locator("tr[data-notification-name='native_ops']")).ToContainTextAsync("smtp_ops");
        Assert.Equal("ops@example.test", (await Catalog.GetNotificationAsync(Tenant, "native_ops"))!.Recipient);
        await Expect(page.Locator("#schedulesNavCount")).ToHaveTextAsync("1");
        await Expect(page.Locator("#notificationsNavCount")).ToHaveTextAsync("1");

        await page.Locator("#orchNavJobs").ClickAsync();
        await page.Locator("#newJobBtn").ClickAsync();
        await page.Locator("#cj-name").FillAsync("native_job");
        await page.Locator("#cj-script-path").FillAsync("native.etlsql");
        await page.Locator("#cj-sched-mode-shared").CheckAsync();
        await page.Locator("#cj-shared-schedule-select").SelectOptionAsync("native_nightly");
        await page.Locator("#cj-saveBtn").ClickAsync();
        await Expect(page.Locator("#createJobModal")).ToBeHiddenAsync();
        await Expect(Rows(page).Filter(new() { HasTextString = "native_job" })).ToHaveCountAsync(1);
        var job = Assert.IsType<JobDefinition>(await Store.GetJobAsync(Tenant, "native_job"));
        Assert.Equal(schedule.Id, Assert.Single(await Catalog.GetJobSchedulesAsync(job.Id)).ScheduleId);

        // A job on its own interval keeps the interval it was given; the service once stored every one as hourly.
        await page.Locator("#newJobBtn").ClickAsync();
        await page.Locator("#cj-name").FillAsync("native_two_daily");
        await page.Locator("#cj-script-path").FillAsync("native.etlsql");
        await page.Locator("#cj-interval").FillAsync("2");
        await page.Locator("#cj-unit").SelectOptionAsync("DAY");
        await page.Locator("#cj-at-time").FillAsync("03:30");
        await page.Locator("#cj-saveBtn").ClickAsync();
        await Expect(page.Locator("#createJobModal")).ToBeHiddenAsync();
        await Expect(Rows(page).Filter(new() { HasTextString = "native_two_daily" })).ToHaveCountAsync(1);
        var interval = Assert.IsType<JobDefinition>(await Store.GetJobAsync(Tenant, "native_two_daily"));
        Assert.Equal((2, "DAY", "03:30"), (interval.Interval, interval.Unit, interval.AtTime));

        // A schedule a job still runs on cannot be deleted; the refusal leaves it in place.
        await page.Locator("#orchNavSchedules").ClickAsync();
        await page.Locator("[data-action='delete-schedule'][data-name='native_nightly']").ClickAsync();
        await page.Locator(".etlsql-feedback-btn-danger").ClickAsync();
        await Expect(page.GetByText("Cannot delete schedule 'native_nightly'").First).ToBeVisibleAsync();
        await Expect(page.Locator("tr[data-schedule-name='native_nightly']")).ToHaveCountAsync(1);
        Assert.NotNull(await Catalog.GetScheduleAsync(Tenant, "native_nightly"));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AJobsDetailPanelWritesReachTheOrchestrator()
    {
        var job = await AddJobAsync("native_detail");
        await Catalog.SaveScheduleAsync(new ScheduleDefinition(Name: "detail_hourly", Cron: "0 * * * *", TimeZone: "UTC",
            CreatedBy: "user:fixture-owner", TenantId: Tenant));
        var schedule = (await Catalog.GetScheduleAsync(Tenant, "detail_hourly"))!;
        await using var session = await browser.NewSessionAsync();
        var page = session.Page;
        await browser.SignInAsync(page);
        await OpenAsync(page);
        await Rows(page).Filter(new() { HasTextString = "native_detail" }).Locator("td").First.ClickAsync();
        await Expect(page.Locator("#detailJobName")).ToHaveTextAsync("native_detail");
        await Expect(page.Locator("#detailLinkedSchedulesWrap")).ToContainTextAsync("No shared schedules attached.");
        await Expect(page.Locator("#historyTableWrap")).ToContainTextAsync("No execution history recorded yet.");

        await page.Locator("#attachScheduleBtn").ClickAsync();
        await page.Locator("#as-select").SelectOptionAsync("detail_hourly");
        await page.Locator("#as-submitBtn").ClickAsync();
        await Expect(page.Locator("[data-detach-schedule='detail_hourly']")).ToBeVisibleAsync();
        Assert.Equal(schedule.Id, Assert.Single(await Catalog.GetJobSchedulesAsync(job.Id)).ScheduleId);

        await page.Locator("#addStateBtn").ClickAsync();
        await page.Locator("#es-key").FillAsync("cursor");
        await page.Locator("#es-val").FillAsync("42");
        await page.Locator("#es-submitBtn").ClickAsync();
        await Expect(page.Locator("#editStateModal")).ToBeHiddenAsync();
        await Expect(page.Locator("#watermarksWrap")).ToContainTextAsync("cursor");
        Assert.Equal("42", Assert.Single(await Store.GetJobStatesAsync(job.Id), state => state.StateKey == "cursor").StateValue);

        // The access panel administers the Orchestrator's own grants; a reload shows what was stored.
        await page.Locator("#aclPrincipalKind").SelectOptionAsync("GROUP");
        await page.Locator("#aclPrincipalKey").FillAsync("native-ops");
        await page.Locator("#aclPermission").SelectOptionAsync("EXECUTE");
        await page.Locator("#aclGrantBtn").ClickAsync();
        await Expect(page.Locator("#accessPanelWrap tbody")).ToContainTextAsync("native-ops");

        // Two saves in one session: the second must carry the version the first produced.
        var version = (await Store.GetJobAsync(Tenant, "native_detail"))!.Version;
        var editor = page.Locator("#detailScriptEditor .cm-content");
        await editor.ClickAsync();
        await page.Keyboard.TypeAsync("-- first edit\n");
        await page.Locator("#saveScriptBtn").ClickAsync();
        await Expect(page.Locator("#metaVersion")).ToHaveTextAsync($"v{version + 1}");
        await editor.ClickAsync();
        await page.Keyboard.TypeAsync("-- second edit\n");
        await page.Locator("#saveScriptBtn").ClickAsync();
        await Expect(page.Locator("#metaVersion")).ToHaveTextAsync($"v{version + 2}");
        var saved = (await Store.GetJobAsync(Tenant, "native_detail"))!;
        Assert.Contains("-- first edit", saved.Script);
        Assert.Contains("-- second edit", saved.Script);

        await page.ReloadAsync();
        await Expect(page.Locator("#statusLabel")).ToHaveTextAsync("Online");
        await Rows(page).Filter(new() { HasTextString = "native_detail" }).Locator("td").First.ClickAsync();
        await Expect(page.Locator("#accessPanelWrap tbody")).ToContainTextAsync("native-ops");
        await Expect(page.Locator("#watermarksWrap")).ToContainTextAsync("cursor");

        await page.Locator("[data-detach-schedule='detail_hourly']").ClickAsync();
        await page.Locator(".etlsql-feedback-btn-danger").ClickAsync();
        await Expect(page.Locator("#detailLinkedSchedulesWrap")).ToContainTextAsync("No shared schedules attached.");
        Assert.Empty(await Catalog.GetJobSchedulesAsync(job.Id));
        AssertClean(session);
    }

    [Fact]
    public async Task AnAdministratorAssignsAnOwnerToObjectsThatHaveNone()
    {
        await Store.SaveJobAsync(new JobDefinition("native_orphan", "SELECT 1;", 0, "HOUR", null, null, null, true, TenantId: Tenant));
        await AddJobAsync("native_owned");
        await using var session = await browser.NewSessionAsync();
        var page = session.Page;
        await browser.SignInAsync(page);
        await OpenAsync(page);
        var opener = page.Locator("#unownedBtn");
        await Expect(opener).ToHaveTextAsync("Unowned objects (1)");
        await opener.ClickAsync();
        await Expect(page.Locator("#unownedList tr")).ToHaveCountAsync(1);
        await Expect(page.Locator("#unownedList")).ToContainTextAsync("native_orphan");
        await page.Locator("#unownedOwnerKind").SelectOptionAsync("SERVICE");
        await page.Locator("#unownedOwnerKey").FillAsync("etl-runner");
        await page.Locator("#unownedAdoptBtn").ClickAsync();
        await page.Locator(".etlsql-feedback-btn-primary").ClickAsync();
        await Expect(page.Locator("#unownedList")).ToContainTextAsync("Every job, schedule, and notification has a recorded owner.");
        Assert.Equal("service:etl-runner", (await Store.GetJobAsync(Tenant, "native_orphan"))!.CreatedBy);
        Assert.Equal("user:fixture-owner", (await Store.GetJobAsync(Tenant, "native_owned"))!.CreatedBy);
        AssertClean(session);
    }
}
