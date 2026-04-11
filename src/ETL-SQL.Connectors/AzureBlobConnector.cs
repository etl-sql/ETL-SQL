using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using ETL_SQL.Data;
using ETL_SQL.Common;
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
        private readonly ILogger _logger;

        public string Name => "AZURE_BLOB";
        public IReadOnlyList<string> Aliases => new[] { "BLOB" };
        public string Path => $"azure-blob://{_containerName}";
        public Dictionary<string, string>? Options => null;
        public string ConnectorType => "AZURE_BLOB";

        public AzureBlobConnector(string connectionString, string containerName, ILogger? logger = null)
        {
            _client = new BlobServiceClient(connectionString);
            _containerName = containerName;
            _logger = logger ?? NullLogger.Instance;
        }

        public Task<string> GetVersionAsync(string connectionString, ILogger? logger = null) => Task.FromResult("Azure Blob Storage");

        public HashSet<string> GetSupportedFunctions() => new();
        public HashSet<string> GetSupportedKeywords() => new();

        public Dictionary<string, string[]> GetSupportedOptions() => new(StringComparer.OrdinalIgnoreCase)
        {
            { "CONTAINER", Array.Empty<string>() }
        };

        public Dictionary<string, string[]> GetOptionValues() => new();

        public string GetHelp() => 
            "AZURE_BLOB Connector: Connects to Azure Blob Storage containers.\n" +
            "Supports listing blobs as a table and performing file transfers (GET_FILE, PUT_FILE).\n\n" +
            "Options:\n" +
            "  CONTAINER: The name of the storage container to use.";

        public IDataSource CreateDataSource(string connectionString, Dictionary<string, string>? options = null, ILogger? logger = null)
        {
            string? container = null;
            options?.TryGetValue("CONTAINER", out container);
            return new AzureBlobConnector(connectionString, container ?? "default", logger);
        }

        public Task<IEnumerable<string>> GetTablesAsync(string connectionString, ILogger? logger = null) => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetViewsAsync(string connectionString, ILogger? logger = null) => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetColumnsAsync(string connectionString, string tableName, ILogger? logger = null) => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetProceduresAsync(string connectionString, ILogger? logger = null) => Task.FromResult(Enumerable.Empty<string>());

        public string BuildConnectionString(Dictionary<string, string> properties) => 
            ConnectionStringBuilder.Build(Name, properties);

        private BlobContainerClient GetContainer() => _client.GetBlobContainerClient(_containerName);

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

        public async Task UploadFileAsync(string localPath, string remotePath, bool overwrite = true)
        {
            var container = GetContainer();
            var blobClient = container.GetBlobClient(remotePath);
            using var fileStream = File.OpenRead(localPath);
            await blobClient.UploadAsync(fileStream, overwrite: overwrite);
        }

        public async Task DownloadFileAsync(string remotePath, string localPath, bool overwrite = true)
        {
            if (!overwrite && File.Exists(localPath))
            {
                throw new ExecutionException($"Local file already exists (overwrite=OFF): {localPath}");
            }
            var container = GetContainer();
            var blobClient = container.GetBlobClient(remotePath);
            await blobClient.DownloadToAsync(localPath);
        }

        public async Task DeleteFileAsync(string remotePath)
        {
            var container = GetContainer();
            var blobClient = container.GetBlobClient(remotePath);
            await blobClient.DeleteIfExistsAsync();
        }

        public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000)
        {
            var files = await ListFilesAsync("");
            var table = new DataTable();
            table.ColumnNames.AddRange(new[] { "Name", "FullPath", "Size", "LastModified", "IsDirectory" });
            foreach (var f in files)
            {
                await table.AddRowAsync(new Row
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

        public Task WriteBatches(IAsyncEnumerable<DataTable> batches) => throw new NotSupportedException("Writing batches to Azure Blob directly is not supported. Use FILE_SEND.");
        public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult((IEnumerable<string>)new[] { "Name", "FullPath", "Size", "LastModified", "IsDirectory" });
        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }
        public IDataSource WithTable(string tableName) => this;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
