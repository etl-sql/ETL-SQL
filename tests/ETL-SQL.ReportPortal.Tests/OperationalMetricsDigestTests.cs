using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Observability;
using ETL_SQL.ReportPortal;
using ETL_SQL.ReportPortal.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static ETL_SQL.ReportPortal.Services.OperationalMetricsService;

namespace ETL_SQL.ReportPortal.Tests;

/// <summary>
/// The operational-metrics digest composition and its alert thresholds (failure rate, queue depth,
/// pending migrations). Pure logic — no DB, SMTP, or hosted-service loop involved.
/// </summary>
public sealed class OperationalMetricsDigestTests
{
    [Fact]
    public void HealthyMetrics_ProduceNoAlerts_AndAPlainDigest()
    {
        var content = OperationalMetricsDigest.Build(Metrics(), new OperationalDigestConfig());

        Assert.False(content.HasAlerts);
        Assert.Empty(content.Alerts);
        Assert.Contains("Operational digest", content.Subject);
        Assert.Contains("Executions:", content.Body);
        Assert.Contains("up to date", content.Body);
    }

    [Fact]
    public void HighFailureRate_RaisesAlert()
    {
        // 30 of 100 failed = 30% >= default 25% threshold.
        var content = OperationalMetricsDigest.Build(
            Metrics(recentExecutions: 100, recentExecutionFailures: 30),
            new OperationalDigestConfig());

        Assert.True(content.HasAlerts);
        Assert.Contains(content.Alerts, a => a.Contains("failure rate"));
        Assert.Contains("ALERT", content.Subject);
    }

    [Fact]
    public void FailureRateBelowThreshold_DoesNotAlert()
    {
        // 10 of 100 = 10% < 25%.
        var content = OperationalMetricsDigest.Build(
            Metrics(recentExecutions: 100, recentExecutionFailures: 10),
            new OperationalDigestConfig());

        Assert.DoesNotContain(content.Alerts, a => a.Contains("failure rate"));
    }

    [Fact]
    public void PendingMigrations_RaiseAlert_WhenEnabled()
    {
        var content = OperationalMetricsDigest.Build(
            Metrics(pendingMigrations: 2, schemaUpToDate: false),
            new OperationalDigestConfig { AlertOnPendingMigrations = true });

        Assert.Contains(content.Alerts, a => a.Contains("migration"));
    }

    [Fact]
    public void PendingMigrations_Suppressed_WhenDisabled()
    {
        var content = OperationalMetricsDigest.Build(
            Metrics(pendingMigrations: 2, schemaUpToDate: false),
            new OperationalDigestConfig { AlertOnPendingMigrations = false });

        Assert.DoesNotContain(content.Alerts, a => a.Contains("migration"));
    }

    [Fact]
    public void QueueBacklog_RaisesAlert_AtThreshold()
    {
        var content = OperationalMetricsDigest.Build(
            Metrics(queuedExecutions: 25),
            new OperationalDigestConfig { QueueDepthAlertThreshold = 20 });

        Assert.Contains(content.Alerts, a => a.Contains("queue depth"));
    }

    [Fact]
    public async Task OperationalDigestService_EmitsLowCardinalityBackgroundTelemetry()
    {
        var stoppedActivities = new List<Activity>();
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == BackgroundServiceObservability.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => stoppedActivities.Add(activity)
        };
        ActivitySource.AddActivityListener(activityListener);

