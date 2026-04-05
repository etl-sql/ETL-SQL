using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using ETL_SQL.Data;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Connectors
{
    /// <summary>
    /// Connector for Azure Blob Storage, implementing remote file system and data source capabilities.
    /// </summary>
    public class AzureBlobConnector : IRemoteFileSystem, IDataSource, IConnector
    {
        private readonly BlobServiceClient _client;
        private readonly string _containerName;

        /// <summary>Returns the canonical name of the connector.</summary>
        public string Name => "AZURE_BLOB";
        
        /// <summary>Returns synonymous names for this connector.</summary>
        public IReadOnlyList<string> Aliases => new[] { "BLOB" };
        
        /// <summary>Gets the virtual path for the blob container.</summary>
        public string Path => $"azure-blob://{_containerName}";

        /// <summary>
        /// Initializes a new instance of the <see cref="AzureBlobConnector"/> class.
        /// </summary>
        /// <param name="connectionString">The Azure Storage connection string.</param>
        /// <param name="containerName">The target container name.</param>
        public AzureBlobConnector(string connectionString, string containerName)
        {
            _client = new BlobServiceClient(connectionString);
            _containerName = containerName;
        }

        /// <summary>Retrieves the version information for the connector.</summary>
        public Task<string> GetVersionAsync(string connectionString) => Task.FromResult("Azure Blob Storage");

        /// <summary>Returns a list of supported SQL functions for this connector.</summary>
        public HashSet<string> GetSupportedFunctions() => new();

        /// <summary>Returns a list of supported SQL keywords for this connector.</summary>
        public HashSet<string> GetSupportedKeywords() => new();

        /// <summary>Returns supported connection string options for this connector.</summary>
        public Dictionary<string, string[]> GetSupportedOptions() => new(StringComparer.OrdinalIgnoreCase)
        {
            { "CONTAINER", Array.Empty<string>() }
        };

        /// <summary>Returns a map of option keys to their current selected values.</summary>
        public Dictionary<string, string[]> GetOptionValues() => new();

        /// <summary>Returns a human-readable help string for the connector.</summary>
        /// <summary>Returns a human-readable help string for the Azure Blob connector.</summary>
        public string GetHelp() => 
            "AZURE_BLOB Connector: Connects to Azure Blob Storage containers.\n" +
            "Supports listing blobs as a table and performing file transfers (GET_FILE, PUT_FILE).\n\n" +
            "Options:\n" +
            "  CONTAINER: The name of the storage container to use.";


        /// <summary>Creates a new data source instance for this connector.</summary>
        public IDataSource CreateDataSource(string connectionString, Dictionary<string, string>? options = null)
        {
            string? container = null;
            options?.TryGetValue("CONTAINER", out container);
            return new AzureBlobConnector(connectionString, container ?? "default");
        }

        /// <summary>Returns a list of logical tables from the connection source.</summary>
        public Task<IEnumerable<string>> GetTablesAsync(string connectionString) => Task.FromResult(Enumerable.Empty<string>());
        
        /// <summary>Returns a list of logical views from the connection source.</summary>
        public Task<IEnumerable<string>> GetViewsAsync(string connectionString) => Task.FromResult(Enumerable.Empty<string>());
        
        /// <summary>Returns a list of columns for the specified table.</summary>
        public Task<IEnumerable<string>> GetColumnsAsync(string connectionString, string tableName) => Task.FromResult(Enumerable.Empty<string>());
        
        /// <summary>Returns a list of procedures/functions from the connection source.</summary>
        public Task<IEnumerable<string>> GetProceduresAsync(string connectionString) => Task.FromResult(Enumerable.Empty<string>());

        private BlobContainerClient GetContainer() => _client.GetBlobContainerClient(_containerName);

        /// <summary>Lists files (blobs) in the specified container path.</summary>
        public async Task<IEnumerable<FileMetaData>> ListFilesAsync(string path)
        {
            var container = GetContainer();
            var results = new List<FileMetaData>();
            await foreach (var blob in container.GetBlobsAsync(BlobTraits.None, BlobStates.None, path, default))
            {
                results.Add(new FileMetaData
                {
                    Name = blob.Name.Split('/').Last(),
                    FullPath = blob.Name,
                    Size = blob.Properties.ContentLength ?? 0,
                    LastModified = blob.Properties.LastModified?.DateTime ?? DateTime.MinValue,
                    IsDirectory = false
                });
            }
            return results;
        }

        /// <summary>Uploads a local file to Azure Blob Storage.</summary>
        public async Task UploadFileAsync(string localPath, string remotePath)
        {
            var container = GetContainer();
            var blobClient = container.GetBlobClient(remotePath);
            using var fileStream = File.OpenRead(localPath);
            await blobClient.UploadAsync(fileStream, overwrite: true);
        }

        /// <summary>Downloads a file from Azure Blob Storage to a local path.</summary>
        public async Task DownloadFileAsync(string remotePath, string localPath)
        {
            var container = GetContainer();
            var blobClient = container.GetBlobClient(remotePath);
            await blobClient.DownloadToAsync(localPath);
        }

        /// <summary>Deletes a file (blob) from Azure Blob Storage.</summary>
        public async Task DeleteFileAsync(string remotePath)
        {
            var container = GetContainer();
            var blobClient = container.GetBlobClient(remotePath);
            await blobClient.DeleteIfExistsAsync();
        }

        // IDataSource Implementation
        /// <summary>Reads data from Azure Blob list as a virtual table.</summary>
        public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000)
        {
            var files = await ListFilesAsync("");
            var table = new DataTable();
            table.ColumnNames.AddRange(new[] { "Name", "FullPath", "Size", "LastModified", "IsDirectory" });
            foreach (var f in files)
            {
                table.AddRow(new Row
                {
                    ["Name"] = f.Name,
                    ["FullPath"] = f.FullPath,
                    ["Size"] = f.Size,
                    ["LastModified"] = f.LastModified,
                    ["IsDirectory"] = f.IsDirectory
                });
            }
            yield return table;
        }

        /// <summary>Writes batches of data (not supported for direct blob data source).</summary>
        public Task WriteBatches(IAsyncEnumerable<DataTable> batches) => throw new NotSupportedException("Writing batches to Azure Blob directly is not supported. Use FILE_SEND.");

        /// <summary>Returns the virtual columns for the blob list.</summary>
        public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult((IEnumerable<string>)new[] { "Name", "FullPath", "Size", "LastModified", "IsDirectory" });

        /// <summary>Captures a snapshot (no-op for Azure Blob).</summary>
        public object? Snapshot() => null;

        /// <summary>Restores from a snapshot (no-op for Azure Blob).</summary>
        public void Restore(object? snapshot) { }

        /// <summary>Returns this instance as a typed table.</summary>
        public IDataSource WithTable(string tableName) => this;

        /// <summary>Asynchronously disposes resources.</summary>
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
