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

        [Fact]
        public async Task HeartbeatService_PrunesExpiredNodes_OnItsLoop()
        {
            var store = await NewStoreAsync();
            // A node that died without deregistering, already past its TTL.
            await store.RegisterOrRenewNodeAsync("dead-node", "Orchestrator", TimeSpan.FromMilliseconds(20));
            await Task.Delay(60);
            Assert.Contains(await store.GetAllNodesAsync(), n => n.NodeId == "dead-node");

            var service = new NodeHeartbeatService(
                store, new ConfigurationBuilder().Build(), NullLogger<NodeHeartbeatService>.Instance, "Portal");
            await service.StartAsync(CancellationToken.None);
            try
            {
                // The first heartbeat loop both registers this node and prunes the expired one.
                var pruned = false;
                for (var i = 0; i < 40 && !pruned; i++)
                {
                    pruned = !(await store.GetAllNodesAsync()).Any(n => n.NodeId == "dead-node");
                    if (!pruned) await Task.Delay(25);
                }
                Assert.True(pruned, "Expected the heartbeat loop to prune the expired node row.");
            }
            finally
            {
                await service.StopAsync(CancellationToken.None);
            }
        }

        [Fact]
        public async Task HeartbeatService_NotifiesHandlers_WhenLocalLeaseExpires()
        {
            var store = new FailingAfterFirstHeartbeatStore();
            var handler = new CapturingLeaseLossHandler();
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Cluster:NodeHeartbeatSeconds"] = "1",
                    ["Cluster:NodeHeartbeatMinimumSeconds"] = "1",
                    ["Cluster:NodeHeartbeatMinimumIntervalSeconds"] = "1"
                })
                .Build();
            var service = new NodeHeartbeatService(
                store,
                config,
                NullLogger<NodeHeartbeatService>.Instance,
                "Portal",
                [handler]);

            await service.StartAsync(CancellationToken.None);
            try
            {
                var completed = await Task.WhenAny(handler.Lost.Task, Task.Delay(TimeSpan.FromSeconds(5)));
                Assert.Same(handler.Lost.Task, completed);
                var loss = await handler.Lost.Task;
                Assert.Equal(service.NodeId, loss.NodeId);
                Assert.Equal("Portal", loss.Role);
                Assert.Contains("expired", loss.Reason, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                await service.StopAsync(CancellationToken.None);
            }
        }

        private sealed class FailingAfterFirstHeartbeatStore : INodeRegistryStore
        {
            private int _heartbeats;

            public Task RegisterOrRenewNodeAsync(
                string nodeId, string role, TimeSpan ttl, string? metadata = null)
            {
                if (Interlocked.Increment(ref _heartbeats) > 1)
                    throw new InvalidOperationException("simulated registry partition");
                return Task.CompletedTask;
            }

            public Task<IReadOnlyList<NodeHeartbeat>> GetLiveNodesAsync() =>
                Task.FromResult<IReadOnlyList<NodeHeartbeat>>([]);

            public Task<IReadOnlyList<NodeHeartbeat>> GetAllNodesAsync() =>
                Task.FromResult<IReadOnlyList<NodeHeartbeat>>([]);

            public Task DeregisterNodeAsync(string nodeId) => Task.CompletedTask;

            public Task<int> PruneExpiredNodesAsync() => Task.FromResult(0);
        }

        private sealed class CapturingLeaseLossHandler : INodeLeaseLossHandler
        {
            public TaskCompletionSource<(string NodeId, string Role, string Reason)> Lost { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public Task OnNodeLeaseLostAsync(string nodeId, string role, string reason, CancellationToken ct)
            {
                Lost.TrySetResult((nodeId, role, reason));
                return Task.CompletedTask;
            }
        }
    }
}
