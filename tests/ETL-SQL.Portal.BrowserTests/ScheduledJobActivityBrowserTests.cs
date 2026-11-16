using ETL_SQL.Core.Data;
using ETL_SQL.Core.Profiling;
using ETL_SQL.Core.Quality;
using ETL_SQL.Orchestrator.Storage;
using ETL_SQL.Portal;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using ETL_SQL.Portal.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Playwright;
using Xunit.Abstractions;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ScheduledJobActivityBrowserCollection
{
    public const string Name = "scheduled-job-activity";
}

/// <summary>Native Portal controls against the signed Portal-to-Orchestrator API and real stored evidence.</summary>
[Trait("Category", "Browser")]
[Collection(ScheduledJobActivityBrowserCollection.Name)]
public sealed partial class ScheduledJobActivityBrowserTests : IAsyncLifetime
{
    private static readonly DateTimeOffset AsOf = DateTimeOffset.Parse("2026-10-07T12:00:00Z");
    private const string Tenant = "portal-host";
    private const string LeaseOwner = "browser-fixture";
    private readonly ActivityOrchestratorFactory orchestrator = new();
    private readonly PortalBrowserFixture browser;
    private readonly ITestOutputHelper output;
    private SQLiteJobHistoryStore Store => orchestrator.Services.GetRequiredService<SQLiteJobHistoryStore>();

    public ScheduledJobActivityBrowserTests(ITestOutputHelper output)
    {
        this.output = output;
        browser = new(new ActivityPortalFactory(orchestrator));
    }

    public async Task InitializeAsync()
    {
        using var client = orchestrator.CreateClient();
        await Store.InitializeAsync();
        await browser.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await browser.DisposeAsync();
        await orchestrator.DisposeAsync();
    }

