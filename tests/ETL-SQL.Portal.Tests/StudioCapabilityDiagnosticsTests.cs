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
/// Studio authority is a separate axis from resource permission: folder <c>Manage</c> does not imply
/// the right to publish, commit, or push. An administrator asking "why can they do that?" has to be
/// able to see both, and a reviewer reading an audited Studio mutation has to be able to see which
/// capability let it through rather than inferring it from the route.
/// </summary>
[Trait("Category", "Portal")]
public sealed class StudioCapabilityDiagnosticsTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task EffectivePermissions_ReportRolesStudioModeAndCapabilities()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        // PortalWebFactory maps Admin to every capability and Publisher to all but SourcePush, so
        // the two roles must produce visibly different answers.
        var publisherId = await CreateUserAsync(client, adminToken, $"cap_pub_{suffix}", "Publisher");
        var viewerId = await CreateUserAsync(client, adminToken, $"cap_view_{suffix}", "Viewer");

        var publisher = await EffectivePermissionsAsync(client, adminToken, publisherId);
        Assert.Equal("SourceControlled", publisher["studioMode"]!.GetValue<string>());
        Assert.Contains("Publisher", publisher["roles"]!.AsArray().Select(r => r!.GetValue<string>()));

        var publisherCaps = publisher["studioCapabilities"]!.AsArray().Select(c => c!.GetValue<string>()).ToList();
        Assert.Contains(StudioCapabilities.ReportPublish, publisherCaps);
        Assert.Contains(StudioCapabilities.SourceCommit, publisherCaps);
        Assert.DoesNotContain(StudioCapabilities.SourcePush, publisherCaps);

        // A Viewer has no Studio mapping at all — capabilities are deny-by-default, so the
        // diagnostic must report an empty set rather than omitting the field.
        var viewer = await EffectivePermissionsAsync(client, adminToken, viewerId);
        Assert.Empty(viewer["studioCapabilities"]!.AsArray());
    }

    [Fact]
    public async Task DisabledStudio_ReportsNoCapabilitiesEvenWhereRolesMapThem()
    {
        // Capabilities mean nothing when Studio is off. Reporting the configured grants anyway would
        // overstate what the user can do, which is the opposite of what a diagnostic is for.
        using var factory = new DisabledStudioFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);

        var admin = await EffectivePermissionsAsync(client, adminToken, await AdminUserIdAsync(factory));

        Assert.Equal("Disabled", admin["studioMode"]!.GetValue<string>());
        Assert.Empty(admin["studioCapabilities"]!.AsArray());
    }

    [Fact]
    public async Task PublishingAReport_RecordsTheCapabilityThatAuthorizedIt()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var folderResponse = await AuthPost(client, adminToken, "/api/folders",
            new { name = $"cap_folder_{suffix}", parentId = (int?)null });
        Assert.Equal(HttpStatusCode.Created, folderResponse.StatusCode);
        var folderId = (await folderResponse.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();

        var scriptPath = Path.Combine(factory.TempDir, "scripts", $"cap-{suffix}.rptsql");
        await File.WriteAllTextAsync(scriptPath, "SET REPORT TITLE = 'Capability';");
        var publish = await AuthPost(client, adminToken, "/api/reports",
            new { folderId, name = $"Capability Report {suffix}", scriptPath });
        Assert.Equal(HttpStatusCode.Created, publish.StatusCode);
        var reportId = (await publish.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var entry = await db.AuditLogs
            .Where(log => log.Action == "PUBLISH_REPORT" && log.ResourceId == reportId.ToString())
            .SingleAsync();
        Assert.Equal(StudioCapabilities.ReportPublish, entry.StudioCapability);

        // The outbox carries it too, so a remote collector is not told less than the local table.
        var outbox = await db.AuditOutboxMessages
            .Where(message => message.Action == "PUBLISH_REPORT" && message.ResourceId == reportId.ToString())
            .SingleAsync();
        Assert.Equal(StudioCapabilities.ReportPublish, outbox.StudioCapability);
        Assert.Contains("\"studioCapability\":\"ReportPublish\"", outbox.PayloadJson);

        // A mutation with no Studio gate must not claim one, or the field stops meaning anything.
        var folderAudit = await db.AuditLogs
            .Where(log => log.Action == "CREATE_FOLDER" && log.ResourceId == folderId.ToString())
            .SingleAsync();
        Assert.Null(folderAudit.StudioCapability);
    }

    /// <summary>A portal whose Studio deployment mode is off.</summary>
    private sealed class DisabledStudioFactory : PortalWebFactory
    {
        protected override void CustomizePortalConfig(PortalConfig config) =>
            config.Studio.Mode = StudioDeploymentMode.Disabled;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static async Task<JsonObject> EffectivePermissionsAsync(
        HttpClient client, string adminToken, int userId)
    {
        var response = await AuthGet(client, adminToken, $"/api/admin/permissions/effective/user/{userId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>(Json))!;
    }

    private static async Task<int> AdminUserIdAsync(PortalWebFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        return (await db.Users.SingleAsync(u => u.UserName == "admin")).Id;
    }

    private static async Task<int> CreateUserAsync(
        HttpClient client, string adminToken, string username, string role)
    {
        var create = await AuthPost(client, adminToken, "/api/admin/users", new
        {
            username,
            email = $"{username}@test.local",
            password = "Initial@Test1!",
            role
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        return (await create.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();
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
