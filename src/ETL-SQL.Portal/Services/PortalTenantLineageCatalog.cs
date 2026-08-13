using ETL_SQL.Core;
using ETL_SQL.Core.Data;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Request/background-facing lineage partition. Shared hosts never fall back to the legacy
/// unscoped catalog: the verified tenant carried by <see cref="DatasetTenantScope"/> is mandatory
/// for every graph write, scan, and lookup.
/// </summary>
public sealed class PortalTenantLineageCatalog(
    ILineageCatalogStore catalog,
    DatasetTenantScope scope,
    PortalConfig config)
{
    private ITenantLineageCatalogStore? TenantCatalog => catalog as ITenantLineageCatalogStore;

    private ITenantLineageCatalogStore RequireTenantCatalog() =>
        TenantCatalog ?? throw new InvalidOperationException(
            "Shared Portal lineage requires a tenant-partitioned catalog provider.");

    private bool UseTenantCatalog => config.SharedTenancy.Enabled || TenantCatalog is not null;

    public Task SaveLineageAsync(
        IEnumerable<LineageEntry> entries,
        string? jobName,
        string? scriptPath,
        DateTime runAt) => UseTenantCatalog
            ? RequireTenantCatalog().SaveLineageAsync(scope.Context, entries, jobName, scriptPath, runAt)
            : catalog.SaveLineageAsync(entries, jobName, scriptPath, runAt);

    public Task<IEnumerable<LineageHistoryEntry>> GetHistoryForTableAsync(string tableName, int limit = 100) =>
        UseTenantCatalog
            ? RequireTenantCatalog().GetHistoryForTableAsync(scope.Context, tableName, limit)
            : catalog.GetHistoryForTableAsync(tableName, limit);

    public Task<IEnumerable<LineageHistoryEntry>> GetHistoryForTablesAsync(
        IReadOnlyCollection<string> tableNames, int limitPerTable = 100) => UseTenantCatalog
            ? RequireTenantCatalog().GetHistoryForTablesAsync(scope.Context, tableNames, limitPerTable)
            : catalog.GetHistoryForTablesAsync(tableNames, limitPerTable);

    public Task<IEnumerable<LineageHistoryEntry>> GetHistoryForTagAsync(
        string tagKey, string? tagValue = null, int limit = 100) => UseTenantCatalog
            ? RequireTenantCatalog().GetHistoryForTagAsync(scope.Context, tagKey, tagValue, limit)
            : catalog.GetHistoryForTagAsync(tagKey, tagValue, limit);

    public Task<IEnumerable<LineageMissingMetadataEntry>> GetMissingMetadataAsync(
        IReadOnlyCollection<string> requiredTags, int limit = 100) => UseTenantCatalog
            ? RequireTenantCatalog().GetMissingMetadataAsync(scope.Context, requiredTags, limit)
            : catalog.GetMissingMetadataAsync(requiredTags, limit);

    public Task<IEnumerable<LineageHistoryEntry>> GetRecentLineageAsync(int limit = 1000) =>
        UseTenantCatalog
            ? RequireTenantCatalog().GetRecentLineageAsync(scope.Context, limit)
            : catalog.GetRecentLineageAsync(limit);

    public Task<IEnumerable<LineageHistoryEntry>> GetHistoryForJobAsync(string jobName, int limit = 100) =>
        UseTenantCatalog
            ? RequireTenantCatalog().GetHistoryForJobAsync(scope.Context, jobName, limit)
            : catalog.GetHistoryForJobAsync(jobName, limit);

    public Task<IEnumerable<LineageHistoryEntry>> GetHistoryForSourceAsync(string sourceName, int limit = 100) =>
        UseTenantCatalog
            ? RequireTenantCatalog().GetHistoryForSourceAsync(scope.Context, sourceName, limit)
            : catalog.GetHistoryForSourceAsync(sourceName, limit);

    public Task<IEnumerable<LineageHistoryEntry>> GetHistoryForSourceFileAsync(string sourceFile, int limit = 100) =>
        UseTenantCatalog
            ? RequireTenantCatalog().GetHistoryForSourceFileAsync(scope.Context, sourceFile, limit)
            : catalog.GetHistoryForSourceFileAsync(sourceFile, limit);
}
