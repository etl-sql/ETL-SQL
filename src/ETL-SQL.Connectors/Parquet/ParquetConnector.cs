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

        public Task<string> GetVersionAsync(IExecutionContext context, string connectionString) => Task.FromResult("Parquet Engine 1.0 (Parquet.Net)");

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

        public IDataSource CreateDataSource(IExecutionContext context, string connectionString, Dictionary<string, string>? options = null) 
            => new ParquetDataSource(context, connectionString, options);

        public Task<IEnumerable<string>> GetTablesAsync(IExecutionContext context, string connectionString) => Task.FromResult<IEnumerable<string>>(new[] { "FILE" });
        public Task<IEnumerable<string>> GetViewsAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetColumnsAsync(IExecutionContext context, string connectionString, string tableName) => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetProceduresAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());

        public string BuildConnectionString(Dictionary<string, string> properties) => 
            ConnectionStringBuilder.Build(Name, properties);
    }
}
