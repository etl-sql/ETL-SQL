using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Shared;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;

namespace ETL_SQL.Connectors
{
    /// <summary>
    /// Connector for Azure Blob Storage, implementing remote file system and data source capabilities.
    /// </summary>
    public class AzureBlobConnector : IRemoteFileSystem, IDataSource, IConnector
    {
        private readonly ILogger _logger;
        private readonly IExecutionContext? _context;

        public string Name => "AZURE_BLOB";
        public IReadOnlyList<string> Aliases => new[] { "BLOB" };
        public string Path => $"azure-blob://{_containerName}";
        public Dictionary<string, string>? Options => null;
        public string ConnectorType => "AZURE_BLOB";

        private readonly BlobServiceClient? _client;
        private readonly string? _containerName;

        public AzureBlobConnector()
        {
            _logger = NullLogger.Instance;
            _containerName = "default";
        }

        public AzureBlobConnector(string connectionString, string containerName)
        {
            _logger = NullLogger.Instance;
            _client = new BlobServiceClient(connectionString);
            _containerName = containerName;
        }

        public AzureBlobConnector(IExecutionContext context, string connectionString, string containerName)
        {
            _context = context;
            _logger = context.Logger;
            _client = new BlobServiceClient(connectionString);
            _containerName = containerName;

            // Security Hardening: egress control
            var host = GetHostStatic(connectionString);
            if (host != null) context.SecurityService.ValidateHost(host);
        }


        public async Task<string> GetVersionAsync(IExecutionContext context, string connectionString)
        {
            return "Azure Blob Storage SDK 12.18.0";
        }

        public HashSet<string> GetSupportedFunctions() => new();
        public HashSet<string> GetSupportedKeywords() => new();

        public Dictionary<string, string[]> GetSupportedOptions() => new(StringComparer.OrdinalIgnoreCase)
        {
            { "CONTAINER", Array.Empty<string>() },
            { "CONNECTION_STRING", Array.Empty<string>() },
            { "ACCOUNT_NAME", Array.Empty<string>() },
            { "ACCOUNT_KEY", Array.Empty<string>() },
            { "SAS_TOKEN", Array.Empty<string>() },
            { "ENDPOINT_SUFFIX", Array.Empty<string>() },
            { "BLOB_ENDPOINT", Array.Empty<string>() }
        };

        public Dictionary<string, string[]> GetOptionValues() => new();

        public string GetHelp() =>
            "AZURE_BLOB Connector: Connects to Azure Blob Storage containers.\n" +
            "Supports listing blobs as a table and performing file transfers (GET_FILE, PUT_FILE).\n\n" +
            "Options:\n" +
            "  CONTAINER: The name of the storage container to use.\n" +
            "  CONNECTION_STRING: Full connection string (optional).\n" +
            "  ACCOUNT_NAME: Storage account name.\n" +
            "  ACCOUNT_KEY: Storage account access key (supports ENC: prefix).\n" +
            "  SAS_TOKEN: Shared Access Signature token (supports ENC: prefix).\n" +
            "  ENDPOINT_SUFFIX: Custom endpoint suffix (default: core.windows.net).\n" +
            "  BLOB_ENDPOINT: Explicit blob service endpoint URL.";

        public IDataSource CreateDataSource(IExecutionContext context, string connectionString, Dictionary<string, string>? options = null)
        {
            string? container = null;
            options?.TryGetValue("CONTAINER", out container);

            string connStr = connectionString;
            if (options != null && string.IsNullOrEmpty(connStr))
            {
                var decryptedOptions = new Dictionary<string, string>(options, StringComparer.OrdinalIgnoreCase);
                if (decryptedOptions.TryGetValue("ACCOUNT_KEY", out var key) && key.StartsWith("ENC:"))
                {
                    decryptedOptions["ACCOUNT_KEY"] = context.DecryptValue(key) ?? "";
                }
                if (decryptedOptions.TryGetValue("SAS_TOKEN", out var sas) && sas.StartsWith("ENC:"))
                {
                    decryptedOptions["SAS_TOKEN"] = context.DecryptValue(sas) ?? "";
                }
                if (decryptedOptions.TryGetValue("CONNECTION_STRING", out var cs) && cs.StartsWith("ENC:"))
                {
                    decryptedOptions["CONNECTION_STRING"] = context.DecryptValue(cs) ?? "";
                }

                connStr = BuildConnectionString(decryptedOptions);
            }
            else if (connStr.StartsWith("ENC:"))
            {
                connStr = context.DecryptValue(connStr) ?? "";
            }

            return new AzureBlobConnector(context, connStr, container ?? "default");
        }

