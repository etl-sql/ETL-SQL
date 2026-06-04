using ETL_SQL.Core.Data;
using ETL_SQL.Orchestrator.Storage;
using ETL_SQL.ReportPortal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.ReportPortal.Services;

public class SubscriptionDeliveryStatusService(
    PortalDbContext db,
    OrchestratorDbLocator dbLocator,
    ILogger<SubscriptionDeliveryStatusService> log)
{
    public async Task<IReadOnlyList<JobHistoryEntry>> SynchronizeAsync(Subscription subscription, int limit = 100)
    {
        var orchDbPath = dbLocator.Resolve();
        if (orchDbPath is null || !File.Exists(orchDbPath))
        {
            return [];
        }

        IReadOnlyList<JobHistoryEntry> history;
        try
        {
            var store = new SQLiteJobHistoryStore(orchDbPath);
            await store.InitializeAsync();
            var jobName = $"SUB:{subscription.Id}:{subscription.Report?.Name}";
            history = (await store.GetHistoryAsync(jobName, int.MaxValue)).ToList();
        }
        catch (Exception ex)
        {
            log.LogWarning("Unable to read Orchestrator history for subscription {SubscriptionId}: {Message}",
                subscription.Id, ex.Message);
            return [];
        }

        var completed = history.Where(h => h.EndTime.HasValue).ToList();
        var failures = completed
            .Where(h => string.Equals(h.Status, "FAILURE", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var latestSuccess = completed
            .Where(h => string.Equals(h.Status, "SUCCESS", StringComparison.OrdinalIgnoreCase))
            .Max(h => h.EndTime);

        subscription.FailCount = failures.Count;
        subscription.LastSentAt = latestSuccess;

        var failureDetails = failures
            .Select(h => $"Job history entry {h.Id} failed.")
            .ToList();
        var existingDetails = await db.AuditLogs
            .Where(a => a.Action == "SUBSCRIPTION_DELIVERY_FAILED"
                && a.ResourceType == "Subscription"
                && a.ResourceId == subscription.Id.ToString()
                && a.Detail != null)
            .Select(a => a.Detail!)
            .ToListAsync();

        foreach (var detail in failureDetails.Except(existingDetails, StringComparer.Ordinal))
        {
            db.AuditLogs.Add(new AuditLog
            {
                UserId = subscription.UserId,
                Action = "SUBSCRIPTION_DELIVERY_FAILED",
                ResourceType = "Subscription",
                ResourceId = subscription.Id.ToString(),
                Timestamp = DateTime.UtcNow,
                Detail = detail
            });
        }

        await db.SaveChangesAsync();
        return history.Take(Math.Max(0, limit)).ToList();
    }

    public async Task SynchronizeAllAsync()
    {
        var subscriptions = await db.Subscriptions
            .Include(s => s.Report)
            .ToListAsync();

        foreach (var subscription in subscriptions)
        {
            await SynchronizeAsync(subscription, 1000);
        }
    }
}
