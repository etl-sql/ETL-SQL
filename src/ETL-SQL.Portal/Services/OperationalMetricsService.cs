using ETL_SQL.Core.Governance;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// P2.8: a point-in-time operational snapshot for a multi-user deployment — active executions and
/// queue depth, recent execution and subscription-delivery failure rates (from the durable
/// PortalExecutionJobs and SubscriptionDelivery ledgers), and dataset/snapshot storage usage.
/// </summary>
public sealed class OperationalMetricsService(
    PortalDbContext db,
    PortalConfig config,
    PortalNodeIdentity? nodeIdentity = null,
    HealthCheckService? healthChecks = null,
    PortalStorageUsageSampler? storageUsage = null,
    PortalTopologyReadinessService? topologyReadiness = null)
{
    /// <summary>The window over which failure rates are computed.</summary>
    public static readonly TimeSpan FailureWindow = TimeSpan.FromHours(24);

    public sealed record OperationalMetrics(
        int ActiveExecutions,
        int QueuedExecutions,
        int ExecutionCap,
        int PerUserExecutionCap,
        string Topology,
        string NodeId,
        string AffinityCookieName,
        int RecentExecutions,
        int RecentExecutionFailures,
        int RecentDeliveries,
        int RecentDeliveryFailures,
        long DatasetStorageBytes,
        long SnapshotStorageBytes,
        int StaleSnapshots,
        int StaleDatasets,
        int ActivePolicyVersionsExpiring,
        int ActivePolicyVersionsExpired,
        int ActiveSubscriptions,
        int SmtpConnections,
        int AppliedMigrations,
        int PendingMigrations,
        string? LastAppliedMigration,
        bool SchemaUpToDate,
        int AuditOutboxPending,
        int AuditOutboxFailed,
        long AuditOutboxPendingBytes,
        double AuditOutboxOldestPendingAgeSeconds,
        int SecurityEventPending,
        int SecurityEventFailed,
        long SecurityEventStoredBytes,
        long SecurityEventDropped,
        double SecurityEventOldestPendingAgeSeconds,
        bool SecurityEventCollectorConfigured,
        bool? SecurityEventCollectorReachable,
        bool DatabaseConnectivityHealthy,
        bool DatabasePoolExhaustionSuspected,
        bool PolicyAuthorityHealthy,
        DateTimeOffset? ClientCertificateExpiresAtUtc,
        int UnhealthyFleetNodes,
        double AverageExecutionDurationMs,
        double AverageQueuedExecutionAgeSeconds,
        IReadOnlyList<HourlyExecutionLoad> HourlyExecutionLoad,
        DateTime GeneratedAt,
        int WindowHours);

    public sealed record HourlyExecutionLoad(
        DateTime HourUtc,
        int Executions,
        int Failures,
        long RowsProcessed,
        long PeakMemoryBytes);

    public async Task<OperationalMetrics> GetAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var since = now - FailureWindow;

        var activeExecutions = await db.PortalExecutionJobs.CountAsync(j => j.Status == "Running", ct);
        var queuedExecutions = await db.PortalExecutionJobs.CountAsync(j => j.Status == "Pending", ct);

        var queuedJobs = await db.PortalExecutionJobs
            .AsNoTracking()
            .Where(j => j.Status == "Pending")
            .Select(j => j.CreatedAt)
            .ToListAsync(ct);
        var recentExecutions = await db.PortalExecutionJobs
            .CountAsync(j => j.CompletedAt != null && j.CompletedAt >= since, ct);
        var recentExecutionFailures = await db.PortalExecutionJobs
            .CountAsync(j => j.CompletedAt >= since && (j.Status == "Failed" || j.Status == "Cancelled"), ct);
        var hourlyRows = await db.PortalExecutionJobs
            .AsNoTracking()
            .Where(j => j.CompletedAt != null && j.CompletedAt >= since)
            .GroupBy(j => new
            {
                j.CompletedAt!.Value.Year,
                j.CompletedAt.Value.Month,
                j.CompletedAt.Value.Day,
                j.CompletedAt.Value.Hour
            })
            .Select(group => new
            {
                group.Key.Year,
                group.Key.Month,
                group.Key.Day,
                group.Key.Hour,
                Executions = group.Count(),
                Failures = group.Count(j => j.Status == "Failed" || j.Status == "Cancelled"),
                RowsProcessed = group.Sum(j => j.RowsProcessed),
                PeakMemoryBytes = group.Max(j => j.PeakMemoryBytes)
            })
            .ToListAsync(ct);
        var hourlyExecutionLoad = BuildHourlyExecutionLoad(since, now, hourlyRows.Select(row =>
            new ExecutionLoadBucket(
                new DateTime(row.Year, row.Month, row.Day, row.Hour, 0, 0, DateTimeKind.Utc),
                row.Executions,
                row.Failures,
                row.RowsProcessed,
                row.PeakMemoryBytes)));
        var durationRows = await db.PortalExecutionJobs
            .AsNoTracking()
            .Where(j => j.CompletedAt != null && j.CompletedAt >= since && j.StartedAt != null)
            .Select(j => new { j.StartedAt, j.CompletedAt })
            .ToListAsync(ct);
        var completedDurations = durationRows
            .Select(j => (j.CompletedAt!.Value - j.StartedAt!.Value).TotalMilliseconds)
            .Where(ms => ms >= 0)
            .ToList();
        var averageExecutionDurationMs = completedDurations.Count == 0
            ? 0
            : completedDurations.Average();
        var averageQueuedExecutionAgeSeconds = queuedJobs.Count == 0
            ? 0
            : queuedJobs.Select(createdAt => Math.Max(0, (now - createdAt).TotalSeconds)).Average();

        var recentDeliveries = await db.SubscriptionDeliveries
            .CountAsync(d => d.CompletedAt != null && d.CompletedAt >= since, ct);
        var recentDeliveryFailures = await db.SubscriptionDeliveries
            .CountAsync(d => d.CompletedAt >= since && (d.Outcome == "Failed" || d.Outcome == "Denied"), ct);

        var activeSubscriptions = await db.Subscriptions.CountAsync(s => s.IsActive, ct);
        var smtpConnections = await db.SmtpConnections.CountAsync(ct);
        var staleSnapshots = await CountStaleSnapshotsAsync(now, ct);
        var staleDatasets = await CountStaleDatasetsAsync(now, ct);
        var policyExpiry = await CountPolicyExpiryAsync(now, ct);

        // Schema migration status: an operator can confirm the catalog is fully migrated after an
        // upgrade (PendingMigrations == 0) without shell access.
        var appliedMigrations = (await db.Database.GetAppliedMigrationsAsync(ct)).ToList();
        var pendingMigrations = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
        var auditPending = await db.AuditOutboxMessages
            .Where(x => x.Status == "Pending")
            .Select(x => new { x.CreatedAt, x.PayloadJson })
            .ToListAsync(ct);
        var auditFailed = await db.AuditOutboxMessages.CountAsync(x => x.Status == "Failed", ct);
        var securityEvents = SecurityEventRuntime.GetDiagnostics();
        var health = await GetHealthSummaryAsync(ct);
        var clientCertificateExpiresAtUtc = TryGetClientCertificateExpiry(
            new EnterpriseEnrollmentStore().GetStatus().Enrollment?.ClientCertificateThumbprint);

        return new OperationalMetrics(
            activeExecutions,
            queuedExecutions,
            config.Resources.MaxConcurrentReportExecutions,
            config.Resources.MaxConcurrentExecutionsPerUser,
            await ResolveTopologyAsync(ct),
            nodeIdentity?.NodeId ?? Environment.MachineName,
            config.LoadBalancer.SessionAffinityCookieName,
            recentExecutions,
            recentExecutionFailures,
            recentDeliveries,
            recentDeliveryFailures,
            storageUsage?.DatasetStorageBytes ?? PortalStorageUsageSampler.MeasureDirectory(config.DatasetRootPath),
            storageUsage?.SnapshotStorageBytes ?? PortalStorageUsageSampler.MeasureDirectory(config.SnapshotDirectory),
            staleSnapshots,
            staleDatasets,
            policyExpiry.Expiring,
            policyExpiry.Expired,
            activeSubscriptions,
            smtpConnections,
            appliedMigrations.Count,
            pendingMigrations.Count,
            appliedMigrations.LastOrDefault(),
            pendingMigrations.Count == 0,
            auditPending.Count,
            auditFailed,
            auditPending.Sum(x => (long)x.PayloadJson.Length),
            auditPending.Count == 0 ? 0 : auditPending.Max(x => Math.Max(0, (now - x.CreatedAt).TotalSeconds)),
            securityEvents.PendingCount,
            securityEvents.FailedCount,
            securityEvents.StoredBytes,
            securityEvents.DroppedCount,
            securityEvents.OldestPendingUtc is null
                ? 0
                : Math.Max(0, (DateTimeOffset.UtcNow - securityEvents.OldestPendingUtc.Value).TotalSeconds),
            securityEvents.CollectorConfigured,
            securityEvents.CollectorReachable,
            health.DatabaseConnectivityHealthy,
            health.DatabasePoolExhaustionSuspected,
            health.PolicyAuthorityHealthy,
            clientCertificateExpiresAtUtc,
            health.UnhealthyFleetNodes,
            averageExecutionDurationMs,
            averageQueuedExecutionAgeSeconds,
            hourlyExecutionLoad,
            DateTime.UtcNow,
            (int)FailureWindow.TotalHours);
    }

    private async Task<string> ResolveTopologyAsync(CancellationToken ct)
    {
        if (topologyReadiness is not null)
        {
            try
            {
                return (await topologyReadiness.CheckAsync(ct)).Mode;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Fall back to config-derived topology below.
            }
        }

        var expected = config.Topology.ExpectedMode?.Trim();
        if (!string.IsNullOrWhiteSpace(expected)
            && !string.Equals(expected, "Auto", StringComparison.OrdinalIgnoreCase))
            return expected;

        return string.Equals(config.Database.Provider, "Postgres", StringComparison.OrdinalIgnoreCase)
            ? "HighAvailability"
            : "Standalone";
    }

    private static IReadOnlyList<HourlyExecutionLoad> BuildHourlyExecutionLoad(
        DateTime since,
        DateTime now,
        IEnumerable<ExecutionLoadBucket> rows)
    {
        var buckets = rows
            .ToDictionary(row => row.HourUtc);

        var start = TruncateToHour(since);
        var end = TruncateToHour(now);
        var result = new List<HourlyExecutionLoad>();
        for (var hour = start; hour <= end; hour = hour.AddHours(1))
        {
            if (buckets.TryGetValue(hour, out var bucket))
            {
                result.Add(new HourlyExecutionLoad(
                    hour,
                    bucket.Executions,
                    bucket.Failures,
                    bucket.RowsProcessed,
                    bucket.PeakMemoryBytes));
            }
            else
            {
                result.Add(new HourlyExecutionLoad(hour, 0, 0, 0, 0));
            }
        }

        return result;
    }

    private static DateTime TruncateToHour(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        return new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, DateTimeKind.Utc);
    }

    private async Task<int> CountStaleSnapshotsAsync(DateTime now, CancellationToken ct)
    {
        if (config.OperationalDigest.SnapshotFreshnessHours <= 0)
            return 0;

        var cutoff = now.AddHours(-config.OperationalDigest.SnapshotFreshnessHours);
        return await db.Reports
            .AsNoTracking()
            .Where(r => !r.IsDeleted
                && (r.LastRefreshCompletedAt == null || r.LastRefreshCompletedAt < cutoff))
            .CountAsync(ct);
    }

    private async Task<int> CountStaleDatasetsAsync(DateTime now, CancellationToken ct)
    {
        if (config.OperationalDigest.DatasetFreshnessHours <= 0)
            return 0;

        var cutoff = now.AddHours(-config.OperationalDigest.DatasetFreshnessHours);
        return await db.Datasets
            .AsNoTracking()
            .Where(d => d.LastRefresh == null || d.LastRefresh < cutoff)
            .CountAsync(ct);
    }

    private async Task<PolicyExpiryCounts> CountPolicyExpiryAsync(DateTime now, CancellationToken ct)
    {
        if (config.OperationalDigest.PolicyVersionExpiryWarningHours <= 0)
            return new PolicyExpiryCounts(0, 0);

        var nowOffset = new DateTimeOffset(now, TimeSpan.Zero);
        var warningCutoff = nowOffset.AddHours(config.OperationalDigest.PolicyVersionExpiryWarningHours);
        var activeVersions = await db.PolicyVersions
            .AsNoTracking()
            .Where(p => p.RolloutState == nameof(PolicyRolloutState.Active))
            .Select(p => p.ExpiresAtUtc)
            .ToListAsync(ct);

        var expired = activeVersions.Count(expiresAt => expiresAt <= nowOffset);
        var expiring = activeVersions.Count(expiresAt => expiresAt > nowOffset && expiresAt <= warningCutoff);
        return new PolicyExpiryCounts(expiring, expired);
    }


    private sealed record ExecutionLoadBucket(
        DateTime HourUtc,
        int Executions,
        int Failures,
        long RowsProcessed,
        long PeakMemoryBytes);

    private sealed record PolicyExpiryCounts(int Expiring, int Expired);

    private sealed record HealthSummary(
        bool DatabaseConnectivityHealthy,
        bool DatabasePoolExhaustionSuspected,
        bool PolicyAuthorityHealthy,
        int UnhealthyFleetNodes);

    private async Task<HealthSummary> GetHealthSummaryAsync(CancellationToken ct)
    {
        if (healthChecks is null)
        {
            try
            {
                var canConnect = await db.Database.CanConnectAsync(ct);
                return new HealthSummary(canConnect, false, true, canConnect ? 0 : 1);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new HealthSummary(false, IsPoolExhaustion(ex), true, 1);
            }
        }

        try
        {
            var report = await healthChecks.CheckHealthAsync(ct);
            var databaseHealthy = !TryGetHealthEntry(report, "db", out var dbEntry)
                || dbEntry.Status != HealthStatus.Unhealthy;
            var poolExhaustion = TryGetHealthEntry(report, "db", out dbEntry)
                && (IsPoolExhaustion(dbEntry.Exception) || IsPoolExhaustion(dbEntry.Description));
            var policyHealthy = !TryGetHealthEntry(report, "policy-authority", out var policyEntry)
                || (policyEntry.Status != HealthStatus.Unhealthy && policyEntry.Status != HealthStatus.Degraded);
            var unhealthyFleetNodes = report.Status == HealthStatus.Healthy ? 0 : 1;

            return new HealthSummary(databaseHealthy, poolExhaustion, policyHealthy, unhealthyFleetNodes);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new HealthSummary(false, IsPoolExhaustion(ex), true, 1);
        }
    }

    private static bool IsPoolExhaustion(Exception? ex) =>
        ex is not null && (IsPoolExhaustion(ex.Message) || IsPoolExhaustion(ex.InnerException));

    private static bool TryGetHealthEntry(
        HealthReport report,
        string name,
        out HealthReportEntry entry)
    {
        foreach (var candidate in report.Entries)
        {
            if (string.Equals(candidate.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                entry = candidate.Value;
                return true;
            }
        }

        entry = default;
        return false;
    }

    private static bool IsPoolExhaustion(string? message) =>
        !string.IsNullOrWhiteSpace(message)
        && message.Contains("pool", StringComparison.OrdinalIgnoreCase)
        && (message.Contains("timeout", StringComparison.OrdinalIgnoreCase)
            || message.Contains("exhaust", StringComparison.OrdinalIgnoreCase)
            || message.Contains("maximum", StringComparison.OrdinalIgnoreCase));

    private static DateTimeOffset? TryGetClientCertificateExpiry(string? thumbprint)
    {
        if (string.IsNullOrWhiteSpace(thumbprint))
            return null;

        var normalized = thumbprint.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
        foreach (var location in new[] { StoreLocation.LocalMachine, StoreLocation.CurrentUser })
        {
            try
            {
                using var store = new X509Store(StoreName.My, location);
                store.Open(OpenFlags.ReadOnly);
                foreach (var cert in store.Certificates)
                {
                    using (cert)
                    {
                        if (string.Equals(cert.Thumbprint, normalized, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(cert.GetCertHashString(HashAlgorithmName.SHA256), normalized,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            return new DateTimeOffset(cert.NotAfter.ToUniversalTime(), TimeSpan.Zero);
                        }
                    }
                }
            }
            catch
            {
                // Operational metrics report absence rather than leaking certificate-store errors.
            }
        }

        return null;
    }

}
