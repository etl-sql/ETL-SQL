using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Short-lived cache of native client session revocation/expiry state consulted by JWT validation.
/// Native logout, refresh-token reuse, and user-wide invalidation evict entries immediately.
/// </summary>
public sealed class NativeClientSessionCache(IMemoryCache cache)
{
    internal static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    public sealed record NativeClientSessionState(
        int UserId,
        string ClientId,
        DateTime ExpiresAt,
        DateTime? RevokedAt);

    private static string Key(string tenantId, string sessionId) =>
        $"native-client-session:{tenantId}:{sessionId}";

    public async Task<NativeClientSessionState?> GetAsync(
        string tenantId,
        string sessionId,
        PortalDbContext db,
        CancellationToken ct = default)
    {
        if (cache.TryGetValue(Key(tenantId, sessionId), out NativeClientSessionState? cached))
            return cached;

        var session = await db.NativeClientSessions
            .AsNoTracking()
            .Where(s => s.Id == sessionId && s.TenantId == tenantId)
            .Select(s => new NativeClientSessionState(s.UserId, s.ClientId, s.ExpiresAt, s.RevokedAt))
            .FirstOrDefaultAsync(ct);

        cache.Set(Key(tenantId, sessionId), session, Ttl);
        return session;
    }

    public void Evict(string tenantId, string sessionId) =>
        cache.Remove(Key(tenantId, sessionId));
}
