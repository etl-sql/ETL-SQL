using System.IO;
using System.Threading.Tasks;
using ETL_SQL.Core.Storage;
using Xunit;

namespace ETL_SQL.Tests.Core;

/// <summary>
/// Practical HA P1.4: the in-memory reference implementation under the shared storage contract, plus
/// its implementation-specific lease behavior (it materializes — and cleans up — a temp copy).
/// </summary>
public class InMemoryArtifactStorageTests : ArtifactStorageContractTests
{
    protected override IArtifactStorage CreateStorage() => new InMemoryArtifactStorage();

    [Fact]
    public async Task LeaseLocalCopy_RemovesTempCopyOnDispose()
    {
        var s = new InMemoryArtifactStorage();
        await s.WriteAllTextAsync(ArtifactArea.Datasets, "cache_2.parquet", "bytes");

        string leasedPath;
        await using (var lease = await s.LeaseLocalCopyAsync(ArtifactArea.Datasets, "cache_2.parquet"))
        {
            leasedPath = lease.LocalPath;
            Assert.True(File.Exists(leasedPath));
        }
        Assert.False(File.Exists(leasedPath)); // an in-memory store's temp materialization is cleaned up
    }
}
