using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Oracle.ManagedDataAccess.Client;
using ETL_SQL.Data;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Common;

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

        /// <summary>
        /// Initializes a new instance of the <see cref="OracleDataSource"/> class.
        /// </summary>
        /// <param name="connectionString">The Oracle connection string.</param>
        /// <param name="tableName">The target table name (optional for raw SQL).</param>
        /// <param name="options">The options used to create this data source.</param>
        public OracleDataSource(string connectionString, string? tableName = null, Dictionary<string, string>? options = null)
        {
            _connectionString = connectionString;
            _tableName = tableName;
            _options = options;
        }

        /// <summary>Gets the connection string for this data source.</summary>
        public string ConnectionString => _connectionString;
        
        /// <summary>Gets the placeholder path for Oracle.</summary>
        public string Path => "ORACLE";

        /// <summary>Gets the database dialect name.</summary>
        public string Dialect => "ORACLE";

        /// <summary>The options used to create this data source.</summary>
        public Dictionary<string, string>? Options => _options;

        /// <summary>Returns a new instance of the data source scoped to the specified table.</summary>
        public IDataSource WithTable(string tableName) => new OracleDataSource(_connectionString, tableName, _options);

        /// <summary>Retrieves the Oracle database version.</summary>
        public async Task<string> GetVersionAsync()
        {
            using var conn = new OracleConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = new OracleCommand("SELECT version FROM v$instance", conn);
            var result = await cmd.ExecuteScalarAsync();
            return result?.ToString() ?? "Unknown Oracle Version";
        }

        /// <summary>Returns Oracle-specific SQL functions.</summary>
        public HashSet<string> GetSupportedFunctions() => OracleSyntax.Functions;

        /// <summary>Reads data from the specified Oracle table in batches.</summary>
        /// <param name="batchSize">The number of rows per batch.</param>
        public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000)
        {
            if (string.IsNullOrEmpty(_tableName))
                throw new ExecutionException("No table specified for Oracle data source read.");

            using var conn = new OracleConnection(_connectionString);
            await conn.OpenAsync();
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
                currentBatch.Rows.Add(row);

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

        /// <summary>Writes batches of data to the Oracle table using high-performance bulk copy.</summary>
        /// <param name="batches">An async enumerable of DataTables.</param>
        public async Task WriteBatches(IAsyncEnumerable<DataTable> batches)
        {
            if (string.IsNullOrEmpty(_tableName))
                throw new ExecutionException("No table specified for Oracle data source write.");

            using var conn = new OracleConnection(_connectionString);
            await conn.OpenAsync();

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

        /// <summary>Executes a raw SQL query and returns the results as a stream of batches.</summary>
        /// <param name="sql">The SQL query to execute.</param>
        public async IAsyncEnumerable<DataTable> ExecuteRawSql(string sql, IEnumerable<object?>? parameters = null)
        {
            using var conn = new OracleConnection(_connectionString);
            await conn.OpenAsync();
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
                resultBatch.AddRow(row);
            }
            yield return resultBatch;
        }

        /// <summary>Discovers column names for the current table.</summary>
        public Task<IEnumerable<string>> GetColumnsAsync()
        {
            if (string.IsNullOrEmpty(_tableName)) return Task.FromResult(Enumerable.Empty<string>());
            return GetColumnsAsync(_tableName);
        }

        /// <summary>Returns a list of all user-accessible tables.</summary>
        public async Task<IEnumerable<string>> GetTablesAsync()
        {
            using var conn = new OracleConnection(_connectionString);
            await conn.OpenAsync();
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

        /// <summary>Returns a list of all user-accessible views.</summary>
        public async Task<IEnumerable<string>> GetViewsAsync()
        {
            using var conn = new OracleConnection(_connectionString);
            await conn.OpenAsync();
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

        /// <summary>Discovers column names for a specific table.</summary>
        public async Task<IEnumerable<string>> GetColumnsAsync(string tableName)
        {
            using var conn = new OracleConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = new OracleCommand($"SELECT * FROM {tableName} WHERE ROWNUM = 0", conn);
            using var reader = await cmd.ExecuteReaderAsync();
            var columns = new List<string>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                columns.Add(reader.GetName(i));
            }
            return columns;
        }

        /// <summary>Captures a snapshot (no-op for Oracle).</summary>
        public object? Snapshot() => null;

        /// <summary>Restores from a snapshot (no-op for Oracle).</summary>
        public void Restore(object? snapshot) { }

        /// <summary>Truncates the target Oracle table.</summary>
        public async Task TruncateAsync()
        {
            if (string.IsNullOrEmpty(_tableName))
                throw new ExecutionException("No table specified for Oracle truncate.");

            await foreach (var _ in ExecuteRawSql($"TRUNCATE TABLE {_tableName}")) { }
        }

        /// <summary>Asynchronously disposes resources.</summary>
        public async ValueTask DisposeAsync()
        {
            await Task.CompletedTask;
        }
    }
}
