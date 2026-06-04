using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Core.Data;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.ReportPortal.Tests;

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
        _client  = factory.CreateClient();
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
        var token  = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var folder = $"/f_acl_{suffix}";

        await RegisterDatasetAsync($"#pub_{suffix}", folder, DatasetAccessLevel.Public);
        await RegisterDatasetAsync($"#prv_{suffix}", folder, DatasetAccessLevel.Private);

        // Create a viewer user and a group with access to the ACL dataset
        var userRes = await AuthPost(token, "/api/admin/users", new
        {
            username = $"dv_{suffix}",
            email    = $"dv_{suffix}@test.local",
            password = "Viewer@1234!",
            role     = "Viewer"
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

    // ── 2. GET /api/datasets/{id} — metadata panel ────────────────────────────

    [Fact]
    [Trait("Category", "Smoke.Portal")]
    public async Task GetById_ReturnsCorrectMetadata()
    {
        var token  = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name   = $"#meta_{suffix}";
        var folder = $"/meta_{suffix}";

        await RegisterDatasetAsync(name, folder, DatasetAccessLevel.Public, rowCount: 42, ttl: "1h",
            lastRefresh: DateTime.UtcNow);
        var id = await GetDatasetIdAsync(name, folder);

        var res = await AuthGet(token, $"/api/datasets/{id}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var dto = await res.Content.ReadFromJsonAsync<JsonObject>(_json);

        Assert.Equal(name,     dto!["name"]!.GetValue<string>());
        Assert.Equal("Public", dto["accessLevel"]!.GetValue<string>());
        Assert.Equal(42L,      dto["rowCount"]!.GetValue<long>());
        Assert.Equal("1h",     dto["ttl"]!.GetValue<string>());
        Assert.False(          dto["isStale"]!.GetValue<bool>());
    }

    [Fact]
    [Trait("Category", "Smoke.Security")]
    public async Task GetById_ForbidsPrivateDatasetForUnrelatedUser()
    {
        var token  = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name   = $"#priv_{suffix}";
        var folder = $"/priv_{suffix}";

        await RegisterDatasetAsync(name, folder, DatasetAccessLevel.Private);
        var id = await GetDatasetIdAsync(name, folder);

        var userRes = await AuthPost(token, "/api/admin/users", new
        {
            username = $"outsider_{suffix}",
            email    = $"outsider_{suffix}@test.local",
            password = "Out@1234!",
            role     = "Viewer"
        });
        Assert.Equal(HttpStatusCode.Created, userRes.StatusCode);
        var outsiderToken = await LoginAndChangePasswordAsync($"outsider_{suffix}", "Out@1234!", "Out@Changed9!");

        var res = await AuthGet(outsiderToken, $"/api/datasets/{id}");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    // ── 3. GET /api/datasets/{id}/rows — column schema preview ────────────────

    [Fact]
    public async Task GetRows_ReturnsColumnSchemaAndRowCount()
    {
        var token  = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name   = $"#rows_{suffix}";
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
        Assert.Equal("INT",       cols[0]!["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task GetRows_ReturnsEmptyColumnsWhenSchemaIsNull()
    {
        var token  = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name   = $"#noschema_{suffix}";
        var folder = $"/noschema_{suffix}";

        await RegisterDatasetAsync(name, folder, DatasetAccessLevel.Public, rowCount: 100);
        var id = await GetDatasetIdAsync(name, folder);

        var res = await AuthGet(token, $"/api/datasets/{id}/rows");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var dto = await res.Content.ReadFromJsonAsync<JsonObject>(_json);

        Assert.Equal(100L,  dto!["rowCount"]!.GetValue<long>());
        Assert.Empty(dto["columns"]!.AsArray());
    }

    // ── 4. POST /api/datasets/{id}/refresh ────────────────────────────────────

    [Fact]
    [Trait("Category", "Smoke.Portal")]
    public async Task Refresh_MarksDatasetStale()
    {
        var token  = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name   = $"#refresh_{suffix}";
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
        var token  = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name   = $"#refreshforbid_{suffix}";
        var folder = $"/refreshforbid_{suffix}";

        await RegisterDatasetAsync(name, folder, DatasetAccessLevel.Public);
        var id = await GetDatasetIdAsync(name, folder);

        var userRes = await AuthPost(token, "/api/admin/users", new
        {
            username = $"viewer_{suffix}",
            email    = $"viewer_{suffix}@test.local",
            password = "View@1234!",
            role     = "Viewer"
        });
        Assert.Equal(HttpStatusCode.Created, userRes.StatusCode);
        var viewerToken = await LoginAndChangePasswordAsync($"viewer_{suffix}", "View@1234!", "View@Changed9!");

        // Viewer gets DatasetPermission.Viewer on public dataset — cannot refresh
        var res = await AuthPost(viewerToken, $"/api/datasets/{id}/refresh", new { });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    // ── 5. PATCH /api/datasets/{id} — update metadata ─────────────────────────

    [Fact]
    public async Task Update_ChangesAccessLevelAndTtl()
    {
        var token  = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name   = $"#update_{suffix}";
        var folder = $"/update_{suffix}";

        await RegisterDatasetAsync(name, folder, DatasetAccessLevel.Private);
        var id = await GetDatasetIdAsync(name, folder);

        var patchRes = await AuthPatch(token, $"/api/datasets/{id}", new
        {
            accessLevel = "Public",
            ttl         = "2h"
        });
        Assert.Equal(HttpStatusCode.OK, patchRes.StatusCode);
        var dto = await patchRes.Content.ReadFromJsonAsync<JsonObject>(_json);

        Assert.Equal("Public", dto!["accessLevel"]!.GetValue<string>());
        Assert.Equal("2h",     dto["ttl"]!.GetValue<string>());
    }

    [Fact]
    public async Task Update_RejectsInvalidAccessLevel()
    {
        var token  = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name   = $"#badup_{suffix}";
        var folder = $"/badup_{suffix}";

        await RegisterDatasetAsync(name, folder, DatasetAccessLevel.Public);
        var id = await GetDatasetIdAsync(name, folder);

        var patchRes = await AuthPatch(token, $"/api/datasets/{id}", new
        {
            accessLevel = "Restricted"
        });
        Assert.Equal(HttpStatusCode.BadRequest, patchRes.StatusCode);
    }

    // ── 6. DELETE /api/datasets/{id} ──────────────────────────────────────────

    [Fact]
    public async Task Delete_RemovesDataset()
    {
        var token  = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name   = $"#del_{suffix}";
        var folder = $"/del_{suffix}";

        await RegisterDatasetAsync(name, folder, DatasetAccessLevel.Public);
        var id = await GetDatasetIdAsync(name, folder);

        var delRes = await AuthDelete(token, $"/api/datasets/{id}");
        Assert.Equal(HttpStatusCode.NoContent, delRes.StatusCode);

        var afterRes = await AuthGet(token, $"/api/datasets/{id}");
        Assert.Equal(HttpStatusCode.NotFound, afterRes.StatusCode);
    }

    [Fact]
    [Trait("Category", "Smoke.Security")]
    public async Task Delete_ForbidsEditorAccess()
    {
        var adminToken = await GetAdminTokenAsync();
        var suffix     = Guid.NewGuid().ToString("N")[..8];
        var name       = $"#delforbid_{suffix}";
        var folder     = $"/delforbid_{suffix}";

        await RegisterDatasetAsync(name, folder, DatasetAccessLevel.Public);
        var id = await GetDatasetIdAsync(name, folder);

        // Create a user and give them Editor ACL
        var userRes = await AuthPost(adminToken, "/api/admin/users", new
        {
            username = $"editor_{suffix}",
            email    = $"editor_{suffix}@test.local",
            password = "Edit@1234!",
            role     = "Viewer"
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

        // Editor can view but cannot delete (CanManage requires Owner)
        var delRes = await AuthDelete(editorToken, $"/api/datasets/{id}");
        Assert.Equal(HttpStatusCode.Forbidden, delRes.StatusCode);
    }

    // ── 7. ACL management ─────────────────────────────────────────────────────

    [Fact]
    public async Task Acl_GrantAndRevoke_RoundTrips()
    {
        var adminToken = await GetAdminTokenAsync();
        var suffix     = Guid.NewGuid().ToString("N")[..8];
        var name       = $"#acl_{suffix}";
        var folder     = $"/acl_{suffix}";

        await RegisterDatasetAsync(name, folder, DatasetAccessLevel.Private);
        var id = await GetDatasetIdAsync(name, folder);

        // Create a group
        var groupRes = await AuthPost(adminToken, "/api/admin/groups", new { name = $"aclgrp_{suffix}" });
        groupRes.EnsureSuccessStatusCode();
        var groupId = (await groupRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["id"]!.GetValue<int>();

        // Grant permission
        var grantRes = await AuthPost(adminToken, $"/api/datasets/{id}/acl", new
        {
            groupId    = groupId,
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
            groupId    = groupId,
            permission = "Editor"
        });
        Assert.Equal(HttpStatusCode.OK, upgradeRes.StatusCode);

        var listRes2 = await AuthGet(adminToken, $"/api/datasets/{id}/acl");
        var aclList2 = await listRes2.Content.ReadFromJsonAsync<JsonArray>(_json);
        Assert.Contains(aclList2!, a => a!["groupId"]!.GetValue<int>() == groupId
                                       && a["permission"]!.GetValue<string>() == "Editor");

        // Revoke
        var revokeRes = await AuthDelete(adminToken, $"/api/datasets/{id}/acl/{groupId}");
        Assert.Equal(HttpStatusCode.NoContent, revokeRes.StatusCode);

        var listRes3 = await AuthGet(adminToken, $"/api/datasets/{id}/acl");
        var aclList3 = await listRes3.Content.ReadFromJsonAsync<JsonArray>(_json);
        Assert.DoesNotContain(aclList3!, a => a!["groupId"]!.GetValue<int>() == groupId);
    }

    // ── 8. Phase 6 — Portal-Triggered Refresh ────────────────────────────────

    [Fact]
    [Trait("Category", "Smoke.Portal")]
    public async Task Refresh_WithOwningReport_Returns202AndJobId()
    {
        var token  = await GetAdminTokenAsync();
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
            folderId, name = $"rf_{suffix}", description = "", scriptPath
        });
        rptRes.EnsureSuccessStatusCode();
        var reportId = (await rptRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["id"]!.GetValue<int>();

        // Register a dataset owned by that report
        var dsName   = $"#rf_{suffix}";
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
        var token  = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name   = $"#noreport_{suffix}";
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
        var token  = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name   = $"#status_{suffix}";
        var folder = $"/status_{suffix}";

        await RegisterDatasetAsync(name, folder, DatasetAccessLevel.Public,
            lastRefresh: DateTime.UtcNow);
        var id = await GetDatasetIdAsync(name, folder);

        var res = await AuthGet(token, $"/api/datasets/{id}/refresh-status");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await res.Content.ReadFromJsonAsync<JsonObject>(_json);
        Assert.Equal("Idle",  body!["status"]!.GetValue<string>());
        Assert.Null(body["jobId"]);
        Assert.False(body["isStale"]!.GetValue<bool>());
    }

    [Fact]
    [Trait("Category", "Smoke.Security")]
    public async Task RefreshStatus_ForbidsUnauthorizedAccess()
    {
        var adminToken = await GetAdminTokenAsync();
        var suffix     = Guid.NewGuid().ToString("N")[..8];
        var name       = $"#statusprv_{suffix}";
        var folder     = $"/statusprv_{suffix}";

        // Private dataset — unrelated user cannot see status
        await RegisterDatasetAsync(name, folder, DatasetAccessLevel.Private);
        var id = await GetDatasetIdAsync(name, folder);

        var userRes = await AuthPost(adminToken, "/api/admin/users", new
        {
            username = $"statusoutsider_{suffix}",
            email    = $"statusoutsider_{suffix}@test.local",
            password = "Out@Status1!",
            role     = "Viewer"
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
        var token  = await GetAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name   = $"#export_{suffix}";
        var folder = $"/export_{suffix}";
        var schema = """[{"name":"ID","type":"INT"},{"name":"Name","type":"VARCHAR"}]""";

        // 1. Write the Parquet file
        var datasetFileName = $"export_{suffix}.parquet";
        var datasetFile = Path.Combine(_factory.TempDir, "datasets", datasetFileName);

        var ds = new ETL_SQL.Connectors.Parquet.ParquetDataSource(ETL_SQL.Core.Common.SystemExecutionContext.Instance, datasetFile);
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
        ETL_SQL.Core.DatasetEncryptionMode encryptionMode = ETL_SQL.Core.DatasetEncryptionMode.None)
    {
        using var scope = _factory.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IDatasetRegistry>();

        await registry.RegisterOrUpdate(new DatasetMetadata
        {
            Name            = name,
            FolderPath      = folder,
            ParquetFilePath = $"{name.TrimStart('&', '#')}.parquet",
            SourceQuery     = "SELECT 1",
            AccessLevel     = accessLevel,
            EncryptionMode  = encryptionMode,
            RowCount        = rowCount,
            Ttl             = ttl,
            ColumnSchema    = columnSchema,
            LastRefresh     = lastRefresh,
            OwningReportId  = owningReportId
        });
    }

    private async Task<int> GetDatasetIdAsync(string name, string folder)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var d  = await db.Datasets.SingleAsync(x => x.Name == name && x.FolderPath == folder);
        return d.Id;
    }

    private async Task AddDatasetAclAsync(int datasetId, int groupId, DatasetPermission permission)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        db.DatasetAcls.Add(new DatasetAcl
        {
            DatasetId  = datasetId,
            GroupId    = groupId,
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
                newPassword     = "Admin@Tests99!"
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

    private Task<HttpResponseMessage> AuthPost(string token, string url, object body)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new("Bearer", token);
        req.Content = JsonContent.Create(body);
        return _client.SendAsync(req);
    }

    private Task<HttpResponseMessage> AuthPatch(string token, string url, object body)
    {
        var req = new HttpRequestMessage(HttpMethod.Patch, url);
        req.Headers.Authorization = new("Bearer", token);
        req.Content = JsonContent.Create(body);
        return _client.SendAsync(req);
    }

    private Task<HttpResponseMessage> AuthDelete(string token, string url)
    {
        var req = new HttpRequestMessage(HttpMethod.Delete, url);
        req.Headers.Authorization = new("Bearer", token);
        return _client.SendAsync(req);
    }
}
