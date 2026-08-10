using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Portal.Data;

namespace ETL_SQL.Portal.Services;

/// <summary>Server-derived partition boundary for the shared dataset catalog and its key namespace.</summary>
public sealed class DatasetTenantScope
{
    public DatasetTenantScope(PortalConfig config, TenantContext? context = null)
    {
        if (config.SharedTenancy.Enabled)
        {
            if (context is null || context.Origin != TenantContextOrigin.VerifiedCredential)
                throw new UnauthorizedAccessException(
                    "Shared dataset access requires a verified tenant context.");
            TenantId = context.Tenant.Value;
            return;
        }

        TenantId = string.IsNullOrWhiteSpace(config.TenantId)
            ? "portal-host"
            : ETL_SQL.Core.Multitenancy.TenantId.FromTrustedSource(config.TenantId).Value;
    }

    public string TenantId { get; }

    public IQueryable<Dataset> Query(PortalDbContext db) =>
        db.Datasets.Where(dataset => dataset.TenantId == TenantId);

    public string DatasetStorageRoot(PortalConfig config) =>
        DatasetStorageRoot(config, TenantId);

    public bool TryResolveDatasetPath(PortalConfig config, string path, out string resolved) =>
        TryResolveDatasetPath(config, TenantId, path, out resolved);

    public static string DatasetStorageRoot(PortalConfig config, string tenantId)
    {
        var root = Path.GetFullPath(config.DatasetRootPath);
        if (!config.SharedTenancy.Enabled)
            return root;
        var tenant = ETL_SQL.Core.Multitenancy.TenantId.FromTrustedSource(tenantId);
        return Path.Combine(root, tenant.Value);
    }

    public static bool TryResolveDatasetPath(
        PortalConfig config,
        string tenantId,
        string path,
        out string resolved)
    {
        resolved = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
            return false;
        return ETL_SQL.Core.SafePath.TryResolveWithinRoot(
            DatasetStorageRoot(config, tenantId), path, out resolved);
    }
}
