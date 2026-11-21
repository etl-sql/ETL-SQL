using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;
using ETL_SQL.Engine.Spill;

namespace ETL_SQL.Engine.Engines;

/// <summary>
/// Packs a <see cref="Row"/> into a compact, type-tagged binary blob (and back) against a fixed column
/// list. Used by the external join build side to hold build rows as contiguous <c>byte[]</c> instead of
/// fat <see cref="Row"/> object graphs (each value boxed; every number a boxed <see cref="decimal"/>),
/// which is the dominant memory cost of a large hash-join build. A blob is decoded back to a <see cref="Row"/>
/// only when a probe row actually matches its key.
///
/// The column list is captured once from the first build row (the build side has a uniform schema —
/// the same assumption the Arrow spill writer already makes when it infers a schema from the first row),
/// so per-row column names are not stored. Values are written in column order; missing columns pack as null.
/// </summary>
internal sealed class RowPacker : IDisposable
{
    private const byte TNull = 0;
    private const byte TBool = 1;
    private const byte TDecimal = 2;
    private const byte TDouble = 3;
    private const byte TDateTime = 4;
    private const byte TString = 5;
    private const byte TJson = 6;
    private const byte TDateOnly = 7;
    private const byte TBinary = 8;

    private const int InitialScratchBytes = 256;

    /// <summary>
    /// Maximum scratch buffer capacity retained across <see cref="Pack"/> calls so an outlier wide row
    /// does not pin multi-megabyte plaintext scratch memory for the rest of a build.
    /// </summary>
    internal const int MaxRetainedBufferBytes = 64 * 1024;

    private byte[] _buffer = new byte[InitialScratchBytes];
    private int _position;
    private bool _disposed;

    internal int RetainedCapacity => _disposed ? 0 : _buffer.Length;

