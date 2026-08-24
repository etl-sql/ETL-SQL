using System;
using System.IO;
using System.Threading.Tasks;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Storage;
using ETL_SQL.Orchestrator.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ETL_SQL.Tests.Orchestration
{
    /// <summary>
    /// Practical HA P1.8: database-backed write-epoch fencing for shared storage. The shared store is the
    /// fencing authority — a stale (lower) token cannot claim a resource a newer token already wrote, and
    /// the <see cref="FencedArtifactStorage"/> decorator turns that into a refused write so a paused-then-
    /// resumed node can't overwrite a newer node's artifact on SMB/UNC.
    /// </summary>
    public sealed class WriteEpochFencingTests : IDisposable
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"epoch_{Guid.NewGuid():N}.db");
        private readonly SQLiteJobHistoryStore _store;

        public WriteEpochFencingTests() => _store = new SQLiteJobHistoryStore(_dbPath);

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            foreach (var p in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
                try { if (File.Exists(p)) File.Delete(p); } catch { /* best-effort */ }
        }

        [Fact]
        public async Task ClaimEpoch_AdvancesForward_AndRejectsStale()
        {
            await _store.InitializeAsync();

            Assert.True(await _store.TryClaimWriteEpochAsync("artifact", "Datasets/a.parquet", 5));
            Assert.Equal(5, await _store.GetWriteEpochAsync("artifact", "Datasets/a.parquet"));

            // A newer token advances the epoch; the same token is idempotent.
            Assert.True(await _store.TryClaimWriteEpochAsync("artifact", "Datasets/a.parquet", 7));
            Assert.True(await _store.TryClaimWriteEpochAsync("artifact", "Datasets/a.parquet", 7));

            // A stale token (older than the current epoch) is rejected and does not move the epoch back.
            Assert.False(await _store.TryClaimWriteEpochAsync("artifact", "Datasets/a.parquet", 6));
            Assert.Equal(7, await _store.GetWriteEpochAsync("artifact", "Datasets/a.parquet"));
        }

        [Fact]
        public async Task Scopes_AndKeys_AreIndependent()
        {
            await _store.InitializeAsync();
            Assert.True(await _store.TryClaimWriteEpochAsync("artifact", "Snapshots/x", 10));
            Assert.True(await _store.TryClaimWriteEpochAsync("artifact", "Snapshots/y", 1));   // different key
            Assert.True(await _store.TryClaimWriteEpochAsync("other", "Snapshots/x", 1));       // different scope
            Assert.Equal(0, await _store.GetWriteEpochAsync("artifact", "never-written"));
        }

        [Fact]
        public async Task FencedStorage_RejectsStaleWriter_AfterNewerNodeWrote()
        {
            await _store.InitializeAsync();
            var inner = new InMemoryArtifactStorage();

            // Node B (newer, token 9) writes the snapshot first.
            var nodeB = new FencedArtifactStorage(inner, _store, () => 9);
            await nodeB.WriteAllTextAsync(ArtifactArea.Snapshots, "report.html", "from-B");

            // Node A resumes from a pause with an older token 4 and tries to overwrite — fenced out.
            var nodeA = new FencedArtifactStorage(inner, _store, () => 4);
            await Assert.ThrowsAsync<FencedWriteException>(() =>
                nodeA.WriteAllTextAsync(ArtifactArea.Snapshots, "report.html", "from-A-stale"));

            // B's content is intact; B can still re-write (same/forward token).
            Assert.Equal("from-B", await inner.ReadAllTextAsync(ArtifactArea.Snapshots, "report.html"));
            await nodeB.WriteAllTextAsync(ArtifactArea.Snapshots, "report.html", "from-B-2");
            Assert.Equal("from-B-2", await inner.ReadAllTextAsync(ArtifactArea.Snapshots, "report.html"));
        }

        [Fact]
        public async Task FencedStorage_GuardsMoveDestination()
        {
            await _store.InitializeAsync();
            var inner = new InMemoryArtifactStorage();

            // Newer node claims the final dataset path.
            await new FencedArtifactStorage(inner, _store, () => 8)
                .WriteAllTextAsync(ArtifactArea.Datasets, "final.parquet", "new");

            // Stale node stages then tries to promote onto the same final name — the move is fenced.
            var stale = new FencedArtifactStorage(inner, _store, () => 3);
            await stale.WriteAllTextAsync(ArtifactArea.Datasets, ".staging", "old"); // distinct key, allowed
            await Assert.ThrowsAsync<FencedWriteException>(() =>
                stale.MoveAsync(ArtifactArea.Datasets, ".staging", "final.parquet"));
        }

        [Fact]
        public async Task CoordinatedStorage_AllowsHealthyNodesToAlternateWrites()
        {
            await _store.InitializeAsync();
            var inner = new InMemoryArtifactStorage();
            var nodeA = new FencedArtifactStorage(inner, _store, _store);
            var nodeB = new FencedArtifactStorage(inner, _store, _store);

            await nodeA.WriteAllTextAsync(ArtifactArea.Snapshots, "shared.html", "A1");
            await nodeB.WriteAllTextAsync(ArtifactArea.Snapshots, "shared.html", "B1");
            await nodeA.WriteAllTextAsync(ArtifactArea.Snapshots, "shared.html", "A2");

            Assert.Equal("A2", await inner.ReadAllTextAsync(ArtifactArea.Snapshots, "shared.html"));
            Assert.Equal(3, await _store.GetWriteEpochAsync("artifact", "Snapshots/shared.html"));
        }

        [Fact]
        public async Task CoordinatedStorage_FencesDeleteAndAdvancesEpoch()
        {
            await _store.InitializeAsync();
            var inner = new InMemoryArtifactStorage();
            var nodeA = new FencedArtifactStorage(inner, _store, _store);
            var nodeB = new FencedArtifactStorage(inner, _store, _store);

            await nodeA.WriteAllTextAsync(ArtifactArea.Datasets, "shared.parquet", "data");
            Assert.True(await nodeB.DeleteAsync(ArtifactArea.Datasets, "shared.parquet"));

            Assert.False(await inner.ExistsAsync(ArtifactArea.Datasets, "shared.parquet"));
            Assert.Equal(2, await _store.GetWriteEpochAsync("artifact", "Datasets/shared.parquet"));
        }

        [Fact]
        public async Task ObjectNativeStorage_UsesDatabaseLockAndMonotonicEpochForSharedMutations()
        {
            await _store.InitializeAsync();
            var objects = new InMemoryObjectStore();
            var nodeA = new ObjectNativeArtifactStorage(objects, _store, _store);
            var nodeB = new ObjectNativeArtifactStorage(objects, _store, _store);
            var adapterA = new ObjectNativeArtifactStorageAdapter(nodeA);
            var adapterB = new ObjectNativeArtifactStorageAdapter(nodeB);

            await Task.WhenAll(
                adapterA.WriteAllTextAsync(ArtifactArea.Snapshots, "object.etlsnap", "A"),
                adapterB.WriteAllTextAsync(ArtifactArea.Snapshots, "object.etlsnap", "B"));

            Assert.Contains(await adapterA.ReadAllTextAsync(ArtifactArea.Snapshots, "object.etlsnap"), new[] { "A", "B" });
            Assert.Equal(2, await _store.GetWriteEpochAsync("object-artifact", "Snapshots/object.etlsnap"));
        }

        [Fact]
        public async Task ExplicitStaleToken_CannotDeleteNewerArtifact()
        {
            await _store.InitializeAsync();
            var inner = new InMemoryArtifactStorage();
            await new FencedArtifactStorage(inner, _store, () => 8)
                .WriteAllTextAsync(ArtifactArea.Datasets, "protected.parquet", "new");

            var stale = new FencedArtifactStorage(inner, _store, () => 3);
            await Assert.ThrowsAsync<FencedWriteException>(() =>
                stale.DeleteAsync(ArtifactArea.Datasets, "protected.parquet"));
            Assert.True(await inner.ExistsAsync(ArtifactArea.Datasets, "protected.parquet"));
        }
    }
}
