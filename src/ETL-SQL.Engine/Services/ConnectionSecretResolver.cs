using System.Text.RegularExpressions;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Governance;

namespace ETL_SQL.Engine.Services;

internal sealed partial class ConnectionSecretResolver(ISecretProvider? secretProvider)
{
    private const string SecretPrefix = "SECRET:";

    public async Task<string> ResolveTargetAsync(
        string target,
        CancellationToken cancellationToken,
        string? connectorType = null,
        IReadOnlyCollection<string>? entrySensitiveFields = null)
    {
        if (string.IsNullOrEmpty(target))
            return target;

        if (IsSecretReference(target))
            return await ResolveSecretValueAsync(target, cancellationToken).ConfigureAwait(false);

        return await ReplaceConnectionStringSecretFieldsAsync(
            target, connectorType, entrySensitiveFields, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Dictionary<string, string>> ResolveOptionsAsync(
        Dictionary<string, string> options,
        CancellationToken cancellationToken,
        string? connectorType = null,
        IReadOnlyCollection<string>? entrySensitiveFields = null)
    {
        var resolved = new Dictionary<string, string>(options, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in options)
        {
            if (!IsSecretReference(value))
                continue;

            if (!IsResolvableField(key, connectorType, entrySensitiveFields))
                throw UnresolvedSecretReferenceError(key);

            resolved[key] = await ResolveSecretValueAsync(value, cancellationToken).ConfigureAwait(false);
        }

        return resolved;
    }

    // Resolvable = built-in credential set ∪ org-designated (global or connector-scoped) ∪ fields
    // the catalog entry itself classifies as sensitive.
    private static bool IsResolvableField(
        string key, string? connectorType, IReadOnlyCollection<string>? entrySensitiveFields) =>
        SecretResolvableFields.IsResolvable(key, connectorType)
        || (entrySensitiveFields?.Contains(key, StringComparer.OrdinalIgnoreCase) ?? false);

    private async Task<string> ReplaceConnectionStringSecretFieldsAsync(
        string target,
        string? connectorType,
        IReadOnlyCollection<string>? entrySensitiveFields,
        CancellationToken cancellationToken)
    {
        var matches = ConnectionStringSecretFieldRegex().Matches(target);
        if (matches.Count == 0)
            return target;

        var resolved = target;
        foreach (Match match in matches.Cast<Match>().Reverse())
        {
            var key = match.Groups["key"].Value;
            if (!IsResolvableField(key, connectorType, entrySensitiveFields))
                throw UnresolvedSecretReferenceError(key);

            var reference = match.Groups["value"].Value;
            var secret = await ResolveSecretValueAsync(reference, cancellationToken).ConfigureAwait(false);
            resolved = resolved.Remove(match.Groups["value"].Index, match.Groups["value"].Length)
                .Insert(match.Groups["value"].Index, secret);
        }

        return resolved;
    }

    // A SECRET: reference on a field outside the resolvable set would otherwise reach the
    // connector as the literal text "SECRET:name" — reject it up front instead.
    private static ExecutionException UnresolvedSecretReferenceError(string key) =>
        new($"Connection field '{key}' uses a SECRET: reference, but secrets are only resolved for credential fields " +
            $"({string.Join(", ", SecretResolvableFields.CredentialKeys.Order(StringComparer.OrdinalIgnoreCase))})" +
            (SecretResolvableFields.OrganizationFields.Count > 0
                ? $" and organization-designated fields ({string.Join(", ", SecretResolvableFields.OrganizationFields.Order(StringComparer.OrdinalIgnoreCase))})"
                : string.Empty) +
            ". The connector would receive the literal reference text instead of the secret value. " +
            "To treat this field as sensitive, add it to Governance:Secrets:SensitiveConnectionFields.");

    private async Task<string> ResolveSecretValueAsync(string reference, CancellationToken cancellationToken)
    {
        if (secretProvider == null)
            throw new InvalidOperationException("A SECRET: reference was used, but no ISecretProvider is configured.");

        var name = reference[SecretPrefix.Length..].Trim();
        var result = await secretProvider.ResolveAsync(name, cancellationToken).ConfigureAwait(false);
        SecretRedactor.RegisterRuntimeSecret(result.Value);
        return result.Value;
    }

    private static bool IsSecretReference(string value) =>
        value.Trim().StartsWith(SecretPrefix, StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"(?i)(?:^|;)\s*(?<key>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<quote>['""]?)(?<value>SECRET:[^;'""]+)\k<quote>")]
    private static partial Regex ConnectionStringSecretFieldRegex();
}
