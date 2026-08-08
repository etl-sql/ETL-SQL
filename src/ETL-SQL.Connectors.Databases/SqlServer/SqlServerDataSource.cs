using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Shared;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using Microsoft.Data.SqlClient;

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
        private readonly Dictionary<string, string>? _options;
        private readonly ILogger _logger;
        private readonly IExecutionContext? _context;
        private readonly int _commandTimeout;
        private SqlConnection? _transactionalConnection;
        private SqlTransaction? _activeTransaction;

        /// <summary>
        /// Initializes a new instance of the <see cref="SqlServerDataSource"/> class.
        /// </summary>
        /// <param name="connectionString">The SQL Server connection string.</param>
        /// <param name="tableName">The target table name (optional for raw SQL).</param>
        /// <param name="options">The options used to create this data source.</param>
        /// <param name="logger">The logger instance.</param>
        public SqlServerDataSource(IExecutionContext context, string connectionString, string? tableName = null, Dictionary<string, string>? options = null)
        {
            _context = context;
            _logger = context.Logger;
            _connectionString = connectionString;
            _tableName = tableName;
            _options = options;
            _commandTimeout = ConnectorTimeouts.ResolveCommandTimeoutSeconds(context, options);

            // Security Hardening: egress control
            var host = SqlServerConnector.GetHostStatic(connectionString, options);
            if (host != null) ETL_SQL.Core.Governance.ConnectorPolicyAuthorizer.EnforceEnterpriseHost(context, host);
        }

        public string ConnectionString => _connectionString;
        /// <summary>
        /// Returns the database name from the connection string (e.g. "EDW"), or the server name,
        /// or "MSSQL" as a fallback. Never exposes passwords or the full connection string.
        /// Used for lineage display so source labels are meaningful ("MSSQL: EDW" not just "MSSQL").
        /// </summary>
        public string Path
        {
            get
            {
                try
                {
                    var builder = new SqlConnectionStringBuilder(_connectionString);
                    if (!string.IsNullOrEmpty(builder.InitialCatalog)) return builder.InitialCatalog;
                    if (!string.IsNullOrEmpty(builder.DataSource)) return builder.DataSource;
                }
                catch { /* malformed or ENC: not yet decrypted — fall through */ }
                return "MSSQL";
            }
        }
        /// <summary>
        /// Server and database for lineage labelling, parsed from the connection string.
        /// Credentials are never read out of the builder.
        /// </summary>
        public (string? Server, string? Database) GetLineageLocation()
        {
            try
            {
                var builder = new SqlConnectionStringBuilder(_connectionString);
                return (
                    string.IsNullOrEmpty(builder.DataSource) ? null : builder.DataSource,
                    string.IsNullOrEmpty(builder.InitialCatalog) ? null : builder.InitialCatalog);
            }
            catch { return (null, null); }   // malformed, or still ENC: — no location to report
        }

        public string Dialect => "MSSQL";
        public bool SupportsSqlPushdown => true;
        public string ConnectorType => "MSSQL";
        public Dictionary<string, string>? Options => _options;
        public ETL_SQL.Data.ICatalogMetadataProvider? GetCatalogProvider() => new SqlServerCatalogProvider(_connectionString);

        public IDataSource WithTable(string tableName)
        {
            var ds = new SqlServerDataSource(_context!, _connectionString, tableName, _options);
            ds._transactionalConnection = _transactionalConnection;
            ds._activeTransaction = _activeTransaction;
            return ds;
        }

        public async Task<string> GetVersionAsync()
        {
            if (string.IsNullOrWhiteSpace(_connectionString)) return "MSSQL (Offline)";
            var (conn, isShared) = await GetConnectionAsync();
            try
            {
                await using var cmd = CreateCommand("SELECT @@VERSION", conn);
                if (_activeTransaction != null) cmd.Transaction = _activeTransaction;
                var result = await cmd.ExecuteScalarAsync();
                return result?.ToString() ?? "Unknown SQL Server Version";
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("SQL Server", ex);
            }
            finally
            {
                if (!isShared) await conn.DisposeAsync();
            }
        }

        public HashSet<string> GetSupportedFunctions() => SqlServerSyntax.Functions;

        public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) =>
            ReadBatches(batchSize, CancellationToken.None);

        public IAsyncEnumerable<DataTable> ReadBatches(int batchSize, CancellationToken cancellationToken) =>
            ConnectorExceptionWrapper.WrapAsync(
                ReadBatchesCore(batchSize, cancellationToken),
                "SQL Server",
                ShouldWrapProviderException);

        private async IAsyncEnumerable<DataTable> ReadBatchesCore(
            int batchSize,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(_tableName))
                throw new ExecutionException("No table specified for SQL Server data source read.");

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
            if (string.IsNullOrEmpty(_tableName))
                throw new ExecutionException("No table specified for SQL Server data source write.");

            if (!append) await TruncateAsync(effectiveCancellationToken);

            var (conn, isShared) = await GetConnectionAsync(effectiveCancellationToken);
            try
            {
                using var bulkCopy = _activeTransaction != null
                    ? new SqlBulkCopy(conn, SqlBulkCopyOptions.Default, _activeTransaction)
                    : new SqlBulkCopy(conn);
                bulkCopy.DestinationTableName = _tableName;

                var isFirstBatch = true;
                System.Data.DataTable? dt = null;

                await foreach (var batch in batches.WithCancellation(effectiveCancellationToken))
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

                    await bulkCopy.WriteToServerAsync(dt, effectiveCancellationToken);
                }
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("SQL Server", ex);
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
                "SQL Server",
                ShouldWrapProviderException);

        private async IAsyncEnumerable<DataTable> ExecuteRawSqlCore(
            string sql,
            IEnumerable<object?>? parameters = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var effectiveCancellationToken = EffectiveCancellationToken(cancellationToken);
            var (conn, isShared) = await GetConnectionAsync(effectiveCancellationToken);

            SqlInfoMessageEventHandler infoHandler = (_, e) =>
            {
                foreach (SqlError msg in e.Errors)
                {
                    var color = msg.Class > 10 ? ConsoleColor.Red : ConsoleColor.Cyan;
                    _logger.WriteLine(msg.Message, color);
                }
            };
            conn.InfoMessage += infoHandler;
            conn.FireInfoMessageEventOnUserErrors = true;

            try
            {
                await using var cmd = CreateCommand(sql, conn);
                if (_activeTransaction != null) cmd.Transaction = _activeTransaction;
                cmd.StatementCompleted += (_, e) =>
                {
                    if (e.RecordCount > 0)
                        _logger.WriteLine($"{e.RecordCount} row(s) affected.", ConsoleColor.Cyan);
                };

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

                    if (currentBatch.Rows.Count > 0 || (reader.FieldCount > 0 && resultSetIndex == 0))
                    {
                        currentBatch.RowsAffected = reader.RecordsAffected;
                        yield return currentBatch;
                        resultSetIndex++;
                    }
                    else if (resultSetIndex == 0 && reader.RecordsAffected >= 0)
                    {
                        // DML statement with no results - yield a summary batch
                        currentBatch.RowsAffected = reader.RecordsAffected;
                        yield return currentBatch;
                        resultSetIndex++;
                    }
                } while (await reader.NextResultAsync(effectiveCancellationToken));
            }
            finally
            {
                conn.InfoMessage -= infoHandler;
                if (!isShared) await conn.DisposeAsync();
            }
        }

        public Task<IEnumerable<string>> GetColumnsAsync()
            => GetColumnsAsync(CancellationToken.None);

        public Task<IEnumerable<string>> GetColumnsAsync(CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(_tableName)) return Task.FromResult(Enumerable.Empty<string>());
            return GetColumnsAsync(_tableName, cancellationToken);
        }

        public Task<IEnumerable<string>> GetTablesAsync()
            => GetTablesAsync(CancellationToken.None);

        public async Task<IEnumerable<string>> GetTablesAsync(CancellationToken cancellationToken)
        {
            var effectiveCancellationToken = EffectiveCancellationToken(cancellationToken);
            if (string.IsNullOrWhiteSpace(_connectionString)) return Enumerable.Empty<string>();
            try
            {
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(effectiveCancellationToken);
                await using var cmd = CreateCommand("SELECT TABLE_SCHEMA, TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'", conn);
                await using var reader = await cmd.ExecuteReaderAsync(effectiveCancellationToken);
                var tables = new List<string>();
                while (await reader.ReadAsync(effectiveCancellationToken))
                {
                    var schema = reader.GetString(0);
                    var table = reader.GetString(1);
                    tables.Add(schema == "dbo" ? table : $"{schema}.{table}");
                }
                return tables.Distinct();
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("SQL Server", ex);
            }
        }

        public Task<IEnumerable<string>> GetViewsAsync()
            => GetViewsAsync(CancellationToken.None);

        public async Task<IEnumerable<string>> GetViewsAsync(CancellationToken cancellationToken)
        {
            var effectiveCancellationToken = EffectiveCancellationToken(cancellationToken);
            if (string.IsNullOrWhiteSpace(_connectionString)) return Enumerable.Empty<string>();
            try
            {
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(effectiveCancellationToken);
                await using var cmd = CreateCommand("SELECT TABLE_SCHEMA, TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'VIEW'", conn);
                await using var reader = await cmd.ExecuteReaderAsync(effectiveCancellationToken);
                var views = new List<string>();
                while (await reader.ReadAsync(effectiveCancellationToken))
                {
                    var schema = reader.GetString(0);
                    var table = reader.GetString(1);
                    views.Add(schema == "dbo" ? table : $"{schema}.{table}");
                }
                return views.Distinct();
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("SQL Server", ex);
            }
        }

        public Task<IEnumerable<string>> GetColumnsAsync(string tableName)
            => GetColumnsAsync(tableName, CancellationToken.None);

        public async Task<IEnumerable<string>> GetColumnsAsync(string tableName, CancellationToken cancellationToken)
        {
            var effectiveCancellationToken = EffectiveCancellationToken(cancellationToken);
            if (string.IsNullOrWhiteSpace(_connectionString)) return Enumerable.Empty<string>();
            try
            {
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(effectiveCancellationToken);
                await using var cmd = CreateCommand($"SELECT TOP 0 * FROM {QuoteIdentifier(tableName)}", conn);
                await using var reader = await cmd.ExecuteReaderAsync(effectiveCancellationToken);
                var columns = new List<string>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    columns.Add(reader.GetName(i));
                }
                return columns;
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("SQL Server", ex);
            }
        }

        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }

        public async Task BeginTransactionAsync()
        {
            if (_activeTransaction != null) return;
            var conn = new SqlConnection(_connectionString);
            try
            {
                await conn.OpenAsync();
                _transactionalConnection = conn;
                _activeTransaction = (SqlTransaction)await _transactionalConnection.BeginTransactionAsync();
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                await conn.DisposeAsync();
                _transactionalConnection = null;
                throw ConnectorExceptionWrapper.Wrap("SQL Server", ex);
            }
        }

        public async Task CommitAsync()
        {
            if (_activeTransaction == null) return;
            try
            {
                await _activeTransaction.CommitAsync();
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("SQL Server", ex);
            }
            finally
            {
                await _activeTransaction.DisposeAsync();
                if (_transactionalConnection != null) await _transactionalConnection.DisposeAsync();
                _activeTransaction = null;
                _transactionalConnection = null;
            }
        }

        public async Task RollbackAsync()
        {
            if (_activeTransaction == null) return;
            try
            {
                await _activeTransaction.RollbackAsync();
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("SQL Server", ex);
            }
            finally
            {
                await _activeTransaction.DisposeAsync();
                if (_transactionalConnection != null) await _transactionalConnection.DisposeAsync();
                _activeTransaction = null;
                _transactionalConnection = null;
            }
        }

        private async Task<(SqlConnection, bool isShared)> GetConnectionAsync(CancellationToken cancellationToken = default)
        {
            if (_transactionalConnection != null) return (_transactionalConnection, true);
            if (string.IsNullOrWhiteSpace(_connectionString))
                throw new ExecutionException("Connection string is missing for SQL Server data source.");
            var conn = new SqlConnection(_connectionString);
            try
            {
                await ConnectorRetryPolicy.ForSqlServer(_logger)
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
                throw ConnectorExceptionWrapper.Wrap("SQL Server", ex);
            }
        }

        public async Task TruncateAsync()
            => await TruncateAsync(CancellationToken.None);

        private async Task TruncateAsync(CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(_tableName))
                throw new ExecutionException("No table specified for SQL Server truncate.");

            await foreach (var _ in ExecuteRawSql(
                $"TRUNCATE TABLE {QuoteIdentifier(_tableName)}",
                null,
                cancellationToken))
            {
            }
        }

        private static string QuoteIdentifier(string name)
        {
            var parts = name.Split('.');
            return string.Join(".", parts.Select(p => $"[{p.Replace("]", "]]")}]"));
        }

        public async ValueTask DisposeAsync()
        {
            if (_activeTransaction != null) await RollbackAsync();
            if (_transactionalConnection != null) await _transactionalConnection.DisposeAsync();
            _activeTransaction = null;
            _transactionalConnection = null;
        }

        private SqlCommand CreateCommand(string sql, SqlConnection conn)
        {
            var cmd = new SqlCommand(sql, conn);
            cmd.CommandTimeout = _commandTimeout;
            return cmd;
        }

        private CancellationToken EffectiveCancellationToken(CancellationToken cancellationToken) =>
            cancellationToken.CanBeCanceled ? cancellationToken : (_context?.CancellationToken ?? CancellationToken.None);

        private static bool ShouldWrapProviderException(Exception ex) =>
            ex is SqlException or InvalidOperationException;
    }
}
