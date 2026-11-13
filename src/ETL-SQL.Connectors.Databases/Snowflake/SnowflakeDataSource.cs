using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Shared;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using Snowflake.Data.Client;

namespace ETL_SQL.Connectors.Snowflake
{
    /// <summary>
    /// Data source for Snowflake Cloud Data Platform.
    /// Supports full SQL pushdown, batch reads, and transactions.
    /// </summary>
    public class SnowflakeDataSource : IDatabaseSource, ITransactionalDataSource
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
            public SnowflakeDbConnection? Connection;
            public DbTransaction? Transaction;
        }

        // PKCS#8 private-key extension Snowflake documents for key-pair auth. Scoped to this connector's
        // PRIVATE_KEY_FILE validation so other file connectors keep their default whitelist.
        private static readonly HashSet<string> SnowflakePrivateKeyExtensions =
            new(StringComparer.OrdinalIgnoreCase) { ".p8" };

        public SnowflakeDataSource(IExecutionContext context, string connectionString, string? tableName, Dictionary<string, string>? options)
            : this(context, connectionString, tableName, options, new TransactionState(), ownsTransactionState: true) { }

        private SnowflakeDataSource(IExecutionContext context, string connectionString, string? tableName,
            Dictionary<string, string>? options, TransactionState transactionState, bool ownsTransactionState)
        {
            _transactionState = transactionState;
            _ownsTransactionState = ownsTransactionState;
            _context = context;
            _logger = context.Logger;
            _connectionString = connectionString;
            _tableName = tableName;
            _options = options;
            _commandTimeout = options != null
                && options.TryGetValue("TIMEOUT_SECONDS", out var ts)
                && int.TryParse(ts, out var t) ? t : ETL_SQL.Connectors.Shared.DataWarehouseTimeouts.DefaultSeconds(context);

            var host = SnowflakeConnector.GetHostStatic(connectionString, options);
            if (!string.IsNullOrEmpty(host))
            {
                var validationHost = SnowflakeConnector.IsLocalOrExplicitEndpoint(host) || HasEmulatorEndpointOptions(connectionString, options)
                    ? host
                    : host.Contains('.') ? host : host + ".snowflakecomputing.com";
                ETL_SQL.Core.Governance.ConnectorPolicyAuthorizer.EnforceEnterpriseHost(context, validationHost);
            }

            // Zero-Trust Path Resolution for PRIVATE_KEY_FILE
            string? pkFile = options?.GetValueOrDefault("PRIVATE_KEY_FILE");
            if (string.IsNullOrEmpty(pkFile))
            {
                pkFile = ParseValueFromConnectionString(connectionString, "private_key_file");
            }
            if (!string.IsNullOrEmpty(pkFile) && context != null)
            {
                var resolvedKeyPath = context.ResolvePath(pkFile);
                context.SecurityService.ValidatePath(resolvedKeyPath);
                // '.p8' is the documented Snowflake PKCS#8 private-key extension. Allow it only here,
                // via the per-call override, so the global connector whitelist stays unchanged.
                context.SecurityService.ValidateFileType(resolvedKeyPath, overrides: SnowflakePrivateKeyExtensions);
                _connectionString = ReplaceConnectionStringValue(connectionString, "private_key_file", resolvedKeyPath);
            }
        }

        private static string? ParseValueFromConnectionString(string cs, string key)
        {
            foreach (var part in cs.Split(';'))
            {
                var kv = part.Trim().Split('=', 2);
                if (kv.Length == 2 && kv[0].Equals(key, StringComparison.OrdinalIgnoreCase))
                    return kv[1];
            }
            return null;
        }

        // Sets key=newValue, appending it when the connection string does not carry the key yet: a
        // PRIVATE_KEY_FILE given only as an option was validated and then silently dropped.
        internal static string ReplaceConnectionStringValue(string cs, string key, string newValue)
        {
            var parts = cs.Split(';');
            var found = false;
            for (int i = 0; i < parts.Length; i++)
            {
                var kv = parts[i].Trim().Split('=', 2);
                if (kv.Length == 2 && kv[0].Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    parts[i] = $"{kv[0]}={newValue}";
                    found = true;
                }
            }
            if (found) return string.Join(";", parts);
            var trimmed = cs.TrimEnd();
            var separator = trimmed.Length == 0 || trimmed.EndsWith(';') ? "" : ";";
            return $"{trimmed}{separator}{key}={newValue};";
        }

        public string ConnectionString => _connectionString;
        public string Path => "SNOWFLAKE";
        public string Dialect => "SNOWFLAKE";
        public bool SupportsSqlPushdown => true;
        public Dictionary<string, string>? Options => _options;
        public string ConnectorType => "SNOWFLAKE";

        // COMPAT_BREAK: 0.21.0 — table views share live state and do not own transaction disposal.
        public IDataSource WithTable(string tableName) => new SnowflakeDataSource(
            _context!, _connectionString, tableName, _options, _transactionState, ownsTransactionState: false);

        public async Task<string> GetVersionAsync()
        {
            var (conn, isShared) = await GetConnectionAsync();
            try
            {
                using var cmd = CreateCommand(conn);
                cmd.CommandText = "SELECT CURRENT_VERSION()";
                var result = await cmd.ExecuteScalarAsync();
                return $"Snowflake {result}";
            }
            catch (Exception ex) when (IsSnowflakeProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("Snowflake", ex);
            }
            finally
            {
                if (!isShared) await conn.DisposeAsync();
            }
        }

        public HashSet<string> GetSupportedFunctions() => SnowflakeSyntax.Functions;

        public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) =>
            ReadBatches(batchSize, CancellationToken.None);

        public IAsyncEnumerable<DataTable> ReadBatches(int batchSize, CancellationToken cancellationToken) =>
            ConnectorExceptionWrapper.WrapAsync(
                ReadBatchesCore(batchSize, cancellationToken),
                "Snowflake",
                IsSnowflakeProviderException);

        private async IAsyncEnumerable<DataTable> ReadBatchesCore(
            int batchSize,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var effectiveCancellationToken = EffectiveCancellationToken(cancellationToken);
            if (string.IsNullOrEmpty(_tableName))
                throw new ExecutionException("No table specified for Snowflake data source read.");

            var (conn, isShared) = await GetConnectionAsync(effectiveCancellationToken);
            try
            {
                using var cmd = CreateCommand(conn);
                cmd.CommandText = $"SELECT * FROM {QuoteIdentifier(_tableName)}";
                if (_transactionState.Transaction != null) cmd.Transaction = _transactionState.Transaction;

                using var reader = await cmd.ExecuteReaderAsync(effectiveCancellationToken);
                var columns = Enumerable.Range(0, reader.FieldCount).Select(i => reader.GetName(i)).ToList();
                var batch = new DataTable();
                batch.SetColumns(columns);

                var noRows = true;
                while (await reader.ReadAsync(effectiveCancellationToken))
                {
                    noRows = false;
                    var row = batch.NewRow();
                    for (int i = 0; i < reader.FieldCount; i++)
                        row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    await batch.AddRowAsync(row);

                    if (batch.Rows.Count >= batchSize)
                    {
                        yield return batch;
                        batch = new DataTable();
                        batch.SetColumns(columns);
                    }
                }
                // An empty result still names its columns, so an outer join can name an empty side.
                if (batch.Rows.Count > 0 || noRows) yield return batch;
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
                throw new ExecutionException("No table specified for Snowflake data source write.");

            if (!append) await TruncateAsync(effectiveCancellationToken);

            var (conn, isShared) = await GetConnectionAsync(effectiveCancellationToken);
            try
            {
                await foreach (var batch in batches.WithCancellation(effectiveCancellationToken))
                {
                    effectiveCancellationToken.ThrowIfCancellationRequested();
                    if (batch.Rows.Count == 0) continue;

                    var cols = string.Join(", ", batch.ColumnNames.Select(QuoteIdentifier));
                    var parms = string.Join(", ", Enumerable.Range(0, batch.ColumnNames.Count).Select(i => $":{i}"));
                    var sql = $"INSERT INTO {QuoteIdentifier(_tableName)} ({cols}) VALUES ({parms})";

                    foreach (var row in batch.Rows)
                    {
                        effectiveCancellationToken.ThrowIfCancellationRequested();
                        using var cmd = CreateCommand(conn);
                        cmd.CommandText = sql;
                        if (_transactionState.Transaction != null) cmd.Transaction = _transactionState.Transaction;
                        for (int i = 0; i < batch.ColumnNames.Count; i++)
                        {
                            var p = cmd.CreateParameter();
                            p.ParameterName = i.ToString();
                            p.Value = row[batch.ColumnNames[i]] ?? DBNull.Value;
                            cmd.Parameters.Add(p);
                        }
                        await cmd.ExecuteNonQueryAsync(effectiveCancellationToken);
                    }
                }
            }
            catch (Exception ex) when (IsSnowflakeProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("Snowflake", ex);
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
                "Snowflake",
                IsSnowflakeProviderException);

        private async IAsyncEnumerable<DataTable> ExecuteRawSqlCore(
            string sql,
            IEnumerable<object?>? parameters = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var effectiveCancellationToken = EffectiveCancellationToken(cancellationToken);
            var (conn, isShared) = await GetConnectionAsync(effectiveCancellationToken);
            try
            {
                using var cmd = CreateCommand(conn);
                cmd.CommandText = sql;
                if (_transactionState.Transaction != null) cmd.Transaction = _transactionState.Transaction;

                int pIdx = 0;
                if (parameters != null)
                {
                    foreach (var p in parameters)
                    {
                        var param = cmd.CreateParameter();
                        param.ParameterName = pIdx++.ToString();
                        param.Value = p ?? DBNull.Value;
                        cmd.Parameters.Add(param);
                    }
                }

                using var reader = await cmd.ExecuteReaderAsync(effectiveCancellationToken);

                int resultSetIndex = 0;
                do
                {
                    var columns = Enumerable.Range(0, reader.FieldCount).Select(i => reader.GetName(i)).ToList();
                    var batch = new DataTable { ResultSetIndex = resultSetIndex };
                    batch.SetColumns(columns);

                    while (await reader.ReadAsync(effectiveCancellationToken))
                    {
                        var row = batch.NewRow();
                        for (int i = 0; i < reader.FieldCount; i++)
                            row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                        await batch.AddRowAsync(row);

                        if (batch.Rows.Count >= 10000)
                        {
                            yield return batch;
                            batch = new DataTable { ResultSetIndex = resultSetIndex };
                            batch.SetColumns(columns);
                        }
                    }

                    batch.RowsAffected = (int)reader.RecordsAffected;
                    yield return batch;
                    resultSetIndex++;
                }
                while (await reader.NextResultAsync(effectiveCancellationToken));
            }
            finally
            {
                if (!isShared) await conn.DisposeAsync();
            }
        }

        public Task<IEnumerable<string>> GetColumnsAsync()
            => GetColumnsAsync(CancellationToken.None);

        public async Task<IEnumerable<string>> GetColumnsAsync(CancellationToken cancellationToken)
        {
            var effectiveCancellationToken = EffectiveCancellationToken(cancellationToken);
            if (string.IsNullOrEmpty(_tableName)) return Enumerable.Empty<string>();
            var (conn, isShared) = await GetConnectionAsync(effectiveCancellationToken);
            try
            {
                using var cmd = CreateCommand(conn);
                cmd.CommandText = $"SELECT * FROM {QuoteIdentifier(_tableName)} LIMIT 0";
                using var reader = await cmd.ExecuteReaderAsync(effectiveCancellationToken);
                return Enumerable.Range(0, reader.FieldCount).Select(i => reader.GetName(i)).ToList();
            }
            catch (Exception ex) when (IsSnowflakeProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("Snowflake", ex);
            }
            finally { if (!isShared) await conn.DisposeAsync(); }
        }

        public Task<IEnumerable<string>> GetTablesAsync()
            => GetTablesAsync(CancellationToken.None);

        public async Task<IEnumerable<string>> GetTablesAsync(CancellationToken cancellationToken)
        {
            var effectiveCancellationToken = EffectiveCancellationToken(cancellationToken);
            var (conn, isShared) = await GetConnectionAsync(effectiveCancellationToken);
            try
            {
                using var cmd = CreateCommand(conn);
                cmd.CommandText =
                    "SELECT TABLE_SCHEMA, TABLE_NAME FROM INFORMATION_SCHEMA.TABLES " +
                    "WHERE TABLE_TYPE = 'BASE TABLE' AND TABLE_SCHEMA <> 'INFORMATION_SCHEMA' " +
                    "ORDER BY TABLE_SCHEMA, TABLE_NAME";
                using var reader = await cmd.ExecuteReaderAsync(effectiveCancellationToken);
                var result = new List<string>();
                while (await reader.ReadAsync(effectiveCancellationToken))
                {
                    var schema = reader.GetString(0);
                    var table = reader.GetString(1);
                    result.Add(schema.Equals("PUBLIC", StringComparison.OrdinalIgnoreCase) ? table : $"{schema}.{table}");
                }
                return result;
            }
            catch (Exception ex) when (IsSnowflakeProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("Snowflake", ex);
            }
            finally { if (!isShared) await conn.DisposeAsync(); }
        }

        public Task<IEnumerable<string>> GetViewsAsync()
            => GetViewsAsync(CancellationToken.None);

        public async Task<IEnumerable<string>> GetViewsAsync(CancellationToken cancellationToken)
        {
            var effectiveCancellationToken = EffectiveCancellationToken(cancellationToken);
            var (conn, isShared) = await GetConnectionAsync(effectiveCancellationToken);
            try
            {
                using var cmd = CreateCommand(conn);
                cmd.CommandText =
                    "SELECT TABLE_SCHEMA, TABLE_NAME FROM INFORMATION_SCHEMA.VIEWS " +
                    "WHERE TABLE_SCHEMA <> 'INFORMATION_SCHEMA' ORDER BY TABLE_SCHEMA, TABLE_NAME";
                using var reader = await cmd.ExecuteReaderAsync(effectiveCancellationToken);
                var result = new List<string>();
                while (await reader.ReadAsync(effectiveCancellationToken))
                {
                    var schema = reader.GetString(0);
                    var view = reader.GetString(1);
                    result.Add(schema.Equals("PUBLIC", StringComparison.OrdinalIgnoreCase) ? view : $"{schema}.{view}");
                }
                return result;
            }
            catch (Exception ex) when (IsSnowflakeProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("Snowflake", ex);
            }
            finally { if (!isShared) await conn.DisposeAsync(); }
        }

        public Task<IEnumerable<string>> GetColumnsAsync(string tableName)
            => GetColumnsAsync(tableName, CancellationToken.None);

        public async Task<IEnumerable<string>> GetColumnsAsync(string tableName, CancellationToken cancellationToken)
        {
            var effectiveCancellationToken = EffectiveCancellationToken(cancellationToken);
            var (conn, isShared) = await GetConnectionAsync(effectiveCancellationToken);
            try
            {
                using var cmd = CreateCommand(conn);
                cmd.CommandText = $"SELECT * FROM {QuoteIdentifier(tableName)} LIMIT 0";
                using var reader = await cmd.ExecuteReaderAsync(effectiveCancellationToken);
                return Enumerable.Range(0, reader.FieldCount).Select(i => reader.GetName(i)).ToList();
            }
            catch (Exception ex) when (IsSnowflakeProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("Snowflake", ex);
            }
            finally { if (!isShared) await conn.DisposeAsync(); }
        }

        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }

        public async Task BeginTransactionAsync()
        {
            if (_transactionState.Transaction != null) return;
            var (conn, _) = await GetConnectionAsync();
            try
            {
                _transactionState.Transaction = await conn.BeginTransactionAsync();
                _transactionState.Connection = conn;
            }
            catch (Exception ex) when (IsSnowflakeProviderException(ex))
            {
                await conn.DisposeAsync();
                throw ConnectorExceptionWrapper.Wrap("Snowflake", ex);
            }
        }

        public Task CommitAsync() => EndTransactionAsync(commit: true);

        public Task RollbackAsync() => EndTransactionAsync(commit: false);

        private async Task EndTransactionAsync(bool commit)
        {
            if (_transactionState.Transaction == null) return;
            try
            {
                if (commit) await _transactionState.Transaction.CommitAsync();
                else await _transactionState.Transaction.RollbackAsync();
            }
            catch (Exception ex) when (IsSnowflakeProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("Snowflake", ex);
            }
            finally
            {
                await _transactionState.Transaction.DisposeAsync();
                if (_transactionState.Connection != null) await _transactionState.Connection.DisposeAsync();
                _transactionState.Transaction = null;
                _transactionState.Connection = null;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (!_ownsTransactionState) return;
            if (_transactionState.Transaction != null) await RollbackAsync();
            if (_transactionState.Connection != null) await _transactionState.Connection.DisposeAsync();
            _transactionState.Transaction = null;
            _transactionState.Connection = null;
        }

        private async Task<(SnowflakeDbConnection conn, bool isShared)> GetConnectionAsync(CancellationToken cancellationToken = default)
        {
            if (_transactionState.Connection != null) return (_transactionState.Connection, true);
            var conn = new SnowflakeDbConnection { ConnectionString = _connectionString };
            try
            {
                await ConnectorRetryPolicy.ForSnowflake(_logger)
                    .ExecuteAsync(async ct => await conn.OpenAsync(ct), cancellationToken);
                return (conn, false);
            }
            catch (Exception ex) when (IsSnowflakeProviderException(ex))
            {
                await conn.DisposeAsync();
                throw ConnectorExceptionWrapper.Wrap("Snowflake", ex);
            }
        }

        private async Task TruncateAsync(CancellationToken cancellationToken = default)
        {
            if (_context != null && _context.IsWhatIf) return;
            await foreach (var _ in ExecuteRawSql($"TRUNCATE TABLE {QuoteIdentifier(_tableName!)}", null, cancellationToken).WithCancellation(cancellationToken)) { }
        }

        private System.Data.Common.DbCommand CreateCommand(SnowflakeDbConnection conn)
        {
            var cmd = conn.CreateCommand();
            cmd.CommandTimeout = _commandTimeout;
            return cmd;
        }

        private static string QuoteIdentifier(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            var parts = name.Split('.');
            return string.Join(".", parts.Select(p =>
            {
                if (p.StartsWith('"') && p.EndsWith('"') && p.Length >= 2)
                {
                    var unquoted = p.Substring(1, p.Length - 2).Replace("\"", "\"\"");
                    return $"\"{unquoted}\"";
                }
                return $"\"{p.Replace("\"", "\"\"")}\"";
            }));
        }

        private static bool HasEmulatorEndpointOptions(string connectionString, Dictionary<string, string>? options)
        {
            if (options != null
                && ((options.TryGetValue("HOST", out var host) && SnowflakeConnector.IsLocalOrExplicitEndpoint(host))
                    || options.ContainsKey("PORT")
                    || options.ContainsKey("PROTOCOL")))
            {
                return true;
            }

            return connectionString.Contains("port=", StringComparison.OrdinalIgnoreCase)
                || connectionString.Contains("scheme=http", StringComparison.OrdinalIgnoreCase)
                || connectionString.Contains("host=", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSnowflakeProviderException(Exception ex)
        {
            if (ex is SnowflakeDbException)
                return true;

            var targetNamespace = ex.TargetSite?.DeclaringType?.Namespace;
            return targetNamespace?.StartsWith("Snowflake.", StringComparison.Ordinal) == true
                || ex.StackTrace?.Contains("Snowflake.Data.", StringComparison.Ordinal) == true;
        }

        private CancellationToken EffectiveCancellationToken(CancellationToken cancellationToken) =>
            cancellationToken.CanBeCanceled ? cancellationToken : (_context?.CancellationToken ?? CancellationToken.None);
    }
}
