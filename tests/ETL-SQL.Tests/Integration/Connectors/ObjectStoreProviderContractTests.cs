using System.Collections.Concurrent;
using System.Text;
using Azure.Storage.Blobs;
using ETL_SQL.Connectors.ObjectStorage;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Portability;
using ETL_SQL.Core.Storage;
using Xunit;

namespace ETL_SQL.Tests.Integration.Connectors;

internal static class ObjectStoreProviderContract
{
    public static async Task ConditionalWritesAndOpaqueVersions(IObjectStore store)
    {
        var key = $"contract/{Guid.NewGuid():N}";
        var first = await store.PutAsync(key, Bytes("one"), ObjectStoreWriteCondition.CreateOnly,
            new Dictionary<string, string> { ["kind"] = "contract" });
        await Assert.ThrowsAsync<ObjectStorePreconditionFailedException>(() =>
            store.PutAsync(key, Bytes("two"), ObjectStoreWriteCondition.CreateOnly));
        await Assert.ThrowsAsync<ObjectStorePreconditionFailedException>(() =>
            store.PutAsync(key, Bytes("two"), ObjectStoreWriteCondition.Match("definitely-stale")));

        var second = await store.PutAsync(key, Bytes("two"), ObjectStoreWriteCondition.Match(first.Version));
        Assert.NotEqual(first.Version, second.Version);
        await using var read = await store.GetAsync(key);
        using var reader = new StreamReader(read!.Content, Encoding.UTF8);
        Assert.Equal("two", await reader.ReadToEndAsync());
        Assert.True(await store.DeleteAsync(key, second.Version));
    }

    public static async Task ObjectNativePublication(IObjectStore store)
    {
        var authority = new Epochs();
        var storage = new ObjectNativeArtifactStorage(store, authority, authority, $"provider-{Guid.NewGuid():N}");
        var path = $"large/{Guid.NewGuid():N}.bin";
        var payload = new byte[2 * 1024 * 1024];
        new Random(7).NextBytes(payload);
        var commit = await storage.PublishAsync(ArtifactArea.Datasets, path, new MemoryStream(payload), 1);
        await using var read = await storage.OpenReadAsync(ArtifactArea.Datasets, path);
        using var actual = new MemoryStream();
        await read!.Content.CopyToAsync(actual);
        Assert.Equal(payload, actual.ToArray());
        Assert.Equal(payload.LongLength, commit.Length);
        Assert.True((await storage.ReconcileAsync(TimeSpan.Zero, DateTimeOffset.UtcNow.AddMinutes(1))).IsHealthy);
    }

    public static async Task HostileFailureMatrix(IObjectStore store)
    {
        var authority = new Epochs();
        var scope = $"hostile-{Guid.NewGuid():N}";
        var native = new ObjectNativeArtifactStorage(store, authority, authority, scope);
        var path = $"hostile/{Guid.NewGuid():N}.bin";

        await Task.WhenAll(
            native.PublishAsync(ArtifactArea.Datasets, path, Bytes("complete-a"), 1),
            native.PublishAsync(ArtifactArea.Datasets, path, Bytes("complete-b"), 1));
        await native.PublishAsync(ArtifactArea.Datasets, path, Bytes("newest"), 3);
        await Assert.ThrowsAsync<FencedWriteException>(() =>
            native.PublishAsync(ArtifactArea.Datasets, path, Bytes("stale"), 2));

        var lost = new ObjectNativeArtifactStorage(new FaultStore(store, Fault.LostResponse), authority, authority, scope + "-lost");
        await lost.PublishAsync(ArtifactArea.Datasets, path + ".lost", Bytes("committed"), 1);
        var retry = new ObjectNativeArtifactStorage(new FaultStore(store, Fault.ConflictOnce), authority, authority, scope + "-retry");
        await retry.PublishAsync(ArtifactArea.Datasets, path + ".retry", Bytes("retried"), 1);
        var partial = new ObjectNativeArtifactStorage(new FaultStore(store, Fault.PartialStaging), authority, authority, scope + "-partial");
        await Assert.ThrowsAsync<IOException>(() =>
            partial.PublishAsync(ArtifactArea.Datasets, path + ".partial", Bytes("partial"), 1));
        Assert.Null(await partial.OpenReadAsync(ArtifactArea.Datasets, path + ".partial"));
        var outage = new ObjectNativeArtifactStorage(new FaultStore(store, Fault.CommitOutage), authority, authority, scope + "-outage");
        await Assert.ThrowsAsync<IOException>(() =>
            outage.PublishAsync(ArtifactArea.Datasets, path + ".outage", Bytes("outage"), 1));
        Assert.Null(await outage.OpenReadAsync(ArtifactArea.Datasets, path + ".outage"));

        await store.PutAsync($"etlsql/v1/staging/{Guid.NewGuid():N}", Bytes("abandoned"), ObjectStoreWriteCondition.CreateOnly);
        Assert.True((await native.ReconcileAsync(TimeSpan.Zero, DateTimeOffset.UtcNow.AddMinutes(1))).DeletedStagingObjects > 0);
    }

