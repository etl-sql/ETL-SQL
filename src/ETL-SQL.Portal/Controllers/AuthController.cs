using System.Security.Claims;
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
    SignInManager<PortalUser> signInManager,
    TokenService tokenService,
    AuditService auditService,
    SecuritySessionService securitySessions,
    PortalDbContext db,
    PortalConfig config,
    ILdapService ldapService,
    StudioCapabilityStore studioCapabilities) : ControllerBase
{
    /// <summary>Advertises the effective identity configuration so the login page can offer the right
    /// affordances (e.g. an SSO button) without hardcoding deployment posture. Anonymous and
    /// secret-free.</summary>
    [HttpGet("providers")]
    [AllowAnonymous]
    public IActionResult Providers() => Ok(new
    {
        local = true,
        oidcEnabled = config.Identity.Oidc.Enabled,
        oidcLoginUrl = config.Identity.Oidc.Enabled ? "/api/auth/oidc/login" : null
    });

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        bool isDomainQualified = req.Username.Contains("@") || req.Username.Contains("\\");
        string cleanUsername = req.Username;
        if (req.Username.Contains("@"))
        {
            cleanUsername = req.Username.Split('@')[0];
        }
        else if (req.Username.Contains("\\"))
        {
            var parts = req.Username.Split('\\');
            cleanUsername = parts.Length > 1 ? parts[1] : parts[0];
        }

        var user = await userManager.FindByNameAsync(cleanUsername);

        // A portal-disabled account stays disabled regardless of identity provider. LDAP
        // authentication must not resurrect it — only an administrator re-enables the account.
        if (user is not null && !user.IsActive)
        {
            await auditService.LogAsync(user.Id, "LOGIN_FAILED", "User", user.Id.ToString(), "Account is disabled.");
            return Unauthorized(new { error = "Invalid credentials" });
        }

        bool useLdap = false;
        if (config.Identity.Ldap.Enabled)
        {
            if (user != null && user.Provider == "LDAP")
            {
                useLdap = true;
            }
            else if (user == null && (isDomainQualified || !string.IsNullOrEmpty(config.Identity.Ldap.Domain)))
            {
                useLdap = true;
            }
        }

        LdapUserResult? ldapResult = null;
        if (useLdap)
        {
            ldapResult = await ldapService.AuthenticateAsync(req.Username, req.Password);
            if (ldapResult == null)
            {
                if (user != null)
                {
                    await auditService.LogAsync(user.Id, "LOGIN_FAILED", "User", user.Id.ToString(), "LDAP authentication failed.");
                }
                return Unauthorized(new { error = "Invalid LDAP credentials" });
            }
        }

        if (useLdap && ldapResult != null)
        {
            using var transaction = await db.Database.BeginTransactionAsync();
            try
            {
                if (user == null)
                {
                    // Auto-provision user
                    user = new PortalUser
                    {
                        UserName = ldapResult.Username,
                        Email = ldapResult.Email ?? $"{ldapResult.Username}@{config.Identity.Ldap.Domain}",
                        FirstName = ldapResult.FirstName,
                        LastName = ldapResult.LastName,
                        IsActive = true,
                        MustChangePassword = false,
                        Provider = "LDAP"
                    };

                    var createResult = await userManager.CreateAsync(user);
                    if (!createResult.Succeeded)
                    {
                        await transaction.RollbackAsync();
                        return BadRequest(new { errors = createResult.Errors.Select(e => e.Description) });
                    }
                }
                else
                {
                    // Sync user details. IsActive is deliberately not touched: a portal disable
                    // must survive LDAP re-authentication (checked and rejected above).
                    user.Email = ldapResult.Email ?? user.Email;
                    user.FirstName = ldapResult.FirstName ?? user.FirstName;
                    user.LastName = ldapResult.LastName ?? user.LastName;
                    await userManager.UpdateAsync(user);
                }

                // Sync Group memberships & Roles
                var mappedRoles = new List<string>();
                foreach (var groupDn in ldapResult.Groups)
                {
                    var cn = ParseCn(groupDn);
                    if (config.Identity.Ldap.RoleMappings.TryGetValue(groupDn, out var roleDn))
                    {
                        mappedRoles.Add(roleDn);
                    }
                    else if (config.Identity.Ldap.RoleMappings.TryGetValue(cn, out var roleCn))
                    {
                        mappedRoles.Add(roleCn);
                    }
                }

                var currentRoles = await userManager.GetRolesAsync(user);
                var rolesToAdd = mappedRoles.Except(currentRoles, StringComparer.OrdinalIgnoreCase).ToList();
                var rolesToRemove = currentRoles.Except(mappedRoles, StringComparer.OrdinalIgnoreCase).ToList();

                if (rolesToRemove.Any())
                {
                    await userManager.RemoveFromRolesAsync(user, rolesToRemove);
                }
                if (rolesToAdd.Any())
                {
                    foreach (var role in rolesToAdd)
                    {
                        await userManager.AddToRoleAsync(user, role);
                    }
                }

                // Sync portal groups (where Provider == "LDAP")
                var ldapGroups = await db.Groups.Where(g => g.Provider == "LDAP").ToListAsync();
                var userAdGroups = ldapResult.Groups;
                var userAdCns = userAdGroups.Select(ParseCn).ToList();

                var matchingGroupIds = new List<int>();
                foreach (var g in ldapGroups)
                {
                    var targetAdName = !string.IsNullOrEmpty(g.AdGroup) ? g.AdGroup : g.Name;
                    bool isMember = userAdGroups.Any(dn => dn.Equals(targetAdName, StringComparison.OrdinalIgnoreCase)) ||
                                    userAdCns.Any(cn => cn.Equals(targetAdName, StringComparison.OrdinalIgnoreCase));
                    if (isMember)
                    {
                        matchingGroupIds.Add(g.Id);
                    }
                }

                var currentUserLdapMemberships = await db.UserGroups
                    .Where(ug => ug.UserId == user.Id && ug.Group.Provider == "LDAP")
                    .ToListAsync();

                var currentUserLdapGroupIds = currentUserLdapMemberships.Select(ug => ug.GroupId).ToList();
                var membershipsToAdd = matchingGroupIds.Except(currentUserLdapGroupIds).ToList();
                var membershipsToRemove = currentUserLdapMemberships.Where(ug => !matchingGroupIds.Contains(ug.GroupId)).ToList();
                var securityContextChanged = rolesToAdd.Count > 0
                    || rolesToRemove.Count > 0
                    || membershipsToAdd.Count > 0
                    || membershipsToRemove.Count > 0;

                if (membershipsToRemove.Any())
                {
                    db.UserGroups.RemoveRange(membershipsToRemove);
                }
                foreach (var groupId in membershipsToAdd)
                {
                    db.UserGroups.Add(new UserGroup { UserId = user.Id, GroupId = groupId });
                }

                await db.SaveChangesAsync();
                if (securityContextChanged)
                {
                    await securitySessions.InvalidateUserAsync(user.Id);
                    if (rolesToRemove.Count > 0)
                        await securitySessions.RevokeAnonymousCapabilitiesAsync([user.Id]);
                }
                await transaction.CommitAsync();
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
        else
        {
            // Local authentication
            if (user is null || !user.IsActive)
                return Unauthorized(new { error = "Invalid credentials" });

            if (user.Provider == "LDAP")
            {
                return Unauthorized(new { error = "Invalid LDAP credentials" });
            }

            // Federated (OIDC) accounts have no local password; they must use the SSO flow.
            if (user.Provider == "OIDC")
            {
                return Unauthorized(new { error = "Use single sign-on for this account" });
            }

            var result = await signInManager.CheckPasswordSignInAsync(user, req.Password, lockoutOnFailure: true);
            if (!result.Succeeded)
            {
                await auditService.LogAsync(user.Id, "LOGIN_FAILED", "User", user.Id.ToString());
                if (result.IsLockedOut)
                    return StatusCode(429, new { error = "Account locked. Try again in 15 minutes." });
                return Unauthorized(new { error = "Invalid credentials" });
            }
        }

        var roles = await userManager.GetRolesAsync(user);
        var jwt = tokenService.GenerateJwt(user, roles,
            await studioCapabilities.ResolveForUserAsync(user.Id));
        var rawRefresh = tokenService.GenerateRefreshToken();
        var expiresAt = DateTime.UtcNow.AddMinutes(config.Jwt.ExpiryMinutes);

        db.RefreshTokens.Add(new RefreshToken
        {
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
                .Where(t => t.Id == token.Id && t.Token == tokenHash && t.RevokedAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.RevokedAt, disabledRevokedAt));
            await transaction.CommitAsync();
            return Unauthorized(new { error = "User account is disabled" });
        }

        var revokedAt = DateTime.UtcNow;
        var consumed = await db.RefreshTokens
            .Where(t => t.Id == token.Id &&
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
            await studioCapabilities.ResolveForUserAsync(user.Id));
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

    private static string ParseCn(string dn)
    {
        if (string.IsNullOrEmpty(dn)) return "";
        var parts = dn.Split(',');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[3..];
            }
        }
        return dn;
    }
}
