using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Observability;
using ETL_SQL.Portal;
using ETL_SQL.Portal.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static ETL_SQL.Portal.Services.OperationalMetricsService;

namespace ETL_SQL.Portal.Tests;

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
        Assert.Contains(content.Alerts, a => a.Code == "portal_execution_failure_rate" && a.Severity == "critical");
        Assert.Contains("ALERT", content.Subject);
        Assert.Contains("Runbook:", content.Body);
    }

    [Fact]
    public void FailureRateBelowThreshold_DoesNotAlert()
    {
        // 10 of 100 = 10% < 25%.
        var content = OperationalMetricsDigest.Build(
            Metrics(recentExecutions: 100, recentExecutionFailures: 10),
            new OperationalDigestConfig());

        Assert.DoesNotContain(content.Alerts, a => a.Code == "portal_execution_failure_rate");
    }

    [Fact]
    public void PendingMigrations_RaiseAlert_WhenEnabled()
    {
        var content = OperationalMetricsDigest.Build(
            Metrics(pendingMigrations: 2, schemaUpToDate: false),
            new OperationalDigestConfig { AlertOnPendingMigrations = true });

        Assert.Contains(content.Alerts, a => a.Code == "portal_schema_pending_migrations");
    }

    [Fact]
    public void PendingMigrations_Suppressed_WhenDisabled()
    {
        var content = OperationalMetricsDigest.Build(
            Metrics(pendingMigrations: 2, schemaUpToDate: false),
            new OperationalDigestConfig { AlertOnPendingMigrations = false });

        Assert.DoesNotContain(content.Alerts, a => a.Code == "portal_schema_pending_migrations");
    }

    [Fact]
    public void QueueBacklog_RaisesAlert_AtThreshold()
    {
        var content = OperationalMetricsDigest.Build(
            Metrics(queuedExecutions: 25),
            new OperationalDigestConfig { QueueDepthAlertThreshold = 20 });

        Assert.Contains(content.Alerts, a => a.Code == "portal_execution_queue_depth");
    }

    [Fact]
    public void QueueAgeDeliveryOutboxAndStorageThresholds_RaiseStructuredAlerts()
    {
        var content = OperationalMetricsDigest.Build(
            Metrics(
                recentDeliveries: 10,
                recentDeliveryFailures: 4,
                averageQueuedExecutionAgeSeconds: 600,
                auditOutboxPending: 1500,
                auditOutboxOldestPendingAgeSeconds: 1200,
                securityEventPending: 1100,
                securityEventOldestPendingAgeSeconds: 1000,
                datasetStorageBytes: 11 * 1024 * 1024,
                snapshotStorageBytes: 12 * 1024 * 1024,
                staleSnapshots: 2,
                staleDatasets: 3,
                activePolicyVersionsExpiring: 1,
                activePolicyVersionsExpired: 1),
            new OperationalDigestConfig
            {
                DatasetStorageBytesAlertThreshold = 10 * 1024 * 1024,
                SnapshotStorageBytesAlertThreshold = 10 * 1024 * 1024,
                SnapshotFreshnessHours = 24,
                DatasetFreshnessHours = 24,
                PolicyVersionExpiryWarningHours = 72
            });

        Assert.Contains(content.Alerts, a => a.Code == "portal_execution_queue_age");
        Assert.Contains(content.Alerts, a => a.Code == "portal_delivery_failure_rate");
        Assert.Contains(content.Alerts, a => a.Code == "portal_audit_outbox_backlog" && a.Severity == "critical");
        Assert.Contains(content.Alerts, a => a.Code == "portal_audit_outbox_age");
        Assert.Contains(content.Alerts, a => a.Code == "security_event_outbox_backlog");
        Assert.Contains(content.Alerts, a => a.Code == "security_event_outbox_age");
        Assert.Contains(content.Alerts, a => a.Code == "portal_dataset_storage_bytes");
        Assert.Contains(content.Alerts, a => a.Code == "portal_snapshot_storage_bytes");
        Assert.Contains(content.Alerts, a => a.Code == "portal_stale_snapshots");
        Assert.Contains(content.Alerts, a => a.Code == "portal_stale_datasets");
        Assert.Contains(content.Alerts, a => a.Code == "portal_policy_version_expiring");
        Assert.Contains(content.Alerts, a => a.Code == "portal_policy_version_expired" && a.Severity == "critical");
        Assert.All(content.Alerts, alert => Assert.Contains("Alerting_Service_Objectives.md#", alert.Runbook));
    }

    [Fact]
    public void EnterpriseHealthSignals_RaiseStructuredAlerts()
    {
        var content = OperationalMetricsDigest.Build(
            Metrics(
                databaseConnectivityHealthy: false,
                databasePoolExhaustionSuspected: true,
                policyAuthorityHealthy: false,
                clientCertificateExpiresAtUtc: DateTimeOffset.UtcNow.AddHours(12),
                unhealthyFleetNodes: 1),
            new OperationalDigestConfig
            {
                CertificateExpiryWarningHours = 24
            });

        Assert.Contains(content.Alerts, a => a.Code == "portal_database_connectivity" && a.Severity == "critical");
        Assert.Contains(content.Alerts, a => a.Code == "portal_database_pool_exhaustion" && a.Severity == "critical");
        Assert.Contains(content.Alerts, a => a.Code == "portal_policy_signature_unavailable" && a.Severity == "critical");
        Assert.Contains(content.Alerts, a => a.Code == "portal_client_certificate_expiry" && a.Severity == "warning");
        Assert.Contains(content.Alerts, a => a.Code == "portal_unhealthy_fleet_nodes" && a.Severity == "warning");
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
        bool schemaUpToDate = true,
        int recentDeliveries = 5,
        int recentDeliveryFailures = 0,
        double averageQueuedExecutionAgeSeconds = 3,
        int auditOutboxPending = 0,
        double auditOutboxOldestPendingAgeSeconds = 0,
        int securityEventPending = 0,
        double securityEventOldestPendingAgeSeconds = 0,
        long datasetStorageBytes = 10 * 1024 * 1024,
        long snapshotStorageBytes = 2 * 1024 * 1024,
        int staleSnapshots = 0,
        int staleDatasets = 0,
        int activePolicyVersionsExpiring = 0,
        int activePolicyVersionsExpired = 0,
        bool databaseConnectivityHealthy = true,
        bool databasePoolExhaustionSuspected = false,
        bool policyAuthorityHealthy = true,
        DateTimeOffset? clientCertificateExpiresAtUtc = null,
        int unhealthyFleetNodes = 0)
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
            RecentDeliveries: recentDeliveries,
            RecentDeliveryFailures: recentDeliveryFailures,
            DatasetStorageBytes: datasetStorageBytes,
            SnapshotStorageBytes: snapshotStorageBytes,
            StorageUsageSampleStale: false,
            StorageUsageLastSuccessfulSampleUtc: null,
            StorageUsageLastFailureUtc: null,
            StaleSnapshots: staleSnapshots,
            StaleDatasets: staleDatasets,
            ActivePolicyVersionsExpiring: activePolicyVersionsExpiring,
            ActivePolicyVersionsExpired: activePolicyVersionsExpired,
            ActiveSubscriptions: 3,
            SmtpConnections: 1,
            AppliedMigrations: 40,
            PendingMigrations: pendingMigrations,
            LastAppliedMigration: "20260101_Init",
            SchemaUpToDate: schemaUpToDate,
            AuditOutboxPending: auditOutboxPending,
            AuditOutboxFailed: 0,
            AuditOutboxPendingBytes: 0,
            AuditOutboxOldestPendingAgeSeconds: auditOutboxOldestPendingAgeSeconds,
            SecurityEventPending: securityEventPending,
            SecurityEventFailed: 0,
            SecurityEventStoredBytes: 0,
            SecurityEventDropped: 0,
            SecurityEventOldestPendingAgeSeconds: securityEventOldestPendingAgeSeconds,
            SecurityEventCollectorConfigured: false,
            SecurityEventCollectorReachable: null,
            DatabaseConnectivityHealthy: databaseConnectivityHealthy,
            DatabasePoolExhaustionSuspected: databasePoolExhaustionSuspected,
            PolicyAuthorityHealthy: policyAuthorityHealthy,
            ClientCertificateExpiresAtUtc: clientCertificateExpiresAtUtc,
            UnhealthyFleetNodes: unhealthyFleetNodes,
            AverageExecutionDurationMs: 1234,
            AverageQueuedExecutionAgeSeconds: averageQueuedExecutionAgeSeconds,
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
