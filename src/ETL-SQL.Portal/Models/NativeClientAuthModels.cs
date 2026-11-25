using System.Text.Json.Serialization;

namespace ETL_SQL.Portal.Models;

public sealed record NativeClientExchangeRequest(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("code_verifier")] string CodeVerifier,
    [property: JsonPropertyName("redirect_uri")] string RedirectUri,
    [property: JsonPropertyName("client_id")] string ClientId);

public sealed record NativeClientRefreshRequest(
    string RefreshToken,
    string? ClientId = null);

public sealed record NativeClientLogoutRequest(
    string? RefreshToken = null);

public sealed record NativeClientUserDto(
    int Id,
    string Username,
    string? Email,
    IReadOnlyList<string> Roles);

public sealed record NativeClientTokenResponse(
    string Token,
    string RefreshToken,
    DateTime ExpiresAt,
    string SessionId,
    string ClientId,
    NativeClientUserDto User,
    string TenantId,
    string Audience,
    IReadOnlyList<string> Scopes);
