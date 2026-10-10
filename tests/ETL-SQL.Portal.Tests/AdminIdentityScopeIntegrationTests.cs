using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// End-to-end cover for the narrow hole opened in the <c>/api/admin</c> deny: a service identity
/// holding <c>admin.identity</c> can administer users and groups, and nothing else.
/// </summary>
[Trait("Category", "Portal")]
public sealed class AdminIdentityScopeIntegrationTests
{
    [Fact]
    public async Task FolderAclSnapshotsAndReceiptsRetainPermissionValuesAndRejectUndefinedGrants()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/folders/1/acl/catalog")).StatusCode);
        var token = await GetAdminTokenAsync(client);
        var created = await SendAsync(client, HttpMethod.Post, "/api/folders", token,
            new { name = "ACL receipt folder", parentId = (int?)null });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var folder = (await created.Content.ReadFromJsonAsync<FolderDto>())!;
        var createdGroup = await SendAsync(client, HttpMethod.Post, "/api/admin/groups", token,
            new { name = "ACL receipt group" });
        var group = (await createdGroup.Content.ReadFromJsonAsync<GroupDto>())!;
        var path = $"/api/folders/{folder.Id}/acl";
        var initial = (await (await SendAsync(client, HttpMethod.Get, path + "/catalog", token))
            .Content.ReadFromJsonAsync<FolderAclCatalogResponse>())!;
        Assert.Equal(folder.Id, initial.FolderId);
        Assert.Equal(folder.Name, initial.FolderName);
        Assert.Equal(folder.Version, initial.Version);
        Assert.Empty(initial.Items);
        Assert.Equal(Enum.GetValues<FolderPermission>().OrderBy(value => value.Rank()), initial.Permissions.Select(option => option.Permission));
        Assert.Contains(initial.Permissions, option => option.Name == "Manage" && (int)option.Permission == 2 && option.Rank == 3);
        Assert.Contains(initial.Permissions, option => option.Name == "Author" && (int)option.Permission == 3 && option.Rank == 2);
        var invalid = await SendAsync(client, HttpMethod.Post, path, token,
            new { groupId = group.Id, permission = 99 }, initial.Version);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            Assert.Equal(initial.Version, await db.Folders.Where(value => value.Id == folder.Id).Select(value => value.Version).SingleAsync());
            Assert.False(await db.FolderAcls.AnyAsync(value => value.FolderId == folder.Id));
            Assert.False(await db.AuditLogs.AnyAsync(value => value.Action == "GRANT_PERMISSION" && value.ResourceId == folder.Id.ToString()));
        }
        var granted = await SendAsync(client, HttpMethod.Post, path, token,
            new { groupId = group.Id, permission = (int)FolderPermission.Manage }, initial.Version);
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
        var receipt = (await granted.Content.ReadFromJsonAsync<FolderAclGrantResponse>())!;
        Assert.Equal(folder.Id, receipt.FolderId);
        Assert.Equal(group.Id, receipt.GroupId);
        Assert.Equal(FolderPermission.Manage, receipt.Permission);
        Assert.Equal(initial.Version + 1, receipt.Version);
        var current = (await (await SendAsync(client, HttpMethod.Get, path + "/catalog", token))
            .Content.ReadFromJsonAsync<FolderAclCatalogResponse>())!;
        Assert.Equal(receipt.Version, current.Version);
        Assert.Equal(FolderPermission.Manage, Assert.Single(current.Items).Permission);
        var stale = await SendAsync(client, HttpMethod.Post, path, token,
            new { groupId = group.Id, permission = (int)FolderPermission.Author }, initial.Version);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        granted = await SendAsync(client, HttpMethod.Post, path, token,
            new { groupId = group.Id, permission = (int)FolderPermission.Author }, current.Version);
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
        receipt = (await granted.Content.ReadFromJsonAsync<FolderAclGrantResponse>())!;
        Assert.Equal(FolderPermission.Author, receipt.Permission);
        var revoked = await SendAsync(client, HttpMethod.Delete, path + $"/{group.Id}", token, version: receipt.Version);
        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
        var removal = (await revoked.Content.ReadFromJsonAsync<FolderAclRevokeResponse>())!;
        Assert.Equal(folder.Id, removal.FolderId);
        Assert.Equal(group.Id, removal.GroupId);
        Assert.Equal(receipt.Version + 1, removal.Version);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            Assert.False(await db.FolderAcls.AnyAsync(value => value.FolderId == folder.Id));
            Assert.Equal(2, await db.AuditLogs.CountAsync(value => value.Action == "GRANT_PERMISSION" && value.ResourceId == folder.Id.ToString()));
            Assert.Equal(1, await db.AuditLogs.CountAsync(value => value.Action == "REVOKE_PERMISSION" && value.ResourceId == folder.Id.ToString()));
        }
    }

    [Fact]
    public async Task GroupMembershipReceiptsAccountForEveryUserAndUseDisplayedVersions()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);
        var createdUser = await SendAsync(client, HttpMethod.Post, "/api/admin/users", token, new
        {
            username = "group_receipt_user",
            email = "group-receipt@example.test",
            password = "Str0ng@Passw0rd!",
            role = "Viewer"
        });
        Assert.Equal(HttpStatusCode.Created, createdUser.StatusCode);
        var user = (await createdUser.Content.ReadFromJsonAsync<UserDto>())!;
        var createdGroup = await SendAsync(client, HttpMethod.Post, "/api/admin/groups", token,
            new { name = "Receipt group", description = "Membership acceptance" });
        Assert.Equal(HttpStatusCode.Created, createdGroup.StatusCode);
        var group = (await createdGroup.Content.ReadFromJsonAsync<GroupDto>())!;
        var path = $"/api/admin/groups/{group.Id}";
        var empty = (await (await SendAsync(client, HttpMethod.Get, path + "/members/catalog", token))
            .Content.ReadFromJsonAsync<GroupMemberCatalogResponse>())!;
        Assert.Equal(group.Id, empty.GroupId);
        Assert.Equal(group.Version, empty.Version);
        Assert.Empty(empty.Items);
        Assert.Equal(0, empty.Total);
        var added = await SendAsync(client, HttpMethod.Post, path + "/members", token,
            new { userId = user.Id }, empty.Version);
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        var single = (await added.Content.ReadFromJsonAsync<GroupMembershipResponse>())!;
        Assert.Equal(group.Id, single.GroupId);
        Assert.Equal(user.Id, single.UserId);
        Assert.Equal(empty.Version + 1, single.Version);
        var stale = await SendAsync(client, HttpMethod.Post, path + "/members/bulk-remove", token,
            new { userIds = new[] { user.Id } }, empty.Version);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var skipped = await SendAsync(client, HttpMethod.Post, path + "/members/bulk-add", token,
            new { userIds = new[] { user.Id, user.Id, int.MaxValue } }, single.Version);
        Assert.Equal(HttpStatusCode.OK, skipped.StatusCode);
        var skippedReceipt = (await skipped.Content.ReadFromJsonAsync<BulkAddGroupMembersResponse>())!;
        Assert.Equal(group.Id, skippedReceipt.GroupId);
        Assert.Equal(0, skippedReceipt.Added);
        Assert.Empty(skippedReceipt.ChangedUserIds);
        Assert.Equal(new[] { user.Id, int.MaxValue }, skippedReceipt.SkippedUserIds);
        var displayed = (await (await SendAsync(client, HttpMethod.Get,
            path + "/members/catalog?q=group_receipt&page=1&pageSize=25", token))
            .Content.ReadFromJsonAsync<GroupMemberCatalogResponse>())!;
        Assert.Equal(skippedReceipt.Version, displayed.Version);
        Assert.Equal(1, displayed.Total);
        Assert.Equal(user.Id, Assert.Single(displayed.Items).Id);
        var removed = await SendAsync(client, HttpMethod.Post, path + "/members/bulk-remove", token,
            new { userIds = new[] { user.Id, int.MaxValue } }, displayed.Version);
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        var removedReceipt = (await removed.Content.ReadFromJsonAsync<BulkRemoveGroupMembersResponse>())!;
        Assert.Equal(group.Id, removedReceipt.GroupId);
        Assert.Equal(1, removedReceipt.Removed);
        Assert.Equal(new[] { user.Id }, removedReceipt.ChangedUserIds);
        Assert.Equal(new[] { int.MaxValue }, removedReceipt.SkippedUserIds);
        Assert.Equal(displayed.Version + 1, removedReceipt.Version);
        added = await SendAsync(client, HttpMethod.Post, path + "/members", token,
            new { userId = user.Id }, removedReceipt.Version);
        single = (await added.Content.ReadFromJsonAsync<GroupMembershipResponse>())!;
        var singleRemoved = await SendAsync(client, HttpMethod.Delete, path + $"/members/{user.Id}", token,
            version: single.Version);
        Assert.Equal(HttpStatusCode.OK, singleRemoved.StatusCode);
        var removal = (await singleRemoved.Content.ReadFromJsonAsync<GroupMembershipResponse>())!;
        Assert.Equal(user.Id, removal.UserId);
        Assert.Equal(group.Id, removal.GroupId);
        Assert.Equal(single.Version + 1, removal.Version);
        var blockedGroupResponse = await SendAsync(client, HttpMethod.Post, "/api/admin/groups", token,
            new { name = "Blocked group" });
        var blocked = (await blockedGroupResponse.Content.ReadFromJsonAsync<GroupDto>())!;
        added = await SendAsync(client, HttpMethod.Post, $"/api/admin/groups/{blocked.Id}/members", token,
            new { userId = user.Id }, blocked.Version);
        var blockedMembership = (await added.Content.ReadFromJsonAsync<GroupMembershipResponse>())!;
        var deleted = await SendAsync(client, HttpMethod.Post, "/api/admin/groups/bulk-delete", token, new
        {
            groups = new[] { new { id = group.Id, version = removal.Version }, new { id = blocked.Id, version = blockedMembership.Version } }
        });
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        var deletion = (await deleted.Content.ReadFromJsonAsync<BulkDeleteGroupsResponse>())!;
        Assert.Equal(1, deletion.Deleted);
        Assert.Contains(deletion.Results, result => result.Id == group.Id && result.Status == "Deleted");
        Assert.Contains(deletion.Results, result => result.Id == blocked.Id && result.Status == "Blocked"
            && result.CurrentVersion == blockedMembership.Version);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        Assert.False(await db.Groups.AnyAsync(value => value.Id == group.Id));
        Assert.True(await db.UserGroups.AnyAsync(value => value.GroupId == blocked.Id && value.UserId == user.Id));
        Assert.Equal(1, await db.AuditLogs.CountAsync(value => value.Action == "BULK_REMOVE_USERS_FROM_GROUP" && value.ResourceId == group.Id.ToString()));
    }

    [Fact]
    public async Task RoleCatalogIncludesTheStoredRolesAndRetainsAdminAuthorization()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/admin/roles")).StatusCode);
        var adminToken = await GetAdminTokenAsync(client);
        string[] expected;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            db.Roles.Add(new PortalRole { Name = "CustomRole", NormalizedName = "CUSTOMROLE" });
            await db.SaveChangesAsync();
            expected = await db.Roles.Where(role => role.Name != null && role.Name != "")
                .OrderBy(role => role.Name).Select(role => role.Name!).ToArrayAsync();
        }
        var response = await SendAsync(client, HttpMethod.Get, "/api/admin/roles", adminToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var roles = await response.Content.ReadFromJsonAsync<ETL_SQL.Portal.Models.AdminRoleOptionDto[]>();
        Assert.NotNull(roles);
        Assert.Equal(expected, roles.Select(role => role.Name));
        Assert.Contains("DataSteward", roles.Select(role => role.Name));
        Assert.Contains("CustomRole", roles.Select(role => role.Name));
        var created = await SendAsync(client, HttpMethod.Post, "/api/admin/users", adminToken, new
        {
            username = "role_catalog_viewer",
            email = "viewer@example.test",
            password = "Str0ng@Passw0rd!",
            role = "Viewer",
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var login = await client.PostAsJsonAsync("/api/auth/login", new { username = "role_catalog_viewer", password = "Str0ng@Passw0rd!" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var viewerToken = (await login.Content.ReadFromJsonAsync<JsonObject>())!["token"]!.GetValue<string>();
        var change = await SendAsync(client, HttpMethod.Post, "/api/auth/change-password", viewerToken,
            new { currentPassword = "Str0ng@Passw0rd!", newPassword = "An0ther@Password!" });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        login = await client.PostAsJsonAsync("/api/auth/login", new { username = "role_catalog_viewer", password = "An0ther@Password!" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        viewerToken = (await login.Content.ReadFromJsonAsync<JsonObject>())!["token"]!.GetValue<string>();
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Get, "/api/admin/roles", viewerToken)).StatusCode);
    }

    [Theory]
    [InlineData("Local")]
    [InlineData("LDAP")]
    public async Task UnknownRoleCreationLeavesNoAccountAndCanBeRetried(string provider)
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var username = "unknown_role_" + Guid.NewGuid().ToString("N")[..8];
        HttpResponseMessage? response = null;
        var failure = await Record.ExceptionAsync(async () => response = await SendAsync(client, HttpMethod.Post, "/api/admin/users", adminToken, new
        {
            username,
            email = username + "@example.test",
            password = "Str0ng@Passw0rd!",
            role = "NotAnExistingRole",
            provider,
        }));
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            Assert.False(await db.Users.AnyAsync(user => user.UserName == username),
                $"Rejected role creation left a persisted {provider} account ({failure?.GetType().Name ?? response?.StatusCode.ToString()}).");
            Assert.False(await db.AuditLogs.AnyAsync(log => log.Action == "CREATE_USER" && log.Detail == username));
        }
        Assert.Null(failure);
        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var corrected = await SendAsync(client, HttpMethod.Post, "/api/admin/users", adminToken, new
        {
            username,
            email = username + "@example.test",
            password = "Str0ng@Passw0rd!",
            role = "Viewer",
            provider,
        });
        Assert.Equal(HttpStatusCode.Created, corrected.StatusCode);
    }

    [Fact]
    public async Task UnknownRoleUpdatePreservesTheExistingUserAndRole()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var username = "unknown_update_" + Guid.NewGuid().ToString("N")[..8];
        var created = await SendAsync(client, HttpMethod.Post, "/api/admin/users", adminToken, new
        {
            username,
            email = username + "@example.test",
            password = "Str0ng@Passw0rd!",
            role = "Viewer",
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var receipt = (await created.Content.ReadFromJsonAsync<JsonObject>())!;
        var id = receipt["id"]!.GetValue<int>();
        var version = receipt["version"]!.GetValue<long>();
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{id}")
        {
            Content = JsonContent.Create(new { role = "NotAnExistingRole", email = "changed@example.test", isActive = false }),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        HttpResponseMessage? response = null;
        var failure = await Record.ExceptionAsync(async () => response = await client.SendAsync(request));
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var user = await db.Users.SingleAsync(user => user.Id == id);
            Assert.Equal(username + "@example.test", user.Email);
            Assert.True(user.IsActive);
            Assert.Equal(version, user.Version);
            var roles = await db.UserRoles.Where(link => link.UserId == id)
                .Join(db.Roles, link => link.RoleId, role => role.Id, (_, role) => role.Name).ToListAsync();
            Assert.Equal(["Viewer"], roles);
            Assert.False(await db.AuditLogs.AnyAsync(log => log.Action == "UPDATE_USER" && log.ResourceId == id.ToString()));
        }
        Assert.Null(failure);
        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("Viewer", "Viewer")]
    [InlineData("vIeWeR", "Viewer")]
    [InlineData("", null)]
    public async Task CreatedUserReceiptMatchesItsActualRoles(string requestedRole, string? expectedRole)
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var username = "role_receipt_" + Guid.NewGuid().ToString("N")[..8];
        var response = await SendAsync(client, HttpMethod.Post, "/api/admin/users", adminToken, new
        {
            username,
            email = username + "@example.test",
            password = "Str0ng@Passw0rd!",
            role = requestedRole,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var receipt = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
        var id = receipt["id"]!.GetValue<int>();
        var expected = expectedRole is null ? Array.Empty<string>() : [expectedRole];
        Assert.Equal(expected, receipt["roles"]!.AsArray().Select(role => role!.GetValue<string>()));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var roles = await db.UserRoles.Where(link => link.UserId == id)
            .Join(db.Roles, link => link.RoleId, role => role.Id, (_, role) => role.Name).ToListAsync();
        Assert.Equal(expected, roles);
        Assert.True(await db.AuditLogs.AnyAsync(log => log.Action == "CREATE_USER" && log.ResourceId == id.ToString()));
        Assert.True(await db.AuditOutboxMessages.AnyAsync(log => log.Action == "CREATE_USER" && log.ResourceId == id.ToString()));
    }

    [Theory]
    [InlineData("Local")]
    [InlineData("LDAP")]
    public async Task CreateUserRollsBackWhenItsAuditCannotBeRecorded(string provider)
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var auditEntity = db.Model.FindEntityType(typeof(AuditLog))!;
            var table = auditEntity.GetTableName()!.Replace("\"", "\"\"");
            var store = Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier.Table(auditEntity.GetTableName()!, auditEntity.GetSchema());
            var action = auditEntity.FindProperty(nameof(AuditLog.Action))!.GetColumnName(store)!.Replace("\"", "\"\"");
            var trigger = $"CREATE TRIGGER fail_test_user_audit BEFORE INSERT ON \"{table}\" "
                + $"WHEN NEW.\"{action}\" = 'CREATE_USER' BEGIN SELECT RAISE(ABORT, 'injected user audit failure'); END;";
            await db.Database.ExecuteSqlRawAsync(trigger);
        }
        var username = "audit_reject_" + Guid.NewGuid().ToString("N")[..8];
        var failure = await Record.ExceptionAsync(async () =>
        {
            var response = await SendAsync(client, HttpMethod.Post, "/api/admin/users", adminToken, new
            {
                username,
                email = username + "@example.test",
                password = "Str0ng@Passw0rd!",
                role = "Viewer",
                provider,
            });
            Assert.False(response.IsSuccessStatusCode, "Account creation succeeded without its audit event.");
        });
        if (failure is not null) Assert.IsType<DbUpdateException>(failure);
        using var verifyScope = factory.Services.CreateScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<PortalDbContext>();
        Assert.False(await verify.Users.AnyAsync(user => user.UserName == username));
        Assert.False(await verify.AuditLogs.AnyAsync(log => log.Action == "CREATE_USER" && log.Detail == username));
        Assert.False(await verify.AuditOutboxMessages.AnyAsync(log => log.Action == "CREATE_USER" && log.PayloadJson.Contains(username)));
    }

    [Fact]
    public async Task IdentityScopeAdministersUsersButReachesNothingElseUnderAdmin()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var ownerId = await GetAdminIdAsync(factory);

        var token = await IssueIdentityTokenAsync(client, adminToken, ownerId, "identity-admin");

        // Reaches identity administration.
        var users = await SendAsync(client, HttpMethod.Get, "/api/admin/users", token);
        Assert.Equal(HttpStatusCode.OK, users.StatusCode);
        var roles = await SendAsync(client, HttpMethod.Get, "/api/admin/roles", token);
        Assert.Equal(HttpStatusCode.OK, roles.StatusCode);
        var groups = await SendAsync(client, HttpMethod.Get, "/api/admin/groups", token);
        Assert.Equal(HttpStatusCode.OK, groups.StatusCode);
        var sessions = await SendAsync(client, HttpMethod.Get, "/api/admin/sessions", token);
        Assert.Equal(HttpStatusCode.OK, sessions.StatusCode);

        // Reaches no unrelated Admin surface. Service-account lifecycle is intentionally part of
        // identity administration and has its own owner/authority delegation checks.
        foreach (var path in new[]
                 {
                     "/api/admin/support-bundle",
                     "/api/admin/configuration/export",
                     "/api/admin/audit",
                     "/api/admin/settings/branding",
                     "/api/admin/metrics/usage"
                 })
        {
            var denied = await SendAsync(client, HttpMethod.Get, path, token);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }

        var shutdown = await SendAsync(client, HttpMethod.Post, "/api/admin/service/shutdown", token);
        Assert.Equal(HttpStatusCode.Forbidden, shutdown.StatusCode);
    }

    [Fact]
    public async Task PortabilityScopeReadsOnlyTheReviewedConfigurationExport()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var ownerId = await GetAdminIdAsync(factory);
        var (_, token) = await IssueIdentityTokenWithIdAsync(
            client, adminToken, ownerId, "portability-export",
            ["admin.identity", "admin.portability"]);

        var plan = await SendAsync(client, HttpMethod.Get,
            "/api/admin/configuration/export/plan", token);
        Assert.Equal(HttpStatusCode.OK, plan.StatusCode);
        var hash = (await plan.Content.ReadFromJsonAsync<JsonObject>())!["planHash"]!.GetValue<string>();
        var export = await SendAsync(client, HttpMethod.Get,
            $"/api/admin/configuration/export?acknowledgedPlan={hash}", token);
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await SendAsync(client, HttpMethod.Get, "/api/admin/configuration/export", token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await SendAsync(client, HttpMethod.Get, "/api/admin/audit", token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await SendAsync(client, HttpMethod.Post, "/api/admin/configuration/export", token)).StatusCode);
    }

    /// <summary>
    /// The escalation question. A token that could mint an Admin could grant itself — through a new
    /// account — every capability this scope withholds, which would make the narrow scope
    /// meaningless.
    /// </summary>
    [Fact]
    public async Task ServiceTokenCannotCreateAnAdminButCanCreateAnOrdinaryUser()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var ownerId = await GetAdminIdAsync(factory);
        var token = await IssueIdentityTokenAsync(client, adminToken, ownerId, "identity-escalation");
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var elevated = await SendAsync(client, HttpMethod.Post, "/api/admin/users", token, new
        {
            username = $"escalated_{suffix}",
            email = $"escalated_{suffix}@corp.local",
            role = "Admin",
            password = "Str0ng@Passw0rd!"
        });
        Assert.Equal(HttpStatusCode.Forbidden, elevated.StatusCode);
        Assert.Contains("admin_elevation_requires_interactive_user",
            await elevated.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // The account must not exist: the guard runs before any mutation.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            Assert.False(await db.Users.AnyAsync(user => user.UserName == $"escalated_{suffix}"));
        }

        // Provisioning ordinary users is the whole point of the scope and still works.
        var ordinary = await SendAsync(client, HttpMethod.Post, "/api/admin/users", token, new
        {
            username = $"ordinary_{suffix}",
            email = $"ordinary_{suffix}@corp.local",
            role = "Viewer",
            password = "Str0ng@Passw0rd!"
        });
        Assert.Equal(HttpStatusCode.Created, ordinary.StatusCode);
    }

    /// <summary>Demotion is deliberately still permitted — revoking an admin should not need a browser.</summary>
    [Fact]
    public async Task ServiceTokenCannotPromoteAnExistingUserToAdmin()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var ownerId = await GetAdminIdAsync(factory);
        var token = await IssueIdentityTokenAsync(client, adminToken, ownerId, "identity-promote");
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var created = await SendAsync(client, HttpMethod.Post, "/api/admin/users", token, new
        {
            username = $"promote_{suffix}",
            email = $"promote_{suffix}@corp.local",
            role = "Viewer",
            password = "Str0ng@Passw0rd!"
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var userId = (await created.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<int>();

        var promote = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{userId}")
        {
            Content = JsonContent.Create(new { role = "Admin" })
        };
        promote.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(promote);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var isAdmin = await db.UserRoles
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .AnyAsync(entry => entry.UserId == userId && entry.Name == "Admin");
        Assert.False(isAdmin);
    }

    /// <summary>
    /// A mutation made by automation must be tellable from the same-named human who owns the token,
    /// or the audit log cannot answer "who did this".
    /// </summary>
    [Fact]
    public async Task MutationsAreAttributedToTheServiceIdentityNotTheOwningHuman()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var ownerId = await GetAdminIdAsync(factory);
        var (accountId, token) = await IssueIdentityTokenWithIdAsync(client, adminToken, ownerId, "identity-audit");
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var created = await SendAsync(client, HttpMethod.Post, "/api/admin/users", token, new
        {
            username = $"audited_{suffix}",
            email = $"audited_{suffix}@corp.local",
            role = "Viewer",
            password = "Str0ng@Passw0rd!"
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var entry = await db.AuditLogs
            .Where(log => log.Action == "CREATE_USER" && log.ActorId == accountId)
            .OrderByDescending(log => log.Id)
            .FirstAsync();

        Assert.Equal("ServiceAccount", entry.ActorType);
        Assert.Equal(accountId, entry.ActorId);
        Assert.Contains("admin.identity", entry.EffectiveScopes ?? "", StringComparison.Ordinal);
        // The owning human's id is still recorded, but it is not what identifies the actor.
        Assert.Equal(ownerId, entry.UserId);
    }

    /// <summary>
    /// The Admin role is only safe on a token because admin.identity confines it to the identity
    /// allowlist. Granting the role without the scope would restore unbounded administrative reach.
    /// </summary>
    [Fact]
    public async Task AdminRoleIsRefusedOnAServiceAccountWithoutTheIdentityScope()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var ownerId = await GetAdminIdAsync(factory);

        var response = await SendAsync(client, HttpMethod.Post, "/api/admin/service-accounts", adminToken,
            new
            {
                name = $"unscoped-admin-{Guid.NewGuid().ToString("N")[..6]}",
                ownerUserId = ownerId,
                scopes = new[] { "portal.read" },
                roles = new[] { "Admin" },
                expiresAt = DateTime.UtcNow.AddHours(1)
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("admin.identity", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdminRoleIsAcceptedWhenPairedWithTheIdentityScope()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var ownerId = await GetAdminIdAsync(factory);

        var response = await SendAsync(client, HttpMethod.Post, "/api/admin/service-accounts", adminToken,
            new
            {
                name = $"scoped-admin-{Guid.NewGuid().ToString("N")[..6]}",
                ownerUserId = ownerId,
                scopes = new[] { "admin.identity" },
                roles = new[] { "Admin" },
                expiresAt = DateTime.UtcNow.AddHours(1)
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // ── Harness ──────────────────────────────────────────────────────────────────

    private static async Task<string> IssueIdentityTokenAsync(
        HttpClient client, string adminToken, int ownerId, string name) =>
        (await IssueIdentityTokenWithIdAsync(client, adminToken, ownerId, name)).Token;

    private static async Task<(string AccountId, string Token)> IssueIdentityTokenWithIdAsync(
        HttpClient client, string adminToken, int ownerId, string name, string[]? scopes = null)
    {
        var response = await SendAsync(client, HttpMethod.Post, "/api/admin/service-accounts", adminToken,
            new
            {
                name = $"{name}-{Guid.NewGuid().ToString("N")[..6]}",
                ownerUserId = ownerId,
                scopes = scopes ?? ["admin.identity"],
                roles = new[] { "Admin" },
                expiresAt = DateTime.UtcNow.AddHours(1)
            });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
        var account = body["account"]!.AsObject();
        var clientId = account["clientId"]!.GetValue<string>();
        var secret = body["clientSecret"]!.GetValue<string>();

        var exchange = await client.PostAsJsonAsync("/api/auth/service-token",
            new { clientId, clientSecret = secret });
        Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);
        var token = (await exchange.Content.ReadFromJsonAsync<JsonObject>())!["accessToken"]!.GetValue<string>();

        return (account["id"]!.GetValue<string>(), token);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string path, string token, object? payload = null, long? version = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (payload is not null) request.Content = JsonContent.Create(payload);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        if (version.HasValue) request.Headers.IfMatch.Add(new System.Net.Http.Headers.EntityTagHeaderValue($"\"{version.Value}\""));
        return await client.SendAsync(request);
    }

    private static async Task<int> GetAdminIdAsync(PortalWebFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        return await db.Users.Where(value => value.UserName == "admin").Select(value => value.Id).SingleAsync();
    }

    private static async Task<string> GetAdminTokenAsync(HttpClient client)
    {
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = "Admin@12345!" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = (await login.Content.ReadFromJsonAsync<JsonObject>())!["token"]!.GetValue<string>();
        var change = await SendAsync(client, HttpMethod.Post, "/api/auth/change-password", token,
            new { currentPassword = "Admin@12345!", newPassword = "Admin@Identity99!" });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        var relogin = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = "Admin@Identity99!" });
        Assert.Equal(HttpStatusCode.OK, relogin.StatusCode);
        return (await relogin.Content.ReadFromJsonAsync<JsonObject>())!["token"]!.GetValue<string>();
    }
}
