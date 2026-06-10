using ETL_SQL.Core.Data;
using ETL_SQL.ReportPortal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ETL_SQL.ReportPortal.Services
{
    public class DatasetRegistryService : IDatasetRegistry
    {
        private readonly PortalDbContext _db;
        private readonly ILogger<DatasetRegistryService> _log;
        private readonly PortalConfig _config;
        private readonly FolderPermissionService _folderPerms;

        public DatasetRegistryService(PortalDbContext db, ILogger<DatasetRegistryService> log, PortalConfig config, FolderPermissionService folderPerms)
        {
            _db = db;
            _log = log;
            _config = config;
            _folderPerms = folderPerms;
        }

        public async Task<int> RegisterOrUpdate(DatasetMetadata metadata)
        {
            var existing = await _db.Datasets
                .FirstOrDefaultAsync(d => d.Name == metadata.Name);

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
            // Link to the owning report's folder so PUBLIC access can be gated by folder permission.
            existing.FolderId = metadata.OwningReportId is int rid
                ? await _db.Reports.Where(r => r.Id == rid).Select(r => (int?)r.FolderId).FirstOrDefaultAsync()
                : null;
            existing.SourceQuery = metadata.SourceQuery;
            existing.AccessLevel = metadata.AccessLevel;
            existing.EncryptionMode = metadata.EncryptionMode;
            existing.LastRefresh = metadata.LastRefresh;
            existing.Ttl = metadata.Ttl;
            existing.RefreshInterval = metadata.RefreshInterval;
            existing.RowCount = metadata.RowCount;
            existing.ColumnSchema = metadata.ColumnSchema;
            existing.FolderPath = metadata.FolderPath;   // mutable display metadata — a moved dataset updates its folder
            existing.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            return existing.Id;
        }

        public async Task<DatasetMetadata?> Lookup(string name, string callerPermissions = "")
        {
            var d = await _db.Datasets
                .Include(x => x.OwningReport)
                .Include(x => x.Acls)
                .FirstOrDefaultAsync(x => x.Name == name);

            if (d == null) return null;
            if (!await CanReadAsync(d, CallerContext.Parse(callerPermissions))) return null;

            return MapIfSafe(d);
        }

        public async Task<bool> Exists(string name)
        {
            return await _db.Datasets.AnyAsync(x => x.Name == name);
        }

        public async Task SetStale(string name)
        {
            var d = await _db.Datasets
                .FirstOrDefaultAsync(x => x.Name == name);
            if (d != null)
            {
                d.LastRefresh = null;
                await _db.SaveChangesAsync();
                _log.LogInformation("Dataset marked as stale: {Name}", name);
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

        public async Task Delete(string name)
        {
            var d = await _db.Datasets
                .FirstOrDefaultAsync(x => x.Name == name);
            if (d != null)
            {
                _db.Datasets.Remove(d);
                await _db.SaveChangesAsync();
                _log.LogInformation("Dataset deleted: {Name}", name);
            }
        }

        public string BuildDatasetFilePath(int datasetId, string name)
        {
            var safeName = Regex.Replace(
                name.TrimStart('&', '#'), @"[^\w\-]", "_", RegexOptions.None).ToLowerInvariant();

            // Suffix with the stable Id so moving/renaming a dataset never rewrites its file.
            var rootPath = Path.GetFullPath(_config.DatasetRootPath);
            Directory.CreateDirectory(rootPath);
            return Path.Combine(rootPath, $"{safeName}_{datasetId}.parquet");
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

            // Resolve the caller's group memberships once (empty for an unauthenticated caller).
            ISet<int> groupIds = caller.UserId is int uid
                ? (await _db.UserGroups
                    .Where(ug => ug.UserId == uid)
                    .Select(ug => ug.GroupId)
                    .ToListAsync()).ToHashSet()
                : new HashSet<int>();

            if (dataset.AccessLevel == DatasetAccessLevel.Public)
            {
                // PUBLIC = any authenticated user with Read on the dataset's folder. When the dataset
                // has no resolvable folder, fall back to "any authenticated caller".
                if (dataset.FolderId is int fid)
                    return await _folderPerms.GetEffectivePermissionAsync(fid, groupIds) is not null;
                return caller.UserId is not null;
            }

            // PRIVATE = owner or an explicit dataset grant.
            if (caller.UserId is null) return false;

            if (dataset.OwningReport?.CreatedBy == caller.UserId.Value)
                return true;

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
                Id = d.Id,
                Name = d.Name,
                FolderPath = d.FolderPath,
                FolderId = d.FolderId,
                ParquetFilePath = parquetFilePath,
                OwningReportId = d.OwningReportId,
                SourceQuery = d.SourceQuery,
                AccessLevel = d.AccessLevel,
                EncryptionMode = d.EncryptionMode,
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