    public static async Task ResumablePortabilityChunks(IObjectStore store)
    {
        var authority = new Epochs();
        var native = new ObjectNativeArtifactStorage(store, authority, authority, $"chunks-{Guid.NewGuid():N}");
        var payload = new byte[10 * 1024 * 1024 + 19];
        new Random(73).NextBytes(payload);
        var operation = $"export-{Guid.NewGuid():N}";
        TenantChunkedContent index;
        await using (var input = new MemoryStream(payload, writable: false))
            index = await TenantChunkTransfer.ExportAsync(native, "tenant-contract", operation,
                "dataset:large", input, 1, 1024 * 1024);
        await using (var retry = new MemoryStream(payload, writable: false))
            await TenantChunkTransfer.ExportAsync(native, "tenant-contract", operation,
                "dataset:large", retry, 1, 1024 * 1024);
        await using var output = new MemoryStream();
        await TenantChunkTransfer.ImportAsync(native, index, output);
        Assert.Equal(payload, output.ToArray());
        Assert.Equal(11, index.Chunks.Count);
    }

    private static MemoryStream Bytes(string value) => new(Encoding.UTF8.GetBytes(value));
    private sealed class Epochs : IWriteEpochStore, IClusterLockStore
    {
        private readonly ConcurrentDictionary<string, long> _epochs = new();
        public Task<long> GetWriteEpochAsync(string scope, string key) => Task.FromResult(_epochs.GetValueOrDefault(scope + key));
        public Task<bool> TryClaimWriteEpochAsync(string scope, string key, long token)
        {
            var accepted = false;
            _epochs.AddOrUpdate(scope + key, _ => { accepted = true; return token; }, (_, old) =>
            { accepted = token >= old; return accepted ? token : old; });
            return Task.FromResult(accepted);
        }
        public Task<bool> TryAcquireLockAsync(string lockName, string owner, TimeSpan ttl) => Task.FromResult(true);
        public Task<bool> TryRenewLockAsync(string lockName, string owner, TimeSpan ttl) => Task.FromResult(true);
        public Task ReleaseLockAsync(string lockName, string owner) => Task.CompletedTask;
        public Task<string?> GetLockHolderAsync(string lockName) => Task.FromResult<string?>(null);
    }

    private enum Fault { PartialStaging, LostResponse, ConflictOnce, CommitOutage }
    private sealed class FaultStore(IObjectStore inner, Fault fault) : IObjectStore
    {
        private int _fired;
        public Task<ObjectStoreItem?> GetAsync(string key, CancellationToken ct = default) => inner.GetAsync(key, ct);
        public IAsyncEnumerable<ObjectStoreEntry> ListAsync(string prefix, CancellationToken ct = default) => inner.ListAsync(prefix, ct);
        public async Task<ObjectStoreWriteResult> PutAsync(string key, Stream content, ObjectStoreWriteCondition condition,
            IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default)
        {
            if (fault == Fault.PartialStaging && key.Contains("/staging/") && Interlocked.Exchange(ref _fired, 1) == 0)
            {
                var fragment = new byte[4];
                var count = await content.ReadAsync(fragment, ct);
                await inner.PutAsync(key, new MemoryStream(fragment, 0, count, writable: false), condition, metadata, ct);
                throw new IOException("injected connection loss after a partial staging upload");
            }
            if (key.Contains("/commits/") && Interlocked.Exchange(ref _fired, 1) == 0)
            {
                if (fault == Fault.ConflictOnce) throw new ObjectStorePreconditionFailedException("injected conflict");
                if (fault == Fault.CommitOutage) throw new IOException("injected outage");
                if (fault == Fault.LostResponse)
                {
                    var result = await inner.PutAsync(key, content, condition, metadata, ct);
                    throw new IOException($"injected lost response after {result.Version}");
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

[Collection("S3 collection")]
public sealed class S3ObjectStoreProviderContractTests(S3Fixture fixture)
{
    private IObjectStore Create() => ObjectStoreProviderFactory.CreateS3(
        S3Fixture.BucketName, serviceUrl: fixture.ServiceUrl, forcePathStyle: true,
        accessKey: S3Fixture.AccessKey, secretKey: S3Fixture.SecretKey,
        prefix: $"artifact-{Guid.NewGuid():N}");
    [Fact] public Task ConditionalWritesAndOpaqueVersions() => ObjectStoreProviderContract.ConditionalWritesAndOpaqueVersions(Create());
    [Fact] public Task ObjectNativePublication() => ObjectStoreProviderContract.ObjectNativePublication(Create());
    [Fact] public Task HostileFailureMatrix() => ObjectStoreProviderContract.HostileFailureMatrix(Create());
    [Fact] public Task ResumablePortabilityChunks() => ObjectStoreProviderContract.ResumablePortabilityChunks(Create());
}

[Collection("AZURE_BLOB collection")]
public sealed class AzureBlobObjectStoreProviderContractTests(AzureBlobFixture fixture)
{
    private async Task<IObjectStore> CreateAsync()
    {
        var containerName = $"artifact-{Guid.NewGuid():N}";
        var container = fixture.CreateServiceClient().GetBlobContainerClient(containerName);
        await container.CreateAsync();
        return ObjectStoreProviderFactory.CreateAzureBlob(
            fixture.ValidConnectionString, containerName, $"prefix-{Guid.NewGuid():N}");
    }
    [Fact]
    public async Task ConditionalWritesAndOpaqueVersions() =>
        await ObjectStoreProviderContract.ConditionalWritesAndOpaqueVersions(await CreateAsync());
    [Fact]
    public async Task ObjectNativePublication() =>
        await ObjectStoreProviderContract.ObjectNativePublication(await CreateAsync());
    [Fact]
    public async Task HostileFailureMatrix() =>
        await ObjectStoreProviderContract.HostileFailureMatrix(await CreateAsync());
    [Fact]
    public async Task ResumablePortabilityChunks() =>
        await ObjectStoreProviderContract.ResumablePortabilityChunks(await CreateAsync());
}
