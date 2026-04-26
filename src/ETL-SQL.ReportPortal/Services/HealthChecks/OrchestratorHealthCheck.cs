using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ETL_SQL.ReportPortal.Services.HealthChecks;

public class OrchestratorHealthCheck(OrchestratorDbLocator locator) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        var path = locator.Resolve();
        return Task.FromResult(path is not null
            ? HealthCheckResult.Healthy($"Orchestrator DB found at {path}")
            : HealthCheckResult.Degraded("Orchestrator DB not found. Scheduled jobs will not run."));
    }
}
