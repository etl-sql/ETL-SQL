using System.Text.Json.Serialization;

namespace ETL_SQL.Core.Hosted;

/// <summary>Registered native client identifiers accepted by Portal's /api/auth/client/* endpoints.</summary>
public static class HostedClientIds
{
    public const string VsCode = "etlsql-vscode";
    public const string Tui = "etlsql-tui";
    public const string DesktopStudio = "etlsql-studio";
}

/// <summary>Bounded scopes supported by Portal native client sessions.</summary>
public static class HostedClientScopes
{
    public const string StudioRead = "studio.read";
    public const string StudioRun = "studio.run";
    public const string StudioSave = "studio.save";

    public const string TuiDefault = $"{StudioRead} {StudioRun}";
    public const string DesktopStudioDefault = $"{StudioRead} {StudioRun} {StudioSave}";
}

/// <summary>
/// A user-level trusted Portal server entry persisted in user settings.
/// Never stores tokens, refresh tokens, or credentials.
/// </summary>
public sealed record HostedServerEntry(
    string Alias,
    string Label,
    string Origin,
    string? Environment = null);

public enum ExecutionTargetKind
{
    Local,
    Hosted
}

public sealed record ExecutionTarget(
    ExecutionTargetKind Kind,
    HostedServerEntry? Server = null)
{
    public static ExecutionTarget Local { get; } = new(ExecutionTargetKind.Local, null);
    public static ExecutionTarget Hosted(HostedServerEntry server) => new(ExecutionTargetKind.Hosted, server);
}

public sealed record HostedAuthProvidersDto(
    bool LocalEnabled,
    bool OidcEnabled,
    string? OidcDisplayName,
    bool LdapEnabled);

public sealed record HostedClientUserDto(
    int Id,
    string Username,
    string? Email = null,
    IReadOnlyList<string>? Roles = null);

public sealed record HostedClientTokenResponseDto(
    string Token,
    string RefreshToken,
    DateTime ExpiresAt,
    string SessionId,
    string ClientId,
    string Audience,
    string TenantId,
    IReadOnlyList<string> Scopes,
    HostedClientUserDto User);

public sealed record HostedStudioSessionDto(
    string Mode,
    IReadOnlyList<string> Capabilities,
    bool SourceControlEnabled,
    bool DraftRecovery,
    bool PrivateDrafts,
    int MaxScriptCharacters);

/// <summary>
/// Active in-memory native client session. Never serialized to disk or settings.
/// </summary>
public sealed record HostedSession(
    HostedServerEntry Server,
    string SessionId,
    string Token,
    string RefreshToken,
    DateTimeOffset ExpiresAtUtc,
    string TenantId,
    HostedClientUserDto User,
    IReadOnlyList<string> Scopes,
    HostedStudioSessionDto StudioSession)
{
    [JsonIgnore]
    public IEnumerable<string> ActiveSecrets
    {
        get
        {
            if (!string.IsNullOrEmpty(Token)) yield return Token;
            if (!string.IsNullOrEmpty(RefreshToken)) yield return RefreshToken;
        }
    }
}

public sealed record RunnablePipelineDto(
    int Id,
    string Name,
    string FolderPath,
    long Version,
    bool CanAuthor);

public sealed record RunnablePipelinePageDto(
    IReadOnlyList<RunnablePipelineDto> Items,
    int? NextAfterId);

public sealed record PipelineRunSubmittedDto(
    long RunId,
    int PipelineId,
    long Version,
    string ScriptHash,
    string JobName,
    bool Replayed);

public sealed record PipelineRunStatementDto(
    string Statement,
    long DurationMs,
    long RowsProcessed,
    bool Failed);

public sealed record PipelineRunDto(
    long RunId,
    int PipelineId,
    string JobName,
    string Status,
    bool IsTerminal,
    DateTime StartTime,
    DateTime? EndTime,
    string? ErrorMessage,
    long RowsProcessed,
    string? ScriptHash,
    bool LogTruncated,
    IReadOnlyList<PipelineRunStatementDto> Statements);

public sealed record PipelineRunCancelRequestedDto(
    long RunId,
    int PipelineId);

public sealed record PipelineRunRefusalDto(
    string Code,
    string Error,
    long? CurrentVersion = null);

public enum PendingSubmissionStatus
{
    Unconfirmed,
    Unknown
}

public sealed record PendingSubmissionRecord(
    string IdempotencyKey,
    string Origin,
    string TenantId,
    int UserId,
    string Username,
    int PipelineId,
    string PipelineName,
    long Version,
    string JobName,
    PendingSubmissionStatus Status,
    DateTimeOffset CreatedAtUtc);

public sealed record ActiveHostedRun(
    int PipelineId,
    string PipelineName,
    long RunId,
    string JobName,
    string Origin,
    string TenantId,
    int UserId,
    string Username,
    long Version,
    string ScriptHash)
{
    public bool CancelRequested { get; set; }
}

public sealed class HostedSessionExpiredException(string message, Exception? inner = null)
    : Exception(message, inner);

public sealed class HostedNetworkException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>Validates trusted hosted Portal origins (HTTPS required; HTTP allowed only for loopback).</summary>
public static class HostedOriginValidator
{
    public static bool TryNormalizeTrustedOrigin(
        string? rawOrigin,
        out string canonicalOrigin,
        out string host,
        out string? error)
    {
        canonicalOrigin = string.Empty;
        host = string.Empty;

        if (string.IsNullOrWhiteSpace(rawOrigin))
        {
            error = "Portal URL is required.";
            return false;
        }

        var trimmed = rawOrigin.Trim().TrimEnd('/');
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            error = "Portal URL must be a valid absolute URL (for example https://portal.company.internal).";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            error = "Portal URL must not include embedded credentials.";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
            || (!string.IsNullOrEmpty(uri.AbsolutePath) && uri.AbsolutePath != "/"))
        {
            error = "Portal URL must be an origin only (scheme, host, and optional port without path or query).";
            return false;
        }

        var scheme = uri.Scheme.ToLowerInvariant();
        var lowerHost = uri.Host.ToLowerInvariant();
        var isLoopback = lowerHost is "localhost" or "127.0.0.1" or "::1" or "[::1]";

        if (scheme == "http")
        {
            if (!isLoopback)
            {
                error = "Hosted Portal servers must use https://. Plain http:// is allowed only for explicit localhost or 127.0.0.1 development.";
                return false;
            }
        }
        else if (scheme != "https")
        {
            error = "Portal URL must use https:// (or http:// for explicit loopback development).";
            return false;
        }

        var formattedHost = lowerHost.Contains(':') && !lowerHost.StartsWith('[')
            ? $"[{lowerHost}]"
            : lowerHost;
        var omitPort = uri.IsDefaultPort
            || (scheme == "https" && uri.Port == 443)
            || (scheme == "http" && uri.Port == 80);

        canonicalOrigin = omitPort ? $"{scheme}://{formattedHost}" : $"{scheme}://{formattedHost}:{uri.Port}";
        host = formattedHost;
        error = null;
        return true;
    }
}

/// <summary>Redacts known tokens, authorization codes, and PKCE verifiers from user-facing or log strings.</summary>
public static class HostedSecretRedactor
{
    public static string Redact(string? text, IEnumerable<string?> secrets)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var result = text;
        foreach (var secret in secrets)
        {
            if (!string.IsNullOrEmpty(secret) && secret.Length >= 8)
                result = result.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
        }

        return result;
    }
}
