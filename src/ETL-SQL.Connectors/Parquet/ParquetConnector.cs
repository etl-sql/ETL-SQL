using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Data;
using ETL_SQL.Common;

namespace ETL_SQL.Connectors.Parquet
{
    /// <summary>
    /// Connector for Apache Parquet columnar storage files.
    /// Supports high-performance reading and writing of columnar data.
    /// </summary>
    public class ParquetConnector : IConnector
    {
        public string Name => "PARQUET";
        public IReadOnlyList<string> Aliases => Array.Empty<string>();

        public Task<string> GetVersionAsync(string connectionString, ILogger? logger = null) => Task.FromResult("Parquet Connector 1.0 (Parquet.Net)");

        public HashSet<string> GetSupportedFunctions() => new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> GetSupportedKeywords() => new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string[]> GetSupportedOptions() => new(StringComparer.OrdinalIgnoreCase)
        {
            { "COMPRESSION", new[] { "SNAPPY", "GZIP", "LZO", "BROTLI", "LZ4", "ZSTD", "UNCOMPRESSED" } }
        };

        public Dictionary<string, string[]> GetOptionValues() => new(StringComparer.OrdinalIgnoreCase);

        public string GetHelp() =>
            "Parquet Connector: Connects to Apache Parquet files.\n" +
            "Options:\n" +
            "  COMPRESSION: SNAPPY (default) | GZIP | LZO | BROTLI | LZ4 | ZSTD | UNCOMPRESSED";

        public IDataSource CreateDataSource(string connectionString, Dictionary<string, string>? options = null, ILogger? logger = null) 
            => new ParquetDataSource(connectionString, options, logger);

        public Task<IEnumerable<string>> GetTablesAsync(string connectionString, ILogger? logger = null) => Task.FromResult(Enumerable.Empty<string>());
        
        public Task<IEnumerable<string>> GetViewsAsync(string connectionString, ILogger? logger = null) => Task.FromResult(Enumerable.Empty<string>());
        
        public async Task<IEnumerable<string>> GetColumnsAsync(string connectionString, string tableName, ILogger? logger = null)
        {
            var ds = new ParquetDataSource(connectionString, null, logger);
            return await ds.GetColumnsAsync();
        }

        public Task<IEnumerable<string>> GetProceduresAsync(string connectionString, ILogger? logger = null) => Task.FromResult(Enumerable.Empty<string>());

        public string BuildConnectionString(Dictionary<string, string> properties) => 
            ConnectionStringBuilder.Build(Name, properties);
    }
}
