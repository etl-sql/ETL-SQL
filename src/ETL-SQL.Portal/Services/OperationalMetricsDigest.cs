using System.Text;
using static ETL_SQL.Portal.Services.OperationalMetricsService;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Pure composition of the operational-metrics digest email from an <see cref="OperationalMetrics"/>
/// snapshot and <see cref="OperationalDigestConfig"/>. Kept side-effect-free (no DB, SMTP, or clock) so
/// the alerting thresholds and body text are unit-testable without a running portal.
/// </summary>
public static class OperationalMetricsDigest
{
    public sealed record OperationalAlert(string Severity, string Code, string Message, string Runbook)
    {
        public override string ToString() => $"[{Severity}] {Code}: {Message} Runbook: {Runbook}";
    }

    public sealed record DigestContent(string Subject, string Body, IReadOnlyList<OperationalAlert> Alerts)
    {
        public bool HasAlerts => Alerts.Count > 0;
    }

    public static DigestContent Build(OperationalMetrics m, OperationalDigestConfig cfg)
    {
        var alerts = BuildAlerts(m, cfg);

        var subject = alerts.Count > 0
            ? $"[ETL-SQL Portal] Operational ALERT ({alerts.Count}) — {m.NodeId}"
            : $"[ETL-SQL Portal] Operational digest — {m.NodeId}";

        var body = new StringBuilder();
        if (alerts.Count > 0)
        {
            body.AppendLine("ALERTS:");
            foreach (var a in alerts)
                body.AppendLine("  * " + a);
            body.AppendLine();
        }

        body.AppendLine($"Portal node: {m.NodeId} ({m.Topology})");
        body.AppendLine($"Generated:   {m.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC");
        body.AppendLine();
        body.AppendLine("Executions:");
        body.AppendLine($"  Active {m.ActiveExecutions} / queued {m.QueuedExecutions} "
            + $"(cap {m.ExecutionCap}, per-user {m.PerUserExecutionCap})");
        body.AppendLine($"  Last {m.WindowHours}h: {m.RecentExecutions} run(s), "
            + $"{m.RecentExecutionFailures} failed ({FailureRate(m.RecentExecutionFailures, m.RecentExecutions):0.#}%)");
        body.AppendLine($"  Avg duration {m.AverageExecutionDurationMs:0} ms; "
            + $"avg queued age {m.AverageQueuedExecutionAgeSeconds:0}s");
        body.AppendLine();
        body.AppendLine("Subscriptions & delivery:");
        body.AppendLine($"  Active subscriptions {m.ActiveSubscriptions}; SMTP connections {m.SmtpConnections}");
        body.AppendLine($"  Last {m.WindowHours}h deliveries: {m.RecentDeliveries} sent, "
            + $"{m.RecentDeliveryFailures} failed");
        body.AppendLine();
        body.AppendLine("Storage:");
        body.AppendLine($"  Datasets {FormatBytes(m.DatasetStorageBytes)}; "
            + $"snapshots {FormatBytes(m.SnapshotStorageBytes)}");
        body.AppendLine($"  Stale snapshots {m.StaleSnapshots}; stale datasets {m.StaleDatasets}");
        body.AppendLine();
        body.AppendLine("Outboxes:");
        body.AppendLine($"  Audit pending {m.AuditOutboxPending}, failed {m.AuditOutboxFailed}, "
            + $"oldest pending {m.AuditOutboxOldestPendingAgeSeconds:0}s");
        body.AppendLine($"  Security events pending {m.SecurityEventPending}, failed {m.SecurityEventFailed}, "
            + $"oldest pending {m.SecurityEventOldestPendingAgeSeconds:0}s");
        body.AppendLine();
        body.AppendLine("Policy authority:");
        body.AppendLine($"  Active versions expiring {m.ActivePolicyVersionsExpiring}; "
            + $"expired {m.ActivePolicyVersionsExpired}");
        body.AppendLine($"  Signing health {(m.PolicyAuthorityHealthy ? "healthy" : "NOT healthy")}; "
            + $"client certificate expires {FormatTimestamp(m.ClientCertificateExpiresAtUtc)}");
        body.AppendLine();
        body.AppendLine("Health:");
        body.AppendLine($"  Database {(m.DatabaseConnectivityHealthy ? "reachable" : "NOT reachable")}; "
            + $"pool exhaustion suspected {(m.DatabasePoolExhaustionSuspected ? "yes" : "no")}");
        body.AppendLine($"  Unhealthy fleet nodes {m.UnhealthyFleetNodes}");
        body.AppendLine();
        body.AppendLine("Schema:");
        body.AppendLine($"  Applied {m.AppliedMigrations}, pending {m.PendingMigrations} "
            + $"({(m.SchemaUpToDate ? "up to date" : "NOT up to date")})");

        return new DigestContent(subject, body.ToString().TrimEnd(), alerts);
    }

