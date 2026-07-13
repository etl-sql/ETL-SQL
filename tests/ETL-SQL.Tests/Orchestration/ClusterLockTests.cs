using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Data;
using ETL_SQL.Orchestrator.Scheduling;
using ETL_SQL.Orchestrator.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
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
        public async Task RunExclusive_RetriesTransientReleaseFailure()
        {
            var releaseAttempts = 0;
            var store = new Mock<IClusterLockStore>();
            store.Setup(s => s.TryAcquireLockAsync("migration", "node-a", It.IsAny<TimeSpan>()))
                .ReturnsAsync(true);
            store.Setup(s => s.ReleaseLockAsync("migration", "node-a"))
                .Returns(() => ++releaseAttempts == 1
                    ? Task.FromException(new IOException("database is locked"))
                    : Task.CompletedTask);
            store.Setup(s => s.GetLockHolderAsync("migration"))
                .ReturnsAsync((string?)null);

            await ClusterLock.RunExclusiveAsync(
                store.Object, "migration", "node-a", () => Task.CompletedTask,
                NullLogger.Instance, ttl: TimeSpan.FromSeconds(30));

            Assert.Equal(2, releaseAttempts);
        }

        [Fact]
        public async Task IntervalGatedSend_ExactlyOneWinnerPerInterval_AndRestartSafe()
        {
            // The Portal operational-metrics digest gates its send cadence on this exact pattern:
            // TryAcquireLockAsync(name, owner, ttl = interval), never renewed, never released — the
            // TTL expiring is what re-enables the next interval's send. Prove the cluster contract:
            //   1) when N nodes race, exactly one wins the interval;
            //   2) losers polling within the interval never win;
            //   3) a restarted node (fresh owner id, as the digest generates per process) cannot
            //      re-send within the interval — restart safety;
            //   4) after the interval elapses, the next attempt wins again.
            await _store.InitializeAsync();
            const string lockName = "portal-operational-digest";
            var interval = TimeSpan.FromMilliseconds(300);

            // 1) Five nodes race for the same interval.
            var owners = new[] { "node-0", "node-1", "node-2", "node-3", "node-4" };
            var races = await Task.WhenAll(
                owners.Select(o => _store.TryAcquireLockAsync(lockName, o, interval)));
            Assert.Equal(1, races.Count(won => won));

            // 2) Losers re-polling inside the interval still lose.
            var holder = await _store.GetLockHolderAsync(lockName);
            foreach (var o in owners.Where(o => o != holder))
                Assert.False(await _store.TryAcquireLockAsync(lockName, o, interval));

            // 3) A restart mints a new owner id; it must NOT win mid-interval (no duplicate digest).
            Assert.False(await _store.TryAcquireLockAsync(lockName, "node-restarted-" + Guid.NewGuid().ToString("N"), interval));

            // 4) Once the interval (TTL) lapses, the next poll sends again.
            await Task.Delay(interval + TimeSpan.FromMilliseconds(200));
            Assert.True(await _store.TryAcquireLockAsync(lockName, "node-next-interval", interval));
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
