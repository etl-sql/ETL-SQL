using ETL_SQL.Core.Data;
using ETL_SQL.ReportPortal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ETL_SQL.ReportPortal.Services
{
    public class DatasetRegistryService : IDatasetRegistry
    {
        private readonly PortalDbContext _db;
        private readonly ILogger<DatasetRegistryService> _log;
        private readonly PortalConfig _config;

        public DatasetRegistryService(PortalDbContext db, ILogger<DatasetRegistryService> log, PortalConfig config)
        {
            _db = db;
            _log = log;
            _config = config;
        }

        public async Task RegisterOrUpdate(DatasetMetadata metadata)
        {
            var existing = await _db.Datasets
                .FirstOrDefaultAsync(d => d.Name == metadata.Name && d.FolderPath == metadata.FolderPath);

            if (existing == null)
            {
                existing = new Dataset
                {
                    Name = metadata.Name,
                    FolderPath = metadata.FolderPath
                };
                _db.Datasets.Add(existing);
                _log.LogInformation("Registering new dataset: {FolderPath}/{Name}", metadata.FolderPath, metadata.Name);
            }
            else
            {
                _log.LogInformation("Updating dataset: {FolderPath}/{Name}", metadata.FolderPath, metadata.Name);
            }

            existing.ParquetFilePath = ResolveDatasetPathOrThrow(metadata.ParquetFilePath);
            existing.OwningReportId = metadata.OwningReportId;
            existing.SourceQuery = metadata.SourceQuery;
            existing.AccessLevel = metadata.AccessLevel;
            existing.LastRefresh = metadata.LastRefresh;
            existing.Ttl = metadata.Ttl;
            existing.RefreshInterval = metadata.RefreshInterval;
            existing.RowCount = metadata.RowCount;
            existing.ColumnSchema = metadata.ColumnSchema;
            existing.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
        }

        public async Task<DatasetMetadata?> Lookup(string name, string folderPath, string callerPermissions = "")
        {
            var d = await _db.Datasets
                .Include(x => x.OwningReport)
                .Include(x => x.Acls)
                .FirstOrDefaultAsync(x => x.Name == name && x.FolderPath == folderPath);

            if (d == null) return null;
            if (!await CanReadAsync(d, CallerContext.Parse(callerPermissions))) return null;

            return MapIfSafe(d);
        }

        public async Task<bool> Exists(string name, string folderPath)
        {
            return await _db.Datasets.AnyAsync(x => x.Name == name && x.FolderPath == folderPath);
        }

        public async Task SetStale(string name, string folderPath)
        {
            var d = await _db.Datasets
                .FirstOrDefaultAsync(x => x.Name == name && x.FolderPath == folderPath);
            if (d != null)
            {
                d.LastRefresh = null;
                await _db.SaveChangesAsync();
                _log.LogInformation("Dataset marked as stale: {FolderPath}/{Name}", folderPath, name);
            }
        }

        public async Task<IEnumerable<DatasetMetadata>> ListAll(string callerPermissions)
        {
            var caller = CallerContext.Parse(callerPermissions);
            var list = await _db.Datasets
                .Include(d => d.OwningReport)
                .Include(d => d.Acls)
                .ToListAsync();

            var allowed = new List<Dataset>();
            foreach (var dataset in list)
            {
                if (await CanReadAsync(dataset, caller))
                    allowed.Add(dataset);
            }

            return allowed.Select(MapIfSafe).Where(m => m is not null)!;
        }

        public async Task Delete(string name, string folderPath)
        {
            var d = await _db.Datasets
                .FirstOrDefaultAsync(x => x.Name == name && x.FolderPath == folderPath);
            if (d != null)
            {
                _db.Datasets.Remove(d);
                await _db.SaveChangesAsync();
                _log.LogInformation("Dataset deleted: {FolderPath}/{Name}", folderPath, name);
            }
        }

        private string ResolveDatasetPathOrThrow(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            if (!PortalPathGuard.TryResolveDataset(_config, path, out var resolved))
                throw new InvalidOperationException("Dataset file path must be within the configured DatasetRootPath.");

            return resolved;
        }

        private DatasetMetadata? MapIfSafe(Dataset d)
        {
            var resolved = ResolveDatasetPathOrNull(d.ParquetFilePath);
            if (resolved is null)
            {
                _log.LogWarning(
                    "Ignoring dataset '{FolderPath}/{Name}' because its file path is outside DatasetRootPath.",
                    d.FolderPath, d.Name);
                return null;
            }

            return Map(d, resolved);
        }

        private string? ResolveDatasetPathOrNull(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            return PortalPathGuard.TryResolveDataset(_config, path, out var resolved)
                ? resolved
                : null;
        }

        private async Task<bool> CanReadAsync(Dataset dataset, CallerContext caller)
        {
            if (caller.IsAdmin) return true;
            if (dataset.AccessLevel == DatasetAccessLevel.Public) return true;
            if (caller.UserId is null) return false;

            if (dataset.OwningReport?.CreatedBy == caller.UserId.Value)
                return true;

            var groupIds = await _db.UserGroups
                .Where(ug => ug.UserId == caller.UserId.Value)
                .Select(ug => ug.GroupId)
                .ToListAsync();

            return dataset.Acls.Any(a =>
                groupIds.Contains(a.GroupId)
                && a.Permission is DatasetPermission.Viewer or DatasetPermission.Editor or DatasetPermission.Owner);
        }

        private sealed record CallerContext(bool IsAdmin, int? UserId)
        {
            public static CallerContext Parse(string? value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return new CallerContext(false, null);

                var trimmed = value.Trim();
                if (trimmed.Equals("Admin", StringComparison.OrdinalIgnoreCase)
                    || trimmed.Equals("IsAdmin=true", StringComparison.OrdinalIgnoreCase))
                    return new CallerContext(true, null);

                foreach (var part in trimmed.Split(new[] { ';', ',', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (part.Equals("Admin", StringComparison.OrdinalIgnoreCase)
                        || part.Equals("IsAdmin=true", StringComparison.OrdinalIgnoreCase))
                        return new CallerContext(true, null);

                    var split = part.Split(new[] { '=', ':' }, 2, StringSplitOptions.TrimEntries);
                    if (split.Length == 2
                        && split[0].Equals("UserId", StringComparison.OrdinalIgnoreCase)
                        && int.TryParse(split[1], out var userId))
                    {
                        return new CallerContext(false, userId);
                    }
                }

                return new CallerContext(false, null);
            }
        }

        private static DatasetMetadata Map(Dataset d, string parquetFilePath)
        {
            return new DatasetMetadata
            {
                Name = d.Name,
                FolderPath = d.FolderPath,
                ParquetFilePath = parquetFilePath,
                OwningReportId = d.OwningReportId,
                SourceQuery = d.SourceQuery,
                AccessLevel = d.AccessLevel,
                LastRefresh = d.LastRefresh,
                Ttl = d.Ttl,
                RefreshInterval = d.RefreshInterval,
                RowCount = d.RowCount,
                ColumnSchema = d.ColumnSchema,
                CreatedAt = d.CreatedAt,
                UpdatedAt = d.UpdatedAt
            };
        }
    }
}
