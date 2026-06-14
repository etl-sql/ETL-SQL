using ETL_SQL.ReportPortal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.ReportPortal.Services;

/// <summary>
/// P2.8: a point-in-time operational snapshot for a multi-user deployment — active executions and
/// queue depth, recent execution and subscription-delivery failure rates (from the durable
/// PortalExecutionJobs and SubscriptionDelivery ledgers), and dataset/snapshot storage usage on disk.
/// </summary>
public sealed class OperationalMetricsService(PortalDbContext db, PortalConfig config)
{
    /// <summary>The window over which failure rates are computed.</summary>
    public static readonly TimeSpan FailureWindow = TimeSpan.FromHours(24);

    public sealed record OperationalMetrics(
        int ActiveExecutions,
        int QueuedExecutions,
        int ExecutionCap,
        int PerUserExecutionCap,
        string Topology,
        int RecentExecutions,
        int RecentExecutionFailures,
        int RecentDeliveries,
        int RecentDeliveryFailures,
        long DatasetStorageBytes,
        long SnapshotStorageBytes,
        int ActiveSubscriptions,
        int SmtpConnections,
        DateTime GeneratedAt,
        int WindowHours);

    public async Task<OperationalMetrics> GetAsync(CancellationToken ct = default)
    {
        var since = DateTime.UtcNow - FailureWindow;

        var activeExecutions = await db.PortalExecutionJobs.CountAsync(j => j.Status == "Running", ct);
        var queuedExecutions = await db.PortalExecutionJobs.CountAsync(j => j.Status == "Pending", ct);

        var recentExecutions = await db.PortalExecutionJobs
            .CountAsync(j => j.CompletedAt != null && j.CompletedAt >= since, ct);
        var recentExecutionFailures = await db.PortalExecutionJobs
            .CountAsync(j => j.CompletedAt >= since && (j.Status == "Failed" || j.Status == "Cancelled"), ct);

        var recentDeliveries = await db.SubscriptionDeliveries
            .CountAsync(d => d.CompletedAt != null && d.CompletedAt >= since, ct);
        var recentDeliveryFailures = await db.SubscriptionDeliveries
            .CountAsync(d => d.CompletedAt >= since && (d.Outcome == "Failed" || d.Outcome == "Denied"), ct);

        var activeSubscriptions = await db.Subscriptions.CountAsync(s => s.IsActive, ct);
        var smtpConnections = await db.SmtpConnections.CountAsync(ct);

        return new OperationalMetrics(
            activeExecutions,
            queuedExecutions,
            config.Resources.MaxConcurrentReportExecutions,
            config.Resources.MaxConcurrentExecutionsPerUser,
            "single-active-instance",
            recentExecutions,
            recentExecutionFailures,
            recentDeliveries,
            recentDeliveryFailures,
            DirectorySizeBytes(config.DatasetRootPath),
            DirectorySizeBytes(config.SnapshotDirectory),
            activeSubscriptions,
            smtpConnections,
            DateTime.UtcNow,
            (int)FailureWindow.TotalHours);
    }

    /// <summary>Total size of regular files directly under a storage root; 0 when absent.</summary>
    private static long DirectorySizeBytes(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return 0;
        try
        {
            long total = 0;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(file).Length; }
                catch { /* a file vanishing mid-scan must not fail metrics */ }
            }
            return total;
        }
        catch
        {
            return 0;
        }
    }
}
