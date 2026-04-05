using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.SqlClient;
using ETL_SQL.Data;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Connectors.SqlServer
{
    /// <summary>
    /// Data source implementation for Microsoft SQL Server.
    /// Supports high-performance bulk operations via <see cref="SqlBulkCopy"/> and transaction management.
    /// </summary>
    public class SqlServerDataSource : IDatabaseSource, ITransactionalDataSource
    {
        private readonly string _connectionString;
        private readonly string? _tableName;
        private SqlConnection? _transactionalConnection;
        private SqlTransaction? _activeTransaction;

        /// <summary>
        /// Initializes a new instance of the <see cref="SqlServerDataSource"/> class.
        /// </summary>
        /// <param name="connectionString">The SQL Server connection string.</param>
        /// <param name="tableName">The target table name (optional for raw SQL).</param>
        public SqlServerDataSource(string connectionString, string? tableName = null)
        {
            _connectionString = connectionString;
            _tableName = tableName;
        }

        /// <summary>Gets the placeholder path for SQL Server.</summary>
        public string Path => "MSSQL";

        /// <summary>Gets the database dialect name.</summary>
        public string Dialect => "MSSQL";

        /// <summary>Returns a new instance scoped to the specified table.</summary>
        public IDataSource WithTable(string tableName) => new SqlServerDataSource(_connectionString, tableName);

        /// <summary>Retrieves the SQL Server version information (@@VERSION).</summary>
        public async Task<string> GetVersionAsync()
        {
            var (conn, isShared) = await GetConnectionAsync();
            try {
                await using var cmd = new SqlCommand("SELECT @@VERSION", conn);
                if (_activeTransaction != null) cmd.Transaction = _activeTransaction;
                var result = await cmd.ExecuteScalarAsync();
                return result?.ToString() ?? "Unknown SQL Server Version";
            } finally {
                if (!isShared) await conn.DisposeAsync();
            }
        }

        /// <summary>Returns SQL Server-specific SQL functions.</summary>
        public HashSet<string> GetSupportedFunctions() => SqlServerSyntax.Functions;

        /// <summary>Reads data from the specified SQL Server table in batches.</summary>
        /// <param name="batchSize">The number of rows per batch.</param>
        public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000)
        {
            if (string.IsNullOrEmpty(_tableName))
                throw new ExecutionException("No table specified for SQL Server data source read.");

            var (conn, isShared) = await GetConnectionAsync();
            try {
                await using var cmd = new SqlCommand($"SELECT * FROM {_tableName}", conn);
                if (_activeTransaction != null) cmd.Transaction = _activeTransaction;
                await using var reader = await cmd.ExecuteReaderAsync();

            var columns = new List<string>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                columns.Add(reader.GetName(i));
            }

            var currentBatch = new DataTable();
            currentBatch.SetColumns(columns);

            while (await reader.ReadAsync())
            {
                var row = currentBatch.NewRow();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    row[i] = await reader.IsDBNullAsync(i) ? null : reader.GetValue(i);
                }
                currentBatch.Rows.Add(row);

                if (currentBatch.Rows.Count >= batchSize)
                {
                    yield return currentBatch;
                    currentBatch = new DataTable();
                    currentBatch.SetColumns(columns);
                }
            }

            if (currentBatch.Rows.Count > 0)
            {
                yield return currentBatch;
            }
            } finally {
                if (!isShared) await conn.DisposeAsync();
            }
        }

        /// <summary>Writes batches of data to the SQL Server table using high-performance <see cref="SqlBulkCopy"/>.</summary>
        /// <param name="batches">An async enumerable of DataTables.</param>
        public async Task WriteBatches(IAsyncEnumerable<DataTable> batches)
        {
            if (string.IsNullOrEmpty(_tableName))
                throw new ExecutionException("No table specified for SQL Server data source write.");

            var (conn, isShared) = await GetConnectionAsync();
            try {
                using var bulkCopy = _activeTransaction != null 
                    ? new SqlBulkCopy(conn, SqlBulkCopyOptions.Default, _activeTransaction)
                    : new SqlBulkCopy(conn);
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
                        dataRow[col] = row[col] ?? DBNull.Value;
                    }
                    dt.Rows.Add(dataRow);
                }

                await bulkCopy.WriteToServerAsync(dt);
            }
            } finally {
                if (!isShared) await conn.DisposeAsync();
            }
        }

        /// <summary>Executes a raw SQL query and returns the results as a stream of batches.</summary>
        /// <param name="sql">The SQL query to execute.</param>
        public async IAsyncEnumerable<DataTable> ExecuteRawSql(string sql, IEnumerable<object?>? parameters = null)
        {
            var (conn, isShared) = await GetConnectionAsync();
            try {
                await using var cmd = new SqlCommand(sql, conn);
                if (_activeTransaction != null) cmd.Transaction = _activeTransaction;
                
                int paramCount = 0;
                if (parameters != null)
                {
                    foreach (var param in parameters)
                    {
                        cmd.Parameters.AddWithValue($"@p{paramCount++}", param ?? DBNull.Value);
                    }
                }
                
                if (paramCount > 0)
                {
                    cmd.CommandText = ETL_SQL.Core.Common.ParameterUtility.ProcessParameters(cmd.CommandText);
                }

                await using var reader = await cmd.ExecuteReaderAsync();

            int resultSetIndex = 0;
            do
            {
                var columns = new List<string>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    columns.Add(reader.GetName(i));
                }

                var currentBatch = new DataTable { ResultSetIndex = resultSetIndex };
                currentBatch.SetColumns(columns);

                while (await reader.ReadAsync())
                {
                    var row = currentBatch.NewRow();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        row[i] = await reader.IsDBNullAsync(i) ? null : reader.GetValue(i);
                    }
                    currentBatch.Rows.Add(row);

                    if (currentBatch.Rows.Count >= 10000)
                    {
                        yield return currentBatch;
                        currentBatch = new DataTable { ResultSetIndex = resultSetIndex };
                        currentBatch.SetColumns(columns);
                    }
                }

                if (currentBatch.Rows.Count > 0 || resultSetIndex == 0 || reader.FieldCount > 0)
                {
                    yield return currentBatch;
                }
                resultSetIndex++;
            } while (await reader.NextResultAsync());
            } finally {
                if (!isShared) await conn.DisposeAsync();
            }
        }

        /// <summary>Discovers column names for the current table.</summary>
        public Task<IEnumerable<string>> GetColumnsAsync()
        {
            if (string.IsNullOrEmpty(_tableName)) return Task.FromResult(Enumerable.Empty<string>());
            return GetColumnsAsync(_tableName);
        }

        /// <summary>Returns a list of all user-accessible base tables.</summary>
        public async Task<IEnumerable<string>> GetTablesAsync()
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("SELECT TABLE_SCHEMA, TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'", conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            var tables = new List<string>();
            while (await reader.ReadAsync())
            {
                var schema = reader.GetString(0);
                var table = reader.GetString(1);
                tables.Add(schema == "dbo" ? table : $"{schema}.{table}");
            }
            return tables.Distinct();
        }

        /// <summary>Returns a list of all user-accessible views.</summary>
        public async Task<IEnumerable<string>> GetViewsAsync()
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("SELECT TABLE_SCHEMA, TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'VIEW'", conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            var views = new List<string>();
            while (await reader.ReadAsync())
            {
                var schema = reader.GetString(0);
                var table = reader.GetString(1);
                views.Add(schema == "dbo" ? table : $"{schema}.{table}");
            }
            return views.Distinct();
        }

        /// <summary>Discovers column names for a specific table.</summary>
        public async Task<IEnumerable<string>> GetColumnsAsync(string tableName)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand($"SELECT TOP 0 * FROM {tableName}", conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            var columns = new List<string>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                columns.Add(reader.GetName(i));
            }
            return columns;
        }

        /// <summary>Captures a snapshot (no-op for SQL Server).</summary>
        public object? Snapshot() => null;

        /// <summary>Restores from a snapshot (no-op for SQL Server).</summary>
        public void Restore(object? snapshot) { }

        /// <summary>Begins a new SQL Server transaction.</summary>
        public async Task BeginTransactionAsync()
        {
            if (_activeTransaction != null) return;
            _transactionalConnection = new SqlConnection(_connectionString);
            await _transactionalConnection.OpenAsync();
            _activeTransaction = (SqlTransaction)await _transactionalConnection.BeginTransactionAsync();
        }

        /// <summary>Commits the active transaction.</summary>
        public async Task CommitAsync()
        {
            if (_activeTransaction == null) return;
            await _activeTransaction.CommitAsync();
            await _activeTransaction.DisposeAsync();
            if (_transactionalConnection != null) await _transactionalConnection.DisposeAsync();
            _activeTransaction = null;
            _transactionalConnection = null;
        }

        /// <summary>Rolls back the active transaction.</summary>
        public async Task RollbackAsync()
        {
            if (_activeTransaction == null) return;
            await _activeTransaction.RollbackAsync();
            await _activeTransaction.DisposeAsync();
            if (_transactionalConnection != null) await _transactionalConnection.DisposeAsync();
            _activeTransaction = null;
            _transactionalConnection = null;
        }

        private async Task<(SqlConnection, bool isShared)> GetConnectionAsync()
        {
            if (_transactionalConnection != null) return (_transactionalConnection, true);
            var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            return (conn, false);
        }

        /// <summary>Truncates the target SQL Server table.</summary>
        public async Task TruncateAsync()
        {
            if (string.IsNullOrEmpty(_tableName))
                throw new ExecutionException("No table specified for SQL Server truncate.");

            await foreach (var _ in ExecuteRawSql($"TRUNCATE TABLE {_tableName}")) { }
        }

        /// <summary>Asynchronously disposes resources and rolls back active transactions.</summary>
        public async ValueTask DisposeAsync()
        {
            if (_activeTransaction != null) await RollbackAsync();
            if (_transactionalConnection != null) await _transactionalConnection.DisposeAsync();
            _activeTransaction = null;
            _transactionalConnection = null;
        }
    }
}
