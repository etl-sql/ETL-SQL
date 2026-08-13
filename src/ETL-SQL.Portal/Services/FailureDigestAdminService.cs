using System.Text;
using ETL_SQL.Core.Data;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Native replacement for samples/admin_operations/daily_failure_digest.etlsql: emails a digest of
/// failed orchestrator jobs, portal executions, and subscription deliveries in the lookback window.
/// In AlertOnly mode (default) nothing is sent when there were no failures.
/// </summary>
public sealed class FailureDigestAdminService(
    IServiceScopeFactory scopeFactory,
    PortalConfig config,
    IClusterLockStore lockStore,
    ILogger<FailureDigestAdminService> log)
    : AdminDigestServiceBase(scopeFactory, config, lockStore, log)
{
    public override string ServiceName => "failure-digest";

    protected override AdminServiceScheduleConfig Schedule => Config.AdminServices.FailureDigest;

    protected override async Task<AdminDigestContent?> BuildAsync(IServiceProvider scope, CancellationToken ct)
    {
        // Failure details can contain job names and delivery metadata. Never aggregate them across
        // tenants on a shared host without an explicit tenant-scoped scheduler invocation.
        if (Config.SharedTenancy.Enabled)
            return null;

        var cfg = Config.AdminServices.FailureDigest;
        var since = DateTime.UtcNow.AddHours(-Math.Max(1, cfg.LookbackHours));
        var db = scope.GetRequiredService<PortalDbContext>();
        var jobHistory = scope.GetRequiredService<IJobHistoryStore>();

        // Orchestrator jobs: everything terminal that is not SUCCESS (INTERRUPTED counts as failure,
        // in-flight RUNNING is excluded) — same contract as the sample script.
        var orchestratorFailures = (await jobHistory.GetCompletedHistoryAsync(since, DateTime.UtcNow, limit: 500))
            .Where(h => !string.Equals(h.Status, "SUCCESS", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(h.Status, "RUNNING", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var executionFailures = await db.PortalExecutionJobs
            .AsNoTracking()
            .Where(j => j.CompletedAt >= since && (j.Status == "Failed" || j.Status == "Cancelled"))
            .OrderByDescending(j => j.CompletedAt)
            .Take(100)
            .ToListAsync(ct);

        var deliveryFailures = await db.SubscriptionDeliveries
            .AsNoTracking()
            .Where(d => d.CompletedAt >= since && (d.Outcome == "Failed" || d.Outcome == "Denied"))
            .OrderByDescending(d => d.CompletedAt)
            .Take(100)
            .ToListAsync(ct);

        var total = orchestratorFailures.Count + executionFailures.Count + deliveryFailures.Count;
        if (total == 0 && cfg.AlertOnly)
            return null;

        var sb = new StringBuilder();
        sb.AppendLine($"ETL-SQL failure digest — last {Math.Max(1, cfg.LookbackHours)}h, {total} failure(s).");
        sb.AppendLine();

        if (orchestratorFailures.Count > 0)
        {
            sb.AppendLine($"Scheduled jobs ({orchestratorFailures.Count}):");
            foreach (var h in orchestratorFailures)
                sb.AppendLine($"  {h.JobName} [{h.Status}] at {h.EndTime:u}: {h.ErrorMessage ?? "no error message"}");
            sb.AppendLine();
        }

        if (executionFailures.Count > 0)
        {
            sb.AppendLine($"Portal executions ({executionFailures.Count}):");
            foreach (var j in executionFailures)
                sb.AppendLine($"  {j.Kind} #{j.Id} [{j.Status}] at {j.CompletedAt:u}: {j.Error ?? "no error message"}");
            sb.AppendLine();
        }

        if (deliveryFailures.Count > 0)
        {
            sb.AppendLine($"Subscription deliveries ({deliveryFailures.Count}):");
            foreach (var d in deliveryFailures)
                sb.AppendLine($"  delivery {d.DeliveryId} [{d.Outcome}] at {d.CompletedAt:u}: {d.Detail ?? "no detail"}");
            sb.AppendLine();
        }

        if (total == 0)
            sb.AppendLine("No failures in the window.");

        var subject = total == 0
            ? "ETL-SQL failure digest: no failures"
            : $"ETL-SQL failure digest: {total} failure(s)";
        return new AdminDigestContent(subject, sb.ToString(),
            $"OrchestratorFailures={orchestratorFailures.Count}; ExecutionFailures={executionFailures.Count}; DeliveryFailures={deliveryFailures.Count}");
    }
}
