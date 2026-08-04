using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// The Portal's authorization matrix, asserted one grant × one operation at a time.
///
/// <para>Portal authorization is spread across ~40 comparisons of an <b>ordered</b>
/// <c>FolderPermission</c> enum, plus roles, plus report-level ACLs that can override folder ones.
/// Every one of those comparisons is a place where a grant can silently mean more than intended,
/// and the ordering is load-bearing: adding a value in the wrong position, or appending one without
/// revisiting the comparisons, hands every holder of the new grant everything above it.</para>
///
/// <para>So the matrix is written as data rather than prose. Each row states what a specific grant
/// may and may not do, which makes a privilege change impossible to ship by accident — a widened
/// grant fails a <c>denied</c> row, and a narrowed one fails an <c>allowed</c> row. The negative
/// rows are the point: an authorization suite that only proves people can do things proves nothing
/// about what stops them.</para>
/// </summary>
[Trait("Category", "Portal")]
[Trait("Category", "Smoke.Security")]
public sealed class AuthorizationMatrixTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const int Read = 0;
    private const int Execute = 1;
    private const int Manage = 2;

    /// <summary>The operations the matrix covers, named by what a person would say they are doing.</summary>
    public enum Operation
    {
        ViewReport,
        RunReport,
        EditReportDefinition,
        MoveReport,
        DeleteReport,
        ReadFolderAcl,
        GrantFolderAcl,
        CreateSubfolder,
        DeleteFolder,
    }

    /// <summary>
    /// Grant × operation × expectation, for the operations an ACL governs.
    ///
    /// <para>Only resource-scoped operations appear here. Portal authorization is two-dimensional
    /// and the axes are not interchangeable: a <b>role</b> decides which class of operation you may
    /// perform at all, and an <b>ACL</b> decides which resources you may perform it on. Folder
    /// administration — reading or granting a folder's ACL, creating a subfolder, deleting a
    /// folder — is gated by role and is asserted separately, because holding Manage on a folder
    /// deliberately does not let you re-grant it.</para>
    /// </summary>
    public static TheoryData<int, Operation, bool> Matrix() => new()
    {
        // ── Read: can see it, and nothing else ──────────────────────────────────────────────────
        { Read, Operation.ViewReport, true },
        { Read, Operation.RunReport, false },
        { Read, Operation.EditReportDefinition, false },
        { Read, Operation.MoveReport, false },
        { Read, Operation.DeleteReport, false },

        // ── Execute: adds running the report, nothing structural ────────────────────────────────
        { Execute, Operation.ViewReport, true },
        { Execute, Operation.RunReport, true },
        // Running a report is not authoring it. This is the line the Author grant will sit on.
        { Execute, Operation.EditReportDefinition, false },
        { Execute, Operation.MoveReport, false },
        { Execute, Operation.DeleteReport, false },

        // ── Manage: administers the reports in the folder ───────────────────────────────────────
        { Manage, Operation.ViewReport, true },
        { Manage, Operation.RunReport, true },
        { Manage, Operation.EditReportDefinition, true },
        { Manage, Operation.MoveReport, true },
        { Manage, Operation.DeleteReport, true },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task GrantedPermission_AllowsExactlyTheOperationsItNames(
        int permission, Operation operation, bool allowed)
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var admin = await GetAdminTokenAsync(client);
        var world = await BuildWorldAsync(client, admin, factory, permission);

        var response = await InvokeAsync(client, world.Token, world, operation);

        if (allowed)
        {
            Assert.True(response.IsSuccessStatusCode,
                $"permission={permission} operation={operation} expected to be allowed but got "
                + $"{(int)response.StatusCode} {response.StatusCode}: "
                + await response.Content.ReadAsStringAsync());
        }
        else
        {
            // 403 and 404 are both acceptable denials — hiding a resource's existence is a
            // legitimate way to refuse. What must never happen is the operation succeeding.
            Assert.True(
                response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound,
                $"permission={permission} operation={operation} expected to be denied but got "
                + $"{(int)response.StatusCode} {response.StatusCode}: "
                + await response.Content.ReadAsStringAsync());
        }
    }

    [Theory]
    [InlineData(Operation.ReadFolderAcl)]
    [InlineData(Operation.GrantFolderAcl)]
    [InlineData(Operation.CreateSubfolder)]
    [InlineData(Operation.DeleteFolder)]
    public async Task FolderManageGrant_DoesNotConferFolderAdministration(Operation operation)
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var admin = await GetAdminTokenAsync(client);
        var world = await BuildWorldAsync(client, admin, factory, Manage);

        var response = await InvokeAsync(client, world.Token, world, operation);

        // The second axis, stated rather than left to be discovered. Manage on a folder is authority
        // over the reports in it; deciding *who else* may reach the folder, and whether the folder
        // exists at all, is an administrative act reserved to the Admin role. Without this split the
        // highest ACL grant would be self-propagating: anyone holding it could hand it out, and the
        // set of people with access could only ever grow.
        Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound,
            $"Manage on a folder conferred {operation}, which belongs to the Admin role: "
            + $"{(int)response.StatusCode} {response.StatusCode}");
    }

    [Theory]
    // A role is not a grant. Publisher can create folders of their own, but holding the role does
    // not hand them somebody else's folder — that has been the source of enough real incidents to
    // be worth pinning separately from the ACL matrix.
    [InlineData("Viewer", false)]
    [InlineData("Publisher", false)]
    [InlineData("Admin", true)]
    public async Task RoleAlone_DoesNotGrantAccessToAnotherOwnersFolder(string role, bool allowed)
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var admin = await GetAdminTokenAsync(client);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var folderId = await CreateFolderAsync(client, admin, $"private_{suffix}");
        var reportId = await PublishAsync(client, admin, factory, folderId, $"Private {suffix}", suffix);
        var token = await CreateUserTokenAsync(client, admin, role, suffix);

        var response = await AuthGet(client, token, $"/api/reports/{reportId}");

        if (allowed)
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        else
            Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound,
                $"{role} reached a folder nobody granted them: {(int)response.StatusCode}");
    }

    [Fact]
    public async Task ApprovedAccessRequest_OpensOnlyTheReportItNamed()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var admin = await GetAdminTokenAsync(client);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var folderId = await CreateFolderAsync(client, admin, $"mixed_{suffix}");
        var granted = await PublishAsync(client, admin, factory, folderId, $"Granted {suffix}", $"g{suffix}");
        var sibling = await PublishAsync(client, admin, factory, folderId, $"Sibling {suffix}", $"s{suffix}");

        await CreateUserAsync(client, admin, "Viewer", suffix);
        var token = await SignInAsync(client, suffix, "Viewer");

        // Report ACLs are only ever created by approving an access request — there is no endpoint
        // that grants one directly. Driving the real path is the point: a shortcut through the
        // database would prove the ACL works and nothing about how one comes to exist.
        Assert.True((await AuthPost(client, token, $"/api/reports/{granted}/request-access",
            new { reason = "Need this for the quarterly close." })).IsSuccessStatusCode);

        var pending = await AuthGet(client, admin, "/api/reports/access-requests/pending");
        Assert.Equal(HttpStatusCode.OK, pending.StatusCode);
        var requestId = (await pending.Content.ReadFromJsonAsync<JsonArray>(Json))!
            .Single(r => r!["reportId"]!.GetValue<int>() == granted)!["id"]!.GetValue<int>();

        Assert.True((await AuthPost(client, admin,
            $"/api/reports/access-requests/{requestId}/approve",
            new { permission = Read, decisionReason = "Approved for the close." })).IsSuccessStatusCode);

        // The grant changed this user's access, which invalidates their session — the mechanism that
        // makes a revoked grant take effect at once rather than at next sign-in.
        token = await LoginAsync(client, UserNameFor("Viewer", suffix), ActorPassword);

        // Approving one request opens exactly one report. A report ACL that leaked to its siblings
        // would silently turn a per-report share into a folder-wide one.
        Assert.True((await AuthGet(client, token, $"/api/reports/{granted}")).IsSuccessStatusCode);
        Assert.True((await AuthGet(client, token, $"/api/reports/{sibling}")).StatusCode
            is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);
    }

    // ── invocation ──────────────────────────────────────────────────────────────────────────────

    private sealed record World(
        string Token, string AdminToken, int FolderId, int ReportId, int OtherFolderId, int GroupId);

    private static Task<HttpResponseMessage> InvokeAsync(
        HttpClient client, string token, World world, Operation operation) => operation switch
        {
            Operation.ViewReport =>
                AuthGet(client, token, $"/api/reports/{world.ReportId}"),
            Operation.RunReport =>
                AuthPost(client, token, $"/api/reports/{world.ReportId}/execute", new { }, world.AdminToken),
            Operation.EditReportDefinition =>
                AuthPut(client, token, $"/api/reports/{world.ReportId}", new { name = "Renamed" },
                    world.AdminToken),
            Operation.MoveReport =>
                AuthPut(client, token, $"/api/reports/{world.ReportId}",
                    new { folderId = world.OtherFolderId }, world.AdminToken),
            Operation.DeleteReport =>
                AuthDelete(client, token, $"/api/reports/{world.ReportId}", world.AdminToken),
            Operation.ReadFolderAcl =>
                AuthGet(client, token, $"/api/folders/{world.FolderId}/acl"),
            Operation.GrantFolderAcl =>
                AuthPost(client, token, $"/api/folders/{world.FolderId}/acl",
                    new { groupId = world.GroupId, permission = Read }, world.AdminToken),
            Operation.CreateSubfolder =>
                AuthPost(client, token, "/api/folders",
                    new { name = $"child_{Guid.NewGuid():N}"[..16], parentId = world.FolderId }),
            Operation.DeleteFolder =>
                AuthDelete(client, token, $"/api/folders/{world.FolderId}", world.AdminToken),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

    /// <summary>
    /// Builds a folder with one report, a second folder to move into, and a Viewer holding exactly
    /// <paramref name="permission"/> on the first folder through a group.
    /// </summary>
    private static async Task<World> BuildWorldAsync(
        HttpClient client, string admin, PortalWebFactory factory, int permission)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var factoryless = $"m{suffix}";

        var folderId = await CreateFolderAsync(client, admin, $"matrix_{suffix}");
        var otherId = await CreateFolderAsync(client, admin, $"target_{suffix}");
        var groupId = await CreateGroupAsync(client, admin, $"mg_{suffix}");
        var userId = await CreateUserAsync(client, admin, "Viewer", suffix);

        Assert.Equal(HttpStatusCode.OK,
            (await AuthPost(client, admin, $"/api/admin/groups/{groupId}/members", new { userId }, admin)).StatusCode);
        Assert.True((await AuthPost(client, admin, $"/api/folders/{folderId}/acl",
            new { groupId, permission }, admin)).IsSuccessStatusCode);
        // Manage on the destination too, so a denied move is denied by the *source* grant rather
        // than by an unrelated missing one — otherwise the row proves nothing about the source.
        Assert.True((await AuthPost(client, admin, $"/api/folders/{otherId}/acl",
            new { groupId, permission = Manage }, admin)).IsSuccessStatusCode);

        var reportId = await PublishAsync(client, admin, factory, folderId, $"Matrix {suffix}", factoryless);
        // Signed in last, once every grant is in place.
        var token = await SignInAsync(client, suffix, "Viewer");
        return new World(token, admin, folderId, reportId, otherId, groupId);
    }

    // ── fixtures ────────────────────────────────────────────────────────────────────────────────

    private const string ReportScript = "SET REPORT TITLE = 'Matrix';";

    private static async Task<int> PublishAsync(
        HttpClient client, string admin, PortalWebFactory factory, int folderId, string name, string suffix)
    {
        var scriptName = $"matrix_{suffix}.rptsql";
        await File.WriteAllTextAsync(
            Path.Combine(factory.TempDir, "scripts", scriptName), ReportScript);

        var res = await AuthPost(client, admin, "/api/reports",
            new { folderId, name, scriptPath = scriptName });
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();
    }

    private static async Task<int> CreateFolderAsync(HttpClient client, string token, string name)
    {
        var res = await AuthPost(client, token, "/api/folders", new { name });
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();
    }

    private static async Task<int> CreateGroupAsync(HttpClient client, string token, string name)
    {
        var res = await AuthPost(client, token, "/api/admin/groups", new { name });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();
    }

    private const string InitialPassword = "Matrix@Tests99!";
    private const string ActorPassword = "Matrix@Tests99b!";

    private static string UserNameFor(string role, string suffix) =>
        $"mx_{role.ToLowerInvariant()}_{suffix}";

    private static async Task<int> CreateUserAsync(
        HttpClient client, string admin, string role, string suffix)
    {
        var username = UserNameFor(role, suffix);
        var created = await AuthPost(client, admin, "/api/admin/users",
            new { username, password = InitialPassword, role, email = $"{username}@example.com" });
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        return (await created.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();
    }

    /// <summary>Clears the forced password change and returns a usable token.</summary>
    private static async Task<string> SignInAsync(HttpClient client, string suffix, string role)
    {
        var username = UserNameFor(role, suffix);
        var first = await LoginAsync(client, username, InitialPassword);
        Assert.Equal(HttpStatusCode.NoContent,
            (await AuthPost(client, first, "/api/auth/change-password",
                new { currentPassword = InitialPassword, newPassword = ActorPassword })).StatusCode);
        return await LoginAsync(client, username, ActorPassword);
    }

    private static async Task<string> CreateUserTokenAsync(
        HttpClient client, string admin, string role, string suffix)
    {
        await CreateUserAsync(client, admin, role, suffix);
        return await SignInAsync(client, suffix, role);
    }

    private static async Task<string> GetAdminTokenAsync(HttpClient client)
    {
        var first = await LoginAsync(client, "admin", "Admin@12345!");
        Assert.Equal(HttpStatusCode.NoContent,
            (await AuthPost(client, first, "/api/auth/change-password",
                new { currentPassword = "Admin@12345!", newPassword = "Admin@Tests99!" })).StatusCode);
        return await LoginAsync(client, "admin", "Admin@Tests99!");
    }

    private static async Task<string> LoginAsync(HttpClient client, string username, string password)
    {
        var res = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<JsonObject>(Json))!["token"]!.GetValue<string>();
    }

    private static Task<HttpResponseMessage> AuthGet(HttpClient client, string token, string url) =>
        Send(client, token, new HttpRequestMessage(HttpMethod.Get, url), stampWith: null);

    private static Task<HttpResponseMessage> AuthDelete(
        HttpClient client, string token, string url, string? stampWith = null) =>
        Send(client, token, new HttpRequestMessage(HttpMethod.Delete, url), stampWith);

    private static Task<HttpResponseMessage> AuthPost(
        HttpClient client, string token, string url, object body, string? stampWith = null) =>
        Send(client, token,
            new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) }, stampWith);

    private static Task<HttpResponseMessage> AuthPut(
        HttpClient client, string token, string url, object body, string? stampWith = null) =>
        Send(client, token,
            new HttpRequestMessage(HttpMethod.Put, url) { Content = JsonContent.Create(body) }, stampWith);

    /// <param name="stampWith">
    /// A privileged token used only to resolve the target's current version for <c>If-Match</c>.
    /// The actor's own request is otherwise unchanged, so a denial is still the endpoint's
    /// authorization check refusing and not the concurrency gate turning the request away — which
    /// would make every negative row pass for the wrong reason.
    /// </param>
    private static async Task<HttpResponseMessage> Send(
        HttpClient client, string token, HttpRequestMessage req, string? stampWith)
    {
        if (stampWith is not null) await IfMatchVersioning.StampAsync(client, req, stampWith);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(req);
    }
}
