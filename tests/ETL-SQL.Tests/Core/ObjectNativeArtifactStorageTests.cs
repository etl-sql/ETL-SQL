using System.Collections.Concurrent;
using System.Text;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Storage;
using Xunit;

namespace ETL_SQL.Tests.Core;

public sealed class ObjectNativeArtifactStorageTests
{
    [Fact]
    public async Task ConcurrentWriters_PublishOnlyCompleteContent()
    {
        var objects = new InMemoryObjectStore();
        var authority = new MemoryEpochs();
        var storage = new ObjectNativeArtifactStorage(objects, authority, authority);
        var first = Bytes("first-complete-payload");
        var second = Bytes("second-complete-payload");

        await Task.WhenAll(
            storage.PublishAsync(ArtifactArea.Datasets, "shared.bin", first, 7),
            storage.PublishAsync(ArtifactArea.Datasets, "shared.bin", second, 7));

        await using var read = await storage.OpenReadAsync(ArtifactArea.Datasets, "shared.bin");
        using var result = new MemoryStream();
        await read!.Content.CopyToAsync(result);
        Assert.Contains(Encoding.UTF8.GetString(result.ToArray()), new[] { "first-complete-payload", "second-complete-payload" });
    }

    [Fact]
    public async Task StaleFence_CannotReplaceNewerCommit()
    {
        var authority = new MemoryEpochs();
        var storage = new ObjectNativeArtifactStorage(new InMemoryObjectStore(), authority, authority);
        await storage.PublishAsync(ArtifactArea.Snapshots, "report.html", Bytes("new"), 12);
        await Assert.ThrowsAsync<FencedWriteException>(() =>
            storage.PublishAsync(ArtifactArea.Snapshots, "report.html", Bytes("stale"), 11));
        Assert.Equal("new", await ReadText(storage, ArtifactArea.Snapshots, "report.html"));
    }

    [Fact]
    public async Task PartialStagingWrite_NeverCreatesCommit()
    {
        var storage = new ObjectNativeArtifactStorage(
            new FaultStore(new InMemoryObjectStore(), FaultMode.FailStaging), new MemoryEpochs());
        await Assert.ThrowsAsync<IOException>(() =>
            storage.PublishAsync(ArtifactArea.Datasets, "partial.bin", Bytes("payload"), 1));
        Assert.Null(await storage.OpenReadAsync(ArtifactArea.Datasets, "partial.bin"));
    }

    [Fact]
    public async Task LostCommitResponse_IsReconciledByOperationIdentity()
    {
        var storage = new ObjectNativeArtifactStorage(
            new FaultStore(new InMemoryObjectStore(), FaultMode.LoseCommitResponse), new MemoryEpochs());
        var commit = await storage.PublishAsync(ArtifactArea.Datasets, "lost.bin", Bytes("survived"), 1);
        Assert.Equal("survived", await ReadText(storage, ArtifactArea.Datasets, "lost.bin"));
        Assert.False(string.IsNullOrWhiteSpace(commit.OperationId));
    }

    [Fact]
    public async Task ConditionalConflict_IsRetriedWithoutRepublishingPartialState()
    {
        var storage = new ObjectNativeArtifactStorage(
            new FaultStore(new InMemoryObjectStore(), FaultMode.ConflictCommitOnce), new MemoryEpochs());
        await storage.PublishAsync(ArtifactArea.Maps, "retry.csv", Bytes("a,b"), 1);
        Assert.Equal("a,b", await ReadText(storage, ArtifactArea.Maps, "retry.csv"));
    }

    [Fact]
    public async Task ProviderOutageBeforeCommit_LeavesArtifactInvisible()
    {
        var storage = new ObjectNativeArtifactStorage(
            new FaultStore(new InMemoryObjectStore(), FaultMode.OutageAtCommit), new MemoryEpochs());
        await Assert.ThrowsAsync<IOException>(() =>
            storage.PublishAsync(ArtifactArea.Datasets, "outage.bin", Bytes("uncommitted"), 1));
        Assert.Null(await storage.OpenReadAsync(ArtifactArea.Datasets, "outage.bin"));
    }

