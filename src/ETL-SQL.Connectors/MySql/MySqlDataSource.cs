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
using MySqlConnector;

namespace ETL_SQL.Connectors.MySql
{
    /// <summary>
    /// Data source implementation for MySQL & MariaDB.
    /// Supports high-performance bulk operations via MySqlBulkCopy and transaction management.
    /// </summary>
    public class MySqlDataSource : IDatabaseSource, ITransactionalDataSource
    {
        private readonly string _connectionString;
        private readonly string? _tableName;
        private readonly Dictionary<string, string>? _options;
        private readonly ILogger _logger;
        private readonly IExecutionContext? _context;
        private readonly int _commandTimeout;
        private MySqlConnection? _transactionalConnection;
        private MySqlTransaction? _activeTransaction;

        /// <summary>
        /// Initializes a new instance of the <see cref="MySqlDataSource"/> class.
        /// </summary>
        public MySqlDataSource(IExecutionContext context, string connectionString, string? tableName = null, Dictionary<string, string>? options = null)
        {
            _context = context;
            _logger = context.Logger;
            _connectionString = connectionString;
            _tableName = tableName;
            _options = options;
            _commandTimeout = ConnectorTimeouts.ResolveCommandTimeoutSeconds(context, options);

            // Security Hardening: egress control
            var host = MySqlConnector.GetHostStatic(connectionString, options);
            if (host != null) ETL_SQL.Core.Governance.ConnectorPolicyAuthorizer.EnforceEnterpriseHost(context, host);
        }

        public string ConnectionString => _connectionString;
        public string Path => "MYSQL";
        public string Dialect => "MYSQL";
        public bool SupportsSqlPushdown => true;
        public Dictionary<string, string>? Options => _options;
        public string ConnectorType => "MYSQL";
        public ICatalogMetadataProvider? GetCatalogProvider() => new MySqlCatalogProvider(_connectionString);

        public IDataSource WithTable(string tableName)
        {
            var ds = new MySqlDataSource(_context!, _connectionString, tableName, _options);
            ds._transactionalConnection = _transactionalConnection;
            ds._activeTransaction = _activeTransaction;
            return ds;
        }

        public async Task<string> GetVersionAsync()
        {
            var (conn, isShared) = await GetConnectionAsync();
            try
            {
                await using var cmd = CreateCommand("SELECT VERSION()", conn);
                if (_activeTransaction != null) cmd.Transaction = _activeTransaction;
                var result = await cmd.ExecuteScalarAsync();
                return result?.ToString() ?? "Unknown MySql/MariaDB Version";
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("MySql", ex);
            }
            finally
            {
                if (!isShared) await conn.DisposeAsync();
            }
        }

        public HashSet<string> GetSupportedFunctions() => MySqlSyntax.Functions;

        public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) =>
            ReadBatches(batchSize, CancellationToken.None);

        public IAsyncEnumerable<DataTable> ReadBatches(int batchSize, CancellationToken cancellationToken) =>
            ConnectorExceptionWrapper.WrapAsync(
                ReadBatchesCore(batchSize, cancellationToken),
                "MySql",
                ShouldWrapProviderException);

        private async IAsyncEnumerable<DataTable> ReadBatchesCore(
            int batchSize,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(_tableName))
                throw new ExecutionException("No table specified for MySql data source read.");

