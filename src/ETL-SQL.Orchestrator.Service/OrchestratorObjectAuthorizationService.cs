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
        string objectName,
        OrchestratorObjectPermission required,
        string? owner,
        CancellationToken cancellationToken) =>
        identity is not null && await CanAsync(
            ToCaller(identity), objectKind, objectName, required, owner, cancellationToken);

    public async Task<bool> CanAsync(
        OrchestratorCaller caller,
        OrchestratorObjectKind objectKind,
        string objectName,
        OrchestratorObjectPermission required,
        string? owner,
        CancellationToken cancellationToken = default)
    {
        if (caller.IsInRole("Admin")) return true;
        if (!string.IsNullOrWhiteSpace(owner)
            && string.Equals(owner, caller.PrincipalKey, StringComparison.OrdinalIgnoreCase))
            return true;
        if (caller.IsInRole("PortalSystem")
            && objectKind == OrchestratorObjectKind.Notification
            && required is OrchestratorObjectPermission.Read or OrchestratorObjectPermission.Execute)
            return true;

        var grants = await store.GetObjectGrantsAsync(objectKind, objectName, cancellationToken);
        foreach (var grant in grants)
        {
            if (!Matches(caller, grant)) continue;
            if (Includes(grant.Permission, required)) return true;
        }
        return false;
    }

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
            identity.Groups.ToArray());
    }
}
