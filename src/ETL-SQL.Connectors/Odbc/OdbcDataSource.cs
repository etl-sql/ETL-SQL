using System;
using System.Collections.Generic;
using System.Data.Odbc;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Shared;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;

namespace ETL_SQL.Connectors.Odbc
{
    /// <summary>
    /// Universal data source implementation for ODBC providers.
    /// Provides standard SQL pushdown and transactional support across legacy drivers.
    /// </summary>
    public class OdbcDataSource : IDatabaseSource, ITransactionalDataSource
    {
        private readonly string _connectionString;
        private readonly string? _tableName;
        private readonly Dictionary<string, string>? _options;
        private readonly ILogger _logger;
        private readonly IExecutionContext? _context;
        private readonly int _commandTimeout;
        private OdbcConnection? _transactionalConnection;
        private OdbcTransaction? _activeTransaction;

        public OdbcDataSource(IExecutionContext context, string connectionString, string? tableName = null, Dictionary<string, string>? options = null)
        {
            _context = context;
            _logger = context.Logger;
            _connectionString = connectionString;
            _tableName = tableName;
            _options = options;
            _commandTimeout = ConnectorTimeouts.ResolveCommandTimeoutSeconds(context, options);

            // Security Hardening: egress control
            var host = OdbcConnector.GetHostStatic(connectionString, options);
            if (host != null) ETL_SQL.Core.Governance.ConnectorPolicyAuthorizer.EnforceEnterpriseHost(context, host);
        }

        public string ConnectionString => _connectionString;
        public string Path => "ODBC";
        public string Dialect => "ODBC";
        public bool SupportsSqlPushdown => true;
        public string ConnectorType => "ODBC";
        public Dictionary<string, string>? Options => _options;

        public IDataSource WithTable(string tableName) => new OdbcDataSource(_context!, _connectionString, tableName, _options);

        public async Task<string> GetVersionAsync()
        {
            if (string.IsNullOrWhiteSpace(_connectionString)) return "ODBC (Offline)";
            var (conn, isShared) = await GetConnectionAsync();
            try
            {
                return conn.ServerVersion ?? "Unknown ODBC Driver Version";
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("ODBC", ex);
            }
            finally
            {
                if (!isShared) conn.Dispose();
            }
        }

        public HashSet<string> GetSupportedFunctions() => OdbcSyntax.Functions;

        public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) =>
            ReadBatches(batchSize, CancellationToken.None);

        public IAsyncEnumerable<DataTable> ReadBatches(int batchSize, CancellationToken cancellationToken) =>
            ConnectorExceptionWrapper.WrapAsync(
                ReadBatchesCore(batchSize, cancellationToken),
                "ODBC",
                ShouldWrapProviderException);

        private async IAsyncEnumerable<DataTable> ReadBatchesCore(
            int batchSize,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(_tableName))
                throw new ExecutionException("No table specified for ODBC data source read.");

            var effectiveCancellationToken = EffectiveCancellationToken(cancellationToken);
            var (conn, isShared) = await GetConnectionAsync(effectiveCancellationToken);
            try
            {
                using var cmd = CreateCommand($"SELECT * FROM {OdbcSyntax.QuoteIdentifier(_tableName)}", conn);
                if (_activeTransaction != null) cmd.Transaction = _activeTransaction;
                using var reader = await cmd.ExecuteReaderAsync(effectiveCancellationToken);

                var columns = new List<string>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    columns.Add(reader.GetName(i));
                }

                var currentBatch = new DataTable();
                currentBatch.SetColumns(columns);

