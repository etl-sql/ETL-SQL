using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Orchestrator.Scheduling;
using ETL_SQL.Orchestrator.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ETL_SQL.Tests.Orchestration
{
    /// <summary>
    /// Practical HA P1.9: database-backed leader election / distributed lock and the
    /// <see cref="ClusterLock.RunExclusiveAsync"/> helper that serializes a cluster-singleton critical
    /// section (the motivating case: applying EF migrations once when nodes boot concurrently). The
    /// cross-provider lock SQL is also exercised on PostgreSQL in <see cref="OrchestratorPostgresStoreTests"/>.
    /// </summary>
    public sealed class ClusterLockTests : IDisposable
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"lock_{Guid.NewGuid():N}.db");
        private readonly SQLiteJobHistoryStore _store;

        public ClusterLockTests() => _store = new SQLiteJobHistoryStore(_dbPath);

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            foreach (var p in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
                try { if (File.Exists(p)) File.Delete(p); } catch { /* best-effort */ }
        }

        [Fact]
        public async Task OnlyOneOwner_HoldsLock_UntilReleasedOrExpired()
        {
            await _store.InitializeAsync();

            Assert.True(await _store.TryAcquireLockAsync("migrations", "node-A", TimeSpan.FromMinutes(5)));
            Assert.Equal("node-A", await _store.GetLockHolderAsync("migrations"));

            // A second node cannot take a live lock; the holder can re-acquire (idempotent) and renew.
            Assert.False(await _store.TryAcquireLockAsync("migrations", "node-B", TimeSpan.FromMinutes(5)));
            Assert.True(await _store.TryAcquireLockAsync("migrations", "node-A", TimeSpan.FromMinutes(5)));
            Assert.True(await _store.TryRenewLockAsync("migrations", "node-A", TimeSpan.FromMinutes(5)));
            Assert.False(await _store.TryRenewLockAsync("migrations", "node-B", TimeSpan.FromMinutes(5)));

            // After release, another node can take it.
            await _store.ReleaseLockAsync("migrations", "node-A");
            Assert.Null(await _store.GetLockHolderAsync("migrations"));
            Assert.True(await _store.TryAcquireLockAsync("migrations", "node-B", TimeSpan.FromMinutes(5)));
        }

        [Fact]
        public async Task ExpiredLock_CanBeStolen()
        {
            await _store.InitializeAsync();
            Assert.True(await _store.TryAcquireLockAsync("L", "node-A", TimeSpan.FromMilliseconds(40)));
            await Task.Delay(120);

            Assert.Null(await _store.GetLockHolderAsync("L")); // expired → no live holder
            Assert.True(await _store.TryAcquireLockAsync("L", "node-B", TimeSpan.FromMinutes(5)));
            Assert.Equal("node-B", await _store.GetLockHolderAsync("L"));
        }

        [Fact]
        public async Task DistinctLockNames_DoNotContend()
        {
            await _store.InitializeAsync();
            Assert.True(await _store.TryAcquireLockAsync("a", "n1", TimeSpan.FromMinutes(5)));
            Assert.True(await _store.TryAcquireLockAsync("b", "n2", TimeSpan.FromMinutes(5)));
        }

        [Fact]
        public async Task RunExclusive_SerializesConcurrentCriticalSections()
        {
            await _store.InitializeAsync();

            int concurrent = 0, maxConcurrent = 0, runs = 0;
            var gate = new object();

            async Task Section()
            {
                lock (gate) { concurrent++; maxConcurrent = Math.Max(maxConcurrent, concurrent); runs++; }
                await Task.Delay(60); // hold the section so an overlap would be observable
                lock (gate) { concurrent--; }
            }

            // Five "nodes" race to run the same guarded singleton; they must take strict turns.
            var nodes = new Task[5];
            for (int i = 0; i < nodes.Length; i++)
            {
                var owner = $"node-{i}";
                nodes[i] = ClusterLock.RunExclusiveAsync(
                    _store, "boot-migration", owner, Section, NullLogger.Instance,
                    ttl: TimeSpan.FromSeconds(30), maxWait: TimeSpan.FromSeconds(30));
            }
            await Task.WhenAll(nodes);

            Assert.Equal(5, runs);          // every node ran the (idempotent) section
            Assert.Equal(1, maxConcurrent); // but never two at once
            Assert.Null(await _store.GetLockHolderAsync("boot-migration")); // released at the end
        }

        [Fact]
        public async Task RunExclusive_TimesOut_WhenLockHeldThroughout()
        {
            await _store.InitializeAsync();
            // A long-lived foreign holder the helper can never acquire within its wait window.
            Assert.True(await _store.TryAcquireLockAsync("busy", "other-node", TimeSpan.FromMinutes(10)));

            await Assert.ThrowsAsync<TimeoutException>(() =>
                ClusterLock.RunExclusiveAsync(
                    _store, "busy", "me", () => Task.CompletedTask, NullLogger.Instance,
                    ttl: TimeSpan.FromSeconds(30), maxWait: TimeSpan.FromMilliseconds(300)));
        }
    }
}
