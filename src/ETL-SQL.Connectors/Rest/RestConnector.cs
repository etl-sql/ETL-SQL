using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Data;
using ETL_SQL.Common;

namespace ETL_SQL.Connectors.Rest
{
    /// <summary>
    /// Connector for Generic REST APIs.
    /// Enables connectivity to any HTTPS endpoint with JSON results.
    /// </summary>
    public class RestConnector : IConnector
    {
        public string Name => "API";
        public IReadOnlyList<string> Aliases => new[] { "REST", "HTTP" };

        public Task<string> GetVersionAsync(IExecutionContext context, string connectionString)
        {
            var ds = new RestDataSource(context, connectionString, null);
            return ds.GetVersionAsync();
        }

        public HashSet<string> GetSupportedFunctions() => new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> GetSupportedKeywords() => new(StringComparer.OrdinalIgnoreCase);

        public IDataSource CreateDataSource(IExecutionContext context, string connectionString, Dictionary<string, string>? options = null)
        {
            return new RestDataSource(context, connectionString, options);
        }

        public string BuildConnectionString(Dictionary<string, string> properties)
        {
            return ConnectionStringBuilder.Build("API", properties);
        }

        public Dictionary<string, string[]> GetSupportedOptions() => new(StringComparer.OrdinalIgnoreCase)
        {
            { "URL", Array.Empty<string>() },
            { "METHOD", new[] { "GET", "POST", "PUT", "PATCH", "DELETE" } },
            { "AUTH_TYPE", new[] { "NONE", "BASIC", "BEARER", "APIKEY" } },
            { "USER", Array.Empty<string>() },
            { "PASSWORD", Array.Empty<string>() },
            { "TOKEN", Array.Empty<string>() },
            { "HEADER_NAME", Array.Empty<string>() },
            { "ROOT_PATH", Array.Empty<string>() },
            { "BODY", Array.Empty<string>() },
            { "BODY_CONTENT_TYPE", Array.Empty<string>() },
            { "TIMEOUT_SECONDS", Array.Empty<string>() },
            { "BODY_MODE", new[] { "ROW_OBJECT", "ROW_ARRAY", "WRAPPED_ARRAY", "TEMPLATE" } },
            { "BATCH_SIZE", Array.Empty<string>() },
            { "BATCH_ROOT", Array.Empty<string>() },
            { "RESPONSE_TABLE", Array.Empty<string>() },
            { "RESPONSE_CORRELATION_COLUMNS", Array.Empty<string>() },
            { "SUCCESS_STATUS", Array.Empty<string>() },
            { "ERROR_MODE", new[] { "FAIL_FAST", "CONTINUE" } },
            { "RETRY_COUNT", Array.Empty<string>() },
            { "RETRY_BACKOFF_MS", Array.Empty<string>() },
            { "RETRY_STATUS", Array.Empty<string>() },
            { "IDEMPOTENCY_KEY_COLUMN", Array.Empty<string>() },
            { "IDEMPOTENCY_HEADER", Array.Empty<string>() },
            { "URL_TEMPLATE", Array.Empty<string>() },
            { "BODY_TEMPLATE", Array.Empty<string>() },
            { "ERROR_BODY_MAX_CHARS", Array.Empty<string>() }
        };

        public Dictionary<string, string[]> GetOptionValues() => new();

        public string GetHelp()
        {
            return @"Generic REST API Connector
---------------------------
Connects to any HTTPS endpoint returning JSON data.

Basic Usage:
  CREATE CONNECTION my_api AS API(URL='https://api.example.com/data');

With Authentication:
  CREATE CONNECTION github AS API(URL='https://api.github.com/repos/microsoft/terminal/issues', AUTH_TYPE='BEARER', TOKEN='my_token');

Supported Options:
  URL         - The base endpoint URL.
  METHOD      - HTTP Method (GET, POST, PUT, PATCH, DELETE). Default: GET.
  AUTH_TYPE   - Authentication mode: NONE, BASIC, BEARER, APIKEY.
  USER/PASS   - Credentials for BASIC auth.
  TOKEN       - Secret for BEARER or APIKEY auth.
  HEADER_NAME - Name of the header for APIKEY auth (e.g. X-API-KEY).
  ROOT_PATH         - JSONPath to the data array (e.g. '$.items').
  BODY              - JSON request body for POST/PUT/PATCH request connections.
  BODY_CONTENT_TYPE - Content-Type for POST/PUT/PATCH BODY. Default: application/json.
  TIMEOUT_SECONDS   - Request timeout in seconds (default 30).

Outbound Write Options:
  INSERT writes support POST, PUT, and PATCH. DELETE is only available for direct request execution.
  BODY_MODE                    - Format for writes (ROW_OBJECT, ROW_ARRAY, WRAPPED_ARRAY, TEMPLATE). Default: ROW_OBJECT.
  BATCH_SIZE                   - Number of rows per batch request for array modes.
  BATCH_ROOT                   - Envelope property for WRAPPED_ARRAY.
  RESPONSE_TABLE               - Temp table name (e.g. #bed_api_results) to capture response metadata.
  RESPONSE_CORRELATION_COLUMNS - Source columns to copy into response table.
  SUCCESS_STATUS               - Comma-separated HTTP success statuses. Default: 200,201,202,204.
  ERROR_MODE                   - Error policy: FAIL_FAST or CONTINUE. Default: FAIL_FAST.
  RETRY_COUNT                  - Retry attempts for transient failures. Default: 0.
  RETRY_BACKOFF_MS             - Base backoff time in milliseconds. Default: 500.
  RETRY_STATUS                 - Status codes triggering retries. Default: 408,429,500,502,503,504.
  IDEMPOTENCY_KEY_COLUMN       - Row column to use as idempotency key header.
  IDEMPOTENCY_HEADER           - Header name for idempotency key. Default: Idempotency-Key.
  URL_TEMPLATE                 - Dynamic url string with ${column_name} placeholders.
  BODY_TEMPLATE                - Dynamic body string with ${column_name} placeholders.
  ERROR_BODY_MAX_CHARS         - Max characters of response body retained in error messages. Default: 4096.";
        }

        public Task<IEnumerable<string>> GetTablesAsync(IExecutionContext context, string connectionString) => throw new NotSupportedException("Use IDataSource.GetTablesAsync instead.");
        public Task<IEnumerable<string>> GetViewsAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetColumnsAsync(IExecutionContext context, string connectionString, string tableName) => throw new NotSupportedException("Use IDataSource.GetColumnsAsync instead.");
        public Task<IEnumerable<string>> GetProceduresAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());

        public string? GetHost(string connectionString, Dictionary<string, string>? options = null)
        {
            if (options != null && options.TryGetValue("URL", out var url))
            {
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri)) return uri.Host;
            }
            
            if (Uri.TryCreate(connectionString, UriKind.Absolute, out var connUri)) return connUri.Host;
            
            return null;
        }
    }
}
