using System.Text;
using ETL_SQL.Core.Data;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

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
    private const int TrendLookbackDays = 30;
    private const double CpuSaturationPercent = 80;
    private const double MemorySaturationPercent = 80;
    private const long DiskSaturationBytes = 10L * 1024 * 1024 * 1024;

    public override string ServiceName => "capacity-report";

    protected override AdminServiceScheduleConfig Schedule => Config.AdminServices.CapacityReport;

    protected override async Task<AdminDigestContent?> BuildAsync(IServiceProvider scope, CancellationToken ct)
    {
        var cfg = Config.AdminServices.CapacityReport;
        var now = DateTime.UtcNow;
        var since = now.AddHours(-Math.Max(1, cfg.LookbackHours));
        var trendSince = now.Date.AddDays(-(TrendLookbackDays - 1));
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

        var history = (await jobHistory.GetCompletedHistoryAsync(since, now, limit: 2000)).ToList();
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
                j.CreatedAt,
                j.StartedAt,
                j.CompletedAt
            })
            .ToListAsync(ct);
        var portalPressureRows = await db.PortalExecutionJobs
            .AsNoTracking()
            .Where(j => j.CreatedAt >= trendSince
                || j.CompletedAt >= trendSince
                || j.Status == "Pending"
                || j.Status == "Running")
            .Select(j => new PortalPressureRow(
                j.CreatedAt,
                j.StartedAt,
                j.CompletedAt,
                j.Status))
            .ToListAsync(ct);
        var portalQueueWaitMs = portalRows
            .Where(j => j.StartedAt is not null)
            .Select(j => Math.Max(0, (j.StartedAt!.Value - j.CreatedAt).TotalMilliseconds))
            .ToList();
        var portalRunDurationMs = portalRows
            .Where(j => j.StartedAt is not null && j.CompletedAt is not null)
            .Select(j => Math.Max(0, (j.CompletedAt!.Value - j.StartedAt!.Value).TotalMilliseconds))
            .ToList();
        var p95PortalQueueWaitMs = Percentile(portalQueueWaitMs, 0.95);
        var p95PortalRunDurationMs = Percentile(portalRunDurationMs, 0.95);
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

        var jobTrend = await jobHistory.GetJobHistoryDailyAsync(null, trendSince, limit: 10000);
        var hostTrend = await hostMetrics.GetHostMetricsDailyAsync(null, trendSince, limit: 10000);

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
        AppendPortalQueueDiagnosis(sb, p95PortalQueueWaitMs, p95PortalRunDurationMs, portalRows.Count);
        AppendPortalHourlyPressure(sb, portalPressureRows, trendSince, now, Config.Resources.MaxConcurrentReportExecutions);
        AppendTrendSummary(sb, jobTrend, hostTrend, trendSince);

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

    private static void AppendTrendSummary(
        StringBuilder sb,
        IReadOnlyList<JobHistoryDailySummary> jobTrend,
        IReadOnlyList<HostMetricsDailySummary> hostTrend,
        DateTime trendSince)
    {
        sb.AppendLine();
        sb.AppendLine($"Historical planning trend - last {TrendLookbackDays} days from {trendSince:yyyy-MM-dd}:");

        if (jobTrend.Count == 0 && hostTrend.Count == 0)
        {
            sb.AppendLine("  no daily rollup rows available yet; keep services running long enough to build measured history before sizing from trends");
            return;
        }

        if (jobTrend.Count > 0)
        {
            var totalRuns = jobTrend.Sum(j => j.RunCount);
            var totalFailures = jobTrend.Sum(j => j.FailureCount);
            var totalRows = jobTrend.Sum(j => j.TotalRows);
            var maxPeakMemoryMB = jobTrend.Max(j => j.MaxPeakMemoryBytes) / (1024 * 1024);
            var failureRate = totalRuns == 0 ? 0 : totalFailures * 100.0 / totalRuns;
            var busiestDay = jobTrend
                .GroupBy(j => j.Day, StringComparer.Ordinal)
                .Select(g => new { Day = g.Key, Runs = g.Sum(j => j.RunCount) })
                .OrderByDescending(g => g.Runs)
                .ThenBy(g => g.Day, StringComparer.Ordinal)
                .First();

            sb.AppendLine(
                $"  workload: {totalRuns} scheduled run(s), {totalFailures} failure(s) ({failureRate:0.0}%), " +
                $"{totalRows} row(s), max job peak memory {maxPeakMemoryMB} MB; busiest day {busiestDay.Day} with {busiestDay.Runs} run(s)");
        }
        else
        {
            sb.AppendLine("  workload: no scheduled-job daily rollups available");
        }

        if (hostTrend.Count > 0)
        {
            sb.AppendLine("  saturation indicators:");
            foreach (var node in hostTrend
                .GroupBy(h => h.NodeId, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                var maxCpu = node.Max(EffectiveDailyCpuPercent);
                var maxMemory = node.Max(h => h.MaxMemoryLoadPercent);
                var minStateDisk = node.Min(h => h.MinStateDiskFreeBytes);
                var minSpillDisk = node.Min(h => h.MinSpillDiskFreeBytes);
                var signals = BuildSaturationSignals(maxCpu, maxMemory, minStateDisk, minSpillDisk);

                sb.AppendLine(
                    $"    {node.Key}: CPU max {maxCpu:0}%, memory max {maxMemory:0}%, " +
                    $"state disk floor {ToGiB(minStateDisk):0.0} GiB, spill disk floor {ToGiB(minSpillDisk):0.0} GiB - {signals}");

                var stateForecast = BuildDiskForecast(node, h => h.MinStateDiskFreeBytes, "state");
                if (!string.IsNullOrWhiteSpace(stateForecast))
                    sb.AppendLine($"      {stateForecast}");

                var spillForecast = BuildDiskForecast(node, h => h.MinSpillDiskFreeBytes, "spill");
                if (!string.IsNullOrWhiteSpace(spillForecast))
                    sb.AppendLine($"      {spillForecast}");
            }
        }
        else
        {
            sb.AppendLine("  saturation indicators: no host daily rollups available");
        }

        sb.AppendLine(
            "  bottleneck guide: CPU/memory/storage signals are measured directly; connector, database, and concurrency bottlenecks require correlating this trend with queue depth, job latency, and external-system telemetry.");
    }

    private static void AppendPortalQueueDiagnosis(
        StringBuilder sb,
        double p95QueueWaitMs,
        double p95RunDurationMs,
        int portalExecutionCount)
    {
        sb.AppendLine();
        sb.AppendLine("Portal queue diagnosis:");
        if (portalExecutionCount == 0)
        {
            sb.AppendLine("  no completed portal executions in window");
            return;
        }

        sb.AppendLine(
            $"  p95 queue wait {p95QueueWaitMs:0} ms; p95 run duration {p95RunDurationMs:0} ms");
        sb.AppendLine($"  signal: {BuildPortalQueueSignal(p95QueueWaitMs, p95RunDurationMs)}");
    }

    private static string BuildPortalQueueSignal(double p95QueueWaitMs, double p95RunDurationMs)
    {
        if (p95QueueWaitMs <= 0 && p95RunDurationMs <= 0)
            return "insufficient timing data";

        if (p95QueueWaitMs >= 5000 && p95QueueWaitMs >= p95RunDurationMs)
            return "execution slots are likely saturated; consider concurrency caps, schedule spreading, or more Portal execution capacity";

        if (p95RunDurationMs >= 5000 && p95QueueWaitMs < p95RunDurationMs / 2)
            return "runs are slow after admission; inspect report logic, connectors, databases, exports, and storage before raising concurrency";

        if (p95QueueWaitMs >= 5000 && p95RunDurationMs >= 5000)
            return "queueing and run time are both elevated; correlate with CPU, memory, storage, and downstream telemetry before scaling";

        return "no baseline queue saturation signal";
    }

    private static void AppendPortalHourlyPressure(
        StringBuilder sb,
        IReadOnlyList<PortalPressureRow> rows,
        DateTime trendSince,
        DateTime now,
        int executionCap)
    {
        sb.AppendLine();
        sb.AppendLine($"Portal hourly pressure - last {TrendLookbackDays} days:");
        if (rows.Count == 0)
        {
            sb.AppendLine("  no portal execution lifecycle rows available");
            return;
        }

        var pressure = BuildHourlyPressure(rows, trendSince, now);
        if (pressure.Count == 0)
        {
            sb.AppendLine("  no queue or active-slot intervals intersect the trend window");
            return;
        }

        var busiestQueue = pressure
            .OrderByDescending(p => p.Queued)
            .ThenByDescending(p => p.Active)
            .ThenBy(p => p.HourUtc)
            .First();
        var busiestActive = pressure
            .OrderByDescending(p => p.Active)
            .ThenByDescending(p => p.Queued)
            .ThenBy(p => p.HourUtc)
            .First();
        var hoursAtCap = executionCap <= 0
            ? 0
            : pressure.Count(p => p.Active >= executionCap);

        sb.AppendLine(
            $"  busiest queued hour {busiestQueue.HourUtc:yyyy-MM-dd HH}:00Z with {busiestQueue.Queued} queued and {busiestQueue.Active} active");
        sb.AppendLine(
            $"  busiest active hour {busiestActive.HourUtc:yyyy-MM-dd HH}:00Z with {busiestActive.Active}/{executionCap} active slots and {busiestActive.Queued} queued");
        sb.AppendLine(
            $"  active-slot cap reached in {hoursAtCap} hour(s); {BuildHourlyPressureSignal(busiestQueue.Queued, busiestActive.Active, hoursAtCap, executionCap)}");
    }

    private static IReadOnlyList<PortalHourlyPressure> BuildHourlyPressure(
        IReadOnlyList<PortalPressureRow> rows,
        DateTime trendSince,
        DateTime now)
    {
        var start = TruncateToHour(trendSince);
        var end = TruncateToHour(now);
        var result = new List<PortalHourlyPressure>();

        for (var hour = start; hour <= end; hour = hour.AddHours(1))
        {
            var hourEnd = hour.AddHours(1);
            var queued = rows.Count(row =>
            {
                var queueEnd = row.StartedAt ?? row.CompletedAt ?? now;
                return Intersects(row.CreatedAt, queueEnd, hour, hourEnd);
            });
            var active = rows.Count(row =>
                row.StartedAt is not null
                && Intersects(row.StartedAt.Value, row.CompletedAt ?? now, hour, hourEnd));

            if (queued > 0 || active > 0)
                result.Add(new PortalHourlyPressure(hour, queued, active));
        }

        return result;
    }

    private static bool Intersects(DateTime start, DateTime end, DateTime windowStart, DateTime windowEnd) =>
        start < windowEnd && end >= windowStart;

    private static string BuildHourlyPressureSignal(int busiestQueued, int busiestActive, int hoursAtCap, int executionCap)
    {
        if (executionCap > 0 && hoursAtCap > 0 && busiestQueued > 0)
            return "historical pressure shows queued work while active slots were at cap";

        if (executionCap > 0 && busiestActive >= executionCap)
            return "active slots reached cap, but queued overlap was low";

        if (busiestQueued > 0)
            return "queued overlap appeared without reaching the configured global cap; inspect per-user/per-group caps and node capacity admission";

        return "no historical concurrency pressure signal";
    }

    private static DateTime TruncateToHour(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        return new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, DateTimeKind.Utc);
    }

    private static string BuildSaturationSignals(double maxCpu, double maxMemory, long minStateDisk, long minSpillDisk)
    {
        var signals = new List<string>();
        if (maxCpu >= CpuSaturationPercent)
            signals.Add("CPU");
        if (maxMemory >= MemorySaturationPercent)
            signals.Add("memory");
        if (minStateDisk <= DiskSaturationBytes)
            signals.Add("state storage");
        if (minSpillDisk <= DiskSaturationBytes)
            signals.Add("spill storage");

        return signals.Count == 0 ? "no baseline saturation breach" : "watch " + string.Join(", ", signals);
    }

    private static string? BuildDiskForecast(
        IEnumerable<HostMetricsDailySummary> rows,
        Func<HostMetricsDailySummary, long> selector,
        string label)
    {
        var ordered = rows
            .OrderBy(r => r.Day, StringComparer.Ordinal)
            .ToList();
        if (ordered.Count < 2)
            return null;

        var first = ordered[0];
        var last = ordered[^1];
        if (!DateTime.TryParse(first.Day, out var firstDay) || !DateTime.TryParse(last.Day, out var lastDay))
            return null;

        var elapsedDays = Math.Max(1, (lastDay.Date - firstDay.Date).TotalDays);
        var bytesPerDay = (selector(first) - selector(last)) / elapsedDays;
        if (bytesPerDay <= 0)
            return $"{label} disk forecast: no measured decline across the rollup window";

        var currentBytes = selector(last);
        var daysToThreshold = Math.Max(0, (currentBytes - DiskSaturationBytes) / bytesPerDay);
        return $"{label} disk forecast: at recent trend, reaches {ToGiB(DiskSaturationBytes):0} GiB floor in ~{daysToThreshold:0} day(s)";
    }

    private static double EffectiveDailyCpuPercent(HostMetricsDailySummary summary) =>
        summary.MaxHostCpuPercent ?? summary.MaxProcessCpuPercent;

    private static double ToGiB(long bytes) => bytes / 1024.0 / 1024.0 / 1024.0;

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

    private sealed record PortalPressureRow(
        DateTime CreatedAt,
        DateTime? StartedAt,
        DateTime? CompletedAt,
        string Status);

    private sealed record PortalHourlyPressure(
        DateTime HourUtc,
        int Queued,
        int Active);
}
