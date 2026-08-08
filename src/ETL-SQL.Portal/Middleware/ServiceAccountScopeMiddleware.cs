using System.Security.Claims;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Middleware;

public sealed class ServiceAccountScopeMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.FindFirst(TokenService.IdentityTypeClaim)?.Value != TokenService.ServiceIdentityType)
        {
            await next(context);
            return;
        }

        var required = RequiredScope(context.Request);
        var scopes = context.User.FindAll(TokenService.ScopeClaim).Select(value => value.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (required is null || !scopes.Contains(required))
        {
            await DenyAsync(context, "service_account_scope_denied", required);
            return;
        }

        // Identity administration is the one place a token can change who may log in and with what
        // authority, so the role is proven here rather than trusted from the token. The role claim
        // is stamped at issue and a service token lives up to 15 minutes, which would otherwise let
        // a just-demoted owner keep creating users for the rest of that window. Ordinary Portal
        // routes keep the cheaper claim-only posture.
        if (required == ServiceAccountScopes.AdminIdentity && !await OwnerIsCurrentlyAdminAsync(context))
        {
            await DenyAsync(context, "service_account_admin_role_required", required);
            return;
        }

        await next(context);
    }

    private static async Task DenyAsync(HttpContext context, string error, string? requiredScope)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new { error, requiredScope });
    }

    /// <summary>
    /// Re-reads the owning user's <c>Admin</c> role from the store. A scope never substitutes for
    /// the role: both the claim and the current assignment must hold.
    /// </summary>
    private static async Task<bool> OwnerIsCurrentlyAdminAsync(HttpContext context)
    {
        if (!context.User.IsInRole("Admin")) return false;

        var ownerId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? context.User.FindFirst("sub")?.Value;
        if (!int.TryParse(ownerId, out var userId)) return false;

        var db = context.RequestServices.GetService<PortalDbContext>();
        if (db is null) return false;

        return await db.UserRoles
            .Join(db.Roles, userRole => userRole.RoleId, role => role.Id, (userRole, role) => new { userRole.UserId, role.Name })
            .AnyAsync(entry => entry.UserId == userId && entry.Name == "Admin");
    }

    private static string? RequiredScope(HttpRequest request)
    {
        var path = request.Path.Value ?? "";
        if (path.StartsWith("/api/admin", StringComparison.OrdinalIgnoreCase))
        {
            // Default-deny survives: only the enumerated identity routes are reachable, and any
            // other admin endpoint — including one added after this was written — returns null and
            // is refused.
            return AdminIdentityRoutes.IsIdentityRoute(path, request.Method)
                ? ServiceAccountScopes.AdminIdentity
                : null;
        }
        if (path.StartsWith("/api/auth", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/oidc", StringComparison.OrdinalIgnoreCase)) return null;
        if (path.StartsWith("/api/orchestrator", StringComparison.OrdinalIgnoreCase))
            return ServiceAccountScopes.OrchestratorExecute;
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
        {
            if ((path.StartsWith("/api/reports/", StringComparison.OrdinalIgnoreCase)
                    && (path.EndsWith("/execute", StringComparison.OrdinalIgnoreCase)
                        || path.EndsWith("/refresh", StringComparison.OrdinalIgnoreCase)))
                || (HttpMethods.IsDelete(request.Method)
                    && path.StartsWith("/api/jobs/", StringComparison.OrdinalIgnoreCase))
                || (path.StartsWith("/api/datasets", StringComparison.OrdinalIgnoreCase)
                    && path.EndsWith("/refresh", StringComparison.OrdinalIgnoreCase)))
                return ServiceAccountScopes.ReportsExecute;
            return null;
        }
        return ServiceAccountScopes.PortalRead;
    }
}
