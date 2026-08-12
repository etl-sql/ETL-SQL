using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// The governance dashboard's contract with the people who rely on it.
///
/// <para>The property under test throughout is that governance state is <b>durable, authorized, and
/// audited</b>. A dashboard whose findings live in a browser tab is a slideshow: it cannot survive a
/// refresh, cannot be reviewed by a second person, and cannot be evidence in an audit. So these
/// tests check persistence across a fresh client, the three authority tiers separately, and that
/// every mutation leaves an audit row.</para>
///
/// <para>The other property is that <b>suppressions expire when the thing they were granted for
/// changes</b>. An accepted risk that silently covers a later version of an asset is worse than no
/// governance at all, because it looks like governance.</para>
/// </summary>
[Trait("Category", "Portal")]
[Trait("Category", "Smoke.Security")]
public sealed class GovernanceDashboardTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ── Read model ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NeverScanned_IsReportedAsUnscanned_NotAsAnEstateWithNoFindings()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);

        var dashboard = await GetJsonAsync(client, token, "/api/governance/dashboard");

        // Zero findings and never-looked are opposite conclusions. If lastScan were omitted or
        // faked, a fresh install would present as a fully governed estate.
        Assert.Null(dashboard["lastScan"]?.AsObject());
        Assert.Equal(0, dashboard["summary"]!["openFindings"]!.GetValue<int>());
    }

    [Fact]
    public async Task ScanProducesExplainedScores_WithEveryLostPointMappedToARule()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);
        await SeedLineageAsync(factory, "sales.orders", tags: new() { ["owner"] = "chuck" });

        var scan = await PostJsonAsync(client, token, "/api/governance/scan", new { });
        Assert.Equal("completed", scan["status"]!.GetValue<string>());
        Assert.True(scan["assetsScanned"]!.GetValue<int>() >= 1);

        var dashboard = await GetJsonAsync(client, token, "/api/governance/dashboard");
        var asset = SingleAsset(dashboard, "sales.orders");

        Assert.True(asset["score"]!.GetValue<int>() < 100);
        var deductions = asset["deductions"]!.AsArray();
        Assert.NotEmpty(deductions);
        // A score with no explanation is a number stewards learn to ignore.
        foreach (var deduction in deductions)
        {
            Assert.False(string.IsNullOrWhiteSpace(deduction!["ruleKey"]!.GetValue<string>()));
            Assert.False(string.IsNullOrWhiteSpace(deduction["reason"]!.GetValue<string>()));
            Assert.True(deduction["points"]!.GetValue<int>() > 0);
        }
        Assert.Equal(
            100 - deductions.Sum(d => d!["points"]!.GetValue<int>()),
            asset["score"]!.GetValue<int>());

        Assert.Contains(
            asset["findings"]!.AsArray(),
            f => f!["ruleKey"]!.GetValue<string>() == "missing-metadata");
    }

    [Fact]
    public async Task FindingsAndDecisions_SurviveAFreshClient()
    {
        using var factory = new PortalWebFactory();
        await SeedLineageAsync(factory, "hr.salaries", tags: new() { ["owner"] = "chuck" });

        int findingId;
        using (var client = factory.CreateClient())
        {
            var token = await GetAdminTokenAsync(client);
            await PostJsonAsync(client, token, "/api/governance/scan", new { });
            await PostJsonAsync(client, token, "/api/governance/categories",
                new { value = "false-positive", label = "False Positive", color = "false-positive", expiryDays = (int?)null, disabled = false });

            var finding = FirstFinding(await GetJsonAsync(client, token, "/api/governance/dashboard"), "hr.salaries");
            findingId = finding["id"]!.GetValue<int>();
            var decided = await PostJsonAsync(client, token, $"/api/governance/findings/{findingId}/decide",
                new
                {
                    decision = "ignore",
                    categoryValue = "false-positive",
                    reason = "Metadata lives in the enterprise catalog for this table.",
                    assetVersion = finding["assetVersion"]!.GetValue<string>()
                });
            Assert.Equal("ignored", decided["status"]!.GetValue<string>());
        }

        // A second client is a second browser: nothing in memory carries over, so anything still
        // here came from the database.
        using (var fresh = factory.CreateClient())
        {
            var token = await LoginAsync(fresh, "admin", "Admin@Tests99!");
            var findings = await GetArrayAsync(fresh, token, "/api/governance/findings?status=ignored");
            var persisted = Assert.Single(findings, f => f!["id"]!.GetValue<int>() == findingId)!.AsObject();
            var decision = Assert.Single(persisted["decisions"]!.AsArray())!.AsObject();
            Assert.Equal("ignore", decision["decision"]!.GetValue<string>());
            Assert.Contains("enterprise catalog", decision["reason"]!.GetValue<string>());
            Assert.Equal("admin", decision["decidedBy"]!.GetValue<string>());
        }
    }

    // ── Suppression lifecycle ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AcceptedRisk_ReopensWhenTheAssetChanges_AndHoldsWhenItDoesNot()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);
        await SeedLineageAsync(factory, "finance.ledger", tags: new() { ["owner"] = "chuck" });
        await PostJsonAsync(client, token, "/api/governance/scan", new { });

        var finding = FirstFinding(await GetJsonAsync(client, token, "/api/governance/dashboard"), "finance.ledger");
        var findingId = finding["id"]!.GetValue<int>();
        await PostJsonAsync(client, token, $"/api/governance/findings/{findingId}/decide",
            new
            {
                decision = "accept-risk",
                categoryValue = (string?)null,
                reason = "Sunsetting this table next quarter.",
                assetVersion = finding["assetVersion"]!.GetValue<string>()
            });

        // Re-scanning the same asset must not disturb a decision made about it.
        await PostJsonAsync(client, token, "/api/governance/scan", new { });
        Assert.Equal("accepted-risk", (await FindingByIdAsync(client, token, findingId))["status"]!.GetValue<string>());

        // A new run of the script is a new version. The steward accepted a risk on content they
        // read; this is different content, so the acceptance stops applying.
        await SeedLineageAsync(factory, "finance.ledger", tags: new() { ["owner"] = "chuck" },
            runAt: DateTime.UtcNow.AddMinutes(5), scriptPath: "loads/ledger_v2.etlsql");
        await PostJsonAsync(client, token, "/api/governance/scan", new { });

        var reopened = await FindingByIdAsync(client, token, findingId);
        Assert.Equal("reopened", reopened["status"]!.GetValue<string>());
        // The decision itself is not erased — it is the record of why it was accepted once.
        Assert.NotEmpty(reopened["decisions"]!.AsArray());
    }

    [Fact]
    public async Task FixedAsset_ResolvesItsFindingWithoutAStewardClosingIt()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);

        await SeedLineageAsync(factory, "ops.events", tags: new() { ["owner"] = "chuck" });
        await PostJsonAsync(client, token, "/api/governance/scan", new { });
        var findingId = FirstFinding(await GetJsonAsync(client, token, "/api/governance/dashboard"), "ops.events")
            ["id"]!.GetValue<int>();

        // The developer publishes a version with the metadata filled in. Automatic reconciliation is
        // the promise: fixing the script clears the queue, nobody closes tickets by hand.
        await SeedLineageAsync(factory, "ops.events",
            tags: AllRequiredTags(),
            runAt: DateTime.UtcNow.AddMinutes(5));
        await PostJsonAsync(client, token, "/api/governance/scan", new { });

        Assert.Equal("resolved", (await FindingByIdAsync(client, token, findingId))["status"]!.GetValue<string>());
    }

    // ── Authorization boundaries ────────────────────────────────────────────────────────────────

    [Theory]
    // Read is deliberately wide: a steward blind to other stewards' work cannot cover for them.
    [InlineData("StewardshipViewer", HttpStatusCode.OK)]
    [InlineData("DataSteward", HttpStatusCode.OK)]
    [InlineData("StewardshipManager", HttpStatusCode.OK)]
    // A report reader is not a governance reader. Findings name assets and their weaknesses.
    [InlineData("Viewer", HttpStatusCode.Forbidden)]
    [InlineData("Publisher", HttpStatusCode.Forbidden)]
    public async Task DashboardRead_FollowsTheReadTier(string role, HttpStatusCode expected)
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var token = await CreateUserTokenAsync(client, adminToken, role);

        Assert.Equal(expected, (await AuthGet(client, token, "/api/governance/dashboard")).StatusCode);
    }

    [Theory]
    // Deciding is steward judgement; viewing is not deciding.
    [InlineData("StewardshipViewer", false)]
    [InlineData("DataSteward", true)]
    [InlineData("StewardshipManager", true)]
    public async Task DecidingAFinding_RequiresTheDecideTier(string role, bool allowed)
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        await SeedLineageAsync(factory, "sales.leads", tags: new() { ["owner"] = "chuck" });
        await PostJsonAsync(client, adminToken, "/api/governance/scan", new { });
        var finding = FirstFinding(await GetJsonAsync(client, adminToken, "/api/governance/dashboard"), "sales.leads");

        var token = await CreateUserTokenAsync(client, adminToken, role);
        var res = await AuthPost(client, token, $"/api/governance/findings/{finding["id"]!.GetValue<int>()}/decide",
            new
            {
                decision = "ignore",
                categoryValue = (string?)null,
                reason = "Reviewed manually.",
                assetVersion = finding["assetVersion"]!.GetValue<string>()
            });

        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Theory]
    // Changing the threshold changes whether the whole estate is compliant. A steward working the
    // queue must not be able to clear it by moving the bar.
    [InlineData("DataSteward", false)]
    [InlineData("StewardshipManager", true)]
    public async Task ChangingSettings_RequiresTheConfigureTier(string role, bool allowed)
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var token = await CreateUserTokenAsync(client, adminToken, role);

        var res = await AuthPut(client, token, "/api/governance/settings", DefaultSettings(targetScore: 10));
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, res.StatusCode);

        // Non-vacuous: a rejected request must also leave the stored threshold alone, not merely
        // return 403 after writing.
        var settings = await GetJsonAsync(client, adminToken, "/api/governance/settings");
        Assert.Equal(allowed ? 10 : 80, settings["targetScore"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("DataSteward", false)]
    [InlineData("StewardshipManager", true)]
    public async Task RunningAScan_RequiresTheConfigureTier(string role, bool allowed)
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var token = await CreateUserTokenAsync(client, adminToken, role);

        // A scan rewrites the queue every steward is working from.
        var res = await AuthPost(client, token, "/api/governance/scan", new { });
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Theory]
    [InlineData("DataSteward", false)]
    [InlineData("StewardshipManager", true)]
    public async Task ManagingGlossaryTerms_RequiresTheConfigureTier(string role, bool allowed)
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var token = await CreateUserTokenAsync(client, adminToken, role);

        var res = await AuthPost(client, token, "/api/governance/glossary",
            new
            {
                term = "revenue",
                dataType = "DECIMAL(18,2)",
                aliases = "rev, gross_sales",
                description = "Sales intake before deductions.",
                formula = "SUM(sales_amount)",
                steward = (string?)null,
                disabled = false
            });
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, res.StatusCode);

        var terms = await GetArrayAsync(client, adminToken, "/api/governance/glossary");
        Assert.Equal(allowed ? 1 : 0, terms.Count);
    }

    // ── Validation and audit ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SuppressionWithoutAReasonOrVersion_IsRejected()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);
        await SeedLineageAsync(factory, "sales.returns", tags: new() { ["owner"] = "chuck" });
        await PostJsonAsync(client, token, "/api/governance/scan", new { });
        var finding = FirstFinding(await GetJsonAsync(client, token, "/api/governance/dashboard"), "sales.returns");
        var id = finding["id"]!.GetValue<int>();
        var version = finding["assetVersion"]!.GetValue<string>();

        // No reason: the decision could never be reviewed.
        var noReason = await AuthPost(client, token, $"/api/governance/findings/{id}/decide",
            new { decision = "ignore", categoryValue = (string?)null, reason = "  ", assetVersion = version });
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);

        // No version: the suppression could never be revisited, making it permanent by accident.
        var noVersion = await AuthPost(client, token, $"/api/governance/findings/{id}/decide",
            new { decision = "accept-risk", categoryValue = (string?)null, reason = "Fine for now.", assetVersion = "" });
        Assert.Equal(HttpStatusCode.BadRequest, noVersion.StatusCode);

        Assert.Equal("open", (await FindingByIdAsync(client, token, id))["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task EveryGovernanceMutation_WritesAnAuditRow()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);
        await SeedLineageAsync(factory, "audit.subject", tags: new() { ["owner"] = "chuck" });

        await PostJsonAsync(client, token, "/api/governance/scan", new { });
        var finding = FirstFinding(await GetJsonAsync(client, token, "/api/governance/dashboard"), "audit.subject");
        var assetKey = finding["assetKey"]!.GetValue<string>();
        var version = finding["assetVersion"]!.GetValue<string>();

        await PostJsonAsync(client, token, $"/api/governance/findings/{finding["id"]!.GetValue<int>()}/decide",
            new { decision = "accept-risk", categoryValue = (string?)null, reason = "Known gap.", assetVersion = version });
        await PostJsonAsync(client, token, "/api/governance/assets/review",
            new { assetKey, assetVersion = version, note = "Checked." });
        await PostJsonAsync(client, token, "/api/governance/assets/badges",
            new { assetKey, badge = "Reviewed", assetVersion = version, reason = (string?)null });
        await AuthPut(client, token, "/api/governance/settings", DefaultSettings(targetScore: 70));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var actions = await db.AuditLogs.AsNoTracking()
            .Where(a => a.Action.StartsWith("GOVERNANCE_"))
            .Select(a => a.Action)
            .ToListAsync();

        // A governance surface whose own changes are unauditable cannot be evidence of anything.
        Assert.Contains("GOVERNANCE_SCAN", actions);
        Assert.Contains("GOVERNANCE_ACCEPT_RISK", actions);
        Assert.Contains("GOVERNANCE_REVIEW_ASSET", actions);
        Assert.Contains("GOVERNANCE_ASSIGN_BADGE", actions);
        Assert.Contains("GOVERNANCE_UPDATE_SETTINGS", actions);

        // The settings row records both sides: "who lowered the threshold" is unanswerable from the
        // new value alone.
        var settingsAudit = await db.AuditLogs.AsNoTracking()
            .FirstAsync(a => a.Action == "GOVERNANCE_UPDATE_SETTINGS");
        Assert.Contains("before[target=80", settingsAudit.Detail);
        Assert.Contains("after[target=70", settingsAudit.Detail);
    }

    [Fact]
    public async Task DisablingACategory_KeepsItReadableForDecisionsThatCiteIt()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);

        await PostJsonAsync(client, token, "/api/governance/categories",
            new { value = "noise", label = "Low Priority", color = "noise", expiryDays = 90, disabled = false });
        Assert.Equal(HttpStatusCode.NoContent,
            (await AuthDelete(client, token, "/api/governance/categories/noise")).StatusCode);

        // Still listed, marked disabled. Hard-deleting would leave historical suppressions citing a
        // reason nobody can look up.
        var categories = await GetArrayAsync(client, token, "/api/governance/categories");
        var category = Assert.Single(categories)!.AsObject();
        Assert.Equal("noise", category["value"]!.GetValue<string>());
        Assert.True(category["disabled"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ExpiringCategory_ReopensTheSuppressionWhenItsWindowPasses()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);
        await SeedLineageAsync(factory, "temp.scratch", tags: new() { ["owner"] = "chuck" });
        await PostJsonAsync(client, token, "/api/governance/scan", new { });
        await PostJsonAsync(client, token, "/api/governance/categories",
            new { value = "temporary", label = "Temporary", color = "noise", expiryDays = 30, disabled = false });

        var finding = FirstFinding(await GetJsonAsync(client, token, "/api/governance/dashboard"), "temp.scratch");
        var id = finding["id"]!.GetValue<int>();
        await PostJsonAsync(client, token, $"/api/governance/findings/{id}/decide",
            new
            {
                decision = "ignore",
                categoryValue = "temporary",
                reason = "Scratch table, removed next sprint.",
                assetVersion = finding["assetVersion"]!.GetValue<string>()
            });

        // Wind the clock past the window rather than waiting 30 days.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var stored = await db.StewardshipFindings.FirstAsync(f => f.Id == id);
            Assert.NotNull(stored.SuppressedUntilUtc);
            stored.SuppressedUntilUtc = DateTime.UtcNow.AddDays(-1);
            await db.SaveChangesAsync();
        }

        await PostJsonAsync(client, token, "/api/governance/scan", new { });
        // "Removed next sprint" was a promise with a date on it. When the date passes the finding
        // comes back rather than the promise standing forever.
        Assert.Equal("reopened", (await FindingByIdAsync(client, token, id))["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task StewardScope_FiltersTheQueueWithoutHidingTheEstate()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);
        await SeedLineageAsync(factory, "mine.table", tags: new() { ["steward"] = "admin" });
        await SeedLineageAsync(factory, "theirs.table", tags: new() { ["steward"] = "dana" });

        var mine = await GetJsonAsync(client, token, "/api/governance/dashboard?scope=mine");
        var all = await GetJsonAsync(client, token, "/api/governance/dashboard?scope=all");

        Assert.Contains(all["assets"]!.AsArray(), a => a!["assetKey"]!.GetValue<string>() == "theirs.table");
        Assert.DoesNotContain(mine["assets"]!.AsArray(), a => a!["assetKey"]!.GetValue<string>() == "theirs.table");
        // 'mine' is a filter the caller chooses, not a boundary imposed on them: the same identity
        // can still ask for the whole estate.
        Assert.Contains(all["assets"]!.AsArray(), a => a!["assetKey"]!.GetValue<string>() == "mine.table");
    }

    // ── fixtures ────────────────────────────────────────────────────────────────────────────────

    private static Dictionary<string, string> AllRequiredTags() =>
        ETL_SQL.Common.StewardshipTagCatalog.RequiredStewardshipTags
            .ToDictionary(tag => tag, _ => "filled", StringComparer.OrdinalIgnoreCase);

    private static async Task SeedLineageAsync(
        PortalWebFactory factory,
        string targetTable,
        Dictionary<string, string> tags,
        DateTime? runAt = null,
        string scriptPath = "loads/seed.etlsql")
    {
        var catalog = factory.Services.GetRequiredService<ILineageCatalogStore>();
        var entry = new LineageEntry(targetTable, "SELECT");
        foreach (var (key, value) in tags) entry.Metadata[key] = value;
        await catalog.SaveLineageAsync([entry], $"job-{targetTable}", scriptPath, runAt ?? DateTime.UtcNow);
    }

    private static object DefaultSettings(int targetScore) => new
    {
        targetScore,
        enableMetadataCheck = true,
        enableProtectedDataCheck = true,
        enableGlossaryCheck = false,
        enableStalenessCheck = true,
        deductMetadata = 5,
        deductProtectedData = 10,
        deductGlossary = 5,
        deductStaleness = 15,
        staleAfterDays = 30,
        policyLevel = "scored"
    };

    private static JsonObject SingleAsset(JsonObject dashboard, string assetKey) =>
        Assert.Single(dashboard["assets"]!.AsArray(),
            a => a!["assetKey"]!.GetValue<string>() == assetKey)!.AsObject();

    private static JsonObject FirstFinding(JsonObject dashboard, string assetKey)
    {
        var asset = SingleAsset(dashboard, assetKey);
        var finding = asset["findings"]!.AsArray().First()!.AsObject();
        // The asset's version is what a decision must be scoped to, and the findings array does not
        // repeat it when null — carry the asset's through.
        finding["assetVersion"] = JsonValue.Create(asset["assetVersion"]!.GetValue<string>());
        return finding;
    }

    private static async Task<JsonObject> FindingByIdAsync(HttpClient client, string token, int id)
    {
        var findings = await GetArrayAsync(client, token, "/api/governance/findings?limit=1000");
        return Assert.Single(findings, f => f!["id"]!.GetValue<int>() == id)!.AsObject();
    }

    // ── http helpers ────────────────────────────────────────────────────────────────────────────

    private static async Task<JsonObject> GetJsonAsync(HttpClient client, string token, string url)
    {
        var res = await AuthGet(client, token, url);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<JsonObject>(Json))!;
    }

    private static async Task<JsonArray> GetArrayAsync(HttpClient client, string token, string url)
    {
        var res = await AuthGet(client, token, url);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<JsonArray>(Json))!;
    }

    private static async Task<JsonObject> PostJsonAsync(HttpClient client, string token, string url, object body)
    {
        var res = await AuthPost(client, token, url, body);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<JsonObject>(Json))!;
    }

    private static Task<HttpResponseMessage> AuthGet(HttpClient client, string token, string url) =>
        Send(client, token, new HttpRequestMessage(HttpMethod.Get, url));

    private static Task<HttpResponseMessage> AuthDelete(HttpClient client, string token, string url) =>
        Send(client, token, new HttpRequestMessage(HttpMethod.Delete, url));

    private static Task<HttpResponseMessage> AuthPost(HttpClient client, string token, string url, object body) =>
        Send(client, token, new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) });

    private static Task<HttpResponseMessage> AuthPut(HttpClient client, string token, string url, object body) =>
        Send(client, token, new HttpRequestMessage(HttpMethod.Put, url) { Content = JsonContent.Create(body) });

    private static Task<HttpResponseMessage> Send(HttpClient client, string token, HttpRequestMessage req)
    {
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(req);
    }

    private static async Task<string> CreateUserTokenAsync(HttpClient client, string adminToken, string role)
    {
        var username = $"gov_{role.ToLowerInvariant()}_{Guid.NewGuid():N}"[..24];
        const string initial = "Gov@Tests99!";
        const string password = "Gov@Tests99b!";

        var created = await AuthPost(client, adminToken, "/api/admin/users",
            new { username, password = initial, role, email = $"{username}@example.com" });
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());

        var first = await LoginAsync(client, username, initial);
        var change = await AuthPost(client, first, "/api/auth/change-password",
            new { currentPassword = initial, newPassword = password });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        return await LoginAsync(client, username, password);
    }

    private static async Task<string> GetAdminTokenAsync(HttpClient client)
    {
        var first = await LoginAsync(client, "admin", "Admin@12345!");
        var change = await AuthPost(client, first, "/api/auth/change-password",
            new { currentPassword = "Admin@12345!", newPassword = "Admin@Tests99!" });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        return await LoginAsync(client, "admin", "Admin@Tests99!");
    }

    private static async Task<string> LoginAsync(HttpClient client, string username, string password)
    {
        var res = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<JsonObject>(Json))!["token"]!.GetValue<string>();
    }
}
