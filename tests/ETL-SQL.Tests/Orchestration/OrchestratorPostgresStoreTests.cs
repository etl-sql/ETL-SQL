using System;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core.Data;
using ETL_SQL.Orchestrator.Storage;
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
