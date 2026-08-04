using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Draft → review → publish.
///
/// <para>The workflow's only reason to exist is that <b>somebody other than the author</b> agrees a
/// change should ship. Everything else here — statuses, concurrency, audit — is scaffolding around
/// that one property, so it is asserted first and from several directions: an author cannot approve
/// their own work even as an Admin, an approval does not survive the content being edited underneath
/// it, and an approval granted against a stale base does not license a publish.</para>
///
/// <para>The whole feature is opt-in, so the disabled case is asserted too. A review step that
/// arrived unannounced with an upgrade would stop every author in an organization that had not yet
/// decided who reviews.</para>
/// </summary>
[Trait("Category", "Portal")]
[Trait("Category", "Smoke.Security")]
public sealed class ReportDraftWorkflowTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string Script = "SET REPORT TITLE = 'Draft';";
    private const string Revised = "SET REPORT TITLE = 'Revised';";

    /// <summary>Approval on, and every capability granted to Admin so the tests exercise the workflow.</summary>
    private sealed class ApprovalFactory(bool enabled = true) : PortalWebFactory
    {
        protected override void CustomizePortalConfig(PortalConfig config)
        {
            config.Studio.Mode = StudioDeploymentMode.SourceControlled;
            config.Studio.RequireApprovalToPublish = enabled;
            config.Studio.RoleCapabilities["Admin"] = [.. StudioCapabilities.All];
            config.Studio.RoleCapabilities["Publisher"] = [.. StudioCapabilities.All];
        }
    }

    // ── Separation of duties ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnAuthorCannotApproveTheirOwnDraft_EvenAsAdmin()
    {
        using var factory = new ApprovalFactory();
        using var client = factory.CreateClient();
        var admin = await GetAdminTokenAsync(client);
        var reportId = await PublishReportAsync(client, admin, factory);

        var draft = await SaveDraftAsync(client, admin, reportId, Revised);
        draft = await PostAsync(client, admin, $"/api/reports/{reportId}/draft/submit", null, draft);

        var res = await PostRawAsync(client, admin, $"/api/reports/{reportId}/draft/approve",
            new { reason = "Looks fine to me." }, draft);

        // The admin authored it, holds every capability, and is still refused. A four-eyes control
        // the most privileged account can bypass fails exactly when it is needed, because the
        // account that gets compromised or leaned on is the privileged one.
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Contains("cannot approve your own draft", await res.Content.ReadAsStringAsync());

        Assert.Equal("pending", (await GetDraftAsync(client, admin, reportId))["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task ADifferentReviewerCanApprove_AndIsRecordedOnWhatShipped()
    {
        using var factory = new ApprovalFactory();
        using var client = factory.CreateClient();
        var admin = await GetAdminTokenAsync(client);
        var reportId = await PublishReportAsync(client, admin, factory);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var reviewer = await CreateReviewerAsync(client, admin, factory, reportId, suffix);

        var draft = await SaveDraftAsync(client, admin, reportId, Revised);
        draft = await PostAsync(client, admin, $"/api/reports/{reportId}/draft/submit", null, draft);
        var approved = await PostAsync(client, reviewer, $"/api/reports/{reportId}/draft/approve",
            new { reason = "Checked the join." }, draft);

        Assert.Equal("approved", approved["status"]!.GetValue<string>());
        // Named on the draft itself, not only in the trail: "who let this into production" has to be
        // answerable from the thing that went to production.
        Assert.Equal($"reviewer_{suffix}", approved["approvedByUserName"]!.GetValue<string>());

        var decisions = approved["decisions"]!.AsArray();
        var decision = decisions.First(d => d!["decision"]!.GetValue<string>() == "approve")!.AsObject();
        // An approval is of specific content, so it names the hash. Without that, "was this
        // reviewed?" cannot be answered for the version that actually shipped.
        Assert.Equal(approved["scriptHash"]!.GetValue<string>(), decision["scriptHash"]!.GetValue<string>());
    }

    [Fact]
    public async Task EditingAfterApproval_RevokesIt()
    {
        using var factory = new ApprovalFactory();
        using var client = factory.CreateClient();
        var admin = await GetAdminTokenAsync(client);
        var reportId = await PublishReportAsync(client, admin, factory);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var reviewer = await CreateReviewerAsync(client, admin, factory, reportId, suffix);

        var draft = await SaveDraftAsync(client, admin, reportId, Revised);
        draft = await PostAsync(client, admin, $"/api/reports/{reportId}/draft/submit", null, draft);
        draft = await PostAsync(client, reviewer, $"/api/reports/{reportId}/draft/approve",
            new { reason = "Fine." }, draft);
        Assert.Equal("approved", draft["status"]!.GetValue<string>());

        // Get a trivial change approved, then swap the body. Without this the reviewer's name would
        // end up attached to content they never saw.
        var edited = await SaveDraftAsync(client, admin, reportId, "SET REPORT TITLE = 'Something else';");

        Assert.Equal("draft", edited["status"]!.GetValue<string>());
        Assert.Null(edited["approvedByUserName"]?.GetValue<string?>());

        var publish = await PostRawAsync(client, admin, $"/api/reports/{reportId}/draft/publish", null, edited);
        Assert.Equal(HttpStatusCode.Conflict, publish.StatusCode);
    }

    // ── Optimistic concurrency ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DecidingWithAStaleVersion_IsRefused()
    {
        using var factory = new ApprovalFactory();
        using var client = factory.CreateClient();
        var admin = await GetAdminTokenAsync(client);
        var reportId = await PublishReportAsync(client, admin, factory);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var reviewer = await CreateReviewerAsync(client, admin, factory, reportId, suffix);

        var draft = await SaveDraftAsync(client, admin, reportId, Revised);
        var stale = draft.DeepClone()!.AsObject();
        draft = await PostAsync(client, admin, $"/api/reports/{reportId}/draft/submit", null, draft);

        // The reviewer loaded the draft, the author changed it, the reviewer approves the version
        // they were looking at. That has to fail, or the approval describes something else.
        var res = await PostRawAsync(client, reviewer, $"/api/reports/{reportId}/draft/approve",
            new { reason = "Approving what I read." }, stale);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task DecidingWithNoVersionAtAll_IsRefused()
    {
        using var factory = new ApprovalFactory();
        using var client = factory.CreateClient();
        var admin = await GetAdminTokenAsync(client);
        var reportId = await PublishReportAsync(client, admin, factory);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var reviewer = await CreateReviewerAsync(client, admin, factory, reportId, suffix);

        var draft = await SaveDraftAsync(client, admin, reportId, Revised);
        await PostAsync(client, admin, $"/api/reports/{reportId}/draft/submit", null, draft);

        var req = new HttpRequestMessage(
            HttpMethod.Post, $"/api/reports/{reportId}/draft/approve")
        { Content = JsonContent.Create(new { reason = "No header." }) };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", reviewer);
        var res = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.PreconditionRequired, res.StatusCode);
    }

    // ── Publishing ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task OnlyAnApprovedDraftPublishes_AndPublishingWritesTheScript()
    {
        using var factory = new ApprovalFactory();
        using var client = factory.CreateClient();
        var admin = await GetAdminTokenAsync(client);
        var (reportId, scriptPath) = await PublishReportWithPathAsync(client, admin, factory);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var reviewer = await CreateReviewerAsync(client, admin, factory, reportId, suffix);

        var draft = await SaveDraftAsync(client, admin, reportId, Revised);

        // Unapproved is exactly what the workflow exists to stop.
        var early = await PostRawAsync(client, admin, $"/api/reports/{reportId}/draft/publish", null, draft);
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        Assert.Equal(Script, await File.ReadAllTextAsync(scriptPath));

        draft = await PostAsync(client, admin, $"/api/reports/{reportId}/draft/submit", null, draft);
        draft = await PostAsync(client, reviewer, $"/api/reports/{reportId}/draft/approve",
            new { reason = "Good." }, draft);

        var published = await PostAsync(client, admin, $"/api/reports/{reportId}/draft/publish", null, draft);
        Assert.Equal("published", published["status"]!.GetValue<string>());
        Assert.Equal(Revised, await File.ReadAllTextAsync(scriptPath));
    }

    [Fact]
    public async Task EveryStepIsAudited_WithTheContentHashOnTheApproval()
    {
        using var factory = new ApprovalFactory();
        using var client = factory.CreateClient();
        var admin = await GetAdminTokenAsync(client);
        var reportId = await PublishReportAsync(client, admin, factory);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var reviewer = await CreateReviewerAsync(client, admin, factory, reportId, suffix);

        var draft = await SaveDraftAsync(client, admin, reportId, Revised);
        draft = await PostAsync(client, admin, $"/api/reports/{reportId}/draft/submit", null, draft);
        draft = await PostAsync(client, reviewer, $"/api/reports/{reportId}/draft/approve",
            new { reason = "Reviewed." }, draft);
        await PostAsync(client, admin, $"/api/reports/{reportId}/draft/publish", null, draft);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var actions = await db.AuditLogs.AsNoTracking()
            .Where(a => a.Action.StartsWith("DRAFT_"))
            .Select(a => new { a.Action, a.Detail })
            .ToListAsync();

        foreach (var expected in new[] { "DRAFT_SAVE", "DRAFT_SUBMIT", "DRAFT_APPROVE", "DRAFT_PUBLISH" })
            Assert.Contains(actions, a => a.Action == expected);

        // The approval's audit row names the content and the author it was reviewed for, so a later
        // reviewer does not have to reconstruct either from surrounding rows.
        var approve = actions.First(a => a.Action == "DRAFT_APPROVE");
        Assert.Contains("hash=sha256:", approve.Detail);
        Assert.Contains("author=admin", approve.Detail);
    }

    // ── Opt-in ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WithTheWorkflowOff_TheDraftEndpointsAreNotPresent()
    {
        using var factory = new ApprovalFactory(enabled: false);
        using var client = factory.CreateClient();
        var admin = await GetAdminTokenAsync(client);
        var reportId = await PublishReportAsync(client, admin, factory);

        // Off by default. An organization that has not decided who reviews must not find every
        // author blocked behind nobody after an upgrade.
        var res = await AuthGet(client, admin, $"/api/reports/{reportId}/draft");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Contains("RequireApprovalToPublish", await res.Content.ReadAsStringAsync());
    }

    // ── fixtures ────────────────────────────────────────────────────────────────────────────────

    /// <summary>A second identity with Author on the report and the ReportApprove capability.</summary>
    private static async Task<string> CreateReviewerAsync(
        HttpClient client, string admin, PortalWebFactory factory, int reportId, string suffix)
    {
        var username = $"reviewer_{suffix}";
        const string initial = "Review@Tests99!";
        const string password = "Review@Tests99b!";

        var created = await AuthPost(client, admin, "/api/admin/users",
            new { username, password = initial, role = "Publisher", email = $"{username}@example.com" });
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        var userId = (await created.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();

        // Author on the report's folder — a reviewer has to be able to read what they approve.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var report = await db.Reports.FirstAsync(r => r.Id == reportId);
            db.ReportAcls.Add(new ReportAcl
            {
                ReportId = reportId,
                UserId = userId,
                Permission = FolderPermission.Author,
            });
            await db.SaveChangesAsync();
        }

        var first = await LoginAsync(client, username, initial);
        Assert.Equal(HttpStatusCode.NoContent,
            (await AuthPost(client, first, "/api/auth/change-password",
                new { currentPassword = initial, newPassword = password })).StatusCode);
        return await LoginAsync(client, username, password);
    }

    private static async Task<int> PublishReportAsync(
        HttpClient client, string admin, PortalWebFactory factory) =>
        (await PublishReportWithPathAsync(client, admin, factory)).ReportId;

    private static async Task<(int ReportId, string ScriptPath)> PublishReportWithPathAsync(
        HttpClient client, string admin, PortalWebFactory factory)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var folder = await AuthPost(client, admin, "/api/folders", new { name = $"drafts_{suffix}" });
        var folderId = (await folder.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();

        var scriptName = $"draft_{suffix}.rptsql";
        var scriptPath = Path.Combine(factory.TempDir, "scripts", scriptName);
        await File.WriteAllTextAsync(scriptPath, Script);

        var res = await AuthPost(client, admin, "/api/reports",
            new { folderId, name = $"Draft {suffix}", scriptPath = scriptName });
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        var id = (await res.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();
        return (id, scriptPath);
    }

    private static async Task<JsonObject> SaveDraftAsync(
        HttpClient client, string token, int reportId, string scriptText)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/reports/{reportId}/draft")
        { Content = JsonContent.Create(new { scriptText, baseScriptHash = (string?)null }) };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var res = await client.SendAsync(req);
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<JsonObject>(Json))!;
    }

    private static async Task<JsonObject> GetDraftAsync(HttpClient client, string token, int reportId)
    {
        var res = await AuthGet(client, token, $"/api/reports/{reportId}/draft");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<JsonObject>(Json))!;
    }

    private static async Task<JsonObject> PostAsync(
        HttpClient client, string token, string url, object? body, JsonObject draft)
    {
        var res = await PostRawAsync(client, token, url, body, draft);
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<JsonObject>(Json))!;
    }

    private static Task<HttpResponseMessage> PostRawAsync(
        HttpClient client, string token, string url, object? body, JsonObject draft)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url)
        { Content = JsonContent.Create(body ?? new { }) };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.TryAddWithoutValidation("If-Match", $"\"{draft["version"]!.GetValue<long>()}\"");
        return client.SendAsync(req);
    }

    private static Task<HttpResponseMessage> AuthGet(HttpClient client, string token, string url)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(req);
    }

    private static Task<HttpResponseMessage> AuthPost(
        HttpClient client, string token, string url, object body)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(req);
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
}
