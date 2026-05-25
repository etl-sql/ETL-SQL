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
        public bool IsFileBased => true;


        public Task<string> GetVersionAsync(IExecutionContext context, string connectionString) => Task.FromResult("Avro Data Connector v1.0 (Apache.Avro)");

        public HashSet<string> GetSupportedFunctions() => new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> GetSupportedKeywords() => new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string[]> GetSupportedOptions() => new(StringComparer.OrdinalIgnoreCase)
        {
            { "SCHEMA_FILE", Array.Empty<string>() },
            { "ENCRYPT", new[] { "ON", "OFF" } },
            { "PASSWORD", Array.Empty<string>() }
        };

        public Dictionary<string, string[]> GetOptionValues() => new(StringComparer.OrdinalIgnoreCase);

        public string GetHelp() => 
            "Avro Connector: Connects to Apache Avro files.\n" +
            "Options:\n" +
            "  SCHEMA_FILE: Optional path to an Avro schema (.avsc) file.";

        public IDataSource CreateDataSource(IExecutionContext context, string connectionString, Dictionary<string, string>? options = null) 
            => new AvroDataSource(context, connectionString, options);

        public Task<IEnumerable<string>> GetTablesAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetViewsAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetColumnsAsync(IExecutionContext context, string connectionString, string tableName) => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetProceduresAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());

        public string BuildConnectionString(Dictionary<string, string> properties) => 
            ConnectionStringBuilder.Build(Name, properties);
    }
}
