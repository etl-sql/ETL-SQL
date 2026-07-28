using System.Text.RegularExpressions;
using ETL_SQL.Core.Data;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

public static class DatasetStorageMaintenance
{
    public const string ClusterLockName = "portal-dataset-storage-reconciliation";
    private static readonly TimeSpan ClusterLockTtl = TimeSpan.FromMinutes(10);

    private static readonly Regex ManagedFileName =
        new(@"^.+_\d+\.parquet$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static async Task ReconcileAsync(
        PortalDbContext db,
        PortalConfig config,
        ILogger logger,
        bool deepOrphanScan = false,
        int pageSize = 1_000,
        IClusterLockStore? clusterLockStore = null,
        string? clusterLockOwner = null)
    {
        if (pageSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(pageSize), "Page size must be positive.");

        if (clusterLockStore is not null)
        {
            var owner = string.IsNullOrWhiteSpace(clusterLockOwner)
                ? $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}"
                : clusterLockOwner;
            if (!await clusterLockStore.TryAcquireLockAsync(ClusterLockName, owner, ClusterLockTtl))
            {
                logger.LogInformation(
                    "Dataset storage reconciliation skipped because another Portal node owns cluster lock {LockName}.",
                    ClusterLockName);
                return;
            }
        }

        var root = Path.GetFullPath(config.DatasetRootPath);
        Directory.CreateDirectory(root);

        foreach (var path in Directory.EnumerateFiles(root, ".*", SearchOption.TopDirectoryOnly)
                     .Where(p => Path.GetFileName(p).Contains(".tmp-", StringComparison.OrdinalIgnoreCase)
                              || Path.GetFileName(p).Contains(".bak-", StringComparison.OrdinalIgnoreCase)
                              || Path.GetFileName(p).StartsWith(".rotate-", StringComparison.OrdinalIgnoreCase)))
        {
            TryDelete(path, logger, "abandoned dataset staging file");
        }

        var referenced = deepOrphanScan
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : null;
        var orphanRowIds = new List<int>(capacity: Math.Min(pageSize, 1_000));
        var lastId = 0;
        var removedRows = 0;

        while (true)
        {
            var datasets = await db.Datasets
                .AsNoTracking()
                .Where(d => d.Id > lastId)
                .OrderBy(d => d.Id)
                .Select(d => new { d.Id, d.ParquetFilePath })
                .Take(pageSize)
                .ToListAsync();
            if (datasets.Count == 0)
                break;

            foreach (var dataset in datasets)
            {
                lastId = dataset.Id;

                if (string.IsNullOrWhiteSpace(dataset.ParquetFilePath))
                {
                    orphanRowIds.Add(dataset.Id);
                    continue;
                }

                if (!PortalPathGuard.TryResolveDataset(config, dataset.ParquetFilePath, out var resolved))
                    continue;

                if (File.Exists(resolved))
                    referenced?.Add(resolved);
                else
                    orphanRowIds.Add(dataset.Id);
            }

            if (orphanRowIds.Count > 0)
            {
                await db.Datasets
                    .Where(d => orphanRowIds.Contains(d.Id))
                    .ExecuteDeleteAsync();
                removedRows += orphanRowIds.Count;
                orphanRowIds.Clear();
            }
        }

        if (removedRows > 0)
        {
            logger.LogWarning(
                "Removed {Count} dataset catalog rows whose managed cache file was missing.",
                removedRows);
        }

        if (!deepOrphanScan || referenced is null)
            return;

        foreach (var path in Directory.EnumerateFiles(root, "*.parquet", SearchOption.TopDirectoryOnly))
        {
            var fullPath = Path.GetFullPath(path);
            if (ManagedFileName.IsMatch(Path.GetFileName(fullPath)) && !referenced.Contains(fullPath))
                TryDelete(fullPath, logger, "orphaned managed dataset file");
        }
    }

    private static void TryDelete(string path, ILogger logger, string description)
    {
        try
        {
            File.Delete(path);
            logger.LogInformation("Removed {Description}: {Path}", description, path);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not remove {Description}: {Path}", description, path);
        }
    }
}
