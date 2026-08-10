using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Two operator surfaces over machinery that already existed but had no way in: cross-environment
/// fleet aggregation (built, but nothing configured the environments to aggregate) and configuration
/// export (which returned its summary to the audit log and nothing to the caller).
/// </summary>
[Trait("Category", "Portal")]
public sealed class FleetWorkspaceAndExportPlanTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task FleetWorkspace_WithNothingConfigured_SaysSoRatherThanLookingEmpty()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);

        var response = await AuthGet(client, adminToken, "/api/fleet/workspace");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(Json);

        // A healthy fleet of zero and a fleet nobody configured look identical unless one says so.
        Assert.False(body!["configured"]!.GetValue<bool>());
        Assert.Contains("Portal:Fleet:Environments",
            body["message"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FleetWorkspace_ReportsUnreachableEnvironmentsWithoutFailingTheView()
    {
        using var factory = new ConfiguredFleetFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);

        var response = await AuthGet(client, adminToken, "/api/fleet/workspace?mode=preflight");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(Json);

        Assert.True(body!["configured"]!.GetValue<bool>());
        var environments = body["report"]!["environments"]!.AsArray();
        Assert.Equal(2, environments.Count);
        // A partial outage is exactly when the view is needed, so unreachable is a result, not an error.
        Assert.All(environments, environment => Assert.False(environment!["reachable"]!.GetValue<bool>()));

        // Preflight must not claim readiness when it cannot see the fleet.
        Assert.False(body["upgrade"]!["ready"]!.GetValue<bool>());
        // The fleet contract serializes these enums numerically, as its other consumers expect.
        Assert.Equal((int)FleetUpgradeReportMode.Preflight, body["upgrade"]!["mode"]!.GetValue<int>());

        // Per-environment tokens are credentials, not status: only their count is reported.
        var raw = body.ToJsonString();
        Assert.DoesNotContain("fleet-token-value", raw, StringComparison.Ordinal);
        Assert.Equal(2, body["credentialsConfigured"]!.GetValue<int>());
    }

    [Fact]
    public async Task FleetWorkspace_RejectsAnUnknownMode()
    {
        using var factory = new ConfiguredFleetFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);

        var response = await AuthGet(client, adminToken, "/api/fleet/workspace?mode=whenever");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ExportPlan_ShowsWhatWillNotTravel_WithoutTheScriptBody()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);

        var response = await AuthGet(client, adminToken, "/api/admin/configuration/export/plan");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var plan = await response.Content.ReadFromJsonAsync<JsonObject>(Json);

        Assert.False(string.IsNullOrWhiteSpace(plan!["planHash"]!.GetValue<string>()));
        Assert.NotNull(plan["emitted"]);
        Assert.NotNull(plan["skipped"]);
        Assert.NotNull(plan["contentManifest"]);
        // The plan is for reviewing, not for downloading: the script body stays out of it.
        Assert.Null(plan["script"]);
    }

    [Fact]
    public async Task ManagedDedicatedExportPlanCarriesHostFixedTenantIdentity()
    {
        using var factory = new TenantFixedFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);

        var response = await AuthGet(client, adminToken, "/api/admin/configuration/export/plan");
        var plan = await response.Content.ReadFromJsonAsync<JsonObject>(Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("tenant-alpha", plan!["tenantExportIdentity"]!.GetValue<string>());
    }

    [Fact]
    public async Task ExportRefusesAStalePlanAcknowledgement()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);

        var planHash = (await (await AuthGet(client, adminToken, "/api/admin/configuration/export/plan"))
            .Content.ReadFromJsonAsync<JsonObject>(Json))!["planHash"]!.GetValue<string>();

        // Acknowledging the plan you actually reviewed works.
        Assert.Equal(HttpStatusCode.OK,
            (await AuthGet(client, adminToken,
                $"/api/admin/configuration/export?acknowledgedPlan={planHash}")).StatusCode);

        // Change what would be exported, so the reviewed plan no longer describes it.
        Assert.Equal(HttpStatusCode.Created,
            (await AuthPost(client, adminToken, "/api/admin/groups",
                new { name = $"plan_drift_{Guid.NewGuid():N}"[..20] })).StatusCode);

        var stale = await AuthGet(client, adminToken,
            $"/api/admin/configuration/export?acknowledgedPlan={planHash}");
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        Assert.True(await db.AuditLogs.AnyAsync(log =>
            log.Action == "EXPORT_PORTAL_CONFIGURATION_REFUSED"));
    }

    [Fact]
    public async Task ExportWithoutAcknowledgement_StillWorks_AndTheAuditSaysSo()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);

        // Review is enforceable when used, not mandatory — the audit records which happened.
        Assert.Equal(HttpStatusCode.OK,
            (await AuthGet(client, adminToken, "/api/admin/configuration/export")).StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var entry = await db.AuditLogs
            .Where(log => log.Action == "EXPORT_PORTAL_CONFIGURATION")
            .OrderByDescending(log => log.Id)
            .FirstAsync();
        Assert.Contains("no plan acknowledged", entry.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PromotionValidation_PlansEachResourceAsCreateMatchOrCollision()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        // One group already here, one that is not.
        Assert.Equal(HttpStatusCode.Created,
            (await AuthPost(client, adminToken, "/api/admin/groups", new { name = $"existing_{suffix}" })).StatusCode);

        var script = $"""
            EXECUTE portal BEGIN
              CREATE GROUP 'existing_{suffix}';
              CREATE GROUP 'brand_new_{suffix}';
            END;
            """;

        var response = await AuthPost(client, adminToken, "/api/admin/configuration/validate", new { script });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonObject>(Json);

        var plan = result!["plan"]!.AsArray()
            .ToDictionary(e => e!["name"]!.GetValue<string>(), e => e!["action"]!.GetValue<string>());

        // Findings carry only collisions; a plan needs the whole picture, or an operator cannot tell
        // an empty target from an identical one.
        Assert.Equal("Match", plan[$"existing_{suffix}"]);
        Assert.Equal("Create", plan[$"brand_new_{suffix}"]);
    }

    /// <summary>A portal with two fleet environments configured, neither of which exists.</summary>
    private sealed class ConfiguredFleetFactory : PortalWebFactory
    {
        protected override void CustomizePortalConfig(PortalConfig config)
        {
            config.Fleet.Environments =
            [
                new PortalFleetEnvironmentConfig
                {
                    Name = "alpha",
                    BaseUrl = "https://alpha.example.invalid",
                    BearerToken = "fleet-token-value"
                },
                new PortalFleetEnvironmentConfig
                {
                    Name = "beta",
                    BaseUrl = "https://beta.example.invalid",
                    BearerToken = "fleet-token-value"
                }
            ];
        }
    }

    private sealed class TenantFixedFactory : PortalWebFactory
    {
        protected override void CustomizePortalConfig(PortalConfig config) =>
            config.TenantId = "tenant-alpha";
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

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
