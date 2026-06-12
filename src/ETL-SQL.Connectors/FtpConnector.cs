using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Shared;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using FluentFTP;

namespace ETL_SQL.Connectors
{
    public class FtpConnector : IRemoteFileSystem, IDataSource, IConnector
    {
        private readonly ILogger _logger;
        private readonly IExecutionContext? _context;

        public string Name => "FTP_CONN";
        public IReadOnlyList<string> Aliases => new[] { "FTP" };
        public string Path => $"ftp://{_host}";
        public Dictionary<string, string>? Options => null;
        public string ConnectorType => "FTP";

        private readonly FtpClient? _client;
        private readonly string? _host;
        private readonly int _port;
        private readonly string? _username;
        private readonly string? _password;

        public FtpConnector()
        {
            _logger = NullLogger.Instance;
        }

        public FtpConnector(string host, string username, string password)
            : this(host, username, password, 21)
        {
        }

        public FtpConnector(string host, string username, string password, int port)
        {
            _logger = NullLogger.Instance;
            (_host, _port) = NormalizeEndpoint(host, port);
            _username = username;
            _password = password;
            _client = CreateClient(_host, _port, username, password);
        }

        public FtpConnector(IExecutionContext context, string host, string username, string password)
            : this(context, host, username, password, 21)
        {
        }

        public FtpConnector(IExecutionContext context, string host, string username, string password, int port,
            string? useSsl = null, bool passive = true)
        {
            _context = context;
            _logger = context.Logger;
            (_host, _port) = NormalizeEndpoint(host, port);
            _username = username;
            _password = password;
            _client = CreateClient(_host, _port, username, password, useSsl, passive);

            // Security Hardening: egress control
            context.SecurityService.ValidateHost(_host);
        }

        public async Task<string> GetVersionAsync(IExecutionContext context, string connectionString)
        {
            // Security constraint: validate host before connecting
            var host = GetHost(connectionString);
            if (host != null) context.SecurityService.ValidateHost(host);
            return await Task.FromResult("FTP Server");
        }
        public HashSet<string> GetSupportedFunctions() => new();
        public HashSet<string> GetSupportedKeywords() => new();
        public Dictionary<string, string[]> GetSupportedOptions() => new()
        {
            ["USER"] = Array.Empty<string>(),
            ["PASSWORD"] = Array.Empty<string>(),
            ["PORT"] = Array.Empty<string>(),
            ["USE_SSL"] = new[] { "OFF", "EXPLICIT", "IMPLICIT" },
            ["PASSIVE"] = new[] { "ON", "OFF" }
        };
        public Dictionary<string, string[]> GetOptionValues() => new();
        public string GetHelp() => "FTP Connector for remote file operations.\nOptions:\n  USER: The username for the FTP connection.\n  PASSWORD: The password for the FTP connection.\n  PORT: The FTP control port. Default: 21.\nMethods: GET_FILE, PUT_FILE, REMOTE_FILE_LIST.";

        public IDataSource CreateDataSource(IExecutionContext context, string connectionString, Dictionary<string, string>? options = null)
        {
            string user = options?.GetValueOrDefault("USER") ?? "";
            string pass = options?.GetValueOrDefault("PASSWORD") ?? "";
            if (pass.StartsWith("ENC:"))
            {
                pass = context.DecryptValue(pass) ?? "";
            }
            var port = 21;
            if (options?.TryGetValue("PORT", out var portText) == true && int.TryParse(portText, out var parsedPort))
                port = parsedPort;
            string? useSsl = options?.GetValueOrDefault("USE_SSL");
            bool passive = options?.GetValueOrDefault("PASSIVE")?.ToUpperInvariant() != "OFF";
            return new FtpConnector(context, connectionString, user, pass, port, useSsl, passive);
        }

