using ETL_SQL.Common;
using ETL_SQL.Orchestrator.Storage;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ETL_SQL.Portal.Services.HealthChecks;

public class OrchestratorHealthCheck(
    OrchestratorDbLocator locator,
    IOrchestratorStoreFactory storeFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        var path = locator.Resolve();
        if (storeFactory.Provider == DatabaseProvider.Sqlite && path is null)
            return HealthCheckResult.Degraded("Orchestrator DB not found. Scheduled jobs will not run.");

        try
        {
            var store = storeFactory.Create(path);
            await store.InitializeAsync();
            return HealthCheckResult.Healthy(
                storeFactory.Provider == DatabaseProvider.Postgres
                    ? "Orchestrator PostgreSQL store is reachable."
                    : $"Orchestrator DB found at {path}");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded(
                "Orchestrator state store is unavailable. Scheduled jobs will not run.", ex);
        }
    }
}
