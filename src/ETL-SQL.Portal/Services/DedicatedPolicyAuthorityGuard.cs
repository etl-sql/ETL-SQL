using System.Security.Claims;
using ETL_SQL.Core.Multitenancy;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Binds the policy-authority surface to one server-derived tenant. A Dedicated host binds the
/// tenant fixed by host configuration; a Shared host binds the tenant carried by the request's
/// verified credential. A caller may assert that tenant but cannot select another one. Platform
/// identities remain operators, not tenant administrators, and therefore cannot author or override
/// tenant policy.
/// </summary>
public sealed class DedicatedPolicyAuthorityGuard(
    PortalConfig config,
    RequestTenantContextAccessor? tenantAccessor = null)
{
    public const string AuthorityScopeClaim = "etlsql_authority_scope";
    public const string PlatformScope = "platform";

    private readonly TenantContext? _hostTenant = string.IsNullOrWhiteSpace(config.TenantId)
        ? null
        : TenantContext.FromHostConfiguration(config.TenantId);

    /// <summary>True when every operation is confined to one server-derived tenant.</summary>
    public bool IsTenantBound => config.SharedTenancy.Enabled || _hostTenant is not null;

    private TenantContext? BoundTenant()
    {
        if (!config.SharedTenancy.Enabled)
            return _hostTenant;
        // Shared: the only authority is the verified credential on this request. No context is not
        // "unbound" — it is a refusal, because an unbound Shared caller could otherwise name any tenant.
        var current = tenantAccessor?.Current;
        if (current is null || current.Origin != TenantContextOrigin.VerifiedCredential)
            throw new UnauthorizedAccessException(
                "Shared policy-authority access requires a server-verified tenant context.");
        return current;
    }

    public string AuthorizeRead(string? assertedTenant)
    {
        var bound = BoundTenant();
        if (bound is null)
            return TenantId.FromTrustedSource(assertedTenant).Value;
        if (string.IsNullOrWhiteSpace(assertedTenant))
            return bound.Tenant.Value;
        return bound.RequireTenant(assertedTenant).Value;
    }

    public string AuthorizeMutation(ClaimsPrincipal principal, string? assertedTenant)
    {
        var tenant = AuthorizeRead(assertedTenant);
        if (!IsTenantBound) return tenant;

        var isPlatform = principal.IsInRole("PlatformAdmin")
            || principal.Claims.Any(claim =>
                claim.Type.Equals(AuthorityScopeClaim, StringComparison.OrdinalIgnoreCase)
                && claim.Value.Equals(PlatformScope, StringComparison.OrdinalIgnoreCase));
        if (isPlatform)
            throw new UnauthorizedAccessException(
                "Platform scope cannot author or override a tenant's organization policy.");
        return tenant;
    }
}
