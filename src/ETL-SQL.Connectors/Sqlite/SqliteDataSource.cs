using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Shared;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using Microsoft.Data.Sqlite;

namespace ETL_SQL.Connectors.Sqlite
{
    public class SqliteDataSource : IDatabaseSource, ITransactionalDataSource
    {
        private readonly string _connectionString;
        private readonly string? _tableName;
        private readonly Dictionary<string, string>? _options;
        private readonly ILogger _logger;
        private readonly IExecutionContext? _context;
        private readonly int _commandTimeout;
        private readonly TransactionState _transactionState;
        private readonly bool _ownsTransactionState;

        private sealed class TransactionState
        {
            public SqliteConnection? Connection;
            public SqliteTransaction? Transaction;
        }

        public SqliteDataSource(IExecutionContext context, string connectionString, string? tableName = null, Dictionary<string, string>? options = null)
            : this(context, connectionString, tableName, options, new TransactionState(), ownsTransactionState: true)
        {
        }

        private SqliteDataSource(IExecutionContext context, string connectionString, string? tableName,
            Dictionary<string, string>? options, TransactionState transactionState, bool ownsTransactionState)
        {
            _context = context;
            _logger = context.Logger;
            _tableName = tableName;
            _options = options;
            _transactionState = transactionState;
            _ownsTransactionState = ownsTransactionState;

            // Zero-Trust Path Resolution
            var builder = new SqliteConnectionStringBuilder(connectionString);
            if (!string.IsNullOrEmpty(builder.Password))
                throw new ExecutionException(
                    "SQLite PASSWORD is unsupported because this distribution does not ship SQLCipher. " +
                    "Use filesystem or volume encryption instead.");

            _commandTimeout = options != null
                && options.TryGetValue("TIMEOUT_SECONDS", out var ts)
                && int.TryParse(ts, out var t)
                && t > 0
                    ? t
                    : builder.DefaultTimeout > 0 ? builder.DefaultTimeout : 30;

            string dbPath = builder.DataSource;
            if (builder.Mode != SqliteOpenMode.Memory
                && dbPath != ":memory:"
                && !string.IsNullOrEmpty(dbPath))
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
            return new SqliteDataSource(_context!, _connectionString, tableName, _options,
                _transactionState, ownsTransactionState: false);
        }

        private async Task<(SqliteConnection Connection, bool IsShared)> GetConnectionAsync()
        {
            if (_transactionState.Connection != null)
            {
                return (_transactionState.Connection, true);
            }
            var connection = new SqliteConnection(_connectionString);
            try
            {
                await connection.OpenAsync(_context?.CancellationToken ?? CancellationToken.None);
                return (connection, false);
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                await connection.DisposeAsync();
                throw ConnectorExceptionWrapper.Wrap("SQLite", ex);
            }
        }

        public async Task<string> GetVersionAsync()
        {
            var (conn, isShared) = await GetConnectionAsync();
            try
            {
                using var cmd = CreateCommand("SELECT sqlite_version()", conn);
                if (_transactionState.Transaction != null) cmd.Transaction = _transactionState.Transaction;
                var result = await cmd.ExecuteScalarAsync(_context?.CancellationToken ?? CancellationToken.None);
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

        private async IAsyncEnumerable<DataTable> ReadBatchesCore(int batchSize,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(_tableName))
                throw new ExecutionException("No table specified for SQLite data source read.");

            var (conn, isShared) = await GetConnectionAsync();
            try
            {
                using var cmd = CreateCommand($"SELECT * FROM \"{_tableName.Replace("\"", "\"\"")}\"", conn);
                if (_transactionState.Transaction != null) cmd.Transaction = _transactionState.Transaction;
                using var reader = await cmd.ExecuteReaderAsync(EffectiveCancellationToken(cancellationToken));

                var columns = new List<string>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    columns.Add(reader.GetName(i));
                }

                var currentBatch = new DataTable();
                currentBatch.SetColumns(columns);

                while (await reader.ReadAsync(EffectiveCancellationToken(cancellationToken)))
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
            if (_context != null && _context.IsWhatIf) return;

            if (string.IsNullOrEmpty(_tableName))
                throw new ExecutionException("No table specified for SQLite data source write.");

            var (conn, isShared) = await GetConnectionAsync();
            SqliteTransaction? trans = null;
            SqliteCommand? insertCmd = null;

            try
            {
                if (!isShared)
                    trans = conn.BeginTransaction();

                if (!append)
                {
                    using var truncCmd = CreateCommand($"DELETE FROM \"{_tableName.Replace("\"", "\"\"")}\"", conn);
                    if (trans != null) truncCmd.Transaction = trans;
                    else if (_transactionState.Transaction != null) truncCmd.Transaction = _transactionState.Transaction;
                    await truncCmd.ExecuteNonQueryAsync(_context?.CancellationToken ?? CancellationToken.None);
                }

