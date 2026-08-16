using System.Security.Cryptography;
using ETL_SQL.Portal.Data;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

public static class ServiceAccountScopes
{
    public const string PortalRead = "portal.read";
    public const string ReportsExecute = "reports.execute";

    // ── The orchestrator ladder ───────────────────────────────────────────────
    //
    // Four scopes mirroring the per-object permission vocabulary (READ/EXECUTE/OVERRIDE/MANAGE) that
    // the Orchestrator already enforces, so a deployment reasons about one ladder rather than two
    // that have to be kept in agreement by hand.
    //
    // They are **explicit, not implicative**: holding `orchestrator.execute` does not confer
    // `orchestrator.read`. A token says exactly what it may do, which is what makes narrowing an
    // account a matter of removing a scope rather than reasoning about what a broader one still
    // implies. Migration therefore grants both where the old single scope meant both.

    /// <summary>View jobs, history, metrics, data-quality status, stewardship. Caps the ACL at READ.</summary>
    public const string OrchestratorRead = "orchestrator.read";

    /// <summary>
    /// Trigger, kill, resume, and supply variable overrides. Caps the ACL at OVERRIDE — an override
    /// changes a run's inputs, which is why it sits above plain execution and below MANAGE.
    /// </summary>
    public const string OrchestratorExecute = "orchestrator.execute";

    /// <summary>
    /// Create objects, and MANAGE the ones you own. Caps the ACL at MANAGE; ownership is still
    /// enforced beneath, so this is authority over your own objects and not over anyone else's.
    /// </summary>
    public const string OrchestratorPublish = "orchestrator.publish";

    /// <summary>
    /// Administer anyone's grants, and control the service itself. Caps the ACL at MANAGE. Distinct
    /// from <see cref="OrchestratorPublish"/> because publishing your own work and re-assigning other
    /// people's access are different powers that should be grantable separately.
    /// </summary>
    public const string OrchestratorAdmin = "orchestrator.admin";

    /// <summary>
    /// Administration of identity only — users, groups, group membership, sessions, and read-only
    /// introspection of a user's effective access. Deliberately narrow: there is no blanket
    /// <c>admin.*</c>, so backup and restore, environment promotion, support bundles, audit
    /// collection, service restart/shutdown, at-rest key rotation, and configuration export unless
    /// separately granted by <c>admin.portability</c> remain unreachable. Granting this scope never
    /// substitutes for the <c>Admin</c> role.
    /// </summary>
    public const string AdminIdentity = "admin.identity";

    /// <summary>
    /// Read-only access to the reviewed tenant configuration export plan and its acknowledged
    /// bootstrap download. It grants no other Admin route and no import/mutation authority.
    /// </summary>
    public const string AdminPortability = "admin.portability";

    public static readonly ISet<string> Allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        PortalRead, ReportsExecute,
        OrchestratorRead, OrchestratorExecute, OrchestratorPublish, OrchestratorAdmin,
        AdminIdentity, AdminPortability
    };

    /// <summary>Every orchestrator scope, narrowest first — the ladder, in order.</summary>
    public static readonly IReadOnlyList<string> OrchestratorLadder =
        [OrchestratorRead, OrchestratorExecute, OrchestratorPublish, OrchestratorAdmin];

    public static string Serialize(IEnumerable<string> values) => string.Join(' ', Normalize(values));

    /// <summary>
    /// Reads a stored scope string, upgrading the pre-ladder meaning of <c>orchestrator.execute</c>.
    ///
    /// <para>That scope once meant "reach the Orchestrator API", covering reads and executions alike.
    /// It now means executions only, so an account stored before the split would silently lose its
    /// read access. Upgrading on read rather than by a data migration keeps the two stores from
    /// disagreeing while one is upgraded. It grants <c>read</c> and never <c>publish</c>: an account
    /// that could trigger a job was never thereby allowed to create one.</para>
    /// </summary>
    public static string[] Parse(string values)
    {
        var scopes = Normalize(values.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (!scopes.Contains(OrchestratorExecute, StringComparer.OrdinalIgnoreCase)
            || scopes.Contains(OrchestratorRead, StringComparer.OrdinalIgnoreCase))
            return scopes;
        return Normalize([.. scopes, OrchestratorRead]);
    }
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

    public Task<State?> GetAsync(string id, string tenantId, PortalDbContext db) => db.ServiceAccounts
        .Where(value => value.Id == id && value.TenantId == tenantId)
        .Select(value => new State(value.IsEnabled, value.ExpiresAt, value.RevokedAt,
            value.SecurityStamp, value.OwnerUserId))
        .FirstOrDefaultAsync();
}
