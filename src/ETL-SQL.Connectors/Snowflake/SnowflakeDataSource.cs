using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
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

        private SnowflakeDbConnection? _transactionalConnection;
        private DbTransaction? _activeTransaction;

        // PKCS#8 private-key extension Snowflake documents for key-pair auth. Scoped to this connector's
        // PRIVATE_KEY_FILE validation so other file connectors keep their default whitelist.
        private static readonly HashSet<string> SnowflakePrivateKeyExtensions =
            new(StringComparer.OrdinalIgnoreCase) { ".p8" };

        public SnowflakeDataSource(IExecutionContext context, string connectionString, string? tableName, Dictionary<string, string>? options)
        {
            _context = context;
            _logger = context.Logger;
            _connectionString = connectionString;
            _tableName = tableName;
            _options = options;
            _commandTimeout = options != null
                && options.TryGetValue("TIMEOUT_SECONDS", out var ts)
                && int.TryParse(ts, out var t) ? t : 1800;

            var host = SnowflakeConnector.GetHostStatic(connectionString, options);
            if (!string.IsNullOrEmpty(host))
            {
                var validationHost = SnowflakeConnector.IsLocalOrExplicitEndpoint(host) || HasEmulatorEndpointOptions(connectionString, options)
                    ? host
                    : host.Contains('.') ? host : host + ".snowflakecomputing.com";
                context.SecurityService.ValidateHost(validationHost);
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

        private static string ReplaceConnectionStringValue(string cs, string key, string newValue)
        {
            var parts = cs.Split(';');
            for (int i = 0; i < parts.Length; i++)
            {
                var kv = parts[i].Trim().Split('=', 2);
                if (kv.Length == 2 && kv[0].Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    parts[i] = $"{kv[0]}={newValue}";
                }
            }
            return string.Join(";", parts);
        }

        public string ConnectionString => _connectionString;
        public string Path => "SNOWFLAKE";
        public string Dialect => "SNOWFLAKE";
        public bool SupportsSqlPushdown => true;
        public Dictionary<string, string>? Options => _options;
        public string ConnectorType => "SNOWFLAKE";

        public IDataSource WithTable(string tableName)
            => new SnowflakeDataSource(_context!, _connectionString, tableName, _options);

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
            catch (SnowflakeDbException ex)
            {
                throw new ExecutionException($"Snowflake error: {ex.Message}");
            }
            finally
            {
                if (!isShared) await conn.DisposeAsync();
            }
        }

        public HashSet<string> GetSupportedFunctions() => SnowflakeSyntax.Functions;

        public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000)
        {
            if (string.IsNullOrEmpty(_tableName))
                throw new ExecutionException("No table specified for Snowflake data source read.");

            var (conn, isShared) = await GetConnectionAsync();
            try
            {
                using var cmd = CreateCommand(conn);
                cmd.CommandText = $"SELECT * FROM {QuoteIdentifier(_tableName)}";
                if (_activeTransaction != null) cmd.Transaction = _activeTransaction;

                using var reader = await cmd.ExecuteReaderAsync();
                var columns = Enumerable.Range(0, reader.FieldCount).Select(i => reader.GetName(i)).ToList();
                var batch = new DataTable();
                batch.SetColumns(columns);

                while (await reader.ReadAsync())
                {
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
                if (batch.Rows.Count > 0) yield return batch;
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
                throw new ExecutionException("No table specified for Snowflake data source write.");

            if (!append) await TruncateAsync();

            var (conn, isShared) = await GetConnectionAsync();
            try
            {
                await foreach (var batch in batches)
                {
                    if (batch.Rows.Count == 0) continue;

                    var cols = string.Join(", ", batch.ColumnNames.Select(QuoteIdentifier));
                    var parms = string.Join(", ", Enumerable.Range(0, batch.ColumnNames.Count).Select(i => $":{i}"));
                    var sql = $"INSERT INTO {QuoteIdentifier(_tableName)} ({cols}) VALUES ({parms})";

                    foreach (var row in batch.Rows)
                    {
                        using var cmd = CreateCommand(conn);
                        cmd.CommandText = sql;
                        if (_activeTransaction != null) cmd.Transaction = _activeTransaction;
                        for (int i = 0; i < batch.ColumnNames.Count; i++)
                        {
                            var p = cmd.CreateParameter();
                            p.ParameterName = i.ToString();
                            p.Value = row[batch.ColumnNames[i]] ?? DBNull.Value;
                            cmd.Parameters.Add(p);
                        }
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
            catch (SnowflakeDbException ex)
            {
                throw new ExecutionException($"Snowflake write error: {ex.Message}");
            }
            finally
            {
                if (!isShared) await conn.DisposeAsync();
            }
        }

        public IAsyncEnumerable<DataTable> ExecuteRawSql(string sql, IEnumerable<object?>? parameters = null) =>
            ConnectorExceptionWrapper.WrapAsync(
                ExecuteRawSqlCore(sql, parameters),
                "Snowflake",
                IsSnowflakeProviderException);

        private async IAsyncEnumerable<DataTable> ExecuteRawSqlCore(string sql, IEnumerable<object?>? parameters = null)
        {
            var (conn, isShared) = await GetConnectionAsync();
            try
            {
                using var cmd = CreateCommand(conn);
                cmd.CommandText = sql;
                if (_activeTransaction != null) cmd.Transaction = _activeTransaction;

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

                using var reader = await cmd.ExecuteReaderAsync();

                int resultSetIndex = 0;
                do
                {
                    var columns = Enumerable.Range(0, reader.FieldCount).Select(i => reader.GetName(i)).ToList();
                    var batch = new DataTable { ResultSetIndex = resultSetIndex };
                    batch.SetColumns(columns);

                    while (await reader.ReadAsync())
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
                while (await reader.NextResultAsync());
            }
            finally
            {
                if (!isShared) await conn.DisposeAsync();
            }
        }

        public async Task<IEnumerable<string>> GetColumnsAsync()
        {
            if (string.IsNullOrEmpty(_tableName)) return Enumerable.Empty<string>();
            var (conn, isShared) = await GetConnectionAsync();
            try
            {
                using var cmd = CreateCommand(conn);
                cmd.CommandText = $"SELECT * FROM {QuoteIdentifier(_tableName)} LIMIT 0";
                using var reader = await cmd.ExecuteReaderAsync();
                return Enumerable.Range(0, reader.FieldCount).Select(i => reader.GetName(i)).ToList();
            }
            catch (SnowflakeDbException ex)
            {
                throw new ExecutionException($"Snowflake error: {ex.Message}");
            }
            finally { if (!isShared) await conn.DisposeAsync(); }
        }

        public async Task<IEnumerable<string>> GetTablesAsync()
        {
            var (conn, isShared) = await GetConnectionAsync();
            try
            {
                using var cmd = CreateCommand(conn);
                cmd.CommandText =
                    "SELECT TABLE_SCHEMA, TABLE_NAME FROM INFORMATION_SCHEMA.TABLES " +
                    "WHERE TABLE_TYPE = 'BASE TABLE' AND TABLE_SCHEMA <> 'INFORMATION_SCHEMA' " +
                    "ORDER BY TABLE_SCHEMA, TABLE_NAME";
                using var reader = await cmd.ExecuteReaderAsync();
                var result = new List<string>();
                while (await reader.ReadAsync())
                {
                    var schema = reader.GetString(0);
                    var table = reader.GetString(1);
                    result.Add(schema.Equals("PUBLIC", StringComparison.OrdinalIgnoreCase) ? table : $"{schema}.{table}");
                }
                return result;
            }
            catch (SnowflakeDbException ex)
            {
                throw new ExecutionException($"Snowflake error: {ex.Message}");
            }
            finally { if (!isShared) await conn.DisposeAsync(); }
        }

        public async Task<IEnumerable<string>> GetViewsAsync()
        {
            var (conn, isShared) = await GetConnectionAsync();
            try
            {
                using var cmd = CreateCommand(conn);
                cmd.CommandText =
                    "SELECT TABLE_SCHEMA, TABLE_NAME FROM INFORMATION_SCHEMA.VIEWS " +
                    "WHERE TABLE_SCHEMA <> 'INFORMATION_SCHEMA' ORDER BY TABLE_SCHEMA, TABLE_NAME";
                using var reader = await cmd.ExecuteReaderAsync();
                var result = new List<string>();
                while (await reader.ReadAsync())
                {
                    var schema = reader.GetString(0);
                    var view = reader.GetString(1);
                    result.Add(schema.Equals("PUBLIC", StringComparison.OrdinalIgnoreCase) ? view : $"{schema}.{view}");
                }
                return result;
            }
            catch (SnowflakeDbException ex)
            {
                throw new ExecutionException($"Snowflake error: {ex.Message}");
            }
            finally { if (!isShared) await conn.DisposeAsync(); }
        }

        public async Task<IEnumerable<string>> GetColumnsAsync(string tableName)
        {
            var (conn, isShared) = await GetConnectionAsync();
            try
            {
                using var cmd = CreateCommand(conn);
                cmd.CommandText = $"SELECT * FROM {QuoteIdentifier(tableName)} LIMIT 0";
                using var reader = await cmd.ExecuteReaderAsync();
                return Enumerable.Range(0, reader.FieldCount).Select(i => reader.GetName(i)).ToList();
            }
            catch (SnowflakeDbException ex)
            {
                throw new ExecutionException($"Snowflake error: {ex.Message}");
            }
            finally { if (!isShared) await conn.DisposeAsync(); }
        }

        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }

        public async Task BeginTransactionAsync()
        {
            if (_activeTransaction != null) return;
            _transactionalConnection = new SnowflakeDbConnection { ConnectionString = _connectionString };
            await ConnectorRetryPolicy.ForSnowflake(_logger)
                .ExecuteAsync(async ct => await _transactionalConnection.OpenAsync(ct));
            _activeTransaction = await _transactionalConnection.BeginTransactionAsync();
        }

        public async Task CommitAsync()
        {
            if (_activeTransaction == null) return;
            await _activeTransaction.CommitAsync();
            await _activeTransaction.DisposeAsync();
            if (_transactionalConnection != null) await _transactionalConnection.DisposeAsync();
            _activeTransaction = null;
            _transactionalConnection = null;
        }

        public async Task RollbackAsync()
        {
            if (_activeTransaction == null) return;
            await _activeTransaction.RollbackAsync();
            await _activeTransaction.DisposeAsync();
            if (_transactionalConnection != null) await _transactionalConnection.DisposeAsync();
            _activeTransaction = null;
            _transactionalConnection = null;
        }

        public async ValueTask DisposeAsync()
        {
            if (_activeTransaction != null) await RollbackAsync();
            if (_transactionalConnection != null) await _transactionalConnection.DisposeAsync();
            _activeTransaction = null;
            _transactionalConnection = null;
        }

        private async Task<(SnowflakeDbConnection conn, bool isShared)> GetConnectionAsync()
        {
            if (_transactionalConnection != null) return (_transactionalConnection, true);
            var conn = new SnowflakeDbConnection { ConnectionString = _connectionString };
            await ConnectorRetryPolicy.ForSnowflake(_logger)
                .ExecuteAsync(async ct => await conn.OpenAsync(ct));
            return (conn, false);
        }

        private async Task TruncateAsync()
        {
            if (_context != null && _context.IsWhatIf) return;
            await foreach (var _ in ExecuteRawSql($"TRUNCATE TABLE {QuoteIdentifier(_tableName!)}")) { }
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
                p.StartsWith('"') ? p : $"\"{p.Replace("\"", "\"\"")}\""));
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
    }
}
