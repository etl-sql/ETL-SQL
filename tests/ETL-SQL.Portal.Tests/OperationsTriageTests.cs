using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Core.Data;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// The triage board answers the question an operator with 200 scheduled jobs actually opens the
/// Portal to ask: what broke, is it one thing or many, and what should have run and did not. These
/// cover the three classifications and the boundaries between them, because a board that quietly
/// mis-files a failure is worse than no board.
/// </summary>
[Trait("Category", "Portal")]
public sealed class OperationsTriageTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task OneOutageAcrossManyJobsIsOneIncidentNamingEveryAffectedJob()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);
        var suffix = Suffix();

        // The 03:00 shape: one source database down, every dependent job failing the same way.
        foreach (var job in new[] { $"sales_{suffix}", $"finance_{suffix}", $"hr_{suffix}" })
        {
            await RecordRunAsync(factory, job, "FAILED",
                $"Login failed for user 'etl_svc'. Connection id {Guid.NewGuid()} at {DateTime.UtcNow:O}");
        }

        var board = await TriageAsync(client, token);

        Assert.Equal(3, board["failureCount"]!.GetValue<int>());
        Assert.Equal(1, board["incidentCount"]!.GetValue<int>());

        var incident = board["incidents"]!.AsArray().Single()!;
        Assert.Equal(3, incident["failureCount"]!.GetValue<int>());
        var affected = incident["jobNames"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();
        Assert.Equal(3, affected.Count);
        Assert.Contains($"sales_{suffix}", affected);
        Assert.Contains($"finance_{suffix}", affected);
        Assert.Contains($"hr_{suffix}", affected);

        // The operator still needs one real message to read, not just a normalized key.
        Assert.Contains("Login failed", incident["sampleError"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnrelatedFailuresStaySeparateIncidents()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);
        var suffix = Suffix();

        await RecordRunAsync(factory, $"a_{suffix}", "FAILED", "Transaction was deadlocked on lock resources.");
        await RecordRunAsync(factory, $"b_{suffix}", "FAILED", "Timeout expired before the operation completed.");

        var board = await TriageAsync(client, token);

        Assert.Equal(2, board["failureCount"]!.GetValue<int>());
        Assert.Equal(2, board["incidentCount"]!.GetValue<int>());
    }

    [Fact]
    public async Task SuccessIsNotAFailureAndInterruptedIs()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);
        var suffix = Suffix();

        await RecordRunAsync(factory, $"ok_{suffix}", "SUCCESS", null);
        // A run the engine could not finish leaves no error message but is still something an
        // operator has to see — the same contract the failure digest email uses.
        await RecordRunAsync(factory, $"cut_{suffix}", "INTERRUPTED", null);

        var board = await TriageAsync(client, token);

        Assert.Equal(1, board["failureCount"]!.GetValue<int>());
        var incident = board["incidents"]!.AsArray().Single()!;
        Assert.Equal($"cut_{suffix}", incident["jobNames"]!.AsArray().Single()!.GetValue<string>());
    }

    [Fact]
    public async Task InFlightRunsAreReportedRunningRatherThanFailed()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);
        var suffix = Suffix();

        using (var scope = factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IJobHistoryStore>();
            await store.LogJobStartAsync($"inflight_{suffix}"); // started, never completed
        }

        var board = await TriageAsync(client, token);

        Assert.Equal(0, board["failureCount"]!.GetValue<int>());
        Assert.Contains(
            board["running"]!.AsArray().Select(r => r!["jobName"]!.GetValue<string>()),
            name => name == $"inflight_{suffix}");
    }

    [Fact]
    public async Task AnOccurrenceThatPassedUnclaimedIsReportedMissed()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);
        var suffix = Suffix();

        // A missed run writes no history row at all, which is exactly why a failure-driven view
        // cannot see it.
        await SaveJobAsync(factory, $"overdue_{suffix}", nextRun: DateTime.Now.AddHours(-2), enabled: true);
        await SaveJobAsync(factory, $"upcoming_{suffix}", nextRun: DateTime.Now.AddHours(2), enabled: true);
        await SaveJobAsync(factory, $"disabled_{suffix}", nextRun: DateTime.Now.AddHours(-2), enabled: false);

        var board = await TriageAsync(client, token);

        var missed = board["missed"]!.AsArray()
            .Select(m => m!["jobName"]!.GetValue<string>())
            .ToList();

        Assert.Contains($"overdue_{suffix}", missed);
        Assert.DoesNotContain($"upcoming_{suffix}", missed);
        // A job an operator deliberately turned off is not an incident.
        Assert.DoesNotContain($"disabled_{suffix}", missed);

        var overdue = board["missed"]!.AsArray()
            .Single(m => m!["jobName"]!.GetValue<string>() == $"overdue_{suffix}")!;
        Assert.True(overdue["overdueMinutes"]!.GetValue<double>() > 100);
    }

    [Fact]
    public async Task RecentLatenessInsideTheGraceWindowIsNotReportedMissed()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);
        var suffix = Suffix();

        // Slightly late is normal under load; reporting it would train operators to ignore the list.
        await SaveJobAsync(factory, $"justlate_{suffix}", nextRun: DateTime.Now.AddMinutes(-1), enabled: true);

        var board = await TriageAsync(client, token);

        Assert.DoesNotContain(
            board["missed"]!.AsArray().Select(m => m!["jobName"]!.GetValue<string>()),
            name => name == $"justlate_{suffix}");
    }

    [Fact]
    public async Task OlderFailuresFallOutOfTheLookbackWindow()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);
        var suffix = Suffix();

        await RecordRunAsync(factory, $"recent_{suffix}", "FAILED", "Connection refused by source_db");

        // The same board, asked about a window the run cannot be in.
        var narrow = await TriageAsync(client, token, "?lookbackHours=1");
        Assert.Equal(1, narrow["failureCount"]!.GetValue<int>());

        var board = await TriageAsync(client, token);
        Assert.Equal(24, board["lookbackHours"]!.GetValue<int>());
    }

    [Fact]
    public async Task BulkRerunReportsEveryJobIndividually()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);
        var suffix = Suffix();

        var response = await AuthPost(client, token, "/api/orchestrator/jobs/rerun",
            new { jobNames = new[] { $"one_{suffix}", $"two_{suffix}" } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<JsonObject>(Json))!;

        // No Orchestrator is running in this host, so both fail — the point under test is that a
        // bulk action reports per job instead of collapsing to one opaque failure.
        Assert.Equal(2, body["requested"]!.GetValue<int>());
        Assert.Equal(2, body["results"]!.AsArray().Count);
        foreach (var result in body["results"]!.AsArray())
        {
            Assert.False(result!["triggered"]!.GetValue<bool>());
            Assert.False(string.IsNullOrWhiteSpace(result["error"]!.GetValue<string>()));
        }
    }

    [Fact]
    public async Task BulkRerunRejectsAnEmptyOrOversizedBatch()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var token = await GetAdminTokenAsync(client);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await AuthPost(client, token, "/api/orchestrator/jobs/rerun", new { jobNames = Array.Empty<string>() })).StatusCode);

        // A mis-click must not be able to enqueue the whole estate.
        var tooMany = Enumerable.Range(0, 60).Select(i => $"job_{i}").ToArray();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await AuthPost(client, token, "/api/orchestrator/jobs/rerun", new { jobNames = tooMany })).StatusCode);
    }

    [Fact]
    public async Task TriageAndBulkRerunRequireOrchestratorAccess()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var username = $"triage_deny_{Suffix()}";
        await CreateViewerAsync(client, adminToken, username);
        var viewerToken = await LoginAsync(client, username, "Ready@Test2!");

        Assert.Equal(HttpStatusCode.Forbidden,
            (await AuthGet(client, viewerToken, "/api/orchestrator/triage")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await AuthPost(client, viewerToken, "/api/orchestrator/jobs/rerun",
                new { jobNames = new[] { "anything" } })).StatusCode);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string Suffix() => Guid.NewGuid().ToString("N")[..8];

    private static async Task<JsonObject> TriageAsync(HttpClient client, string token, string query = "")
    {
        var response = await AuthGet(client, token, "/api/orchestrator/triage" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>(Json))!;
    }

    private static async Task RecordRunAsync(
        PortalWebFactory factory, string jobName, string status, string? error)
    {
        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IJobHistoryStore>();
        var id = await store.LogJobStartAsync(jobName);
        await store.LogJobEndAsync(id, status, error);
    }

    private static async Task SaveJobAsync(
        PortalWebFactory factory, string name, DateTime nextRun, bool enabled)
    {
        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IJobHistoryStore>();
        await store.SaveJobAsync(new JobDefinition(
            Name: name,
            Script: "SELECT 1;",
            Interval: 1,
            Unit: "hours",
            AtTime: null,
            LastRun: null,
            NextRun: nextRun,
            IsEnabled: enabled));
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
