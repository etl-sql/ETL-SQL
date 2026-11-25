using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ETL_SQL.Portal.Services;

public static class NativeClientScopes
{
    public const string StudioRead = "studio.read";
    public const string StudioRun = "studio.run";
    public const string StudioSave = "studio.save";

    public static readonly IReadOnlyList<string> All = [StudioRead, StudioRun, StudioSave];

    public static string Describe(string scope) => scope switch
    {
        StudioRead => "Read Studio session capabilities, catalog entries, and document summaries",
        StudioRun => "Submit, monitor, and cancel saved pipeline runs",
        StudioSave => "Save linked pipeline documents subject to server publication policy",
        _ => scope
    };

    public static bool TryNormalizeRequested(
        string? rawScopes,
        IReadOnlyList<string> allowedScopes,
        out IReadOnlyList<string> normalized,
        out string? error)
    {
        if (string.IsNullOrWhiteSpace(rawScopes))
        {
            normalized = allowedScopes;
            error = null;
            return true;
        }

        var allowedSet = new HashSet<string>(allowedScopes, StringComparer.Ordinal);
        var tokens = rawScopes.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            normalized = allowedScopes;
            error = null;
            return true;
        }

        var accepted = new List<string>(tokens.Length);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in tokens)
        {
            if (!allowedSet.Contains(token))
            {
                normalized = [];
                error = $"Unsupported or disallowed scope '{token}'.";
                return false;
            }
            if (seen.Add(token))
                accepted.Add(token);
        }

        normalized = accepted;
        error = null;
        return true;
    }

    public static IReadOnlyList<string> ParseStored(string stored) =>
        string.IsNullOrWhiteSpace(stored)
            ? []
            : stored.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public sealed record NativeClientDefinition(
    string ClientId,
    string DisplayName,
    IReadOnlyList<string> AllowedScopes);

public static partial class NativeClientRegistry
{
    public const string VsCodeClientId = "etlsql-vscode";
    public const string TuiClientId = "etlsql-tui";
    public const string DesktopStudioClientId = "etlsql-studio";

    private static readonly Dictionary<string, NativeClientDefinition> Clients = new(StringComparer.Ordinal)
    {
        [VsCodeClientId] = new(VsCodeClientId, "ETL-SQL for VS Code", NativeClientScopes.All),
        [TuiClientId] = new(TuiClientId, "ETL-SQL TUI", NativeClientScopes.All),
        [DesktopStudioClientId] = new(DesktopStudioClientId, "ETL-SQL Studio", NativeClientScopes.All),
    };

    [GeneratedRegex("^[A-Za-z0-9_-]{43}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Base64UrlRegex();

    [GeneratedRegex("^[A-Za-z0-9\\-._~]{43,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex PkceVerifierRegex();

    public static bool TryGet(string? clientId, out NativeClientDefinition? client)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            client = null;
            return false;
        }

        return Clients.TryGetValue(clientId.Trim(), out client);
    }

    public static bool TryValidateLoopbackRedirectUri(
        string? value,
        out string canonicalUri,
        out string? error)
    {
        canonicalUri = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "redirect_uri is required.";
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.Contains('?') || trimmed.Contains('#'))
        {
            error = "redirect_uri must not contain a query string or fragment.";
            return false;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            error = "redirect_uri must be a valid absolute URI.";
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            error = "redirect_uri must use the http scheme for RFC 8252 loopback callbacks.";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            error = "redirect_uri must not contain userinfo.";
            return false;
        }

        var isIpv4Loopback = string.Equals(uri.Host, "127.0.0.1", StringComparison.Ordinal);
        var isIpv6Loopback = string.Equals(uri.Host, "[::1]", StringComparison.Ordinal)
            || string.Equals(uri.Host, "::1", StringComparison.Ordinal);
        if (!isIpv4Loopback && !isIpv6Loopback)
        {
            error = "redirect_uri host must be 127.0.0.1 or [::1].";
            return false;
        }

        if (uri.IsDefaultPort || uri.Port is < 1 or > 65535)
        {
            error = "redirect_uri must specify an explicit loopback port.";
            return false;
        }

        if (!string.Equals(uri.AbsolutePath, "/callback", StringComparison.Ordinal))
        {
            error = "redirect_uri path must be exactly /callback.";
            return false;
        }

        var canonicalHost = isIpv6Loopback ? "[::1]" : "127.0.0.1";
        canonicalUri = $"http://{canonicalHost}:{uri.Port}/callback";
        error = null;
        return true;
    }

    public static bool TryValidateCodeChallenge(
        string? codeChallenge,
        string? codeChallengeMethod,
        out string? error)
    {
        var method = string.IsNullOrWhiteSpace(codeChallengeMethod) ? "S256" : codeChallengeMethod.Trim();
        if (!string.Equals(method, "S256", StringComparison.Ordinal))
        {
            error = "code_challenge_method must be S256.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(codeChallenge) || !Sha256Base64UrlRegex().IsMatch(codeChallenge.Trim()))
        {
            error = "code_challenge must be a valid base64url-encoded SHA-256 digest.";
            return false;
        }

        error = null;
        return true;
    }

    public static bool VerifyPkceS256(string? codeVerifier, string expectedChallenge)
    {
        if (string.IsNullOrWhiteSpace(codeVerifier) || !PkceVerifierRegex().IsMatch(codeVerifier))
            return false;

        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        var computed = Base64UrlEncode(hash);
        var a = Encoding.ASCII.GetBytes(computed);
        var b = Encoding.ASCII.GetBytes(expectedChallenge);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    public static bool TryValidateClientState(string? state, out string? error)
    {
        if (string.IsNullOrWhiteSpace(state) || state.Length is < 8 or > 512)
        {
            error = "state is required and must be between 8 and 512 characters.";
            return false;
        }

        error = null;
        return true;
    }

    public static string BuildCanonicalOrigin(HttpRequest request)
    {
        var scheme = request.Scheme.ToLowerInvariant();
        var host = request.Host.Host.ToLowerInvariant();
        if (host.Contains(':') && !host.StartsWith('['))
            host = $"[{host}]";

        var port = request.Host.Port;
        var omitPort = port is null
            || (scheme == "https" && port == 443)
            || (scheme == "http" && port == 80);

        return omitPort ? $"{scheme}://{host}" : $"{scheme}://{host}:{port}";
    }

    public static string GenerateOpaqueToken(int byteCount = 32) =>
        Base64UrlEncode(RandomNumberGenerator.GetBytes(byteCount));

    public static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