        public Task<IEnumerable<string>> GetTablesAsync(string connectionString, ILogger? logger = null)
        {
            throw new NotSupportedException("Use IDataSource.GetTablesAsync instead or provide a context via a specialized internal call.");
        }
        public Task<IEnumerable<string>> GetTablesAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetViewsAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetColumnsAsync(IExecutionContext context, string connectionString, string tableName) => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetProceduresAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());

        public string BuildConnectionString(Dictionary<string, string> properties)
        {
            if (properties.TryGetValue("CONNECTION_STRING", out var connStr) && !string.IsNullOrEmpty(connStr))
            {
                return connStr;
            }

            if (properties.TryGetValue("ACCOUNT_NAME", out var accountName) && !string.IsNullOrEmpty(accountName))
            {
                var suffix = properties.GetValueOrDefault("ENDPOINT_SUFFIX", "core.windows.net");
                var builder = new System.Text.StringBuilder();
                builder.Append($"DefaultEndpointsProtocol=https;AccountName={accountName};");

                if (properties.TryGetValue("ACCOUNT_KEY", out var accountKey) && !string.IsNullOrEmpty(accountKey))
                {
                    builder.Append($"AccountKey={accountKey};");
                }
                else if (properties.TryGetValue("SAS_TOKEN", out var sasToken) && !string.IsNullOrEmpty(sasToken))
                {
                    builder.Append($"SharedAccessSignature={sasToken};");
                }

                if (properties.TryGetValue("BLOB_ENDPOINT", out var blobEndpoint) && !string.IsNullOrEmpty(blobEndpoint))
                {
                    builder.Append($"BlobEndpoint={blobEndpoint};");
                }
                else
                {
                    builder.Append($"EndpointSuffix={suffix};");
                }

                return builder.ToString();
            }

            return string.Empty;
        }

        private BlobContainerClient GetContainer()
        {
            if (_client == null || _containerName == null) throw new InvalidOperationException("Connector not initialized with connection details.");
            return _client.GetBlobContainerClient(_containerName);
        }

        public IAsyncEnumerable<FileMetaData> ListFilesAsync(string path) =>
            ConnectorExceptionWrapper.WrapAsync(ListFilesCoreAsync(path), "Azure Blob", ShouldWrapProviderException);

