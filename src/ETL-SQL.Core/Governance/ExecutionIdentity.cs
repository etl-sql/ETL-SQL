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
    private readonly HashSet<string> _scopes = new(StringComparer.OrdinalIgnoreCase);

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

    /// <summary>
    /// The ceiling carried by a **service** caller's token — what the automation was issued to do,
    /// capping what its roles and grants can then authorize. Empty for an interactive user, whose
    /// authority is their roles and grants, already bounded by the session the identity came from.
    ///
    /// <para>This is carried here so that the same caller reaches the same decision whether a verb
    /// arrives as an HTTP call or as an ETL-SQL statement. It was previously dropped at the host
    /// boundary, which left every service principal looking scopeless to the engine — and a service
    /// caller with no scopes is denied everything, so the two doors disagreed.</para>
    /// </summary>
    public IEnumerable<string> Scopes
    {
        get => _scopes;
        init => _scopes = ToCaseInsensitiveSet(value);
    }

    /// <summary>
    /// The identity a <b>preview-as</b> run evaluates author predicates under: an audience described
    /// by its groups and roles, with no portal user behind it.
    ///
    /// <para>Three things are fixed here rather than left to each host, because a preview identity
    /// built two ways is a security-relevant difference nobody would notice until it mattered.
    /// <see cref="RealUser"/> stays the actual actor, so dataset and connection authority — which is
    /// keyed on the real user — is untouched by the preview. <see cref="IsAdmin"/> is false and the
    /// bypass with it: an administrator sees every row by design, so previewing as one would answer
    /// "all rows" regardless of what the predicate says, which is the one answer a preview must never
    /// give. And <see cref="EffectiveUserId"/> is null, because there is no such user — a made-up
    /// audience that carried somebody's id would compare equal to them in a predicate written against
    /// <c>@@CURRENT_USER_ID</c>.</para>
    ///
    /// <para><see cref="TenantId"/> is the caller's own, never the request's: it is a server-verified
    /// binding for catalog and execution authority, and a preview does not get to change it.</para>
    /// </summary>
    /// <param name="label">What to answer <c>@@CURRENT_USER</c> with; the audience's name.</param>
    /// <param name="scopes">
    /// The caller's own token ceiling, carried through. A ceiling only caps what roles and grants
    /// authorize — it never grants — so passing the caller's cannot escalate, while dropping it
    /// would deny a service caller things their own run would have been allowed.
    /// </param>
    public static ExecutionIdentity Preview(
        string? label,
        IEnumerable<string>? groups,
        IEnumerable<string>? roles,
        string realUser,
        string? tenantId,
        IEnumerable<string>? scopes = null) =>
        new()
        {
            EffectiveUser = string.IsNullOrWhiteSpace(label) ? "preview" : label!.Trim(),
            EffectiveUserId = null,
            TenantId = tenantId,
            RealUser = realUser,
            IsAdmin = false,
            AdminBypassesRowLevelSecurity = false,
            Groups = groups ?? [],
            Roles = roles ?? [],
            Scopes = scopes ?? [],
        };

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
