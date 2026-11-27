using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Execution;
using ETL_SQL.Orchestrator;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.Orchestrator.Scheduling;
using ETL_SQL.Orchestrator.Storage;
using ETL_SQL.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ETL_SQL.Tests.Orchestration;

public sealed class HostWideAdmissionWorkloadTests : IAsyncDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"etlsql-host-admission-{Guid.NewGuid():N}");

    public HostWideAdmissionWorkloadTests()
    {
        Directory.CreateDirectory(_root);
    }

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Best effort cleanup for temp test directories.
        }
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task ProcessJobExecutor_FairShareAndExplicitGrant_ReserveHostBufferManagerAndEnforceChildCeiling()
    {
        var pidStore = Path.Combine(_root, "pids-fairshare.json");
        var sessionRoot = Path.Combine(_root, "sessions-fairshare");
        Directory.CreateDirectory(sessionRoot);

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Engine:TotalMemoryGrantMB"] = "64",
            ["Engine:OperatorMemoryGrantMB"] = "256",
            ["Engine:SpillMinimumFreeDiskMB"] = "0",
            ["Session:Root"] = sessionRoot,
            ["Session:PersistenceDefault"] = "false",
            ["Orchestration:JobThrottle:MaxConcurrentJobs"] = "4",
            ["Orchestration:ResourceManagement:SystemMemoryFloorMB"] = "0"
        }).Build();

        using var bufferManager = new BufferManager(
            Options.Create(new BufferManagerOptions
            {
                MaxGlobalMemoryMB = 64,
                HysteresisMemoryMB = 8,
                SystemMemoryFloorMB = 0,
                ResourceWaitTimeoutSeconds = 10
            }),
            NullLogger<BufferManager>.Instance,
            new FixedSystemResources(),
            configuration);
        using var tracker = new ChildProcessTracker(NullLogger<ChildProcessTracker>.Instance, pidStore);

        var exePath = FindAppHost();
        Assert.True(File.Exists(exePath), $"Expected ETL-SQL apphost at {exePath}");

        // Auto fair-share splits 64 MB across 4 max concurrent workers -> 16 MB per worker.
        var autoExecutor = new ProcessJobExecutor(
            Options.Create(new ProcessJobExecutorOptions
            {
                ExecutablePath = exePath,
                TimeoutSeconds = 45,
                WorkerMemoryGrantMB = 0
            }),
            tracker,
            NullLogger<ProcessJobExecutor>.Instance,
            configuration,
            bufferManager);
        Assert.Equal(16, autoExecutor.ResolveWorkerMemoryGrantMB());

        // Explicit grant is still capped at the host's 64 MB budget.
        var cappedExecutor = new ProcessJobExecutor(
            Options.Create(new ProcessJobExecutorOptions
            {
                ExecutablePath = exePath,
                TimeoutSeconds = 45,
                WorkerMemoryGrantMB = 128
            }),
            tracker,
            NullLogger<ProcessJobExecutor>.Instance,
            configuration,
            bufferManager);
        Assert.Equal(64, cappedExecutor.ResolveWorkerMemoryGrantMB());

        // Tight 2 MB child grant forces spill cleanly and releases host reservation when child exits.
        var boundedExecutor = new ProcessJobExecutor(
            Options.Create(new ProcessJobExecutorOptions
            {
                ExecutablePath = exePath,
                TimeoutSeconds = 45,
                WorkerMemoryGrantMB = 2
            }),
            tracker,
            NullLogger<ProcessJobExecutor>.Instance,
            configuration,
            bufferManager);
        Assert.Equal(2, boundedExecutor.ResolveWorkerMemoryGrantMB());

        const string spillSortScript = """
            SET EXTERNAL_SORT_CHUNK_SIZE = 100;
            SET TEMP_TABLE_SPILL_THRESHOLD = 100;
            SELECT value AS id, REPEAT('z', 48) AS pad
            INTO #work
            FROM GENERATE_SERIES(1, 500);
            SELECT id, pad FROM #work ORDER BY id DESC;
            """;

        var result = await boundedExecutor.ExecuteTextAsync(spillSortScript, jobName: "child-bounded-spill");

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(1000, result.RowsProcessed);
        Assert.Equal(0L, bufferManager.ReservedBytes);
        Assert.Equal(0, tracker.ActiveCount);
    }

    [Fact]
    public async Task ProcessJobExecutor_HostReservationBlocksSecondWorkerExceedingGlobalBudget()
    {
        const long mb = 1024L * 1024L;
        var pidStore = Path.Combine(_root, "pids-oversub.json");
        var sessionRoot = Path.Combine(_root, "sessions-oversub");
        Directory.CreateDirectory(sessionRoot);

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Engine:TotalMemoryGrantMB"] = "64",
            ["Engine:SpillMinimumFreeDiskMB"] = "0",
            ["Session:Root"] = sessionRoot,
            ["Session:PersistenceDefault"] = "false",
            ["Orchestration:ResourceManagement:SystemMemoryFloorMB"] = "0"
        }).Build();

        using var bufferManager = new BufferManager(
            Options.Create(new BufferManagerOptions
            {
                MaxGlobalMemoryMB = 64,
                HysteresisMemoryMB = 8,
                SystemMemoryFloorMB = 0,
                ResourceWaitTimeoutSeconds = 20
            }),
            NullLogger<BufferManager>.Instance,
            new FixedSystemResources(),
            configuration);
        using var tracker = new ChildProcessTracker(NullLogger<ChildProcessTracker>.Instance, pidStore);

        var exePath = FindAppHost();
        Assert.True(File.Exists(exePath), $"Expected ETL-SQL apphost at {exePath}");

        var executor = new ProcessJobExecutor(
            Options.Create(new ProcessJobExecutorOptions
            {
                ExecutablePath = exePath,
                TimeoutSeconds = 45,
                WorkerMemoryGrantMB = 40
            }),
            tracker,
            NullLogger<ProcessJobExecutor>.Instance,
            configuration,
            bufferManager);

        // Hold 30 MB on the host coordinator so only 34 MB remains out of 64 MB.
        // Even though a 40 MB child fits locally, host admission must queue it until the 30 MB frees.
        var existingHostGrant = await bufferManager.ReserveMemoryAsync("active-heavy-worker", 30 * mb);
        try
        {
            var monitor = new NodeCapacityMonitor(
                configuration,
                bufferManager,
                tracker,
                () => 10.0,
                () => 5.0,
                () => 10.0,
                _ => 10_000 * mb,
                TimeSpan.Zero);

            Assert.Equal(30 * mb, monitor.Capture().AggregateWorkerReservedBytes);

            var secondWorkerTask = executor.ExecuteTextAsync("SELECT 1 AS Ok;", jobName: "blocked-worker");
            await Task.Delay(250);
            Assert.False(secondWorkerTask.IsCompleted);

            existingHostGrant.Dispose();
            var result = await secondWorkerTask;
            Assert.True(result.Success, result.ErrorMessage);
        }
        finally
        {
            existingHostGrant.Dispose();
            Assert.Equal(0L, bufferManager.ReservedBytes);
        }
    }

    [Fact]
    public async Task SchedulerService_InProcess_HeavySpillingJobsAlongsideShortRuns_CompletesWithoutStarvation()
    {
        var dbPath = Path.Combine(_root, "orch-inproc.db");
        var sessionRoot = Path.Combine(_root, "sessions-inproc");
        Directory.CreateDirectory(sessionRoot);

        var overrides = new Dictionary<string, string?>
        {
            ["Orchestrator:DatabasePath"] = dbPath,
            ["Engine:TotalMemoryGrantMB"] = "64",
            ["Engine:OperatorMemoryGrantMB"] = "16",
            ["Engine:SpillMinimumFreeDiskMB"] = "0",
            ["Session:Root"] = sessionRoot,
            ["Session:PersistenceDefault"] = "false",
            ["Orchestration:JobThrottle:MaxConcurrentJobs"] = "2",
            ["Orchestration:JobThrottle:ReservedShortJobSlots"] = "1",
            ["Orchestration:JobThrottle:PollInitialDelayMs"] = "20",
            ["Orchestration:JobThrottle:PollMaxDelayMs"] = "80",
            ["Orchestration:JobThrottle:PollJitterRatio"] = "0",
            ["Orchestration:ResourceManagement:HysteresisMemoryMB"] = "8",
            ["Orchestration:ResourceManagement:SystemMemoryFloorMB"] = "0"
        };

        await using var provider = (ServiceProvider)DependencyInjectionSetup.BuildServiceProvider(overrides);
        var store = provider.GetRequiredService<IJobHistoryStore>();
        await store.InitializeAsync();
        var scheduler = provider.GetRequiredService<SchedulerService>();
        var bufferManager = provider.GetRequiredService<BufferManager>();
        var monitor = provider.GetRequiredService<INodeCapacityMonitor>();

        const string heavyScript = """
            SET EXTERNAL_SORT_CHUNK_SIZE = 100;
            SET TEMP_TABLE_SPILL_THRESHOLD = 100;
            SELECT value AS id, REPEAT('a', 64) AS payload
            INTO #heavy_inproc
            FROM GENERATE_SERIES(1, 1500);
            SELECT id, payload FROM #heavy_inproc ORDER BY id DESC;
            """;

        var heavy1 = new JobDefinition("HeavyInProc1", heavyScript, 1, "HOUR", null, null, null, Id: JobId.New());
        var heavy2 = new JobDefinition("HeavyInProc2", heavyScript, 1, "HOUR", null, null, null, Id: JobId.New());
        var short1 = new JobDefinition("ShortInProc1", "SELECT 1 AS Ok;", 1, "HOUR", null, null, null, Id: JobId.New());
        var short2 = new JobDefinition("ShortInProc2", "SELECT 2 AS Ok;", 1, "HOUR", null, null, null, Id: JobId.New());

        foreach (var job in new[] { heavy1, heavy2, short1, short2 })
            await store.SaveJobAsync(job);

        var cap = monitor.Capture();
        Assert.False(cap.IsOverloaded,
            $"Unexpected overload before trigger: CPU={cap.ProcessCpuPercent:F1}% HostCPU={cap.HostCpuPercent?.ToString("F1") ?? "null"} Mem={cap.MemoryLoadPercent:F1}% Reserved={cap.AggregateWorkerReservedBytes}/{cap.EffectiveMemoryBudgetBytes} ({cap.WorkerMemoryReservationPercent:F1}%) SpillFree={cap.SpillDiskFreeBytes} MinSpill={cap.SpillMinimumFreeBytes} SpillPath={cap.SpillVolumePath}");

        Assert.Equal(ManualTriggerResult.Accepted, await scheduler.TriggerJobWithOverridesAsync(heavy1.Id, null));
        Assert.Equal(ManualTriggerResult.Accepted, await scheduler.TriggerJobWithOverridesAsync(heavy2.Id, null));
        Assert.Equal(ManualTriggerResult.Accepted, await scheduler.TriggerJobWithOverridesAsync(short1.Id, null));
        Assert.Equal(ManualTriggerResult.Accepted, await scheduler.TriggerJobWithOverridesAsync(short2.Id, null));

        await LoadAwareWait.UntilAsync(
            "all in-process heavy and short jobs to finish",
            async _ =>
            {
                var entries = new List<JobHistoryEntry>();
                foreach (var job in new[] { heavy1, heavy2, short1, short2 })
                    entries.AddRange(await store.GetHistoryAsync(job.Id, 5));
                return entries;
            },
            entries => entries.Count == 4 && entries.All(e => e.Status is "SUCCESS" or "FAILURE" or "CANCELLED"),
            TimeSpan.FromSeconds(45),
            describe: entries => $"count={entries.Count} statuses={string.Join(",", entries.Select(e => $"{e.JobName}:{e.Status}"))}");

        await scheduler.StopAsync();

        foreach (var job in new[] { heavy1, heavy2, short1, short2 })
        {
            var entry = Assert.Single(await store.GetHistoryAsync(job.Id, 5));
            Assert.True(entry.Status == "SUCCESS", $"{entry.JobName} ended {entry.Status}: {entry.ErrorMessage}");
        }

        Assert.Equal(0L, bufferManager.ReservedBytes);
        Assert.Equal(0L, monitor.Capture().AggregateWorkerReservedBytes);
    }

    [Fact]
    public async Task SchedulerService_ChildWorkers_HeavySpillingJobAlongsideShortRuns_CompletesCleanly()
    {
        var dbPath = Path.Combine(_root, "orch-child.db");
        var sessionRoot = Path.Combine(_root, "sessions-child");
        var pidStore = Path.Combine(_root, "child-pids.json");
        Directory.CreateDirectory(sessionRoot);

        var exePath = FindAppHost();
        Assert.True(File.Exists(exePath), $"Expected ETL-SQL apphost at {exePath}");

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Orchestrator:DatabasePath"] = dbPath,
            ["Engine:TotalMemoryGrantMB"] = "128",
            ["Engine:OperatorMemoryGrantMB"] = "32",
            ["Engine:SpillMinimumFreeDiskMB"] = "0",
            ["Session:Root"] = sessionRoot,
            ["Session:PersistenceDefault"] = "false",
            ["Jobs:UseProcessSpawning"] = "true",
            ["Jobs:ExecutablePath"] = exePath,
            ["Jobs:TimeoutSeconds"] = "45",
            ["Jobs:WorkerMemoryGrantMB"] = "24",
            ["Orchestration:JobThrottle:MaxConcurrentJobs"] = "2",
            ["Orchestration:JobThrottle:ReservedShortJobSlots"] = "1",
            ["Orchestration:JobThrottle:PollInitialDelayMs"] = "20",
            ["Orchestration:JobThrottle:PollMaxDelayMs"] = "80",
            ["Orchestration:JobThrottle:PollJitterRatio"] = "0",
            ["Orchestration:ResourceManagement:HysteresisMemoryMB"] = "16",
            ["Orchestration:ResourceManagement:SystemMemoryFloorMB"] = "0"
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging(lb => lb.AddProvider(NullLoggerProvider.Instance));
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(new ETL_SQL.Common.LoggerService());
        services.AddSingleton<ETL_SQL.Common.ILogger>(sp => sp.GetRequiredService<ETL_SQL.Common.LoggerService>());
        services.AddSingleton<ETL_SQL.Common.ILoggerService>(sp => sp.GetRequiredService<ETL_SQL.Common.LoggerService>());
        services.AddEtlSqlEngine(configuration);
        services.Configure<ProcessJobExecutorOptions>(configuration.GetSection("Jobs"));
        services.AddSingleton(sp => new ChildProcessTracker(
            sp.GetRequiredService<ILogger<ChildProcessTracker>>(),
            pidStore));
        services.AddSingleton<ISystemResources, FixedSystemResources>();
        services.AddTransient<IScriptExecutor, ProcessJobExecutor>();

        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IJobHistoryStore>();
        await store.InitializeAsync();
        var scheduler = provider.GetRequiredService<SchedulerService>();
        var bufferManager = provider.GetRequiredService<BufferManager>();
        var tracker = provider.GetRequiredService<ChildProcessTracker>();
        var monitor = provider.GetRequiredService<INodeCapacityMonitor>();

        const string heavyChildScript = """
            SET EXTERNAL_SORT_CHUNK_SIZE = 100;
            SET TEMP_TABLE_SPILL_THRESHOLD = 100;
            SELECT value AS id, REPEAT('b', 48) AS payload
            INTO #heavy_child
            FROM GENERATE_SERIES(1, 800);
            SELECT id, payload FROM #heavy_child ORDER BY id DESC;
            """;

        var heavy1 = new JobDefinition("HeavyChild1", heavyChildScript, 1, "HOUR", null, null, null, Id: JobId.New());
        var heavy2 = new JobDefinition("HeavyChild2", heavyChildScript, 1, "HOUR", null, null, null, Id: JobId.New());
        var short1 = new JobDefinition("ShortChild1", "SELECT 99 AS ShortOk;", 1, "HOUR", null, null, null, Id: JobId.New());

        foreach (var job in new[] { heavy1, heavy2, short1 })
            await store.SaveJobAsync(job);

        var cap = monitor.Capture();
        Assert.False(cap.IsOverloaded,
            $"Unexpected overload before trigger: CPU={cap.ProcessCpuPercent:F1}% HostCPU={cap.HostCpuPercent?.ToString("F1") ?? "null"} Mem={cap.MemoryLoadPercent:F1}% Reserved={cap.AggregateWorkerReservedBytes}/{cap.EffectiveMemoryBudgetBytes} ({cap.WorkerMemoryReservationPercent:F1}%) SpillFree={cap.SpillDiskFreeBytes} MinSpill={cap.SpillMinimumFreeBytes} SpillPath={cap.SpillVolumePath}");

        Assert.Equal(ManualTriggerResult.Accepted, await scheduler.TriggerJobWithOverridesAsync(heavy1.Id, null));
        Assert.Equal(ManualTriggerResult.Accepted, await scheduler.TriggerJobWithOverridesAsync(heavy2.Id, null));
        Assert.Equal(ManualTriggerResult.Accepted, await scheduler.TriggerJobWithOverridesAsync(short1.Id, null));

        await LoadAwareWait.UntilAsync(
            "all child-worker heavy and short jobs to finish",
            async _ =>
            {
                var entries = new List<JobHistoryEntry>();
                foreach (var job in new[] { heavy1, heavy2, short1 })
                    entries.AddRange(await store.GetHistoryAsync(job.Id, 5));
                return entries;
            },
            entries => entries.Count == 3 && entries.All(e => e.Status is "SUCCESS" or "FAILURE" or "CANCELLED"),
            TimeSpan.FromSeconds(60),
            describe: entries => $"count={entries.Count} statuses={string.Join(",", entries.Select(e => $"{e.JobName}:{e.Status}:{e.ErrorMessage}"))}");

        await scheduler.StopAsync();

        var shortEntry = Assert.Single(await store.GetHistoryAsync(short1.Id, 5));
        var heavyEntry1 = Assert.Single(await store.GetHistoryAsync(heavy1.Id, 5));
        var heavyEntry2 = Assert.Single(await store.GetHistoryAsync(heavy2.Id, 5));

        Assert.Equal("SUCCESS", shortEntry.Status);
        Assert.Equal("SUCCESS", heavyEntry1.Status);
        Assert.Equal(1600, heavyEntry1.RowsProcessed);
        Assert.Equal("SUCCESS", heavyEntry2.Status);
        Assert.Equal(1600, heavyEntry2.RowsProcessed);

        Assert.Equal(0, tracker.ActiveCount);
        Assert.Equal(0L, bufferManager.ReservedBytes);
        Assert.Equal(0L, monitor.Capture().AggregateWorkerReservedBytes);
    }

    private sealed class FixedSystemResources : ISystemResources
    {
        public long GetAvailableMemoryBytes() => 16L * 1024 * 1024 * 1024;
        public long GetTotalMemoryBytes() => 32L * 1024 * 1024 * 1024;
    }

    private static string FindAppHost([CallerFilePath] string sourceFilePath = "")
    {
        var appHostName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ETL-SQL.exe" : "ETL-SQL";
        foreach (var dir in CandidateAppHostDirectories(sourceFilePath))
        {
            var candidate = Path.Combine(dir, appHostName);
            if (File.Exists(candidate) && File.Exists(Path.Combine(dir, "Microsoft.Extensions.DependencyInjection.Abstractions.dll")))
                return candidate;
        }

        return Path.Combine(AppContext.BaseDirectory, appHostName);
    }

    private static IEnumerable<string> CandidateAppHostDirectories(string sourceFilePath)
    {
        var testDir = AppContext.BaseDirectory.Replace('\\', '/').TrimEnd('/');
        var appDir = testDir.Replace("ETL-SQL.Tests", "ETL-SQL.App");
        yield return appDir.Replace("/tests/", "/src/");
        yield return appDir;

        var current = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath) ?? AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ETL-SQL.slnx")))
            {
                foreach (var cfg in new[] { "Debug", "Release" })
                {
                    var appOutput = Path.Combine(current.FullName, "src", "ETL-SQL.App", "bin", cfg, "net10.0");
                    if (Directory.Exists(appOutput))
                    {
                        foreach (var ridDir in Directory.GetDirectories(appOutput))
                        {
                            var rid = Path.GetFileName(ridDir);
                            if (IsRunnableRid(rid))
                                yield return ridDir;
                        }
                        yield return appOutput;
                    }
                }
                break;
            }
            current = current.Parent;
        }
    }

    private static bool IsRunnableRid(string? rid)
    {
        if (string.IsNullOrEmpty(rid)) return false;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return rid.StartsWith("win", StringComparison.OrdinalIgnoreCase);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return rid.StartsWith("osx", StringComparison.OrdinalIgnoreCase);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return rid.StartsWith("linux", StringComparison.OrdinalIgnoreCase);
        return false;
    }
}
