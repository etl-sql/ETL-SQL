using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Governance;

namespace ETL_SQL.Engine.Services;

/// <summary>
/// Expands a 'SHARED:alias' connection target into the cataloged definition (SSRS
/// shared-data-source model): looks up the alias, verifies the script-declared connector type
/// matches the catalog entry, and merges options. The expanded result still flows through
/// <see cref="ConnectionSecretResolver"/>, so SECRET: references inside the definition resolve
/// under the same rules and redaction as everywhere else.
/// </summary>
internal sealed class SharedConnectionExpander(IConnectionCatalogProvider? catalogProvider)
{
    private const string SharedPrefix = "SHARED:";

    public sealed record ExpandedConnection(
        string Target,
        Dictionary<string, string> Options,
        IReadOnlyCollection<string>? SensitiveFields,
        GatewayResourceBinding? Gateway = null);

    public static bool IsSharedReference(string? target) =>
        !string.IsNullOrEmpty(target) && target.TrimStart().StartsWith(SharedPrefix, StringComparison.OrdinalIgnoreCase);

    public async Task<ExpandedConnection> ExpandAsync(
        string? declaredConnectorType,
        string target,
        Dictionary<string, string>? scriptOptions,
        ExecutionIdentity? identity,
        CancellationToken cancellationToken)
    {
        var alias = target.Trim()[SharedPrefix.Length..].Trim();
        if (alias.Length == 0)
            throw new ExecutionException("A SHARED: reference requires a catalog alias, e.g. 'SHARED:my_sql_server'.");

        if (catalogProvider == null)
        {
            EmitUseDenied(alias, identity, providerName: null, "CatalogProviderMissing");
            throw new ExecutionException(
                $"Connection catalog entry '{alias}' was referenced, but no connection catalog provider is configured " +
                "(Governance:ConnectionCatalog:Provider).");
        }

        SharedConnectionDefinition definition;
        try
        {
            definition = await catalogProvider.ResolveAsync(alias, identity, cancellationToken).ConfigureAwait(false);
        }
        catch (KeyNotFoundException)
        {
            EmitUseDenied(alias, identity, catalogProvider.ProviderName, "AliasNotFound");
            throw new ExecutionException($"Connection catalog entry '{alias}' was not found in provider '{catalogProvider.ProviderName}'.");
        }
        catch (UnauthorizedAccessException ex)
        {
            EmitUseDenied(alias, identity, catalogProvider.ProviderName, "Unauthorized");
            throw new ExecutionException(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            EmitUseDenied(alias, identity, catalogProvider.ProviderName, "Unavailable");
            throw new ExecutionException($"Connection catalog entry '{alias}' cannot be used: {ex.Message}");
        }

        if (definition.Disabled)
        {
            EmitUseDenied(alias, identity, catalogProvider.ProviderName, "Disabled");
            throw new ExecutionException($"Connection catalog entry '{alias}' is disabled.");
        }

        if (definition.Gateway != null)
        {
            if (scriptOptions?.Count > 0)
                throw new ExecutionException("A Gateway-bound SHARED connection does not accept script-supplied connection options.");
            return new ExpandedConnection(string.Empty, new(StringComparer.OrdinalIgnoreCase), null, definition.Gateway);
        }

        if (declaredConnectorType != null
            && !definition.ConnectorType.Equals(declaredConnectorType, StringComparison.OrdinalIgnoreCase))
        {
            EmitUseDenied(alias, identity, catalogProvider.ProviderName, "ConnectorTypeMismatch");
            throw new ExecutionException(
                $"Connection catalog entry '{alias}' is a {definition.ConnectorType} connection, but the script declares " +
                $"{declaredConnectorType}. Match the declared connector type to the catalog entry.");
        }

        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in definition.Options)
            merged[key] = value;

        if (scriptOptions != null)
        {
            foreach (var (key, value) in scriptOptions)
            {
                // The catalog owns credentials; a script overriding them would redirect a shared
                // connection's identity without the administrator's knowledge.
                if (IsCatalogOwnedSensitiveField(definition, key) && definition.Options.ContainsKey(key))
                {
                    EmitUseDenied(alias, identity, catalogProvider.ProviderName, $"CatalogOwnedSensitiveOverride; Option={key}");
                    throw new ExecutionException(
                        $"Option '{key}' is a sensitive field managed by connection catalog entry '{alias}' and cannot " +
                        "be overridden in the script.");
                }

                merged[key] = value;
            }
        }

        return new ExpandedConnection(definition.Target ?? string.Empty, merged, definition.SensitiveFields);
    }

    private static bool IsCatalogOwnedSensitiveField(SharedConnectionDefinition definition, string key) =>
        SecretResolvableFields.IsResolvable(key, definition.ConnectorType)
        || (definition.SensitiveFields?.Contains(key, StringComparer.OrdinalIgnoreCase) ?? false);

    private static void EmitUseDenied(
        string alias,
        ExecutionIdentity? identity,
        string? providerName,
        string reason)
    {
        var actor = string.IsNullOrWhiteSpace(identity?.RealUser)
            ? "unknown"
            : identity.RealUser;
        var effective = string.IsNullOrWhiteSpace(identity?.EffectiveUser)
            ? actor
            : identity.EffectiveUser;

        SecurityEventRuntime.Emit(SecurityEventContract.Create(
            SecurityEventSeverity.Error,
            SecurityEventType.OperationDenied,
            actor,
            effective,
            $"SHARED_CONNECTION:{alias}",
            SecurityEventDecision.Denied,
            $"SHARED_CONNECTION_USE_DENIED: Provider={providerName ?? "(none)"}; Reason={reason}") with
        {
            HostName = Environment.MachineName
        });
    }
}
