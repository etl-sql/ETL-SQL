using System;
using System.Buffers;
using System.Collections.Generic;
using System.Security.Cryptography;
using ETL_SQL.Core.Data;

namespace ETL_SQL.Engine.Engines;

/// <summary>
/// Accumulates rows copied directly from source <see cref="ColumnBatch"/> instances into a
/// fresh typed <see cref="ColumnBatch"/> without materializing <see cref="ETL_SQL.Data.Row"/>
/// objects or boxing scalar values. Used by columnar external sort chunking and intermediate merges.
/// </summary>
internal sealed class ColumnBatchRowBuilder : IDisposable
{
    private readonly ColumnBatchSchema _schema;
    private readonly int _capacity;
    private IColumnWriter[]? _writers;
    private int _count;
    private long _estimatedBytes;
    private bool _disposed;

    public ColumnBatchRowBuilder(ColumnBatchSchema schema, int capacity)
    {
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
        _capacity = Math.Max(1, capacity);
        EnsureWriters();
    }

    public int Count => _count;
    public int Capacity => _capacity;
    public long EstimatedBytes => _estimatedBytes;

    public static bool CanBuild(ColumnBatchSchema schema)
    {
        for (int i = 0; i < schema.Count; i++)
        {
            if (!IsSupportedType(schema.Fields[i].ElementType))
                return false;
        }
        return true;
    }

