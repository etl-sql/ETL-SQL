using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Core.Governance;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.ReportPortal.Tests;

[Trait("Category", "Portal")]
public class PortalConnectionCatalogApiTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Endpoints_RejectAnonymousCallers()
    {
        using var factory = new CatalogFactory();
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/connections")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PutAsJsonAsync("/api/admin/connections/x", new { connectorType = "MSSQL" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/connections/export")).StatusCode);
    }

    [Fact]
    public async Task Lifecycle_SetVerifyDisableDeleteExportImport_WithAudit()
    {
        using var factory = new CatalogFactory();
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);

        // stage the secret the entry references
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<PortalSecretStoreService>()
                .StoreAsync("sales_db_password", "s3cret-value");
        }

        // create
        var set = await SendAsync(client, HttpMethod.Put, token, "/api/admin/connections/sales_dw", new
        {
            connectorType = "MSSQL",
            options = new Dictionary<string, string>
            {
                ["SERVER"] = "sql01",
                ["DATABASE"] = "Sales",
                ["PASSWORD"] = "SECRET:sales_db_password"
            },
            environmentScope = "Prod"
        });
        Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);

        // list + detail: reference visible, secret value never present
        var list = await SendAsync(client, HttpMethod.Get, token, "/api/admin/connections", null);
        Assert.Contains("sales_dw", await list.Content.ReadAsStringAsync());
        var detail = await SendAsync(client, HttpMethod.Get, token, "/api/admin/connections/sales_dw", null);
        var detailBody = await detail.Content.ReadAsStringAsync();
        Assert.Contains("SECRET:sales_db_password", detailBody);
        Assert.DoesNotContain("s3cret-value", detailBody);

        // verify resolves the SECRET: reference
        var verify = await SendAsync(client, HttpMethod.Post, token, "/api/admin/connections/sales_dw/verify", null);
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        var verifyBody = await verify.Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.Equal(1, verifyBody!["secretReferences"]!.GetValue<int>());

        // engine-facing provider resolves the definition and touches last-used
        var provider = factory.Services.GetRequiredService<IConnectionCatalogProvider>();
        Assert.Equal("PortalCatalog", provider.ProviderName);
        var definition = await provider.ResolveAsync("sales_dw");
        Assert.Equal("MSSQL", definition.ConnectorType);
        Assert.Equal("sql01", definition.Options["SERVER"]);
        using (var scope = factory.Services.CreateScope())
        {
            var entity = await scope.ServiceProvider.GetRequiredService<PortalDbContext>()
                .PortalSharedConnections.AsNoTracking().SingleAsync(c => c.Alias == "sales_dw");
            Assert.NotNull(entity.LastUsedAtUtc);
            Assert.NotNull(entity.LastVerifiedAtUtc);
        }

        // export → delete → import round-trip
        var export = await SendAsync(client, HttpMethod.Get, token, "/api/admin/connections/export", null);
        var exported = await export.Content.ReadFromJsonAsync<JsonArray>(Json);
        Assert.Single(exported!);

        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, HttpMethod.Delete, token, "/api/admin/connections/sales_dw", null)).StatusCode);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => provider.ResolveAsync("sales_dw"));

        var import = await SendAsync(client, HttpMethod.Post, token, "/api/admin/connections/import", exported);
        Assert.Equal(HttpStatusCode.OK, import.StatusCode);
        var reimported = await provider.ResolveAsync("sales_dw");
        Assert.Equal("SECRET:sales_db_password", reimported.Options["PASSWORD"]);

        // disable blocks resolution; enable restores it without re-supplying the definition
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, HttpMethod.Post, token, "/api/admin/connections/sales_dw/disable", null)).StatusCode);
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.ResolveAsync("sales_dw"));
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, HttpMethod.Post, token, "/api/admin/connections/sales_dw/enable", null)).StatusCode);
        Assert.Equal("sql01", (await provider.ResolveAsync("sales_dw")).Options["SERVER"]);
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, HttpMethod.Post, token, "/api/admin/connections/sales_dw/disable", null)).StatusCode);

        // audit trail
        using (var scope = factory.Services.CreateScope())
        {
            var actions = await scope.ServiceProvider.GetRequiredService<PortalDbContext>()
                .AuditLogs.Where(a => a.ResourceType == "PortalSharedConnection")
                .Select(a => a.Action).ToListAsync();
            Assert.Contains("SHARED_CONNECTION_CREATE", actions);
            Assert.Contains("SHARED_CONNECTION_VERIFY", actions);
            Assert.Contains("SHARED_CONNECTION_EXPORT", actions);
            Assert.Contains("SHARED_CONNECTION_IMPORT", actions);
            Assert.Contains("SHARED_CONNECTION_DELETE", actions);
            Assert.Contains("SHARED_CONNECTION_DISABLE", actions);
            Assert.Contains("SHARED_CONNECTION_ENABLE", actions);
        }
    }

    [Fact]
    public async Task Set_RejectsRawCredentialValues()
    {
        using var factory = new CatalogFactory();
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);

        var response = await SendAsync(client, HttpMethod.Put, token, "/api/admin/connections/bad", new
        {
            connectorType = "MSSQL",
            options = new Dictionary<string, string> { ["PASSWORD"] = "hunter2" }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("SECRET:name", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Detail_MasksNonReferenceCredentialValues()
    {
        using var factory = new CatalogFactory();
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);

        // Simulate a legacy/imported row that bypassed write-side validation.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            db.PortalSharedConnections.Add(new PortalSharedConnection
            {
                Alias = "legacy",
                ConnectorType = "MSSQL",
                Target = "Server=db;Password=raw-value",
                OptionsJson = """{"TOKEN":"raw-token"}"""
            });
            await db.SaveChangesAsync();
        }

        var detail = await SendAsync(client, HttpMethod.Get, token, "/api/admin/connections/legacy", null);
        var body = await detail.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.DoesNotContain("raw-value", body);
        Assert.DoesNotContain("raw-token", body);
        Assert.Contains("********", body);
    }

    private sealed class CatalogFactory : PortalWebFactory
    {
        protected override void CustomizeConfiguration(Dictionary<string, string?> settings)
        {
            settings["Governance:Secrets:Provider"] = "PortalStore";
            settings["Governance:ConnectionCatalog:Provider"] = "Portal";
        }
    }

    private static async Task<string> GetAdminTokenAsync(HttpClient client)
    {
        var initial = await LoginAsync(client, "admin", "Admin@12345!");
        var change = await SendAsync(client, HttpMethod.Post, initial.AccessToken, "/api/auth/change-password",
            new { currentPassword = "Admin@12345!", newPassword = "Admin@Tests99!" });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        return (await LoginAsync(client, "admin", "Admin@Tests99!")).AccessToken;
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
