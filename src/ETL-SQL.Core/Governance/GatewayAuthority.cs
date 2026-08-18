namespace ETL_SQL.Core.Governance;

/// <summary>Why a Gateway routing request was refused. Ordered by the check that rejected it.</summary>
public enum GatewayAuthorityDenial
{
    None = 0,
    ExecutionTenantMissing,
    CapabilityTenantMismatch,
    GatewayIdentityTenantMismatch,
    GatewayNotEnrolled,
    GatewayRevoked,
    BindingMissing,
    BindingGatewayMismatch,
    ResourceNotOwnedByGateway,
    ResourceNotApproved,
    OperationNotPermitted,
    ActorGrantMissing,
    PolicyVersionMismatch
}

/// <summary>The facts a routing decision is made from. Every one is server-derived; none comes from a script.</summary>
public sealed record GatewayRoutingRequest(
    string? ExecutionTenantId,
    string? CapabilityTenantId,
    string? GatewayIdentityTenantId,
    string GatewayId,
    string ResourceId,
    GatewayOperationClass Operation,
    string ActorPrincipal,
    string PolicyVersion);

/// <summary>Outcome of an authority evaluation.</summary>
public sealed record GatewayAuthorityDecision(bool Allowed, GatewayAuthorityDenial Denial, string Reason)
{
    public static GatewayAuthorityDecision Allow() =>
        new(true, GatewayAuthorityDenial.None, "Gateway routing authorized.");

    public static GatewayAuthorityDecision Deny(GatewayAuthorityDenial denial, string reason) =>
        new(false, denial, reason);
}

/// <summary>A tenant administrator's grant of a mapped alias to a principal (§11.3 step 5).</summary>
public sealed record GatewayResourceGrant(
    string TenantId, string GatewayId, string ResourceId, string PrincipalName, GatewayOperationClass Operations);

/// <summary>
/// The single place a Gateway routing decision is made.
///
/// <para>§11.4: "Routing occurs only when execution tenant, capability tenant, Gateway identity
/// tenant, catalog binding, resource ownership, actor grant, and policy version agree." All seven
/// are checked here rather than spread across call sites, because the recurring failure mode in this
/// codebase has been each door being tested on its own while no test asserts the whole set. There is
/// no partial success: the method returns allow only when every clause holds.</para>
/// </summary>
public static class GatewayAuthority
{
    public static GatewayAuthorityDecision Evaluate(
        GatewayRoutingRequest request,
        GatewayEnrollment? enrollment,
        GatewayResourceBinding? binding,
        GatewayPublishedResource? resource,
        IReadOnlyCollection<GatewayResourceGrant> grants,
        string currentPolicyVersion)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(grants);

        // 1. Execution tenant. Absent context is a denial, never an implicit "any tenant".
        if (string.IsNullOrWhiteSpace(request.ExecutionTenantId))
            return GatewayAuthorityDecision.Deny(GatewayAuthorityDenial.ExecutionTenantMissing,
                "The attempt carries no server-derived execution tenant.");

        // 2-3. The capability and the Gateway's own workload identity must name the same tenant.
        if (!TenantMatches(request.ExecutionTenantId, request.CapabilityTenantId))
            return GatewayAuthorityDecision.Deny(GatewayAuthorityDenial.CapabilityTenantMismatch,
                "The presented capability belongs to a different tenant than the execution context.");
        if (!TenantMatches(request.ExecutionTenantId, request.GatewayIdentityTenantId))
            return GatewayAuthorityDecision.Deny(GatewayAuthorityDenial.GatewayIdentityTenantMismatch,
                "The Gateway session identity belongs to a different tenant than the execution context.");

        // 4. The Gateway is enrolled to this tenant and not revoked.
        if (enrollment is null || !TenantMatches(request.ExecutionTenantId, enrollment.TenantId)
            || !string.Equals(enrollment.GatewayId, request.GatewayId, StringComparison.Ordinal))
        {
            return GatewayAuthorityDecision.Deny(GatewayAuthorityDenial.GatewayNotEnrolled,
                "No enrollment binds this Gateway to this tenant.");
        }
        if (enrollment.State != GatewayEnrollmentState.Consumed)
            return GatewayAuthorityDecision.Deny(GatewayAuthorityDenial.GatewayRevoked,
                "The Gateway enrollment is not active.");

        // 5. The catalog binding must exist and must name this Gateway and resource.
        if (binding is null)
            return GatewayAuthorityDecision.Deny(GatewayAuthorityDenial.BindingMissing,
                "The alias has no Gateway binding.");
        if (!string.Equals(binding.GatewayId, request.GatewayId, StringComparison.Ordinal))
            return GatewayAuthorityDecision.Deny(GatewayAuthorityDenial.BindingGatewayMismatch,
                "The alias is bound to a different Gateway.");
        if (!string.Equals(binding.ResourceId, request.ResourceId, StringComparison.Ordinal))
            return GatewayAuthorityDecision.Deny(GatewayAuthorityDenial.ResourceNotOwnedByGateway,
                "The alias is bound to a different resource.");

        // 6. Resource ownership and approval, as published by that Gateway.
        if (resource is null || !string.Equals(resource.ResourceId, request.ResourceId, StringComparison.Ordinal))
            return GatewayAuthorityDecision.Deny(GatewayAuthorityDenial.ResourceNotOwnedByGateway,
                "The Gateway does not publish this resource.");
        if (resource.State != GatewayResourceState.Approved)
            return GatewayAuthorityDecision.Deny(GatewayAuthorityDenial.ResourceNotApproved,
                "The resource is not approved on the Gateway.");
        if (request.Operation == GatewayOperationClass.None
            || (resource.AllowedOperations & request.Operation) != request.Operation)
        {
            return GatewayAuthorityDecision.Deny(GatewayAuthorityDenial.OperationNotPermitted,
                "The resource does not permit the requested operation class.");
        }

        // 7. Actor grant. No grant means deny — there is no implicit tenant-wide access.
        var granted = grants.Any(grant =>
            TenantMatches(request.ExecutionTenantId, grant.TenantId)
            && string.Equals(grant.GatewayId, request.GatewayId, StringComparison.Ordinal)
            && string.Equals(grant.ResourceId, request.ResourceId, StringComparison.Ordinal)
            && string.Equals(grant.PrincipalName, request.ActorPrincipal, StringComparison.OrdinalIgnoreCase)
            && (grant.Operations & request.Operation) == request.Operation);
        if (!granted)
            return GatewayAuthorityDecision.Deny(GatewayAuthorityDenial.ActorGrantMissing,
                "The actor has no grant for this resource and operation.");

        // 8. Policy version. A run started under an older policy does not get to keep using it.
        if (!string.Equals(request.PolicyVersion, currentPolicyVersion, StringComparison.Ordinal))
            return GatewayAuthorityDecision.Deny(GatewayAuthorityDenial.PolicyVersionMismatch,
                "The attempt carries a stale policy version.");

        return GatewayAuthorityDecision.Allow();
    }

    private static bool TenantMatches(string executionTenantId, string? other) =>
        !string.IsNullOrWhiteSpace(other)
        && string.Equals(executionTenantId, other, StringComparison.Ordinal);
}
