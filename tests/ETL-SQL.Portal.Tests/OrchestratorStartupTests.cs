using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Execution;
using ETL_SQL.Core.Observability;
using ETL_SQL.Orchestrator.Channels;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.Orchestrator.Scheduling;
using ETL_SQL.Orchestrator.Service;
using ETL_SQL.Orchestrator.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Unit coverage for the startup guard that refuses to run the unauthenticated job API on a
/// network-reachable address.
/// </summary>
public class OrchestratorStartupTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] pairs)
    {
        var dict = new Dictionary<string, string?>();
        foreach (var (key, value) in pairs) dict[key] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Theory]
    [InlineData("http://0.0.0.0:5001", true)]
    [InlineData("http://+:5001", true)]
    [InlineData("http://*:5001", true)]
    [InlineData("http://[::]:5001", true)]
    [InlineData("http://example.com:5001", true)]
    [InlineData("http://192.168.1.10:5001", true)]
    [InlineData("http://localhost:5001", false)]
    [InlineData("http://127.0.0.1:5001", false)]
    [InlineData("https://127.0.0.1", false)]
    [InlineData("http://[::1]:5001", false)]
    public void IsNonLoopbackBinding_ClassifiesHosts(string url, bool expected)
    {
        Assert.Equal(expected, OrchestratorStartup.IsNonLoopbackBinding(url));
    }

    [Fact]
    public void NoKey_NonLoopbackUrls_Throws()
    {
        var cfg = Config(("Orchestrator:ApiKey", ""), ("urls", "http://0.0.0.0:5001"));
        var ex = Assert.Throws<InvalidOperationException>(() => OrchestratorStartup.ValidateApiKeyBinding(cfg));
        Assert.Contains("Orchestrator:ApiKey", ex.Message);
    }

    [Fact]
    public void NoKey_NonLoopbackKestrelEndpoint_Throws()
    {
        var cfg = Config(
            ("Orchestrator:ApiKey", null),
            ("Kestrel:Endpoints:Http:Url", "http://0.0.0.0:5001"));
        Assert.Throws<InvalidOperationException>(() => OrchestratorStartup.ValidateApiKeyBinding(cfg));
    }

    [Fact]
    public void NoKey_LoopbackUrls_DoesNotThrow()
    {
        var cfg = Config(("Orchestrator:ApiKey", ""), ("urls", "http://localhost:5001"));
        OrchestratorStartup.ValidateApiKeyBinding(cfg); // must not throw
    }

    [Fact]
    public void NoKey_NoConfiguredUrls_DoesNotThrow()
    {
        // No explicit binding → host default is loopback, which is safe without a key.
        var cfg = Config(("Orchestrator:ApiKey", ""));
        OrchestratorStartup.ValidateApiKeyBinding(cfg);
    }

    [Fact]
    public void KeyConfigured_NonLoopback_DoesNotThrow()
    {
        var cfg = Config(
            ("Orchestrator:ApiKey", "a-real-key"),
            ("Orchestrator:IdentitySigningSecret", "a-dedicated-test-identity-secret-at-least-32-bytes"),
            ("urls", "http://0.0.0.0:5001"));
        OrchestratorStartup.ValidateApiKeyBinding(cfg);
    }

    [Fact]
    public void KeyWithoutFederatedIdentitySecret_NonLoopback_Throws()
    {
        var cfg = Config(("Orchestrator:ApiKey", "a-real-key"), ("urls", "http://0.0.0.0:5001"));
        var ex = Assert.Throws<InvalidOperationException>(() => OrchestratorStartup.ValidateApiKeyBinding(cfg));
        Assert.Contains("IdentitySigningSecret", ex.Message);
    }

    [Fact]
    public async Task HostedService_StartStop_EmitsBackgroundTelemetry()
    {
        using var telemetry = new BackgroundTelemetryCapture();
        var dbPath = Path.Combine(Path.GetTempPath(), $"orchestrator_host_{Guid.NewGuid():N}.db");
        var store = new SQLiteJobHistoryStore(dbPath);
        var services = new ServiceCollection().BuildServiceProvider();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Scheduler:SleepIntervalSeconds"] = "30"
            })
            .Build();
        var scheduler = new SchedulerService(
            services,
            store,
            NullLogger<SchedulerService>.Instance,
            new JobThrottle(
                Microsoft.Extensions.Options.Options.Create(new JobThrottleOptions { MaxConcurrentJobs = 1 }),
                NullLogger<JobThrottle>.Instance),
            config,
            new NullSessionStateManager());
        var hosted = new OrchestratorHostedService(
            scheduler,
            NullLogger<OrchestratorHostedService>.Instance);

        try
        {
            await hosted.StartAsync(CancellationToken.None);
            await hosted.StopAsync(CancellationToken.None);

            AssertServiceRun(telemetry, "start", "success");
            AssertServiceRun(telemetry, "stop", "success");
            Assert.DoesNotContain(telemetry.Measurements, measurement => measurement.Tags.Any(tag =>
                tag.Value is string value
                && (value.Contains(dbPath, StringComparison.OrdinalIgnoreCase)
                    || value.Contains("Scheduler", StringComparison.OrdinalIgnoreCase))));
        }
        finally
        {
            await hosted.StopAsync(CancellationToken.None);
            foreach (var path in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void AdHocJobObservability_UsesSharedNodeMetricDimension()
    {
        var measurements = new List<(string Name, double Value, Dictionary<string, object?> Tags)>();
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == OrchestratorObservability.MeterName)
                    listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            measurements.Add((instrument.Name, value, ToDictionary(tags))));
        meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            measurements.Add((instrument.Name, value, ToDictionary(tags))));
        meterListener.Start();

        OrchestratorObservability.CompleteAdHocJobActivity(
            null,
            "secret-job-id",
            JobRunStatus.Completed,
            durationMs: 25,
            rowsProcessed: 10,
            peakMemoryBytes: 1024,
            cpuTimeSeconds: 0.1);

        Assert.Contains(measurements, measurement =>
            measurement.Name == "etlsql.orchestrator.job.completed"
            && HasTag(measurement.Tags, ObservabilityConventions.Tags.Node, Environment.MachineName)
            && HasTag(measurement.Tags, ObservabilityConventions.Tags.Component, "orchestrator")
            && HasTag(measurement.Tags, ObservabilityConventions.Tags.WorkloadKind, "ad-hoc")
            && HasTag(measurement.Tags, ObservabilityConventions.Tags.Status, "Completed"));
        Assert.DoesNotContain(measurements, measurement =>
            measurement.Tags.ContainsKey(ObservabilityConventions.Tags.JobId));
        Assert.DoesNotContain(measurements, measurement => measurement.Tags.Any(tag =>
            tag.Value is string value && value.Contains("secret-job-id", StringComparison.OrdinalIgnoreCase)));
    }

    private static void AssertServiceRun(BackgroundTelemetryCapture telemetry, string operation, string status)
    {
        Assert.Contains(telemetry.Activities, activity =>
            activity.OperationName == "background_service.run"
            && Tag(activity, ObservabilityConventions.Tags.Component) == "orchestrator"
            && Tag(activity, ObservabilityConventions.Tags.ServiceName) == "orchestrator-host"
            && Tag(activity, BackgroundServiceObservability.OperationTag) == operation
            && Tag(activity, ObservabilityConventions.Tags.Status) == status);
        Assert.Contains(telemetry.Measurements, measurement =>
            measurement.Name == "etlsql.background_service.run.completed"
            && HasTag(measurement.Tags, ObservabilityConventions.Tags.Component, "orchestrator")
            && HasTag(measurement.Tags, ObservabilityConventions.Tags.ServiceName, "orchestrator-host")
            && HasTag(measurement.Tags, BackgroundServiceObservability.OperationTag, operation)
            && HasTag(measurement.Tags, ObservabilityConventions.Tags.Status, status));
    }

    private sealed class BackgroundTelemetryCapture : IDisposable
    {
        private readonly ActivityListener _activityListener;
        private readonly MeterListener _meterListener;

        public List<Activity> Activities { get; } = [];
        public List<(string Name, double Value, Dictionary<string, object?> Tags)> Measurements { get; } = [];

        public BackgroundTelemetryCapture()
        {
            _activityListener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == BackgroundServiceObservability.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStopped = activity => Activities.Add(activity)
            };
            ActivitySource.AddActivityListener(_activityListener);

            _meterListener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == BackgroundServiceObservability.MeterName)
                        listener.EnableMeasurementEvents(instrument);
                }
            };
            _meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
                Measurements.Add((instrument.Name, value, ToDictionary(tags))));
            _meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
                Measurements.Add((instrument.Name, value, ToDictionary(tags))));
            _meterListener.Start();
        }

        public void Dispose()
        {
            _activityListener.Dispose();
            _meterListener.Dispose();
        }
    }

    private static string? Tag(Activity activity, string key)
    {
        var value = activity.TagObjects.FirstOrDefault(t => t.Key == key).Value;
        return value?.ToString();
    }

    private static Dictionary<string, object?> ToDictionary(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var result = new Dictionary<string, object?>();
        foreach (var tag in tags)
            result[tag.Key] = tag.Value;
        return result;
    }

    private static bool HasTag(Dictionary<string, object?> tags, string key, object value) =>
        tags.TryGetValue(key, out var actual) && Equals(actual, value);

    private sealed class NullSessionStateManager : ISessionStateManager
    {
        public string SessionRoot => string.Empty;
        public byte[] GetSpillKey(string sessionId) => new byte[32];
        public Task SaveSession(string sessionId, object evaluator, string? scriptSource = null) => Task.CompletedTask;
        public Task<SessionState?> LoadSession(string sessionId, string? keyScope = null) =>
            Task.FromResult<SessionState?>(null);
        public void ClearSession(string sessionId) { }
        public IEnumerable<SessionSummary> GetSessions(bool includeSize = false) => [];
        public bool IsSessionInUse(string sessionId) => false;
        public void RegisterActiveSession(string sessionId) { }
        public void UnregisterActiveSession(string sessionId) { }
        public void ReapStaleSessions(TimeSpan maxAge) { }
        public void ReapStaleSessions() { }
    }
}