        private async IAsyncEnumerable<FileMetaData> ListFilesCoreAsync(string path)
        {
            var container = GetContainer();
            var prefix = path ?? string.Empty;
            await foreach (var blob in container.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, default))
            {
                if (blob?.Name == null) continue;
                yield return new FileMetaData
                {
                    Name = blob.Name.Split('/').Last() ?? string.Empty,
                    FullPath = blob.Name,
                    Size = blob.Properties?.ContentLength ?? 0,
                    LastModified = blob.Properties?.LastModified?.DateTime ?? DateTime.MinValue,
                    IsDirectory = false
                };
            }
        }

        public async Task UploadFileAsync(string localPath, string remotePath, bool overwrite = true)
        {
            try
            {
                var container = GetContainer();
                var blobClient = container.GetBlobClient(remotePath);
                using var fileStream = File.OpenRead(localPath);
                await blobClient.UploadAsync(fileStream, overwrite: overwrite);
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("Azure Blob", ex);
            }
        }

        public async Task DownloadFileAsync(string remotePath, string localPath, bool overwrite = true)
        {
            try
            {
                if (!overwrite && File.Exists(localPath))
                {
                    throw new ExecutionException($"Local file already exists (overwrite=OFF): {localPath}");
                }
                var container = GetContainer();
                var blobClient = container.GetBlobClient(remotePath);
                await blobClient.DownloadToAsync(localPath);
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("Azure Blob", ex);
            }
        }

        public async Task DeleteFileAsync(string remotePath)
        {
            try
            {
                var container = GetContainer();
                var blobClient = container.GetBlobClient(remotePath);
                await blobClient.DeleteIfExistsAsync();
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("Azure Blob", ex);
            }
        }

        public async Task<bool> FileExistsAsync(string remotePath)
        {
            try
            {
                var container = GetContainer();
                var blobClient = container.GetBlobClient(remotePath);
                return await blobClient.ExistsAsync();
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("Azure Blob", ex);
            }
        }

        public async Task<bool> DirectoryExistsAsync(string remotePath)
        {
            try
            {
                var container = GetContainer();
                string prefix = remotePath.EndsWith('/') ? remotePath : remotePath + "/";
                var result = container.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, default);
                await foreach (var item in result)
                {
                    return true;
                }
                return false;
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("Azure Blob", ex);
            }
        }

        public async Task RenameFileAsync(string remoteSource, string remoteDest, bool overwrite = true)
        {
            try
            {
                var container = GetContainer();
                var srcBlob = container.GetBlobClient(remoteSource);
                var destBlob = container.GetBlobClient(remoteDest);

                if (!overwrite && await destBlob.ExistsAsync())
                    throw new ExecutionException($"Destination blob already exists: {remoteDest}");

                var operation = await destBlob.StartCopyFromUriAsync(srcBlob.Uri);
                await operation.WaitForCompletionAsync();
                await srcBlob.DeleteIfExistsAsync();
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("Azure Blob", ex);
            }
        }

        public Task CreateDirectoryAsync(string remotePath)
        {
            // Blob storage directories are virtual, no creation needed
            return Task.CompletedTask;
        }

        public async Task DeleteDirectoryAsync(string remotePath)
        {
            try
            {
                var container = GetContainer();
                string prefix = remotePath.EndsWith('/') ? remotePath : remotePath + "/";
                var blobs = container.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, default);
                await foreach (var blob in blobs)
                {
                    var blobClient = container.GetBlobClient(blob.Name);
                    await blobClient.DeleteIfExistsAsync();
                }
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("Azure Blob", ex);
            }
        }

        public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) =>
            ConnectorExceptionWrapper.WrapAsync(ReadBatchesCore(batchSize), "Azure Blob", ShouldWrapProviderException);

        private async IAsyncEnumerable<DataTable> ReadBatchesCore(int batchSize)
        {
            var table = new DataTable();
            table.SetColumns(new[] { "Name", "FullPath", "Size", "LastModified", "IsDirectory" });
            await foreach (var f in ListFilesAsync(""))
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

        public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) => throw new NotSupportedException("Writing batches to Azure Blob directly is not supported. Use FILE_SEND.");
        public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult((IEnumerable<string>)new[] { "Name", "FullPath", "Size", "LastModified", "IsDirectory" });
        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }
        public IDataSource WithTable(string tableName) => this;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public string? GetHost(string connectionString, Dictionary<string, string>? options = null) => GetHostStatic(connectionString);

        public static string? GetHostStatic(string connectionString)
        {
            var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Split('=', 2))
                .Where(p => p.Length == 2)
                .ToDictionary(p => p[0].Trim(), p => p[1].Trim(), StringComparer.OrdinalIgnoreCase);

            if (parts.TryGetValue("BlobEndpoint", out var endpoint))
            {
                if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)) return uri.Host;
            }

            if (parts.TryGetValue("AccountName", out var account))
            {
                var suffix = parts.GetValueOrDefault("EndpointSuffix") ?? "core.windows.net";
                return $"{account}.blob.{suffix}";
            }

            return null;
        }

        private static bool ShouldWrapProviderException(Exception ex) =>
            ex is RequestFailedException or InvalidOperationException or AggregateException;
    }
}
