using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Core.Data;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Datasets follow the same rule as reports — authorship is not standing permission — but they get
/// there differently. <c>ReportAcl</c> can name a user; <c>DatasetAcl</c> is group-scoped, so there
/// was no per-user grant for authorship to upgrade and <c>CreatedBy == userId</c> was the only way a
/// creator reached their own private dataset. Removing that check alone would have made a freshly
/// created dataset invisible to its author.
///
/// So a creator is now granted <see cref="DatasetPermission.Owner"/> explicitly, in
/// <c>DatasetUserAcls</c>, when the dataset is registered. These tests pin both halves: the grant
/// exists and works, and it is the <em>only</em> thing keeping the creator in — revoke it and the
/// access goes with it, which is exactly what an identity comparison could never do.
/// </summary>
[Trait("Category", "Portal")]
public sealed class DatasetAuthorshipRevocationTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task CreatorOfAPrivateDataset_HoldsAGrantRatherThanAnIdentityMatch()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var creator = await CreateReadyUserAsync(client, adminToken, $"dsauthor_{suffix}", "Publisher");
        var datasetName = $"ds_private_{suffix}";
        await RegisterDatasetAsync(factory, datasetName, createdBy: creator.UserId);
        var datasetId = await DatasetIdAsync(factory, datasetName);

        // The grant is a durable row, not a derived fact.
        Assert.True(await HasUserGrantAsync(factory, datasetId, creator.UserId, DatasetPermission.Owner),
            "Registering a dataset must record its creator's Owner grant.");

        var creatorToken = (await LoginAsync(client, creator.Username, "Ready@Test2!")).AccessToken;
        Assert.Equal(HttpStatusCode.OK, (await AuthGet(client, creatorToken, $"/api/datasets/{datasetId}")).StatusCode);
        Assert.True(await DatasetListContainsAsync(client, creatorToken, datasetId),
            "The batch permission path must agree with the single-dataset path.");

        // Revoke the grant. Under the old CreatedBy short-circuit this changed nothing at all.
        await RemoveUserGrantAsync(factory, datasetId, creator.UserId);

        var afterGet = (await AuthGet(client, creatorToken, $"/api/datasets/{datasetId}")).StatusCode;
        Assert.True(afterGet is HttpStatusCode.Forbidden or HttpStatusCode.NotFound,
            $"Authorship must not survive losing the grant; got {afterGet}.");
        Assert.False(await DatasetListContainsAsync(client, creatorToken, datasetId),
            "A dataset the creator can no longer open must not remain in their dataset list.");
    }

    [Fact]
    public async Task CreatorRetainingOnlyALesserGroupGrant_GetsThatGrantAndNotOwner()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var creator = await CreateReadyUserAsync(client, adminToken, $"dslesser_{suffix}", "Publisher");
        var groupId = await CreateGroupAsync(client, adminToken, $"ds_group_{suffix}");
        Assert.Equal(HttpStatusCode.OK,
            (await AuthPost(client, adminToken, $"/api/admin/groups/{groupId}/members",
                new { userId = creator.UserId })).StatusCode);

        var datasetName = $"ds_lesser_{suffix}";
        await RegisterDatasetAsync(factory, datasetName, createdBy: creator.UserId);
        var datasetId = await DatasetIdAsync(factory, datasetName);
        await AddGroupGrantAsync(factory, datasetId, groupId, DatasetPermission.Viewer);

        // Drop the authorship grant; only the group's Viewer grant survives.
        await RemoveUserGrantAsync(factory, datasetId, creator.UserId);
        var creatorToken = (await LoginAsync(client, creator.Username, "Ready@Test2!")).AccessToken;

        // Viewer can read...
        Assert.Equal(HttpStatusCode.OK, (await AuthGet(client, creatorToken, $"/api/datasets/{datasetId}")).StatusCode);

        // ...and nothing more. Granting an ACL needs Owner; having authored the dataset must not
        // silently restore that.
        var grant = await AuthPost(client, creatorToken, $"/api/datasets/{datasetId}/acl",
            new { groupId, permission = "Editor" });
        Assert.Equal(HttpStatusCode.Forbidden, grant.StatusCode);
    }

    [Fact]
    public async Task ReportDependencies_StopListingAPrivateDatasetOnceTheAuthorsGrantIsGone()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var author = await CreateReadyUserAsync(client, adminToken, $"dsdep_{suffix}", "Publisher");
        var groupId = await CreateGroupAsync(client, adminToken, $"dsdep_group_{suffix}");
        Assert.Equal(HttpStatusCode.OK,
            (await AuthPost(client, adminToken, $"/api/admin/groups/{groupId}/members",
                new { userId = author.UserId })).StatusCode);

        var folderResponse = await AuthPost(client, adminToken, "/api/folders",
            new { name = $"dsdep_folder_{suffix}", parentId = (int?)null });
        Assert.Equal(HttpStatusCode.Created, folderResponse.StatusCode);
        var folderId = (await folderResponse.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();
        Assert.Equal(HttpStatusCode.OK,
            (await AuthPost(client, adminToken, $"/api/folders/{folderId}/acl",
                new { groupId, permission = 2 /* Manage */ })).StatusCode);

        var authorToken = (await LoginAsync(client, author.Username, "Ready@Test2!")).AccessToken;
        var scriptPath = Path.Combine(factory.TempDir, "scripts", $"dsdep-{suffix}.rptsql");
        await File.WriteAllTextAsync(scriptPath, "SET REPORT TITLE = 'Dependency';");
        var reportResponse = await AuthPost(client, authorToken, "/api/reports",
            new { folderId, name = $"Dependency Report {suffix}", scriptPath });
        Assert.Equal(HttpStatusCode.Created, reportResponse.StatusCode);
        var reportId = (await reportResponse.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();

        var datasetName = $"ds_dep_{suffix}";
        await RegisterDatasetAsync(factory, datasetName, owningReportId: reportId);
        var datasetId = await DatasetIdAsync(factory, datasetName);

        // The owning report's author is granted Owner, so the dependency view shows the dataset.
        Assert.True(await HasUserGrantAsync(factory, datasetId, author.UserId, DatasetPermission.Owner),
            "The owning report's author must receive an explicit dataset grant.");
        Assert.True(await DependenciesListDatasetAsync(client, authorToken, reportId, datasetName));

        // Revoking it removes the dataset from the view. Previously OwningReport.CreatedBy
        // short-circuited to true here regardless of any grant.
        await RemoveUserGrantAsync(factory, datasetId, author.UserId);
        Assert.False(await DependenciesListDatasetAsync(client, authorToken, reportId, datasetName),
            "The dependency view must not keep exposing a private dataset the caller has no grant on.");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static async Task RegisterDatasetAsync(
        PortalWebFactory factory, string name, int? createdBy = null, int? owningReportId = null)
    {
        using var scope = factory.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IDatasetRegistry>();
        await registry.RegisterOrUpdate(new DatasetMetadata
        {
            Name = name,
            FolderPath = "/authorship",
            ParquetFilePath = $"{name}.parquet",
            SourceQuery = "SELECT 1",
            AccessLevel = DatasetAccessLevel.Private,
            CreatedBy = createdBy,
            OwningReportId = owningReportId
        });
    }

    private static async Task<int> DatasetIdAsync(PortalWebFactory factory, string name)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        return (await db.Datasets.SingleAsync(d => d.Name == name)).Id;
    }

    private static async Task<bool> HasUserGrantAsync(
        PortalWebFactory factory, int datasetId, int userId, DatasetPermission permission)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        return await db.DatasetUserAcls.AnyAsync(a =>
            a.DatasetId == datasetId && a.UserId == userId && a.Permission == permission);
    }

    private static async Task RemoveUserGrantAsync(PortalWebFactory factory, int datasetId, int userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var rows = await db.DatasetUserAcls
            .Where(a => a.DatasetId == datasetId && a.UserId == userId)
            .ToListAsync();
        db.DatasetUserAcls.RemoveRange(rows);
        await db.SaveChangesAsync();
    }

    private static async Task AddGroupGrantAsync(
        PortalWebFactory factory, int datasetId, int groupId, DatasetPermission permission)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        db.DatasetAcls.Add(new DatasetAcl { DatasetId = datasetId, GroupId = groupId, Permission = permission });
        await db.SaveChangesAsync();
    }

    private static async Task<bool> DatasetListContainsAsync(HttpClient client, string token, int datasetId)
    {
        var response = await AuthGet(client, token, "/api/datasets");
        if (response.StatusCode != HttpStatusCode.OK) return false;
        var items = await response.Content.ReadFromJsonAsync<JsonArray>(Json);
        return items!.Any(item => item?["id"]?.GetValue<int>() == datasetId);
    }

    private static async Task<bool> DependenciesListDatasetAsync(
        HttpClient client, string token, int reportId, string datasetName)
    {
        var response = await AuthGet(client, token, $"/api/reports/{reportId}/dependencies");
        if (response.StatusCode != HttpStatusCode.OK) return false;
        return (await response.Content.ReadAsStringAsync()).Contains(datasetName, StringComparison.Ordinal);
    }

    private static async Task<int> CreateGroupAsync(HttpClient client, string adminToken, string name)
    {
        var response = await AuthPost(client, adminToken, "/api/admin/groups", new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();
    }

    private static async Task<(int UserId, string Username)> CreateReadyUserAsync(
        HttpClient client, string adminToken, string username, string role = "Viewer")
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
            (await AuthPost(client, initial.AccessToken, "/api/auth/change-password",
                new { currentPassword = "Initial@Test1!", newPassword = "Ready@Test2!" })).StatusCode);
        return (userId, username);
    }

    private static async Task<string> GetAdminTokenAsync(HttpClient client)
    {
        var initial = await LoginAsync(client, "admin", "Admin@12345!");
        Assert.Equal(HttpStatusCode.NoContent,
            (await AuthPost(client, initial.AccessToken, "/api/auth/change-password",
                new { currentPassword = "Admin@12345!", newPassword = "Admin@Tests99!" })).StatusCode);
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
