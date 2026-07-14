using System.Text;
using ETL_SQL.Core.Data;

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
        sb.AppendLine("Long-term daily trends are retained in JobHistoryDaily and HostMetricsDaily rollups.");

        return new AdminDigestContent(
            $"ETL-SQL capacity report: {perNode.Count} node(s), {failures} job failure(s)",
            sb.ToString(),
            $"Nodes={perNode.Count}; JobRuns={history.Count}; JobFailures={failures}");
    }

    private static double EffectiveCpuPercent(HostMetricSample sample) =>
        sample.HostCpuPercent ?? sample.ProcessCpuPercent;

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
}
