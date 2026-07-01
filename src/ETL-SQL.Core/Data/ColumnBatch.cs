using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace ETL_SQL.Core.Data;

/// <summary>Describes one physical column in a native column batch.</summary>
public sealed record ColumnBatchField(string Name, Type ElementType, string LogicalType, bool IsNullable = true);

/// <summary>Immutable ordered schema shared by compatible column batches.</summary>
public sealed class ColumnBatchSchema
{
    private readonly Dictionary<string, int> _ordinals;

    public ColumnBatchSchema(IEnumerable<ColumnBatchField> fields)
    {
        var fieldArray = fields?.ToArray() ?? throw new ArgumentNullException(nameof(fields));
        Fields = Array.AsReadOnly(fieldArray);
        if (Fields.Count == 0)
            throw new ArgumentException("A column batch schema must contain at least one field.", nameof(fields));

        _ordinals = new Dictionary<string, int>(Fields.Count, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < Fields.Count; i++)
        {
            var field = Fields[i];
            if (string.IsNullOrWhiteSpace(field.Name))
                throw new ArgumentException("Column batch field names cannot be empty.", nameof(fields));
            if (!_ordinals.TryAdd(field.Name, i))
                throw new ArgumentException($"Duplicate column batch field '{field.Name}'.", nameof(fields));
        }
    }

    public IReadOnlyList<ColumnBatchField> Fields { get; }
    public int Count => Fields.Count;

    public int GetOrdinal(string name) => _ordinals.TryGetValue(name, out var ordinal)
        ? ordinal
        : throw new KeyNotFoundException($"Column '{name}' was not found in the batch schema.");
}

/// <summary>Non-generic view used by operators that dispatch on the physical element type.</summary>
public interface IColumnBuffer : IDisposable
{
    Type ElementType { get; }
    int Count { get; }
    ReadOnlyMemory<byte> NullBitmap { get; }
    long AllocatedBytes { get; }
    bool IsNull(int index);
    object? GetBoxedValue(int index);
}

/// <summary>
/// Fixed-width native column storage. Rented buffers own their pool leases until disposal; wrapped
/// arrays remain caller-owned. Operators use <see cref="Values"/> and synchronous spans directly.
/// </summary>
public sealed class ColumnBuffer<T> : IColumnBuffer where T : unmanaged
{
    private T[]? _values;
    private byte[]? _nullBitmap;
    private readonly bool _pooled;
    private readonly int _count;
    private readonly int _nullByteCount;

    public ColumnBuffer(T[] values, int count, byte[]? nullBitmap = null)
        : this(values, count, nullBitmap ?? new byte[GetNullByteCount(count)], pooled: false)
    {
    }

    private ColumnBuffer(T[] values, int count, byte[] nullBitmap, bool pooled)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(nullBitmap);
        if ((uint)count > (uint)values.Length)
            throw new ArgumentOutOfRangeException(nameof(count));

        _nullByteCount = GetNullByteCount(count);
        if (nullBitmap.Length < _nullByteCount)
            throw new ArgumentException("Null bitmap is too small for the requested row count.", nameof(nullBitmap));

        _values = values;
        _nullBitmap = nullBitmap;
        _pooled = pooled;
        _count = count;
    }

    public static ColumnBuffer<T> Rent(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        var nullBytes = GetNullByteCount(count);
        var values = ArrayPool<T>.Shared.Rent(Math.Max(1, count));
        var bitmap = ArrayPool<byte>.Shared.Rent(Math.Max(1, nullBytes));
        bitmap.AsSpan(0, nullBytes).Clear();
        return new ColumnBuffer<T>(values, count, bitmap, pooled: true);
    }

    public Type ElementType => typeof(T);
    public int Count => _count;
    public Memory<T> Values => GetValues().AsMemory(0, _count);
    public ReadOnlyMemory<byte> NullBitmap => GetNullBitmap().AsMemory(0, _nullByteCount);

    public long AllocatedBytes
    {
        get
        {
            var values = GetValues();
            var elementBytes = Unsafe.SizeOf<T>();
            return (long)values.Length * elementBytes + GetNullBitmap().LongLength;
        }
    }

    public bool IsNull(int index)
    {
        ValidateIndex(index);
        return (GetNullBitmap()[index >> 3] & (1 << (index & 7))) != 0;
    }

    public void SetNull(int index, bool isNull = true)
    {
        ValidateIndex(index);
        ref var slot = ref GetNullBitmap()[index >> 3];
        var mask = (byte)(1 << (index & 7));
        if (isNull) slot |= mask;
        else slot &= (byte)~mask;
    }

    public object? GetBoxedValue(int index) => IsNull(index) ? null : GetValues()[index];

    public void Dispose()
    {
        var values = _values;
        var bitmap = _nullBitmap;
        _values = null;
        _nullBitmap = null;

        if (!_pooled) return;
        if (values != null)
            ArrayPool<T>.Shared.Return(values, clearArray: false);
        if (bitmap != null)
            ArrayPool<byte>.Shared.Return(bitmap, clearArray: true);
    }

    private T[] GetValues() => _values ?? throw new ObjectDisposedException(nameof(ColumnBuffer<T>));
    private byte[] GetNullBitmap() => _nullBitmap ?? throw new ObjectDisposedException(nameof(ColumnBuffer<T>));

    private void ValidateIndex(int index)
    {
        if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
    }

    private static int GetNullByteCount(int count) => checked((count + 7) / 8);

}

