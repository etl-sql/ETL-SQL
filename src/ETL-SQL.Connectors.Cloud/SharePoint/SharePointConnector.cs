using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Shared;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Governance;
using ETL_SQL.Data;

namespace ETL_SQL.Connectors
{
    public class SharePointConnector : IRemoteFileSystem, IDataSource, IConnector
    {
        private readonly ILogger _logger;
        private readonly IExecutionContext? _context;
        private readonly HttpClient _httpClient;
        private readonly bool _ownsHttpClient = true;

        private readonly string _siteUrl = "";
        private readonly string _authMode = "INTEGRATED";
        private readonly string _documentLibrary = "Shared Documents";
        private readonly string _listName = "";
        private readonly Dictionary<string, string> _options = new(StringComparer.OrdinalIgnoreCase);

        private string? _cachedToken;
        private DateTime _tokenExpiry = DateTime.MinValue;

        public string Name => "SHAREPOINT";
        public IReadOnlyList<string> Aliases => new[] { "SP" };
        public string Path => _siteUrl;
        public Dictionary<string, string>? Options => _options;
        public string ConnectorType => "SHAREPOINT";

        public SharePointConnector()
        {
            _logger = NullLogger.Instance;
            _httpClient = PolicyBoundHttp.CreateClient();
        }

        public SharePointConnector(IExecutionContext context, string connectionString, Dictionary<string, string>? options = null, HttpMessageHandler? handler = null)
        {
            _context = context;
            _logger = context.Logger;
            _siteUrl = connectionString;

            if (options != null)
            {
                foreach (var kv in options)
                {
                    _options[kv.Key] = kv.Value;
                }
            }

            _authMode = _options.GetValueOrDefault("AUTH_MODE", "INTEGRATED").ToUpperInvariant();
            _documentLibrary = _options.GetValueOrDefault("DOCUMENT_LIBRARY", "Shared Documents");
            _listName = _options.GetValueOrDefault("LIST_NAME", "");

            // Security Hardening: Validate site host against egress policy
            if (!string.IsNullOrEmpty(_siteUrl) && Uri.TryCreate(_siteUrl, UriKind.Absolute, out var uri))
            {
                ConnectorPolicyAuthorizer.EnforceEnterpriseUrl(context, uri);
            }

            // Setup HTTP client based on authentication mode
            if (handler != null)
            {
                _httpClient = PolicyBoundHttp.CreateClient(handler);
            }
            else
            {
                NetworkCredential? credentials = null;
                if (_authMode == "INTEGRATED")
                {
                    credentials = CredentialCache.DefaultNetworkCredentials;
                }
                else if (_authMode == "AD_WINDOWS")
                {
                    string user = _options.GetValueOrDefault("USER", "");
                    string pass = DecryptIfNeeded(_options.GetValueOrDefault("PASSWORD", ""));
                    string domain = _options.GetValueOrDefault("DOMAIN", "");

                    credentials = new NetworkCredential(user, pass, domain);
                }

                _httpClient = PolicyBoundHttp.CreateClient(h => h.Credentials = credentials);
            }
        }

