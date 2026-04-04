using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Data;
using ETL_SQL.Core;
using Parquet;
using Parquet.Data;
using Parquet.Schema;

namespace ETL_SQL.Connectors.Parquet
{
    /// <summary>
    /// Data source implementation for Apache Parquet files.
    /// Supports high-performance columnar reading and writing.
    /// </summary>
    public class ParquetDataSource : IDataSource
    {
        private readonly string _filePath;
        private readonly string _compression;

        /// <summary>Gets the physical path to the Parquet file.</summary>
        public string Path => _filePath;
        
        /// <summary>Returns this instance as a typed table (no-op for Parquet).</summary>
        public IDataSource WithTable(string tableName) => this;

        /// <summary>
        /// Initializes a new instance of the <see cref="ParquetDataSource"/> class.
        /// </summary>
        /// <param name="filePath">The path to the Parquet file.</param>
        /// <param name="options">Optional configuration params (e.g. COMPRESSION).</param>
        public ParquetDataSource(string filePath, Dictionary<string, string>? options = null)
        {
            _filePath = filePath;
            _compression = options != null && options.TryGetValue("COMPRESSION", out var c) ? c.ToUpperInvariant() : "SNAPPY";
        }

        /// <summary>Reads data from the Parquet file in batches.</summary>
        /// <param name="batchSize">The maximum number of rows per batch.</param>
        /// <returns>An async enumerable of DataTables.</returns>
        public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000)
        {
            if (!System.IO.File.Exists(_filePath)) yield break;

            using var stream = System.IO.File.OpenRead(_filePath);
            using var reader = await ParquetReader.CreateAsync(stream);
            var dataFields = reader.Schema.GetDataFields();
            var colNames = dataFields.Select(f => f.Name).ToList();

            for (int i = 0; i < reader.RowGroupCount; i++)
            {
                using var rgReader = reader.OpenRowGroupReader(i);
                int rowCount = (int)rgReader.RowCount;
                
                var columns = new Array[dataFields.Length];
                for (int j = 0; j < dataFields.Length; j++)
                {
                    columns[j] = (await rgReader.ReadColumnAsync(dataFields[j])).Data;
                }

                DataTable? currentBatch = null;

                for (int r = 0; r < rowCount; r++)
                {
                    if (currentBatch == null)
                    {
                        currentBatch = new DataTable();
                        currentBatch.SetColumns(colNames);
                    }

                    var etlRow = new ETL_SQL.Data.Row();
                    for (int c = 0; c < dataFields.Length; c++)
                    {
                        etlRow[colNames[c]] = columns[c].GetValue(r);
                    }
                    currentBatch.AddRow(etlRow);

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

        /// <summary>Writes batches of data to the Parquet file.</summary>
        /// <param name="batches">An async enumerable of DataTables.</param>
        public async Task WriteBatches(IAsyncEnumerable<DataTable> batches)
        {
            var enumerator = batches.GetAsyncEnumerator();
            if (!await enumerator.MoveNextAsync()) return;

            var firstBatch = enumerator.Current;
            var fields = new List<Field>();
            foreach (var col in firstBatch.ColumnNames)
            {
                object? firstVal = firstBatch.Rows.Count > 0 ? firstBatch.Rows[0][col] : null;
                Type t = firstVal?.GetType() ?? typeof(string);
                
                if (t == typeof(int) || t == typeof(long)) fields.Add(new DataField<long>(col));
                else if (t == typeof(decimal)) fields.Add(new DataField<decimal>(col));
                else if (t == typeof(double) || t == typeof(float)) fields.Add(new DataField<double>(col));
                else if (t == typeof(bool)) fields.Add(new DataField<bool>(col));
                else if (t == typeof(DateTime)) fields.Add(new DataField<DateTime>(col));
                else fields.Add(new DataField<string>(col));
            }

            var schema = new ParquetSchema(fields);
            
            using var stream = System.IO.File.Create(_filePath);
            using var writer = await ParquetWriter.CreateAsync(schema, stream);
            
            if (Enum.TryParse<CompressionMethod>(_compression, true, out var comp))
            {
                writer.CompressionMethod = comp;
            }

            // We need to write in row groups. For simplicity, we'll write each batch as a row group.
            bool hasMore = true;
            DataTable batch = firstBatch;

            while (hasMore)
            {
                using (var rgWriter = writer.CreateRowGroup())
                {
                    var dataFields = schema.GetDataFields();
                    for (int i = 0; i < dataFields.Length; i++)
                    {
                        var field = dataFields[i];
                        var values = Array.CreateInstance(field.ClrType, batch.Rows.Count);
                        for (int r = 0; r < batch.Rows.Count; r++)
                        {
                            values.SetValue(CastValue(batch.Rows[r][field.Name], field), r);
                        }
                        var column = new DataColumn(field, values);
                        await rgWriter.WriteColumnAsync(column);
                    }
                }
                hasMore = await enumerator.MoveNextAsync();
                if (hasMore) batch = enumerator.Current;
            }
        }

        private object? CastValue(object? val, DataField field)
        {
            if (val == null) return null;
            try
            {
                if (field.ClrType == typeof(long)) return Convert.ToInt64(val);
                if (field.ClrType == typeof(decimal)) return Convert.ToDecimal(val);
                if (field.ClrType == typeof(double)) return Convert.ToDouble(val);
                if (field.ClrType == typeof(bool)) return Convert.ToBoolean(val);
                if (field.ClrType == typeof(DateTime)) return Convert.ToDateTime(val);
                return val.ToString();
            }
            catch { return null; }
        }

        /// <summary>Asynchronously retrieves the column names from the Parquet schema.</summary>
        /// <returns>A collection of field names.</returns>
        public async Task<IEnumerable<string>> GetColumnsAsync()
        {
            if (!System.IO.File.Exists(_filePath)) return Enumerable.Empty<string>();
            using var stream = System.IO.File.OpenRead(_filePath);
            try
            {
                using var reader = await ParquetReader.CreateAsync(stream);
                return reader.Schema.Fields.Select(f => f.Name).ToList();
            }
            catch { return Enumerable.Empty<string>(); }
        }

        /// <summary>Captures a snapshot (no-op for Parquet).</summary>
        public object? Snapshot() => null;

        /// <summary>Restores from a snapshot (no-op for Parquet).</summary>
        public void Restore(object? snapshot) { }

        /// <summary>Asynchronously disposes resources.</summary>
        public async ValueTask DisposeAsync()
        {
            await Task.CompletedTask;
        }
    }
}