/// <summary>UTF-8 variable-width string column backed by pooled offsets and byte data.</summary>
public sealed class Utf8ColumnBuffer : IColumnBuffer
{
    private int[]? _offsets;
    private byte[]? _data;
    private byte[]? _nullBitmap;
    private readonly int _count;
    private readonly int _dataLength;
    private readonly int _nullByteCount;

    private Utf8ColumnBuffer(int[] offsets, byte[] data, byte[] nullBitmap, int count, int dataLength)
    {
        _offsets = offsets;
        _data = data;
        _nullBitmap = nullBitmap;
        _count = count;
        _dataLength = dataLength;
        _nullByteCount = (count + 7) / 8;
    }

    public static Utf8ColumnBuffer FromStrings(IReadOnlyList<string?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var dataLength = 0;
        for (var i = 0; i < values.Count; i++)
            if (values[i] is { } value)
                dataLength = checked(dataLength + Encoding.UTF8.GetByteCount(value));

        var nullBytes = checked((values.Count + 7) / 8);
        var offsets = ArrayPool<int>.Shared.Rent(Math.Max(1, values.Count + 1));
        var data = ArrayPool<byte>.Shared.Rent(Math.Max(1, dataLength));
        var bitmap = ArrayPool<byte>.Shared.Rent(Math.Max(1, nullBytes));
        try
        {
            bitmap.AsSpan(0, nullBytes).Clear();
            var position = 0;
            offsets[0] = 0;
            for (var i = 0; i < values.Count; i++)
            {
                if (values[i] is { } value)
                {
                    position += Encoding.UTF8.GetBytes(value.AsSpan(), data.AsSpan(position));
                }
                else
                {
                    bitmap[i >> 3] |= (byte)(1 << (i & 7));
                }
                offsets[i + 1] = position;
            }
            return new Utf8ColumnBuffer(offsets, data, bitmap, values.Count, dataLength);
        }
        catch
        {
            ArrayPool<int>.Shared.Return(offsets, clearArray: false);
            ArrayPool<byte>.Shared.Return(data, clearArray: false);
            ArrayPool<byte>.Shared.Return(bitmap, clearArray: true);
            throw;
        }
    }

    public Type ElementType => typeof(string);
    public int Count => _count;
    public ReadOnlyMemory<int> Offsets => GetOffsets().AsMemory(0, _count + 1);
    public ReadOnlyMemory<byte> Utf8Data => GetData().AsMemory(0, _dataLength);
    public ReadOnlyMemory<byte> NullBitmap => GetNullBitmap().AsMemory(0, _nullByteCount);
    public long AllocatedBytes => (long)GetOffsets().Length * sizeof(int) + GetData().LongLength + GetNullBitmap().LongLength;

    public bool IsNull(int index)
    {
        ValidateIndex(index);
        return (GetNullBitmap()[index >> 3] & (1 << (index & 7))) != 0;
    }

    public ReadOnlySpan<byte> GetUtf8Bytes(int index)
    {
        ValidateIndex(index);
        var offsets = GetOffsets();
        return GetData().AsSpan(offsets[index], offsets[index + 1] - offsets[index]);
    }

    public object? GetBoxedValue(int index) => IsNull(index) ? null : Encoding.UTF8.GetString(GetUtf8Bytes(index));

    public void Dispose()
    {
        var offsets = _offsets;
        var data = _data;
        var bitmap = _nullBitmap;
        _offsets = null;
        _data = null;
        _nullBitmap = null;
        if (offsets != null) ArrayPool<int>.Shared.Return(offsets, clearArray: false);
        if (data != null) ArrayPool<byte>.Shared.Return(data, clearArray: false);
        if (bitmap != null) ArrayPool<byte>.Shared.Return(bitmap, clearArray: true);
    }

