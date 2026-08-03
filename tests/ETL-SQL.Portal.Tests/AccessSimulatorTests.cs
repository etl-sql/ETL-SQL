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
/// The access simulator answers "what can this identity reach, and why?" from one place. Roles,
/// groups, folder and report ACLs, connection grants, Studio capability, and row-level security were
/// each already queryable — that is the problem it solves, because composing them by hand is where
/// the mistake gets made.
///
/// The property these tests care about most is the one that makes it safe to give an administrator:
/// it explains row-level security without ever running the report.
/// </summary>
[Trait("Category", "Portal")]
[Trait("Category", "Smoke.Security")]
public sealed class AccessSimulatorTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const int Read = 0;
    private const int Manage = 2;

    [Fact]
    public async Task ExplainsWhyAGrantedUserHasAccess_AndWhyAnUngrantedOneDoesNot()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var granted = await CreateUserAsync(client, adminToken, $"sim_in_{suffix}", "Viewer");
        var outsider = await CreateUserAsync(client, adminToken, $"sim_out_{suffix}", "Viewer");
        var groupId = await CreateGroupAsync(client, adminToken, $"sim_group_{suffix}");
        Assert.Equal(HttpStatusCode.OK,
            (await AuthPost(client, adminToken, $"/api/admin/groups/{groupId}/members",
                new { userId = granted })).StatusCode);

        var folderId = await CreateFolderAsync(client, adminToken, $"sim_folder_{suffix}");
        Assert.Equal(HttpStatusCode.OK,
            (await AuthPost(client, adminToken, $"/api/folders/{folderId}/acl",
                new { groupId, permission = Read })).StatusCode);

        var scriptPath = Path.Combine(factory.TempDir, "scripts", $"sim-{suffix}.rptsql");
        await File.WriteAllTextAsync(scriptPath, "SET REPORT TITLE = 'Plain';");
        var reportId = await PublishAsync(client, adminToken, folderId, $"Sim Report {suffix}", scriptPath);

        var inside = await SimulateAsync(client, adminToken, granted, reportId);
        var report = inside["report"]!.AsObject();
        Assert.True(report["canView"]!.GetValue<bool>());
        Assert.Equal("Read", report["permission"]!.GetValue<string>());
        // The source is the point: an answer with no reasoning cannot be acted on.
        Assert.Contains(report["sources"]!.AsArray().Select(s => s!.GetValue<string>()),
            source => source.Contains("Folder ACL", StringComparison.Ordinal));
        Assert.False(report["canManage"]!.GetValue<bool>());

        var outside = await SimulateAsync(client, adminToken, outsider, reportId);
        var outsideReport = outside["report"]!.AsObject();
        Assert.False(outsideReport["canView"]!.GetValue<bool>());
        Assert.Null(outsideReport["permission"]?.GetValue<string?>());
        Assert.Equal(["No grant"], outsideReport["sources"]!.AsArray().Select(s => s!.GetValue<string>()));
    }

    [Fact]
    public async Task ExplainsRowLevelSecurityByNamingIt_WithoutReturningAnyRows()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var viewer = await CreateUserAsync(client, adminToken, $"sim_rls_{suffix}", "Viewer");
        var groupId = await CreateGroupAsync(client, adminToken, $"sim_rls_group_{suffix}");
        Assert.Equal(HttpStatusCode.OK,
            (await AuthPost(client, adminToken, $"/api/admin/groups/{groupId}/members",
                new { userId = viewer })).StatusCode);
        var folderId = await CreateFolderAsync(client, adminToken, $"sim_rls_folder_{suffix}");
        Assert.Equal(HttpStatusCode.OK,
            (await AuthPost(client, adminToken, $"/api/folders/{folderId}/acl",
                new { groupId, permission = Manage })).StatusCode);

        var scriptPath = Path.Combine(factory.TempDir, "scripts", $"sim-rls-{suffix}.rptsql");
        await File.WriteAllTextAsync(scriptPath, """
            SET REPORT TITLE = 'Regional';
            SELECT 'North' AS Region, 4200 AS Secret INTO #rows
            WHERE HAS_GROUP('Regional') OR @@CURRENT_USER = 'ops';
            """);
        var reportId = await PublishAsync(client, adminToken, folderId, $"RLS Report {suffix}", scriptPath);

        var simulation = await SimulateAsync(client, adminToken, viewer, reportId);
        var rls = simulation["report"]!["rowLevelSecurity"]!.AsObject();

        Assert.True(rls["identitySensitive"]!.GetValue<bool>());
        var references = rls["identityReferences"]!.AsArray().Select(r => r!.GetValue<string>()).ToList();
        Assert.Contains("HAS_GROUP", references);
        Assert.Contains("@@CURRENT_USER", references);

        // The identity that would be bound is named, so an operator can reason about the filter.
        Assert.Equal($"sim_rls_{suffix}", rls["boundUser"]!.GetValue<string>());
        Assert.Contains($"sim_rls_group_{suffix}",
            rls["boundGroups"]!.AsArray().Select(g => g!.GetValue<string>()));

        // And nothing from the data is anywhere in the response. A tool for auditing who can see
        // data must not become a way to see it.
        var raw = simulation.ToJsonString();
        Assert.DoesNotContain("4200", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("North", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExplainsConnectionGrantsAndStudioCapability()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var viewer = await CreateUserAsync(client, adminToken, $"sim_conn_{suffix}", "Viewer");
        var groupId = await CreateGroupAsync(client, adminToken, $"sim_conn_group_{suffix}");
        Assert.Equal(HttpStatusCode.OK,
            (await AuthPost(client, adminToken, $"/api/admin/groups/{groupId}/members",
                new { userId = viewer })).StatusCode);

        // Two connections: one restricted to a group the user is not in, one they are.
        await SeedConnectionAsync(factory, $"open_{suffix}", groupId);
        await SeedConnectionAsync(factory, $"closed_{suffix}", groupId: null, restrictToUnrelatedGroup: true);

        var simulation = await SimulateAsync(client, adminToken, viewer, reportId: null);

        var connections = simulation["connections"]!.AsArray()
            .ToDictionary(c => c!["alias"]!.GetValue<string>(), c => c!.AsObject());
        Assert.True(connections[$"open_{suffix}"]["usable"]!.GetValue<bool>());
        Assert.False(connections[$"closed_{suffix}"]["usable"]!.GetValue<bool>());
        Assert.Equal("No group grant.", connections[$"closed_{suffix}"]["reason"]!.GetValue<string>());

        // Studio authority is separate from resource permission, so it is reported separately —
        // and split by where it came from, because the remedy differs.
        var studio = simulation["studio"]!.AsObject();
        Assert.Equal("SourceControlled", studio["mode"]!.GetValue<string>());
        Assert.Empty(studio["capabilities"]!.AsArray());

        Assert.Equal(HttpStatusCode.OK,
            (await SendAsync(client, HttpMethod.Put, adminToken,
                $"/api/admin/groups/{groupId}/studio-capabilities",
                new { capabilities = new[] { StudioCapabilities.ScriptPreview } })).StatusCode);

        var after = await SimulateAsync(client, adminToken, viewer, reportId: null);
        Assert.Contains(StudioCapabilities.ScriptPreview,
            after["studio"]!["fromGroups"]!.AsArray().Select(c => c!.GetValue<string>()));
        Assert.Empty(after["studio"]!["fromRoles"]!.AsArray());
    }

    [Fact]
    public async Task ReadingSomeoneElsesAccess_IsItselfAudited()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var target = await CreateUserAsync(client, adminToken, $"sim_aud_{Guid.NewGuid():N}"[..24], "Viewer");

        await SimulateAsync(client, adminToken, target, reportId: null);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        Assert.True(await db.AuditLogs.AnyAsync(log =>
            log.Action == "SIMULATE_ACCESS" && log.ResourceId == target.ToString()));
    }

    [Fact]
    public async Task IsAdministratorOnly()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var viewer = await CreateUserAsync(client, adminToken, $"sim_deny_{suffix}", "Viewer");
        var viewerToken = await LoginAsync(client, $"sim_deny_{suffix}", "Ready@Test2!");

        var response = await AuthGet(client, viewerToken, $"/api/admin/access-simulator/user/{viewer}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static async Task SeedConnectionAsync(
        PortalWebFactory factory, string alias, int? groupId, bool restrictToUnrelatedGroup = false)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var connection = new PortalSharedConnection
        {
            Alias = alias,
            ConnectorType = "SQLITE",
            OptionsJson = "{}",
            CreatedByUserId = 1
        };
        db.PortalSharedConnections.Add(connection);
        await db.SaveChangesAsync();

        var aclGroupId = groupId;
        if (restrictToUnrelatedGroup)
        {
            var other = new Group { Name = $"unrelated_{alias}" };
            db.Groups.Add(other);
            await db.SaveChangesAsync();
            aclGroupId = other.Id;
        }

        if (aclGroupId is int id)
        {
            db.SharedConnectionAcls.Add(new SharedConnectionAcl { SharedConnectionId = connection.Id, GroupId = id });
            await db.SaveChangesAsync();
        }
    }

    private static async Task<JsonObject> SimulateAsync(
        HttpClient client, string adminToken, int userId, int? reportId)
    {
        var url = $"/api/admin/access-simulator/user/{userId}"
            + (reportId is int id ? $"?reportId={id}" : "");
        var response = await AuthGet(client, adminToken, url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>(Json))!;
    }

    private static async Task<int> PublishAsync(
        HttpClient client, string adminToken, int folderId, string name, string scriptPath)
    {
        var response = await AuthPost(client, adminToken, "/api/reports", new { folderId, name, scriptPath });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();
    }

    private static async Task<int> CreateFolderAsync(HttpClient client, string adminToken, string name)
    {
        var response = await AuthPost(client, adminToken, "/api/folders", new { name, parentId = (int?)null });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();
    }

    private static async Task<int> CreateGroupAsync(HttpClient client, string adminToken, string name)
    {
        var response = await AuthPost(client, adminToken, "/api/admin/groups", new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();
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
        var userId = (await create.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();
        var initial = await LoginAsync(client, username, "Initial@Test1!");
        Assert.Equal(HttpStatusCode.NoContent,
            (await AuthPost(client, initial, "/api/auth/change-password",
                new { currentPassword = "Initial@Test1!", newPassword = "Ready@Test2!" })).StatusCode);
        return userId;
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
