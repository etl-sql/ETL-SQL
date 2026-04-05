using System;
using System.Collections.Generic;
using System.Linq;
using Npgsql;
using ETL_SQL.Data;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Connectors.Postgres
{
    /// <summary>
    /// Data source implementation for PostgreSQL.
    /// Supports high-performance batch operations via COPY and transaction management.
    /// </summary>
    public class PostgresDataSource : IDatabaseSource, ITransactionalDataSource
    {
        private readonly string _connectionString;
        private readonly string? _tableName;
        private readonly Dictionary<string, string>? _options;
        private NpgsqlConnection? _transactionalConnection;
        private NpgsqlTransaction? _activeTransaction;

        /// <summary>
        /// Initializes a new instance of the <see cref="PostgresDataSource"/> class.
        /// </summary>
        /// <param name="connectionString">The PostgreSQL connection string.</param>
        /// <param name="tableName">The target table name (optional for raw SQL).</param>
        /// <param name="options">The options used to create this data source.</param>
        public PostgresDataSource(string connectionString, string? tableName = null, Dictionary<string, string>? options = null)
        {
            _connectionString = connectionString;
            _tableName = tableName;
            _options = options;
        }

        /// <summary>Gets the connection string for this data source.</summary>
        public string ConnectionString => _connectionString;
        
        /// <summary>Gets the placeholder path for PostgreSQL.</summary>
        public string Path => "POSTGRES";

        /// <summary>Gets the database dialect name.</summary>
        public string Dialect => "POSTGRES";

        /// <summary>The options used to create this data source.</summary>
        public Dictionary<string, string>? Options => _options;

        /// <summary>Returns a new instance scoped to the specified table.</summary>
        public IDataSource WithTable(string tableName) => new PostgresDataSource(_connectionString, tableName, _options);

        /// <summary>Retrieves the PostgreSQL version information.</summary>
        public async Task<string> GetVersionAsync()
        {
            var (conn, isShared) = await GetConnectionAsync();
            try {
                await using var cmd = new NpgsqlCommand("SELECT version()", conn);
                if (_activeTransaction != null) cmd.Transaction = _activeTransaction;
                var result = await cmd.ExecuteScalarAsync();
                return result?.ToString() ?? "Unknown PostgreSQL Version";
            } finally {
                if (!isShared) await conn.DisposeAsync();
            }
        }

        /// <summary>Returns PostgreSQL-specific SQL functions.</summary>
        public HashSet<string> GetSupportedFunctions() => PostgresSyntax.Functions;

        /// <summary>Reads data from the specified PostgreSQL table in batches.</summary>
        /// <param name="batchSize">The number of rows per batch.</param>
        public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000)
        {
            if (string.IsNullOrEmpty(_tableName))
                throw new ExecutionException("No table specified for Postgres data source read.");

            var (conn, isShared) = await GetConnectionAsync();
            try {
                await using var cmd = new NpgsqlCommand($"SELECT * FROM {_tableName}", conn);
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

        /// <summary>Writes batches of data to the PostgreSQL table using high-performance COPY.</summary>
        /// <param name="batches">An async enumerable of DataTables.</param>
        public async Task WriteBatches(IAsyncEnumerable<DataTable> batches)
        {
            if (string.IsNullOrEmpty(_tableName))
                throw new ExecutionException("No table specified for Postgres data source write.");

            var (conn, isShared) = await GetConnectionAsync();
            System.IO.TextWriter? writer = null;
            try 
            {
                await foreach (var batch in batches)
                {
                    if (batch.Rows.Count == 0) continue;
                    
                    if (writer == null)
                    {
                        var cols = string.Join(", ", batch.ColumnNames.Select(c => $"\"{c}\""));
                        var tableNameSafe = _tableName; 
                        writer = await conn.BeginTextImportAsync($"COPY {tableNameSafe} ({cols}) FROM STDIN");
                    }
                    
                    foreach (var row in batch.Rows)
                    {
                        var values = batch.ColumnNames.Select(key => {
                            var val = row[key];
                            if (val == null || val == DBNull.Value) return "\\N";
                            var s = val.ToString() ?? "";
                            return s.Replace("\t", " ").Replace("\n", " ").Replace("\r", " ");
                        });
                        await writer.WriteLineAsync(string.Join("\t", values));
                    }
                }
            }
            finally
            {
                if (writer != null) await writer.DisposeAsync();
                if (!isShared) await conn.DisposeAsync();
            }
        }

        /// <summary>Executes a raw SQL query and returns the results as a stream of batches.</summary>
        /// <param name="sql">The SQL query to execute.</param>
        public async IAsyncEnumerable<DataTable> ExecuteRawSql(string sql, IEnumerable<object?>? parameters = null)
        {
            var (conn, isShared) = await GetConnectionAsync();
            try {
                await using var cmd = new NpgsqlCommand(sql, conn);
                if (_activeTransaction != null) cmd.Transaction = _activeTransaction;

                int paramCount = 0;
                if (parameters != null)
                {
                    foreach (var param in parameters)
                    {
                        cmd.Parameters.AddWithValue($"p{paramCount++}", param ?? DBNull.Value);
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
        public async Task<IEnumerable<string>> GetColumnsAsync()
        {
            if (string.IsNullOrEmpty(_tableName)) return Enumerable.Empty<string>();

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($"SELECT * FROM {_tableName} LIMIT 0", conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            var columns = new List<string>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                columns.Add(reader.GetName(i));
            }
            return columns;
        }

        /// <summary>Returns a list of all user-accessible base tables.</summary>
        public async Task<IEnumerable<string>> GetTablesAsync()
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT table_schema, table_name FROM information_schema.tables WHERE table_schema NOT IN ('information_schema', 'pg_catalog') AND table_type = 'BASE TABLE'", conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            var tables = new List<string>();
            while (await reader.ReadAsync())
            {
                var schema = reader.GetString(0);
                var table = reader.GetString(1);
                tables.Add(schema == "public" ? table : $"{schema}.{table}");
            }
            return tables.Distinct();
        }

        /// <summary>Returns a list of all user-accessible views.</summary>
        public async Task<IEnumerable<string>> GetViewsAsync()
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT table_schema, table_name FROM information_schema.tables WHERE table_schema NOT IN ('information_schema', 'pg_catalog') AND table_type = 'VIEW'", conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            var views = new List<string>();
            while (await reader.ReadAsync())
            {
                var schema = reader.GetString(0);
                var table = reader.GetString(1);
                views.Add(schema == "public" ? table : $"{schema}.{table}");
            }
            return views.Distinct();
        }

        /// <summary>Discovers column names for a specific table.</summary>
        public async Task<IEnumerable<string>> GetColumnsAsync(string tableName)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($"SELECT * FROM {tableName} LIMIT 0", conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            var columns = new List<string>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                columns.Add(reader.GetName(i));
            }
            return columns;
        }

        /// <summary>Captures a snapshot (no-op for Postgres).</summary>
        public object? Snapshot() => null;

        /// <summary>Restores from a snapshot (no-op for Postgres).</summary>
        public void Restore(object? snapshot) { }

        /// <summary>Begins a new PostgreSQL transaction.</summary>
        public async Task BeginTransactionAsync()
        {
            if (_activeTransaction != null) return;
            _transactionalConnection = new NpgsqlConnection(_connectionString);
            await _transactionalConnection.OpenAsync();
            _activeTransaction = await _transactionalConnection.BeginTransactionAsync();
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

        private async Task<(NpgsqlConnection, bool isShared)> GetConnectionAsync()
        {
            if (_transactionalConnection != null) return (_transactionalConnection, true);
            var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            return (conn, false);
        }

        /// <summary>Truncates the target table.</summary>
        public async Task TruncateAsync()
        {
            if (string.IsNullOrEmpty(_tableName))
                throw new ExecutionException("No table specified for Postgres truncate.");

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
