using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Apache.Arrow;
using Apache.Arrow.Ipc;
using Apache.Arrow.Types;
using ETL_SQL.Data;
using ETL_SQL.Core;
using ETL_SQL.Core.Spill;

namespace ETL_SQL.Engine.Spill
{
    /// <summary>
    /// Implements encrypted, compressed storage for spilling large data sets to disk.
    /// This implementation is session-aware and dynamically reacts to changes in
    /// SessionId or SessionRoot after initialization.
    /// </summary>
    public class SpillStore : ISpillStore
    {
        private string? _cachedRootPath;
        private byte[]? _cachedSessionKey;
        private string? _cachedSessionId;
        private readonly IExecutionContext _context;
        private bool _disposed;

        public bool IsPersistent { get; set; }

        public string RootPath
        {
            get
            {
                EnsureInitialized();
                return _cachedRootPath!;
            }
        }

        public SpillStore(IExecutionContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Ensures that the spill directory and encryption keys are correctly initialized
        /// based on the current context state.
        /// </summary>
        private void EnsureInitialized()
        {
            // If already initialized and context hasn't changed its persistent/session state, skip.
            if (_cachedRootPath != null &&
                IsPersistent == _context.IsPersistentSession &&
                (!IsPersistent || _cachedSessionId == _context.SessionId))
            {
                return;
            }

            IsPersistent = _context.IsPersistentSession;
            _cachedSessionId = _context.SessionId;

            if (IsPersistent)
            {
                // Stable path in the session directory
                _cachedRootPath = Path.Combine(_context.SessionRoot, "spill");

                // Deterministic Key based on MachineKey + SessionId (Centralized)
                _cachedSessionKey = _context.SessionStateManager.GetSpillKey(_context.SessionId ?? "DEFAULT");
            }
            else if (_cachedRootPath == null || IsPersistent != _context.IsPersistentSession)
            {
                // Disposable temp path
                _cachedRootPath = Path.Combine(Path.GetTempPath(), "ETL-SQL-Spill", Guid.NewGuid().ToString("N"));
                _cachedSessionKey = RandomNumberGenerator.GetBytes(32);
            }

            if (!Directory.Exists(_cachedRootPath))
            {
                Directory.CreateDirectory(_cachedRootPath);
            }
        }

        public async Task<ISpillWriter> CreateWriterAsync(string chunkName)
        {
            EnsureInitialized();
            var path = Path.Combine(_cachedRootPath!, chunkName);
            var encrypt = _context.SpillEncryptionEnabled;
            var compress = _context.SpillCompressionEnabled;

            if (_context.SpillFormat == "Json")
                return await Task.FromResult(new SecureSpillWriter(path, chunkName, _cachedSessionKey!, _context, encrypt, compress));

            return await Task.FromResult(new ArrowSpillWriter(path, chunkName, _cachedSessionKey!, _context, encrypt, compress));
        }

        public async Task<ISpillReader> CreateReaderAsync(string chunkName)
        {
            EnsureInitialized();
            var path = Path.Combine(_cachedRootPath!, chunkName);
            var encrypt = _context.SpillEncryptionEnabled;
            var compress = _context.SpillCompressionEnabled;

            if (_context.SpillFormat == "Json")
                return await Task.FromResult(new SecureSpillReader(path, chunkName, _cachedSessionKey!, encrypt, compress));

            return await Task.FromResult(new ArrowSpillReader(path, chunkName, _cachedSessionKey!, encrypt, compress));
        }

        public void DeleteChunk(string chunkName)
        {
            EnsureInitialized();
            var path = Path.Combine(_cachedRootPath!, chunkName);
            if (File.Exists(path))
            {
                try { File.Delete(path); }
                catch (Exception ex)
                {
                    _context.Logger.Warning("Failed to delete spill chunk {ChunkName}: {Message}", chunkName, ex.Message);
                }
            }
        }

        public void Cleanup()
        {
            EnsureInitialized();
            if (Directory.Exists(_cachedRootPath))
            {
                try
                {
                    Directory.Delete(_cachedRootPath, true);
                }
                catch (Exception ex)
                {
                    _context.Logger.Warning("Failed to cleanup spill directory {Path}: {Message}", _cachedRootPath, ex.Message);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            // Clean up non-persistent spill directories
            if (!IsPersistent && _cachedRootPath != null && Directory.Exists(_cachedRootPath))
            {
                try { Directory.Delete(_cachedRootPath, true); } catch { }
            }

            if (_cachedSessionKey != null) System.Array.Clear(_cachedSessionKey);

            _disposed = true;
            GC.SuppressFinalize(this);
        }

        // ── JSON (legacy) ────────────────────────────────────────────────────────

        private class SecureSpillWriter : ISpillWriter
        {
            private readonly FileStream _fileStream;
            private readonly CryptoStream? _cryptoStream;
            private readonly GZipStream? _gzipStream;
            private readonly StreamWriter _writer;
            private readonly IExecutionContext _context;
            private readonly string _chunkName;

            public string ChunkName => _chunkName;

            public SecureSpillWriter(string path, string chunkName, byte[] key, IExecutionContext context, bool encrypt, bool compress)
            {
                _chunkName = chunkName;
                _context = context;
                _fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);

                Stream current = _fileStream;

                if (encrypt)
                {
                    using var aes = Aes.Create();
                    aes.Key = key;
                    aes.GenerateIV();
                    _fileStream.Write(aes.IV, 0, aes.IV.Length); // Write IV to start of file

                    var encryptor = aes.CreateEncryptor(key, aes.IV);
                    _cryptoStream = new CryptoStream(_fileStream, encryptor, CryptoStreamMode.Write);
                    current = _cryptoStream;
                }

                if (compress)
                {
                    _gzipStream = new GZipStream(current, CompressionLevel.Optimal);
                    current = _gzipStream;
                }

                _writer = new StreamWriter(current, Encoding.UTF8);
            }

            public async Task WriteRowAsync(Row row)
            {
                var json = JsonSerializer.Serialize(row.Columns);
                if (_context.TelemetryEnabled)
                {
                    // Fast approximation (+2 for newline)
                    _context.TotalSpilledBytes += json.Length + 2;
                }
                await _writer.WriteLineAsync(json);
            }

            public async Task WriteRowsAsync(IEnumerable<Row> rows)
            {
                foreach (var r in rows) await WriteRowAsync(r);
            }

            public async ValueTask DisposeAsync()
            {
                await _writer.DisposeAsync();
                if (_gzipStream != null) await _gzipStream.DisposeAsync();
                if (_cryptoStream != null) await _cryptoStream.DisposeAsync();
                await _fileStream.DisposeAsync();
            }
        }

        private class SecureSpillReader : ISpillReader
        {
            private readonly FileStream? _fileStream;
            private readonly CryptoStream? _cryptoStream;
            private readonly GZipStream? _gzipStream;
            private readonly StreamReader? _reader;
            private readonly string _chunkName;

            public string ChunkName => _chunkName;

            public SecureSpillReader(string path, string chunkName, byte[] key, bool encrypt, bool compress)
            {
                _chunkName = chunkName;
                if (!File.Exists(path))
                {
                    _fileStream = null!;
                    return;
                }

                _fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                Stream current = _fileStream;

                if (encrypt)
                {
                    byte[] iv = new byte[16];
                    _fileStream.ReadExactly(iv, 0, 16);

                    using var aes = Aes.Create();
                    var decryptor = aes.CreateDecryptor(key, iv);
                    _cryptoStream = new CryptoStream(_fileStream, decryptor, CryptoStreamMode.Read);
                    current = _cryptoStream;
                }

                if (compress)
                {
                    _gzipStream = new GZipStream(current, CompressionMode.Decompress);
                    current = _gzipStream;
                }

                _reader = new StreamReader(current, Encoding.UTF8);
            }

            public async Task<Row?> ReadRowAsync()
            {
                if (_reader == null) return null;
                var line = await _reader.ReadLineAsync();
                if (line == null) return null;

                var cols = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(line);
                if (cols == null) return null;

                var row = new Row();
                foreach (var kvp in cols) row[kvp.Key] = UnwrapJsonValue(kvp.Value);
                return row;
            }

            public async IAsyncEnumerable<Row> AsEnumerableAsync()
            {
                while (true)
                {
                    var row = await ReadRowAsync();
                    if (row == null) break;
                    yield return row;
                }
            }

            private static object? UnwrapJsonValue(JsonElement element) =>
                element.ValueKind switch
                {
                    JsonValueKind.Number => element.TryGetDecimal(out var d) ? d : (object?)element.GetDouble(),
                    JsonValueKind.String => decimal.TryParse(element.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d) ? (object?)d : (DateTime.TryParse(element.GetString() ?? "", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt : (object?)element.GetString()),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null,
                    JsonValueKind.Array => element,  // Preserve array for downstream unwrapping
                    JsonValueKind.Object => element, // Preserve object
                    _ => element.GetRawText()
                };

            public async ValueTask DisposeAsync()
            {
                if (_reader != null) _reader.Dispose();
                if (_gzipStream != null) await _gzipStream.DisposeAsync();
                if (_cryptoStream != null) await _cryptoStream.DisposeAsync();
                if (_fileStream != null) await _fileStream.DisposeAsync();
            }
        }

        // ── Arrow IPC ────────────────────────────────────────────────────────────

        private class ArrowSpillWriter : ISpillWriter
        {
            private readonly FileStream _fileStream;
            private readonly CryptoStream? _cryptoStream;
            private readonly GZipStream? _gzipStream;
            private readonly Stream _payloadStream;
            private readonly IExecutionContext _context;
            private readonly string _chunkName;
            private readonly int _flushBatchSize;
            private readonly string _filePath;

            private const string JsonPrefix = "\x1Ejson:";

            private Schema? _schema;
            private ArrowStreamWriter? _arrowWriter;
            private readonly List<Row> _buffer = new();

            public string ChunkName => _chunkName;

            public ArrowSpillWriter(string path, string chunkName, byte[] key, IExecutionContext context, bool encrypt, bool compress)
            {
                _chunkName = chunkName;
                _context = context;
                _filePath = path;
                _flushBatchSize = Math.Max(1, context.BatchSize);
                _fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);

                Stream current = _fileStream;

                if (encrypt)
                {
                    using var aes = Aes.Create();
                    aes.Key = key;
                    aes.GenerateIV();
                    _fileStream.Write(aes.IV, 0, aes.IV.Length);
                    var encryptor = aes.CreateEncryptor(key, aes.IV);
                    _cryptoStream = new CryptoStream(_fileStream, encryptor, CryptoStreamMode.Write);
                    current = _cryptoStream;
                }

                if (compress)
                {
                    _gzipStream = new GZipStream(current, CompressionLevel.Optimal);
                    current = _gzipStream;
                }

                _payloadStream = current;
            }

            public async Task WriteRowAsync(Row row)
            {
                // Snapshot columns immediately — callers may mutate the row after WriteRowAsync returns
                // (e.g. PartitionStreamMultiSet stamps __SET_IDX on the same Row object across iterations).
                var snapshot = SnapshotRow(row);

                if (_schema == null)
                {
                    _schema = InferSchema(snapshot);
                    _arrowWriter = new ArrowStreamWriter(_payloadStream, _schema, leaveOpen: true);
                    await _arrowWriter.WriteStartAsync(CancellationToken.None);
                }

                _buffer.Add(snapshot);

                if (_context.TelemetryEnabled)
                {
                    long inc = row.Columns.Count * 16L;
                    _context.TotalSpilledBytes += inc;
                    if (_context.IsVerbose) _context.Logger.WriteLine($"DEBUG: Spilled {inc} bytes. Total: {_context.TotalSpilledBytes}");
                }

                if (_buffer.Count >= _flushBatchSize)
                    await FlushBatchAsync();
            }

            private static Row SnapshotRow(Row row)
            {
                var copy = new Row();
                foreach (var kvp in row.Columns) copy[kvp.Key] = kvp.Value;
                return copy;
            }

            public async Task WriteRowsAsync(IEnumerable<Row> rows)
            {
                foreach (var r in rows) await WriteRowAsync(r);
            }

            private async Task FlushBatchAsync()
            {
                if (_buffer.Count == 0 || _arrowWriter == null) return;
                var batch = BuildBatch(_buffer);
                await _arrowWriter.WriteRecordBatchAsync(batch);
                _buffer.Clear();
            }

            private static Schema InferSchema(Row row)
            {
                var fields = row.Columns
                    .Select(kvp => new Field(kvp.Key, GetArrowType(kvp.Value), nullable: true))
                    .ToList();
                return new Schema(fields, metadata: null);
            }

            private static IArrowType GetArrowType(object? value) => value switch
            {
                int or long or short or byte or uint or ulong or ushort or sbyte => Int64Type.Default,
                decimal => new Decimal128Type(29, 9),
                double or float => DoubleType.Default,
                bool => BooleanType.Default,
                DateTime => new TimestampType(TimeUnit.Microsecond, "UTC"),
                _ => StringType.Default
            };

            private RecordBatch BuildBatch(List<Row> rows)
            {
                var arrays = _schema!.FieldsList
                    .Select(f => BuildArray(f, rows))
                    .ToList();
                return new RecordBatch(_schema, arrays, rows.Count);
            }

            private static IArrowArray BuildArray(Field field, List<Row> rows)
            {
                switch (field.DataType)
                {
                    case Int64Type:
                    {
                        var b = new Int64Array.Builder();
                        b.Reserve(rows.Count);
                        foreach (var row in rows)
                        {
                            var v = row.Columns.TryGetValue(field.Name, out var val) ? val : null;
                            if (v == null) b.AppendNull();
                            else b.Append(Convert.ToInt64(v));
                        }
                        return b.Build();
                    }
                    case Decimal128Type dt:
                    {
                        var b = new Decimal128Array.Builder(dt);
                        b.Reserve(rows.Count);
                        foreach (var row in rows)
                        {
                            var v = row.Columns.TryGetValue(field.Name, out var val) ? val : null;
                            if (v == null) b.AppendNull();
                            else b.Append(Convert.ToDecimal(v));
                        }
                        return b.Build();
                    }
                    case DoubleType:
                    {
                        var b = new DoubleArray.Builder();
                        b.Reserve(rows.Count);
                        foreach (var row in rows)
                        {
                            var v = row.Columns.TryGetValue(field.Name, out var val) ? val : null;
                            if (v == null) b.AppendNull();
                            else b.Append(Convert.ToDouble(v));
                        }
                        return b.Build();
                    }
                    case BooleanType:
                    {
                        var b = new BooleanArray.Builder();
                        b.Reserve(rows.Count);
                        foreach (var row in rows)
                        {
                            var v = row.Columns.TryGetValue(field.Name, out var val) ? val : null;
                            if (v == null) b.AppendNull();
                            else b.Append(Convert.ToBoolean(v));
                        }
                        return b.Build();
                    }
                    case TimestampType tt:
                    {
                        var b = new TimestampArray.Builder(tt.Unit, tt.Timezone);
                        b.Reserve(rows.Count);
                        foreach (var row in rows)
                        {
                            var v = row.Columns.TryGetValue(field.Name, out var val) ? val : null;
                            if (v == null) b.AppendNull();
                            else
                            {
                                var dt = Convert.ToDateTime(v);
                                var dto = dt.Kind == DateTimeKind.Unspecified
                                    ? new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc))
                                    : new DateTimeOffset(dt);
                                b.Append(dto);
                            }
                        }
                        return b.Build();
                    }
                    default:
                    {
                        var b = new StringArray.Builder();
                        b.Reserve(rows.Count);
                        foreach (var row in rows)
                        {
                            var v = row.Columns.TryGetValue(field.Name, out var val) ? val : null;
                            if (v == null) b.AppendNull();
                            else if (v is string s) b.Append(s);
                            else b.Append(JsonPrefix + JsonSerializer.Serialize(v));
                        }
                        return b.Build();
                    }
                }
            }

            public async ValueTask DisposeAsync()
            {
                await FlushBatchAsync();
                if (_arrowWriter != null)
                {
                    await _arrowWriter.WriteEndAsync(CancellationToken.None);
                    _arrowWriter.Dispose();
                }
                if (_gzipStream != null) await _gzipStream.DisposeAsync();
                if (_cryptoStream != null) await _cryptoStream.DisposeAsync();
                await _fileStream.DisposeAsync();

                // Empty partition: delete the file so the reader sees "not found" → null
                if (_arrowWriter == null)
                {
                    try { File.Delete(_filePath); } catch { }
                }
            }
        }

