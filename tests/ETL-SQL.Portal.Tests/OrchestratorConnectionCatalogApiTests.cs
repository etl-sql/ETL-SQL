using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Core.Governance;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Portal")]
public class OrchestratorConnectionCatalogApiTests : IDisposable
{
    private const string ValidKey = "test-orch-key-12345";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly OrchestratorWebFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Endpoints_RejectAnonymousCallers()
    {
        var client = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/connections")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PutAsJsonAsync("/api/admin/connections/x", new { connectorType = "MSSQL" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.DeleteAsync("/api/admin/connections/x")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/connections/export")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/admin/connections/import", Array.Empty<object>())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/connections/x/impact")).StatusCode);
    }

    [Fact]
    public async Task Lifecycle_SetVerifyDisableEnableDelete_UsesLocalCatalog()
    {
        var client = _factory.CreateClient();
        var securityEvents = new RecordingSecurityEventSink();
        using var securityEventScope = new SecurityEventSinkScope(securityEvents);

        var raw = await SendAsync(client, HttpMethod.Put, "/api/admin/connections/raw_smtp", new
        {
            connectorType = "SMTP",
            options = new Dictionary<string, string>
            {
                ["HOST"] = "smtp.example.test",
                ["PASSWORD"] = "plain-text"
            }
        });
        Assert.Equal(HttpStatusCode.BadRequest, raw.StatusCode);

        var set = await SendAsync(client, HttpMethod.Put, "/api/admin/connections/notify_smtp", new
        {
            connectorType = "SMTP",
            options = new Dictionary<string, string>
            {
                ["HOST"] = "smtp.example.test",
                ["USER"] = "etl",
                ["PASSWORD"] = "SECRET:smtp_password"
            }
        });
        Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);

        var list = await SendAsync(client, HttpMethod.Get, "/api/admin/connections", null);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var listed = await list.Content.ReadFromJsonAsync<JsonArray>(Json);
        var entry = Assert.Single(listed!);
        Assert.Equal("notify_smtp", entry!["alias"]!.GetValue<string>());
        Assert.Equal("SMTP", entry["connectorType"]!.GetValue<string>());
        Assert.Equal("active", entry["status"]!.GetValue<string>());
        Assert.Contains("SECRET:smtp_password", entry.ToJsonString());

        var provider = _factory.Services.GetRequiredService<IConnectionCatalogProvider>();
        var definition = await provider.ResolveAsync("notify_smtp");
        Assert.Equal("smtp.example.test", definition.Options["HOST"]);
        Assert.Equal("SECRET:smtp_password", definition.Options["PASSWORD"]);

        var job = await SendAsync(client, HttpMethod.Post, "/api/scheduled-jobs", new
        {
            name = "notify-impact-job",
            scriptText = "CREATE CONNECTION smtp AS SMTP('SHARED:notify_smtp');",
            interval = 1,
            unit = "HOUR"
        });
        Assert.Equal(HttpStatusCode.Created, job.StatusCode);

        var impact = await SendAsync(client, HttpMethod.Get, "/api/admin/connections/notify_smtp/impact", null);
        Assert.Equal(HttpStatusCode.OK, impact.StatusCode);
        var impactBody = await impact.Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.Equal("SHARED:notify_smtp", impactBody!["reference"]!.GetValue<string>());
        Assert.Equal(1, impactBody["consumerCount"]!.GetValue<int>());
        Assert.Equal("notify-impact-job",
            impactBody["consumers"]!.AsArray()[0]!["name"]!.GetValue<string>());

        var mockSet = await SendAsync(client, HttpMethod.Put, "/api/admin/connections/mock_conn", new
        {
            connectorType = "MOCKDB",
            options = new Dictionary<string, string>()
        });
        Assert.Equal(HttpStatusCode.NoContent, mockSet.StatusCode);
        var test = await SendAsync(client, HttpMethod.Post, "/api/admin/connections/mock_conn/test", null);
        Assert.Equal(HttpStatusCode.OK, test.StatusCode);
        var testBody = await test.Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.Equal("mock_conn", testBody!["alias"]!.GetValue<string>());
        Assert.NotEmpty(testBody["steps"]!.AsArray());

        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, HttpMethod.Post, "/api/admin/connections/notify_smtp/disable", null)).StatusCode);
        var disabled = await SendAsync(client, HttpMethod.Get, "/api/admin/connections/notify_smtp", null);
        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
        var disabledBody = await disabled.Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.Equal("disabled", disabledBody!["status"]!.GetValue<string>());
        Assert.Equal("SMTP", disabledBody["connectorType"]!.GetValue<string>());
        Assert.Equal("SECRET:smtp_password",
            disabledBody["options"]!["PASSWORD"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.Conflict,
            (await SendAsync(client, HttpMethod.Post, "/api/admin/connections/notify_smtp/test", null)).StatusCode);
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.ResolveAsync("notify_smtp"));

        var disabledExport = await SendAsync(client, HttpMethod.Get, "/api/admin/connections/export", null);
        Assert.Equal(HttpStatusCode.OK, disabledExport.StatusCode);
        var disabledExported = await disabledExport.Content.ReadFromJsonAsync<JsonArray>(Json);
        var disabledEntry = Assert.Single(disabledExported!, e => e!["alias"]!.GetValue<string>() == "notify_smtp");
        Assert.Equal("disabled", disabledEntry!["status"]!.GetValue<string>());
        Assert.Equal("SECRET:smtp_password", disabledEntry["options"]!["PASSWORD"]!.GetValue<string>());

        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, HttpMethod.Post, "/api/admin/connections/notify_smtp/enable", null)).StatusCode);
        Assert.Equal("SECRET:smtp_password", (await provider.ResolveAsync("notify_smtp")).Options["PASSWORD"]);

        var export = await SendAsync(client, HttpMethod.Get, "/api/admin/connections/export", null);
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        var exported = await export.Content.ReadFromJsonAsync<JsonArray>(Json);
        Assert.Contains(exported!, e => e!["alias"]!.GetValue<string>() == "notify_smtp");

        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, HttpMethod.Delete, "/api/admin/connections/notify_smtp", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, HttpMethod.Delete, "/api/admin/connections/mock_conn", null)).StatusCode);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => provider.ResolveAsync("notify_smtp"));

        var import = await SendAsync(client, HttpMethod.Post, "/api/admin/connections/import", exported);
        Assert.Equal(HttpStatusCode.OK, import.StatusCode);
        Assert.Equal("SECRET:smtp_password", (await provider.ResolveAsync("notify_smtp")).Options["PASSWORD"]);

        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, HttpMethod.Delete, "/api/admin/connections/notify_smtp", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await SendAsync(client, HttpMethod.Post, "/api/admin/connections/import", disabledExported)).StatusCode);
        Assert.Equal(SecretLifecycleStatus.Disabled,
            await ((IWritableConnectionCatalogProvider)provider).GetStatusAsync("notify_smtp"));
        var reimportedDisabled = await SendAsync(client, HttpMethod.Get, "/api/admin/connections/notify_smtp", null);
        var reimportedDisabledBody = await reimportedDisabled.Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.Equal("SECRET:smtp_password",
            reimportedDisabledBody!["options"]!["PASSWORD"]!.GetValue<string>());

        AssertConnectionEvent(securityEvents, SecurityEventType.OperationDenied, "SHARED_CONNECTION_WRITE_DENIED", "SHARED_CONNECTION:raw_smtp");
        AssertConnectionEvent(securityEvents, SecurityEventType.CatalogMutation, "SHARED_CONNECTION_CREATE", "SHARED_CONNECTION:notify_smtp");
        AssertConnectionEvent(securityEvents, SecurityEventType.CatalogMutation, "SHARED_CONNECTION_CREATE", "SHARED_CONNECTION:mock_conn");
        AssertConnectionEvent(securityEvents, SecurityEventType.CatalogMutation, "SHARED_CONNECTION_TEST", "SHARED_CONNECTION:mock_conn");
        AssertConnectionEvent(securityEvents, SecurityEventType.CatalogMutation, "SHARED_CONNECTION_IMPACT", "SHARED_CONNECTION:notify_smtp");
        AssertConnectionEvent(securityEvents, SecurityEventType.CatalogMutation, "SHARED_CONNECTION_DISABLE", "SHARED_CONNECTION:notify_smtp");
        AssertConnectionEvent(securityEvents, SecurityEventType.OperationDenied, "SHARED_CONNECTION_TEST_DENIED", "SHARED_CONNECTION:notify_smtp");
        AssertConnectionEvent(securityEvents, SecurityEventType.CatalogMutation, "SHARED_CONNECTION_EXPORT", "SHARED_CONNECTION:*");
        AssertConnectionEvent(securityEvents, SecurityEventType.CatalogMutation, "SHARED_CONNECTION_ENABLE", "SHARED_CONNECTION:notify_smtp");
        AssertConnectionEvent(securityEvents, SecurityEventType.CatalogMutation, "SHARED_CONNECTION_DELETE", "SHARED_CONNECTION:notify_smtp");
        AssertConnectionEvent(securityEvents, SecurityEventType.CatalogMutation, "SHARED_CONNECTION_IMPORT", "SHARED_CONNECTION:*");
        Assert.DoesNotContain(securityEvents.Events, e =>
            e.Reason.Contains("SECRET:smtp_password", StringComparison.OrdinalIgnoreCase)
            || e.Reason.Contains("plain-text", StringComparison.OrdinalIgnoreCase)
            || e.SanitizedTarget.Contains("SECRET:", StringComparison.OrdinalIgnoreCase));
    }

    private static Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string uri,
        object? body)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Add("X-Orchestrator-Key", ValidKey);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return client.SendAsync(request);
    }

    private static void AssertConnectionEvent(
        RecordingSecurityEventSink sink,
        SecurityEventType type,
        string action,
        string target)
    {
        Assert.Contains(sink.Events, e =>
            e.Type == type
            && e.SanitizedTarget == target
            && e.Reason.Contains(action, StringComparison.Ordinal));
    }

    private sealed class SecurityEventSinkScope : IDisposable
    {
        private readonly ISecurityEventSink _previous;

        public SecurityEventSinkScope(ISecurityEventSink sink)
        {
            _previous = SecurityEventRuntime.Sink;
            SecurityEventRuntime.Sink = sink;
        }

        public void Dispose() => SecurityEventRuntime.Sink = _previous;
    }

    private sealed class RecordingSecurityEventSink : ISecurityEventSink
    {
        public List<SecurityEvent> Events { get; } = [];

        public void Emit(SecurityEvent securityEvent) => Events.Add(securityEvent);
    }
}
