using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace ETL_SQL.Core.Multitenancy;

/// <summary>
/// A validated tenant identifier in its canonical form.
/// </summary>
/// <remarks>
/// A type rather than a <see cref="string"/> on purpose. Every isolation domain in the SaaS track
/// depends on "the tenant" being a thing the server derived, and a bare string makes the two sources
/// — server-derived and caller-supplied — indistinguishable at every call site. The rule is the one
/// tenant provisioning already enforces (<c>SaasTenantOnboardingService</c>), lifted here so there is
/// one definition instead of two that can drift.
/// </remarks>
public readonly partial record struct TenantId
{
    private TenantId(string value) => Value = value;

    public string Value { get; }

    public override string ToString() => Value;

    /// <summary>
    /// Creates a tenant id from a value the <em>server</em> owns — configuration, a verified token
    /// claim, a database row. Throws on anything malformed, because a tenant id that fails to parse
    /// at this boundary would otherwise become a scope that matches nothing, or worse, everything.
    /// </summary>
    public static TenantId FromTrustedSource(string? value)
    {
        if (!TryParse(value, out var tenant))
        {
            throw new ArgumentException(
                "A tenant id must be 3-63 characters of lowercase letters, digits, or hyphens, " +
                $"starting and ending with a letter or digit. Got: '{value}'.", nameof(value));
        }

        return tenant;
    }

    public static bool TryParse(string? value, out TenantId tenant)
    {
        var candidate = value?.Trim();
        if (!string.IsNullOrEmpty(candidate) && Pattern().IsMatch(candidate))
        {
            tenant = new TenantId(candidate);
            return true;
        }

        tenant = default;
        return false;
    }

    public bool IsEmpty => string.IsNullOrEmpty(Value);

    [GeneratedRegex(@"^[a-z0-9](?:[a-z0-9-]{1,61}[a-z0-9])$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