        private class ArrowSpillReader : ISpillReader
        {
            private readonly FileStream? _fileStream;
            private readonly CryptoStream? _cryptoStream;
            private readonly GZipStream? _gzipStream;
            private readonly ArrowStreamReader? _arrowReader;
            private readonly string _chunkName;

            private RecordBatch? _currentBatch;
            private int _currentBatchRow;

            public string ChunkName => _chunkName;

            public ArrowSpillReader(string path, string chunkName, byte[] key, bool encrypt, bool compress)
            {
                _chunkName = chunkName;
                if (!File.Exists(path))
                {
                    _fileStream = null!;
                    return;
                }

                _fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                Stream current = _fileStream;

                if (encrypt)
                {
                    byte[] iv = new byte[16];
                    _fileStream.ReadExactly(iv, 0, 16);
                    using var aes = Aes.Create();
                    var decryptor = aes.CreateDecryptor(key, iv);
                    _cryptoStream = new CryptoStream(_fileStream, decryptor, CryptoStreamMode.Read);
                    current = _cryptoStream;
                }

                if (compress)
                {
                    _gzipStream = new GZipStream(current, CompressionMode.Decompress);
                    current = _gzipStream;
                }

                _arrowReader = new ArrowStreamReader(current);
            }

            public async Task<Row?> ReadRowAsync()
            {
                if (_arrowReader == null) return null;

                while (true)
                {
                    if (_currentBatch != null && _currentBatchRow < _currentBatch.Length)
                        return ExtractRow(_currentBatch, _currentBatchRow++);

                    _currentBatch?.Dispose();
                    _currentBatch = await _arrowReader.ReadNextRecordBatchAsync();
                    if (_currentBatch == null) return null;
                    _currentBatchRow = 0;
                }
            }

