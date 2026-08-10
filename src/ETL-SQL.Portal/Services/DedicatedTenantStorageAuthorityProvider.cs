using ETL_SQL.Core.Multitenancy;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Services;

/// <summary>Builds run storage authority from host configuration or verified/persisted Shared scope.</summary>
public sealed class DedicatedTenantStorageAuthorityProvider(
    PortalConfig config,
    IHttpContextAccessor httpContextAccessor)
    : ITenantStorageHostAuthorityProvider
{
    public TenantStorageHostAuthority? GetAuthority(TenantContext? persistedContext = null)
    {
        TenantContext tenant;
        if (config.SharedTenancy.Enabled)
        {
            tenant = persistedContext
                ?? httpContextAccessor.HttpContext?.RequestServices.GetService<TenantContext>()
                ?? throw new UnauthorizedAccessException(
                    "Shared run storage requires verified or persisted server tenant authority.");
            if (tenant.Origin != TenantContextOrigin.VerifiedCredential)
            {
                throw new UnauthorizedAccessException(
                    "Shared run storage cannot derive authority from host-fixed or caller scope.");
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(config.TenantId))
                return null;
            tenant = TenantContext.FromHostConfiguration(config.TenantId);
            if (persistedContext is not null)
                tenant.RequireTenant(persistedContext.Tenant.Value);
        }

        var scripts = PortalPathGuard.TenantAreaRoot(
            config, config.ScriptRootPath, tenant.Tenant.Value);
        var maps = PortalPathGuard.TenantAreaRoot(
            config, config.MapRootPath, tenant.Tenant.Value);
        var datasets = PortalPathGuard.TenantAreaRoot(
            config, config.DatasetRootPath, tenant.Tenant.Value);
        var snapshots = PortalPathGuard.TenantAreaRoot(
            config, config.SnapshotDirectory, tenant.Tenant.Value);
        return TenantStorageHostAuthority.FromServerContext(
            tenant,
            Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(config.DatabasePath))!,
                ".sessions",
                tenant.Tenant.Value),
            Path.Combine(Path.GetTempPath(), "ETL-SQL-Runs"),
            [
                ("scripts", scripts, TenantStorageAccess.Read),
                ("maps", maps, TenantStorageAccess.Read),
                ("datasets", datasets, TenantStorageAccess.All),
                ("snapshots", snapshots, TenantStorageAccess.All)
            ]);
    }
}
