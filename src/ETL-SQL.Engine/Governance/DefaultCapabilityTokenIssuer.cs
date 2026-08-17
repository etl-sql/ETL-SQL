using System;
using System.Text;
using System.Text.Json;
using ETL_SQL.Core.Governance;

namespace ETL_SQL.Engine.Governance;

public class DefaultCapabilityTokenIssuer : ICapabilityTokenIssuer
{
    // A production implementation would cryptographically sign and encrypt this payload
    // using ASP.NET DataProtection or JWT. This is the structural foundation.
    public string IssueToken(CapabilityToken capability)
    {
        var json = JsonSerializer.Serialize(capability);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    public bool TryValidateToken(string rawToken, out CapabilityToken? capability)
    {
        capability = null;
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(rawToken));
            var parsed = JsonSerializer.Deserialize<CapabilityToken>(json);

            if (parsed == null || parsed.ExpiresAt < DateTimeOffset.UtcNow)
                return false;

            capability = parsed;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