        public async Task<string> GetVersionAsync(IExecutionContext context, string connectionString)
        {
            // The registered connector has no site of its own: probe the site it is handed.
            if (string.IsNullOrEmpty(_siteUrl) && !string.IsNullOrEmpty(connectionString))
            {
                if (!Uri.TryCreate(connectionString, UriKind.Absolute, out _))
                    return "SharePoint REST API Connector v1.0 (Offline - Invalid Site URL)";
                await using var probe = new SharePointConnector(context, connectionString);
                return await probe.GetVersionAsync(context, connectionString);
            }

            if (string.IsNullOrEmpty(_siteUrl) || !Uri.TryCreate(_siteUrl, UriKind.Absolute, out var uri))
            {
                return "SharePoint REST API Connector v1.0 (Offline - Invalid Site URL)";
            }

            ConnectorPolicyAuthorizer.EnforceEnterpriseUrl(context, uri);

            try
            {
                await AuthenticateAsync();
                string requestUrl = $"{_siteUrl.TrimEnd('/')}/_api/web";

                _httpClient.DefaultRequestHeaders.Accept.Clear();
                _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var response = await _httpClient.GetAsync(requestUrl);
                if (!response.IsSuccessStatusCode)
                {
                    throw new ExecutionException($"SharePoint site connection failed. HTTP Status: {response.StatusCode}");
                }

                var json = await response.Content.ReadAsStringAsync();
                string siteTitle = "Unknown SharePoint Site";

                using var doc = JsonDocument.Parse(json);
                JsonElement dProp;
                if (doc.RootElement.TryGetProperty("d", out dProp))
                {
                    if (dProp.TryGetProperty("Title", out var titleProp))
                    {
                        siteTitle = titleProp.GetString() ?? siteTitle;
                    }
                }
                else if (doc.RootElement.TryGetProperty("value", out var valProp))
                {
                    if (valProp.TryGetProperty("Title", out var titleProp))
                    {
                        siteTitle = titleProp.GetString() ?? siteTitle;
                    }
                }
                else if (doc.RootElement.TryGetProperty("Title", out var titleProp))
                {
                    siteTitle = titleProp.GetString() ?? siteTitle;
                }

                return $"SharePoint REST API Connector v1.0 (Connected - Site: {siteTitle})";
            }
            catch (Exception ex)
            {
                throw ConnectorExceptionWrapper.Wrap("SharePoint", ex);
            }
        }

        public HashSet<string> GetSupportedFunctions() => new();
        public HashSet<string> GetSupportedKeywords() => new();

        public Dictionary<string, string[]> GetSupportedOptions() => new(StringComparer.OrdinalIgnoreCase)
        {
            { "AUTH_MODE", new[] { "ENTRA_ID", "AD_WINDOWS", "INTEGRATED", "ADFS" } },
            { "USER", Array.Empty<string>() },
            { "PASSWORD", Array.Empty<string>() },
            { "DOMAIN", Array.Empty<string>() },
            { "CLIENT_ID", Array.Empty<string>() },
            { "CLIENT_SECRET", Array.Empty<string>() },
            { "TENANT_ID", Array.Empty<string>() },
            { "DOCUMENT_LIBRARY", Array.Empty<string>() },
            { "LIST_NAME", Array.Empty<string>() }
        };

        public Dictionary<string, string[]> GetOptionValues() => new(StringComparer.OrdinalIgnoreCase)
        {
            { "AUTH_MODE", new[] { "ENTRA_ID", "AD_WINDOWS", "INTEGRATED", "ADFS" } }
        };

        public string GetHelp() =>
            "SHAREPOINT Connector: Manages files in Document Libraries and reads/writes SharePoint Lists.\n" +
            "Supports: SEND FILE, RECEIVE FILE, DELETE FILE, RENAME FILE, CREATE/DELETE DIRECTORY.\n\n" +
            "Options:\n" +
            "  AUTH_MODE: ENTRA_ID (default for cloud), AD_WINDOWS (NTLM/Kerberos), INTEGRATED, or ADFS.\n" +
            "  USER: Domain service account username.\n" +
            "  PASSWORD: Account password (use ENC: prefix for safety).\n" +
            "  DOMAIN: Active Directory domain name.\n" +
            "  CLIENT_ID / CLIENT_SECRET / TENANT_ID: Microsoft Entra ID App Credentials.\n" +
            "  DOCUMENT_LIBRARY: Target library path/title (default: 'Shared Documents').\n" +
            "  LIST_NAME: Default list title for list queries.";

        public IDataSource CreateDataSource(IExecutionContext context, string connectionString, Dictionary<string, string>? options = null)
        {
            return new SharePointConnector(context, connectionString, options);
        }

        public Task<IEnumerable<string>> GetTablesAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetViewsAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetColumnsAsync(IExecutionContext context, string connectionString, string tableName) => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetProceduresAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());

        public string BuildConnectionString(Dictionary<string, string> properties)
        {
            return properties.GetValueOrDefault("URL", "");
        }

        public string? GetHost(string connectionString, Dictionary<string, string>? options = null)
        {
            return Uri.TryCreate(connectionString, UriKind.Absolute, out var uri) ? uri.Host : null;
        }

