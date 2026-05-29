using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using ETL_SQL.Data;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Connectors.Shared;

namespace ETL_SQL.Connectors.Sqlite
{
    public class SqliteDataSource : IDatabaseSource, ITransactionalDataSource
    {
        private string _connectionString;
        private readonly string? _tableName;
        private readonly Dictionary<string, string>? _options;
        private readonly ILogger _logger;
        private readonly IExecutionContext? _context;
        private readonly int _commandTimeout;
        private SqliteConnection? _transactionalConnection;
        private SqliteTransaction? _activeTransaction;

        public SqliteDataSource(IExecutionContext context, string connectionString, string? tableName = null, Dictionary<string, string>? options = null)
        {
            _context = context;
            _logger = context.Logger;
            _tableName = tableName;
            _options = options;
            _commandTimeout = options != null && options.TryGetValue("TIMEOUT_SECONDS", out var ts) && int.TryParse(ts, out var t) && t > 0 ? t : 30;

            // Zero-Trust Path Resolution
            var builder = new SqliteConnectionStringBuilder(connectionString);
            string dbPath = builder.DataSource;
            if (dbPath != ":memory:" && !string.IsNullOrEmpty(dbPath) && context != null)
            {
                dbPath = context.ResolvePath(dbPath);
                builder.DataSource = dbPath;
            }
            _connectionString = builder.ConnectionString;
        }

        public string ConnectionString => _connectionString;
        public string Path => "SQLITE";
        public string Dialect => "SQLITE";
        public bool SupportsSqlPushdown => true;
        public Dictionary<string, string>? Options => _options;
        public string ConnectorType => "SQLITE";

        public IDataSource WithTable(string tableName)
        {
            var ds = new SqliteDataSource(_context!, _connectionString, tableName, _options);
            ds._transactionalConnection = _transactionalConnection;
            ds._activeTransaction = _activeTransaction;
            return ds;
        }

        private async Task<(SqliteConnection Connection, bool IsShared)> GetConnectionAsync()
        {
            if (_transactionalConnection != null)
            {
                return (_transactionalConnection, true);
            }
            var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            return (conn, false);
        }

        public async Task<string> GetVersionAsync()
        {
            var (conn, isShared) = await GetConnectionAsync();
            try
            {
                using var cmd = CreateCommand("SELECT sqlite_version()", conn);
                if (_activeTransaction != null) cmd.Transaction = _activeTransaction;
                var result = await cmd.ExecuteScalarAsync();
                return result?.ToString() ?? "Unknown SQLite Version";
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("SQLite", ex);
            }
            finally
            {
                if (!isShared) await conn.DisposeAsync();
            }
        }

        public HashSet<string> GetSupportedFunctions() => SqliteSyntax.Functions;

        public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) =>
            ConnectorExceptionWrapper.WrapAsync(ReadBatchesCore(batchSize), "SQLite", ShouldWrapProviderException);