    /// <summary>Packs the named columns of <paramref name="row"/> into a fresh byte[] blob.</summary>
    public byte[] Pack(Row row, IReadOnlyList<string> columns)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _position = 0;
        for (int i = 0; i < columns.Count; i++)
            WriteValue(row[columns[i]]);
        return CopyAndResetScratch();
    }

    /// <summary>Packs one native batch row without constructing a <see cref="Row"/> object.</summary>
    public byte[] Pack(ColumnBatch batch, int rowIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _position = 0;
        var plan = BatchColumnPlan.For(batch);
        WriteBatchRow(batch, rowIndex, plan);
        return CopyAndResetScratch();
    }

    /// <summary>Packs the named columns of <paramref name="row"/> directly into a pooled build slab.</summary>
    internal (int RowIndex, int ByteLength) PackInto(Row row, IReadOnlyList<string> columns, ExternalJoinEngine.PackedBuildTable table)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _position = 0;
        for (int i = 0; i < columns.Count; i++)
            WriteValue(row[columns[i]]);
        return CopyToTableAndResetScratch(table);
    }

    /// <summary>Packs one native batch row directly into a pooled build slab.</summary>
    internal (int RowIndex, int ByteLength) PackInto(ColumnBatch batch, int rowIndex, ExternalJoinEngine.PackedBuildTable table)
        => PackInto(batch, rowIndex, BatchColumnPlan.For(batch), table);

    internal (int RowIndex, int ByteLength) PackInto(
        ColumnBatch batch,
        int rowIndex,
        BatchColumnPlan plan,
        ExternalJoinEngine.PackedBuildTable table)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _position = 0;
        WriteBatchRow(batch, rowIndex, plan);
        return CopyToTableAndResetScratch(table);
    }

    internal byte[] GetBackingBufferSnapshotForTests()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _buffer;
    }

    private void WriteBatchRow(ColumnBatch batch, int rowIndex, BatchColumnPlan plan)
    {
        var columns = plan.Buffers;
        var kinds = plan.Kinds;
        for (var col = 0; col < columns.Length; col++)
        {
            var buffer = columns[col];
            if (buffer.IsNull(rowIndex))
            {
                WriteNull();
                continue;
            }

            switch (kinds[col])
            {
                case BatchColumnKind.Int64:
                    WriteDecimal(((ColumnBuffer<long>)buffer).Values.Span[rowIndex]);
                    break;
                case BatchColumnKind.Int32:
                    WriteDecimal(((ColumnBuffer<int>)buffer).Values.Span[rowIndex]);
                    break;
                case BatchColumnKind.Int16:
                    WriteDecimal(((ColumnBuffer<short>)buffer).Values.Span[rowIndex]);
                    break;
                case BatchColumnKind.Byte:
                    WriteDecimal(((ColumnBuffer<byte>)buffer).Values.Span[rowIndex]);
                    break;
                case BatchColumnKind.Decimal:
                    WriteDecimal(((ColumnBuffer<decimal>)buffer).Values.Span[rowIndex]);
                    break;
                case BatchColumnKind.Double:
                    WriteDouble(((ColumnBuffer<double>)buffer).Values.Span[rowIndex]);
                    break;
                case BatchColumnKind.Boolean:
                    WriteBool(((ColumnBuffer<bool>)buffer).Values.Span[rowIndex]);
                    break;
                case BatchColumnKind.DateTime:
                    WriteDateTime(((ColumnBuffer<DateTime>)buffer).Values.Span[rowIndex]);
                    break;
                case BatchColumnKind.DateOnly:
                    WriteDateOnly(((ColumnBuffer<DateOnly>)buffer).Values.Span[rowIndex]);
                    break;
                case BatchColumnKind.Utf8String:
                    {
                        var utf8Bytes = ((Utf8ColumnBuffer)buffer).GetUtf8Bytes(rowIndex);
                        if (!utf8Bytes.IsEmpty && utf8Bytes[0] == 0x1E)
                            WriteValue(ReadBatchValue(batch, col, rowIndex));
                        else
                            WriteUtf8String(utf8Bytes);
                        break;
                    }
                case BatchColumnKind.Binary:
                    WriteBinary(((BinaryColumnBuffer)buffer).GetBytes(rowIndex));
                    break;
                default:
                    WriteValue(ReadBatchValue(batch, col, rowIndex));
                    break;
            }
        }
    }

    private byte[] CopyAndResetScratch()
    {
        var written = _position;
        var result = new byte[written];
        if (written > 0)
            Buffer.BlockCopy(_buffer, 0, result, 0, written);
        ResetScratch(written);
        return result;
    }

    private (int RowIndex, int ByteLength) CopyToTableAndResetScratch(ExternalJoinEngine.PackedBuildTable table)
    {
        var written = _position;
        try
        {
            return written == 0
                ? table.AppendRowBytes(Array.Empty<byte>(), 0, 0)
                : table.AppendRowBytes(_buffer, 0, written);
        }
        finally
        {
            ResetScratch(written);
        }
    }

    private void ResetScratch(int written)
    {
        if (written > 0)
            CryptographicOperations.ZeroMemory(_buffer.AsSpan(0, written));
        if (_buffer.Length > MaxRetainedBufferBytes)
        {
            CryptographicOperations.ZeroMemory(_buffer);
            _buffer = new byte[InitialScratchBytes];
        }
        _position = 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CryptographicOperations.ZeroMemory(_buffer);
        _buffer = Array.Empty<byte>();
        _position = 0;
    }

    private void EnsureCapacity(int additionalBytes)
    {
        int required = _position + additionalBytes;
        if (required <= _buffer.Length) return;

        int nextLength = _buffer.Length * 2;
        if (nextLength < required) nextLength = required;
        var grown = new byte[nextLength];
        if (_position > 0)
            Buffer.BlockCopy(_buffer, 0, grown, 0, _position);
        CryptographicOperations.ZeroMemory(_buffer);
        _buffer = grown;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteNull()
    {
        EnsureCapacity(1);
        _buffer[_position++] = TNull;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteBool(bool value)
    {
        EnsureCapacity(2);
        _buffer[_position++] = TBool;
        _buffer[_position++] = value ? (byte)1 : (byte)0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteDecimal(decimal value)
    {
        EnsureCapacity(17);
        _buffer[_position++] = TDecimal;
        Span<int> bits = stackalloc int[4];
        decimal.GetBits(value, bits);
        var span = _buffer.AsSpan(_position, 16);
        BinaryPrimitives.WriteInt32LittleEndian(span, bits[0]);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(4), bits[1]);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(8), bits[2]);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(12), bits[3]);
        _position += 16;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteDouble(double value)
    {
        EnsureCapacity(9);
        _buffer[_position++] = TDouble;
        BinaryPrimitives.WriteInt64LittleEndian(_buffer.AsSpan(_position, 8), BitConverter.DoubleToInt64Bits(value));
        _position += 8;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteDateTime(DateTime value)
    {
        EnsureCapacity(10);
        _buffer[_position++] = TDateTime;
        BinaryPrimitives.WriteInt64LittleEndian(_buffer.AsSpan(_position, 8), value.Ticks);
        _buffer[_position + 8] = (byte)value.Kind;
        _position += 9;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteDateOnly(DateOnly value)
    {
        EnsureCapacity(5);
        _buffer[_position++] = TDateOnly;
        BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(_position, 4), value.DayNumber);
        _position += 4;
    }

    private void WriteString(string value)
    {
        int byteCount = Encoding.UTF8.GetByteCount(value);
        EnsureCapacity(1 + 5 + byteCount);
        _buffer[_position++] = TString;
        Write7BitEncodedInt(byteCount);
        if (byteCount > 0)
        {
            Encoding.UTF8.GetBytes(value, _buffer.AsSpan(_position, byteCount));
            _position += byteCount;
        }
    }

    private void WriteUtf8String(ReadOnlySpan<byte> utf8)
    {
        EnsureCapacity(1 + 5 + utf8.Length);
        _buffer[_position++] = TString;
        Write7BitEncodedInt(utf8.Length);
        if (!utf8.IsEmpty)
        {
            utf8.CopyTo(_buffer.AsSpan(_position));
            _position += utf8.Length;
        }
    }

    private void WriteBinary(ReadOnlySpan<byte> bytes)
    {
        EnsureCapacity(1 + 5 + bytes.Length);
        _buffer[_position++] = TBinary;
        Write7BitEncodedInt(bytes.Length);
        if (!bytes.IsEmpty)
        {
            bytes.CopyTo(_buffer.AsSpan(_position));
            _position += bytes.Length;
        }
    }

    private void WriteJson(string json)
    {
        int byteCount = Encoding.UTF8.GetByteCount(json);
        EnsureCapacity(1 + 5 + byteCount);
        _buffer[_position++] = TJson;
        Write7BitEncodedInt(byteCount);
        if (byteCount > 0)
        {
            Encoding.UTF8.GetBytes(json, _buffer.AsSpan(_position, byteCount));
            _position += byteCount;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Write7BitEncodedInt(int value)
    {
        uint uValue = (uint)value;
        while (uValue >= 0x80u)
        {
            _buffer[_position++] = (byte)(uValue | 0x80u);
            uValue >>= 7;
        }
        _buffer[_position++] = (byte)uValue;
    }

    internal static object? ReadBatchValue(ColumnBatch batch, int columnIndex, int rowIndex)
    {
        var value = batch.Columns[columnIndex].GetBoxedValue(rowIndex);
        if (value is not string text) return value;
        var logicalType = batch.Schema.Fields[columnIndex].LogicalType;
        const string jsonPrefix = "\x1Ejson:";
        if (text.StartsWith(jsonPrefix, StringComparison.Ordinal))
        {
            try
            {
                var element = JsonSerializer.Deserialize<JsonElement>(text.AsSpan(jsonPrefix.Length));
                return SpillSerializationHelper.UnwrapJsonElement(element);
            }
            catch { return text; }
        }
        const string dateOnlyPrefix = "\x1Edateonly:";
        if (text.StartsWith(dateOnlyPrefix, StringComparison.Ordinal)
            && DateOnly.TryParseExact(text.AsSpan(dateOnlyPrefix.Length), "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var dateOnly))
        {
            return dateOnly;
        }
        // "String" and "Dynamic" (first spilled row NULL) both hold text unprefixed: the row
        // writer prefixes every non-string value, so plain text stays text.
        return text;
    }

    /// <summary>
    /// Reads one cell from a native batch column as an engine runtime scalar (integral columns box
    /// as <see cref="decimal"/> rather than <see cref="long"/>, matching <see cref="Unpack"/> and
    /// Arrow row extraction).
    /// </summary>
    internal static object? ReadBatchScalar(ColumnBatch batch, BatchColumnPlan plan, int columnIndex, int rowIndex)
    {
        var buffer = plan.Buffers[columnIndex];
        if (buffer.IsNull(rowIndex)) return null;

        return plan.Kinds[columnIndex] switch
        {
            BatchColumnKind.Int64 => (decimal)((ColumnBuffer<long>)buffer).Values.Span[rowIndex],
            BatchColumnKind.Int32 => (decimal)((ColumnBuffer<int>)buffer).Values.Span[rowIndex],
            BatchColumnKind.Int16 => (decimal)((ColumnBuffer<short>)buffer).Values.Span[rowIndex],
            BatchColumnKind.Byte => (decimal)((ColumnBuffer<byte>)buffer).Values.Span[rowIndex],
            BatchColumnKind.Decimal => ((ColumnBuffer<decimal>)buffer).Values.Span[rowIndex],
            BatchColumnKind.Double => ((ColumnBuffer<double>)buffer).Values.Span[rowIndex],
            BatchColumnKind.Boolean => ((ColumnBuffer<bool>)buffer).Values.Span[rowIndex],
            BatchColumnKind.DateTime => ((ColumnBuffer<DateTime>)buffer).Values.Span[rowIndex],
            BatchColumnKind.DateOnly => ((ColumnBuffer<DateOnly>)buffer).Values.Span[rowIndex],
            BatchColumnKind.Utf8String => ReadUtf8Scalar(batch, (Utf8ColumnBuffer)buffer, columnIndex, rowIndex),
            BatchColumnKind.Binary => ((BinaryColumnBuffer)buffer).GetBytes(rowIndex).ToArray(),
            _ => ReadBatchValue(batch, columnIndex, rowIndex),
        };
    }

    private static object? ReadUtf8Scalar(ColumnBatch batch, Utf8ColumnBuffer buffer, int columnIndex, int rowIndex)
    {
        var bytes = buffer.GetUtf8Bytes(rowIndex);
        if (!bytes.IsEmpty && bytes[0] == 0x1E)
            return ReadBatchValue(batch, columnIndex, rowIndex);
        return Encoding.UTF8.GetString(bytes);
    }

    internal enum BatchColumnKind : byte
    {
        Fallback = 0,
        Int64 = 1,
        Int32 = 2,
        Int16 = 3,
        Byte = 4,
        Decimal = 5,
        Double = 6,
        Boolean = 7,
        DateTime = 8,
        DateOnly = 9,
        Utf8String = 10,
        Binary = 11,
    }

    internal sealed class BatchColumnPlan
    {
        public IColumnBuffer[] Buffers { get; }
        public BatchColumnKind[] Kinds { get; }

        private BatchColumnPlan(ColumnBatch batch)
        {
            var count = batch.Schema.Count;
            Buffers = new IColumnBuffer[count];
            Kinds = new BatchColumnKind[count];
            for (var i = 0; i < count; i++)
            {
                var buffer = batch.Columns[i];
                var logicalType = batch.Schema.Fields[i].LogicalType;
                Buffers[i] = buffer;
                Kinds[i] = Classify(buffer, logicalType);
            }
        }

        public static BatchColumnPlan For(ColumnBatch batch)
        {
            // Buffers are per-batch instances, so build one plan per batch rather than sharing buffer refs across batches.
            return new BatchColumnPlan(batch);
        }

        private static BatchColumnKind Classify(IColumnBuffer buffer, string logicalType) => buffer switch
        {
            ColumnBuffer<long> => BatchColumnKind.Int64,
            ColumnBuffer<int> => BatchColumnKind.Int32,
            ColumnBuffer<short> => BatchColumnKind.Int16,
            ColumnBuffer<byte> => BatchColumnKind.Byte,
            ColumnBuffer<decimal> => BatchColumnKind.Decimal,
            ColumnBuffer<double> => BatchColumnKind.Double,
            ColumnBuffer<bool> => BatchColumnKind.Boolean,
            ColumnBuffer<DateTime> => BatchColumnKind.DateTime,
            ColumnBuffer<DateOnly> => BatchColumnKind.DateOnly,
            Utf8ColumnBuffer when logicalType == "String" => BatchColumnKind.Utf8String,
            BinaryColumnBuffer => BatchColumnKind.Binary,
            _ => BatchColumnKind.Fallback,
        };
    }

    /// <summary>
    /// A row layout for a list of column names, with the semantics of setting each name on an
    /// empty row in order: distinct names (case-insensitive) in first-seen order, and a repeated
    /// name writes to its first slot so the last value wins. Rows built over it hold their values
    /// positionally instead of in a per-row name dictionary.
    /// </summary>
    internal sealed class NameLayout
    {
        public NameLayout(IReadOnlyList<string> names, IReadOnlyList<IReadOnlyList<string>?>? aliases = null)
        {
            var distinct = new List<string>(names.Count);
            var slotByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            Slots = new int[names.Count];
            for (var i = 0; i < names.Count; i++)
            {
                if (!slotByName.TryGetValue(names[i], out var slot))
                {
                    slot = distinct.Count;
                    slotByName[names[i]] = slot;
                    distinct.Add(names[i]);
                }
                Slots[i] = slot;
            }
            Schema = new TableSchema(distinct);
            if (aliases != null)
            {
                for (var i = 0; i < Math.Min(names.Count, aliases.Count); i++)
                {
                    var colAliases = aliases[i];
                    if (colAliases == null) continue;
                    var slot = Slots[i];
                    for (var a = 0; a < colAliases.Count; a++)
                        Schema.AddAlias(colAliases[a], slot);
                }
            }
            SourceCount = names.Count;
        }

        public TableSchema Schema { get; }
        public int[] Slots { get; }
        public int SourceCount { get; }
    }

    // Keyed by the Arrow schema or column-list instance that rows of one stream share; weak, so a
    // finished operator's layouts go with it.
    private static readonly ConditionalWeakTable<object, NameLayout> s_layouts = new();

    internal static NameLayout LayoutFor(
        object key,
        IReadOnlyList<string> names,
        IReadOnlyList<IReadOnlyList<string>?>? aliases = null)
    {
        if (s_layouts.TryGetValue(key, out var layout) && layout.SourceCount == names.Count) return layout;
        layout = new NameLayout(names, aliases);
        s_layouts.AddOrUpdate(key, layout);
        return layout;
    }

    internal static NameLayout LayoutFor(ColumnBatchSchema schema)
    {
        if (s_layouts.TryGetValue(schema, out var layout) && layout.SourceCount == schema.Count) return layout;
        var names = new string[schema.Count];
        IReadOnlyList<string>?[]? aliases = null;
        for (var i = 0; i < schema.Count; i++)
        {
            var field = schema.Fields[i];
            names[i] = field.Name;
            if (field.Aliases is { Count: > 0 })
            {
                aliases ??= new IReadOnlyList<string>?[schema.Count];
                aliases[i] = field.Aliases;
            }
        }
        layout = new NameLayout(names, aliases);
        s_layouts.AddOrUpdate(schema, layout);
        return layout;
    }

    internal static Row MaterializeBatchRow(ColumnBatch batch, int rowIndex)
    {
        var fields = batch.Schema.Fields;
        var layout = LayoutFor(batch.Schema);
        var values = new object?[layout.Schema.ColumnCount];
        for (var column = 0; column < fields.Count; column++)
            values[layout.Slots[column]] = ReadBatchValue(batch, column, rowIndex);
        return new Row(layout.Schema, values);
    }

    internal static Row MaterializeBatchScalarRow(ColumnBatch batch, BatchColumnPlan plan, NameLayout layout, int rowIndex)
    {
        var values = new object?[layout.Schema.ColumnCount];
        var count = plan.Buffers.Length;
        for (var column = 0; column < count; column++)
            values[layout.Slots[column]] = ReadBatchScalar(batch, plan, column, rowIndex);
        return new Row(layout.Schema, values);
    }

    /// <summary>Reconstructs a <see cref="Row"/> from a blob produced by <see cref="Pack"/>.</summary>
    public static Row Unpack(byte[] data, IReadOnlyList<string> columns) =>
        Unpack(data, 0, data.Length, columns);

    /// <summary>Reconstructs a <see cref="Row"/> from a pooled slab slice produced by <see cref="PackInto"/>.</summary>
    public static Row Unpack(
        byte[] data,
        int offset,
        int count,
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyList<string>?>? aliases = null)
    {
        var layout = LayoutFor(columns, columns, aliases);
        var values = new object?[layout.Schema.ColumnCount];
        var span = data.AsSpan(offset, count);
        int pos = 0;
        for (int i = 0; i < columns.Count; i++)
            values[layout.Slots[i]] = ReadValue(span, ref pos);
        return new Row(layout.Schema, values);
    }

    /// <summary>Decodes <paramref name="columnCount"/> packed values directly into <paramref name="destination"/> at <paramref name="targetSlots"/>.</summary>
    internal static void UnpackInto(ReadOnlySpan<byte> data, int columnCount, object?[] destination, int[] targetSlots)
    {
        int pos = 0;
        for (int i = 0; i < columnCount; i++)
            destination[targetSlots[i]] = ReadValue(data, ref pos);
    }

    private void WriteValue(object? value)
    {
        switch (value)
        {
            case null:
                WriteNull();
                break;
            case bool b:
                WriteBool(b);
                break;
            case decimal d:
                WriteDecimal(d);
                break;
            case double db:
                WriteDouble(db);
                break;
            case float f:
                WriteDouble(f);
                break;
            case DateTime dt:
                WriteDateTime(dt);
                break;
            case DateOnly dOnly:
                WriteDateOnly(dOnly);
                break;
            case string s:
                WriteString(s);
                break;
            case byte[] bin:
                WriteBinary(bin);
                break;
            case sbyte or byte or short or ushort or int or uint or long or ulong:
                // Integral types box as decimal on the common runtime path; store losslessly as decimal.
                WriteDecimal(Convert.ToDecimal(value));
                break;
            default:
                // Arrays / nested JSON objects / anything unexpected: round-trip through JSON.
                WriteJson(JsonSerializer.Serialize(value));
                break;
        }
    }

    private static object? ReadValue(ReadOnlySpan<byte> data, ref int pos)
    {
        byte tag = data[pos++];
        switch (tag)
        {
            case TNull: return null;
            case TBool: return data[pos++] != 0;
            case TDecimal:
                {
                    Span<int> bits = stackalloc int[4];
                    var slice = data.Slice(pos, 16);
                    bits[0] = BinaryPrimitives.ReadInt32LittleEndian(slice);
                    bits[1] = BinaryPrimitives.ReadInt32LittleEndian(slice.Slice(4));
                    bits[2] = BinaryPrimitives.ReadInt32LittleEndian(slice.Slice(8));
                    bits[3] = BinaryPrimitives.ReadInt32LittleEndian(slice.Slice(12));
                    pos += 16;
                    return new decimal(bits);
                }
            case TDouble:
                {
                    var val = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(data.Slice(pos, 8)));
                    pos += 8;
                    return val;
                }
            case TDateTime:
                {
                    long ticks = BinaryPrimitives.ReadInt64LittleEndian(data.Slice(pos, 8));
                    var kind = (DateTimeKind)data[pos + 8];
                    pos += 9;
                    return new DateTime(ticks, kind);
                }
            case TDateOnly:
                {
                    var dayNumber = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(pos, 4));
                    pos += 4;
                    return DateOnly.FromDayNumber(dayNumber);
                }
            case TString:
                {
                    int len = Read7BitEncodedInt(data, ref pos);
                    if (len == 0) return string.Empty;
                    var s = Encoding.UTF8.GetString(data.Slice(pos, len));
                    pos += len;
                    return s;
                }
            case TBinary:
                {
                    int len = Read7BitEncodedInt(data, ref pos);
                    if (len == 0) return Array.Empty<byte>();
                    var bytes = data.Slice(pos, len).ToArray();
                    pos += len;
                    return bytes;
                }
            case TJson:
                {
                    int len = Read7BitEncodedInt(data, ref pos);
                    var json = len == 0 ? string.Empty : Encoding.UTF8.GetString(data.Slice(pos, len));
                    pos += len;
                    try
                    {
                        var element = JsonSerializer.Deserialize<JsonElement>(json);
                        return SpillSerializationHelper.UnwrapJsonElement(element);
                    }
                    catch { return json; }
                }
            default:
                throw new InvalidDataException($"Unknown packed-row type tag {tag}.");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Read7BitEncodedInt(ReadOnlySpan<byte> data, ref int pos)
    {
        uint result = 0;
        byte byteReadJustNow;
        const int maxBytesWithoutOverflow = 4;
        for (int shift = 0; shift < maxBytesWithoutOverflow * 7; shift += 7)
        {
            byteReadJustNow = data[pos++];
            result |= (byteReadJustNow & 0x7Fu) << shift;
            if (byteReadJustNow <= 0x7Fu)
                return (int)result;
        }
        byteReadJustNow = data[pos++];
        if (byteReadJustNow > 0b_1111u)
            throw new FormatException("Invalid 7-bit encoded integer in packed row.");
        result |= (uint)byteReadJustNow << (maxBytesWithoutOverflow * 7);
        return (int)result;
    }
}
