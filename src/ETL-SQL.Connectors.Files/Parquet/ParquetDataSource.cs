using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Shared;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;

using Parquet;
using Parquet.Data;
using Parquet.Schema;

namespace ETL_SQL.Connectors.Parquet
{
    /// <summary>
    /// Data source implementation for Apache Parquet files.
    /// Supports high-performance columnar reading and writing.
    /// </summary>
    public class ParquetDataSource : IDatabaseSource
    {
        private readonly string _filePath;
        private readonly string _compression;
        private readonly EncryptionOptions _encryption;
        private readonly Dictionary<string, string>? _options;
        private readonly ILogger _logger;
        private readonly IExecutionContext? _context;
        private readonly bool _transactional = false;

        public string Path => _filePath;
        public Dictionary<string, string>? Options => _options;

        public IDataSource WithTable(string tableName) => this;
        public string ConnectorType => "PARQUET";

        public ParquetDataSource(IExecutionContext context, string filePath, Dictionary<string, string>? options = null)
        {
            _context = context;
            _logger = context.Logger;

            _options = options;
            _compression = options != null && options.TryGetValue("COMPRESSION", out var c) ? c.ToUpperInvariant() : "SNAPPY";
            _encryption = new EncryptionOptions(options);

            var resolvedPath = context.ResolvePath(filePath.Trim('\'', '\"', ' ', '\t', '\r', '\n'));
            _filePath = FileConnectorPathHelper.CoerceFilePathExtension(resolvedPath, _encryption.Enabled, false);

            // Security Hardening: Defense in depth
            context.SecurityService.ValidatePath(_filePath);
            context.SecurityService.ValidateFileType(_filePath, context.AllowUnknownFileTypes);

            if (options != null && options.TryGetValue("TRANSACTIONAL", out var tx))
            {
                _transactional = tx.ToUpperInvariant() == "ON" || tx.ToUpperInvariant() == "TRUE";
            }
        }

        public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) =>
            ReadBatches(batchSize, CancellationToken.None);

        public IAsyncEnumerable<DataTable> ReadBatches(int batchSize, CancellationToken cancellationToken) =>
            ConnectorExceptionWrapper.WrapAsync(ReadBatchesCore(batchSize, cancellationToken), "Parquet", ex => ex is not ExecutionException);

        private async IAsyncEnumerable<DataTable> ReadBatchesCore(
            int batchSize,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var effectiveCancellationToken = EffectiveCancellationToken(cancellationToken);
            ETL_SQL.Core.Common.FileConnectorPathHelper.AuthorizeRead(_context, _filePath);
            if (!System.IO.File.Exists(_filePath)) yield break;

            string effectivePath = _filePath;
            string? tempFile = null;

            if (_encryption.Enabled)
            {
                effectiveCancellationToken.ThrowIfCancellationRequested();
                tempFile = System.IO.Path.GetTempFileName();
                try
                {
                    await _encryption.DecryptFileAsync(_filePath, tempFile, effectiveCancellationToken);
                    effectivePath = tempFile;
                }
                catch
                {
                    TempFileHelper.SafeDelete(tempFile, _logger);
                    throw;
                }
            }

            try
            {
                using var stream = System.IO.File.OpenRead(effectivePath);
                await using var reader = await ParquetReader.CreateAsync(stream);
                var dataFields = reader.Schema.GetDataFields();
                var colNames = dataFields.Select(f => f.Name).ToList();

                for (int i = 0; i < reader.RowGroupCount; i++)
                {
                    effectiveCancellationToken.ThrowIfCancellationRequested();
                    using var rgReader = reader.OpenRowGroupReader(i);
                    int rowCount = (int)rgReader.RowCount;

                    var columns = new object?[dataFields.Length][];
                    for (int j = 0; j < dataFields.Length; j++)
                    {
                        columns[j] = await ReadColumnAsObjectsAsync(rgReader, dataFields[j], rowCount, effectiveCancellationToken);
                    }

                    DataTable? currentBatch = null;

                    for (int r = 0; r < rowCount; r++)
                    {
                        effectiveCancellationToken.ThrowIfCancellationRequested();
                        if (currentBatch == null)
                        {
                            currentBatch = new DataTable();
                            currentBatch.SetColumns(colNames);
                        }

                        var etlRow = currentBatch.NewRow();
                        for (int c = 0; c < dataFields.Length; c++)
                        {
                            etlRow[colNames[c]] = columns[c][r];
                        }
                        await currentBatch.AddRowAsync(etlRow);

                        if (currentBatch.Rows.Count >= batchSize)
                        {
                            yield return currentBatch;
                            currentBatch = null;
                        }
                    }

                    if (currentBatch != null && currentBatch.Rows.Count > 0)
                    {
                        yield return currentBatch;
                    }
                }
            }
            finally
            {
                TempFileHelper.SafeDelete(tempFile, _logger);
            }
        }