            var effectiveCancellationToken = EffectiveCancellationToken(cancellationToken);
            var (conn, isShared) = await GetConnectionAsync(effectiveCancellationToken);
            try
            {
                await using var cmd = CreateCommand($"SELECT * FROM {QuoteIdentifier(_tableName)}", conn);
                if (_activeTransaction != null) cmd.Transaction = _activeTransaction;
                await using var reader = await cmd.ExecuteReaderAsync(effectiveCancellationToken);

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
                if (!isShared) await conn.DisposeAsync();
            }
        }

        public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) =>
            WriteBatches(batches, append, CancellationToken.None);

        public async Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append, CancellationToken cancellationToken)
        {
            var effectiveCancellationToken = EffectiveCancellationToken(cancellationToken);
            if (_context != null && _context.IsWhatIf) return;

            if (string.IsNullOrEmpty(_tableName))
                throw new ExecutionException("No table specified for MySql data source write.");

            if (!append) await TruncateAsync(effectiveCancellationToken);

            var (conn, isShared) = await GetConnectionAsync(effectiveCancellationToken);
            try
            {
                var bulkCopy = _activeTransaction != null
                    ? new MySqlBulkCopy(conn, _activeTransaction)
                    : new MySqlBulkCopy(conn);
                bulkCopy.DestinationTableName = _tableName;
                bulkCopy.BulkCopyTimeout = _commandTimeout;

                var isFirstBatch = true;
                System.Data.DataTable? dt = null;

                await foreach (var batch in batches.WithCancellation(effectiveCancellationToken))
                {
                    if (batch.Rows.Count == 0) continue;

                    if (isFirstBatch)
                    {
                        dt = new System.Data.DataTable();
                        for (int i = 0; i < batch.ColumnNames.Count; i++)
                        {
                            var col = batch.ColumnNames[i];
                            dt.Columns.Add(col);
                            bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(i, col));
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

                    await bulkCopy.WriteToServerAsync(dt, effectiveCancellationToken);
                }
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("MySql", ex);
            }
            finally
            {
                if (!isShared) await conn.DisposeAsync();
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
                "MySql",
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
                await using var cmd = CreateCommand(sql, conn);
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

                await using var reader = await cmd.ExecuteReaderAsync(effectiveCancellationToken);

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

                    if (currentBatch.Rows.Count > 0 || resultSetIndex == 0 || reader.FieldCount > 0)
                    {
                        currentBatch.RowsAffected = (int)reader.RecordsAffected;
                        yield return currentBatch;
                    }
                    else if (resultSetIndex == 0 && reader.RecordsAffected >= 0)
                    {
                        currentBatch.RowsAffected = (int)reader.RecordsAffected;
                        yield return currentBatch;
                    }
                    resultSetIndex++;
                } while (await reader.NextResultAsync(effectiveCancellationToken));
            }
            finally
            {
                if (!isShared) await conn.DisposeAsync();
            }
        }

        public async Task<IEnumerable<string>> GetColumnsAsync()
        {
            if (string.IsNullOrEmpty(_tableName)) return Enumerable.Empty<string>();

            try
            {
                await using var conn = new MySqlConnection(_connectionString);
                await conn.OpenAsync();
                await using var cmd = CreateCommand($"SELECT * FROM {QuoteIdentifier(_tableName)} LIMIT 0", conn);
                await using var reader = await cmd.ExecuteReaderAsync();
                var columns = new List<string>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    columns.Add(reader.GetName(i));
                }
                return columns;
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("MySql", ex);
            }
        }

        public async Task<IEnumerable<string>> GetTablesAsync()
        {
            try
            {
                var connBuilder = new MySqlConnectionStringBuilder(_connectionString);
                var defaultDb = connBuilder.Database;

                await using var conn = new MySqlConnection(_connectionString);
                await conn.OpenAsync();
                await using var cmd = CreateCommand("SELECT TABLE_SCHEMA, TABLE_NAME FROM information_schema.tables WHERE TABLE_SCHEMA NOT IN ('information_schema', 'mysql', 'performance_schema', 'sys') AND TABLE_TYPE = 'BASE TABLE'", conn);
                await using var reader = await cmd.ExecuteReaderAsync();
                var tables = new List<string>();
                while (await reader.ReadAsync())
                {
                    var schema = reader.GetString(0);
                    var table = reader.GetString(1);
                    tables.Add(string.Equals(schema, defaultDb, StringComparison.OrdinalIgnoreCase) ? table : $"{schema}.{table}");
                }
                return tables.Distinct();
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("MySql", ex);
            }
        }

        public async Task<IEnumerable<string>> GetViewsAsync()
        {
            try
            {
                var connBuilder = new MySqlConnectionStringBuilder(_connectionString);
                var defaultDb = connBuilder.Database;

                await using var conn = new MySqlConnection(_connectionString);
                await conn.OpenAsync();
                await using var cmd = CreateCommand("SELECT TABLE_SCHEMA, TABLE_NAME FROM information_schema.tables WHERE TABLE_SCHEMA NOT IN ('information_schema', 'mysql', 'performance_schema', 'sys') AND TABLE_TYPE = 'VIEW'", conn);
                await using var reader = await cmd.ExecuteReaderAsync();
                var views = new List<string>();
                while (await reader.ReadAsync())
                {
                    var schema = reader.GetString(0);
                    var table = reader.GetString(1);
                    views.Add(string.Equals(schema, defaultDb, StringComparison.OrdinalIgnoreCase) ? table : $"{schema}.{table}");
                }
                return views.Distinct();
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("MySql", ex);
            }
        }

        public async Task<IEnumerable<string>> GetColumnsAsync(string tableName)
        {
            try
            {
                await using var conn = new MySqlConnection(_connectionString);
                await conn.OpenAsync();
                await using var cmd = CreateCommand($"SELECT * FROM {QuoteIdentifier(tableName)} LIMIT 0", conn);
                await using var reader = await cmd.ExecuteReaderAsync();
                var columns = new List<string>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    columns.Add(reader.GetName(i));
                }
                return columns;
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("MySql", ex);
            }
        }

        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }

        public async Task BeginTransactionAsync()
        {
            if (_activeTransaction != null) return;
            var conn = new MySqlConnection(_connectionString);
            try
            {
                await conn.OpenAsync();
                _transactionalConnection = conn;
                _activeTransaction = await _transactionalConnection.BeginTransactionAsync();
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                await conn.DisposeAsync();
                _transactionalConnection = null;
                throw ConnectorExceptionWrapper.Wrap("MySql", ex);
            }
        }

        public async Task CommitAsync()
        {
            if (_activeTransaction == null) return;
            var tx = _activeTransaction;
            var conn = _transactionalConnection;
            _activeTransaction = null;
            _transactionalConnection = null;
            try
            {
                await tx.CommitAsync();
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("MySql", ex);
            }
            finally
            {
                await tx.DisposeAsync();
                if (conn != null) await conn.DisposeAsync();
            }
        }

        public async Task RollbackAsync()
        {
            if (_activeTransaction == null) return;
            var tx = _activeTransaction;
            var conn = _transactionalConnection;
            _activeTransaction = null;
            _transactionalConnection = null;
            try
            {
                await tx.RollbackAsync();
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("MySql", ex);
            }
            finally
            {
                await tx.DisposeAsync();
                if (conn != null) await conn.DisposeAsync();
            }
        }

        private async Task<(MySqlConnection, bool isShared)> GetConnectionAsync(CancellationToken cancellationToken = default)
        {
            if (_transactionalConnection != null) return (_transactionalConnection, true);
            var conn = new MySqlConnection(_connectionString);
            try
            {
                await ConnectorRetryPolicy.ForMySql(_logger)
                    .ExecuteAsync(async ct =>
                    {
                        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                            ct,
                            EffectiveCancellationToken(cancellationToken));
                        await conn.OpenAsync(linked.Token);
                    });
                return (conn, false);
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                await conn.DisposeAsync();
                throw ConnectorExceptionWrapper.Wrap("MySql", ex);
            }
        }

        public async Task TruncateAsync()
            => await TruncateAsync(CancellationToken.None);

        private async Task TruncateAsync(CancellationToken cancellationToken)
        {
            if (_context != null && _context.IsWhatIf) return;

            if (string.IsNullOrEmpty(_tableName))
                throw new ExecutionException("No table specified for MySql truncate.");

            await foreach (var _ in ExecuteRawSql(
                $"TRUNCATE TABLE {QuoteIdentifier(_tableName)}",
                null,
                cancellationToken))
            {
            }
        }

        private static string QuoteIdentifier(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            var parts = name.Split('.');
            return string.Join(".", parts.Select(p =>
            {
                if (p.StartsWith('`') && p.EndsWith('`') && p.Length >= 2)
                {
                    var unquoted = p.Substring(1, p.Length - 2).Replace("`", "``");
                    return $"`{unquoted}`";
                }
                bool needsQuoting = p.Any(c => !char.IsLetterOrDigit(c) && c != '_');
                return needsQuoting ? $"`{p.Replace("`", "``")}`" : p;
            }));
        }

        public async ValueTask DisposeAsync()
        {
            if (_activeTransaction != null) await RollbackAsync();
            if (_transactionalConnection != null) await _transactionalConnection.DisposeAsync();
            _activeTransaction = null;
            _transactionalConnection = null;
        }

        private MySqlCommand CreateCommand(string sql, MySqlConnection conn)
        {
            var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = _commandTimeout;
            return cmd;
        }

        private CancellationToken EffectiveCancellationToken(CancellationToken cancellationToken) =>
            cancellationToken.CanBeCanceled ? cancellationToken : (_context?.CancellationToken ?? CancellationToken.None);

        private static bool ShouldWrapProviderException(Exception ex) =>
            ex is MySqlException or InvalidOperationException;
    }
}
