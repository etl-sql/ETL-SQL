using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Invalidates issued access tokens by rotating the Identity security stamp and revokes
/// outstanding refresh tokens for the affected users.
/// </summary>
public class SecuritySessionService(
    PortalDbContext db,
    UserSecurityStateCache securityStateCache,
    PortalConfig config,
    RequestTenantContextAccessor tenantAccessor)
{
    private string TenantId => config.SharedTenancy.Enabled
        ? tenantAccessor.RequireCurrent().Tenant.Value
        : string.IsNullOrWhiteSpace(config.TenantId)
            ? "portal-host"
            : ETL_SQL.Core.Multitenancy.TenantId.FromTrustedSource(config.TenantId).Value;

    public Task InvalidateUserAsync(int userId, CancellationToken ct = default) =>
        InvalidateUsersAsync([userId], ct);

    public async Task InvalidateUsersAsync(
        IEnumerable<int> userIds,
        CancellationToken ct = default)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return;
        var tenantId = TenantId;

        var now = DateTime.UtcNow;
        var users = await db.Users.Where(u => u.TenantId == tenantId && ids.Contains(u.Id)).ToListAsync(ct);
        foreach (var user in users)
            user.SecurityStamp = Guid.NewGuid().ToString("N");

        var refreshTokens = await db.RefreshTokens
            .Where(t => t.TenantId == tenantId && ids.Contains(t.UserId) && t.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var token in refreshTokens)
            token.RevokedAt = now;

        await db.SaveChangesAsync(ct);

        // Make in-process revocation immediate; cross-process latency stays bounded by the TTL.
        foreach (var id in ids)
            securityStateCache.Evict(tenantId, id);
    }

    public async Task InvalidateGroupMembersAsync(int groupId, CancellationToken ct = default)
    {
        var tenantId = TenantId;
        var userIds = await db.UserGroups
            .Where(ug => ug.TenantId == tenantId && ug.GroupId == groupId)
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
