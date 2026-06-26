using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Execution;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.Orchestrator.Scheduling;
using ETL_SQL.Orchestrator.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Orchestration
{
    /// <summary>
    /// P1.1 — durable per-job execution lease. The store-level tests prove the claim is atomic,
    /// owner-checked, reclaimable after expiry, and survives a job-definition re-save. The
    /// scheduler-level test proves two scheduler instances sharing one store produce exactly one
    /// execution for the same due occurrence.
    /// </summary>
    public class JobExecutionLeaseTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly SQLiteJobHistoryStore _store;

        public JobExecutionLeaseTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"etlsql-lease-{Guid.NewGuid():N}.db");
            _store = new SQLiteJobHistoryStore(_dbPath);
        }

        public void Dispose()
        {
            try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
            try { if (File.Exists(_dbPath + "-wal")) File.Delete(_dbPath + "-wal"); } catch (IOException) { }
            try { if (File.Exists(_dbPath + "-shm")) File.Delete(_dbPath + "-shm"); } catch (IOException) { }
        }

        private static JobDefinition MakeJob(string name) =>
            new JobDefinition(name, "SELECT 1;", 1, "HOUR", null, null, null, true);

        // ── Store primitives ─────────────────────────────────────────────────────

        [Fact]
        public async Task TryAcquire_ParallelClaims_ExactlyOneWinner()
        {
            await _store.InitializeAsync();
            await _store.SaveJobAsync(MakeJob("lease_parallel"));

            var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(i =>
                _store.TryAcquireJobLeaseAsync("lease_parallel", $"owner-{i}", TimeSpan.FromMinutes(10))));

            Assert.Equal(1, results.Count(r => r));
        }

        [Fact]
        public async Task TryAcquire_HeldLease_DeniedUntilExpiryThenReclaimable()
        {
            await _store.InitializeAsync();
            await _store.SaveJobAsync(MakeJob("lease_expiry"));

            Assert.True(await _store.TryAcquireJobLeaseAsync("lease_expiry", "owner-a", TimeSpan.FromMilliseconds(50)));
            // Held — a second owner is denied while the lease is current... (timing: claim again
            // with a long lease first to prove denial deterministically)
            Assert.True(await _store.TryRenewJobLeaseAsync("lease_expiry", "owner-a", TimeSpan.FromMinutes(10)));
            Assert.False(await _store.TryAcquireJobLeaseAsync("lease_expiry", "owner-b", TimeSpan.FromMinutes(10)));

            // Let the lease lapse, then another owner reclaims it (crash recovery).
            Assert.True(await _store.TryRenewJobLeaseAsync("lease_expiry", "owner-a", TimeSpan.FromMilliseconds(20)));
            await Task.Delay(200);
            Assert.True(await _store.TryAcquireJobLeaseAsync("lease_expiry", "owner-b", TimeSpan.FromMinutes(10)));
        }

        [Fact]
        public async Task TryRenew_IsOwnerChecked()
        {
            await _store.InitializeAsync();
            await _store.SaveJobAsync(MakeJob("lease_renew"));

            Assert.True(await _store.TryAcquireJobLeaseAsync("lease_renew", "owner-a", TimeSpan.FromMinutes(10)));
            Assert.False(await _store.TryRenewJobLeaseAsync("lease_renew", "stranger", TimeSpan.FromMinutes(10)));
            Assert.True(await _store.TryRenewJobLeaseAsync("lease_renew", "owner-a", TimeSpan.FromMinutes(10)));
        }

        [Fact]
        public async Task Release_IsOwnerChecked_AndFreesTheLease()
        {
            await _store.InitializeAsync();
            await _store.SaveJobAsync(MakeJob("lease_release"));

            Assert.True(await _store.TryAcquireJobLeaseAsync("lease_release", "owner-a", TimeSpan.FromMinutes(10)));

            // A non-owner release is a no-op: the lease stays held.
            await _store.ReleaseJobLeaseAsync("lease_release", "stranger");
            Assert.False(await _store.TryAcquireJobLeaseAsync("lease_release", "owner-b", TimeSpan.FromMinutes(10)));

            // The owner's release frees it immediately.
            await _store.ReleaseJobLeaseAsync("lease_release", "owner-a");
            Assert.True(await _store.TryAcquireJobLeaseAsync("lease_release", "owner-b", TimeSpan.FromMinutes(10)));
        }

        [Fact]
        public async Task SaveJob_ReSavingDefinition_DoesNotClearActiveLease()
        {
            await _store.InitializeAsync();
            await _store.SaveJobAsync(MakeJob("lease_resave"));

            Assert.True(await _store.TryAcquireJobLeaseAsync("lease_resave", "owner-a", TimeSpan.FromMinutes(10)));

            // Regression: SaveJobAsync used INSERT OR REPLACE, which deleted and reinserted the
            // row — silently clearing the lease whenever a job definition was re-saved mid-run.
            await _store.SaveJobAsync(MakeJob("lease_resave") with { Script = "SELECT 2;" });

            Assert.False(await _store.TryAcquireJobLeaseAsync("lease_resave", "owner-b", TimeSpan.FromMinutes(10)));
            var saved = (await _store.GetAllJobsAsync()).Single(j => j.Name == "lease_resave");
            Assert.Equal("SELECT 2;", saved.Script);
        }

        // ── Scheduler-level: two instances, one execution ─────────────────────────

        [Fact]
        public async Task TwoSchedulerInstances_SameDueJob_ExecuteExactlyOnce()
        {
            await _store.InitializeAsync();
            await _store.SaveJobAsync(MakeJob("lease_once"));
            var job = (await _store.GetAllJobsAsync()).Single(j => j.Name == "lease_once");

            var executor1 = new CountingExecutor();
            var executor2 = new CountingExecutor();
            var scheduler1 = BuildScheduler(
                new SQLiteJobHistoryStore(_dbPath),
                executor1,
                new FixedCapacityMonitor(isOverloaded: false),
                _dbPath);
            var scheduler2 = BuildScheduler(
                new SQLiteJobHistoryStore(_dbPath),
                executor2,
                new FixedCapacityMonitor(isOverloaded: false),
                _dbPath);

            await Task.WhenAll(
                InvokeExecuteJobAsync(scheduler1, job),
                InvokeExecuteJobAsync(scheduler2, job));

            Assert.Equal(1, executor1.Count + executor2.Count);

            // The winner released its lease on completion, so the job is claimable again.
            Assert.True(await _store.TryAcquireJobLeaseAsync("lease_once", "verifier", TimeSpan.FromMinutes(5)));
        }

        [Fact]
        public async Task HealthySchedulerClaimsDueJobWhenPeerIsOverloaded()
        {
            await _store.InitializeAsync();
            await _store.SaveJobAsync(MakeJob("capacity_claim"));
            var job = (await _store.GetAllJobsAsync()).Single(j => j.Name == "capacity_claim");

            var overloadedExecutor = new CountingExecutor();
            var healthyExecutor = new CountingExecutor();
            var overloadedScheduler = BuildScheduler(
                new SQLiteJobHistoryStore(_dbPath),
                overloadedExecutor,
                new FixedCapacityMonitor(isOverloaded: true),
                _dbPath);
            var healthyScheduler = BuildScheduler(
                new SQLiteJobHistoryStore(_dbPath),
                healthyExecutor,
                new FixedCapacityMonitor(isOverloaded: false),
                _dbPath);

            await InvokeExecuteJobAsync(overloadedScheduler, job);
            await InvokeExecuteJobAsync(healthyScheduler, job);

            Assert.Equal(0, overloadedExecutor.Count);
            Assert.Equal(1, healthyExecutor.Count);
        }

        [Fact]
        public async Task HealthySchedulerClaimsDueJobBurstWhenPeerIsOverloaded()
        {
            await _store.InitializeAsync();
            const int jobCount = 12;
            for (var i = 0; i < jobCount; i++)
                await _store.SaveJobAsync(MakeJob($"capacity_burst_{i:00}"));

            var jobs = await _store.GetAllJobsAsync();
            var overloadedExecutor = new CountingExecutor();
            var healthyExecutor = new CountingExecutor();
            var overloadedScheduler = BuildScheduler(
                new SQLiteJobHistoryStore(_dbPath),
                overloadedExecutor,
                new FixedCapacityMonitor(isOverloaded: true),
                _dbPath);
            var healthyScheduler = BuildScheduler(
                new SQLiteJobHistoryStore(_dbPath),
                healthyExecutor,
                new FixedCapacityMonitor(isOverloaded: false),
                _dbPath);

            await Task.WhenAll(jobs.Select(job => Task.WhenAll(
                InvokeExecuteJobAsync(overloadedScheduler, job),
                InvokeExecuteJobAsync(healthyScheduler, job))));

            Assert.Equal(0, overloadedExecutor.Count);
            Assert.Equal(jobCount, healthyExecutor.Count);
            foreach (var job in jobs)
            {
                Assert.True(await _store.TryAcquireJobLeaseAsync(
                    job.Name,
                    $"verifier-{job.Name}",
                    TimeSpan.FromMinutes(5)));
            }
        }

        private static SchedulerService BuildScheduler(
            IJobHistoryStore store,
            IScriptExecutor executor,
            INodeCapacityMonitor? capacityMonitor = null,
            string? databasePath = null)
        {
            var services = new ServiceCollection();
            services.AddSingleton(executor);
            var serviceProvider = services.BuildServiceProvider();

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(databasePath is null
                    ? Array.Empty<KeyValuePair<string, string?>>()
                    : new[] { new KeyValuePair<string, string?>("Orchestrator:DatabasePath", databasePath) })
                .Build();

            var throttle = new JobThrottle(
                Options.Create(new JobThrottleOptions { MaxConcurrentJobs = 4 }),
                new Mock<ILogger<JobThrottle>>().Object,
                config);

            return new SchedulerService(
                serviceProvider,
                store,
                new Mock<ILogger<SchedulerService>>().Object,
                throttle,
                config,
                new Mock<ISessionStateManager>().Object,
                capacityMonitor);
        }

        private static Task InvokeExecuteJobAsync(SchedulerService service, JobDefinition job)
        {
            var method = typeof(SchedulerService).GetMethod("ExecuteJobAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            return (Task)method.Invoke(service, new object[] { job })!;
        }

        private sealed class CountingExecutor : IScriptExecutor
        {
            private int _count;
            public int Count => _count;

            public async Task<ScriptExecutionResult> ExecuteTextAsync(
                string scriptText,
                string? sessionId = null,
                CancellationToken cancellationToken = default,
                string? jobName = null,
                long queueWaitMs = 0)
            {
                Interlocked.Increment(ref _count);
                // Hold the slot briefly so the losing scheduler's claim attempt overlaps the run.
                await Task.Delay(150, cancellationToken);
                return new ScriptExecutionResult(true, 1);
            }
        }

        private sealed class FixedCapacityMonitor(bool isOverloaded) : INodeCapacityMonitor
        {
            public NodeCapacitySnapshot Capture() => new(
                WorkingSetBytes: 128 * 1024 * 1024,
                GcHeapBytes: 64 * 1024 * 1024,
                TotalAvailableMemoryBytes: 1024L * 1024 * 1024,
                MemoryLoadPercent: isOverloaded ? 99 : 10,
                ProcessCpuPercent: isOverloaded ? 99 : 1,
                ProcessorCount: Environment.ProcessorCount,
                IsOverloaded: isOverloaded,
                CapturedAtUtc: DateTime.UtcNow);
        }
    }
}
