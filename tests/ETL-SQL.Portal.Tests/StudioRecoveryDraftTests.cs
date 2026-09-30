using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Recovery drafts keep an author's unsaved Studio edits on the Portal. They hold script text that
/// has not been through a save, so what matters is who can see one and what one may contain: only
/// its author, only for a report they may author, and never a plaintext credential.
/// </summary>
[Trait("Category", "Portal")]
public sealed class StudioRecoveryDraftTests
{
    private const int Author = 3;

    private sealed class RecoveryOffFactory : PortalWebFactory
    {
        protected override void CustomizePortalConfig(PortalConfig config) => config.Studio.DraftRecovery = false;
    }

    [Fact]
    public async Task ADraftIsKeptForItsAuthorAndIsInvisibleToEveryoneElse()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var setup = await SetUpAsync(client);

        Assert.Equal(HttpStatusCode.NoContent, (await Put(client, setup.Admin, setup.ReportId,
            new { content = "-- admin's edit", baseVersion = 1, baseSourceRevision = "r1" })).StatusCode);
        var kept = await Get(client, setup.Admin, setup.ReportId);
        Assert.Equal(HttpStatusCode.OK, kept.StatusCode);
        var body = await kept.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal("-- admin's edit", body!["content"]!.GetValue<string>());
        Assert.Equal(1, body["baseVersion"]!.GetValue<long>());
        Assert.Equal("r1", body["baseSourceRevision"]!.GetValue<string>());

        // Another author of the same report sees nothing of it, and keeps a draft of their own.
        Assert.Equal(HttpStatusCode.NotFound, (await Get(client, setup.Author, setup.ReportId)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Put(client, setup.Author, setup.ReportId,
            new { content = "-- author's edit" })).StatusCode);
        var own = await (await Get(client, setup.Author, setup.ReportId)).Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal("-- author's edit", own!["content"]!.GetValue<string>());
        var admins = await (await Get(client, setup.Admin, setup.ReportId)).Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal("-- admin's edit", admins!["content"]!.GetValue<string>());

