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
}
