using System;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Governance;

namespace ETL_SQL.App.Admin;

/// <summary>
/// Service-account credentials for the admin CLI, and the rules for obtaining them.
///
/// <para><b>Never from argv.</b> A command line is visible to every process on the host, lands in
/// shell history, and is captured verbatim by CI logs. The client secret is therefore accepted only
/// from the environment or from a <c>SECRET:name</c> reference resolved through the machine secret
/// store — the same never-echo discipline the secret admin verbs already follow.</para>
/// </summary>
public sealed record PortalAdminCredentials(string ClientId, string ClientSecret)
{
    public const string UrlVariable = "ETLSQL_PORTAL_URL";
    public const string ClientIdVariable = "ETLSQL_PORTAL_CLIENT_ID";
    public const string SecretVariable = "ETLSQL_PORTAL_CLIENT_SECRET";

    /// <summary>
    /// Resolves credentials from the environment, dereferencing a <c>SECRET:name</c> value through
    /// <paramref name="secrets"/>. <paramref name="clientIdOverride"/> may come from a flag — a
    /// client id is an identifier, not a secret — but the secret itself never may.
    /// </summary>
    public static async Task<PortalAdminCredentials> ResolveAsync(
        ISecretProvider? secrets, string? clientIdOverride, CancellationToken ct)
    {
        var clientId = Coalesce(clientIdOverride, Environment.GetEnvironmentVariable(ClientIdVariable));
        if (string.IsNullOrWhiteSpace(clientId))
            throw new AdminCliException(AdminExitCode.AuthFailure,
                $"No client id. Set {ClientIdVariable} or pass --client-id.");

        var rawSecret = Environment.GetEnvironmentVariable(SecretVariable);
        if (string.IsNullOrWhiteSpace(rawSecret))
            throw new AdminCliException(AdminExitCode.AuthFailure,
                $"No client secret. Set {SecretVariable}, optionally to a SECRET:name reference. " +
                "The secret is never accepted as a command-line argument.");

        var secret = await DereferenceAsync(rawSecret.Trim(), secrets, ct);
        return new PortalAdminCredentials(clientId.Trim(), secret);
    }

    /// <summary>Resolves the Portal base URL from a flag or the environment.</summary>
    public static string ResolveUrl(string? urlOverride)
    {
        var url = Coalesce(urlOverride, Environment.GetEnvironmentVariable(UrlVariable));
        if (string.IsNullOrWhiteSpace(url))
            throw new AdminCliException(AdminExitCode.AuthFailure,
                $"No Portal URL. Set {UrlVariable} or pass --portal-url.");

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
            throw new AdminCliException(AdminExitCode.AuthFailure,
                $"'{url}' is not an absolute http(s) URL.");

        return parsed.ToString().TrimEnd('/');
    }

    private static async Task<string> DereferenceAsync(string value, ISecretProvider? secrets, CancellationToken ct)
    {
        if (!value.StartsWith("SECRET:", StringComparison.OrdinalIgnoreCase)) return value;

        var name = value["SECRET:".Length..].Trim().Trim('\'', '"');
        if (name.Length == 0)
            throw new AdminCliException(AdminExitCode.AuthFailure, "SECRET: reference has no name.");
        if (secrets is null)
            throw new AdminCliException(AdminExitCode.AuthFailure,
                $"'{value}' is a secret reference, but no secret store is configured to resolve it.");

        try
        {
            var resolved = await secrets.ResolveAsync(name, ct);
            if (string.IsNullOrEmpty(resolved.Value))
                throw new AdminCliException(AdminExitCode.AuthFailure, $"Secret '{name}' resolved to an empty value.");
            return resolved.Value;
        }
        catch (AdminCliException) { throw; }
        catch (Exception ex)
        {
            // The message deliberately names the secret, never its value.
            throw new AdminCliException(AdminExitCode.AuthFailure,
                $"Could not resolve secret '{name}': {ex.Message}");
        }
    }

    private static string? Coalesce(string? preferred, string? fallback) =>
        string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;

    /// <summary>Never let the secret reach a log, a screen, or an exception message.</summary>
    public override string ToString() => $"PortalAdminCredentials {{ ClientId = {ClientId} }}";
}
