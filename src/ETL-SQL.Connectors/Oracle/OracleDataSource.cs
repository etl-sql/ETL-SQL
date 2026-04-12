using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Oracle.ManagedDataAccess.Client;
using ETL_SQL.Data;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Shared;

namespace ETL_SQL.Connectors.Oracle
{
    /// <summary>
    /// Data source implementation for Oracle Database.
    /// Supports batch reading, bulk insertion, and raw SQL execution.
    /// </summary>
    public class OracleDataSource : IDatabaseSource
    {
        private readonly string _connectionString;
        private readonly string? _tableName;
        private readonly Dictionary<string, string>? _options;
        private readonly ILogger _logger;

        public string ConnectionString => _connectionString;
        public string Path => "ORACLE";
        public string Dialect => "ORACLE";
        public bool SupportsSqlPushdown => true;
        public Dictionary<string, string>? Options => _options;
        public string ConnectorType => "ORACLE";

        public OracleDataSource(string connectionString, string? tableName = null, Dictionary<string, string>? options = null, ILogger? logger = null)
        {
            _connectionString = connectionString;
            _tableName = tableName;
            _options = options;
            _logger = logger ?? NullLogger.Instance;
        }

        public IDataSource WithTable(string tableName) => new OracleDataSource(_connectionString, tableName, _options, _logger);

        private async Task<OracleConnection> OpenConnectionAsync()
        {
            var conn = new OracleConnection(_connectionString);
            await ConnectorRetryPolicy.ForOracle(_logger)
                .ExecuteAsync(async ct => await conn.OpenAsync(ct));
            return conn;
        }

        public async Task<string> GetVersionAsync()
        {
            using var conn = await OpenConnectionAsync();
            using var cmd = new OracleCommand("SELECT version FROM v$instance", conn);
            var result = await cmd.ExecuteScalarAsync();
            return result?.ToString() ?? "Unknown Oracle Version";
        }

        public HashSet<string> GetSupportedFunctions() => OracleSyntax.Functions;

        public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000)
        {
            if (string.IsNullOrEmpty(_tableName))
                throw new ExecutionException("No table specified for Oracle data source read.");

            using var conn = await OpenConnectionAsync();
            using var cmd = new OracleCommand($"SELECT * FROM {_tableName}", conn);
            using var reader = await cmd.ExecuteReaderAsync();

            var columns = new List<string>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                columns.Add(reader.GetName(i));
            }

            var currentBatch = new DataTable();
            foreach (var col in columns) currentBatch.ColumnNames.Add(col);

            while (await reader.ReadAsync())
            {
                var row = new Row();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    row[columns[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }
                await currentBatch.AddRowAsync(row);

                if (currentBatch.Rows.Count >= batchSize)
                {
                    yield return currentBatch;
                    currentBatch = new DataTable();
                    foreach (var col in columns) currentBatch.ColumnNames.Add(col);
                }
            }

            if (currentBatch.Rows.Count > 0)
            {
                yield return currentBatch;
            }
        }

        public async Task WriteBatches(IAsyncEnumerable<DataTable> batches)
        {
            if (string.IsNullOrEmpty(_tableName))
                throw new ExecutionException("No table specified for Oracle data source write.");

            using var conn = await OpenConnectionAsync();

            using var bulkCopy = new OracleBulkCopy(conn);
            bulkCopy.DestinationTableName = _tableName;

            var isFirstBatch = true;
            System.Data.DataTable? dt = null;

            await foreach (var batch in batches)
            {
                if (batch.Rows.Count == 0) continue;

                if (isFirstBatch)
                {
                    dt = new System.Data.DataTable();
                    foreach (var col in batch.ColumnNames)
                    {
                        dt.Columns.Add(col);
                        bulkCopy.ColumnMappings.Add(col, col);
                    }
                    isFirstBatch = false;
                }
                
                dt!.Clear();
                foreach (var row in batch.Rows)
                {
                    var dataRow = dt.NewRow();
                    foreach (var col in batch.ColumnNames)
                    {
                        dataRow[col] = row.Columns.TryGetValue(col, out var val) && val != null ? val : DBNull.Value;
                    }
                    dt.Rows.Add(dataRow);
                }

                bulkCopy.WriteToServer(dt);
            }
        }

        public async IAsyncEnumerable<DataTable> ExecuteRawSql(string sql, IEnumerable<object?>? parameters = null)
        {
            using var conn = await OpenConnectionAsync();
            
            // Wire up InfoMessage if needed, Oracle doesn't have InfoMessage like SQL Server,
            // but we can log execution start/finish if we want.
            
            using var cmd = new OracleCommand(sql, conn);
            
            int paramCount = 0;
            if (parameters != null)
            {
                foreach (var param in parameters)
                {
                    cmd.Parameters.Add(new OracleParameter($"p{paramCount++}", param ?? DBNull.Value));
                }
            }
            
            if (paramCount > 0)
            {
                cmd.CommandText = ETL_SQL.Core.Common.ParameterUtility.ProcessParameters(cmd.CommandText, ":");
            }

            using var reader = await cmd.ExecuteReaderAsync();

            var columns = new List<string>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                columns.Add(reader.GetName(i));
            }

            var resultBatch = new DataTable();
            foreach (var col in columns) resultBatch.ColumnNames.Add(col);

            while (await reader.ReadAsync())
            {
                var row = new Row();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    row[columns[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }
                await resultBatch.AddRowAsync(row);
            }
            yield return resultBatch;
        }

        public Task<IEnumerable<string>> GetColumnsAsync()
        {
            if (string.IsNullOrEmpty(_tableName)) return Task.FromResult(Enumerable.Empty<string>());
            return GetColumnsAsync(_tableName);
        }

        public async Task<IEnumerable<string>> GetTablesAsync()
        {
            using var conn = await OpenConnectionAsync();
            using var cmd = new OracleCommand("SELECT owner, table_name FROM all_tables WHERE owner NOT IN ('SYS')", conn);
            using var reader = await cmd.ExecuteReaderAsync();
            var tables = new List<string>();
            while (await reader.ReadAsync())
            {
                var owner = reader.GetString(0);
                var table = reader.GetString(1);
                tables.Add($"{owner}.{table}");
            }
            return tables.Distinct();
        }

        public async Task<IEnumerable<string>> GetViewsAsync()
        {
            using var conn = await OpenConnectionAsync();
            using var cmd = new OracleCommand("SELECT owner, view_name FROM all_views WHERE owner NOT IN ('SYS')", conn);
            using var reader = await cmd.ExecuteReaderAsync();
            var views = new List<string>();
            while (await reader.ReadAsync())
            {
                var owner = reader.GetString(0);
                var view = reader.GetString(1);
                views.Add($"{owner}.{view}");
            }
            return views.Distinct();
        }

        public async Task<IEnumerable<string>> GetColumnsAsync(string tableName)
        {
            using var conn = await OpenConnectionAsync();
            using var cmd = new OracleCommand($"SELECT * FROM {tableName} WHERE ROWNUM = 0", conn);
            using var reader = await cmd.ExecuteReaderAsync();
            var columns = new List<string>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                columns.Add(reader.GetName(i));
            }
            return columns;
        }

        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }

        public async Task TruncateAsync()
        {
            if (string.IsNullOrEmpty(_tableName))
                throw new ExecutionException("No table specified for Oracle truncate.");

            await foreach (var _ in ExecuteRawSql($"TRUNCATE TABLE {_tableName}")) { }
        }

        public async ValueTask DisposeAsync()
        {
            await Task.CompletedTask;
        }
    }
}
