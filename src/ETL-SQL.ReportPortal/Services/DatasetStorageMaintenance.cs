using System.Text.RegularExpressions;
using ETL_SQL.ReportPortal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.ReportPortal.Services;

public static class DatasetStorageMaintenance
{
    private static readonly Regex ManagedFileName =
        new(@"^.+_\d+\.parquet$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static async Task ReconcileAsync(
        PortalDbContext db,
        PortalConfig config,
        ILogger logger)
    {
        var root = Path.GetFullPath(config.DatasetRootPath);
        Directory.CreateDirectory(root);

        foreach (var path in Directory.EnumerateFiles(root, ".*", SearchOption.TopDirectoryOnly)
                     .Where(p => Path.GetFileName(p).Contains(".tmp-", StringComparison.OrdinalIgnoreCase)
                              || Path.GetFileName(p).Contains(".bak-", StringComparison.OrdinalIgnoreCase)))
        {
            TryDelete(path, logger, "abandoned dataset staging file");
        }

        var datasets = await db.Datasets.ToListAsync();
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var orphanRows = new List<Dataset>();

        foreach (var dataset in datasets)
        {
            if (string.IsNullOrWhiteSpace(dataset.ParquetFilePath))
            {
                orphanRows.Add(dataset);
                continue;
            }

            if (!PortalPathGuard.TryResolveDataset(config, dataset.ParquetFilePath, out var resolved))
                continue;

            if (File.Exists(resolved))
                referenced.Add(resolved);
            else
                orphanRows.Add(dataset);
        }

        if (orphanRows.Count > 0)
        {
            db.Datasets.RemoveRange(orphanRows);
            await db.SaveChangesAsync();
            logger.LogWarning(
                "Removed {Count} dataset catalog rows whose managed cache file was missing.",
                orphanRows.Count);
        }

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
