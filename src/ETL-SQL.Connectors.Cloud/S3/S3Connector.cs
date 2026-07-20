using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Shared;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using IExecutionContext = ETL_SQL.Core.IExecutionContext;

namespace ETL_SQL.Connectors.S3
{
    public class S3Connector : IRemoteFileSystem, IDataSource, IConnector, IAsyncDisposable
    {
        private readonly ILogger _logger;
        private readonly IExecutionContext? _context;
        private IAmazonS3? _client;

        private readonly string _bucketName = "";
        private readonly string _endpoint = "";
        private readonly string _accessKey = "";
        private readonly string _secretKey = "";
        private readonly string _region = "us-east-1";
        private readonly bool _forcePathStyle = false;
        private readonly Dictionary<string, string> _options = new(StringComparer.OrdinalIgnoreCase);

        public string Name => "S3";
        public IReadOnlyList<string> Aliases => new[] { "AWS_S3" };
        public string Path => _bucketName;
        public Dictionary<string, string>? Options => _options;
        public string ConnectorType => "S3";

        public S3Connector()
        {
            _logger = NullLogger.Instance;
        }

        public S3Connector(IExecutionContext context, string connectionString, Dictionary<string, string>? options = null, IAmazonS3? client = null)
        {
            _context = context;
            _logger = context.Logger;
            _bucketName = connectionString;

            if (options != null)
            {
                foreach (var kv in options)
                {
                    _options[kv.Key] = kv.Value;
                }
            }

            _bucketName = _options.GetValueOrDefault("BUCKET", _bucketName);
            _endpoint = _options.GetValueOrDefault("ENDPOINT", "");
            _accessKey = _options.GetValueOrDefault("ACCESS_KEY", "");
            _secretKey = _options.GetValueOrDefault("SECRET_KEY", "");
            _region = _options.GetValueOrDefault("REGION", "us-east-1");

            var forcePathStr = _options.GetValueOrDefault("FORCE_PATH_STYLE", "FALSE");
            _forcePathStyle = forcePathStr.Equals("TRUE", StringComparison.OrdinalIgnoreCase);

            // Egress Security Hardening: Validate host against egress policies
            var host = GetHost(connectionString, _options);
            if (!string.IsNullOrEmpty(host))
            {
                ETL_SQL.Core.Governance.ConnectorPolicyAuthorizer.EnforceEnterpriseHost(context, host);
            }

            if (client != null)
            {
                _client = client;
            }
        }

        private IAmazonS3 GetClient()
        {
            if (_context != null)
                ETL_SQL.Core.Governance.ConnectorPolicyAuthorizer.EnforceEnterpriseHost(
                    _context, GetHost(_bucketName, _options));
            if (_client != null) return _client;

            var config = new AmazonS3Config();
            if (!string.IsNullOrEmpty(_endpoint))
            {
                config.ServiceURL = _endpoint;
            }
            else
            {
                config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(_region);
            }

            config.ForcePathStyle = _forcePathStyle;

            string secret = _secretKey;
            if (secret.StartsWith("ENC:") && _context != null)
            {
                secret = _context.DecryptValue(secret) ?? "";
            }

            string sessionToken = _options.GetValueOrDefault("SESSION_TOKEN", "");

            if (string.IsNullOrEmpty(_accessKey) || string.IsNullOrEmpty(secret))
            {
                _client = new AmazonS3Client(new AnonymousAWSCredentials(), config);
            }
            else if (!string.IsNullOrEmpty(sessionToken))
            {
                _client = new AmazonS3Client(_accessKey, secret, sessionToken, config);
            }
            else
            {
                _client = new AmazonS3Client(_accessKey, secret, config);
            }

            return _client;
        }

        public async Task<string> GetVersionAsync(IExecutionContext context, string connectionString)
        {
            var opts = _options;
            var bucket = opts.GetValueOrDefault("BUCKET", connectionString);
            if (string.IsNullOrWhiteSpace(bucket))
            {
                return "S3 Storage Connector v1.0 (Offline - No Bucket Specified)";
            }

            var host = GetHost(connectionString, opts);
            if (!string.IsNullOrEmpty(host))
            {
                ETL_SQL.Core.Governance.ConnectorPolicyAuthorizer.EnforceEnterpriseHost(context, host);
            }

            try
            {
                var client = GetClient();
                var request = new ListObjectsV2Request
                {
                    BucketName = bucket,
                    MaxKeys = 1
                };
                var response = await client.ListObjectsV2Async(request);
                return $"S3 Storage Connector v1.0 (Connected - Bucket: {bucket}, Status: {response.HttpStatusCode})";
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("S3", ex);
            }
        }

