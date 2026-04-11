using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ETL_SQL.Data;
using ETL_SQL.Core;
using ETL_SQL.Common;
using ETL_SQL.Core.Common;
using Avro;
using Avro.File;
using Avro.Generic;

namespace ETL_SQL.Connectors.Avro
{
    /// <summary>
    /// Data source implementation for reading and writing Apache Avro files.
    /// </summary>
    public class AvroDataSource : IDataSource
    {
        private readonly string _filePath;
        private readonly string? _schemaFile;
        private readonly EncryptionOptions _encryption;
        private readonly Dictionary<string, string>? _options;
        private readonly ILogger _logger;

        public string Path => _filePath;
        public Dictionary<string, string>? Options => _options;
        public IDataSource WithTable(string tableName) => this;
        public string ConnectorType => "AVRO";
        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }

        public AvroDataSource(string filePath, Dictionary<string, string>? options = null, ILogger? logger = null)
        {
            _filePath = filePath;
            _options = options;
            _logger = logger ?? NullLogger.Instance;
            if (options != null && options.TryGetValue("SCHEMA_FILE", out var sf)) _schemaFile = sf;
            _encryption = new EncryptionOptions(options);
        }

        public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000)
        {
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
                using var reader = await Task.Run(() => DataFileReader<GenericRecord>.OpenReader(stream));
                
                var schema = (RecordSchema)reader.GetSchema();
                var colNames = schema.Fields.Select(f => f.Name).ToList();

                var currentBatch = new DataTable();
                currentBatch.SetColumns(colNames);

                while (await Task.Run(() => reader.HasNext()))
                {
                    var record = await Task.Run(() => reader.Next());
                    var row = new Row();
                    foreach (var field in schema.Fields)
                    {
                        row[field.Name] = record[field.Name];
                    }
                    await currentBatch.AddRowAsync(row);

                    if (currentBatch.Rows.Count >= batchSize)
                    {
                        yield return currentBatch;
                        currentBatch = new DataTable();
                        currentBatch.SetColumns(colNames);
                    }
                }

                if (currentBatch.Rows.Count > 0)
                {
                    yield return currentBatch;
                }
            }
            finally
            {
                TempFileHelper.SafeDelete(tempFile, _logger);
            }
        }

        public async Task WriteBatches(IAsyncEnumerable<DataTable> batches)
        {
            var enumerator = batches.GetAsyncEnumerator();
            if (!await enumerator.MoveNextAsync()) return;

            var firstBatch = enumerator.Current;
            if (firstBatch == null) return;

            RecordSchema schema;
            if (!string.IsNullOrEmpty(_schemaFile) && System.IO.File.Exists(_schemaFile))
            {
                schema = (RecordSchema)Schema.Parse(await System.IO.File.ReadAllTextAsync(_schemaFile));
            }
            else
            {
                schema = (RecordSchema)Schema.Parse(GenerateSchemaJson(firstBatch));
            }

            string targetPath = _filePath;
            string? tempFile = null;

            if (_encryption.Enabled)
            {
                tempFile = System.IO.Path.GetTempFileName();
                targetPath = tempFile;
            }

            try
            {
                using (var stream = System.IO.File.Create(targetPath))
                using (var writer = await Task.Run(() => DataFileWriter<GenericRecord>.OpenWriter(new GenericWriter<GenericRecord>(schema), stream)))
                {
                    do
                    {
                        var batch = enumerator.Current;
                        foreach (var r in batch.Rows)
                        {
                            var record = new GenericRecord(schema);
                            foreach (var field in schema.Fields)
                            {
                                var val = r[field.Name];
                                record.Add(field.Name, CastValue(val, field.Schema));
                            }
                            await Task.Run(() => writer.Append(record));
                        }
                    } while (await enumerator.MoveNextAsync());
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

        private string GenerateSchemaJson(DataTable batch)
        {
            var fields = new List<string>();
            foreach (var col in batch.ColumnNames)
            {
                var val = batch.Rows.Count > 0 ? batch.Rows[0][col] : null;
                string type = val switch {
                    int or long => "long",
                    double or float or decimal => "double",
                    bool => "boolean",
                    _ => "string"
                };
                fields.Add($"{{\"name\": \"{col}\", \"type\": [\"null\", \"{type}\"], \"default\": null}}");
            }
            return $@"{{""type"": ""record"", ""name"": ""ETLRow"", ""namespace"": ""etl.sql"", ""fields"": [{string.Join(",", fields)}]}}";
        }

        private object? CastValue(object? val, Schema schema)
        {
            if (val == null) return null;
            
            Schema actualSchema = schema;
            if (schema is UnionSchema us)
            {
                actualSchema = us.Schemas.FirstOrDefault(s => s.Tag != Schema.Type.Null) ?? us.Schemas[0];
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
            catch { return val; }
        }

        public async Task<IEnumerable<string>> GetColumnsAsync()
        {
            if (!System.IO.File.Exists(_filePath)) return Enumerable.Empty<string>();
            
            string effectivePath = _filePath;
            string? tempFile = null;

            if (_encryption.Enabled)
            {
                tempFile = System.IO.Path.GetTempFileName();
                try { _encryption.DecryptFile(_filePath, tempFile); effectivePath = tempFile; }
                catch (Exception ex) { _logger.Debug($"[AvroDataSource.GetColumnsAsync] Failed to decrypt '{_filePath}': {ex.Message}"); return Enumerable.Empty<string>(); }
            }

            try
            {
                using var stream = System.IO.File.OpenRead(effectivePath);
                using var reader = DataFileReader<GenericRecord>.OpenReader(stream);
                return ((RecordSchema)reader.GetSchema()).Fields.Select(f => f.Name).ToList();
            }
            catch { return Enumerable.Empty<string>(); }
            finally { TempFileHelper.SafeDelete(tempFile, _logger); }
        }

        public async ValueTask DisposeAsync()
        {
            await Task.CompletedTask;
        }
    }
}
