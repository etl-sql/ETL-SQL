using System.Net;
using System.Net.Http.Json;
using ETL_SQL.Core.Governance;
using Xunit;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// POST /jobs runs a caller-supplied script and /api/bundles publishes and reads catalog bundles.
/// In federated mode both accepted any signed caller: a read-scoped automation or a user with no
/// role could run a script or publish a bundle.
/// </summary>
[Trait("Category", "Portal")]
public class OrchestratorAdHocAndBundleRbacTests : IDisposable
{
    private const string ValidKey = "test-orch-key-12345";
    private readonly OrchestratorWebFactory _factory = new(requireFederatedIdentity: true);

    public void Dispose() => _factory.Dispose();

    private static readonly OrchestratorCaller NoRole = new("user", "nobody-1", "No Role", [], []);
    private static readonly OrchestratorCaller ReadOnlyUser = new("user", "ov-1", "Orchestrator Viewer", ["OrchestratorViewer"], []);
    private static readonly OrchestratorCaller ReadScopedService = new(
        "service", "svc-read", "Read automation", ["Admin"], [], null, ["orchestrator.read"]);
    private static readonly OrchestratorCaller ReportViewer = new("user", "viewer-1", "Vera Viewer", ["Viewer"], []);
    private static readonly OrchestratorCaller ExecuteService = new(
        "service", "svc-exec", "Run automation", [], [], null, ["orchestrator.execute"]);
    private static readonly OrchestratorCaller Manager = new("user", "mgr-1", "Mia Manager", ["OrchestratorManager"], []);

    [Fact]
    public async Task AdHocJob_RequiresAnExecutingRoleOrExecuteScope()
    {
        using var client = _factory.CreateClient();
        var job = new { scriptText = "SELECT 1 AS x;", label = "rbac" };

        foreach (var caller in new[] { NoRole, ReadOnlyUser, ReadScopedService })
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsAsync(client, caller, HttpMethod.Post, "/jobs", job)).StatusCode);

        // Report viewers run report executions through this endpoint as themselves.
        foreach (var caller in new[] { ReportViewer, ExecuteService, Manager })
            Assert.Equal(HttpStatusCode.Accepted, (await SendAsAsync(client, caller, HttpMethod.Post, "/jobs", job)).StatusCode);
    }

    [Fact]
    public async Task Bundles_PublishTakesAnAuthorAndReadTakesARole()
    {
        using var client = _factory.CreateClient();
        var publish = new
        {
            bundle = new
            {
                bundleName = "rbac-bundle",
                entryPath = "main.etlsql",
                files = new[] { new { path = "main.etlsql", content = "SELECT 1;" } }
            }
        };

        foreach (var caller in new[] { NoRole, ReportViewer, ReadScopedService, ExecuteService })
            Assert.Equal(HttpStatusCode.Forbidden,
                (await SendAsAsync(client, caller, HttpMethod.Post, "/api/bundles", publish)).StatusCode);

        foreach (var caller in new[] { NoRole })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsAsync(client, caller, HttpMethod.Get, "/api/bundles", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await SendAsAsync(client, caller, HttpMethod.Get, "/api/bundles/rbac-bundle/versions", null)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, (await SendAsAsync(client, ReportViewer, HttpMethod.Get, "/api/bundles", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsAsync(client, ReadScopedService, HttpMethod.Get, "/api/bundles", null)).StatusCode);
    }

    private static Task<HttpResponseMessage> SendAsAsync(
        HttpClient client, OrchestratorCaller caller, HttpMethod method, string uri, object? body)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Add("X-Orchestrator-Key", ValidKey);
        request.Headers.Add(
            OrchestratorIdentityAssertion.HeaderName,
            OrchestratorIdentityAssertion.Create(caller, OrchestratorWebFactory.IdentitySecret));
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return client.SendAsync(request);
    }
}
