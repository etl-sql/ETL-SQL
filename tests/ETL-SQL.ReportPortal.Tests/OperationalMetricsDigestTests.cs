using System;
using System.Collections.Generic;
using ETL_SQL.ReportPortal;
using ETL_SQL.ReportPortal.Services;
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
}
