using System.Net;
using System.Net.Http.Json;
using ETL_SQL.Core.Governance;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// <c>POST /api/notifications/{name}/dispatch</c>: it dispatches the notification it authorized, and
/// its attachment handling does not tell a caller which host files exist.
/// </summary>
[Trait("Category", "Portal")]
public sealed class OrchestratorNotificationDispatchApiTests
{
    private const string ApiKey = "test-orch-key-12345";

    [Fact]
    public async Task Dispatch_SendsTheTenantsOwnNotification_NotTheUnboundOneOfTheSameName()
    {
        using var factory = new OrchestratorWebFactory(requireFederatedIdentity: true);
        using var client = factory.CreateClient();
        var tenantAdmin = new OrchestratorCaller("user", "admin-a", "Tenant admin", ["Admin"], [], "tenant-a");

        using (var create = Request(HttpMethod.Post, "/api/notifications", tenantAdmin,
                   new { name = "alerts", connectionName = "missing_smtp", recipient = "ops@example.test" }))
            Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(create)).StatusCode);

        // The tenant's notification is enabled and names a connection that does not exist, so its
        // dispatch fails on the connection. Resolved again by name in the unbound scope, there is no
        // such notification and the dispatch was skipped instead.
        using var dispatch = Request(HttpMethod.Post, "/api/notifications/alerts/dispatch", tenantAdmin,
            new { title = "Disk", text = "Disk is full." });
        var response = await client.SendAsync(dispatch);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Contains("connection is unavailable", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("does not exist", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Dispatch_AnswersAForbiddenAttachmentPathTheSameWhetherOrNotTheFileExists()
    {
        using var factory = new OrchestratorWebFactory();
        using var client = factory.CreateClient();
        using (var create = LegacyRequest(HttpMethod.Post, "/api/notifications",
                   new { name = "ops_mail", connectionName = "missing_smtp", recipient = "ops@example.test" }))
            Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(create)).StatusCode);

        var (existing, missing) = OperatingSystem.IsWindows()
            ? (@"C:\Windows\System32\drivers\etc\hosts", @"C:\Windows\System32\drivers\etc\etlsql-no-such-file")
            : ("/etc/passwd", "/etc/etlsql-no-such-file");

        async Task<(HttpStatusCode Status, string Body)> DispatchWithAsync(string path)
        {
            using var request = LegacyRequest(HttpMethod.Post, "/api/notifications/ops_mail/dispatch", new
            {
                title = "Report",
                text = "Attached.",
                recipientOverride = "someone@example.test",
                attachmentPaths = new[] { path }
            });
            var response = await client.SendAsync(request);
            return (response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        var forExisting = await DispatchWithAsync(existing);
        var forMissing = await DispatchWithAsync(missing);

        Assert.Equal(HttpStatusCode.BadRequest, forExisting.Status);
        Assert.Equal(forExisting, forMissing);
        Assert.DoesNotContain("does not exist", forMissing.Body, StringComparison.OrdinalIgnoreCase);
    }

    private static HttpRequestMessage Request(
        HttpMethod method, string path, OrchestratorCaller caller, object? body = null)
    {
        var request = LegacyRequest(method, path, body);
        request.Headers.Add(
            OrchestratorIdentityAssertion.HeaderName,
            OrchestratorIdentityAssertion.Create(caller, OrchestratorWebFactory.IdentitySecret));
        return request;
    }

    private static HttpRequestMessage LegacyRequest(HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Orchestrator-Key", ApiKey);
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }
}
