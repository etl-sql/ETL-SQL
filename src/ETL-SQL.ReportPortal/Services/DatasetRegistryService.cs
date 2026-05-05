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

        public DatasetRegistryService(PortalDbContext db, ILogger<DatasetRegistryService> log)
        {
            _db = db;
            _log = log;
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

            existing.ParquetFilePath = metadata.ParquetFilePath;
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

        public async Task<DatasetMetadata?> Lookup(string name, string folderPath)
        {
            var d = await _db.Datasets
                .FirstOrDefaultAsync(x => x.Name == name && x.FolderPath == folderPath);

            if (d == null) return null;

            return Map(d);
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
            // TODO: Implement actual ACL filtering based on callerPermissions
            var list = await _db.Datasets.ToListAsync();
            return list.Select(Map);
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

        private static DatasetMetadata Map(Dataset d)
        {
            return new DatasetMetadata
            {
                Name = d.Name,
                FolderPath = d.FolderPath,
                ParquetFilePath = d.ParquetFilePath,
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
