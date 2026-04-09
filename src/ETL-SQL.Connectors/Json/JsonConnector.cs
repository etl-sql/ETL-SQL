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

        public Task<string> GetVersionAsync(string connectionString, ILogger? logger = null) => Task.FromResult("JSON Connector 1.0");

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

        public IDataSource CreateDataSource(string connectionString, Dictionary<string, string>? options = null, ILogger? logger = null)
        {
            return new JsonDataSource(connectionString, options, logger);
        }

        public Task<IEnumerable<string>> GetTablesAsync(string connectionString, ILogger? logger = null) => Task.FromResult(Enumerable.Empty<string>());
        
        public Task<IEnumerable<string>> GetViewsAsync(string connectionString, ILogger? logger = null) => Task.FromResult(Enumerable.Empty<string>());
        
        public async Task<IEnumerable<string>> GetColumnsAsync(string connectionString, string tableName, ILogger? logger = null)
        {
            var ds = new JsonDataSource(connectionString, null, logger);
            return await ds.GetColumnsAsync();
        }

        public Task<IEnumerable<string>> GetProceduresAsync(string connectionString, ILogger? logger = null) => Task.FromResult(Enumerable.Empty<string>());

        public string BuildConnectionString(Dictionary<string, string> properties) => 
            ConnectionStringBuilder.Build(Name, properties);
    }
}
