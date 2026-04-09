using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Data;
using ETL_SQL.Common;

namespace ETL_SQL.Connectors.Avro
{
    /// <summary>
    /// Connector for Apache Avro data files.
    /// </summary>
    public class AvroConnector : IConnector
    {
        public string Name => "AVRO";
        public IReadOnlyList<string> Aliases => Array.Empty<string>();

        public Task<string> GetVersionAsync(string connectionString, ILogger? logger = null) => Task.FromResult("Avro Data Connector v1.0 (Apache.Avro)");

        public HashSet<string> GetSupportedFunctions() => new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> GetSupportedKeywords() => new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string[]> GetSupportedOptions() => new(StringComparer.OrdinalIgnoreCase)
        {
            { "SCHEMA_FILE", Array.Empty<string>() }
        };

        public Dictionary<string, string[]> GetOptionValues() => new(StringComparer.OrdinalIgnoreCase);

        public string GetHelp() => 
            "Avro Connector: Connects to Apache Avro files.\n" +
            "Options:\n" +
            "  SCHEMA_FILE: Optional path to an Avro schema (.avsc) file.";

        public IDataSource CreateDataSource(string connectionString, Dictionary<string, string>? options = null, ILogger? logger = null) 
            => new AvroDataSource(connectionString, options, logger);

        public Task<IEnumerable<string>> GetTablesAsync(string connectionString, ILogger? logger = null) => Task.FromResult(Enumerable.Empty<string>());
        
        public Task<IEnumerable<string>> GetViewsAsync(string connectionString, ILogger? logger = null) => Task.FromResult(Enumerable.Empty<string>());
        
        public async Task<IEnumerable<string>> GetColumnsAsync(string connectionString, string tableName, ILogger? logger = null)
        {
            var ds = new AvroDataSource(connectionString, null, logger);
            return await ds.GetColumnsAsync();
        }

        public Task<IEnumerable<string>> GetProceduresAsync(string connectionString, ILogger? logger = null) => Task.FromResult(Enumerable.Empty<string>());

        public string BuildConnectionString(Dictionary<string, string> properties) => 
            ConnectionStringBuilder.Build(Name, properties);
    }
}
