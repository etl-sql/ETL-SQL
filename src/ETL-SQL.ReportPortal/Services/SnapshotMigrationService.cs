using System.Text.Json;
using ETL_SQL.Core.Storage;
using ETL_SQL.ReportPortal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.ReportPortal.Services;

public sealed class SnapshotMigrationService(
    IServiceScopeFactory scopeFactory,
    ILogger<SnapshotMigrationService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await StartCoreAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("Snapshot migration skipped because portal startup is stopping.");
        }
    }

    private async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var config = scope.ServiceProvider.GetRequiredService<PortalConfig>();
        var artifacts = scope.ServiceProvider.GetRequiredService<IArtifactStorage>();
        var packages = scope.ServiceProvider.GetRequiredService<SnapshotPackageService>();
        var keyValidation = DatasetAtRestKeyValidator.Validate(config.Dataset);
        if (keyValidation.Severity != DatasetAtRestKeyValidator.Severity.Ok)
        {
            logger.LogInformation(
                "Skipping snapshot migration because the dataset at-rest key is not ready: {Reason}",
                keyValidation.Message);
            return;
        }

        var migratedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var snapshots = await db.ReportSnapshots
            .Where(s => s.ManifestPath.EndsWith(".json"))
            .ToListAsync(cancellationToken);

        foreach (var snapshot in snapshots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var legacyKey = PortalPathGuard.ToSnapshotKey(config, snapshot.ManifestPath);
            if (legacyKey is null || !SnapshotPackageService.IsLegacyJsonKey(legacyKey))
                continue;

            try
            {
                var packageKey = await packages.MigrateLegacyJsonAsync(legacyKey, cancellationToken);
                if (packageKey is null)
                    continue;

                snapshot.ManifestPath = packageKey;
                migratedKeys.Add(legacyKey);
            }
            catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or JsonException or IOException)
            {
                logger.LogWarning(ex, "Failed to migrate legacy snapshot manifest {ManifestPath}", snapshot.ManifestPath);
            }
        }

        if (migratedKeys.Count > 0)
            await db.SaveChangesAsync(cancellationToken);

        await MigrateOrphanedLegacySnapshotsAsync(artifacts, packages, migratedKeys, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task MigrateOrphanedLegacySnapshotsAsync(
        IArtifactStorage artifacts,
        SnapshotPackageService packages,
        HashSet<string> migratedKeys,
        CancellationToken cancellationToken)
    {
        await foreach (var artifact in artifacts.EnumerateAsync(
            ArtifactArea.Snapshots,
            prefix: null,
            recursive: true,
            cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!artifact.Path.EndsWith(".snapshot.json", StringComparison.OrdinalIgnoreCase)
                || migratedKeys.Contains(artifact.Path))
            {
                continue;
            }

            try
            {
                await packages.MigrateLegacyJsonAsync(artifact.Path, cancellationToken);
            }
            catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or JsonException or IOException)
            {
                logger.LogWarning(ex, "Failed to migrate orphaned legacy snapshot manifest {SnapshotKey}", artifact.Path);
            }
        }
    }
}
