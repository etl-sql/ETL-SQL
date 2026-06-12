using ETL_SQL.ReportPortal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ETL_SQL.ReportPortal.Services;

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

    private static string Key(int userId) => $"user-security-state:{userId}";

    /// <summary>Returns the user's current state, from cache or a single DB lookup.
    /// A missing user is cached as null for the same TTL.</summary>
    public async Task<UserSecurityState?> GetAsync(int userId, PortalDbContext db)
    {
        if (cache.TryGetValue(Key(userId), out UserSecurityState? cached))
            return cached;

        var user = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => new UserSecurityState(u.IsActive, u.SecurityStamp))
            .FirstOrDefaultAsync();

        cache.Set(Key(userId), user, Ttl);
        return user;
    }

    public void Evict(int userId) => cache.Remove(Key(userId));
}
