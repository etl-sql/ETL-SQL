using ETL_SQL.Portal.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

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

    /// <summary>Outcome of provisioning. <see cref="Session"/> is null when the login was not
    /// completed: <see cref="Disabled"/> for a portal-disabled account, <see cref="Refused"/> when a
    /// federated login is rejected because the username belongs to a non-OIDC account.</summary>
    public sealed record Result(OidcSession? Session, bool Disabled, bool Refused, int? UserId);

    public async Task<Result> SignInAsync(OidcIdentity identity, CancellationToken ct = default)
    {
        // Key federated accounts on the immutable provider subject (sub), not the mutable username:
        // a renamed or reused preferred_username can neither create a duplicate nor seize another
        // account. (Audits below run outside the transaction so they survive a refusal/rollback.)
        var user = await db.Users.FirstOrDefaultAsync(
            u => u.Provider == ProviderName && u.ExternalSubject == identity.Subject, ct);

        // A portal-disabled account stays disabled; the IdP must not resurrect it.
        if (user is not null && !user.IsActive)
        {
            await auditService.LogAsync(user.Id, "LOGIN_FAILED", "User", user.Id.ToString(),
                "Account is disabled.");
            return new Result(null, Disabled: true, Refused: false, user.Id);
        }

        // First login for this subject: the desired username must be free. If it already belongs to
        // ANY account — Local, LDAP, or a different OIDC subject — refuse rather than attach, which
        // is what blocks account takeover via provider confusion or username reuse.
        if (user is null)
        {
            var existingByName = await userManager.FindByNameAsync(identity.Username);
            if (existingByName is not null)
            {
                await auditService.LogAsync(existingByName.Id, "LOGIN_FAILED", "User", existingByName.Id.ToString(),
                    $"OIDC login refused: username belongs to a {existingByName.Provider} account.");
                return new Result(null, Disabled: false, Refused: true, existingByName.Id);
            }
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
                    Provider = ProviderName,
                    ExternalSubject = identity.Subject
                };
                var created = await userManager.CreateAsync(user);
                if (!created.Succeeded)
                    throw new InvalidOperationException(
                        "Failed to provision OIDC user: " + string.Join("; ", created.Errors.Select(e => e.Description)));
            }
            else
            {
                await UpdateProfileAsync(user, identity);
            }

            var sync = await SyncGroupsAsync(user, identity.Groups, ct);
            await db.SaveChangesAsync(ct);
            if (sync.Changed)
            {
                // Rotate the security stamp + revoke refresh tokens so no privilege survives a claim
                // change in an already-issued token.
                await securitySessions.InvalidateUserAsync(user.Id);
                // A privilege reduction (group removed) also revokes anonymous share/embed links the
                // user created, so access granted through the lost group cannot persist anonymously.
                if (sync.Removed)
                    await securitySessions.RevokeAnonymousCapabilitiesAsync([user.Id], ct);
            }

            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }

        var session = await IssueSessionAsync(user, ct);
        await auditService.LogAsync(user.Id, "LOGIN", "User", user.Id.ToString(), "OIDC");
        return new Result(session, Disabled: false, Refused: false, user.Id);
    }

    /// <summary>Refreshes a returning federated user's profile from the latest token: adopts a new
    /// preferred_username when the IdP changed it and the name is still free (kept via the immutable
    /// subject, so the account is stable either way), and updates the email.</summary>
    private async Task UpdateProfileAsync(PortalUser user, OidcIdentity identity)
    {
        if (!string.Equals(user.UserName, identity.Username, StringComparison.Ordinal)
            && await userManager.FindByNameAsync(identity.Username) is null)
        {
            await userManager.SetUserNameAsync(user, identity.Username);
        }

        if (!string.IsNullOrEmpty(identity.Email)
            && !string.Equals(user.Email, identity.Email, StringComparison.OrdinalIgnoreCase))
        {
            user.Email = identity.Email;
            await userManager.UpdateAsync(user);
        }
    }

    private readonly record struct GroupSyncResult(bool Added, bool Removed)
    {
        public bool Changed => Added || Removed;
    }

    /// <summary>Deterministically reconciles the user's OIDC-provider group memberships against the
    /// token's group claims: adds matched groups, removes those no longer claimed. Idempotent — an
    /// unchanged claim set yields no writes. Only OIDC-provider groups are touched, so local and LDAP
    /// memberships are preserved. Matching by group <c>AdGroup</c> (when set) else <c>Name</c>,
    /// case-insensitive.</summary>
    private async Task<GroupSyncResult> SyncGroupsAsync(PortalUser user, IReadOnlyList<string> claimedGroups, CancellationToken ct)
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

        return new GroupSyncResult(Added: toAdd.Count > 0, Removed: toRemove.Count > 0);
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
