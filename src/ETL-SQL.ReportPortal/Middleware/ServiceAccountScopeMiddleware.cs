using ETL_SQL.ReportPortal.Services;

namespace ETL_SQL.ReportPortal.Middleware;

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
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "service_account_scope_denied", requiredScope = required });
            return;
        }
        await next(context);
    }

    private static string? RequiredScope(HttpRequest request)
    {
        var path = request.Path.Value ?? "";
        if (path.StartsWith("/api/admin", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/auth", StringComparison.OrdinalIgnoreCase)
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
