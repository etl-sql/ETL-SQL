using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Data;
using ETL_SQL.Core;
using ETL_SQL.Common;
using ETL_SQL.Core.Common;
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
        private readonly EncryptionOptions _encryption;
        private readonly Dictionary<string, string>? _options;
        private readonly ILogger _logger;

        public string Path => _filePath;
        public Dictionary<string, string>? Options => _options;
        
        public IDataSource WithTable(string tableName) => this;
        public string ConnectorType => "PARQUET";

        public ParquetDataSource(string filePath, Dictionary<string, string>? options = null, ILogger? logger = null)
        {
            _filePath = filePath;
            _options = options;
            _logger = logger ?? NullLogger.Instance;
            _compression = options != null && options.TryGetValue("COMPRESSION", out var c) ? c.ToUpperInvariant() : "SNAPPY";
            _encryption = new EncryptionOptions(options);
        }

        public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000)
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

        public async Task WriteBatches(IAsyncEnumerable<DataTable> batches)
        {
            var enumerator = batches.GetAsyncEnumerator();
            if (!await enumerator.MoveNextAsync()) return;

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
                
                if (t == typeof(int) || t == typeof(long)) fields.Add(new DataField<long>(col));
                else if (t == typeof(decimal)) fields.Add(new DataField<decimal>(col));
                else if (t == typeof(double) || t == typeof(float)) fields.Add(new DataField<double>(col));
                else if (t == typeof(bool)) fields.Add(new DataField<bool>(col));
                else if (t == typeof(DateTime)) fields.Add(new DataField<DateTime>(col));
                else fields.Add(new DataField<string>(col));
            }

            string targetPath = _filePath;
            string? tempFile = null;

            if (_encryption.Enabled)
            {
                tempFile = System.IO.Path.GetTempFileName();
                targetPath = tempFile;
            }

            var schema = new ParquetSchema(fields);

            try
            {
                using (var stream = System.IO.File.Create(targetPath))
                using (var writer = await ParquetWriter.CreateAsync(schema, stream))
                {
                    if (Enum.TryParse<CompressionMethod>(_compression, true, out var comp))
                    {
                        writer.CompressionMethod = comp;
                    }

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

        public async Task<IEnumerable<string>> GetColumnsAsync()
        {
            if (!System.IO.File.Exists(_filePath)) return Enumerable.Empty<string>();
            
            string effectivePath = _filePath;
            string? tempFile = null;

            if (_encryption.Enabled)
            {
                tempFile = System.IO.Path.GetTempFileName();
                try { _encryption.DecryptFile(_filePath, tempFile); effectivePath = tempFile; }
                catch (Exception ex) { _logger.Debug("[ParquetDataSource.GetColumnsAsync] Failed to decrypt '{FilePath}': {Message}", _filePath, ex.Message); return Enumerable.Empty<string>(); }
            }

            try
            {
                using var stream = System.IO.File.OpenRead(effectivePath);
                using var reader = await ParquetReader.CreateAsync(stream);
                return reader.Schema.Fields.Select(f => f.Name).ToList();
            }
            catch { return Enumerable.Empty<string>(); }
            finally { TempFileHelper.SafeDelete(tempFile, _logger); }
        }

        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }

        public async ValueTask DisposeAsync()
        {
            await Task.CompletedTask;
        }
    }
}
