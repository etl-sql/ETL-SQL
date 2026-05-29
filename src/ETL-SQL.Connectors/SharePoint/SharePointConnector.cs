using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.Data;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Connectors.Shared;

namespace ETL_SQL.Connectors
{
    public class SharePointConnector : IRemoteFileSystem, IDataSource, IConnector
    {
        private readonly ILogger _logger;
        private readonly IExecutionContext? _context;
        private readonly HttpClient _httpClient;
        
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
            _httpClient = new HttpClient();
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
                context.SecurityService.ValidateHost(uri.Host);
            }

            // Setup HTTP client based on authentication mode
            if (handler != null)
            {
                _httpClient = new HttpClient(handler);
            }
            else
            {
                var clientHandler = new HttpClientHandler();
                if (_authMode == "INTEGRATED")
                {
                    clientHandler.UseDefaultCredentials = true;
                }
                else if (_authMode == "AD_WINDOWS")
                {
                    string user = _options.GetValueOrDefault("USER", "");
                    string pass = _options.GetValueOrDefault("PASSWORD", "");
                    string domain = _options.GetValueOrDefault("DOMAIN", "");
                    
                    if (pass.StartsWith("ENC:"))
                    {
                        pass = context.DecryptValue(pass) ?? "";
                    }

                    clientHandler.Credentials = new NetworkCredential(user, pass, domain);
                }
                
                _httpClient = new HttpClient(clientHandler);
            }
        }

        public async Task<string> GetVersionAsync(IExecutionContext context, string connectionString)
        {
            return "SharePoint REST API Connector v1.0";
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

        private async Task AuthenticateAsync()
        {
            if (_authMode != "ENTRA_ID") return;
            if (_cachedToken != null && DateTime.UtcNow < _tokenExpiry)
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _cachedToken);
                return;
            }

            string tenantId = _options.GetValueOrDefault("TENANT_ID", "");
            string clientId = _options.GetValueOrDefault("CLIENT_ID", "");
            string clientSecret = _options.GetValueOrDefault("CLIENT_SECRET", "");
            if (clientSecret.StartsWith("ENC:") && _context != null)
            {
                clientSecret = _context.DecryptValue(clientSecret) ?? "";
            }

            if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            {
                throw new ExecutionException("ENTRA_ID authentication requires TENANT_ID, CLIENT_ID, and CLIENT_SECRET.");
            }

            _logger.Debug("Acquiring Entra ID access token for SharePoint.");
            
            // Build resource/scope scope based on SharePoint URL host
            var siteUri = new Uri(_siteUrl);
            string scope = $"https://{siteUri.Host}/.default";

            using var tokenClient = new HttpClient();
            var dict = new Dictionary<string, string>
            {
                { "grant_type", "client_credentials" },
                { "client_id", clientId },
                { "client_secret", clientSecret },
                { "scope", scope }
            };

            var req = new HttpRequestMessage(HttpMethod.Post, $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token")
            {
                Content = new FormUrlEncodedContent(dict)
            };

            var res = await tokenClient.SendAsync(req);
            if (!res.IsSuccessStatusCode)
            {
                var errContent = await res.Content.ReadAsStringAsync();
                throw new ExecutionException($"Failed to acquire OAuth token from Entra ID. Status: {res.StatusCode}, Details: {errContent}");
            }

            var json = await res.Content.ReadAsStringAsync();
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

        public async IAsyncEnumerable<FileMetaData> ListFilesAsync(string path)
        {
            await AuthenticateAsync();
            string folderUrl = GetServerRelativeUrl(path);
            
            // SharePoint list files REST call
            string requestUrl = $"{_siteUrl.TrimEnd('/')}/_api/web/GetFolderByServerRelativeUrl('{Uri.EscapeDataString(folderUrl)}')/Files";
            System.Console.WriteLine($"[TEST-PRINT] ListFiles requestUrl={requestUrl}");
            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var response = await _httpClient.GetAsync(requestUrl);
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

        public async Task UploadFileAsync(string localPath, string remotePath, bool overwrite = true)
        {
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

            var response = await _httpClient.PostAsync(requestUrl, content);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new ExecutionException($"Failed to upload file to SharePoint: {response.StatusCode}. Details: {error}");
            }
        }

        public async Task DownloadFileAsync(string remotePath, string localPath, bool overwrite = true)
        {
            await AuthenticateAsync();

            if (!overwrite && File.Exists(localPath))
            {
                throw new ExecutionException($"Local destination file already exists (overwrite=OFF): {localPath}");
            }

            string relativeUrl = GetServerRelativeUrl(remotePath);
            string requestUrl = $"{_siteUrl.TrimEnd('/')}/_api/web/GetFileByServerRelativeUrl('{Uri.EscapeDataString(relativeUrl)}')/$value";
            System.Console.WriteLine($"[TEST-PRINT] requestUrl={requestUrl}");
            var response = await _httpClient.GetAsync(requestUrl);
            if (!response.IsSuccessStatusCode)
            {
                throw new ExecutionException($"Failed to download file from SharePoint: {response.StatusCode}");
            }

            using var destStream = File.Create(localPath);
            await response.Content.CopyToAsync(destStream);
        }

        public async Task DeleteFileAsync(string remotePath)
        {
            await AuthenticateAsync();
            string relativeUrl = GetServerRelativeUrl(remotePath);
            string requestUrl = $"{_siteUrl.TrimEnd('/')}/_api/web/GetFileByServerRelativeUrl('{Uri.EscapeDataString(relativeUrl)}')";

            var req = new HttpRequestMessage(HttpMethod.Post, requestUrl);
            req.Headers.Add("X-HTTP-Method", "DELETE");
            req.Headers.Add("IF-MATCH", "*");

            var response = await _httpClient.SendAsync(req);
            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
            {
                throw new ExecutionException($"Failed to delete file on SharePoint: {response.StatusCode}");
            }
        }

        public async Task<bool> FileExistsAsync(string remotePath)
        {
            await AuthenticateAsync();
            string relativeUrl = GetServerRelativeUrl(remotePath);
            string requestUrl = $"{_siteUrl.TrimEnd('/')}/_api/web/GetFileByServerRelativeUrl('{Uri.EscapeDataString(relativeUrl)}')";

            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var response = await _httpClient.GetAsync(requestUrl);
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> DirectoryExistsAsync(string remotePath)
        {
            await AuthenticateAsync();
            string relativeUrl = GetServerRelativeUrl(remotePath);
            string requestUrl = $"{_siteUrl.TrimEnd('/')}/_api/web/GetFolderByServerRelativeUrl('{Uri.EscapeDataString(relativeUrl)}')";

            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var response = await _httpClient.GetAsync(requestUrl);
            return response.IsSuccessStatusCode;
        }

        public async Task RenameFileAsync(string remoteSource, string remoteDest, bool overwrite = true)
        {
            await AuthenticateAsync();
            string srcUrl = GetServerRelativeUrl(remoteSource);
            string destUrl = GetServerRelativeUrl(remoteDest);

            string requestUrl = $"{_siteUrl.TrimEnd('/')}/_api/web/GetFileByServerRelativeUrl('{Uri.EscapeDataString(srcUrl)}')/MoveTo(newUrl='{Uri.EscapeDataString(destUrl)}', flags={(overwrite ? 1 : 0)})";

            var response = await _httpClient.PostAsync(requestUrl, null);
            if (!response.IsSuccessStatusCode)
            {
                throw new ExecutionException($"Failed to rename SharePoint file: {response.StatusCode}");
            }
        }

        public async Task CreateDirectoryAsync(string remotePath)
        {
            await AuthenticateAsync();
            string relativeUrl = GetServerRelativeUrl(remotePath);
            string parentPath = System.IO.Path.GetDirectoryName(relativeUrl)?.Replace('\\', '/') ?? "";
            string newFolderName = System.IO.Path.GetFileName(relativeUrl);

            string requestUrl = $"{_siteUrl.TrimEnd('/')}/_api/web/GetFolderByServerRelativeUrl('{Uri.EscapeDataString(parentPath)}')/Folders";
            
            var payload = new { ServerRelativeUrl = relativeUrl };
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(requestUrl, content);
            if (!response.IsSuccessStatusCode)
            {
                throw new ExecutionException($"Failed to create folder on SharePoint: {response.StatusCode}");
            }
        }

        public async Task DeleteDirectoryAsync(string remotePath)
        {
            await AuthenticateAsync();
            string relativeUrl = GetServerRelativeUrl(remotePath);
            string requestUrl = $"{_siteUrl.TrimEnd('/')}/_api/web/GetFolderByServerRelativeUrl('{Uri.EscapeDataString(relativeUrl)}')";

            var req = new HttpRequestMessage(HttpMethod.Post, requestUrl);
            req.Headers.Add("X-HTTP-Method", "DELETE");
            req.Headers.Add("IF-MATCH", "*");

            var response = await _httpClient.SendAsync(req);
            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
            {
                throw new ExecutionException($"Failed to delete folder on SharePoint: {response.StatusCode}");
            }
        }

        // ── IDataSource Implementation ────────────────────────────────────────────

        public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000)
        {
            await AuthenticateAsync();
            string list = string.IsNullOrEmpty(_listName) ? throw new ExecutionException("LIST_NAME option must be configured to query SharePoint lists.") : _listName;

            string requestUrl = $"{_siteUrl.TrimEnd('/')}/_api/web/lists/GetByTitle('{Uri.EscapeDataString(list)}')/items";
            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var response = await _httpClient.GetAsync(requestUrl);
            if (!response.IsSuccessStatusCode)
            {
                throw new ExecutionException($"Failed to query SharePoint List '{list}': {response.StatusCode}");
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            
            JsonElement valueProp;
            if (doc.RootElement.TryGetProperty("value", out valueProp) || doc.RootElement.TryGetProperty("d", out valueProp))
            {
                if (valueProp.ValueKind == JsonValueKind.Array)
                {
                    var table = new DataTable();
                    var rows = valueProp.EnumerateArray().ToList();
                    
                    if (rows.Count > 0)
                    {
                        // Infer schema columns from first row
                        var first = rows[0];
                        var columns = first.EnumerateObject().Select(p => p.Name).ToList();
                        table.SetColumns(columns);

                        foreach (var rowObj in rows)
                        {
                            var newRow = table.NewRow();
                            foreach (var prop in rowObj.EnumerateObject())
                            {
                                string val = prop.Value.ValueKind switch
                                {
                                    JsonValueKind.String => prop.Value.GetString() ?? "",
                                    JsonValueKind.Number => prop.Value.GetRawText(),
                                    JsonValueKind.True => "True",
                                    JsonValueKind.False => "False",
                                    JsonValueKind.Null => "",
                                    _ => prop.Value.GetRawText()
                                };
                                newRow[prop.Name] = val;
                            }
                            await table.AddRowAsync(newRow);

                            if (table.Rows.Count >= batchSize)
                            {
                                yield return table;
                                table = table.Clone();
                            }
                        }
                    }

                    if (table.Rows.Count > 0)
                    {
                        yield return table;
                    }
                }
            }
        }

        public async Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false)
        {
            await AuthenticateAsync();
            string list = string.IsNullOrEmpty(_listName) ? throw new ExecutionException("LIST_NAME option must be configured to write to SharePoint lists.") : _listName;

            string requestUrl = $"{_siteUrl.TrimEnd('/')}/_api/web/lists/GetByTitle('{Uri.EscapeDataString(list)}')/items";
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

                    var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                    var response = await _httpClient.PostAsync(requestUrl, content);
                    if (!response.IsSuccessStatusCode)
                    {
                        var err = await response.Content.ReadAsStringAsync();
                        throw new ExecutionException($"Failed to write item to SharePoint list: {response.StatusCode}. Details: {err}");
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
            return new SharePointConnector(_context!, _siteUrl, options);
        }

        public ValueTask DisposeAsync()
        {
            _httpClient.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
