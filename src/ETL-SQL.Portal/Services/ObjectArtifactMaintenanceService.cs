using ETL_SQL.Core.Storage;

namespace ETL_SQL.Portal.Services;

/// <summary>Verifies committed object content and collects non-authoritative staging residue.</summary>
public sealed class ObjectArtifactMaintenanceService(
    ObjectNativeArtifactStorage storage,
    PortalConfig config,
    ILogger<ObjectArtifactMaintenanceService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var retention = TimeSpan.FromHours(Math.Max(1, config.Storage.StagingRetentionHours));
        var interval = TimeSpan.FromMinutes(Math.Max(1, config.Storage.ReconciliationIntervalMinutes));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await storage.ReconcileAsync(retention, DateTimeOffset.UtcNow, stoppingToken);
                if (!result.IsHealthy)
                    logger.LogError(
                        "Object artifact reconciliation found {MissingCount} missing and {CorruptCount} corrupt committed objects; no repair was attempted.",
                        result.MissingObjects.Count, result.CorruptObjects.Count);
                if (result.DeletedStagingObjects > 0)
                    logger.LogInformation("Object artifact reconciliation collected {Count} abandoned staging objects.", result.DeletedStagingObjects);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Object artifact reconciliation failed; committed state was left unchanged.");
            }
            await Task.Delay(interval, stoppingToken);
        }
    }
}
