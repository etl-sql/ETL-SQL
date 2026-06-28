using System.Security.Cryptography;
using ETL_SQL.ReportPortal.Data;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.ReportPortal.Services;

public static class ServiceAccountScopes
{
    public const string PortalRead = "portal.read";
    public const string ReportsExecute = "reports.execute";
    public const string OrchestratorExecute = "orchestrator.execute";
    public static readonly ISet<string> Allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        PortalRead, ReportsExecute, OrchestratorExecute
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
