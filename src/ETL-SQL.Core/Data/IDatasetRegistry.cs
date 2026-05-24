using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Core;

namespace ETL_SQL.Core.Data
{
    public enum DatasetAccessLevel { Private, Public }
    public enum DatasetPermission { Viewer, Editor, Owner }

    public class DatasetMetadata
    {
        public string                Name            { get; set; } = "";
        public string                FolderPath      { get; set; } = "";
        public string                ParquetFilePath { get; set; } = "";
        public int?                  OwningReportId  { get; set; }
        public string                SourceQuery     { get; set; } = "";
        public DatasetAccessLevel    AccessLevel     { get; set; } = DatasetAccessLevel.Private;
        public DatasetEncryptionMode EncryptionMode  { get; set; } = DatasetEncryptionMode.MachineBound;
        public DateTime?             LastRefresh     { get; set; }
        public string?               Ttl             { get; set; }
        public TimeSpan?             CachedTtl       { get; set; } // Parsed from Ttl at registration; avoids repeated string parsing
        public string?               RefreshInterval { get; set; }
        public long                  RowCount        { get; set; }
        public string?               ColumnSchema    { get; set; } // JSON
        public DateTime              CreatedAt       { get; set; } = DateTime.UtcNow;
        public DateTime              UpdatedAt       { get; set; } = DateTime.UtcNow;
    }

    public interface IDatasetRegistry
    {
        Task RegisterOrUpdate(DatasetMetadata metadata);
        Task<DatasetMetadata?> Lookup(string name, string folderPath, string callerPermissions = "");
        Task<bool> Exists(string name, string folderPath);
        Task SetStale(string name, string folderPath);
        Task<IEnumerable<DatasetMetadata>> ListAll(string callerPermissions);
        Task Delete(string name, string folderPath);

        /// <summary>
        /// Computes the absolute Parquet file path for a dataset within the registry's
        /// configured storage root. Deterministic: same inputs always produce the same path.
        /// </summary>
        string BuildDatasetFilePath(string name, string folderPath);
    }
}
