using System.Security.Claims;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Models;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public class AuthController(
    UserManager<PortalUser> userManager,
    PortalPasswordAuthenticationService passwordAuthentication,
    TokenService tokenService,
    AuditService auditService,
    SecuritySessionService securitySessions,
    PortalDbContext db,
    PortalConfig config,
    StudioCapabilityStore studioCapabilities,
    SharedIdentityAuthorityResolver sharedIdentityAuthorities,
    RequestTenantContextAccessor tenantAccessor) : ControllerBase
{
    /// <summary>Advertises the effective identity configuration so the login page can offer the right
    /// affordances (e.g. an SSO button) without hardcoding deployment posture. Anonymous and
    /// secret-free.</summary>
    [HttpGet("providers")]
    [AllowAnonymous]
    public async Task<IActionResult> Providers(CancellationToken ct)
    {
        var oidcEnabled = config.SharedTenancy.Enabled
            ? await sharedIdentityAuthorities.ResolveForRequestAsync(Request, ct) is not null
            : config.Identity.Oidc.Enabled;
        return Ok(new AuthProvidersResponse(true, oidcEnabled, oidcEnabled ? "/api/auth/oidc/login" : null));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        var result = await passwordAuthentication.AuthenticateAsync(req.Username, req.Password, ct);
        if (result.ValidationErrors is { } errors)
            return BadRequest(new { errors });
        if (result.User is not { } user)
            return StatusCode(result.StatusCode, new { error = result.Error });

        var sessionTenant = string.IsNullOrWhiteSpace(config.TenantId)
            ? null
            : TenantContext.FromHostConfiguration(config.TenantId);
        var roles = await userManager.GetRolesAsync(user);
        var jwt = tokenService.GenerateJwt(user, roles,
            await studioCapabilities.ResolveForUserAsync(user.Id), sessionTenant);
        var rawRefresh = tokenService.GenerateRefreshToken();
        var expiresAt = DateTime.UtcNow.AddMinutes(config.Jwt.ExpiryMinutes);

        db.RefreshTokens.Add(new RefreshToken
        {
            TenantId = sessionTenant?.Tenant.Value ?? "portal-host",
            UserId = user.Id,
            Token = TokenService.HashRefreshToken(rawRefresh),
            ExpiresAt = DateTime.UtcNow.AddDays(config.Jwt.RefreshExpiryDays)
        });
        await db.SaveChangesAsync();
        await auditService.LogAsync(user.Id, "LOGIN", "User", user.Id.ToString());

        return Ok(new LoginResponse(jwt, rawRefresh, expiresAt));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest req)
    {
        var tokenHash = TokenService.HashRefreshToken(req.RefreshToken);
        var token = await db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.Token == tokenHash);

        if (token is null)
            return Unauthorized(new { error = "Invalid or expired refresh token" });

        TenantContext? sessionTenant = null;
        if (config.SharedTenancy.Enabled)
        {
            if (token.User is null
                || !string.Equals(token.TenantId, token.User.TenantId, StringComparison.Ordinal))
                return Unauthorized(new { error = "Invalid or expired refresh token" });
            try
            {
                sessionTenant = TenantContext.FromVerifiedCredential(token.TenantId);
                tenantAccessor.SetVerifiedCredential(sessionTenant);
            }
            catch (ArgumentException)
            {
                return Unauthorized(new { error = "Invalid or expired refresh token" });
            }
        }
        else if (!string.IsNullOrWhiteSpace(config.TenantId))
        {
            sessionTenant = TenantContext.FromHostConfiguration(config.TenantId);
        }

        if (token.RevokedAt is not null)
        {
            // Presenting an already-rotated/revoked token is a theft signal: the legitimate
            // client holds the rotated successor, so someone else replayed this one. Standard
            // response is to revoke the user's whole session/token family. Only meaningful
            // while the replayed token would otherwise still be live.
            if (token.ExpiresAt > DateTime.UtcNow)
            {
                await securitySessions.InvalidateUserAsync(token.UserId);
                await auditService.LogAsync(token.UserId, "REFRESH_TOKEN_REUSE", "User",
                    token.UserId.ToString(),
                    "A revoked refresh token was presented; all sessions were invalidated.");
            }
            return Unauthorized(new { error = "Invalid or expired refresh token" });
        }

        if (token.ExpiresAt <= DateTime.UtcNow)
            return Unauthorized(new { error = "Invalid or expired refresh token" });

        await using var transaction = await db.Database.BeginTransactionAsync();

        if (token.User is null || !token.User.IsActive)
        {
            var disabledRevokedAt = DateTime.UtcNow;
            await db.RefreshTokens
                .Where(t => t.Id == token.Id
                    && t.TenantId == token.TenantId
                    && t.Token == tokenHash
                    && t.RevokedAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.RevokedAt, disabledRevokedAt));
            await transaction.CommitAsync();
            return Unauthorized(new { error = "User account is disabled" });
        }

        var revokedAt = DateTime.UtcNow;
        var consumed = await db.RefreshTokens
            .Where(t => t.Id == token.Id &&
                        t.TenantId == token.TenantId &&
                        t.Token == tokenHash &&
                        t.RevokedAt == null &&
                        t.ExpiresAt > revokedAt)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.RevokedAt, revokedAt));
        if (consumed != 1)
        {
            await transaction.RollbackAsync();
            return Unauthorized(new { error = "Invalid or expired refresh token" });
        }

        var newRaw = tokenService.GenerateRefreshToken();
        db.RefreshTokens.Add(new RefreshToken
        {
            TenantId = token.TenantId,
            UserId = token.UserId,
            Token = TokenService.HashRefreshToken(newRaw),
            ExpiresAt = DateTime.UtcNow.AddDays(config.Jwt.RefreshExpiryDays)
        });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        var user = token.User;
        var roles = await userManager.GetRolesAsync(user);
        // Refresh re-resolves group capabilities, so a grant changed since sign-in takes effect on
        // the next refresh rather than lingering for the life of the session.
        var jwt = tokenService.GenerateJwt(user, roles,
            await studioCapabilities.ResolveForUserAsync(user.Id), sessionTenant);
        var expiresAt = DateTime.UtcNow.AddMinutes(config.Jwt.ExpiryMinutes);

        return Ok(new LoginResponse(jwt, newRaw, expiresAt));
    }

    private int? GetCurrentUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(value, out var id) ? id : null;
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        var user = await userManager.FindByIdAsync(userId.Value.ToString());
        if (user is null) return NotFound();

        if (user.Provider == "LDAP")
            return BadRequest(new { errors = new[] { "Password changes are not supported for LDAP accounts." } });

        var result = await userManager.ChangePasswordAsync(user, req.CurrentPassword, req.NewPassword);
        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors.Select(e => e.Description) });

        user.MustChangePassword = false;
        await userManager.UpdateAsync(user);
        await securitySessions.InvalidateUserAsync(user.Id);
        await auditService.LogAsync(userId.Value, "PASSWORD_CHANGED", "User", userId.Value.ToString());

        return NoContent();
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout([FromBody] RefreshRequest req)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        await securitySessions.InvalidateUserAsync(userId.Value);
        await auditService.LogAsync(userId.Value, "LOGOUT", "User", userId.Value.ToString());
        return NoContent();
    }

    /// <summary>
    /// Exchanges the caller's Portal session for a short-lived Orchestrator identity assertion, so a
    /// client can call the Orchestrator directly rather than through a Portal twin of every
    /// orchestrator route.
    ///
    /// <para>The Orchestrator needs no new trust code for this: it already validates exactly this
    /// token on every request, which is why the exchange shape was chosen over proxying. Presenting
    /// the browser's Portal JWT to the Orchestrator was rejected for the opposite reason — it would
    /// undo the deliberate audience separation that stops either token being replayed at the other
    /// service.</para>
    ///
    /// <para>There is no request body, and there is nothing to ask for. The identity, its groups, its
    /// tenant and its scopes are read from the server's own view of the caller; a caller presents an
    /// identity and never requests one.</para>
    /// </summary>
    [HttpPost("orchestrator-assertion")]
    [Authorize]
    public async Task<IActionResult> IssueOrchestratorAssertion(
        [FromServices] OrchestratorAssertionIssuer issuer, CancellationToken ct)
    {
        var issued = await issuer.IssueForAsync(User, ct);
        if (issued is null)
        {
            // Either the deployment does not federate identity to an Orchestrator, or this principal
            // cannot be resolved to one. Both are refusals to issue rather than failures, and neither
            // says which — a caller learning that the signing secret is unset learns something about
            // the deployment it is not authenticated to know.
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new { error = "orchestrator_assertion_unavailable" });
        }

        await auditService.LogAsync(
            CurrentUserIdOrNull(),
            "ISSUE_ORCHESTRATOR_ASSERTION",
            "Orchestrator",
            null,
            issued.Scopes.Count == 0 ? "no scopes" : string.Join(' ', issued.Scopes));

        return Ok(new
        {
            assertion = issued.Assertion,
            headerName = ETL_SQL.Core.Governance.OrchestratorIdentityAssertion.HeaderName,
            audience = issued.Audience,
            expiresAt = issued.ExpiresAt,
            scopes = issued.Scopes
        });
    }

    private int? CurrentUserIdOrNull() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

}