        public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) =>
            WriteBatches(batches, append, CancellationToken.None);

        public async Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append, CancellationToken cancellationToken)
        {
            ETL_SQL.Core.Common.FileConnectorPathHelper.AuthorizeWrite(_context, _filePath);
            try
            {
                await WriteBatchesCore(batches, append, EffectiveCancellationToken(cancellationToken));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ParquetException or InvalidDataException)
            {
                throw ConnectorExceptionWrapper.Wrap("Parquet", ex);
            }
        }

        private async Task WriteBatchesCore(IAsyncEnumerable<DataTable> batches, bool append, CancellationToken effectiveCancellationToken)
        {
            await using var enumerator = batches.GetAsyncEnumerator(effectiveCancellationToken);
            if (!await enumerator.MoveNextAsync())
            {
                _logger.Debug("[ParquetDataSource.WriteBatches] Received empty batch stream for '{FilePath}'. No file will be created.", _filePath);
                return;
            }
            _logger.Debug("[ParquetDataSource.WriteBatches] Starting write to '{FilePath}'...", _filePath);

            var firstBatch = enumerator.Current;
            var colNames = firstBatch.ColumnNames;
            if (!colNames.Any() && firstBatch.Rows.Count > 0)
            {
                colNames = firstBatch.Rows[0].Columns.Keys.ToList();
            }

            string targetPath = ETL_SQL.Core.Common.FileConnectorPathHelper.GetStagingFilePath(_context, _filePath, _transactional);
            string? publicationFile = null;

            var dir = System.IO.Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);

            try
            {
                // INSERT INTO a Parquet file arrives here with append: true. The existing file is
                // staged as plain Parquet and the new rows are added as row groups, in its schema.
                bool appendToExisting = append && System.IO.File.Exists(_filePath);
                ParquetSchema schema;
                if (appendToExisting)
                {
                    if (_encryption.Enabled)
                        await _encryption.DecryptFileAsync(_filePath, targetPath, effectiveCancellationToken);
                    else
                        System.IO.File.Copy(_filePath, targetPath, overwrite: true);
                    schema = await ReadAppendableSchemaAsync(targetPath, colNames, effectiveCancellationToken);
                }
                else
                {
                    schema = new ParquetSchema(colNames.Select(col => (Field)CreateField(col, InferColumnType(firstBatch.Rows, col))).ToList());
                }

                var options = new ParquetOptions();
                if (Enum.TryParse<CompressionMethod>(_compression, true, out var comp))
                    options.CompressionMethod = comp;

                using (var stream = appendToExisting
                    ? new FileStream(targetPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)
                    : System.IO.File.Create(targetPath))
                await using (var writer = await ParquetWriter.CreateAsync(schema, stream, options, appendToExisting, effectiveCancellationToken))
                {
                    bool hasMore = true;
                    DataTable batch = firstBatch;

                    while (hasMore)
                    {
                        effectiveCancellationToken.ThrowIfCancellationRequested();
                        using (var rgWriter = writer.CreateRowGroup())
                        {
                            var dataFields = schema.GetDataFields();
                            for (int i = 0; i < dataFields.Length; i++)
                            {
                                await WriteColumnToGroupAsync(rgWriter, dataFields[i], batch.Rows, effectiveCancellationToken);
                            }
                        }
                        hasMore = await enumerator.MoveNextAsync();
                        if (hasMore) batch = enumerator.Current;
                    }
                }

                string fileToPublish = targetPath;
                if (_encryption.Enabled)
                {
                    effectiveCancellationToken.ThrowIfCancellationRequested();
                    publicationFile = ETL_SQL.Core.Common.FileConnectorPathHelper.GetStagingFilePath(_context, _filePath, _transactional);
                    await _encryption.EncryptFileAsync(targetPath, publicationFile, effectiveCancellationToken);
                    fileToPublish = publicationFile;
                }

                effectiveCancellationToken.ThrowIfCancellationRequested();
                ETL_SQL.Core.Common.FileConnectorPathHelper.PublishStagedFile(fileToPublish, _filePath, _transactional);
            }
            finally
            {
                TempFileHelper.SafeDelete(publicationFile, _logger);
                TempFileHelper.SafeDelete(targetPath, _logger);
            }
        }

        public async Task TruncateAsync()
        {
            // Deleting the file is a write: the same guard WriteBatches applies.
            ETL_SQL.Core.Common.FileConnectorPathHelper.AuthorizeWrite(_context, _filePath);
            if (System.IO.File.Exists(_filePath))
            {
                System.IO.File.Delete(_filePath);
            }
            await Task.CompletedTask;
        }

        private static async Task<object?[]> ReadColumnAsObjectsAsync(
            ParquetRowGroupReader rgReader,
            DataField field,
            int rowCount,
            CancellationToken cancellationToken)
        {
            var rawData = await rgReader.ReadRawColumnDataBaseAsync(field, cancellationToken);
            var prop = rawData.GetType().GetProperty("NullableValues");
            if (prop != null)
            {
                try
                {
                    var seq = (System.Collections.IEnumerable)prop.GetValue(rawData)!;
                    var result = new object?[rowCount];
                    int idx = 0;
                    foreach (var v in seq)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (idx >= rowCount) break;
                        result[idx++] = v;
                    }
                    return result;
                }
                catch (InvalidOperationException)
                {
                    // Column has no definition levels (non-nullable); fall through to typed read
                }
            }
            return await ReadNonNullableColumnAsync(rgReader, field, rowCount, cancellationToken);
        }

        /// <summary>
        /// The column type for a new file: every non-null value in the first batch is considered,
        /// not just row 0, so a leading null no longer makes a numeric column a string. Mixed
        /// integer and decimal values widen to decimal; integer or decimal with double widen to
        /// double; anything else mixed is written as text.
        /// </summary>
        internal static Type InferColumnType(IEnumerable<Row> rows, string column)
        {
            Type? inferred = null;
            foreach (var row in rows)
            {
                var value = row[column];
                if (value is null || value is DBNull) continue;
                Type kind = value switch
                {
                    int or long or short or byte or sbyte or uint or ushort => typeof(long),
                    decimal => typeof(decimal),
                    double or float => typeof(double),
                    bool => typeof(bool),
                    DateTime => typeof(DateTime),
                    _ => typeof(string)
                };
                if (inferred is null || inferred == kind) { inferred = kind; continue; }
                if (IsNumeric(inferred) && IsNumeric(kind))
                    inferred = inferred == typeof(double) || kind == typeof(double) ? typeof(double) : typeof(decimal);
                else
                    return typeof(string);
            }
            return inferred ?? typeof(string);

            static bool IsNumeric(Type t) => t == typeof(long) || t == typeof(decimal) || t == typeof(double);
        }

        private static DataField CreateField(string name, Type type) =>
            type == typeof(long) ? new DataField<long>(name, nullable: true)
            : type == typeof(decimal) ? new DataField<decimal>(name, nullable: true)
            : type == typeof(double) ? new DataField<double>(name, nullable: true)
            : type == typeof(bool) ? new DataField<bool>(name, nullable: true)
            : type == typeof(DateTime) ? new DataField<DateTime>(name, nullable: true)
            : new DataField<string>(name, nullable: true);

        private static readonly HashSet<Type> AppendableTypes =
            [typeof(long), typeof(decimal), typeof(double), typeof(bool), typeof(DateTime), typeof(string)];

        /// <summary>
        /// The existing file's schema, when this connector can add row groups to it: flat nullable
        /// columns of the types it writes, holding every incoming column. Anything else is refused,
        /// because writing the rows another way would change or drop data.
        /// </summary>
        private static async Task<ParquetSchema> ReadAppendableSchemaAsync(
            string path, IReadOnlyCollection<string> incomingColumns, CancellationToken cancellationToken)
        {
            ParquetSchema schema;
            await using (var stream = System.IO.File.OpenRead(path))
            await using (var reader = await ParquetReader.CreateAsync(stream, cancellationToken: cancellationToken))
            {
                schema = reader.Schema;
            }

            var dataFields = schema.GetDataFields();
            if (dataFields.Length != schema.Fields.Count)
                throw new ExecutionException("Cannot append to this Parquet file: it has nested columns. Write to a new file instead.");
            foreach (var field in dataFields)
            {
                if ((field.ClrType != typeof(string) && !field.IsNullable) || field.IsArray || !AppendableTypes.Contains(field.ClrType))
                    throw new ExecutionException(
                        $"Cannot append to this Parquet file: column '{field.Name}' has a type or nullability this connector does not write. Write to a new file instead.");
            }
            var missing = incomingColumns.Where(c => !dataFields.Any(f => string.Equals(f.Name, c, StringComparison.Ordinal))).ToList();
            if (missing.Count > 0)
                throw new ExecutionException(
                    $"Cannot append to this Parquet file: it has no column {string.Join(", ", missing.Select(m => $"'{m}'"))}.");
            return schema;
        }

        private static long ToInt64Exact(object value, string column)
        {
            if (value is decimal d && d != decimal.Truncate(d)
                || value is double dbl && dbl != Math.Truncate(dbl)
                || value is float flt && flt != MathF.Truncate(flt))
                throw new ExecutionException(
                    $"Parquet column '{column}' is an integer column; value {value} has a fractional part and would be rounded.");
            return Convert.ToInt64(value);
        }

        private static Task<object?[]> ReadNonNullableColumnAsync(
            ParquetRowGroupReader rgReader,
            DataField field,
            int rowCount,
            CancellationToken cancellationToken)
        {
            var clrType = Nullable.GetUnderlyingType(field.ClrType) ?? field.ClrType;
            // Non-nullable INT32/INT16/FLOAT and friends are common in files other tools write.
            // Every value type gets a typed buffer; only text and binary fall through.
            if (clrType == typeof(int)) return ReadValuesAsync<int>(rgReader, field, rowCount, cancellationToken);
            if (clrType == typeof(short)) return ReadValuesAsync<short>(rgReader, field, rowCount, cancellationToken);
            if (clrType == typeof(byte)) return ReadValuesAsync<byte>(rgReader, field, rowCount, cancellationToken);
            if (clrType == typeof(sbyte)) return ReadValuesAsync<sbyte>(rgReader, field, rowCount, cancellationToken);
            if (clrType == typeof(ushort)) return ReadValuesAsync<ushort>(rgReader, field, rowCount, cancellationToken);
            if (clrType == typeof(uint)) return ReadValuesAsync<uint>(rgReader, field, rowCount, cancellationToken);
            if (clrType == typeof(ulong)) return ReadValuesAsync<ulong>(rgReader, field, rowCount, cancellationToken);
            if (clrType == typeof(float)) return ReadValuesAsync<float>(rgReader, field, rowCount, cancellationToken);
            if (clrType == typeof(Guid)) return ReadValuesAsync<Guid>(rgReader, field, rowCount, cancellationToken);
            if (clrType == typeof(DateTimeOffset)) return ReadValuesAsync<DateTimeOffset>(rgReader, field, rowCount, cancellationToken);
            if (clrType == typeof(TimeSpan)) return ReadValuesAsync<TimeSpan>(rgReader, field, rowCount, cancellationToken);
            if (clrType == typeof(DateOnly)) return ReadValuesAsync<DateOnly>(rgReader, field, rowCount, cancellationToken);
            if (clrType == typeof(TimeOnly)) return ReadValuesAsync<TimeOnly>(rgReader, field, rowCount, cancellationToken);
            if (clrType == typeof(byte[])) return ReadBinaryAsync(rgReader, field, rowCount, cancellationToken);
            return ReadKnownNonNullableColumnAsync(rgReader, field, rowCount, cancellationToken);
        }

        private static async Task<object?[]> ReadValuesAsync<T>(
            ParquetRowGroupReader rgReader, DataField field, int rowCount, CancellationToken cancellationToken)
            where T : struct
        {
            var buf = new T[rowCount];
            await rgReader.ReadAsync(field, buf.AsMemory(), null, cancellationToken);
            return Array.ConvertAll(buf, v => (object?)v);
        }

        private static async Task<object?[]> ReadBinaryAsync(
            ParquetRowGroupReader rgReader, DataField field, int rowCount, CancellationToken cancellationToken)
        {
            var buf = new byte[]?[rowCount];
            await rgReader.ReadAsync(field, buf.AsMemory(), null, cancellationToken);
            return Array.ConvertAll(buf, v => (object?)v);
        }

        private static async Task<object?[]> ReadKnownNonNullableColumnAsync(
            ParquetRowGroupReader rgReader,
            DataField field,
            int rowCount,
            CancellationToken cancellationToken)
        {
            var clrType = field.ClrType;
            if (clrType == typeof(long) || clrType == typeof(long?))
            {
                var buf = new long[rowCount];
                await rgReader.ReadAsync(field, buf.AsMemory(), null, cancellationToken);
                return Array.ConvertAll(buf, v => (object?)v);
            }
            if (clrType == typeof(decimal) || clrType == typeof(decimal?))
            {
                var buf = new decimal[rowCount];
                await rgReader.ReadAsync(field, buf.AsMemory(), null, cancellationToken);
                return Array.ConvertAll(buf, v => (object?)v);
            }
            if (clrType == typeof(double) || clrType == typeof(double?))
            {
                var buf = new double[rowCount];
                await rgReader.ReadAsync(field, buf.AsMemory(), null, cancellationToken);
                return Array.ConvertAll(buf, v => (object?)v);
            }
            if (clrType == typeof(bool) || clrType == typeof(bool?))
            {
                var buf = new bool[rowCount];
                await rgReader.ReadAsync(field, buf.AsMemory(), null, cancellationToken);
                return Array.ConvertAll(buf, v => (object?)v);
            }
            if (clrType == typeof(DateTime) || clrType == typeof(DateTime?))
            {
                var buf = new DateTime[rowCount];
                await rgReader.ReadAsync(field, buf.AsMemory(), null, cancellationToken);
                return Array.ConvertAll(buf, v => (object?)v);
            }
            var sBuf = new string?[rowCount];
            await rgReader.ReadAsync(field, sBuf.AsMemory(), null, cancellationToken);
            return Array.ConvertAll(sBuf, v => (object?)v);
        }

        private static async Task WriteColumnToGroupAsync(
            ParquetRowGroupWriter rgWriter,
            DataField field,
            List<Row> rows,
            CancellationToken cancellationToken)
        {
            int count = rows.Count;
            var clrType = field.ClrType;
            if (clrType == typeof(long))
            {
                var buf = new long?[count];
                for (int r = 0; r < count; r++) { cancellationToken.ThrowIfCancellationRequested(); var v = rows[r][field.Name]; buf[r] = v != null && v is not DBNull ? ToInt64Exact(v, field.Name) : (long?)null; }
                await rgWriter.WriteAsync(field, new ReadOnlyMemory<long?>(buf), null, null, cancellationToken);
            }
            else if (clrType == typeof(decimal))
            {
                var buf = new decimal?[count];
                for (int r = 0; r < count; r++) { cancellationToken.ThrowIfCancellationRequested(); var v = rows[r][field.Name]; buf[r] = v != null ? Convert.ToDecimal(v) : (decimal?)null; }
                await rgWriter.WriteAsync(field, new ReadOnlyMemory<decimal?>(buf), null, null, cancellationToken);
            }
            else if (clrType == typeof(double))
            {
                var buf = new double?[count];
                for (int r = 0; r < count; r++) { cancellationToken.ThrowIfCancellationRequested(); var v = rows[r][field.Name]; buf[r] = v != null ? Convert.ToDouble(v) : (double?)null; }
                await rgWriter.WriteAsync(field, new ReadOnlyMemory<double?>(buf), null, null, cancellationToken);
            }
            else if (clrType == typeof(bool))
            {
                var buf = new bool?[count];
                for (int r = 0; r < count; r++) { cancellationToken.ThrowIfCancellationRequested(); var v = rows[r][field.Name]; buf[r] = v != null ? Convert.ToBoolean(v) : (bool?)null; }
                await rgWriter.WriteAsync(field, new ReadOnlyMemory<bool?>(buf), null, null, cancellationToken);
            }
            else if (clrType == typeof(DateTime))
            {
                var buf = new DateTime?[count];
                for (int r = 0; r < count; r++) { cancellationToken.ThrowIfCancellationRequested(); var v = rows[r][field.Name]; buf[r] = v != null ? Convert.ToDateTime(v) : (DateTime?)null; }
                await rgWriter.WriteAsync(field, new ReadOnlyMemory<DateTime?>(buf), null, null, cancellationToken);
            }
            else
            {
                var buf = rows.Select(r => r[field.Name]?.ToString()).ToList();
                await rgWriter.WriteAsync(field, buf, null);
            }
        }

        public Task<IEnumerable<string>> GetColumnsAsync(string tableName) =>
            GetColumnsAsync(tableName, CancellationToken.None);

        public Task<IEnumerable<string>> GetColumnsAsync(string tableName, CancellationToken cancellationToken)
        {
            if (!string.Equals(tableName, "FILE", StringComparison.OrdinalIgnoreCase))
            {
                _logger.Debug("[PARQUET] GetColumnsAsync requested for unknown table '{TableName}'. Only 'FILE' is supported.", tableName);
                return Task.FromResult(Enumerable.Empty<string>());
            }
            return GetColumnsAsync(cancellationToken);
        }

        public Task<IEnumerable<string>> GetColumnsAsync() => GetColumnsAsync(CancellationToken.None);

        public async Task<IEnumerable<string>> GetColumnsAsync(CancellationToken cancellationToken)
        {
            var effectiveCancellationToken = EffectiveCancellationToken(cancellationToken);
            effectiveCancellationToken.ThrowIfCancellationRequested();
            _logger.Debug("[PARQUET] GetColumnsAsync requested for {FilePath}", _filePath);
            if (!System.IO.File.Exists(_filePath))
            {
                _logger.Debug("[PARQUET] GetColumnsAsync failed: File does not exist at {FilePath}", _filePath);
                return Enumerable.Empty<string>();
            }

            string effectivePath = _filePath;
            string? tempFile = null;

            if (_encryption.Enabled)
            {
                effectiveCancellationToken.ThrowIfCancellationRequested();
                tempFile = System.IO.Path.GetTempFileName();
                try
                {
                    await _encryption.DecryptFileAsync(_filePath, tempFile, effectiveCancellationToken);
                    effectivePath = tempFile;
                    _logger.Debug("[PARQUET] Decrypted to {TempFile} for schema discovery.", tempFile);
                }
                catch (OperationCanceledException)
                {
                    TempFileHelper.SafeDelete(tempFile, _logger);
                    throw;
                }
                catch (Exception ex)
                {
                    TempFileHelper.SafeDelete(tempFile, _logger);
                    _logger.Debug("[PARQUET] Failed to decrypt '{FilePath}': {Message}", _filePath, ex.Message);
                    return Enumerable.Empty<string>();
                }
            }

            try
            {
                await using var stream = new FileStream(effectivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                await using var reader = await ParquetReader.CreateAsync(stream, cancellationToken: effectiveCancellationToken);
                var cols = reader.Schema.Fields.Select(f => f.Name).ToList();
                _logger.Debug("[PARQUET] Found {Count} columns: {Cols}", cols.Count, string.Join(", ", cols));
                return cols;
            }
            catch (Exception ex)
            {
                _logger.Debug("[PARQUET] Failed to read schema from '{FilePath}': {Message}", _filePath, ex.Message);
                return Enumerable.Empty<string>();
            }
            finally { TempFileHelper.SafeDelete(tempFile, _logger); }
        }

        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }

        public async ValueTask DisposeAsync()
        {
            await Task.CompletedTask;
        }

        public async Task<string> GetVersionAsync() => await Task.FromResult("Parquet.Net 4.0");
        public HashSet<string> GetSupportedFunctions() => new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public IAsyncEnumerable<DataTable> ExecuteRawSql(string sql, IEnumerable<object?>? parameters = null) =>
            ExecuteRawSql(sql, parameters, CancellationToken.None);

        public async IAsyncEnumerable<DataTable> ExecuteRawSql(
            string sql,
            IEnumerable<object?>? parameters,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (sql.Trim().ToUpperInvariant().StartsWith("SELECT * FROM FILE"))
            {
                await foreach (var batch in ReadBatches(10000, cancellationToken).WithCancellation(cancellationToken))
                    yield return batch;
            }
            else
            {
                _logger.Debug("[PARQUET] ExecuteRawSql received unknown SQL: {Sql}. Returning empty result as native pushdown is not supported.", sql);
                yield return new DataTable { ColumnNames = { "Status" }, Rows = { new Row { ["Status"] = "NOT_SUPPORTED" } } };
            }
        }

        public string ConnectionString => _filePath;
        public string Dialect => "PARQUET";
        public bool SupportsSqlPushdown => false;
        public Task<IEnumerable<string>> GetTablesAsync() => Task.FromResult<IEnumerable<string>>(new[] { "FILE" });
        public Task<IEnumerable<string>> GetTablesAsync(CancellationToken cancellationToken)
        {
            EffectiveCancellationToken(cancellationToken).ThrowIfCancellationRequested();
            return GetTablesAsync();
        }
        public Task<IEnumerable<string>> GetViewsAsync() => Task.FromResult<IEnumerable<string>>(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetViewsAsync(CancellationToken cancellationToken)
        {
            EffectiveCancellationToken(cancellationToken).ThrowIfCancellationRequested();
            return GetViewsAsync();
        }

        private CancellationToken EffectiveCancellationToken(CancellationToken cancellationToken) =>
            cancellationToken.CanBeCanceled ? cancellationToken : (_context?.CancellationToken ?? CancellationToken.None);
    }
}
