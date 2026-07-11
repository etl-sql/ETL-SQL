namespace ETL_SQL.Core.Governance;

/// <summary>
/// Connection option and connection-string field names on which <c>SECRET:</c> references are
/// resolved. Single source of truth shared by the engine's connection secret resolver, lint
/// diagnostics, and redaction so runtime behavior and editor feedback never disagree. Beyond the
/// built-in credential set, an organization can designate additional sensitive metadata fields
/// (<c>Governance:Secrets:SensitiveConnectionFields</c>) — those become SECRET:-resolvable and
/// masked in display surfaces without being globally secret for every deployment. References on
/// any other field are rejected at execution time.
/// </summary>
public static class SecretResolvableFields
{
    // Organization-designated sensitive metadata fields (HOST, PATH, BUCKET, ...) — set once at
    // host startup by the composition root. Unlike CredentialKeys these fields may still hold
    // plain values; designation makes them SECRET:-resolvable and masked.
    private static volatile HashSet<string> _organizationFields = new(StringComparer.OrdinalIgnoreCase);

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

    public static bool IsResolvable(string key) =>
        CredentialKeys.Contains(key) || _organizationFields.Contains(key);

    /// <summary>True for credential fields only — the set on which raw values are never stored.</summary>
    public static bool IsCredential(string key) => CredentialKeys.Contains(key);

    /// <summary>True when the organization designated this metadata field as sensitive.</summary>
    public static bool IsOrganizationDesignated(string key) => _organizationFields.Contains(key);

    public static IReadOnlyCollection<string> OrganizationFields => _organizationFields;

    /// <summary>Replaces the organization-designated field set (composition-root startup call).</summary>
    public static void ConfigureOrganizationFields(IEnumerable<string>? fields)
    {
        _organizationFields = new HashSet<string>(
            (fields ?? []).Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f.Trim()),
            StringComparer.OrdinalIgnoreCase);
    }
}
