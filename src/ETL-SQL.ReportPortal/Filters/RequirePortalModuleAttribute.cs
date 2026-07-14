using ETL_SQL.ReportPortal.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ETL_SQL.ReportPortal.Filters;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RequirePortalModuleAttribute(string moduleName) : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var modules = context.HttpContext.RequestServices.GetRequiredService<PortalModuleRegistry>();
        if (!modules.IsEnabled(moduleName))
        {
            context.Result = new NotFoundObjectResult(new
            {
                error = $"Portal module '{moduleName}' is disabled."
            });
            return;
        }

        await next();
    }
}
