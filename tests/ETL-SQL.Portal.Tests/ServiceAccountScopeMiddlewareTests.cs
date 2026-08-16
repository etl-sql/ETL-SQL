using System.Security.Claims;
using ETL_SQL.Portal.Middleware;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Http;

namespace ETL_SQL.Portal.Tests;

public sealed class ServiceAccountScopeMiddlewareTests
{
    [Theory]
    [InlineData("POST", "/api/reports/7/execute", ServiceAccountScopes.ReportsExecute)]
    [InlineData("POST", "/api/reports/7/refresh", ServiceAccountScopes.ReportsExecute)]
    [InlineData("POST", "/api/datasets/4/refresh", ServiceAccountScopes.ReportsExecute)]
    [InlineData("DELETE", "/api/jobs/job-id", ServiceAccountScopes.ReportsExecute)]
    // The orchestrator ladder: defining what runs is publication, running it is execution, looking is
    // reading, and stopping the service sits with grant administration.
    [InlineData("POST", "/api/orchestrator/jobs", ServiceAccountScopes.OrchestratorPublish)]
    [InlineData("PUT", "/api/orchestrator/jobs/nightly", ServiceAccountScopes.OrchestratorPublish)]
    [InlineData("DELETE", "/api/orchestrator/jobs/nightly", ServiceAccountScopes.OrchestratorPublish)]
    [InlineData("POST", "/api/orchestrator/jobs/nightly/trigger", ServiceAccountScopes.OrchestratorExecute)]
    [InlineData("POST", "/api/orchestrator/jobs/nightly/kill", ServiceAccountScopes.OrchestratorExecute)]
    [InlineData("POST", "/api/orchestrator/runs/42/resume", ServiceAccountScopes.OrchestratorExecute)]
    [InlineData("GET", "/api/orchestrator/jobs", ServiceAccountScopes.OrchestratorRead)]
    [InlineData("POST", "/api/orchestrator/service/stop", ServiceAccountScopes.OrchestratorAdmin)]
    [InlineData("GET", "/api/folders", ServiceAccountScopes.PortalRead)]
    public async Task RequiredScope_AllowsSupportedOperation(string method, string path, string scope)
    {
        var called = false;
        var middleware = new ServiceAccountScopeMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        });
        var context = Context(method, path, scope);

        await middleware.InvokeAsync(context);

        Assert.True(called);
    }

    [Fact]
    public async Task ExecuteScope_DoesNotGrantPortalReadsOrAdministration()
    {
        var middleware = new ServiceAccountScopeMiddleware(_ => Task.CompletedTask);
        var read = Context("GET", "/api/folders", ServiceAccountScopes.ReportsExecute);
        var admin = Context("GET", "/api/admin/service-accounts", ServiceAccountScopes.PortalRead);

        await middleware.InvokeAsync(read);
        await middleware.InvokeAsync(admin);

        Assert.Equal(StatusCodes.Status403Forbidden, read.Response.StatusCode);
        Assert.Equal(StatusCodes.Status403Forbidden, admin.Response.StatusCode);
    }

    private static DefaultHttpContext Context(string method, string path, string scope)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(TokenService.IdentityTypeClaim, TokenService.ServiceIdentityType),
            new Claim(TokenService.ScopeClaim, scope)
        ], "test"));
        context.Response.Body = new MemoryStream();
        return context;
    }
}
