using ETL_SQL.Core.Multitenancy;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Validates the server-owned scope attached to host key-provider bindings. Request and job input
/// never participate in this decision.
/// </summary>
public static class KeyManagementBindingScope
{
    public static string Resolve(PortalConfig config, KeyManagementBindingConfig binding)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(binding);

        if (config.SharedTenancy.Enabled)
        {
            if (string.IsNullOrWhiteSpace(binding.Scope))
                throw new InvalidOperationException(
                    "Shared key-management bindings require an explicit server-configured Scope.");
            return TenantId.FromTrustedSource(binding.Scope).Value;
        }

        var hostScope = string.IsNullOrWhiteSpace(config.TenantId)
            ? "portal-host"
            : TenantId.FromTrustedSource(config.TenantId).Value;
        if (!string.IsNullOrWhiteSpace(binding.Scope)
            && !string.Equals(
                TenantId.FromTrustedSource(binding.Scope).Value,
                hostScope,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Key-management binding scope '{binding.Scope}' does not match host scope '{hostScope}'.");
        }
        return hostScope;
    }

    public static IReadOnlyList<string> ConfiguredScopes(PortalConfig config) =>
        config.KeyManagement.Bindings
            .Select(binding => Resolve(config, binding))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(scope => scope, StringComparer.Ordinal)
            .ToArray();
}
