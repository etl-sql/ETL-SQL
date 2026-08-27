using System.Text.Json;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Governance;
using ETL_SQL.Gateway;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Services;

/// <summary>Catalog-authorized Portal route into an authenticated outbound Gateway session.</summary>
public interface IPortalGatewayGrantResolver
{
    Task<IReadOnlyList<GatewayResourceGrant>> ResolveAsync(
        ExecutionIdentity identity,
        GatewayResourceBinding binding,
        GatewayOperationClass operationClass,
        CancellationToken cancellationToken);
}

public sealed class PortalGatewayCatalogGrantResolver(IServiceScopeFactory scopeFactory, PortalConfig config)
    : IPortalGatewayGrantResolver
{
    public async Task<IReadOnlyList<GatewayResourceGrant>> ResolveAsync(
        ExecutionIdentity identity,
        GatewayResourceBinding binding,
        GatewayOperationClass operationClass,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(identity.TenantId) || string.IsNullOrWhiteSpace(binding.CatalogAlias))
            return [];

        var catalogTenant = identity.TenantId == "default"
            && config.SharedTenancy.Enabled != true
            && string.IsNullOrWhiteSpace(config.TenantId)
                ? "portal-host"
                : identity.TenantId;

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var connection = await db.PortalSharedConnections
            .AsNoTracking()
            .Include(item => item.Acls).ThenInclude(acl => acl.Group)
            .SingleOrDefaultAsync(item => item.TenantId == catalogTenant
                && item.Alias == binding.CatalogAlias
                && !item.Disabled, cancellationToken)
            .ConfigureAwait(false);
        if (connection is null || connection.Acls.Count == 0)
            return [];

        Dictionary<string, string>? options;
        try
        {
            options = JsonSerializer.Deserialize<Dictionary<string, string>>(connection.OptionsJson);
        }
        catch (JsonException)
        {
            return [];
        }

        if (options is null
            || !options.TryGetValue("__gateway_id", out var storedGatewayId)
            || !options.TryGetValue("__gateway_resource_id", out var storedResourceId)
            || !string.Equals(storedGatewayId, binding.GatewayId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(storedResourceId, binding.ResourceId, StringComparison.OrdinalIgnoreCase))
            return [];

        var isGranted = identity.IsAdmin
            || (connection.OwnerUserId is not null && identity.EffectiveUserId == connection.OwnerUserId);
        if (!isGranted && identity.EffectiveUserId is int userId)
        {
            var groupIds = await db.UserGroups.AsNoTracking()
                .Where(item => item.TenantId == catalogTenant && item.UserId == userId)
                .Select(item => item.GroupId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            isGranted = connection.Acls.Any(acl => groupIds.Contains(acl.GroupId));
        }
        else if (!isGranted)
        {
            isGranted = connection.Acls.Any(acl => identity.HasGroup(acl.Group.Name));
        }

        return isGranted
            ? [new GatewayResourceGrant(
                identity.TenantId, binding.GatewayId, binding.ResourceId,
                identity.EffectiveUser, operationClass)]
            : [];
    }
}

public sealed class PortalGatewayOperationRouter(
    IGatewayEnrollmentStore enrollments,
    GatewaySessionRegistry sessions,
    IPortalGatewayGrantResolver grantResolver,
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
        var policyVersion = "catalog-acl-v2";
        var grants = await grantResolver.ResolveAsync(
            identity, binding, operationClass, cancellationToken).ConfigureAwait(false);
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
