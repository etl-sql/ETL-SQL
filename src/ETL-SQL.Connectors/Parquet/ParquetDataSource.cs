using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        }

        public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) =>
            ConnectorExceptionWrapper.WrapAsync(ReadBatchesCore(batchSize), "Parquet", ex => ex is not ExecutionException);

        private async IAsyncEnumerable<DataTable> ReadBatchesCore(int batchSize)
        {
            if (!System.IO.File.Exists(_filePath)) yield break;

            string effectivePath = _filePath;
            string? tempFile = null;

            if (_encryption.Enabled)
            {
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
                    using var rgReader = reader.OpenRowGroupReader(i);
                    int rowCount = (int)rgReader.RowCount;

                    var columns = new object?[dataFields.Length][];
                    for (int j = 0; j < dataFields.Length; j++)
                    {
                        columns[j] = await ReadColumnAsObjectsAsync(rgReader, dataFields[j], rowCount);
                    }

                    DataTable? currentBatch = null;

                    for (int r = 0; r < rowCount; r++)
                    {
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

        public async Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false)
        {
            ETL_SQL.Core.Common.FileConnectorPathHelper.AuthorizeWrite(_context, _filePath);
            var enumerator = batches.GetAsyncEnumerator();
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

            string targetPath = _filePath;
            string? tempFile = null;

            if (_encryption.Enabled)
            {
                tempFile = System.IO.Path.GetTempFileName();
                targetPath = tempFile;
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
                        using (var rgWriter = writer.CreateRowGroup())
                        {
                            var dataFields = schema.GetDataFields();
                            for (int i = 0; i < dataFields.Length; i++)
                            {
                                await WriteColumnToGroupAsync(rgWriter, dataFields[i], batch.Rows);
                            }
                        }
                        hasMore = await enumerator.MoveNextAsync();
                        if (hasMore) batch = enumerator.Current;
                    }
                }

                if (_encryption.Enabled)
                {
                    _encryption.EncryptFile(targetPath, _filePath);
                }
            }
            finally
            {
                TempFileHelper.SafeDelete(tempFile, _logger);
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

        private static async Task<object?[]> ReadColumnAsObjectsAsync(ParquetRowGroupReader rgReader, DataField field, int rowCount)
        {
            var rawData = await rgReader.ReadRawColumnDataBaseAsync(field, default);
            var prop = rawData.GetType().GetProperty("NullableValues");
            if (prop != null)
            {
                try
                {
                    var seq = (System.Collections.IEnumerable)prop.GetValue(rawData)!;
                    var result = new object?[rowCount];
                    int idx = 0;
                    foreach (var v in seq) { if (idx >= rowCount) break; result[idx++] = v; }
                    return result;
                }
                catch (InvalidOperationException)
                {
                    // Column has no definition levels (non-nullable); fall through to typed read
                }
            }
            return await ReadNonNullableColumnAsync(rgReader, field, rowCount);
        }

        private static async Task<object?[]> ReadNonNullableColumnAsync(ParquetRowGroupReader rgReader, DataField field, int rowCount)
        {
            var clrType = field.ClrType;
            if (clrType == typeof(long) || clrType == typeof(long?))
            {
                var buf = new long[rowCount];
                await rgReader.ReadAsync(field, buf.AsMemory(), null, default);
                return Array.ConvertAll(buf, v => (object?)v);
            }
            if (clrType == typeof(decimal) || clrType == typeof(decimal?))
            {
                var buf = new decimal[rowCount];
                await rgReader.ReadAsync(field, buf.AsMemory(), null, default);
                return Array.ConvertAll(buf, v => (object?)v);
            }
            if (clrType == typeof(double) || clrType == typeof(double?))
            {
                var buf = new double[rowCount];
                await rgReader.ReadAsync(field, buf.AsMemory(), null, default);
                return Array.ConvertAll(buf, v => (object?)v);
            }
            if (clrType == typeof(bool) || clrType == typeof(bool?))
            {
                var buf = new bool[rowCount];
                await rgReader.ReadAsync(field, buf.AsMemory(), null, default);
                return Array.ConvertAll(buf, v => (object?)v);
            }
            if (clrType == typeof(DateTime) || clrType == typeof(DateTime?))
            {
                var buf = new DateTime[rowCount];
                await rgReader.ReadAsync(field, buf.AsMemory(), null, default);
                return Array.ConvertAll(buf, v => (object?)v);
            }
            var sBuf = new string?[rowCount];
            await rgReader.ReadAsync(field, sBuf.AsMemory(), null, default);
            return Array.ConvertAll(sBuf, v => (object?)v);
        }

        private static async Task WriteColumnToGroupAsync(ParquetRowGroupWriter rgWriter, DataField field, List<Row> rows)
        {
            int count = rows.Count;
            var clrType = field.ClrType;
            if (clrType == typeof(long))
            {
                var buf = new long?[count];
                for (int r = 0; r < count; r++) { var v = rows[r][field.Name]; buf[r] = v != null ? Convert.ToInt64(v) : (long?)null; }
                await rgWriter.WriteAsync(field, new ReadOnlyMemory<long?>(buf), null, null, default);
            }
            else if (clrType == typeof(decimal))
            {
                var buf = new decimal?[count];
                for (int r = 0; r < count; r++) { var v = rows[r][field.Name]; buf[r] = v != null ? Convert.ToDecimal(v) : (decimal?)null; }
                await rgWriter.WriteAsync(field, new ReadOnlyMemory<decimal?>(buf), null, null, default);
            }
            else if (clrType == typeof(double))
            {
                var buf = new double?[count];
                for (int r = 0; r < count; r++) { var v = rows[r][field.Name]; buf[r] = v != null ? Convert.ToDouble(v) : (double?)null; }
                await rgWriter.WriteAsync(field, new ReadOnlyMemory<double?>(buf), null, null, default);
            }
            else if (clrType == typeof(bool))
            {
                var buf = new bool?[count];
                for (int r = 0; r < count; r++) { var v = rows[r][field.Name]; buf[r] = v != null ? Convert.ToBoolean(v) : (bool?)null; }
                await rgWriter.WriteAsync(field, new ReadOnlyMemory<bool?>(buf), null, null, default);
            }
            else if (clrType == typeof(DateTime))
            {
                var buf = new DateTime?[count];
                for (int r = 0; r < count; r++) { var v = rows[r][field.Name]; buf[r] = v != null ? Convert.ToDateTime(v) : (DateTime?)null; }
                await rgWriter.WriteAsync(field, new ReadOnlyMemory<DateTime?>(buf), null, null, default);
            }
            else
            {
                var buf = rows.Select(r => r[field.Name]?.ToString()).ToList();
                await rgWriter.WriteAsync(field, buf, null);
            }
        }

        public Task<IEnumerable<string>> GetColumnsAsync(string tableName)
        {
            if (!string.Equals(tableName, "FILE", StringComparison.OrdinalIgnoreCase))
            {
                _logger.Debug("[PARQUET] GetColumnsAsync requested for unknown table '{TableName}'. Only 'FILE' is supported.", tableName);
                return Task.FromResult(Enumerable.Empty<string>());
            }
            return GetColumnsAsync();
        }

        public async Task<IEnumerable<string>> GetColumnsAsync()
        {
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
                using var stream = System.IO.File.OpenRead(effectivePath);
                await using var reader = await ParquetReader.CreateAsync(stream);
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

        public async IAsyncEnumerable<DataTable> ExecuteRawSql(string sql, IEnumerable<object?>? parameters = null)
        {
            if (sql.Trim().ToUpperInvariant().StartsWith("SELECT * FROM FILE"))
            {
                await foreach (var batch in ReadBatches()) yield return batch;
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
        public Task<IEnumerable<string>> GetViewsAsync() => Task.FromResult<IEnumerable<string>>(Enumerable.Empty<string>());
    }
}
