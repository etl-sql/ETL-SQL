using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Short-lived per-user cache of the active flag and security stamp consulted by JWT
/// validation, so revocation checks do not cost a database roundtrip on every request.
/// <see cref="SecuritySessionService"/> evicts on stamp rotation, making in-process
/// revocation immediate; cross-process staleness is bounded by <see cref="Ttl"/>.
/// </summary>
public sealed class UserSecurityStateCache(IMemoryCache cache)
{
    internal static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    public sealed record UserSecurityState(bool IsActive, string? SecurityStamp);

    private static string Key(string tenantId, int userId) => $"user-security-state:{tenantId}:{userId}";

    /// <summary>Returns the user's current state, from cache or a single DB lookup.
    /// A missing user is cached as null for the same TTL.</summary>
    public Task<UserSecurityState?> GetAsync(int userId, PortalDbContext db) =>
        GetAsync("portal-host", userId, db);

    public async Task<UserSecurityState?> GetAsync(string tenantId, int userId, PortalDbContext db)
    {
        if (cache.TryGetValue(Key(tenantId, userId), out UserSecurityState? cached))
            return cached;

        var user = await db.Users
            .Where(u => u.Id == userId && u.TenantId == tenantId)
            .Select(u => new UserSecurityState(u.IsActive, u.SecurityStamp))
            .FirstOrDefaultAsync();

        cache.Set(Key(tenantId, userId), user, Ttl);
        return user;
    }

    public void Evict(int userId) => Evict("portal-host", userId);

    public void Evict(string tenantId, int userId) => cache.Remove(Key(tenantId, userId));
}
