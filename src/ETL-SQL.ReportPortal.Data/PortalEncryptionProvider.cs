using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ETL_SQL.ReportPortal.Data;

public static class PortalEncryptionProvider
{
    private const string Prefix = "dp:";
    private static IDataProtector? _protector;

    public static void Initialize(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("ETL-SQL.Portal.PII");
    }

    public static string? Encrypt(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (_protector == null) return value; // Return as-is when uninitialized (e.g. at migration/design-time)
        if (value.StartsWith(Prefix, StringComparison.Ordinal)) return value;

        return Prefix + _protector.Protect(value);
    }

    public static string? Decrypt(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (_protector == null) return value;

        try
        {
            if (value.StartsWith(Prefix, StringComparison.Ordinal))
                return _protector.Unprotect(value[Prefix.Length..]);

            return _protector.Unprotect(value);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            if (value.StartsWith(Prefix, StringComparison.Ordinal))
                throw;

            // Legacy unencrypted data remains readable; startup maintenance rewrites it encrypted.
            return value;
        }
    }

    public static bool IsEncrypted(string? value)
    {
        if (string.IsNullOrEmpty(value) || _protector == null) return false;
        if (value.StartsWith(Prefix, StringComparison.Ordinal)) return true;

        try
        {
            _protector.Unprotect(value);
            return true;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return false;
        }
    }
}

public class EncryptedDbConverter : ValueConverter<string, string>
{
    public EncryptedDbConverter()
        : base(
            v => PortalEncryptionProvider.Encrypt(v) ?? "",
            v => PortalEncryptionProvider.Decrypt(v) ?? "")
    {
    }
}

public class EncryptedDbNullableConverter : ValueConverter<string?, string?>
{
    public EncryptedDbNullableConverter()
        : base(
            v => PortalEncryptionProvider.Encrypt(v),
            v => PortalEncryptionProvider.Decrypt(v))
    {
    }
}