        var measurements = new List<(string Name, double Value, Dictionary<string, object?> Tags)>();
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == BackgroundServiceObservability.MeterName)
                    listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            measurements.Add((instrument.Name, value, ToDictionary(tags))));
        meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            measurements.Add((instrument.Name, value, ToDictionary(tags))));
        meterListener.Start();

        using var factory = new OperationalDigestFactory();
        var cfg = factory.Services.GetRequiredService<PortalConfig>().OperationalDigest;
        var service = ActivatorUtilities.CreateInstance<OperationalMetricsDigestService>(
            factory.Services,
            NullLogger<OperationalMetricsDigestService>.Instance);

        await service.SendDigestOnceAsync(cfg, CancellationToken.None);

        Assert.Single(factory.Sender.Sent);
        Assert.Contains(stoppedActivities, activity =>
            activity.OperationName == "background_service.run"
            && Tag(activity, ObservabilityConventions.Tags.Component) == "portal"
            && Tag(activity, ObservabilityConventions.Tags.WorkloadKind) == "background"
            && Tag(activity, ObservabilityConventions.Tags.ServiceName) == "operational-digest"
            && Tag(activity, BackgroundServiceObservability.OperationTag) == "send"
            && Tag(activity, ObservabilityConventions.Tags.Status) == "sent");
        Assert.Contains(measurements, measurement => measurement.Name == "etlsql.background_service.run.completed"
            && HasTag(measurement.Tags, ObservabilityConventions.Tags.ServiceName, "operational-digest")
            && HasTag(measurement.Tags, ObservabilityConventions.Tags.Status, "sent"));
        Assert.DoesNotContain(measurements, measurement => measurement.Tags.Any(tag =>
            tag.Value is string value
            && (value.Contains("ops@example.com", StringComparison.OrdinalIgnoreCase)
                || value.Contains("mailer", StringComparison.OrdinalIgnoreCase))));
    }

    private static OperationalMetrics Metrics(
        int recentExecutions = 10,
        int recentExecutionFailures = 0,
        int queuedExecutions = 0,
        int pendingMigrations = 0,
        bool schemaUpToDate = true)
        => new(
            ActiveExecutions: 1,
            QueuedExecutions: queuedExecutions,
            ExecutionCap: 8,
            PerUserExecutionCap: 2,
            Topology: "shared-state-ha",
            NodeId: "portal-node-1",
            AffinityCookieName: "ETLSQL_PORTAL_AFFINITY",
            RecentExecutions: recentExecutions,
            RecentExecutionFailures: recentExecutionFailures,
            RecentDeliveries: 5,
            RecentDeliveryFailures: 0,
            DatasetStorageBytes: 10 * 1024 * 1024,
            SnapshotStorageBytes: 2 * 1024 * 1024,
            ActiveSubscriptions: 3,
            SmtpConnections: 1,
            AppliedMigrations: 40,
            PendingMigrations: pendingMigrations,
            LastAppliedMigration: "20260101_Init",
            SchemaUpToDate: schemaUpToDate,
            AuditOutboxPending: 0,
            AuditOutboxFailed: 0,
            AuditOutboxPendingBytes: 0,
            AuditOutboxOldestPendingAgeSeconds: 0,
            SecurityEventPending: 0,
            SecurityEventFailed: 0,
            SecurityEventStoredBytes: 0,
            SecurityEventDropped: 0,
            SecurityEventOldestPendingAgeSeconds: 0,
            SecurityEventCollectorConfigured: false,
            SecurityEventCollectorReachable: null,
            AverageExecutionDurationMs: 1234,
            AverageQueuedExecutionAgeSeconds: 3,
            HourlyExecutionLoad: new List<HourlyExecutionLoad>(),
            GeneratedAt: new DateTime(2026, 7, 3, 12, 0, 0, DateTimeKind.Utc),
            WindowHours: 24);

    private sealed class OperationalDigestFactory : PortalWebFactory
    {
        public FakeSender Sender { get; } = new();

        protected override void CustomizePortalConfig(PortalConfig config)
        {
            config.OperationalDigest.Enabled = true;
            config.OperationalDigest.AlertOnly = false;
            config.OperationalDigest.Recipients = "ops@example.com";
            config.OperationalDigest.SmtpAlias = "mailer";
        }

        protected override void CustomizeServices(IServiceCollection services)
        {
            services.RemoveAll<IAdminNotificationSender>();
            services.AddSingleton<IAdminNotificationSender>(Sender);
        }
    }

    private sealed class FakeSender : IAdminNotificationSender
    {
        public List<AdminNotification> Sent { get; } = new();

        public Task<(bool Success, string? Error)> SendAsync(AdminNotification notification, CancellationToken ct)
        {
            Sent.Add(notification);
            return Task.FromResult<(bool, string?)>((true, null));
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
}
