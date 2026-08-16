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

        // A route names every scope that satisfies it; the caller must hold at least one. Most name
        // exactly one — the "any of" shape exists for the assertion exchange, which any rung of the
        // orchestrator ladder may reach because it hands back a token that carries that rung and no
        // more. Null still means default-deny.
        var required = RequiredScopes(context.Request);
        var scopes = context.User.FindAll(TokenService.ScopeClaim).Select(value => value.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (required is null || !required.Any(scopes.Contains))
        {
            await DenyAsync(context, "service_account_scope_denied", required is null ? null : string.Join(' ', required));
            return;
        }

        // Identity administration is the one place a token can change who may log in and with what
        // authority, so the role is proven here rather than trusted from the token. The role claim
        // is stamped at issue and a service token lives up to 15 minutes, which would otherwise let
        // a just-demoted owner keep creating users for the rest of that window. Ordinary Portal
        // routes keep the cheaper claim-only posture.
        if ((required.Contains(ServiceAccountScopes.AdminIdentity)
             || required.Contains(ServiceAccountScopes.AdminPortability))
            && !await OwnerIsCurrentlyAdminAsync(context))
        {
            await DenyAsync(context, "service_account_admin_role_required", string.Join(' ', required));
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
        var accessor = context.RequestServices.GetService<RequestTenantContextAccessor>();
        var config = context.RequestServices.GetService<PortalConfig>();
        var tenantId = accessor?.Current?.Tenant.Value
            ?? (config?.SharedTenancy.Enabled != true
                ? string.IsNullOrWhiteSpace(config?.TenantId) ? "portal-host" : config.TenantId
                : null);
        if (db is null || tenantId is null) return false;

        return await db.UserRoles
            .Join(db.Users.Where(user => user.TenantId == tenantId),
                userRole => userRole.UserId, user => user.Id, (userRole, user) => userRole)
            .Join(db.Roles, userRole => userRole.RoleId, role => role.Id,
                (userRole, role) => new { userRole.UserId, role.Name })
            .AnyAsync(entry => entry.UserId == userId && entry.Name == "Admin");
    }

    /// <summary>
    /// Places one orchestrator proxy route on the scope ladder.
    ///
    /// <para>Default-deny, like the admin block above: an unrecognised orchestrator route returns
    /// <c>null</c> and is refused rather than falling to the narrowest scope. A route added later is
    /// therefore unreachable by a service account until someone decides which rung it belongs on,
    /// which is the safe direction for a surface that runs other people's jobs.</para>
    /// </summary>
    private static string[]? OrchestratorScopes(string path, string method)
    {
        // Service control is not an ordinary execution: stopping the Orchestrator stops everyone's
        // work, so it sits with grant administration rather than with trigger and kill.
        if (path.StartsWith("/api/orchestrator/service/", StringComparison.OrdinalIgnoreCase))
            return [ServiceAccountScopes.OrchestratorAdmin];

        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method))
            return [ServiceAccountScopes.OrchestratorRead];

        // Defining what runs — create, redefine, drop — is publication, not execution.
        var isJobDefinition =
            (HttpMethods.IsPost(method) && path.Equals("/api/orchestrator/jobs", StringComparison.OrdinalIgnoreCase))
            || HttpMethods.IsPut(method)
            || HttpMethods.IsDelete(method);
        if (isJobDefinition) return [ServiceAccountScopes.OrchestratorPublish];

        if (HttpMethods.IsPost(method)
            && (path.EndsWith("/trigger", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("/kill", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("/resume", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/api/orchestrator/jobs/rerun", StringComparison.OrdinalIgnoreCase)))
            return [ServiceAccountScopes.OrchestratorExecute];

        return null;
    }

    private static string[]? RequiredScopes(HttpRequest request)
    {
        var path = request.Path.Value ?? "";

        // Exchanging a session for an Orchestrator assertion is reachable from any rung: the token it
        // returns carries the caller's own scopes, so the ceiling that governs what the assertion can
        // do is the same one that governs the account. Requiring a particular rung here would only
        // stop an execute-only account from obtaining the token that says "execute only".
        if (HttpMethods.IsPost(request.Method)
            && path.Equals("/api/auth/orchestrator-assertion", StringComparison.OrdinalIgnoreCase))
            return [.. ServiceAccountScopes.OrchestratorLadder];
        if (path.StartsWith("/api/admin", StringComparison.OrdinalIgnoreCase))
        {
            // Default-deny survives: only the enumerated identity routes are reachable, and any
            // other admin endpoint — including one added after this was written — returns null and
            // is refused.
            if (AdminIdentityRoutes.IsIdentityRoute(path, request.Method))
                return [ServiceAccountScopes.AdminIdentity];
            if (HttpMethods.IsGet(request.Method)
                && path.Equals("/api/admin/configuration/export/plan", StringComparison.OrdinalIgnoreCase))
                return [ServiceAccountScopes.AdminPortability];
            if (HttpMethods.IsGet(request.Method)
                && path.Equals("/api/admin/configuration/export", StringComparison.OrdinalIgnoreCase)
                && request.Query.TryGetValue("acknowledgedPlan", out var acknowledged)
                && !string.IsNullOrWhiteSpace(acknowledged.ToString()))
                return [ServiceAccountScopes.AdminPortability];
            return null;
        }
        if (path.StartsWith("/api/auth", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/oidc", StringComparison.OrdinalIgnoreCase)) return null;
        if (path.StartsWith("/api/orchestrator", StringComparison.OrdinalIgnoreCase))
            return OrchestratorScopes(path, request.Method);
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
        {
            if ((path.StartsWith("/api/reports/", StringComparison.OrdinalIgnoreCase)
                    && (path.EndsWith("/execute", StringComparison.OrdinalIgnoreCase)
                        || path.EndsWith("/refresh", StringComparison.OrdinalIgnoreCase)))
                || (HttpMethods.IsDelete(request.Method)
                    && path.StartsWith("/api/jobs/", StringComparison.OrdinalIgnoreCase))
                || (path.StartsWith("/api/datasets", StringComparison.OrdinalIgnoreCase)
                    && path.EndsWith("/refresh", StringComparison.OrdinalIgnoreCase)))
                return [ServiceAccountScopes.ReportsExecute];
            return null;
        }
        return [ServiceAccountScopes.PortalRead];
    }
}
