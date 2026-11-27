using System.Security.Claims;
using ETL_SQL.Core.Governance;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

internal static class ScheduledToolPrincipalEndpoints
{
    public static void MapScheduledToolPrincipalLookup(this IEndpointRouteBuilder app) =>
        app.MapPost(ScheduledToolPrincipalRequest.Path, ResolveAsync).RequireAuthorization();

    private static async Task<IResult> ResolveAsync(
        ScheduledToolPrincipalRequest request, HttpContext context, PortalConfig config,
        RequestTenantContextAccessor tenantAccessor, PortalDbContext db, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (context.User.FindFirstValue(TokenService.IdentityTypeClaim) != TokenService.ServiceIdentityType
            || context.User.HasClaim(claim => claim.Type == TokenService.WorkloadBindingClaim)
            || !context.User.HasClaim(TokenService.ScopeClaim, ScheduledToolPrincipalRequest.Scope))
            return Results.Forbid();

        var tenant = tenantAccessor.Current?.Tenant.Value
            ?? (config.SharedTenancy.Enabled ? null : "portal-host");
        if (tenant is null || !request.TryGetOwner(out var kind, out var id))
            return Results.NotFound();

        string[]? groupIds;
        if (kind == ToolPrincipalKind.User)
        {
            groupIds = await db.Users.AsNoTracking()
                .Where(user => user.TenantId == tenant && user.PrincipalKey == id && user.IsActive)
                .Select(user => user.UserGroups
                    .Where(membership => membership.TenantId == tenant
                        && membership.Group.TenantId == tenant && membership.Group.PrincipalKey != null)
                    .Select(membership => membership.Group.PrincipalKey!)
                    .ToArray())
                .SingleOrDefaultAsync(ct);
        }
        else
        {
            var now = DateTime.UtcNow;
            var scopes = await db.ServiceAccounts.AsNoTracking()
                .Where(account => account.Id == id && account.TenantId == tenant && account.IsEnabled
                    && account.RevokedAt == null && (account.ExpiresAt == null || account.ExpiresAt > now)
                    && account.OwnerUser.IsActive && account.OwnerUser.TenantId == tenant)
                .Select(account => account.Scopes)
                .SingleOrDefaultAsync(ct);
            groupIds = scopes is not null && ServiceAccountScopes.Parse(scopes).Any(scope =>
                scope is ServiceAccountScopes.OrchestratorExecute
                    or ServiceAccountScopes.OrchestratorPublish or ServiceAccountScopes.OrchestratorAdmin)
                ? [] : null;
        }

        return groupIds is null
            ? Results.NotFound()
            : Results.Ok(new ScheduledToolPrincipalResponse(tenant, request.OwnerKey, groupIds));
    }
}
