using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ETL_SQL.Orchestrator.Channels;
using ETL_SQL.ReportHosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace ETL_SQL.ReportPortal.Tests;

/// <summary>
/// Verifies that the two execution paths available to report hosts produce
/// structurally identical manifests for the same .rptsql fixture:
///
///   Path A — DashboardService directly (the shared engine layer used by
///             ReportPlayer, Portal API, and VS Code preview).
///   Path B — Portal API: publish → execute job → snapshot.
///
/// If the two paths diverge — e.g. a Portal serialisation change omits a
/// visual, a column-mapping bug affects only the job path, or a Portal
/// parameter-passing regression appears — this test will catch it.
/// </summary>
[Trait("Category", "Portal")]
public class CrossHostConsistencyTests : IClassFixture<PortalWebFactory>
{
    private readonly HttpClient _client;
    private readonly PortalWebFactory _factory;
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    // A deterministic fixture with no external dependencies.
    // Produces: one TABLE visual (3 rows × 3 columns) + one CARD visual (1 row).
    private const string FixtureScript = """
        /* @owner: Test; @tags: cross-host-smoke; @category: Test */
        SET REPORT TITLE = 'Cross-Host Consistency';

        CREATE VISUAL ItemTable AS TABLE (
            SOURCE = (
                SELECT 1 AS Id, 'Alpha' AS Name, 100 AS Amount
                UNION ALL SELECT 2 AS Id, 'Beta'  AS Name, 200 AS Amount
                UNION ALL SELECT 3 AS Id, 'Gamma' AS Name, 300 AS Amount
            ),
            MAPPINGS (Id = Id, Name = Name, Amount = Amount)
        );

        CREATE VISUAL SummaryCard AS CARD (
            SOURCE = (SELECT 42 AS Total),
            MAPPINGS (VALUE = Total)
        );
        """;

