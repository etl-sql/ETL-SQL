using System.Text.Json;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Governance;
using ETL_SQL.Gateway;

namespace ETL_SQL.Portal.Services;

/// <summary>Catalog-authorized Portal route into an authenticated outbound Gateway session.</summary>
public sealed class PortalGatewayOperationRouter(
    IGatewayEnrollmentStore enrollments,
    GatewaySessionRegistry sessions,
    IViewerContextEnvelopeSigner? viewerContextSigner = null) : IGatewayOperationRouter
{
    public async Task<GatewayRoutedResult> ExecuteAsync(
        ExecutionIdentity identity, GatewayResourceBinding binding,
        GatewayOperationClass operationClass, GatewayOperationEffect effect,
        GatewayOperationBounds bounds, string request, IReadOnlyList<string>? parameters,
        CancellationToken cancellationToken)
    {
        var tenantId = identity.TenantId;
        if (string.IsNullOrWhiteSpace(tenantId)
            || !sessions.TryGet(tenantId, binding.GatewayId, out var session)
            || session is null)
            throw new ExecutionException("The Gateway-bound connection is not currently available.");

        var enrollment = await enrollments.FindByGatewayAsync(tenantId, binding.GatewayId, cancellationToken)
            .ConfigureAwait(false);
        var resource = session.PublishedResources.FirstOrDefault(item =>
            string.Equals(item.ResourceId, binding.ResourceId, StringComparison.OrdinalIgnoreCase));
        var policyVersion = "catalog-acl-v1";
        var grants = new[]
        {
            new GatewayResourceGrant(
                tenantId, binding.GatewayId, binding.ResourceId, identity.EffectiveUser,
                operationClass)
        };
        var decision = GatewayAuthority.Evaluate(
            new GatewayRoutingRequest(
                tenantId, tenantId, session.TenantId, binding.GatewayId, binding.ResourceId,
                operationClass, identity.EffectiveUser, policyVersion),
            enrollment, binding, resource, grants, policyVersion);
        if (!decision.Allowed)
            throw new ExecutionException("Gateway authority denied the catalog-bound operation.");

        var operation = new GatewayOperation(
            Guid.NewGuid().ToString("N"), tenantId, binding.GatewayId, binding.ResourceId,
            operationClass, effect, bounds, Guid.NewGuid().ToString("N"), session.NodeId);
        ViewerContextEnvelope? viewerContext = null;
        if (resource?.ViewerContextPolicy is not null)
        {
            if (viewerContextSigner is null || string.IsNullOrWhiteSpace(resource.ExecutingCredentialId))
                throw new ExecutionException("Verified viewer context is not configured for this Gateway resource.");
            viewerContext = viewerContextSigner.Sign(
                operation, identity.EffectiveUser, identity.RealUser, resource.ExecutingCredentialId,
                BuildClaims(identity, resource.ViewerContextPolicy), resource.ViewerContextPolicy);
        }
        var result = await session.ExecuteAsync(operation, request, parameters, viewerContext, cancellationToken)
            .ConfigureAwait(false);
        return new GatewayRoutedResult(result.Columns, result.Rows, result.Truncated);
    }

    private static IReadOnlyDictionary<string, string> BuildClaims(
        ExecutionIdentity identity, ViewerContextPolicy policy)
    {
        var claims = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var allowed in policy.AllowedClaims)
        {
            var value = allowed.ToLowerInvariant() switch
            {
                "viewer_groups" => JsonSerializer.Serialize(
                    identity.Groups.OrderBy(item => item, StringComparer.OrdinalIgnoreCase)),
                "viewer_roles" => JsonSerializer.Serialize(
                    identity.Roles.OrderBy(item => item, StringComparer.OrdinalIgnoreCase)),
                "viewer_scopes" => JsonSerializer.Serialize(
                    identity.Scopes.OrderBy(item => item, StringComparer.OrdinalIgnoreCase)),
                "is_admin" => identity.IsAdmin ? "true" : "false",
                _ => null
            };
            if (value is not null) claims[allowed] = value;
        }
        return claims;
    }
}
