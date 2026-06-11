using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using ETL_SQL.ReportPortal.Data;

namespace ETL_SQL.ReportPortal.Services;

public class TokenService(PortalConfig config)
{
    public const string SecurityStampClaim = "security_stamp";

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

        var key   = JwtSigningKeyRing.Current(config.Jwt);
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            claims:  claims,
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

    public static string HashRefreshToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }
}
