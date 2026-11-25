using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

public sealed class NativeClientAuthService(
    PortalDbContext db,
    UserManager<PortalUser> userManager,
    TokenService tokenService,
    StudioCapabilityStore studioCapabilities,
    SharedIdentityAuthorityResolver sharedAuthorities,
    RequestTenantContextAccessor tenantAccessor,
    AuditService auditService,
    PortalConfig config,
    IDataProtectionProvider dataProtection,
    TimeProvider clock)
{
    public const string TransactionCookieName = "ETLSQL_NATIVE_AUTH_TX";
    public static readonly TimeSpan TransactionLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan AuthorizationCodeLifetime = TimeSpan.FromSeconds(60);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IDataProtector _protector = dataProtection.CreateProtector("ETL_SQL.Portal.NativeClient.AuthTx.v1");

    public sealed record TxCookiePayload(
        string TransactionId,
        string CsrfToken,
        string CanonicalOrigin,
        string TenantId,
        int? AuthenticatedUserId,
        DateTimeOffset ExpiresAtUtc);

    public sealed record AuthorizeStartResult(
        bool Succeeded,
        int StatusCode,
        string? Error,
        string? Detail,
        NativeClientAuthTransaction? Transaction,
        string? ProtectedCookie,
        bool UseOidc);

    public sealed record PendingConsentView(
        NativeClientAuthTransaction Transaction,
        NativeClientDefinition Client,
        PortalUser User,
        string CsrfToken,
        IReadOnlyList<string> Scopes);

    public sealed record ConsentOutcome(
        bool Succeeded,
        int StatusCode,
        string? Error,
        string? RedirectUrl);

    public sealed record TokenOutcome(
        bool Succeeded,
        int StatusCode,
        string? Error,
        string? Detail,
        NativeClientTokenResponse? Response);

    public CookieOptions BuildTransactionCookieOptions(HttpRequest request, bool expires) => new()
    {
        HttpOnly = true,
        Secure = request.IsHttps,
        SameSite = SameSiteMode.Lax,
        IsEssential = true,
        Path = "/api/auth",
        MaxAge = expires ? TransactionLifetime : TimeSpan.Zero
    };

    public TxCookiePayload? UnprotectTransactionCookie(string? cookieValue)
    {
        if (string.IsNullOrWhiteSpace(cookieValue))
            return null;
        try
        {
            var payload = JsonSerializer.Deserialize<TxCookiePayload>(_protector.Unprotect(cookieValue), Json);
            if (payload is null || payload.ExpiresAtUtc <= clock.GetUtcNow())
                return null;
            return payload;
        }
        catch
        {
            return null;
        }
    }

    public string ProtectTransactionCookie(TxCookiePayload payload) =>
        _protector.Protect(JsonSerializer.Serialize(payload, Json));

    public async Task<AuthorizeStartResult> StartAuthorizationAsync(
        HttpRequest request,
        string? clientId,
        string? redirectUri,
        string? codeChallenge,
        string? codeChallengeMethod,
        string? state,
        string? scope,
        CancellationToken ct)
    {
        if (!NativeClientRegistry.TryGet(clientId, out var client) || client is null)
            return FailedStart(StatusCodes.Status400BadRequest, "invalid_client", "Unknown or unregistered native client_id.");

        if (!NativeClientRegistry.TryValidateLoopbackRedirectUri(redirectUri, out var canonicalRedirect, out var redirectError))
            return FailedStart(StatusCodes.Status400BadRequest, "invalid_request", redirectError);

        if (!NativeClientRegistry.TryValidateCodeChallenge(codeChallenge, codeChallengeMethod, out var pkceError))
            return FailedStart(StatusCodes.Status400BadRequest, "invalid_request", pkceError);

        if (!NativeClientRegistry.TryValidateClientState(state, out var stateError))
            return FailedStart(StatusCodes.Status400BadRequest, "invalid_request", stateError);

        if (!NativeClientScopes.TryNormalizeRequested(scope, client.AllowedScopes, out var scopes, out var scopeError))
            return FailedStart(StatusCodes.Status400BadRequest, "invalid_scope", scopeError);

        string tenantId;
        bool useOidc;
        if (config.SharedTenancy.Enabled)
        {
            var authority = await sharedAuthorities.ResolveForRequestAsync(request, ct);
            if (authority is null)
                return FailedStart(StatusCodes.Status404NotFound, "oidc_not_configured", "No identity authority is configured for this host.");
            tenantId = authority.TenantId;
            useOidc = true;
        }
        else
        {
            tenantId = ResolveDedicatedTenantId();
            useOidc = config.Identity.Oidc.Enabled;
        }

        var now = clock.GetUtcNow();
        var txId = NativeClientRegistry.GenerateOpaqueToken(24);
        var csrfToken = NativeClientRegistry.GenerateOpaqueToken(24);
        var origin = NativeClientRegistry.BuildCanonicalOrigin(request);

        var tx = new NativeClientAuthTransaction
        {
            Id = txId,
            TenantId = tenantId,
            CanonicalOrigin = origin,
            ClientId = client.ClientId,
            RedirectUri = canonicalRedirect,
            CodeChallenge = codeChallenge!.Trim(),
            CodeChallengeMethod = "S256",
            ClientState = state!,
            RequestedScopes = string.Join(' ', scopes),
            CsrfTokenHash = TokenService.HashRefreshToken(csrfToken),
            Status = "PendingLogin",
            CreatedAt = now.UtcDateTime,
            ExpiresAt = now.Add(TransactionLifetime).UtcDateTime
        };

        db.NativeClientAuthTransactions.Add(tx);
        await db.SaveChangesAsync(ct);

        var cookie = ProtectTransactionCookie(new TxCookiePayload(
            tx.Id,
            csrfToken,
            origin,
            tenantId,
            AuthenticatedUserId: null,
            ExpiresAtUtc: now.Add(TransactionLifetime)));

        return new AuthorizeStartResult(true, StatusCodes.Status302Found, null, null, tx, cookie, useOidc);
    }

    public async Task<(NativeClientAuthTransaction? Transaction, TxCookiePayload? Cookie)> ValidatePendingLoginAsync(
        HttpRequest request,
        string? txId,
        string? expectedTenantId = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(txId))
            return (null, null);

        var cookie = UnprotectTransactionCookie(request.Cookies[TransactionCookieName]);
        var currentOrigin = NativeClientRegistry.BuildCanonicalOrigin(request);
        if (cookie is null
            || !CryptoEquals(cookie.TransactionId, txId.Trim())
            || !string.Equals(cookie.CanonicalOrigin, currentOrigin, StringComparison.OrdinalIgnoreCase))
            return (null, null);

        if (expectedTenantId is not null
            && !string.Equals(cookie.TenantId, expectedTenantId, StringComparison.Ordinal))
            return (null, null);

        var now = clock.GetUtcNow().UtcDateTime;
        var tx = await db.NativeClientAuthTransactions
            .FirstOrDefaultAsync(t => t.Id == txId.Trim(), ct);
        if (tx is null
            || tx.Status != "PendingLogin"
            || tx.ExpiresAt <= now
            || !string.Equals(tx.CanonicalOrigin, currentOrigin, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(tx.TenantId, cookie.TenantId, StringComparison.Ordinal))
            return (null, null);

        return (tx, cookie);
    }

    public async Task<string?> CompleteLoginAsync(
        HttpRequest request,
        string txId,
        PortalUser user,
        string verifiedTenantId,
        CancellationToken ct = default)
    {
        var (tx, cookie) = await ValidatePendingLoginAsync(request, txId, verifiedTenantId, ct);
        if (tx is null || cookie is null)
            return null;

        if (!string.Equals(user.TenantId, verifiedTenantId, StringComparison.Ordinal))
            return null;

        var now = clock.GetUtcNow().UtcDateTime;
        var updated = await db.NativeClientAuthTransactions
            .Where(t => t.Id == tx.Id
                && t.TenantId == verifiedTenantId
                && t.Status == "PendingLogin"
                && t.ExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(t => t.UserId, user.Id)
                .SetProperty(t => t.Status, "PendingConsent"), ct);
        if (updated != 1)
            return null;

        return ProtectTransactionCookie(cookie with { AuthenticatedUserId = user.Id, TenantId = verifiedTenantId });
    }

    public async Task<PendingConsentView?> LoadPendingConsentAsync(
        HttpRequest request,
        string? txId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(txId))
            return null;

        var cookie = UnprotectTransactionCookie(request.Cookies[TransactionCookieName]);
        var currentOrigin = NativeClientRegistry.BuildCanonicalOrigin(request);
        if (cookie is null
            || cookie.AuthenticatedUserId is null
            || !CryptoEquals(cookie.TransactionId, txId.Trim())
            || !string.Equals(cookie.CanonicalOrigin, currentOrigin, StringComparison.OrdinalIgnoreCase))
            return null;

        if (config.SharedTenancy.Enabled)
        {
            var authority = await sharedAuthorities.ResolveForRequestAsync(request, ct);
            if (authority is null || !string.Equals(authority.TenantId, cookie.TenantId, StringComparison.Ordinal))
                return null;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var tx = await db.NativeClientAuthTransactions
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == txId.Trim(), ct);
        if (tx is null
            || tx.Status != "PendingConsent"
            || tx.ExpiresAt <= now
            || tx.UserId != cookie.AuthenticatedUserId
            || !string.Equals(tx.TenantId, cookie.TenantId, StringComparison.Ordinal)
            || !string.Equals(tx.CanonicalOrigin, currentOrigin, StringComparison.OrdinalIgnoreCase))
            return null;

        if (!NativeClientRegistry.TryGet(tx.ClientId, out var client) || client is null)
            return null;

        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == tx.UserId.Value && u.TenantId == tx.TenantId, ct);
        if (user is null || !user.IsActive)
            return null;

        return new PendingConsentView(
            tx,
            client,
            user,
            cookie.CsrfToken,
            NativeClientScopes.ParseStored(tx.RequestedScopes));
    }

    public async Task<ConsentOutcome> CompleteConsentAsync(
        HttpRequest request,
        string? txId,
        string? csrfToken,
        string? decision,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(txId) || string.IsNullOrWhiteSpace(csrfToken))
            return new ConsentOutcome(false, StatusCodes.Status400BadRequest, "invalid_request", null);

        var currentOrigin = NativeClientRegistry.BuildCanonicalOrigin(request);
        if (!ValidateSameOriginHeaders(request, currentOrigin))
            return new ConsentOutcome(false, StatusCodes.Status403Forbidden, "invalid_origin", null);

        var cookie = UnprotectTransactionCookie(request.Cookies[TransactionCookieName]);
        if (cookie is null
            || cookie.AuthenticatedUserId is null
            || !CryptoEquals(cookie.TransactionId, txId.Trim())
            || !CryptoEquals(cookie.CsrfToken, csrfToken)
            || !string.Equals(cookie.CanonicalOrigin, currentOrigin, StringComparison.OrdinalIgnoreCase))
            return new ConsentOutcome(false, StatusCodes.Status403Forbidden, "invalid_csrf_or_session", null);

        if (config.SharedTenancy.Enabled)
        {
            var authority = await sharedAuthorities.ResolveForRequestAsync(request, ct);
            if (authority is null || !string.Equals(authority.TenantId, cookie.TenantId, StringComparison.Ordinal))
                return new ConsentOutcome(false, StatusCodes.Status403Forbidden, "tenant_mismatch", null);
            tenantAccessor.SetVerifiedCredential(TenantContext.FromVerifiedCredential(authority.TenantId));
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var tx = await db.NativeClientAuthTransactions
            .FirstOrDefaultAsync(t => t.Id == txId.Trim(), ct);
        if (tx is null
            || tx.Status != "PendingConsent"
            || tx.ExpiresAt <= now
            || tx.UserId != cookie.AuthenticatedUserId
            || !string.Equals(tx.TenantId, cookie.TenantId, StringComparison.Ordinal)
            || !string.Equals(tx.CanonicalOrigin, currentOrigin, StringComparison.OrdinalIgnoreCase))
            return new ConsentOutcome(false, StatusCodes.Status400BadRequest, "invalid_transaction", null);

        var expectedCsrfHash = TokenService.HashRefreshToken(csrfToken);
        if (!CryptoEquals(expectedCsrfHash, tx.CsrfTokenHash))
            return new ConsentOutcome(false, StatusCodes.Status403Forbidden, "invalid_csrf_or_session", null);

        var approve = string.Equals(decision, "approve", StringComparison.OrdinalIgnoreCase);
        var nextStatus = approve ? "Approved" : "Denied";

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var transitioned = await db.NativeClientAuthTransactions
            .Where(t => t.Id == tx.Id
                && t.TenantId == tx.TenantId
                && t.UserId == tx.UserId
                && t.Status == "PendingConsent"
                && t.ExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(t => t.Status, nextStatus)
                .SetProperty(t => t.CompletedAt, now), ct);
        if (transitioned != 1)
        {
            await transaction.RollbackAsync(ct);
            return new ConsentOutcome(false, StatusCodes.Status400BadRequest, "transaction_already_completed", null);
        }

        if (!approve)
        {
            await transaction.CommitAsync(ct);
            var deniedUrl = $"{tx.RedirectUri}?error=access_denied&state={Uri.EscapeDataString(tx.ClientState)}";
            return new ConsentOutcome(true, StatusCodes.Status302Found, null, deniedUrl);
        }

        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == tx.UserId.Value && u.TenantId == tx.TenantId, ct);
        if (user is null || !user.IsActive || user.MustChangePassword)
        {
            await transaction.RollbackAsync(ct);
            return new ConsentOutcome(false, StatusCodes.Status403Forbidden, "account_unavailable", null);
        }

        var rawCode = NativeClientRegistry.GenerateOpaqueToken(32);
        db.NativeClientAuthCodes.Add(new NativeClientAuthCode
        {
            CodeHash = TokenService.HashRefreshToken(rawCode),
            TransactionId = tx.Id,
            TenantId = tx.TenantId,
            CanonicalOrigin = tx.CanonicalOrigin,
            ClientId = tx.ClientId,
            RedirectUri = tx.RedirectUri,
            CodeChallenge = tx.CodeChallenge,
            CodeChallengeMethod = tx.CodeChallengeMethod,
            Scopes = tx.RequestedScopes,
            UserId = user.Id,
            CreatedAt = now,
            ExpiresAt = now.Add(AuthorizationCodeLifetime)
        });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        var callbackUrl = $"{tx.RedirectUri}?code={Uri.EscapeDataString(rawCode)}&state={Uri.EscapeDataString(tx.ClientState)}";
        return new ConsentOutcome(true, StatusCodes.Status302Found, null, callbackUrl);
    }

    public async Task<TokenOutcome> ExchangeCodeAsync(
        HttpRequest request,
        NativeClientExchangeRequest req,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Code)
            || string.IsNullOrWhiteSpace(req.CodeVerifier)
            || string.IsNullOrWhiteSpace(req.RedirectUri)
            || string.IsNullOrWhiteSpace(req.ClientId))
            return FailedToken(StatusCodes.Status400BadRequest, "invalid_request", "code, code_verifier, redirect_uri, and client_id are required.");

        if (!NativeClientRegistry.TryGet(req.ClientId, out _))
            return FailedToken(StatusCodes.Status400BadRequest, "invalid_grant", "Invalid authorization grant.");

        if (!NativeClientRegistry.TryValidateLoopbackRedirectUri(req.RedirectUri, out var canonicalRedirect, out _))
            return FailedToken(StatusCodes.Status400BadRequest, "invalid_grant", "Invalid authorization grant.");

        var codeHash = TokenService.HashRefreshToken(req.Code.Trim());
        var authCode = await db.NativeClientAuthCodes
            .FirstOrDefaultAsync(c => c.CodeHash == codeHash, ct);
        if (authCode is null)
            return FailedToken(StatusCodes.Status400BadRequest, "invalid_grant", "Invalid authorization grant.");

        var now = clock.GetUtcNow().UtcDateTime;

        if (authCode.ExpiresAt <= now)
            return FailedToken(StatusCodes.Status400BadRequest, "invalid_grant", "Authorization code has expired.");

        var currentOrigin = NativeClientRegistry.BuildCanonicalOrigin(request);
        if (!string.Equals(authCode.CanonicalOrigin, currentOrigin, StringComparison.OrdinalIgnoreCase))
            return FailedToken(StatusCodes.Status400BadRequest, "invalid_grant", "Authorization code origin mismatch.");

        var tenantContext = await ResolveAndVerifyTenantForRequestAsync(request, authCode.TenantId, ct);
        if (tenantContext.Failed)
            return FailedToken(StatusCodes.Status400BadRequest, "invalid_grant", "Authorization code tenant mismatch.");

        if (!string.Equals(authCode.ClientId, req.ClientId.Trim(), StringComparison.Ordinal)
            || !string.Equals(authCode.RedirectUri, canonicalRedirect, StringComparison.Ordinal)
            || !NativeClientRegistry.VerifyPkceS256(req.CodeVerifier, authCode.CodeChallenge))
            return FailedToken(StatusCodes.Status400BadRequest, "invalid_grant", "Invalid client_id, redirect_uri, or PKCE code_verifier.");

        if (authCode.ConsumedAt is not null)
            return await RejectCodeReplayAsync(authCode, now, ct);

        var sessionId = NativeClientRegistry.GenerateOpaqueToken(24);
        var rawRefresh = tokenService.GenerateRefreshToken();
        var sessionExpires = now.AddDays(config.Jwt.RefreshExpiryDays);

        PortalUser? user;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            var consumed = await db.NativeClientAuthCodes
                .Where(c => c.CodeHash == codeHash
                    && c.TenantId == authCode.TenantId
                    && c.ConsumedAt == null
                    && c.ExpiresAt > now)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(c => c.ConsumedAt, now)
                    .SetProperty(c => c.IssuedSessionId, sessionId), ct);
            if (consumed != 1)
            {
                await transaction.RollbackAsync(ct);
                var updated = await db.NativeClientAuthCodes.AsNoTracking()
                    .SingleOrDefaultAsync(c => c.CodeHash == codeHash && c.TenantId == authCode.TenantId, ct);
                if (updated?.ConsumedAt is not null)
                    return await RejectCodeReplayAsync(updated, now, ct);
                return FailedToken(StatusCodes.Status400BadRequest, "invalid_grant", "Invalid authorization grant.");
            }

            user = await db.Users
                .FirstOrDefaultAsync(u => u.Id == authCode.UserId && u.TenantId == authCode.TenantId, ct);
            if (user is null || !user.IsActive || user.MustChangePassword)
            {
                await transaction.RollbackAsync(ct);
                return FailedToken(StatusCodes.Status401Unauthorized, "account_unavailable", "User account is disabled or requires a password change.");
            }

            db.NativeClientSessions.Add(new NativeClientSession
            {
                Id = sessionId,
                TenantId = authCode.TenantId,
                UserId = user.Id,
                ClientId = authCode.ClientId,
                Scopes = authCode.Scopes,
                CreatedAt = now,
                ExpiresAt = sessionExpires
            });

            db.NativeClientRefreshTokens.Add(new NativeClientRefreshToken
            {
                SessionId = sessionId,
                TenantId = authCode.TenantId,
                UserId = user.Id,
                TokenHash = TokenService.HashRefreshToken(rawRefresh),
                CreatedAt = now,
                ExpiresAt = sessionExpires
            });

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        var response = await MintTokenResponseAsync(
            user!,
            sessionId,
            authCode.ClientId,
            authCode.TenantId,
            NativeClientScopes.ParseStored(authCode.Scopes),
            rawRefresh,
            tenantContext.Context,
            ct);

        await auditService.LogAsync(
            user!.Id,
            "CLIENT_LOGIN",
            "NativeClientSession",
            sessionId,
            $"client={authCode.ClientId}; scopes={authCode.Scopes}");

        return new TokenOutcome(true, StatusCodes.Status200OK, null, null, response);
    }

    public async Task<TokenOutcome> RefreshAsync(
        HttpRequest request,
        NativeClientRefreshRequest req,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.RefreshToken))
            return FailedToken(StatusCodes.Status401Unauthorized, "invalid_grant", "Invalid or expired refresh token.");

        var tokenHash = TokenService.HashRefreshToken(req.RefreshToken);
        var token = await db.NativeClientRefreshTokens
            .Include(t => t.Session)
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);
        if (token is null || token.Session is null || token.User is null)
            return FailedToken(StatusCodes.Status401Unauthorized, "invalid_grant", "Invalid or expired refresh token.");

        if (!string.Equals(token.TenantId, token.User.TenantId, StringComparison.Ordinal)
            || !string.Equals(token.TenantId, token.Session.TenantId, StringComparison.Ordinal))
            return FailedToken(StatusCodes.Status401Unauthorized, "invalid_grant", "Invalid or expired refresh token.");

        var tenantContext = await ResolveAndVerifyTenantForRequestAsync(request, token.TenantId, ct);
        if (tenantContext.Failed)
            return FailedToken(StatusCodes.Status401Unauthorized, "invalid_grant", "Invalid or expired refresh token.");

        if (!string.IsNullOrWhiteSpace(req.ClientId)
            && !string.Equals(req.ClientId.Trim(), token.Session.ClientId, StringComparison.Ordinal))
            return FailedToken(StatusCodes.Status401Unauthorized, "invalid_grant", "Invalid or expired refresh token.");

        var now = clock.GetUtcNow().UtcDateTime;

        if (token.RevokedAt is not null)
            return await RejectRefreshReuseAsync(token, now, ct);

        if (token.ExpiresAt <= now || token.Session.RevokedAt is not null || token.Session.ExpiresAt <= now)
            return FailedToken(StatusCodes.Status401Unauthorized, "invalid_grant", "Invalid or expired refresh token.");

        if (!token.User.IsActive || token.User.MustChangePassword)
        {
            await RevokeSessionInternalAsync(token.TenantId, token.SessionId, "account_unavailable", now, ct);
            return FailedToken(StatusCodes.Status401Unauthorized, "account_unavailable", "User account is disabled or requires a password change.");
        }

        var newRawRefresh = tokenService.GenerateRefreshToken();
        var nextRefreshExpiry = token.Session.ExpiresAt;

        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            var consumed = await db.NativeClientRefreshTokens
                .Where(t => t.Id == token.Id
                    && t.SessionId == token.SessionId
                    && t.TenantId == token.TenantId
                    && t.TokenHash == tokenHash
                    && t.RevokedAt == null
                    && t.ExpiresAt > now)
                .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.RevokedAt, now), ct);
            if (consumed != 1)
            {
                await transaction.RollbackAsync(ct);
                var reused = await db.NativeClientRefreshTokens.AsNoTracking()
                    .AnyAsync(t => t.Id == token.Id && t.TenantId == token.TenantId && t.RevokedAt != null, ct);
                if (reused)
                    return await RejectRefreshReuseAsync(token, now, ct);
                return FailedToken(StatusCodes.Status401Unauthorized, "invalid_grant", "Invalid or expired refresh token.");
            }

            var sessionActive = await db.NativeClientSessions
                .Where(s => s.Id == token.SessionId
                    && s.TenantId == token.TenantId
                    && s.RevokedAt == null
                    && s.ExpiresAt > now)
                .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.LastRefreshedAt, now), ct);
            if (sessionActive != 1)
            {
                await transaction.RollbackAsync(ct);
                return FailedToken(StatusCodes.Status401Unauthorized, "invalid_grant", "Invalid or expired refresh token.");
            }

            db.NativeClientRefreshTokens.Add(new NativeClientRefreshToken
            {
                SessionId = token.SessionId,
                TenantId = token.TenantId,
                UserId = token.UserId,
                TokenHash = TokenService.HashRefreshToken(newRawRefresh),
                CreatedAt = now,
                ExpiresAt = nextRefreshExpiry
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        var response = await MintTokenResponseAsync(
            token.User,
            token.SessionId,
            token.Session.ClientId,
            token.TenantId,
            NativeClientScopes.ParseStored(token.Session.Scopes),
            newRawRefresh,
            tenantContext.Context,
            ct);

        return new TokenOutcome(true, StatusCodes.Status200OK, null, null, response);
    }

    public async Task<int> LogoutAsync(
        HttpRequest request,
        ClaimsPrincipal user,
        NativeClientLogoutRequest? req,
        CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;

        if (user.Identity?.IsAuthenticated == true)
        {
            var isNative = string.Equals(
                user.FindFirstValue(TokenService.IdentityTypeClaim),
                TokenService.NativeClientIdentityType,
                StringComparison.Ordinal);
            if (!isNative)
                return StatusCodes.Status403Forbidden;

            var sessionId = user.FindFirstValue(TokenService.NativeSessionIdClaim);
            var userIdRaw = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(sessionId) || !int.TryParse(userIdRaw, out var userId))
                return StatusCodes.Status401Unauthorized;

            var tenantId = config.SharedTenancy.Enabled
                ? tenantAccessor.RequireCurrent().Tenant.Value
                : ResolveDedicatedTenantId();

            var revoked = await RevokeSessionInternalAsync(tenantId, sessionId, "logout", now, ct);
            if (revoked)
            {
                await auditService.LogAsync(
                    userId,
                    "LOGOUT",
                    "NativeClientSession",
                    sessionId,
                    $"native_session={sessionId}");
            }

            return StatusCodes.Status204NoContent;
        }

        // Unauthenticated fallback (expired or already-revoked access token during retry):
        // allow single-session logout via the session's refresh token, idempotently.
        if (string.IsNullOrWhiteSpace(req?.RefreshToken))
            return StatusCodes.Status401Unauthorized;

        var tokenHash = TokenService.HashRefreshToken(req.RefreshToken);
        var refresh = await db.NativeClientRefreshTokens
            .Include(t => t.Session)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);
        if (refresh is null || refresh.Session is null)
            return StatusCodes.Status204NoContent;

        var tenantCheck = await ResolveAndVerifyTenantForRequestAsync(request, refresh.TenantId, ct);
        if (tenantCheck.Failed)
            return StatusCodes.Status401Unauthorized;

        var revokedByRefresh = await RevokeSessionInternalAsync(refresh.TenantId, refresh.SessionId, "logout", now, ct);
        if (revokedByRefresh)
        {
            await auditService.LogAsync(
                refresh.UserId,
                "LOGOUT",
                "NativeClientSession",
                refresh.SessionId,
                $"native_session={refresh.SessionId}");
        }

        return StatusCodes.Status204NoContent;
    }

    private async Task<TokenOutcome> RejectCodeReplayAsync(NativeClientAuthCode code, DateTime now, CancellationToken ct)
    {
        if (code.IssuedSessionId is { Length: > 0 } sessionId
            && await RevokeSessionInternalAsync(code.TenantId, sessionId, "code_replay", now, ct))
        {
            await auditService.LogAsync(code.UserId, "NATIVE_CLIENT_CODE_REPLAY", "NativeClientSession", sessionId,
                $"Replayed authorization code for client={code.ClientId}; issued session revoked.");
        }
        return FailedToken(StatusCodes.Status400BadRequest, "invalid_grant", "Authorization code has already been used.");
    }

    private async Task<TokenOutcome> RejectRefreshReuseAsync(NativeClientRefreshToken token, DateTime now, CancellationToken ct)
    {
        if (token.ExpiresAt > now && token.Session.RevokedAt is null
            && await RevokeSessionInternalAsync(token.TenantId, token.SessionId, "refresh_token_reuse", now, ct))
        {
            await auditService.LogAsync(token.UserId, "NATIVE_REFRESH_TOKEN_REUSE", "NativeClientSession", token.SessionId,
                $"A revoked native refresh token was presented for client={token.Session.ClientId}; native session family revoked.");
        }
        return FailedToken(StatusCodes.Status401Unauthorized, "invalid_grant", "Invalid or expired refresh token.");
    }

    private async Task<bool> RevokeSessionInternalAsync(
        string tenantId,
        string sessionId,
        string reason,
        DateTime now,
        CancellationToken ct)
    {
        var updatedSessions = await db.NativeClientSessions
            .Where(s => s.Id == sessionId && s.TenantId == tenantId && s.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.RevokedAt, now)
                .SetProperty(s => s.RevokedReason, reason), ct);

        await db.NativeClientRefreshTokens
            .Where(t => t.SessionId == sessionId && t.TenantId == tenantId && t.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.RevokedAt, now), ct);

        return updatedSessions > 0;
    }

    private async Task<NativeClientTokenResponse> MintTokenResponseAsync(
        PortalUser user,
        string sessionId,
        string clientId,
        string tenantId,
        IReadOnlyList<string> scopes,
        string rawRefresh,
        TenantContext? tenantContext,
        CancellationToken ct)
    {
        var roles = await userManager.GetRolesAsync(user);
        var capabilities = await studioCapabilities.ResolveForUserAsync(user.Id, ct);
        var expiresAt = clock.GetUtcNow().UtcDateTime.AddMinutes(config.Jwt.ExpiryMinutes);
        var jwt = tokenService.GenerateNativeClientJwt(
            user,
            roles,
            sessionId,
            clientId,
            scopes,
            capabilities,
            tenantContext,
            expiresAt);

        return new NativeClientTokenResponse(
            jwt,
            rawRefresh,
            expiresAt,
            sessionId,
            clientId,
            new NativeClientUserDto(user.Id, user.UserName ?? string.Empty, user.Email, roles.ToArray()),
            tenantId,
            TokenService.NativeTokenAudience,
            scopes);
    }

    private async Task<(bool Failed, TenantContext? Context)> ResolveAndVerifyTenantForRequestAsync(
        HttpRequest request,
        string recordTenantId,
        CancellationToken ct)
    {
        if (config.SharedTenancy.Enabled)
        {
            var authority = await sharedAuthorities.ResolveForRequestAsync(request, ct);
            if (authority is null || !string.Equals(authority.TenantId, recordTenantId, StringComparison.Ordinal))
                return (true, null);

            try
            {
                var ctx = TenantContext.FromVerifiedCredential(recordTenantId);
                tenantAccessor.SetVerifiedCredential(ctx);
                return (false, ctx);
            }
            catch (ArgumentException)
            {
                return (true, null);
            }
        }

        if (!string.IsNullOrWhiteSpace(config.TenantId))
        {
            if (!string.Equals(config.TenantId, recordTenantId, StringComparison.Ordinal))
                return (true, null);
            return (false, TenantContext.FromHostConfiguration(config.TenantId));
        }

        return (false, null);
    }

    private string ResolveDedicatedTenantId() =>
        string.IsNullOrWhiteSpace(config.TenantId)
            ? "portal-host"
            : TenantId.FromTrustedSource(config.TenantId).Value;

    private static bool ValidateSameOriginHeaders(HttpRequest request, string canonicalOrigin)
    {
        if (request.Headers.TryGetValue("Origin", out var originValues))
        {
            var origin = originValues.ToString().Trim();
            if (!string.IsNullOrEmpty(origin)
                && !string.Equals(origin, canonicalOrigin, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (request.Headers.TryGetValue("Referer", out var refererValues))
        {
            var referer = refererValues.ToString().Trim();
            if (!string.IsNullOrEmpty(referer))
            {
                if (!Uri.TryCreate(referer, UriKind.Absolute, out var refUri))
                    return false;
                var refOrigin = refUri.GetLeftPart(UriPartial.Authority);
                if (!string.Equals(refOrigin, canonicalOrigin, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
        }

        return true;
    }

    private static AuthorizeStartResult FailedStart(int statusCode, string error, string? detail) =>
        new(false, statusCode, error, detail, null, null, false);

    private static TokenOutcome FailedToken(int statusCode, string error, string? detail) =>
        new(false, statusCode, error, detail, null);

    private static bool CryptoEquals(string left, string right)
    {
        var a = Encoding.UTF8.GetBytes(left);
        var b = Encoding.UTF8.GetBytes(right);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