        public Task<IEnumerable<string>> GetTablesAsync(IExecutionContext context, string connectionString) => throw new NotSupportedException("Use IDataSource.GetTablesAsync instead.");
        public Task<IEnumerable<string>> GetViewsAsync(IExecutionContext context, string connectionString) => throw new NotSupportedException("Use IDataSource.GetViewsAsync instead.");
        public Task<IEnumerable<string>> GetColumnsAsync(IExecutionContext context, string connectionString, string tableName) => throw new NotSupportedException("Use IDataSource.GetColumnsAsync instead.");
        public Task<IEnumerable<string>> GetProceduresAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());

        public string BuildConnectionString(Dictionary<string, string> properties) =>
            ConnectionStringBuilder.Build(Name, properties);

        private void EnsureConnected()
        {
            if (_client == null) throw new ExecutionException("FTP Client is not initialized.");
            if (!_client.IsConnected)
            {
                _client.Connect();
            }
        }

        public IAsyncEnumerable<FileMetaData> ListFilesAsync(string path) =>
            ConnectorExceptionWrapper.WrapAsync(ListFilesCoreAsync(path), "FTP", ShouldWrapProviderException);

        private async IAsyncEnumerable<FileMetaData> ListFilesCoreAsync(string path)
        {
            await Task.CompletedTask;
            EnsureConnected();
            foreach (var i in _client!.GetListing(path))
                yield return new FileMetaData
                {
                    Name = i.Name,
                    FullPath = i.FullName,
                    Size = i.Size,
                    LastModified = i.Modified,
                    IsDirectory = i.Type == FtpObjectType.Directory
                };
        }

        public Task UploadFileAsync(string localPath, string remotePath, bool overwrite = true)
        {
            try
            {
                EnsureConnected();
                var existsMode = overwrite ? FtpRemoteExists.Overwrite : FtpRemoteExists.Skip;
                var status = _client!.UploadFile(localPath, remotePath, existsMode);
                if (status == FtpStatus.Skipped && !overwrite) throw new ExecutionException($"Remote file already exists (overwrite=OFF): {remotePath}");
                if (status == FtpStatus.Failed) throw new ExecutionException($"Failed to upload file to FTP: {remotePath}");
                return Task.CompletedTask;
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("FTP", ex);
            }
        }

        public Task DownloadFileAsync(string remotePath, string localPath, bool overwrite = true)
        {
            try
            {
                EnsureConnected();
                var existsMode = overwrite ? FtpLocalExists.Overwrite : FtpLocalExists.Skip;
                var status = _client!.DownloadFile(localPath, remotePath, existsMode);
                if (status == FtpStatus.Skipped && !overwrite) throw new ExecutionException($"Local file already exists (overwrite=OFF): {localPath}");
                if (status == FtpStatus.Failed) throw new ExecutionException($"Failed to download file from FTP: {remotePath}");
                return Task.CompletedTask;
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("FTP", ex);
            }
        }

        public Task DeleteFileAsync(string remotePath)
        {
            try
            {
                EnsureConnected();
                _client!.DeleteFile(remotePath);
                return Task.CompletedTask;
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("FTP", ex);
            }
        }

        public Task<bool> FileExistsAsync(string remotePath)
        {
            try
            {
                EnsureConnected();
                return Task.FromResult(_client!.FileExists(remotePath));
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("FTP", ex);
            }
        }

        public Task<bool> DirectoryExistsAsync(string remotePath)
        {
            try
            {
                EnsureConnected();
                return Task.FromResult(_client!.DirectoryExists(remotePath));
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("FTP", ex);
            }
        }

        public Task RenameFileAsync(string remoteSource, string remoteDest, bool overwrite = true)
        {
            try
            {
                EnsureConnected();
                if (overwrite && _client!.FileExists(remoteDest))
                {
                    _client.DeleteFile(remoteDest);
                }
                _client!.Rename(remoteSource, remoteDest);
                return Task.CompletedTask;
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("FTP", ex);
            }
        }

