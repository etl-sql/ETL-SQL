using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Issues and verifies narrowly scoped bearer capabilities for platform support access to a
/// Managed Dedicated Portal. The tenant administrator approves one disclosed bundle, one named
/// platform actor, one purpose, and a short time window. This is intentionally separate from Portal
/// user JWTs: possessing platform infrastructure authority never creates a tenant user session.
/// </summary>
public sealed class SupportAccessApprovalService(PortalConfig config)
{
    public const string TokenIssuer = "etl-sql-portal-support-approval";
    public const string TokenAudience = "etl-sql-managed-dedicated-support";
    public const int MaximumLifetimeMinutes = 60;
    public const string HeaderName = "X-ETL-SQL-Support-Capability";

    private const string TenantClaim = "tenant_id";
    private const string PlatformActorClaim = "platform_actor";
    private const string ContentHashClaim = "content_hash";
    private const string PurposeClaim = "support_purpose";
    private const string ApprovedByClaim = "approved_by";

    public sealed record Approval(
        string TenantId,
        string PlatformActor,
        string ContentHash,
        string Purpose,
        string ApprovedBy,
        DateTime ValidFromUtc,
        DateTime ExpiresUtc,
        string CapabilityId);

    public sealed record IssuedApproval(string Capability, Approval Approval);

    public IssuedApproval Issue(
        string platformActor,
        string contentHash,
        string purpose,
        string approvedBy,
        int lifetimeMinutes,
        DateTime? nowUtc = null)
    {
        var tenant = RequireDedicatedTenant();
        platformActor = RequireBoundedText(platformActor, nameof(platformActor), 3, 200);
        contentHash = RequireContentHash(contentHash);
        purpose = RequireBoundedText(purpose, nameof(purpose), 10, 500);
        approvedBy = RequireBoundedText(approvedBy, nameof(approvedBy), 1, 200);
        if (lifetimeMinutes is < 1 or > MaximumLifetimeMinutes)
            throw new ArgumentOutOfRangeException(nameof(lifetimeMinutes),
                $"Support access must last between 1 and {MaximumLifetimeMinutes} minutes.");

        var now = DateTime.SpecifyKind(nowUtc ?? DateTime.UtcNow, DateTimeKind.Utc);
        var expires = now.AddMinutes(lifetimeMinutes);
        var capabilityId = Guid.NewGuid().ToString("N");
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Jti, capabilityId),
            new Claim(TenantClaim, tenant),
            new Claim(PlatformActorClaim, platformActor),
            new Claim(ContentHashClaim, contentHash),
            new Claim(PurposeClaim, purpose),
            new Claim(ApprovedByClaim, approvedBy)
        };
        var token = new JwtSecurityToken(
            issuer: TokenIssuer,
            audience: TokenAudience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: new SigningCredentials(CurrentKey(), SecurityAlgorithms.HmacSha256));
        var encoded = new JwtSecurityTokenHandler().WriteToken(token);
        return new IssuedApproval(encoded,
            new Approval(tenant, platformActor, contentHash, purpose, approvedBy, now, expires, capabilityId));
    }

    public Approval Validate(string capability, string expectedContentHash, DateTime? nowUtc = null)
    {
        RequireDedicatedTenant();
        if (string.IsNullOrWhiteSpace(capability) || capability.Length > 8192)
            throw new SecurityTokenException("A bounded support capability is required.");
        expectedContentHash = RequireContentHash(expectedContentHash);

        var handler = new JwtSecurityTokenHandler();
        var principal = handler.ValidateToken(capability, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = TokenIssuer,
            ValidateAudience = true,
            ValidAudience = TokenAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = ValidationKeys(),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ClockSkew = TimeSpan.FromSeconds(15),
            LifetimeValidator = (notBefore, expires, _, _) =>
            {
                if (notBefore is null || expires is null) return false;
                var now = DateTime.SpecifyKind(nowUtc ?? DateTime.UtcNow, DateTimeKind.Utc);
                return notBefore.Value.ToUniversalTime() <= now.AddSeconds(15)
                       && expires.Value.ToUniversalTime() >= now.AddSeconds(-15);
            }
        }, out var validatedToken);

        if (validatedToken is not JwtSecurityToken jwt
            || !string.Equals(jwt.Header.Alg, SecurityAlgorithms.HmacSha256, StringComparison.Ordinal))
            throw new SecurityTokenException("The support capability algorithm is invalid.");

        var tenant = SingleClaim(principal, TenantClaim);
        if (!string.Equals(tenant, RequireDedicatedTenant(), StringComparison.Ordinal))
            throw new SecurityTokenException("The support capability belongs to another tenant boundary.");
        var contentHash = SingleClaim(principal, ContentHashClaim);
        if (!string.Equals(contentHash, expectedContentHash, StringComparison.OrdinalIgnoreCase))
            throw new SecurityTokenException("The approved support disclosure is stale or different.");

        var validFrom = jwt.ValidFrom.ToUniversalTime();
        var expires = jwt.ValidTo.ToUniversalTime();
        if (expires - validFrom > TimeSpan.FromMinutes(MaximumLifetimeMinutes + 1))
            throw new SecurityTokenException("The support capability exceeds the maximum lifetime.");

        return new Approval(
            tenant,
            SingleClaim(principal, PlatformActorClaim),
            contentHash,
            SingleClaim(principal, PurposeClaim),
            SingleClaim(principal, ApprovedByClaim),
            validFrom,
            expires,
            SingleClaim(principal, JwtRegisteredClaimNames.Jti));
    }

    private string RequireDedicatedTenant()
    {
        if (config.SharedTenancy.Enabled || string.IsNullOrWhiteSpace(config.TenantId))
            throw new InvalidOperationException(
                "Tenant-approved platform support capabilities are available only on a host-fixed Managed Dedicated Portal.");
        return ETL_SQL.Core.Multitenancy.TenantId.FromTrustedSource(config.TenantId).Value;
    }

    private SymmetricSecurityKey CurrentKey() => DeriveKey(config.Jwt.Secret);

    private IReadOnlyList<SecurityKey> ValidationKeys() =>
        new[] { config.Jwt.Secret }
            .Concat((config.Jwt.PreviousSecrets ?? []).Take(1))
            .Where(secret => !string.IsNullOrWhiteSpace(secret))
            .Distinct(StringComparer.Ordinal)
            .Select(secret => (SecurityKey)DeriveKey(secret))
            .ToArray();

    private static SymmetricSecurityKey DeriveKey(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("Portal JWT key material is required for support approvals.");
        var material = Encoding.UTF8.GetBytes("etl-sql/support-access/v1\0" + secret);
        return new SymmetricSecurityKey(SHA256.HashData(material));
    }

    private static string RequireContentHash(string value)
    {
        value = value?.Trim() ?? string.Empty;
        if (value.Length != 64 || value.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("The reviewed support content hash must be a SHA-256 hex digest.");
        return value.ToLowerInvariant();
    }

    private static string RequireBoundedText(string value, string name, int minimum, int maximum)
    {
        value = value?.Trim() ?? string.Empty;
        if (value.Length < minimum || value.Length > maximum || value.Any(char.IsControl))
            throw new ArgumentException($"{name} must contain {minimum}-{maximum} printable characters.", name);
        return value;
    }

    private static string SingleClaim(ClaimsPrincipal principal, string type)
    {
        var values = principal.FindAll(type).Select(claim => claim.Value).ToArray();
        if (values.Length != 1 || string.IsNullOrWhiteSpace(values[0]))
            throw new SecurityTokenException($"The support capability has an invalid '{type}' claim.");
        return values[0];
    }
}
