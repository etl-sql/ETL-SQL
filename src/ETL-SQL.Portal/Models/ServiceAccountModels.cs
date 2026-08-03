namespace ETL_SQL.Portal.Models;

public sealed record CreateServiceAccountRequest(
    string Name,
    string? Description,
    int OwnerUserId,
    string[] Scopes,
    string[] Roles,
    DateTime? ExpiresAt,
    /// <summary>Studio capabilities to assign. Capped by the owner's own at token issue.</summary>
    string[]? StudioCapabilities = null);

public sealed record UpdateServiceAccountRequest(
    bool IsEnabled, DateTime? ExpiresAt, string[] Scopes, string[]? StudioCapabilities = null);
public sealed record ServiceAccountTokenRequest(string ClientId, string ClientSecret);
public sealed record ServiceAccountTokenResponse(string AccessToken, string TokenType, int ExpiresIn);

public sealed record ServiceAccountDto(
    string Id,
    string ClientId,
    string Name,
    string? Description,
    int OwnerUserId,
    string[] Scopes,
    string[] Roles,
    bool IsEnabled,
    DateTime? ExpiresAt,
    DateTime? RevokedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? LastUsedAt,
    long Version,
    string[]? StudioCapabilities = null);

public sealed record ServiceAccountCreatedResponse(ServiceAccountDto Account, string ClientSecret);
