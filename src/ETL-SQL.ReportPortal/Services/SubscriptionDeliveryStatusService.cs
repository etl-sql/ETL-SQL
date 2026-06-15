using ETL_SQL.Core.Data;
using ETL_SQL.Orchestrator.Storage;
using ETL_SQL.ReportPortal.Data;
namespace ETL_SQL.ReportPortal.Services;

public class SubscriptionDeliveryStatusService(
    OrchestratorDbLocator dbLocator,
    IOrchestratorStoreFactory orchestratorStoreFactory,
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
            var store = orchestratorStoreFactory.Create(orchDbPath);
            await store.InitializeAsync();
            var jobName = SubscriptionOrchestration.JobName(subscription.Id, subscription.Report?.Name);
            history = (await store.GetHistoryAsync(jobName, int.MaxValue)).ToList();
        }
        catch (Exception ex)
        {
            log.LogWarning("Unable to read Orchestrator history for subscription {SubscriptionId}: {Message}",
                subscription.Id, ex.Message);
            return [];
        }

        // Orchestrator history now describes only the credential-free scheduler trigger.
        // Delivery state is owned by SubscriptionDeliveryService and must not be derived
        // from trigger success/failure rows.
        return history.Take(Math.Max(0, limit)).ToList();
    }

    public Task SynchronizeAllAsync() => Task.CompletedTask;
}
