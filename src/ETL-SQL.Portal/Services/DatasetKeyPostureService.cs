using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Models;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Inventory, preflight, and verification for the dataset at-rest key — everything around rotation
/// except the rotation itself.
///
/// Rotation was a single POST that reported what it had done. That is fine when it works and no help
/// when it does not: a dataset encrypted under a version whose key is no longer configured cannot be
/// rotated <em>or read</em>, and the only way to discover that was to start the rotation and read the
/// failure list. Preflight makes it knowable beforehand.
///
/// <b>No key material appears anywhere here.</b> Key versions are non-secret identifiers and are
/// named freely; keys themselves are reported only as configured or not.
/// </summary>
public sealed class DatasetKeyPostureService(PortalDbContext db, PortalConfig config)
{
    public async Task<DatasetKeyPostureDto> BuildAsync(CancellationToken ct = default)
    {
        var currentVersion = config.Dataset.AtRestKeyVersion;
        var currentConfigured = !string.IsNullOrWhiteSpace(config.Dataset.AtRestKey);

        // Only encrypted caches carry a key version; a dataset with no cache file has nothing to rotate.
        var datasets = await db.Datasets
            .AsNoTracking()
            .Where(dataset => dataset.ParquetFilePath != "")
            .Select(dataset => new
            {
                dataset.Id,
                dataset.Name,
                dataset.AtRestKeyVersion,
                dataset.ParquetFilePath
            })
            .ToListAsync(ct);

        var resolved = datasets
            .Select(dataset => new
            {
                dataset.Id,
                dataset.Name,
                dataset.ParquetFilePath,
                // Mirrors DatasetAtRestKeyRotationService.ResolveSourceVersion: an unstamped row is
                // whatever the deployment declares its legacy version to be.
                Version = dataset.AtRestKeyVersion
                    ?? config.Dataset.LegacyAtRestKeyVersion
                    ?? currentVersion
            })
            .ToList();

        var inventory = resolved
            .GroupBy(dataset => dataset.Version, StringComparer.OrdinalIgnoreCase)
            .Select(group => new DatasetKeyVersionDto(
                group.Key,
                group.Count(),
                IsCurrent: string.Equals(group.Key, currentVersion, StringComparison.OrdinalIgnoreCase),
                KeyConfigured: KeyConfiguredFor(group.Key)))
            .OrderByDescending(entry => entry.IsCurrent)
            .ThenBy(entry => entry.Version, StringComparer.Ordinal)
            .ToList();

        var blocked = new List<DatasetKeyBlockerDto>();
        var wouldRotate = 0;
        var alreadyCurrent = 0;
        var missingFiles = 0;

        foreach (var dataset in resolved)
        {
            var isCurrent = string.Equals(dataset.Version, currentVersion, StringComparison.OrdinalIgnoreCase);
            var fileMissing = !PortalPathGuard.TryResolveDataset(config, dataset.ParquetFilePath, out var path)
                || !File.Exists(path);
            if (fileMissing) missingFiles++;

            if (isCurrent) { alreadyCurrent++; continue; }

            if (!KeyConfiguredFor(dataset.Version))
            {
                blocked.Add(new DatasetKeyBlockerDto(dataset.Id, dataset.Name, dataset.Version,
                    $"No at-rest key is configured for version '{dataset.Version}', so this cache can "
                    + "neither be rotated nor read. Restore that key under "
                    + "Portal:Dataset:PreviousAtRestKeys, or re-materialise the dataset."));
                continue;
            }

            if (fileMissing)
            {
                blocked.Add(new DatasetKeyBlockerDto(dataset.Id, dataset.Name, dataset.Version,
                    "The cache file is missing, so there is nothing to re-encrypt. Refresh the dataset."));
                continue;
            }

            wouldRotate++;
        }

        var findings = new List<string>();
        if (!currentConfigured)
            findings.Add("No portal at-rest key is configured, so rotation cannot run at all.");
        if (blocked.Count > 0)
            findings.Add($"{blocked.Count} dataset(s) cannot be rotated; see the blocked list.");

        var referenced = resolved
            .Select(dataset => dataset.Version)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var retired = config.Dataset.PreviousAtRestKeys.Keys
            .Where(version => !referenced.Contains(version))
            .OrderBy(version => version, StringComparer.Ordinal)
            .ToList();

        return new DatasetKeyPostureDto(
            currentVersion,
            currentConfigured,
            inventory,
            new DatasetKeyRotationPreflightDto(
                CanProceed: currentConfigured && blocked.Count == 0,
                wouldRotate, alreadyCurrent, blocked, findings),
            new DatasetKeyVerificationDto(
                FullyRotated: resolved.Count > 0 && resolved.All(dataset =>
                    string.Equals(dataset.Version, currentVersion, StringComparison.OrdinalIgnoreCase)),
                alreadyCurrent,
                resolved.Count - alreadyCurrent,
                missingFiles,
                retired),
            RollbackGuidance:
                "Rotation re-encrypts each cache in place and stamps the new version as it goes, so it "
                + "is resumable rather than transactional: re-running it retries only what has not "
                + "moved. To roll back, keep the previous key under Portal:Dataset:PreviousAtRestKeys, "
                + "set Portal:Dataset:AtRestKeyVersion back to it, and rotate again — never remove a "
                + "key while any dataset or retained backup still references its version, because "
                + "that data becomes unreadable rather than merely un-rotatable.");
    }

    private bool KeyConfiguredFor(string version) =>
        (string.Equals(version, config.Dataset.AtRestKeyVersion, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(config.Dataset.AtRestKey))
        || config.Dataset.PreviousAtRestKeys.ContainsKey(version);
}
