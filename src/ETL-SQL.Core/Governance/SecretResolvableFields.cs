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

    // Connector-scoped designations from "TYPE:FIELD" config entries (e.g. "SFTP:HOST"): the
    // field is sensitive only for that connector type.
    private static volatile Dictionary<string, HashSet<string>> _connectorFields =
        new(StringComparer.OrdinalIgnoreCase);

    // Built-in connector-scoped designations that hold in every deployment, independent of
    // organization configuration. A webhook endpoint URL embeds its auth token (Slack/Teams
    // incoming webhooks) — the URL IS the credential, so SECRET: must resolve on it and display
    // surfaces must mask it. Keyed by canonical name and each registered alias, because the
    // resolver and lint see the connector type as typed in the script.
    private static readonly Dictionary<string, HashSet<string>> BuiltInConnectorFields =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["WEBHOOK"] = new(StringComparer.OrdinalIgnoreCase) { "URL" },
            ["SLACK"] = new(StringComparer.OrdinalIgnoreCase) { "URL" },
            ["TEAMS"] = new(StringComparer.OrdinalIgnoreCase) { "URL" },
        };

    public static bool IsResolvable(string key) =>
        CredentialKeys.Contains(key) || _organizationFields.Contains(key);

    /// <summary>Connector-type-aware variant: also honors "TYPE:FIELD" designations for that type.</summary>
    public static bool IsResolvable(string key, string? connectorType) =>
        IsResolvable(key) || IsConnectorDesignated(key, connectorType);

    /// <summary>True for credential fields only — the set on which raw values are never stored.</summary>
    public static bool IsCredential(string key) => CredentialKeys.Contains(key);

    /// <summary>True when the organization designated this metadata field as sensitive (globally).</summary>
    public static bool IsOrganizationDesignated(string key) => _organizationFields.Contains(key);

    /// <summary>True when the field is sensitive for this connector type — built-in (e.g. the
    /// WEBHOOK URL, which embeds its auth token) or organization-designated via "TYPE:FIELD".</summary>
    public static bool IsConnectorDesignated(string key, string? connectorType)
    {
        if (connectorType == null) return false;
        var type = connectorType.Trim();
        return (BuiltInConnectorFields.TryGetValue(type, out var builtIn) && builtIn.Contains(key))
            || (_connectorFields.TryGetValue(type, out var fields) && fields.Contains(key));
    }

    public static IReadOnlyCollection<string> OrganizationFields => _organizationFields;

    /// <summary>
    /// Replaces the organization-designated field set (composition-root startup call). Plain
    /// entries apply to every connector; "TYPE:FIELD" entries apply to that connector type only.
    /// </summary>
    public static void ConfigureOrganizationFields(IEnumerable<string>? fields)
    {
        var global = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var perConnector = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in (fields ?? []).Where(f => !string.IsNullOrWhiteSpace(f)))
        {
            var entry = raw.Trim();
            var separator = entry.IndexOf(':');
            if (separator > 0 && separator < entry.Length - 1)
            {
                var type = entry[..separator].Trim();
                if (!perConnector.TryGetValue(type, out var set))
                    perConnector[type] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                set.Add(entry[(separator + 1)..].Trim());
            }
            else
            {
                global.Add(entry);
            }
        }

        _organizationFields = global;
        _connectorFields = perConnector;
    }
}
