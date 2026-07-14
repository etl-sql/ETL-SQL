using System.Text;
using ETL_SQL.Core.Data;
using ETL_SQL.ReportPortal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.ReportPortal.Services;

/// <summary>
/// Native replacement for samples/admin_operations/capacity_report.etlsql: emails a periodic
/// capacity summary — worst-point host metrics per node (free disk, memory, CPU) plus scheduled-job
/// run/failure counts over the lookback window. Always sends when enabled.
/// </summary>
public sealed class CapacityReportAdminService(
    IServiceScopeFactory scopeFactory,
    PortalConfig config,
    IClusterLockStore lockStore,
    ILogger<CapacityReportAdminService> log)
    : AdminDigestServiceBase(scopeFactory, config, lockStore, log)
{
    public override string ServiceName => "capacity-report";

    protected override AdminServiceScheduleConfig Schedule => Config.AdminServices.CapacityReport;

    protected override async Task<AdminDigestContent?> BuildAsync(IServiceProvider scope, CancellationToken ct)
    {
        var cfg = Config.AdminServices.CapacityReport;
        var since = DateTime.UtcNow.AddHours(-Math.Max(1, cfg.LookbackHours));
        var hostMetrics = scope.GetRequiredService<IHostMetricsStore>();
        var jobHistory = scope.GetRequiredService<IJobHistoryStore>();

        var samples = await hostMetrics.GetHostMetricsAsync(nodeId: null, since, limit: 10000);
        var perNode = samples
            .GroupBy(s => s.NodeId, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new
            {
                NodeId = g.Key,
                MinStateDiskFreeMB = g.Min(s => s.StateDiskFreeBytes) / (1024 * 1024),
                MinSpillDiskFreeMB = g.Min(s => s.SpillDiskFreeBytes) / (1024 * 1024),
                MaxMemoryLoadPercent = g.Max(s => s.MemoryLoadPercent),
                P95MemoryLoadPercent = Percentile(g.Select(s => s.MemoryLoadPercent), 0.95),
                MaxCpuPercent = g.Max(EffectiveCpuPercent),
                P95CpuPercent = Percentile(g.Select(EffectiveCpuPercent), 0.95),
                Samples = g.Count()
            })
            .ToList();

        var history = (await jobHistory.GetCompletedHistoryAsync(since, DateTime.UtcNow, limit: 2000)).ToList();
        var failures = history.Count(h =>
            !string.Equals(h.Status, "SUCCESS", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(h.Status, "RUNNING", StringComparison.OrdinalIgnoreCase));
        var completedDurationsMs = history
            .Where(h => h.EndTime is not null)
            .Select(h => Math.Max(0, (h.EndTime!.Value - h.StartTime).TotalMilliseconds))
            .ToList();
        var p95DurationMs = Percentile(completedDurationsMs, 0.95);
        var maxPeakMemoryMB = history.Count == 0 ? 0 : history.Max(h => h.PeakMemoryBytes) / (1024 * 1024);
        var p95PeakMemoryMB = Percentile(history.Select(h => h.PeakMemoryBytes / (1024.0 * 1024.0)), 0.95);
        var p95CpuSeconds = Percentile(history.Select(h => h.CpuTimeSeconds), 0.95);
        var failureRate = history.Count == 0 ? 0 : failures * 100.0 / history.Count;
        var scheduledJobBreakdown = history
            .GroupBy(h => h.JobName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new WorkloadBreakdown(
                g.Key,
                g.Count(),
                g.Count(IsFailure),
                g.Sum(h => h.RowsProcessed),
                g.Max(h => h.PeakMemoryBytes) / (1024 * 1024),
                Percentile(g.Where(h => h.EndTime is not null)
                    .Select(h => Math.Max(0, (h.EndTime!.Value - h.StartTime).TotalMilliseconds)), 0.95)))
            .OrderByDescending(w => w.Runs)
            .ThenBy(w => w.Key, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

        var db = scope.GetRequiredService<PortalDbContext>();
        var portalRows = await db.PortalExecutionJobs
            .AsNoTracking()
            .Where(j => j.CompletedAt != null && j.CompletedAt >= since)
            .Select(j => new
            {
                j.Kind,
                j.ReportId,
                j.UserId,
                j.ActorType,
                j.ActorId,
                j.Status,
                j.RowsProcessed,
                j.PeakMemoryBytes,
                j.StartedAt,
                j.CompletedAt
            })
            .ToListAsync(ct);
        var portalBreakdown = portalRows
            .GroupBy(j => $"{j.Kind}|report:{j.ReportId}|owner:{OwnerKey(j.ActorType, j.ActorId, j.UserId)}")
            .Select(g => new WorkloadBreakdown(
                g.Key,
                g.Count(),
                g.Count(j => j.Status is "Failed" or "Cancelled"),
                g.Sum(j => j.RowsProcessed),
                g.Max(j => j.PeakMemoryBytes) / (1024 * 1024),
                Percentile(g.Where(j => j.StartedAt is not null && j.CompletedAt is not null)
                    .Select(j => Math.Max(0, (j.CompletedAt!.Value - j.StartedAt!.Value).TotalMilliseconds)), 0.95)))
            .OrderByDescending(w => w.Runs)
            .ThenBy(w => w.Key, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"ETL-SQL capacity report — last {Math.Max(1, cfg.LookbackHours)}h.");
        sb.AppendLine();

        if (perNode.Count == 0)
        {
            sb.AppendLine("No host metric samples in the window (is the node heartbeat running?).");
        }
        else
        {
            sb.AppendLine($"Nodes ({perNode.Count}) — worst point in window:");
            foreach (var node in perNode)
            {
                sb.AppendLine(
                    $"  {node.NodeId}: state disk free {node.MinStateDiskFreeMB} MB, spill disk free {node.MinSpillDiskFreeMB} MB, " +
                    $"memory max/p95 {node.MaxMemoryLoadPercent:0}%/{node.P95MemoryLoadPercent:0}%, " +
                    $"CPU max/p95 {node.MaxCpuPercent:0}%/{node.P95CpuPercent:0}% ({node.Samples} samples)");
            }
        }

        sb.AppendLine();
        sb.AppendLine(
            $"Scheduled jobs: {history.Count} run(s), {failures} non-success ({failureRate:0.0}%).");
        sb.AppendLine(
            $"Execution p95: duration {p95DurationMs:0} ms, peak memory {p95PeakMemoryMB:0} MB, CPU {p95CpuSeconds:0.00} s; max peak memory {maxPeakMemoryMB} MB.");
        AppendBreakdown(sb, "Scheduled job breakdown", scheduledJobBreakdown);
        AppendBreakdown(sb, "Portal execution breakdown", portalBreakdown);
        sb.AppendLine("Long-term daily trends are retained in JobHistoryDaily and HostMetricsDaily rollups.");

        return new AdminDigestContent(
            $"ETL-SQL capacity report: {perNode.Count} node(s), {failures} job failure(s)",
            sb.ToString(),
            $"Nodes={perNode.Count}; JobRuns={history.Count}; JobFailures={failures}");
    }

    private static double EffectiveCpuPercent(HostMetricSample sample) =>
        sample.HostCpuPercent ?? sample.ProcessCpuPercent;

    private static bool IsFailure(JobHistoryEntry entry) =>
        !string.Equals(entry.Status, "SUCCESS", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(entry.Status, "RUNNING", StringComparison.OrdinalIgnoreCase);

    private static string OwnerKey(string actorType, string? actorId, int userId)
    {
        if (string.Equals(actorType, "ServiceAccount", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(actorId))
            return $"service:{actorId}";
        return $"user:{userId}";
    }

    private static void AppendBreakdown(StringBuilder sb, string heading, IReadOnlyList<WorkloadBreakdown> rows)
    {
        sb.AppendLine();
        sb.AppendLine($"{heading}:");
        if (rows.Count == 0)
        {
            sb.AppendLine("  none in window");
            return;
        }

        foreach (var row in rows)
        {
            var failureRate = row.Runs == 0 ? 0 : row.Failures * 100.0 / row.Runs;
            sb.AppendLine(
                $"  {row.Key}: runs {row.Runs}, failures {row.Failures} ({failureRate:0.0}%), " +
                $"rows {row.RowsProcessed}, max memory {row.MaxPeakMemoryMB} MB, p95 duration {row.P95DurationMs:0} ms");
        }
    }

    private static double Percentile(IEnumerable<double> values, double percentile)
    {
        var sorted = values
            .Where(v => !double.IsNaN(v) && !double.IsInfinity(v))
            .OrderBy(v => v)
            .ToArray();
        if (sorted.Length == 0)
            return 0;

        var rank = Math.Clamp(percentile, 0, 1) * (sorted.Length - 1);
        var lower = (int)Math.Floor(rank);
        var upper = (int)Math.Ceiling(rank);
        if (lower == upper)
            return sorted[lower];

        return sorted[lower] + (sorted[upper] - sorted[lower]) * (rank - lower);
    }

    private sealed record WorkloadBreakdown(
        string Key,
        int Runs,
        int Failures,
        long RowsProcessed,
        long MaxPeakMemoryMB,
        double P95DurationMs);
}
