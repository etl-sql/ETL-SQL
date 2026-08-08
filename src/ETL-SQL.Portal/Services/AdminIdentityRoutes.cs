namespace ETL_SQL.Portal.Services;

/// <summary>
/// The explicit set of <c>/api/admin</c> routes a service identity holding
/// <see cref="ServiceAccountScopes.AdminIdentity"/> may reach.
///
/// <para><b>This is an allowlist, and that is the point.</b> Service accounts were categorically
/// barred from every admin route, which is a deliberate posture. Opening identity administration to
/// automation carves a hole in that deny, so the hole is enumerated rather than described by a
/// pattern: an admin endpoint added later is unreachable by token until someone adds it here on
/// purpose. A prefix rule such as "anything under users/" would silently admit the next endpoint
/// someone hangs off that prefix.</para>
///
/// <para>Everything outside this list — backup and restore, configuration export, environment
/// promotion, support bundles, audit collection and export, operational metrics, branding and
/// orchestrator settings, service restart and shutdown, and dataset at-rest key rotation — keeps
/// returning 403 for service identities regardless of scope.</para>
/// </summary>
public static class AdminIdentityRoutes
{
    private const string Prefix = "/api/admin/";

    /// <summary>
    /// Route templates, relative to <c>/api/admin/</c>. A <c>*</c> segment matches exactly one
    /// path segment (an id), never a separator, so <c>users/*</c> cannot match
    /// <c>users/1/favorites</c>.
    /// </summary>
    private static readonly (string Method, string Template)[] Templates =
    [
        // Users
        ("GET",    "users"),
        ("GET",    "users/catalog"),
        ("POST",   "users"),
        ("GET",    "users/*"),
        ("PUT",    "users/*"),
        ("DELETE", "users/*"),
        ("POST",   "users/bulk-status"),
        ("POST",   "users/*/reset-password"),
        ("POST",   "users/*/revoke-tokens"),
        ("POST",   "users/*/disconnect"),

        // Sessions
        ("GET",    "sessions"),

        // Groups
        ("GET",    "groups"),
        ("GET",    "groups/catalog"),
        ("POST",   "groups"),
        ("GET",    "groups/*"),
        ("PUT",    "groups/*"),
        ("DELETE", "groups/*"),
        ("POST",   "groups/bulk-delete"),

        // Group membership
        ("GET",    "groups/*/members"),
        ("GET",    "groups/*/members/catalog"),
        ("POST",   "groups/*/members"),
        ("POST",   "groups/*/members/bulk-add"),
        ("POST",   "groups/*/members/bulk-remove"),
        ("DELETE", "groups/*/members/*"),

        // Studio capabilities are granted per group, so they are part of group administration.
        ("GET",    "groups/*/studio-capabilities"),
        ("PUT",    "groups/*/studio-capabilities"),

        // Read-only introspection of a single user's access. Answers "why can this person see
        // this" without a browser. Folder- and report-keyed effective permissions are not identity
        // administration and are deliberately absent.
        ("GET",    "permissions/effective/user/*"),
        ("GET",    "access-simulator/user/*")
    ];

    /// <summary>True when the request targets an identity route reachable with the identity scope.</summary>
    public static bool IsIdentityRoute(string path, string method)
    {
        if (string.IsNullOrEmpty(path)) return false;

        // Compare without a trailing slash or query; the caller passes HttpRequest.Path.
        var trimmed = path.TrimEnd('/');
        if (!trimmed.StartsWith(Prefix.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase)) return false;

        var relative = trimmed[Prefix.Length..];
        if (relative.Length == 0) return false;

        var segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);

        foreach (var (templateMethod, template) in Templates)
        {
            if (!string.Equals(templateMethod, method, StringComparison.OrdinalIgnoreCase)) continue;
            if (Matches(segments, template)) return true;
        }
        return false;
    }

    private static bool Matches(string[] segments, string template)
    {
        var expected = template.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (expected.Length != segments.Length) return false;

        for (var i = 0; i < expected.Length; i++)
        {
            if (expected[i] == "*")
            {
                // An id segment must be present, but is not otherwise constrained here — model
                // binding rejects a non-integer before the action runs.
                if (segments[i].Length == 0) return false;
                continue;
            }
            if (!string.Equals(expected[i], segments[i], StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }
}
