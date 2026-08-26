using System.Text;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Portability;
using ETL_SQL.Core.Storage;
using ETL_SQL.Orchestrator.Storage;
using Xunit;

namespace ETL_SQL.Tests.Portability;

public sealed class TenantPortabilityScaleTests
{
    [Fact]
    public async Task ConcurrentSnapshotRetriesUntilDatabaseAndArtifactBoundaryIsStable()
    {
        var source = new MovingConsistencySource();
        var point = await TenantExportConsistencyCoordinator.CaptureAsync(
            source, "tenant-a", "export-1", finalCutover: false,
            DateTimeOffset.Parse("2026-08-24T12:00:00Z"));
        Assert.Equal("portal-2", point.Revisions.Single(x => x.System == "portal").Revision);
        Assert.True(TenantExportConsistencyCoordinator.Verify(point));
        Assert.True(source.RevisionReads >= 4);
    }

    [Fact]
    public async Task FinalConsistencyPointCarriesDurableFence()
    {
        var source = new MovingConsistencySource(stable: true);
        var point = await TenantExportConsistencyCoordinator.CaptureAsync(
            source, "tenant-a", "cutover-1", finalCutover: true, DateTimeOffset.UtcNow);
        Assert.True(point.MutationsFenced);
        Assert.Equal(41, point.FenceEpoch);
    }

