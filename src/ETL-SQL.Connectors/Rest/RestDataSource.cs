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
using ETL_SQL.Connectors.Shared;

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
        private static readonly HttpClient _httpClient = new HttpClient();

        public RestDataSource(IExecutionContext context, string url, Dictionary<string, string>? options = null)
        {
            _context = context;
            _url = url;
            _options = options;
            _logger = context.Logger;
            _timeoutSeconds = options != null && options.TryGetValue("TIMEOUT_SECONDS", out var ts) && int.TryParse(ts, out var t) && t > 0 ? t : 30;

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

        public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) =>
            ConnectorExceptionWrapper.WrapAsync(ReadBatchesCore(batchSize), "REST", ShouldWrapProviderException);

        private async IAsyncEnumerable<DataTable> ReadBatchesCore(int batchSize)
        {
            var request = BuildRequest();
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(_timeoutSeconds));
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            
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
                        row,
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
                    null,
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
            Row? singleRow,
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
            int attempts = 0;
            HttpResponseMessage? response = null;
            string? errorMessage = null;
            string? responseBodyText = null;
            int? statusCode = null;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            int currentRequestIndex = stats.RequestIndex++;
            ValidateRequestUrl(url);

            while (true)
            {
                using var request = new HttpRequestMessage(new HttpMethod(method), url);
                if (content != null)
                {
                    request.Content = new StringContent(content, System.Text.Encoding.UTF8, GetBodyContentType());
                }

                ApplyHeaders(request, singleRow, idempotencyKeyCol, idempotencyHeader, columnNames);

                try
                {
                    using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(_timeoutSeconds));
                    response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cts.Token);
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
                        await Task.Delay(retryBackoffMs * (int)Math.Pow(2, attempts - 1));
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
                catch (Exception ex)
                {
                    if (attempts < retryCount)
                    {
                        attempts++;
                        await Task.Delay(retryBackoffMs * (int)Math.Pow(2, attempts - 1));
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

            _context?.SecurityService.ValidateHost(uri.Host);
        }

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

        private void ApplyHeaders(HttpRequestMessage request, Row? row, string? idempotencyKeyCol, string idempotencyHeader, List<string>? columnNames)
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
            if (string.IsNullOrEmpty(value) || _options == null)
            {
                return value ?? string.Empty;
            }

            var sanitized = value;
            foreach (var opt in _options)
            {
                if (!IsSensitiveHeader(opt.Key) || string.IsNullOrEmpty(opt.Value))
                {
                    continue;
                }

                sanitized = sanitized.Replace(opt.Value, "***REDACTED***", StringComparison.Ordinal);
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

                var request = BuildRequest();
                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                if (!response.IsSuccessStatusCode) return Enumerable.Empty<string>();

                using var stream = await response.Content.ReadAsStreamAsync();
            
                string? rootPath = null;
                _options?.TryGetValue("ROOT_PATH", out rootPath);

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
                    if (headerName.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    request.Headers.Add(headerName, opt.Value);
                }
            }

            // Body for request methods that support JSON payloads.
            if ((method == HttpMethod.Post || method == HttpMethod.Put) &&
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
