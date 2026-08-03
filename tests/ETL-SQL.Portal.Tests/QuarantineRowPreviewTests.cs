using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Core.Data;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Reading quarantined rows in the Portal means the web tier opens a production connection and
/// returns raw source data. Every test here exists to pin one of the conditions that has to hold
/// before that happens, because each of them is individually sufficient to make it unsafe:
///
/// <list type="bullet">
/// <item>the capture must have <b>proved</b> at write time that its target sits behind a governed
/// shared connection — inferring it later would mean opening a connection on a guess;</item>
/// <item>the operator must have turned the feature on;</item>
/// <item>the caller must hold a grant on that connection in their own right, not merely the
/// data-quality steward capability that gets them to the page;</item>
/// <item>the connection opened must be the <b>manifest's</b>, never one named in the request — and
/// where the two disagree, neither is used.</item>
/// </list>
///
/// The queue and the row endpoint are asserted together throughout: a list that offers a row editor
/// the row endpoint then refuses is its own defect.
/// </summary>
[Trait("Category", "Portal")]
[Trait("Category", "Smoke.Security")]
public sealed class QuarantineRowPreviewTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed class PreviewFactory(bool previewEnabled) : PortalWebFactory
    {
        protected override void CustomizeConfiguration(Dictionary<string, string?> settings)
        {
            settings["Governance:ConnectionCatalog:Provider"] = "Portal";
            settings["Portal:DataQuality:AllowConnectionPreview"] = previewEnabled ? "true" : "false";
        }

        protected override void CustomizePortalConfig(PortalConfig config)
            => config.DataQuality.AllowConnectionPreview = previewEnabled;
    }

    [Fact]
    public async Task CatalogBackedTarget_WithGrantAndSwitchOn_ReturnsQuarantinedRows()
    {
        using var factory = new PreviewFactory(previewEnabled: true);
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);

        var dataPath = SeedQuarantineCsv(factory, rows: 3);
        await SeedCatalogEntryAsync(factory, "dq_src", "FLATFILE", dataPath);
        var job = await SeedManifestAsync(
            factory, "dq_src.q_rows", alias: "dq_src", connectorType: "FLATFILE", catalogBacked: true);

        // The queue is the entry point: if it says view-only, no steward ever reaches the editor.
        var item = await SingleQueueItemAsync(client, token, job);
        Assert.True(item["rowsReadable"]!.GetValue<bool>());
        Assert.Null(item["rowsUnavailableReason"]?.GetValue<string?>());

        var res = await AuthGet(client, token, $"/api/data-quality/quarantine/rows?quarantineTarget=dq_src.q_rows&jobName={job}");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = (await res.Content.ReadFromJsonAsync<JsonObject>(Json))!;
        Assert.Equal("dq_src.q_rows", body["quarantineTarget"]!.GetValue<string>());
        var rows = body["rows"]!.AsArray();
        Assert.Equal(3, rows.Count);
        Assert.Contains("__dq_status", body["columns"]!.AsArray().Select(c => c!.GetValue<string>()));

        // Reading raw source rows is a data-access event. Without this the only trace of who read
        // production data through the Portal is a web log.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var audit = await db.AuditLogs.AsNoTracking()
            .Where(a => a.Action == "READ_QUARANTINE_ROWS")
            .OrderByDescending(a => a.Id)
            .FirstOrDefaultAsync();
        Assert.NotNull(audit);
        Assert.Equal("dq_src.q_rows", audit!.ResourceId);
        Assert.Contains("connection=dq_src", audit.Detail);
    }

    [Fact]
    public async Task SwitchOff_LeavesAnOtherwiseEligibleTargetViewOnly()
    {
        using var factory = new PreviewFactory(previewEnabled: false);
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);

        var dataPath = SeedQuarantineCsv(factory, rows: 3);
        await SeedCatalogEntryAsync(factory, "dq_src", "FLATFILE", dataPath);
        var job = await SeedManifestAsync(
            factory, "dq_src.q_rows", alias: "dq_src", connectorType: "FLATFILE", catalogBacked: true);

        var item = await SingleQueueItemAsync(client, token, job);
        Assert.False(item["rowsReadable"]!.GetValue<bool>());
        // Named, so an operator can tell "turned off" from "not eligible" and act on it.
        Assert.Contains("AllowConnectionPreview", item["rowsUnavailableReason"]!.GetValue<string>());

        var res = await AuthGet(client, token, $"/api/data-quality/quarantine/rows?quarantineTarget=dq_src.q_rows&jobName={job}");
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Contains("AllowConnectionPreview", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CatalogMiss_And_DisabledEntry_AreBothViewOnly_WithoutDisclosingWhich()
    {
        using var factory = new PreviewFactory(previewEnabled: true);
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);
        var dataPath = SeedQuarantineCsv(factory, rows: 2);

        // Never in the catalog.
        var missing = await SeedManifestAsync(
            factory, "absent_src.q_rows", alias: "absent_src", connectorType: "FLATFILE", catalogBacked: true);
        // In the catalog, but administratively disabled.
        await SeedCatalogEntryAsync(factory, "off_src", "FLATFILE", dataPath, disabled: true);
        var disabled = await SeedManifestAsync(
            factory, "off_src.q_rows", alias: "off_src", connectorType: "FLATFILE", catalogBacked: true);

        var missingReason = (await SingleQueueItemAsync(client, token, missing))["rowsUnavailableReason"]!.GetValue<string>();
        var disabledReason = (await SingleQueueItemAsync(client, token, disabled))["rowsUnavailableReason"]!.GetValue<string>();

        // Same wording, differing only in the alias the caller already supplied. The catalog does
        // not disclose the existence of connections a caller cannot use, and a reason that said
        // "disabled" versus "no such alias" would leak precisely that.
        Assert.Equal(
            missingReason.Replace("absent_src", "ALIAS", StringComparison.Ordinal),
            disabledReason.Replace("off_src", "ALIAS", StringComparison.Ordinal));

        foreach (var (job, target) in new[] { (missing, "absent_src.q_rows"), (disabled, "off_src.q_rows") })
        {
            var res = await AuthGet(client, token, $"/api/data-quality/quarantine/rows?quarantineTarget={target}&jobName={job}");
            Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        }
    }

    [Fact]
    public async Task StewardWithoutTheConnectionGrant_IsRefused_EvenThoughTheyReachThePage()
    {
        using var factory = new PreviewFactory(previewEnabled: true);
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);

        var dataPath = SeedQuarantineCsv(factory, rows: 2);
        await SeedCatalogEntryAsync(factory, "dq_src", "FLATFILE", dataPath);
        var job = await SeedManifestAsync(
            factory, "dq_src.q_rows", alias: "dq_src", connectorType: "FLATFILE", catalogBacked: true);

        // Grant the connection to a group nobody outside admin is in. Ungranted entries are open to
        // all, so the grant has to exist before "no grant" means anything.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var groupId = await CreateGroupAsync(client, adminToken, $"conn_owners_{suffix}");
        await GrantConnectionAsync(factory, "dq_src", groupId);

        // A non-admin steward: same DataQualityStewardAccess that renders the queue, no connection grant.
        var stewardToken = await CreateStewardTokenAsync(client, adminToken, suffix);

        var item = await SingleQueueItemAsync(client, stewardToken, job);
        Assert.False(item["rowsReadable"]!.GetValue<bool>());
        Assert.Contains("no grant", item["rowsUnavailableReason"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase);

        var refused = await AuthGet(client, stewardToken, $"/api/data-quality/quarantine/rows?quarantineTarget=dq_src.q_rows&jobName={job}");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);

        // Not vacuous: the identical request from an identity that does hold the grant succeeds, so
        // the refusal is the grant check and not the fixture failing to work at all.
        var allowed = await AuthGet(client, adminToken, $"/api/data-quality/quarantine/rows?quarantineTarget=dq_src.q_rows&jobName={job}");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task LegacyManifestWithoutProvenance_StaysViewOnly_EvenWithTheConnectionCataloged()
    {
        using var factory = new PreviewFactory(previewEnabled: true);
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);

        var dataPath = SeedQuarantineCsv(factory, rows: 2);
        await SeedCatalogEntryAsync(factory, "dq_src", "FLATFILE", dataPath);

        // A manifest written by an engine that predates provenance: the target string is identical
        // to the readable case, and the connection is right there in the catalog. Absent provenance
        // still means unknown — matching the alias by name would be inferring what capture never
        // proved, and the shape of the target is not evidence of where it lives.
        var job = $"legacy_{Guid.NewGuid():N}";
        var store = factory.Services.GetRequiredService<IJobHistoryStore>();
        await store.SetJobStateAsync(
            job,
            "dq:quarantine-manifest:dq_src.q_rows",
            """
            {"JobName":"legacy","ScriptPath":"loads/x.etlsql","SectionLabel":"sec","SourceTable":"#src",
             "QuarantineTarget":"dq_src.q_rows","IsReplayable":true,"NonReplayableReason":null,
             "InputColumns":["Id"],"InputSchemaFingerprint":"schema-v","UpdatedAtUtc":"2026-01-01T00:00:00+00:00"}
            """);

        var item = await SingleQueueItemAsync(client, token, job);
        Assert.False(item["rowsReadable"]!.GetValue<bool>());
        Assert.Contains("no record of a governed", item["rowsUnavailableReason"]!.GetValue<string>());

        var res = await AuthGet(client, token, $"/api/data-quality/quarantine/rows?quarantineTarget=dq_src.q_rows&jobName={job}");
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        // The steward is still left somewhere to go.
        Assert.Contains("SELECT * FROM dq_src.q_rows", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ProvenanceMissingItsConnectorType_IsTreatedAsUnknown_NotAsCatalogBacked()
    {
        using var factory = new PreviewFactory(previewEnabled: true);
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);

        var dataPath = SeedQuarantineCsv(factory, rows: 2);
        await SeedCatalogEntryAsync(factory, "dq_src", "FLATFILE", dataPath);

        // Flagged catalog-backed with an alias but no connector type — the shape a run produces when
        // the script declared no type on a SHARED reference. Reopening means emitting a typed
        // CREATE CONNECTION, so half a record is no record: it must not fall through to a read.
        var job = await SeedManifestAsync(
            factory, "dq_src.q_rows", alias: "dq_src", connectorType: null, catalogBacked: true);

        var item = await SingleQueueItemAsync(client, token, job);
        Assert.False(item["rowsReadable"]!.GetValue<bool>());

        var res = await AuthGet(client, token, $"/api/data-quality/quarantine/rows?quarantineTarget=dq_src.q_rows&jobName={job}");
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task ProvenanceThatIsNotAPlainIdentifier_IsRefused_NotInterpolated()
    {
        using var factory = new PreviewFactory(previewEnabled: true);
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);

        var dataPath = SeedQuarantineCsv(factory, rows: 2);
        await SeedCatalogEntryAsync(factory, "dq_src", "FLATFILE", dataPath);

        // The connector type is interpolated into a CREATE CONNECTION statement. Job state is
        // engine-written, but it is still a stored blob, so the value is checked as an identifier
        // rather than trusted for where it came from.
        var job = await SeedManifestAsync(
            factory, "dq_src.q_rows", alias: "dq_src",
            connectorType: "FLATFILE('x'); DROP TABLE users; --", catalogBacked: true);

        var item = await SingleQueueItemAsync(client, token, job);
        Assert.False(item["rowsReadable"]!.GetValue<bool>());

        var res = await AuthGet(client, token, $"/api/data-quality/quarantine/rows?quarantineTarget=dq_src.q_rows&jobName={job}");
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        Assert.False(await db.AuditLogs.AsNoTracking().AnyAsync(a => a.Action == "READ_QUARANTINE_ROWS"));
    }

    [Fact]
    public async Task ManifestWhoseAliasDisagreesWithItsTarget_IsRefused_NotReconciled()
    {
        using var factory = new PreviewFactory(previewEnabled: true);
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);

        var dataPath = SeedQuarantineCsv(factory, rows: 2);
        await SeedCatalogEntryAsync(factory, "real_src", "FLATFILE", dataPath);

        // Recorded provenance says 'real_src'; the target says 'decoy'. Two readings are available
        // and both are wrong: opening real_src would read a connection the target does not name, and
        // trusting the target's prefix would let a string choose the connection. A capture that
        // contradicts itself is not evidence of anything, so it is refused.
        var job = await SeedManifestAsync(
            factory, "decoy.q_rows", alias: "real_src", connectorType: "FLATFILE", catalogBacked: true);

        var item = await SingleQueueItemAsync(client, token, job);
        Assert.False(item["rowsReadable"]!.GetValue<bool>());

        var res = await AuthGet(client, token, $"/api/data-quality/quarantine/rows?quarantineTarget=decoy.q_rows&jobName={job}");
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);

        // Nothing was opened and nothing was read, so nothing is audited as read.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        Assert.False(await db.AuditLogs.AsNoTracking().AnyAsync(a => a.Action == "READ_QUARANTINE_ROWS"));
    }

    [Fact]
    public async Task RowLimitIsCappedAndReported()
    {
        using var factory = new PreviewFactory(previewEnabled: true);
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);

        var dataPath = SeedQuarantineCsv(factory, rows: 40);
        await SeedCatalogEntryAsync(factory, "dq_src", "FLATFILE", dataPath);
        var job = await SeedManifestAsync(
            factory, "dq_src.q_rows", alias: "dq_src", connectorType: "FLATFILE", catalogBacked: true);

        // 5000 is above the endpoint's ceiling; the clamp is what stops a preview from becoming an
        // unbounded extract of a production table through a web request.
        var res = await AuthGet(client, token,
            $"/api/data-quality/quarantine/rows?quarantineTarget=dq_src.q_rows&jobName={job}&limit=5000");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = (await res.Content.ReadFromJsonAsync<JsonObject>(Json))!;
        Assert.True(body["rows"]!.AsArray().Count <= 200);

        var capped = await AuthGet(client, token,
            $"/api/data-quality/quarantine/rows?quarantineTarget=dq_src.q_rows&jobName={job}&limit=5");
        var cappedBody = (await capped.Content.ReadFromJsonAsync<JsonObject>(Json))!;
        Assert.Equal(5, cappedBody["rows"]!.AsArray().Count);
        // Silently truncating would let a steward conclude only five rows were quarantined.
        Assert.True(cappedBody["capped"]!.GetValue<bool>());
    }

    [Fact]
    public async Task FailurePathRedactsSecretsFromTheEngineDiagnostic()
    {
        using var factory = new PreviewFactory(previewEnabled: true);
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);

        // The catalog entry has been retyped since the capture — real drift, and something the
        // engine rejects rather than papers over. It is used here because it is the failure that
        // reliably reaches the 502 offline, and the entry it fails on carries a credential.
        var dataPath = SeedQuarantineCsv(factory, rows: 1);
        await SeedCatalogEntryAsync(
            factory, "dq_src", "MSSQL", dataPath, password: "hunter2-should-never-surface");
        var job = await SeedManifestAsync(
            factory, "dq_src.q_rows", alias: "dq_src", connectorType: "FLATFILE", catalogBacked: true);

        var res = await AuthGet(client, token,
            $"/api/data-quality/quarantine/rows?quarantineTarget=dq_src.q_rows&jobName={job}");

        Assert.Equal(HttpStatusCode.BadGateway, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        // Non-vacuous: the engine's own diagnostic really is what came back, so the credential's
        // absence below is the redactor working and not an empty body.
        Assert.Contains("MSSQL", body);
        Assert.DoesNotContain("hunter2", body);
    }

    // ── fixtures ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes the captured rows and returns the path a catalog entry points at. FLATFILE is used
    /// because it is the one connector that opens for real in-process, so these tests exercise the
    /// actual governed open rather than a stub that would pass whatever the wiring did.
    /// </summary>
    private static string SeedQuarantineCsv(PortalWebFactory factory, int rows)
    {
        var dir = Path.Combine(factory.TempDir, $"dq_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "q_rows.csv");
        var lines = new List<string> { "Id,Email,__dq_status,__dq_rule" };
        for (var i = 1; i <= rows; i++)
            lines.Add($"{i},user{i}@example.com,quarantined,email_format");
        File.WriteAllLines(path, lines);
        return path.Replace('\\', '/');
    }

    private static async Task SeedCatalogEntryAsync(
        PortalWebFactory factory,
        string alias,
        string connectorType,
        string target,
        bool disabled = false,
        string? password = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        db.PortalSharedConnections.Add(new PortalSharedConnection
        {
            Alias = alias,
            ConnectorType = connectorType,
            Target = target,
            OptionsJson = password is null
                ? """{"HEADER":"ON"}"""
                : $$"""{"HEADER":"ON","PASSWORD":"{{password}}"}""",
            Disabled = disabled
        });
        await db.SaveChangesAsync();
    }

    private static async Task<string> SeedManifestAsync(
        PortalWebFactory factory,
        string target,
        string? alias,
        string? connectorType,
        bool? catalogBacked)
    {
        var job = $"dq_{Guid.NewGuid():N}";
        var store = factory.Services.GetRequiredService<IJobHistoryStore>();
        await store.SetJobStateAsync(
            job,
            $"dq:quarantine-manifest:{target}",
            JsonSerializer.Serialize(new QuarantineReplayManifest(
                job, "loads/x.etlsql", "sec", "#src", target,
                true, null, ["Id"], "schema-v", DateTimeOffset.UtcNow,
                TargetConnectionAlias: alias,
                TargetConnectorType: connectorType,
                TargetIsCatalogBacked: catalogBacked)));
        return job;
    }

    private static async Task GrantConnectionAsync(PortalWebFactory factory, string alias, int groupId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var entry = await db.PortalSharedConnections.SingleAsync(c => c.Alias == alias);
        db.SharedConnectionAcls.Add(new SharedConnectionAcl
        {
            SharedConnectionId = entry.Id,
            GroupId = groupId,
            Permission = SharedConnectionPermission.Use
        });
        await db.SaveChangesAsync();
    }

    private static async Task<JsonObject> SingleQueueItemAsync(HttpClient client, string token, string job)
    {
        var res = await AuthGet(client, token, $"/api/data-quality/quarantine?jobName={job}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var items = (await res.Content.ReadFromJsonAsync<JsonArray>(Json))!;
        return Assert.Single(items)!.AsObject();
    }

    // ── auth helpers ────────────────────────────────────────────────────────────────────────────

    private static async Task<int> CreateGroupAsync(HttpClient client, string token, string name)
    {
        var res = await AuthPost(client, token, "/api/admin/groups", new { name });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<JsonObject>(Json))!["id"]!.GetValue<int>();
    }

    /// <summary>
    /// A real <c>DataSteward</c> — the role that satisfies <c>DataQualityStewardAccess</c> and gets
    /// someone to the quarantine queue in the first place. That is the point of the test using it:
    /// the queue must render for them while the rows must not.
    /// </summary>
    private static async Task<string> CreateStewardTokenAsync(HttpClient client, string adminToken, string suffix)
    {
        var username = $"steward_{suffix}";
        const string initial = "Steward@Tests99!";
        const string password = "Steward@Tests99b!";
        var created = await AuthPost(client, adminToken, "/api/admin/users",
            new { username, password = initial, role = "DataSteward", email = $"{username}@example.com" });
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());

        // New local users are created must-change-password, so the first token is not usable.
        var first = await client.PostAsJsonAsync("/api/auth/login", new { username, password = initial });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstToken = (await first.Content.ReadFromJsonAsync<JsonObject>(Json))!["token"]!.GetValue<string>();
        var change = await AuthPost(client, firstToken, "/api/auth/change-password",
            new { currentPassword = initial, newPassword = password });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);

        var login = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return (await login.Content.ReadFromJsonAsync<JsonObject>(Json))!["token"]!.GetValue<string>();
    }

    private static async Task<string> GetAdminTokenAsync(HttpClient client)
    {
        var initial = await client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "Admin@12345!" });
        Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
        var first = (await initial.Content.ReadFromJsonAsync<JsonObject>(Json))!["token"]!.GetValue<string>();

        var change = await AuthPost(client, first, "/api/auth/change-password",
            new { currentPassword = "Admin@12345!", newPassword = "Admin@Tests99!" });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);

        var login = await client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "Admin@Tests99!" });
        return (await login.Content.ReadFromJsonAsync<JsonObject>(Json))!["token"]!.GetValue<string>();
    }

    private static Task<HttpResponseMessage> AuthGet(HttpClient client, string token, string url)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(req);
    }

    private static Task<HttpResponseMessage> AuthPost(HttpClient client, string token, string url, object body)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(req);
    }
}
