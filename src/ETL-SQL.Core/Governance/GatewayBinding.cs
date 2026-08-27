using System.Text.Json.Serialization;

namespace ETL_SQL.Core.Governance;

/// <summary>
/// A catalog entry's binding to a resource behind a tenant-owned outbound Gateway.
///
/// <para>This record is deliberately almost empty, and that is the security property. Per the SaaS
/// isolation architecture (<c>docs/architecture/SaaSTenantIsolation.md</c> §11.2) a Gateway binding
/// carries the connector type plus immutable Gateway and resource IDs and <b>nothing else</b> — no
/// cloud-side physical endpoint, and no credential. The physical target and the credential live only
/// on the on-premises Gateway, which resolves them locally at operation time. If either could be
/// stored here, a compromised cloud-side catalog would hand an attacker the private address and the
/// key to reach it, which is the whole thing the Gateway exists to prevent.</para>
///
/// <para>The IDs are opaque to the cloud side. They are compared, logged, and routed on; they are
/// never parsed into an address.</para>
/// </summary>
public sealed record GatewayResourceBinding(string GatewayId, string ResourceId)
{
    /// <summary>
    /// Server-derived catalog alias used only to revalidate the persisted use grant at execution.
    /// It is never accepted from, or returned to, a client as part of the Gateway binding.
    /// </summary>
    [JsonIgnore]
    public string? CatalogAlias { get; init; }
}

/// <summary>
/// Write-side validation for Gateway-bound catalog entries. Used by every surface that can store a
/// catalog entry — the admin CLI, the Portal catalog API, and the Orchestrator job API — so all of
/// them reject the same shapes rather than each re-deriving the rule.
/// </summary>
public static class GatewayBindingValidator
{
    /// <summary>
    /// Option keys that name a physical network destination. A Gateway-bound entry may not carry
    /// any of them: the destination is a Gateway-local fact, and accepting one here would let an
    /// administrator quietly turn a Gateway binding back into a direct connection.
    /// </summary>
    private static readonly string[] EndpointKeys =
    [
        "HOST", "SERVER", "ADDRESS", "ENDPOINT", "URL", "URI", "PORT", "DSN",
        "DATA SOURCE", "DATASOURCE", "ACCOUNT", "REGION", "PATH", "SHARE", "BASEURL", "BASE_URL"
    ];

    /// <summary>
    /// Returns the first reason the binding is not storable, or null when it is valid.
    /// Callers surface the reason verbatim; it never contains a credential or an address.
    /// </summary>
    public static string? FindViolation(
        GatewayResourceBinding? binding,
        string? target,
        IReadOnlyDictionary<string, string>? options)
    {
        if (binding is null) return null;

        if (string.IsNullOrWhiteSpace(binding.GatewayId))
            return "A Gateway binding requires a Gateway ID.";
        if (string.IsNullOrWhiteSpace(binding.ResourceId))
            return "A Gateway binding requires a resource ID.";
        if (!IsWellFormedId(binding.GatewayId))
            return "A Gateway ID may contain only letters, digits, period, underscore, and hyphen.";
        if (!IsWellFormedId(binding.ResourceId))
            return "A Gateway resource ID may contain only letters, digits, period, underscore, and hyphen.";

        // A Gateway-bound entry names no cloud-side destination at all. Anything in the target
        // position is a physical address by definition, so the check is presence, not shape.
        if (!string.IsNullOrWhiteSpace(target))
            return "A Gateway-bound connection cannot carry a target; the physical destination is resolved on the Gateway.";

        if (options is null) return null;

        foreach (var (key, value) in options)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            var trimmed = key.Trim();
            if (EndpointKeys.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                return $"A Gateway-bound connection cannot carry option '{trimmed}'; the physical destination is resolved on the Gateway.";
            if (SecretResolvableFields.IsCredential(trimmed))
                return $"A Gateway-bound connection cannot carry credential option '{trimmed}'; credentials are held only on the Gateway.";
        }

        return null;
    }

    /// <summary>Throws <see cref="InvalidOperationException"/> when the binding is not storable.</summary>
    public static void EnsureValid(
        GatewayResourceBinding? binding,
        string? target,
        IReadOnlyDictionary<string, string>? options)
    {
        var violation = FindViolation(binding, target, options);
        if (violation != null) throw new InvalidOperationException(violation);
    }

    private static bool IsWellFormedId(string value) =>
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');
}
