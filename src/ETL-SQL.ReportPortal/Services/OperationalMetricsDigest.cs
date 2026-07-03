using System.Text;
using static ETL_SQL.ReportPortal.Services.OperationalMetricsService;

namespace ETL_SQL.ReportPortal.Services;

/// <summary>
/// Pure composition of the operational-metrics digest email from an <see cref="OperationalMetrics"/>
/// snapshot and <see cref="OperationalDigestConfig"/>. Kept side-effect-free (no DB, SMTP, or clock) so
/// the alerting thresholds and body text are unit-testable without a running portal.
/// </summary>
public static class OperationalMetricsDigest
{
    public sealed record DigestContent(string Subject, string Body, IReadOnlyList<string> Alerts)
    {
        public bool HasAlerts => Alerts.Count > 0;
    }

    public static DigestContent Build(OperationalMetrics m, OperationalDigestConfig cfg)
    {
        var alerts = new List<string>();

        var failureRate = m.RecentExecutions > 0
            ? m.RecentExecutionFailures * 100.0 / m.RecentExecutions
            : 0.0;
        if (cfg.FailureRatePercentThreshold > 0
            && m.RecentExecutions > 0
            && failureRate >= cfg.FailureRatePercentThreshold)
        {
            alerts.Add($"Execution failure rate {failureRate:0.#}% over the last {m.WindowHours}h "
                + $"({m.RecentExecutionFailures}/{m.RecentExecutions}) is at or above the "
                + $"{cfg.FailureRatePercentThreshold}% threshold.");
        }

        if (cfg.QueueDepthAlertThreshold > 0 && m.QueuedExecutions >= cfg.QueueDepthAlertThreshold)
        {
            alerts.Add($"Execution queue depth {m.QueuedExecutions} is at or above the "
                + $"{cfg.QueueDepthAlertThreshold} backlog threshold (cap {m.ExecutionCap}).");
        }

        if (cfg.AlertOnPendingMigrations && m.PendingMigrations > 0)
        {
            alerts.Add($"{m.PendingMigrations} database migration(s) are pending — the catalog schema is "
                + "not fully upgraded.");
        }

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
            + $"{m.RecentExecutionFailures} failed ({failureRate:0.#}%)");
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
        body.AppendLine();
        body.AppendLine("Schema:");
        body.AppendLine($"  Applied {m.AppliedMigrations}, pending {m.PendingMigrations} "
            + $"({(m.SchemaUpToDate ? "up to date" : "NOT up to date")})");

        return new DigestContent(subject, body.ToString().TrimEnd(), alerts);
    }

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
