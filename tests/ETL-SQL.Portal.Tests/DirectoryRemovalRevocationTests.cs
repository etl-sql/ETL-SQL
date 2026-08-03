using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Core.Data;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// The end-to-end proof for <c>docs/architecture/decisions/AuthorshipIsNotPermission.md</c>: one
/// identity creates everything it can, then loses its access, and every surface it left behind
/// stops answering.
///
/// This is deliberately one scenario rather than a suite of unit checks. The v0.17.0 regression was
/// not one broken function — it was five surfaces that each looked reasonable alone and together
/// meant deprovisioning deprovisioned nothing. What has to hold is the property across all of them
/// at once.
///
/// Two phases, because they revoke different things:
/// <list type="number">
///   <item><description><b>Group removal.</b> Everything the identity reached <em>through a group</em>
///     goes: the report, its saved views, and the anonymous share/embed links it issued. A dataset it
///     created does <b>not</b> — that access is a direct grant, and losing a group is not supposed to
///     revoke a grant made to you personally.</description></item>
///   <item><description><b>Directory removal.</b> Deleting the account cascades the direct grants
///     away too, which is what makes the dataset grant revocable at all.</description></item>
/// </list>
///
/// Subscription and alert delivery are re-authorized on the same rule and are proven against it in
/// <see cref="SubscriptionDeliverySecurityTests"/> and
/// <see cref="PortalAlertEvaluationServiceTests"/>, which drive the delivery paths directly.
/// </summary>
[Trait("Category", "Portal")]
[Trait("Category", "Smoke.Security")]
public sealed class DirectoryRemovalRevocationTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const int Manage = 2;

    [Fact]
    public async Task LosingEveryGroup_ThenTheAccount_RevokesEverythingThatIdentityCreated()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        // ── An author whose only route to the folder is one group ────────────
        var author = await CreateReadyUserAsync(client, adminToken, $"leaver_{suffix}", "Publisher");
        var groupId = await CreateGroupAsync(client, adminToken, $"leaver_group_{suffix}");
        Assert.Equal(HttpStatusCode.OK,
            (await AuthPost(client, adminToken, $"/api/admin/groups/{groupId}/members",
                new { userId = author.UserId })).StatusCode);

        var folderId = await CreateFolderAsync(client, adminToken, $"leaver_folder_{suffix}");
        Assert.Equal(HttpStatusCode.OK,
            (await AuthPost(client, adminToken, $"/api/folders/{folderId}/acl",
                new { groupId, permission = Manage })).StatusCode);

        var authorToken = (await LoginAsync(client, author.Username, "Ready@Test2!")).AccessToken;

        // ── Everything that identity creates ─────────────────────────────────
        var scriptPath = Path.Combine(factory.TempDir, "scripts", $"leaver-{suffix}.rptsql");
        await File.WriteAllTextAsync(scriptPath, "SET REPORT TITLE = 'Leaver';");
        var reportId = await CreatedIdAsync(await AuthPost(client, authorToken, "/api/reports",
            new { folderId, name = $"Leaver Report {suffix}", scriptPath }));

        var savedViewId = await CreatedIdAsync(await AuthPost(client, authorToken,
            $"/api/reports/{reportId}/saved-views",
            new { name = $"view_{suffix}", parameters = new Dictionary<string, string>() }));

        var shareToken = await TokenAsync(await AuthPost(client, authorToken,
            $"/api/reports/{reportId}/share-links", new { }));
        var embedToken = await TokenAsync(await AuthPost(client, authorToken,
            $"/api/reports/{reportId}/embed-tokens", new { name = $"embed_{suffix}" }));

        var datasetName = $"ds_leaver_{suffix}";
        await RegisterDatasetAsync(factory, datasetName, author.UserId);
        var datasetId = await DatasetIdAsync(factory, datasetName);

        // ── Baseline: all of it works ────────────────────────────────────────
        Assert.Equal(HttpStatusCode.OK, (await AuthGet(client, authorToken, $"/api/reports/{reportId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await AuthGet(client, authorToken, $"/api/reports/{reportId}/saved-views")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await AuthGet(client, authorToken, $"/api/datasets/{datasetId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/share/{shareToken}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/embed/{embedToken}")).StatusCode);

        // ── Phase 1: removed from every group ────────────────────────────────
        Assert.Equal(HttpStatusCode.OK,
            (await AuthPost(client, adminToken, $"/api/admin/groups/{groupId}/members/bulk-remove",
                new { userIds = new[] { author.UserId } })).StatusCode);
        authorToken = (await LoginAsync(client, author.Username, "Ready@Test2!")).AccessToken;

        var reportAfter = (await AuthGet(client, authorToken, $"/api/reports/{reportId}")).StatusCode;
        Assert.True(reportAfter is HttpStatusCode.Forbidden or HttpStatusCode.NotFound,
            $"The report must not survive losing every grant; got {reportAfter}.");

        var viewsAfter = (await AuthGet(client, authorToken, $"/api/reports/{reportId}/saved-views")).StatusCode;
        Assert.True(viewsAfter is HttpStatusCode.Forbidden or HttpStatusCode.NotFound,
            $"Saved views must go with the report access they were made under; got {viewsAfter}.");

        // The links carry no identity of their own — their authority is the grantor's *continuing*
        // access, so revoking that revokes them without anyone having to remember they exist.
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/share/{shareToken}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/embed/{embedToken}")).StatusCode);

        // The admin inventory must agree, or an operator auditing anonymous access is told a link is
        // live when it answers nothing.
        var inventory = await AuthGet(client, adminToken, "/api/admin/anonymous-report-access");
        Assert.Equal(HttpStatusCode.OK, inventory.StatusCode);
        var items = await inventory.Content.ReadFromJsonAsync<JsonArray>(Json);
        Assert.All(
            items!.Where(item => item!["reportId"]?.GetValue<int>() == reportId),
            item => Assert.NotEqual("Active", item!["status"]!.GetValue<string>()));

        // The dataset is the deliberate exception: its grant was made to the person, not the group.
        Assert.Equal(HttpStatusCode.OK, (await AuthGet(client, authorToken, $"/api/datasets/{datasetId}")).StatusCode);
        Assert.True(await HasUserGrantAsync(factory, datasetId, author.UserId),
            "A direct grant must not be collateral damage of a group change.");

        // ── Phase 2: removed from the directory ──────────────────────────────
        var reassignTo = await AdminUserIdAsync(factory);
        var deleted = await SendAsync(client, HttpMethod.Delete, adminToken,
            $"/api/admin/users/{author.UserId}?reassignTo={reassignTo}", null);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        Assert.False(await HasUserGrantAsync(factory, datasetId, author.UserId),
            "Deleting the account must cascade its direct dataset grants away.");

        // Ownership moved rather than dangling, so the dataset is not left administrator-only.
        Assert.True(await HasUserGrantAsync(factory, datasetId, reassignTo),
            "Transferring ownership must carry the grant, not just the CreatedBy column.");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static async Task RegisterDatasetAsync(PortalWebFactory factory, string name, int createdBy)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IDatasetRegistry>().RegisterOrUpdate(new DatasetMetadata
        {
            Name = name,
            FolderPath = "/leaver",
            ParquetFilePath = $"{name}.parquet",
            SourceQuery = "SELECT 1",
            AccessLevel = DatasetAccessLevel.Private,
            CreatedBy = createdBy
        });
    }

    private static async Task<int> DatasetIdAsync(PortalWebFactory factory, string name)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        return (await db.Datasets.SingleAsync(d => d.Name == name)).Id;
    }

    private static async Task<bool> HasUserGrantAsync(PortalWebFactory factory, int datasetId, int userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        return await db.DatasetUserAcls.AnyAsync(a => a.DatasetId == datasetId && a.UserId == userId);
    }

    private static async Task<int> AdminUserIdAsync(PortalWebFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        return (await db.Users.SingleAsync(u => u.UserName == "admin")).Id;
    }

    private static async Task<int> CreateFolderAsync(HttpClient client, string adminToken, string name)
    {
        var response = await AuthPost(client, adminToken, "/api/folders", new { name, parentId = (int?)null });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();
    }

    private static async Task<int> CreatedIdAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();
    }

    private static async Task<string> TokenAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>(Json))!["token"]!.GetValue<string>();
    }

    private static async Task<int> CreateGroupAsync(HttpClient client, string adminToken, string name)
    {
        var response = await AuthPost(client, adminToken, "/api/admin/groups", new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();
    }

    private static async Task<(int UserId, string Username)> CreateReadyUserAsync(
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
