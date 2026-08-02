using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ETL_SQL.Portal.Filters;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RequireStudioCapabilityAttribute(
    string capability,
    params StudioDeploymentMode[] allowedModes) : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var authorization = context.HttpContext.RequestServices.GetRequiredService<StudioAuthorizationService>();
        var modeAllowed = allowedModes.Length == 0 || allowedModes.Contains(authorization.Mode);
        if (authorization.Mode == StudioDeploymentMode.Disabled || !modeAllowed)
        {
            context.Result = new NotFoundObjectResult(new { error = "Studio authoring is unavailable in this deployment mode." });
            return;
        }

        if (!authorization.HasCapability(context.HttpContext.User, capability))
        {
            context.Result = new ForbidResult();
            return;
        }

        await next();
    }
}
