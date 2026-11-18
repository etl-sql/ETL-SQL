using ETL_SQL.Orchestrator.Execution;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ETL_SQL.Tests.Orchestration;

public class JobThrottleBackoffTests
{
    [Fact]
    public async Task AsyncSlotDisposal_CompletesReleaseBeforeReturningAndIsIdempotent()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"etlsql-throttle-disposal-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "throttle.db");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Orchestrator:DatabasePath"] = databasePath
        }).Build();
        using var throttle = new JobThrottle(Options.Create(new JobThrottleOptions { MaxConcurrentJobs = 1 }),
            NullLogger<JobThrottle>.Instance, configuration);
        IDisposable? slot = null;
        try
        {
            slot = await throttle.AcquireAsync("shutdown");
            Assert.Equal(1, throttle.GetMetrics().ActiveJobs);
            var asyncSlot = Assert.IsAssignableFrom<IAsyncDisposable>(slot);
            await asyncSlot.DisposeAsync();
            await asyncSlot.DisposeAsync();
            slot.Dispose();
            Assert.Equal(0, throttle.GetMetrics().ActiveJobs);
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Pooling = false
            }.ToString());
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM throttle_slots;";
            Assert.Equal(0L, await command.ExecuteScalarAsync());
        }
        finally
        {
            slot?.Dispose();
            using var connection = new SqliteConnection($"Data Source={databasePath}");
            SqliteConnection.ClearPool(connection);
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PostgresWithoutConnectionString_FailsClosed()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Orchestrator:Database:Provider"] = "Postgres"
            })
            .Build();

        var error = Assert.Throws<InvalidOperationException>(() => new JobThrottle(
            Options.Create(new JobThrottleOptions()),
            NullLogger<JobThrottle>.Instance,
            configuration));

        Assert.Contains("ConnectionString", error.Message);
    }

    [Fact]
    public void CalculatePollDelay_UsesExponentialBackoffAndCap()
    {
        using var throttle = new JobThrottle(
            Options.Create(new JobThrottleOptions
            {
                MaxConcurrentJobs = 1,
                PollInitialDelayMs = 100,
                PollMaxDelayMs = 800,
                PollJitterRatio = 0
            }),
            NullLogger<JobThrottle>.Instance);

        Assert.Equal(100, throttle.CalculatePollDelay(0).TotalMilliseconds);
        Assert.Equal(200, throttle.CalculatePollDelay(1).TotalMilliseconds);
        Assert.Equal(400, throttle.CalculatePollDelay(2).TotalMilliseconds);
        Assert.Equal(800, throttle.CalculatePollDelay(3).TotalMilliseconds);
        Assert.Equal(800, throttle.CalculatePollDelay(20).TotalMilliseconds);
    }

    [Fact]
    public void CalculatePollDelay_AppliesBoundedJitter()
    {
        using var throttle = new JobThrottle(
            Options.Create(new JobThrottleOptions
            {
                MaxConcurrentJobs = 1,
                PollInitialDelayMs = 100,
                PollMaxDelayMs = 1000,
                PollJitterRatio = 0.2
            }),
            NullLogger<JobThrottle>.Instance);

        for (var i = 0; i < 100; i++)
        {
            var delay = throttle.CalculatePollDelay(0).TotalMilliseconds;
            Assert.InRange(delay, 80, 120);
        }
    }

    [Fact]
    public async Task ReservedShortSlotAndWeightedHeavyAdmission_AllowsShortProgressUnderHeavySaturation()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"etlsql-throttle-short-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "throttle.db");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Orchestrator:DatabasePath"] = databasePath
        }).Build();

        using var throttle = new JobThrottle(
            Options.Create(new JobThrottleOptions
            {
                MaxConcurrentJobs = 3,
                ReservedShortJobSlots = 1,
                HeavyJobSlotWeight = 2,
                PollInitialDelayMs = 20,
                PollMaxDelayMs = 80,
                PollJitterRatio = 0
            }),
            NullLogger<JobThrottle>.Instance,
            configuration);

        IDisposable? heavy1 = null;
        IDisposable? short1 = null;
        try
        {
            heavy1 = await throttle.AcquireAsync("heavy-1", JobWorkloadClass.Heavy);

            // Second heavy job (weight 2) exceeds MaxConcurrentJobs (3) when combined with heavy-1 and blocks.
            using var heavy2Cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => throttle.AcquireAsync("heavy-2", JobWorkloadClass.Heavy, heavy2Cts.Token));

            // A short job acquires immediately in the reserved capacity while heavy-1 keeps running.
            using var shortTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            short1 = await throttle.AcquireAsync("short-1", JobWorkloadClass.Short, shortTimeout.Token);
            Assert.Equal(2, throttle.GetMetrics().ActiveJobs);
        }
        finally
        {
            if (short1 is IAsyncDisposable sa) await sa.DisposeAsync(); else short1?.Dispose();
            if (heavy1 is IAsyncDisposable ha) await ha.DisposeAsync(); else heavy1?.Dispose();
            using var connection = new SqliteConnection($"Data Source={databasePath}");
            SqliteConnection.ClearPool(connection);
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task PriorityWakeup_DispatchesQueuedShortJobAheadOfQueuedHeavyJob()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"etlsql-throttle-prio-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "throttle.db");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Orchestrator:DatabasePath"] = databasePath
        }).Build();

        using var throttle = new JobThrottle(
            Options.Create(new JobThrottleOptions
            {
                MaxConcurrentJobs = 2,
                ReservedShortJobSlots = 0,
                PollInitialDelayMs = 25,
                PollMaxDelayMs = 100,
                PollJitterRatio = 0
            }),
            NullLogger<JobThrottle>.Instance,
            configuration);

        IDisposable? slot1 = await throttle.AcquireAsync("holder-1", JobWorkloadClass.Normal);
        IDisposable? slot2 = await throttle.AcquireAsync("holder-2", JobWorkloadClass.Normal);
        var order = new List<string>();
        var orderLock = new object();

        using var testCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            var heavyWaitTask = Task.Run(async () =>
            {
                await using var acquired = (IAsyncDisposable)await throttle.AcquireAsync("queued-heavy", JobWorkloadClass.Heavy, testCts.Token);
                lock (orderLock) order.Add("heavy");
                await Task.Delay(50, testCts.Token);
            }, testCts.Token);

            // Wait until the heavy job enters the queue before queuing the short job.
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (throttle.GetMetrics().QueuedJobs < 1 && DateTime.UtcNow < deadline)
                await Task.Delay(10);

            var shortWaitTask = Task.Run(async () =>
            {
                await using var acquired = (IAsyncDisposable)await throttle.AcquireAsync("queued-short", JobWorkloadClass.Short, testCts.Token);
                lock (orderLock) order.Add("short");
            }, testCts.Token);

            while (throttle.GetMetrics().QueuedJobs < 2 && DateTime.UtcNow < deadline)
                await Task.Delay(10);

            // Releasing one holder slot must let the queued Short job proceed before the Heavy job.
            await ((IAsyncDisposable)slot1).DisposeAsync();
            slot1 = null;
            await shortWaitTask;

            await ((IAsyncDisposable)slot2).DisposeAsync();
            slot2 = null;
            await heavyWaitTask;

            Assert.Equal(["short", "heavy"], order);
        }
        finally
        {
            if (slot1 is IAsyncDisposable s1a) await s1a.DisposeAsync(); else slot1?.Dispose();
            if (slot2 is IAsyncDisposable s2a) await s2a.DisposeAsync(); else slot2?.Dispose();
            using var connection = new SqliteConnection($"Data Source={databasePath}");
            SqliteConnection.ClearPool(connection);
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ClassifyWorkload_RecognizesHeavyOperatorsAndMetadataOverrides()
    {
        Assert.Equal(JobWorkloadClass.Short, JobThrottle.ClassifyWorkload("SELECT 1 AS Ok;"));
        Assert.Equal(JobWorkloadClass.Heavy, JobThrottle.ClassifyWorkload("SELECT a FROM #t ORDER BY a DESC;"));
        Assert.Equal(JobWorkloadClass.Heavy, JobThrottle.ClassifyWorkload("SELECT a FROM #x JOIN #y ON #x.id = #y.id;"));
        Assert.Equal(JobWorkloadClass.Heavy, JobThrottle.ClassifyWorkload("SELECT ROW_NUMBER() OVER (ORDER BY id) FROM #t;"));
        Assert.Equal(JobWorkloadClass.Heavy, JobThrottle.ClassifyWorkload("TRANSFORM #out FROM #src USING PIVOT (KEY = k, VAL = v);"));
        Assert.Equal(JobWorkloadClass.Heavy, JobThrottle.ClassifyWorkload("SELECT 1;", new Dictionary<string, string> { ["WorkloadClass"] = "Heavy" }));
        Assert.Equal(JobWorkloadClass.Short, JobThrottle.ClassifyWorkload("SELECT * FROM #a ORDER BY x;", new Dictionary<string, string> { ["Priority"] = "High" }));
    }
}
