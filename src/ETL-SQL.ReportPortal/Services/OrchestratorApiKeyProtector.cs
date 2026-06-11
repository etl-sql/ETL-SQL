using Microsoft.AspNetCore.DataProtection;

namespace ETL_SQL.ReportPortal.Services;

public sealed class OrchestratorApiKeyProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector protector =
        provider.CreateProtector("ETL_SQL.Portal.OrchestratorApiKey.v1");

    public string Protect(string plaintext) => protector.Protect(plaintext);

    public string? Unprotect(string? protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue))
            return null;

        try
        {
            return protector.Unprotect(protectedValue);
        }
        catch
        {
            return null;
        }
    }
}
