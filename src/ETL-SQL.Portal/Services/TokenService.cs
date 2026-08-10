using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Portal.Data;
using Microsoft.IdentityModel.Tokens;

namespace ETL_SQL.Portal.Services;

public class TokenService(PortalConfig config)
{
    public const string SecurityStampClaim = "security_stamp";
    public const string IdentityTypeClaim = "identity_type";
    public const string ServiceIdentityType = "service";
    public const string ServiceAccountIdClaim = "service_account_id";
    public const string ScopeClaim = "scope";
    public const string TenantClaim = "tenant_id";

    // Pinning iss/aud scopes these tokens to portal API access: another token type signed
    // with the same shared secret (or a portal token replayed against a different consumer
    // of that secret) no longer validates interchangeably.
    public const string TokenIssuer = "etl-sql-portal";
    public const string TokenAudience = "etl-sql-portal-api";
    public int ServiceTokenLifetimeSeconds => Math.Min(15, Math.Max(1, config.Jwt.ExpiryMinutes)) * 60;

    /// <param name="studioCapabilities">
    /// Capabilities the user's groups grant, resolved at sign-in and carried as claims so the
    /// per-request check stays a claim lookup. Role-mapped capabilities are resolved from
    /// configuration at check time and are deliberately not duplicated here.
    /// </param>
    public string GenerateJwt(PortalUser user, IList<string> roles,
        IEnumerable<string>? studioCapabilities = null,
        TenantContext? tenantContext = null)
    {
        // COMPAT_BREAK: 0.11
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.UserName!),
            new("mustChangePassword", user.MustChangePassword.ToString().ToLower()),
            new(SecurityStampClaim, user.SecurityStamp ?? string.Empty),
        };
        foreach (var role in roles)
            claims.Add(new Claim(ClaimTypes.Role, role));
        foreach (var capability in (studioCapabilities ?? []).Distinct(StringComparer.OrdinalIgnoreCase))
            claims.Add(new Claim(StudioAuthorizationService.CapabilityClaim, capability));
        AddTenantClaim(claims, tenantContext);

        var key = JwtSigningKeyRing.Current(config.Jwt);
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: TokenIssuer,
            audience: TokenAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(config.Jwt.ExpiryMinutes),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken()
    {
        var bytes = new byte[64];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    public string GenerateServiceJwt(ServiceAccount account, IEnumerable<string> roles,
        IEnumerable<string> scopes, IEnumerable<string>? studioCapabilities = null,
        TenantContext? tenantContext = null)
    {
        var claims = new List<Claim>
        {
            // Existing resource ACLs consume the mapped sub/NameIdentifier as an integer user ID.
            // The immutable service identity remains in ServiceAccountIdClaim.
            new(JwtRegisteredClaimNames.Sub, account.OwnerUserId.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, account.Name),
            new(IdentityTypeClaim, ServiceIdentityType),
            new(ServiceAccountIdClaim, account.Id),
            new(SecurityStampClaim, account.SecurityStamp)
        };
        foreach (var role in roles.Distinct(StringComparer.OrdinalIgnoreCase))
            claims.Add(new Claim(ClaimTypes.Role, role));
        foreach (var scope in scopes.Distinct(StringComparer.OrdinalIgnoreCase))
            claims.Add(new Claim(ScopeClaim, scope));
        foreach (var capability in (studioCapabilities ?? []).Distinct(StringComparer.OrdinalIgnoreCase))
            claims.Add(new Claim(StudioAuthorizationService.CapabilityClaim, capability));
        AddTenantClaim(claims, tenantContext);

        var key = JwtSigningKeyRing.Current(config.Jwt);
        var token = new JwtSecurityToken(
            issuer: TokenIssuer,
            audience: TokenAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddSeconds(ServiceTokenLifetimeSeconds),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static string HashRefreshToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }

    private void AddTenantClaim(List<Claim> claims, TenantContext? supplied)
    {
        var context = supplied;
        if (context is null && !string.IsNullOrWhiteSpace(config.TenantId))
            context = TenantContext.FromHostConfiguration(config.TenantId);

        if (context is null)
        {
            if (config.SharedTenancy.Enabled)
                throw new UnauthorizedAccessException(
                    "Shared Portal tokens require a server-verified tenant context.");
            return;
        }

        if (context.Origin == TenantContextOrigin.PlatformAuthorization)
            throw new UnauthorizedAccessException(
                "Platform authorization cannot mint a tenant-user or tenant-service session.");

        claims.Add(new Claim(TenantClaim, context.Tenant.Value));
    }
}