        private async Task AuthenticateAsync(CancellationToken cancellationToken = default)
        {
            if (_authMode != "ENTRA_ID") return;
            if (_cachedToken != null && DateTime.UtcNow < _tokenExpiry)
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _cachedToken);
                return;
            }

            string tenantId = _options.GetValueOrDefault("TENANT_ID", "");
            string clientId = _options.GetValueOrDefault("CLIENT_ID", "");
            string clientSecret = DecryptIfNeeded(_options.GetValueOrDefault("CLIENT_SECRET", ""));
            if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            {
                throw new ExecutionException("ENTRA_ID authentication requires TENANT_ID, CLIENT_ID, and CLIENT_SECRET.");
            }

            _logger.Debug("Acquiring Entra ID access token for SharePoint.");

            // Build resource/scope scope based on SharePoint URL host
            var siteUri = new Uri(_siteUrl);
            string scope = $"https://{siteUri.Host}/.default";

            using var tokenClient = PolicyBoundHttp.CreateClient();
            var dict = new Dictionary<string, string>
            {
                { "grant_type", "client_credentials" },
                { "client_id", clientId },
                { "client_secret", clientSecret },
                { "scope", scope }
            };

            var tokenUri = new Uri($"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token");
            if (_context != null)
                ConnectorPolicyAuthorizer.EnforceEnterpriseUrl(_context, tokenUri);

            using var req = new HttpRequestMessage(HttpMethod.Post, tokenUri)
            {
                Content = new FormUrlEncodedContent(dict)
            };

            using var res = await tokenClient.SendAsync(req, cancellationToken);
            if (!res.IsSuccessStatusCode)
            {
                throw new ExecutionException($"Failed to acquire OAuth token from Entra ID. Status: {res.StatusCode}");
            }

            var json = await res.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            _cachedToken = root.GetProperty("access_token").GetString() ?? throw new ExecutionException("Access token not found in Entra ID response.");
            int expires = root.TryGetProperty("expires_in", out var expVal) ? expVal.GetInt32() : 3600;
            _tokenExpiry = DateTime.UtcNow.AddSeconds(expires - 60);

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _cachedToken);
        }

        internal string GetServerRelativeUrl(string path)
        {
            // Ensures correct URL format: /sites/MySite/Shared Documents/SubFolder
            var baseUri = new Uri(_siteUrl);
            string baseRelative = baseUri.AbsolutePath.TrimEnd('/');
            string cleanPath = path.Replace('\\', '/').Trim('/');

            // Remove leading slash for matching cleanPath which has also had it trimmed
            string baseRelativeNoSlash = baseRelative.TrimStart('/');

            if (cleanPath.StartsWith(baseRelativeNoSlash, StringComparison.OrdinalIgnoreCase))
            {
                return "/" + cleanPath;
            }

            if (string.IsNullOrEmpty(cleanPath))
            {
                return $"{baseRelative}/{_documentLibrary}".TrimEnd('/');
            }

            return $"{baseRelative}/{_documentLibrary}/{cleanPath}".TrimEnd('/');
        }

        // ── IRemoteFileSystem Implementation ──────────────────────────────────────

        // Transport, JSON-shape and local-file failures are provider errors: they leave the connector
        // as a sanitized ExecutionException. Policy denials and cancellation pass through unchanged.
        private static bool ShouldWrapProviderException(Exception ex) =>
            ex is HttpRequestException or JsonException or IOException or UnauthorizedAccessException
                or UriFormatException or InvalidOperationException or KeyNotFoundException or FormatException;

        private static async Task Guard(Func<Task> operation)
        {
            try
            {
                await operation();
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("SharePoint", ex);
            }
        }

        private static Task<T> Guard<T>(Func<Task<T>> operation) =>
            ConnectorExceptionWrapper.RunAsync("SharePoint", ShouldWrapProviderException, operation);

        public IAsyncEnumerable<FileMetaData> ListFilesAsync(string path) =>
            ConnectorExceptionWrapper.WrapAsync(ListFilesCoreAsync(path), "SharePoint", ShouldWrapProviderException);

        private async IAsyncEnumerable<FileMetaData> ListFilesCoreAsync(string path)
        {
            await AuthenticateAsync();
            string folderUrl = GetServerRelativeUrl(path);

            // SharePoint list files REST call
            string requestUrl = $"{_siteUrl.TrimEnd('/')}/_api/web/GetFolderByServerRelativeUrl('{Uri.EscapeDataString(folderUrl)}')/Files";
            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _httpClient.GetAsync(requestUrl);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.NotFound) yield break;
                throw new ExecutionException($"Failed to list files in '{path}'. SharePoint responded with {response.StatusCode}");
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            JsonElement valueProp;
            // OData structure can have values under root or root.value
            if (doc.RootElement.TryGetProperty("value", out valueProp) || doc.RootElement.TryGetProperty("d", out valueProp))
            {
                if (valueProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in valueProp.EnumerateArray())
                    {
                        string name = item.GetProperty("Name").GetString() ?? "";
                        string relativeUrl = item.GetProperty("ServerRelativeUrl").GetString() ?? "";
                        long size = 0;
                        if (item.TryGetProperty("Length", out var lenProp))
                        {
                            if (lenProp.ValueKind == JsonValueKind.Number)
                            {
                                size = lenProp.GetInt64();
                            }
                            else if (lenProp.ValueKind == JsonValueKind.String)
                            {
                                long.TryParse(lenProp.GetString(), out size);
                            }
                        }

                        DateTime modified = DateTime.MinValue;
                        if (item.TryGetProperty("TimeLastModified", out var modProp))
                        {
                            DateTime.TryParse(modProp.GetString(), out modified);
                        }

                        yield return new FileMetaData
                        {
                            Name = name,
                            FullPath = relativeUrl,
                            Size = size,
                            LastModified = modified,
                            IsDirectory = false
                        };
                    }
                }
            }
        }

        public Task UploadFileAsync(string localPath, string remotePath, bool overwrite = true) =>
            Guard(() => UploadFileCoreAsync(localPath, remotePath, overwrite));

        private async Task UploadFileCoreAsync(string localPath, string remotePath, bool overwrite)
        {
            if (_context != null && _context.IsWhatIf) return;
            await AuthenticateAsync();

            if (!File.Exists(localPath))
            {
                throw new ExecutionException($"Local source file not found: {localPath}");
            }

            string relativeUrl = GetServerRelativeUrl(remotePath);
            string folderPath = System.IO.Path.GetDirectoryName(relativeUrl)?.Replace('\\', '/') ?? "";
            string fileName = System.IO.Path.GetFileName(relativeUrl);

            string requestUrl = $"{_siteUrl.TrimEnd('/')}/_api/web/GetFolderByServerRelativeUrl('{Uri.EscapeDataString(folderPath)}')/Files/Add(url='{Uri.EscapeDataString(fileName)}', overwrite={overwrite.ToString().ToLowerInvariant()})";

            using var fileStream = File.OpenRead(localPath);
            using var content = new StreamContent(fileStream);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            using var response = await _httpClient.PostAsync(requestUrl, content);
            if (!response.IsSuccessStatusCode)
            {
                throw new ExecutionException($"Failed to upload file to SharePoint: {response.StatusCode}");
            }
        }

        public Task DownloadFileAsync(string remotePath, string localPath, bool overwrite = true) =>
            Guard(() => DownloadFileCoreAsync(remotePath, localPath, overwrite));

        private async Task DownloadFileCoreAsync(string remotePath, string localPath, bool overwrite)
        {
            await AuthenticateAsync();

            if (!overwrite && File.Exists(localPath))
            {
                throw new ExecutionException($"Local destination file already exists (overwrite=OFF): {localPath}");
            }

            string relativeUrl = GetServerRelativeUrl(remotePath);
            string requestUrl = $"{_siteUrl.TrimEnd('/')}/_api/web/GetFileByServerRelativeUrl('{Uri.EscapeDataString(relativeUrl)}')/$value";
            using var response = await _httpClient.GetAsync(requestUrl);
            if (!response.IsSuccessStatusCode)
            {
                throw new ExecutionException($"Failed to download file from SharePoint: {response.StatusCode}");
            }

            using var destStream = File.Create(localPath);
            await response.Content.CopyToAsync(destStream);
        }

        public Task DeleteFileAsync(string remotePath) => Guard(() => DeleteFileCoreAsync(remotePath));

        private async Task DeleteFileCoreAsync(string remotePath)
        {
            if (_context != null && _context.IsWhatIf) return;
            await AuthenticateAsync();
            string relativeUrl = GetServerRelativeUrl(remotePath);
            string requestUrl = $"{_siteUrl.TrimEnd('/')}/_api/web/GetFileByServerRelativeUrl('{Uri.EscapeDataString(relativeUrl)}')";

            using var req = new HttpRequestMessage(HttpMethod.Post, requestUrl);
            req.Headers.Add("X-HTTP-Method", "DELETE");
            req.Headers.Add("IF-MATCH", "*");

            using var response = await _httpClient.SendAsync(req);
            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
            {
                throw new ExecutionException($"Failed to delete file on SharePoint: {response.StatusCode}");
            }
        }

        public Task<bool> FileExistsAsync(string remotePath) => Guard(() => FileExistsCoreAsync(remotePath));

        private async Task<bool> FileExistsCoreAsync(string remotePath)
        {
            await AuthenticateAsync();
            string relativeUrl = GetServerRelativeUrl(remotePath);
            string requestUrl = $"{_siteUrl.TrimEnd('/')}/_api/web/GetFileByServerRelativeUrl('{Uri.EscapeDataString(relativeUrl)}')";

            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _httpClient.GetAsync(requestUrl);
            return response.IsSuccessStatusCode;
        }

        public Task<bool> DirectoryExistsAsync(string remotePath) => Guard(() => DirectoryExistsCoreAsync(remotePath));

        private async Task<bool> DirectoryExistsCoreAsync(string remotePath)
        {
            await AuthenticateAsync();
            string relativeUrl = GetServerRelativeUrl(remotePath);
            string requestUrl = $"{_siteUrl.TrimEnd('/')}/_api/web/GetFolderByServerRelativeUrl('{Uri.EscapeDataString(relativeUrl)}')";

            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _httpClient.GetAsync(requestUrl);
            return response.IsSuccessStatusCode;
        }

        public Task RenameFileAsync(string remoteSource, string remoteDest, bool overwrite = true) =>
            Guard(() => RenameFileCoreAsync(remoteSource, remoteDest, overwrite));

        private async Task RenameFileCoreAsync(string remoteSource, string remoteDest, bool overwrite)
        {
            if (_context != null && _context.IsWhatIf) return;
            await AuthenticateAsync();
            string srcUrl = GetServerRelativeUrl(remoteSource);
            string destUrl = GetServerRelativeUrl(remoteDest);

            string requestUrl = $"{_siteUrl.TrimEnd('/')}/_api/web/GetFileByServerRelativeUrl('{Uri.EscapeDataString(srcUrl)}')/MoveTo(newUrl='{Uri.EscapeDataString(destUrl)}', flags={(overwrite ? 1 : 0)})";

            using var response = await _httpClient.PostAsync(requestUrl, null);
            if (!response.IsSuccessStatusCode)
            {
                throw new ExecutionException($"Failed to rename SharePoint file: {response.StatusCode}");
            }
        }

        public Task CreateDirectoryAsync(string remotePath) => Guard(() => CreateDirectoryCoreAsync(remotePath));

        private async Task CreateDirectoryCoreAsync(string remotePath)
        {
            if (_context != null && _context.IsWhatIf) return;
            await AuthenticateAsync();
            string relativeUrl = GetServerRelativeUrl(remotePath);
            string parentPath = System.IO.Path.GetDirectoryName(relativeUrl)?.Replace('\\', '/') ?? "";
            string newFolderName = System.IO.Path.GetFileName(relativeUrl);

            string requestUrl = $"{_siteUrl.TrimEnd('/')}/_api/web/GetFolderByServerRelativeUrl('{Uri.EscapeDataString(parentPath)}')/Folders";

            var payload = new { ServerRelativeUrl = relativeUrl };
            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var response = await _httpClient.PostAsync(requestUrl, content);
            if (!response.IsSuccessStatusCode)
            {
                throw new ExecutionException($"Failed to create folder on SharePoint: {response.StatusCode}");
            }
        }

        public Task DeleteDirectoryAsync(string remotePath) => Guard(() => DeleteDirectoryCoreAsync(remotePath));

        private async Task DeleteDirectoryCoreAsync(string remotePath)
        {
            if (_context != null && _context.IsWhatIf) return;
            await AuthenticateAsync();
            string relativeUrl = GetServerRelativeUrl(remotePath);
            string requestUrl = $"{_siteUrl.TrimEnd('/')}/_api/web/GetFolderByServerRelativeUrl('{Uri.EscapeDataString(relativeUrl)}')";

            using var req = new HttpRequestMessage(HttpMethod.Post, requestUrl);
            req.Headers.Add("X-HTTP-Method", "DELETE");
            req.Headers.Add("IF-MATCH", "*");

            using var response = await _httpClient.SendAsync(req);
            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
            {
                throw new ExecutionException($"Failed to delete folder on SharePoint: {response.StatusCode}");
            }
        }

        // ── IDataSource Implementation ────────────────────────────────────────────

        public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) =>
            ReadBatches(batchSize, _context?.CancellationToken ?? CancellationToken.None);

        public IAsyncEnumerable<DataTable> ReadBatches(int batchSize, CancellationToken cancellationToken) =>
            ConnectorExceptionWrapper.WrapAsync(ReadBatchesCore(batchSize, cancellationToken), "SharePoint",
                ex => ex is not ExecutionException && ex is not OperationCanceledException, cancellationToken);

        private async IAsyncEnumerable<DataTable> ReadBatchesCore(int batchSize,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
            await AuthenticateAsync(cancellationToken);
            string list = string.IsNullOrEmpty(_listName) ? throw new ExecutionException("LIST_NAME option must be configured to query SharePoint lists.") : _listName;

            var siteUri = new Uri(_siteUrl);
            Uri? requestUri = new($"{_siteUrl.TrimEnd('/')}/_api/web/lists/GetByTitle('{ODataTitle(list)}')/items");
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var table = new DataTable();
            while (requestUri != null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!requestUri.Scheme.Equals(siteUri.Scheme, StringComparison.OrdinalIgnoreCase)
                    || !requestUri.Host.Equals(siteUri.Host, StringComparison.OrdinalIgnoreCase)
                    || requestUri.Port != siteUri.Port)
                    throw new ExecutionException("SharePoint list continuation must remain on the configured site origin.");
                if (!visited.Add(requestUri.AbsoluteUri))
                    throw new ExecutionException("SharePoint list continuation repeated an already-read page.");
                if (_context != null) ConnectorPolicyAuthorizer.EnforceEnterpriseUrl(_context, requestUri);

                using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                using var response = await _httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                    throw new ExecutionException($"Failed to query SharePoint List '{list}': {response.StatusCode}");

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var envelope = root.TryGetProperty("d", out var d) ? d : root;
                // COMPAT_BREAK: 0.20.0 — verbose OData results are rows, not an empty list.
                var rows = envelope.ValueKind == JsonValueKind.Array ? envelope
                    : envelope.TryGetProperty("results", out var results) ? results
                    : envelope.TryGetProperty("value", out var value) ? value
                    : throw new ExecutionException("Unsupported SharePoint list response: expected an OData result array.");
                if (rows.ValueKind != JsonValueKind.Array)
                    throw new ExecutionException("Unsupported SharePoint list response: expected an OData result array.");

                foreach (var rowObj in rows.EnumerateArray())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (table.Schema.ColumnNames.Count == 0)
                        table.SetColumns(rowObj.EnumerateObject().Select(p => p.Name));
                    var newRow = table.NewRow();
                    foreach (var prop in rowObj.EnumerateObject())
                    {
                        newRow[prop.Name] = ListValue(prop.Value);
                    }
                    await table.AddRowAsync(newRow);
                    if (table.Rows.Count >= batchSize)
                    {
                        yield return table;
                        // COMPAT_BREAK: 0.20.0 — returned rows must not appear in later batches.
                        table = new DataTable { Schema = table.Schema };
                    }
                }

                // COMPAT_BREAK: 0.20.0 — a list extraction includes every continuation page.
                var next = root.TryGetProperty("@odata.nextLink", out var modern) ? modern
                    : root.TryGetProperty("odata.nextLink", out var minimal) ? minimal
                    : envelope.ValueKind == JsonValueKind.Object && envelope.TryGetProperty("__next", out var verbose) ? verbose
                    : default;
                if (next.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                    || next.ValueKind == JsonValueKind.String && string.IsNullOrEmpty(next.GetString()))
                    requestUri = null;
                else if (next.ValueKind == JsonValueKind.String && Uri.TryCreate(requestUri, next.GetString(), out var nextUri))
                    requestUri = nextUri;
                else
                    throw new ExecutionException("Invalid SharePoint list continuation URL.");
            }
            if (table.Rows.Count > 0) yield return table;
        }

        /// <summary>
        /// Encodes a list title as an OData string literal. A single quote is doubled before URL
        /// escaping: SharePoint decodes <c>%27</c> back to a quote before it parses the literal.
        /// </summary>
        /// <summary>
        /// Maps a list field to the engine's runtime types: numbers are <see cref="decimal"/> (double
        /// only when out of decimal range), booleans stay booleans, JSON null is SQL NULL, and nested
        /// objects/arrays keep their JSON text.
        /// </summary>
        // COMPAT_BREAK: 0.22.0 — list numbers and booleans were strings ("7", "True") and null was "".
        internal static object? ListValue(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number => value.TryGetDecimal(out var number) ? number : value.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => value.GetRawText()
        };

        internal static string ODataTitle(string title) => Uri.EscapeDataString(title.Replace("'", "''"));

        public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) =>
            Guard(() => WriteBatchesCoreAsync(batches));

        private async Task WriteBatchesCoreAsync(IAsyncEnumerable<DataTable> batches)
        {
            if (_context != null && _context.IsWhatIf) return;

            await AuthenticateAsync();
            string list = string.IsNullOrEmpty(_listName) ? throw new ExecutionException("LIST_NAME option must be configured to write to SharePoint lists.") : _listName;

            string requestUrl = $"{_siteUrl.TrimEnd('/')}/_api/web/lists/GetByTitle('{ODataTitle(list)}')/items";
            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            await foreach (var batch in batches)
            {
                foreach (var row in batch.Rows)
                {
                    var payload = new Dictionary<string, object?>();
                    foreach (var colName in batch.ColumnNames)
                    {
                        var val = row[colName];
                        if (val != null)
                        {
                            payload[colName] = val;
                        }
                    }

                    using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                    using var response = await _httpClient.PostAsync(requestUrl, content);
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new ExecutionException($"Failed to write item to SharePoint list: {response.StatusCode}");
                    }
                }
            }
        }

        public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult(Enumerable.Empty<string>());
        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }
        public IDataSource WithTable(string tableName)
        {
            var options = new Dictionary<string, string>(_options, StringComparer.OrdinalIgnoreCase);
            options["LIST_NAME"] = tableName;
            // The clone borrows this connection's HttpClient (same site, same credentials) instead
            // of opening an undisposed client and socket handler per table reference.
            return new SharePointConnector(this, options);
        }

        private SharePointConnector(SharePointConnector parent, Dictionary<string, string> options)
        {
            _context = parent._context;
            _logger = parent._logger;
            _siteUrl = parent._siteUrl;
            _httpClient = parent._httpClient;
            _ownsHttpClient = false;
            foreach (var kv in options) _options[kv.Key] = kv.Value;
            _authMode = parent._authMode;
            _documentLibrary = _options.GetValueOrDefault("DOCUMENT_LIBRARY", "Shared Documents");
            _listName = _options.GetValueOrDefault("LIST_NAME", "");
        }

        public ValueTask DisposeAsync()
        {
            if (_ownsHttpClient) _httpClient.Dispose();
            return ValueTask.CompletedTask;
        }

        private string DecryptIfNeeded(string value)
        {
            return value.StartsWith("ENC:", StringComparison.OrdinalIgnoreCase) && _context != null
                ? _context.DecryptValue(value) ?? ""
                : value;
        }
    }
}
