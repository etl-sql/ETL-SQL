using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Data;
using ETL_SQL.Orchestrator.Scheduling;
using ETL_SQL.Orchestrator.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ETL_SQL.Tests.Orchestration
{
    /// <summary>
    /// Practical HA P1.7: the database-backed node registry and its heartbeat service, against SQLite
    /// (the default store). The cross-provider behavior is also exercised on real PostgreSQL in
    /// <see cref="OrchestratorPostgresStoreTests"/>.
    /// </summary>
    public sealed class NodeRegistryTests : IDisposable
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"node_reg_{Guid.NewGuid():N}.db");

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            foreach (var p in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
                try { if (File.Exists(p)) File.Delete(p); } catch { /* best-effort */ }
        }

        private async Task<RelationalJobHistoryStore> NewStoreAsync()
        {
            var store = new SQLiteJobHistoryStore(_dbPath);
            await store.InitializeAsync();
            return store;
        }

        [Fact]
        public async Task Register_MakesNodeLive_AndRenewPreservesFirstSeen()
        {
            var store = await NewStoreAsync();
            await store.RegisterOrRenewNodeAsync("node-A", "Portal", TimeSpan.FromMinutes(5), "{\"v\":1}");

            var live = await store.GetLiveNodesAsync();
            var node = Assert.Single(live);
            Assert.Equal("node-A", node.NodeId);
            Assert.Equal("Portal", node.Role);
            Assert.Equal("{\"v\":1}", node.Metadata);
            var firstSeen = node.FirstSeenUtc;

            // A renewal pushes the heartbeat/expiry forward but preserves the original first-seen time.
            await Task.Delay(20);
            await store.RegisterOrRenewNodeAsync("node-A", "Portal", TimeSpan.FromMinutes(5));
            var renewed = Assert.Single(await store.GetLiveNodesAsync());
            Assert.Equal(firstSeen, renewed.FirstSeenUtc);
            Assert.True(renewed.LastHeartbeatUtc >= firstSeen);
        }

        [Fact]
        public async Task ExpiredNodes_AreNotLive_ButRemainUntilPruned()
        {
            var store = await NewStoreAsync();
            await store.RegisterOrRenewNodeAsync("ttl-node", "Orchestrator", TimeSpan.FromMilliseconds(40));
            await Task.Delay(120);

            Assert.Empty(await store.GetLiveNodesAsync());
            Assert.Single(await store.GetAllNodesAsync()); // still present, just stale

            Assert.Equal(1, await store.PruneExpiredNodesAsync());
            Assert.Empty(await store.GetAllNodesAsync());
        }

        [Fact]
        public async Task MultipleNodes_AreAllTracked()
        {
            var store = await NewStoreAsync();
            await store.RegisterOrRenewNodeAsync("p1", "Portal", TimeSpan.FromMinutes(5));
            await store.RegisterOrRenewNodeAsync("p2", "Portal", TimeSpan.FromMinutes(5));
            await store.RegisterOrRenewNodeAsync("o1", "Orchestrator", TimeSpan.FromMinutes(5));

            var live = await store.GetLiveNodesAsync();
            Assert.Equal(3, live.Count);
            Assert.Equal(2, live.Count(n => n.Role == "Portal"));
            Assert.Equal(1, live.Count(n => n.Role == "Orchestrator"));
        }

        [Fact]
        public async Task Deregister_RemovesNodeImmediately()
        {
            var store = await NewStoreAsync();
            await store.RegisterOrRenewNodeAsync("bye", "Portal", TimeSpan.FromMinutes(5));
            Assert.Single(await store.GetLiveNodesAsync());

            await store.DeregisterNodeAsync("bye");
            Assert.Empty(await store.GetLiveNodesAsync());
            await store.DeregisterNodeAsync("bye"); // idempotent — unknown node is a no-op
        }

        [Fact]
        public async Task HeartbeatService_RegistersOnStart_AndDeregistersOnStop()
        {
            var store = await NewStoreAsync();
            var service = new NodeHeartbeatService(
                store, new ConfigurationBuilder().Build(), NullLogger<NodeHeartbeatService>.Instance, "Portal");

            await service.StartAsync(CancellationToken.None);
            try
            {
                // The first heartbeat runs on the background loop; wait briefly for it to land.
                NodeHeartbeat? node = null;
                for (var i = 0; i < 40 && node is null; i++)
                {
                    node = (await store.GetLiveNodesAsync()).FirstOrDefault(n => n.NodeId == service.NodeId);
                    if (node is null) await Task.Delay(25);
                }
                Assert.NotNull(node);
                Assert.Equal("Portal", node!.Role);
            }
            finally
            {
                await service.StopAsync(CancellationToken.None);
            }

            Assert.DoesNotContain(await store.GetAllNodesAsync(), n => n.NodeId == service.NodeId);
        }
    }
}
