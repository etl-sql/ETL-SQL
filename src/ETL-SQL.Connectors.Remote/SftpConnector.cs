using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Shared;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Diagnostics;
using ETL_SQL.Data;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace ETL_SQL.Connectors
{
    public class SftpConnector : IRemoteFileSystem, IDataSource, IConnector, IConnectionDiagnosticAuthProbe
    {
        private SftpClient? _client;
        private int _disposed;
        private readonly string? _host;
        private readonly int _port = 22;
        private readonly string? _username;
        private readonly string? _password;
        private readonly string? _keyFilePath;
        private readonly string? _passphrase;
        private readonly int _timeoutSeconds = 30;
        private readonly string? _hostKeyFingerprint;
        private readonly bool _allowUnpinnedHostKey;
        private readonly bool _atomicUpload;
        private readonly Dictionary<string, string>? _options;
        private readonly ILogger _logger;
        private readonly IExecutionContext? _context;
        private readonly Func<string, string, string?, string?, string?, Task<SftpClient>>? _clientFactory;
        private readonly SemaphoreSlim _clientLock = new(1, 1);

        public string Name => "SFTP";
        public IReadOnlyList<string> Aliases => new[] { "SSH" };

        public SftpConnector()
        {
            _logger = NullLogger.Instance;
        }

        public SftpConnector(string host, string username, string? password = null, string? keyFilePath = null, string? passphrase = null, int timeoutSeconds = 30)
             : this(host, 22, username, password, keyFilePath, passphrase, timeoutSeconds)
        {
        }

        public SftpConnector(string host, int port, string username, string? password = null, string? keyFilePath = null, string? passphrase = null, int timeoutSeconds = 30)
             : this(null!, host, port, username, password, keyFilePath, passphrase, timeoutSeconds,
                   (h, u, p, k, pp) =>
                   {
                       var info = !string.IsNullOrEmpty(k)
                           ? new Renci.SshNet.ConnectionInfo(h, port, u, new PrivateKeyAuthenticationMethod(u, new PrivateKeyFile(k, pp)))
                           : new Renci.SshNet.ConnectionInfo(h, port, u, new PasswordAuthenticationMethod(u, p ?? ""));
                       info.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
                       return new SftpClient(info);
                   })
        {
            _logger = NullLogger.Instance;
        }

        public SftpConnector(IExecutionContext context, string host, string username, string? password = null, string? keyFilePath = null, string? passphrase = null, int timeoutSeconds = 30)
            : this(context, host, 22, username, password, keyFilePath, passphrase, timeoutSeconds)
        {
        }

        public SftpConnector(IExecutionContext context, string host, int port, string username, string? password = null, string? keyFilePath = null, string? passphrase = null, int timeoutSeconds = 30, string? hostKeyFingerprint = null, bool atomicUpload = false, bool allowUnpinnedHostKey = false)
            : this(context, host, port, username, password, keyFilePath, passphrase, timeoutSeconds,
                  (h, u, p, k, pp) =>
                  {
                      var info = !string.IsNullOrEmpty(k)
                          ? new Renci.SshNet.ConnectionInfo(h, port, u, new PrivateKeyAuthenticationMethod(u, new PrivateKeyFile(k, pp)))
                          : new Renci.SshNet.ConnectionInfo(h, port, u, new PasswordAuthenticationMethod(u, p ?? ""));
                      info.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
                      return new SftpClient(info);
                  },
                  hostKeyFingerprint, atomicUpload, allowUnpinnedHostKey)
        {
        }

        internal SftpConnector(IExecutionContext? context, string host, string username, string? password, string? keyFilePath, string? passphrase,
            Func<string, string, string?, string?, string?, SftpClient> clientFactory)
            : this(context, host, 22, username, password, keyFilePath, passphrase, 30, clientFactory)
        {
        }

        internal SftpConnector(IExecutionContext? context, string host, string username, string? password, string? keyFilePath, string? passphrase, int timeoutSeconds,
            Func<string, string, string?, string?, string?, SftpClient> clientFactory)
            : this(context, host, 22, username, password, keyFilePath, passphrase, timeoutSeconds, clientFactory)
        {
        }

        internal SftpConnector(IExecutionContext? context, string host, int port, string username, string? password, string? keyFilePath, string? passphrase, int timeoutSeconds,
            Func<string, string, string?, string?, string?, SftpClient> clientFactory,
            string? hostKeyFingerprint = null, bool atomicUpload = false, bool allowUnpinnedHostKey = false)
        {
            _context = context;
            _host = host;
            _port = port;
            _username = username;
            _password = password;
            _keyFilePath = (string.IsNullOrEmpty(keyFilePath) || context == null) ? keyFilePath : context.ResolvePath(keyFilePath);
            _passphrase = passphrase;
            _timeoutSeconds = timeoutSeconds;
            _hostKeyFingerprint = string.IsNullOrWhiteSpace(hostKeyFingerprint) ? null : hostKeyFingerprint.Trim();
            _allowUnpinnedHostKey = allowUnpinnedHostKey;
            _atomicUpload = atomicUpload;
            _options = BuildOptions(host, port, username, password, _keyFilePath, passphrase, timeoutSeconds, hostKeyFingerprint, atomicUpload, allowUnpinnedHostKey);
            _logger = context?.Logger ?? NullLogger.Instance;
            _clientFactory = (h, u, p, k, pp) => Task.Run(() => clientFactory(h, u, p, k, pp));

            // Security Hardening: egress control
            if (context != null)
            {
                ETL_SQL.Core.Governance.ConnectorPolicyAuthorizer.EnforceEnterpriseHost(context, host);

                // Validate key file path if provided
                if (_keyFilePath != null)
                {
                    context.SecurityService.ValidatePath(_keyFilePath);
                }
            }
        }

        private async Task<SftpClient> GetOrCreateClientAsync()
        {
            if (_client == null)
            {
                if (_clientFactory == null || _host == null || _username == null)
                    throw new InvalidOperationException("Connector not initialized with connection details.");
                _client = await _clientFactory(_host, _username, _password, _keyFilePath, _passphrase);
            }

            return _client;
        }

        public string Path => _port == 22 ? $"sftp://{_host}" : $"sftp://{_host}:{_port}";
        public Dictionary<string, string>? Options => _options;
        public string ConnectorType => "SFTP";

        public async Task<string> GetVersionAsync(IExecutionContext context, string connectionString)
        {
            // Security constraint: validate host before connecting
            var host = GetHost(connectionString);
            if (host != null) ETL_SQL.Core.Governance.ConnectorPolicyAuthorizer.EnforceEnterpriseHost(context, host);
            return await Task.FromResult("SFTP Server");
        }
        public HashSet<string> GetSupportedFunctions() => new();
        public HashSet<string> GetSupportedKeywords() => new();
        public Dictionary<string, string[]> GetSupportedOptions() => new()
        {
            ["HOST"] = Array.Empty<string>(),
            ["USER"] = new[] { "Username for SSH" },
            ["PASSWORD"] = new[] { "Password for SSH" },
            ["KEYFILE"] = new[] { "Path to the private key file" },
            ["PASSPHRASE"] = new[] { "Passphrase for the private key" },
            ["PORT"] = new[] { "SSH/SFTP Port (default 22)" },
            ["TIMEOUT_SECONDS"] = new[] { "Connection timeout in seconds (default 30)" },
            ["HOST_KEY_FINGERPRINT"] = new[] { "Pinned server host-key fingerprint (SHA256:base64 or MD5 hex). Required unless ALLOW_UNPINNED_HOST_KEY is set: a mismatched or unpinned host key rejects the connection (MITM protection)." },
            ["ALLOW_UNPINNED_HOST_KEY"] = new[] { "true", "false" },
            ["ATOMIC_UPLOAD"] = new[] { "true/false (default false): upload to a temp name then rename into place so consumers never read a partial file. Requires rename permission on the target directory." }
        };
        public Dictionary<string, string[]> GetOptionValues() => new();
        public string GetHelp() => "SFTP Connector for remote file operations over SSH.";

        public IDataSource CreateDataSource(IExecutionContext context, string connectionString, Dictionary<string, string>? options = null)
        {
            string user = options?.GetValueOrDefault("USER") ?? "";
            string? pass = options?.GetValueOrDefault("PASSWORD");
            string? keyFile = options?.GetValueOrDefault("KEYFILE");
            string? passphrase = options?.GetValueOrDefault("PASSPHRASE");

            if (pass != null && pass.StartsWith("ENC:"))
            {
                pass = context.DecryptValue(pass);
            }
            if (passphrase != null && passphrase.StartsWith("ENC:"))
            {
                passphrase = context.DecryptValue(passphrase);
            }

            int timeoutSeconds = 30;
            if (options != null && options.TryGetValue("TIMEOUT_SECONDS", out var timeoutStr) && int.TryParse(timeoutStr, out var parsedTimeout))
            {
                timeoutSeconds = parsedTimeout;
            }

            string? hostKeyFingerprint = options?.GetValueOrDefault("HOST_KEY_FINGERPRINT");
            bool atomicUpload = options != null
                && options.TryGetValue("ATOMIC_UPLOAD", out var atomicStr)
                && bool.TryParse(atomicStr, out var parsedAtomic) && parsedAtomic;
            bool allowUnpinnedHostKey = options != null
                && options.TryGetValue("ALLOW_UNPINNED_HOST_KEY", out var allowUnpinnedStr)
                && bool.TryParse(allowUnpinnedStr, out var parsedAllowUnpinned) && parsedAllowUnpinned;

            string host = connectionString;
            int port = 22;
            if (options != null && options.TryGetValue("PORT", out var portStr) && int.TryParse(portStr, out var parsedPort))
            {
                port = parsedPort;
            }
            else if (!string.IsNullOrEmpty(connectionString))
            {
                int colonIdx = connectionString.IndexOf(':');
                if (colonIdx >= 0)
                {
                    host = connectionString.Substring(0, colonIdx);
                    var portPart = connectionString.Substring(colonIdx + 1);
                    if (int.TryParse(portPart, out var parsedPortFromConnStr))
                    {
                        port = parsedPortFromConnStr;
                    }
                }
            }

            return new SftpConnector(context, host, port, user, pass, keyFile, passphrase, timeoutSeconds, hostKeyFingerprint, atomicUpload, allowUnpinnedHostKey);
        }

        public Task<IEnumerable<string>> GetTablesAsync(IExecutionContext context, string connectionString) => throw new NotSupportedException("Use IDataSource.GetTablesAsync instead.");
        public Task<IEnumerable<string>> GetViewsAsync(IExecutionContext context, string connectionString) => throw new NotSupportedException("Use IDataSource.GetViewsAsync instead.");
        public Task<IEnumerable<string>> GetColumnsAsync(IExecutionContext context, string connectionString, string tableName) => throw new NotSupportedException("Use IDataSource.GetColumnsAsync instead.");
        public Task<IEnumerable<string>> GetProceduresAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());

        public string BuildConnectionString(Dictionary<string, string> properties) =>
            ConnectionStringBuilder.Build(Name, properties);

        private async Task<SftpClient> EnsureConnectedAsync()
        {
            var client = await GetOrCreateClientAsync();
            if (!client.IsConnected)
            {
                // Verify the server host key before the connection is trusted. Idempotent subscribe.
                client.HostKeyReceived -= OnHostKeyReceived;
                client.HostKeyReceived += OnHostKeyReceived;
                await Task.Run(client.Connect);
            }

            return client;
        }

        /// <summary>
        /// Host-key verification, closed by default. A connection is trusted only when the server key
        /// matches the pinned <c>HOST_KEY_FINGERPRINT</c>. With no pin there is no trust anchor — the
        /// client would accept whatever server answers — so the connection is rejected unless the
        /// caller has explicitly opted out with <c>ALLOW_UNPINNED_HOST_KEY = true</c>, which makes
        /// running without man-in-the-middle protection a deliberate choice rather than the default.
        /// </summary>
        private void OnHostKeyReceived(object? sender, HostKeyEventArgs e)
        {
            var decision = EvaluateHostKey(_hostKeyFingerprint, _allowUnpinnedHostKey, e.FingerPrintSHA256, e.FingerPrint);
            var host = _host ?? "(unknown)";

            switch (decision)
            {
                case HostKeyDecision.TrustedByPin:
                    return;

                case HostKeyDecision.UnpinnedAllowed:
                    _logger.Warning(
                        "SFTP host key for {Host} is not pinned and ALLOW_UNPINNED_HOST_KEY is set; the transfer trusts whatever server answers and is vulnerable to man-in-the-middle interception. Pin HOST_KEY_FINGERPRINT for outbound/vendor transfers.",
                        host);
                    return;

                case HostKeyDecision.RejectedUnpinned:
                    e.CanTrust = false;
                    _logger.Error(
                        "SFTP host key for {Host} is not pinned; rejecting the connection. Set HOST_KEY_FINGERPRINT to the server's fingerprint (ssh-keygen -lf <server_host_key>), or set ALLOW_UNPINNED_HOST_KEY = true to accept any host key and its man-in-the-middle risk.",
                        null, host);
                    return;

                default:
                    e.CanTrust = false;
                    _logger.Error(
                        "SFTP host key for {Host} did not match the pinned HOST_KEY_FINGERPRINT; rejecting the connection (possible man-in-the-middle).",
                        null, host);
                    return;
            }
        }

        /// <summary>The outcome of host-key verification. Only the trusted outcomes leave the connection open.</summary>
        internal enum HostKeyDecision
        {
            /// <summary>The server key matched the pinned fingerprint.</summary>
            TrustedByPin,
            /// <summary>No pin was configured, but the caller explicitly opted out of pinning.</summary>
            UnpinnedAllowed,
            /// <summary>No pin was configured and no opt-out was given — there is no trust anchor.</summary>
            RejectedUnpinned,
            /// <summary>A pin was configured and the server key did not match it.</summary>
            RejectedMismatch
        }

        /// <summary>
        /// Decides whether a server host key may be trusted. Closed by default: without a pin the only
        /// way through is an explicit <paramref name="allowUnpinned"/> opt-out. Internal so the decision
        /// can be unit tested without a live SSH server, matching <see cref="FingerprintMatches"/>.
        /// </summary>
        internal static HostKeyDecision EvaluateHostKey(string? pin, bool allowUnpinned, string? actualSha256, byte[]? actualMd5)
        {
            // COMPAT_BREAK: 0.17 — an unpinned host key used to be trusted with only a warning.
            // It is now rejected unless ALLOW_UNPINNED_HOST_KEY opts out explicitly.
            if (string.IsNullOrWhiteSpace(pin))
                return allowUnpinned ? HostKeyDecision.UnpinnedAllowed : HostKeyDecision.RejectedUnpinned;

            return FingerprintMatches(pin, actualSha256, actualMd5)
                ? HostKeyDecision.TrustedByPin
                : HostKeyDecision.RejectedMismatch;
        }

        /// <summary>
        /// Compares a pinned host-key fingerprint against the server's actual SHA256 (base64) and MD5
        /// (bytes) fingerprints. Accepts an optional <c>SHA256:</c>/<c>MD5:</c> algorithm prefix (as
        /// shown by ssh-keygen), tolerates SHA256 base64 padding, and matches MD5 hex ignoring case and
        /// separators. Internal for unit testing without a live SSH server.
        /// </summary>
        internal static bool FingerprintMatches(string? pin, string? actualSha256, byte[]? actualMd5)
        {
            if (string.IsNullOrWhiteSpace(pin)) return false;
            pin = pin.Trim();

            // Only treat the text before the first colon as an algorithm prefix when it is actually
            // "SHA256"/"MD5" — a bare MD5 fingerprint ("aa:bb:cc:dd") also contains colons.
            var colon = pin.IndexOf(':');
            var prefix = colon > 0 ? pin[..colon].Trim().ToUpperInvariant() : null;
            var algo = prefix is "SHA256" or "MD5" ? prefix : null;
            var value = algo is not null ? pin[(colon + 1)..].Trim() : pin;

            if (algo is null or "SHA256" && !string.IsNullOrEmpty(actualSha256))
            {
                if (string.Equals(actualSha256.TrimEnd('='), value.TrimEnd('='), StringComparison.Ordinal))
                    return true;
            }

            if (algo is null or "MD5" && actualMd5 is { Length: > 0 })
            {
                var actualHex = string.Join("", actualMd5.Select(b => b.ToString("x2")));
                var normalizedPin = value.Replace(":", "").Replace("-", "");
                if (string.Equals(actualHex, normalizedPin, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private async Task RunClientOperationAsync(Action<SftpClient> operation)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (_context != null)
                ETL_SQL.Core.Governance.ConnectorPolicyAuthorizer.EnforceEnterpriseHost(_context, _host);
            await _clientLock.WaitAsync();
            try
            {
                var client = await EnsureConnectedAsync();
                await Task.Run(() => operation(client));
            }
            finally
            {
                _clientLock.Release();
            }
        }

        private async Task<T> RunClientOperationAsync<T>(Func<SftpClient, T> operation)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (_context != null)
                ETL_SQL.Core.Governance.ConnectorPolicyAuthorizer.EnforceEnterpriseHost(_context, _host);
            await _clientLock.WaitAsync();
            try
            {
                var client = await EnsureConnectedAsync();
                return await Task.Run(() => operation(client));
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

                    if (_atomicUpload)
                    {
                        // Upload to a temp name and rename into place so a polling consumer never sees a
                        // partially written file. Requires rename permission on the target directory
                        // (off by default for write-only vendors). Rename cannot overwrite on SFTP, so an
                        // existing destination is removed first — a small non-atomic window that only
                        // applies when replacing an existing file, not to first-time deliveries.
                        var tempPath = remotePath + ".tmp-" + Guid.NewGuid().ToString("N");
                        try
                        {
                            using (var fileStream = File.OpenRead(localPath))
                                client.UploadFile(fileStream, tempPath);
                            if (client.Exists(remotePath))
                                client.DeleteFile(remotePath);
                            client.RenameFile(tempPath, remotePath);
                        }
                        catch
                        {
                            try { if (client.Exists(tempPath)) client.DeleteFile(tempPath); } catch { /* best effort cleanup */ }
                            throw;
                        }
                        return;
                    }

                    using var stream = File.OpenRead(localPath);
                    client.UploadFile(stream, remotePath);
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

        public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) => throw new NotSupportedException("Writing batches to SFTP directly is not supported. Use SEND FILE.");
        public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult((IEnumerable<string>)new[] { "Name", "FullPath", "Size", "LastModified", "IsDirectory" });
        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }
        public IDataSource WithTable(string tableName) => this;

        public async ValueTask DisposeAsync()
        {
            // Idempotent: only the first caller runs teardown. Without this guard a second
            // DisposeAsync (e.g. `await using` plus DI teardown) would re-enter the block below and
            // call WaitAsync on the already-disposed _clientLock, throwing ObjectDisposedException.
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

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
                    _client = null;
                }
                finally
                {
                    _clientLock.Release();
                }
            }
            _clientLock.Dispose();
        }

        public string? GetHost(string connectionString, Dictionary<string, string>? options = null)
        {
            if (options != null && options.TryGetValue("HOST", out var host)) return host;
            if (Uri.TryCreate(connectionString, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
                return uri.Host;
            return connectionString;
        }

        public async Task<IReadOnlyList<DiagnosticStep>> DiagnoseAuthenticationAsync(
            ConnectionDiagnosticAuthContext context,
            CancellationToken cancellationToken = default)
        {
            var options = new Dictionary<string, string>(
                context.Options ?? new Dictionary<string, string>(),
                StringComparer.OrdinalIgnoreCase);
            var endpoint = ParseEndpoint(context.Target, options);
            var user = GetOption(options, "USER", "USERNAME");
            var password = GetOption(options, "PASSWORD");
            var keyFile = GetOption(options, "KEYFILE", "KEY_FILE");
            var passphrase = GetOption(options, "PASSPHRASE");
            var pin = GetOption(options, "HOST_KEY_FINGERPRINT");

            if (string.IsNullOrWhiteSpace(endpoint.Host) || string.IsNullOrWhiteSpace(user))
            {
                return
                [
                    new DiagnosticStep("AUTH", DiagnosticStatus.Skipped,
                        "SFTP authentication was not attempted because HOST or USER is missing.",
                        "Add HOST and USER options, plus either PASSWORD or KEYFILE.")
                ];
            }

            if (string.IsNullOrWhiteSpace(password) && string.IsNullOrWhiteSpace(keyFile))
            {
                return
                [
                    new DiagnosticStep("HOST_KEY", DiagnosticStatus.Skipped,
                        "SFTP host-key validation was not attempted because no credential method is configured.",
                        "Add PASSWORD or KEYFILE, and pin HOST_KEY_FINGERPRINT for vendor/outbound SFTP."),
                    new DiagnosticStep("AUTH", DiagnosticStatus.Skipped,
                        "SFTP authentication was not attempted because no credential method is configured.",
                        "Add PASSWORD or KEYFILE.")
                ];
            }

            string? observedSha256 = null;
            byte[]? observedMd5 = null;
            var hostKeyMismatch = false;
            var hostKeyObserved = false;

            try
            {
                using var client = CreateDiagnosticClient(endpoint.Host, endpoint.Port, user, password, keyFile, passphrase, context.ProbeTimeoutSeconds);
                client.HostKeyReceived += (_, e) =>
                {
                    hostKeyObserved = true;
                    observedSha256 = e.FingerPrintSHA256;
                    observedMd5 = e.FingerPrint;
                    if (!string.IsNullOrWhiteSpace(pin) && !FingerprintMatches(pin, observedSha256, observedMd5))
                    {
                        hostKeyMismatch = true;
                        e.CanTrust = false;
                    }
                };

                var timeout = TimeSpan.FromSeconds(context.ProbeTimeoutSeconds > 0 ? context.ProbeTimeoutSeconds : 5);
                await Task.Run(client.Connect, cancellationToken).WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
                client.Disconnect();
            }
            catch (Exception ex) when (hostKeyMismatch && ex is not OperationCanceledException)
            {
                return
                [
                    new DiagnosticStep("HOST_KEY", DiagnosticStatus.Failed,
                        "SFTP server host key did not match HOST_KEY_FINGERPRINT.",
                        "Verify the server fingerprint out-of-band and update HOST_KEY_FINGERPRINT only if the change is expected."),
                    new DiagnosticStep("AUTH", DiagnosticStatus.Skipped,
                        "SFTP authentication was not attempted because host-key validation failed.",
                        "Fix the host-key pin before testing credentials.")
                ];
            }
            catch (Exception ex) when (ex is SshAuthenticationException or SshConnectionException or SshException
                                          or SocketException or TimeoutException or InvalidOperationException
                                          or System.IO.IOException)
            {
                var hostKeyStep = BuildHostKeyStep(pin, hostKeyObserved, observedSha256, observedMd5);
                return
                [
                    hostKeyStep,
                    new DiagnosticStep("AUTH", DiagnosticStatus.Failed,
                        "SFTP authentication failed.",
                        "Verify USER, PASSWORD or KEYFILE/PASSPHRASE, account status, and server authentication policy.")
                ];
            }

            return
            [
                BuildHostKeyStep(pin, hostKeyObserved, observedSha256, observedMd5),
                new DiagnosticStep("AUTH", DiagnosticStatus.Ok, "SFTP authentication succeeded.")
            ];
        }

        private static SftpClient CreateDiagnosticClient(
            string host,
            int port,
            string user,
            string? password,
            string? keyFile,
            string? passphrase,
            int timeoutSeconds)
        {
            AuthenticationMethod auth = !string.IsNullOrWhiteSpace(keyFile)
                ? new PrivateKeyAuthenticationMethod(user, new PrivateKeyFile(keyFile, passphrase))
                : new PasswordAuthenticationMethod(user, password ?? string.Empty);
            var info = new Renci.SshNet.ConnectionInfo(host, port, user, auth)
            {
                Timeout = TimeSpan.FromSeconds(timeoutSeconds > 0 ? timeoutSeconds : 5)
            };
            return new SftpClient(info);
        }

        private static DiagnosticStep BuildHostKeyStep(string? pin, bool observed, string? sha256, byte[]? md5)
        {
            if (!observed)
            {
                return new DiagnosticStep("HOST_KEY", DiagnosticStatus.Skipped,
                    "SFTP host key was not observed before the connection ended.",
                    "Retry when the server is reachable; pin HOST_KEY_FINGERPRINT for vendor/outbound SFTP.");
            }

            if (string.IsNullOrWhiteSpace(pin))
            {
                var fingerprint = !string.IsNullOrWhiteSpace(sha256)
                    ? $"SHA256:{sha256.TrimEnd('=')}"
                    : md5 is { Length: > 0 } ? "MD5:" + string.Join(":", md5.Select(b => b.ToString("x2"))) : "(unavailable)";
                return new DiagnosticStep("HOST_KEY", DiagnosticStatus.Skipped,
                    $"SFTP host key was observed ({fingerprint}) but no HOST_KEY_FINGERPRINT is pinned.",
                    "Pin HOST_KEY_FINGERPRINT after verifying the fingerprint out-of-band.");
            }

            return new DiagnosticStep("HOST_KEY", DiagnosticStatus.Ok,
                "SFTP server host key matches HOST_KEY_FINGERPRINT.");
        }

        private static (string Host, int Port) ParseEndpoint(string target, IReadOnlyDictionary<string, string> options)
        {
            var host = GetOption(options, "HOST") ?? target;
            var port = 22;
            if (int.TryParse(GetOption(options, "PORT"), out var parsedPort) && parsedPort is > 0 and <= 65535)
                port = parsedPort;

            if (Uri.TryCreate(host, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
            {
                host = uri.Host;
                if (!options.ContainsKey("PORT") && !uri.IsDefaultPort)
                    port = uri.Port;
            }
            else
            {
                var colon = host.LastIndexOf(':');
                if (colon > 0 && colon == host.IndexOf(':') && int.TryParse(host[(colon + 1)..], out var inlinePort))
                {
                    host = host[..colon];
                    if (!options.ContainsKey("PORT"))
                        port = inlinePort;
                }
            }

            return (host, port);
        }

        private static string? GetOption(IReadOnlyDictionary<string, string> options, params string[] names)
        {
            foreach (var name in names)
            {
                if (options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
                    return value;
            }

            return null;
        }

        private static Dictionary<string, string> BuildOptions(
            string host,
            int port,
            string username,
            string? password,
            string? keyFilePath,
            string? passphrase,
            int timeoutSeconds,
            string? hostKeyFingerprint,
            bool atomicUpload,
            bool allowUnpinnedHostKey)
        {
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["HOST"] = host,
                ["PORT"] = port.ToString(),
                ["USER"] = username,
                ["TIMEOUT_SECONDS"] = timeoutSeconds.ToString(),
                ["ATOMIC_UPLOAD"] = atomicUpload.ToString(),
                ["ALLOW_UNPINNED_HOST_KEY"] = allowUnpinnedHostKey.ToString()
            };
            if (!string.IsNullOrEmpty(password)) options["PASSWORD"] = password;
            if (!string.IsNullOrEmpty(keyFilePath)) options["KEYFILE"] = keyFilePath;
            if (!string.IsNullOrEmpty(passphrase)) options["PASSPHRASE"] = passphrase;
            if (!string.IsNullOrEmpty(hostKeyFingerprint)) options["HOST_KEY_FINGERPRINT"] = hostKeyFingerprint;
            return options;
        }

        internal static string NormalizeRemotePath(string path) =>
            string.IsNullOrEmpty(path) ? path : path.Replace('\\', '/');

        private static bool ShouldWrapProviderException(Exception ex) =>
            ex is SshException or SftpPathNotFoundException or SftpPermissionDeniedException
                or System.Net.Sockets.SocketException or TimeoutException or InvalidOperationException;
    }
}