    [Fact]
    public void InventoryRejectsForeignRowsAndRequiresOwnersHashesAndExplicitExclusions()
    {
        var result = TenantPortabilityInventory.Reconcile("tenant-a",
        [
            new("report:1", "report", TenantInventoryDisposition.Included, 3, new string('a', 64),
                "user:owner", ["group:analysts:read"], null, null, "tenant-a"),
            new("report:2", "report", TenantInventoryDisposition.Included, 3, null,
                null, [], null, null, "tenant-b"),
            new("session:1", "session", TenantInventoryDisposition.Excluded, 0, null,
                null, [], null, null, "tenant-a")
        ]);
        Assert.False(result.IsComplete);
        Assert.Contains(result.Errors, x => x.Contains("foreign tenant", StringComparison.Ordinal));
        Assert.Contains(result.Errors, x => x.Contains("ownership", StringComparison.Ordinal));
        Assert.Contains(result.Errors, x => x.Contains("explicit reason", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ChunkedTransferResumesCommittedChunksAndReassemblesExactContent()
    {
        var objects = new CountingObjectStore();
        var epochs = new MemoryAuthority();
        var storage = new ObjectNativeArtifactStorage(objects, epochs, epochs);
        var content = Enumerable.Range(0, 300_000).Select(i => (byte)(i % 251)).ToArray();

        TenantChunkedContent first;
        await using (var input = new MemoryStream(content, writable: false))
            first = await TenantChunkTransfer.ExportAsync(storage, "tenant-a", "export-1", "dataset:large",
                input, 7, chunkSize: 64 * 1024);
        var writes = objects.PutCount;
        await using (var retry = new MemoryStream(content, writable: false))
            await TenantChunkTransfer.ExportAsync(storage, "tenant-a", "export-1", "dataset:large",
                retry, 7, chunkSize: 64 * 1024);
        Assert.Equal(writes, objects.PutCount);

        await using var output = new MemoryStream();
        await TenantChunkTransfer.ImportAsync(storage, first, output);
        Assert.Equal(content, output.ToArray());
        Assert.True(first.Chunks.Count > 1);
    }

    [Fact]
    public async Task TargetCannotAcquireAuthorityUntilSourceExecutionsDrain()
    {
        var store = new MemoryCutoverStore(new("tenant-a", "", TenantExecutionAuthorityLocation.Source,
            9, true, false, 1, DateTimeOffset.UtcNow));
        var fenced = await TenantCutoverAuthority.FenceSourceAsync(store, "tenant-a", "move-1", DateTimeOffset.UtcNow);
        Assert.False(fenced.SourceSchedulesEnabled);
        await Assert.ThrowsAsync<InvalidOperationException>(() => TenantCutoverAuthority.TransferToTargetAsync(
            store, "tenant-a", "move-1", fenced.FenceEpoch, DateTimeOffset.UtcNow));
        store.State = store.State with { SourceActiveExecutions = 0 };
        var target = await TenantCutoverAuthority.TransferToTargetAsync(
            store, "tenant-a", "move-1", fenced.FenceEpoch, DateTimeOffset.UtcNow);
        Assert.Equal(TenantExecutionAuthorityLocation.Target, target.Authority);
        Assert.True(target.TargetSchedulesEnabled);
        Assert.False(target.SourceSchedulesEnabled);
    }

    [Fact]
    public async Task ScaleBundleBindsConsistencyInventoryContentAndDeltaBase()
    {
        var source = new MovingConsistencySource(stable: true);
        var point = await TenantExportConsistencyCoordinator.CaptureAsync(
            source, "tenant-a", "delta-1", finalCutover: false, DateTimeOffset.UtcNow);
        var bytes = Encoding.UTF8.GetBytes("CREATE FOLDER 'Sales';");
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        var root = Path.Combine(Path.GetTempPath(), $"phase2-bundle-{Guid.NewGuid():N}");
        try
        {
            var manifest = await TenantBundleWriter.WriteAsync(root, new TenantBundleRequest(
                "bundle-2", DateTimeOffset.UtcNow, "0.19.0", "Shared", "tenant-a",
                TenantBundleExportMode.IncrementalDelta, point.Digest,
                [new TenantBundlePayload("catalog:portal", "catalog", "application/x-etlsql",
                    "catalog/portal.etlsql", bytes, [])], [], [],
                DeclaredConsistencyPoint: point,
                Inventory: [new("catalog:portal", "catalog", TenantInventoryDisposition.Included,
                    bytes.Length, hash, "user:owner", ["group:admins:owner"], null, null, "tenant-a")],
                BaseConsistencyPointDigest: new string('b', 64)));
            Assert.Equal(TenantBundle.Phase2SchemaVersion, manifest.SchemaVersion);
            var result = await TenantBundleValidator.ValidateAsync(root);
            Assert.True(result.IsValid, string.Join("; ", result.Findings.Select(x => x.Message)));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task DeltaSequenceRejectsMixedTenantReplayAndOutOfOrderFinalDelta()
    {
        var source = new MovingConsistencySource(stable: true);
        var point = await TenantExportConsistencyCoordinator.CaptureAsync(
            source, "tenant-a", "delta-1", false, DateTimeOffset.UtcNow);
        var manifest = new TenantBundleManifest(TenantBundle.Phase2SchemaVersion, "b", DateTimeOffset.UtcNow,
            "0.19", "Shared", "tenant-a", TenantBundleExportMode.FinalCutoverDelta, point.Digest,
            [], [], [], new TenantBundleCounts(new Dictionary<string, int>(), new Dictionary<string, int>()),
            DeclaredConsistencyPoint: point, Inventory: [], BaseConsistencyPointDigest: "base");
        var result = TenantDeltaSequence.Validate("tenant-a", "base", [manifest, manifest]);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.Contains("last bundle", StringComparison.Ordinal));
        Assert.Contains(result.Errors, x => x.Contains("replay", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HostileSharedSourceForeignMarkerFailsBeforeBundlePublication()
    {
        var source = new MovingConsistencySource(stable: true);
        var point = await TenantExportConsistencyCoordinator.CaptureAsync(
            source, "tenant-a", "shared-export", false, DateTimeOffset.UtcNow);
        var bytes = Encoding.UTF8.GetBytes("foreign-marker-tenant-b");
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        var root = Path.Combine(Path.GetTempPath(), $"shared-hostile-{Guid.NewGuid():N}");
        try
        {
            var request = new TenantBundleRequest("bundle-hostile", DateTimeOffset.UtcNow, "0.19", "Shared",
                "tenant-a", TenantBundleExportMode.FullEligibleTenantExport, point.Digest,
                [new TenantBundlePayload("report:foreign", "report", "application/json",
                    "catalog/foreign.json", bytes, [])], [], [],
                DeclaredConsistencyPoint: point,
                Inventory: [new("report:foreign", "report", TenantInventoryDisposition.Included,
                    bytes.Length, hash, "user:foreign", ["tenant-b:owner"], null, null, "tenant-b")]);
            var error = await Assert.ThrowsAsync<ArgumentException>(() => TenantBundleWriter.WriteAsync(root, request));
            Assert.Contains("foreign tenant", error.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(root, TenantBundle.ManifestFileName)));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RelationalCutoverFenceIsDurableTenantScopedAndBlocksNewSourceLeases()
    {
        var database = Path.Combine(Path.GetTempPath(), $"cutover-{Guid.NewGuid():N}.db");
        try
        {
            var store = new RelationalJobHistoryStore(new SqliteOrchestratorDialect($"Data Source={database}"));
            await store.SaveJobAsync(new JobDefinition("alpha-job", "SELECT 1;", 1, "DAY",
                null, null, null, IsEnabled: true, TenantId: "tenant-a"));
            await store.SaveJobAsync(new JobDefinition("beta-job", "SELECT 2;", 1, "DAY",
                null, null, null, IsEnabled: true, TenantId: "tenant-b"));
            var alpha = (await store.GetJobAsync("tenant-a", "alpha-job"))!;
            var beta = (await store.GetJobAsync("tenant-b", "beta-job"))!;
            Assert.NotNull(await store.AcquireJobLeaseAsync(alpha.Id, "source-node", TimeSpan.FromMinutes(5)));
            await store.EnsureTenantCutoverSourceAuthorityAsync(
                ETL_SQL.Core.Multitenancy.TenantContext.FromVerifiedCredential("tenant-a"), DateTimeOffset.UtcNow);

            var fenced = await TenantCutoverAuthority.FenceSourceAsync(
                store, "tenant-a", "move-durable", DateTimeOffset.UtcNow);
            Assert.Equal(TenantExecutionAuthorityLocation.None, fenced.Authority);
            Assert.False((await store.GetJobAsync("tenant-a", "alpha-job"))!.IsEnabled);
            Assert.True((await store.GetJobAsync("tenant-b", "beta-job"))!.IsEnabled);
            Assert.Null(await store.AcquireJobLeaseAsync(alpha.Id, "late-source-node", TimeSpan.FromMinutes(5)));
            Assert.NotNull(await store.AcquireJobLeaseAsync(beta.Id, "beta-node", TimeSpan.FromMinutes(5)));

            await Assert.ThrowsAsync<InvalidOperationException>(() => TenantCutoverAuthority.TransferToTargetAsync(
                store, "tenant-a", "move-durable", fenced.FenceEpoch, DateTimeOffset.UtcNow));
            await store.ReleaseJobLeaseAsync(alpha.Id, "source-node");
            var target = await TenantCutoverAuthority.TransferToTargetAsync(
                store, "tenant-a", "move-durable", fenced.FenceEpoch, DateTimeOffset.UtcNow);
            Assert.Equal(TenantExecutionAuthorityLocation.Target, target.Authority);

            var restarted = new RelationalJobHistoryStore(new SqliteOrchestratorDialect($"Data Source={database}"));
            var durable = await restarted.ReadAsync("tenant-a");
            Assert.Equal(TenantExecutionAuthorityLocation.Target, durable!.Authority);
            Assert.True(durable.TargetSchedulesEnabled);
            Assert.False(durable.SourceSchedulesEnabled);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(database)) File.Delete(database);
        }
    }

    private sealed class MovingConsistencySource(bool stable = false) : ITenantExportConsistencySource
    {
        public int RevisionReads { get; private set; }
        public Task<long> FenceMutationsAsync(string tenantId, string operationId, CancellationToken ct = default) => Task.FromResult(41L);
        public Task<IReadOnlyList<string>> ReadArtifactCommitIdsAsync(string tenantId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>(["artifact-b", "artifact-a"]);
        public Task<IReadOnlyList<TenantExportRevision>> ReadRevisionsAsync(string tenantId, CancellationToken ct = default)
        {
            RevisionReads++;
            var revision = stable || RevisionReads > 2 ? "portal-2" : $"portal-{RevisionReads}";
            return Task.FromResult<IReadOnlyList<TenantExportRevision>>([new("portal", revision), new("orchestrator", "orch-7")]);
        }
    }

    private sealed class MemoryCutoverStore(TenantCutoverState state) : ITenantCutoverStateStore
    {
        public TenantCutoverState State { get; set; } = state;
        public Task<TenantCutoverState?> ReadAsync(string tenantId, CancellationToken ct = default) => Task.FromResult<TenantCutoverState?>(State);
        public Task<bool> TryWriteAsync(TenantCutoverState? expected, TenantCutoverState next, CancellationToken ct = default)
        {
            if (State != expected) return Task.FromResult(false);
            State = next;
            return Task.FromResult(true);
        }
    }

    private sealed class MemoryAuthority : IWriteEpochStore, IClusterLockStore
    {
        private readonly Dictionary<string, long> _epochs = [];
        private readonly HashSet<string> _locks = [];
        public Task<long> GetWriteEpochAsync(string scope, string key) => Task.FromResult(_epochs.GetValueOrDefault($"{scope}:{key}"));
        public Task<bool> TryClaimWriteEpochAsync(string scope, string key, long epoch)
        {
            var k = $"{scope}:{key}"; var current = _epochs.GetValueOrDefault(k);
            if (epoch < current) return Task.FromResult(false); _epochs[k] = epoch; return Task.FromResult(true);
        }
        public Task<bool> TryAcquireLockAsync(string name, string owner, TimeSpan duration)
        { lock (_locks) return Task.FromResult(_locks.Add(name)); }
        public Task<bool> TryRenewLockAsync(string name, string owner, TimeSpan duration) => Task.FromResult(true);
        public Task ReleaseLockAsync(string name, string owner) { lock (_locks) _locks.Remove(name); return Task.CompletedTask; }
        public Task<string?> GetLockHolderAsync(string name) =>
            Task.FromResult<string?>(_locks.Contains(name) ? "test-owner" : null);
    }

    private sealed class CountingObjectStore : IObjectStore
    {
        private readonly InMemoryObjectStore _inner = new();
        private int _putCount;
        public int PutCount => _putCount;
        public Task<ObjectStoreItem?> GetAsync(string key, CancellationToken ct = default) => _inner.GetAsync(key, ct);
        public IAsyncEnumerable<ObjectStoreEntry> ListAsync(string prefix, CancellationToken ct = default) => _inner.ListAsync(prefix, ct);
        public Task<ObjectStoreWriteResult> PutAsync(string key, Stream content, ObjectStoreWriteCondition condition,
            IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default)
        { Interlocked.Increment(ref _putCount); return _inner.PutAsync(key, content, condition, metadata, ct); }
        public Task<ObjectStoreWriteResult> CopyAsync(string sourceKey, string destinationKey, ObjectStoreWriteCondition condition,
            IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default) =>
            _inner.CopyAsync(sourceKey, destinationKey, condition, metadata, ct);
        public Task<bool> DeleteAsync(string key, string? ifVersion = null, CancellationToken ct = default) => _inner.DeleteAsync(key, ifVersion, ct);
    }
}