    public static bool HasCompatibleSchema(ColumnBatchSchema a, ColumnBatchSchema b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            var fa = a.Fields[i];
            var fb = b.Fields[i];
            if (!string.Equals(fa.Name, fb.Name, StringComparison.OrdinalIgnoreCase)
                || fa.ElementType != fb.ElementType
                || !string.Equals(fa.LogicalType, fb.LogicalType, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsSupportedType(Type t) =>
        t == typeof(long)
        || t == typeof(int)
        || t == typeof(short)
        || t == typeof(byte)
        || t == typeof(decimal)
        || t == typeof(double)
        || t == typeof(bool)
        || t == typeof(DateTime)
        || t == typeof(DateOnly)
        || t == typeof(DateTimeOffset)
        || t == typeof(Guid)
        || t == typeof(TimeSpan)
        || t == typeof(string);

    private void EnsureWriters()
    {
        if (_writers != null) return;
        var writers = new IColumnWriter[_schema.Count];
        try
        {
            for (int i = 0; i < _schema.Count; i++)
                writers[i] = CreateWriter(_schema.Fields[i].ElementType, _capacity);
            _writers = writers;
        }
        catch
        {
            for (int i = 0; i < writers.Length; i++)
                writers[i]?.Dispose();
            throw;
        }
    }

    private static IColumnWriter CreateWriter(Type elementType, int capacity)
    {
        if (elementType == typeof(long)) return new FixedColumnWriter<long>(capacity);
        if (elementType == typeof(int)) return new FixedColumnWriter<int>(capacity);
        if (elementType == typeof(short)) return new FixedColumnWriter<short>(capacity);
        if (elementType == typeof(byte)) return new FixedColumnWriter<byte>(capacity);
        if (elementType == typeof(decimal)) return new FixedColumnWriter<decimal>(capacity);
        if (elementType == typeof(double)) return new FixedColumnWriter<double>(capacity);
        if (elementType == typeof(bool)) return new FixedColumnWriter<bool>(capacity);
        if (elementType == typeof(DateTime)) return new FixedColumnWriter<DateTime>(capacity);
        if (elementType == typeof(DateOnly)) return new FixedColumnWriter<DateOnly>(capacity);
        if (elementType == typeof(DateTimeOffset)) return new FixedColumnWriter<DateTimeOffset>(capacity);
        if (elementType == typeof(Guid)) return new FixedColumnWriter<Guid>(capacity);
        if (elementType == typeof(TimeSpan)) return new FixedColumnWriter<TimeSpan>(capacity);
        if (elementType == typeof(string)) return new Utf8ColumnWriter(capacity);
        throw new NotSupportedException($"Columnar row builder does not support '{elementType.Name}'.");
    }

    public void AppendFrom(ColumnBatch source, int sourceRowIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureWriters();
        var writers = _writers!;
        int destRow = _count;
        long rowBytes = 0;
        for (int col = 0; col < writers.Length; col++)
            rowBytes += writers[col].AppendFrom(source.Columns[col], sourceRowIndex, destRow);
        _count++;
        _estimatedBytes += rowBytes;
    }

    public ColumnBatch Flush()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_count == 0 || _writers == null)
            throw new InvalidOperationException("Cannot flush an empty ColumnBatchRowBuilder.");

        var writers = _writers;
        int rowCount = _count;
        _writers = null;
        _count = 0;
        _estimatedBytes = 0;

        var columns = new List<IColumnBuffer>(writers.Length);
        try
        {
            for (int i = 0; i < writers.Length; i++)
                columns.Add(writers[i].Build(rowCount));
            return new ColumnBatch(_schema, columns, rowCount);
        }
        catch
        {
            foreach (var col in columns) col.Dispose();
            for (int i = columns.Count; i < writers.Length; i++) writers[i].Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_writers != null)
        {
            for (int i = 0; i < _writers.Length; i++)
                _writers[i]?.Dispose();
            _writers = null;
        }
        _count = 0;
        _estimatedBytes = 0;
    }

    private interface IColumnWriter : IDisposable
    {
        long AppendFrom(IColumnBuffer source, int sourceRow, int destRow);
        IColumnBuffer Build(int rowCount);
    }

    private sealed class FixedColumnWriter<T> : IColumnWriter where T : unmanaged
    {
        private ColumnBuffer<T>? _buffer;
        private static readonly int ElementBytes = System.Runtime.CompilerServices.Unsafe.SizeOf<T>();

        public FixedColumnWriter(int capacity)
        {
            _buffer = ColumnBuffer<T>.Rent(capacity);
        }

        public long AppendFrom(IColumnBuffer source, int sourceRow, int destRow)
        {
            var typed = (ColumnBuffer<T>)source;
            if (typed.IsNull(sourceRow))
            {
                _buffer!.SetNull(destRow);
                return 1;
            }
            _buffer!.Values.Span[destRow] = typed.Values.Span[sourceRow];
            return ElementBytes;
        }

        public IColumnBuffer Build(int rowCount)
        {
            var buf = _buffer!;
            if (rowCount == buf.Count)
            {
                _buffer = null;
                return buf;
            }

            // Partial final batch: copy into an exact-sized pooled buffer so ColumnBatch.RowCount matches.
            var trimmed = ColumnBuffer<T>.Rent(rowCount);
            try
            {
                buf.Values.Span.Slice(0, rowCount).CopyTo(trimmed.Values.Span);
                for (int i = 0; i < rowCount; i++)
                {
                    if (buf.IsNull(i)) trimmed.SetNull(i);
                }
                buf.Dispose();
                _buffer = null;
                return trimmed;
            }
            catch
            {
                trimmed.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            _buffer?.Dispose();
            _buffer = null;
        }
    }

    private sealed class Utf8ColumnWriter : IColumnWriter
    {
        private int[]? _offsets;
        private byte[]? _data;
        private byte[]? _nullBitmap;
        private int _dataLength;

        public Utf8ColumnWriter(int capacity)
        {
            _offsets = ArrayPool<int>.Shared.Rent(Math.Max(2, capacity + 1));
            _offsets[0] = 0;
            _data = ArrayPool<byte>.Shared.Rent(Math.Max(256, capacity * 16));
            int bitmapBytes = (capacity + 7) / 8;
            _nullBitmap = ArrayPool<byte>.Shared.Rent(Math.Max(1, bitmapBytes));
            _nullBitmap.AsSpan(0, bitmapBytes).Clear();
        }

        public long AppendFrom(IColumnBuffer source, int sourceRow, int destRow)
        {
            var utf8 = (Utf8ColumnBuffer)source;
            if (utf8.IsNull(sourceRow))
            {
                _nullBitmap![destRow >> 3] |= (byte)(1 << (destRow & 7));
                _offsets![destRow + 1] = _dataLength;
                return 4;
            }

            var bytes = utf8.GetUtf8Bytes(sourceRow);
            if (!bytes.IsEmpty)
            {
                EnsureDataCapacity(_dataLength + bytes.Length);
                bytes.CopyTo(_data.AsSpan(_dataLength));
                _dataLength += bytes.Length;
            }
            _offsets![destRow + 1] = _dataLength;
            return 4L + bytes.Length;
        }

        private void EnsureDataCapacity(int required)
        {
            if (required <= _data!.Length) return;
            int nextSize = Math.Max(_data.Length * 2, required);
            var grown = ArrayPool<byte>.Shared.Rent(nextSize);
            if (_dataLength > 0)
                Buffer.BlockCopy(_data, 0, grown, 0, _dataLength);
            CryptographicOperations.ZeroMemory(_data.AsSpan(0, _dataLength));
            ArrayPool<byte>.Shared.Return(_data);
            _data = grown;
        }

        public IColumnBuffer Build(int rowCount)
        {
            var offsets = _offsets!;
            var data = _data!;
            var nullBitmap = _nullBitmap!;
            int dataLen = _dataLength;
            int bitmapBytes = (rowCount + 7) / 8;

            // Convert our 1=null bitmap into Arrow's 1=valid bitmap for CopyEncoded,
            // or pass empty validity span when there are no nulls.
            bool hasNulls = nullBitmap.AsSpan(0, bitmapBytes).IndexOfAnyExcept((byte)0) >= 0;
            byte[]? arrowValidity = null;
            try
            {
                ReadOnlySpan<byte> validitySpan = ReadOnlySpan<byte>.Empty;
                if (hasNulls)
                {
                    arrowValidity = ArrayPool<byte>.Shared.Rent(Math.Max(1, bitmapBytes));
                    for (int i = 0; i < bitmapBytes; i++)
                        arrowValidity[i] = (byte)~nullBitmap[i];
                    validitySpan = arrowValidity.AsSpan(0, bitmapBytes);
                }

                var result = Utf8ColumnBuffer.CopyEncoded(
                    offsets.AsSpan(0, rowCount + 1),
                    data.AsSpan(0, dataLen),
                    validitySpan,
                    validityBitOffset: 0);

                Dispose();
                return result;
            }
            finally
            {
                if (arrowValidity != null)
                    ArrayPool<byte>.Shared.Return(arrowValidity, clearArray: true);
            }
        }

        public void Dispose()
        {
            if (_offsets != null)
            {
                ArrayPool<int>.Shared.Return(_offsets, clearArray: true);
                _offsets = null;
            }
            if (_data != null)
            {
                if (_dataLength > 0)
                    CryptographicOperations.ZeroMemory(_data.AsSpan(0, _dataLength));
                ArrayPool<byte>.Shared.Return(_data);
                _data = null;
            }
            if (_nullBitmap != null)
            {
                ArrayPool<byte>.Shared.Return(_nullBitmap, clearArray: true);
                _nullBitmap = null;
            }
            _dataLength = 0;
        }
    }
}
