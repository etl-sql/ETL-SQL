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
                MaxProcessCpuPercent = g.Max(s => s.ProcessCpuPercent),
                Samples = g.Count()
            })
            .ToList();

        var history = (await jobHistory.GetCompletedHistoryAsync(since, DateTime.UtcNow, limit: 2000)).ToList();
        var failures = history.Count(h =>
            !string.Equals(h.Status, "SUCCESS", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(h.Status, "RUNNING", StringComparison.OrdinalIgnoreCase));

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
                    $"memory {node.MaxMemoryLoadPercent:0}%, CPU {node.MaxProcessCpuPercent:0}% ({node.Samples} samples)");
            }
        }

        sb.AppendLine();
        sb.AppendLine($"Scheduled jobs: {history.Count} run(s), {failures} non-success.");

        return new AdminDigestContent(
            $"ETL-SQL capacity report: {perNode.Count} node(s), {failures} job failure(s)",
            sb.ToString(),
            $"Nodes={perNode.Count}; JobRuns={history.Count}; JobFailures={failures}");
    }
}
