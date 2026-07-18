using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Storage;
using ETL_SQL.Orchestrator.Channels;
using ETL_SQL.Orchestrator.Scheduling;
using ETL_SQL.Orchestrator.Storage;
using ETL_SQL.Reporting;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ETL_SQL.Portal.Tests;

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
    public async Task StartAsync_MarksReportInterrupted_WhenJobPersistedBeforeRunningStatus()
    {
        var (config, provider) = CreatePersistentServices();
        await using var services = provider;
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();
        int reportId;
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var folder = new Folder { Name = "restart-race", Path = "/restart-race" };
            var report = new Report { Folder = folder, Name = "restart race", ScriptPath = "restart-race.rptsql" };
            db.AddRange(folder, report);
            await db.SaveChangesAsync();
            reportId = report.Id;
            db.PortalExecutionJobs.Add(new PortalExecutionJob
            {
                Id = "abandoned-race-job",
                ReportId = reportId,
                UserId = 1,
                Kind = "Refresh",
                Status = "Running",
                StartedAt = DateTime.UtcNow.AddSeconds(-1)
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

        await using var verifyScope = provider.CreateAsyncScope();
        var reportState = await verifyScope.ServiceProvider.GetRequiredService<PortalDbContext>()
            .Reports.FindAsync(reportId);
        Assert.Equal("Cancelled", reportState!.LastRefreshStatus);
        Assert.Contains("interrupted", reportState.LastRefreshError!, StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public async Task PartitionRecovery_CancelsLocalWork_AndFencesStaleArtifactWriter()
    {
        var scriptPath = await HangScriptAsync();
        using var service = HangingService(FairnessConfig(globalCap: 1, perUserCap: 1, timeoutSeconds: 30));
        var jobId = await service.EnqueueExecutionAsync(reportId: 1, userId: 7, scriptPath);
        await WaitForRunningCountAsync(service, 1, jobId);

        var store = new SQLiteJobHistoryStore(Path.Combine(_tempDir, "partition-fence.db"));
        await store.InitializeAsync();
        await store.SaveJobAsync(new JobDefinition(
            "partition-job",
            "SELECT 1;",
            1,
            "DAY",
            "06:00",
            null,
            null,
            true));

        var staleToken = await store.AcquireJobLeaseAsync(
            "partition-job", "node-a", TimeSpan.FromMilliseconds(40));
        Assert.NotNull(staleToken);
        await Task.Delay(120); // flaky-delay-ok: wall-clock wait for the 40 ms lease to expire
        var freshToken = await store.AcquireJobLeaseAsync(
            "partition-job", "node-b", TimeSpan.FromMinutes(5));
        Assert.NotNull(freshToken);
        Assert.True(freshToken > staleToken);

        await service.OnNodeLeaseLostAsync(
            "node-a",
            "Portal",
            "Simulated partition: node heartbeat lease expired.",
            CancellationToken.None);
        await WaitForTerminalAsync(service, jobId);
        var job = await service.GetAsync(jobId);
        Assert.Equal(JobStatus.Cancelled, job!.Status);
        Assert.Contains("lease", job.Error!, StringComparison.OrdinalIgnoreCase);

        var inner = new InMemoryArtifactStorage();
        var freshWriter = new FencedArtifactStorage(inner, store, () => freshToken.Value);
        await freshWriter.WriteAllTextAsync(ArtifactArea.Snapshots, "partition/report.json", "fresh");

        var staleWriter = new FencedArtifactStorage(inner, store, () => staleToken.Value);
        await Assert.ThrowsAsync<FencedWriteException>(() =>
            staleWriter.WriteAllTextAsync(ArtifactArea.Snapshots, "partition/report.json", "stale"));
        Assert.Equal("fresh", await inner.ReadAllTextAsync(ArtifactArea.Snapshots, "partition/report.json"));
    }

    [Fact]
    public async Task CompletedRemoteExecution_PersistsResourceMetrics()
    {
        var scriptPath = Path.Combine(_tempDir, "scripts", "metrics.rptsql");
        await File.WriteAllTextAsync(scriptPath, "SET REPORT TITLE = 'Metrics';");
        var (config, provider) = CreatePersistentServices();
        await using var services = provider;
        var activities = new ConcurrentBag<Activity>();
        using var listener = CapturePortalActivities(activities);

        int reportId;
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var folder = new Folder { Name = "metrics", Path = "/metrics", OwnerId = 1 };
            var report = new Report
            {
                Folder = folder,
                Name = "Metrics Report",
                ScriptPath = scriptPath,
                CreatedBy = 1
            };
            db.AddRange(folder, report);
            await db.SaveChangesAsync();
            reportId = report.Id;
        }

        var scopes = services.GetRequiredService<IServiceScopeFactory>();
        var sessions = new SessionCache(config, scopes, NullLogger<SessionCache>.Instance);
        using var service = new ExecutionJobService(
            config,
            scopes,
            NullLogger<ExecutionJobService>.Instance,
            sessions,
            new HttpJobChannelClient(
                new HttpClient(new CompletedMetricJobHandler()) { BaseAddress = new Uri("http://orchestrator.test") },
                NullLogger<HttpJobChannelClient>.Instance),
            artifacts: new InMemoryArtifactStorage(),
            capacityMonitor: new MutableCapacityMonitor(isOverloaded: false));

        var jobId = await service.EnqueueExecutionAsync(
            reportId,
            userId: 7,
            scriptPath,
            correlationId: "corr-observe-1");
        await WaitForTerminalAsync(service, jobId);

        var job = await service.GetAsync(jobId);
        Assert.Equal(JobStatus.Completed, job!.Status);
        Assert.Equal(1234, job.RowsProcessed);
        Assert.Equal(987654321, job.PeakMemoryBytes);
        Assert.Equal(12.5, job.CpuTimeSeconds);

        await using var verifyScope = services.CreateAsyncScope();
        var stored = await verifyScope.ServiceProvider.GetRequiredService<PortalDbContext>()
            .PortalExecutionJobs
            .AsNoTracking()
            .SingleAsync(value => value.Id == jobId);
        Assert.Equal(1234, stored.RowsProcessed);
        Assert.Equal(987654321, stored.PeakMemoryBytes);
        Assert.Equal(12.5, stored.CpuTimeSeconds);

        var activity = Assert.Single(activities, value => value.DisplayName == "portal.execution_job");
        Assert.Equal("corr-observe-1", Tag(activity, PortalObservability.Tags.CorrelationId));
        Assert.Equal(jobId, Tag(activity, PortalObservability.Tags.JobId));
        Assert.Equal(reportId.ToString(), Tag(activity, PortalObservability.Tags.ReportId));
        Assert.Equal("7", Tag(activity, PortalObservability.Tags.UserId));
        Assert.Equal("Interactive", Tag(activity, PortalObservability.Tags.WorkloadKind));
        Assert.Equal("Completed", Tag(activity, PortalObservability.Tags.Status));
        Assert.Equal("1234", Tag(activity, PortalObservability.Tags.RowsProcessed));
        Assert.Equal("987654321", Tag(activity, PortalObservability.Tags.PeakMemoryBytes));
        Assert.StartsWith("sha256:", Tag(activity, PortalObservability.Tags.ScriptHash));
    }

    private static ActivityListener CapturePortalActivities(ConcurrentBag<Activity> activities)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == PortalObservability.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => activities.Add(activity)
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static string Tag(Activity activity, string key) =>
        activity.TagObjects.Single(tag => tag.Key == key).Value?.ToString() ?? "";

    private (PortalConfig Config, ServiceProvider Provider) CreatePersistentServices()
    {
        var config = new PortalConfig
        {
            DatabasePath = Path.Combine(_tempDir, "portal.db"),
            ScriptRootPath = Path.Combine(_tempDir, "scripts"),
            SnapshotDirectory = Path.Combine(_tempDir, "snapshots"),
            DatasetRootPath = Path.Combine(_tempDir, "datasets"),
            Dataset = new DatasetConfig
            {
                AtRestKey = HostedPortalFactory.DefaultAtRestKey,
                AtRestKeyVersion = "v1"
            },
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

    /// <summary>Interactive executions and refresh jobs share the same global pool and quota
    /// gates: a busy group gets one slot, another group still gets one, and duplicate refresh
    /// requests debounce to the existing refresh job instead of adding pressure to the queue.</summary>
    [Fact]
    public async Task MixedExecutionAndRefresh_RespectGroupQuotaAndRefreshDebounce()
    {
        var (config, provider) = await CreateGroupFairnessServicesAsync(globalCap: 2, perUserCap: 2, perGroupCap: 1);
        await using var services = provider;
        var scriptPath = await HangScriptAsync();
        using var service = HangingService(config, provider.GetRequiredService<IServiceScopeFactory>());

        var sharedRefresh = await service.EnqueueRefreshAsync(reportId: 1, userId: 7, scriptPath);
        var duplicateRefresh = await service.EnqueueRefreshAsync(reportId: 1, userId: 7, scriptPath);
        var sharedExecution = await service.EnqueueExecutionAsync(reportId: 2, userId: 8, scriptPath);
        var otherRefresh = await service.EnqueueRefreshAsync(reportId: 3, userId: 9, scriptPath);

        Assert.Equal(sharedRefresh, duplicateRefresh);

        await WaitForRunningCountAsync(service, 2, sharedRefresh, sharedExecution, otherRefresh);

        Assert.Equal(JobStatus.Running, (await service.GetAsync(otherRefresh))!.Status);
        var sharedRefreshRunning = (await service.GetAsync(sharedRefresh))!.Status == JobStatus.Running;
        var sharedExecutionRunning = (await service.GetAsync(sharedExecution))!.Status == JobStatus.Running;
        Assert.True(sharedRefreshRunning ^ sharedExecutionRunning,
            "the shared group should hold exactly one slot across refresh and execution work");

        await WaitForTerminalAsync(service, sharedRefresh);
        await WaitForTerminalAsync(service, sharedExecution);
        await WaitForTerminalAsync(service, otherRefresh);
    }

    /// <summary>When the global pool is saturated, queued interactive and refresh work is admitted
    /// by the configured weight cycle. With the default 2:1 weighting, two interactive jobs get the
    /// next two turns before the queued refresh.</summary>
    [Fact]
    public async Task WeightedQueue_AdmitsInteractiveAndRefreshByConfiguredWeights()
    {
        var scriptPath = await HangScriptAsync();
        var handler = new RecordingControlledCompletionHandler();
        using var service = HangingService(
            FairnessConfig(globalCap: 1, perUserCap: 4, timeoutSeconds: 10),
            httpHandler: handler);

        await service.EnqueueRefreshAsync(reportId: 1, userId: 7, scriptPath);
        await handler.WaitForSubmittedCountAsync(1);

        var queuedRefresh = await service.EnqueueRefreshAsync(reportId: 2, userId: 7, scriptPath);
        var firstInteractive = await service.EnqueueExecutionAsync(reportId: 3, userId: 8, scriptPath);
        var secondInteractive = await service.EnqueueExecutionAsync(reportId: 4, userId: 9, scriptPath);

        handler.ReleaseFirstJob();
        await handler.WaitForSubmittedCountAsync(4, TimeSpan.FromSeconds(5));

        Assert.Equal([1, 3, 4, 2], handler.SubmittedReportIds.Take(4).ToArray());
        await WaitForTerminalAsync(service, queuedRefresh);
        await WaitForTerminalAsync(service, firstInteractive);
        await WaitForTerminalAsync(service, secondInteractive);
    }

    /// <summary>An overloaded Portal node leaves work pending instead of consuming execution
    /// slots; once capacity recovers, the same queued job can start.</summary>
    [Fact]
    public async Task OverloadedNode_WaitsBeforeStartingExecution()
    {
        var scriptPath = await HangScriptAsync();
        var capacity = new MutableCapacityMonitor(isOverloaded: true);
        using var service = HangingService(
            FairnessConfig(globalCap: 1, perUserCap: 1, timeoutSeconds: 4),
            capacityMonitor: capacity);

        var jobId = await service.EnqueueExecutionAsync(reportId: 1, userId: 7, scriptPath);

        await AssertStatusForAsync(service, jobId, JobStatus.Pending, TimeSpan.FromMilliseconds(600));

        capacity.IsOverloaded = false;
        await WaitForRunningCountAsync(service, 1, jobId);

        await WaitForTerminalAsync(service, jobId);
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

    private static ExecutionJobService HangingService(
        PortalConfig config,
        IServiceScopeFactory? scopeFactory = null,
        INodeCapacityMonitor? capacityMonitor = null,
        HttpMessageHandler? httpHandler = null)
    {
        scopeFactory ??= new ServiceCollection().BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();
        var sessions = new SessionCache(config, scopeFactory, NullLogger<SessionCache>.Instance);
        var channel = new HttpJobChannelClient(
            new HttpClient(httpHandler ?? new NeverRespondingHandler()) { BaseAddress = new Uri("http://localhost:9") },
            NullLogger<HttpJobChannelClient>.Instance);
        return new ExecutionJobService(
            config, scopeFactory, NullLogger<ExecutionJobService>.Instance, sessions, channel,
            capacityMonitor: capacityMonitor ?? new MutableCapacityMonitor(isOverloaded: false));
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
        // Generous deadline so the assertion is about the steady-state outcome, not how fast jobs
        // reach Running. It returns the instant the count matches, so a slow CI box only ever waits
        // longer when something is genuinely wrong (a tight 1.5s window flaked under parallel load).
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (await RunningCountAsync(service, jobIds) == expected) return;
            await Task.Delay(25);
        }
        Assert.Fail($"Expected {expected} running job(s) within the observation window.");
    }

    private static async Task WaitForTerminalAsync(ExecutionJobService service, string jobId)
    {
        // Generous deadline: a queued job (held behind a per-user cap) only times out after the job
        // ahead of it does, and on a saturated CI box timer callbacks/continuations are delayed, so a
        // 2s logical timeout can take noticeably longer to fully unwind. Returns the instant the job
        // is terminal, so the normal case is unaffected.
        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < deadline)
        {
            var job = await service.GetAsync(jobId);
            Assert.NotNull(job);
            if (job.Status is JobStatus.Cancelled or JobStatus.Failed or JobStatus.Completed)
                return;
            await Task.Delay(50);
        }

        Assert.Fail(
            $"Job {jobId} did not reach a terminal state within 45s " +
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

    private static async Task AssertStatusForAsync(
        ExecutionJobService service,
        string jobId,
        JobStatus expected,
        TimeSpan duration)
    {
        var deadline = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < deadline)
        {
            var job = await service.GetAsync(jobId);
            Assert.NotNull(job);
            Assert.Equal(expected, job.Status);
            await Task.Delay(25);
        }
    }

    private sealed class MutableCapacityMonitor(bool isOverloaded) : INodeCapacityMonitor
    {
        public bool IsOverloaded { get; set; } = isOverloaded;

        public NodeCapacitySnapshot Capture() => new(
            WorkingSetBytes: 1,
            GcHeapBytes: 1,
            TotalAvailableMemoryBytes: 100,
            MemoryLoadPercent: IsOverloaded ? 96 : 10,
            ProcessCpuPercent: IsOverloaded ? 96 : 10,
            ProcessorCount: 1,
            IsOverloaded: IsOverloaded,
            CapturedAtUtc: DateTime.UtcNow);
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

    private sealed class CompletedMetricJobHandler : HttpMessageHandler
    {
        private static readonly string ManifestJson = System.Text.Json.JsonSerializer.Serialize(new ReportManifest
        {
            Source = "metrics.rptsql",
            Title = "Metrics",
            Telemetry = new TelemetryManifest { RowsProcessed = 1234, ExecutionTimeMs = 250 }
        });

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/jobs")
            {
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.Accepted)
                {
                    Content = JsonContent.Create(new { jobId = "remote-metrics" })
                });
            }

            if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath == "/jobs/remote-metrics")
            {
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new ETL_SQL.Orchestrator.Channels.JobStatusResponse
                    {
                        JobId = "remote-metrics",
                        Status = JobRunStatus.Completed,
                        RowsProcessed = 1234,
                        ExecutionTimeMs = 250,
                        PeakMemoryBytes = 987654321,
                        CpuTimeSeconds = 12.5,
                        ReportManifestJson = ManifestJson
                    })
                });
            }

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        }
    }

    private sealed class RecordingControlledCompletionHandler : HttpMessageHandler
    {
        private readonly object _sync = new();
        private readonly List<int> _submittedReportIds = new();
        private readonly TaskCompletionSource _releaseFirstJob =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _nextRemoteId;

        public IReadOnlyList<int> SubmittedReportIds
        {
            get
            {
                lock (_sync)
                    return _submittedReportIds.ToArray();
            }
        }

        public async Task WaitForSubmittedCountAsync(int expected, TimeSpan? timeout = null)
        {
            var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(3));
            while (DateTime.UtcNow < deadline)
            {
                lock (_sync)
                {
                    if (_submittedReportIds.Count >= expected)
                        return;
                }

                await Task.Delay(25);
            }

            Assert.Fail($"Expected at least {expected} submitted job(s), saw {SubmittedReportIds.Count}.");
        }

        public void ReleaseFirstJob() => _releaseFirstJob.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/jobs")
            {
                var remoteId = Interlocked.Increment(ref _nextRemoteId);
                var json = await request.Content!.ReadAsStringAsync(cancellationToken);
                var match = System.Text.RegularExpressions.Regex.Match(json, @"""ReportId""\s*:\s*""(?<id>\d+)""");
                if (match.Success)
                {
                    lock (_sync)
                        _submittedReportIds.Add(int.Parse(match.Groups["id"].Value));
                }

                return new HttpResponseMessage(System.Net.HttpStatusCode.Accepted)
                {
                    Content = new StringContent(
                        $$"""{"jobId":"remote-{{remoteId}}"}""",
                        System.Text.Encoding.UTF8,
                        "application/json")
                };
            }

            if (request.Method == HttpMethod.Get)
            {
                var jobId = request.RequestUri?.Segments.LastOrDefault()?.TrimEnd('/');
                if (string.Equals(jobId, "remote-1", StringComparison.OrdinalIgnoreCase))
                    await _releaseFirstJob.Task.WaitAsync(cancellationToken);

                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $$"""{"jobId":"{{jobId}}","status":"Completed","rowsProcessed":0,"executionTimeMs":1}""",
                        System.Text.Encoding.UTF8,
                        "application/json")
                };
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        }
    }
}
