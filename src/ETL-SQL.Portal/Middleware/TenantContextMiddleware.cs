using ETL_SQL.Portal.Services;

namespace ETL_SQL.Portal.Middleware;

/// <summary>
/// Converts the tenant claim from an authenticated, validated Portal JWT into the request-scoped
/// <see cref="ETL_SQL.Core.Multitenancy.TenantContext"/> consumed below controller code.
/// </summary>
public sealed class TenantContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext httpContext,
        PortalConfig config,
        RequestTenantContextAccessor accessor)
    {
        if (httpContext.User.Identity?.IsAuthenticated == true)
        {
            if (!TenantCredentialBinding.TryResolve(
                    httpContext.User, config, out var tenantContext, out var error))
            {
                httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await httpContext.Response.WriteAsJsonAsync(new
                {
                    error = "invalid_tenant_credential",
                    detail = error
                });
                return;
            }

            if (tenantContext?.Origin == ETL_SQL.Core.Multitenancy.TenantContextOrigin.VerifiedCredential)
                accessor.SetVerifiedCredential(tenantContext);
        }

        await next(httpContext);
    }
}