    private int[] GetOffsets() => _offsets ?? throw new ObjectDisposedException(nameof(Utf8ColumnBuffer));
    private byte[] GetData() => _data ?? throw new ObjectDisposedException(nameof(Utf8ColumnBuffer));
    private byte[] GetNullBitmap() => _nullBitmap ?? throw new ObjectDisposedException(nameof(Utf8ColumnBuffer));

    private void ValidateIndex(int index)
    {
        if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
    }
}

/// <summary>Owned, immutable-shape group of equal-length typed columns.</summary>
public sealed class ColumnBatch : IDisposable
{
    private IColumnBuffer[]? _columns;
    private readonly IReadOnlyList<IColumnBuffer> _columnView;
    private int _referenceCount = 1;
    private Action? _finalRelease;
    private readonly object _releaseGate = new();

    public ColumnBatch(ColumnBatchSchema schema, IEnumerable<IColumnBuffer> columns, int rowCount)
    {
        Schema = schema ?? throw new ArgumentNullException(nameof(schema));
        if (rowCount < 0) throw new ArgumentOutOfRangeException(nameof(rowCount));
        _columns = columns?.ToArray() ?? throw new ArgumentNullException(nameof(columns));
        _columnView = Array.AsReadOnly(_columns);
        if (_columns.Length != schema.Count)
            throw new ArgumentException("Column count does not match the batch schema.", nameof(columns));

        for (var i = 0; i < _columns.Length; i++)
        {
            if (_columns[i].Count != rowCount)
                throw new ArgumentException($"Column '{schema.Fields[i].Name}' has {_columns[i].Count} values; expected {rowCount}.", nameof(columns));
            if (_columns[i].ElementType != schema.Fields[i].ElementType)
                throw new ArgumentException($"Column '{schema.Fields[i].Name}' has physical type {_columns[i].ElementType.Name}; expected {schema.Fields[i].ElementType.Name}.", nameof(columns));
        }
        RowCount = rowCount;
    }

    public ColumnBatchSchema Schema { get; }
    public int RowCount { get; }
    public IReadOnlyList<IColumnBuffer> Columns
    {
        get
        {
            GetColumns();
            return _columnView;
        }
    }
    public long AllocatedBytes => GetColumns().Sum(column => column.AllocatedBytes);

    public IColumnBuffer GetColumn(string name) => GetColumns()[Schema.GetOrdinal(name)];

    public ColumnBuffer<T> GetColumn<T>(string name) where T : unmanaged
    {
        var column = GetColumn(name);
        return column as ColumnBuffer<T>
            ?? throw new InvalidOperationException($"Column '{name}' is {column.ElementType.Name}, not {typeof(T).Name}.");
    }

    public Utf8ColumnBuffer GetUtf8Column(string name)
    {
        var column = GetColumn(name);
        return column as Utf8ColumnBuffer
            ?? throw new InvalidOperationException($"Column '{name}' is not UTF-8 string storage.");
    }

    /// <summary>Acquires another ownership reference to this immutable batch.</summary>
    public ColumnBatch Retain()
    {
        while (true)
        {
            var current = Volatile.Read(ref _referenceCount);
            if (current <= 0) throw new ObjectDisposedException(nameof(ColumnBatch));
            if (Interlocked.CompareExchange(ref _referenceCount, current + 1, current) == current)
                return this;
        }
    }

    /// <summary>Registers cleanup that runs exactly once when the final ownership reference is released.</summary>
    internal void OnFinalRelease(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        lock (_releaseGate)
        {
            if (Volatile.Read(ref _referenceCount) <= 0) throw new ObjectDisposedException(nameof(ColumnBatch));
            _finalRelease += callback;
        }
    }

    public void Dispose()
    {
        var remaining = Interlocked.Decrement(ref _referenceCount);
        if (remaining > 0) return;
        if (remaining < 0) throw new ObjectDisposedException(nameof(ColumnBatch));
        var columns = _columns;
        _columns = null;
        if (columns == null) return;
        Action? finalRelease;
        lock (_releaseGate)
        {
            finalRelease = _finalRelease;
            _finalRelease = null;
        }
        foreach (var column in columns) column.Dispose();
        Array.Clear(columns);
        finalRelease?.Invoke();
    }

    private IColumnBuffer[] GetColumns() => _columns ?? throw new ObjectDisposedException(nameof(ColumnBatch));
}
