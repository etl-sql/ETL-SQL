using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace ETL_SQL.Portal.Services;

public static class JwtSigningKeyRing
{
    public static SymmetricSecurityKey Current(JwtConfig config) =>
        new(Encoding.UTF8.GetBytes(config.Secret));

    public static IReadOnlyList<SecurityKey> ValidationKeys(JwtConfig config)
    {
        var secrets = new[] { config.Secret }
            .Concat((config.PreviousSecrets ?? []).Take(1))
            .Where(secret => !string.IsNullOrWhiteSpace(secret))
            .Distinct(StringComparer.Ordinal);
        return secrets
            .Select(secret => (SecurityKey)new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)))
            .ToArray();
    }
}
