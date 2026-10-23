using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Share links and embed tokens are bearer credentials for a report, so the catalog keeps only a
/// SHA-256 of each. The token itself is shown once, in the response that creates it.
/// </summary>
public static class AnonymousReportTokens
{
    public const string HashPrefix = "sha256:";

    public static string Hash(string token) => HashPrefix + TokenService.HashRefreshToken(token);

    public static bool IsHashed(string stored) => stored.StartsWith(HashPrefix, StringComparison.Ordinal);

    /// <summary>
    /// Replaces any token stored before hashing with its hash. Links already handed out keep
    /// working; they can no longer be read back from the catalog.
    /// </summary>
    public static async Task<int> HashExistingPlaintextAsync(PortalDbContext db, ILogger logger, CancellationToken ct = default)
    {
        var updated = 0;
        foreach (var link in await db.ReportShareLinks.IgnoreQueryFilters()
                     .Where(l => !l.Token.StartsWith(HashPrefix)).ToListAsync(ct))
        {
            link.Token = Hash(link.Token);
            updated++;
        }
        foreach (var token in await db.ReportEmbedTokens.IgnoreQueryFilters()
                     .Where(t => !t.Token.StartsWith(HashPrefix)).ToListAsync(ct))
        {
            token.Token = Hash(token.Token);
            updated++;
        }

        if (updated > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Hashed {Count} stored share link and embed token value(s).", updated);
        }
        return updated;
    }
}
