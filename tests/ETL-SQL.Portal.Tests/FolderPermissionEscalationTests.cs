using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Portal.Data;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Whether an <c>Author</c> grant confers <c>Manage</c> authority over a folder.
///
/// <para>It must not, and the arithmetic makes it easy to. <c>FolderPermission</c> stores
/// <c>Manage = 2</c> and <c>Author = 3</c> — Author was appended rather than inserted, so that
/// adding it would not renumber every ACL row already in force — while its *authority* ranks below
/// Manage. Every comparison therefore has to go through <c>AtLeast()</c>, which ranks, rather than
/// through <c>&gt;=</c>, which reads the storage value.</para>
///
/// <para><c>AuthorizationMatrixTests</c> covers the report-level operations. These are the
/// folder-level ones, reached through <c>FolderPermissionService.HasPermissionAsync</c>, and they
/// were the sites the ordinal comparison survived at: publishing a new report into a folder is
/// exactly what an Author grant is defined not to permit.</para>
/// </summary>
[Trait("Category", "Portal")]
public sealed class FolderPermissionEscalationTests
{
    private const int Author = 3;
    private const int Manage = 2;

    /// <param name="permission">The ACL grant the publisher holds on the target folder.</param>
    /// <param name="mayPublish">Whether publishing a new report into it should be allowed.</param>
    [Theory]
    [InlineData(Author, false)]
    [InlineData(Manage, true)]
    public async Task PublishingIntoAFolder_RequiresManage_NotMerelyAuthor(
        int permission, bool mayPublish)
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var folderId = await CreateFolderAsync(client, admin, $"esc_{suffix}");
        var groupId = await CreateGroupAsync(client, admin, $"eg_{suffix}");

        // Publisher, because POST /api/studio/reports is role-gated to Admin,Publisher. The ACL is
        // the second axis: the role decides the class of operation, the grant decides where.
        var userId = await CreateUserAsync(client, admin, "Publisher", suffix);
        Assert.True((await AuthPost(client, admin, $"/api/admin/groups/{groupId}/members",
            new { userId })).IsSuccessStatusCode);
        Assert.True((await AuthPost(client, admin, $"/api/folders/{folderId}/acl",
            new { groupId, permission })).IsSuccessStatusCode);

        var token = await SignInAsync(client, suffix);

        var response = await AuthPost(client, token, "/api/studio/reports", new
        {
            folderId,
            name = $"Escalation {suffix}",
            scriptText = "SET REPORT TITLE = 'Escalation probe';"
        });

        if (mayPublish)
        {
            Assert.True(response.IsSuccessStatusCode,
                $"Manage should permit publishing but got {(int)response.StatusCode}. Without this "
                + "row, denying everyone would satisfy the Author case and take the feature away.");
        }
        else
        {
            Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound,
                $"An Author grant published a new report into the folder (got "
                + $"{(int)response.StatusCode}). Author is stored as 3 and Manage as 2, so an "
                + "ordinal `>=` hands Author everything Manage has — which is the escalation the "
                + "rank/AtLeast split exists to prevent.");
        }
    }

    /// <summary>
    /// The service-level statement of the same rule, so a future caller that asks the question
    /// directly is covered even if no route happens to exercise it.
    /// </summary>
    [Fact]
    public void AtLeast_IsTheOnlySafeComparison_BecauseStorageOrderIsNotAuthorityOrder()
    {
        Assert.True((int)FolderPermission.Author > (int)FolderPermission.Manage);
        Assert.False(FolderPermission.Author.AtLeast(FolderPermission.Manage));
        // The comparison that was live in FolderPermissionService, stated as the trap it is.
        Assert.True(FolderPermission.Author >= FolderPermission.Manage);
    }

    private static async Task<string> AdminTokenAsync(HttpClient client)
    {
        const string initial = "Admin@12345!";
        const string changed = "Admin@Escalation99!";
        var first = await LoginAsync(client, "admin", initial);
        var change = await AuthPost(client, first, "/api/auth/change-password",
            new { currentPassword = initial, newPassword = changed });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        return await LoginAsync(client, "admin", changed);
    }

    private static async Task<string> SignInAsync(HttpClient client, string suffix)
    {
        var initial = $"Init@{suffix}9!";
        var changed = $"Next@{suffix}9!";
        var first = await LoginAsync(client, $"u{suffix}", initial);
        var change = await AuthPost(client, first, "/api/auth/change-password",
            new { currentPassword = initial, newPassword = changed });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        return await LoginAsync(client, $"u{suffix}", changed);
    }

    private static async Task<string> LoginAsync(HttpClient client, string user, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username = user, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        return body!["token"]!.GetValue<string>();
    }

    private static async Task<int> CreateFolderAsync(HttpClient client, string token, string name)
    {
        var response = await AuthPost(client, token, "/api/folders", new { name, parentId = (int?)null });
        Assert.True(response.IsSuccessStatusCode, $"folder create: {response.StatusCode}");
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        return body!["id"]!.GetValue<int>();
    }

    private static async Task<int> CreateGroupAsync(HttpClient client, string token, string name)
    {
        var response = await AuthPost(client, token, "/api/admin/groups", new { name, description = "escalation probe" });
        Assert.True(response.IsSuccessStatusCode, $"group create: {response.StatusCode}");
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        return body!["id"]!.GetValue<int>();
    }

    private static async Task<int> CreateUserAsync(HttpClient client, string token, string role, string suffix)
    {
        var response = await AuthPost(client, token, "/api/admin/users", new
        {
            username = $"u{suffix}",
            password = $"Init@{suffix}9!",
            role,
            email = $"u{suffix}@localhost"
        });
        Assert.True(response.IsSuccessStatusCode, $"user create: {response.StatusCode}");
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        return body!["id"]!.GetValue<int>();
    }

    private static async Task<HttpResponseMessage> AuthPost(
        HttpClient client, string token, string path, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = new("Bearer", token);
        // Group and ACL mutations are version-checked; without the If-Match stamp they 428/412 and
        // the setup fails in a way that looks like the assertion under test.
        await IfMatchVersioning.StampAsync(client, request, token);
        return await client.SendAsync(request);
    }
}
