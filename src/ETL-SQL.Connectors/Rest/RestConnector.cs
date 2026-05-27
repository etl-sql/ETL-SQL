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
            { "METHOD", new[] { "GET", "POST", "PUT", "DELETE" } },
            { "AUTH_TYPE", new[] { "NONE", "BASIC", "BEARER", "APIKEY" } },
            { "USER", Array.Empty<string>() },
            { "PASSWORD", Array.Empty<string>() },
            { "TOKEN", Array.Empty<string>() },
            { "HEADER_NAME", Array.Empty<string>() },
            { "ROOT_PATH", Array.Empty<string>() },
            { "BODY", Array.Empty<string>() },
            { "BODY_CONTENT_TYPE", Array.Empty<string>() },
            { "TIMEOUT_SECONDS", Array.Empty<string>() }
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
  METHOD      - HTTP Method (GET, POST, PUT, DELETE). Default: GET.
  AUTH_TYPE   - Authentication mode: NONE, BASIC, BEARER, APIKEY.
  USER/PASS   - Credentials for BASIC auth.
  TOKEN       - Secret for BEARER or APIKEY auth.
  HEADER_NAME - Name of the header for APIKEY auth (e.g. X-API-KEY).
  ROOT_PATH         - JSONPath to the data array (e.g. '$.items').
  BODY              - JSON request body for POST/PUT methods.
  BODY_CONTENT_TYPE - Content-Type for POST/PUT BODY. Default: application/json.
  TIMEOUT_SECONDS   - Request timeout in seconds (default 30).";
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
