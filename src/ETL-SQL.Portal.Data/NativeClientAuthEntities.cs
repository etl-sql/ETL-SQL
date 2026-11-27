namespace ETL_SQL.Portal.Data;

public sealed class NativeClientAuthTransaction
{
    public string Id { get; set; } = "";
    public string TenantId { get; set; } = "portal-host";
    public string CanonicalOrigin { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string RedirectUri { get; set; } = "";
    public string CodeChallenge { get; set; } = "";
    public string CodeChallengeMethod { get; set; } = "S256";
    public string ClientState { get; set; } = "";
    public string RequestedScopes { get; set; } = "";
    public string CsrfTokenHash { get; set; } = "";
    public int? UserId { get; set; }
    public string Status { get; set; } = "PendingLogin";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public sealed class NativeClientAuthCode
{
    public string CodeHash { get; set; } = "";
    public string TransactionId { get; set; } = "";
    public string TenantId { get; set; } = "portal-host";
    public string CanonicalOrigin { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string RedirectUri { get; set; } = "";
    public string CodeChallenge { get; set; } = "";
    public string CodeChallengeMethod { get; set; } = "S256";
    public string Scopes { get; set; } = "";
    public int UserId { get; set; }
    public PortalUser User { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public string? IssuedSessionId { get; set; }
}

public sealed class NativeClientSession
{
    public string Id { get; set; } = "";
    public string TenantId { get; set; } = "portal-host";
    public int UserId { get; set; }
    public PortalUser User { get; set; } = null!;
    public string ClientId { get; set; } = "";
    public string Scopes { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastRefreshedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }

    public ICollection<NativeClientRefreshToken> RefreshTokens { get; set; } = [];
}

public sealed class NativeClientRefreshToken
{
    public int Id { get; set; }
    public string SessionId { get; set; } = "";
    public NativeClientSession Session { get; set; } = null!;
    public string TenantId { get; set; } = "portal-host";
    public int UserId { get; set; }
    public PortalUser User { get; set; } = null!;
    public string TokenHash { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}
