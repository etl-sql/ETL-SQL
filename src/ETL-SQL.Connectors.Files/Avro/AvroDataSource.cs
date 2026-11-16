using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Avro;
using Avro.File;
using Avro.Generic;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Shared;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;

namespace ETL_SQL.Connectors.Avro
{
    /// <summary>
    /// Data source implementation for reading and writing Apache Avro files.
    /// </summary>
    public class AvroDataSource : IDatabaseSource
    {
        private readonly string _filePath;
        private readonly string? _schemaFile;
        private readonly EncryptionOptions _encryption;
        private readonly Dictionary<string, string>? _options;
        private readonly ILogger _logger;
        private readonly IExecutionContext? _context;

        public string Path => _filePath;
        public Dictionary<string, string>? Options => _options;
        public IDataSource WithTable(string tableName) => this;
        public string ConnectorType => "AVRO";
        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }

        public AvroDataSource(IExecutionContext context, string filePath, Dictionary<string, string>? options = null)
        {
            _context = context;
            _logger = context.Logger;

            _options = options;
            if (options != null && options.TryGetValue("SCHEMA_FILE", out var sf))
            {
                _schemaFile = context.ResolvePath(sf);
                context.SecurityService.ValidatePath(_schemaFile);
            }
            _encryption = new EncryptionOptions(options);

            var resolvedPath = context.ResolvePath(filePath.Trim('\'', '\"', ' ', '\t', '\r', '\n'));
            _filePath = FileConnectorPathHelper.CoerceFilePathExtension(resolvedPath, _encryption.Enabled, false);

            // Security Hardening: Defense in depth
            context.SecurityService.ValidatePath(_filePath);
            context.SecurityService.ValidateFileType(_filePath, context.AllowUnknownFileTypes);
        }

