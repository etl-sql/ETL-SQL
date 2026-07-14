using ETL_SQL.Core.Observability;
using ETL_SQL.ReportPortal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.ReportPortal.Services;

/// <summary>
/// Enforces the configured audit retention window (Portal:Audit:RetentionDays). Disabled by
/// default (0 = keep forever): audit rows are the portal's security record, so an administrator
/// must opt in to deletion — and is expected to export or forward rows externally first (see the
/// administrators guide). The clock is injectable so the hosted-service lane can pin "now".
/// </summary>
public sealed class AuditRetentionService(
    IServiceScopeFactory scopeFactory,
    PortalConfig config,
    TimeProvider clock,
    ILogger<AuditRetentionService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (config.Audit.RetentionDays <= 0)
            return; // retention disabled — keep every audit row

        var interval = TimeSpan.FromSeconds(Math.Max(1, config.Audit.PurgeIntervalSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var cutoff = clock.GetUtcNow().UtcDateTime.AddDays(-config.Audit.RetentionDays);
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
                var removed = await PurgeExpiredAsync(db, cutoff, stoppingToken);
                if (removed > 0)
                    log.LogInformation(
                        "Purged {Count} audit rows older than {Cutoff:o} (retention {Days}d)",
                        removed, cutoff, config.Audit.RetentionDays);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Audit retention sweep failed; will retry next interval");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    internal static async Task<int> PurgeExpiredAsync(
        PortalDbContext db, DateTime cutoffUtc, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var activity = BackgroundServiceObservability.StartRun(
            "portal", "audit-retention", "purge_expired");
        try
        {
            var removed = await db.AuditLogs
                .Where(a => a.Timestamp < cutoffUtc)
                .ExecuteDeleteAsync(ct);
            sw.Stop();
            BackgroundServiceObservability.SetRowsProcessed(activity, removed);
            BackgroundServiceObservability.CompleteRun(
                activity,
                "portal",
                "audit-retention",
                "purge_expired",
                "success",
                sw.ElapsedMilliseconds);
            return removed;
        }
        catch
        {
            sw.Stop();
            BackgroundServiceObservability.CompleteRun(
                activity,
                "portal",
                "audit-retention",
                "purge_expired",
                "failure",
                sw.ElapsedMilliseconds);
            throw;
        }
    }
}
