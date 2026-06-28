using ETL_SQL.Core.Governance;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ETL_SQL.Orchestrator.Scheduling;

public sealed class EnterprisePolicyRefreshService(
    ILogger<EnterprisePolicyRefreshService> logger,
    IHostApplicationLifetime lifetime) : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!EnterprisePolicyRuntime.Current.IsEnrolled) return;

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(RefreshInterval, stoppingToken).ConfigureAwait(false);
            try
            {
                var policy = await EnterprisePolicyRuntime.InitializeFromMachineAsync(
                    cancellationToken: stoppingToken).ConfigureAwait(false);
                logger.LogInformation(
                    "Enterprise policy {PolicyVersion} refreshed from {PolicySource} with status {PolicyStatus}",
                    policy.PolicyVersion, policy.Source, policy.Status);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex,
                    "Authoritative enterprise policy could not be refreshed; stopping the fail-closed host");
                lifetime.StopApplication();
                return;
            }
        }
    }
}