        public HashSet<string> GetSupportedFunctions() => new();
        public HashSet<string> GetSupportedKeywords() => new();

        public Dictionary<string, string[]> GetSupportedOptions() => new(StringComparer.OrdinalIgnoreCase)
        {
            { "BUCKET", Array.Empty<string>() },
            { "ENDPOINT", Array.Empty<string>() },
            { "ACCESS_KEY", Array.Empty<string>() },
            { "SECRET_KEY", Array.Empty<string>() },
            { "SESSION_TOKEN", Array.Empty<string>() },
            { "REGION", Array.Empty<string>() },
            { "FORCE_PATH_STYLE", new[] { "TRUE", "FALSE" } }
        };

        public Dictionary<string, string[]> GetOptionValues() => new(StringComparer.OrdinalIgnoreCase)
        {
            { "FORCE_PATH_STYLE", new[] { "TRUE", "FALSE" } }
        };

        public string GetHelp() =>
            "S3 Connector: Transfer files to and from AWS S3 or S3-compatible cloud object storage.\n" +
            "Supports: SEND FILE, RECEIVE FILE, DELETE FILE, RENAME FILE, FILE EXISTS checks.\n\n" +
            "Options:\n" +
            "  BUCKET: S3 bucket name (required).\n" +
            "  ENDPOINT: Custom URL for S3-compatible providers (R2, GCS, MinIO, LocalStack).\n" +
            "  ACCESS_KEY / SECRET_KEY: Cloud connection credentials (omit for public read access).\n" +
            "  REGION: AWS region context (default: us-east-1).\n" +
            "  FORCE_PATH_STYLE: Set to TRUE for local storage emulators like MinIO.";

        public IDataSource CreateDataSource(IExecutionContext context, string connectionString, Dictionary<string, string>? options = null)
        {
            return new S3Connector(context, connectionString, options);
        }

        public Task<IEnumerable<string>> GetTablesAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetViewsAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetColumnsAsync(IExecutionContext context, string connectionString, string tableName) => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetProceduresAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());

        public string BuildConnectionString(Dictionary<string, string> properties)
        {
            return properties.GetValueOrDefault("BUCKET", "");
        }

