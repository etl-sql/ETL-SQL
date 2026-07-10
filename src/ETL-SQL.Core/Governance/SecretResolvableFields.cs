namespace ETL_SQL.Core.Governance;

/// <summary>
/// Connection option and connection-string field names on which <c>SECRET:</c> references are
/// resolved. Single source of truth shared by the engine's connection secret resolver and lint
/// diagnostics so runtime behavior and editor feedback never disagree. References on any other
/// field are rejected at execution time; extending resolution to classified sensitive metadata
/// is planned governance work (Phase 7 Section 6).
/// </summary>
public static class SecretResolvableFields
{
    public static readonly IReadOnlySet<string> CredentialKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "PASSWORD",
        "PWD",
        "API_KEY",
        "APIKEY",
        "TOKEN",
        "ACCESS_TOKEN",
        "REFRESH_TOKEN",
        "CLIENT_SECRET",
        "CLIENTSECRET",
        "SECRET",
        "SECRET_KEY",
        "SECRETKEY",
        "ACCESS_KEY",
        "ACCESSKEY",
        "SAS_TOKEN",
        "ACCOUNT_KEY",
        "ACCOUNTKEY",
        "PASSPHRASE",
        "PRIVATE_KEY",
        "SASL_PASSWORD",
        "SASL_JAAS_CONFIG"
    };

    public static bool IsResolvable(string key) => CredentialKeys.Contains(key);
}