        private async IAsyncEnumerable<DataTable> ReadBatchesCore(int batchSize)
        {
            if (string.IsNullOrEmpty(_tableName))
                throw new ExecutionException("No table specified for SQLite data source read.");

            var (conn, isShared) = await GetConnectionAsync();
            try
            {
                using var cmd = CreateCommand($"SELECT * FROM \"{_tableName.Replace("\"", "\"\"")}\"", conn);
                if (_activeTransaction != null) cmd.Transaction = _activeTransaction;
                using var reader = await cmd.ExecuteReaderAsync();

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
                        row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    }
                    await currentBatch.AddRowAsync(row);

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
            }
            finally
            {
                if (!isShared) await conn.DisposeAsync();
            }
        }

        public async Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false)
        {
            if (string.IsNullOrEmpty(_tableName))
                throw new ExecutionException("No table specified for SQLite data source write.");

            var (conn, isShared) = await GetConnectionAsync();
            SqliteTransaction? trans = null;
            if (!isShared)
            {
                trans = conn.BeginTransaction();
            }

            try
            {
                if (!append)
                {
                    using var truncCmd = CreateCommand($"DELETE FROM \"{_tableName.Replace("\"", "\"\"")}\"", conn);
                    if (trans != null) truncCmd.Transaction = trans;
                    else if (_activeTransaction != null) truncCmd.Transaction = _activeTransaction;
                    await truncCmd.ExecuteNonQueryAsync();
                }

                string insertSql = "";
                SqliteCommand? insertCmd = null;

                await foreach (var batch in batches)
                {
                    if (batch.Rows.Count == 0) continue;

                    if (insertCmd == null)
                    {
                        var colNames = batch.ColumnNames.ToList();
                        var cols = string.Join(", ", colNames.Select(c => $"\"{c.Replace("\"", "\"\"")}\""));
                        var pars = string.Join(", ", colNames.Select((c, idx) => $"$p{idx}"));
                        insertSql = $"INSERT INTO \"{_tableName.Replace("\"", "\"\"")}\" ({cols}) VALUES ({pars})";
                        
                        insertCmd = CreateCommand(insertSql, conn);
                        if (trans != null) insertCmd.Transaction = trans;
                        else if (_activeTransaction != null) insertCmd.Transaction = _activeTransaction;

                        for (int idx = 0; idx < colNames.Count; idx++)
                        {
                            insertCmd.Parameters.Add(new SqliteParameter($"$p{idx}", null));
                        }
                    }

                    foreach (var row in batch.Rows)
                    {
                        for (int idx = 0; idx < batch.ColumnNames.Count; idx++)
                        {
                            insertCmd.Parameters[idx].Value = row[idx] ?? DBNull.Value;
                        }
                        await insertCmd.ExecuteNonQueryAsync();
                    }
                }

                if (trans != null)
                {
                    await trans.CommitAsync();
                }
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                if (trans != null)
                {
                    try { await trans.RollbackAsync(); } catch { }
                }
                throw ConnectorExceptionWrapper.Wrap("SQLite", ex);
            }
            finally
            {
                if (trans != null) await trans.DisposeAsync();
                if (!isShared) await conn.DisposeAsync();
            }
        }

        public async Task TruncateAsync()
        {
            if (string.IsNullOrEmpty(_tableName))
                throw new ExecutionException("No table specified for SQLite truncate.");

            var (conn, isShared) = await GetConnectionAsync();
            try
            {
                using var cmd = CreateCommand($"DELETE FROM \"{_tableName.Replace("\"", "\"\"")}\"", conn);
                if (_activeTransaction != null) cmd.Transaction = _activeTransaction;
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("SQLite", ex);
            }
            finally
            {
                if (!isShared) await conn.DisposeAsync();
            }
        }

        public async Task<IEnumerable<string>> GetColumnsAsync()
        {
            if (string.IsNullOrEmpty(_tableName))
                throw new ExecutionException("No table specified for SQLite columns lookup.");
            return await GetColumnsAsync(_tableName);
        }

        public async Task<IEnumerable<string>> GetColumnsAsync(string tableName)
        {
            var (conn, isShared) = await GetConnectionAsync();
            try
            {
                // PRAGMA table_info returns columns: cid, name, type, notnull, dflt_value, pk
                using var cmd = CreateCommand($"PRAGMA table_info(\"{tableName.Replace("\"", "\"\"")}\")", conn);
                if (_activeTransaction != null) cmd.Transaction = _activeTransaction;
                using var reader = await cmd.ExecuteReaderAsync();
                var columns = new List<string>();
                while (await reader.ReadAsync())
                {
                    columns.Add(reader.GetString(1)); // name is the second column
                }
                return columns;
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("SQLite", ex);
            }
            finally
            {
                if (!isShared) await conn.DisposeAsync();
            }
        }

        public async Task<IEnumerable<string>> GetTablesAsync()
        {
            var (conn, isShared) = await GetConnectionAsync();
            try
            {
                using var cmd = CreateCommand("SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'", conn);
                if (_activeTransaction != null) cmd.Transaction = _activeTransaction;
                using var reader = await cmd.ExecuteReaderAsync();
                var tables = new List<string>();
                while (await reader.ReadAsync())
                {
                    tables.Add(reader.GetString(0));
                }
                return tables;
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("SQLite", ex);
            }
            finally
            {
                if (!isShared) await conn.DisposeAsync();
            }
        }

        public async Task<IEnumerable<string>> GetViewsAsync()
        {
            var (conn, isShared) = await GetConnectionAsync();
            try
            {
                using var cmd = CreateCommand("SELECT name FROM sqlite_master WHERE type = 'view'", conn);
                if (_activeTransaction != null) cmd.Transaction = _activeTransaction;
                using var reader = await cmd.ExecuteReaderAsync();
                var views = new List<string>();
                while (await reader.ReadAsync())
                {
                    views.Add(reader.GetString(0));
                }
                return views;
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("SQLite", ex);
            }
            finally
            {
                if (!isShared) await conn.DisposeAsync();
            }
        }

        public IAsyncEnumerable<DataTable> ExecuteRawSql(string sql, IEnumerable<object?>? parameters = null) =>
            ConnectorExceptionWrapper.WrapAsync(ExecuteRawSqlCore(sql, parameters), "SQLite", ShouldWrapProviderException);

        private async IAsyncEnumerable<DataTable> ExecuteRawSqlCore(string sql, IEnumerable<object?>? parameters)
        {
            var (conn, isShared) = await GetConnectionAsync();
            try
            {
                using var cmd = CreateCommand(sql, conn);
                if (_activeTransaction != null) cmd.Transaction = _activeTransaction;

                if (parameters != null)
                {
                    int idx = 0;
                    foreach (var val in parameters)
                    {
                        cmd.Parameters.Add(new SqliteParameter($"$p{idx++}", val ?? DBNull.Value));
                    }
                }

                using var reader = await cmd.ExecuteReaderAsync();
                var columns = new List<string>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    columns.Add(reader.GetName(i));
                }

                // If query returns no schemas (like INSERT/UPDATE), return empty table with rows affected
                if (columns.Count == 0)
                {
                    var resultTable = new DataTable();
                    resultTable.SetColumns(new[] { "RowsAffected" });
                    var affectedRow = resultTable.NewRow();
                    affectedRow["RowsAffected"] = reader.RecordsAffected;
                    await resultTable.AddRowAsync(affectedRow);
                    yield return resultTable;
                    yield break;
                }

                var currentBatch = new DataTable();
                currentBatch.SetColumns(columns);

                while (await reader.ReadAsync())
                {
                    var row = currentBatch.NewRow();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    }
                    await currentBatch.AddRowAsync(row);

                    if (currentBatch.Rows.Count >= 10000)
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
            }
            finally
            {
                if (!isShared) await conn.DisposeAsync();
            }
        }

        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }

        // ITransactionalDataSource
        public async Task BeginTransactionAsync()
        {
            if (_transactionalConnection == null)
            {
                _transactionalConnection = new SqliteConnection(_connectionString);
                await _transactionalConnection.OpenAsync();
            }
            _activeTransaction = _transactionalConnection.BeginTransaction();
        }

        public async Task CommitAsync()
        {
            if (_activeTransaction != null)
            {
                await _activeTransaction.CommitAsync();
                await _activeTransaction.DisposeAsync();
                _activeTransaction = null;
            }
            if (_transactionalConnection != null)
            {
                await _transactionalConnection.CloseAsync();
                await _transactionalConnection.DisposeAsync();
                _transactionalConnection = null;
            }
        }

        public async Task RollbackAsync()
        {
            if (_activeTransaction != null)
            {
                await _activeTransaction.RollbackAsync();
                await _activeTransaction.DisposeAsync();
                _activeTransaction = null;
            }
            if (_transactionalConnection != null)
            {
                await _transactionalConnection.CloseAsync();
                await _transactionalConnection.DisposeAsync();
                _transactionalConnection = null;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_activeTransaction != null)
            {
                await _activeTransaction.DisposeAsync();
            }
            if (_transactionalConnection != null)
            {
                await _transactionalConnection.DisposeAsync();
            }
        }

        private SqliteCommand CreateCommand(string sql, SqliteConnection conn)
        {
            var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = _commandTimeout;
            return cmd;
        }

        private static bool ShouldWrapProviderException(Exception ex) =>
            ex is SqliteException or InvalidOperationException;
    }
}
