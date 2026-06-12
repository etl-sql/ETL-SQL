using Microsoft.AspNetCore.DataProtection;

namespace ETL_SQL.ReportPortal.Services;

public class SmtpPasswordProtector(IDataProtectionProvider dpProvider)
{
    private readonly IDataProtector _protector = dpProvider.CreateProtector("ETL_SQL.Portal.SmtpPassword");

    public string Protect(string plaintext) => _protector.Protect(plaintext);
    public string? Unprotect(string? cipher)
    {
        if (string.IsNullOrEmpty(cipher)) return null;
        try { return _protector.Unprotect(cipher); }
        catch { return null; }
    }
}
