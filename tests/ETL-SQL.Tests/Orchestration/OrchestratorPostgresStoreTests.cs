using System;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Multitenancy;
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

        private RelationalSandboxAdmissionLedger NewAdmissionLedger() =>
            new(new NpgsqlOrchestratorDialect(_pg.GetConnectionString()));

        private RelationalTenantMeteringLedger NewMeteringLedger() =>
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

        [Fact]
        public async Task SharedTenantLifecycle_IsPartitionedAndDurableOnPostgres()
        {
            var store = NewStore();
            await store.InitializeAsync();
            var now = DateTimeOffset.UtcNow;
            var alpha = TenantContext.FromVerifiedCredential("tenant-alpha");
            var beta = TenantContext.FromVerifiedCredential("tenant-beta");
            static SharedTenantLifecycleCommand Provision(
                string operation, string reference, DateTimeOffset timestamp) => new(
                    operation, SharedTenantLifecycleKind.Provision, "platform-operator", reference,
                    "release-1", 3, 2048, 4, timestamp);

            await store.ApplySharedTenantLifecycleAsync(alpha, Provision("pg-alpha", "change-a", now));
            await store.ApplySharedTenantLifecycleAsync(beta, Provision("pg-beta", "change-b", now));
            await store.SaveJobAsync(new JobDefinition(
                "pg-alpha-job", "RUN SCRIPT 'x.etlsql';", 1, "DAY", null, null, null,
                TenantId: "tenant-alpha"));
            await store.SaveJobAsync(new JobDefinition(
                "pg-beta-job", "RUN SCRIPT 'x.etlsql';", 1, "DAY", null, null, null,
                TenantId: "tenant-beta"));

            var deleted = await NewStore().ApplySharedTenantLifecycleAsync(alpha, new(
                "pg-delete-alpha", SharedTenantLifecycleKind.Delete, "platform-operator", "change-d",
                "release-1", 3, 2048, 4, now.AddSeconds(1)));

            Assert.Equal("Deleted", deleted.State.State);
            Assert.Null(await store.GetJobAsync("pg-alpha-job"));
            Assert.NotNull(await store.GetJobAsync("pg-beta-job"));
            Assert.Equal("Active", (await NewStore().GetSharedTenantStateAsync(beta))!.State);
        }

        [Fact]
        public async Task TenantMeteringLedger_IsPartitionedIdempotentAndUsesInt64_OnPostgres()
        {
            var ledger = NewMeteringLedger();
            var alpha = TenantContext.FromVerifiedCredential("tenant-alpha");
            var beta = TenantContext.FromVerifiedCredential("tenant-beta");
            var large = (long)int.MaxValue + 100;
            TenantMeteringEvent Usage(long rows) => new()
            {
                SourceEventId = "attempt-1",
                Source = TenantMeteringSource.Sandbox,
                WorkloadClass = TenantWorkloadClass.Script,
                ConnectorClass = TenantConnectorClass.Database,
                Status = TenantMeteringStatus.Succeeded,
                Rows = rows,
                BytesRead = large,
                SandboxPeakMemoryBytes = large,
                ConcurrencyUnits = 1,
                RecordedAtUtc = DateTimeOffset.UtcNow
            };

            await ledger.AppendAsync(alpha, Usage(large));
            await ledger.AppendAsync(alpha, Usage(1));
            await ledger.AppendAsync(beta, Usage(7));

            var alphaRow = Assert.Single(await ledger.ListAsync(alpha));
            var betaRow = Assert.Single(await ledger.ListAsync(beta));
            Assert.Equal(large, alphaRow.Event.Rows);
            Assert.Equal(large, alphaRow.Event.BytesRead);
            Assert.Equal(7, betaRow.Event.Rows);
            Assert.DoesNotContain((await ledger.ListAsync(alpha)), row => row.TenantId == "tenant-beta");
        }

        [Fact]
        public async Task SandboxAdmissionLedger_FencesCapacityAndRetainedState_OnPostgres()
        {
            var first = NewAdmissionLedger();
            var second = NewAdmissionLedger();
            var policy = new ResolvedSandboxAdmissionPolicy
            {
                PoolId = "pg-shared-hardened",
                TenantWeight = 1,
                MaxConcurrentAttempts = 1,
                MaxQueuedAttempts = 8
            };
            await first.EnqueueAsync(
                "pg-admission-a", TenantContext.FromVerifiedCredential("tenant-a"), policy);
            await first.EnqueueAsync(
                "pg-admission-b", TenantContext.FromVerifiedCredential("tenant-b"), policy);

            var claims = await Task.WhenAll(
                first.TryActivateAsync(
                    "pg-admission-a", "pg-node-a", 1, TimeSpan.FromMinutes(5)),
                second.TryActivateAsync(
                    "pg-admission-b", "pg-node-b", 1, TimeSpan.FromMinutes(5)));

            Assert.Single(claims, token => token.HasValue);
            var firstWon = claims[0].HasValue;
            var activeId = firstWon ? "pg-admission-a" : "pg-admission-b";
            var queuedId = firstWon ? "pg-admission-b" : "pg-admission-a";
            var owner = firstWon ? "pg-node-a" : "pg-node-b";
            var token = claims.First(value => value.HasValue)!.Value;
            Assert.True(await first.TryRetainAsync(
                activeId, owner, token, "runtime detach unconfirmed"));
            Assert.Null(await second.TryActivateAsync(
                queuedId, "pg-node-c", 1, TimeSpan.FromMinutes(5)));

            var restarted = NewAdmissionLedger();
            var retained = await restarted.ReadAsync(activeId);
            Assert.Equal(SandboxAdmissionState.Retained, retained!.State);
            Assert.True(await restarted.ReleaseRetainedAsync(activeId, token));
            Assert.NotNull(await restarted.TryActivateAsync(
                queuedId, "pg-node-c", 1, TimeSpan.FromMinutes(5)));
        }
    }
}
