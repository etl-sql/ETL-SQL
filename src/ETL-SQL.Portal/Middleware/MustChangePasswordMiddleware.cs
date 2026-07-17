using System.Security.Claims;
using System.Text.Json;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Identity;

namespace ETL_SQL.Portal.Middleware;

/// <summary>
/// Blocks authenticated users with MustChangePassword = true from all API endpoints
/// except /api/auth/change-password and /api/auth/logout.
/// </summary>
public class MustChangePasswordMiddleware(RequestDelegate next)
{
    private static readonly HashSet<string> _allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/auth/change-password",
        "/api/auth/logout",
        "/api/auth/login",
        "/api/auth/refresh",
    };

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (ctx.User.Identity?.IsAuthenticated == true
            && ctx.User.FindFirstValue(TokenService.IdentityTypeClaim) != TokenService.ServiceIdentityType
            && ctx.Request.Path.StartsWithSegments("/api")
            && !_allowed.Contains(ctx.Request.Path.Value ?? ""))
        {
            var userMgr = ctx.RequestServices.GetRequiredService<UserManager<PortalUser>>();
            var nameId = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (nameId is not null)
            {
                var user = await userMgr.FindByIdAsync(nameId);
                if (user?.MustChangePassword == true)
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.WriteAsync(JsonSerializer.Serialize(new
                    {
                        error = "Password change required before continuing.",
                        redirect = "/login.html?changePassword=true"
                    }));
                    return;
                }
            }
        }

        await next(ctx);
    }
}
