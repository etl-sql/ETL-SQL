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

    private static HttpRequestMessage Request(HttpMethod method, string uri, string? apiKey, object? body = null)
    {
        var req = new HttpRequestMessage(method, uri);
        if (apiKey != null) req.Headers.Add("X-Orchestrator-Key", apiKey);
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
    public async Task Probes_StayOpenWithoutApiKey(string path)
    {
        var client = _factory.CreateClient();
        using var req = Request(HttpMethod.Get, path, apiKey: null);
        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }
}
