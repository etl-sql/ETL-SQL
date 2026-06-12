using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Core;

namespace ETL_SQL.Core.Data
{
    public enum DatasetAccessLevel { Private, Public }
    public enum DatasetPermission
    {
        Viewer = 0,
        Refresh = 1,
        Editor = 2,
        Owner = 3
    }

    public sealed record DatasetPublishTarget(
        int FolderId,
        string FolderPath,
        int OwnerUserId);

    public class DatasetMetadata
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string FolderPath { get; set; } = "";
        public int? FolderId { get; set; }
        public int? CreatedBy { get; set; }
        public string ParquetFilePath { get; set; } = "";
        public string? AtRestKeyVersion { get; set; }
        /// <summary>Resolved in-memory decryption key; registry implementations must never persist it.</summary>
        public string? AtRestDecryptionKey { get; set; }
        public int? OwningReportId { get; set; }
        public string SourceQuery { get; set; } = "";
        public DatasetAccessLevel AccessLevel { get; set; } = DatasetAccessLevel.Private;
        public DatasetEncryptionMode EncryptionMode { get; set; } = DatasetEncryptionMode.MachineBound;
        public DateTime? LastRefresh { get; set; }
        public string? Ttl { get; set; }
        public TimeSpan? CachedTtl { get; set; } // Parsed from Ttl at registration; avoids repeated string parsing
        public string? RefreshInterval { get; set; }
        public long RowCount { get; set; }
        public string? ColumnSchema { get; set; } // JSON
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
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

        /// <summary>
        /// True if the caller may edit the named dataset (admin, the owner, or an Editor/Owner
        /// dataset grant). False if the dataset does not exist or the caller lacks write access.
        /// Used to gate CREATE OR ALTER DATASET.
        /// </summary>
        Task<bool> CanEditAsync(string name, string callerPermissions);

        /// <summary>
        /// True if the caller may refresh the named dataset (admin, the owner, or a
        /// Refresh/Editor/Owner dataset grant). Registries that do not support the distinct
        /// Refresh permission retain the legacy editor check.
        /// </summary>
        Task<bool> CanRefreshAsync(string name, string callerPermissions) =>
            CanEditAsync(name, callerPermissions);
        Task SetStale(string name);
        Task<IEnumerable<DatasetMetadata>> ListAll(string callerPermissions);
        Task Delete(string name);

        /// <summary>
        /// Registers the durable scheduler trigger that causes the portal to re-run the owning report.
        /// Registries without a report host may ignore this operation.
        /// </summary>
        Task RegisterRefreshJobAsync(
            int reportId,
            string orchestratorJobName,
            string refreshInterval) => Task.CompletedTask;

        /// <summary>
        /// Resolves and authorizes a PUBLISH DATASET destination before any dataset row is allocated.
        /// Returns null when the folder is missing or the caller lacks publish/manage permission.
        /// </summary>
        Task<DatasetPublishTarget?> AuthorizePublishAsync(
            string targetFolderPath,
            string callerPermissions) => Task.FromResult<DatasetPublishTarget?>(null);

        /// <summary>
        /// Records a sanitized publish result. Implementations must not include transport credentials.
        /// </summary>
        Task AuditPublishAsync(
            int? userId,
            string datasetName,
            string targetFolderPath,
            bool succeeded,
            string? failureReason = null) => Task.CompletedTask;

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
