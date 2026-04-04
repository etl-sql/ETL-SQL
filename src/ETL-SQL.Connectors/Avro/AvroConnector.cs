using System;
using System.Collections.Generic;
using ETL_SQL.Data;

namespace ETL_SQL.Connectors.Avro
{
    /// <summary>
    /// Connector for Apache Avro data files.
    /// </summary>
    public class AvroConnector : IConnector
    {
        /// <summary>Returns the canonical name of the connector.</summary>
        public string Name => "AVRO";
        
        /// <summary>Returns synonymous names for this connector.</summary>
        public IReadOnlyList<string> Aliases => Array.Empty<string>();

        /// <summary>Retrieves the version information for the connector.</summary>
        public Task<string> GetVersionAsync(string connectionString) => Task.FromResult("Avro Data Connector v1.0 (Apache.Avro)");

        /// <summary>Returns a list of supported SQL functions for this connector.</summary>
        public HashSet<string> GetSupportedFunctions() => new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Returns a list of supported SQL keywords for this connector.</summary>
        public HashSet<string> GetSupportedKeywords() => new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Returns supported connection string options and their available values (if any).</summary>
        public Dictionary<string, string[]> GetSupportedOptions() => new(StringComparer.OrdinalIgnoreCase)
        {
            { "SCHEMA_FILE", Array.Empty<string>() }
        };

        /// <summary>Returns a map of option keys to their current selected values from the UI/prompt.</summary>
        public Dictionary<string, string[]> GetOptionValues() => new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Returns a human-readable help string for the connector.</summary>
        public string GetHelp() => 
            "Avro Connector: Connects to Apache Avro files.\n" +
            "Options:\n" +
            "  SCHEMA_FILE: Optional path to an Avro schema (.avsc) file.";

        /// <summary>Creates a new data source instance for this connector.</summary>
        public IDataSource CreateDataSource(string connectionString, Dictionary<string, string>? options = null) 
            => new AvroDataSource(connectionString);

        /// <summary>Returns a list of logical tables from the connection source.</summary>
        public Task<IEnumerable<string>> GetTablesAsync(string connectionString) => Task.FromResult(Enumerable.Empty<string>());
        
        /// <summary>Returns a list of logical views from the connection source.</summary>
        public Task<IEnumerable<string>> GetViewsAsync(string connectionString) => Task.FromResult(Enumerable.Empty<string>());
        
        /// <summary>Returns a list of columns for the specified table.</summary>
        public async Task<IEnumerable<string>> GetColumnsAsync(string connectionString, string tableName)
        {
            var ds = new AvroDataSource(connectionString);
            return await ds.GetColumnsAsync();
        }

        /// <summary>Returns a list of procedures/functions from the connection source.</summary>
        public Task<IEnumerable<string>> GetProceduresAsync(string connectionString) => Task.FromResult(Enumerable.Empty<string>());
    }
}
