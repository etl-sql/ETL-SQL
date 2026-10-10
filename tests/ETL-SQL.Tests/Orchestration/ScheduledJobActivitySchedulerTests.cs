using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Execution;
using ETL_SQL.Core.Governance;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.Orchestrator.Scheduling;
using ETL_SQL.Orchestrator.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Orchestration;

[Trait("CompatBreak", "0.21.0")]
public sealed class ScheduledJobActivitySchedulerTests : IAsyncLifetime
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"etlsql-activity-scheduler-{Guid.NewGuid():N}.db");
    private readonly SQLiteJobHistoryStore store;
    private readonly BlockingExecutor executor = new();
    private readonly ServiceProvider services;
    private readonly JobThrottle throttle;
    private readonly SchedulerService scheduler;

    public ScheduledJobActivitySchedulerTests()
    {
        store = new SQLiteJobHistoryStore(databasePath);
        services = new ServiceCollection().AddSingleton<IScriptExecutor>(executor).BuildServiceProvider();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Orchestrator:DatabasePath"] = databasePath,
            ["Scheduler:QuarantineFailureThreshold"] = "0"
        }).Build();
        throttle = new JobThrottle(Options.Create(new JobThrottleOptions { MaxConcurrentJobs = 2 }),
            NullLogger<JobThrottle>.Instance, config);
        scheduler = new SchedulerService(services, store, NullLogger<SchedulerService>.Instance,
            throttle, config, new Mock<ISessionStateManager>().Object, new HealthyCapacity());
    }

    public Task InitializeAsync() => store.InitializeAsync();

    public async Task DisposeAsync()
    {
        executor.Release.TrySetResult();
        await scheduler.StopAsync();
        throttle.Dispose();
        await services.DisposeAsync();
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        SqliteConnection.ClearPool(connection);
        foreach (var suffix in new[] { "", "-wal", "-shm" })
            if (File.Exists(databasePath + suffix)) File.Delete(databasePath + suffix);
    }

    [Fact]
    public async Task SameNameAcrossTenantsStartsIndependentlyAndStateClearsOnCompletion()
    {
        var first = await AddJobAsync("same-name", "tenant-a");
        var second = await AddJobAsync("same-name", "tenant-b");
        Assert.Equal(ManualTriggerResult.Accepted, await scheduler.TriggerJobWithOverridesAsync(first.Id, null));
        Assert.Equal(ManualTriggerResult.Accepted, await scheduler.TriggerJobWithOverridesAsync(second.Id, null));
        await executor.TwoStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(ScheduledJobRunState.Running, await StateAsync(first));
        Assert.Equal(ScheduledJobRunState.Running, await StateAsync(second));
        Assert.Equal(ManualTriggerResult.AlreadyRunning, await scheduler.TriggerJobWithOverridesAsync(first.Id, null));
        executor.Release.SetResult();
        await scheduler.StopAsync();
        Assert.Equal(ScheduledJobRunState.Idle, await StateAsync(first));
        Assert.Equal(ScheduledJobRunState.Idle, await StateAsync(second));
        Assert.Equal(2, executor.Count);
        Assert.Equal("SUCCESS", Assert.Single(await store.GetHistoryAsync(first.Id)).Status);
        Assert.Equal("SUCCESS", Assert.Single(await store.GetHistoryAsync(second.Id)).Status);
    }

    [Fact]
    public async Task WaitingJobWhoseLeaseWasReclaimedNeverExecutesOrOverwritesTheNewOwner()
    {
        await using var occupied1 = (IAsyncDisposable)await throttle.AcquireAsync("fixture-one");
        await using var occupied2 = (IAsyncDisposable)await throttle.AcquireAsync("fixture-two");
        var job = await AddJobAsync("waiter", "tenant-a");
        var execution = ExecuteAsync(job);
        await WaitForStateAsync(job, ScheduledJobRunState.Queued);
        Assert.Equal(0, executor.Count);
        using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE jobs SET lease_expires_at = '2000-01-01T00:00:00.0000000Z' WHERE id = $id";
            command.Parameters.AddWithValue("$id", job.Id.Value);
            await command.ExecuteNonQueryAsync();
        }
        var token = (await store.AcquireJobLeaseAsync(job.Id, "new-owner", TimeSpan.FromMinutes(10)))!.Value;
        Assert.True(await store.TrySetScheduledJobRunStateAsync(job.Id, "new-owner", token, ScheduledJobRunState.Queued));
        await occupied1.DisposeAsync();
        await execution.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, executor.Count);
        Assert.Equal(ScheduledJobRunState.Queued, await StateAsync(job));
        Assert.Equal("FAILURE", Assert.Single(await store.GetHistoryAsync(job.Id)).Status);
        Assert.Null(await store.AcquireJobLeaseAsync(job.Id, "third-owner", TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public async Task RuntimeChangesFromQueuedToRunningOnlyAfterTheSlotIsAvailable()
    {
        await using var occupied1 = (IAsyncDisposable)await throttle.AcquireAsync("fixture-one");
        await using var occupied2 = (IAsyncDisposable)await throttle.AcquireAsync("fixture-two");
        var job = await AddJobAsync("waiter", "tenant-a");
        var execution = ExecuteAsync(job);
        try
        {
            await WaitForStateAsync(job, ScheduledJobRunState.Queued);
            Assert.Equal(0, executor.Count);
            await occupied1.DisposeAsync();
            await executor.OneStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(ScheduledJobRunState.Running, await StateAsync(job));
        }
        finally
        {
            executor.Release.TrySetResult();
            await execution.WaitAsync(TimeSpan.FromSeconds(10));
        }
        Assert.Equal(ScheduledJobRunState.Idle, await StateAsync(job));
    }

    private Task ExecuteAsync(JobDefinition job) => (Task)typeof(SchedulerService)
        .GetMethod("ExecuteJobAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(scheduler, [job, null, null, false])!;

    [Theory]
    [InlineData(ScheduledJobRunState.Queued, false)]
    [InlineData(ScheduledJobRunState.Queued, true)]
    [InlineData(ScheduledJobRunState.Running, false)]
    [InlineData(ScheduledJobRunState.Running, true)]
    public async Task MissingStateAuthorityBeforeExecutionStopsTheCycleWithoutRetries(
        ScheduledJobRunState refusedState, bool outage)
    {
        var mockStore = ActivityStoreMock();
        mockStore.As<IScheduledJobActivityStore>().Setup(s => s.TrySetScheduledJobRunStateAsync(
                It.IsAny<JobId>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<ScheduledJobRunState>(),
                It.IsAny<CancellationToken>()))
            .Returns((JobId _, string _, long _, ScheduledJobRunState state, CancellationToken _) =>
                state == refusedState
                    ? outage ? Task.FromException<bool>(new IOException("State store unavailable")) : Task.FromResult(false)
                    : Task.FromResult(true));
        var service = MockStoreScheduler(mockStore.Object);
        var job = new JobDefinition("refused", "SELECT 1;", 0, "HOUR", null, null, null, true,
            MaxRetries: 3, RetryDelaySeconds: 0, Id: JobId.New());
        await ExecuteAsync(service, job);
        Assert.Equal(0, executor.Count);
        mockStore.Verify(s => s.LogJobStartAsync(job.Id), Times.Once);
        mockStore.Verify(s => s.ReleaseJobLeaseAsync(job.Id, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task StateCleanupOutageCannotReplaySuccessfulExternalWork()
    {
        var mockStore = ActivityStoreMock();
        mockStore.As<IScheduledJobActivityStore>().Setup(s => s.TrySetScheduledJobRunStateAsync(
                It.IsAny<JobId>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<ScheduledJobRunState>(),
                It.IsAny<CancellationToken>()))
            .Returns((JobId _, string _, long _, ScheduledJobRunState state, CancellationToken _) =>
                state == ScheduledJobRunState.Idle
                    ? Task.FromException<bool>(new IOException("State store unavailable after commit"))
                    : Task.FromResult(true));
        executor.Release.SetResult();
        var job = new JobDefinition("committed", "SELECT 1;", 0, "HOUR", null, null, null, true,
            MaxRetries: 3, RetryDelaySeconds: 0, Id: JobId.New());
        await ExecuteAsync(MockStoreScheduler(mockStore.Object), job);
        Assert.Equal(1, executor.Count);
        mockStore.Verify(s => s.LogJobEndAsync(1L, "SUCCESS", It.IsAny<string?>(),
            It.IsAny<long>(), It.IsAny<long>(), It.IsAny<double>(), It.IsAny<string?>(),
            It.IsAny<bool?>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<string?>()), Times.Once);
        mockStore.Verify(s => s.ReleaseJobLeaseAsync(job.Id, It.IsAny<string>()), Times.Once);
    }

    private static Mock<IJobHistoryStore> ActivityStoreMock()
    {
        var mock = new Mock<IJobHistoryStore>();
        mock.As<IScheduledJobActivityStore>();
        mock.Setup(s => s.AcquireJobLeaseAsync(It.IsAny<JobId>(), It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(1L);
        mock.Setup(s => s.LogJobStartAsync(It.IsAny<JobId>())).ReturnsAsync(1L);
        return mock;
    }

    private SchedulerService MockStoreScheduler(IJobHistoryStore jobStore)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Scheduler:QuarantineFailureThreshold"] = "0"
        }).Build();
        return new SchedulerService(services, jobStore, NullLogger<SchedulerService>.Instance,
            throttle, config, new Mock<ISessionStateManager>().Object, new HealthyCapacity());
    }

    private static Task ExecuteAsync(SchedulerService service, JobDefinition job) => (Task)typeof(SchedulerService)
        .GetMethod("ExecuteJobAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(service, [job, null, null, false])!;

    private async Task<JobDefinition> AddJobAsync(string name, string tenant)
    {
        await store.SaveJobAsync(new JobDefinition(name, "SELECT 1;", 0, "HOUR", null, null, null, true, TenantId: tenant));
        return (await store.GetJobAsync(tenant, name))!;
    }

    private async Task<ScheduledJobRunState> StateAsync(JobDefinition job) =>
        (await store.GetScheduledJobActivityAsync(job.TenantId, DateTimeOffset.UtcNow)).Single(a => a.Job.Id == job.Id).RunState;

    private async Task WaitForStateAsync(JobDefinition job, ScheduledJobRunState expected)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (await StateAsync(job) != expected)
            await Task.Delay(10, timeout.Token);
    }

    private sealed class BlockingExecutor : IScriptExecutor
    {
        private int count;
        public int Count => Volatile.Read(ref count);
        public TaskCompletionSource OneStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource TwoStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ScriptExecutionResult> ExecuteTextAsync(string scriptText, string? sessionId = null,
            CancellationToken cancellationToken = default, string? jobName = null, long queueWaitMs = 0,
            ExecutionIdentity? executionIdentity = null)
        {
            if (Interlocked.Increment(ref count) == 2) TwoStarted.TrySetResult();
            OneStarted.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return new ScriptExecutionResult(true, 1);
        }
    }

    private sealed class HealthyCapacity : INodeCapacityMonitor
    {
        public NodeCapacitySnapshot Capture() => new(1, 1, 1000, 1, 1, 1, false, DateTime.UtcNow);
    }
}
