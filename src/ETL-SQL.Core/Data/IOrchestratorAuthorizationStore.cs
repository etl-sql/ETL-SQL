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

/// <summary>
/// A grant against one object's surrogate identity. Grants deliberately do not carry the object's
/// name: resolving a name to an <see cref="ObjectId"/> already required the caller's tenant, so a
/// grant cannot be read across a tenant boundary, and a dropped object's id is never reissued, so a
/// later object of the same name starts with no grants.
/// </summary>
public sealed record OrchestratorObjectGrant(
    string ObjectId,
    OrchestratorObjectKind ObjectKind,
    OrchestratorPrincipalKind PrincipalKind,
    string PrincipalId,
    OrchestratorObjectPermission Permission,
    string GrantedBy,
    long Version = 1);

public interface IOrchestratorAuthorizationStore
{
    /// <summary>
    /// Resolves a tenant-scoped object name to its surrogate id, or null when the tenant has no such
    /// object. This is the only supported way to get from a name to a grant: there is deliberately no
    /// name-based grant lookup, because a name is only unique within a tenant.
    /// </summary>
    Task<string?> ResolveObjectIdAsync(
        string? tenantId,
        OrchestratorObjectKind objectKind,
        string objectName,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrchestratorObjectGrant>> GetObjectGrantsAsync(
        string objectId,
        CancellationToken cancellationToken = default);

    Task SaveObjectGrantAsync(
        OrchestratorObjectGrant grant,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteObjectGrantAsync(
        string objectId,
        OrchestratorPrincipalKind principalKind,
        string principalId,
        CancellationToken cancellationToken = default);

    Task DeleteObjectGrantsAsync(
        string objectId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Host-supplied policy boundary used by engine statement handlers. Local Solo/Team hosts may omit
/// it; a shared Orchestrator registers it so catalog SQL cannot bypass the HTTP authorization layer.
/// </summary>
public interface IOrchestratorObjectAuthorizer
{
    bool CanCreate(ExecutionIdentity? identity);

    /// <summary>
    /// Decides one access. <paramref name="objectTenantId"/> is the tenant the object is bound to and
    /// is compared against the caller's own before any grant is consulted — a grant must never be
    /// evaluated across a tenant boundary even if the calling surface forgot to filter.
    /// <para>
    /// A null or empty <paramref name="objectId"/> denies. An object with no identity cannot be the
    /// subject of a grant, so the only safe answer is no — decided here rather than at each caller,
    /// where it would be fifteen chances to suppress the null and accidentally allow.
    /// </para>
    /// </summary>
    Task<bool> CanAsync(
        ExecutionIdentity? identity,
        OrchestratorObjectKind objectKind,
        string? objectId,
        string? objectTenantId,
        OrchestratorObjectPermission required,
        string? owner,
        CancellationToken cancellationToken = default);
}
