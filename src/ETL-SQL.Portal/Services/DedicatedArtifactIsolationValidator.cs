using ETL_SQL.Core.Storage;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Fails dedicated-host startup when legacy or foreign artifacts remain outside the configured
/// tenant prefix. Operators must migrate or quarantine them explicitly; silently hiding them would
/// turn an upgrade into apparent data loss and silently adopting them would invent tenant ownership.
/// </summary>
public sealed class DedicatedArtifactIsolationValidator(
    IArtifactStorage storage,
    PortalConfig config) : IHostedService
{
    private static readonly ArtifactArea[] TenantArtifactAreas =
    [
        ArtifactArea.Scripts,
        ArtifactArea.Snapshots,
        ArtifactArea.Maps,
        ArtifactArea.Datasets
    ];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (config.SharedTenancy.Enabled || string.IsNullOrWhiteSpace(config.TenantId))
            return;

        foreach (var area in TenantArtifactAreas)
        {
            await foreach (var _ in storage.EnumerateAsync(
                area, prefix: null, recursive: true, cancellationToken))
            {
                // Enumeration performs provider-neutral isolation validation. No materialization needed.
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