    [Fact]
    public async Task Reconciliation_ReportsMissingContent_AndCollectsAbandonedStaging()
    {
        var objects = new InMemoryObjectStore();
        var storage = new ObjectNativeArtifactStorage(objects, new MemoryEpochs());
        var commit = await storage.PublishAsync(ArtifactArea.Datasets, "missing.bin", Bytes("payload"), 1);
        await objects.DeleteAsync(commit.ObjectKey);
        await objects.PutAsync("etlsql/v1/staging/abandoned", Bytes("partial"), ObjectStoreWriteCondition.CreateOnly);

        var result = await storage.ReconcileAsync(TimeSpan.Zero, DateTimeOffset.UtcNow.AddSeconds(1));

        Assert.False(result.IsHealthy);
        Assert.Single(result.MissingObjects);
        Assert.Equal(1, result.DeletedStagingObjects);
    }

    [Fact]
    public async Task LargeContent_IsStreamedAndHashVerified()
    {
        var bytes = new byte[8 * 1024 * 1024];
        new Random(42).NextBytes(bytes);
        var storage = new ObjectNativeArtifactStorage(new InMemoryObjectStore(), new MemoryEpochs());
        var commit = await storage.PublishAsync(ArtifactArea.Datasets, "large.bin", new MemoryStream(bytes), 1);

        Assert.Equal(bytes.LongLength, commit.Length);
        await using var read = await storage.OpenReadAsync(ArtifactArea.Datasets, "large.bin");
        using var actual = new MemoryStream();
        await read!.Content.CopyToAsync(actual);
        Assert.Equal(bytes, actual.ToArray());
    }

    private static MemoryStream Bytes(string value) => new(Encoding.UTF8.GetBytes(value));
    private static async Task<string> ReadText(ObjectNativeArtifactStorage storage, ArtifactArea area, string path)
    {
        await using var read = await storage.OpenReadAsync(area, path);
        using var reader = new StreamReader(read!.Content, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private sealed class MemoryEpochs : IWriteEpochStore, IClusterLockStore
    {
        private readonly ConcurrentDictionary<string, long> _values = new();
        public Task<long> GetWriteEpochAsync(string scope, string key) =>
            Task.FromResult(_values.GetValueOrDefault($"{scope}/{key}"));
        public Task<bool> TryClaimWriteEpochAsync(string scope, string key, long token)
        {
            var accepted = false;
            _values.AddOrUpdate($"{scope}/{key}", _ => { accepted = true; return token; }, (_, old) =>
            {
                accepted = token >= old;
                return accepted ? token : old;
            });
            return Task.FromResult(accepted);
        }
        public Task<bool> TryAcquireLockAsync(string lockName, string owner, TimeSpan ttl) => Task.FromResult(true);
        public Task<bool> TryRenewLockAsync(string lockName, string owner, TimeSpan ttl) => Task.FromResult(true);
        public Task ReleaseLockAsync(string lockName, string owner) => Task.CompletedTask;
        public Task<string?> GetLockHolderAsync(string lockName) => Task.FromResult<string?>(null);
    }

    private enum FaultMode { FailStaging, LoseCommitResponse, ConflictCommitOnce, OutageAtCommit }

    private sealed class FaultStore(IObjectStore inner, FaultMode mode) : IObjectStore
    {
        private int _fired;
        public Task<ObjectStoreItem?> GetAsync(string key, CancellationToken ct = default) => inner.GetAsync(key, ct);
        public IAsyncEnumerable<ObjectStoreEntry> ListAsync(string prefix, CancellationToken ct = default) => inner.ListAsync(prefix, ct);
        public async Task<ObjectStoreWriteResult> PutAsync(string key, Stream content, ObjectStoreWriteCondition condition,
            IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default)
        {
            if (mode == FaultMode.FailStaging && key.Contains("/staging/") && Interlocked.Exchange(ref _fired, 1) == 0)
                throw new IOException("injected partial staging failure");
            if (key.Contains("/commits/") && Interlocked.Exchange(ref _fired, 1) == 0)
            {
                if (mode == FaultMode.ConflictCommitOnce) throw new ObjectStorePreconditionFailedException("injected race");
                if (mode == FaultMode.OutageAtCommit) throw new IOException("provider unavailable");
                if (mode == FaultMode.LoseCommitResponse)
                {
                    var result = await inner.PutAsync(key, content, condition, metadata, ct);
                    throw new IOException($"response lost after {result.Version}");
                }
            }
            return await inner.PutAsync(key, content, condition, metadata, ct);
        }
        public Task<ObjectStoreWriteResult> CopyAsync(string sourceKey, string destinationKey, ObjectStoreWriteCondition condition,
            IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default) =>
            inner.CopyAsync(sourceKey, destinationKey, condition, metadata, ct);
        public Task<bool> DeleteAsync(string key, string? ifVersion = null, CancellationToken ct = default) =>
            inner.DeleteAsync(key, ifVersion, ct);
    }
}
