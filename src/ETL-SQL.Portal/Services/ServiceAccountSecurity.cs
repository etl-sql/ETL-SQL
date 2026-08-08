using System.Security.Cryptography;
using ETL_SQL.Portal.Data;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

public static class ServiceAccountScopes
{
    public const string PortalRead = "portal.read";
    public const string ReportsExecute = "reports.execute";
    public const string OrchestratorExecute = "orchestrator.execute";

    /// <summary>
    /// Administration of identity only — users, groups, group membership, sessions, and read-only
    /// introspection of a user's effective access. Deliberately narrow: there is no blanket
    /// <c>admin.*</c>, so backup and restore, configuration export, environment promotion, support
    /// bundles, audit collection, service restart/shutdown, and at-rest key rotation stay
    /// unreachable by any token. Granting this scope never substitutes for the <c>Admin</c> role.
    /// </summary>
    public const string AdminIdentity = "admin.identity";

    public static readonly ISet<string> Allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        PortalRead, ReportsExecute, OrchestratorExecute, AdminIdentity
    };

    public static string Serialize(IEnumerable<string> values) => string.Join(' ', Normalize(values));
    public static string[] Parse(string values) => Normalize(values.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    public static string[] Normalize(IEnumerable<string>? values) => (values ?? [])
        .Select(value => value.Trim().ToLowerInvariant())
        .Where(value => value.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

public static class ServiceAccountCredentials
{
    public static string NewClientId() => "sa_" + Guid.NewGuid().ToString("N");
    public static string NewSecret() => "sas_" + WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
}

public sealed class ServiceAccountSecurityStateCache
{
    public sealed record State(bool IsEnabled, DateTime? ExpiresAt, DateTime? RevokedAt,
        string SecurityStamp, int OwnerUserId);

    public Task<State?> GetAsync(string id, PortalDbContext db) => db.ServiceAccounts
        .Where(value => value.Id == id)
        .Select(value => new State(value.IsEnabled, value.ExpiresAt, value.RevokedAt,
            value.SecurityStamp, value.OwnerUserId))
        .FirstOrDefaultAsync();
}
