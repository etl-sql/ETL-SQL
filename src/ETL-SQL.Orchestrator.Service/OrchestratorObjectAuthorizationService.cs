using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;

namespace ETL_SQL.Orchestrator.Service;

public sealed class OrchestratorObjectAuthorizationService(IOrchestratorAuthorizationStore store)
    : IOrchestratorObjectAuthorizer
{
    public static bool CanCreate(OrchestratorCaller caller) =>
        caller.IsInRole("Admin")
        || caller.IsInRole("OrchestratorManager")
        || caller.SubjectId.Equals("legacy-api-key", StringComparison.OrdinalIgnoreCase);

    bool IOrchestratorObjectAuthorizer.CanCreate(ExecutionIdentity? identity) =>
        identity is not null && CanCreate(ToCaller(identity));

    async Task<bool> IOrchestratorObjectAuthorizer.CanAsync(
        ExecutionIdentity? identity,
        OrchestratorObjectKind objectKind,
        string? objectId,
        string? objectTenantId,
        OrchestratorObjectPermission required,
        string? owner,
        CancellationToken cancellationToken) =>
        identity is not null && await CanAsync(
            ToCaller(identity), objectKind, objectId, objectTenantId, required, owner, cancellationToken);

    // Typed entry points. The grant store keys every object kind in one column and so takes a
    // string, but callers hold a typed identity and must not be able to hand a *name* to an
    // authorization decision — a name resolves only within a tenant, so a decision made about one
    // object could otherwise be applied to another.
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
        // No identity, no authority: an object that has not been assigned a surrogate id cannot be
        // the subject of a grant, and guessing from its name is exactly the ambiguity the id removes.
        if (string.IsNullOrWhiteSpace(objectId)) return false;

        // Tenant first, before ownership, roles, or grants — including before Admin. A grant, an
        // ownership match, or an administrator role are all authority *within* a tenant; none of them
        // is authority to reach across one. Checking here rather than only at the calling surface
        // means a future endpoint that forgets to filter still cannot decide across the boundary.
        if (!TenantsMatch(caller.TenantId, objectTenantId)) return false;

        // Explicit legacy mode historically grants the configured API key full catalog authority.
        // It is available only when federated identity is disabled at startup.
        if (caller.SubjectType.Equals("service", StringComparison.OrdinalIgnoreCase)
            && caller.SubjectId.Equals("legacy-api-key", StringComparison.OrdinalIgnoreCase))
            return true;
        if (caller.IsInRole("Admin")) return true;
        if (!string.IsNullOrWhiteSpace(owner)
            && string.Equals(owner, caller.PrincipalKey, StringComparison.OrdinalIgnoreCase))
            return true;
        if (caller.IsInRole("PortalSystem")
            && objectKind == OrchestratorObjectKind.Notification
            && required is OrchestratorObjectPermission.Read or OrchestratorObjectPermission.Execute)
            return true;

        var grants = await store.GetObjectGrantsAsync(objectId, cancellationToken);
        foreach (var grant in grants)
        {
            if (!Matches(caller, grant)) continue;
            if (Includes(grant.Permission, required)) return true;
        }
        return false;
    }

    /// <summary>
    /// An unbound object (Solo, no signed tenant) is reachable only by an equally unbound caller, and
    /// a tenant-bound object only by that same tenant. Neither direction is permitted to fall back to
    /// the other: an unbound caller must not inherit a tenant's objects when a host is later attached
    /// to a Portal, and a tenant must not reach objects created before it existed.
    /// </summary>
    internal static bool TenantsMatch(string? callerTenantId, string? objectTenantId) =>
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
            identity.TenantId);
    }
}
