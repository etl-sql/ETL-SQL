using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Orchestrator.Storage;
using ETL_SQL.Portal.Services;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Smoke.Security")]
public sealed class SharedLineageTenantIsolationTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(), $"portal-shared-lineage-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task PortalGraphSearchAndWritesUseVerifiedTenantPartition()
    {
        var store = new SQLiteJobHistoryStore(_dbPath);
        var config = new PortalConfig
        {
            SharedTenancy = new SharedTenancyConfig { Enabled = true }
        };
        var alpha = Catalog(store, config, "tenant-alpha");
        var beta = Catalog(store, config, "tenant-beta");
        static LineageEntry Entry(string owner) => new("same.table", "SELECT")
        {
            SourceTables = ["same.source"],
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["owner"] = owner
            }
        };

        await alpha.SaveLineageAsync([Entry("alpha")], "same-job", "same.etlsql", DateTime.UtcNow);
        await beta.SaveLineageAsync([Entry("beta")], "same-job", "same.etlsql", DateTime.UtcNow);

        Assert.Equal("alpha", Assert.Single(await alpha.GetRecentLineageAsync()).Tags["owner"]);
        Assert.Equal("beta", Assert.Single(await beta.GetRecentLineageAsync()).Tags["owner"]);
        Assert.Empty(await alpha.GetHistoryForTagAsync("owner", "beta"));
        Assert.Single(await alpha.GetHistoryForSourceAsync("same.source"));
    }

    [Fact]
    public async Task SharedPortalRefusesProviderWithoutTenantContract()
    {
        var config = new PortalConfig
        {
            SharedTenancy = new SharedTenancyConfig { Enabled = true }
        };
        var wrapper = new PortalTenantLineageCatalog(
            new LegacyOnlyCatalog(),
            new DatasetTenantScope(config, TenantContext.FromVerifiedCredential("tenant-alpha")),
            config);

        await Assert.ThrowsAsync<InvalidOperationException>(() => wrapper.GetRecentLineageAsync());
    }

    private static PortalTenantLineageCatalog Catalog(
        SQLiteJobHistoryStore store, PortalConfig config, string tenant) => new(
        store,
        new DatasetTenantScope(config, TenantContext.FromVerifiedCredential(tenant)),
        config);

    public void Dispose()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
        try { if (File.Exists(_dbPath + "-wal")) File.Delete(_dbPath + "-wal"); } catch { }
        try { if (File.Exists(_dbPath + "-shm")) File.Delete(_dbPath + "-shm"); } catch { }
    }

    private sealed class LegacyOnlyCatalog : ILineageCatalogStore
    {
        public Task SaveLineageAsync(IEnumerable<LineageEntry> entries, string? jobName, string? scriptPath, DateTime runAt) => Task.CompletedTask;
        public Task<IEnumerable<LineageHistoryEntry>> GetHistoryForTableAsync(string tableName, int limit = 100) => Empty();
        public Task<IEnumerable<LineageHistoryEntry>> GetHistoryForTagAsync(string tagKey, string? tagValue = null, int limit = 100) => Empty();
        public Task<IEnumerable<LineageMissingMetadataEntry>> GetMissingMetadataAsync(IReadOnlyCollection<string> requiredTags, int limit = 100) => Task.FromResult<IEnumerable<LineageMissingMetadataEntry>>([]);
        public Task<IEnumerable<LineageHistoryEntry>> GetRecentLineageAsync(int limit = 1000) => Empty();
        public Task<IEnumerable<LineageHistoryEntry>> GetHistoryForJobAsync(string jobName, int limit = 100) => Empty();
        public Task<IEnumerable<LineageHistoryEntry>> GetHistoryForSourceAsync(string sourceName, int limit = 100) => Empty();
        public Task<IEnumerable<LineageHistoryEntry>> GetHistoryForSourceFileAsync(string sourceFile, int limit = 100) => Empty();
        private static Task<IEnumerable<LineageHistoryEntry>> Empty() =>
            Task.FromResult<IEnumerable<LineageHistoryEntry>>([]);
    }
}
