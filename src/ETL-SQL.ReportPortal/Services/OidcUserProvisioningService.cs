using ETL_SQL.ReportPortal.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.ReportPortal.Services;

/// <summary>The portal session minted for a federated user: the portal's own JWT plus a refresh
/// token, identical in shape to a password/LDAP login so the rest of the system is unchanged.</summary>
public sealed record OidcSession(string AccessToken, string RefreshToken, DateTime ExpiresAt);

/// <summary>
/// Bridges a validated <see cref="OidcIdentity"/> into a portal session (P1.2): provisions or
/// updates the <see cref="PortalUser"/> (Provider="OIDC"), syncs portal group memberships from the
/// token's group claims, and issues the portal's internal JWT + refresh token. Mirrors the LDAP
/// bridge in <c>AuthController</c> so federated and directory logins behave consistently. A
/// portal-disabled account stays disabled regardless of the identity provider.
/// </summary>
public sealed class OidcUserProvisioningService(
    UserManager<PortalUser> userManager,
    PortalDbContext db,
    TokenService tokenService,
    PortalConfig config,
    AuditService auditService,
    SecuritySessionService securitySessions)
{
    public const string ProviderName = "OIDC";

    /// <summary>Outcome of provisioning. <see cref="Session"/> is null when the account is disabled.</summary>
    public sealed record Result(OidcSession? Session, bool Disabled, int? UserId);

    public async Task<Result> SignInAsync(OidcIdentity identity, CancellationToken ct = default)
    {
        var user = await userManager.FindByNameAsync(identity.Username);

        // A portal-disabled account stays disabled; the IdP must not resurrect it.
        if (user is not null && !user.IsActive)
        {
            await auditService.LogAsync(user.Id, "LOGIN_FAILED", "User", user.Id.ToString(),
                "Account is disabled.");
            return new Result(null, Disabled: true, user.Id);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            if (user is null)
            {
                user = new PortalUser
                {
                    UserName = identity.Username,
                    Email = identity.Email,
                    IsActive = true,
                    MustChangePassword = false,
                    Provider = ProviderName
                };
                var created = await userManager.CreateAsync(user);
                if (!created.Succeeded)
                    throw new InvalidOperationException(
                        "Failed to provision OIDC user: " + string.Join("; ", created.Errors.Select(e => e.Description)));
            }
            else if (!string.IsNullOrEmpty(identity.Email) && !string.Equals(user.Email, identity.Email, StringComparison.OrdinalIgnoreCase))
            {
                user.Email = identity.Email;
                await userManager.UpdateAsync(user);
            }

            var membershipChanged = await SyncGroupsAsync(user, identity.Groups, ct);
            await db.SaveChangesAsync(ct);
            if (membershipChanged)
                await securitySessions.InvalidateUserAsync(user.Id);

            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }

        var session = await IssueSessionAsync(user, ct);
        await auditService.LogAsync(user.Id, "LOGIN", "User", user.Id.ToString(), "OIDC");
        return new Result(session, Disabled: false, user.Id);
    }

    /// <summary>Deterministically reconciles the user's OIDC-provider group memberships against the
    /// token's group claims: adds matched groups, removes those no longer claimed. Returns whether
    /// anything changed (so callers can invalidate cached security state). Matching by group
    /// <c>AdGroup</c> (when set) else <c>Name</c>, case-insensitive.</summary>
    private async Task<bool> SyncGroupsAsync(PortalUser user, IReadOnlyList<string> claimedGroups, CancellationToken ct)
    {
        var oidcGroups = await db.Groups.Where(g => g.Provider == ProviderName).ToListAsync(ct);
        var matchingGroupIds = oidcGroups
            .Where(g => claimedGroups.Contains(
                string.IsNullOrEmpty(g.AdGroup) ? g.Name : g.AdGroup!, StringComparer.OrdinalIgnoreCase))
            .Select(g => g.Id)
            .ToHashSet();

        var current = await db.UserGroups
            .Where(ug => ug.UserId == user.Id && ug.Group.Provider == ProviderName)
            .ToListAsync(ct);
        var currentIds = current.Select(ug => ug.GroupId).ToHashSet();

        var toRemove = current.Where(ug => !matchingGroupIds.Contains(ug.GroupId)).ToList();
        var toAdd = matchingGroupIds.Where(id => !currentIds.Contains(id)).ToList();

        if (toRemove.Count > 0) db.UserGroups.RemoveRange(toRemove);
        foreach (var groupId in toAdd) db.UserGroups.Add(new UserGroup { UserId = user.Id, GroupId = groupId });

        return toRemove.Count > 0 || toAdd.Count > 0;
    }

    private async Task<OidcSession> IssueSessionAsync(PortalUser user, CancellationToken ct)
    {
        var roles = await userManager.GetRolesAsync(user);
        var jwt = tokenService.GenerateJwt(user, roles);
        var rawRefresh = tokenService.GenerateRefreshToken();
        var expiresAt = DateTime.UtcNow.AddMinutes(config.Jwt.ExpiryMinutes);

        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            Token = TokenService.HashRefreshToken(rawRefresh),
            ExpiresAt = DateTime.UtcNow.AddDays(config.Jwt.RefreshExpiryDays)
        });
        await db.SaveChangesAsync(ct);

        return new OidcSession(jwt, rawRefresh, expiresAt);
    }
}
