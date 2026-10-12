using ETL_SQL.Core.Governance;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Builds the row-level-security identity of a Portal user from the database: name, roles, groups
/// and whether administrators bypass row filters. Returns null when the user cannot be resolved,
/// so an identity-sensitive report fails closed. See docs/architecture/decisions/row-level-security.md.
/// </summary>
public sealed class PortalExecutionIdentityService(
    IServiceScopeFactory scopeFactory, PortalConfig config, ILogger<PortalExecutionIdentityService> log)
{
    /// <param name="userId">The real actor.</param>
    /// <param name="impersonatedUserId">When set, the user whose rows are shown; the actor stays <paramref name="userId"/>.</param>
    public async Task<ExecutionIdentity?> BuildAsync(
        int userId, string tenantId, bool isAdministrator, int? impersonatedUserId, CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();

            // Under impersonation the effective identity (whose rows are shown) is the target user;
            // the real actor stays the administrator. Row-level predicates and the admin bypass key
            // off the *effective* (target) identity, so impersonating a non-admin shows that user's
            // restricted view. Permission to run the report was already gated on the real actor.
            var effectiveUserId = impersonatedUserId ?? userId;

            var effective = await db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == effectiveUserId && u.TenantId == tenantId, ct);
            if (effective is null) return null;

            var roles = await (from ur in db.UserRoles
                               join r in db.Roles on ur.RoleId equals r.Id
                               where ur.UserId == effectiveUserId && r.Name != null
                               select r.Name!).ToListAsync(ct);
            var groups = await (from ug in db.UserGroups
                                join g in db.Groups on ug.GroupId equals g.Id
                                where ug.UserId == effectiveUserId
                                select g.Name).ToListAsync(ct);

            var effectiveName = effective.UserName ?? effectiveUserId.ToString();
            var realName = effectiveName;
            if (impersonatedUserId is not null)
            {
                var actor = await db.Users.AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == tenantId, ct);
                realName = actor?.UserName ?? userId.ToString();
            }

            return new ExecutionIdentity
            {
                EffectiveUser = effectiveName,
                EffectiveUserId = effectiveUserId,
                RealUser = realName,
                // IsAdmin reflects the effective identity, not the real actor, so an admin
                // impersonating a normal user does not carry admin bypass into the target's view.
                IsAdmin = impersonatedUserId is null
                    ? isAdministrator
                    : roles.Contains("Admin", StringComparer.OrdinalIgnoreCase),
                AdminBypassesRowLevelSecurity = config.Security.AdminBypassRowLevelSecurity,
                Groups = groups,
                Roles = roles
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex,
                "Failed to build execution identity for user {UserId}; identity-sensitive reports will fail closed.",
                userId);
            return null;
        }
    }
}
