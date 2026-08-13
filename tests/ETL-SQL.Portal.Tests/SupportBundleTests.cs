using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// The Portal's online-safe support bundle. Two properties make it safe to expose: it collects
/// counts, versions and states rather than content, and everything textual goes through the same
/// redactor the CLI bundle uses.
///
/// The CLI's <c>admin support-bundle</c> stays the recovery path for when the Portal is down. This
/// exists for the common case — someone with only a browser being asked for diagnostics.
/// </summary>
[Trait("Category", "Portal")]
[Trait("Category", "Smoke.Security")]
public sealed class SupportBundleTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task ReviewShowsEverySection_AndSaysWhatItLeavesOut()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);

        var review = await ReviewAsync(client, adminToken);

        var sections = review["sections"]!.AsArray()
            .Select(section => section!["key"]!.GetValue<string>()).ToList();
        Assert.Contains("health", sections);
        Assert.Contains("deployment", sections);
        Assert.Contains("catalog", sections);
        Assert.Contains("auditDelivery", sections);
        Assert.Contains("configuration", sections);

        // An artifact that does not say what it omitted invites the assumption it omitted nothing.
        var excluded = review["excluded"]!.AsArray().Select(e => e!.GetValue<string>()).ToList();
        Assert.Contains(excluded, entry => entry.Contains("Report and dataset contents", StringComparison.Ordinal));
        Assert.Contains(excluded, entry => entry.Contains("Secret values", StringComparison.Ordinal));
        Assert.False(string.IsNullOrWhiteSpace(review["contentHash"]!.GetValue<string>()));
    }

    [Fact]
    public async Task DedicatedSupportBundleUsesHostFixedTenantContext()
    {
        using var factory = new TenantFixedFactory();
        using var client = factory.CreateClient();
        var review = await ReviewAsync(client, await GetAdminTokenAsync(client));
        var deployment = review["sections"]!.AsArray()
            .Single(section => section!["key"]!.GetValue<string>() == "deployment")!["payload"]!.AsObject();

        Assert.Equal("tenant-alpha", deployment["tenantId"]!.GetValue<string>());
        Assert.Equal("HostFixed", deployment["tenantContextOrigin"]!.GetValue<string>());
    }

    [Fact]
    public async Task DedicatedPlatformSupportRequiresAndAuditsTenantApproval()
    {
        using var factory = new TenantFixedFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);

        using (var missing = new HttpRequestMessage(HttpMethod.Post, "/api/platform/support-bundle"))
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(missing)).StatusCode);

        var hash = (await ReviewAsync(client, adminToken))["contentHash"]!.GetValue<string>();
        var issuedResponse = await AuthPost(client, adminToken, "/api/admin/support-access/approvals", new
        {
            platformActor = "platform:case-42",
            purpose = "Investigate failed refreshes",
            acknowledgedContent = hash,
            lifetimeMinutes = 15
        });
        Assert.Equal(HttpStatusCode.OK, issuedResponse.StatusCode);
        var issued = (await issuedResponse.Content.ReadFromJsonAsync<JsonObject>(Json))!;
        var capability = issued["capability"]!.GetValue<string>();

        using var download = new HttpRequestMessage(HttpMethod.Post, "/api/platform/support-bundle");
        download.Headers.Add(SupportAccessApprovalService.HeaderName, capability);
        var result = await client.SendAsync(download);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("application/json", result.Content.Headers.ContentType?.MediaType);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        Assert.True(await db.AuditLogs.AnyAsync(log =>
            log.Action == "APPROVE_PLATFORM_SUPPORT_ACCESS" && log.UserId != null));
        var access = await db.AuditLogs.SingleAsync(log =>
            log.Action == "PLATFORM_SUPPORT_BUNDLE_DOWNLOADED");
        Assert.Null(access.UserId);
        Assert.Equal("PlatformOperator", access.ActorType);
        Assert.Equal("platform:case-42", access.ActorId);
        Assert.Equal("support.bundle.read", access.EffectiveScopes);
        Assert.DoesNotContain(capability, access.Detail ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SupportApprovalRequiresTheCurrentReviewedDisclosure()
    {
        using var factory = new TenantFixedFactory();
        using var client = factory.CreateClient();
        var response = await AuthPost(client, await GetAdminTokenAsync(client),
            "/api/admin/support-access/approvals", new
            {
                platformActor = "platform:case-42",
                purpose = "Investigate failed refreshes",
                acknowledgedContent = new string('0', 64),
                lifetimeMinutes = 15
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task TheBundleCarriesCountsRatherThanContent()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var folderId = await CreateFolderAsync(client, adminToken, $"bundle_folder_{suffix}");
        var scriptPath = Path.Combine(factory.TempDir, "scripts", $"bundle-{suffix}.rptsql");
        await File.WriteAllTextAsync(scriptPath, "SET REPORT TITLE = 'Confidential Margin Review';");
        Assert.Equal(HttpStatusCode.Created,
            (await AuthPost(client, adminToken, "/api/reports",
                new { folderId, name = $"Secret Report {suffix}", scriptPath })).StatusCode);

        var review = await ReviewAsync(client, adminToken);
        var raw = review.ToJsonString();

        // The catalog section counts reports; it must not name them.
        var catalog = review["sections"]!.AsArray()
            .Single(section => section!["key"]!.GetValue<string>() == "catalog")!["payload"]!.AsObject();
        Assert.True(catalog["reports"]!.GetValue<int>() >= 1);
        Assert.DoesNotContain($"Secret Report {suffix}", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("Confidential Margin Review", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfigurationIsRedactedByKey()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);

        var review = await ReviewAsync(client, adminToken);
        var raw = review.ToJsonString();

        // The JWT secret and the dataset at-rest key are both configured in the test host.
        Assert.DoesNotContain("integration-test-secret-key-1234567890", raw, StringComparison.Ordinal);
        Assert.DoesNotContain(HostedPortalFactory.DefaultAtRestKey, raw, StringComparison.Ordinal);

        // ...while the non-secret metadata that makes the bundle useful survives.
        Assert.Contains("v1", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AcknowledgingTheReviewYouActuallyMade_Succeeds()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);

        var hash = (await ReviewAsync(client, adminToken))["contentHash"]!.GetValue<string>();

        // Reviewing audits the review, which moves the outbox counters the bundle reports, and
        // health timings vary run to run. Neither is a reason to refuse a download, so neither is in
        // the hash — otherwise every review would be stale the instant it was made.
        Assert.Equal(HttpStatusCode.OK,
            (await AuthGet(client, adminToken, $"/api/admin/support-bundle?acknowledgedContent={hash}")).StatusCode);

        await CreateFolderAsync(client, adminToken, $"churn_{Guid.NewGuid():N}"[..20]);
        Assert.Equal(HttpStatusCode.OK,
            (await AuthGet(client, adminToken, $"/api/admin/support-bundle?acknowledgedContent={hash}")).StatusCode);
    }

    [Fact]
    public async Task DownloadRefusesAReviewOfADifferentDisclosure()
    {
        // What an acknowledgement is about is the disclosure: this deployment, this configuration,
        // these exclusions. Change the configuration and the earlier approval no longer describes
        // what would be handed over.
        using var baseline = new PortalWebFactory();
        using var changed = new DifferentDisclosureFactory();
        using var baselineClient = baseline.CreateClient();
        using var changedClient = changed.CreateClient();

        var baselineHash = (await ReviewAsync(baselineClient, await GetAdminTokenAsync(baselineClient)))
            ["contentHash"]!.GetValue<string>();
        var changedAdmin = await GetAdminTokenAsync(changedClient);
        var changedHash = (await ReviewAsync(changedClient, changedAdmin))["contentHash"]!.GetValue<string>();

        Assert.NotEqual(baselineHash, changedHash);

        var stale = await AuthGet(changedClient,
            changedAdmin, $"/api/admin/support-bundle?acknowledgedContent={baselineHash}");
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        using var scope = changed.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        Assert.True(await db.AuditLogs.AnyAsync(log => log.Action == "DOWNLOAD_SUPPORT_BUNDLE_REFUSED"));
    }

    /// <summary>A portal whose disclosed configuration differs from the default test host's.</summary>
    private sealed class DifferentDisclosureFactory : PortalWebFactory
    {
        protected override void CustomizePortalConfig(PortalConfig config)
        {
            config.Studio.Mode = StudioDeploymentMode.CatalogOnly;
            config.Audit.TransportEndpoint = "https://collector.example.invalid/ingest";
        }
    }

    private sealed class TenantFixedFactory : PortalWebFactory
    {
        protected override void CustomizePortalConfig(PortalConfig config) =>
            config.TenantId = "tenant-alpha";
    }

    [Fact]
    public async Task ReviewAndDownloadAreBothAudited()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);

        await ReviewAsync(client, adminToken);
        Assert.Equal(HttpStatusCode.OK, (await AuthGet(client, adminToken, "/api/admin/support-bundle")).StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        Assert.True(await db.AuditLogs.AnyAsync(log => log.Action == "REVIEW_SUPPORT_BUNDLE"));
        var download = await db.AuditLogs
            .Where(log => log.Action == "DOWNLOAD_SUPPORT_BUNDLE")
            .OrderByDescending(log => log.Id)
            .FirstAsync();
        Assert.Contains("no review acknowledged", download.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IsAdministratorOnly()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await CreateViewerAsync(client, adminToken, $"bundle_deny_{suffix}");
        var viewerToken = await LoginAsync(client, $"bundle_deny_{suffix}", "Ready@Test2!");

        Assert.Equal(HttpStatusCode.Forbidden,
            (await AuthGet(client, viewerToken, "/api/admin/support-bundle/review")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await AuthGet(client, viewerToken, "/api/admin/support-bundle")).StatusCode);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static async Task<JsonObject> ReviewAsync(HttpClient client, string adminToken)
    {
        var response = await AuthGet(client, adminToken, "/api/admin/support-bundle/review");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>(Json))!;
    }

    private static async Task<int> CreateFolderAsync(HttpClient client, string adminToken, string name)
    {
        var response = await AuthPost(client, adminToken, "/api/folders", new { name, parentId = (int?)null });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();
    }

    private static async Task CreateViewerAsync(HttpClient client, string adminToken, string username)
    {
        var create = await AuthPost(client, adminToken, "/api/admin/users", new
        {
            username,
            email = $"{username}@test.local",
            password = "Initial@Test1!",
            role = "Viewer"
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var initial = await LoginAsync(client, username, "Initial@Test1!");
        Assert.Equal(HttpStatusCode.NoContent,
            (await AuthPost(client, initial, "/api/auth/change-password",
                new { currentPassword = "Initial@Test1!", newPassword = "Ready@Test2!" })).StatusCode);
    }

    private static async Task<string> GetAdminTokenAsync(HttpClient client)
    {
        var initial = await LoginAsync(client, "admin", "Admin@12345!");
        Assert.Equal(HttpStatusCode.NoContent,
            (await AuthPost(client, initial, "/api/auth/change-password",
                new { currentPassword = "Admin@12345!", newPassword = "Admin@Tests99!" })).StatusCode);
        return await LoginAsync(client, "admin", "Admin@Tests99!");
    }

    private static async Task<string> LoginAsync(HttpClient client, string username, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>(Json))!["token"]!.GetValue<string>();
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
