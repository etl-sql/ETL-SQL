using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Governance;

namespace ETL_SQL.Core.Data;

public enum OrchestratorObjectKind
{
    Job,
    Schedule,
    Notification
}

/// <summary>
/// Portal-aligned object grants. Manage includes every lower capability; Override includes Execute
/// and Read; Execute includes Read. Override is explicit because changing inputs may widen scope.
/// </summary>
public enum OrchestratorObjectPermission
{
    Read,
    Execute,
    Override,
    Manage
}

public enum OrchestratorPrincipalKind
{
    User,
    Group,
    Service
}

public sealed record OrchestratorObjectGrant(
    OrchestratorObjectKind ObjectKind,
    string ObjectName,
    OrchestratorPrincipalKind PrincipalKind,
    string PrincipalId,
    OrchestratorObjectPermission Permission,
    string GrantedBy,
    long Version = 1);

public interface IOrchestratorAuthorizationStore
{
    Task<IReadOnlyList<OrchestratorObjectGrant>> GetObjectGrantsAsync(
        OrchestratorObjectKind objectKind,
        string objectName,
        CancellationToken cancellationToken = default);

    Task SaveObjectGrantAsync(
        OrchestratorObjectGrant grant,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteObjectGrantAsync(
        OrchestratorObjectKind objectKind,
        string objectName,
        OrchestratorPrincipalKind principalKind,
        string principalId,
        CancellationToken cancellationToken = default);

    Task DeleteObjectGrantsAsync(
        OrchestratorObjectKind objectKind,
        string objectName,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Host-supplied policy boundary used by engine statement handlers. Local Solo/Team hosts may omit
/// it; a shared Orchestrator registers it so catalog SQL cannot bypass the HTTP authorization layer.
/// </summary>
public interface IOrchestratorObjectAuthorizer
{
    bool CanCreate(ExecutionIdentity? identity);

    Task<bool> CanAsync(
        ExecutionIdentity? identity,
        OrchestratorObjectKind objectKind,
        string objectName,
        OrchestratorObjectPermission required,
        string? owner,
        CancellationToken cancellationToken = default);
}
