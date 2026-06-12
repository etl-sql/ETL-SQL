using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ETL_SQL.Core.Data;
using ETL_SQL.ReportPortal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ETL_SQL.ReportPortal.Services
{
    public class DatasetRegistryService : IDatasetRegistry
    {
        private readonly PortalDbContext _db;
        private readonly ILogger<DatasetRegistryService> _log;
        private readonly PortalConfig _config;
        private readonly DatasetPermissionService _permissions;

        public DatasetRegistryService(PortalDbContext db, ILogger<DatasetRegistryService> log, PortalConfig config, DatasetPermissionService permissions)
        {
            _db = db;
            _log = log;
            _config = config;
            _permissions = permissions;
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
            if (!string.IsNullOrWhiteSpace(metadata.ParquetFilePath))
            {
                existing.AtRestKeyVersion = metadata.AtRestKeyVersion
                    ?? (!string.IsNullOrWhiteSpace(_config.Dataset.AtRestKey)
                        ? _config.Dataset.AtRestKeyVersion
                        : null);
                if (!string.IsNullOrWhiteSpace(_config.Dataset.AtRestKey))
                    existing.EncryptionMode = ETL_SQL.Core.DatasetEncryptionMode.MachineBound;
            }
            existing.OwningReportId = metadata.OwningReportId;
            existing.CreatedBy = metadata.CreatedBy;
            // Link to a folder for PUBLIC access checks: from the owning report when there is one,
            // otherwise (e.g. a published dataset) resolve the target folder by its logical Path.
            existing.FolderId = metadata.OwningReportId is int rid
                ? await _db.Reports.Where(r => r.Id == rid).Select(r => (int?)r.FolderId).FirstOrDefaultAsync()
                : await _db.Folders.Where(f => f.Path == metadata.FolderPath).Select(f => (int?)f.Id).FirstOrDefaultAsync();
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

        public async Task<bool> CanEditAsync(string name, string callerPermissions)
        {
            var d = await _db.Datasets
                .Include(x => x.OwningReport)
                .Include(x => x.Acls)
                .FirstOrDefaultAsync(x => x.Name == name);

            if (d == null) return false;
            return await CanWriteAsync(d, CallerContext.Parse(callerPermissions));
        }

        public async Task<bool> CanRefreshAsync(string name, string callerPermissions)
        {
            var d = await _db.Datasets
                .Include(x => x.OwningReport)
                .Include(x => x.Acls)
                .FirstOrDefaultAsync(x => x.Name == name);

            if (d == null) return false;
            var caller = CallerContext.Parse(callerPermissions);
            var permission = await _permissions.GetEffectivePermissionAsync(
                d,
                caller.UserId,
                caller.IsAdmin);
            return DatasetPermissionService.CanRefresh(permission);
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
                var managedPath = ResolveDatasetPathOrNull(d.ParquetFilePath);
                _db.Datasets.Remove(d);
                await _db.SaveChangesAsync();

                if (!string.IsNullOrWhiteSpace(managedPath))
                {
                    try
                    {
                        if (File.Exists(managedPath))
                            File.Delete(managedPath);
                    }
                    catch (Exception ex)
                    {
                        _log.LogWarning(
                            ex,
                            "Dataset '{Name}' was removed from the catalog but its managed file could not be deleted: {Path}",
                            name,
                            managedPath);
                    }
                }

                _log.LogInformation("Dataset deleted: {Name}", name);
            }
        }

        public async Task RegisterRefreshJobAsync(
            int reportId,
            string orchestratorJobName,
            string refreshInterval)
        {
            var job = await _db.DatasetJobs
                .FirstOrDefaultAsync(j => j.OrchestratorJobName == orchestratorJobName);
            if (job == null)
            {
                job = new DatasetJob
                {
                    ReportId = reportId,
                    OrchestratorJobName = orchestratorJobName
                };
                _db.DatasetJobs.Add(job);
            }
            else
            {
                job.ReportId = reportId;
            }

            job.RefreshInterval = refreshInterval;
            await _db.SaveChangesAsync();
        }

        public async Task<DatasetPublishTarget?> AuthorizePublishAsync(
            string targetFolderPath,
            string callerPermissions)
        {
            if (string.IsNullOrWhiteSpace(targetFolderPath))
                return null;

            var folder = await _db.Folders
                .FirstOrDefaultAsync(f => f.Path == targetFolderPath);
            if (folder == null)
                return null;

            var caller = CallerContext.Parse(callerPermissions);
            if (!caller.IsAdmin)
            {
                if (caller.UserId is null)
                    return null;

                var groupIds = await _permissions.GetUserGroupIdsAsync(caller.UserId.Value);
                var permission = await _db.FolderAcls
                    .Where(a => a.FolderId == folder.Id && groupIds.Contains(a.GroupId))
                    .Select(a => (FolderPermission?)a.Permission)
                    .MaxAsync();
                if (permission is null || permission < FolderPermission.Manage)
                    return null;
            }

            return new DatasetPublishTarget(
                folder.Id,
                folder.Path,
                caller.UserId ?? folder.OwnerId);
        }

        public async Task AuditPublishAsync(
            int? userId,
            string datasetName,
            string targetFolderPath,
            bool succeeded,
            string? failureReason = null)
        {
            _db.AuditLogs.Add(new AuditLog
            {
                UserId = userId,
                Action = succeeded ? "PUBLISH_DATASET" : "PUBLISH_DATASET_FAILED",
                ResourceType = "Dataset",
                ResourceId = datasetName,
                Detail = succeeded
                    ? $"Published to {targetFolderPath}"
                    : $"Target {targetFolderPath}: {failureReason ?? "publish failed"}",
                Timestamp = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
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
            return await _permissions.GetEffectivePermissionAsync(
                dataset,
                caller.UserId,
                caller.IsAdmin) is not null;
        }

        private async Task<bool> CanWriteAsync(Dataset dataset, CallerContext caller)
        {
            var permission = await _permissions.GetEffectivePermissionAsync(
                dataset,
                caller.UserId,
                caller.IsAdmin);
            return DatasetPermissionService.CanEdit(permission);
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

                var isAdmin = false;
                int? parsedUserId = null;
                foreach (var part in trimmed.Split(new[] { ';', ',', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (part.Equals("Admin", StringComparison.OrdinalIgnoreCase)
                        || part.Equals("IsAdmin=true", StringComparison.OrdinalIgnoreCase))
                    {
                        isAdmin = true;
                        continue;
                    }

                    var split = part.Split(new[] { '=', ':' }, 2, StringSplitOptions.TrimEntries);
                    if (split.Length == 2
                        && split[0].Equals("UserId", StringComparison.OrdinalIgnoreCase)
                        && int.TryParse(split[1], out var userId))
                    {
                        parsedUserId = userId;
                    }
                }

                return new CallerContext(isAdmin, parsedUserId);
            }
        }

        private DatasetMetadata Map(Dataset d, string parquetFilePath)
        {
            return new DatasetMetadata
            {
                Id = d.Id,
                Name = d.Name,
                FolderPath = d.FolderPath,
                FolderId = d.FolderId,
                CreatedBy = d.CreatedBy,
                ParquetFilePath = parquetFilePath,
                AtRestKeyVersion = d.AtRestKeyVersion,
                AtRestDecryptionKey = ResolveAtRestKey(d.AtRestKeyVersion),
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

        private string? ResolveAtRestKey(string? version)
        {
            if (string.IsNullOrWhiteSpace(_config.Dataset.AtRestKey))
                return null;

            var effectiveVersion = version
                ?? _config.Dataset.LegacyAtRestKeyVersion
                ?? _config.Dataset.AtRestKeyVersion;
            if (effectiveVersion.Equals(
                    _config.Dataset.AtRestKeyVersion,
                    StringComparison.OrdinalIgnoreCase))
            {
                return _config.Dataset.AtRestKey;
            }

            return _config.Dataset.PreviousAtRestKeys.TryGetValue(effectiveVersion, out var key)
                ? key
                : null;
        }
    }
}
