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
        private readonly Func<string, string, string?, string?, string?, SftpClient>? _clientFactory;

        public string Name => "SFTP";
        public IReadOnlyList<string> Aliases => new[] { "SSH" };

        public SftpConnector()
        {
            _logger = NullLogger.Instance;
        }

        public SftpConnector(string host, string username, string? password = null, string? keyFilePath = null, string? passphrase = null, ILogger? logger = null)
            : this(host, username, password, keyFilePath, passphrase, logger,
                  (h, u, p, k, pp) => !string.IsNullOrEmpty(k) ? new SftpClient(h, u, new PrivateKeyFile(k, pp)) : new SftpClient(h, u, p ?? ""))
        {
        }

        internal SftpConnector(string host, string username, string? password, string? keyFilePath, string? passphrase, ILogger? logger,
            Func<string, string, string?, string?, string?, SftpClient> clientFactory)
        {
            _host = host;
            _username = username;
            _password = password;
            _keyFilePath = keyFilePath;
            _passphrase = passphrase;
            _logger = logger ?? NullLogger.Instance;
            _clientFactory = clientFactory;
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

        public Task<string> GetVersionAsync(string connectionString, ILogger? logger = null) => Task.FromResult("SFTP Server");
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

        public IDataSource CreateDataSource(string connectionString, Dictionary<string, string>? options = null, ILogger? logger = null)
        {
            string user = options?.GetValueOrDefault("USER") ?? "";
            string? pass = options?.GetValueOrDefault("PASSWORD");
            string? keyFile = options?.GetValueOrDefault("KEYFILE");
            string? passphrase = options?.GetValueOrDefault("PASSPHRASE");
            return new SftpConnector(connectionString, user, pass, keyFile, passphrase, logger);
        }

        public Task<IEnumerable<string>> GetTablesAsync(string connectionString, ILogger? logger = null) => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetViewsAsync(string connectionString, ILogger? logger = null) => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetColumnsAsync(string connectionString, string tableName, ILogger? logger = null) => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetProceduresAsync(string connectionString, ILogger? logger = null) => Task.FromResult(Enumerable.Empty<string>());

        public string BuildConnectionString(Dictionary<string, string> properties) => 
            ConnectionStringBuilder.Build(Name, properties);

        private void EnsureConnected()
        {
            if (!Client.IsConnected)
            {
                Client.Connect();
            }
        }

        public Task<IEnumerable<FileMetaData>> ListFilesAsync(string path)
        {
            EnsureConnected();
            var items = Client.ListDirectory(path);
            return Task.FromResult(items.Select(i => new FileMetaData
            {
                Name = i.Name,
                FullPath = i.FullName,
                Size = i.Attributes.Size,
                LastModified = i.LastWriteTime,
                IsDirectory = i.IsDirectory
            }));
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

        public Task WriteBatches(IAsyncEnumerable<DataTable> batches) => throw new NotSupportedException("Writing batches to SFTP directly is not supported. Use FILE_SEND.");
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
    }
}