        // Deleting one author's draft leaves the other's.
        Assert.Equal(HttpStatusCode.NoContent, (await Send(client, HttpMethod.Delete, setup.Author, $"/api/studio/drafts/{setup.ReportId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Get(client, setup.Author, setup.ReportId)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Get(client, setup.Admin, setup.ReportId)).StatusCode);
    }

    [Fact]
    public async Task ADraftIsRefusedForAReportTheCallerMayNotAuthor()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var setup = await SetUpAsync(client);

        // A publisher with no grant on the folder: the report answers exactly as a missing one.
        var outsider = await CreateSignedInUserAsync(client, setup.Admin, "Publisher");
        Assert.Equal(HttpStatusCode.NotFound, (await Put(client, outsider, setup.ReportId, new { content = "-- probe" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Get(client, outsider, setup.ReportId)).StatusCode);
    }

    [Fact]
    public async Task ADraftHoldingAPlaintextCredentialIsNotKept()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var setup = await SetUpAsync(client);

        var refused = await Put(client, setup.Admin, setup.ReportId,
            new { content = "CREATE CONNECTION db AS MSSQL(SERVER = 'h', PASSWORD = 'hunter2');" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        Assert.Equal("PLAINTEXT_SECRET", (await refused.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.NotFound, (await Get(client, setup.Admin, setup.ReportId)).StatusCode);

        // A secret reference is not a credential.
        Assert.Equal(HttpStatusCode.NoContent, (await Put(client, setup.Admin, setup.ReportId,
            new { content = "CREATE CONNECTION db AS MSSQL(SERVER = 'h', PASSWORD = 'SECRET:db');" })).StatusCode);
    }

    [Fact]
    public async Task AnUntouchedDraftExpires()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var setup = await SetUpAsync(client);
        Assert.Equal(HttpStatusCode.NoContent, (await Put(client, setup.Admin, setup.ReportId, new { content = "-- old" })).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var draft = await db.StudioRecoveryDrafts.SingleAsync(item => item.ReportId == setup.ReportId);
            draft.UpdatedAt = DateTime.UtcNow.AddDays(-8);
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.NotFound, (await Get(client, setup.Admin, setup.ReportId)).StatusCode);
        using var check = factory.Services.CreateScope();
        Assert.False(await check.ServiceProvider.GetRequiredService<PortalDbContext>()
            .StudioRecoveryDrafts.AnyAsync(item => item.ReportId == setup.ReportId));
    }

    [Fact]
    public async Task WithRecoveryOff_NoDraftIsKeptAndTheSessionSaysSo()
    {
        using var factory = new RecoveryOffFactory();
        using var client = factory.CreateClient();
        var setup = await SetUpAsync(client);

        var session = await (await Send(client, HttpMethod.Get, setup.Admin, "/api/studio/session")).Content.ReadFromJsonAsync<JsonObject>();
        Assert.False(session!["draftRecovery"]!.GetValue<bool>());
        var refused = await Put(client, setup.Admin, setup.ReportId, new { content = "-- edit" });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("DRAFT_RECOVERY_OFF", (await refused.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>());
        using var scope = factory.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<PortalDbContext>().StudioRecoveryDrafts.AnyAsync());
    }

    // ── Setup ────────────────────────────────────────────────────────────────

    private sealed record Setup(string Admin, string Author, int ReportId);

    /// <summary>A report the admin created, and a second user with an Author grant on its folder.</summary>
    private static async Task<Setup> SetUpAsync(HttpClient client)
    {
        var admin = await AdminTokenAsync(client);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var folder = await Json(await Send(client, HttpMethod.Post, admin, "/api/folders", new { name = $"drafts_{suffix}", parentId = (int?)null }));
        var folderId = folder["id"]!.GetValue<int>();
        var report = await Send(client, HttpMethod.Post, admin, "/api/studio/reports",
            new { folderId, name = $"Drafts {suffix}", scriptText = "SET REPORT TITLE = 'Drafts';" });
        Assert.True(report.IsSuccessStatusCode, $"report create: {(int)report.StatusCode}");
        var reportId = (await Json(report))["id"]!.GetValue<int>();

        var group = await Json(await Send(client, HttpMethod.Post, admin, "/api/admin/groups", new { name = $"dg_{suffix}", description = "drafts" }));
        var groupId = group["id"]!.GetValue<int>();
        // The grant goes first: changing a group's access ends its members' open sessions.
        Assert.True((await Send(client, HttpMethod.Post, admin, $"/api/folders/{folderId}/acl",
            new { groupId, permission = Author })).IsSuccessStatusCode);
        var author = await CreateSignedInUserAsync(client, admin, "Publisher", groupId);
        return new Setup(admin, author, reportId);
    }

    private static async Task<string> CreateSignedInUserAsync(HttpClient client, string admin, string role, int? groupId = null)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var created = await Send(client, HttpMethod.Post, admin, "/api/admin/users",
            new { username = $"u{suffix}", password = $"Init@{suffix}9!", role, email = $"u{suffix}@localhost" });
        Assert.True(created.IsSuccessStatusCode, $"user create: {created.StatusCode}");
        var userId = (await Json(created))["id"]!.GetValue<int>();
        if (groupId is { } id)
            Assert.True((await Send(client, HttpMethod.Post, admin, $"/api/admin/groups/{id}/members", new { userId })).IsSuccessStatusCode);

        var first = await LoginAsync(client, $"u{suffix}", $"Init@{suffix}9!");
        Assert.Equal(HttpStatusCode.NoContent, (await Send(client, HttpMethod.Post, first, "/api/auth/change-password",
            new { currentPassword = $"Init@{suffix}9!", newPassword = $"Next@{suffix}9!" })).StatusCode);
        return await LoginAsync(client, $"u{suffix}", $"Next@{suffix}9!");
    }

    private static async Task<string> AdminTokenAsync(HttpClient client)
    {
        const string initial = "Admin@12345!";
        const string changed = "Admin@Drafts99!";
        var first = await LoginAsync(client, "admin", initial);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(client, HttpMethod.Post, first, "/api/auth/change-password",
            new { currentPassword = initial, newPassword = changed })).StatusCode);
        return await LoginAsync(client, "admin", changed);
    }

    private static async Task<string> LoginAsync(HttpClient client, string user, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username = user, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await Json(response))["token"]!.GetValue<string>();
    }

    private static Task<HttpResponseMessage> Get(HttpClient client, string token, int reportId) =>
        Send(client, HttpMethod.Get, token, $"/api/studio/drafts/{reportId}");

    private static Task<HttpResponseMessage> Put(HttpClient client, string token, int reportId, object body) =>
        Send(client, HttpMethod.Put, token, $"/api/studio/drafts/{reportId}", body);

    private static async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string token, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.Authorization = new("Bearer", token);
        // Group and ACL mutations are version-checked; without the If-Match stamp they 428/412.
        if (method == HttpMethod.Post) await IfMatchVersioning.StampAsync(client, request, token);
        return await client.SendAsync(request);
    }

    private static async Task<JsonObject> Json(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonObject>())!;
}