    public CrossHostConsistencyTests(PortalWebFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    [Trait("Category", "Smoke.Portal")]
    public async Task DashboardServiceAndPortalAPI_ProduceSameManifestStructure()
    {
        var scriptPath = Path.Combine(_factory.TempDir, "scripts", "cross_host_fixture.rptsql");
        await File.WriteAllTextAsync(scriptPath, FixtureScript);

        // ── Path A: DashboardService (shared engine layer, no HTTP) ──────────
        await using var svc = new DashboardService(
            scriptPath,
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            TimeSpan.FromSeconds(30));

        var directManifest = await svc.GetManifestAsync();

        Assert.Null(directManifest.Error);
        Assert.Equal("Cross-Host Consistency", directManifest.Title);
        Assert.Equal(2, directManifest.Visuals.Count);

        // ── Path B: Portal API execute → snapshot ─────────────────────────────
        var token = await GetAdminTokenAsync();

        var folderRes = await AuthPost(token, "/api/folders", new { name = "Cross-Host", parentId = (int?)null });
        Assert.Equal(HttpStatusCode.Created, folderRes.StatusCode);
        var folderId = (await folderRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["id"]!.GetValue<int>();

        var publishRes = await AuthPost(token, "/api/reports", new
        {
            folderId,
            name = "Cross-Host Fixture",
            description = "Cross-host consistency smoke fixture",
            scriptPath
        });
        Assert.Equal(HttpStatusCode.Created, publishRes.StatusCode);
        var reportId = (await publishRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["id"]!.GetValue<int>();

        var executeRes = await AuthPost(token, $"/api/reports/{reportId}/execute",
            new { parameters = new Dictionary<string, string>() });
        Assert.Equal(HttpStatusCode.Accepted, executeRes.StatusCode);
        var jobId = (await executeRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["jobId"]!.GetValue<string>();

        var job = await WaitForJobAsync(token, jobId);
        var jobError = job["error"]?.GetValue<string>() ?? "(none)";
        Assert.True(job["status"]!.GetValue<string>() == "Completed",
            $"Portal job ended with status '{job["status"]}': {jobError}");

        var snapshotRes = await AuthGet(token, $"/api/reports/{reportId}/snapshot?includeManifest=true");
        Assert.Equal(HttpStatusCode.OK, snapshotRes.StatusCode);
        var snapshot = await snapshotRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        var portalManifest = snapshot!["manifest"]!.AsObject();

        // ── Compare: title ────────────────────────────────────────────────────
        Assert.Equal(directManifest.Title, portalManifest["title"]?.GetValue<string>());

        // ── Compare: visual count and names (order-independent) ───────────────
        var portalVisuals = portalManifest["visuals"]!.AsArray();
        Assert.Equal(directManifest.Visuals.Count, portalVisuals.Count);

        var directNames = directManifest.Visuals.Select(v => v.Name).OrderBy(n => n).ToList();
        var portalNames = portalVisuals.Select(v => v!["name"]!.GetValue<string>()).OrderBy(n => n).ToList();
        Assert.Equal(directNames, portalNames);

        // ── Compare: per-visual row counts and column names ───────────────────
        foreach (var directVisual in directManifest.Visuals)
        {
            var portalVisual = portalVisuals
                .Single(v => v!["name"]!.GetValue<string>() == directVisual.Name)!
                .AsObject();

            Assert.Equal(
                directVisual.Rows.Count,
                portalVisual["rows"]?.AsArray().Count ?? 0);

            if (directVisual.Columns.Count > 0)
            {
                var directCols = directVisual.Columns.OrderBy(c => c).ToList();
                var portalCols = (portalVisual["columns"]?.AsArray()
                        .Select(c => c!.GetValue<string>())
                    ?? Enumerable.Empty<string>())
                    .OrderBy(c => c)
                    .ToList();
                Assert.Equal(directCols, portalCols);
            }
        }
    }

    [Fact]
    [Trait("Category", "Smoke.Portal")]
    public async Task RemoteOrchestratorExecution_PersistsPortalSnapshotAndSupportsExport()
    {
        using var portalFactory = new PortalWebFactory();
        using var orchestratorFactory = new OrchestratorWebFactory();
        Assert.NotEqual(portalFactory.TempDir, orchestratorFactory.TempDir);
        using var orchestratorClient = orchestratorFactory.CreateClient();
        orchestratorClient.DefaultRequestHeaders.Add("X-Orchestrator-Key", "test-orch-key-12345");
        using var remotePortalFactory = portalFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IJobChannel>();
                services.AddSingleton<IJobChannel>(_ =>
                    new HttpJobChannelClient(orchestratorClient, NullLogger<HttpJobChannelClient>.Instance));
            });
        });
        using var client = remotePortalFactory.CreateClient();

        var scriptPath = Path.Combine(portalFactory.TempDir, "scripts", "remote_fixture.rptsql");
        await File.WriteAllTextAsync(scriptPath, FixtureScript);
        var token = await GetAdminTokenAsync(client);

        var folderRes = await AuthPost(client, token, "/api/folders", new { name = "Remote", parentId = (int?)null });
        Assert.Equal(HttpStatusCode.Created, folderRes.StatusCode);
        var folderId = (await folderRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["id"]!.GetValue<int>();

        var publishRes = await AuthPost(client, token, "/api/reports", new
        {
            folderId,
            name = "Remote Fixture",
            description = "Remote Orchestrator manifest transport fixture",
            scriptPath
        });
        Assert.Equal(HttpStatusCode.Created, publishRes.StatusCode);
        var reportId = (await publishRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["id"]!.GetValue<int>();

        var executeRes = await AuthPost(client, token, $"/api/reports/{reportId}/execute",
            new { parameters = new Dictionary<string, string>() });
        Assert.Equal(HttpStatusCode.Accepted, executeRes.StatusCode);
        var jobId = (await executeRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["jobId"]!.GetValue<string>();

        var job = await WaitForJobAsync(client, token, jobId);
        Assert.Equal("Completed", job["status"]?.GetValue<string>());

        var manifestRes = await AuthGet(client, token, $"/api/reports/{reportId}/snapshot/manifest");
        Assert.Equal(HttpStatusCode.OK, manifestRes.StatusCode);
        var manifest = await manifestRes.Content.ReadFromJsonAsync<JsonObject>(_json);
        Assert.Equal("Cross-Host Consistency", manifest?["title"]?.GetValue<string>());

        var sessions = remotePortalFactory.Services.GetRequiredService<ETL_SQL.ReportPortal.Services.SessionCache>();
        var sessionBeforeRefresh = sessions.GetOrCreate(reportId, 1, scriptPath);

        var refreshRes = await AuthPost(client, token, $"/api/reports/{reportId}/refresh", new { });
        Assert.Equal(HttpStatusCode.Accepted, refreshRes.StatusCode);
        var refreshJobId = (await refreshRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["jobId"]!.GetValue<string>();
        var refreshJob = await WaitForJobAsync(client, token, refreshJobId);
        Assert.Equal("Completed", refreshJob["status"]?.GetValue<string>());
        var sessionAfterRefresh = sessions.GetOrCreate(reportId, 1, scriptPath);
        Assert.NotSame(sessionBeforeRefresh, sessionAfterRefresh);

        var csvRes = await AuthGet(client, token, $"/api/reports/{reportId}/export/csv");
        Assert.Equal(HttpStatusCode.OK, csvRes.StatusCode);
        Assert.Contains("Alpha", await csvRes.Content.ReadAsStringAsync());

        var xlsxRes = await AuthGet(client, token, $"/api/reports/{reportId}/export/xlsx");
        Assert.Equal(HttpStatusCode.OK, xlsxRes.StatusCode);
        Assert.NotEmpty(await xlsxRes.Content.ReadAsByteArrayAsync());

        var pdfRes = await AuthGet(client, token, $"/api/reports/{reportId}/export/pdf");
        Assert.Equal(HttpStatusCode.OK, pdfRes.StatusCode);
        Assert.NotEmpty(await pdfRes.Content.ReadAsByteArrayAsync());
        var snapshotRoot = Path.Combine(portalFactory.TempDir, "snapshots");
        Assert.NotEmpty(Directory.GetFiles(snapshotRoot, "*.etlsnap"));
        Assert.Empty(Directory.GetFiles(snapshotRoot, "*.snapshot.json"));
    }

    [Fact]
    [Trait("Category", "Smoke.Portal")]
    public async Task OrchestratorReportManifest_IsReturnedOnlyWithValidApiKey()
    {
        using var orchestratorFactory = new OrchestratorWebFactory();
        using var authenticatedClient = orchestratorFactory.CreateClient();
        authenticatedClient.DefaultRequestHeaders.Add("X-Orchestrator-Key", "test-orch-key-12345");

        var submitRes = await authenticatedClient.PostAsJsonAsync("/jobs", new JobSubmitRequest
        {
            ScriptText = FixtureScript,
            SessionId = "manifest-security",
            Label = "Manifest security fixture",
            Metadata = new Dictionary<string, string>
            {
                ["IsReport"] = "true",
                ["ReportId"] = "security"
            }
        });
        Assert.Equal(HttpStatusCode.Accepted, submitRes.StatusCode);
        var jobId = (await submitRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["jobId"]!.GetValue<string>();

        ETL_SQL.Orchestrator.Channels.JobStatusResponse authenticatedStatus = null!;
        for (var i = 0; i < 300; i++)
        {
            authenticatedStatus = (await authenticatedClient.GetFromJsonAsync<ETL_SQL.Orchestrator.Channels.JobStatusResponse>(
                $"/jobs/{jobId}", _json))!;
            if (authenticatedStatus.Status is JobRunStatus.Completed or JobRunStatus.Failed or JobRunStatus.Cancelled)
                break;
            await Task.Delay(100);
        }
        Assert.Equal(JobRunStatus.Completed, authenticatedStatus.Status);
        Assert.False(string.IsNullOrWhiteSpace(authenticatedStatus.ReportManifestJson));

        // The ad-hoc job status route now requires a valid API key outright: the manifest — and all
        // job status — is withheld behind 401, not merely redacted from a 200 response.
        using var unauthenticatedClient = orchestratorFactory.CreateClient();
        var unauthenticatedResponse = await unauthenticatedClient.GetAsync($"/jobs/{jobId}");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticatedResponse.StatusCode);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<string> GetAdminTokenAsync()
        => await GetAdminTokenAsync(_client);

    private static async Task<string> GetAdminTokenAsync(HttpClient client)
    {
        var loginRes = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = "Admin@12345!" });
        loginRes.EnsureSuccessStatusCode();
        var token = (await loginRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["token"]!.GetValue<string>();

        using var cpReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password");
        cpReq.Headers.Authorization = new("Bearer", token);
        cpReq.Content = JsonContent.Create(new { currentPassword = "Admin@12345!", newPassword = "Admin@Tests99!" });
        (await client.SendAsync(cpReq)).EnsureSuccessStatusCode();

        var reloginRes = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = "Admin@Tests99!" });
        reloginRes.EnsureSuccessStatusCode();
        return (await reloginRes.Content.ReadFromJsonAsync<JsonObject>(_json))!["token"]!.GetValue<string>();
    }

    private Task<HttpResponseMessage> AuthGet(string token, string url)
        => AuthGet(_client, token, url);

    private static Task<HttpResponseMessage> AuthGet(HttpClient client, string token, string url)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new("Bearer", token);
        return client.SendAsync(req);
    }

    private Task<HttpResponseMessage> AuthPost(string token, string url, object body)
        => AuthPost(_client, token, url, body);

    private static Task<HttpResponseMessage> AuthPost(HttpClient client, string token, string url, object body)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new("Bearer", token);
        req.Content = JsonContent.Create(body);
        return client.SendAsync(req);
    }

    private async Task<JsonObject> WaitForJobAsync(string token, string jobId)
        => await WaitForJobAsync(_client, token, jobId);

    private static async Task<JsonObject> WaitForJobAsync(HttpClient client, string token, string jobId)
    {
        for (var i = 0; i < 300; i++)
        {
            var res = await AuthGet(client, token, $"/api/jobs/{jobId}");
            var job = await res.Content.ReadFromJsonAsync<JsonObject>(_json);
            if (job!["status"]!.GetValue<string>() is "Completed" or "Failed" or "Cancelled")
                return job;
            await Task.Delay(200);
        }
        throw new TimeoutException($"Job {jobId} did not complete within the expected time.");
    }
}
