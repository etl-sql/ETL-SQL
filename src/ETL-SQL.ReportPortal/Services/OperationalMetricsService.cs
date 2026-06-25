using ETL_SQL.ReportPortal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.ReportPortal.Services;

/// <summary>
/// P2.8: a point-in-time operational snapshot for a multi-user deployment — active executions and
/// queue depth, recent execution and subscription-delivery failure rates (from the durable
/// PortalExecutionJobs and SubscriptionDelivery ledgers), and dataset/snapshot storage usage.
/// </summary>
public sealed class OperationalMetricsService(
    PortalDbContext db,
    PortalConfig config,
    PortalNodeIdentity? nodeIdentity = null)
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
        int ActiveSubscriptions,
        int SmtpConnections,
        int AppliedMigrations,
        int PendingMigrations,
        string? LastAppliedMigration,
        bool SchemaUpToDate,
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
        var recentExecutionRows = await db.PortalExecutionJobs
            .AsNoTracking()
            .Where(j => j.CompletedAt != null && j.CompletedAt >= since)
            .Select(j => new
            {
                j.CompletedAt,
                j.StartedAt,
                j.Status,
                j.RowsProcessed,
                j.PeakMemoryBytes
            })
            .ToListAsync(ct);
        var hourlyExecutionLoad = BuildHourlyExecutionLoad(since, now, recentExecutionRows
            .Where(j => j.CompletedAt is not null)
            .Select(j => new ExecutionLoadRow(
                j.CompletedAt!.Value,
                j.Status,
                j.RowsProcessed,
                j.PeakMemoryBytes)));
        var completedDurations = recentExecutionRows
            .Where(j => j.CompletedAt is not null && j.StartedAt is not null)
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

        // Schema migration status: an operator can confirm the catalog is fully migrated after an
        // upgrade (PendingMigrations == 0) without shell access.
        var appliedMigrations = (await db.Database.GetAppliedMigrationsAsync(ct)).ToList();
        var pendingMigrations = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();

        return new OperationalMetrics(
            activeExecutions,
            queuedExecutions,
            config.Resources.MaxConcurrentReportExecutions,
            config.Resources.MaxConcurrentExecutionsPerUser,
            "shared-state-ha",
            nodeIdentity?.NodeId ?? Environment.MachineName,
            config.LoadBalancer.SessionAffinityCookieName,
            recentExecutions,
            recentExecutionFailures,
            recentDeliveries,
            recentDeliveryFailures,
            DirectorySizeBytes(config.DatasetRootPath),
            DirectorySizeBytes(config.SnapshotDirectory),
            activeSubscriptions,
            smtpConnections,
            appliedMigrations.Count,
            pendingMigrations.Count,
            appliedMigrations.LastOrDefault(),
            pendingMigrations.Count == 0,
            averageExecutionDurationMs,
            averageQueuedExecutionAgeSeconds,
            hourlyExecutionLoad,
            DateTime.UtcNow,
            (int)FailureWindow.TotalHours);
    }

    private static IReadOnlyList<HourlyExecutionLoad> BuildHourlyExecutionLoad(
        DateTime since,
        DateTime now,
        IEnumerable<ExecutionLoadRow> rows)
    {
        var buckets = rows
            .GroupBy(row => TruncateToHour(row.CompletedAt))
            .ToDictionary(
                group => group.Key,
                group => new
                {
                    Executions = group.Count(),
                    Failures = group.Count(row => row.Status is "Failed" or "Cancelled"),
                    RowsProcessed = group.Sum(row => row.RowsProcessed),
                    PeakMemoryBytes = group.Max(row => row.PeakMemoryBytes)
                });

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


    private sealed record ExecutionLoadRow(
        DateTime CompletedAt,
        string Status,
        long RowsProcessed,
        long PeakMemoryBytes);

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
