using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.ReportPortal;

namespace ETL_SQL.ReportPortal.Tests;

[Trait("Category", "Portal")]
public class PortalModuleRouteFencingTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Disabled_Reporting_Module_Hides_Reporting_Api_Routes()
    {
        using var factory = new ModuleFenceFactory(config => config.Modules.Reporting = false);
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, token, "/api/reports/1", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, token, "/api/catalog/search?q=x", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, token, "/api/datasets", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, token, "/api/subscriptions", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, token, "/api/reports/1/export/csv", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/index.html")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/login.html")).StatusCode);
    }

    [Fact]
    public async Task Disabled_Designer_Module_Hides_Designer_Api_Routes()
    {
        using var factory = new ModuleFenceFactory(config => config.Modules.Designer = false);
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);

        var response = await SendAsync(client, HttpMethod.Post, token, "/api/designer/parse", new { script = "" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/designer.html")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/index.html")).StatusCode);
    }

    [Fact]
    public async Task Disabled_Module_Does_Not_Replace_Authentication_Challenge()
    {
        using var factory = new ModuleFenceFactory(config => config.Modules.Reporting = false);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/reports/1");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed class ModuleFenceFactory(Action<PortalConfig> customize) : PortalWebFactory
    {
        protected override void CustomizePortalConfig(PortalConfig config) => customize(config);
    }

    private static async Task<string> GetAdminTokenAsync(HttpClient client)
    {
        var initial = await LoginAsync(client, "admin", "Admin@12345!");
        var change = await SendAsync(client, HttpMethod.Post, initial.AccessToken, "/api/auth/change-password",
            new { currentPassword = "Admin@12345!", newPassword = "Admin@ModuleFence99!" });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        return (await LoginAsync(client, "admin", "Admin@ModuleFence99!")).AccessToken;
    }

    private static async Task<(string AccessToken, string RefreshToken)> LoginAsync(
        HttpClient client, string username, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(Json);
        return (body!["token"]!.GetValue<string>(), body["refreshToken"]!.GetValue<string>());
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method,
        string token, string url, object? body)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }
}
