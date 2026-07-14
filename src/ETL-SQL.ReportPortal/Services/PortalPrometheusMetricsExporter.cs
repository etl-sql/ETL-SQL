using System.Diagnostics;
using System.Globalization;
using System.Text;
using ETL_SQL.Core.Observability;

namespace ETL_SQL.ReportPortal.Services;

/// <summary>
/// Prometheus text export for the same non-secret operational snapshot exposed through the admin
/// metrics API. The endpoint is intended for network-controlled scrapers; it never emits paths,
/// credentials, usernames, report names, scripts, connection strings, or policy payload values.
/// </summary>
public sealed class PortalPrometheusMetricsExporter(
    OperationalMetricsService metricsService,
    PortalConfig? portalConfig = null)
{
    public async Task<string> ExportAsync(CancellationToken ct = default)
    {
        var metrics = await metricsService.GetAsync(ct);
        var alertConfig = portalConfig?.OperationalDigest ?? new OperationalDigestConfig();
        var labels = new Dictionary<string, string>
        {
            [ObservabilityConventions.PrometheusLabel(PortalObservability.Tags.Environment)] = Environment.GetEnvironmentVariable("ETLSQL_ENV") ?? "default",
            [ObservabilityConventions.PrometheusLabel(PortalObservability.Tags.Node)] = metrics.NodeId,
            [ObservabilityConventions.PrometheusLabel(PortalObservability.Tags.Component)] = "portal"
        };

        var sb = new StringBuilder();
        AppendGauge(sb, "etlsql_portal_execution_active",
            "Currently running report executions.", metrics.ActiveExecutions, labels);
        AppendGauge(sb, "etlsql_portal_execution_queued",
            "Currently queued report executions.", metrics.QueuedExecutions, labels);
        AppendGauge(sb, "etlsql_portal_execution_cap",
            "Maximum concurrent report executions allowed on this node.", metrics.ExecutionCap, labels);
        AppendGauge(sb, "etlsql_portal_execution_per_user_cap",
            "Maximum concurrent report executions allowed per user.", metrics.PerUserExecutionCap, labels);
        AppendGauge(sb, "etlsql_portal_execution_recent_total",
            "Executions completed within the metrics window.", metrics.RecentExecutions, labels);
        AppendGauge(sb, "etlsql_portal_execution_recent_failures",
            "Failed or cancelled executions within the metrics window.", metrics.RecentExecutionFailures, labels);
        AppendGauge(sb, "etlsql_portal_execution_duration_average_ms",
            "Average completed execution duration in milliseconds within the metrics window.",
            metrics.AverageExecutionDurationMs, labels);
        AppendGauge(sb, "etlsql_portal_execution_queue_age_average_seconds",
            "Average age in seconds of currently queued executions.", metrics.AverageQueuedExecutionAgeSeconds, labels);
        AppendGauge(sb, "etlsql_portal_subscription_delivery_recent_total",
            "Subscription deliveries completed within the metrics window.", metrics.RecentDeliveries, labels);
        AppendGauge(sb, "etlsql_portal_subscription_delivery_recent_failures",
            "Failed or denied subscription deliveries within the metrics window.",
            metrics.RecentDeliveryFailures, labels);
        AppendGauge(sb, "etlsql_portal_dataset_storage_bytes",
            "Bytes currently used by Portal dataset storage.", metrics.DatasetStorageBytes, labels);
        AppendGauge(sb, "etlsql_portal_snapshot_storage_bytes",
            "Bytes currently used by Portal snapshot storage.", metrics.SnapshotStorageBytes, labels);
        AppendGauge(sb, "etlsql_portal_stale_snapshots",
            "Report snapshots older than the configured operational freshness objective.",
            metrics.StaleSnapshots, labels);
        AppendGauge(sb, "etlsql_portal_stale_datasets",
            "Datasets older than the configured operational freshness objective.",
            metrics.StaleDatasets, labels);
        AppendGauge(sb, "etlsql_portal_policy_versions_expiring",
            "Active policy versions expiring within the configured operational warning window.",
            metrics.ActivePolicyVersionsExpiring, labels);
        AppendGauge(sb, "etlsql_portal_policy_versions_expired",
            "Active policy versions that have passed their expiration timestamp.",
            metrics.ActivePolicyVersionsExpired, labels);
        AppendGauge(sb, "etlsql_portal_policy_authority_healthy",
            "Whether the policy-authority signing surface is healthy.",
            metrics.PolicyAuthorityHealthy ? 1 : 0, labels);
        AppendGauge(sb, "etlsql_portal_client_certificate_expires_in_seconds",
            "Seconds until the enrolled client certificate expires; -1 means not configured or unavailable.",
            metrics.ClientCertificateExpiresAtUtc is null
                ? -1
                : Math.Floor((metrics.ClientCertificateExpiresAtUtc.Value - DateTimeOffset.UtcNow).TotalSeconds),
            labels);
        AppendGauge(sb, "etlsql_portal_subscriptions_active",
            "Active report subscriptions.", metrics.ActiveSubscriptions, labels);
        AppendGauge(sb, "etlsql_portal_smtp_connections",
            "Configured SMTP connections.", metrics.SmtpConnections, labels);
        AppendGauge(sb, "etlsql_portal_schema_applied_migrations",
            "Applied Portal database migrations.", metrics.AppliedMigrations, labels);
        AppendGauge(sb, "etlsql_portal_schema_pending_migrations",
            "Pending Portal database migrations.", metrics.PendingMigrations, labels);
        AppendGauge(sb, "etlsql_portal_schema_up_to_date",
            "Whether the Portal database schema has no pending migrations.", metrics.SchemaUpToDate ? 1 : 0, labels);
        AppendGauge(sb, "etlsql_portal_database_reachable",
            "Whether the Portal database was reachable while composing this metrics snapshot.", 1, labels);
        AppendGauge(sb, "etlsql_portal_database_connectivity_healthy",
            "Whether the Portal database health check is healthy.",
            metrics.DatabaseConnectivityHealthy ? 1 : 0, labels);
        AppendGauge(sb, "etlsql_portal_database_pool_exhaustion_suspected",
            "Whether Portal database diagnostics indicate pool exhaustion or timeout pressure.",
            metrics.DatabasePoolExhaustionSuspected ? 1 : 0, labels);
        AppendGauge(sb, "etlsql_portal_fleet_nodes_unhealthy",
            "Number of fleet nodes represented by this snapshot that report degraded or unhealthy readiness.",
            metrics.UnhealthyFleetNodes, labels);
        AppendGauge(sb, "etlsql_portal_metrics_window_hours",
            "Operational metrics lookback window in hours.", metrics.WindowHours, labels);
        AppendGauge(sb, "etlsql_portal_audit_outbox_pending",
            "Pending durable audit outbox messages.", metrics.AuditOutboxPending, labels);
        AppendGauge(sb, "etlsql_portal_audit_outbox_failed",
            "Failed durable audit outbox messages.", metrics.AuditOutboxFailed, labels);
        AppendGauge(sb, "etlsql_portal_audit_outbox_pending_bytes",
            "Approximate bytes stored by pending durable audit outbox messages.",
            metrics.AuditOutboxPendingBytes, labels);
        AppendGauge(sb, "etlsql_portal_audit_outbox_oldest_pending_age_seconds",
            "Age in seconds of the oldest pending durable audit outbox message.",
            metrics.AuditOutboxOldestPendingAgeSeconds, labels);
        AppendGauge(sb, "etlsql_security_event_pending",
            "Pending local security-event outbox messages.", metrics.SecurityEventPending, labels);
        AppendGauge(sb, "etlsql_security_event_failed",
            "Failed local security-event outbox messages.", metrics.SecurityEventFailed, labels);
        AppendGauge(sb, "etlsql_security_event_stored_bytes",
            "Bytes stored by the local security-event outbox.", metrics.SecurityEventStoredBytes, labels);
        AppendGauge(sb, "etlsql_security_event_dropped",
            "Security events dropped by the local runtime sink.", metrics.SecurityEventDropped, labels);
        AppendGauge(sb, "etlsql_security_event_oldest_pending_age_seconds",
            "Age in seconds of the oldest pending local security-event outbox message.",
            metrics.SecurityEventOldestPendingAgeSeconds, labels);
        AppendGauge(sb, "etlsql_security_event_collector_configured",
            "Whether remote security-event collector delivery is configured.",
            metrics.SecurityEventCollectorConfigured ? 1 : 0, labels);
        AppendGauge(sb, "etlsql_security_event_collector_reachable",
            "Whether the remote security-event collector was reachable on the last known attempt; -1 means unknown.",
            metrics.SecurityEventCollectorReachable is null ? -1 : metrics.SecurityEventCollectorReachable.Value ? 1 : 0,
            labels);
        AppendAlertGauges(sb, metrics, alertConfig, labels);
        AppendRuntimeGauges(sb, labels);

        return sb.ToString();
    }

    private static void AppendRuntimeGauges(StringBuilder sb, IReadOnlyDictionary<string, string> labels)
    {
        var process = Process.GetCurrentProcess();
        process.Refresh();
        AppendGauge(sb, "etlsql_runtime_process_working_set_bytes",
            "Current process working set in bytes.", process.WorkingSet64, labels);
        AppendGauge(sb, "etlsql_runtime_process_private_memory_bytes",
            "Current process private memory in bytes.", process.PrivateMemorySize64, labels);
        AppendGauge(sb, "etlsql_runtime_gc_heap_bytes",
            "Approximate managed heap bytes reported by GC.GetTotalMemory.", GC.GetTotalMemory(forceFullCollection: false), labels);
        AppendGauge(sb, "etlsql_runtime_gc_collections_gen0_total",
            "Total generation 0 garbage collections since process start.", GC.CollectionCount(0), labels);
        AppendGauge(sb, "etlsql_runtime_gc_collections_gen1_total",
            "Total generation 1 garbage collections since process start.", GC.CollectionCount(1), labels);
        AppendGauge(sb, "etlsql_runtime_gc_collections_gen2_total",
            "Total generation 2 garbage collections since process start.", GC.CollectionCount(2), labels);
    }

    private static void AppendAlertGauges(
        StringBuilder sb,
        OperationalMetricsService.OperationalMetrics metrics,
        OperationalDigestConfig config,
        IReadOnlyDictionary<string, string> baseLabels)
    {
        foreach (var alert in OperationalMetricsDigest.BuildAlerts(metrics, config))
        {
            var labels = new Dictionary<string, string>(baseLabels)
            {
                ["severity"] = alert.Severity,
                ["alert_code"] = alert.Code,
                ["runbook"] = alert.Runbook
            };
            AppendGauge(sb, "etlsql_portal_operational_alert_active",
                "Active Portal operational alert signals. Labels identify severity, stable code, and runbook.",
                1,
                labels);
        }
    }

    private static void AppendGauge(
        StringBuilder sb,
        string name,
        string help,
        double value,
        IReadOnlyDictionary<string, string> labels)
    {
        sb.Append("# HELP ").Append(name).Append(' ').AppendLine(help);
        sb.Append("# TYPE ").Append(name).AppendLine(" gauge");
        sb.Append(name).Append(FormatLabels(labels)).Append(' ')
            .AppendLine(value.ToString("G17", CultureInfo.InvariantCulture));
    }

    private static string FormatLabels(IReadOnlyDictionary<string, string> labels) =>
        "{" + string.Join(",", labels.Select(label =>
            $"{label.Key}=\"{EscapeLabelValue(label.Value)}\"")) + "}";

    private static string EscapeLabelValue(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

}
