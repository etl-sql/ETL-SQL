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
        public int                   Id              { get; set; }
        public string                Name            { get; set; } = "";
        public string                FolderPath      { get; set; } = "";
        public int?                  FolderId        { get; set; }
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
        /// <summary>
        /// Inserts or updates the dataset (keyed by globally unique <see cref="DatasetMetadata.Name"/>)
        /// and returns its stable database Id. The returned Id is used to derive the on-disk
        /// Parquet filename via <see cref="BuildDatasetFilePath"/>.
        /// </summary>
        Task<int> RegisterOrUpdate(DatasetMetadata metadata);
        Task<DatasetMetadata?> Lookup(string name, string callerPermissions = "");
        Task<bool> Exists(string name);
        Task SetStale(string name);
        Task<IEnumerable<DatasetMetadata>> ListAll(string callerPermissions);
        Task Delete(string name);

        /// <summary>
        /// Computes the absolute Parquet file path for a dataset within the registry's
        /// configured storage root. Deterministic: same inputs always produce the same path.
        /// The filename is based on the stable <paramref name="datasetId"/> so moving (or
        /// renaming) a dataset never rewrites its file; <paramref name="name"/> is only a
        /// human-readable prefix.
        /// </summary>
        string BuildDatasetFilePath(int datasetId, string name);
    }
}
