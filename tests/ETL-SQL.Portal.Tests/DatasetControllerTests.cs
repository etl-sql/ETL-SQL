using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Core.Data;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Integration tests for DatasetController: list, metadata, rows preview,
/// refresh, update, delete, and ACL management.
/// </summary>
[Trait("Category", "Portal")]
public class DatasetControllerTests : IClassFixture<PortalWebFactory>
{
    private readonly HttpClient _client;
    private readonly PortalWebFactory _factory;
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    private static string? _adminToken;
    private static readonly SemaphoreSlim _tokenLock = new(1, 1);

    public DatasetControllerTests(PortalWebFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ── 1. GET /api/datasets — authentication & listing ───────────────────────

    [Fact]
    [Trait("Category", "Smoke.Portal")]
    [Trait("Category", "Smoke.Security")]
    public async Task GetAll_RequiresAuthentication()
    {
        var res = await _client.GetAsync("/api/datasets");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task GetAll_AdminSeesAllDatasets()
    {
        var token = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        await RegisterDatasetAsync($"#pub_{suffix}", $"/f_{suffix}", DatasetAccessLevel.Public);
        await RegisterDatasetAsync($"#prv_{suffix}", $"/f_{suffix}", DatasetAccessLevel.Private);

        var res = await AuthGet(token, "/api/datasets");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var list = await res.Content.ReadFromJsonAsync<JsonArray>(_json);

        var names = list!.Select(d => d!["name"]!.GetValue<string>()).ToHashSet();
        Assert.Contains($"#pub_{suffix}", names);
        Assert.Contains($"#prv_{suffix}", names);
    }

    [Fact]
    public async Task GetAll_NonAdminSeesOnlyPublicAndAclDatasets()
    {
        var token = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var folder = $"/f_acl_{suffix}";

        await RegisterDatasetAsync($"#pub_{suffix}", folder, DatasetAccessLevel.Public);
        await RegisterDatasetAsync($"#prv_{suffix}", folder, DatasetAccessLevel.Private);

        // Create a viewer user and a group with access to the ACL dataset
        var userRes = await AuthPost(token, "/api/admin/users", new
        {
            username = $"dv_{suffix}",
            email = $"dv_{suffix}@test.local",
            password = "Viewer@1234!",
            role = "Viewer"
        });
        Assert.Equal(HttpStatusCode.Created, userRes.StatusCode);

        var viewerToken = await LoginAndChangePasswordAsync($"dv_{suffix}", "Viewer@1234!", "Viewer@Changed9!");

        var res = await AuthGet(viewerToken, "/api/datasets");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var list = await res.Content.ReadFromJsonAsync<JsonArray>(_json);
        var names = list!.Select(d => d!["name"]!.GetValue<string>()).ToHashSet();

        Assert.Contains($"#pub_{suffix}", names);
        Assert.DoesNotContain($"#prv_{suffix}", names);
    }

    [Fact]
    [Trait("Category", "Smoke.Security")]
    public async Task PublicDataset_WithFolder_RequiresFolderReadEverywhere()
    {
        var adminToken = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var username = $"folder_reader_{suffix}";

        var folderRes = await AuthPost(adminToken, "/api/folders", new
        {
            name = $"dataset_folder_{suffix}",
            parentId = (int?)null
        });
        folderRes.EnsureSuccessStatusCode();
        var folderId = (await folderRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["id"]!.GetValue<int>();

        string folderPath;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            folderPath = (await db.Folders.SingleAsync(f => f.Id == folderId)).Path;
        }

        var userRes = await AuthPost(adminToken, "/api/admin/users", new
        {
            username,
            email = $"{username}@test.local",
            password = "Folder@1234!",
            role = "Viewer"
        });
        userRes.EnsureSuccessStatusCode();
        var userId = (await userRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["id"]!.GetValue<int>();
        var userToken = await LoginAndChangePasswordAsync(username, "Folder@1234!", "Folder@Changed9!");

        var name = $"#folder_public_{suffix}";
        await RegisterDatasetAsync(name, folderPath, DatasetAccessLevel.Public);
        var datasetId = await GetDatasetIdAsync(name, folderPath);

        Assert.Equal(HttpStatusCode.Forbidden, (await AuthGet(userToken, $"/api/datasets/{datasetId}")).StatusCode);
        using (var scope = _factory.Services.CreateScope())
        {
            var registry = scope.ServiceProvider.GetRequiredService<IDatasetRegistry>();
            Assert.Null(await registry.Lookup(name, $"UserId={userId}"));
        }

        var groupRes = await AuthPost(adminToken, "/api/admin/groups", new { name = $"folder_group_{suffix}" });
        groupRes.EnsureSuccessStatusCode();
        var groupId = (await groupRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["id"]!.GetValue<int>();
        (await AuthPost(adminToken, $"/api/admin/groups/{groupId}/members", new { userId }))
            .EnsureSuccessStatusCode();
        await AddFolderAclAsync(folderId, groupId, FolderPermission.Read);
        userToken = await LoginExistingUserAsync(username, "Folder@Changed9!");

        Assert.Equal(HttpStatusCode.OK, (await AuthGet(userToken, $"/api/datasets/{datasetId}")).StatusCode);
        using (var scope = _factory.Services.CreateScope())
        {
            var registry = scope.ServiceProvider.GetRequiredService<IDatasetRegistry>();
            Assert.NotNull(await registry.Lookup(name, $"UserId={userId}"));
        }
    }

    // ── 2. GET /api/datasets/{id} — metadata panel ────────────────────────────

    [Fact]
    [Trait("Category", "Smoke.Portal")]
    public async Task GetById_ReturnsCorrectMetadata()
    {
        var token = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name = $"#meta_{suffix}";
        var folder = $"/meta_{suffix}";

        await RegisterDatasetAsync(name, folder, DatasetAccessLevel.Public, rowCount: 42, ttl: "1h",
            lastRefresh: DateTime.UtcNow);
        var id = await GetDatasetIdAsync(name, folder);

        var res = await AuthGet(token, $"/api/datasets/{id}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var dto = await res.Content.ReadFromJsonAsync<JsonObject>(_json);

        Assert.Equal(name, dto!["name"]!.GetValue<string>());
        Assert.Equal("Public", dto["accessLevel"]!.GetValue<string>());
        Assert.Equal(42L, dto["rowCount"]!.GetValue<long>());
        Assert.Equal("1h", dto["ttl"]!.GetValue<string>());
        Assert.False(dto["isStale"]!.GetValue<bool>());
    }

    [Fact]
    [Trait("Category", "Smoke.Security")]
    public async Task GetById_ForbidsPrivateDatasetForUnrelatedUser()
    {
        var token = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name = $"#priv_{suffix}";
        var folder = $"/priv_{suffix}";

        await RegisterDatasetAsync(name, folder, DatasetAccessLevel.Private);
        var id = await GetDatasetIdAsync(name, folder);

        var userRes = await AuthPost(token, "/api/admin/users", new
        {
            username = $"outsider_{suffix}",
            email = $"outsider_{suffix}@test.local",
            password = "Out@1234!",
            role = "Viewer"
        });
        Assert.Equal(HttpStatusCode.Created, userRes.StatusCode);
        var outsiderToken = await LoginAndChangePasswordAsync($"outsider_{suffix}", "Out@1234!", "Out@Changed9!");

        var res = await AuthGet(outsiderToken, $"/api/datasets/{id}");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    [Trait("Category", "Smoke.Security")]
    public async Task PublisherOwnsPrivateDatasetWithoutOwningReport()
    {
        var adminToken = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var username = $"publisher_{suffix}";

        var userRes = await AuthPost(adminToken, "/api/admin/users", new
        {
            username,
            email = $"{username}@test.local",
            password = "Publish@1234!",
            role = "Viewer"
        });
        userRes.EnsureSuccessStatusCode();
        var userId = (await userRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["id"]!.GetValue<int>();
        var publisherToken = await LoginAndChangePasswordAsync(username, "Publish@1234!", "Publish@Changed9!");

        var name = $"#published_{suffix}";
        var folder = $"/published_{suffix}";
        await RegisterDatasetAsync(
            name,
            folder,
            DatasetAccessLevel.Private,
            createdBy: userId);
        var id = await GetDatasetIdAsync(name, folder);

        Assert.Equal(HttpStatusCode.OK, (await AuthGet(publisherToken, $"/api/datasets/{id}")).StatusCode);
        var patchRes = await AuthPatch(publisherToken, $"/api/datasets/{id}", new { ttl = "2h" });
        Assert.Equal(HttpStatusCode.OK, patchRes.StatusCode);
    }

    /// <summary>
    /// Orphaning a private dataset — deleting the report that owned it — must leave access resting
    /// on an explicit, revocable grant and nothing else.
    ///
    /// This assertion changed when dataset authorship stopped being standing permission. It used to
    /// prove that nulling <c>OwningReportId</c>/<c>CreatedBy</c> denied the former report owner,
    /// because access was <em>derived</em> from those columns. It no longer is: the report's author
    /// was granted Owner in <c>DatasetUserAcls</c> when the dataset was registered, and deleting the
    /// report does not delete that grant. That is deliberate — the alternative leaves an orphaned
    /// private dataset reachable by administrators only, which is the outcome
    /// <c>AdminController</c>'s ownership-transfer path exists to avoid.
    ///
    /// The security property is preserved and strengthened: access is now revocable. Deleting the
    /// user cascades their grants away, and revoking the grant directly denies them immediately —
    /// neither of which an identity comparison could do. See
    /// <see cref="DatasetAuthorshipRevocationTests"/>.
    /// </summary>
    [Fact]
    [Trait("Category", "Smoke.Security")]
    public async Task PrivateDataset_OrphanedOwningReport_LeavesOnlyARevocableGrant()
    {
        var adminToken = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var username = $"orphan_owner_{suffix}";

        var userRes = await AuthPost(adminToken, "/api/admin/users", new
        {
            username,
            email = $"{username}@test.local",
            password = "Orphan@1234!",
            role = "Viewer"
        });
        userRes.EnsureSuccessStatusCode();
        var userId = (await userRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["id"]!.GetValue<int>();
        var ownerToken = await LoginAndChangePasswordAsync(username, "Orphan@1234!", "Orphan@Changed9!");

        int reportId;
        string folderPath;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var folder = new Folder
            {
                Name = $"orphan_folder_{suffix}",
                Path = $"/orphan_folder_{suffix}",
                OwnerId = userId
            };
            db.Folders.Add(folder);
            await db.SaveChangesAsync();

            var report = new Report
            {
                FolderId = folder.Id,
                Name = $"orphan_report_{suffix}",
                ScriptPath = $"orphan_report_{suffix}.rptsql",
                ScriptLastModified = DateTime.UtcNow,
                CreatedBy = userId
            };
            db.Reports.Add(report);
            await db.SaveChangesAsync();
            reportId = report.Id;
            folderPath = folder.Path;
        }

        var name = $"#orphan_private_{suffix}";
        await RegisterDatasetAsync(
            name,
            folderPath,
            DatasetAccessLevel.Private,
            owningReportId: reportId);
        var datasetId = await GetDatasetIdAsync(name, folderPath);

        Assert.Equal(HttpStatusCode.OK, (await AuthGet(ownerToken, $"/api/datasets/{datasetId}")).StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            db.Reports.Remove(await db.Reports.SingleAsync(r => r.Id == reportId));
            await db.SaveChangesAsync();

            var orphan = await db.Datasets.SingleAsync(d => d.Id == datasetId);
            Assert.Null(orphan.OwningReportId);
            Assert.Null(orphan.CreatedBy);
        }

        // Every authorship link is gone, so whatever access remains can only come from a grant.
        Assert.Equal(HttpStatusCode.OK, (await AuthGet(ownerToken, $"/api/datasets/{datasetId}")).StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var grants = await db.DatasetUserAcls
                .Where(a => a.DatasetId == datasetId && a.UserId == userId)
                .ToListAsync();
            Assert.Single(grants);
            db.DatasetUserAcls.RemoveRange(grants);
            await db.SaveChangesAsync();
        }

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await AuthGet(ownerToken, $"/api/datasets/{datasetId}")).StatusCode);
    }

    [Fact]
    [Trait("Category", "Smoke.Security")]
    public async Task HttpAndRegistryAuthorization_DecisionsMatchAcrossDatasetIdentityMatrix()
    {
        var adminToken = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        async Task<(int Id, string Token)> CreateUser(string role)
        {
            var username = $"{role}_{suffix}";
            var initial = $"{role}A@1234!";
            var changed = $"{role}B@5678!";
            var response = await AuthPost(adminToken, "/api/admin/users", new
            {
                username,
                email = $"{username}@test.local",
                password = initial,
                role = "Viewer"
            });
            response.EnsureSuccessStatusCode();
            var id = (await response.Content.ReadFromJsonAsync<JsonObject>(_json))!["id"]!.GetValue<int>();
            return (id, await LoginAndChangePasswordAsync(username, initial, changed));
        }

        var reader = await CreateUser("matrix_reader");
        var owner = await CreateUser("matrix_owner");
        var granted = await CreateUser("matrix_granted");
        var outsider = await CreateUser("matrix_outsider");

        int folderId;
        string folderPath;
        int readerGroupId;
        int grantedGroupId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var folder = new Folder
            {
                Name = $"matrix_folder_{suffix}",
                Path = $"/matrix_folder_{suffix}",
                OwnerId = owner.Id
            };
            var readerGroup = new Group { Name = $"matrix_readers_{suffix}" };
            var grantedGroup = new Group { Name = $"matrix_grants_{suffix}" };
            db.AddRange(folder, readerGroup, grantedGroup);
            await db.SaveChangesAsync();

            db.UserGroups.AddRange(
                new UserGroup { UserId = reader.Id, GroupId = readerGroup.Id },
                new UserGroup { UserId = granted.Id, GroupId = grantedGroup.Id });
            db.FolderAcls.Add(new FolderAcl
            {
                FolderId = folder.Id,
                GroupId = readerGroup.Id,
                Permission = FolderPermission.Read
            });
            await db.SaveChangesAsync();

            folderId = folder.Id;
            folderPath = folder.Path;
            readerGroupId = readerGroup.Id;
            grantedGroupId = grantedGroup.Id;
        }

        var publicName = $"#matrix_public_{suffix}";
        var privateName = $"#matrix_private_{suffix}";
        await RegisterDatasetAsync(publicName, folderPath, DatasetAccessLevel.Public);
        await RegisterDatasetAsync(
            privateName,
            folderPath,
            DatasetAccessLevel.Private,
            createdBy: owner.Id);
        var publicId = await GetDatasetIdAsync(publicName, folderPath);
        var privateId = await GetDatasetIdAsync(privateName, folderPath);
        await AddDatasetAclAsync(privateId, grantedGroupId, DatasetPermission.Viewer);

        using var registryScope = _factory.Services.CreateScope();
        var registry = registryScope.ServiceProvider.GetRequiredService<IDatasetRegistry>();

        async Task AssertParity(
            string token,
            string callerContext,
            int datasetId,
            string datasetName,
            bool expected)
        {
            var http = await AuthGet(token, $"/api/datasets/{datasetId}");
            Assert.Equal(expected ? HttpStatusCode.OK : HttpStatusCode.Forbidden, http.StatusCode);
            Assert.Equal(expected, await registry.Lookup(datasetName, callerContext) is not null);
        }

        await AssertParity(reader.Token, $"UserId={reader.Id}", publicId, publicName, expected: true);
        await AssertParity(outsider.Token, $"UserId={outsider.Id}", publicId, publicName, expected: false);
        await AssertParity(owner.Token, $"UserId={owner.Id}", privateId, privateName, expected: true);
        await AssertParity(granted.Token, $"UserId={granted.Id}", privateId, privateName, expected: true);
        await AssertParity(outsider.Token, $"UserId={outsider.Id}", privateId, privateName, expected: false);

        int adminId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            adminId = (await db.Users.SingleAsync(u => u.UserName == "admin")).Id;
        }
        await AssertParity(
            adminToken,
            $"UserId={adminId};IsAdmin=true",
            privateId,
            privateName,
            expected: true);

        Assert.True(folderId > 0);
        Assert.True(readerGroupId > 0);
    }

    // ── 3. GET /api/datasets/{id}/rows — column schema preview ────────────────

    [Fact]
    public async Task GetRows_ReturnsColumnSchemaAndRowCount()
    {
        var token = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name = $"#rows_{suffix}";
        var folder = $"/rows_{suffix}";
        var schema = """[{"name":"ProductId","type":"INT"},{"name":"Revenue","type":"DECIMAL"}]""";

        await RegisterDatasetAsync(name, folder, DatasetAccessLevel.Public, rowCount: 7, columnSchema: schema);
        var id = await GetDatasetIdAsync(name, folder);

        var res = await AuthGet(token, $"/api/datasets/{id}/rows");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var dto = await res.Content.ReadFromJsonAsync<JsonObject>(_json);

        Assert.Equal(7L, dto!["rowCount"]!.GetValue<long>());
        var cols = dto["columns"]!.AsArray();
        Assert.Equal(2, cols.Count);
        Assert.Equal("ProductId", cols[0]!["name"]!.GetValue<string>());
        Assert.Equal("INT", cols[0]!["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task GetRows_ReturnsEmptyColumnsWhenSchemaIsNull()
    {
        var token = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name = $"#noschema_{suffix}";
        var folder = $"/noschema_{suffix}";

        await RegisterDatasetAsync(name, folder, DatasetAccessLevel.Public, rowCount: 100);
        var id = await GetDatasetIdAsync(name, folder);

        var res = await AuthGet(token, $"/api/datasets/{id}/rows");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var dto = await res.Content.ReadFromJsonAsync<JsonObject>(_json);

        Assert.Equal(100L, dto!["rowCount"]!.GetValue<long>());
        Assert.Empty(dto["columns"]!.AsArray());
    }

    // ── 4. POST /api/datasets/{id}/refresh ────────────────────────────────────

    [Fact]
    [Trait("Category", "Smoke.Portal")]
    public async Task Refresh_MarksDatasetStale()
    {
        var token = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name = $"#refresh_{suffix}";
        var folder = $"/refresh_{suffix}";

        await RegisterDatasetAsync(name, folder, DatasetAccessLevel.Public,
            lastRefresh: DateTime.UtcNow);
        var id = await GetDatasetIdAsync(name, folder);

        // Dataset is not stale yet (LastRefresh is set, no TTL → not stale)
        var before = await (await AuthGet(token, $"/api/datasets/{id}")).Content
            .ReadFromJsonAsync<JsonObject>(_json);
        Assert.False(before!["isStale"]!.GetValue<bool>());

        var refreshRes = await AuthPost(token, $"/api/datasets/{id}/refresh", new { });
        Assert.Equal(HttpStatusCode.Accepted, refreshRes.StatusCode);

        // After SetStale(), LastRefresh is null → IsStale = true
        var after = await (await AuthGet(token, $"/api/datasets/{id}")).Content
            .ReadFromJsonAsync<JsonObject>(_json);
        Assert.True(after!["isStale"]!.GetValue<bool>());
    }

    [Fact]
    [Trait("Category", "Smoke.Security")]
    public async Task Refresh_ForbidsViewerAccess()
    {
        var token = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name = $"#refreshforbid_{suffix}";
        var folder = $"/refreshforbid_{suffix}";

        await RegisterDatasetAsync(name, folder, DatasetAccessLevel.Public);
        var id = await GetDatasetIdAsync(name, folder);

        var userRes = await AuthPost(token, "/api/admin/users", new
        {
            username = $"viewer_{suffix}",
            email = $"viewer_{suffix}@test.local",
            password = "View@1234!",
            role = "Viewer"
        });
        Assert.Equal(HttpStatusCode.Created, userRes.StatusCode);
        var viewerToken = await LoginAndChangePasswordAsync($"viewer_{suffix}", "View@1234!", "View@Changed9!");

        // Viewer gets DatasetPermission.Viewer on public dataset — cannot refresh
        var res = await AuthPost(viewerToken, $"/api/datasets/{id}/refresh", new { });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    [Trait("Category", "Smoke.Security")]
    public async Task RefreshPermission_AllowsRefreshWithoutMetadataEdit()
    {
        var adminToken = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var username = $"refresher_{suffix}";
        var name = $"#refresh_only_{suffix}";
        var folder = $"/refresh_only_{suffix}";

        var userRes = await AuthPost(adminToken, "/api/admin/users", new
        {
            username,
            email = $"{username}@test.local",
            password = "Refresh@1234!",
            role = "Viewer"
        });
        userRes.EnsureSuccessStatusCode();
        var userId = (await userRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["id"]!.GetValue<int>();
        var userToken = await LoginAndChangePasswordAsync(
            username,
            "Refresh@1234!",
            "Refresh@Changed9!");

        var groupRes = await AuthPost(
            adminToken,
            "/api/admin/groups",
            new { name = $"refreshers_{suffix}" });
        groupRes.EnsureSuccessStatusCode();
        var groupId = (await groupRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["id"]!.GetValue<int>();
        (await AuthPost(adminToken, $"/api/admin/groups/{groupId}/members", new { userId }))
            .EnsureSuccessStatusCode();

        await RegisterDatasetAsync(name, folder, DatasetAccessLevel.Private, lastRefresh: DateTime.UtcNow);
        var id = await GetDatasetIdAsync(name, folder);
        var grantRes = await AuthPost(
            adminToken,
            $"/api/datasets/{id}/acl",
            new { groupId, permission = "Refresh" });
        Assert.Equal(HttpStatusCode.OK, grantRes.StatusCode);
        userToken = await LoginExistingUserAsync(username, "Refresh@Changed9!");

        var refreshRes = await AuthPost(userToken, $"/api/datasets/{id}/refresh", new { });
        Assert.Equal(HttpStatusCode.Accepted, refreshRes.StatusCode);

        var editRes = await AuthPatch(userToken, $"/api/datasets/{id}", new { ttl = "2h" });
        Assert.Equal(HttpStatusCode.Forbidden, editRes.StatusCode);
    }

    // ── 5. PATCH /api/datasets/{id} — update metadata ─────────────────────────

    [Fact]
    public async Task Update_ChangesAccessLevelAndTtl()
    {
        var token = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name = $"#update_{suffix}";
        var folder = $"/update_{suffix}";

        await RegisterDatasetAsync(name, folder, DatasetAccessLevel.Private);
        var id = await GetDatasetIdAsync(name, folder);

        var patchRes = await AuthPatch(token, $"/api/datasets/{id}", new
        {
            accessLevel = "Public",
            ttl = "2h"
        });
        Assert.Equal(HttpStatusCode.OK, patchRes.StatusCode);
        var dto = await patchRes.Content.ReadFromJsonAsync<JsonObject>(_json);

        Assert.Equal("Public", dto!["accessLevel"]!.GetValue<string>());
        Assert.Equal("2h", dto["ttl"]!.GetValue<string>());
    }

    [Fact]
    public async Task Update_RejectsInvalidAccessLevel()
    {
        var token = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name = $"#badup_{suffix}";
        var folder = $"/badup_{suffix}";

        await RegisterDatasetAsync(name, folder, DatasetAccessLevel.Public);
        var id = await GetDatasetIdAsync(name, folder);

        var patchRes = await AuthPatch(token, $"/api/datasets/{id}", new
        {
            accessLevel = "Restricted"
        });
        Assert.Equal(HttpStatusCode.BadRequest, patchRes.StatusCode);
    }

    [Fact]
    [Trait("Category", "Smoke.Security")]
    public async Task Update_AccessLevelChangeRequiresManage_NotEdit()
    {
        var adminToken = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var username = $"dseditor_{suffix}";
        var name = $"#lvl_{suffix}";
        var folder = $"/lvl_{suffix}";

        var userRes = await AuthPost(adminToken, "/api/admin/users", new
        {
            username,
            email = $"{username}@test.local",
            password = "Editor@1234!",
            role = "Viewer"
        });
        userRes.EnsureSuccessStatusCode();
        var userId = (await userRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["id"]!.GetValue<int>();
        await LoginAndChangePasswordAsync(username, "Editor@1234!", "Editor@Changed9!");

        var groupRes = await AuthPost(
            adminToken,
            "/api/admin/groups",
            new { name = $"dseditors_{suffix}" });
        groupRes.EnsureSuccessStatusCode();
        var groupId = (await groupRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["id"]!.GetValue<int>();
        (await AuthPost(adminToken, $"/api/admin/groups/{groupId}/members", new { userId }))
            .EnsureSuccessStatusCode();

        await RegisterDatasetAsync(name, folder, DatasetAccessLevel.Private);
        var id = await GetDatasetIdAsync(name, folder);
        var grantRes = await AuthPost(
            adminToken,
            $"/api/datasets/{id}/acl",
            new { groupId, permission = "Editor" });
        Assert.Equal(HttpStatusCode.OK, grantRes.StatusCode);
        var userToken = await LoginExistingUserAsync(username, "Editor@Changed9!");

        // Editor may update metadata...
        var ttlRes = await AuthPatch(userToken, $"/api/datasets/{id}", new { ttl = "2h" });
        Assert.Equal(HttpStatusCode.OK, ttlRes.StatusCode);

        // ...and may re-state the current access level (no exposure change)...
        var sameRes = await AuthPatch(userToken, $"/api/datasets/{id}", new { accessLevel = "Private" });
        Assert.Equal(HttpStatusCode.OK, sameRes.StatusCode);

        // ...but widening Private→Public is an ACL-class operation requiring Manage.
        var flipRes = await AuthPatch(userToken, $"/api/datasets/{id}", new { accessLevel = "Public" });
        Assert.Equal(HttpStatusCode.Forbidden, flipRes.StatusCode);

        var detail = await AuthGet(adminToken, $"/api/datasets/{id}");
        var dto = await detail.Content.ReadFromJsonAsync<JsonObject>(_json);
        Assert.Equal("Private", dto!["accessLevel"]!.GetValue<string>());
    }

    [Fact]
    [Trait("Category", "Smoke.Security")]
    public async Task Move_RequiresManageOnSourceAndDestination_AndPreservesFileIdentity()
    {
        var adminToken = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var username = $"mover_{suffix}";

        var sourceRes = await AuthPost(adminToken, "/api/folders", new
        {
            name = $"move_source_{suffix}",
            parentId = (int?)null
        });
        sourceRes.EnsureSuccessStatusCode();
        var sourceFolderId = (await sourceRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["id"]!.GetValue<int>();

        var destinationRes = await AuthPost(adminToken, "/api/folders", new
        {
            name = $"move_destination_{suffix}",
            parentId = (int?)null
        });
        destinationRes.EnsureSuccessStatusCode();
        var destinationFolderId =
            (await destinationRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["id"]!.GetValue<int>();

        string sourcePath;
        string destinationPath;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            sourcePath = (await db.Folders.SingleAsync(f => f.Id == sourceFolderId)).Path;
            destinationPath = (await db.Folders.SingleAsync(f => f.Id == destinationFolderId)).Path;
        }

        var userRes = await AuthPost(adminToken, "/api/admin/users", new
        {
            username,
            email = $"{username}@test.local",
            password = "Mover@1234!",
            role = "Viewer"
        });
        userRes.EnsureSuccessStatusCode();
        var userId = (await userRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["id"]!.GetValue<int>();
        var userToken = await LoginAndChangePasswordAsync(username, "Mover@1234!", "Mover@Changed9!");

        var groupRes = await AuthPost(adminToken, "/api/admin/groups", new { name = $"movers_{suffix}" });
        groupRes.EnsureSuccessStatusCode();
        var groupId = (await groupRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["id"]!.GetValue<int>();
        (await AuthPost(adminToken, $"/api/admin/groups/{groupId}/members", new { userId }))
            .EnsureSuccessStatusCode();
        await AddFolderAclAsync(sourceFolderId, groupId, FolderPermission.Manage);
        userToken = await LoginExistingUserAsync(username, "Mover@Changed9!");

        var name = $"#move_{suffix}";
        await RegisterDatasetAsync(name, sourcePath, DatasetAccessLevel.Public);
        var datasetId = await GetDatasetIdAsync(name, sourcePath);

        string originalFilePath;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            originalFilePath = (await db.Datasets.SingleAsync(d => d.Id == datasetId)).ParquetFilePath;
        }

        var denied = await AuthPost(
            userToken,
            $"/api/datasets/{datasetId}/move",
            new { destinationFolderId });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        await AddFolderAclAsync(destinationFolderId, groupId, FolderPermission.Manage);
        userToken = await LoginExistingUserAsync(username, "Mover@Changed9!");
        var moved = await AuthPost(
            userToken,
            $"/api/datasets/{datasetId}/move",
            new { destinationFolderId });
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        var dto = await moved.Content.ReadFromJsonAsync<JsonObject>(_json);
        Assert.Equal(destinationPath, dto!["folderPath"]!.GetValue<string>());

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var dataset = await db.Datasets.SingleAsync(d => d.Id == datasetId);
            Assert.Equal(destinationFolderId, dataset.FolderId);
            Assert.Equal(destinationPath, dataset.FolderPath);
            Assert.Equal(originalFilePath, dataset.ParquetFilePath);
            Assert.True(await db.AuditLogs.AnyAsync(a =>
                a.Action == "MOVE_DATASET" &&
                a.ResourceId == datasetId.ToString() &&
                a.Detail != null &&
                a.Detail.Contains(sourcePath) &&
                a.Detail.Contains(destinationPath)));
        }
    }

    // ── 6. DELETE /api/datasets/{id} ──────────────────────────────────────────

    [Fact]
    public async Task Delete_RemovesDataset()
    {
        var token = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name = $"#del_{suffix}";
        var folder = $"/del_{suffix}";

        await RegisterDatasetAsync(name, folder, DatasetAccessLevel.Public);
        var id = await GetDatasetIdAsync(name, folder);
        string managedPath;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            managedPath = (await db.Datasets.SingleAsync(d => d.Id == id)).ParquetFilePath;
            await File.WriteAllTextAsync(managedPath, "managed dataset cache");
        }

        var delRes = await AuthDelete(token, $"/api/datasets/{id}");
        Assert.Equal(HttpStatusCode.NoContent, delRes.StatusCode);
        Assert.False(File.Exists(managedPath));

        var afterRes = await AuthGet(token, $"/api/datasets/{id}");
        Assert.Equal(HttpStatusCode.NotFound, afterRes.StatusCode);
    }

    [Fact]
    public async Task DeleteReport_RemovesOwnedDatasetFiles()
    {
        var token = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var folderRes = await AuthPost(token, "/api/folders", new
        {
            name = $"report_cleanup_{suffix}",
            parentId = (int?)null
        });
        folderRes.EnsureSuccessStatusCode();
        var folderId = (await folderRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["id"]!.GetValue<int>();

        string folderPath;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            folderPath = (await db.Folders.SingleAsync(f => f.Id == folderId)).Path;
        }

        var scriptPath = Path.Combine(_factory.TempDir, "scripts", $"report_cleanup_{suffix}.rptsql");
        await File.WriteAllTextAsync(scriptPath, "PRINT 'cleanup';");
        var reportRes = await AuthPost(token, "/api/reports", new
        {
            folderId,
            name = $"report_cleanup_{suffix}",
            description = "",
            scriptPath
        });
        reportRes.EnsureSuccessStatusCode();
        var reportId = (await reportRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["id"]!.GetValue<int>();

        var datasetName = $"#report_cleanup_{suffix}";
        await RegisterDatasetAsync(
            datasetName,
            folderPath,
            DatasetAccessLevel.Private,
            owningReportId: reportId);
        var datasetId = await GetDatasetIdAsync(datasetName, folderPath);

        string managedPath;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            managedPath = (await db.Datasets.SingleAsync(d => d.Id == datasetId)).ParquetFilePath;
            await File.WriteAllTextAsync(managedPath, "managed report dataset");
        }

        var deleteRes = await AuthDelete(token, $"/api/reports/{reportId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteRes.StatusCode);
        Assert.False(File.Exists(managedPath));

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<PortalDbContext>();
        Assert.False(await verifyDb.Datasets.AnyAsync(d => d.Id == datasetId));
    }

    [Fact]
    public async Task DeleteReport_WithAttachedRefreshJob_Conflicts()
    {
        var token = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var folderRes = await AuthPost(token, "/api/folders", new
        {
            name = $"report_job_guard_{suffix}",
            parentId = (int?)null
        });
        folderRes.EnsureSuccessStatusCode();
        var folderId = (await folderRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["id"]!.GetValue<int>();

        var scriptPath = Path.Combine(_factory.TempDir, "scripts", $"report_job_guard_{suffix}.rptsql");
        await File.WriteAllTextAsync(scriptPath, "PRINT 'guard';");
        var reportRes = await AuthPost(token, "/api/reports", new
        {
            folderId,
            name = $"report_job_guard_{suffix}",
            description = "",
            scriptPath
        });
        reportRes.EnsureSuccessStatusCode();
        var reportId = (await reportRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["id"]!.GetValue<int>();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            db.ReportJobLinks.Add(new ReportJobLink
            {
                ReportId = reportId,
                OrchestratorAlias = "prod_orchestrator",
                JobName = $"refresh_v2_{suffix}"
            });
            await db.SaveChangesAsync();
        }

        var deleteRes = await AuthDelete(token, $"/api/reports/{reportId}?cascade=true");

        Assert.Equal(HttpStatusCode.Conflict, deleteRes.StatusCode);
        var body = (await deleteRes.Content.ReadFromJsonAsync<JsonObject>(_json))!;
        Assert.Contains("attached refresh jobs", body["error"]!.GetValue<string>());
        Assert.Contains(body["refreshJobs"]!.AsArray(), n =>
            n!.GetValue<string>() == $"prod_orchestrator:refresh_v2_{suffix}");

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<PortalDbContext>();
        Assert.False((await verifyDb.Reports.SingleAsync(r => r.Id == reportId)).IsDeleted);
    }

    [Fact]
    [Trait("Category", "Smoke.Security")]
    public async Task Delete_ForbidsEditorAccess()
    {
        var adminToken = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name = $"#delforbid_{suffix}";
        var folder = $"/delforbid_{suffix}";

        await RegisterDatasetAsync(name, folder, DatasetAccessLevel.Public);
        var id = await GetDatasetIdAsync(name, folder);

        // Create a user and give them Editor ACL
        var userRes = await AuthPost(adminToken, "/api/admin/users", new
        {
            username = $"editor_{suffix}",
            email = $"editor_{suffix}@test.local",
            password = "Edit@1234!",
            role = "Viewer"
        });
        Assert.Equal(HttpStatusCode.Created, userRes.StatusCode);
        var editorId = (await userRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["id"]!.GetValue<int>();
        var editorToken = await LoginAndChangePasswordAsync($"editor_{suffix}", "Edit@1234!", "Edit@Changed9!");

        // Create group, add user, grant Editor ACL
        var groupRes = await AuthPost(adminToken, "/api/admin/groups", new { name = $"grp_{suffix}" });
        groupRes.EnsureSuccessStatusCode();
        var groupId = (await groupRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["id"]!.GetValue<int>();

        await AuthPost(adminToken, $"/api/admin/groups/{groupId}/members", new { userId = editorId });
        await AddDatasetAclAsync(id, groupId, DatasetPermission.Editor);
        editorToken = await LoginExistingUserAsync($"editor_{suffix}", "Edit@Changed9!");

        // Editor can view but cannot delete (CanManage requires Owner)
        var delRes = await AuthDelete(editorToken, $"/api/datasets/{id}");
        Assert.Equal(HttpStatusCode.Forbidden, delRes.StatusCode);
    }

    // ── 7. ACL management ─────────────────────────────────────────────────────

    [Fact]
    public async Task Acl_GrantAndRevoke_RoundTrips()
    {
        var adminToken = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name = $"#acl_{suffix}";
        var folder = $"/acl_{suffix}";

        await RegisterDatasetAsync(name, folder, DatasetAccessLevel.Private);
        var id = await GetDatasetIdAsync(name, folder);

        // Create a group
        var groupRes = await AuthPost(adminToken, "/api/admin/groups", new { name = $"aclgrp_{suffix}" });
        groupRes.EnsureSuccessStatusCode();
        var groupId = (await groupRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["id"]!.GetValue<int>();

        // Grant permission
        var grantRes = await AuthPost(adminToken, $"/api/datasets/{id}/acl", new
        {
            groupId = groupId,
            permission = "Viewer"
        });
        Assert.Equal(HttpStatusCode.OK, grantRes.StatusCode);

        // List ACLs — should contain the new entry
        var listRes = await AuthGet(adminToken, $"/api/datasets/{id}/acl");
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
        var aclList = await listRes.Content.ReadFromJsonAsync<JsonArray>(_json);
        Assert.Contains(aclList!, a => a!["groupId"]!.GetValue<int>() == groupId
                                     && a["permission"]!.GetValue<string>() == "Viewer");

        // Upgrade permission
        var upgradeRes = await AuthPost(adminToken, $"/api/datasets/{id}/acl", new
        {
            groupId = groupId,
            permission = "Editor"
        });
        Assert.Equal(HttpStatusCode.OK, upgradeRes.StatusCode);

        var listRes2 = await AuthGet(adminToken, $"/api/datasets/{id}/acl");
        var aclList2 = await listRes2.Content.ReadFromJsonAsync<JsonArray>(_json);
        Assert.Contains(aclList2!, a => a!["groupId"]!.GetValue<int>() == groupId
                                       && a["permission"]!.GetValue<string>() == "Editor");

        // Revoke — ACL mutations return 200 with the dataset's bumped version (ETag) so the
        // client can chain further versioned mutations.
        var revokeRes = await AuthDelete(adminToken, $"/api/datasets/{id}/acl/{groupId}");
        Assert.Equal(HttpStatusCode.OK, revokeRes.StatusCode);

        var listRes3 = await AuthGet(adminToken, $"/api/datasets/{id}/acl");
        var aclList3 = await listRes3.Content.ReadFromJsonAsync<JsonArray>(_json);
        Assert.DoesNotContain(aclList3!, a => a!["groupId"]!.GetValue<int>() == groupId);
    }

    // ── 8. Phase 6 — Portal-Triggered Refresh ────────────────────────────────

    [Fact]
    public async Task RefreshJobRegistration_UpsertsNormalizedReportJobLink()
    {
        var token = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var folderRes = await AuthPost(token, "/api/folders", new
        {
            name = $"schedule_{suffix}",
            parentId = (int?)null
        });
        folderRes.EnsureSuccessStatusCode();
        var folderId = (await folderRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["id"]!.GetValue<int>();

        var scriptPath = Path.Combine(_factory.TempDir, "scripts", $"schedule_{suffix}.rptsql");
        await File.WriteAllTextAsync(scriptPath, "PRINT 'scheduled dataset owner';");
        var reportRes = await AuthPost(token, "/api/reports", new
        {
            folderId,
            name = $"schedule_{suffix}",
            description = "",
            scriptPath
        });
        reportRes.EnsureSuccessStatusCode();
        var reportId = (await reportRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["id"]!.GetValue<int>();
        var jobName = $"portal-refresh:prod_orchestrator:{reportId}";

        using (var scope = _factory.Services.CreateScope())
        {
            var registry = scope.ServiceProvider.GetRequiredService<IDatasetRegistry>();
            await registry.RegisterRefreshJobAsync(reportId, jobName, "5m");
            await registry.RegisterRefreshJobAsync(reportId, jobName, "10m");
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var link = Assert.Single(await db.ReportJobLinks
                .Where(j => j.JobName == jobName)
                .ToListAsync());
            Assert.Equal(reportId, link.ReportId);
            Assert.Equal("prod_orchestrator", link.OrchestratorAlias);
        }
    }

    [Fact]
    public async Task DeleteRefreshJob_RemovesReportJobLinkMappings()
    {
        var token = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var folderRes = await AuthPost(token, "/api/folders", new
        {
            name = $"drop_refresh_{suffix}",
            parentId = (int?)null
        });
        folderRes.EnsureSuccessStatusCode();
        var folderId = (await folderRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["id"]!.GetValue<int>();

        var scriptPath = Path.Combine(_factory.TempDir, "scripts", $"drop_refresh_{suffix}.rptsql");
        await File.WriteAllTextAsync(scriptPath, "PRINT 'drop refresh job';");
        var reportName = $"drop_refresh_{suffix}";
        var reportRes = await AuthPost(token, "/api/reports", new
        {
            folderId,
            name = reportName,
            description = "",
            scriptPath
        });
        reportRes.EnsureSuccessStatusCode();
        var reportId = (await reportRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["id"]!.GetValue<int>();
        var jobName = $"portal-refresh:prod_orchestrator:{reportId}";

        using (var scope = _factory.Services.CreateScope())
        {
            var registry = scope.ServiceProvider.GetRequiredService<IDatasetRegistry>();
            await registry.RegisterRefreshJobAsync(reportId, jobName, "0 2 * * *");
        }

        var deleteRes = await AuthDelete(token, $"/api/subscriptions/refresh-jobs/{Uri.EscapeDataString(reportName)}");
        Assert.Equal(HttpStatusCode.NoContent, deleteRes.StatusCode);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<PortalDbContext>();
        Assert.False(await verifyDb.ReportJobLinks.AnyAsync(j => j.ReportId == reportId));
    }

    [Fact]
    [Trait("Category", "Smoke.Portal")]
    public async Task Refresh_WithOwningReport_Returns202AndJobId()
    {
        var token = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        // Create a folder, report, and script file
        var folderRes = await AuthPost(token, "/api/folders", new { name = $"rf_{suffix}", parentId = (int?)null });
        folderRes.EnsureSuccessStatusCode();
        var folderId = (await folderRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["id"]!.GetValue<int>();

        var scriptPath = Path.Combine(_factory.TempDir, "scripts", $"rf_{suffix}.rptsql");
        await File.WriteAllTextAsync(scriptPath, """
            CREATE VISUAL Total AS CARD (
                SOURCE = (SELECT 42 AS Answer),
                MAPPINGS (VALUE = Answer)
            );
            """);

        var rptRes = await AuthPost(token, "/api/reports", new
        {
            folderId,
            name = $"rf_{suffix}",
            description = "",
            scriptPath
        });
        rptRes.EnsureSuccessStatusCode();
        var reportId = (await rptRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["id"]!.GetValue<int>();

        // Register a dataset owned by that report
        var dsName = $"#rf_{suffix}";
        var dsFolder = $"/rf_{suffix}";
        await RegisterDatasetAsync(dsName, dsFolder, DatasetAccessLevel.Public, owningReportId: reportId);
        var dsId = await GetDatasetIdAsync(dsName, dsFolder);

        // POST refresh — should 202 with triggered=true and a jobId
        var res = await AuthPost(token, $"/api/datasets/{dsId}/refresh", new { });
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);

        var body = await res.Content.ReadFromJsonAsync<JsonObject>(_json);
        Assert.True(body!["triggered"]!.GetValue<bool>());
        var jobId = body["jobId"]!.GetValue<string>();
        Assert.False(string.IsNullOrWhiteSpace(jobId));

        // The Location header should point to the job
        Assert.Contains($"/api/jobs/{jobId}", res.Headers.Location?.ToString() ?? "");
    }

    [Fact]
    public async Task Refresh_WithoutOwningReport_Returns202AndTriggeredFalse()
    {
        var token = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name = $"#noreport_{suffix}";
        var folder = $"/noreport_{suffix}";

        // Dataset with no owning report
        await RegisterDatasetAsync(name, folder, DatasetAccessLevel.Public);
        var id = await GetDatasetIdAsync(name, folder);

        var res = await AuthPost(token, $"/api/datasets/{id}/refresh", new { });
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);

        var body = await res.Content.ReadFromJsonAsync<JsonObject>(_json);
        Assert.False(body!["triggered"]!.GetValue<bool>());
        Assert.Null(body["jobId"]);
    }

    [Fact]
    [Trait("Category", "Smoke.Portal")]
    public async Task RefreshStatus_ReturnsIdleWhenNoJobRunning()
    {
        var token = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name = $"#status_{suffix}";
        var folder = $"/status_{suffix}";

        await RegisterDatasetAsync(name, folder, DatasetAccessLevel.Public,
            lastRefresh: DateTime.UtcNow);
        var id = await GetDatasetIdAsync(name, folder);

        var res = await AuthGet(token, $"/api/datasets/{id}/refresh-status");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await res.Content.ReadFromJsonAsync<JsonObject>(_json);
        Assert.Equal("Idle", body!["status"]!.GetValue<string>());
        Assert.Null(body["jobId"]);
        Assert.False(body["isStale"]!.GetValue<bool>());
    }

    [Fact]
    [Trait("Category", "Smoke.Security")]
    public async Task RefreshStatus_ForbidsUnauthorizedAccess()
    {
        var adminToken = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name = $"#statusprv_{suffix}";
        var folder = $"/statusprv_{suffix}";

        // Private dataset — unrelated user cannot see status
        await RegisterDatasetAsync(name, folder, DatasetAccessLevel.Private);
        var id = await GetDatasetIdAsync(name, folder);

        var userRes = await AuthPost(adminToken, "/api/admin/users", new
        {
            username = $"statusoutsider_{suffix}",
            email = $"statusoutsider_{suffix}@test.local",
            password = "Out@Status1!",
            role = "Viewer"
        });
        Assert.Equal(HttpStatusCode.Created, userRes.StatusCode);
        var outsiderToken = await LoginAndChangePasswordAsync(
            $"statusoutsider_{suffix}", "Out@Status1!", "Out@Status2!");

        var res = await AuthGet(outsiderToken, $"/api/datasets/{id}/refresh-status");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task ExportData_CsvAndXlsx_StreamCorrectly()
    {
        var token = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name = $"#export_{suffix}_12345";
        var folder = $"/export_{suffix}";
        var schema = """[{"name":"ID","type":"INT"},{"name":"Name","type":"VARCHAR"}]""";

        // 1. Write the Parquet file
        var datasetFileName = $"export_{suffix}_12345.parquet";
        var datasetFile = Path.Combine(_factory.TempDir, "datasets", datasetFileName);

        var ds = new ETL_SQL.Connectors.Parquet.ParquetDataSource(
            ETL_SQL.Core.Common.SystemExecutionContext.Instance,
            datasetFile,
            new Dictionary<string, string>
            {
                ["ENCRYPT"] = "PASSWORD",
                ["PASSWORD"] = HostedPortalFactory.DefaultAtRestKey
            });
        var batch = new ETL_SQL.Data.DataTable();
        batch.ColumnNames.AddRange(new[] { "ID", "Name" });
        var r1 = new ETL_SQL.Data.Row(); r1["ID"] = 1L; r1["Name"] = "Alice";
        await batch.AddRowAsync(r1);
        await ds.WriteBatches(new[] { batch }.ToAsyncEnumerable());

        // 2. Register it in the Db
        await RegisterDatasetAsync(name, folder, DatasetAccessLevel.Public, rowCount: 1, columnSchema: schema, lastRefresh: DateTime.UtcNow);

        var id = await GetDatasetIdAsync(name, folder);

        // 3. Request CSV export
        var csvRes = await AuthGet(token, $"/api/datasets/{id}/data/export?format=csv");
        if (csvRes.StatusCode != HttpStatusCode.OK)
        {
            var errMsg = await csvRes.Content.ReadAsStringAsync();
            Assert.Fail($"Export failed: {csvRes.StatusCode} - {errMsg}");
        }
        Assert.StartsWith("text/csv", csvRes.Content.Headers.ContentType?.MediaType);
        var csvContent = await csvRes.Content.ReadAsStringAsync();
        Assert.Contains("ID,Name", csvContent);
        Assert.Contains("1,Alice", csvContent);

        // 4. Request XLSX export
        var xlsxRes = await AuthGet(token, $"/api/datasets/{id}/data/export?format=xlsx");
        Assert.Equal(HttpStatusCode.OK, xlsxRes.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", xlsxRes.Content.Headers.ContentType?.MediaType);
        var xlsxBytes = await xlsxRes.Content.ReadAsByteArrayAsync();
        Assert.NotEmpty(xlsxBytes);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task RegisterDatasetAsync(
        string name, string folder,
        DatasetAccessLevel accessLevel = DatasetAccessLevel.Public,
        long rowCount = 0, string? ttl = null, string? columnSchema = null,
        DateTime? lastRefresh = null, int? owningReportId = null,
        ETL_SQL.Core.DatasetEncryptionMode encryptionMode = ETL_SQL.Core.DatasetEncryptionMode.None,
        int? createdBy = null)
    {
        using var scope = _factory.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IDatasetRegistry>();

        await registry.RegisterOrUpdate(new DatasetMetadata
        {
            Name = name,
            FolderPath = folder,
            ParquetFilePath = $"{name.TrimStart('&', '#')}.parquet",
            SourceQuery = "SELECT 1",
            AccessLevel = accessLevel,
            EncryptionMode = encryptionMode,
            RowCount = rowCount,
            Ttl = ttl,
            ColumnSchema = columnSchema,
            LastRefresh = lastRefresh,
            OwningReportId = owningReportId,
            CreatedBy = createdBy
        });
    }

    private async Task<int> GetDatasetIdAsync(string name, string folder)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var d = await db.Datasets.SingleAsync(x => x.Name == name && x.FolderPath == folder);
        return d.Id;
    }

    private async Task AddDatasetAclAsync(int datasetId, int groupId, DatasetPermission permission)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        db.DatasetAcls.Add(new DatasetAcl
        {
            DatasetId = datasetId,
            GroupId = groupId,
            Permission = permission
        });
        await db.SaveChangesAsync();
    }

    private async Task AddFolderAclAsync(int folderId, int groupId, FolderPermission permission)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        db.FolderAcls.Add(new FolderAcl
        {
            FolderId = folderId,
            GroupId = groupId,
            Permission = permission
        });
        await db.SaveChangesAsync();
    }

    private async Task<string> LoginAndChangePasswordAsync(
        string username, string initialPassword, string newPassword)
    {
        var loginRes = await _client.PostAsJsonAsync("/api/auth/login", new { username, password = initialPassword });
        loginRes.EnsureSuccessStatusCode();
        var token = (await loginRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["token"]!.GetValue<string>();

        using var cpReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password");
        cpReq.Headers.Authorization = new("Bearer", token);
        cpReq.Content = JsonContent.Create(new { currentPassword = initialPassword, newPassword });
        (await _client.SendAsync(cpReq)).EnsureSuccessStatusCode();

        var reloginRes = await _client.PostAsJsonAsync("/api/auth/login", new { username, password = newPassword });
        reloginRes.EnsureSuccessStatusCode();
        return (await reloginRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["token"]!.GetValue<string>();
    }

    private async Task<string> LoginExistingUserAsync(string username, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { username, password });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonObject>(_json))!["token"]!.GetValue<string>();
    }

    private async Task<string> GetAdminTokenAsync()
    {
        await _tokenLock.WaitAsync();
        try
        {
            if (_adminToken is not null) return _adminToken;

            var loginRes = await _client.PostAsJsonAsync("/api/auth/login", new
            {
                username = "admin",
                password = "Admin@12345!"
            });
            loginRes.EnsureSuccessStatusCode();
            var token = (await loginRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["token"]!.GetValue<string>();

            using var cpReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password");
            cpReq.Headers.Authorization = new("Bearer", token);
            cpReq.Content = JsonContent.Create(new
            {
                currentPassword = "Admin@12345!",
                newPassword = "Admin@Tests99!"
            });
            (await _client.SendAsync(cpReq)).EnsureSuccessStatusCode();

            var reloginRes = await _client.PostAsJsonAsync("/api/auth/login", new
            {
                username = "admin",
                password = "Admin@Tests99!"
            });
            reloginRes.EnsureSuccessStatusCode();
            _adminToken = (await reloginRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["token"]!.GetValue<string>();

            return _adminToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private Task<HttpResponseMessage> AuthGet(string token, string url)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new("Bearer", token);
        return _client.SendAsync(req);
    }

    private async Task<HttpResponseMessage> AuthPost(string token, string url, object body)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new("Bearer", token);
        req.Content = JsonContent.Create(body);
        await IfMatchVersioning.StampAsync(_client, req, await GetAdminTokenAsync());
        return await _client.SendAsync(req);
    }

    private async Task<HttpResponseMessage> AuthPatch(string token, string url, object body)
    {
        var req = new HttpRequestMessage(HttpMethod.Patch, url);
        req.Headers.Authorization = new("Bearer", token);
        req.Content = JsonContent.Create(body);
        await IfMatchVersioning.StampAsync(_client, req, await GetAdminTokenAsync());
        return await _client.SendAsync(req);
    }

    private async Task<HttpResponseMessage> AuthDelete(string token, string url)
    {
        var req = new HttpRequestMessage(HttpMethod.Delete, url);
        req.Headers.Authorization = new("Bearer", token);
        await IfMatchVersioning.StampAsync(_client, req, await GetAdminTokenAsync());
        return await _client.SendAsync(req);
    }
}
