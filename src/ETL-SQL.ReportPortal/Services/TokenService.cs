using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ETL_SQL.ReportPortal.Data;
using Microsoft.IdentityModel.Tokens;

namespace ETL_SQL.ReportPortal.Services;

public class TokenService(PortalConfig config)
{
    public const string SecurityStampClaim = "security_stamp";
    public const string IdentityTypeClaim = "identity_type";
    public const string ServiceIdentityType = "service";
    public const string ServiceAccountIdClaim = "service_account_id";
    public const string ScopeClaim = "scope";
    public int ServiceTokenLifetimeSeconds => Math.Min(15, Math.Max(1, config.Jwt.ExpiryMinutes)) * 60;

    public string GenerateJwt(PortalUser user, IList<string> roles)
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

        var key = JwtSigningKeyRing.Current(config.Jwt);
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
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

    public string GenerateServiceJwt(ServiceAccount account, IEnumerable<string> roles, IEnumerable<string> scopes)
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

        var key = JwtSigningKeyRing.Current(config.Jwt);
        var token = new JwtSecurityToken(
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
}
