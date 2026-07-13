using System;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core.Data;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.Orchestrator.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;
using Xunit;

namespace ETL_SQL.Tests.Orchestration
{
    /// <summary>
    /// Practical HA P1.2: proves the provider-neutral Orchestrator store runs on a real PostgreSQL
    /// (Testcontainers, Docker-backed). Exercises the dialect-divergent paths — the case-insensitive
    /// <c>nocase</c> collation (COLLATE NOCASE lookups), the auto-increment identity + RETURNING
    /// (LogJobStart), the column sweep, leases, history, and optimistic concurrency.
    /// </summary>
    [Trait("Category", "Integration")]
    public sealed class OrchestratorPostgresStoreTests : IAsyncLifetime
    {
        private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder("postgres:16-alpine").Build();

        public Task InitializeAsync() => _pg.StartAsync();

        public Task DisposeAsync() => _pg.DisposeAsync().AsTask();

        private RelationalJobHistoryStore NewStore() =>
            new(new NpgsqlOrchestratorDialect(_pg.GetConnectionString()));

        private JobThrottle NewThrottle()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Orchestrator:Database:Provider"] = "Postgres",
                    ["Orchestrator:Database:ConnectionString"] = _pg.GetConnectionString()
                })
                .Build();
            return new JobThrottle(
                Options.Create(new JobThrottleOptions
                {
                    MaxConcurrentJobs = 1,
                    PollInitialDelayMs = 20,
                    PollMaxDelayMs = 50,
                    PollJitterRatio = 0,
                    SlotLeaseSeconds = 2,
                    SlotHeartbeatSeconds = 1
                }),
                NullLogger<JobThrottle>.Instance,
                configuration);
        }

        private static JobDefinition Job(string name, bool enabled = true) =>
            new(name, "RUN SCRIPT 'x.etlsql';", 1, "DAY", "06:00", null, null, enabled);

        [Fact]
        public async Task SaveAndGet_IsCaseInsensitive_OnPostgres()
        {
            var store = NewStore();
            await store.InitializeAsync();
            await store.SaveJobAsync(Job("CamelCaseJob"));

            // Proves the PostgreSQL nocase collation backs COLLATE NOCASE lookups.
            var byLower = await store.GetJobAsync("camelcasejob");
            Assert.NotNull(byLower);
            Assert.Equal("CamelCaseJob", byLower!.Name);
        }

        [Fact]
        public async Task History_UsesIdentityReturning_AndRoundTrips()
        {
            var store = NewStore();
            await store.InitializeAsync();
            await store.SaveJobAsync(Job("hist-job"));

            // LogJobStart inserts and returns the generated identity (RETURNING on Postgres).
            var id = await store.LogJobStartAsync("hist-job");
            Assert.True(id > 0);

            await store.LogJobEndAsync(id, "SUCCESS", rowsProcessed: 42, peakMemoryBytes: 1024, cpuTimeSeconds: 1.5);

            var history = (await store.GetHistoryAsync("hist-job")).ToList();
            var entry = Assert.Single(history);
            Assert.Equal(id, entry.Id);
            Assert.Equal("SUCCESS", entry.Status);
            Assert.Equal(42, entry.RowsProcessed);
        }

        [Fact]
        public async Task Lease_AcquireRenewRelease_Coordinates()
        {
            var store = NewStore();
            await store.InitializeAsync();
            await store.SaveJobAsync(Job("lease-job"));

            Assert.True(await store.TryAcquireJobLeaseAsync("lease-job", "owner-A", TimeSpan.FromMinutes(5)));
            // A second owner cannot steal a live lease.
            Assert.False(await store.TryAcquireJobLeaseAsync("lease-job", "owner-B", TimeSpan.FromMinutes(5)));
            Assert.True(await store.TryRenewJobLeaseAsync("lease-job", "owner-A", TimeSpan.FromMinutes(5)));

            await store.ReleaseJobLeaseAsync("lease-job", "owner-A");
            Assert.True(await store.TryAcquireJobLeaseAsync("lease-job", "owner-B", TimeSpan.FromMinutes(5)));
        }

        [Fact]
        public async Task JobThrottle_CoordinatesAcrossPostgresInstances()
        {
            using var firstThrottle = NewThrottle();
            using var secondThrottle = NewThrottle();
            using var firstSlot = await firstThrottle.AcquireAsync("first");

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var secondAcquire = secondThrottle.AcquireAsync("second", timeout.Token);
            await Task.Delay(2500, timeout.Token);
            Assert.False(secondAcquire.IsCompleted);

            firstSlot.Dispose();
            using var secondSlot = await secondAcquire;
            Assert.Equal(1, secondThrottle.GetMetrics().ActiveJobs);
        }

        [Fact]
        public async Task JobThrottle_ReclaimsExpiredRemotePostgresSlot()
        {
            using (var initializer = NewThrottle())
            using (var slot = await initializer.AcquireAsync("initialize"))
            {
            }
            await Task.Delay(150);

            await using (var connection = new Npgsql.NpgsqlConnection(_pg.GetConnectionString()))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT INTO ThrottleSlots (ProcessId, JobName, AcquiredAt, MachineName)
                    VALUES (999999, 'abandoned', @at, 'remote-node');";
                command.Parameters.AddWithValue("at", DateTime.UtcNow.AddMinutes(-5).ToString("O"));
                await command.ExecuteNonQueryAsync();
            }

            using var throttle = NewThrottle();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var acquired = await throttle.AcquireAsync("replacement", timeout.Token);
            Assert.Equal(1, throttle.GetMetrics().ActiveJobs);
        }

        [Fact]
        public async Task FencingToken_AdvancesAndFencesStaleWriter_OnPostgres()
        {
            var store = NewStore();
            await store.InitializeAsync();
            await store.SaveJobAsync(Job("fence-job"));

            var stale = await store.AcquireJobLeaseAsync("fence-job", "owner-A", TimeSpan.FromMilliseconds(40));
            Assert.NotNull(stale);
            await Task.Delay(120); // flaky-delay-ok: wall-clock wait for the lease TTL to expire
            var fresh = await store.AcquireJobLeaseAsync("fence-job", "owner-B", TimeSpan.FromMinutes(5));
            Assert.True(fresh!.Value > stale!.Value);

            var nextRun = DateTime.UtcNow.AddHours(1);
            Assert.False(await store.TryUpdateJobLastRunFencedAsync("fence-job", DateTime.UtcNow, nextRun, stale.Value));
            Assert.True(await store.TryUpdateJobLastRunFencedAsync("fence-job", DateTime.UtcNow, nextRun, fresh.Value));
        }

        [Fact]
        public async Task ClusterLock_AcquireContendExpire_OnPostgres()
        {
            var store = NewStore();
            await store.InitializeAsync();

            Assert.True(await store.TryAcquireLockAsync("migrations", "node-A", TimeSpan.FromMinutes(5)));
            Assert.False(await store.TryAcquireLockAsync("migrations", "node-B", TimeSpan.FromMinutes(5)));
            Assert.Equal("node-A", await store.GetLockHolderAsync("migrations"));

            await store.ReleaseLockAsync("migrations", "node-A");
            Assert.True(await store.TryAcquireLockAsync("migrations", "node-B", TimeSpan.FromMinutes(5)));

            // Expiry-based steal.
            Assert.True(await store.TryAcquireLockAsync("ttl-lock", "node-A", TimeSpan.FromMilliseconds(40)));
            await Task.Delay(120); // flaky-delay-ok: wall-clock wait for the lease TTL to expire
            Assert.True(await store.TryAcquireLockAsync("ttl-lock", "node-B", TimeSpan.FromMinutes(5)));
        }

        [Fact]
        public async Task WriteEpoch_CompareAndAdvance_OnPostgres()
        {
            var store = NewStore();
            await store.InitializeAsync();

            Assert.True(await store.TryClaimWriteEpochAsync("artifact", "Datasets/pg.parquet", 5));
            Assert.True(await store.TryClaimWriteEpochAsync("artifact", "Datasets/pg.parquet", 9)); // advance
            Assert.False(await store.TryClaimWriteEpochAsync("artifact", "Datasets/pg.parquet", 8)); // stale
            Assert.Equal(9, await store.GetWriteEpochAsync("artifact", "Datasets/pg.parquet"));
        }

        [Fact]
        public async Task NodeRegistry_Heartbeat_Upsert_AndExpiry_OnPostgres()
        {
            var store = NewStore();
            await store.InitializeAsync();

            // Upsert: register then renew preserves first-seen and keeps a single row (ON CONFLICT path).
            await store.RegisterOrRenewNodeAsync("pg-node", "Portal", TimeSpan.FromMinutes(5), "{\"v\":1}");
            var first = Assert.Single(await store.GetLiveNodesAsync());
            await store.RegisterOrRenewNodeAsync("pg-node", "Portal", TimeSpan.FromMinutes(5));
            var renewed = Assert.Single(await store.GetLiveNodesAsync());
            Assert.Equal(first.FirstSeenUtc, renewed.FirstSeenUtc);

            // TTL expiry + prune behave the same on Postgres (ISO-8601 string comparison).
            await store.RegisterOrRenewNodeAsync("pg-ttl", "Orchestrator", TimeSpan.FromMilliseconds(40));
            await Task.Delay(120); // flaky-delay-ok: wall-clock wait for the lease TTL to expire
            Assert.DoesNotContain(await store.GetLiveNodesAsync(), n => n.NodeId == "pg-ttl");
            Assert.True(await store.PruneExpiredNodesAsync() >= 1);
        }

        [Fact]
        public async Task OptimisticConcurrency_And_ActiveJobs()
        {
            var store = NewStore();
            await store.InitializeAsync();
            await store.SaveJobAsync(Job("cc-job"));

            var saved = await store.GetJobAsync("cc-job");
            Assert.NotNull(saved);

            // Stale version is rejected; current version succeeds.
            Assert.False(await store.TrySaveJobAsync(Job("cc-job"), saved!.Version + 99));
            Assert.True(await store.TrySaveJobAsync(Job("cc-job"), saved.Version));

            await store.SaveJobAsync(Job("disabled-job", enabled: false));
            var active = (await store.GetActiveJobsAsync()).Select(j => j.Name).ToList();
            Assert.Contains("cc-job", active);
            Assert.DoesNotContain("disabled-job", active);
        }
    }
}
