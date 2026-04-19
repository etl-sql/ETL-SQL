using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentFTP;
using ETL_SQL.Data;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;

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
        private readonly string? _username;
        private readonly string? _password;

        public FtpConnector()
        {
            _logger = NullLogger.Instance;
        }

        public FtpConnector(string host, string username, string password)
        {
            _logger = NullLogger.Instance;
            _host = host;
            _username = username;
            _password = password;
            _client = new FtpClient(host, username, password);
        }

        public FtpConnector(IExecutionContext context, string host, string username, string password)
        {
            _context = context;
            _logger = context.Logger;
            _host = host;
            _username = username;
            _password = password;
            _client = new FtpClient(host, username, password);

            // Security Hardening: egress control
            context.SecurityService.ValidateHost(host);
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
        public Dictionary<string, string[]> GetSupportedOptions() => new() { ["USER"] = new[] { "Username for FTP server" }, ["PASSWORD"] = new[] { "Password for FTP server" } };
        public Dictionary<string, string[]> GetOptionValues() => new();
        public string GetHelp() => "FTP Connector for remote file operations.\nOptions:\n  USER: The username for the FTP connection.\n  PASSWORD: The password for the FTP connection.\nMethods: GET_FILE, PUT_FILE, REMOTE_FILE_LIST.";

        public IDataSource CreateDataSource(IExecutionContext context, string connectionString, Dictionary<string, string>? options = null)
        {
            string user = options?.GetValueOrDefault("USER") ?? "";
            string pass = options?.GetValueOrDefault("PASSWORD") ?? "";
            return new FtpConnector(context, connectionString, user, pass);
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

        public Task<IEnumerable<FileMetaData>> ListFilesAsync(string path)
        {
            EnsureConnected();
            var items = _client!.GetListing(path);
            return Task.FromResult(items.Select(i => new FileMetaData
            {
                Name = i.Name,
                FullPath = i.FullName,
                Size = i.Size,
                LastModified = i.Modified,
                IsDirectory = i.Type == FtpObjectType.Directory
            }));
        }

        public Task UploadFileAsync(string localPath, string remotePath, bool overwrite = true)
        {
            EnsureConnected();
            var existsMode = overwrite ? FtpRemoteExists.Overwrite : FtpRemoteExists.Skip;
            var status = _client!.UploadFile(localPath, remotePath, existsMode);
            if (status == FtpStatus.Skipped && !overwrite) throw new ExecutionException($"Remote file already exists (overwrite=OFF): {remotePath}");
            if (status == FtpStatus.Failed) throw new ExecutionException($"Failed to upload file to FTP: {remotePath}");
            return Task.CompletedTask;
        }

        public Task DownloadFileAsync(string remotePath, string localPath, bool overwrite = true)
        {
            EnsureConnected();
            var existsMode = overwrite ? FtpLocalExists.Overwrite : FtpLocalExists.Skip;
            var status = _client!.DownloadFile(localPath, remotePath, existsMode);
            if (status == FtpStatus.Skipped && !overwrite) throw new ExecutionException($"Local file already exists (overwrite=OFF): {localPath}");
            if (status == FtpStatus.Failed) throw new ExecutionException($"Failed to download file from FTP: {remotePath}");
            return Task.CompletedTask;
        }

        public Task DeleteFileAsync(string remotePath)
        {
            EnsureConnected();
            _client!.DeleteFile(remotePath);
            return Task.CompletedTask;
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

        public Task WriteBatches(IAsyncEnumerable<DataTable> batches) => throw new NotSupportedException("Writing batches to FTP directly is not supported. Use FILE_SEND.");
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
            return connectionString;
        }
    }
}