    [Fact]
    public async Task NativeChipsUseStoredOutcomesAndRefreshTheSelectedFilter()
    {
        var running = await SeedAsync();
        await using var session = await browser.NewSessionAsync();
        var page = session.Page;
        await browser.SignInAsync(page);
        await OpenAsync(page);
        await Expect(Rows(page)).ToHaveCountAsync(6);
        await Expect(page.Locator("#activeCount")).ToHaveTextAsync("1");
        await Expect(page.Locator("#queuedCount")).ToHaveTextAsync("1");
        await Expect(page.Locator("#completedCount")).ToHaveTextAsync("2");
        await Expect(page.Locator("#failedCount")).ToHaveTextAsync("2");
        var imageDirectory = Path.GetFullPath(Path.Combine(
            browser.Factory.Services.GetRequiredService<IWebHostEnvironment>().ContentRootPath,
            "..", "..", "artifacts", "section1", "qa"));
        Directory.CreateDirectory(imageDirectory);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(imageDirectory, "orchestrator-activity-portal.png"),
            FullPage = true
        });

        await page.Locator("#activeFilterBtn").ClickAsync();
        await Expect(Rows(page)).ToHaveCountAsync(1);
        await Expect(Rows(page)).ToContainTextAsync("activity-running-00");
        await Expect(page.Locator("#activeFilterBtn")).ToHaveAttributeAsync("aria-pressed", "true");
        await page.Locator("#queuedFilterBtn").FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Expect(Rows(page)).ToHaveCountAsync(1);
        await Expect(Rows(page)).ToContainTextAsync("activity-queued");
        await page.Locator("#completedTodayFilterBtn").ClickAsync();
        await Expect(Rows(page)).ToHaveCountAsync(2);
        await Expect(Rows(page).Filter(new() { HasTextString = "activity-idle" })).ToHaveCountAsync(0);
        await page.Locator("#failedTodayFilterBtn").ClickAsync();
        await Expect(Rows(page)).ToHaveCountAsync(2);
        await page.Locator("#jobsSearchInput").FillAsync("activity-retry");
        await Expect(Rows(page)).ToHaveCountAsync(1);
        await Expect(page.Locator("#failedCount")).ToHaveTextAsync("2");
        await page.Locator("#clearJobsFilterBtn").ClickAsync();
        await Expect(page.Locator("#jobsSectionKicker")).ToHaveTextAsync("All jobs");
        await Expect(Rows(page)).ToHaveCountAsync(1);
        await page.Locator("#jobsSearchClearBtn").ClickAsync();
        await Expect(page.Locator("#jobsSearchInput")).ToBeFocusedAsync();
        await Expect(Rows(page)).ToHaveCountAsync(6);

        await page.Locator("#activeFilterBtn").ClickAsync();
        await Expect(Rows(page)).ToHaveCountAsync(1);
        await Store.ReleaseJobLeaseAsync(running.Id, LeaseOwner);
        await page.Locator("#refreshJobsBtn").ClickAsync();
        await Expect(Rows(page)).ToHaveCountAsync(0);
        await Expect(page.Locator("#activeCount")).ToHaveTextAsync("0");
        await Expect(page.Locator("#activeFilterBtn")).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(page.Locator("#jobsTableWrap")).ToContainTextAsync("No jobs match");
        AssertClean(session);
    }

    [Fact]
    public async Task TriageUsesRealDurableEvidenceAndReportsPartialProxyReruns()
    {
        await AddJobAsync("triage-quality");
        await AddJobAsync("triage-running");
        using var scope = browser.Factory.Services.CreateScope();
        var evidenceStore = scope.ServiceProvider.GetRequiredService<IJobHistoryStore>();
        async Task<long> StartEvidenceAsync(string name)
        {
            await evidenceStore.SaveJobAsync(new JobDefinition(name, "SELECT 1;", 0, "HOUR", null, null, null, false, TenantId: Tenant));
            var job = (await evidenceStore.GetJobAsync(Tenant, name))!;
            return await evidenceStore.LogJobStartAsync(job.Id);
        }
        var running = await StartEvidenceAsync("triage-running");
        var quality = await StartEvidenceAsync("triage-quality");
        await evidenceStore.SaveJobStatementMetricsAsync(quality,
        [new StatementMetricsPayload { Statement = "SELECT * FROM source WHERE value = ?", DurationMs = 412, RowsProcessed = 93, Failed = true }]);
        await evidenceStore.SaveJobDataQualityFailuresAsync(quality,
        [new DataQualityRuleFailureMetric("warehouse.Customers", "Email", "not-null", "QUARANTINE", 3, "Data Steward")]);
        await evidenceStore.LogJobEndAsync(quality, "FAILED", "quality gate <failed>", rowsProcessed: 93,
            scriptHashAtRunTime: "sha256:runtime", hashMatched: false, rowsQuarantined: 3,
            dataQualityFailures: "Email:not-null=3");
        var missing = await StartEvidenceAsync("triage-missing");
        await evidenceStore.LogJobEndAsync(missing, "FAILED", "job removed after failure");

        await using var session = await browser.NewSessionAsync();
        var page = session.Page;
        await browser.SignInAsync(page);
        await OpenAsync(page);
        var root = page.Locator("#triageBoard");
        await Expect(root.Locator(".triage-incident")).ToHaveCountAsync(2);
        await Expect(root).ToContainTextAsync("triage-running");
        var qualityIncident = root.Locator(".triage-incident").Filter(new() { HasTextString = "quality gate <failed>" });
        await qualityIncident.Locator(".triage-incident-toggle").ClickAsync();
        await root.Locator($"[data-run='{quality}']").ClickAsync();
        await Expect(root.Locator(".triage-evidence-row")).ToContainTextAsync("MISMATCH");
        await Expect(root.Locator(".triage-evidence-row")).ToContainTextAsync("Statement timeline (1)");
        await Expect(root.Locator(".triage-evidence-row")).ToContainTextAsync("SELECT * FROM source WHERE value = ?");
        await Expect(root.Locator(".triage-evidence-row")).ToContainTextAsync("Quality failures (1)");
        await Expect(root.Locator(".triage-evidence-row")).ToContainTextAsync("Data Steward");
        await Expect(root.Locator(".triage-evidence-row a")).ToHaveAttributeAsync("href",
            "/index.html?jobName=triage-quality&q=warehouse.Customers#governance/quarantine");
        await root.Locator(".triage-incident-check").Nth(0).CheckAsync();
        await root.Locator(".triage-incident-check").Nth(1).CheckAsync();
        await root.Locator(".triage-rerun-selected").ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Re-run jobs", Exact = true }).ClickAsync();
        await Expect(page.GetByText("1 of 2 jobs triggered. Not triggered: triage-missing.", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(qualityIncident.Locator(".triage-incident-check")).Not.ToBeCheckedAsync();
        var missingIncident = root.Locator(".triage-incident").Filter(new() { HasTextString = "job removed after failure" });
        await Expect(missingIncident.Locator(".triage-incident-check")).ToBeCheckedAsync();

        await evidenceStore.LogJobEndAsync(quality, "SUCCESS");
        await evidenceStore.LogJobEndAsync(missing, "SUCCESS");
        await page.Locator("#triageRefreshBtn").ClickAsync();
        await Expect(root).ToContainTextAsync("Nothing to triage");
        await Expect(root).ToContainTextAsync("triage-running");
        await Expect(root).ToContainTextAsync("In flight (1)");
        await evidenceStore.LogJobEndAsync(running, "SUCCESS");
        AssertClean(session);
    }

    [Fact]
    public async Task SelectedChipUsesServerPaginationAndStatusWithGlobalCounts()
    {
        await SeedAsync(runningJobs: 30);
        await using var session = await browser.NewSessionAsync();
        var page = session.Page;
        await browser.SignInAsync(page);
        await OpenAsync(page);
        await Expect(page.Locator("#activeCount")).ToHaveTextAsync("30");
        await page.Locator("#activeFilterBtn").ClickAsync();
        await Expect(Rows(page)).ToHaveCountAsync(25);
        await Expect(page.Locator("#jobsPaginationInfo")).ToHaveTextAsync("Showing 1–25 of 30 jobs");
        await page.Locator("#jobsNextPageBtn").ClickAsync();
        await Expect(Rows(page)).ToHaveCountAsync(5);
        await Expect(Rows(page).First).ToContainTextAsync("activity-running-25");
        await Expect(page.Locator("#jobsPageIndicator")).ToHaveTextAsync("2 / 2");
        await page.Locator("#jobsPrevPageBtn").ClickAsync();
        await Expect(Rows(page)).ToHaveCountAsync(25);
        await page.Locator("#jobsPageSizeSelect").SelectOptionAsync("50");
        await Expect(Rows(page)).ToHaveCountAsync(30);
        await page.Locator("#jobsStatusSelect").SelectOptionAsync("disabled");
        await Expect(Rows(page)).ToHaveCountAsync(0);
        await Expect(page.Locator("#activeCount")).ToHaveTextAsync("30");
        await page.Locator("#clearJobsFilterBtn").ClickAsync();
        await Expect(Rows(page)).ToHaveCountAsync(1);
        await Expect(Rows(page)).ToContainTextAsync("activity-idle");
        AssertClean(session);
    }

    [Fact]
    public async Task ReadGrantsLimitRowsAndCountsAndRevocationClearsThePage()
    {
        await SeedAsync();
        string readerId;
        using (var scope = browser.Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
            var reader = new PortalUser
            {
                UserName = "activity-reader",
                Email = "activity-reader@test.local",
                IsActive = true,
                MustChangePassword = false,
                Provider = "Local"
            };
            var created = await users.CreateAsync(reader, "Activity@Reader99!");
            Assert.True(created.Succeeded, string.Join(", ", created.Errors.Select(error => error.Description)));
            Assert.True((await users.AddToRoleAsync(reader, "OrchestratorViewer")).Succeeded);
            readerId = reader.PrincipalKey!;
            Assert.False(string.IsNullOrWhiteSpace(readerId));
        }
        var complete = (await Store.GetJobAsync(Tenant, "activity-complete"))!;
        await Store.SaveObjectGrantAsync(new OrchestratorObjectGrant(complete.Id.Value,
            OrchestratorObjectKind.Job, OrchestratorPrincipalKind.User, readerId,
            OrchestratorObjectPermission.Read, "user:admin"));
        await using var session = await browser.NewSessionAsync();
        var page = session.Page;
        await page.GotoWithDiagnosticsAsync("/login.html");
        await page.FillAsync("#username", "activity-reader");
        await page.FillAsync("#password", "Activity@Reader99!");
        await page.ClickAsync("#loginBtn");
        await page.WaitForURLWithDiagnosticsAsync("**/index.html");
        await OpenAsync(page);
        await Expect(Rows(page)).ToHaveCountAsync(1);
        await Expect(Rows(page)).ToContainTextAsync("activity-complete");
        await Expect(page.Locator("#activeCount")).ToHaveTextAsync("0");
        await Expect(page.Locator("#queuedCount")).ToHaveTextAsync("0");
        await Expect(page.Locator("#completedCount")).ToHaveTextAsync("1");
        await Expect(page.Locator("#failedCount")).ToHaveTextAsync("0");
        await page.Locator("#completedTodayFilterBtn").ClickAsync();
        await Expect(Rows(page)).ToHaveCountAsync(1);
        Assert.True(await Store.DeleteObjectGrantAsync(complete.Id.Value, OrchestratorPrincipalKind.User, readerId));
        await page.Locator("#refreshJobsBtn").ClickAsync();
        await Expect(Rows(page)).ToHaveCountAsync(0);
        await Expect(page.Locator("#completedCount")).ToHaveTextAsync("0");
        await Expect(page.Locator("#completedTodayFilterBtn")).ToHaveAttributeAsync("aria-pressed", "true");
        AssertClean(session);
    }

    [Fact]
    public async Task AutomaticRefreshRetainsKeyboardFocusInTheJobTable()
    {
        await SeedAsync();
        await using var session = await browser.NewSessionAsync();
        var page = session.Page;
        await browser.SignInAsync(page);
        await OpenAsync(page);
        await Expect(Rows(page)).ToHaveCountAsync(6);
        var run = page.Locator("#jobsTableWrap [data-action=trigger][data-name=activity-running-00]");
        await run.FocusAsync();
        await page.WaitForResponseAsync(response => response.Url.Contains("/jobs/activity?")
            && response.Url.Contains("limit=25") && response.Ok,
            new PageWaitForResponseOptions { Timeout = 15_000 });
        await Expect(run).ToBeFocusedAsync();
        AssertClean(session);
    }

    [Fact]
    public async Task ConnectionLossClearsStaleActivityAndPollingRestoresTheCatalog()
    {
        await SeedAsync();
        await using var session = await browser.NewSessionAsync();
        var page = session.Page;
        await browser.SignInAsync(page);
        await OpenAsync(page);
        await Expect(Rows(page)).ToHaveCountAsync(6);
        await Expect(page.Locator("#jobsNavCount")).ToHaveTextAsync("6");
        await page.Context.SetOfflineAsync(true);
        await Expect(page.Locator("#statusLabel")).ToHaveTextAsync("Offline",
            new LocatorAssertionsToHaveTextOptions { Timeout = 15_000 });
        await Expect(page.Locator("#activeCount")).ToHaveTextAsync("—",
            new LocatorAssertionsToHaveTextOptions { Timeout = 15_000 });
        await Expect(Rows(page)).ToHaveCountAsync(0);
        await Expect(page.Locator("#jobsNavCount")).ToHaveTextAsync("—");
        await page.Context.SetOfflineAsync(false);
        await Expect(page.Locator("#statusLabel")).ToHaveTextAsync("Online",
            new LocatorAssertionsToHaveTextOptions { Timeout = 15_000 });
        await Expect(page.Locator("#activeCount")).ToHaveTextAsync("1",
            new LocatorAssertionsToHaveTextOptions { Timeout = 15_000 });
        await Expect(Rows(page)).ToHaveCountAsync(6);
        await Expect(page.Locator("#jobsNavCount")).ToHaveTextAsync("6");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task UnavailableEvidenceClearsRowsAndCountsAndRefreshRecovers()
    {
        await SeedAsync();
        await using var session = await browser.NewSessionAsync();
        var page = session.Page;
        await browser.SignInAsync(page);
        await OpenAsync(page);
        await Expect(Rows(page)).ToHaveCountAsync(6);
        var job = (await Store.GetJobAsync(Tenant, "activity-idle"))!;
        var history = await AddOutcomeAsync(job, "SUCCESS", "2026-10-07Tinvalid");
        await page.Locator("#refreshJobsBtn").ClickAsync();
        await Expect(page.Locator("#jobsTableWrap [role=alert]")).ToBeVisibleAsync();
        await Expect(Rows(page)).ToHaveCountAsync(0);
        foreach (var id in new[] { "activeCount", "queuedCount", "completedCount", "failedCount", "jobsNavCount" })
            await Expect(page.Locator($"#{id}")).ToHaveTextAsync("—");
        Assert.Contains(session.FailedRequests, request => request.Contains("503 GET") && request.Contains("/jobs/activity"));
        Assert.Empty(session.PageErrors);
        await SetOutcomeAsync(history, "SUCCESS", "2026-10-06T23:59:59Z");
        await page.Locator("#refreshJobsBtn").ClickAsync();
        await Expect(Rows(page)).ToHaveCountAsync(6);
        await Expect(page.Locator("#jobsNavCount")).ToHaveTextAsync("6");
        await Expect(page.Locator("#activeCount")).ToHaveTextAsync("1");
        await Expect(page.Locator("#completedCount")).ToHaveTextAsync("2");
        Assert.Empty(session.PageErrors);
    }

    private static ILocator Rows(IPage page) => page.Locator("#jobsTableWrap tr.job-row");

    private static async Task OpenAsync(IPage page)
    {
        await page.GotoWithDiagnosticsAsync("/orchestrator.html");
        await Expect(page.Locator("#statusLabel")).ToHaveTextAsync("Online");
        await Expect(page.Locator("#jobsTableWrap")).ToHaveAttributeAsync("aria-busy", "false");
    }

    private void AssertClean(BrowserSession session)
    {
        if (session.PageErrors.Count != 0 || session.ConsoleErrors.Count != 0 || session.FailedRequests.Count != 0)
        {
            output.WriteLine("Page errors:{0}{1}", Environment.NewLine, string.Join(Environment.NewLine, session.PageErrors));
            output.WriteLine("Console errors:{0}{1}", Environment.NewLine, string.Join(Environment.NewLine, session.ConsoleErrors));
            output.WriteLine("Failed HTTP requests:{0}{1}", Environment.NewLine, string.Join(Environment.NewLine, session.FailedRequests));
        }
        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
        Assert.Empty(session.FailedRequests);
    }

    private async Task<JobDefinition> SeedAsync(int runningJobs = 1)
    {
        JobDefinition? first = null;
        for (var index = 0; index < runningJobs; index++)
        {
            var job = await AddJobAsync($"activity-running-{index:00}", ScheduledJobRunState.Running);
            first ??= job;
        }
        await AddJobAsync("activity-queued", ScheduledJobRunState.Queued);
        var complete = await AddJobAsync("activity-complete");
        await AddOutcomeAsync(complete, "SUCCESS", "2026-10-06T20:00:00-04:00");
        await AddOutcomeAsync(complete, "SUCCESS", "2026-10-07T03:00:00Z");
        await AddOutcomeAsync(await AddJobAsync("activity-failed"), "FAILURE", "2026-10-07T02:00:00Z");
        var retry = await AddJobAsync("activity-retry");
        await AddOutcomeAsync(retry, "BLOCKED", "2026-10-07T04:00:00Z");
        await AddOutcomeAsync(retry, "SUCCESS", "2026-10-07T05:00:00Z");
        var idle = await AddJobAsync("activity-idle", enabled: false);
        await Store.SaveJobAsync(idle with { LastRun = AsOf.UtcDateTime });
        await AddJobAsync("activity-running-00", ScheduledJobRunState.Running, tenant: "other-tenant");
        return first!;
    }

    private async Task<JobDefinition> AddJobAsync(string name,
        ScheduledJobRunState state = ScheduledJobRunState.Idle, bool enabled = true, string tenant = Tenant)
    {
        await Store.SaveJobAsync(new JobDefinition(name, "SELECT 1;", 0, "HOUR", null, null, null, enabled,
            CreatedBy: "user:fixture-owner", TenantId: tenant));
        var job = (await Store.GetJobAsync(tenant, name))!;
        if (state != ScheduledJobRunState.Idle)
        {
            var fence = (await Store.AcquireJobLeaseAsync(job.Id, LeaseOwner, TimeSpan.FromDays(3650)))!.Value;
            Assert.True(await Store.TrySetScheduledJobRunStateAsync(job.Id, LeaseOwner, fence, state));
        }
        return job;
    }

    private async Task<long> AddOutcomeAsync(JobDefinition job, string status, string ended)
    {
        var history = await Store.LogJobStartAsync(job.Id);
        await SetOutcomeAsync(history, status, ended);
        return history;
    }

    private async Task SetOutcomeAsync(long history, string status, string ended)
    {
        using var connection = new SqliteConnection($"Data Source={Path.Combine(orchestrator.TempDir, "etlsql.db")}");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE job_history SET status = $status, end_time = $ended WHERE id = $id";
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$ended", ended);
        command.Parameters.AddWithValue("$id", history);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class ActivityOrchestratorFactory() : OrchestratorWebFactory(requireFederatedIdentity: true)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new ActivityClock());
            });
        }
    }

    private sealed class ActivityClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => AsOf;
    }

    private sealed class ActivityPortalFactory(OrchestratorWebFactory orchestrator) : PortalBrowserFactory
    {
        protected override void CustomizePortalConfig(PortalConfig config)
        {
            config.Orchestrator.ApiUrl = "http://orchestrator.test";
            config.Orchestrator.ApiKey = "test-orch-key-12345";
            config.Orchestrator.IdentitySigningSecret = OrchestratorWebFactory.IdentitySecret;
        }

        protected override void CustomizeServices(IServiceCollection services) =>
            services.AddHttpClient<OrchestratorProxyService>()
                .ConfigurePrimaryHttpMessageHandler(() => orchestrator.Server.CreateHandler());
    }
}
