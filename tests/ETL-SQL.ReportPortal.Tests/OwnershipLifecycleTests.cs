using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Core.Data;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.ReportPortal.Tests;

/// <summary>
/// P1.5 ownership lifecycle: folder ownership implies Manage, durable resources must be
/// explicitly reassigned before their owner is deleted, and personal artifacts (subscriptions
/// with their Orchestrator jobs/scripts, share-link capabilities) die with the user.
/// </summary>
[Trait("Category", "Portal")]
public sealed class OwnershipLifecycleTests
{
    /// <summary>
    /// A Publisher who creates a subfolder owns it and can administer it (rename, grant ACLs)
    /// without any explicit ACL on the subfolder itself; a different Publisher with no grant
    /// is still denied.
    /// </summary>
    [Fact]
    public async Task FolderOwner_HasManageOnOwnFolder_WithoutExplicitAcl()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var owner = await CreateReadyUserAsync(client, adminToken, $"owner_{suffix}", "Publisher");
        var bystander = await CreateReadyUserAsync(client, adminToken, $"bystander_{suffix}", "Publisher");

        // Root folder + group Manage so the owner may create a subfolder under it.
        var rootId = await CreateFolderAsync(client, adminToken, $"own_root_{suffix}", null);
        var groupId = await CreateGroupAsync(client, adminToken, $"own_grp_{suffix}");
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Post,
            $"/api/admin/groups/{groupId}/members", adminToken, new { userId = owner.UserId })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Post,
            $"/api/folders/{rootId}/acl", adminToken,
            new { groupId, permission = FolderPermission.Manage })).StatusCode);

        // Group/ACL changes rotate the security stamp (P0.3), so refresh the owner's session.
        var ownerToken = await LoginAsync(client, $"owner_{suffix}", "Ready@Test2!");

        // The owner creates a subfolder: no ACL exists on it, only OwnerId.
        var create = await SendAsync(client, HttpMethod.Post, "/api/folders", ownerToken,
            new { name = $"own_sub_{suffix}", parentId = rootId });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var subId = (await create.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<int>();

        // Ownership grants Manage: rename succeeds without an explicit grant.
        // (parentId must be echoed — a null parent on PUT means "move to root", admin-only.)
        var rename = await SendAsync(client, HttpMethod.Put, $"/api/folders/{subId}",
            ownerToken, new { name = $"own_sub_renamed_{suffix}", parentId = rootId });
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);

        // A different Publisher with no grant and no ownership is denied.
        var denied = await SendAsync(client, HttpMethod.Put, $"/api/folders/{subId}",
            bystander.AccessToken, new { name = "hijack", parentId = rootId });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    /// <summary>
    /// Deleting a user who owns durable resources conflicts until ownership is explicitly
    /// reassigned; with ?reassignTo the folder transfers and the transfer is audited.
    /// </summary>
    [Fact]
    public async Task DeleteUser_WithOwnedResources_ConflictsThenTransfersWithReassign()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var owner = await CreateReadyUserAsync(client, adminToken, $"departing_{suffix}", "Publisher");
        var rootId = await CreateFolderAsync(client, adminToken, $"del_root_{suffix}", null);
        var groupId = await CreateGroupAsync(client, adminToken, $"del_grp_{suffix}");
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Post,
            $"/api/admin/groups/{groupId}/members", adminToken, new { userId = owner.UserId })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Post,
            $"/api/folders/{rootId}/acl", adminToken,
            new { groupId, permission = FolderPermission.Manage })).StatusCode);
        var ownerToken = await LoginAsync(client, $"departing_{suffix}", "Ready@Test2!");
        var create = await SendAsync(client, HttpMethod.Post, "/api/folders", ownerToken,
            new { name = $"del_sub_{suffix}", parentId = rootId });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var subId = (await create.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<int>();

        // No reassignment target: the delete must conflict and report the owned inventory.
        var conflict = await SendAsync(client, HttpMethod.Delete,
            $"/api/admin/users/{owner.UserId}?cascade=true", adminToken, null);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var inventory = await conflict.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal(1, inventory!["ownedFolders"]!.GetValue<int>());

        // Invalid targets are rejected.
        var self = await SendAsync(client, HttpMethod.Delete,
            $"/api/admin/users/{owner.UserId}?cascade=true&reassignTo={owner.UserId}", adminToken, null);
        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
        var missing = await SendAsync(client, HttpMethod.Delete,
            $"/api/admin/users/{owner.UserId}?cascade=true&reassignTo=999999", adminToken, null);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        // Reassign to the admin: delete succeeds, ownership moves, transfer is audited.
        int adminId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            adminId = (await db.Users.FirstAsync(u => u.UserName == "admin")).Id;
        }
        var deleted = await SendAsync(client, HttpMethod.Delete,
            $"/api/admin/users/{owner.UserId}?cascade=true&reassignTo={adminId}", adminToken, null);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var sub = await db.Folders.SingleAsync(f => f.Id == subId);
            Assert.Equal(adminId, sub.OwnerId);
            Assert.True(await db.AuditLogs.AnyAsync(a =>
                a.Action == "TRANSFER_OWNERSHIP" && a.ResourceId == owner.UserId.ToString()));
        }
    }

    /// <summary>
    /// Personal artifacts die with the user: the subscription row cascades, its Orchestrator
    /// job and generated trigger script are removed inline, and share-link capabilities vanish.
    /// </summary>
    [Fact]
    public async Task DeleteUser_RemovesSubscriptionJobScriptAndCapabilities()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var user = await CreateReadyUserAsync(client, adminToken, $"personal_{suffix}", "Viewer");

        int subscriptionId;
        string jobName;
        var scriptPath = Path.Combine(factory.TempDir, "scripts", $"sub_trigger_{suffix}.etlsql");
        await File.WriteAllTextAsync(scriptPath, "PRINT 'trigger';");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var adminId = (await db.Users.FirstAsync(u => u.UserName == "admin")).Id;
            var folder = new Folder { Name = $"pf_{suffix}", Path = $"/pf_{suffix}", OwnerId = adminId };
            db.Folders.Add(folder);
            await db.SaveChangesAsync();
            var report = new Report
            {
                FolderId = folder.Id,
                Name = $"Personal Report {suffix}",
                ScriptPath = Path.Combine(factory.TempDir, "scripts", "personal.rptsql"),
                ScriptLastModified = DateTime.UtcNow,
                CreatedBy = adminId
            };
            db.Reports.Add(report);
            await db.SaveChangesAsync();

            var subscription = new Subscription
            {
                ReportId = report.Id,
                UserId = user.UserId,
                SmtpAlias = "test",
                Recipients = "user@test.local",
                ScriptPath = scriptPath,
                IsActive = true
            };
            db.Subscriptions.Add(subscription);
            db.ReportShareLinks.Add(new ReportShareLink
            {
                ReportId = report.Id,
                CreatedBy = user.UserId,
                Token = $"cap_{suffix}",
                ExpiresAt = DateTime.UtcNow.AddDays(1)
            });
            await db.SaveChangesAsync();
            subscriptionId = subscription.Id;
            jobName = SubscriptionOrchestration.JobName(subscription.Id, report.Name);
        }

        var jobStore = factory.Services.GetRequiredService<IJobHistoryStore>();
        await jobStore.SaveJobAsync(new JobDefinition(
            jobName, "PRINT 'trigger';", 1, "Days", null, null, DateTime.UtcNow.AddDays(1)));
        Assert.NotNull(await jobStore.GetJobAsync(jobName));

        var deleted = await SendAsync(client, HttpMethod.Delete,
            $"/api/admin/users/{user.UserId}?cascade=true", adminToken, null);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        Assert.Null(await jobStore.GetJobAsync(jobName));
        Assert.False(File.Exists(scriptPath), "expected the generated trigger script to be deleted");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            Assert.False(await db.Subscriptions.AnyAsync(s => s.Id == subscriptionId));
            Assert.False(await db.ReportShareLinks.AnyAsync(l => l.Token == $"cap_{suffix}"));
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static async Task<int> CreateFolderAsync(
        HttpClient client, string token, string name, int? parentId)
    {
        var response = await SendAsync(client, HttpMethod.Post, "/api/folders", token,
            new { name, parentId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<int>();
    }

    private static async Task<int> CreateGroupAsync(HttpClient client, string token, string name)
    {
        var response = await SendAsync(client, HttpMethod.Post, "/api/admin/groups", token, new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<int>();
    }

    private static async Task<(int UserId, string AccessToken)> CreateReadyUserAsync(
        HttpClient client, string adminToken, string username, string role)
    {
        var create = await SendAsync(client, HttpMethod.Post, "/api/admin/users", adminToken, new
        {
            username,
            email = $"{username}@test.local",
            password = "Initial@Test1!",
            role
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var userId = (await create.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<int>();
        var initial = await LoginAsync(client, username, "Initial@Test1!");
        var change = await SendAsync(client, HttpMethod.Post, "/api/auth/change-password", initial,
            new { currentPassword = "Initial@Test1!", newPassword = "Ready@Test2!" });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        return (userId, await LoginAsync(client, username, "Ready@Test2!"));
    }

    private static async Task<string> GetAdminTokenAsync(HttpClient client)
    {
        var initial = await LoginAsync(client, "admin", "Admin@12345!");
        var change = await SendAsync(client, HttpMethod.Post, "/api/auth/change-password", initial,
            new { currentPassword = "Admin@12345!", newPassword = "Admin@Tests99!" });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        return await LoginAsync(client, "admin", "Admin@Tests99!");
    }

    private static async Task<string> LoginAsync(HttpClient client, string username, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!["token"]!.GetValue<string>();
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string url, string token, object? body)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        await IfMatchVersioning.StampAsync(client, request, token);
        return await client.SendAsync(request);
    }
}
