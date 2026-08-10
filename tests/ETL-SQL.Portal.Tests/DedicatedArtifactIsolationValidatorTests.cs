using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Core.Storage;
using ETL_SQL.Portal.Services;

namespace ETL_SQL.Portal.Tests;

public sealed class DedicatedArtifactIsolationValidatorTests
{
    [Fact]
    public async Task StartupRejectsLegacyUnprefixedArtifacts()
    {
        var inner = new InMemoryArtifactStorage();
        await inner.WriteAllTextAsync(ArtifactArea.Scripts, "legacy/report.rptsql", "select 1;");
        var scoped = new TenantScopedArtifactStorage(
            inner, TenantContext.FromHostConfiguration("tenant-alpha"));
        var validator = new DedicatedArtifactIsolationValidator(
            scoped,
            new PortalConfig { TenantId = "tenant-alpha" });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            validator.StartAsync(CancellationToken.None));

        Assert.Contains("Migrate or quarantine legacy artifacts", error.Message);
    }

    [Fact]
    public async Task StartupAcceptsOnlyHostPrefixedArtifacts()
    {
        var inner = new InMemoryArtifactStorage();
        var scoped = new TenantScopedArtifactStorage(
            inner, TenantContext.FromHostConfiguration("tenant-alpha"));
        await scoped.WriteAllTextAsync(
            ArtifactArea.Snapshots, "reports/daily.etlsnap", "snapshot");
        var validator = new DedicatedArtifactIsolationValidator(
            scoped,
            new PortalConfig { TenantId = "tenant-alpha" });

        await validator.StartAsync(CancellationToken.None);
    }
}
