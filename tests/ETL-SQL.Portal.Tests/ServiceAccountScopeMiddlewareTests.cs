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
    // Grants are administration whichever verb they arrive under — including GET, since listing an
    // object's grants requires MANAGE on it. Publish reaches them for objects it owns; admin for any.
    [InlineData("GET", "/api/orchestrator/authorization/JOB/nightly", ServiceAccountScopes.OrchestratorAdmin)]
    [InlineData("GET", "/api/orchestrator/authorization/JOB/nightly", ServiceAccountScopes.OrchestratorPublish)]
    [InlineData("PUT", "/api/orchestrator/authorization/JOB/nightly/GROUP/key", ServiceAccountScopes.OrchestratorAdmin)]
    [InlineData("DELETE", "/api/orchestrator/authorization/JOB/nightly/GROUP/key", ServiceAccountScopes.OrchestratorAdmin)]
    // Ownership is narrower still: an owner may manage their own object, so handing ownership on is
    // an administrator's act and publish does not reach it.
    [InlineData("GET", "/api/orchestrator/authorization/unowned", ServiceAccountScopes.OrchestratorAdmin)]
    [InlineData("PUT", "/api/orchestrator/authorization/JOB/nightly/owner", ServiceAccountScopes.OrchestratorAdmin)]
    [InlineData("POST", "/api/orchestrator/authorization/adopt", ServiceAccountScopes.OrchestratorAdmin)]
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

    [Theory]
    [InlineData("GET", "/api/orchestrator/authorization/JOB/nightly")]
    [InlineData("PUT", "/api/orchestrator/authorization/JOB/nightly/GROUP/key")]
    [InlineData("DELETE", "/api/orchestrator/authorization/JOB/nightly/GROUP/key")]
    public async Task ReadOrExecuteScope_DoesNotReachGrantAdministration(string method, string path)
    {
        // A monitoring account is issued the read rung precisely so it can look without changing
        // anything. Grants are the record of who may change things, and reading them requires MANAGE
        // on the object — so the refusal belongs here, naming the scope actually needed, rather than
        // arriving later as an unexplained 403 from the Orchestrator.
        var middleware = new ServiceAccountScopeMiddleware(_ => Task.CompletedTask);
        var read = Context(method, path, ServiceAccountScopes.OrchestratorRead);
        var execute = Context(method, path, ServiceAccountScopes.OrchestratorExecute);

        await middleware.InvokeAsync(read);
        await middleware.InvokeAsync(execute);

        Assert.Equal(StatusCodes.Status403Forbidden, read.Response.StatusCode);
        Assert.Equal(StatusCodes.Status403Forbidden, execute.Response.StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/orchestrator/authorization/unowned")]
    [InlineData("PUT", "/api/orchestrator/authorization/JOB/nightly/owner")]
    [InlineData("POST", "/api/orchestrator/authorization/adopt")]
    public async Task PublishScope_DoesNotReachOwnership(string method, string path)
    {
        // Publish means "MANAGE what you own". Ownership is the authority that comes from, so a token
        // that could reassign it could give itself an object — the escalation the split exists to stop.
        var middleware = new ServiceAccountScopeMiddleware(_ => Task.CompletedTask);
        var publish = Context(method, path, ServiceAccountScopes.OrchestratorPublish);

        await middleware.InvokeAsync(publish);

        Assert.Equal(StatusCodes.Status403Forbidden, publish.Response.StatusCode);
    }

    [Fact]
    public async Task WorkloadToken_IsBoundToExactResourcePathAndOperation()
    {
        var called = false;
        var middleware = new ServiceAccountScopeMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        });
        var allowed = WorkloadContext("POST", "/api/reports/7/execute",
            "/api/reports/7/execute", ServiceAccountScopes.ReportsExecute);
        await middleware.InvokeAsync(allowed);
        Assert.True(called);

        called = false;
        var otherResource = WorkloadContext("POST", "/api/reports/8/execute",
            "/api/reports/7/execute", ServiceAccountScopes.ReportsExecute);
        await middleware.InvokeAsync(otherResource);
        Assert.False(called);
        Assert.Equal(StatusCodes.Status403Forbidden, otherResource.Response.StatusCode);

        var otherOperation = WorkloadContext("POST", "/api/reports/7/execute",
            "/api/reports/7/execute", ServiceAccountScopes.OrchestratorExecute,
            tokenScope: ServiceAccountScopes.ReportsExecute);
        await middleware.InvokeAsync(otherOperation);
        Assert.Equal(StatusCodes.Status403Forbidden, otherOperation.Response.StatusCode);
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

    private static DefaultHttpContext WorkloadContext(
        string method, string path, string resource, string operation, string? tokenScope = null)
    {
        var context = Context(method, path, tokenScope ?? operation);
        context.User.AddIdentity(new ClaimsIdentity([
            new Claim(TokenService.WorkloadBindingClaim, "ci-main"),
            new Claim(TokenService.WorkloadResourceClaim, resource),
            new Claim(TokenService.WorkloadOperationClaim, operation)
        ]));
        return context;
    }
}
