using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Middleware;

/// <summary>
/// Enforces the Portal half of the lifecycle fence. It is enabled together with the separate
/// platform-management binding, so rollout can create lifecycle rows before switching enforcement
/// on. Missing, provisioning, upgrading, deleting, and deleted tenants all fail closed.
/// </summary>
public sealed class SharedTenantLifecycleMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        PortalConfig config,
        RequestTenantContextAccessor tenantAccessor,
        PortalDbContext db)
    {
        if (config.SharedTenancy.Enabled
            && !string.IsNullOrWhiteSpace(config.SharedTenancy.LifecycleManagementKey)
            && context.User.Identity?.IsAuthenticated == true)
        {
            var tenant = tenantAccessor.RequireCurrent().Tenant.Value;
            var state = await db.SharedTenantLifecycles.AsNoTracking()
                .Where(value => value.TenantId == tenant)
                .Select(value => value.State)
                .SingleOrDefaultAsync(context.RequestAborted);
            if (!string.Equals(state, "Active", StringComparison.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.Status423Locked;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "tenant_lifecycle_fenced",
                    state = state ?? "Unprovisioned"
                }, context.RequestAborted);
                return;
            }
        }

        await next(context);
    }
}