                while (await reader.ReadAsync(effectiveCancellationToken))
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
                if (!isShared) conn.Dispose();
            }
        }

        public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) =>
            WriteBatches(batches, append, CancellationToken.None);

        public async Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append, CancellationToken cancellationToken)
        {
            var effectiveCancellationToken = EffectiveCancellationToken(cancellationToken);
            if (string.IsNullOrEmpty(_tableName))
                throw new ExecutionException("No table specified for ODBC data source write.");

            if (!append) await TruncateAsync(effectiveCancellationToken);

            var (conn, isShared) = await GetConnectionAsync(effectiveCancellationToken);
            OdbcTransaction? localTx = null;
            if (_activeTransaction == null) localTx = conn.BeginTransaction();

            try
            {
                var isFirstBatch = true;
                OdbcCommand? insertCmd = null;

                await foreach (var batch in batches.WithCancellation(effectiveCancellationToken))
                {
                    if (batch.Rows.Count == 0) continue;

                    if (isFirstBatch)
                    {
                        var cols = string.Join(", ", batch.ColumnNames.Select(OdbcSyntax.QuoteIdentifier));
                        var params_arr = string.Join(", ", batch.ColumnNames.Select(_ => "?"));
                        insertCmd = CreateCommand($"INSERT INTO {OdbcSyntax.QuoteIdentifier(_tableName)} ({cols}) VALUES ({params_arr})", conn);
                        if (_activeTransaction != null) insertCmd.Transaction = _activeTransaction;
                        else insertCmd.Transaction = localTx;

                        foreach (var col in batch.ColumnNames)
                        {
                            insertCmd.Parameters.Add(new OdbcParameter(col, null));
                        }
                        isFirstBatch = false;
                    }

                    foreach (var row in batch.Rows)
                    {
                        for (int i = 0; i < batch.ColumnNames.Count; i++)
                        {
                            var colName = batch.ColumnNames[i];
                            insertCmd!.Parameters[i].Value = row[colName] ?? DBNull.Value;
                        }
                        await insertCmd!.ExecuteNonQueryAsync(effectiveCancellationToken);
                    }
                }
                localTx?.Commit();
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                localTx?.Rollback();
                throw ConnectorExceptionWrapper.Wrap("ODBC", ex);
            }
            catch
            {
                localTx?.Rollback();
                throw;
            }
            finally
            {
                localTx?.Dispose();
                if (!isShared) conn.Dispose();
            }
        }

        public IAsyncEnumerable<DataTable> ExecuteRawSql(string sql, IEnumerable<object?>? parameters = null) =>
            ExecuteRawSql(sql, parameters, CancellationToken.None);

        public IAsyncEnumerable<DataTable> ExecuteRawSql(
            string sql,
            IEnumerable<object?>? parameters,
            CancellationToken cancellationToken) =>
            ConnectorExceptionWrapper.WrapAsync(
                ExecuteRawSqlCore(sql, parameters, cancellationToken),
                "ODBC",
                ShouldWrapProviderException);

        private async IAsyncEnumerable<DataTable> ExecuteRawSqlCore(
            string sql,
            IEnumerable<object?>? parameters = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var effectiveCancellationToken = EffectiveCancellationToken(cancellationToken);
            var (conn, isShared) = await GetConnectionAsync(effectiveCancellationToken);
            try
            {
                using var cmd = CreateCommand(sql, conn);
                if (_activeTransaction != null) cmd.Transaction = _activeTransaction;

                int paramCount = 0;
                if (parameters != null)
                {
                    foreach (var param in parameters)
                    {
                        cmd.Parameters.Add(new OdbcParameter($"p{paramCount++}", param ?? DBNull.Value));
                    }
                }

                using var reader = await cmd.ExecuteReaderAsync(effectiveCancellationToken);

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

                    while (await reader.ReadAsync(effectiveCancellationToken))
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
                            currentBatch = new DataTable { ResultSetIndex = resultSetIndex };
                            currentBatch.SetColumns(columns);
                        }
                    }

                    if (currentBatch.Rows.Count > 0 || (reader.FieldCount > 0 && resultSetIndex == 0))
                    {
                        currentBatch.RowsAffected = reader.RecordsAffected;
                        yield return currentBatch;
                        resultSetIndex++;
                    }
                    else if (resultSetIndex == 0 && reader.RecordsAffected >= 0)
                    {
                        currentBatch.RowsAffected = reader.RecordsAffected;
                        yield return currentBatch;
                        resultSetIndex++;
                    }
                } while (await reader.NextResultAsync(effectiveCancellationToken));
            }
            finally
            {
                if (!isShared) conn.Dispose();
            }
        }

        public async Task<IEnumerable<string>> GetColumnsAsync()
        {
            if (string.IsNullOrEmpty(_tableName)) return Enumerable.Empty<string>();
            return await GetColumnsAsync(_tableName);
        }

        public async Task<IEnumerable<string>> GetTablesAsync()
        {
            if (string.IsNullOrWhiteSpace(_connectionString)) return Enumerable.Empty<string>();
            var (conn, isShared) = await GetConnectionAsync();
            try
            {
                var tables = new List<string>();
                using var schemaTable = conn.GetSchema("Tables");
                foreach (System.Data.DataRow row in schemaTable.Rows)
                {
                    var table = row["TABLE_NAME"].ToString();
                    if (!string.IsNullOrEmpty(table)) tables.Add(table);
                }
                return tables;
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("ODBC", ex);
            }
            finally
            {
                if (!isShared) conn.Dispose();
            }
        }

        public async Task<IEnumerable<string>> GetViewsAsync()
        {
            if (string.IsNullOrWhiteSpace(_connectionString)) return Enumerable.Empty<string>();
            var (conn, isShared) = await GetConnectionAsync();
            try
            {
                var views = new List<string>();
                using var schemaTable = conn.GetSchema("Views");
                foreach (System.Data.DataRow row in schemaTable.Rows)
                {
                    var view = row["TABLE_NAME"].ToString();
                    if (!string.IsNullOrEmpty(view)) views.Add(view);
                }
                return views;
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("ODBC", ex);
            }
            finally
            {
                if (!isShared) conn.Dispose();
            }
        }

        public async Task<IEnumerable<string>> GetColumnsAsync(string tableName)
        {
            if (string.IsNullOrWhiteSpace(_connectionString)) return Enumerable.Empty<string>();
            var (conn, isShared) = await GetConnectionAsync();
            try
            {
                var columns = new List<string>();
                using var cmd = CreateCommand($"SELECT * FROM {OdbcSyntax.QuoteIdentifier(tableName)} WHERE 1=0", conn);
                using var reader = await cmd.ExecuteReaderAsync();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    columns.Add(reader.GetName(i));
                }
                return columns;
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("ODBC", ex);
            }
            finally
            {
                if (!isShared) conn.Dispose();
            }
        }

        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }

        public async Task BeginTransactionAsync()
        {
            if (_activeTransaction != null) return;
            var (conn, _) = await GetConnectionAsync();
            try
            {
                _transactionalConnection = conn;
                _activeTransaction = _transactionalConnection.BeginTransaction();
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                conn.Dispose();
                _transactionalConnection = null;
                throw ConnectorExceptionWrapper.Wrap("ODBC", ex);
            }
        }

        public Task CommitAsync()
        {
            if (_activeTransaction == null) return Task.CompletedTask;
            try
            {
                _activeTransaction.Commit();
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("ODBC", ex);
            }
            finally
            {
                _activeTransaction.Dispose();
                _transactionalConnection?.Dispose();
                _activeTransaction = null;
                _transactionalConnection = null;
            }
            return Task.CompletedTask;
        }

        public Task RollbackAsync()
        {
            if (_activeTransaction == null) return Task.CompletedTask;
            try
            {
                _activeTransaction.Rollback();
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("ODBC", ex);
            }
            finally
            {
                _activeTransaction.Dispose();
                _transactionalConnection?.Dispose();
                _activeTransaction = null;
                _transactionalConnection = null;
            }
            return Task.CompletedTask;
        }

        private async Task<(OdbcConnection, bool isShared)> GetConnectionAsync(CancellationToken cancellationToken = default)
        {
            if (_transactionalConnection != null) return (_transactionalConnection, true);
            if (string.IsNullOrWhiteSpace(_connectionString))
                throw new ExecutionException("Connection string is missing for ODBC data source.");

            var conn = new OdbcConnection(_connectionString);
            try
            {
                await ConnectorRetryPolicy.ForOdbc(_logger)
                    .ExecuteAsync(async ct =>
                    {
                        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                            ct,
                            EffectiveCancellationToken(cancellationToken));
                        await Task.Run(() => conn.Open(), linked.Token);
                    });
                return (conn, false);
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                conn.Dispose();
                throw ConnectorExceptionWrapper.Wrap("ODBC", ex);
            }
        }

        public async Task TruncateAsync()
            => await TruncateAsync(CancellationToken.None);

        private async Task TruncateAsync(CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(_tableName))
                throw new ExecutionException("No table specified for ODBC truncate.");

            await foreach (var _ in ExecuteRawSql(
                $"DELETE FROM {OdbcSyntax.QuoteIdentifier(_tableName)}",
                null,
                cancellationToken))
            {
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_activeTransaction != null) await RollbackAsync();
            _transactionalConnection?.Dispose();
            _activeTransaction = null;
            _transactionalConnection = null;
        }

        private OdbcCommand CreateCommand(string sql, OdbcConnection conn)
        {
            var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = _commandTimeout;
            return cmd;
        }

        private CancellationToken EffectiveCancellationToken(CancellationToken cancellationToken) =>
            cancellationToken.CanBeCanceled ? cancellationToken : (_context?.CancellationToken ?? CancellationToken.None);

        private static bool ShouldWrapProviderException(Exception ex) =>
            ex is OdbcException or InvalidOperationException;
    }
}
