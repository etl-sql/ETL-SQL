using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.ReportPortal;
using ETL_SQL.ReportPortal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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

        var parse = await SendAsync(client, HttpMethod.Post, token, "/api/designer/parse", new { script = "" });
        var analyze = await SendAsync(client, HttpMethod.Post, token, "/api/designer/analyze", new { script = "SELECT * FROM #stage;" });
        var complete = await SendAsync(client, HttpMethod.Post, token, "/api/designer/complete", new { script = "SEL", line = 0, column = 3 });
        var run = await SendAsync(client, HttpMethod.Post, token, "/api/designer/run", new { script = "SELECT 1 AS One;" });
        var save = await SendAsync(client, HttpMethod.Post, token, "/api/designer/save", new { reportId = 1, scriptText = "SELECT 1;" });
        var schema = await SendAsync(client, HttpMethod.Get, token, "/api/designer/schema?connection=x", null);

        Assert.Equal(HttpStatusCode.NotFound, parse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, analyze.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, complete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, run.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, save.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, schema.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/designer.html")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/index.html")).StatusCode);
    }

    [Fact]
    public async Task Designer_Run_ExecutesReadOnlySelectAndAudits()
    {
        using var factory = new ModuleFenceFactory(_ => { });
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);

        var response = await SendAsync(client, HttpMethod.Post, token, "/api/designer/run", new
        {
            script = "SELECT 'ssn-123-45-6789' AS SensitiveValue, 1 AS One;",
            selection = "SELECT 'ssn-123-45-6789' AS SensitiveValue, 1 AS One;"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.Single(body!["rows"]!.AsArray());
        Assert.Equal(1, body["rows"]![0]!["One"]!.GetValue<int>());
        Assert.False(body["capped"]!.GetValue<bool>());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var audit = await db.AuditLogs.SingleAsync(a => a.Action == "AD_HOC_RUN");
        Assert.Contains("QueryHash=sha256:", audit.Detail);
        Assert.Contains("Statement=SelectStatement", audit.Detail);
        Assert.DoesNotContain("Query=", audit.Detail);
        Assert.DoesNotContain("ssn-123-45-6789", audit.Detail);
    }

    [Fact]
    public async Task Designer_Run_RejectsNonSelect()
    {
        using var factory = new ModuleFenceFactory(_ => { });
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);

        var response = await SendAsync(client, HttpMethod.Post, token, "/api/designer/run", new
        {
            script = "DELETE FROM #stage;"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("SELECT 1 AS Value INTO #written UNION SELECT 2 AS Value;")]
    [InlineData("SELECT 1 AS Value UNION SELECT 2 AS Value INTO #written;")]
    [InlineData("SELECT 1 AS Value UNION SELECT 2 AS Value INTERSECT SELECT 3 AS Value INTO #written;")]
    [InlineData("SELECT 1 AS Value EXCEPT SELECT 2 AS Value INTO #written;")]
    public async Task Designer_Run_RejectsNestedSelectInto(string script)
    {
        using var factory = new ModuleFenceFactory(_ => { });
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);

        var response = await SendAsync(client, HttpMethod.Post, token, "/api/designer/run", new { script });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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