        public string? GetHost(string connectionString, Dictionary<string, string>? options = null)
        {
            var opts = options ?? _options;
            string endpoint = opts.GetValueOrDefault("ENDPOINT", "");
            if (!string.IsNullOrEmpty(endpoint) && Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            {
                return uri.Host;
            }

            string region = opts.GetValueOrDefault("REGION", "us-east-1");
            return $"s3.{region}.amazonaws.com";
        }

        // ── IRemoteFileSystem Implementation ──────────────────────────────────────

        public async IAsyncEnumerable<FileMetaData> ListFilesAsync(string path)
        {
            var client = GetClient();
            string prefix = path.Replace('\\', '/').Trim('/');
            if (!string.IsNullOrEmpty(prefix))
            {
                prefix += "/";
            }

            var request = new ListObjectsV2Request
            {
                BucketName = _bucketName,
                Prefix = prefix
            };

            ListObjectsV2Response response;
            try
            {
                response = await client.ListObjectsV2Async(request);
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("S3", ex);
            }

            foreach (var obj in response.S3Objects)
            {
                string name = System.IO.Path.GetFileName(obj.Key);
                yield return new FileMetaData
                {
                    Name = name,
                    FullPath = obj.Key,
                    Size = obj.Size ?? 0L,
                    LastModified = obj.LastModified,
                    IsDirectory = false
                };
            }
        }

        public async Task UploadFileAsync(string localPath, string remotePath, bool overwrite = true)
        {
            var client = GetClient();
            if (!File.Exists(localPath))
            {
                throw new ExecutionException($"Local source file not found: {localPath}");
            }

            string key = remotePath.Replace('\\', '/').TrimStart('/');

            // Check overwrite rules
            if (!overwrite && await FileExistsAsync(key))
            {
                throw new ExecutionException($"Remote destination file already exists (overwrite=OFF): {key}");
            }

            var request = new PutObjectRequest
            {
                BucketName = _bucketName,
                Key = key,
                FilePath = localPath
            };

            try
            {
                await client.PutObjectAsync(request);
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("S3", ex);
            }
        }

        public async Task DownloadFileAsync(string remotePath, string localPath, bool overwrite = true)
        {
            var client = GetClient();
            if (!overwrite && File.Exists(localPath))
            {
                throw new ExecutionException($"Local destination file already exists (overwrite=OFF): {localPath}");
            }

            string key = remotePath.Replace('\\', '/').TrimStart('/');

            var request = new GetObjectRequest
            {
                BucketName = _bucketName,
                Key = key
            };

            try
            {
                using var response = await client.GetObjectAsync(request);

                string? dir = System.IO.Path.GetDirectoryName(localPath);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                {
                    System.IO.Directory.CreateDirectory(dir);
                }

                using var fileStream = File.Create(localPath);
                await response.ResponseStream.CopyToAsync(fileStream);
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("S3", ex);
            }
        }

        public async Task DeleteFileAsync(string remotePath)
        {
            var client = GetClient();
            string key = remotePath.Replace('\\', '/').TrimStart('/');

            var request = new DeleteObjectRequest
            {
                BucketName = _bucketName,
                Key = key
            };

            try
            {
                await client.DeleteObjectAsync(request);
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("S3", ex);
            }
        }

        public async Task<bool> FileExistsAsync(string remotePath)
        {
            var client = GetClient();
            string key = remotePath.Replace('\\', '/').TrimStart('/');

            try
            {
                await client.GetObjectMetadataAsync(_bucketName, key);
                return true;
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("S3", ex);
            }
        }

        public async Task<bool> DirectoryExistsAsync(string remotePath)
        {
            var client = GetClient();
            string prefix = remotePath.Replace('\\', '/').Trim('/');
            if (!string.IsNullOrEmpty(prefix))
            {
                prefix += "/";
            }

            var request = new ListObjectsV2Request
            {
                BucketName = _bucketName,
                Prefix = prefix,
                MaxKeys = 1
            };

            try
            {
                var response = await client.ListObjectsV2Async(request);
                return response.KeyCount > 0;
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("S3", ex);
            }
        }

        public async Task RenameFileAsync(string remoteSource, string remoteDest, bool overwrite = true)
        {
            var client = GetClient();
            string srcKey = remoteSource.Replace('\\', '/').TrimStart('/');
            string destKey = remoteDest.Replace('\\', '/').TrimStart('/');

            if (!overwrite && await FileExistsAsync(destKey))
            {
                throw new ExecutionException($"Remote destination file already exists (overwrite=OFF): {destKey}");
            }

            var copyRequest = new CopyObjectRequest
            {
                SourceBucket = _bucketName,
                SourceKey = srcKey,
                DestinationBucket = _bucketName,
                DestinationKey = destKey
            };

            var deleteRequest = new DeleteObjectRequest
            {
                BucketName = _bucketName,
                Key = srcKey
            };

            try
            {
                await client.CopyObjectAsync(copyRequest);
                await client.DeleteObjectAsync(deleteRequest);
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("S3", ex);
            }
        }

        public Task CreateDirectoryAsync(string remotePath)
        {
            // S3 directories are virtual, no creation needed
            return Task.CompletedTask;
        }

        public async Task DeleteDirectoryAsync(string remotePath)
        {
            var client = GetClient();
            string prefix = remotePath.Replace('\\', '/').Trim('/');
            if (!string.IsNullOrEmpty(prefix))
            {
                prefix += "/";
            }

            try
            {
                var listRequest = new ListObjectsV2Request
                {
                    BucketName = _bucketName,
                    Prefix = prefix
                };

                ListObjectsV2Response listResponse;
                do
                {
                    listResponse = await client.ListObjectsV2Async(listRequest);
                    if (listResponse.S3Objects.Count > 0)
                    {
                        var deleteRequest = new DeleteObjectsRequest
                        {
                            BucketName = _bucketName,
                            Objects = listResponse.S3Objects.Select(o => new KeyVersion { Key = o.Key }).ToList()
                        };
                        await client.DeleteObjectsAsync(deleteRequest);
                    }
                    listRequest.ContinuationToken = listResponse.NextContinuationToken;
                } while (listResponse.IsTruncated == true);
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("S3", ex);
            }
        }

        public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) =>
            ConnectorExceptionWrapper.WrapAsync(ReadBatchesCore(batchSize), "S3", ShouldWrapProviderException);

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

        public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) => throw new NotSupportedException("Writing batches to S3 directly is not supported. Use FILE_SEND.");
        public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult((IEnumerable<string>)new[] { "Name", "FullPath", "Size", "LastModified", "IsDirectory" });
        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }
        public IDataSource WithTable(string tableName) => this;

        public async ValueTask DisposeAsync()
        {
            if (_client != null)
            {
                _client.Dispose();
                _client = null;
            }
            await Task.CompletedTask;
        }

        private static bool ShouldWrapProviderException(Exception ex) =>
            ex is AmazonS3Exception or AmazonClientException or InvalidOperationException;
    }
}
