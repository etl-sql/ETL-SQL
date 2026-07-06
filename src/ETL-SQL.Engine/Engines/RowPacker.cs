using System;
using System.Collections.Generic;
using System.IO;
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
internal sealed class RowPacker
{
    private const byte TNull = 0;
    private const byte TBool = 1;
    private const byte TDecimal = 2;
    private const byte TDouble = 3;
    private const byte TDateTime = 4;
    private const byte TString = 5;
    private const byte TJson = 6;

    // Reused across rows in a single (sequential) build so packing doesn't allocate a stream per row;
    // only the returned byte[] (the retained blob) is freshly allocated.
    private readonly MemoryStream _ms = new();
    private readonly BinaryWriter _writer;

    public RowPacker()
    {
        _writer = new BinaryWriter(_ms, Encoding.UTF8, leaveOpen: true);
    }

    /// <summary>Packs the named columns of <paramref name="row"/> into a fresh byte[] blob.</summary>
    public byte[] Pack(Row row, IReadOnlyList<string> columns)
    {
        _ms.SetLength(0);
        for (int i = 0; i < columns.Count; i++)
            WriteValue(_writer, row[columns[i]]);
        _writer.Flush();
        return _ms.ToArray();
    }

    /// <summary>Packs one native batch row without constructing a <see cref="Row"/> object.</summary>
    public byte[] Pack(ColumnBatch batch, int rowIndex)
    {
        _ms.SetLength(0);
        for (var column = 0; column < batch.Schema.Count; column++)
            WriteValue(_writer, ReadBatchValue(batch, column, rowIndex));
        _writer.Flush();
        return _ms.ToArray();
    }

    internal static object? ReadBatchValue(ColumnBatch batch, int columnIndex, int rowIndex)
    {
        var value = batch.Columns[columnIndex].GetBoxedValue(rowIndex);
        if (value is not string text) return value;
        var logicalType = batch.Schema.Fields[columnIndex].LogicalType;
        if (logicalType == "String") return text;
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
        if (logicalType == "Dynamic")
        {
            if (decimal.TryParse(text, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var number)) return number;
            if (DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var date)) return date;
        }
        return text;
    }

    internal static Row MaterializeBatchRow(ColumnBatch batch, int rowIndex)
    {
        var row = new Row();
        for (var column = 0; column < batch.Schema.Count; column++)
            row[batch.Schema.Fields[column].Name] = ReadBatchValue(batch, column, rowIndex);
        return row;
    }

    /// <summary>Reconstructs a <see cref="Row"/> from a blob produced by <see cref="Pack"/>.</summary>
    public static Row Unpack(byte[] data, IReadOnlyList<string> columns)
    {
        var row = new Row();
        using var ms = new MemoryStream(data, writable: false);
        using var reader = new BinaryReader(ms, Encoding.UTF8);
        for (int i = 0; i < columns.Count; i++)
            row[columns[i]] = ReadValue(reader);
        return row;
    }

    private static void WriteValue(BinaryWriter w, object? value)
    {
        switch (value)
        {
            case null:
                w.Write(TNull);
                break;
            case bool b:
                w.Write(TBool);
                w.Write(b);
                break;
            case decimal d:
                w.Write(TDecimal);
                foreach (var bits in decimal.GetBits(d)) w.Write(bits);
                break;
            case double db:
                w.Write(TDouble);
                w.Write(db);
                break;
            case float f:
                w.Write(TDouble);
                w.Write((double)f);
                break;
            case DateTime dt:
                w.Write(TDateTime);
                w.Write(dt.Ticks);
                w.Write((byte)dt.Kind);
                break;
            case string s:
                w.Write(TString);
                w.Write(s);
                break;
            case sbyte or byte or short or ushort or int or uint or long or ulong:
                // Integral types box as decimal on the common runtime path; store losslessly as decimal.
                w.Write(TDecimal);
                foreach (var bits in decimal.GetBits(Convert.ToDecimal(value))) w.Write(bits);
                break;
            default:
                // Arrays / nested JSON objects / anything unexpected: round-trip through JSON.
                w.Write(TJson);
                w.Write(JsonSerializer.Serialize(value));
                break;
        }
    }

    private static object? ReadValue(BinaryReader r)
    {
        byte tag = r.ReadByte();
        switch (tag)
        {
            case TNull: return null;
            case TBool: return r.ReadBoolean();
            case TDecimal:
                {
                    Span<int> bits = stackalloc int[4];
                    for (int i = 0; i < 4; i++) bits[i] = r.ReadInt32();
                    return new decimal(bits);
                }
            case TDouble: return r.ReadDouble();
            case TDateTime:
                {
                    long ticks = r.ReadInt64();
                    var kind = (DateTimeKind)r.ReadByte();
                    return new DateTime(ticks, kind);
                }
            case TString: return r.ReadString();
            case TJson:
                {
                    var json = r.ReadString();
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
}
