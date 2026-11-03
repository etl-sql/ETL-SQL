using System.Net;
using System.Text;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Orchestrator;
using ETL_SQL.Connectors.Portal;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using Xunit;

namespace ETL_SQL.Tests.Connectors;

/// <summary>
/// Regression tests for the Portal and Orchestrator connector findings of the 2026-11-01
/// section 6 review: error bodies echoed unredacted, raw EnsureSuccessStatusCode failures, and
/// a WithTable view that disposed its parent's HttpClient.
/// </summary>
public sealed class PlatformConnectorReviewFixTests
{
    private const string LeakedBody = "{\"error\":\"denied\",\"password\":\"hunter2-secret\"}";

    private static SystemExecutionContext Ctx => SystemExecutionContext.Instance;

    private static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> responder, string baseUrl) =>
        new(new Handler(responder)) { BaseAddress = new Uri(baseUrl) };

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage PortalLoginOr(HttpRequestMessage request, Func<HttpResponseMessage> otherwise) =>
        request.RequestUri!.AbsolutePath.EndsWith("/api/auth/login", StringComparison.Ordinal)
            ? Json(HttpStatusCode.OK, "{\"token\":\"t\",\"expiresAt\":\"2099-01-01T00:00:00Z\"}")
            : otherwise();

    [Fact]
    public async Task Portal_ShowUsersServerError_IsAnExecutionExceptionWithARedactedBody()
    {
        await using var portal = new PortalDataSource(
            Client(r => PortalLoginOr(r, () => Json(HttpStatusCode.InternalServerError, LeakedBody)), "http://portal.test/"),
            "user", "pass", NullLogger.Instance);

        var ex = await Assert.ThrowsAsync<ExecutionException>(
            () => portal.ExecuteAdminStatementAsync(new ShowPortalUsersStatement(), Ctx));

        Assert.Contains("500", ex.Message);
        Assert.DoesNotContain("hunter2-secret", ex.Message);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public async Task Portal_LoginFailure_DoesNotEchoTheBody()
    {
        await using var portal = new PortalDataSource(
            Client(_ => Json(HttpStatusCode.Unauthorized, LeakedBody), "http://portal.test/"),
            "user", "pass", NullLogger.Instance);

        var ex = await Assert.ThrowsAsync<ExecutionException>(
            () => portal.ExecuteAdminStatementAsync(new ShowPortalUsersStatement(), Ctx));

        Assert.DoesNotContain("hunter2-secret", ex.Message);
    }

    [Fact]
    public async Task Portal_UnreachableServer_DoesNotChainTheTransportException()
    {
        await using var portal = new PortalDataSource("http://127.0.0.1:1", "user", "pass", NullLogger.Instance);

        var ex = await Assert.ThrowsAsync<ExecutionException>(
            () => portal.ExecuteAdminStatementAsync(new ShowPortalUsersStatement(), Ctx));

        Assert.Null(ex.InnerException);
    }

    [Fact]
    public async Task Portal_DisposingAWithTableView_LeavesTheParentConnectionUsable()
    {
        var calls = 0;
        var http = Client(r => PortalLoginOr(r, () =>
        {
            calls++;
            return Json(HttpStatusCode.OK, "[]");
        }), "http://portal.test/");
        await using var portal = new PortalDataSource(http, "user", "pass", NullLogger.Instance);

        await portal.WithTable("eng.reports").DisposeAsync();
        await portal.ExecuteAdminStatementAsync(new ShowPortalUsersStatement(), Ctx);

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Orchestrator_ApiError_RedactsTheBody()
    {
        await using var orchestrator = new OrchestratorDataSource(
            Client(_ => Json(HttpStatusCode.BadRequest, LeakedBody), "http://orchestrator.test/"),
            "key", NullLogger.Instance);

        var ex = await Assert.ThrowsAsync<ExecutionException>(
            () => orchestrator.ExecuteAdminStatementAsync(new TriggerJobStatement("nightly"), Ctx));

        Assert.Contains("400", ex.Message);
        Assert.DoesNotContain("hunter2-secret", ex.Message);
    }

    [Fact]
    public async Task Orchestrator_UnreachableServer_DoesNotChainTheTransportException()
    {
        await using var orchestrator = new OrchestratorDataSource("http://127.0.0.1:1", "key", NullLogger.Instance);

        var ex = await Assert.ThrowsAsync<ExecutionException>(
            () => orchestrator.ExecuteAdminStatementAsync(new ShowJobsStatement(), Ctx));

        Assert.Null(ex.InnerException);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
