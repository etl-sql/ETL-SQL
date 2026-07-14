using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Observability;
using ETL_SQL.Orchestrator.Channels;
using ETL_SQL.Orchestrator.Service;
using Microsoft.Extensions.DependencyInjection;
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
        using (var scope = _factory.Services.CreateScope())
        {
            var history = scope.ServiceProvider.GetRequiredService<IJobHistoryStore>();
            var hostMetrics = scope.ServiceProvider.GetRequiredService<IHostMetricsStore>();
            await history.InitializeAsync();
            var started = DateTime.UtcNow.AddMinutes(-10);
            var entryId = await history.LogJobStartAsync("secret-job-name");
            await history.LogJobEndAsync(
                entryId,
                "FAILED",
                "secret failure should not be in labels",
                rowsProcessed: 123,
                peakMemoryBytes: 456,
                cpuTimeSeconds: 1.5);
            await hostMetrics.AppendHostMetricAsync(new HostMetricSample(
                Environment.MachineName,
                started,
                MemoryLoadPercent: 55.5,
                ProcessCpuPercent: 12.5,
                HostCpuPercent: 33.5,
                StateDiskFreeBytes: 1000,
                SpillDiskFreeBytes: 2000));
        }

        using var req = Request(HttpMethod.Get, "/metrics/prometheus", apiKey: null);
        var res = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.StartsWith("text/plain", res.Content.Headers.ContentType?.MediaType);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("# HELP etlsql_orchestrator_jobs_active", body);
        Assert.Contains("# HELP etlsql_orchestrator_jobs_completed_1h", body);
        Assert.Contains("etlsql_orchestrator_jobs_completed_1h", body);
        Assert.Contains("etlsql_orchestrator_jobs_failed_1h", body);
        Assert.Contains("etlsql_orchestrator_rows_processed_1h", body);
        Assert.Contains("etlsql_orchestrator_memory_load_percent", body);
        Assert.Contains("etlsql_orchestrator_state_disk_free_bytes", body);
        Assert.Contains("component=\"orchestrator\"", body);
        Assert.Contains($"node=\"{Environment.MachineName}\"", body);
        Assert.DoesNotContain("secret-job-name", body);
        Assert.DoesNotContain("secret failure", body);
    }

    [Fact]
    public void OrchestratorObservability_EmitsAdHocJobSpanAndMetrics()
    {
        var stoppedActivities = new List<Activity>();
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == OrchestratorObservability.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => stoppedActivities.Add(activity)
        };
        ActivitySource.AddActivityListener(activityListener);

        var measurements = new List<(string Name, double Value, Dictionary<string, object?> Tags)>();
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == OrchestratorObservability.MeterName)
                    l.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            measurements.Add((instrument.Name, value, ToDictionary(tags))));
        meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            measurements.Add((instrument.Name, value, ToDictionary(tags))));
        meterListener.Start();

        var activity = OrchestratorObservability.StartAdHocJobActivity("job-observe", "corr-observe");
        OrchestratorObservability.CompleteAdHocJobActivity(
            activity,
            "job-observe",
            JobRunStatus.Completed,
            durationMs: 25,
            rowsProcessed: 12,
            peakMemoryBytes: 4096,
            cpuTimeSeconds: 0.5);
        activity?.Dispose();

        var span = Assert.Single(stoppedActivities);
        Assert.Equal("orchestrator.job", span.OperationName);
        Assert.Equal("job-observe", Tag(span, ObservabilityConventions.Tags.JobId));
        Assert.Equal("corr-observe", Tag(span, ObservabilityConventions.Tags.CorrelationId));
        Assert.Equal("Completed", Tag(span, ObservabilityConventions.Tags.Status));
        Assert.Contains(measurements, m => m.Name == "etlsql.orchestrator.job.completed"
            && HasTag(m.Tags, ObservabilityConventions.Tags.Component, "orchestrator")
            && HasTag(m.Tags, ObservabilityConventions.Tags.Status, "Completed")
            && HasTag(m.Tags, ObservabilityConventions.Tags.WorkloadKind, "ad-hoc"));
        Assert.Contains(measurements, m => m.Name == "etlsql.orchestrator.job.rows_processed" && m.Value == 12);
        Assert.DoesNotContain(measurements, m => m.Tags.ContainsKey(ObservabilityConventions.Tags.JobId));
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

    private static string? Tag(Activity activity, string key) =>
        activity.Tags.FirstOrDefault(t => t.Key == key).Value;

    private static Dictionary<string, object?> ToDictionary(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var result = new Dictionary<string, object?>();
        foreach (var tag in tags)
            result[tag.Key] = tag.Value;
        return result;
    }

    private static bool HasTag(Dictionary<string, object?> tags, string key, object value) =>
        tags.TryGetValue(key, out var actual) && Equals(actual, value);
}
