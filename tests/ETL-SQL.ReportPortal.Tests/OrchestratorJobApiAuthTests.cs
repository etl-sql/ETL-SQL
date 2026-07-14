using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Xunit;

namespace ETL_SQL.ReportPortal.Tests;

/// <summary>
/// Auth coverage for the ad-hoc job API. Proves that submitting, cancelling, and inspecting jobs all
/// require a valid X-Orchestrator-Key, while liveness/readiness probes stay open. The factory always
/// configures an API key, so the "no key" cases exercise the deny path.
/// </summary>
[Trait("Category", "Portal")]
public class OrchestratorJobApiAuthTests : IDisposable
{
    private const string ValidKey = "test-orch-key-12345";
    private readonly OrchestratorWebFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private static HttpRequestMessage Request(
        HttpMethod method,
        string uri,
        string? apiKey,
        object? body = null,
        long? version = null)
    {
        var req = new HttpRequestMessage(method, uri);
        if (apiKey != null) req.Headers.Add("X-Orchestrator-Key", apiKey);
        if (version.HasValue) req.Headers.TryAddWithoutValidation("If-Match", $"\"{version.Value}\"");
        if (body != null) req.Content = JsonContent.Create(body);
        return req;
    }

    [Fact]
    public async Task PostJob_WithoutApiKey_IsUnauthorized()
    {
        var client = _factory.CreateClient();
        using var req = Request(HttpMethod.Post, "/jobs", apiKey: null, body: new { ScriptText = "PRINT 'hi';" });
        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task PostJob_WithWrongApiKey_IsUnauthorized()
    {
        var client = _factory.CreateClient();
        using var req = Request(HttpMethod.Post, "/jobs", apiKey: "wrong-key", body: new { ScriptText = "PRINT 'hi';" });
        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task PostJob_WithValidApiKey_IsAccepted()
    {
        var client = _factory.CreateClient();
        using var req = Request(HttpMethod.Post, "/jobs", apiKey: ValidKey, body: new { ScriptText = "PRINT 'hi';" });
        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
    }

    [Fact]
    public async Task CancelJob_WithoutApiKey_IsUnauthorized()
    {
        var client = _factory.CreateClient();
        using var req = Request(HttpMethod.Delete, "/jobs/deadbeef", apiKey: null);
        var res = await client.SendAsync(req);
        // Auth is checked before the job-exists lookup, so this is 401, not 404.
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task GetJobStatus_WithoutApiKey_IsUnauthorized()
    {
        var client = _factory.CreateClient();
        using var req = Request(HttpMethod.Get, "/jobs/deadbeef", apiKey: null);
        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/metrics")]
    [InlineData("/metrics/prometheus")]
    public async Task Probes_StayOpenWithoutApiKey(string path)
    {
        var client = _factory.CreateClient();
        using var req = Request(HttpMethod.Get, path, apiKey: null);
        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.True(res.Headers.Contains("X-Correlation-ID"));
    }

    [Fact]
    public async Task PrometheusMetrics_UsesTextFormat()
    {
        var client = _factory.CreateClient();
        using var req = Request(HttpMethod.Get, "/metrics/prometheus", apiKey: null);
        var res = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.StartsWith("text/plain", res.Content.Headers.ContentType?.MediaType);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("# HELP etlsql_orchestrator_jobs_active", body);
        Assert.Contains("component=\"orchestrator\"", body);
    }

    [Fact]
    [Trait("CompatBreak", "0.12")]
    public async Task ScheduledJobUpdate_RequiresVersion_AndReturnsCurrentStateOnConflict()
    {
        var client = _factory.CreateClient();
        using var create = Request(
            HttpMethod.Post,
            "/api/scheduled-jobs",
            ValidKey,
            new
            {
                Name = "versioned-job",
                ScriptText = "PRINT 'hi';",
                Interval = 1,
                Unit = "HOUR"
            });
        Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(create)).StatusCode);

        using var missing = Request(
            HttpMethod.Put,
            "/api/scheduled-jobs/versioned-job",
            ValidKey,
            new { Interval = 2 });
        Assert.Equal(HttpStatusCode.PreconditionRequired, (await client.SendAsync(missing)).StatusCode);

        using var update = Request(
            HttpMethod.Put,
            "/api/scheduled-jobs/versioned-job",
            ValidKey,
            new { Interval = 2 },
            version: 1);
        var updated = await client.SendAsync(update);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var updatedBody = await updated.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>();
        Assert.Equal(2, updatedBody!["version"]!.GetValue<long>());

        using var stale = Request(
            HttpMethod.Put,
            "/api/scheduled-jobs/versioned-job",
            ValidKey,
            new { Interval = 3 },
            version: 1);
        var conflict = await client.SendAsync(stale);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var conflictBody = await conflict.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>();
        Assert.Equal(2, conflictBody!["current"]!["version"]!.GetValue<long>());
        Assert.Equal(2, conflictBody["current"]!["interval"]!.GetValue<int>());
    }
}
