using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.Data;
using ETL_SQL.Common;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Connectors.Rest
{
    /// <summary>
    /// Data source implementation for REST APIs.
    /// Supports flexible authentication, JSONPath extraction, and batched reading.
    /// </summary>
    public class RestDataSource : IDatabaseSource
    {
        private readonly string _url;
        private readonly Dictionary<string, string>? _options;
        private readonly ILogger _logger;
        private readonly IExecutionContext? _context;
        private static readonly HttpClient _httpClient = new HttpClient();

        public RestDataSource(IExecutionContext context, string url, Dictionary<string, string>? options = null)
        {
            _context = context;
            _url = url;
            _options = options;
            _logger = context.Logger;
            
            // Security Hardening: egress control
            context.SecurityService.ValidateHost(new Uri(url).Host);

            // Set default User-Agent as many APIs (like GitHub) require it
            if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
            {
                _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ETL-SQL-Engine", "1.0"));
            }
        }

        public string ConnectionString => _url;
        public string Path => _url;
        public Dictionary<string, string>? Options => _options;
        public string ConnectorType => "API";
        public string Dialect => "REST";
        public bool SupportsSqlPushdown => false;

        public IDataSource WithTable(string tableName) => this;

        public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000)
        {
            var request = BuildRequest();
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new ExecutionException($"API request failed with status {response.StatusCode}: {error}");
            }

            using var stream = await response.Content.ReadAsStreamAsync();
            
            string? rootPath = null;
            _options?.TryGetValue("ROOT_PATH", out rootPath);

            await foreach (var batch in JsonExtractor.ExtractBatchesAsync(stream, rootPath, batchSize))
            {
                yield return batch;
            }
        }

        public Task WriteBatches(IAsyncEnumerable<DataTable> batches)
        {
            throw new NotSupportedException("Writing to REST APIs is not yet supported in this version.");
        }

        public async Task<IEnumerable<string>> GetColumnsAsync()
        {
            var request = BuildRequest();
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode) return Enumerable.Empty<string>();

            using var stream = await response.Content.ReadAsStreamAsync();
            
            string? rootPath = null;
            _options?.TryGetValue("ROOT_PATH", out rootPath);

            return await JsonExtractor.GetColumnsAsync(stream, rootPath);
        }

        public Task<IEnumerable<string>> GetTablesAsync() => Task.FromResult<IEnumerable<string>>(new[] { "ENDPOINT" });
        public Task<IEnumerable<string>> GetViewsAsync() => Task.FromResult<IEnumerable<string>>(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetColumnsAsync(string tableName) => GetColumnsAsync();

        public Task TruncateAsync() => throw new NotSupportedException();
        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }
        public async Task<string> GetVersionAsync() => await Task.FromResult("REST API Connector 1.0");
        public HashSet<string> GetSupportedFunctions() => new(StringComparer.OrdinalIgnoreCase);

        public async IAsyncEnumerable<DataTable> ExecuteRawSql(string sql, IEnumerable<object?>? parameters = null)
        {
            if (sql.Trim().ToUpperInvariant() == "SELECT * FROM ENDPOINT")
            {
                await foreach (var batch in ReadBatches()) yield return batch;
            }
            else
            {
                throw new ExecutionException("REST Connector only supports 'SELECT * FROM ENDPOINT' as native SQL.");
            }
        }

        public async ValueTask DisposeAsync() => await Task.CompletedTask;

        private HttpRequestMessage BuildRequest()
        {
            string? methodStr = "GET";
            _options?.TryGetValue("METHOD", out methodStr);
            var method = new HttpMethod(methodStr ?? "GET");

            var request = new HttpRequestMessage(method, _url);

            // Authentication
            if (_options != null && _options.TryGetValue("AUTH_TYPE", out var authType))
            {
                switch (authType.ToUpperInvariant())
                {
                    case "BASIC":
                        if (_options.TryGetValue("USER", out var user) && _options.TryGetValue("PASSWORD", out var pass))
                        {
                            var authBytes = System.Text.Encoding.ASCII.GetBytes($"{user}:{pass}");
                            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
                        }
                        break;
                    case "BEARER":
                        if (_options.TryGetValue("TOKEN", out var token))
                        {
                            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                        }
                        break;
                    case "APIKEY":
                        if (_options.TryGetValue("HEADER_NAME", out var header) && _options.TryGetValue("TOKEN", out var apiToken))
                        {
                            request.Headers.Add(header, apiToken);
                        }
                        break;
                }
            }

            // Custom Headers (Any property starting with HEADER_)
            if (_options != null)
            {
                foreach (var opt in _options.Where(o => o.Key.StartsWith("HEADER_", StringComparison.OrdinalIgnoreCase)))
                {
                    var headerName = opt.Key.Substring(7).Replace("_", "-");
                    request.Headers.Add(headerName, opt.Value);
                }
            }

            // Body for POST
            if (method == HttpMethod.Post && _options != null && _options.TryGetValue("BODY", out var body))
            {
                request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
            }

            return request;
        }
    }
}
