using ETL_SQL.Common;
using ETL_SQL.Core.Data;
using ETL_SQL.Orchestrator.Storage;
using ETL_SQL.Portal.Data;
namespace ETL_SQL.Portal.Services;

public class SubscriptionDeliveryStatusService(
    OrchestratorDbLocator dbLocator,
    IOrchestratorStoreFactory orchestratorStoreFactory,
    ILogger<SubscriptionDeliveryStatusService> log,
    DatasetTenantScope? tenantScope = null)
{
    public async Task<IReadOnlyList<JobHistoryEntry>> SynchronizeAsync(Subscription subscription, int limit = 100)
    {
        if (limit <= 0) return [];
        var orchDbPath = dbLocator.Resolve();
        if (orchestratorStoreFactory.Provider == DatabaseProvider.Sqlite
            && (orchDbPath is null || !File.Exists(orchDbPath)))
        {
            return [];
        }

        IReadOnlyList<JobHistoryEntry> history;
        try
        {
            var store = orchestratorStoreFactory.Create(orchDbPath);
            await store.InitializeAsync();
            var jobName = SubscriptionOrchestration.JobName(subscription.Id, subscription.Report?.Name);
            // Read by name, not identity: history outlives the job, so a re-created subscription job
            // must still show its earlier runs. The caller's verified tenant is what keeps a name
            // shared with another tenant's subscription from reaching that tenant's runs.
            history = (await store.GetHistoryForNameAsync(
                tenantScope?.TenantId, jobName, Math.Clamp(limit, 1, 1000))).ToList();
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
