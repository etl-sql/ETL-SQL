using ETL_SQL.ReportPortal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.ReportPortal.Services;

/// <summary>
/// Invalidates issued access tokens by rotating the Identity security stamp and revokes
/// outstanding refresh tokens for the affected users.
/// </summary>
public class SecuritySessionService(PortalDbContext db)
{
    public Task InvalidateUserAsync(int userId, CancellationToken ct = default) =>
        InvalidateUsersAsync([userId], ct);

    public async Task InvalidateUsersAsync(
        IEnumerable<int> userIds,
        CancellationToken ct = default)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return;

        var now = DateTime.UtcNow;
        var users = await db.Users.Where(u => ids.Contains(u.Id)).ToListAsync(ct);
        foreach (var user in users)
            user.SecurityStamp = Guid.NewGuid().ToString("N");

        var refreshTokens = await db.RefreshTokens
            .Where(t => ids.Contains(t.UserId) && t.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var token in refreshTokens)
            token.RevokedAt = now;

        await db.SaveChangesAsync(ct);
    }

    public async Task InvalidateGroupMembersAsync(int groupId, CancellationToken ct = default)
    {
        var userIds = await db.UserGroups
            .Where(ug => ug.GroupId == groupId)
            .Select(ug => ug.UserId)
            .ToListAsync(ct);
        await InvalidateUsersAsync(userIds, ct);
    }

    public async Task RevokeAnonymousCapabilitiesAsync(
        IEnumerable<int> userIds,
        CancellationToken ct = default)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return;

        var now = DateTime.UtcNow;
        var shareLinks = await db.ReportShareLinks
            .Where(link => ids.Contains(link.CreatedBy) && link.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var link in shareLinks)
            link.RevokedAt = now;

        var embedTokens = await db.ReportEmbedTokens
            .Where(token => ids.Contains(token.CreatedBy) && token.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var token in embedTokens)
            token.RevokedAt = now;

        await db.SaveChangesAsync(ct);
    }
}
