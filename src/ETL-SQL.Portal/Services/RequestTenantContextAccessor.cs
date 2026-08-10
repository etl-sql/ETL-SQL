using System.Security.Claims;
using ETL_SQL.Core.Multitenancy;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Owns the server-derived tenant context for one Portal request. Dedicated deployments resolve
/// their immutable host tenant immediately. Shared deployments are populated only after JWT
/// signature, issuer, audience, and lifetime validation has completed.
/// </summary>
public sealed class RequestTenantContextAccessor(PortalConfig config)
{
    private TenantContext? _verified;
    private readonly TenantContext? _host = string.IsNullOrWhiteSpace(config.TenantId)
        ? null
        : TenantContext.FromHostConfiguration(config.TenantId);

    public TenantContext? Current => _verified ?? _host;

    public TenantContext RequireCurrent() => Current
        ?? throw new UnauthorizedAccessException(
            "This operation requires a server-verified tenant context.");

    public void SetVerifiedCredential(TenantContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Origin != TenantContextOrigin.VerifiedCredential)
            throw new UnauthorizedAccessException(
                "A shared request tenant must come from a verified credential.");
        if (_host is not null && _host.Tenant != context.Tenant)
            throw new UnauthorizedAccessException(
                "The verified credential tenant does not match the host tenant.");
        if (_verified is not null && _verified.Tenant != context.Tenant)
            throw new UnauthorizedAccessException(
                "A request tenant context cannot be replaced after it is established.");
        _verified = context;
    }
}

/// <summary>Validates the tenant binding carried by an already-authenticated Portal principal.</summary>
public static class TenantCredentialBinding
{
    public static bool TryResolve(
        ClaimsPrincipal principal,
        PortalConfig config,
        out TenantContext? context,
        out string? error)
    {
        context = null;
        error = null;
        var claims = principal.FindAll(TokenService.TenantClaim).Select(c => c.Value).ToArray();

        if (!config.SharedTenancy.Enabled)
        {
            if (string.IsNullOrWhiteSpace(config.TenantId))
                return true;

            var host = TenantContext.FromHostConfiguration(config.TenantId);
            if (claims.Length == 0)
            {
                context = host;
                return true;
            }
            if (claims.Length != 1)
            {
                error = "The credential contains multiple tenant claims.";
                return false;
            }
            try
            {
                host.RequireTenant(claims[0]);
                context = host;
                return true;
            }
            catch (ArgumentException)
            {
                error = "The credential tenant claim is malformed.";
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                error = "The credential tenant does not match the host tenant.";
                return false;
            }
        }

        if (claims.Length != 1)
        {
            error = claims.Length == 0
                ? "Shared Portal credentials require a tenant claim."
                : "Shared Portal credentials require exactly one tenant claim.";
            return false;
        }

        try
        {
            context = TenantContext.FromVerifiedCredential(claims[0]);
            return true;
        }
        catch (ArgumentException)
        {
            error = "The credential tenant claim is malformed.";
            return false;
        }
    }
}
