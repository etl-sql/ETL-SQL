using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ETL_SQL.Portal.Filters;

/// <summary>
/// Exempts one action from a class-level <see cref="RequireStudioCapabilityAttribute"/>.
///
/// <para>Exists for capability <b>probes</b>: an endpoint whose answer is "here is what you may do"
/// cannot require the thing it reports on, or the answer for everyone without it is an error rather
/// than an empty list.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AllowStudioCapabilityBypassAttribute : Attribute;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RequireStudioCapabilityAttribute(
    string capability,
    params StudioDeploymentMode[] allowedModes) : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var bypass = context.ActionDescriptor.EndpointMetadata
            .OfType<AllowStudioCapabilityBypassAttribute>().Any();
        if (bypass)
        {
            await next();
            return;
        }

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

        // Record what authorized this request so any audit the action stages names the capability.
        context.HttpContext.Items[StudioAuthorizationService.AuthorizedCapabilityItem] = capability;

        await next();
    }
}