    public static IReadOnlyList<OperationalAlert> BuildAlerts(OperationalMetrics m, OperationalDigestConfig cfg)
    {
        var alerts = new List<OperationalAlert>();

        var failureRate = FailureRate(m.RecentExecutionFailures, m.RecentExecutions);
        if (cfg.FailureRatePercentThreshold > 0
            && m.RecentExecutions > 0
            && failureRate >= cfg.FailureRatePercentThreshold)
        {
            alerts.Add(Alert("critical", "portal_execution_failure_rate",
                $"Execution failure rate {failureRate:0.#}% over the last {m.WindowHours}h "
                + $"({m.RecentExecutionFailures}/{m.RecentExecutions}) is at or above the "
                + $"{cfg.FailureRatePercentThreshold}% threshold.",
                cfg));
        }

        if (cfg.QueueDepthAlertThreshold > 0 && m.QueuedExecutions >= cfg.QueueDepthAlertThreshold)
        {
            alerts.Add(Alert("warning", "portal_execution_queue_depth",
                $"Execution queue depth {m.QueuedExecutions} is at or above the "
                + $"{cfg.QueueDepthAlertThreshold} backlog threshold (cap {m.ExecutionCap}).",
                cfg));
        }

        if (cfg.QueueAgeSecondsAlertThreshold > 0
            && m.AverageQueuedExecutionAgeSeconds >= cfg.QueueAgeSecondsAlertThreshold)
        {
            alerts.Add(Alert("warning", "portal_execution_queue_age",
                $"Average queued execution age {m.AverageQueuedExecutionAgeSeconds:0}s is at or above the "
                + $"{cfg.QueueAgeSecondsAlertThreshold}s threshold.",
                cfg));
        }

        var deliveryFailureRate = FailureRate(m.RecentDeliveryFailures, m.RecentDeliveries);
        if (cfg.DeliveryFailureRatePercentThreshold > 0
            && m.RecentDeliveries > 0
            && deliveryFailureRate >= cfg.DeliveryFailureRatePercentThreshold)
        {
            alerts.Add(Alert("warning", "portal_delivery_failure_rate",
                $"Subscription delivery failure rate {deliveryFailureRate:0.#}% over the last {m.WindowHours}h "
                + $"({m.RecentDeliveryFailures}/{m.RecentDeliveries}) is at or above the "
                + $"{cfg.DeliveryFailureRatePercentThreshold}% threshold.",
                cfg));
        }

        if (cfg.AlertOnPendingMigrations && m.PendingMigrations > 0)
        {
            alerts.Add(Alert("warning", "portal_schema_pending_migrations",
                $"{m.PendingMigrations} database migration(s) are pending; the catalog schema is not fully upgraded.",
                cfg));
        }

        if (cfg.AuditOutboxPendingAlertThreshold > 0
            && m.AuditOutboxPending >= cfg.AuditOutboxPendingAlertThreshold)
        {
            alerts.Add(Alert("critical", "portal_audit_outbox_backlog",
                $"Audit outbox pending backlog {m.AuditOutboxPending} is at or above the "
                + $"{cfg.AuditOutboxPendingAlertThreshold} threshold.",
                cfg));
        }

        if (cfg.AuditOutboxAgeSecondsAlertThreshold > 0
            && m.AuditOutboxOldestPendingAgeSeconds >= cfg.AuditOutboxAgeSecondsAlertThreshold)
        {
            alerts.Add(Alert("critical", "portal_audit_outbox_age",
                $"Oldest pending audit outbox message age {m.AuditOutboxOldestPendingAgeSeconds:0}s is at or above the "
                + $"{cfg.AuditOutboxAgeSecondsAlertThreshold}s threshold.",
                cfg));
        }

        if (cfg.SecurityEventPendingAlertThreshold > 0
            && m.SecurityEventPending >= cfg.SecurityEventPendingAlertThreshold)
        {
            alerts.Add(Alert("warning", "security_event_outbox_backlog",
                $"Security-event pending backlog {m.SecurityEventPending} is at or above the "
                + $"{cfg.SecurityEventPendingAlertThreshold} threshold.",
                cfg));
        }

        if (cfg.SecurityEventAgeSecondsAlertThreshold > 0
            && m.SecurityEventOldestPendingAgeSeconds >= cfg.SecurityEventAgeSecondsAlertThreshold)
        {
            alerts.Add(Alert("warning", "security_event_outbox_age",
                $"Oldest pending security-event age {m.SecurityEventOldestPendingAgeSeconds:0}s is at or above the "
                + $"{cfg.SecurityEventAgeSecondsAlertThreshold}s threshold.",
                cfg));
        }

        if (cfg.DatasetStorageBytesAlertThreshold > 0
            && m.DatasetStorageBytes >= cfg.DatasetStorageBytesAlertThreshold)
        {
            alerts.Add(Alert("warning", "portal_dataset_storage_bytes",
                $"Dataset storage {FormatBytes(m.DatasetStorageBytes)} is at or above the "
                + $"{FormatBytes(cfg.DatasetStorageBytesAlertThreshold)} threshold.",
                cfg));
        }

        if (cfg.SnapshotStorageBytesAlertThreshold > 0
            && m.SnapshotStorageBytes >= cfg.SnapshotStorageBytesAlertThreshold)
        {
            alerts.Add(Alert("warning", "portal_snapshot_storage_bytes",
                $"Snapshot storage {FormatBytes(m.SnapshotStorageBytes)} is at or above the "
                + $"{FormatBytes(cfg.SnapshotStorageBytesAlertThreshold)} threshold.",
                cfg));
        }

        if (cfg.SnapshotFreshnessHours > 0 && m.StaleSnapshots > 0)
        {
            alerts.Add(Alert("warning", "portal_stale_snapshots",
                $"{m.StaleSnapshots} report snapshot(s) are older than the configured "
                + $"{cfg.SnapshotFreshnessHours}h freshness objective.",
                cfg));
        }

        if (cfg.DatasetFreshnessHours > 0 && m.StaleDatasets > 0)
        {
            alerts.Add(Alert("warning", "portal_stale_datasets",
                $"{m.StaleDatasets} dataset(s) are older than the configured "
                + $"{cfg.DatasetFreshnessHours}h freshness objective.",
                cfg));
        }

        if (cfg.PolicyVersionExpiryWarningHours > 0 && m.ActivePolicyVersionsExpired > 0)
        {
            alerts.Add(Alert("critical", "portal_policy_version_expired",
                $"{m.ActivePolicyVersionsExpired} active policy version(s) are expired.",
                cfg));
        }

        if (cfg.PolicyVersionExpiryWarningHours > 0 && m.ActivePolicyVersionsExpiring > 0)
        {
            alerts.Add(Alert("warning", "portal_policy_version_expiring",
                $"{m.ActivePolicyVersionsExpiring} active policy version(s) expire within "
                + $"{cfg.PolicyVersionExpiryWarningHours}h.",
                cfg));
        }

        if (cfg.AlertOnPolicyAuthorityUnavailable && !m.PolicyAuthorityHealthy)
        {
            alerts.Add(Alert("critical", "portal_policy_signature_unavailable",
                "Policy-authority signing health is degraded or unavailable; policy publication and emergency rollback may fail.",
                cfg));
        }

        if (cfg.CertificateExpiryWarningHours > 0
            && m.ClientCertificateExpiresAtUtc is { } certExpiry
            && certExpiry <= DateTimeOffset.UtcNow.AddHours(cfg.CertificateExpiryWarningHours))
        {
            var severity = certExpiry <= DateTimeOffset.UtcNow ? "critical" : "warning";
            alerts.Add(Alert(severity, "portal_client_certificate_expiry",
                certExpiry <= DateTimeOffset.UtcNow
                    ? "The enrolled client certificate is expired."
                    : $"The enrolled client certificate expires within {cfg.CertificateExpiryWarningHours}h.",
                cfg));
        }

        if (cfg.AlertOnDatabaseConnectivityFailure && !m.DatabaseConnectivityHealthy)
        {
            alerts.Add(Alert("critical", "portal_database_connectivity",
                "Portal database health check is unhealthy; the node may be unsafe to serve traffic.",
                cfg));
        }

        if (cfg.AlertOnDatabasePoolExhaustion && m.DatabasePoolExhaustionSuspected)
        {
            alerts.Add(Alert("critical", "portal_database_pool_exhaustion",
                "Portal database diagnostics indicate connection pool exhaustion or timeout pressure.",
                cfg));
        }

        if (cfg.AlertOnUnhealthyFleetNodes && m.UnhealthyFleetNodes > 0)
        {
            alerts.Add(Alert("warning", "portal_unhealthy_fleet_nodes",
                $"{m.UnhealthyFleetNodes} fleet node(s) report degraded or unhealthy readiness.",
                cfg));
        }

        return alerts;
    }

    private static OperationalAlert Alert(string severity, string code, string message, OperationalDigestConfig cfg) =>
        new(severity, code, message, BuildRunbook(cfg, code));

    private static string BuildRunbook(OperationalDigestConfig cfg, string code)
    {
        var baseUri = string.IsNullOrWhiteSpace(cfg.RunbookBaseUri)
            ? "docs/architecture/decisions/alerting-service-objectives.md"
            : cfg.RunbookBaseUri.Trim();
        return baseUri.Contains('#', StringComparison.Ordinal)
            ? baseUri
            : $"{baseUri}#{code.Replace('_', '-')}";
    }

    private static double FailureRate(int failures, int total) =>
        total > 0 ? failures * 100.0 / total : 0.0;

    private static string FormatTimestamp(DateTimeOffset? value) =>
        value is null ? "not configured" : value.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:0.#} {units[unit]}";
    }
}
