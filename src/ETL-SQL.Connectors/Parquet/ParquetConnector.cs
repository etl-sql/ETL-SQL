using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Data;

namespace ETL_SQL.Connectors.Parquet
{
    /// <summary>
    /// Connector for Apache Parquet columnar storage files.
    /// Supports high-performance reading and writing of columnar data.
    /// </summary>
    public class ParquetConnector : IConnector
    {
        /// <summary>Returns the canonical name of the connector.</summary>
        public string Name => "PARQUET";
        
        /// <summary>Returns synonymous names for this connector.</summary>
        public IReadOnlyList<string> Aliases => Array.Empty<string>();

        /// <summary>Retrieves the version information for the Parquet connector.</summary>
        public Task<string> GetVersionAsync(string connectionString) => Task.FromResult("Parquet Connector 1.0 (Parquet.Net)");

        /// <summary>Returns supported SQL functions (none for Parquet).</summary>
        public HashSet<string> GetSupportedFunctions() => new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Returns supported SQL keywords (none for Parquet).</summary>
        public HashSet<string> GetSupportedKeywords() => new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Returns supported options for Parquet (e.g. COMPRESSION).</summary>
        public Dictionary<string, string[]> GetSupportedOptions() => new(StringComparer.OrdinalIgnoreCase)
        {
            { "COMPRESSION", new[] { "SNAPPY", "GZIP", "LZO", "BROTLI", "LZ4", "ZSTD", "UNCOMPRESSED" } }
        };

        /// <summary>Returns current option values.</summary>
        public Dictionary<string, string[]> GetOptionValues() => new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Returns a human-readable help string for the Parquet connector.</summary>
        public string GetHelp() =>
            "Parquet Connector: Connects to Apache Parquet files.\n" +
            "Options:\n" +
            "  COMPRESSION: SNAPPY (default) | GZIP | LZO | BROTLI | LZ4 | ZSTD | UNCOMPRESSED";

        /// <summary>Creates a new Parquet data source instance.</summary>
        public IDataSource CreateDataSource(string connectionString, Dictionary<string, string>? options = null) 
            => new ParquetDataSource(connectionString);

        /// <summary>Returns a list of logical tables (none for Parquet files).</summary>
        public Task<IEnumerable<string>> GetTablesAsync(string connectionString) => Task.FromResult(Enumerable.Empty<string>());
        
        /// <summary>Returns a list of logical views (none for Parquet).</summary>
        public Task<IEnumerable<string>> GetViewsAsync(string connectionString) => Task.FromResult(Enumerable.Empty<string>());
        
        /// <summary>Returns a list of columns for the specified Parquet file.</summary>
        public async Task<IEnumerable<string>> GetColumnsAsync(string connectionString, string tableName)
        {
            var ds = new ParquetDataSource(connectionString);
            return await ds.GetColumnsAsync();
        }

        /// <summary>Returns a list of procedures/functions (none for Parquet).</summary>
        public Task<IEnumerable<string>> GetProceduresAsync(string connectionString) => Task.FromResult(Enumerable.Empty<string>());

        /// <summary>Builds a Parquet file path from named properties.</summary>
        public string BuildConnectionString(Dictionary<string, string> properties) => 
            ConnectionStringBuilder.Build(Name, properties);
    }
}
