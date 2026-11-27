using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;

namespace ETL_SQL.Orchestrator.Execution;

/// <summary>
/// Shared object-level authorization evaluator used by both the Orchestrator HTTP API/in-process
/// runner and spawned child processes. When <paramref name="allowAnonymousWhenUnbound"/> is false
/// (hosted Orchestrator service), an unauthenticated null identity is rejected; when true (standalone
/// CLI / unbound local runs), null identities skip per-object checks while any identity-bound run
/// enforces full tenant, scope, ownership, role, and grant rules.
/// </summary>
public class OrchestratorObjectAuthorizer(
    IOrchestratorAuthorizationStore store,
    bool allowAnonymousWhenUnbound = false) : IOrchestratorObjectAuthorizer
{
    public const string ScopeRead = "orchestrator.read";
    public const string ScopeExecute = "orchestrator.execute";
    public const string ScopePublish = "orchestrator.publish";
    public const string ScopeAdmin = "orchestrator.admin";

    public static bool CanCreate(OrchestratorCaller caller)
    {
        if (caller.SubjectId.Equals("legacy-api-key", StringComparison.OrdinalIgnoreCase)) return true;
        if (!caller.IsInRole("Admin") && !caller.IsInRole("OrchestratorManager")) return false;
        return WithinScopeCeiling(caller, OrchestratorObjectPermission.Manage);
    }

    bool IOrchestratorObjectAuthorizer.CanCreate(ExecutionIdentity? identity)
    {
        if (identity is null)
            return allowAnonymousWhenUnbound;

        return CanCreate(ToCaller(identity));
    }

    async Task<bool> IOrchestratorObjectAuthorizer.CanAsync(
        ExecutionIdentity? identity,
        OrchestratorObjectKind objectKind,
        string? objectId,
        string? objectTenantId,
        OrchestratorObjectPermission required,
        string? owner,
        CancellationToken cancellationToken)
    {
        if (identity is null)
            return allowAnonymousWhenUnbound;

        return await CanAsync(
            ToCaller(identity), objectKind, objectId, objectTenantId, required, owner, cancellationToken);
    }

    public Task<bool> CanAsync(
        OrchestratorCaller caller, OrchestratorObjectKind objectKind, JobId objectId,
        string? objectTenantId, OrchestratorObjectPermission required, string? owner,
        CancellationToken cancellationToken = default) =>
        CanAsync(caller, objectKind, objectId.ToString(), objectTenantId, required, owner, cancellationToken);

    public Task<bool> CanAsync(
        OrchestratorCaller caller, OrchestratorObjectKind objectKind, ScheduleId objectId,
        string? objectTenantId, OrchestratorObjectPermission required, string? owner,
        CancellationToken cancellationToken = default) =>
        CanAsync(caller, objectKind, objectId.ToString(), objectTenantId, required, owner, cancellationToken);

    public Task<bool> CanAsync(
        OrchestratorCaller caller, OrchestratorObjectKind objectKind, NotificationId objectId,
        string? objectTenantId, OrchestratorObjectPermission required, string? owner,
        CancellationToken cancellationToken = default) =>
        CanAsync(caller, objectKind, objectId.ToString(), objectTenantId, required, owner, cancellationToken);

    public async Task<bool> CanAsync(
        OrchestratorCaller caller,
        OrchestratorObjectKind objectKind,
        string? objectId,
        string? objectTenantId,
        OrchestratorObjectPermission required,
        string? owner,
        CancellationToken cancellationToken = default)
    {
        if (DecideWithoutGrants(caller, objectKind, objectId, objectTenantId, required, owner) is { } decided)
            return decided;

        var grants = await store.GetObjectGrantsAsync(objectId!, cancellationToken);
        return GrantsAllow(caller, grants, required);
    }

    public async Task<List<T>> FilterAsync<T>(
        OrchestratorCaller caller,
        OrchestratorObjectKind objectKind,
        IEnumerable<T> items,
        Func<T, string?> objectId,
        Func<T, string?> objectTenantId,
        Func<T, string?> owner,
        OrchestratorObjectPermission required,
        CancellationToken cancellationToken = default)
    {
        var all = items as IReadOnlyList<T> ?? items.ToList();
        var decisions = new bool?[all.Count];
        var undecided = new List<string>();
        for (var i = 0; i < all.Count; i++)
        {
            decisions[i] = DecideWithoutGrants(
                caller, objectKind, objectId(all[i]), objectTenantId(all[i]), required, owner(all[i]));
            if (decisions[i] is null) undecided.Add(objectId(all[i])!);
        }

        var grants = undecided.Count == 0
            ? null
            : await store.GetObjectGrantsAsync(undecided, cancellationToken);

        var allowed = new List<T>(all.Count);
        for (var i = 0; i < all.Count; i++)
        {
            var allow = decisions[i]
                ?? (grants!.TryGetValue(objectId(all[i])!, out var objectGrants)
                    && GrantsAllow(caller, objectGrants, required));
            if (allow) allowed.Add(all[i]);
        }
        return allowed;
    }

    private static bool GrantsAllow(
        OrchestratorCaller caller, IReadOnlyList<OrchestratorObjectGrant> grants, OrchestratorObjectPermission required)
    {
        foreach (var grant in grants)
        {
            if (!Matches(caller, grant)) continue;
            if (Includes(grant.Permission, required)) return true;
        }
        return false;
    }

    private static bool? DecideWithoutGrants(
        OrchestratorCaller caller,
        OrchestratorObjectKind objectKind,
        string? objectId,
        string? objectTenantId,
        OrchestratorObjectPermission required,
        string? owner)
    {
        if (string.IsNullOrWhiteSpace(objectId)) return false;
        if (!TenantsMatch(caller.TenantId, objectTenantId)) return false;

        if (caller.SubjectType.Equals("service", StringComparison.OrdinalIgnoreCase)
            && caller.SubjectId.Equals("legacy-api-key", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!WithinScopeCeiling(caller, required)) return false;

        if (caller.IsInRole("Admin")) return true;
        if (!string.IsNullOrWhiteSpace(owner)
            && string.Equals(owner, caller.PrincipalKey, StringComparison.OrdinalIgnoreCase))
            return true;
        if (caller.IsInRole("PortalSystem")
            && objectKind == OrchestratorObjectKind.Notification
            && required is OrchestratorObjectPermission.Read or OrchestratorObjectPermission.Execute)
            return true;

        return null;
    }

    public static bool WithinScopeCeiling(OrchestratorCaller caller, OrchestratorObjectPermission required)
    {
        var isService = caller.SubjectType.Equals("service", StringComparison.OrdinalIgnoreCase);
        var scopes = caller.EffectiveScopes;
        if (scopes.Count == 0) return !isService;

        foreach (var scope in scopes)
        {
            var ceiling = CeilingOf(scope);
            if (ceiling is { } permission && Includes(permission, required)) return true;
        }
        return false;
    }

    private static OrchestratorObjectPermission? CeilingOf(string scope) => scope switch
    {
        _ when scope.Equals(ScopeAdmin, StringComparison.OrdinalIgnoreCase) => OrchestratorObjectPermission.Manage,
        _ when scope.Equals(ScopePublish, StringComparison.OrdinalIgnoreCase) => OrchestratorObjectPermission.Manage,
        _ when scope.Equals(ScopeExecute, StringComparison.OrdinalIgnoreCase) => OrchestratorObjectPermission.Override,
        _ when scope.Equals(ScopeRead, StringComparison.OrdinalIgnoreCase) => OrchestratorObjectPermission.Read,
        _ => null
    };

    public static bool TenantsMatch(string? callerTenantId, string? objectTenantId) =>
        string.Equals(
            string.IsNullOrWhiteSpace(callerTenantId) ? string.Empty : callerTenantId,
            string.IsNullOrWhiteSpace(objectTenantId) ? string.Empty : objectTenantId,
            StringComparison.OrdinalIgnoreCase);

    public static bool Includes(
        OrchestratorObjectPermission granted,
        OrchestratorObjectPermission required) => granted switch
        {
            OrchestratorObjectPermission.Manage => true,
            OrchestratorObjectPermission.Override => required is not OrchestratorObjectPermission.Manage,
            OrchestratorObjectPermission.Execute => required is OrchestratorObjectPermission.Read or OrchestratorObjectPermission.Execute,
            _ => required == OrchestratorObjectPermission.Read
        };

    private static bool Matches(OrchestratorCaller caller, OrchestratorObjectGrant grant) =>
        grant.PrincipalKind switch
        {
            OrchestratorPrincipalKind.User => caller.SubjectType.Equals("user", StringComparison.OrdinalIgnoreCase)
                && caller.SubjectId.Equals(grant.PrincipalId, StringComparison.OrdinalIgnoreCase),
            OrchestratorPrincipalKind.Service => caller.SubjectType.Equals("service", StringComparison.OrdinalIgnoreCase)
                && caller.SubjectId.Equals(grant.PrincipalId, StringComparison.OrdinalIgnoreCase),
            OrchestratorPrincipalKind.Group => caller.GroupIds.Contains(grant.PrincipalId, StringComparer.OrdinalIgnoreCase),
            _ => false
        };

    private static OrchestratorCaller ToCaller(ExecutionIdentity identity)
    {
        var separator = identity.EffectiveUser.IndexOf(':');
        var subjectType = separator > 0 ? identity.EffectiveUser[..separator] : "user";
        var subjectId = separator > 0 ? identity.EffectiveUser[(separator + 1)..] : identity.EffectiveUser;
        return new OrchestratorCaller(
            subjectType,
            subjectId,
            identity.RealUser,
            identity.Roles.ToArray(),
            identity.Groups.ToArray(),
            identity.TenantId,
            identity.Scopes.ToArray());
    }
}
