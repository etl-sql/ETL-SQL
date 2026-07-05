using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Shared;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using ETL_SQL.Services;

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
        private readonly int _timeoutSeconds;
        // Auto-redirect is disabled so the connector follows redirects explicitly and re-validates every
        // target host against the egress allowlist (SSRF hardening). See SendWithRedirectsAsync.
        private static readonly HttpClient _httpClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        private const int DefaultMaxRedirects = 5;

        private string? _cachedToken;
        private DateTime? _tokenExpiry;
        private readonly System.Threading.SemaphoreSlim _tokenSemaphore = new(1, 1);

        public RestDataSource(IExecutionContext context, string url, Dictionary<string, string>? options = null)
        {
            _context = context;
            _url = url;
            _options = options;
            _logger = context.Logger;
            _timeoutSeconds = options != null && options.TryGetValue("TIMEOUT_SECONDS", out var ts) && int.TryParse(ts, out var t) && t > 0 ? t : 30;

            // Security Hardening: egress control (local guardrail + enterprise host/scheme/port/range policy)
            ETL_SQL.Core.Governance.ConnectorPolicyAuthorizer.EnforceEnterpriseUrl(context, new Uri(url));

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

        public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) =>
            ConnectorExceptionWrapper.WrapAsync(ReadBatchesCore(batchSize), "REST", ShouldWrapProviderException);

        private static string UpdateQueryParameter(string url, string key, string value)
        {
            var uri = new Uri(url);
            var query = uri.Query;
            var path = url;
            int queryIdx = url.IndexOf('?');
            if (queryIdx >= 0)
            {
                path = url.Substring(0, queryIdx);
            }

            var queryParams = new List<KeyValuePair<string, string>>();
            if (!string.IsNullOrEmpty(query))
            {
                var parts = query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    var eqIdx = part.IndexOf('=');
                    if (eqIdx >= 0)
                    {
                        var k = Uri.UnescapeDataString(part.Substring(0, eqIdx));
                        var v = Uri.UnescapeDataString(part.Substring(eqIdx + 1));
                        queryParams.Add(new KeyValuePair<string, string>(k, v));
                    }
                    else
                    {
                        var k = Uri.UnescapeDataString(part);
                        queryParams.Add(new KeyValuePair<string, string>(k, string.Empty));
                    }
                }
            }

            queryParams.RemoveAll(kvp => kvp.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            queryParams.Add(new KeyValuePair<string, string>(key, value));

            var newQuery = string.Join("&", queryParams.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));
            return path + "?" + newQuery;
        }

        private static string? ParseLinkHeaderNextUrl(HttpResponseMessage response)
        {
            if (response.Headers.TryGetValues("Link", out var linkValues))
            {
                foreach (var linkVal in linkValues)
                {
                    var parts = linkVal.Split(',');
                    foreach (var part in parts)
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(part, @"<([^>]+)>;\s*rel=""next""", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (match.Success)
                        {
                            return match.Groups[1].Value;
                        }
                    }
                }
            }
            return null;
        }

        private static JsonElement? ResolveJsonElement(JsonElement root, string? path)
        {
            if (string.IsNullOrEmpty(path) || path == "$")
            {
                return root;
            }

            var cleanPath = path;
            if (cleanPath.StartsWith("$."))
            {
                cleanPath = cleanPath.Substring(2);
            }
            else if (cleanPath.StartsWith("$"))
            {
                cleanPath = cleanPath.Substring(1);
            }

            var parts = cleanPath.Split('.', StringSplitOptions.RemoveEmptyEntries);
            var current = root;
            foreach (var part in parts)
            {
                if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(part, out var next))
                {
                    current = next;
                }
                else if (current.ValueKind == JsonValueKind.Array && int.TryParse(part, out var idx) && idx >= 0 && idx < current.GetArrayLength())
                {
                    current = current[idx];
                }
                else
                {
                    return null;
                }
            }
            return current;
        }

        private static object? GetJsonValueForElement(JsonElement element) => element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetDecimal(out var d) ? d : (object?)element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.GetRawText()
        };

        private int CalculateRetryDelay(HttpResponseMessage? response, int baseBackoffMs, int attempt)
        {
            int maxRetryAfterSeconds = 60;
            if (_options != null && _options.TryGetValue("MAX_RETRY_AFTER_SECONDS", out var mrasStr) && int.TryParse(mrasStr, out var mras) && mras >= 0)
            {
                maxRetryAfterSeconds = mras;
            }

            int delayMs = baseBackoffMs * (int)Math.Pow(2, attempt - 1);

            if (response != null && response.Headers.TryGetValues("Retry-After", out var values))
            {
                var val = values.FirstOrDefault();
                if (!string.IsNullOrEmpty(val))
                {
                    if (int.TryParse(val, out var seconds))
                    {
                        if (seconds >= 0)
                        {
                            int sleepMs = Math.Min(seconds, maxRetryAfterSeconds) * 1000;
                            return sleepMs;
                        }
                    }
                    else if (DateTime.TryParse(val, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date))
                    {
                        var sleepSpan = date.ToUniversalTime() - DateTime.UtcNow;
                        if (sleepSpan.TotalMilliseconds > 0)
                        {
                            int sleepMs = (int)Math.Min(sleepSpan.TotalMilliseconds, maxRetryAfterSeconds * 1000);
                            return sleepMs;
                        }
                    }
                }
            }

            return delayMs;
        }

        private async Task<string> GetOAuthTokenAsync()
        {
            if (_cachedToken != null && _tokenExpiry.HasValue && _tokenExpiry.Value > DateTime.UtcNow.AddSeconds(5))
            {
                return _cachedToken;
            }

            await _tokenSemaphore.WaitAsync();
            try
            {
                if (_cachedToken != null && _tokenExpiry.HasValue && _tokenExpiry.Value > DateTime.UtcNow.AddSeconds(5))
                {
                    return _cachedToken;
                }

                if (_options == null ||
                    !_options.TryGetValue("TOKEN_URL", out var tokenUrl) ||
                    !_options.TryGetValue("CLIENT_ID", out var clientId) ||
                    !_options.TryGetValue("CLIENT_SECRET", out var clientSecret))
                {
                    throw new ExecutionException("OAuth2 Client Credentials requires TOKEN_URL, CLIENT_ID, and CLIENT_SECRET options.");
                }

                if (!Uri.TryCreate(tokenUrl, UriKind.Absolute, out var uri))
                {
                    throw new ExecutionException("OAuth2 TOKEN_URL is not a valid absolute URI.");
                }
                if (_context != null) ETL_SQL.Core.Governance.ConnectorPolicyAuthorizer.EnforceEnterpriseUrl(_context, uri);

                _options.TryGetValue("SCOPE", out var scope);

                var postParams = new List<KeyValuePair<string, string>>
                {
                    new("grant_type", "client_credentials"),
                    new("client_id", clientId),
                    new("client_secret", clientSecret)
                };
                if (!string.IsNullOrEmpty(scope))
                {
                    postParams.Add(new("scope", scope));
                }

                using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl)
                {
                    Content = new FormUrlEncodedContent(postParams)
                };

                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(_timeoutSeconds));
                using var response = await SendWithRedirectsAsync(request, HttpCompletionOption.ResponseContentRead, cts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    errorContent = SanitizeForDiagnostics(errorContent);
                    throw new ExecutionException($"OAuth2 token request failed with status {response.StatusCode}: {errorContent}");
                }

                var responseBody = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseBody);
                var root = doc.RootElement;

                if (!root.TryGetProperty("access_token", out var tokenProp))
                {
                    throw new ExecutionException("OAuth2 token response did not contain 'access_token'.");
                }

                var accessToken = tokenProp.GetString();
                if (string.IsNullOrEmpty(accessToken))
                {
                    throw new ExecutionException("OAuth2 token response contained an empty 'access_token'.");
                }

                int expiresIn = 3600;
                if (root.TryGetProperty("expires_in", out var expiresProp) && expiresProp.TryGetInt32(out var exp))
                {
                    expiresIn = exp;
                }

                if (_options.TryGetValue("TOKEN_CACHE_SECONDS", out var tcsStr) && int.TryParse(tcsStr, out var tcs) && tcs > 0)
                {
                    expiresIn = tcs;
                }

                _cachedToken = accessToken;
                _tokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn);

                return accessToken;
            }
            catch (Exception ex) when (ex is not ExecutionException)
            {
                throw new ExecutionException($"OAuth2 token acquisition failed: {SanitizeForDiagnostics(ex.Message)}", ex);
            }
            finally
            {
                _tokenSemaphore.Release();
            }
        }

        private async IAsyncEnumerable<DataTable> ReadBatchesCore(int batchSize)
        {
            string paginationMode = "NONE";
            if (_options != null && _options.TryGetValue("PAGINATION_MODE", out var pm))
            {
                paginationMode = pm.ToUpperInvariant();
            }
            if (paginationMode != "NONE" &&
                paginationMode != "PAGE" &&
                paginationMode != "OFFSET" &&
                paginationMode != "CURSOR" &&
                paginationMode != "LINK_HEADER")
            {
                throw new ExecutionException($"Unsupported PAGINATION_MODE: '{paginationMode}'. Supported values are NONE, PAGE, OFFSET, CURSOR, and LINK_HEADER.");
            }

            int maxPages = 1000;
            if (_options != null && _options.TryGetValue("MAX_PAGES", out var mpStr) && int.TryParse(mpStr, out var mp) && mp > 0)
            {
                maxPages = mp;
            }

            string pageParam = "page";
            if (_options != null && _options.TryGetValue("PAGE_PARAM", out var pp) && !string.IsNullOrWhiteSpace(pp))
            {
                pageParam = pp;
            }

            int pageStart = 1;
            if (_options != null && _options.TryGetValue("PAGE_START", out var psStr) && int.TryParse(psStr, out var ps))
            {
                pageStart = ps;
            }

            string offsetParam = "offset";
            if (_options != null && _options.TryGetValue("OFFSET_PARAM", out var op) && !string.IsNullOrWhiteSpace(op))
            {
                offsetParam = op;
            }

            string limitParam = "limit";
            if (_options != null && _options.TryGetValue("LIMIT_PARAM", out var lp) && !string.IsNullOrWhiteSpace(lp))
            {
                limitParam = lp;
            }

            int? pageSize = null;
            if (_options != null && _options.TryGetValue("PAGE_SIZE", out var pSizeStr) && int.TryParse(pSizeStr, out var pSize) && pSize > 0)
            {
                pageSize = pSize;
            }

            string? cursorParam = null;
            _options?.TryGetValue("CURSOR_PARAM", out cursorParam);

            string? cursorPath = null;
            _options?.TryGetValue("CURSOR_PATH", out cursorPath);

            string? nextUrlPath = null;
            _options?.TryGetValue("NEXT_URL_PATH", out nextUrlPath);
            if (paginationMode == "CURSOR" && string.IsNullOrWhiteSpace(cursorParam) && string.IsNullOrWhiteSpace(nextUrlPath))
            {
                throw new ExecutionException("CURSOR pagination requires CURSOR_PARAM or NEXT_URL_PATH.");
            }

            string? rootPath = null;
            if (_options != null && _options.TryGetValue("ROOT_PATH", out rootPath))
            {
                if (rootPath.StartsWith("$.")) rootPath = rootPath.Substring(2);
                else if (rootPath.StartsWith("$") && rootPath.Length > 1) rootPath = rootPath.Substring(1);
            }

            int retryCount = 0;
            if (_options != null && _options.TryGetValue("RETRY_COUNT", out var rcStr) && int.TryParse(rcStr, out var rc) && rc >= 0)
            {
                retryCount = rc;
            }

            int retryBackoffMs = 500;
            if (_options != null && _options.TryGetValue("RETRY_BACKOFF_MS", out var rbStr) && int.TryParse(rbStr, out var rb) && rb >= 0)
            {
                retryBackoffMs = rb;
            }

            var retryStatuses = new HashSet<int> { 408, 429, 500, 502, 503, 504 };
            if (_options != null && _options.TryGetValue("RETRY_STATUS", out var rsStr) && !string.IsNullOrWhiteSpace(rsStr))
            {
                retryStatuses = new HashSet<int>(rsStr.Split(',').Select(s => int.TryParse(s.Trim(), out var code) ? code : -1).Where(code => code != -1));
            }

            if (paginationMode == "NONE")
            {
                HttpResponseMessage? response = null;
                int attempts = 0;
                while (true)
                {
                    var request = await BuildRequestAsync();
                    try
                    {
                        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(_timeoutSeconds));
                        response = await SendWithRedirectsAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

                        if (response.IsSuccessStatusCode)
                        {
                            break;
                        }

                        int statusCode = (int)response.StatusCode;
                        if (attempts < retryCount && retryStatuses.Contains(statusCode))
                        {
                            attempts++;
                            int delayMs = CalculateRetryDelay(response, retryBackoffMs, attempts);
                            await Task.Delay(delayMs);
                            continue;
                        }

                        var error = SanitizeForDiagnostics(await response.Content.ReadAsStringAsync());
                        throw new HttpRequestException($"API request failed with status {response.StatusCode}: {error}");
                    }
                    // A blocked redirect target (SecurityException) or redirect loop (ExecutionException)
                    // must fail fast and keep its message — never get swallowed into the retry/HTTP wrapper.
                    catch (Exception ex) when (ex is not HttpRequestException && ex is not SecurityException && ex is not ExecutionException)
                    {
                        if (attempts < retryCount)
                        {
                            attempts++;
                            int delayMs = CalculateRetryDelay(null, retryBackoffMs, attempts);
                            await Task.Delay(delayMs);
                            continue;
                        }
                        throw new HttpRequestException($"API request failed with exception: {SanitizeForDiagnostics(ex.Message)}", ex);
                    }
                }

                using var stream = await response.Content.ReadAsStreamAsync();
                await foreach (var batch in JsonExtractor.ExtractBatchesAsync(stream, rootPath, batchSize))
                {
                    yield return batch;
                }
                yield break;
            }

            int pageCount = 0;
            string currentUrl = _url;
            int currentPage = pageStart;
            int currentOffset = 0;
            string? currentCursor = null;
            bool hasMore = true;

            while (hasMore && pageCount < maxPages)
            {
                string requestUrl = currentUrl;
                if (paginationMode == "PAGE")
                {
                    requestUrl = UpdateQueryParameter(requestUrl, pageParam, currentPage.ToString());
                    if (pageSize.HasValue)
                    {
                        requestUrl = UpdateQueryParameter(requestUrl, limitParam, pageSize.Value.ToString());
                    }
                }
                else if (paginationMode == "OFFSET")
                {
                    requestUrl = UpdateQueryParameter(requestUrl, offsetParam, currentOffset.ToString());
                    if (pageSize.HasValue)
                    {
                        requestUrl = UpdateQueryParameter(requestUrl, limitParam, pageSize.Value.ToString());
                    }
                }
                else if (paginationMode == "CURSOR")
                {
                    if (currentCursor != null && !string.IsNullOrWhiteSpace(cursorParam))
                    {
                        requestUrl = UpdateQueryParameter(requestUrl, cursorParam, currentCursor);
                    }
                    if (pageSize.HasValue)
                    {
                        requestUrl = UpdateQueryParameter(requestUrl, limitParam, pageSize.Value.ToString());
                    }
                }

                HttpResponseMessage? response = null;
                int attempts = 0;
                string responseBodyText;

                while (true)
                {
                    var request = await BuildRequestAsync(requestUrl);
                    try
                    {
                        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(_timeoutSeconds));
                        response = await SendWithRedirectsAsync(request, HttpCompletionOption.ResponseContentRead, cts.Token);

                        int statusCode = (int)response.StatusCode;
                        if (response.IsSuccessStatusCode)
                        {
                            responseBodyText = await response.Content.ReadAsStringAsync();
                            break;
                        }

                        if (attempts < retryCount && retryStatuses.Contains(statusCode))
                        {
                            attempts++;
                            int delayMs = CalculateRetryDelay(response, retryBackoffMs, attempts);
                            await Task.Delay(delayMs);
                            continue;
                        }

                        var error = SanitizeForDiagnostics(await response.Content.ReadAsStringAsync());
                        throw new HttpRequestException($"API request failed with status {response.StatusCode}: {error}");
                    }
                    catch (Exception ex) when (ex is not HttpRequestException && ex is not SecurityException && ex is not ExecutionException)
                    {
                        if (attempts < retryCount)
                        {
                            attempts++;
                            int delayMs = CalculateRetryDelay(null, retryBackoffMs, attempts);
                            await Task.Delay(delayMs);
                            continue;
                        }
                        throw new HttpRequestException($"API request failed with exception: {SanitizeForDiagnostics(ex.Message)}", ex);
                    }
                }

                int rowsInPage = 0;
                using (var doc = JsonDocument.Parse(responseBodyText))
                {
                    await foreach (var batch in JsonExtractor.ExtractBatches(doc, rootPath, batchSize))
                    {
                        rowsInPage += batch.Rows.Count;
                        yield return batch;
                    }

                    if (rowsInPage == 0)
                    {
                        hasMore = false;
                        break;
                    }

                    if (paginationMode == "CURSOR" && !string.IsNullOrEmpty(cursorPath))
                    {
                        var cursorElem = ResolveJsonElement(doc.RootElement, cursorPath);
                        if (cursorElem.HasValue && cursorElem.Value.ValueKind != JsonValueKind.Null && cursorElem.Value.ValueKind != JsonValueKind.Undefined)
                        {
                            string? nextCursor = cursorElem.Value.ValueKind == JsonValueKind.String ? cursorElem.Value.GetString() : cursorElem.Value.GetRawText();
                            if (!string.IsNullOrWhiteSpace(nextCursor) && nextCursor != currentCursor)
                            {
                                currentCursor = nextCursor;
                            }
                            else
                            {
                                hasMore = false;
                            }
                        }
                        else
                        {
                            hasMore = false;
                        }
                    }
                    else if (!string.IsNullOrEmpty(nextUrlPath))
                    {
                        var nextUrlElem = ResolveJsonElement(doc.RootElement, nextUrlPath);
                        if (nextUrlElem.HasValue && nextUrlElem.Value.ValueKind == JsonValueKind.String)
                        {
                            var nextUrl = nextUrlElem.Value.GetString();
                            if (!string.IsNullOrEmpty(nextUrl))
                            {
                                if (Uri.TryCreate(nextUrl, UriKind.Absolute, out var nextUri))
                                {
                                    currentUrl = nextUrl;
                                }
                                else if (Uri.TryCreate(new Uri(currentUrl), nextUrl, out var absoluteNextUri))
                                {
                                    currentUrl = absoluteNextUri.AbsoluteUri;
                                }
                                else
                                {
                                    throw new ExecutionException($"Invalid next URL returned by API: '{nextUrl}'");
                                }
                                ValidateRequestUrl(currentUrl);
                            }
                            else
                            {
                                hasMore = false;
                            }
                        }
                        else
                        {
                            hasMore = false;
                        }
                    }
                }

                if (paginationMode == "PAGE")
                {
                    currentPage++;
                }
                else if (paginationMode == "OFFSET")
                {
                    int increment = pageSize ?? rowsInPage;
                    currentOffset += increment;
                }
                else if (paginationMode == "LINK_HEADER")
                {
                    var nextUrl = ParseLinkHeaderNextUrl(response);
                    if (!string.IsNullOrEmpty(nextUrl))
                    {
                        if (Uri.TryCreate(nextUrl, UriKind.Absolute, out var nextUri))
                        {
                            currentUrl = nextUrl;
                        }
                        else if (Uri.TryCreate(new Uri(currentUrl), nextUrl, out var absoluteNextUri))
                        {
                            currentUrl = absoluteNextUri.AbsoluteUri;
                        }
                        else
                        {
                            throw new ExecutionException($"Invalid next URL returned by API: '{nextUrl}'");
                        }
                        ValidateRequestUrl(currentUrl);
                    }
                    else
                    {
                        hasMore = false;
                    }
                }

                pageCount++;
            }
        }

        private class WriteStats
        {
            public int Successes { get; set; }
            public int Failures { get; set; }
            public int RequestIndex { get; set; }
        }

        public async Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false)
        {
            // Parse options
            string? bodyModeStr = null;
            _options?.TryGetValue("BODY_MODE", out bodyModeStr);
            string bodyMode = bodyModeStr?.ToUpperInvariant() ?? "ROW_OBJECT";
            if (bodyMode != "ROW_OBJECT" && bodyMode != "ROW_ARRAY" && bodyMode != "WRAPPED_ARRAY" && bodyMode != "TEMPLATE")
            {
                throw new ExecutionException($"Unsupported BODY_MODE: '{bodyMode}'.");
            }

            int batchSizeVal = 1;
            if (bodyMode == "ROW_ARRAY" || bodyMode == "WRAPPED_ARRAY")
            {
                batchSizeVal = 500;
            }
            if (_options != null && _options.TryGetValue("BATCH_SIZE", out var bsStr) && int.TryParse(bsStr, out var bs) && bs > 0)
            {
                batchSizeVal = bs;
            }
            else if (_options != null && _options.TryGetValue("BATCH_SIZE", out bsStr))
            {
                throw new ExecutionException("BATCH_SIZE must be a positive integer.");
            }

            string? batchRoot = null;
            _options?.TryGetValue("BATCH_ROOT", out batchRoot);
            if (bodyMode == "WRAPPED_ARRAY" && string.IsNullOrEmpty(batchRoot))
            {
                throw new ExecutionException("BATCH_ROOT option is required when BODY_MODE is 'WRAPPED_ARRAY'.");
            }

            string? responseTable = null;
            _options?.TryGetValue("RESPONSE_TABLE", out responseTable);

            var correlationCols = Array.Empty<string>();
            if (_options != null && _options.TryGetValue("RESPONSE_CORRELATION_COLUMNS", out var ccStr) && !string.IsNullOrWhiteSpace(ccStr))
            {
                correlationCols = ccStr.Split(',').Select(c => c.Trim()).ToArray();
            }

            var successStatuses = new HashSet<int> { 200, 201, 202, 204 };
            if (_options != null && _options.TryGetValue("SUCCESS_STATUS", out var ssStr) && !string.IsNullOrWhiteSpace(ssStr))
            {
                successStatuses = new HashSet<int>(ssStr.Split(',').Select(s => int.TryParse(s.Trim(), out var code) ? code : -1).Where(code => code != -1));
            }

            string? errorModeStr = null;
            _options?.TryGetValue("ERROR_MODE", out errorModeStr);
            string errorMode = errorModeStr?.ToUpperInvariant() ?? "FAIL_FAST";
            if (errorMode != "FAIL_FAST" && errorMode != "CONTINUE")
            {
                throw new ExecutionException($"Unsupported ERROR_MODE: '{errorMode}'. Supported values are FAIL_FAST and CONTINUE.");
            }

            int retryCount = 0;
            if (_options != null && _options.TryGetValue("RETRY_COUNT", out var rcStr) && int.TryParse(rcStr, out var rc) && rc >= 0)
            {
                retryCount = rc;
            }

            int retryBackoffMs = 500;
            if (_options != null && _options.TryGetValue("RETRY_BACKOFF_MS", out var rbStr) && int.TryParse(rbStr, out var rb) && rb >= 0)
            {
                retryBackoffMs = rb;
            }

            var retryStatuses = new HashSet<int> { 408, 429, 500, 502, 503, 504 };
            if (_options != null && _options.TryGetValue("RETRY_STATUS", out var rsStr) && !string.IsNullOrWhiteSpace(rsStr))
            {
                retryStatuses = new HashSet<int>(rsStr.Split(',').Select(s => int.TryParse(s.Trim(), out var code) ? code : -1).Where(code => code != -1));
            }

            string? idempotencyKeyCol = null;
            _options?.TryGetValue("IDEMPOTENCY_KEY_COLUMN", out idempotencyKeyCol);
            if (!string.IsNullOrWhiteSpace(idempotencyKeyCol) && bodyMode != "ROW_OBJECT" && bodyMode != "TEMPLATE")
            {
                throw new ExecutionException("IDEMPOTENCY_KEY_COLUMN is only supported with ROW_OBJECT or TEMPLATE body modes.");
            }

            string idempotencyHeader = "Idempotency-Key";
            if (_options != null && _options.TryGetValue("IDEMPOTENCY_HEADER", out var ih) && !string.IsNullOrWhiteSpace(ih))
            {
                idempotencyHeader = ih;
            }

            string? urlTemplate = null;
            _options?.TryGetValue("URL_TEMPLATE", out urlTemplate);

            string? bodyTemplate = null;
            _options?.TryGetValue("BODY_TEMPLATE", out bodyTemplate);
            if (bodyMode == "TEMPLATE" && string.IsNullOrEmpty(bodyTemplate))
            {
                throw new ExecutionException("BODY_TEMPLATE is required when BODY_MODE is 'TEMPLATE'.");
            }

            int errorBodyMaxChars = 4096;
            if (_options != null && _options.TryGetValue("ERROR_BODY_MAX_CHARS", out var ebmcStr) && int.TryParse(ebmcStr, out var ebmc) && ebmc > 0)
            {
                errorBodyMaxChars = ebmc;
            }

            string? methodStr = "POST";
            if (_options == null || !_options.TryGetValue("METHOD", out methodStr) || string.IsNullOrEmpty(methodStr))
            {
                methodStr = "POST";
            }
            methodStr = methodStr.ToUpperInvariant();
            if (methodStr != "POST" && methodStr != "PUT" && methodStr != "PATCH")
            {
                throw new ExecutionException($"HTTP method '{methodStr}' is not supported for writing. Supported methods are POST, PUT, and PATCH.");
            }

            if (_options != null && _options.ContainsKey("BODY") && bodyMode != "TEMPLATE")
            {
                throw new ExecutionException("Connection-level static 'BODY' cannot be used with INSERT statement unless BODY_MODE is 'TEMPLATE'.");
            }

            // Respect WHAT_IF behavior
            if (_context != null && _context.IsWhatIf)
            {
                var uri = new Uri(_url);
                _logger.WriteLine("WHAT IF: REST Outbound Write Ingestion Summary", ConsoleColor.Yellow);
                _logger.WriteLine($"  Method: {methodStr}", ConsoleColor.Yellow);
                _logger.WriteLine($"  Host: {uri.Host}", ConsoleColor.Yellow);
                _logger.WriteLine($"  Path: {uri.AbsolutePath}", ConsoleColor.Yellow);
                _logger.WriteLine($"  Body Mode: {bodyMode}", ConsoleColor.Yellow);
                if (bodyMode == "WRAPPED_ARRAY")
                {
                    _logger.WriteLine($"  Batch Root: {batchRoot}", ConsoleColor.Yellow);
                }
                _logger.WriteLine($"  Batch Size: {batchSizeVal}", ConsoleColor.Yellow);
                _logger.WriteLine($"  Error Mode: {errorMode}", ConsoleColor.Yellow);

                long totalRows = 0;
                await foreach (var batch in batches)
                {
                    totalRows += batch.Rows.Count;
                    if (idempotencyKeyCol != null && !batch.Schema.Contains(idempotencyKeyCol))
                    {
                        throw new ExecutionException($"Idempotency key column '{idempotencyKeyCol}' not found in the source rows.");
                    }
                    foreach (var col in correlationCols)
                    {
                        if (!batch.Schema.Contains(col))
                        {
                            throw new ExecutionException($"Correlation column '{col}' not found in the source rows.");
                        }
                    }
                }

                long expectedRequests = 0;
                if (bodyMode == "ROW_OBJECT" || bodyMode == "TEMPLATE")
                {
                    expectedRequests = totalRows;
                }
                else
                {
                    expectedRequests = (totalRows + batchSizeVal - 1) / batchSizeVal;
                }
                _logger.WriteLine($"  Source Row Count: {totalRows}", ConsoleColor.Yellow);
                _logger.WriteLine($"  Expected HTTP Request Count: {expectedRequests}", ConsoleColor.Yellow);

                if (_options != null)
                {
                    var redactedHeaders = new List<string>();
                    foreach (var opt in _options.Where(o => o.Key.StartsWith("HEADER_", StringComparison.OrdinalIgnoreCase)))
                    {
                        var hName = opt.Key.Substring(7).Replace("_", "-");
                        var hVal = IsSensitiveHeader(hName) ? "***REDACTED***" : opt.Value;
                        redactedHeaders.Add($"{hName}: {hVal}");
                    }
                    if (_options.TryGetValue("AUTH_TYPE", out var authType) && authType.ToUpperInvariant() != "NONE")
                    {
                        redactedHeaders.Add($"Authorization: ***REDACTED*** ({authType})");
                    }
                    if (redactedHeaders.Count > 0)
                    {
                        _logger.WriteLine($"  Headers: {string.Join(", ", redactedHeaders)}", ConsoleColor.Yellow);
                    }
                }
                return;
            }

            // Real execution
            InMemoryDataSource? respTableDs = null;
            var rowBuffer = new List<Row>();
            var columnNames = new List<string>();

            var stats = new WriteStats { RequestIndex = 0, Successes = 0, Failures = 0 };
            int batchIndex = 0;
            int nextSourceRowIndex = 0;
            int rowBufferStartIndex = 0;

            await foreach (var batch in batches)
            {
                if (columnNames.Count == 0)
                {
                    columnNames.AddRange(batch.ColumnNames);
                    ValidateWriteColumns(columnNames, correlationCols, idempotencyKeyCol);

                    if (!string.IsNullOrEmpty(responseTable))
                    {
                        respTableDs = GetOrCreateResponseTable(responseTable, batch, correlationCols);
                    }
                }
                else
                {
                    ValidateWriteColumns(batch.ColumnNames.ToList(), correlationCols, idempotencyKeyCol);
                }

                foreach (var row in batch.Rows)
                {
                    if (rowBuffer.Count == 0)
                    {
                        rowBufferStartIndex = nextSourceRowIndex;
                    }

                    rowBuffer.Add(row);
                    nextSourceRowIndex++;
                    if (rowBuffer.Count >= batchSizeVal)
                    {
                        await ProcessChunkAsync(rowBuffer, columnNames, batchIndex, rowBufferStartIndex, stats, successStatuses, errorMode, methodStr, retryCount, retryBackoffMs, retryStatuses, idempotencyKeyCol, idempotencyHeader, urlTemplate, bodyTemplate, bodyMode, batchRoot, errorBodyMaxChars, respTableDs, correlationCols);
                        rowBuffer.Clear();
                    }
                }
                batchIndex++;
            }

            if (rowBuffer.Count > 0)
            {
                await ProcessChunkAsync(rowBuffer, columnNames, batchIndex, rowBufferStartIndex, stats, successStatuses, errorMode, methodStr, retryCount, retryBackoffMs, retryStatuses, idempotencyKeyCol, idempotencyHeader, urlTemplate, bodyTemplate, bodyMode, batchRoot, errorBodyMaxChars, respTableDs, correlationCols);
            }
        }

        private async Task ProcessChunkAsync(
            List<Row> rows,
            List<string> columnNames,
            int batchIndex,
            int sourceRowStartIndex,
            WriteStats stats,
            HashSet<int> successStatuses,
            string errorMode,
            string method,
            int retryCount,
            int retryBackoffMs,
            HashSet<int> retryStatuses,
            string? idempotencyKeyCol,
            string idempotencyHeader,
            string? urlTemplate,
            string? bodyTemplate,
            string bodyMode,
            string? batchRoot,
            int errorBodyMaxChars,
            InMemoryDataSource? respTableDs,
            string[] correlationCols)
        {
            if (bodyMode == "ROW_OBJECT" || bodyMode == "TEMPLATE")
            {
                int rowIdx = 0;
                foreach (var row in rows)
                {
                    string url = _url;
                    if (!string.IsNullOrEmpty(urlTemplate))
                    {
                        url = ProcessUrlTemplate(urlTemplate, row, columnNames);
                    }
                    else if (_url.Contains("${"))
                    {
                        url = ProcessUrlTemplate(_url, row, columnNames);
                    }

                    string? bodyText = null;
                    if (bodyMode == "ROW_OBJECT")
                    {
                        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                        foreach (var col in columnNames)
                        {
                            var val = row[col];
                            if (val == DBNull.Value) val = null;
                            dict[col] = val;
                        }
                        bodyText = JsonSerializer.Serialize(dict);
                    }
                    else if (bodyMode == "TEMPLATE")
                    {
                        bodyText = ProcessTemplate(bodyTemplate!, row, columnNames);
                    }

                    await SendHttpRequestAsync(
                        url,
                        method,
                        bodyText,
                        new List<Row> { row },
                        sourceRowStartIndex + rowIdx,
                        batchIndex,
                        stats,
                        1,
                        successStatuses,
                        errorMode,
                        retryCount,
                        retryBackoffMs,
                        retryStatuses,
                        idempotencyKeyCol,
                        idempotencyHeader,
                        columnNames,
                        errorBodyMaxChars,
                        respTableDs,
                        correlationCols);

                    rowIdx++;
                }
            }
            else
            {
                string url = _url;
                if (!string.IsNullOrEmpty(urlTemplate))
                {
                    throw new ExecutionException("URL_TEMPLATE is only supported in ROW_OBJECT or TEMPLATE body modes.");
                }

                var list = new List<Dictionary<string, object?>>();
                foreach (var r in rows)
                {
                    var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    foreach (var col in columnNames)
                    {
                        var val = r[col];
                        if (val == DBNull.Value) val = null;
                        dict[col] = val;
                    }
                    list.Add(dict);
                }

                string bodyText;
                if (bodyMode == "WRAPPED_ARRAY")
                {
                    bodyText = JsonSerializer.Serialize(new Dictionary<string, object?> { { batchRoot!, list } });
                }
                else
                {
                    bodyText = JsonSerializer.Serialize(list);
                }

                await SendHttpRequestAsync(
                    url,
                    method,
                    bodyText,
                    rows,
                    null,
                    batchIndex,
                    stats,
                    rows.Count,
                    successStatuses,
                    errorMode,
                    retryCount,
                    retryBackoffMs,
                    retryStatuses,
                    null,
                    idempotencyHeader,
                    columnNames,
                    errorBodyMaxChars,
                    respTableDs,
                    correlationCols);
            }
        }

        private async Task SendHttpRequestAsync(
            string url,
            string method,
            string? content,
            List<Row>? rows,
            int? sourceRowIndex,
            int batchIndex,
            WriteStats stats,
            int rowCount,
            HashSet<int> successStatuses,
            string errorMode,
            int retryCount,
            int retryBackoffMs,
            HashSet<int> retryStatuses,
            string? idempotencyKeyCol,
            string idempotencyHeader,
            List<string> columnNames,
            int errorBodyMaxChars,
            InMemoryDataSource? respTableDs,
            string[] correlationCols)
        {
            bool validateJsonBody = true;
            if (_options != null && _options.TryGetValue("VALIDATE_JSON_BODY", out var vjbStr) && bool.TryParse(vjbStr, out var vjb))
            {
                validateJsonBody = vjb;
            }

            var contentType = GetBodyContentType().Trim().ToLowerInvariant();
            bool isJsonContentType = contentType.StartsWith("application/json") || contentType.EndsWith("+json");
            if (content != null && isJsonContentType && validateJsonBody)
            {
                try
                {
                    using var tempDoc = JsonDocument.Parse(content);
                }
                catch (JsonException ex)
                {
                    throw new ExecutionException($"Generated JSON body is invalid: {ex.Message}");
                }
            }

            int attempts = 0;
            HttpResponseMessage? response = null;
            string? errorMessage = null;
            string? responseBodyText = null;
            int? statusCode = null;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            int currentRequestIndex = stats.RequestIndex++;
            ValidateRequestUrl(url);

            var singleRow = rows != null && rows.Count == 1 ? rows[0] : null;

            while (true)
            {
                using var request = new HttpRequestMessage(new HttpMethod(method), url);
                if (content != null)
                {
                    request.Content = new StringContent(content, System.Text.Encoding.UTF8, GetBodyContentType());
                }

                await ApplyHeadersAsync(request, singleRow, idempotencyKeyCol, idempotencyHeader, columnNames);

                try
                {
                    using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(_timeoutSeconds));
                    response = await SendWithRedirectsAsync(request, HttpCompletionOption.ResponseContentRead, cts.Token);
                    statusCode = (int)response.StatusCode;

                    if (successStatuses.Contains(statusCode.Value))
                    {
                        responseBodyText = await response.Content.ReadAsStringAsync();
                        errorMessage = null;
                        break;
                    }

                    if (attempts < retryCount && retryStatuses.Contains(statusCode.Value))
                    {
                        attempts++;
                        int delayMs = CalculateRetryDelay(response, retryBackoffMs, attempts);
                        await Task.Delay(delayMs);
                        continue;
                    }

                    var errorContent = SanitizeForDiagnostics(await response.Content.ReadAsStringAsync());
                    responseBodyText = errorContent;
                    if (errorContent.Length > errorBodyMaxChars)
                    {
                        errorContent = errorContent.Substring(0, errorBodyMaxChars);
                    }
                    errorMessage = $"API request failed with status {response.StatusCode}: {errorContent}";
                    break;
                }
                catch (Exception ex) when (ex is SecurityException or ExecutionException)
                {
                    // Blocked redirect target or redirect loop: surface immediately with its own
                    // message instead of retrying or masking it as a generic request failure.
                    throw;
                }
                catch (Exception ex)
                {
                    if (attempts < retryCount)
                    {
                        attempts++;
                        int delayMs = CalculateRetryDelay(null, retryBackoffMs, attempts);
                        await Task.Delay(delayMs);
                        continue;
                    }

                    errorMessage = $"API request failed with exception: {SanitizeForDiagnostics(ex.Message)}";
                    break;
                }
            }
            stopwatch.Stop();

            bool success = errorMessage == null;
            if (success)
            {
                stats.Successes++;
            }
            else
            {
                stats.Failures++;
            }

            if (respTableDs != null)
            {
                var responseBatch = new DataTable();
                responseBatch.SetColumns(respTableDs.Schema.Keys);

                string? responseItemPath = null;
                _options?.TryGetValue("RESPONSE_ITEM_PATH", out responseItemPath);

                bool processedItems = false;
                if (!string.IsNullOrEmpty(responseItemPath) && !string.IsNullOrEmpty(responseBodyText))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(responseBodyText);
                        var itemsElement = ResolveJsonElement(doc.RootElement, responseItemPath);
                        if (itemsElement.HasValue && itemsElement.Value.ValueKind == JsonValueKind.Array)
                        {
                            var array = itemsElement.Value;
                            int itemIdx = 0;
                            foreach (var item in array.EnumerateArray())
                            {
                                Row? correspondingSourceRow = (rows != null && itemIdx >= 0 && itemIdx < rows.Count) ? rows[itemIdx] : null;

                                var respRow = new Row(responseBatch.Schema);
                                respRow["request_index"] = currentRequestIndex;
                                respRow["batch_index"] = batchIndex;
                                respRow["source_row_index"] = sourceRowIndex.HasValue ? sourceRowIndex.Value + itemIdx : (int?)null;

                                bool itemSuccess = success;
                                if (item.ValueKind == JsonValueKind.Object)
                                {
                                    if (item.TryGetProperty("success", out var succProp) && succProp.ValueKind == JsonValueKind.False)
                                    {
                                        itemSuccess = false;
                                    }
                                    if (item.TryGetProperty("status", out var statusProp) && statusProp.ValueKind == JsonValueKind.False)
                                    {
                                        itemSuccess = false;
                                    }
                                }
                                respRow["success"] = itemSuccess;
                                respRow["status_code"] = statusCode;
                                respRow["method"] = method;
                                respRow["url"] = RedactUrl(url);
                                respRow["retry_count"] = attempts;
                                respRow["duration_ms"] = (int)stopwatch.ElapsedMilliseconds;
                                respRow["row_count"] = 1;
                                respRow["response_body"] = item.GetRawText();

                                string? itemError = errorMessage;
                                if (!itemSuccess && item.ValueKind == JsonValueKind.Object)
                                {
                                    if (item.TryGetProperty("error", out var errProp))
                                    {
                                        itemError = errProp.ValueKind == JsonValueKind.String ? errProp.GetString() : errProp.GetRawText();
                                    }
                                    else if (item.TryGetProperty("message", out var msgProp))
                                    {
                                        itemError = msgProp.ValueKind == JsonValueKind.String ? msgProp.GetString() : msgProp.GetRawText();
                                    }
                                }
                                respRow["error_message"] = itemError;

                                foreach (var col in correlationCols)
                                {
                                    if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty(col, out var itemProp))
                                    {
                                        respRow[col] = GetJsonValueForElement(itemProp);
                                    }
                                    else if (correspondingSourceRow != null)
                                    {
                                        respRow[col] = correspondingSourceRow[col];
                                    }
                                    else
                                    {
                                        respRow[col] = null;
                                    }
                                }

                                await responseBatch.AddRowAsync(respRow);
                                itemIdx++;
                            }
                            processedItems = true;
                        }
                    }
                    catch (JsonException)
                    {
                        // Non-JSON response body: keep the original one-row response capture.
                    }
                }

                if (!processedItems)
                {
                    var respRow = new Row(responseBatch.Schema);
                    respRow["request_index"] = currentRequestIndex;
                    respRow["batch_index"] = batchIndex;
                    respRow["source_row_index"] = sourceRowIndex;
                    respRow["success"] = success;
                    respRow["status_code"] = statusCode;
                    respRow["method"] = method;
                    respRow["url"] = RedactUrl(url);
                    respRow["retry_count"] = attempts;
                    respRow["duration_ms"] = (int)stopwatch.ElapsedMilliseconds;
                    respRow["row_count"] = rowCount;
                    respRow["response_body"] = responseBodyText;
                    respRow["error_message"] = errorMessage;

                    foreach (var col in correlationCols)
                    {
                        if (singleRow != null)
                        {
                            respRow[col] = singleRow[col];
                        }
                        else
                        {
                            respRow[col] = null;
                        }
                    }

                    await responseBatch.AddRowAsync(respRow);
                }

                await respTableDs.WriteBatches(ToAsyncEnumerable(responseBatch), append: true);
            }

            if (!success && errorMode == "FAIL_FAST")
            {
                throw new ExecutionException(errorMessage ?? "API request failed.");
            }
        }

        private static void ValidateWriteColumns(List<string> columnNames, string[] correlationCols, string? idempotencyKeyCol)
        {
            foreach (var col in correlationCols)
            {
                if (!columnNames.Any(c => c.Equals(col, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new ExecutionException($"Correlation column '{col}' not found in the source rows.");
                }
            }

            if (!string.IsNullOrWhiteSpace(idempotencyKeyCol) &&
                !columnNames.Any(c => c.Equals(idempotencyKeyCol, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ExecutionException($"Idempotency key column '{idempotencyKeyCol}' not found in the source rows.");
            }
        }

        private void ValidateRequestUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                throw new ExecutionException("Generated API request URL is not a valid absolute URI.");
            }

            if (_context != null) ETL_SQL.Core.Governance.ConnectorPolicyAuthorizer.EnforceEnterpriseUrl(_context, uri);
        }

        private int GetMaxRedirects()
        {
            if (_options != null && _options.TryGetValue("MAX_REDIRECTS", out var mrStr) && int.TryParse(mrStr, out var mr) && mr >= 0)
            {
                return mr;
            }
            return DefaultMaxRedirects;
        }

        /// <summary>
        /// Sends <paramref name="request"/> with automatic redirects disabled, then follows any
        /// redirect responses manually up to a bounded count. Every redirect target is re-validated
        /// against the egress allowlist (<see cref="SecurityService.ValidateHost"/>) so an allowed
        /// endpoint cannot bounce the request to a blocked internal host. Authorization and other
        /// sensitive headers are dropped on cross-host redirects so credentials never leak to a
        /// different origin.
        /// </summary>
        private async Task<HttpResponseMessage> SendWithRedirectsAsync(
            HttpRequestMessage request, HttpCompletionOption completion, CancellationToken ct)
        {
            int maxRedirects = GetMaxRedirects();

            // Buffer the body once so it can be replayed for 307/308 redirects (the original
            // HttpContent cannot be re-sent after the first SendAsync).
            byte[]? bodyBytes = null;
            string? contentType = null;
            if (request.Content != null)
            {
                bodyBytes = await request.Content.ReadAsByteArrayAsync();
                contentType = request.Content.Headers.ContentType?.ToString();
            }

            var current = request;
            int redirects = 0;

            while (true)
            {
                var response = await _httpClient.SendAsync(current, completion, ct);
                if (!IsRedirectStatus(response.StatusCode))
                {
                    return response;
                }

                if (redirects >= maxRedirects)
                {
                    response.Dispose();
                    if (current != request) current.Dispose();
                    throw new ExecutionException(
                        $"REST request exceeded the maximum of {maxRedirects} redirect(s) (possible redirect loop).");
                }

                var location = response.Headers.Location;
                if (location == null)
                {
                    // Redirect status without a Location header — nothing to follow; hand it back.
                    return response;
                }

                var target = location.IsAbsoluteUri ? location : new Uri(current.RequestUri!, location);
                if (target.Scheme != Uri.UriSchemeHttp && target.Scheme != Uri.UriSchemeHttps)
                {
                    response.Dispose();
                    if (current != request) current.Dispose();
                    throw new ExecutionException($"REST redirect to unsupported scheme '{target.Scheme}' was blocked.");
                }

                // Re-validate every hop against the egress policy before following it.
                if (_context != null) ETL_SQL.Core.Governance.ConnectorPolicyAuthorizer.EnforceEnterpriseUrl(_context, target);

                // Strip credentials when the redirect crosses to a different host OR downgrades the
                // transport (HTTPS -> HTTP). A same-host downgrade would otherwise leak the bearer
                // token / cookies over cleartext.
                bool stripCredentials = ShouldStripCredentialsOnRedirect(current.RequestUri!, target);
                var next = CloneForRedirect(current, target, response.StatusCode, bodyBytes, contentType, stripCredentials);

                response.Dispose();
                if (current != request) current.Dispose();
                current = next;
                redirects++;
            }
        }

        private static bool IsRedirectStatus(System.Net.HttpStatusCode status) =>
            status is System.Net.HttpStatusCode.MovedPermanently      // 301
                   or System.Net.HttpStatusCode.Found                 // 302
                   or System.Net.HttpStatusCode.SeeOther              // 303
                   or System.Net.HttpStatusCode.TemporaryRedirect     // 307
                   or System.Net.HttpStatusCode.PermanentRedirect;    // 308

        private static HttpRequestMessage CloneForRedirect(
            HttpRequestMessage original, Uri target, System.Net.HttpStatusCode status,
            byte[]? bodyBytes, string? contentType, bool stripSensitiveHeaders)
        {
            // 307/308 preserve the method and body; 301/302/303 downgrade non-idempotent verbs to GET
            // (the long-standing browser/curl convention) and drop the body.
            bool preserveMethodAndBody =
                status is System.Net.HttpStatusCode.TemporaryRedirect or System.Net.HttpStatusCode.PermanentRedirect;

            var method = preserveMethodAndBody
                ? original.Method
                : (original.Method == HttpMethod.Get || original.Method == HttpMethod.Head ? original.Method : HttpMethod.Get);

            var clone = new HttpRequestMessage(method, target);

            bool methodHasBody = method == HttpMethod.Post || method == HttpMethod.Put
                || method.Method.Equals("PATCH", StringComparison.OrdinalIgnoreCase);
            if (preserveMethodAndBody && bodyBytes != null && methodHasBody)
            {
                var content = new ByteArrayContent(bodyBytes);
                if (!string.IsNullOrEmpty(contentType))
                {
                    content.Headers.TryAddWithoutValidation("Content-Type", contentType);
                }
                clone.Content = content;
            }

            foreach (var header in original.Headers)
            {
                if (stripSensitiveHeaders && IsSensitiveRequestHeader(header.Key))
                {
                    continue;
                }
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return clone;
        }

        /// <summary>
        /// Decides whether credential-bearing headers must be dropped when following a redirect from
        /// <paramref name="from"/> to <paramref name="to"/>. True when the host changes (cross-origin)
        /// or the transport is downgraded HTTPS -> HTTP (which would expose the credential over
        /// cleartext even on the same host).
        /// </summary>
        internal static bool ShouldStripCredentialsOnRedirect(Uri from, Uri to)
        {
            bool crossHost = !string.Equals(to.Host, from.Host, StringComparison.OrdinalIgnoreCase);
            bool schemeDowngrade =
                string.Equals(from.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(to.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
            return crossHost || schemeDowngrade;
        }

        // Headers that must never be forwarded to a different origin on a cross-host redirect.
        private static bool IsSensitiveRequestHeader(string headerName) =>
            headerName.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
            || headerName.Equals("Cookie", StringComparison.OrdinalIgnoreCase)
            || headerName.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase)
            || headerName.Contains("KEY", StringComparison.OrdinalIgnoreCase)
            || headerName.Contains("TOKEN", StringComparison.OrdinalIgnoreCase)
            || headerName.Contains("SECRET", StringComparison.OrdinalIgnoreCase);

        private InMemoryDataSource GetOrCreateResponseTable(string name, DataTable firstBatch, string[] correlationCols)
        {
            if (_context == null) throw new ExecutionException("IExecutionContext is not available.");

            if (_context.Connections.TryGetValue(name, out var existingDs))
            {
                if (existingDs is not InMemoryDataSource memDs)
                {
                    throw new ExecutionException($"Connection/table '{name}' exists but is not an in-memory temp table.");
                }

                var expectedCols = new[] { "request_index", "batch_index", "source_row_index", "success", "status_code", "method", "url", "retry_count", "duration_ms", "row_count", "response_body", "error_message" };
                foreach (var col in expectedCols)
                {
                    if (!memDs.Schema.ContainsKey(col))
                    {
                        throw new ExecutionException($"Existing response table '{name}' is missing required column '{col}'.");
                    }
                }
                foreach (var col in correlationCols)
                {
                    if (!memDs.Schema.ContainsKey(col))
                    {
                        throw new ExecutionException($"Existing response table '{name}' is missing correlation column '{col}'.");
                    }
                }
                return memDs;
            }
            else
            {
                var memDs = new InMemoryDataSource();
                memDs.ExecutionContext = _context;

                var colDefs = new List<ETL_SQL.Core.ColumnDefinition>
                {
                    new ETL_SQL.Core.ColumnDefinition("request_index", "INT", false) { IsNullable = false },
                    new ETL_SQL.Core.ColumnDefinition("batch_index", "INT", false) { IsNullable = false },
                    new ETL_SQL.Core.ColumnDefinition("source_row_index", "INT", false) { IsNullable = true },
                    new ETL_SQL.Core.ColumnDefinition("success", "BOOL", false) { IsNullable = false },
                    new ETL_SQL.Core.ColumnDefinition("status_code", "INT", false) { IsNullable = true },
                    new ETL_SQL.Core.ColumnDefinition("method", "STRING", false) { IsNullable = false },
                    new ETL_SQL.Core.ColumnDefinition("url", "STRING", false) { IsNullable = false },
                    new ETL_SQL.Core.ColumnDefinition("retry_count", "INT", false) { IsNullable = false },
                    new ETL_SQL.Core.ColumnDefinition("duration_ms", "INT", false) { IsNullable = false },
                    new ETL_SQL.Core.ColumnDefinition("row_count", "INT", false) { IsNullable = false },
                    new ETL_SQL.Core.ColumnDefinition("response_body", "STRING", false) { IsNullable = true },
                    new ETL_SQL.Core.ColumnDefinition("error_message", "STRING", false) { IsNullable = true }
                };

                foreach (var col in correlationCols)
                {
                    string dataType = "STRING";
                    if (firstBatch.Rows.Count > 0)
                    {
                        var val = firstBatch.Rows[0][col];
                        if (val != null && val != DBNull.Value)
                        {
                            dataType = val switch
                            {
                                int or long or short or byte => "INT",
                                decimal or double or float => "DECIMAL",
                                bool => "BOOL",
                                DateTime => "DATETIME",
                                _ => "STRING"
                            };
                        }
                    }
                    colDefs.Add(new ETL_SQL.Core.ColumnDefinition(col, dataType, false) { IsNullable = true });
                }

                memDs.SetSchema(colDefs);
                _context.Connections[name] = memDs;
                return memDs;
            }
        }

        private async Task ApplyHeadersAsync(HttpRequestMessage request, Row? row, string? idempotencyKeyCol, string idempotencyHeader, List<string>? columnNames)
        {
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
                            if (!request.Headers.Contains(header))
                            {
                                request.Headers.Add(header, apiToken);
                            }
                        }
                        break;
                    case "OAUTH2_CLIENT_CREDENTIALS":
                        var oauthToken = await GetOAuthTokenAsync();
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", oauthToken);
                        break;
                }
            }

            if (_options != null)
            {
                foreach (var opt in _options.Where(o => o.Key.StartsWith("HEADER_", StringComparison.OrdinalIgnoreCase)))
                {
                    var headerName = opt.Key.Substring(7).Replace("_", "-");
                    if (headerName.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!request.Headers.Contains(headerName))
                    {
                        request.Headers.Add(headerName, opt.Value);
                    }
                }
            }

            if (row != null && !string.IsNullOrEmpty(idempotencyKeyCol) && columnNames != null)
            {
                var colIndex = columnNames.FindIndex(c => c.Equals(idempotencyKeyCol, StringComparison.OrdinalIgnoreCase));
                if (colIndex < 0)
                {
                    throw new ExecutionException($"Idempotency key column '{idempotencyKeyCol}' not found in the source rows.");
                }
                var actualColName = columnNames[colIndex];
                var val = row[actualColName];
                if (val != null && val != DBNull.Value)
                {
                    if (!request.Headers.Contains(idempotencyHeader))
                    {
                        request.Headers.Add(idempotencyHeader, val.ToString());
                    }
                }
            }
        }

        private string ProcessUrlTemplate(string urlTemplate, Row row, List<string> columns)
        {
            return System.Text.RegularExpressions.Regex.Replace(urlTemplate, @"\${([^}]+)}", match =>
            {
                var columnName = match.Groups[1].Value;
                var colIndex = columns.FindIndex(c => c.Equals(columnName, StringComparison.OrdinalIgnoreCase));
                if (colIndex < 0)
                {
                    throw new ExecutionException($"Column '{columnName}' referenced in URL template was not found in the source rows.");
                }

                var actualColName = columns[colIndex];
                var val = row[actualColName];
                if (val == null || val == DBNull.Value)
                {
                    return "";
                }

                return Uri.EscapeDataString(val.ToString() ?? "");
            });
        }

        private string ProcessTemplate(string template, Row row, List<string> columns)
        {
            return System.Text.RegularExpressions.Regex.Replace(template, @"\${([^}]+)}", match =>
            {
                var columnName = match.Groups[1].Value;
                var colIndex = columns.FindIndex(c => c.Equals(columnName, StringComparison.OrdinalIgnoreCase));
                if (colIndex < 0)
                {
                    throw new ExecutionException($"Column '{columnName}' referenced in template was not found in the source rows.");
                }

                var actualColName = columns[colIndex];
                var val = row[actualColName];
                if (val == null || val == DBNull.Value)
                {
                    return "null";
                }

                var str = val.ToString() ?? "";
                if (val is bool b)
                {
                    return b ? "true" : "false";
                }
                if (val is string || val is char)
                {
                    str = str.Replace("\\", "\\\\").Replace("\"", "\\\"");
                }

                return str;
            });
        }

        private string SanitizeForDiagnostics(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value ?? string.Empty;
            }

            var sanitized = value;
            if (_options != null)
            {
                foreach (var opt in _options)
                {
                    if (!IsSensitiveHeader(opt.Key) || string.IsNullOrEmpty(opt.Value))
                    {
                        continue;
                    }

                    sanitized = sanitized.Replace(opt.Value, "***REDACTED***", StringComparison.Ordinal);
                }
            }

            if (!string.IsNullOrEmpty(_cachedToken))
            {
                sanitized = sanitized.Replace(_cachedToken, "***REDACTED***", StringComparison.Ordinal);
            }

            return sanitized;
        }

        private static bool IsSensitiveHeader(string headerName)
        {
            return headerName.Equals("TOKEN", StringComparison.OrdinalIgnoreCase) ||
                   headerName.Equals("PASSWORD", StringComparison.OrdinalIgnoreCase) ||
                   headerName.Equals("CLIENT_SECRET", StringComparison.OrdinalIgnoreCase) ||
                   headerName.Equals("AUTHORIZATION", StringComparison.OrdinalIgnoreCase) ||
                   headerName.Contains("KEY", StringComparison.OrdinalIgnoreCase) ||
                   headerName.Contains("TOKEN", StringComparison.OrdinalIgnoreCase) ||
                   headerName.Contains("SECRET", StringComparison.OrdinalIgnoreCase) ||
                   headerName.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase);
        }

        private static string RedactUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;
            int queryIdx = url.IndexOf('?');
            if (queryIdx < 0) return url;

            var baseUrl = url.Substring(0, queryIdx);
            var query = url.Substring(queryIdx + 1);
            var parts = query.Split('&');
            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                int eqIdx = part.IndexOf('=');
                if (eqIdx > 0)
                {
                    var key = part.Substring(0, eqIdx);
                    if (key.Contains("key", StringComparison.OrdinalIgnoreCase) ||
                        key.Contains("token", StringComparison.OrdinalIgnoreCase) ||
                        key.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
                        key.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                        key.Contains("pass", StringComparison.OrdinalIgnoreCase))
                    {
                        parts[i] = key + "=***REDACTED***";
                    }
                }
            }
            return baseUrl + "?" + string.Join("&", parts);
        }

        private static async IAsyncEnumerable<DataTable> ToAsyncEnumerable(DataTable table)
        {
            yield return table;
            await Task.CompletedTask;
        }

        public async Task<IEnumerable<string>> GetColumnsAsync()
        {
            try
            {
                if (!IsSafeSchemaProbeMethod())
                {
                    return Enumerable.Empty<string>();
                }

                var request = await BuildRequestAsync();
                var response = await SendWithRedirectsAsync(request, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None);
                if (!response.IsSuccessStatusCode) return Enumerable.Empty<string>();

                using var stream = await response.Content.ReadAsStreamAsync();

                string? rootPath = null;
                if (_options != null && _options.TryGetValue("ROOT_PATH", out rootPath))
                {
                    if (rootPath.StartsWith("$.")) rootPath = rootPath.Substring(2);
                    else if (rootPath.StartsWith("$") && rootPath.Length > 1) rootPath = rootPath.Substring(1);
                }

                return await JsonExtractor.GetColumnsAsync(stream, rootPath);
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("REST", ex);
            }
        }

        private bool IsSafeSchemaProbeMethod()
        {
            string? methodStr = "GET";
            _options?.TryGetValue("METHOD", out methodStr);
            return string.Equals(methodStr ?? "GET", "GET", StringComparison.OrdinalIgnoreCase);
        }

        public Task<IEnumerable<string>> GetTablesAsync() => Task.FromResult<IEnumerable<string>>(new[] { "ENDPOINT" });
        public Task<IEnumerable<string>> GetViewsAsync() => Task.FromResult<IEnumerable<string>>(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetColumnsAsync(string tableName) => GetColumnsAsync();

        public Task TruncateAsync() => throw new NotSupportedException();
        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }
        public async Task<string> GetVersionAsync() => await Task.FromResult("REST API Connector 1.0");
        public HashSet<string> GetSupportedFunctions() => new(StringComparer.OrdinalIgnoreCase);

        public IAsyncEnumerable<DataTable> ExecuteRawSql(string sql, IEnumerable<object?>? parameters = null) =>
            ConnectorExceptionWrapper.WrapAsync(ExecuteRawSqlCore(sql, parameters), "REST", ShouldWrapProviderException);

        private async IAsyncEnumerable<DataTable> ExecuteRawSqlCore(string sql, IEnumerable<object?>? parameters = null)
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

        private async Task<HttpRequestMessage> BuildRequestAsync(string? url = null)
        {
            string? methodStr = "GET";
            _options?.TryGetValue("METHOD", out methodStr);
            var method = new HttpMethod(methodStr ?? "GET");

            var targetUrl = url ?? _url;
            var request = new HttpRequestMessage(method, targetUrl);

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
                    case "OAUTH2_CLIENT_CREDENTIALS":
                        var oauthToken = await GetOAuthTokenAsync();
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", oauthToken);
                        break;
                }
            }

            if (_options != null)
            {
                foreach (var opt in _options.Where(o => o.Key.StartsWith("HEADER_", StringComparison.OrdinalIgnoreCase)))
                {
                    var headerName = opt.Key.Substring(7).Replace("_", "-");
                    if (headerName.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    request.Headers.Add(headerName, opt.Value);
                }
            }

            if ((method == HttpMethod.Post || method == HttpMethod.Put || method.Method.Equals("PATCH", StringComparison.OrdinalIgnoreCase)) &&
                _options != null &&
                _options.TryGetValue("BODY", out var body))
            {
                request.Content = new StringContent(body, System.Text.Encoding.UTF8, GetBodyContentType());
            }

            return request;
        }

        private string GetBodyContentType()
        {
            if (_options == null)
            {
                return "application/json";
            }

            if (_options.TryGetValue("BODY_CONTENT_TYPE", out var bodyContentType) &&
                !string.IsNullOrWhiteSpace(bodyContentType))
            {
                return bodyContentType;
            }

            if (_options.TryGetValue("HEADER_Content-Type", out var headerContentType) &&
                !string.IsNullOrWhiteSpace(headerContentType))
            {
                return headerContentType;
            }

            return "application/json";
        }

        private static bool ShouldWrapProviderException(Exception ex) =>
            ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException;
    }
}