            public async IAsyncEnumerable<Row> AsEnumerableAsync()
            {
                while (true)
                {
                    var row = await ReadRowAsync();
                    if (row == null) break;
                    yield return row;
                }
            }

            private static Row ExtractRow(RecordBatch batch, int rowIndex)
            {
                var row = new Row();
                for (int i = 0; i < batch.Schema.FieldsList.Count; i++)
                {
                    var field = batch.Schema.FieldsList[i];
                    row[field.Name] = ExtractValue(batch.Column(i), rowIndex);
                }
                return row;
            }

            private static object? ExtractValue(IArrowArray array, int index)
            {
                if (array.IsNull(index)) return null;

                return array switch
                {
                    Int64Array a    => (object?)(decimal?)a.GetValue(index),
                    Decimal128Array a => (object?)a.GetValue(index),
                    DoubleArray a   => ToDecimalOrDouble(a.GetValue(index)),
                    BooleanArray a  => (object?)a.GetValue(index),
                    TimestampArray a => (object?)a.GetTimestamp(index)?.UtcDateTime,
                    StringArray a   => (object?)DecodeArrowString(a.GetString(index)),
                    _ => null
                };
            }

            private const string JsonPrefix = "\x1Ejson:";

            private static object? DecodeArrowString(string? s)
            {
                if (s == null) return null;
                if (s.StartsWith(JsonPrefix, StringComparison.Ordinal))
                {
                    try { return JsonSerializer.Deserialize<JsonElement>(s.AsSpan(JsonPrefix.Length)); }
                    catch { }
                }
                // Match JSON reader: try numeric then date parse on plain strings
                if (decimal.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d)) return d;
                if (DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var dt)) return dt;
                return s;
            }

            private static object? ToDecimalOrDouble(double? val)
            {
                if (val == null) return null;
                try { return (decimal)val.Value; }
                catch { return (object?)val.Value; }
            }

            public async ValueTask DisposeAsync()
            {
                _currentBatch?.Dispose();
                _arrowReader?.Dispose();
                if (_gzipStream != null) await _gzipStream.DisposeAsync();
                if (_cryptoStream != null) await _cryptoStream.DisposeAsync();
                if (_fileStream != null) await _fileStream.DisposeAsync();
            }
        }
    }
}
