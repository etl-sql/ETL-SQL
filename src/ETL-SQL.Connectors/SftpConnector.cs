using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;
using Renci.SshNet.Common;
using ETL_SQL.Data;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Connectors.Shared;

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
        private readonly int _timeoutSeconds = 30;
        private readonly ILogger _logger;
        private readonly IExecutionContext? _context;
        private readonly Func<string, string, string?, string?, string?, SftpClient>? _clientFactory;
        private readonly SemaphoreSlim _clientLock = new(1, 1);

        public string Name => "SFTP";
        public IReadOnlyList<string> Aliases => new[] { "SSH" };

        public SftpConnector()
        {
            _logger = NullLogger.Instance;
        }

        public SftpConnector(string host, string username, string? password = null, string? keyFilePath = null, string? passphrase = null, int timeoutSeconds = 30)
             : this(null!, host, username, password, keyFilePath, passphrase, timeoutSeconds,
                   (h, u, p, k, pp) => {
                       var info = !string.IsNullOrEmpty(k)
                           ? new Renci.SshNet.ConnectionInfo(h, u, new PrivateKeyAuthenticationMethod(u, new PrivateKeyFile(k, pp)))
                           : new Renci.SshNet.ConnectionInfo(h, u, new PasswordAuthenticationMethod(u, p ?? ""));
                       info.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
                       return new SftpClient(info);
                   })
        {
            _logger = NullLogger.Instance;
            // Note: _context will be null here, so path resolution and security validation are deferred.
        }

        public SftpConnector(IExecutionContext context, string host, string username, string? password = null, string? keyFilePath = null, string? passphrase = null, int timeoutSeconds = 30)
            : this(context, host, username, password, keyFilePath, passphrase, timeoutSeconds,
                  (h, u, p, k, pp) => {
                      var info = !string.IsNullOrEmpty(k)
                          ? new Renci.SshNet.ConnectionInfo(h, u, new PrivateKeyAuthenticationMethod(u, new PrivateKeyFile(k, pp)))
                          : new Renci.SshNet.ConnectionInfo(h, u, new PasswordAuthenticationMethod(u, p ?? ""));
                      info.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
                      return new SftpClient(info);
                  })
        {
        }

        internal SftpConnector(IExecutionContext? context, string host, string username, string? password, string? keyFilePath, string? passphrase,
            Func<string, string, string?, string?, string?, SftpClient> clientFactory)
            : this(context, host, username, password, keyFilePath, passphrase, 30, clientFactory)
        {
        }

        internal SftpConnector(IExecutionContext? context, string host, string username, string? password, string? keyFilePath, string? passphrase, int timeoutSeconds,
            Func<string, string, string?, string?, string?, SftpClient> clientFactory)
        {
            _context = context;
            _host = host;
            _username = username;
            _password = password;
            _keyFilePath = (string.IsNullOrEmpty(keyFilePath) || context == null) ? keyFilePath : context.ResolvePath(keyFilePath);
            _passphrase = passphrase;
            _timeoutSeconds = timeoutSeconds;
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
            ["PASSPHRASE"] = new[] { "Passphrase for the private key" },
            ["TIMEOUT_SECONDS"] = new[] { "Connection timeout in seconds (default 30)" }
        };
        public Dictionary<string, string[]> GetOptionValues() => new();
        public string GetHelp() => "SFTP Connector for remote file operations over SSH.";

        public IDataSource CreateDataSource(IExecutionContext context, string connectionString, Dictionary<string, string>? options = null)
        {
            string user = options?.GetValueOrDefault("USER") ?? "";
            string? pass = options?.GetValueOrDefault("PASSWORD");
            string? keyFile = options?.GetValueOrDefault("KEYFILE");
            string? passphrase = options?.GetValueOrDefault("PASSPHRASE");
            
            int timeoutSeconds = 30;
            if (options != null && options.TryGetValue("TIMEOUT_SECONDS", out var timeoutStr) && int.TryParse(timeoutStr, out var parsedTimeout))
            {
                timeoutSeconds = parsedTimeout;
            }

            return new SftpConnector(context, connectionString, user, pass, keyFile, passphrase, timeoutSeconds);
        }

        public Task<IEnumerable<string>> GetTablesAsync(IExecutionContext context, string connectionString) => throw new NotSupportedException("Use IDataSource.GetTablesAsync instead.");
        public Task<IEnumerable<string>> GetViewsAsync(IExecutionContext context, string connectionString) => throw new NotSupportedException("Use IDataSource.GetViewsAsync instead.");
        public Task<IEnumerable<string>> GetColumnsAsync(IExecutionContext context, string connectionString, string tableName) => throw new NotSupportedException("Use IDataSource.GetColumnsAsync instead.");
        public Task<IEnumerable<string>> GetProceduresAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());

        public string BuildConnectionString(Dictionary<string, string> properties) => 
            ConnectionStringBuilder.Build(Name, properties);

        private SftpClient EnsureConnected()
        {
            if (!Client.IsConnected)
            {
                Client.Connect();
            }

            return Client;
        }

        private async Task RunClientOperationAsync(Action<SftpClient> operation)
        {
            await _clientLock.WaitAsync();
            try
            {
                await Task.Run(() => operation(EnsureConnected()));
            }
            finally
            {
                _clientLock.Release();
            }
        }

        private async Task<T> RunClientOperationAsync<T>(Func<SftpClient, T> operation)
        {
            await _clientLock.WaitAsync();
            try
            {
                return await Task.Run(() => operation(EnsureConnected()));
            }
            finally
            {
                _clientLock.Release();
            }
        }

        public IAsyncEnumerable<FileMetaData> ListFilesAsync(string path) =>
            ConnectorExceptionWrapper.WrapAsync(ListFilesCoreAsync(path), "SFTP", ShouldWrapProviderException);

        private async IAsyncEnumerable<FileMetaData> ListFilesCoreAsync(string path)
        {
            path = NormalizeRemotePath(path);
            var files = await RunClientOperationAsync(client =>
                client.ListDirectory(path)
                    .Select(i => new FileMetaData
                    {
                        Name = i.Name,
                        FullPath = i.FullName,
                        Size = i.Attributes.Size,
                        LastModified = i.LastWriteTime,
                        IsDirectory = i.IsDirectory
                    })
                    .ToList());

            foreach (var file in files)
                yield return file;
        }

        private bool RemoteFileExistsNormalized(SftpClient client, string remotePath) =>
            client.Exists(remotePath) && !client.Get(remotePath).IsDirectory;

        private bool RemoteDirectoryExistsNormalized(SftpClient client, string remotePath) =>
            client.Exists(remotePath) && client.Get(remotePath).IsDirectory;

        public async Task UploadFileAsync(string localPath, string remotePath, bool overwrite = true)
        {
            try
            {
                remotePath = NormalizeRemotePath(remotePath);
                await RunClientOperationAsync(client =>
                {
                    if (!overwrite && client.Exists(remotePath))
                    {
                        throw new ExecutionException($"Remote file already exists: {remotePath}");
                    }

                    using var fileStream = File.OpenRead(localPath);
                    client.UploadFile(fileStream, remotePath);
                });
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("SFTP", ex);
            }
        }

        public async Task DownloadFileAsync(string remotePath, string localPath, bool overwrite = true)
        {
            try
            {
                remotePath = NormalizeRemotePath(remotePath);
                if (!overwrite && File.Exists(localPath))
                {
                    throw new ExecutionException($"Local file already exists: {localPath}");
                }

                await RunClientOperationAsync(client =>
                {
                    using var fileStream = File.Open(localPath, overwrite ? FileMode.Create : FileMode.CreateNew);
                    client.DownloadFile(remotePath, fileStream);
                });
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("SFTP", ex);
            }
        }

        public async Task DeleteFileAsync(string remotePath)
        {
            try
            {
                remotePath = NormalizeRemotePath(remotePath);
                await RunClientOperationAsync(client => client.DeleteFile(remotePath));
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("SFTP", ex);
            }
        }

        public async Task<bool> FileExistsAsync(string remotePath)
        {
            try
            {
                remotePath = NormalizeRemotePath(remotePath);
                return await RunClientOperationAsync(client => RemoteFileExistsNormalized(client, remotePath));
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("SFTP", ex);
            }
        }

        public async Task<bool> DirectoryExistsAsync(string remotePath)
        {
            try
            {
                remotePath = NormalizeRemotePath(remotePath);
                return await RunClientOperationAsync(client => RemoteDirectoryExistsNormalized(client, remotePath));
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("SFTP", ex);
            }
        }

        public async Task RenameFileAsync(string remoteSource, string remoteDest, bool overwrite = true)
        {
            try
            {
                remoteSource = NormalizeRemotePath(remoteSource);
                remoteDest = NormalizeRemotePath(remoteDest);
                await RunClientOperationAsync(client =>
                {
                    if (overwrite && client.Exists(remoteDest))
                    {
                        client.DeleteFile(remoteDest);
                    }

                    client.RenameFile(remoteSource, remoteDest);
                });
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("SFTP", ex);
            }
        }

        public async Task CreateDirectoryAsync(string remotePath)
        {
            try
            {
                remotePath = NormalizeRemotePath(remotePath);
                await RunClientOperationAsync(client => client.CreateDirectory(remotePath));
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("SFTP", ex);
            }
        }

        public async Task DeleteDirectoryAsync(string remotePath)
        {
            try
            {
                remotePath = NormalizeRemotePath(remotePath);
                await RunClientOperationAsync(client => client.DeleteDirectory(remotePath));
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("SFTP", ex);
            }
        }

        public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) =>
            ConnectorExceptionWrapper.WrapAsync(ReadBatchesCore(batchSize), "SFTP", ShouldWrapProviderException);

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

        public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) => throw new NotSupportedException("Writing batches to SFTP directly is not supported. Use FILE_SEND.");
        public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult((IEnumerable<string>)new[] { "Name", "FullPath", "Size", "LastModified", "IsDirectory" });
        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }
        public IDataSource WithTable(string tableName) => this;

        public async ValueTask DisposeAsync()
        {
            if (_client != null)
            {
                await _clientLock.WaitAsync();
                try
                {
                    if (_client.IsConnected)
                    {
                        _client.Disconnect();
                    }
                    _client.Dispose();
                }
                finally
                {
                    _clientLock.Release();
                }
            }
            _clientLock.Dispose();
            await Task.CompletedTask;
        }

        public string? GetHost(string connectionString, Dictionary<string, string>? options = null)
        {
            if (options != null && options.TryGetValue("HOST", out var host)) return host;
            return connectionString;
        }

        internal static string NormalizeRemotePath(string path) =>
            string.IsNullOrEmpty(path) ? path : path.Replace('\\', '/');

        private static bool ShouldWrapProviderException(Exception ex) =>
            ex is SshException or SftpPathNotFoundException or SftpPermissionDeniedException
                or System.Net.Sockets.SocketException or TimeoutException or InvalidOperationException;
    }
}
