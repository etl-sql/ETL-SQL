using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Portal.Data;

namespace ETL_SQL.Portal.Services;

/// <summary>Server-derived partition boundary for the shared dataset catalog and its key namespace.</summary>
public sealed class DatasetTenantScope
{
    private const string UnverifiedMessage = "Shared dataset access requires a verified tenant context.";
    private readonly RequestTenantContextAccessor? _deferredAccessor;
    private TenantContext? _context;

    public DatasetTenantScope(PortalConfig config, TenantContext? context = null)
    {
        if (config.SharedTenancy.Enabled)
        {
            if (context is null || context.Origin != TenantContextOrigin.VerifiedCredential)
                throw new UnauthorizedAccessException(UnverifiedMessage);
            _context = context;
            return;
        }

        _context = TenantContext.FromHostConfiguration(
            string.IsNullOrWhiteSpace(config.TenantId) ? "portal-host" : config.TenantId);
    }

    private DatasetTenantScope(RequestTenantContextAccessor accessor) => _deferredAccessor = accessor;

    /// <summary>
    /// A Shared-mode scope for a request whose tenant is not verified yet: an anonymous endpoint that
    /// establishes the tenant from its own credential (a refresh secret, an OIDC callback, a share
    /// link) after the controller and its services were constructed. It binds on first use to the
    /// request's verified credential, and refuses any use before one exists.
    /// </summary>
    public static DatasetTenantScope Deferred(RequestTenantContextAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        return new DatasetTenantScope(accessor);
    }

    public TenantContext Context => _context ??= Bind();
    public string TenantId => Context.Tenant.Value;

    /// <summary>The tenant when one is established, without throwing when it is not.</summary>
    public bool TryGetTenantId(out string tenantId)
    {
        if (_context is null && _deferredAccessor?.Current is
            { Origin: TenantContextOrigin.VerifiedCredential } verified)
            _context = verified;
        tenantId = _context?.Tenant.Value ?? string.Empty;
        return _context is not null;
    }

    private TenantContext Bind() =>
        _deferredAccessor?.Current is { Origin: TenantContextOrigin.VerifiedCredential } verified
            ? verified
            : throw new UnauthorizedAccessException(UnverifiedMessage);

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
