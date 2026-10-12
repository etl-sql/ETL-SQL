using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// ST11: a report authored in Studio is handed to a schedule and comes back as a stored snapshot.
///
/// <para>The handoff has two halves that meet on one name. An administrator registers a refresh job
/// link for the report in the Portal, which names the Orchestrator job
/// <c>portal-refresh:&lt;alias&gt;:&lt;reportId&gt;</c>. An operator schedules a job of exactly that
/// name in the Orchestrator. When an occurrence of it succeeds, the Portal's poller sees the
/// completion and refreshes the report under the scheduled identity: a trusted, non-interactive
/// execution with no user behind it. The artifact is the report's snapshot.</para>
///
/// <para>The Portal and the Orchestrator share one job-history database here, as they do in a
/// deployment where the Portal can poll it, and the scheduler runs on a test clock.</para>
/// </summary>
[Trait("Category", "Portal")]
public sealed class ReportScheduleHandoffTests : IAsyncLifetime
{
    private const string Password = "Handoff@Tests99!";
    private const string ReportScript = """
        SET REPORT TITLE = 'Scheduled Handoff';

        CREATE VISUAL ItemTable AS TABLE (
            SOURCE = (
                SELECT 1 AS Id, 'Alpha' AS Name
                UNION ALL SELECT 2 AS Id, 'Beta' AS Name
            ),
            MAPPINGS (Id = Id, Name = Name)
        );
        """;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset Start = new(2030, 1, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly OrchestratorCaller Operator =
        new("user", "handoff-operator", "operator", ["OrchestratorManager"], [], "portal-host");

    private readonly ManualClock clock = new(Start);
    private readonly PairedPortalFactory portal = new();
    private OrchestratorWebFactory orchestrator = null!;
    private HttpClient client = null!;
    private string adminToken = "";

    public async Task InitializeAsync()
    {
        // The Orchestrator is given the Portal's directory, so both use the same job-history file.
        orchestrator = new ClockedOrchestratorFactory(portal.TempDir, clock);
        portal.Orchestrator = orchestrator;
        orchestrator.CreateClient().Dispose();
        client = portal.CreateClient();

        var initial = await LoginAsync("admin", "Admin@12345!");
        using var change = Request(HttpMethod.Post, "/api/auth/change-password", initial,
            new { currentPassword = "Admin@12345!", newPassword = Password });
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(change)).StatusCode);
        adminToken = await LoginAsync("admin", Password);
    }

    public async Task DisposeAsync()
    {
        client.Dispose();
        await orchestrator.DisposeAsync();
        await portal.DisposeAsync();
    }

    [Fact]
    public async Task AStudioAuthoredReportIsRefreshedIntoASnapshotWhenItsScheduledJobRuns()
    {
        // ── Authored in Studio ──────────────────────────────────────────────────
        int folderId;
        using (var scope = portal.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var adminId = await db.Users.Where(user => user.UserName == "admin").Select(user => user.Id).SingleAsync();
            var folder = new Folder { Name = "Handoff", Path = "/Handoff", OwnerId = adminId };
            db.Folders.Add(folder);
            await db.SaveChangesAsync();
            folderId = folder.Id;
        }
        var created = await Send(HttpMethod.Post, "/api/studio/reports", adminToken,
            new { folderId, name = "Scheduled Handoff", scriptText = ReportScript, kind = "Report" });
        Assert.True(created.IsSuccessStatusCode, $"create: {(int)created.StatusCode} {await created.Content.ReadAsStringAsync()}");
        var reportId = (await Body(created))["id"]!.GetValue<int>();
        Assert.Equal(HttpStatusCode.NotFound,
            (await Send(HttpMethod.Get, $"/api/reports/{reportId}/snapshot/manifest", adminToken)).StatusCode);

        // ── Authorized handoff: only an administrator links a report to a refresh job ──
        var (_, publisherToken) = await CreateUserAsync("handoff_publisher", "Publisher");
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, "/api/subscriptions/refresh-jobs", publisherToken,
            new { reportName = "/Handoff/Scheduled Handoff", schedule = "0 2 * * *", orchestratorAlias = "orch" })).StatusCode);
        var linked = await Send(HttpMethod.Post, "/api/subscriptions/refresh-jobs", adminToken,
            new { reportName = "/Handoff/Scheduled Handoff", schedule = "0 2 * * *", orchestratorAlias = "orch" });
        Assert.True(linked.IsSuccessStatusCode, $"link: {(int)linked.StatusCode} {await linked.Content.ReadAsStringAsync()}");
        var jobName = (await Body(linked))["jobName"]!.GetValue<string>();
        Assert.Equal($"portal-refresh:orch:{reportId}", jobName);

        // ── The operator schedules a job of that name in the Orchestrator ───────
        using (var orchestratorClient = orchestrator.CreateClient())
        using (var schedule = OperatorRequest(HttpMethod.Put,
            $"/api/scheduled-jobs/{Uri.EscapeDataString(jobName)}/revision-schedule",
            new { scriptText = "PRINT 'Report refresh trigger';", cron = "0 2 * * *", timeZone = "UTC", provisionIfMissing = true }))
        {
            var response = await orchestratorClient.SendAsync(schedule);
            Assert.True(response.IsSuccessStatusCode, $"schedule: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }

        // Before the occurrence nothing has completed, so a poll refreshes nothing.
        await PollAsync();
        Assert.Empty(await ExecutionJobsAsync(reportId));

        // ── The clock reaches the occurrence; the job runs and succeeds ─────────
        clock.Set(new DateTimeOffset(2030, 1, 2, 2, 0, 5, TimeSpan.Zero));
        var run = await WaitForAsync(async () => (await HistoryAsync(jobName)).FirstOrDefault(entry => entry.EndTime is not null),
            "the scheduled job did not run");
        Assert.Equal("SUCCESS", run.Status);

        // ── The Portal sees the completion and refreshes the report ─────────────
        await PollAsync();
        var refresh = await WaitForAsync(async () =>
            (await ExecutionJobsAsync(reportId)).FirstOrDefault(job => job.Status is "Completed" or "Failed"),
            "the report was not refreshed");
        Assert.True(refresh.Status == "Completed", $"refresh {refresh.Status}: {refresh.Error}");
        Assert.Equal("Refresh", refresh.Kind);
        // The scheduled identity: no user is behind this execution.
        Assert.Equal(0, refresh.UserId);

        // ── One verified artifact: the stored snapshot ──────────────────────────
        // The job records the snapshot by its key under the Portal's snapshot root.
        Assert.False(string.IsNullOrWhiteSpace(refresh.ManifestPath));
        Assert.EndsWith(".etlsnap", refresh.ManifestPath!, StringComparison.Ordinal);
        var snapshotFile = Path.Combine(portal.TempDir, "snapshots", refresh.ManifestPath!);
        Assert.True(File.Exists(snapshotFile), $"no snapshot at {snapshotFile}");
        Assert.True(new FileInfo(snapshotFile).Length > 0);
        var manifest = await Send(HttpMethod.Get, $"/api/reports/{reportId}/snapshot/manifest", adminToken);
        Assert.Equal(HttpStatusCode.OK, manifest.StatusCode);
        Assert.Equal("Scheduled Handoff", (await Body(manifest))["title"]!.GetValue<string>());
        var rows = await Send(HttpMethod.Get, $"/api/reports/{reportId}/snapshot/rows/0", adminToken);
        Assert.Equal(HttpStatusCode.OK, rows.StatusCode);
        var rowText = await rows.Content.ReadAsStringAsync();
        Assert.Contains("Alpha", rowText);
        Assert.Contains("Beta", rowText);

        using (var scope = portal.Services.CreateScope())
        {
            var link = await scope.ServiceProvider.GetRequiredService<PortalDbContext>().ReportJobLinks
                .SingleAsync(item => item.ReportId == reportId);
            Assert.NotNull(link.LastRefreshedAt);
        }

        // One occurrence, one refresh. Each poll here is a poller starting fresh, as after a Portal
        // restart, so it looks back over the same completion and must not act on it again.
        await PollAsync();
        Assert.Single(await ExecutionJobsAsync(reportId));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task PollAsync()
    {
        var poller = ActivatorUtilities.CreateInstance<OrchestratorPollerService>(portal.Services);
        await poller.PollAsync(CancellationToken.None);
    }

    private static async Task<T> WaitForAsync<T>(Func<Task<T?>> probe, string failure) where T : class
    {
        for (var attempt = 0; attempt < 600; attempt++)
        {
            if (await probe() is { } found) return found;
            await Task.Delay(50);
        }
        throw new TimeoutException(failure);
    }

    private async Task<List<PortalExecutionJob>> ExecutionJobsAsync(int reportId)
    {
        using var scope = portal.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<PortalDbContext>().PortalExecutionJobs
            .AsNoTracking().Where(job => job.ReportId == reportId).ToListAsync();
    }

    private async Task<List<JobHistoryEntry>> HistoryAsync(string job)
    {
        using var scope = orchestrator.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IJobHistoryStore>();
        var stored = await store.GetJobAsync("portal-host", job);
        return stored is null ? [] : [.. await store.GetHistoryAsync(stored.Id, 20)];
    }

    private async Task<(int Id, string Token)> CreateUserAsync(string userName, string role)
    {
        using (var scope = portal.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<PortalUser>>();
            var user = new PortalUser
            {
                UserName = userName,
                Email = $"{userName}@test.local",
                IsActive = true,
                MustChangePassword = false,
                Provider = "Local"
            };
            Assert.True((await users.CreateAsync(user, Password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
            return (user.Id, await LoginAsync(userName, Password));
        }
    }

    private async Task<string> LoginAsync(string username, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>(Json))!["token"]!.GetValue<string>();
    }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, string token, object? body = null)
    {
        using var request = Request(method, path, token, body);
        return await client.SendAsync(request);
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }

    private static HttpRequestMessage OperatorRequest(HttpMethod method, string path, object body)
    {
        var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Orchestrator-Key", "test-orch-key-12345");
        request.Headers.Add(OrchestratorIdentityAssertion.HeaderName,
            OrchestratorIdentityAssertion.Create(Operator, OrchestratorWebFactory.IdentitySecret));
        return request;
    }

    private static async Task<JsonObject> Body(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonObject>(Json))!;

    private sealed class PairedPortalFactory : PortalWebFactory
    {
        public OrchestratorWebFactory? Orchestrator { get; set; }

        protected override void CustomizePortalConfig(PortalConfig config)
        {
            config.Orchestrator.ApiUrl = "http://orchestrator.test";
            config.Orchestrator.ApiKey = "test-orch-key-12345";
            config.Orchestrator.IdentitySigningSecret = OrchestratorWebFactory.IdentitySecret;
        }

        protected override void CustomizeServices(IServiceCollection services) =>
            services.AddHttpClient<OrchestratorProxyService>()
                .ConfigurePrimaryHttpMessageHandler(() => Orchestrator!.Server.CreateHandler());
    }

    private sealed class ClockedOrchestratorFactory(string sharedDirectory, ManualClock clock)
        : OrchestratorWebFactory(sharedDirectory, requireFederatedIdentity: true)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock);
            });
        }
    }
}
