using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Portal;
using ETL_SQL.Portal.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Portal")]
public class PortalModuleTopologyCertificationTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Gateway_Profile_Serves_Secret_And_Connection_Catalog_Without_Reporting_Workers()
    {
        using var factory = CreateFactory(config =>
        {
            config.Modules.Reporting = false;
            config.Modules.Designer = false;
            config.Modules.Scheduling = false;
            config.Modules.Operations = false;
            config.Modules.ConnectionCatalog = true;
            config.Modules.SecretStore = true;
        });
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Get, token, "/api/admin/secrets", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Get, token, "/api/admin/connections", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, token, "/api/reports/1", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Post, token, "/api/designer/parse", new { script = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/index.html")).StatusCode);

        var hostedTypes = HostedTypes(factory);
        Assert.DoesNotContain(typeof(SessionCache), hostedTypes);
        Assert.DoesNotContain(typeof(ExecutionJobService), hostedTypes);
        Assert.DoesNotContain(typeof(OrchestratorPollerService), hostedTypes);
        Assert.DoesNotContain(typeof(OperationalMetricsDigestService), hostedTypes);
        Assert.Contains(typeof(JwtSecretValidationService), hostedTypes);
        Assert.Contains(typeof(AuditOutboxTransportService), hostedTypes);
    }

    [Fact]
    public async Task Reporting_Node_Profile_Serves_Report_Player_Without_Catalog_Secret_Or_Admin_Workers()
    {
        using var factory = CreateFactory(config =>
        {
            config.Modules.Reporting = true;
            config.Modules.Designer = false;
            config.Modules.Scheduling = false;
            config.Modules.Operations = false;
            config.Modules.ConnectionCatalog = false;
            config.Modules.SecretStore = false;
        });
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/index.html")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/designer.html")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, token, "/api/admin/secrets", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, token, "/api/admin/connections", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Post, token, "/api/designer/parse", new { script = "" })).StatusCode);

        var hostedTypes = HostedTypes(factory);
        Assert.Contains(typeof(SessionCache), hostedTypes);
        Assert.Contains(typeof(ExecutionJobService), hostedTypes);
        Assert.DoesNotContain(typeof(OrchestratorPollerService), hostedTypes);
        Assert.DoesNotContain(typeof(OperationalMetricsDigestService), hostedTypes);
        Assert.DoesNotContain(typeof(FailureDigestAdminService), hostedTypes);
    }

    private static HashSet<Type> HostedTypes(PortalWebFactory factory) =>
        factory.Services.GetServices<IHostedService>()
            .Select(service => service.GetType())
            .ToHashSet();

    private static HostedPortalFactory CreateFactory(Action<PortalConfig> customize) =>
        new(settings: settings =>
        {
            settings["Governance:Secrets:Provider"] = "PortalStore";
            settings["Governance:ConnectionCatalog:Provider"] = "Portal";
            ApplyModuleSettings(settings, customize);
        }, portalConfig: customize);

    private static void ApplyModuleSettings(Dictionary<string, string?> settings, Action<PortalConfig> customize)
    {
        var config = new PortalConfig();
        customize(config);
        settings["Portal:Modules:Reporting"] = config.Modules.Reporting.ToString();
        settings["Portal:Modules:Designer"] = config.Modules.Designer.ToString();
        settings["Portal:Modules:ConnectionCatalog"] = config.Modules.ConnectionCatalog.ToString();
        settings["Portal:Modules:SecretStore"] = config.Modules.SecretStore.ToString();
        settings["Portal:Modules:Scheduling"] = config.Modules.Scheduling.ToString();
        settings["Portal:Modules:Operations"] = config.Modules.Operations.ToString();
        settings["Portal:Modules:Documentation"] = config.Modules.Documentation.ToString();
    }

    private static async Task<string> GetAdminTokenAsync(HttpClient client)
    {
        var initial = await LoginAsync(client, "admin", "Admin@12345!");
        var change = await SendAsync(client, HttpMethod.Post, initial.AccessToken, "/api/auth/change-password",
            new { currentPassword = "Admin@12345!", newPassword = "Admin@Topology99!" });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        return (await LoginAsync(client, "admin", "Admin@Topology99!")).AccessToken;
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
