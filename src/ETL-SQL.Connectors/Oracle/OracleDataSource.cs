using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Shared;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using Oracle.ManagedDataAccess.Client;
using Oracle.ManagedDataAccess.Types;

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
        private readonly ILogger _logger;
        private readonly IExecutionContext? _context;
        private readonly int _commandTimeout;

        public string ConnectionString => _connectionString;
        public string Path => "ORACLE";
        public string Dialect => "ORACLE";
        public bool SupportsSqlPushdown => true;
        public Dictionary<string, string>? Options => _options;
        public string ConnectorType => "ORACLE";

        public OracleDataSource(IExecutionContext context, string connectionString, string? tableName = null, Dictionary<string, string>? options = null)
        {
            _context = context;
            _logger = context.Logger;
            _connectionString = connectionString;
            _tableName = tableName;
            _options = options;
            _commandTimeout = options != null && options.TryGetValue("TIMEOUT_SECONDS", out var ts) && int.TryParse(ts, out var t) && t > 0 ? t : 30;

            // Security Hardening: egress control
            var host = OracleConnector.GetHostStatic(connectionString, options);
            if (host != null) context.SecurityService.ValidateHost(host);
        }

        public IDataSource WithTable(string tableName) => new OracleDataSource(_context!, _connectionString, tableName, _options);

        private async Task<OracleConnection> OpenConnectionAsync()
        {
            var conn = new OracleConnection(_connectionString);
            try
            {
                await ConnectorRetryPolicy.ForOracle(_logger)
                    .ExecuteAsync(async ct => await conn.OpenAsync(ct));
                return conn;
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                conn.Dispose();
                throw ConnectorExceptionWrapper.Wrap("Oracle", ex);
            }
        }

        public async Task<string> GetVersionAsync()
        {
            try
            {
                using var conn = await OpenConnectionAsync();
                using var cmd = CreateCommand("SELECT version FROM v$instance", conn);
                var result = await cmd.ExecuteScalarAsync();
                return result?.ToString() ?? "Unknown Oracle Version";
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("Oracle", ex);
            }
        }

        public HashSet<string> GetSupportedFunctions() => OracleSyntax.Functions;

        public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) =>
            ConnectorExceptionWrapper.WrapAsync(ReadBatchesCore(batchSize), "Oracle", ShouldWrapProviderException);

        private async IAsyncEnumerable<DataTable> ReadBatchesCore(int batchSize)
        {
            if (string.IsNullOrEmpty(_tableName))
                throw new ExecutionException("No table specified for Oracle data source read.");

            using var conn = await OpenConnectionAsync();
            using var cmd = CreateCommand($"SELECT * FROM {QuoteIdentifier(_tableName)}", conn);
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
                var row = new Row();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    row[columns[i]] = reader.IsDBNull(i) ? null : MapOracleValue(reader.GetValue(i));
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

        public async Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false)
        {
            if (string.IsNullOrEmpty(_tableName))
                throw new ExecutionException("No table specified for Oracle data source write.");

            if (!append) await TruncateAsync();

            try
            {
                using var conn = await OpenConnectionAsync();

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
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("Oracle", ex);
            }
        }

        public IAsyncEnumerable<DataTable> ExecuteRawSql(string sql, IEnumerable<object?>? parameters = null) =>
            ConnectorExceptionWrapper.WrapAsync(ExecuteRawSqlCore(sql, parameters), "Oracle", ShouldWrapProviderException);

        private async IAsyncEnumerable<DataTable> ExecuteRawSqlCore(string sql, IEnumerable<object?>? parameters = null)
        {
            using var conn = await OpenConnectionAsync();

            // Oracle ODP.NET does not support multi-statement batches. Split on ';' and execute individually.
            var statements = SplitOracleStatements(sql);
            var paramList = parameters?.ToList() ?? new List<object?>();

            foreach (var stmtSql in statements)
            {
                using var cmd = CreateCommand(stmtSql, conn);
                int paramCount = 0;
                foreach (var param in paramList)
                {
                    var p = new OracleParameter($"p{paramCount++}", param ?? DBNull.Value);
                    if (param is DateTimeOffset)
                    {
                        p.OracleDbType = OracleDbType.TimeStampTZ;
                    }
                    cmd.Parameters.Add(p);
                }
                if (paramCount > 0)
                    cmd.CommandText = ETL_SQL.Core.Common.ParameterUtility.ProcessParameters(cmd.CommandText, ":");

                using var reader = await cmd.ExecuteReaderAsync();
                var columns = new List<string>();
                for (int i = 0; i < reader.FieldCount; i++)
                    columns.Add(reader.GetName(i));

                var resultBatch = new DataTable();
                resultBatch.SetColumns(columns);
                while (await reader.ReadAsync())
                {
                    var row = new Row();
                    for (int i = 0; i < reader.FieldCount; i++)
                        row[columns[i]] = reader.IsDBNull(i) ? null : MapOracleValue(reader.GetValue(i));
                    await resultBatch.AddRowAsync(row);
                }
                resultBatch.RowsAffected = (int)reader.RecordsAffected;
                yield return resultBatch;
            }
        }

        private static IEnumerable<string> SplitOracleStatements(string sql)
        {
            // Split by semicolons outside string literals, skip empty segments.
            var sb = new System.Text.StringBuilder();
            bool inString = false;
            for (int i = 0; i < sql.Length; i++)
            {
                char c = sql[i];
                if (c == '\'' && !inString) { inString = true; sb.Append(c); }
                else if (c == '\'' && inString)
                {
                    sb.Append(c);
                    if (i + 1 < sql.Length && sql[i + 1] == '\'') { sb.Append(sql[++i]); } // escaped ''
                    else inString = false;
                }
                else if (c == ';' && !inString)
                {
                    var stmt = sb.ToString().Trim();
                    if (!string.IsNullOrEmpty(stmt)) yield return stmt;
                    sb.Clear();
                }
                else sb.Append(c);
            }
            var last = sb.ToString().Trim();
            if (!string.IsNullOrEmpty(last)) yield return last;
        }

        public Task<IEnumerable<string>> GetColumnsAsync()
        {
            if (string.IsNullOrEmpty(_tableName)) return Task.FromResult(Enumerable.Empty<string>());
            return GetColumnsAsync(_tableName);
        }

        public async Task<IEnumerable<string>> GetTablesAsync()
        {
            try
            {
                using var conn = await OpenConnectionAsync();
                using var cmd = CreateCommand("SELECT owner, table_name FROM all_tables WHERE owner NOT IN ('SYS')", conn);
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
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("Oracle", ex);
            }
        }

        public async Task<IEnumerable<string>> GetViewsAsync()
        {
            try
            {
                using var conn = await OpenConnectionAsync();
                using var cmd = CreateCommand("SELECT owner, view_name FROM all_views WHERE owner NOT IN ('SYS')", conn);
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
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("Oracle", ex);
            }
        }

        public async Task<IEnumerable<string>> GetColumnsAsync(string tableName)
        {
            try
            {
                using var conn = await OpenConnectionAsync();
                using var cmd = CreateCommand($"SELECT * FROM {QuoteIdentifier(tableName)} WHERE ROWNUM = 0", conn);
                using var reader = await cmd.ExecuteReaderAsync();
                var columns = new List<string>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    columns.Add(reader.GetName(i));
                }
                return columns;
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("Oracle", ex);
            }
        }

        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }

        public async Task TruncateAsync()
        {
            if (string.IsNullOrEmpty(_tableName))
                throw new ExecutionException("No table specified for Oracle truncate.");

            await foreach (var _ in ExecuteRawSql($"TRUNCATE TABLE {QuoteIdentifier(_tableName)}")) { }
        }

        private static string QuoteIdentifier(string name)
        {
            // Oracle stores unquoted identifiers as uppercase; uppercase when quoting to match
            var parts = name.Split('.');
            return string.Join(".", parts.Select(p => $"\"{p.ToUpperInvariant().Replace("\"", "\"\"")}\""));
        }

        public async ValueTask DisposeAsync()
        {
            await Task.CompletedTask;
        }

        private OracleCommand CreateCommand(string sql, OracleConnection conn)
        {
            var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.BindByName = true;
            cmd.CommandTimeout = _commandTimeout;
            return cmd;
        }

        private static object? MapOracleValue(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            
            if (value is OracleTimeStampTZ timestampTz)
                return new DateTimeOffset(
                    DateTime.SpecifyKind(timestampTz.Value, DateTimeKind.Unspecified),
                    timestampTz.GetTimeZoneOffset());
            if (value is OracleTimeStampLTZ timestampLtz)
                return new DateTimeOffset(
                    DateTime.SpecifyKind(timestampLtz.Value, DateTimeKind.Unspecified),
                    OracleTimeStampLTZ.GetLocalTimeZoneOffset());
            if (value is OracleTimeStamp timestamp) return timestamp.Value;
            if (value is OracleDate date) return date.Value;
            var typeName = value.GetType().FullName;
            if (typeName == "Oracle.ManagedDataAccess.Types.OracleDecimal")
            {
                return (decimal)(dynamic)value;
            }
            if (typeName == "Oracle.ManagedDataAccess.Types.OracleIntervalDS")
            {
                return (TimeSpan)(dynamic)value;
            }
            return value;
        }

        private static bool ShouldWrapProviderException(Exception ex) =>
            ex is OracleException or InvalidOperationException;
    }
}
