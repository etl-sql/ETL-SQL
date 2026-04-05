using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ETL_SQL.Data;
using ETL_SQL.Core;
using ETL_SQL.Common;
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
        private readonly Dictionary<string, string>? _options;

        /// <summary>Gets the physical path to the Avro file.</summary>
        public string Path => _filePath;
        /// <summary>The options used to create this data source.</summary>
        public Dictionary<string, string>? Options => _options;
        /// <summary>Returns this instance as a typed table (no-op for Avro).</summary>
        public IDataSource WithTable(string tableName) => this;

        /// <summary>
        /// Initializes a new instance of the <see cref="AvroDataSource"/> class.
        /// </summary>
        /// <param name="filePath">The path to the Avro file.</param>
        /// <param name="options">Optional configuration (e.g., SCHEMA_FILE).</param>
        public AvroDataSource(string filePath, Dictionary<string, string>? options = null)
        {
            _filePath = filePath;
            _options = options;
            if (options != null && options.TryGetValue("SCHEMA_FILE", out var sf)) _schemaFile = sf;
        }

        /// <summary>Reads data from the Avro file in batches.</summary>
        public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000)
        {
            if (!System.IO.File.Exists(_filePath)) yield break;

            using var stream = System.IO.File.OpenRead(_filePath);
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
                currentBatch.AddRow(row);

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

        /// <summary>Writes batches of data to the Avro file.</summary>
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

            using var stream = System.IO.File.Create(_filePath);
            using var writer = await Task.Run(() => DataFileWriter<GenericRecord>.OpenWriter(new GenericWriter<GenericRecord>(schema), stream));

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
                // Avro Union with null for optionality
                fields.Add($"{{\"name\": \"{col}\", \"type\": [\"null\", \"{type}\"], \"default\": null}}");
            }
            return $@"{{""type"": ""record"", ""name"": ""ETLRow"", ""namespace"": ""etl.sql"", ""fields"": [{string.Join(",", fields)}]}}";
        }

        private object? CastValue(object? val, Schema schema)
        {
            if (val == null) return null;
            
            // Handle Unions (["null", "type"])
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

        /// <summary>Discovers the column names from the Avro file schema.</summary>
        public Task<IEnumerable<string>> GetColumnsAsync()
        {
            if (!System.IO.File.Exists(_filePath)) return Task.FromResult(Enumerable.Empty<string>());
            try
            {
                using var stream = System.IO.File.OpenRead(_filePath);
                using var reader = DataFileReader<GenericRecord>.OpenReader(stream);
                return Task.FromResult((IEnumerable<string>)((RecordSchema)reader.GetSchema()).Fields.Select(f => f.Name).ToList());
            }
            catch { return Task.FromResult(Enumerable.Empty<string>()); }
        }

        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }
        public async ValueTask DisposeAsync()
        {
            await Task.CompletedTask;
        }
    }
}

