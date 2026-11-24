using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Shared;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Governance;
using ETL_SQL.Data;
using Npgsql;
using NpgsqlTypes;

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
        private readonly ILogger _logger;
        private readonly IExecutionContext? _context;
        private readonly int _commandTimeout;
        private NpgsqlConnection? _transactionalConnection;
        private NpgsqlTransaction? _activeTransaction;

        /// <summary>
        /// Initializes a new instance of the <see cref="PostgresDataSource"/> class.
        /// </summary>
        /// <param name="connectionString">The PostgreSQL connection string.</param>
        /// <param name="tableName">The target table name (optional for raw SQL).</param>
        /// <param name="options">The options used to create this data source.</param>
        /// <param name="logger">The logger instance.</param>
        public PostgresDataSource(IExecutionContext context, string connectionString, string? tableName = null, Dictionary<string, string>? options = null)
        {
            _context = context;
            _logger = context.Logger;
            if (options != null && options.TryGetValue("SEARCH_PATH", out var searchPath))
            {
                var normalizedSearchPath = DatabaseConnectionStringBuilder.ValidateAndNormalizePostgresSearchPath(searchPath);
                if (!string.IsNullOrWhiteSpace(connectionString))
                {
                    var builder = new NpgsqlConnectionStringBuilder(connectionString);
                    if (string.IsNullOrEmpty(builder.SearchPath))
                    {
                        builder.SearchPath = normalizedSearchPath;
                        connectionString = builder.ConnectionString;
                    }
                }
            }
            _connectionString = connectionString;
            _tableName = tableName;
            _options = options;
            _commandTimeout = ConnectorTimeouts.ResolveCommandTimeoutSeconds(context, options);

            // Security Hardening: egress control
            var host = PostgresConnector.GetHostStatic(connectionString, options);
            if (host != null) ETL_SQL.Core.Governance.ConnectorPolicyAuthorizer.EnforceEnterpriseHost(context, host);
        }

        public string ConnectionString => _connectionString;
        public string Path => "POSTGRES";

        /// <summary>Server and database for lineage labelling. Credentials are never read out.</summary>
        public (string? Server, string? Database) GetLineageLocation()
        {
            try
            {
                var builder = new NpgsqlConnectionStringBuilder(_connectionString);
                return (
                    string.IsNullOrEmpty(builder.Host) ? null : builder.Host,
                    string.IsNullOrEmpty(builder.Database) ? null : builder.Database);
            }
            catch { return (null, null); }
        }

        public string Dialect => "POSTGRES";
        public bool SupportsSqlPushdown => true;
        public Dictionary<string, string>? Options => _options;
        public string ConnectorType => "POSTGRES";
        public ETL_SQL.Data.ICatalogMetadataProvider? GetCatalogProvider() => new PostgresCatalogProvider(_connectionString);

        public IDataSource WithTable(string tableName)
        {
            var ds = new PostgresDataSource(_context!, _connectionString, tableName, _options);
            ds._transactionalConnection = _transactionalConnection;
            ds._activeTransaction = _activeTransaction;
            return ds;
        }

        public async Task<string> GetVersionAsync()
        {
            var (conn, isShared) = await GetConnectionAsync();
            try
            {
                await using var cmd = CreateCommand("SELECT version()", conn);
                if (_activeTransaction != null) cmd.Transaction = _activeTransaction;
                var result = await cmd.ExecuteScalarAsync();
                return result?.ToString() ?? "Unknown PostgreSQL Version";
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("PostgreSQL", ex);
            }
            finally
            {
                if (!isShared) await conn.DisposeAsync();
            }
        }

        public HashSet<string> GetSupportedFunctions() => PostgresSyntax.Functions;

        public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) =>
            ReadBatches(batchSize, CancellationToken.None);

        public IAsyncEnumerable<DataTable> ReadBatches(int batchSize, CancellationToken cancellationToken) =>
            ConnectorExceptionWrapper.WrapAsync(
                ReadBatchesCore(batchSize, cancellationToken),
                "PostgreSQL",
                ShouldWrapProviderException);

        private async IAsyncEnumerable<DataTable> ReadBatchesCore(
            int batchSize,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(_tableName))
                throw new ExecutionException("No table specified for Postgres data source read.");

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

                var noRows = true;
                while (await reader.ReadAsync(effectiveCancellationToken))
                {
                    noRows = false;
                    var row = currentBatch.NewRow();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        row[i] = reader.IsDBNull(i) ? null : MapPostgresValue(reader, i);
                    }
                    await currentBatch.AddRowAsync(row);

                    if (currentBatch.Rows.Count >= batchSize)
                    {
                        yield return currentBatch;
                        currentBatch = new DataTable();
                        currentBatch.SetColumns(columns);
                    }
                }

                // An empty result still names its columns, so an outer join can name an empty side.
                if (currentBatch.Rows.Count > 0 || noRows)
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
                throw new ExecutionException("No table specified for Postgres data source write.");

            if (!append) await TruncateAsync(effectiveCancellationToken);

            var (conn, isShared) = await GetConnectionAsync(effectiveCancellationToken);
            NpgsqlBinaryImporter? importer = null;
            bool completed = false;
            try
            {
                Dictionary<string, (string PgColName, string TypName)>? catalogTypes = null;
                (string BatchCol, string PgCol, string TypName, NpgsqlDbType DbType)[]? resolvedColumns = null;
                long rowOrdinal = 0;

                await foreach (var batch in batches.WithCancellation(effectiveCancellationToken))
                {
                    if (batch.Rows.Count == 0) continue;

                    if (importer == null)
                    {
                        catalogTypes ??= await LoadTargetColumnTypesAsync(conn, _tableName, effectiveCancellationToken);
                        resolvedColumns = ResolveBinaryCopyColumns(batch.ColumnNames, catalogTypes, _tableName);
                        var cols = string.Join(", ", resolvedColumns.Select(c => $"\"{c.PgCol.Replace("\"", "\"\"")}\""));
                        importer = await conn.BeginBinaryImportAsync(
                            $"COPY {QuoteIdentifier(_tableName)} ({cols}) FROM STDIN (FORMAT BINARY)",
                            effectiveCancellationToken);
                    }

                    foreach (var row in batch.Rows)
                    {
                        rowOrdinal++;
                        await importer.StartRowAsync(effectiveCancellationToken);

                        for (int colIdx = 0; colIdx < resolvedColumns!.Length; colIdx++)
                        {
                            var col = resolvedColumns[colIdx];
                            var val = row[col.BatchCol];
                            if (val == null || val == DBNull.Value)
                            {
                                await importer.WriteNullAsync(effectiveCancellationToken);
                                continue;
                            }

                            object converted;
                            try
                            {
                                converted = ConvertValueForBinaryCopy(val, col.DbType);
                            }
                            catch (Exception ex) when (ex is not OperationCanceledException)
                            {
                                throw new ExecutionException(
                                    $"PostgreSQL binary COPY failed at row {rowOrdinal}, column '{col.BatchCol}' (PostgreSQL type '{col.TypName}'): {ex.Message}",
                                    ex);
                            }

                            try
                            {
                                await importer.WriteAsync(converted, col.DbType, effectiveCancellationToken);
                            }
                            catch (Exception ex) when (ex is not OperationCanceledException)
                            {
                                throw new ExecutionException(
                                    $"PostgreSQL binary COPY failed writing row {rowOrdinal}, column '{col.BatchCol}' (PostgreSQL type '{col.TypName}'): {ex.Message}",
                                    ex);
                            }
                        }
                    }
                }

                if (importer != null)
                {
                    await importer.CompleteAsync(effectiveCancellationToken);
                    completed = true;
                }
            }
            catch (ExecutionException) { throw; }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("PostgreSQL", ex);
            }
            finally
            {
                if (importer != null)
                {
                    // If CompleteAsync was not reached, DisposeAsync cancels the COPY stream so nothing is committed.
                    _ = completed;
                    await importer.DisposeAsync();
                }
                if (!isShared) await conn.DisposeAsync();
            }
        }

        /// <summary>
        /// Legacy text-mode COPY loader kept internal for repeatable benchmark comparisons against
        /// <see cref="WriteBatches(IAsyncEnumerable{DataTable}, bool, CancellationToken)"/>.
        /// </summary>
        internal async Task WriteBatchesTextCopyAsync(IAsyncEnumerable<DataTable> batches, bool append = false, CancellationToken cancellationToken = default)
        {
            var effectiveCancellationToken = EffectiveCancellationToken(cancellationToken);
            if (_context != null && _context.IsWhatIf) return;
            if (string.IsNullOrEmpty(_tableName))
                throw new ExecutionException("No table specified for Postgres data source write.");

            if (!append) await TruncateAsync(effectiveCancellationToken);

            var (conn, isShared) = await GetConnectionAsync(effectiveCancellationToken);
            System.IO.TextWriter? writer = null;
            try
            {
                await foreach (var batch in batches.WithCancellation(effectiveCancellationToken))
                {
                    if (batch.Rows.Count == 0) continue;

                    if (writer == null)
                    {
                        var cols = string.Join(", ", batch.ColumnNames.Select(c => $"\"{c.Replace("\"", "\"\"")}\""));
                        writer = await conn.BeginTextImportAsync(
                            $"COPY {QuoteIdentifier(_tableName)} ({cols}) FROM STDIN",
                            effectiveCancellationToken);
                    }

                    foreach (var row in batch.Rows)
                    {
                        var values = batch.ColumnNames.Select(key =>
                        {
                            var val = row[key];
                            if (val == null || val == DBNull.Value) return "\\N";
                            var s = FormatCopyValue(val);
                            return EscapeCopyText(s);
                        });
                        await writer.WriteLineAsync(string.Join("\t", values).AsMemory(), effectiveCancellationToken);
                    }
                }
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("PostgreSQL", ex);
            }
            finally
            {
                if (writer != null) await writer.DisposeAsync();
                if (!isShared) await conn.DisposeAsync();
            }
        }

        private async Task<Dictionary<string, (string PgColName, string TypName)>> LoadTargetColumnTypesAsync(
            NpgsqlConnection conn,
            string tableName,
            CancellationToken cancellationToken)
        {
            const string sql = @"
                SELECT a.attname,
                       CASE WHEN t.typtype = 'd' AND bt.typname IS NOT NULL THEN bt.typname ELSE t.typname END AS typname
                FROM pg_attribute a
                JOIN pg_type t ON a.atttypid = t.oid
                LEFT JOIN pg_type bt ON t.typbasetype = bt.oid
                WHERE a.attrelid = to_regclass(@regclass)
                  AND a.attnum > 0
                  AND NOT a.attisdropped";

            var result = new Dictionary<string, (string PgColName, string TypName)>(StringComparer.Ordinal);
            await using var cmd = CreateCommand(sql, conn);
            if (_activeTransaction != null) cmd.Transaction = _activeTransaction;
            cmd.Parameters.AddWithValue("regclass", QuoteIdentifier(tableName));

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var colName = reader.GetString(0);
                var typName = reader.GetString(1);
                result[colName] = (colName, typName);
            }

            if (result.Count == 0)
                throw new ExecutionException($"PostgreSQL binary COPY failed: target table '{tableName}' was not found.");

            return result;
        }

        private static (string BatchCol, string PgCol, string TypName, NpgsqlDbType DbType)[] ResolveBinaryCopyColumns(
            IReadOnlyList<string> batchColumns,
            Dictionary<string, (string PgColName, string TypName)> catalogTypes,
            string tableName)
        {
            var ciLookup = new Dictionary<string, (string PgColName, string TypName)>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in catalogTypes)
                ciLookup.TryAdd(kvp.Key, kvp.Value);

            var resolved = new (string BatchCol, string PgCol, string TypName, NpgsqlDbType DbType)[batchColumns.Count];
            for (int i = 0; i < batchColumns.Count; i++)
            {
                var col = batchColumns[i];
                if (!catalogTypes.TryGetValue(col, out var info) && !ciLookup.TryGetValue(col, out info))
                {
                    throw new ExecutionException(
                        $"PostgreSQL binary COPY failed: column '{col}' does not exist on target table '{tableName}'.");
                }

                if (!TryMapPostgresTypeToNpgsqlDbType(info.TypName, out var dbType))
                {
                    throw new ExecutionException(
                        $"PostgreSQL binary COPY does not support column '{col}' of PostgreSQL type '{info.TypName}' on table '{tableName}'.");
                }

                resolved[i] = (col, info.PgColName, info.TypName, dbType);
            }
            return resolved;
        }

        internal static bool TryMapPostgresTypeToNpgsqlDbType(string pgTypeName, out NpgsqlDbType dbType)
        {
            switch (pgTypeName.ToLowerInvariant())
            {
                case "int2": dbType = NpgsqlDbType.Smallint; return true;
                case "int4": dbType = NpgsqlDbType.Integer; return true;
                case "int8": dbType = NpgsqlDbType.Bigint; return true;
                case "numeric":
                case "decimal": dbType = NpgsqlDbType.Numeric; return true;
                case "float4": dbType = NpgsqlDbType.Real; return true;
                case "float8": dbType = NpgsqlDbType.Double; return true;
                case "money": dbType = NpgsqlDbType.Money; return true;
                case "bool": dbType = NpgsqlDbType.Boolean; return true;
                case "text": dbType = NpgsqlDbType.Text; return true;
                case "varchar": dbType = NpgsqlDbType.Varchar; return true;
                case "bpchar":
                case "char": dbType = NpgsqlDbType.Char; return true;
                case "name": dbType = NpgsqlDbType.Name; return true;
                case "citext": dbType = NpgsqlDbType.Citext; return true;
                case "json": dbType = NpgsqlDbType.Json; return true;
                case "jsonb": dbType = NpgsqlDbType.Jsonb; return true;
                case "xml": dbType = NpgsqlDbType.Xml; return true;
                case "uuid": dbType = NpgsqlDbType.Uuid; return true;
                case "bytea": dbType = NpgsqlDbType.Bytea; return true;
                case "date": dbType = NpgsqlDbType.Date; return true;
                case "timestamp": dbType = NpgsqlDbType.Timestamp; return true;
                case "timestamptz": dbType = NpgsqlDbType.TimestampTz; return true;
                case "time": dbType = NpgsqlDbType.Time; return true;
                case "interval": dbType = NpgsqlDbType.Interval; return true;
                default:
                    dbType = default;
                    return false;
            }
        }

        internal static object ConvertValueForBinaryCopy(object value, NpgsqlDbType dbType) => dbType switch
        {
            NpgsqlDbType.Smallint => Convert.ToInt16(value, CultureInfo.InvariantCulture),
            NpgsqlDbType.Integer => Convert.ToInt32(value, CultureInfo.InvariantCulture),
            NpgsqlDbType.Bigint => Convert.ToInt64(value, CultureInfo.InvariantCulture),
            NpgsqlDbType.Numeric or NpgsqlDbType.Money => Convert.ToDecimal(value, CultureInfo.InvariantCulture),
            NpgsqlDbType.Real => Convert.ToSingle(value, CultureInfo.InvariantCulture),
            NpgsqlDbType.Double => Convert.ToDouble(value, CultureInfo.InvariantCulture),
            NpgsqlDbType.Boolean => value switch
            {
                bool b => b,
                string s when s.Equals("true", StringComparison.OrdinalIgnoreCase) || s.Equals("t", StringComparison.OrdinalIgnoreCase) || s == "1" => true,
                string s when s.Equals("false", StringComparison.OrdinalIgnoreCase) || s.Equals("f", StringComparison.OrdinalIgnoreCase) || s == "0" => false,
                string s => throw new FormatException($"Cannot convert '{s}' to boolean."),
                _ => Convert.ToBoolean(value, CultureInfo.InvariantCulture)
            },
            NpgsqlDbType.Text or NpgsqlDbType.Varchar or NpgsqlDbType.Char or NpgsqlDbType.Name or NpgsqlDbType.Citext or NpgsqlDbType.Json or NpgsqlDbType.Jsonb or NpgsqlDbType.Xml =>
                Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
            NpgsqlDbType.Uuid => value switch
            {
                Guid g => g,
                string s => Guid.Parse(s),
                _ => throw new InvalidCastException($"Cannot convert {value.GetType().Name} to uuid.")
            },
            NpgsqlDbType.Bytea => value switch
            {
                byte[] bytes => bytes,
                ReadOnlyMemory<byte> mem => mem.ToArray(),
                _ => throw new InvalidCastException($"Cannot convert {value.GetType().Name} to bytea.")
            },
            NpgsqlDbType.Date => value switch
            {
                DateOnly d => d,
                DateTime dt => DateOnly.FromDateTime(dt),
                DateTimeOffset dto => DateOnly.FromDateTime(dto.DateTime),
                string s => DateOnly.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                    ? d
                    : DateOnly.FromDateTime(DateTime.Parse(s, CultureInfo.InvariantCulture)),
                _ => throw new InvalidCastException($"Cannot convert {value.GetType().Name} to date.")
            },
            NpgsqlDbType.Timestamp => value switch
            {
                DateTime dt => DateTime.SpecifyKind(dt, DateTimeKind.Unspecified),
                DateTimeOffset dto => DateTime.SpecifyKind(dto.DateTime, DateTimeKind.Unspecified),
                DateOnly d => d.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified),
                string s => DateTime.SpecifyKind(DateTime.Parse(s, CultureInfo.InvariantCulture), DateTimeKind.Unspecified),
                _ => throw new InvalidCastException($"Cannot convert {value.GetType().Name} to timestamp.")
            },
            NpgsqlDbType.TimestampTz => value switch
            {
                DateTimeOffset dto => dto.UtcDateTime,
                DateTime dt => dt.Kind == DateTimeKind.Utc ? dt : (dt.Kind == DateTimeKind.Local ? dt.ToUniversalTime() : DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
                string s => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal).UtcDateTime,
                _ => throw new InvalidCastException($"Cannot convert {value.GetType().Name} to timestamptz.")
            },
            NpgsqlDbType.Time => value switch
            {
                TimeOnly t => t,
                TimeSpan ts => TimeOnly.FromTimeSpan(ts),
                string s => TimeOnly.Parse(s, CultureInfo.InvariantCulture),
                _ => throw new InvalidCastException($"Cannot convert {value.GetType().Name} to time.")
            },
            NpgsqlDbType.Interval => value switch
            {
                TimeSpan ts => ts,
                NpgsqlInterval iv => iv,
                string s => TimeSpan.Parse(s, CultureInfo.InvariantCulture),
                _ => throw new InvalidCastException($"Cannot convert {value.GetType().Name} to interval.")
            },
            _ => value
        };

        public IAsyncEnumerable<DataTable> ExecuteRawSql(string sql, IEnumerable<object?>? parameters = null) =>
            ExecuteRawSql(sql, parameters, CancellationToken.None);

        public IAsyncEnumerable<DataTable> ExecuteRawSql(
            string sql,
            IEnumerable<object?>? parameters,
            CancellationToken cancellationToken) =>
            ConnectorExceptionWrapper.WrapAsync(
                ExecuteRawSqlCore(sql, parameters, cancellationToken),
                "PostgreSQL",
                ShouldWrapProviderException);

        private async IAsyncEnumerable<DataTable> ExecuteRawSqlCore(
            string sql,
            IEnumerable<object?>? parameters = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var effectiveCancellationToken = EffectiveCancellationToken(cancellationToken);
            var (conn, isShared) = await GetConnectionAsync(effectiveCancellationToken);

            void NoticeHandler(object sender, NpgsqlNoticeEventArgs e)
            {
                _logger.WriteLine(e.Notice.MessageText, ConsoleColor.Cyan);
            }

            conn.Notice += NoticeHandler;

            try
            {
                await using var cmd = CreateCommand(sql, conn);
                if (_activeTransaction != null) cmd.Transaction = _activeTransaction;

                int paramCount = 0;
                if (parameters != null)
                {
                    foreach (var param in parameters)
                    {
                        var pName = $"p{paramCount++}";
                        if (param is string strVal)
                        {
                            cmd.Parameters.Add(new NpgsqlParameter(pName, NpgsqlTypes.NpgsqlDbType.Unknown) { Value = strVal });
                        }
                        else if (param is DateTimeOffset dto)
                        {
                            cmd.Parameters.Add(new NpgsqlParameter(pName, NpgsqlTypes.NpgsqlDbType.TimestampTz)
                            {
                                Value = NormalizeDateTimeOffset(dto)
                            });
                        }
                        else
                        {
                            cmd.Parameters.AddWithValue(pName, param ?? DBNull.Value);
                        }
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
                            row[i] = reader.IsDBNull(i) ? null : MapPostgresValue(reader, i);
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
                conn.Notice -= NoticeHandler;
                if (!isShared) await conn.DisposeAsync();
            }
        }

        private static object MapPostgresValue(NpgsqlDataReader reader, int ordinal)
        {
            var value = reader.GetValue(ordinal);
            var providerType = reader.GetDataTypeName(ordinal);
            if (providerType.Equals("timestamp with time zone", StringComparison.OrdinalIgnoreCase)
                || providerType.Equals("timestamptz", StringComparison.OrdinalIgnoreCase))
            {
                if (value is DateTime dateTime)
                    return new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc));
                if (value is DateTimeOffset offset) return offset.ToUniversalTime();
            }
            return value;
        }

        // COPY text format treats backslash as an escape: \N is NULL and \t, \n, \r are control
        // characters. Escape the backslash first, then the delimiter and line breaks, so every value
        // (a Windows path, text with tabs or newlines, a literal \N) arrives exactly as written.
        internal static string EscapeCopyText(string value) => value
            .Replace("\\", "\\\\")
            .Replace("\t", "\\t")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r");

        private static string FormatCopyValue(object value) => value switch
        {
            DateTimeOffset offset => offset.UtcDateTime.ToString(
                "yyyy-MM-dd HH:mm:ss.ffffff+00", System.Globalization.CultureInfo.InvariantCulture),
            DateOnly dateOnly => dateOnly.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            DateTime dateTime => dateTime.ToString("yyyy-MM-dd HH:mm:ss.ffffff",
                System.Globalization.CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };

        private static DateTime NormalizeDateTimeOffset(DateTimeOffset value) => value.UtcDateTime;

        public Task<IEnumerable<string>> GetColumnsAsync()
            => GetColumnsAsync(CancellationToken.None);

        public async Task<IEnumerable<string>> GetColumnsAsync(CancellationToken cancellationToken)
        {
            var effectiveCancellationToken = EffectiveCancellationToken(cancellationToken);
            if (string.IsNullOrEmpty(_tableName)) return Enumerable.Empty<string>();

            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync(effectiveCancellationToken);
                await using var cmd = CreateCommand($"SELECT * FROM {QuoteIdentifier(_tableName)} LIMIT 0", conn);
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
                throw ConnectorExceptionWrapper.Wrap("PostgreSQL", ex);
            }
        }

        public Task<IEnumerable<string>> GetTablesAsync()
            => GetTablesAsync(CancellationToken.None);

        public async Task<IEnumerable<string>> GetTablesAsync(CancellationToken cancellationToken)
        {
            var effectiveCancellationToken = EffectiveCancellationToken(cancellationToken);
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync(effectiveCancellationToken);
                await using var cmd = CreateCommand("SELECT table_schema, table_name FROM information_schema.tables WHERE table_schema NOT IN ('information_schema', 'pg_catalog') AND table_type = 'BASE TABLE'", conn);
                await using var reader = await cmd.ExecuteReaderAsync(effectiveCancellationToken);
                var tables = new List<string>();
                while (await reader.ReadAsync(effectiveCancellationToken))
                {
                    var schema = reader.GetString(0);
                    var table = reader.GetString(1);
                    tables.Add(schema == "public" ? table : $"{schema}.{table}");
                }
                return tables.Distinct();
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("PostgreSQL", ex);
            }
        }

        public Task<IEnumerable<string>> GetViewsAsync()
            => GetViewsAsync(CancellationToken.None);

        public async Task<IEnumerable<string>> GetViewsAsync(CancellationToken cancellationToken)
        {
            var effectiveCancellationToken = EffectiveCancellationToken(cancellationToken);
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync(effectiveCancellationToken);
                await using var cmd = CreateCommand("SELECT table_schema, table_name FROM information_schema.tables WHERE table_schema NOT IN ('information_schema', 'pg_catalog') AND table_type = 'VIEW'", conn);
                await using var reader = await cmd.ExecuteReaderAsync(effectiveCancellationToken);
                var views = new List<string>();
                while (await reader.ReadAsync(effectiveCancellationToken))
                {
                    var schema = reader.GetString(0);
                    var table = reader.GetString(1);
                    views.Add(schema == "public" ? table : $"{schema}.{table}");
                }
                return views.Distinct();
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("PostgreSQL", ex);
            }
        }

        public Task<IEnumerable<string>> GetColumnsAsync(string tableName)
            => GetColumnsAsync(tableName, CancellationToken.None);

        public async Task<IEnumerable<string>> GetColumnsAsync(string tableName, CancellationToken cancellationToken)
        {
            var effectiveCancellationToken = EffectiveCancellationToken(cancellationToken);
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync(effectiveCancellationToken);
                await using var cmd = CreateCommand($"SELECT * FROM {QuoteIdentifier(tableName)} LIMIT 0", conn);
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
                throw ConnectorExceptionWrapper.Wrap("PostgreSQL", ex);
            }
        }

        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }

        public async Task BeginTransactionAsync()
        {
            if (_activeTransaction != null) return;
            var conn = new NpgsqlConnection(_connectionString);
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
                throw ConnectorExceptionWrapper.Wrap("PostgreSQL", ex);
            }
        }

        /// <summary>
        /// Starts the transaction that carries asserted viewer values and installs them with
        /// parameterized set_config(..., true). PostgreSQL still authenticates the connection's
        /// service credential; this method never executes SET ROLE or derives a role from claims.
        /// </summary>
        public async Task BeginVerifiedViewerContextAsync(
            VerifiedViewerContext viewerContext,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(viewerContext);
            if (_activeTransaction is not null)
                throw new ExecutionException("Verified viewer context requires a fresh PostgreSQL transaction.");

            var conn = new NpgsqlConnection(_connectionString);
            try
            {
                await conn.OpenAsync(cancellationToken);
                _transactionalConnection = conn;
                _activeTransaction = await conn.BeginTransactionAsync(cancellationToken);

                await using (var identityCommand = CreateCommand("SELECT session_user", conn))
                {
                    identityCommand.Transaction = _activeTransaction;
                    var authenticatedIdentity = Convert.ToString(
                        await identityCommand.ExecuteScalarAsync(cancellationToken));
                    if (!string.Equals(authenticatedIdentity, viewerContext.ExecutingCredentialId,
                        StringComparison.Ordinal))
                    {
                        throw new ExecutionException(
                            "The PostgreSQL identity does not match the viewer context executing credential.");
                    }
                }

                var values = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["etlsql.viewer_id"] = viewerContext.ViewerId,
                    ["etlsql.real_viewer_id"] = viewerContext.RealViewerId,
                    ["etlsql.executing_credential"] = viewerContext.ExecutingCredentialId,
                    ["etlsql.tenant_id"] = viewerContext.TenantId,
                    ["etlsql.resource_id"] = viewerContext.ResourceId,
                    ["etlsql.operation_id"] = viewerContext.OperationId
                };
                foreach (var (key, value) in viewerContext.Claims)
                    values[$"etlsql.claim_{key.ToLowerInvariant().Replace('-', '_')}"] = value;

                foreach (var (name, value) in values)
                {
                    await using var command = CreateCommand("SELECT set_config(@name, @value, true)", conn);
                    command.Transaction = _activeTransaction;
                    command.Parameters.AddWithValue("name", name);
                    command.Parameters.AddWithValue("value", value);
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                try
                {
                    if (_activeTransaction is not null)
                        await _activeTransaction.RollbackAsync(CancellationToken.None);
                }
                finally
                {
                    if (_activeTransaction is not null) await _activeTransaction.DisposeAsync();
                    await conn.DisposeAsync();
                    _activeTransaction = null;
                    _transactionalConnection = null;
                }
                if (ShouldWrapProviderException(ex))
                    throw ConnectorExceptionWrapper.Wrap("PostgreSQL", ex);
                throw;
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
                throw ConnectorExceptionWrapper.Wrap("PostgreSQL", ex);
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
                throw ConnectorExceptionWrapper.Wrap("PostgreSQL", ex);
            }
            finally
            {
                await _activeTransaction.DisposeAsync();
                if (_transactionalConnection != null) await _transactionalConnection.DisposeAsync();
                _activeTransaction = null;
                _transactionalConnection = null;
            }
        }

        private async Task<(NpgsqlConnection, bool isShared)> GetConnectionAsync(CancellationToken cancellationToken = default)
        {
            if (_transactionalConnection != null) return (_transactionalConnection, true);
            var conn = new NpgsqlConnection(_connectionString);
            try
            {
                await ConnectorRetryPolicy.ForPostgres(_logger)
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
                throw ConnectorExceptionWrapper.Wrap("PostgreSQL", ex);
            }
        }

        public async Task TruncateAsync()
            => await TruncateAsync(CancellationToken.None);

        private async Task TruncateAsync(CancellationToken cancellationToken)
        {
            if (_context != null && _context.IsWhatIf) return;
            if (string.IsNullOrEmpty(_tableName))
                throw new ExecutionException("No table specified for Postgres truncate.");

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
                if (p.StartsWith('"') && p.EndsWith('"') && p.Length >= 2)
                {
                    var unquoted = p.Substring(1, p.Length - 2).Replace("\"", "\"\"");
                    return $"\"{unquoted}\"";
                }
                // For Postgres, we only quote if the identifier contains special characters 
                // that REQUIRE quoting. This prevents accidental case-sensitivity issues.
                bool needsQuoting = p.Any(c => !char.IsLetterOrDigit(c) && c != '_');
                return needsQuoting ? $"\"{p.Replace("\"", "\"\"")}\"" : p;
            }));
        }

        public async ValueTask DisposeAsync()
        {
            if (_activeTransaction != null) await RollbackAsync();
            if (_transactionalConnection != null) await _transactionalConnection.DisposeAsync();
            _activeTransaction = null;
            _transactionalConnection = null;
        }

        private NpgsqlCommand CreateCommand(string sql, NpgsqlConnection conn)
        {
            var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = _commandTimeout;
            return cmd;
        }

        private CancellationToken EffectiveCancellationToken(CancellationToken cancellationToken) =>
            cancellationToken.CanBeCanceled ? cancellationToken : (_context?.CancellationToken ?? CancellationToken.None);

        private static bool ShouldWrapProviderException(Exception ex) =>
            ex is NpgsqlException or InvalidOperationException;
    }
}