        public Task CreateDirectoryAsync(string remotePath)
        {
            try
            {
                EnsureConnected();
                _client!.CreateDirectory(remotePath);
                return Task.CompletedTask;
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("FTP", ex);
            }
        }

        public Task DeleteDirectoryAsync(string remotePath)
        {
            try
            {
                EnsureConnected();
                _client!.DeleteDirectory(remotePath);
                return Task.CompletedTask;
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("FTP", ex);
            }
        }

        public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) =>
            ConnectorExceptionWrapper.WrapAsync(ReadBatchesCore(batchSize), "FTP", ShouldWrapProviderException);

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

        public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) => throw new NotSupportedException("Writing batches to FTP directly is not supported. Use FILE_SEND.");
        public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult((IEnumerable<string>)new[] { "Name", "FullPath", "Size", "LastModified", "IsDirectory" });
        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }
        public IDataSource WithTable(string tableName) => this;

        public async ValueTask DisposeAsync()
        {
            if (_client != null && _client.IsConnected)
            {
                _client.Disconnect();
            }
            _client?.Dispose();
            await Task.CompletedTask;
        }

        public string? GetHost(string connectionString, Dictionary<string, string>? options = null)
        {
            if (options != null && options.TryGetValue("HOST", out var host)) return host;
            return NormalizeEndpoint(connectionString, 21).Host;
        }

        private static FtpClient CreateClient(string host, int port, string username, string password,
            string? useSsl = null, bool passive = true)
        {
            var client = new FtpClient(host, username, password) { Port = port };
            client.Config.EncryptionMode = useSsl?.ToUpperInvariant() switch
            {
                "EXPLICIT" or "ON" => FtpEncryptionMode.Explicit,
                "IMPLICIT" => FtpEncryptionMode.Implicit,
                _ => FtpEncryptionMode.None
            };
            if (!passive)
                client.Config.DataConnectionType = FtpDataConnectionType.PORT;
            return client;
        }

        private static (string Host, int Port) NormalizeEndpoint(string host, int fallbackPort)
        {
            if (Uri.TryCreate(host, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
            {
                var explicitPort = GetExplicitPort(host);
                return int.TryParse(explicitPort, out var uriPort)
                    ? (uri.Host, uriPort)
                    : (uri.Host, fallbackPort);
            }

            var colonIndex = host.LastIndexOf(':');
            if (colonIndex > 0 && colonIndex == host.IndexOf(':') &&
                int.TryParse(host[(colonIndex + 1)..], out var parsedPort))
            {
                return (host[..colonIndex], parsedPort);
            }

            return (host, fallbackPort);
        }

        private static string? GetExplicitPort(string endpoint)
        {
            var schemeSeparator = endpoint.IndexOf("://", StringComparison.Ordinal);
            var authority = schemeSeparator >= 0 ? endpoint[(schemeSeparator + 3)..] : endpoint;
            var terminator = authority.IndexOfAny(new[] { '/', '?', '#' });
            if (terminator >= 0)
            {
                authority = authority[..terminator];
            }

            var atIndex = authority.LastIndexOf('@');
            if (atIndex >= 0)
            {
                authority = authority[(atIndex + 1)..];
            }

            if (authority.StartsWith("[", StringComparison.Ordinal))
            {
                var closeBracket = authority.IndexOf(']');
                return closeBracket >= 0
                    && authority.Length > closeBracket + 1
                    && authority[closeBracket + 1] == ':'
                    ? authority[(closeBracket + 2)..]
                    : null;
            }

            var colonIndex = authority.LastIndexOf(':');
            return colonIndex > 0 && colonIndex == authority.IndexOf(':')
                ? authority[(colonIndex + 1)..]
                : null;
        }

        private static bool ShouldWrapProviderException(Exception ex) =>
            ex.GetType().Namespace?.StartsWith("FluentFTP", StringComparison.Ordinal) == true
                || ex is System.Net.Sockets.SocketException or TimeoutException or InvalidOperationException
                || ex is System.IO.IOException;
    }
}
