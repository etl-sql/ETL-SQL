using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Renci.SshNet;
using ETL_SQL.Data;
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
        private readonly Func<string, string, string?, string?, string?, SftpClient>? _clientFactory;

        public string Name => "SFTP";
        public IReadOnlyList<string> Aliases => new[] { "SSH" };

        public SftpConnector()
        {
        }

        public SftpConnector(string host, string username, string? password = null, string? keyFilePath = null, string? passphrase = null)
            : this(host, username, password, keyFilePath, passphrase, 
                  (h, u, p, k, pp) => !string.IsNullOrEmpty(k) ? new SftpClient(h, u, new PrivateKeyFile(k, pp)) : new SftpClient(h, u, p ?? ""))
        {
        }

        internal SftpConnector(string host, string username, string? password, string? keyFilePath, string? passphrase, 
            Func<string, string, string?, string?, string?, SftpClient> clientFactory)
        {
            _host = host;
            _username = username;
            _password = password;
            _keyFilePath = keyFilePath;
            _passphrase = passphrase;
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

        public async Task<string> GetVersionAsync(string connectionString) => "SFTP Server";
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
        public string GetHelp() => "SFTP Connector for remote file operations over SSH.\nOptions:\n  USER: The username for the SSH connection.\n  PASSWORD: The password for the SSH connection.\n  KEYFILE: Path to the private key file for authentication.\n  PASSPHRASE: The passphrase for the private key file.\nMethods: GET_FILE, PUT_FILE, REMOTE_FILE_LIST.";

        public IDataSource CreateDataSource(string connectionString, Dictionary<string, string>? options = null)
        {
            string user = options?.GetValueOrDefault("USER") ?? "";
            string pass = options?.GetValueOrDefault("PASSWORD");
            string keyFile = options?.GetValueOrDefault("KEYFILE");
            string passphrase = options?.GetValueOrDefault("PASSPHRASE");
            return new SftpConnector(connectionString, user, pass, keyFile, passphrase);
        }

        public Task<IEnumerable<string>> GetTablesAsync(string connectionString) => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetViewsAsync(string connectionString) => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetColumnsAsync(string connectionString, string tableName) => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetProceduresAsync(string connectionString) => Task.FromResult(Enumerable.Empty<string>());

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

        public async Task UploadFileAsync(string localPath, string remotePath)
        {
            EnsureConnected();
            using var fileStream = File.OpenRead(localPath);
            await Task.Run(() => Client.UploadFile(fileStream, remotePath));
        }

        public async Task DownloadFileAsync(string remotePath, string localPath)
        {
            EnsureConnected();
            using var fileStream = File.Create(localPath);
            await Task.Run(() => Client.DownloadFile(remotePath, fileStream));
        }

        public async Task DeleteFileAsync(string remotePath)
        {
            EnsureConnected();
            await Task.Run(() => Client.DeleteFile(remotePath));
        }

        // IDataSource Implementation
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
