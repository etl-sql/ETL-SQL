using System.Collections.Concurrent;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Security;
using ETL_SQL.Core.Storage;
using ETL_SQL.Portal.Services;
using ETL_SQL.Reporting;
using Microsoft.Extensions.Logging.Abstractions;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Portal")]
public sealed class ObjectNativeSharedArtifactConsumerTests
{
    [Fact]
    public async Task SnapshotConsumer_RoundTripsThroughObjectNativeCommitRecords()
    {
        var native = new ObjectNativeArtifactStorage(new InMemoryObjectStore(), new Epochs());
        long fence = 1;
        IArtifactStorage artifacts = new ObjectNativeArtifactStorageAdapter(native, () => fence);
        var key = new KeyMaterialDescriptor("test", "artifact", "tenant-alpha", KeyPurpose.Artifact, "v1");
        var keys = new ResolvedKeyMaterialProvider("test", [(key, Enumerable.Repeat((byte)31, 32).ToArray())]);
        var service = new SnapshotPackageService(
            new PortalConfig { TenantId = "tenant-alpha" }, artifacts,
            NullLogger<SnapshotPackageService>.Instance, keys);
        var manifest = new ReportManifest
        {
            Source = "object-native.rptsql",
            Title = "Object Native",
            Visuals =
            {
                new VisualManifest
                {
                    Name = "Payload", VisualType = "TABLE", Columns = ["Value"],
                    Rows = [[new string('x', 2 * 1024 * 1024)]]
                }
            }
        };

        await service.SaveAsync(manifest, "shared-large.etlsnap");
        var loaded = await service.LoadAsync("shared-large.etlsnap");

        Assert.Equal(manifest.Visuals[0].Rows[0][0], loaded!.Visuals[0].Rows[0][0]);
        Assert.Single(await native.EnumerateCommitsAsync(ArtifactArea.Snapshots).ToListAsync());

        fence = 2;
        Assert.True(await artifacts.DeleteAsync(ArtifactArea.Snapshots, "shared-large.etlsnap"));
        Assert.False(await artifacts.ExistsAsync(ArtifactArea.Snapshots, "shared-large.etlsnap"));
    }

    [Fact]
    public async Task Adapter_ExplicitlyRejectsRenameEmulation()
    {
        IArtifactStorage artifacts = new ObjectNativeArtifactStorageAdapter(
            new ObjectNativeArtifactStorage(new InMemoryObjectStore(), new Epochs()), () => 1);
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            artifacts.MoveAsync(ArtifactArea.Datasets, "staging", "final"));
    }

    private sealed class Epochs : IWriteEpochStore
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
    }
}
