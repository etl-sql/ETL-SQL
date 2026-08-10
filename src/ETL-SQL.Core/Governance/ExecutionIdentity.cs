using System;
using System.Collections.Generic;
using System.Linq;

namespace ETL_SQL.Core.Governance;

/// <summary>
/// The authenticated identity a script/report executes under, injected by the trusted host
/// (the Portal) from the authenticated principal. It is the sole source for the identity
/// system variables (<c>@@CURRENT_USER</c>, <c>@@IS_ADMIN</c>, …) and the <c>HAS_GROUP</c>/
/// <c>HAS_ROLE</c> predicate functions used for row-level security.
///
/// <para>Security invariants (see <c>Docs/Design/RowLevelSecurity.md</c>):</para>
/// <list type="bullet">
/// <item>Set only through the host-owned channel — never from report parameters, <c>SET</c>,
/// environment, or saved sessions.</item>
/// <item><see cref="EffectiveUser"/> is who the script runs *as* (the impersonated user under
/// impersonation); <see cref="RealUser"/> is the actual actor and is used for audit.</item>
/// <item>Group and role membership is matched case-insensitively.</item>
/// </list>
/// </summary>
public sealed record ExecutionIdentity
{
    public required string EffectiveUser { get; init; }
    public int? EffectiveUserId { get; init; }

    /// <summary>Server-verified tenant binding for host-managed catalog and execution authority.</summary>
    public string? TenantId { get; init; }

    /// <summary>The actual actor; differs from <see cref="EffectiveUser"/> only under impersonation.</summary>
    public required string RealUser { get; init; }

    /// <summary>Whether the effective identity holds administrator authority.</summary>
    public required bool IsAdmin { get; init; }

    /// <summary>
    /// Whether administrators bypass row-level security (see
    /// <c>Portal:Security:AdminBypassRowLevelSecurity</c>, default on). Set by the host; when true
    /// and <see cref="IsAdmin"/> is true, <see cref="EffectiveHasGroup"/> / <see cref="EffectiveHasRole"/>
    /// short-circuit to true so author predicates naturally let admins see all rows.
    /// </summary>
    public bool AdminBypassesRowLevelSecurity { get; init; } = true;

    /// <summary>Whether the effective identity differs from the real actor.</summary>
    public bool IsImpersonating => !string.Equals(EffectiveUser, RealUser, StringComparison.Ordinal);

    private readonly HashSet<string> _groups = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _roles = new(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<string> Groups
    {
        get => _groups;
        init => _groups = ToCaseInsensitiveSet(value);
    }

    public IEnumerable<string> Roles
    {
        get => _roles;
        init => _roles = ToCaseInsensitiveSet(value);
    }

    public bool HasGroup(string name) =>
        !string.IsNullOrEmpty(name) && _groups.Contains(name);

    public bool HasRole(string name) =>
        !string.IsNullOrEmpty(name) && _roles.Contains(name);

    /// <summary>Group membership as seen by RLS predicates, applying the admin bypass.</summary>
    public bool EffectiveHasGroup(string name) =>
        (IsAdmin && AdminBypassesRowLevelSecurity) || HasGroup(name);

    /// <summary>Role membership as seen by RLS predicates, applying the admin bypass.</summary>
    public bool EffectiveHasRole(string name) =>
        (IsAdmin && AdminBypassesRowLevelSecurity) || HasRole(name);

    private static HashSet<string> ToCaseInsensitiveSet(IEnumerable<string>? values) =>
        new(values?.Where(v => !string.IsNullOrWhiteSpace(v)) ?? Enumerable.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
}
