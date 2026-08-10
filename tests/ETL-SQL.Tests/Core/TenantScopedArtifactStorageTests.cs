using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Core.Storage;

namespace ETL_SQL.Tests.Core;

public sealed class TenantScopedArtifactStorageTests
{
    [Fact]
    public async Task LogicalKeysArePhysicallyPrefixedAndCannotCrossTenantNamespaces()
    {
        var inner = new InMemoryArtifactStorage();
        var alpha = new TenantScopedArtifactStorage(
            inner, TenantContext.FromHostConfiguration("tenant-alpha"));
        var beta = new TenantScopedArtifactStorage(
            inner, TenantContext.FromHostConfiguration("tenant-beta"));

        await alpha.WriteAllTextAsync(ArtifactArea.Snapshots, "reports/daily.etlsnap", "alpha");
        await beta.WriteAllTextAsync(ArtifactArea.Snapshots, "reports/daily.etlsnap", "beta");
        await alpha.WriteAllTextAsync(
            ArtifactArea.Snapshots, "tenant-beta/attempt.etlsnap", "still-alpha");

        Assert.Equal("alpha", await alpha.ReadAllTextAsync(
            ArtifactArea.Snapshots, "reports/daily.etlsnap"));
        Assert.Equal("beta", await beta.ReadAllTextAsync(
            ArtifactArea.Snapshots, "reports/daily.etlsnap"));
        Assert.Equal("still-alpha", await inner.ReadAllTextAsync(
            ArtifactArea.Snapshots, "tenant-alpha/tenant-beta/attempt.etlsnap"));

        var physical = new List<string>();
        await foreach (var info in inner.EnumerateAsync(ArtifactArea.Snapshots))
            physical.Add(info.Path);
        Assert.Contains("tenant-alpha/reports/daily.etlsnap", physical);
        Assert.Contains("tenant-beta/reports/daily.etlsnap", physical);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in alpha.EnumerateAsync(ArtifactArea.Snapshots)) { }
        });
    }

    [Fact]
    public async Task MoveDeleteAndMetadataRemainInsideTenantPrefix()
    {
        var inner = new InMemoryArtifactStorage();
        var storage = new TenantScopedArtifactStorage(
            inner, TenantContext.FromHostConfiguration("tenant-alpha"));
        await storage.WriteAllTextAsync(ArtifactArea.Datasets, "stage/data.parquet", "rows");

        await storage.MoveAsync(
            ArtifactArea.Datasets, "stage/data.parquet", "current/data.parquet");
        var info = await storage.GetInfoAsync(ArtifactArea.Datasets, "current/data.parquet");

        Assert.Equal("current/data.parquet", info?.Path);
        Assert.False(await inner.ExistsAsync(
            ArtifactArea.Datasets, "tenant-alpha/stage/data.parquet"));
        Assert.True(await inner.ExistsAsync(
            ArtifactArea.Datasets, "tenant-alpha/current/data.parquet"));
        Assert.True(await storage.DeleteAsync(
            ArtifactArea.Datasets, "current/data.parquet"));
    }

    [Fact]
    public async Task LegacyUnprefixedArtifactsFailVisiblyInsteadOfBeingShadowed()
    {
        var inner = new InMemoryArtifactStorage();
        await inner.WriteAllTextAsync(
            ArtifactArea.Scripts, "reports/daily.rptsql", "legacy");
        var storage = new TenantScopedArtifactStorage(
            inner, TenantContext.FromHostConfiguration("tenant-alpha"));

        var readError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            storage.ReadAllTextAsync(ArtifactArea.Scripts, "reports/daily.rptsql"));
        var writeError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            storage.WriteAllTextAsync(
                ArtifactArea.Scripts, "reports/daily.rptsql", "replacement"));

        Assert.Contains("Migrate or quarantine legacy artifacts", readError.Message);
        Assert.Contains("Migrate or quarantine legacy artifacts", writeError.Message);
        Assert.False(await inner.ExistsAsync(
            ArtifactArea.Scripts, "tenant-alpha/reports/daily.rptsql"));
    }
}
