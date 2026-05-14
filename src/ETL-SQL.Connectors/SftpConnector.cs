using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Renci.SshNet;
using ETL_SQL.Data;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Connectors
{
    public class SftpConnector : IRemoteFileSystem, IDataSource, IConnector
    {
        private SftpClient? _client;
        private readonly string? _host;
        private readonly string? _username;
        private readonly string? _password;
        private readonly string? _keyFilePath;
        private readonly string? _passphrase;
        private readonly ILogger _logger;
        private readonly IExecutionContext? _context;
        private readonly Func<string, string, string?, string?, string?, SftpClient>? _clientFactory;

        public string Name => "SFTP";
        public IReadOnlyList<string> Aliases => new[] { "SSH" };

        public SftpConnector()
        {
            _logger = NullLogger.Instance;
        }

        public SftpConnector(string host, string username, string? password = null, string? keyFilePath = null, string? passphrase = null)
             : this(null!, host, username, password, keyFilePath, passphrase,
                   (h, u, p, k, pp) => !string.IsNullOrEmpty(k) ? new SftpClient(h, u, new PrivateKeyFile(k, pp)) : new SftpClient(h, u, p ?? ""))
        {
            _logger = NullLogger.Instance;
            // Note: _context will be null here, so path resolution and security validation are deferred.
        }

        public SftpConnector(IExecutionContext context, string host, string username, string? password = null, string? keyFilePath = null, string? passphrase = null)
            : this(context, host, username, password, keyFilePath, passphrase,
                  (h, u, p, k, pp) => !string.IsNullOrEmpty(k) ? new SftpClient(h, u, new PrivateKeyFile(k, pp)) : new SftpClient(h, u, p ?? ""))
        {
        }

        internal SftpConnector(IExecutionContext? context, string host, string username, string? password, string? keyFilePath, string? passphrase,
            Func<string, string, string?, string?, string?, SftpClient> clientFactory)
        {
            _context = context;
            _host = host;
            _username = username;
            _password = password;
            _keyFilePath = (string.IsNullOrEmpty(keyFilePath) || context == null) ? keyFilePath : context.ResolvePath(keyFilePath);
            _passphrase = passphrase;
            _logger = context?.Logger ?? NullLogger.Instance;
            _clientFactory = clientFactory;

            // Security Hardening: egress control
            if (context != null)
            {
                context.SecurityService.ValidateHost(host);
                
                // Validate key file path if provided
                if (_keyFilePath != null)
                {
                    context.SecurityService.ValidatePath(_keyFilePath);
                }
            }
        }

        private SftpClient Client
        {
            get
            {
                if (_client == null)
                {
                    if (_clientFactory == null || _host == null || _username == null)
                        throw new InvalidOperationException("Connector not initialized with connection details.");
                    _client = _clientFactory(_host, _username, _password, _keyFilePath, _passphrase);
                }
                return _client;
            }
        }

        public string Path => $"sftp://{_host}";
        public Dictionary<string, string>? Options => null;
        public string ConnectorType => "SFTP";

        public async Task<string> GetVersionAsync(IExecutionContext context, string connectionString)
        {
            // Security constraint: validate host before connecting
            var host = GetHost(connectionString);
            if (host != null) context.SecurityService.ValidateHost(host);
            return await Task.FromResult("SFTP Server");
        }
        public HashSet<string> GetSupportedFunctions() => new();
        public HashSet<string> GetSupportedKeywords() => new();
        public Dictionary<string, string[]> GetSupportedOptions() => new() 
        { 
            ["USER"] = new[] { "Username for SSH" }, 
            ["PASSWORD"] = new[] { "Password for SSH" },
            ["KEYFILE"] = new[] { "Path to the private key file" },
            ["PASSPHRASE"] = new[] { "Passphrase for the private key" }
        };
        public Dictionary<string, string[]> GetOptionValues() => new();
        public string GetHelp() => "SFTP Connector for remote file operations over SSH.";

        public IDataSource CreateDataSource(IExecutionContext context, string connectionString, Dictionary<string, string>? options = null)
        {
            string user = options?.GetValueOrDefault("USER") ?? "";
            string? pass = options?.GetValueOrDefault("PASSWORD");
            string? keyFile = options?.GetValueOrDefault("KEYFILE");
            string? passphrase = options?.GetValueOrDefault("PASSPHRASE");
            return new SftpConnector(context, connectionString, user, pass, keyFile, passphrase);
        }

        public Task<IEnumerable<string>> GetTablesAsync(IExecutionContext context, string connectionString) => throw new NotSupportedException("Use IDataSource.GetTablesAsync instead.");
        public Task<IEnumerable<string>> GetViewsAsync(IExecutionContext context, string connectionString) => throw new NotSupportedException("Use IDataSource.GetViewsAsync instead.");
        public Task<IEnumerable<string>> GetColumnsAsync(IExecutionContext context, string connectionString, string tableName) => throw new NotSupportedException("Use IDataSource.GetColumnsAsync instead.");
        public Task<IEnumerable<string>> GetProceduresAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());

        public string BuildConnectionString(Dictionary<string, string> properties) => 
            ConnectionStringBuilder.Build(Name, properties);

        private void EnsureConnected()
        {
            if (!Client.IsConnected)
            {
                Client.Connect();
            }
        }

        public async IAsyncEnumerable<FileMetaData> ListFilesAsync(string path)
        {
            await Task.CompletedTask;
            EnsureConnected();
            foreach (var i in Client.ListDirectory(path))
                yield return new FileMetaData
                {
                    Name = i.Name,
                    FullPath = i.FullName,
                    Size = i.Attributes.Size,
                    LastModified = i.LastWriteTime,
                    IsDirectory = i.IsDirectory
                };
        }

        public async Task UploadFileAsync(string localPath, string remotePath, bool overwrite = true)
        {
            EnsureConnected();
            if (!overwrite && await Task.Run(() => Client.Exists(remotePath)))
            {
                throw new ExecutionException($"Remote file already exists: {remotePath}");
            }
            using var fileStream = File.OpenRead(localPath);
            await Task.Run(() => Client.UploadFile(fileStream, remotePath));
        }

        public async Task DownloadFileAsync(string remotePath, string localPath, bool overwrite = true)
        {
            EnsureConnected();
            if (!overwrite && File.Exists(localPath))
            {
                throw new ExecutionException($"Local file already exists: {localPath}");
            }
            using var fileStream = File.Open(localPath, overwrite ? FileMode.Create : FileMode.CreateNew);
            await Task.Run(() => Client.DownloadFile(remotePath, fileStream));
        }

        public async Task DeleteFileAsync(string remotePath)
        {
            EnsureConnected();
            await Task.Run(() => Client.DeleteFile(remotePath));
        }

        public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000)
        {
            var table = new DataTable();
            table.ColumnNames.AddRange(new[] { "Name", "FullPath", "Size", "LastModified", "IsDirectory" });
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

        public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) => throw new NotSupportedException("Writing batches to SFTP directly is not supported. Use FILE_SEND.");
        public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult((IEnumerable<string>)new[] { "Name", "FullPath", "Size", "LastModified", "IsDirectory" });
        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }
        public IDataSource WithTable(string tableName) => this;

        public async ValueTask DisposeAsync()
        {
            if (_client != null)
            {
                if (_client.IsConnected)
                {
                    _client.Disconnect();
                }
                _client.Dispose();
            }
            await Task.CompletedTask;
        }

        public string? GetHost(string connectionString, Dictionary<string, string>? options = null)
        {
            if (options != null && options.TryGetValue("HOST", out var host)) return host;
            return connectionString;
        }
    }
}
