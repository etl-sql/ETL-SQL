namespace ETL_SQL.ReportPortal.Models;

public sealed record CreateServiceAccountRequest(
    string Name,
    string? Description,
    int OwnerUserId,
    string[] Scopes,
    string[] Roles,
    DateTime? ExpiresAt);

public sealed record UpdateServiceAccountRequest(bool IsEnabled, DateTime? ExpiresAt, string[] Scopes);
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
    long Version);

public sealed record ServiceAccountCreatedResponse(ServiceAccountDto Account, string ClientSecret);