        public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) =>
            ReadBatches(batchSize, CancellationToken.None);

        public IAsyncEnumerable<DataTable> ReadBatches(int batchSize, CancellationToken cancellationToken) =>
            ConnectorExceptionWrapper.WrapAsync(ReadBatchesCore(batchSize, cancellationToken), "Avro", ex => ex is not ExecutionException);

        private async IAsyncEnumerable<DataTable> ReadBatchesCore(
            int batchSize,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var effectiveCancellationToken = EffectiveCancellationToken(cancellationToken);
            ETL_SQL.Core.Common.FileConnectorPathHelper.AuthorizeRead(_context, _filePath);
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
                using var reader = await Task.Run(() => DataFileReader<GenericRecord>.OpenReader(stream), effectiveCancellationToken);

                var schema = (RecordSchema)reader.GetSchema();
                var colNames = schema.Fields.Select(f => f.Name).ToList();

                var currentBatch = new DataTable();
                currentBatch.SetColumns(colNames);

                // The Avro reader is synchronous. One background task decodes a whole batch; a task per
                // row cost more in scheduling than the decode itself.
                var records = new List<GenericRecord>(Math.Max(1, batchSize));
                while (true)
                {
                    records.Clear();
                    await Task.Run(() =>
                    {
                        while (records.Count < batchSize && reader.HasNext())
                        {
                            effectiveCancellationToken.ThrowIfCancellationRequested();
                            records.Add(reader.Next());
                        }
                    }, effectiveCancellationToken);
                    if (records.Count == 0) break;

                    foreach (var record in records)
                    {
                        var row = currentBatch.NewRow();
                        foreach (var field in schema.Fields)
                        {
                            row[field.Name] = record[field.Name];
                        }
                        await currentBatch.AddRowAsync(row);
                    }

                    yield return currentBatch;
                    currentBatch = new DataTable();
                    currentBatch.SetColumns(colNames);
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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or AvroException or InvalidDataException)
            {
                throw ConnectorExceptionWrapper.Wrap("Avro", ex);
            }
        }

        private async Task WriteBatchesCore(IAsyncEnumerable<DataTable> batches, bool append, CancellationToken effectiveCancellationToken)
        {
            await using var enumerator = batches.GetAsyncEnumerator(effectiveCancellationToken);
            if (!await enumerator.MoveNextAsync()) return;

            var firstBatch = enumerator.Current;
            if (firstBatch == null) return;

            var dir = System.IO.Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);

            // Rows are written to a plain staging file, then published (encrypted when asked).
            // INSERT INTO an Avro file arrives with append: true; the existing file is staged first
            // and the new records are appended to it in its own schema. It used to be overwritten.
            bool appendToExisting = append && System.IO.File.Exists(_filePath);
            string stagingFile = System.IO.Path.GetTempFileName();
            try
            {
                RecordSchema schema;
                if (appendToExisting)
                {
                    if (_encryption.Enabled)
                        await _encryption.DecryptFileAsync(_filePath, stagingFile, effectiveCancellationToken);
                    else
                        System.IO.File.Copy(_filePath, stagingFile, overwrite: true);
                    schema = ReadAppendableSchema(stagingFile, firstBatch.ColumnNames);
                }
                else if (!string.IsNullOrEmpty(_schemaFile) && System.IO.File.Exists(_schemaFile))
                {
                    schema = (RecordSchema)Schema.Parse(await System.IO.File.ReadAllTextAsync(_schemaFile, effectiveCancellationToken));
                }
                else
                {
                    schema = (RecordSchema)Schema.Parse(GenerateSchemaJson(firstBatch));
                }

                var datumWriter = new GenericWriter<GenericRecord>(schema);
                using (var writer = appendToExisting
                    ? await Task.Run(() => DataFileWriter<GenericRecord>.OpenAppendWriter(datumWriter, stagingFile), effectiveCancellationToken)
                    : await Task.Run(() => DataFileWriter<GenericRecord>.OpenWriter(datumWriter, stagingFile), effectiveCancellationToken))
                {
                    do
                    {
                        effectiveCancellationToken.ThrowIfCancellationRequested();
                        var batch = enumerator.Current;
                        await Task.Run(() =>
                        {
                            foreach (var r in batch.Rows)
                            {
                                effectiveCancellationToken.ThrowIfCancellationRequested();
                                var record = new GenericRecord(schema);
                                foreach (var field in schema.Fields)
                                {
                                    var val = r[field.Name];
                                    record.Add(field.Name, CastValue(val, field.Schema, field.Name));
                                }
                                writer.Append(record);
                            }
                        }, effectiveCancellationToken);
                    } while (await enumerator.MoveNextAsync());
                }

                effectiveCancellationToken.ThrowIfCancellationRequested();
                if (_encryption.Enabled)
                    await _encryption.EncryptFileAsync(stagingFile, _filePath, effectiveCancellationToken);
                else
                    System.IO.File.Move(stagingFile, _filePath, overwrite: true);
            }
            finally
            {
                TempFileHelper.SafeDelete(stagingFile, _logger);
            }
        }

        /// <summary>
        /// The existing file's record schema, when every incoming column is one of its fields.
        /// A column the file does not have is refused rather than silently dropped.
        /// </summary>
        private static RecordSchema ReadAppendableSchema(string path, IReadOnlyCollection<string> incomingColumns)
        {
            using var reader = DataFileReader<GenericRecord>.OpenReader(path);
            if (reader.GetSchema() is not RecordSchema schema)
                throw new ExecutionException("Cannot append to this Avro file: its schema is not a record.");
            var missing = incomingColumns.Where(c => !schema.Fields.Any(f => string.Equals(f.Name, c, StringComparison.Ordinal))).ToList();
            if (missing.Count > 0)
                throw new ExecutionException(
                    $"Cannot append to this Avro file: it has no field {string.Join(", ", missing.Select(m => $"'{m}'"))}.");
            return schema;
        }

        /// <summary>
        /// Builds the record schema for a new file. Engine integers are decimal at runtime, so a
        /// column whose values are all whole numbers is an Avro long, not a double; a fractional
        /// value makes it double. Every non-null value in the first batch decides the type, so a
        /// leading null no longer makes a numeric column a string. Names are JSON-escaped.
        /// </summary>
        internal static string GenerateSchemaJson(DataTable batch)
        {
            var fields = new List<string>();
            foreach (var col in batch.ColumnNames)
            {
                string? type = null;
                foreach (var row in batch.Rows)
                {
                    var kind = row[col] switch
                    {
                        null or DBNull => null,
                        int or long or short or byte or sbyte or uint or ushort => "long",
                        decimal d => d == decimal.Truncate(d) ? "long" : "double",
                        double or float => "double",
                        bool => "boolean",
                        _ => "string"
                    };
                    if (kind == null) continue;
                    if (type == null || type == kind) { type = kind; continue; }
                    type = (type, kind) is ("long", "double") or ("double", "long") ? "double" : "string";
                    if (type == "string") break;
                }
                var name = System.Text.Json.JsonSerializer.Serialize(col);
                fields.Add($"{{\"name\": {name}, \"type\": [\"null\", \"{type ?? "string"}\"], \"default\": null}}");
            }
            return $@"{{""type"": ""record"", ""name"": ""ETLRow"", ""namespace"": ""etl.sql"", ""fields"": [{string.Join(",", fields)}]}}";
        }

        private static object? CastValue(object? val, Schema schema, string fieldName)
        {
            if (val == null || val is DBNull) return null;

            Schema actualSchema = schema;
            if (schema is UnionSchema us)
            {
                actualSchema = us.Schemas.FirstOrDefault(s => s.Tag != Schema.Type.Null) ?? us.Schemas[0];
            }

            // Integer targets never round: a fractional value in a later batch is an error, not a
            // silently different number.
            if (actualSchema.Tag is Schema.Type.Int or Schema.Type.Long
                && (val is decimal d && d != decimal.Truncate(d) || val is double dbl && dbl != Math.Truncate(dbl)))
            {
                throw new ExecutionException(
                    $"Avro field '{fieldName}' is an integer field; value {val} has a fractional part and would be rounded.");
            }

            try
            {
                return actualSchema.Tag switch
                {
                    Schema.Type.Int => Convert.ToInt32(val),
                    Schema.Type.Long => Convert.ToInt64(val),
                    Schema.Type.Double => Convert.ToDouble(val),
                    Schema.Type.Float => Convert.ToSingle(val),
                    Schema.Type.Boolean => Convert.ToBoolean(val),
                    Schema.Type.String => val.ToString(),
                    _ => val
                };
            }
            catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
            {
                return val;
            }
        }
        public Task<IEnumerable<string>> GetColumnsAsync() => GetColumnsAsync(CancellationToken.None);

        public async Task<IEnumerable<string>> GetColumnsAsync(CancellationToken cancellationToken)
        {
            var effectiveCancellationToken = EffectiveCancellationToken(cancellationToken);
            effectiveCancellationToken.ThrowIfCancellationRequested();
            if (!System.IO.File.Exists(_filePath)) return Enumerable.Empty<string>();

            string effectivePath = _filePath;
            string? tempFile = null;

            if (_encryption.Enabled)
            {
                effectiveCancellationToken.ThrowIfCancellationRequested();
                tempFile = System.IO.Path.GetTempFileName();
                try { await _encryption.DecryptFileAsync(_filePath, tempFile, effectiveCancellationToken); effectivePath = tempFile; }
                catch (OperationCanceledException) { TempFileHelper.SafeDelete(tempFile, _logger); throw; }
                catch (Exception ex) { TempFileHelper.SafeDelete(tempFile, _logger); _logger.Debug("[AvroDataSource.GetColumnsAsync] Failed to decrypt '{FilePath}': {Message}", _filePath, ex.Message); return Enumerable.Empty<string>(); }
            }

            try
            {
                using var stream = System.IO.File.OpenRead(effectivePath);
                using var reader = await Task.Run(() => DataFileReader<GenericRecord>.OpenReader(stream), effectiveCancellationToken);
                effectiveCancellationToken.ThrowIfCancellationRequested();
                return ((RecordSchema)reader.GetSchema()).Fields.Select(f => f.Name).ToList();
            }
            catch { return Enumerable.Empty<string>(); }
            finally { TempFileHelper.SafeDelete(tempFile, _logger); }
        }

        public async ValueTask DisposeAsync()
        {
            await Task.CompletedTask;
        }

        public async Task<string> GetVersionAsync() => await Task.FromResult("1.0.0");
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
                _logger.Debug("[AVRO] ExecuteRawSql received unknown SQL: {Sql}. Returning empty result as native pushdown is not supported.", sql);
                yield return new DataTable { ColumnNames = { "Status" }, Rows = { new Row { ["Status"] = "NOT_SUPPORTED" } } };
            }
        }

        public string ConnectionString => _filePath;
        public string Dialect => "AVRO";
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
        public Task<IEnumerable<string>> GetColumnsAsync(string tableName) => GetColumnsAsync(tableName, CancellationToken.None);
        public Task<IEnumerable<string>> GetColumnsAsync(string tableName, CancellationToken cancellationToken) =>
            string.Equals(tableName, "FILE", StringComparison.OrdinalIgnoreCase)
                ? GetColumnsAsync(cancellationToken)
                : Task.FromResult(Enumerable.Empty<string>());

        private CancellationToken EffectiveCancellationToken(CancellationToken cancellationToken) =>
            cancellationToken.CanBeCanceled ? cancellationToken : (_context?.CancellationToken ?? CancellationToken.None);
    }
}
