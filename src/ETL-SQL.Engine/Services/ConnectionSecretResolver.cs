using System.Text.RegularExpressions;
using ETL_SQL.Core.Governance;

namespace ETL_SQL.Engine.Services;

internal sealed partial class ConnectionSecretResolver(ISecretProvider? secretProvider)
{
    private const string SecretPrefix = "SECRET:";

    private static readonly HashSet<string> SensitiveOptionKeys = new(StringComparer.OrdinalIgnoreCase)
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
        "SAS_TOKEN",
        "ACCOUNT_KEY",
        "PASSPHRASE",
        "PRIVATE_KEY",
        "SASL_PASSWORD",
        "SASL_JAAS_CONFIG"
    };

    public async Task<string> ResolveTargetAsync(string target, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(target))
            return target;

        if (IsSecretReference(target))
            return await ResolveSecretValueAsync(target, cancellationToken).ConfigureAwait(false);

        return await ReplaceConnectionStringSecretFieldsAsync(target, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Dictionary<string, string>> ResolveOptionsAsync(
        Dictionary<string, string> options,
        CancellationToken cancellationToken)
    {
        var resolved = new Dictionary<string, string>(options, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in options)
        {
            if (SensitiveOptionKeys.Contains(key) && IsSecretReference(value))
                resolved[key] = await ResolveSecretValueAsync(value, cancellationToken).ConfigureAwait(false);
        }

        return resolved;
    }

    private async Task<string> ReplaceConnectionStringSecretFieldsAsync(
        string target,
        CancellationToken cancellationToken)
    {
        var matches = ConnectionStringSecretFieldRegex().Matches(target);
        if (matches.Count == 0)
            return target;

        var resolved = target;
        foreach (Match match in matches.Cast<Match>().Reverse())
        {
            var reference = match.Groups["value"].Value;
            var secret = await ResolveSecretValueAsync(reference, cancellationToken).ConfigureAwait(false);
            resolved = resolved.Remove(match.Groups["value"].Index, match.Groups["value"].Length)
                .Insert(match.Groups["value"].Index, secret);
        }

        return resolved;
    }

    private async Task<string> ResolveSecretValueAsync(string reference, CancellationToken cancellationToken)
    {
        if (secretProvider == null)
            throw new InvalidOperationException("A SECRET: reference was used, but no ISecretProvider is configured.");

        var name = reference[SecretPrefix.Length..].Trim();
        var result = await secretProvider.ResolveAsync(name, cancellationToken).ConfigureAwait(false);
        return result.Value;
    }

    private static bool IsSecretReference(string value) =>
        value.Trim().StartsWith(SecretPrefix, StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"(?i)(?:^|;)\s*(?:PASSWORD|PWD|API_KEY|APIKEY|TOKEN|ACCESS_TOKEN|REFRESH_TOKEN|CLIENT_SECRET|CLIENTSECRET|SECRET|SECRET_KEY|SECRETKEY|SAS_TOKEN|ACCOUNT_KEY|PASSPHRASE|PRIVATE_KEY|SASL_PASSWORD|SASL_JAAS_CONFIG)\s*=\s*(?<quote>['""]?)(?<value>SECRET:[^;'""]+)\k<quote>")]
    private static partial Regex ConnectionStringSecretFieldRegex();
}
