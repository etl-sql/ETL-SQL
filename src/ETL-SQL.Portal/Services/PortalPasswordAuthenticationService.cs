using ETL_SQL.Portal.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

public sealed class PortalPasswordAuthenticationService(
    UserManager<PortalUser> userManager,
    SignInManager<PortalUser> signInManager,
    ILdapService ldapService,
    AuditService auditService,
    SecuritySessionService securitySessions,
    PortalDbContext db,
    PortalConfig config)
{
    public sealed record Result(PortalUser? User, int StatusCode, string? Error = null, string[]? ValidationErrors = null);

    public async Task<Result> AuthenticateAsync(string username, string password, CancellationToken ct)
    {
        // A Shared host has no tenant for a password: usernames repeat across tenants and LDAP
        // groups are tenant-owned, so a lookup here could only be global. Shared sign-in starts at
        // the routed host's identity authority (OIDC) and refresh preserves its partition. Refuse
        // before any lookup or LDAP provisioning rather than minting nothing after writing rows.
        if (config.SharedTenancy.Enabled)
            return Failed("Use single sign-on for this host");

        bool isDomainQualified = username.Contains("@") || username.Contains("\\");
        // COMPAT_BREAK: 0.22.0 — a name qualified with a domain other than Identity:Ldap:Domain is
        // refused. It used to be stripped to the bare name and sign in as this domain's account.
        var accountName = LdapService.AccountName(username,
            config.Identity.Ldap.Enabled ? config.Identity.Ldap.Domain : null);
        if (accountName is null)
            return Failed("Invalid credentials");
        string cleanUsername = accountName;

        var user = await userManager.FindByNameAsync(cleanUsername);

        // A portal-disabled account stays disabled regardless of identity provider. LDAP
        // authentication must not resurrect it — only an administrator re-enables the account.
        if (user is not null && !user.IsActive)
        {
            await auditService.LogAsync(user.Id, "LOGIN_FAILED", "User", user.Id.ToString(), "Account is disabled.");
            return Failed("Invalid credentials");
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
            ldapResult = await ldapService.AuthenticateAsync(username, password);
            if (ldapResult == null)
            {
                if (user != null)
                {
                    await auditService.LogAsync(user.Id, "LOGIN_FAILED", "User", user.Id.ToString(), "LDAP authentication failed.");
                }
                return Failed("Invalid LDAP credentials");
            }
        }

        if (useLdap && ldapResult != null)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
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
                        await transaction.RollbackAsync(ct);
                        return new(null, StatusCodes.Status400BadRequest, ValidationErrors: createResult.Errors.Select(e => e.Description).ToArray());
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
                var ldapGroups = await db.Groups.Where(g => g.Provider == "LDAP").ToListAsync(ct);
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
                    .ToListAsync(ct);

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

                await db.SaveChangesAsync(ct);
                if (securityContextChanged)
                {
                    await securitySessions.InvalidateUserAsync(user.Id, ct);
                    if (rolesToRemove.Count > 0)
                        await securitySessions.RevokeAnonymousCapabilitiesAsync([user.Id], ct);
                }
                await transaction.CommitAsync(ct);
            }
            catch (Exception)
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        }
        else
        {
            // Local authentication
            if (user is null || !user.IsActive)
                return Failed("Invalid credentials");

            if (user.Provider == "LDAP")
            {
                return Failed("Invalid LDAP credentials");
            }

            // Federated (OIDC) accounts have no local password; they must use the SSO flow.
            if (user.Provider == "OIDC")
            {
                return Failed("Use single sign-on for this account");
            }

            var result = await signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
            if (!result.Succeeded)
            {
                await auditService.LogAsync(user.Id, "LOGIN_FAILED", "User", user.Id.ToString());
                if (result.IsLockedOut)
                    return Failed("Account locked. Try again in 15 minutes.", StatusCodes.Status429TooManyRequests);
                return Failed("Invalid credentials");
            }
        }

        return new(user, StatusCodes.Status200OK);
    }

    private static Result Failed(string error, int statusCode = StatusCodes.Status401Unauthorized) =>
        new(null, statusCode, error);

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
