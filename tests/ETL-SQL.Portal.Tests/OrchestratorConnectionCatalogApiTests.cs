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
    }

    [Fact]
    public async Task Lifecycle_SetVerifyDisableEnableDelete_UsesLocalCatalog()
    {
        var client = _factory.CreateClient();

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

        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, HttpMethod.Post, "/api/admin/connections/notify_smtp/disable", null)).StatusCode);
        var disabled = await SendAsync(client, HttpMethod.Get, "/api/admin/connections/notify_smtp", null);
        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
        var disabledBody = await disabled.Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.Equal("disabled", disabledBody!["status"]!.GetValue<string>());
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.ResolveAsync("notify_smtp"));

        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, HttpMethod.Post, "/api/admin/connections/notify_smtp/enable", null)).StatusCode);
        Assert.Equal("SECRET:smtp_password", (await provider.ResolveAsync("notify_smtp")).Options["PASSWORD"]);

        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, HttpMethod.Delete, "/api/admin/connections/notify_smtp", null)).StatusCode);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => provider.ResolveAsync("notify_smtp"));
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
}
