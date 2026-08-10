using System.Security.Claims;
using ETL_SQL.Core.Multitenancy;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Binds the Dedicated policy-authority surface to the tenant fixed by host configuration. A caller
/// may assert that tenant but cannot select another one. Platform identities remain operators, not
/// tenant administrators, and therefore cannot author or override tenant policy.
/// </summary>
public sealed class DedicatedPolicyAuthorityGuard(PortalConfig config)
{
    public const string AuthorityScopeClaim = "etlsql_authority_scope";
    public const string PlatformScope = "platform";

    private readonly TenantContext? _hostTenant = string.IsNullOrWhiteSpace(config.TenantId)
        ? null
        : TenantContext.FromHostConfiguration(config.TenantId);

    public bool IsDedicated => _hostTenant is not null;

    public string AuthorizeRead(string? assertedTenant)
    {
        if (_hostTenant is null)
            return TenantId.FromTrustedSource(assertedTenant).Value;
        if (string.IsNullOrWhiteSpace(assertedTenant))
            return _hostTenant.Tenant.Value;
        return _hostTenant.RequireTenant(assertedTenant).Value;
    }

    public string AuthorizeMutation(ClaimsPrincipal principal, string? assertedTenant)
    {
        var tenant = AuthorizeRead(assertedTenant);
        if (_hostTenant is null) return tenant;

        var isPlatform = principal.IsInRole("PlatformAdmin")
            || principal.Claims.Any(claim =>
                claim.Type.Equals(AuthorityScopeClaim, StringComparison.OrdinalIgnoreCase)
                && claim.Value.Equals(PlatformScope, StringComparison.OrdinalIgnoreCase));
        if (isPlatform)
            throw new UnauthorizedAccessException(
                "Platform scope cannot author or override a Dedicated tenant's organization policy.");
        return tenant;
    }
}
