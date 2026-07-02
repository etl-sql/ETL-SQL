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
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Spill;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Spill;

/// <summary>
/// Implements encrypted, compressed storage for spilling large data sets to disk.
/// This implementation is session-aware and dynamically reacts to changes in
/// SessionId or SessionRoot after initialization.
/// </summary>
public partial class SpillStore : ISpillStore
{
    private static object? UnwrapJsonValue(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetDecimal(out var d) ? d : (object?)element.GetDouble(),
            JsonValueKind.String => decimal.TryParse(element.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d) ? (object?)d : (DateTime.TryParse(element.GetString() ?? "", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt : (object?)element.GetString()),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => element,
            JsonValueKind.Object => element,
            _ => element.GetRawText()
        };
    private string? _cachedRootPath;
    private byte[]? _cachedSessionKey;
    private string? _cachedSessionId;
    private bool _usingTemporaryFallback;
    private readonly object _initLock = new();
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
        if (CacheMatchesContext())
        {
            return;
        }

        lock (_initLock)
        {
            if (CacheMatchesContext())
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
                _cachedSessionKey = NormalizeAesKey(
                    _context.SessionStateManager.GetSpillKey(_context.SessionId ?? "DEFAULT"),
                    _context.SessionId ?? "DEFAULT");
                _usingTemporaryFallback = false;
            }
            else if (_cachedRootPath == null || IsPersistent != _context.IsPersistentSession)
            {
                // Disposable temp path
                _cachedRootPath = Path.Combine(Path.GetTempPath(), "ETL-SQL-Spill", Guid.NewGuid().ToString("N"));
                _cachedSessionKey = RandomNumberGenerator.GetBytes(32);
                _usingTemporaryFallback = false;
            }

            try
            {
                if (!Directory.Exists(_cachedRootPath))
                {
                    Directory.CreateDirectory(_cachedRootPath!);
                }
            }
            catch (UnauthorizedAccessException ex) when (IsPersistent)
            {
                _context.Logger.Warning("Persistent spill directory is not writable for session {SessionId}: {Message}. Falling back to temporary spill storage.", _context.SessionId, ex.Message);
                IsPersistent = false;
                _usingTemporaryFallback = true;
                _cachedRootPath = Path.Combine(Path.GetTempPath(), "ETL-SQL-Spill", Guid.NewGuid().ToString("N"));
                _cachedSessionKey = RandomNumberGenerator.GetBytes(32);
                Directory.CreateDirectory(_cachedRootPath);
            }
        }
    }

    private bool CacheMatchesContext()
    {
        if (_cachedRootPath == null) return false;

        if (_usingTemporaryFallback)
        {
            return _context.IsPersistentSession && _cachedSessionId == _context.SessionId;
        }

        return IsPersistent == _context.IsPersistentSession &&
            (!IsPersistent || _cachedSessionId == _context.SessionId);
    }

    private static byte[] NormalizeAesKey(byte[]? key, string sessionId)
    {
        if (key is { Length: 16 or 24 or 32 }) return key;

        using var sha256 = SHA256.Create();
        if (key is { Length: > 0 })
            return sha256.ComputeHash(key);

        return sha256.ComputeHash(Encoding.UTF8.GetBytes("ETL-SQL-SPILL:" + sessionId));
    }

    public async Task<ISpillWriter> CreateWriterAsync(string chunkName)
    {
        EnsureInitialized();
        var path = Path.Combine(_cachedRootPath!, chunkName);
        var encrypt = _context.SpillEncryptionEnabled;
        var compress = _context.SpillCompressionEnabled;

        ISpillWriter writer;
        if (_context.SpillFormat == "Json")
            writer = new SecureSpillWriter(path, chunkName, _cachedSessionKey!, _context, encrypt, compress);
        else
            writer = new ArrowSpillWriter(path, chunkName, _cachedSessionKey!, _context, encrypt, compress);

        var telemetry = _context.Telemetry;
        if (telemetry != null)
        {
            lock (telemetry)
                telemetry.SpillExtentCount++;
        }
        return await Task.FromResult(writer);
    }

    public async Task<ISpillReader> CreateReaderAsync(string chunkName)
    {
        EnsureInitialized();
        var path = Path.Combine(_cachedRootPath!, chunkName);
        var encrypt = _context.SpillEncryptionEnabled;
        var compress = _context.SpillCompressionEnabled;

        ISpillReader reader;
        if (_context.SpillFormat == "Json")
        {
            var jsonReader = new SecureSpillReader(path, chunkName, _cachedSessionKey!, encrypt, compress);
            await jsonReader.InitializeAsync();
            reader = jsonReader;
        }
        else
        {
            var arrowReader = new ArrowSpillReader(path, chunkName, _cachedSessionKey!, encrypt, compress);
            await arrowReader.InitializeAsync();
            reader = arrowReader;
        }

        if (File.Exists(path))
        {
            var telemetry = _context.Telemetry;
            if (telemetry != null)
            {
                lock (telemetry)
                    telemetry.SpillReadBytes += new FileInfo(path).Length;
            }
        }
        return reader;
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
                Directory.Delete(_cachedRootPath!, true);
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
        public long BytesWritten { get; private set; }

        public SecureSpillWriter(string path, string chunkName, byte[] key, IExecutionContext context, bool encrypt, bool compress)
        {
            _chunkName = chunkName;
            _context = context;
            _fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);

            try
            {
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
            catch
            {
                _fileStream.Dispose();
                throw;
            }
        }

        public async Task WriteRowAsync(Row row)
        {
            var dict = row.Columns;
            // Expand schema aliases into real entries so qualified names survive JSON round-trip.
            if (row.Schema != null)
            {
                for (int i = 0; i < row.Schema.ColumnCount; i++)
                {
                    var canon = row.Schema.GetName(i);
                    foreach (var alias in row.Schema.EnumerateAliasesOf(canon))
                        dict.TryAdd(alias, row[i]);
                }
            }
            var json = JsonSerializer.Serialize(dict);
            if (_context.Telemetry?.TelemetryEnabled ?? false)
            {
                // Fast approximation (+2 for newline)
                long inc = json.Length + 2;
                _context.Telemetry.TotalSpilledBytes += inc;
                BytesWritten += inc;
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
        private readonly string _path;
        private readonly byte[] _key;
        private readonly bool _encrypt;
        private readonly bool _compress;
        private readonly string _chunkName;
        private FileStream? _fileStream;
        private CryptoStream? _cryptoStream;
        private GZipStream? _gzipStream;
        private StreamReader? _reader;

        public string ChunkName => _chunkName;

        public SecureSpillReader(string path, string chunkName, byte[] key, bool encrypt, bool compress)
        {
            _path = path;
            _chunkName = chunkName;
            _key = key;
            _encrypt = encrypt;
            _compress = compress;
        }

        public async Task InitializeAsync()
        {
            if (!File.Exists(_path))
            {
                _fileStream = null;
                return;
            }

            _fileStream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            try
            {
                Stream current = _fileStream;

                if (_encrypt)
                {
                    byte[] iv = new byte[16];
                    await _fileStream.ReadExactlyAsync(iv, 0, 16);

                    using var aes = Aes.Create();
                    var decryptor = aes.CreateDecryptor(_key, iv);
                    _cryptoStream = new CryptoStream(_fileStream, decryptor, CryptoStreamMode.Read);
                    current = _cryptoStream;
                }

                if (_compress)
                {
                    _gzipStream = new GZipStream(current, CompressionMode.Decompress);
                    current = _gzipStream;
                }

                _reader = new StreamReader(current, Encoding.UTF8);
            }
            catch
            {
                if (_fileStream != null)
                {
                    await _fileStream.DisposeAsync();
                    _fileStream = null;
                }
                throw;
            }
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

        public async ValueTask DisposeAsync()
        {
            if (_reader != null) _reader.Dispose();
            if (_gzipStream != null) await _gzipStream.DisposeAsync();
            if (_cryptoStream != null) await _cryptoStream.DisposeAsync();
            if (_fileStream != null) await _fileStream.DisposeAsync();
        }
    }

    // ── Arrow IPC ────────────────────────────────────────────────────────────

    private class ArrowSpillWriter : ISpillWriter, IColumnarSpillWriter
    {
        private const string SchemaVersionKey = "etlsql.spill.schema_version";
        private const string LogicalTypeKeyPrefix = "etlsql.spill.column.";
        private const string SchemaVersion = "1";
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
        public long BytesWritten { get; private set; }

        public ArrowSpillWriter(string path, string chunkName, byte[] key, IExecutionContext context, bool encrypt, bool compress)
        {
            _chunkName = chunkName;
            _context = context;
            _filePath = path;
            _flushBatchSize = Math.Max(1, context.BatchSize);
            _fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);

            try
            {
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
            catch
            {
                _fileStream.Dispose();
                throw;
            }
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

            if (_context.Telemetry?.TelemetryEnabled ?? false)
            {
                long inc = row.Columns.Count * 16L;
                _context.Telemetry.TotalSpilledBytes += inc;
                BytesWritten += inc;
            }

            if (_buffer.Count >= _flushBatchSize)
                await FlushBatchAsync();
        }

        private static Row SnapshotRow(Row row)
        {
            var copy = new Row();
            foreach (var kvp in row.Columns) copy[kvp.Key] = kvp.Value;
            // Expand schema aliases (e.g. "R.Id" → slot 0) into real dynamic entries so they
            // survive Arrow serialization. row.Columns only returns canonical names; aliases are
            // lost on round-trip, breaking join conditions that reference qualified names.
            if (row.Schema != null)
            {
                for (int i = 0; i < row.Schema.ColumnCount; i++)
                {
                    var canon = row.Schema.GetName(i);
                    foreach (var alias in row.Schema.EnumerateAliasesOf(canon))
                        if (!copy.HasColumn(alias))
                            copy[alias] = row[i];
                }
            }
            return copy;
        }

        public async Task WriteRowsAsync(IEnumerable<Row> rows)
        {
            long telemetryBytes = 0;
            foreach (var row in rows)
            {
                var snapshot = SnapshotRow(row);
                if (_schema == null)
                {
                    _schema = InferSchema(snapshot);
                    _arrowWriter = new ArrowStreamWriter(_payloadStream, _schema, leaveOpen: true);
                    await _arrowWriter.WriteStartAsync(CancellationToken.None);
                }

                _buffer.Add(snapshot);
                telemetryBytes += row.Columns.Count * 16L;
                if (_buffer.Count >= _flushBatchSize)
                    await FlushBatchAsync();
            }

            if ((_context.Telemetry?.TelemetryEnabled ?? false) && telemetryBytes > 0)
            {
                _context.Telemetry.TotalSpilledBytes += telemetryBytes;
                BytesWritten += telemetryBytes;
            }
        }

        public async Task WriteBatchAsync(ColumnBatch batch)
        {
            ArgumentNullException.ThrowIfNull(batch);
            await FlushBatchAsync();
            if (_schema == null)
            {
                _schema = InferSchema(batch);
                _arrowWriter = new ArrowStreamWriter(_payloadStream, _schema, leaveOpen: true);
                await _arrowWriter.WriteStartAsync(CancellationToken.None);
            }
            else
            {
                ValidateSchema(batch);
            }

            using var recordBatch = BuildBatch(batch);
            await _arrowWriter!.WriteRecordBatchAsync(recordBatch);
            if (_context.Telemetry?.TelemetryEnabled ?? false)
            {
                var increment = batch.AllocatedBytes;
                _context.Telemetry.TotalSpilledBytes += increment;
                BytesWritten += increment;
            }
        }

        private async Task FlushBatchAsync()
        {
            if (_buffer.Count == 0 || _arrowWriter == null) return;
            using var batch = BuildBatch(_buffer);
            await _arrowWriter.WriteRecordBatchAsync(batch);
            _buffer.Clear();
        }

        private static Schema InferSchema(Row row)
        {
            var columns = row.Columns.ToList();
            var fields = columns
                .Select(kvp => new Field(kvp.Key, GetArrowType(kvp.Value), nullable: true))
                .ToList();
            var metadata = new Dictionary<string, string>
            {
                [SchemaVersionKey] = SchemaVersion
            };
            for (var i = 0; i < columns.Count; i++)
                metadata[$"{LogicalTypeKeyPrefix}{i}.logical_type"] = GetLogicalType(columns[i].Value);
            return new Schema(fields, metadata);
        }

        private static Schema InferSchema(ColumnBatch batch)
        {
            var fields = batch.Schema.Fields
                .Select(field => new Field(field.Name, GetArrowType(field.ElementType), field.IsNullable))
                .ToList();
            var metadata = new Dictionary<string, string>
            {
                [SchemaVersionKey] = SchemaVersion
            };
            for (var i = 0; i < batch.Schema.Count; i++)
                metadata[$"{LogicalTypeKeyPrefix}{i}.logical_type"] = batch.Schema.Fields[i].LogicalType;
            return new Schema(fields, metadata);
        }

        private static IArrowType GetArrowType(Type elementType)
        {
            if (elementType == typeof(byte) || elementType == typeof(short)
                || elementType == typeof(int) || elementType == typeof(long)) return Int64Type.Default;
            if (elementType == typeof(decimal)) return new Decimal128Type(29, 9);
            if (elementType == typeof(double) || elementType == typeof(float)) return DoubleType.Default;
            if (elementType == typeof(bool)) return BooleanType.Default;
            if (elementType == typeof(DateTime)) return new TimestampType(TimeUnit.Microsecond, "UTC");
            // Arrow timestamps preserve an instant but not the original DateTimeOffset offset. Store
            // the round-trip representation as UTF-8 so spill/reload retains both pieces of information.
            if (elementType == typeof(DateTimeOffset)) return StringType.Default;
            if (elementType == typeof(string)) return StringType.Default;
            throw new NotSupportedException($"Native spill writing does not support '{elementType.Name}' columns.");
        }

        private void ValidateSchema(ColumnBatch batch)
        {
            if (_schema!.FieldsList.Count != batch.Schema.Count)
                throw new InvalidOperationException("Column batch does not match the spill writer schema.");
            for (var i = 0; i < batch.Schema.Count; i++)
            {
                var field = batch.Schema.Fields[i];
                var expectedType = GetArrowType(field.ElementType);
                if (!_schema.FieldsList[i].Name.Equals(field.Name, StringComparison.OrdinalIgnoreCase)
                    || _schema.FieldsList[i].DataType.TypeId != expectedType.TypeId)
                    throw new InvalidOperationException(
                        $"Column batch field {i} ('{field.Name}', {expectedType.Name}) does not match " +
                        $"spill field '{_schema.FieldsList[i].Name}' ({_schema.FieldsList[i].DataType.Name}).");
            }
        }

        private static string GetLogicalType(object? value) => value switch
        {
            null => "Dynamic",
            string => "String",
            int or long or short or byte or uint or ulong or ushort or sbyte => "Integer",
            decimal => "Decimal",
            double or float => "Double",
            bool => "Boolean",
            DateTime => "Timestamp",
            DateTimeOffset => "DateTimeOffset",
            _ => "Json"
        };

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

        private RecordBatch BuildBatch(ColumnBatch batch)
        {
            var arrays = batch.Columns.Select(BuildArray).ToList();
            return new RecordBatch(_schema!, arrays, batch.RowCount);
        }

        private static IArrowArray BuildArray(IColumnBuffer column)
        {
            if (column is ColumnBuffer<byte> bytes) return BuildInt64(bytes);
            if (column is ColumnBuffer<short> shorts) return BuildInt64(shorts);
            if (column is ColumnBuffer<int> integers) return BuildInt64(integers);
            if (column is ColumnBuffer<long> longs) return BuildInt64(longs);
            if (column is ColumnBuffer<decimal> decimals)
            {
                var type = new Decimal128Type(29, 9);
                var builder = new Decimal128Array.Builder(type);
                for (var i = 0; i < decimals.Count; i++)
                    if (decimals.IsNull(i)) builder.AppendNull(); else builder.Append(Math.Round(decimals.Values.Span[i], type.Scale));
                return builder.Build();
            }
            if (column is ColumnBuffer<double> doubles)
            {
                var builder = new DoubleArray.Builder();
                for (var i = 0; i < doubles.Count; i++)
                    if (doubles.IsNull(i)) builder.AppendNull(); else builder.Append(doubles.Values.Span[i]);
                return builder.Build();
            }
            if (column is ColumnBuffer<bool> booleans)
            {
                var builder = new BooleanArray.Builder();
                for (var i = 0; i < booleans.Count; i++)
                    if (booleans.IsNull(i)) builder.AppendNull(); else builder.Append(booleans.Values.Span[i]);
                return builder.Build();
            }
            if (column is ColumnBuffer<DateTime> dates)
            {
                var builder = new TimestampArray.Builder(TimeUnit.Microsecond, "UTC");
                for (var i = 0; i < dates.Count; i++)
                {
                    if (dates.IsNull(i)) builder.AppendNull();
                    else
                    {
                        var value = dates.Values.Span[i];
                        var offset = value.Kind == DateTimeKind.Unspecified
                            ? new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc))
                            : new DateTimeOffset(value);
                        builder.Append(offset);
                    }
                }
                return builder.Build();
            }
            if (column is ColumnBuffer<DateTimeOffset> offsets)
            {
                var builder = new StringArray.Builder();
                for (var i = 0; i < offsets.Count; i++)
                    if (offsets.IsNull(i)) builder.AppendNull();
                    else builder.Append(offsets.Values.Span[i].ToString("O", System.Globalization.CultureInfo.InvariantCulture));
                return builder.Build();
            }
            if (column is Utf8ColumnBuffer strings)
            {
                var builder = new StringArray.Builder();
                for (var i = 0; i < strings.Count; i++)
                    if (strings.IsNull(i)) builder.AppendNull(); else builder.Append(System.Text.Encoding.UTF8.GetString(strings.GetUtf8Bytes(i)));
                return builder.Build();
            }
            throw new NotSupportedException($"Native spill writing does not support '{column.ElementType.Name}' columns.");
        }

        private static Int64Array BuildInt64<T>(ColumnBuffer<T> column) where T : unmanaged
        {
            var builder = new Int64Array.Builder();
            for (var i = 0; i < column.Count; i++)
                if (column.IsNull(i)) builder.AppendNull(); else builder.Append(Convert.ToInt64(column.Values.Span[i]));
            return builder.Build();
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
                            var v = row.TryGetValue(field.Name, out var val) ? val : null;
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
                            var v = row.TryGetValue(field.Name, out var val) ? val : null;
                            if (v == null) b.AppendNull();
                            else b.Append(Math.Round(Convert.ToDecimal(v), dt.Scale));
                        }
                        return b.Build();
                    }
                case DoubleType:
                    {
                        var b = new DoubleArray.Builder();
                        b.Reserve(rows.Count);
                        foreach (var row in rows)
                        {
                            var v = row.TryGetValue(field.Name, out var val) ? val : null;
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
                            var v = row.TryGetValue(field.Name, out var val) ? val : null;
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
                            var v = row.TryGetValue(field.Name, out var val) ? val : null;
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
                            var v = row.TryGetValue(field.Name, out var val) ? val : null;
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

    private class ArrowSpillReader : ISpillReader, IColumnarSpillReader
    {
        private const string SchemaVersionKey = "etlsql.spill.schema_version";
        private const string LogicalTypeKeyPrefix = "etlsql.spill.column.";
        private readonly string _path;
        private readonly byte[] _key;
        private readonly bool _encrypt;
        private readonly bool _compress;
        private readonly string _chunkName;
        private FileStream? _fileStream;
        private CryptoStream? _cryptoStream;
        private GZipStream? _gzipStream;
        private ArrowStreamReader? _arrowReader;

        private RecordBatch? _currentBatch;
        private int _currentBatchRow;
        private SpillReadMode _readMode;

        public string ChunkName => _chunkName;

        public ArrowSpillReader(string path, string chunkName, byte[] key, bool encrypt, bool compress)
        {
            _path = path;
            _chunkName = chunkName;
            _key = key;
            _encrypt = encrypt;
            _compress = compress;
        }

        public async Task InitializeAsync()
        {
            if (!File.Exists(_path))
            {
                _fileStream = null;
                return;
            }

            _fileStream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            try
            {
                Stream current = _fileStream;

                if (_encrypt)
                {
                    byte[] iv = new byte[16];
                    await _fileStream.ReadExactlyAsync(iv, 0, 16);
                    using var aes = Aes.Create();
                    var decryptor = aes.CreateDecryptor(_key, iv);
                    _cryptoStream = new CryptoStream(_fileStream, decryptor, CryptoStreamMode.Read);
                    current = _cryptoStream;
                }

                if (_compress)
                {
                    _gzipStream = new GZipStream(current, CompressionMode.Decompress);
                    current = _gzipStream;
                }

                _arrowReader = new ArrowStreamReader(current);
            }
            catch
            {
                if (_fileStream != null)
                {
                    await _fileStream.DisposeAsync();
                    _fileStream = null;
                }
                throw;
            }
        }

        public async Task<Row?> ReadRowAsync()
        {
            EnterReadMode(SpillReadMode.Rows);
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
                var logicalType = GetLogicalType(batch.Schema, i);
                row[field.Name] = ExtractValue(batch.Column(i), rowIndex, logicalType);
            }
            return row;
        }

        public async IAsyncEnumerable<ColumnBatch> AsColumnBatchesAsync()
        {
            EnterReadMode(SpillReadMode.Columns);
            if (_arrowReader == null) yield break;

            while (await _arrowReader.ReadNextRecordBatchAsync() is { } batch)
            {
                using (batch)
                    yield return ConvertBatch(batch);
            }
        }

        private void EnterReadMode(SpillReadMode mode)
        {
            if (_readMode != SpillReadMode.None && _readMode != mode)
                throw new InvalidOperationException("A spill reader cannot mix row and column-batch consumption.");
            _readMode = mode;
        }

        private static ColumnBatch ConvertBatch(RecordBatch batch)
        {
            var fields = new List<ColumnBatchField>(batch.Schema.FieldsList.Count);
            var columns = new List<IColumnBuffer>(batch.Schema.FieldsList.Count);
            try
            {
                for (var i = 0; i < batch.Schema.FieldsList.Count; i++)
                {
                    var field = batch.Schema.FieldsList[i];
                    var logicalType = GetLogicalType(batch.Schema, i) ?? field.DataType.Name;
                    var column = ConvertArray(batch.Column(i), batch.Length, logicalType);
                    fields.Add(new ColumnBatchField(field.Name, column.ElementType, logicalType, field.IsNullable));
                    columns.Add(column);
                }
                return new ColumnBatch(new ColumnBatchSchema(fields), columns, batch.Length);
            }
            catch
            {
                foreach (var column in columns) column.Dispose();
                throw;
            }
        }

        private static IColumnBuffer ConvertArray(IArrowArray array, int count, string logicalType) => array switch
        {
            Int64Array values => CopyFixed<long>(count, values.IsNull, i => values.GetValue(i) ?? default),
            Decimal128Array values => CopyFixed<decimal>(count, values.IsNull, i => values.GetValue(i) ?? default),
            DoubleArray values => CopyFixed<double>(count, values.IsNull, i => values.GetValue(i) ?? default),
            BooleanArray values => CopyFixed<bool>(count, values.IsNull, i => values.GetValue(i) ?? default),
            TimestampArray values => CopyFixed<DateTime>(count, values.IsNull,
                i => values.GetTimestamp(i)?.UtcDateTime ?? default),
            StringArray values when IsDateTimeOffsetLogicalType(logicalType)
                => CopyFixed<DateTimeOffset>(count, values.IsNull,
                    i => ParseDateTimeOffsetString(values.GetString(i)!)),
            StringArray values => Utf8ColumnBuffer.CopyEncoded(
                values.ValueOffsets,
                values.Values,
                values.NullBitmapBuffer.Span,
                values.Offset),
            _ => throw new NotSupportedException($"Arrow spill column type '{array.Data.DataType.Name}' is not supported by native batches.")
        };

        private static ColumnBuffer<T> CopyFixed<T>(
            int count,
            Func<int, bool> isNull,
            Func<int, T> getValue) where T : unmanaged
        {
            var buffer = ColumnBuffer<T>.Rent(count);
            try
            {
                var values = buffer.Values.Span;
                for (var i = 0; i < count; i++)
                {
                    if (isNull(i)) buffer.SetNull(i);
                    else values[i] = getValue(i);
                }
                return buffer;
            }
            catch
            {
                buffer.Dispose();
                throw;
            }
        }

        private static string? GetLogicalType(Schema schema, int columnIndex)
        {
            if (schema.Metadata == null
                || !schema.Metadata.TryGetValue(SchemaVersionKey, out var version)
                || version != "1")
                return null;
            return schema.Metadata.TryGetValue(
                $"{LogicalTypeKeyPrefix}{columnIndex}.logical_type", out var logicalType)
                ? logicalType
                : null;
        }

        private static object? ExtractValue(IArrowArray array, int index, string? logicalType)
        {
            if (array.IsNull(index)) return null;

            return array switch
            {
                Int64Array a => (object?)(decimal?)a.GetValue(index),
                Decimal128Array a => (object?)a.GetValue(index),
                DoubleArray a => ToDecimalOrDouble(a.GetValue(index)),
                BooleanArray a => (object?)a.GetValue(index),
                TimestampArray a => (object?)a.GetTimestamp(index)?.UtcDateTime,
                StringArray a when IsDateTimeOffsetLogicalType(logicalType)
                    => ParseDateTimeOffsetString(a.GetString(index)!),
                StringArray a => (object?)DecodeArrowString(a.GetString(index), logicalType),
                _ => null
            };
        }

        private const string JsonPrefix = "\x1Ejson:";

        private static object? DecodeArrowString(string? s, string? logicalType)
        {
            if (s == null) return null;
            if (logicalType == "String") return s;
            if (IsDateTimeOffsetLogicalType(logicalType))
                return ParseDateTimeOffsetString(s);
            if (s.StartsWith(JsonPrefix, StringComparison.Ordinal))
            {
                try
                {
                    var element = JsonSerializer.Deserialize<JsonElement>(s.AsSpan(JsonPrefix.Length));
                    return UnwrapJsonValue(element);
                }
                catch { }
            }
            // Match JSON reader: try numeric then date parse on plain strings
            if (decimal.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d)) return d;
            if (DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var dt)) return dt;
            return s;
        }

        private static bool IsDateTimeOffsetLogicalType(string? logicalType)
            => logicalType?.StartsWith("DATETIMEOFFSET", StringComparison.OrdinalIgnoreCase) == true
                || logicalType?.Equals("DateTimeOffset", StringComparison.OrdinalIgnoreCase) == true;

        private static DateTimeOffset ParseDateTimeOffsetString(string value)
        {
            var payload = value.StartsWith(JsonPrefix, StringComparison.Ordinal)
                ? value[JsonPrefix.Length..]
                : value;
            if (payload.Length >= 2 && payload[0] == '"' && payload[^1] == '"')
                payload = JsonSerializer.Deserialize<string>(payload) ?? payload;
            return DateTimeOffset.Parse(payload, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind);
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

        private enum SpillReadMode { None, Rows, Columns }
    }
}


