using ETL_SQL.Orchestrator.Channels;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ETL_SQL.ReportPortal.Tests;

[Trait("Category", "Portal")]
public class ExecutionJobServiceTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), $"exec_job_svc_{Guid.NewGuid():N}");

    public ExecutionJobServiceTests()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, "scripts"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "snapshots"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "datasets"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>
    /// Regression for the queued-timeout leak: a job whose timeout fires while it is still
    /// waiting for the concurrency gate must reach a terminal state, clear its refresh
    /// debounce entry, and leave the gate usable — not stay Pending and block the report forever.
    /// </summary>
    [Fact]
    public async Task RefreshTimedOutWhileQueued_ReachesTerminalStateAndFreesGateAndDebounce()
    {
        var scriptPath = Path.Combine(_tempDir, "scripts", "report.rptsql");
        await File.WriteAllTextAsync(scriptPath, "PRINT 'never executed';");

        var config = new PortalConfig
        {
            DatabasePath = Path.Combine(_tempDir, "portal.db"),
            ScriptRootPath = Path.Combine(_tempDir, "scripts"),
            SnapshotDirectory = Path.Combine(_tempDir, "snapshots"),
            DatasetRootPath = Path.Combine(_tempDir, "datasets"),
            Resources = new ResourcesConfig
            {
                MaxConcurrentReportExecutions = 1,
                ExecutionTimeoutSeconds = 1
            }
        };

        var scopeFactory = new ServiceCollection()
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();
        var sessions = new SessionCache(config, scopeFactory, NullLogger<SessionCache>.Instance);
        // A remote channel that never responds keeps the first job inside the gate until its
        // timeout, forcing the second job to time out while still queued behind the gate.
        var channel = new HttpJobChannelClient(
            new HttpClient(new NeverRespondingHandler()) { BaseAddress = new Uri("http://localhost:9") },
            NullLogger<HttpJobChannelClient>.Instance);
        using var service = new ExecutionJobService(
            config, scopeFactory, NullLogger<ExecutionJobService>.Instance, sessions, channel);

        var first = await service.EnqueueRefreshAsync(reportId: 1, userId: 7, scriptPath);
        var second = await service.EnqueueRefreshAsync(reportId: 2, userId: 7, scriptPath);

        await WaitForTerminalAsync(service, first);
        await WaitForTerminalAsync(service, second);

        await WaitForNoActiveRefreshAsync(service, 1);
        await WaitForNoActiveRefreshAsync(service, 2);

        // The debounce entry must be gone (a new job id is issued) and the gate must still
        // have capacity (the new job also runs to a terminal state instead of queueing forever).
        var third = await service.EnqueueRefreshAsync(reportId: 1, userId: 7, scriptPath);
        Assert.NotEqual(first, third);
        await WaitForTerminalAsync(service, third);
    }

    /// <summary>
    /// Regression for unbounded job-table growth: terminal jobs older than the retention
    /// window are evicted on the next enqueue, while recent terminal jobs stay queryable.
    /// </summary>
    [Fact]
    public async Task Enqueue_EvictsTerminalJobsPastRetention_KeepsRecentOnes()
    {
        var scriptPath = Path.Combine(_tempDir, "scripts", "report.rptsql");
        await File.WriteAllTextAsync(scriptPath, "PRINT 'never executed';");

        var config = new PortalConfig
        {
            DatabasePath = Path.Combine(_tempDir, "portal.db"),
            ScriptRootPath = Path.Combine(_tempDir, "scripts"),
            SnapshotDirectory = Path.Combine(_tempDir, "snapshots"),
            DatasetRootPath = Path.Combine(_tempDir, "datasets"),
            Resources = new ResourcesConfig
            {
                MaxConcurrentReportExecutions = 1,
                ExecutionTimeoutSeconds = 1
            }
        };

        var scopeFactory = new ServiceCollection()
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();
        var sessions = new SessionCache(config, scopeFactory, NullLogger<SessionCache>.Instance);
        var channel = new HttpJobChannelClient(
            new HttpClient(new NeverRespondingHandler()) { BaseAddress = new Uri("http://localhost:9") },
            NullLogger<HttpJobChannelClient>.Instance);
        using var service = new ExecutionJobService(
            config, scopeFactory, NullLogger<ExecutionJobService>.Instance, sessions, channel);

        var old = await service.EnqueueRefreshAsync(reportId: 1, userId: 7, scriptPath);
        await WaitForTerminalAsync(service, old);
        (await service.GetAsync(old))!.CompletedAt =
            DateTime.UtcNow - ExecutionJobService.CompletedJobRetention - TimeSpan.FromMinutes(1);

        var recent = await service.EnqueueRefreshAsync(reportId: 2, userId: 7, scriptPath);
        await WaitForTerminalAsync(service, recent);

        var trigger = await service.EnqueueRefreshAsync(reportId: 3, userId: 7, scriptPath);

        Assert.Null(await service.GetAsync(old));        // past retention — evicted
        Assert.NotNull(await service.GetAsync(recent));  // recent terminal job — still queryable
        await WaitForTerminalAsync(service, trigger);
    }

    /// <summary>
    /// Regression for unbounded snapshot accumulation: pruning keeps the newest
    /// SnapshotRetentionPerReport rows (and their manifest files) for the report and leaves
    /// other reports' snapshots untouched.
    /// </summary>
    [Fact]
    public async Task PruneSnapshots_KeepsNewestPerReport_DeletesRowsAndFiles()
    {
        var snapshotDir = Path.Combine(_tempDir, "snapshots");
        var config = new PortalConfig
        {
            DatabasePath = Path.Combine(_tempDir, "portal.db"),
            ScriptRootPath = Path.Combine(_tempDir, "scripts"),
            SnapshotDirectory = snapshotDir,
            DatasetRootPath = Path.Combine(_tempDir, "datasets"),
            Resources = new ResourcesConfig { SnapshotRetentionPerReport = 3 }
        };

        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlite($"Data Source={config.DatabasePath}")
            .Options;
        using var db = new PortalDbContext(options);
        db.Database.EnsureCreated();

        var folder = new Folder { Name = "f", Path = "/f" };
        var report = new Report { Folder = folder, Name = "r", ScriptPath = "r.rptsql" };
        var other = new Report { Folder = folder, Name = "o", ScriptPath = "o.rptsql" };
        db.AddRange(folder, report, other);
        await db.SaveChangesAsync();

        string AddSnapshot(Report r, int age)
        {
            var path = Path.Combine(snapshotDir, $"report_{r.Name}_{age}.snapshot.json");
            File.WriteAllText(path, "{}");
            db.ReportSnapshots.Add(new ReportSnapshot
            {
                Report = r,
                ManifestPath = path,
                BuiltAt = DateTime.UtcNow.AddMinutes(-age)
            });
            return path;
        }

        var reportFiles = Enumerable.Range(0, 5).Select(age => AddSnapshot(report, age)).ToList();
        var otherFiles = Enumerable.Range(0, 2).Select(age => AddSnapshot(other, age)).ToList();
        await db.SaveChangesAsync();

        var scopeFactory = new ServiceCollection()
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();
        var sessions = new SessionCache(config, scopeFactory, NullLogger<SessionCache>.Instance);
        var channel = new HttpJobChannelClient(
            new HttpClient(new NeverRespondingHandler()) { BaseAddress = new Uri("http://localhost:9") },
            NullLogger<HttpJobChannelClient>.Instance);
        using var service = new ExecutionJobService(
            config, scopeFactory, NullLogger<ExecutionJobService>.Instance, sessions, channel);

        await service.PruneSnapshotsAsync(db, report.Id);

        var remaining = await db.ReportSnapshots.Where(s => s.ReportId == report.Id).ToListAsync();
        Assert.Equal(3, remaining.Count);
        Assert.All(remaining, s => Assert.True(File.Exists(s.ManifestPath)));

        // The two oldest (ages 3, 4) are gone — rows and files.
        Assert.False(File.Exists(reportFiles[3]));
        Assert.False(File.Exists(reportFiles[4]));

        // The other report is untouched.
        Assert.Equal(2, await db.ReportSnapshots.CountAsync(s => s.ReportId == other.Id));
        Assert.All(otherFiles, f => Assert.True(File.Exists(f)));
    }

    [Fact]
    [Trait("CompatBreak", "0.12")]
    public async Task StartAsync_AllowsMultiplePortalInstancesUsingSharedState()
    {
        var (config, provider) = CreatePersistentServices();
        await using var services = provider;
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();
        var sessions = new SessionCache(config, scopes, NullLogger<SessionCache>.Instance);
        var channel = new HttpJobChannelClient(
            new HttpClient(new NeverRespondingHandler()) { BaseAddress = new Uri("http://localhost:9") },
            NullLogger<HttpJobChannelClient>.Instance);
        using var first = new ExecutionJobService(
            config, scopes, NullLogger<ExecutionJobService>.Instance, sessions, channel);
        using var second = new ExecutionJobService(
            config, scopes, NullLogger<ExecutionJobService>.Instance, sessions, channel);

        await first.StartAsync(CancellationToken.None);
        await second.StartAsync(CancellationToken.None);

        await second.StopAsync(CancellationToken.None);
        await first.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_MarksAbandonedJobsAndReportRefreshAsInterrupted()
    {
        var (config, provider) = CreatePersistentServices();
        await using var services = provider;
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();
        int reportId;
        const string jobId = "abandoned-job";
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var folder = new Folder { Name = "restart", Path = "/restart" };
            var report = new Report
            {
                Folder = folder,
                Name = "restart report",
                ScriptPath = "restart.rptsql",
                LastRefreshStatus = "Running",
                LastRefreshStartedAt = DateTime.UtcNow.AddMinutes(-2)
            };
            db.AddRange(folder, report);
            await db.SaveChangesAsync();
            reportId = report.Id;
            db.PortalExecutionJobs.Add(new PortalExecutionJob
            {
                Id = jobId,
                ReportId = reportId,
                UserId = 1,
                Kind = "Refresh",
                Status = "Running",
                StartedAt = report.LastRefreshStartedAt
            });
            await db.SaveChangesAsync();
        }

        var sessions = new SessionCache(config, scopes, NullLogger<SessionCache>.Instance);
        var channel = new HttpJobChannelClient(
            new HttpClient(new NeverRespondingHandler()) { BaseAddress = new Uri("http://localhost:9") },
            NullLogger<HttpJobChannelClient>.Instance);
        using var service = new ExecutionJobService(
            config, scopes, NullLogger<ExecutionJobService>.Instance, sessions, channel);

        await service.StartAsync(CancellationToken.None);

        var job = await service.GetAsync(jobId);
        Assert.NotNull(job);
        Assert.Equal(JobStatus.Cancelled, job.Status);
        Assert.Contains("interrupted", job.Error!, StringComparison.OrdinalIgnoreCase);

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var reportState = await verifyDb.Reports.FindAsync(reportId);
        Assert.Equal("Cancelled", reportState!.LastRefreshStatus);
        Assert.Contains("interrupted", reportState.LastRefreshError!, StringComparison.OrdinalIgnoreCase);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task NodeLeaseLoss_CancelsLocalRunningJobs()
    {
        var scriptPath = await HangScriptAsync();
        using var service = HangingService(FairnessConfig(globalCap: 1, perUserCap: 1, timeoutSeconds: 30));

        var jobId = await service.EnqueueExecutionAsync(reportId: 1, userId: 7, scriptPath);
        await WaitForRunningCountAsync(service, 1, jobId);

        await service.OnNodeLeaseLostAsync(
            "portal-node-a",
            "Portal",
            "Node heartbeat lease expired.",
            CancellationToken.None);

        await WaitForTerminalAsync(service, jobId);
        var job = await service.GetAsync(jobId);
        Assert.Equal(JobStatus.Cancelled, job!.Status);
        Assert.Contains("lease", job.Error!, StringComparison.OrdinalIgnoreCase);
    }

    private (PortalConfig Config, ServiceProvider Provider) CreatePersistentServices()
    {
        var config = new PortalConfig
        {
            DatabasePath = Path.Combine(_tempDir, "portal.db"),
            ScriptRootPath = Path.Combine(_tempDir, "scripts"),
            SnapshotDirectory = Path.Combine(_tempDir, "snapshots"),
            DatasetRootPath = Path.Combine(_tempDir, "datasets"),
            Resources = new ResourcesConfig
            {
                MaxConcurrentReportExecutions = 1,
                ExecutionTimeoutSeconds = 1
            }
        };
        var services = new ServiceCollection()
            .AddDbContext<PortalDbContext>(options =>
                options.UseSqlite($"Data Source={config.DatabasePath}"))
            .BuildServiceProvider();
        using var scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<PortalDbContext>().Database.EnsureCreated();
        return (config, services);
    }

    // ── Workload fairness (P2.6) ──────────────────────────────────────────────────
    // The NeverRespondingHandler channel makes any job that gets past the gates hang (Running)
    // until its timeout, so the gate states are directly observable.

    /// <summary>With a per-user cap below the global cap, a user who floods the queue cannot take
    /// every slot — another user still gets one.</summary>
    [Fact]
    public async Task PerUserLimit_OneUserCannotStarveAnother()
    {
        var scriptPath = await HangScriptAsync();
        using var service = HangingService(FairnessConfig(globalCap: 2, perUserCap: 1));

        var a1 = await service.EnqueueExecutionAsync(reportId: 1, userId: 7, scriptPath);
        var a2 = await service.EnqueueExecutionAsync(reportId: 2, userId: 7, scriptPath);
        var b1 = await service.EnqueueExecutionAsync(reportId: 3, userId: 8, scriptPath);

        await WaitForRunningCountAsync(service, 2, a1, a2, b1);

        // User B runs; user A holds exactly one of the two slots, never both.
        Assert.Equal(JobStatus.Running, (await service.GetAsync(b1))!.Status);
        var a1Running = (await service.GetAsync(a1))!.Status == JobStatus.Running;
        var a2Running = (await service.GetAsync(a2))!.Status == JobStatus.Running;
        Assert.True(a1Running ^ a2Running, "user A should hold exactly one execution slot");

        await WaitForTerminalAsync(service, a1);
        await WaitForTerminalAsync(service, a2);
        await WaitForTerminalAsync(service, b1);
    }

    /// <summary>Contrast: with the per-user cap at the global cap (no fairness), one user's two
    /// jobs take the whole pool and the other user is starved — what the limit prevents.</summary>
    [Fact]
    public async Task WithoutPerUserLimit_OneUserSaturatesTheWholePool()
    {
        var scriptPath = await HangScriptAsync();
        using var service = HangingService(FairnessConfig(globalCap: 2, perUserCap: 2));

        var a1 = await service.EnqueueExecutionAsync(reportId: 1, userId: 7, scriptPath);
        var a2 = await service.EnqueueExecutionAsync(reportId: 2, userId: 7, scriptPath);
        var b1 = await service.EnqueueExecutionAsync(reportId: 3, userId: 8, scriptPath);

        await WaitForRunningCountAsync(service, 2, a1, a2, b1);

        Assert.Equal(JobStatus.Running, (await service.GetAsync(a1))!.Status);
        Assert.Equal(JobStatus.Running, (await service.GetAsync(a2))!.Status);
        Assert.NotEqual(JobStatus.Running, (await service.GetAsync(b1))!.Status); // queued, starved

        await WaitForTerminalAsync(service, a1);
        await WaitForTerminalAsync(service, a2);
        await WaitForTerminalAsync(service, b1);
    }

    /// <summary>Administrators are exempt from the per-user cap (the administrative override).</summary>
    [Fact]
    public async Task Administrator_BypassesPerUserLimit()
    {
        var scriptPath = await HangScriptAsync();
        using var service = HangingService(FairnessConfig(globalCap: 2, perUserCap: 1));

        var j1 = await service.EnqueueExecutionAsync(reportId: 1, userId: 9, scriptPath, isAdministrator: true);
        var j2 = await service.EnqueueExecutionAsync(reportId: 2, userId: 9, scriptPath, isAdministrator: true);

        await WaitForRunningCountAsync(service, 2, j1, j2);
        Assert.Equal(JobStatus.Running, (await service.GetAsync(j1))!.Status);
        Assert.Equal(JobStatus.Running, (await service.GetAsync(j2))!.Status);

        await WaitForTerminalAsync(service, j1);
        await WaitForTerminalAsync(service, j2);
    }

    /// <summary>With a per-group cap below the global cap, members of one busy group cannot
    /// consume every slot — another group still gets one.</summary>
    [Fact]
    public async Task PerGroupLimit_OneGroupCannotStarveAnother()
    {
        var (config, provider) = await CreateGroupFairnessServicesAsync(globalCap: 2, perUserCap: 2, perGroupCap: 1);
        await using var services = provider;
        var scriptPath = await HangScriptAsync();
        using var service = HangingService(config, provider.GetRequiredService<IServiceScopeFactory>());

        var sharedA = await service.EnqueueExecutionAsync(reportId: 1, userId: 7, scriptPath);
        var sharedB = await service.EnqueueExecutionAsync(reportId: 2, userId: 8, scriptPath);
        var other = await service.EnqueueExecutionAsync(reportId: 3, userId: 9, scriptPath);

        await WaitForRunningCountAsync(service, 2, sharedA, sharedB, other);

        Assert.Equal(JobStatus.Running, (await service.GetAsync(other))!.Status);
        var sharedARunning = (await service.GetAsync(sharedA))!.Status == JobStatus.Running;
        var sharedBRunning = (await service.GetAsync(sharedB))!.Status == JobStatus.Running;
        Assert.True(sharedARunning ^ sharedBRunning, "the shared group should hold exactly one execution slot");

        await WaitForTerminalAsync(service, sharedA);
        await WaitForTerminalAsync(service, sharedB);
        await WaitForTerminalAsync(service, other);
    }

    /// <summary>Administrators are exempt from both user and group caps.</summary>
    [Fact]
    public async Task Administrator_BypassesGroupLimit()
    {
        var (config, provider) = await CreateGroupFairnessServicesAsync(globalCap: 2, perUserCap: 1, perGroupCap: 1);
        await using var services = provider;
        var scriptPath = await HangScriptAsync();
        using var service = HangingService(config, provider.GetRequiredService<IServiceScopeFactory>());

        var j1 = await service.EnqueueExecutionAsync(reportId: 1, userId: 7, scriptPath, isAdministrator: true);
        var j2 = await service.EnqueueExecutionAsync(reportId: 2, userId: 7, scriptPath, isAdministrator: true);

        await WaitForRunningCountAsync(service, 2, j1, j2);
        Assert.Equal(JobStatus.Running, (await service.GetAsync(j1))!.Status);
        Assert.Equal(JobStatus.Running, (await service.GetAsync(j2))!.Status);

        await WaitForTerminalAsync(service, j1);
        await WaitForTerminalAsync(service, j2);
    }

    private async Task<string> HangScriptAsync()
    {
        var scriptPath = Path.Combine(_tempDir, "scripts", "report.rptsql");
        await File.WriteAllTextAsync(scriptPath, "PRINT 'hangs in the channel';");
        return scriptPath;
    }

    private PortalConfig FairnessConfig(int globalCap, int perUserCap, int timeoutSeconds = 2, int perGroupCap = 0) => new()
    {
        DatabasePath = Path.Combine(_tempDir, "portal.db"),
        ScriptRootPath = Path.Combine(_tempDir, "scripts"),
        SnapshotDirectory = Path.Combine(_tempDir, "snapshots"),
        DatasetRootPath = Path.Combine(_tempDir, "datasets"),
        Resources = new ResourcesConfig
        {
            MaxConcurrentReportExecutions = globalCap,
            MaxConcurrentExecutionsPerUser = perUserCap,
            MaxConcurrentExecutionsPerGroup = perGroupCap,
            ExecutionTimeoutSeconds = timeoutSeconds
        }
    };

    private async Task<(PortalConfig Config, ServiceProvider Provider)> CreateGroupFairnessServicesAsync(
        int globalCap,
        int perUserCap,
        int perGroupCap)
    {
        var config = FairnessConfig(globalCap, perUserCap, timeoutSeconds: 2, perGroupCap: perGroupCap);
        var services = new ServiceCollection()
            .AddDbContext<PortalDbContext>(options =>
                options.UseSqlite($"Data Source={config.DatabasePath}"))
            .BuildServiceProvider();

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        await db.Database.EnsureCreatedAsync();

        var shared = new Group { Name = "shared-exec-group" };
        var other = new Group { Name = "other-exec-group" };
        db.Groups.AddRange(shared, other);
        db.Users.AddRange(
            new PortalUser { Id = 7, UserName = "shared-a", NormalizedUserName = "SHARED-A" },
            new PortalUser { Id = 8, UserName = "shared-b", NormalizedUserName = "SHARED-B" },
            new PortalUser { Id = 9, UserName = "other-a", NormalizedUserName = "OTHER-A" });
        await db.SaveChangesAsync();

        db.UserGroups.AddRange(
            new UserGroup { UserId = 7, GroupId = shared.Id },
            new UserGroup { UserId = 8, GroupId = shared.Id },
            new UserGroup { UserId = 9, GroupId = other.Id });
        await db.SaveChangesAsync();

        return (config, services);
    }

    private static ExecutionJobService HangingService(PortalConfig config, IServiceScopeFactory? scopeFactory = null)
    {
        scopeFactory ??= new ServiceCollection().BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();
        var sessions = new SessionCache(config, scopeFactory, NullLogger<SessionCache>.Instance);
        var channel = new HttpJobChannelClient(
            new HttpClient(new NeverRespondingHandler()) { BaseAddress = new Uri("http://localhost:9") },
            NullLogger<HttpJobChannelClient>.Instance);
        return new ExecutionJobService(
            config, scopeFactory, NullLogger<ExecutionJobService>.Instance, sessions, channel);
    }

    private static async Task<int> RunningCountAsync(ExecutionJobService service, params string[] jobIds)
    {
        var count = 0;
        foreach (var id in jobIds)
            if ((await service.GetAsync(id))!.Status == JobStatus.Running)
                count++;
        return count;
    }

    private static async Task WaitForRunningCountAsync(
        ExecutionJobService service, int expected, params string[] jobIds)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(1500);
        while (DateTime.UtcNow < deadline)
        {
            if (await RunningCountAsync(service, jobIds) == expected) return;
            await Task.Delay(25);
        }
        Assert.Fail($"Expected {expected} running job(s) within the observation window.");
    }

    private static async Task WaitForTerminalAsync(ExecutionJobService service, string jobId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var job = await service.GetAsync(jobId);
            Assert.NotNull(job);
            if (job.Status is JobStatus.Cancelled or JobStatus.Failed or JobStatus.Completed)
                return;
            await Task.Delay(50);
        }

        Assert.Fail(
            $"Job {jobId} did not reach a terminal state within 15s " +
            $"(status={(await service.GetAsync(jobId))?.Status}).");
    }

    private static async Task WaitForNoActiveRefreshAsync(ExecutionJobService service, int reportId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (await service.GetActiveRefreshJobIdAsync(reportId) is null)
                return;

            await Task.Delay(25);
        }

        Assert.Null(await service.GetActiveRefreshJobIdAsync(reportId));
    }

    private sealed class NeverRespondingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new OperationCanceledException(cancellationToken);
        }
    }
}
