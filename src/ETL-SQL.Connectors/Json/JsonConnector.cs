using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Data;
using ETL_SQL.Common;

namespace ETL_SQL.Connectors.Json
{
    /// <summary>
    /// Connector for JSON data files.
    /// </summary>
    public class JsonConnector : IConnector
    {
        public string Name => "JSON";
        public IReadOnlyList<string> Aliases => Array.Empty<string>();

        public Task<string> GetVersionAsync(IExecutionContext context, string connectionString) => Task.FromResult("JSON FlatFile Engine 1.0");

        public HashSet<string> GetSupportedFunctions() => new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> GetSupportedKeywords() => new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string[]> GetSupportedOptions() => new(StringComparer.OrdinalIgnoreCase)
        {
            { "ROOT_PATH", Array.Empty<string>() },
            { "COMPRESS", new[] { "ON", "OFF" } },
            { "ENCRYPT", new[] { "ON", "OFF" } },
            { "PASSWORD", Array.Empty<string>() }
        };

        public Dictionary<string, string[]> GetOptionValues() => new(StringComparer.OrdinalIgnoreCase);

        public string GetHelp() =>
            "JSON Connector: Connects to JSON files.\n" +
            "Options:\n" +
            "  ROOT_PATH: JSONPath to the data array (e.g. '$.Rows')\n" +
            "  COMPRESS: ON | OFF (Transparent GZip support)\n" +
            "  ENCRYPT: ON | OFF (AES encryption for the file)\n" +
            "  PASSWORD: Password for encryption/decryption";

        public IDataSource CreateDataSource(IExecutionContext context, string connectionString, Dictionary<string, string>? options = null)
        {
            return new JsonDataSource(context, connectionString, options);
        }

        public Task<IEnumerable<string>> GetTablesAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());
        
        public Task<IEnumerable<string>> GetViewsAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());
        
        public Task<IEnumerable<string>> GetColumnsAsync(IExecutionContext context, string connectionString, string tableName) => Task.FromResult(Enumerable.Empty<string>());

        public Task<IEnumerable<string>> GetProceduresAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());

        public string BuildConnectionString(Dictionary<string, string> properties) => 
            ConnectionStringBuilder.Build(Name, properties);
    }
}
