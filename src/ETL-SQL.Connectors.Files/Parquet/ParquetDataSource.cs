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
                _encryption.DecryptFile(_filePath, tempFile);
                effectivePath = tempFile;
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
            var effectiveCancellationToken = EffectiveCancellationToken(cancellationToken);
            ETL_SQL.Core.Common.FileConnectorPathHelper.AuthorizeWrite(_context, _filePath);
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

            var fields = new List<Field>();
            foreach (var col in colNames)
            {
                object? firstVal = firstBatch.Rows.Count > 0 ? firstBatch.Rows[0][col] : null;
                Type t = firstVal?.GetType() ?? typeof(string);

                if (t == typeof(int) || t == typeof(long)) fields.Add(new DataField<long>(col, nullable: true));
                else if (t == typeof(decimal)) fields.Add(new DataField<decimal>(col, nullable: true));
                else if (t == typeof(double) || t == typeof(float)) fields.Add(new DataField<double>(col, nullable: true));
                else if (t == typeof(bool)) fields.Add(new DataField<bool>(col, nullable: true));
                else if (t == typeof(DateTime)) fields.Add(new DataField<DateTime>(col, nullable: true));
                else fields.Add(new DataField<string>(col, nullable: true));
            }

            string targetPath = ETL_SQL.Core.Common.FileConnectorPathHelper.GetStagingFilePath(_filePath, _transactional);
            string? tempFile = null;

            if (_encryption.Enabled)
            {
                effectiveCancellationToken.ThrowIfCancellationRequested();
                tempFile = targetPath;
            }

            var dir = System.IO.Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);

            var schema = new ParquetSchema(fields);

            try
            {
                var options = new ParquetOptions();
                if (Enum.TryParse<CompressionMethod>(_compression, true, out var comp))
                    options.CompressionMethod = comp;

                using (var stream = System.IO.File.Create(targetPath))
                await using (var writer = await ParquetWriter.CreateAsync(schema, stream, options))
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

                if (_encryption.Enabled && tempFile != null)
                {
                    effectiveCancellationToken.ThrowIfCancellationRequested();
                    _encryption.EncryptFile(tempFile, _filePath);
                    System.IO.File.Delete(tempFile);
                }
                else
                {
                    // Atomic rename if staging file was used (i.e. transactional, or always if staging path differs from final)
                    if (targetPath != _filePath)
                    {
                        if (System.IO.File.Exists(_filePath)) System.IO.File.Delete(_filePath);
                        System.IO.File.Move(targetPath, _filePath);
                    }
                }
            }
            finally
            {
                if (tempFile != null && System.IO.File.Exists(tempFile))
                {
                    try { System.IO.File.Delete(tempFile); } catch { /* best effort */ }
                }
                if (targetPath != _filePath && System.IO.File.Exists(targetPath))
                {
                    try { System.IO.File.Delete(targetPath); } catch { /* best effort */ }
                }
            }
        }

        public async Task TruncateAsync()
        {
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

        private static async Task<object?[]> ReadNonNullableColumnAsync(
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
                for (int r = 0; r < count; r++) { cancellationToken.ThrowIfCancellationRequested(); var v = rows[r][field.Name]; buf[r] = v != null ? Convert.ToInt64(v) : (long?)null; }
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
                    _encryption.DecryptFile(_filePath, tempFile);
                    effectivePath = tempFile;
                    _logger.Debug("[PARQUET] Decrypted to {TempFile} for schema discovery.", tempFile);
                }
                catch (Exception ex)
                {
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