                await foreach (var batch in batches.WithCancellation(
                    _context?.CancellationToken ?? CancellationToken.None))
                {
                    if (batch.Rows.Count == 0) continue;

                    if (insertCmd == null)
                    {
                        var colNames = batch.ColumnNames.ToList();
                        var cols = string.Join(", ", colNames.Select(c => $"\"{c.Replace("\"", "\"\"")}\""));
                        var pars = string.Join(", ", colNames.Select((c, idx) => $"$p{idx}"));
                        var insertSql = $"INSERT INTO \"{_tableName.Replace("\"", "\"\"")}\" ({cols}) VALUES ({pars})";
                        insertCmd = CreateCommand(insertSql, conn);
                        if (trans != null) insertCmd.Transaction = trans;
                        else if (_transactionState.Transaction != null) insertCmd.Transaction = _transactionState.Transaction;

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
                        await insertCmd.ExecuteNonQueryAsync(_context?.CancellationToken ?? CancellationToken.None);
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
                if (insertCmd != null) await insertCmd.DisposeAsync();
                if (trans != null) await trans.DisposeAsync();
                if (!isShared) await conn.DisposeAsync();
            }
        }

        public async Task TruncateAsync()
        {
            if (_context != null && _context.IsWhatIf) return;

            if (string.IsNullOrEmpty(_tableName))
                throw new ExecutionException("No table specified for SQLite truncate.");

            var (conn, isShared) = await GetConnectionAsync();
            try
            {
                using var cmd = CreateCommand($"DELETE FROM \"{_tableName.Replace("\"", "\"\"")}\"", conn);
                if (_transactionState.Transaction != null) cmd.Transaction = _transactionState.Transaction;
                await cmd.ExecuteNonQueryAsync(_context?.CancellationToken ?? CancellationToken.None);
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
                if (_transactionState.Transaction != null) cmd.Transaction = _transactionState.Transaction;
                using var reader = await cmd.ExecuteReaderAsync(_context?.CancellationToken ?? CancellationToken.None);
                var columns = new List<string>();
                while (await reader.ReadAsync(_context?.CancellationToken ?? CancellationToken.None))
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
                if (_transactionState.Transaction != null) cmd.Transaction = _transactionState.Transaction;
                using var reader = await cmd.ExecuteReaderAsync(_context?.CancellationToken ?? CancellationToken.None);
                var tables = new List<string>();
                while (await reader.ReadAsync(_context?.CancellationToken ?? CancellationToken.None))
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
                if (_transactionState.Transaction != null) cmd.Transaction = _transactionState.Transaction;
                using var reader = await cmd.ExecuteReaderAsync(_context?.CancellationToken ?? CancellationToken.None);
                var views = new List<string>();
                while (await reader.ReadAsync(_context?.CancellationToken ?? CancellationToken.None))
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

        private async IAsyncEnumerable<DataTable> ExecuteRawSqlCore(string sql, IEnumerable<object?>? parameters,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var (conn, isShared) = await GetConnectionAsync();
            try
            {
                using var cmd = CreateCommand(sql, conn);
                if (_transactionState.Transaction != null) cmd.Transaction = _transactionState.Transaction;

                if (parameters != null)
                {
                    int idx = 0;
                    foreach (var val in parameters)
                    {
                        cmd.Parameters.Add(new SqliteParameter($"$p{idx++}", val ?? DBNull.Value));
                    }
                }

                using var reader = await cmd.ExecuteReaderAsync(EffectiveCancellationToken(cancellationToken));
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

                while (await reader.ReadAsync(EffectiveCancellationToken(cancellationToken)))
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
            try
            {
                if (_transactionState.Transaction != null)
                    throw new InvalidOperationException("A SQLite transaction is already active.");

                if (_transactionState.Connection == null)
                {
                    _transactionState.Connection = new SqliteConnection(_connectionString);
                    await _transactionState.Connection.OpenAsync(_context?.CancellationToken ?? CancellationToken.None);
                }
                _transactionState.Transaction = _transactionState.Connection.BeginTransaction();
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                await CloseTransactionStateAsync(rollback: false, suppressErrors: true);
                throw ConnectorExceptionWrapper.Wrap("SQLite", ex);
            }
        }

        public async Task CommitAsync()
        {
            try
            {
                if (_transactionState.Transaction != null)
                    await _transactionState.Transaction.CommitAsync(_context?.CancellationToken ?? CancellationToken.None);
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("SQLite", ex);
            }
            finally
            {
                await CloseTransactionStateAsync(rollback: false, suppressErrors: true);
            }
        }

        public async Task RollbackAsync()
        {
            try
            {
                if (_transactionState.Transaction != null)
                    await _transactionState.Transaction.RollbackAsync(_context?.CancellationToken ?? CancellationToken.None);
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("SQLite", ex);
            }
            finally
            {
                await CloseTransactionStateAsync(rollback: false, suppressErrors: true);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_ownsTransactionState)
                await CloseTransactionStateAsync(rollback: true, suppressErrors: true);
        }

        private async Task CloseTransactionStateAsync(bool rollback, bool suppressErrors)
        {
            try
            {
                if (rollback && _transactionState.Transaction != null)
                    await _transactionState.Transaction.RollbackAsync(CancellationToken.None);
            }
            catch when (suppressErrors) { }

            try
            {
                if (_transactionState.Transaction != null)
                    await _transactionState.Transaction.DisposeAsync();
            }
            catch when (suppressErrors) { }
            finally
            {
                _transactionState.Transaction = null;
            }

            try
            {
                if (_transactionState.Connection != null)
                    await _transactionState.Connection.DisposeAsync();
            }
            catch when (suppressErrors) { }
            finally
            {
                _transactionState.Connection = null;
            }
        }

        private CancellationToken EffectiveCancellationToken(CancellationToken enumeratorToken) =>
            enumeratorToken.CanBeCanceled
                ? enumeratorToken
                : _context?.CancellationToken ?? CancellationToken.None;

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
