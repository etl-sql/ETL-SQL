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
public class PortalSecretsApiTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Endpoints_RejectAnonymousCallers()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/secrets")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PutAsJsonAsync("/api/admin/secrets/x", new { value = "v" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsync("/api/admin/secrets/x/verify", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.DeleteAsync("/api/admin/secrets/x")).StatusCode);
    }

    [Fact]
    public async Task Lifecycle_SetListVerifyDisableDelete_NeverReturnsValue()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);
        const string secretValue = "sup3r-s3cret-value";

        // set
        var set = await SendAsync(client, HttpMethod.Put, token, "/api/admin/secrets/sales_db_password",
            new { value = secretValue });
        Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);

        // list shows metadata, not the value
        var list = await SendAsync(client, HttpMethod.Get, token, "/api/admin/secrets", null);
        var listBody = await list.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Contains("sales_db_password", listBody);
        Assert.DoesNotContain(secretValue, listBody);

        // verify ok
        var verify = await SendAsync(client, HttpMethod.Post, token, "/api/admin/secrets/sales_db_password/verify", null);
        var verifyBody = await verify.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        Assert.Contains("\"ok\"", verifyBody);
        Assert.DoesNotContain(secretValue, verifyBody);

        // verify-all reports zero failures
        var verifyAll = await SendAsync(client, HttpMethod.Post, token, "/api/admin/secrets/verify-all", null);
        Assert.Equal(HttpStatusCode.OK, verifyAll.StatusCode);
        var verifyAllBody = await verifyAll.Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.Equal(1, verifyAllBody!["secretCount"]!.GetValue<int>());
        Assert.Equal(0, verifyAllBody["failedCount"]!.GetValue<int>());

        // disable → verify conflicts
        var disable = await SendAsync(client, HttpMethod.Post, token, "/api/admin/secrets/sales_db_password/disable", null);
        Assert.Equal(HttpStatusCode.NoContent, disable.StatusCode);
        var verifyDisabled = await SendAsync(client, HttpMethod.Post, token, "/api/admin/secrets/sales_db_password/verify", null);
        Assert.Equal(HttpStatusCode.Conflict, verifyDisabled.StatusCode);

        // set again re-enables
        var reset = await SendAsync(client, HttpMethod.Put, token, "/api/admin/secrets/sales_db_password",
            new { value = "rotated-value" });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        var verifyAgain = await SendAsync(client, HttpMethod.Post, token, "/api/admin/secrets/sales_db_password/verify", null);
        Assert.Equal(HttpStatusCode.OK, verifyAgain.StatusCode);

        // delete → verify 404
        var delete = await SendAsync(client, HttpMethod.Delete, token, "/api/admin/secrets/sales_db_password", null);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        var verifyGone = await SendAsync(client, HttpMethod.Post, token, "/api/admin/secrets/sales_db_password/verify", null);
        Assert.Equal(HttpStatusCode.NotFound, verifyGone.StatusCode);

        // audit trail exists for the mutations and never contains the value
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var auditRows = await db.AuditLogs
            .Where(a => a.ResourceType == "PortalSecret")
            .Select(a => new { a.Action, a.Detail, a.ResourceId })
            .ToListAsync();
        Assert.Contains(auditRows, a => a.Action == "SECRET_SET");
        Assert.Contains(auditRows, a => a.Action == "SECRET_ROTATE");
        Assert.Contains(auditRows, a => a.Action == "SECRET_VERIFY");
        Assert.Contains(auditRows, a => a.Action == "SECRET_VERIFY_ALL");
        Assert.Contains(auditRows, a => a.Action == "SECRET_DISABLE");
        Assert.Contains(auditRows, a => a.Action == "SECRET_DELETE");
        Assert.DoesNotContain(auditRows, a =>
            (a.Detail ?? "").Contains(secretValue) || (a.ResourceId ?? "").Contains(secretValue));
    }

    [Fact]
    public async Task Set_RejectsInvalidNamesAndEmptyValues()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);

        var badName = await SendAsync(client, HttpMethod.Put, token, "/api/admin/secrets/bad%2Fname",
            new { value = "v" });
        Assert.Equal(HttpStatusCode.BadRequest, badName.StatusCode);

        var emptyValue = await SendAsync(client, HttpMethod.Put, token, "/api/admin/secrets/good_name",
            new { value = "" });
        Assert.Equal(HttpStatusCode.BadRequest, emptyValue.StatusCode);
    }

    [Fact]
    public async Task PortalStoreProvider_ResolvesFromStore_WhenConfigured()
    {
        using var factory = new PortalStoreProviderFactory();
        using var client = factory.CreateClient(); // forces host startup
        using var scope = factory.Services.CreateScope();

        var store = scope.ServiceProvider.GetRequiredService<PortalSecretStoreService>();
        await store.StoreAsync("provider_test", "resolved-by-provider");

        var provider = factory.Services.GetRequiredService<ISecretProvider>();
        Assert.Equal("PortalStore", provider.ProviderName);

        var result = await provider.ResolveAsync("provider_test");
        Assert.Equal("resolved-by-provider", result.Value);
        Assert.Equal("PortalStore", result.Provider);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => provider.ResolveAsync("missing_secret"));

        await store.DisableAsync("provider_test");
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.ResolveAsync("provider_test"));
    }

    private sealed class PortalStoreProviderFactory : PortalWebFactory
    {
        protected override void CustomizeConfiguration(Dictionary<string, string?> settings)
        {
            settings["Governance:Secrets:Provider"] = "PortalStore";
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
