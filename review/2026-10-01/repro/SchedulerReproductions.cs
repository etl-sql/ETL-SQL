using System.Reflection;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Execution;
using ETL_SQL.Core.Governance;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.Orchestrator.Scheduling;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace ETL_SQL.Review;

public sealed class SchedulerReproductions
{
    [Fact]
    public async Task HistoryFailureMustNotReplayAnAlreadySuccessfulScript()
    {
        var executor = new Mock<IScriptExecutor>();
        var store = new Mock<IJobHistoryStore>();
        store.Setup(s => s.LogJobStartAsync(It.IsAny<JobId>())).ReturnsAsync(1L);
        store.Setup(s => s.AcquireJobLeaseAsync(It.IsAny<JobId>(), It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(1L);
        store.Setup(s => s.TryRenewJobLeaseAsync(It.IsAny<JobId>(), It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(true);
        store.Setup(s => s.ReleaseJobLeaseAsync(It.IsAny<JobId>(), It.IsAny<string>())).Returns(Task.CompletedTask);
        store.Setup(s => s.TryUpdateJobLastRunFencedAsync(It.IsAny<JobId>(), It.IsAny<DateTime>(), It.IsAny<DateTime?>(), It.IsAny<long>())).ReturnsAsync(true);
        var successfulHistoryWrites = 0;
        store.Setup(s => s.LogJobEndAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<long>(), It.IsAny<long>(), It.IsAny<double>(), It.IsAny<string?>(),
                It.IsAny<bool?>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<string?>()))
            .Returns((long id, string status, string? error, long rows, long memory, double cpu,
                string? hash, bool? matched, long quarantined, long warned, string? failures) =>
                status == "SUCCESS" && ++successfulHistoryWrites == 1
                    ? Task.FromException(new IOException("Synthetic temporary history outage"))
                    : Task.CompletedTask);
        var committedWrites = new List<string>();
        executor.Setup(e => e.ExecuteTextAsync(It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>(), It.IsAny<string?>(), It.IsAny<long>(), It.IsAny<ExecutionIdentity?>()))
            .ReturnsAsync(() =>
            {
                // Stand in for an external write that has committed before the executor reports success.
                committedWrites.Add("committed-row");
                return new ScriptExecutionResult(true, 1, null);
            });
        var directory = Path.Combine(Path.GetTempPath(), $"etlsql-review-scheduler-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Orchestrator:DatabasePath"] = Path.Combine(directory, "throttle.db"),
                ["Scheduler:QuarantineFailureThreshold"] = "0"
            }).Build();
            using var throttle = new JobThrottle(Options.Create(new JobThrottleOptions { MaxConcurrentJobs = 1 }),
                NullLogger<JobThrottle>.Instance, config);
            using var provider = new ServiceCollection().AddSingleton(executor.Object).BuildServiceProvider();
            var service = new SchedulerService(provider, store.Object, NullLogger<SchedulerService>.Instance,
                throttle, config, new Mock<ISessionStateManager>().Object, new HealthyCapacityMonitor());
            var job = new JobDefinition("ReviewHistoryFailure", "SELECT 1;", 1, "HOUR", null, null, null, true,
                MaxRetries: 1, RetryDelaySeconds: 0);
            var method = typeof(SchedulerService).GetMethod("ExecuteJobAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
            await (Task)method.Invoke(service, [job, null, null, false])!;
            Assert.Single(committedWrites);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class HealthyCapacityMonitor : INodeCapacityMonitor
    {
        public NodeCapacitySnapshot Capture() => new(128L * 1024 * 1024, 64L * 1024 * 1024,
            8L * 1024 * 1024 * 1024, 1, 1, Environment.ProcessorCount, false, DateTime.UtcNow);
    }
}
