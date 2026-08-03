using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Audit-delivery signals were already emitted through health, Prometheus, and fleet status — fine
/// for a dashboard, no use to someone mid-incident deciding whether to raise a threshold or fix the
/// collector. These cover the operator view: what is queued, how old it is, and whether the
/// fail-closed policy is currently refusing mutations.
/// </summary>
[Trait("Category", "Portal")]
public sealed class AuditCollectorHealthTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task ReportsQueueDepthAgeAndThresholds()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);

        await SeedOutboxAsync(factory, status: "Pending", age: TimeSpan.FromMinutes(30));
        await SeedOutboxAsync(factory, status: "Pending", age: TimeSpan.FromMinutes(5));
        await SeedOutboxAsync(factory, status: "Failed", age: TimeSpan.FromHours(2), error: "collector refused");

        var health = await HealthAsync(client, adminToken);

        Assert.True(health["pending"]!.GetValue<int>() >= 2);
        Assert.Equal(1, health["failed"]!.GetValue<int>());
        Assert.True(health["pendingBytes"]!.GetValue<long>() > 0);

        // Age, not just depth: a small queue that has not moved for an hour is the worse signal.
        Assert.True(health["oldestPendingAgeSeconds"]!.GetValue<int>() >= 1700);

        // The thresholds ship with the reading, so a number can actually be interpreted.
        var thresholds = health["thresholds"]!.AsObject();
        Assert.True(thresholds["failClosedMaxPendingBacklog"]!.GetValue<int>() > 0);
        Assert.True(thresholds["failClosedMaxBacklogSeconds"]!.GetValue<int>() > 0);

        Assert.Equal("collector refused", health["lastError"]!.GetValue<string>());
    }

    [Fact]
    public async Task ReportsFailClosedStateFromTheGateItself()
    {
        // Remote delivery required, and a terminally failed row — the condition the gate trips on.
        using var factory = new RequiredDeliveryFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);

        var healthy = await HealthAsync(client, adminToken);
        Assert.True(healthy["remoteDeliveryRequired"]!.GetValue<bool>());
        Assert.False(healthy["failClosed"]!["tripped"]!.GetValue<bool>());

        await SeedOutboxAsync(factory, status: "Failed", age: TimeSpan.FromMinutes(1), error: "gone");

        var tripped = await HealthAsync(client, adminToken);
        var failClosed = tripped["failClosed"]!.AsObject();
        Assert.True(failClosed["tripped"]!.GetValue<bool>());
        // The reason is the gate's own message, so what is reported is what would actually happen.
        Assert.False(string.IsNullOrWhiteSpace(failClosed["reason"]!.GetValue<string>()));
        Assert.Contains("503", failClosed["explanation"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithNoCollectorConfigured_SaysMutationsAreNeverBlocked()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);

        var health = await HealthAsync(client, adminToken);

        Assert.False(health["collectorConfigured"]!.GetValue<bool>());
        Assert.Null(health["collectorEndpoint"]?.GetValue<string?>());
        Assert.False(health["failClosed"]!["tripped"]!.GetValue<bool>());
        Assert.Contains("never blocked",
            health["failClosed"]!["explanation"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestDelivery_ReportsFailureWithoutLeakingTheEndpointQueryOrToken()
    {
        using var factory = new RequiredDeliveryFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);

        var response = await AuthPost(client, adminToken, "/api/admin/audit/collector/test-delivery", new { });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonObject>(Json);

        // The collector host does not exist, so the probe fails — and says so plainly.
        Assert.False(result!["delivered"]!.GetValue<bool>());
        Assert.False(string.IsNullOrWhiteSpace(result["error"]!.GetValue<string>()));

        // The endpoint is echoed without its query string, which carries the token here.
        var raw = result.ToJsonString();
        Assert.DoesNotContain("collector-token-value", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("access_token", raw, StringComparison.Ordinal);
        Assert.Contains("collector.example.invalid", result["endpoint"]!.GetValue<string>(), StringComparison.Ordinal);

        // Reaching out to a configured external endpoint on demand is worth recording.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        Assert.True(await db.AuditLogs.AnyAsync(log => log.Action == "TEST_AUDIT_COLLECTOR_DELIVERY"));
    }

    [Fact]
    public async Task IsAdministratorOnly()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await CreateViewerAsync(client, adminToken, $"col_deny_{suffix}");
        var viewerToken = await LoginAsync(client, $"col_deny_{suffix}", "Ready@Test2!");

        Assert.Equal(HttpStatusCode.Forbidden,
            (await AuthGet(client, viewerToken, "/api/admin/audit/collector")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await AuthPost(client, viewerToken, "/api/admin/audit/collector/test-delivery", new { })).StatusCode);
    }

    /// <summary>A portal that requires remote delivery, with an unreachable collector.</summary>
    private sealed class RequiredDeliveryFactory : PortalWebFactory
    {
        protected override void CustomizePortalConfig(PortalConfig config)
        {
            config.Audit.TransportEndpoint =
                "https://collector.example.invalid/ingest?access_token=collector-token-value";
            config.Audit.TransportBearerToken = "collector-token-value";
            config.Audit.RequireRemoteDelivery = true;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static async Task SeedOutboxAsync(
        PortalWebFactory factory, string status, TimeSpan age, string? error = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var occurred = DateTime.UtcNow - age;
        db.AuditOutboxMessages.Add(new AuditOutboxMessage
        {
            Action = "SEEDED_EVENT",
            ResourceType = "Test",
            OccurredAt = occurred,
            CreatedAt = occurred,
            UpdatedAt = occurred,
            Status = status,
            LastError = error,
            NextAttemptAt = error is null ? null : occurred,
            PayloadJson = """{"seeded":true}"""
        });
        await db.SaveChangesAsync();
    }

    private static async Task<JsonObject> HealthAsync(HttpClient client, string adminToken)
    {
        var response = await AuthGet(client, adminToken, "/api/admin/audit/collector");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>(Json))!;
    }

    private static async Task CreateViewerAsync(HttpClient client, string adminToken, string username)
    {
        var create = await AuthPost(client, adminToken, "/api/admin/users", new
        {
            username,
            email = $"{username}@test.local",
            password = "Initial@Test1!",
            role = "Viewer"
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var initial = await LoginAsync(client, username, "Initial@Test1!");
        Assert.Equal(HttpStatusCode.NoContent,
            (await AuthPost(client, initial, "/api/auth/change-password",
                new { currentPassword = "Initial@Test1!", newPassword = "Ready@Test2!" })).StatusCode);
    }

    private static async Task<string> GetAdminTokenAsync(HttpClient client)
    {
        var initial = await LoginAsync(client, "admin", "Admin@12345!");
        Assert.Equal(HttpStatusCode.NoContent,
            (await AuthPost(client, initial, "/api/auth/change-password",
                new { currentPassword = "Admin@12345!", newPassword = "Admin@Tests99!" })).StatusCode);
        return await LoginAsync(client, "admin", "Admin@Tests99!");
    }

    private static async Task<string> LoginAsync(HttpClient client, string username, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>(Json))!["token"]!.GetValue<string>();
    }

    private static Task<HttpResponseMessage> AuthGet(HttpClient client, string token, string url) =>
        SendAsync(client, HttpMethod.Get, token, url, null);

    private static Task<HttpResponseMessage> AuthPost(HttpClient client, string token, string url, object body) =>
        SendAsync(client, HttpMethod.Post, token, url, body);

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string token, string url, object? body)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        await IfMatchVersioning.StampAsync(client, request, token);
        return await client.SendAsync(request);
    }
}
