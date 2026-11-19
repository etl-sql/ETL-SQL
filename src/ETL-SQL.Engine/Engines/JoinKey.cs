using System;
using System.Buffers.Text;
using System.Globalization;
using System.Text;
using System.Text.Json;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Engines;

internal enum JoinKeyKind : byte
{
    Null = 0,
    Decimal = 1,
    DateTime = 2,
    Boolean = 3,
    String = 4,
    Other = 5,
}

/// <summary>
/// Unboxed tagged value container for normalized join keys. Preserves
/// <see cref="CompoundKey.NormalizeValue"/> semantics (including decimal scale stripping,
/// plain-number string coercion, date-string parsing, and case sensitivity) without allocating
/// per-row key arrays or boxing primitives.
/// </summary>
internal readonly struct JoinKey
{
    private readonly decimal _decimal;
    private readonly DateTime _dateTime;
    private readonly object? _obj;
    private readonly JoinKeyKind _kind;
    private readonly bool _bool;

    public static JoinKey Null => default;

    public JoinKeyKind Kind => _kind;
    public bool IsNull => _kind == JoinKeyKind.Null;

    private JoinKey(JoinKeyKind kind, decimal dec = 0m, DateTime dt = default, bool b = false, object? obj = null)
    {
        _kind = kind;
        _decimal = dec;
        _dateTime = dt;
        _bool = b;
        _obj = obj;
    }

    public static JoinKey FromDecimal(decimal value)
    {
        if (value.Scale != 0)
            value /= 1.00000000000000000000000000000m;
        return new(JoinKeyKind.Decimal, dec: value);
    }

    public static JoinKey FromDateTime(DateTime value) => new(JoinKeyKind.DateTime, dt: value);
    public static JoinKey FromBoolean(bool value) => new(JoinKeyKind.Boolean, b: value);
    public static JoinKey FromString(string value) => new(JoinKeyKind.String, obj: value);
    public static JoinKey FromOther(object value) => new(JoinKeyKind.Other, obj: value);

    public static JoinKey FromValue(object? val)
    {
        if (val == null || val == DBNull.Value) return Null;

        if (val is JsonElement je)
        {
            val = je.ValueKind switch
            {
                JsonValueKind.Number when je.TryGetDecimal(out var dv) => dv,
                JsonValueKind.Number => je.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => je.GetString(),
                JsonValueKind.Null => null,
                _ => (object?)je.ToString()
            };
            if (val == null) return Null;
        }

        if (val is sbyte sb) return new(JoinKeyKind.Decimal, dec: sb);
        if (val is byte by) return new(JoinKeyKind.Decimal, dec: by);
        if (val is short sh) return new(JoinKeyKind.Decimal, dec: sh);
        if (val is ushort ush) return new(JoinKeyKind.Decimal, dec: ush);
        if (val is int i) return new(JoinKeyKind.Decimal, dec: i);
        if (val is uint ui) return new(JoinKeyKind.Decimal, dec: ui);
        if (val is long l) return new(JoinKeyKind.Decimal, dec: l);
        if (val is ulong ul) return new(JoinKeyKind.Decimal, dec: ul);
        if (val is decimal dec) return FromDecimal(dec);
        if (val is double d) return FromDecimal((decimal)d);
        if (val is float f) return FromDecimal((decimal)f);
        if (val is bool b) return FromBoolean(b);
        if (val is DateTime dt) return FromDateTime(dt);
        if (val is DateOnly dateOnly) return FromDateTime(dateOnly.ToDateTime(TimeOnly.MinValue));
        if (val is DateTimeOffset dto) return FromDateTime(dto.DateTime);
        if (val is string s) return FromStringValue(s);

        return FromOther(val);
    }

    public static JoinKey FromBatchColumn(ColumnBatch batch, int columnIndex, int rowIndex)
    {
        var column = batch.Columns[columnIndex];
        if (column.IsNull(rowIndex)) return Null;

        return column switch
        {
            ColumnBuffer<long> longs => new(JoinKeyKind.Decimal, dec: longs.Values.Span[rowIndex]),
            ColumnBuffer<int> ints => new(JoinKeyKind.Decimal, dec: ints.Values.Span[rowIndex]),
            ColumnBuffer<short> shorts => new(JoinKeyKind.Decimal, dec: shorts.Values.Span[rowIndex]),
            ColumnBuffer<byte> bytes => new(JoinKeyKind.Decimal, dec: bytes.Values.Span[rowIndex]),
            ColumnBuffer<decimal> decimals => FromDecimal(decimals.Values.Span[rowIndex]),
            ColumnBuffer<double> doubles => FromDecimal((decimal)doubles.Values.Span[rowIndex]),
            ColumnBuffer<bool> booleans => FromBoolean(booleans.Values.Span[rowIndex]),
            ColumnBuffer<DateTime> dates => FromDateTime(dates.Values.Span[rowIndex]),
            ColumnBuffer<DateOnly> dateOnlies => FromDateTime(dateOnlies.Values.Span[rowIndex].ToDateTime(TimeOnly.MinValue)),
            ColumnBuffer<DateTimeOffset> offsets => FromDateTime(offsets.Values.Span[rowIndex].DateTime),
            Utf8ColumnBuffer utf8 => FromUtf8Column(utf8, batch.Schema.Fields[columnIndex].LogicalType, rowIndex),
            _ => FromValue(RowPacker.ReadBatchValue(batch, columnIndex, rowIndex)),
        };
    }

    private static JoinKey FromUtf8Column(Utf8ColumnBuffer utf8, string logicalType, int rowIndex)
    {
        var bytes = utf8.GetUtf8Bytes(rowIndex);
        if (bytes.StartsWith("\x1Ejson:"u8))
        {
            var text = Encoding.UTF8.GetString(bytes);
            try
            {
                var element = JsonSerializer.Deserialize<JsonElement>(text.AsSpan("\x1Ejson:".Length));
                return FromValue(ETL_SQL.Engine.Spill.SpillSerializationHelper.UnwrapJsonElement(element));
            }
            catch
            {
                return FromStringValue(text);
            }
        }

        if (bytes.StartsWith("\x1Edateonly:"u8))
        {
            var text = Encoding.UTF8.GetString(bytes.Slice("\x1Edateonly:".Length));
            if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
                return FromDateTime(dateOnly.ToDateTime(TimeOnly.MinValue));
            return FromStringValue(text);
        }

        if (logicalType == "String")
            return FromUtf8StringBytes(bytes);

        if (logicalType == "Dynamic")
        {
            var text = Encoding.UTF8.GetString(bytes);
            if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
                return FromDecimal(number);
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date))
                return FromDateTime(date);
            return FromStringValue(text);
        }

        return FromUtf8StringBytes(bytes);
    }

    private static JoinKey FromUtf8StringBytes(ReadOnlySpan<byte> bytes)
    {
        var trimmed = TrimAsciiWhitespace(bytes);
        if (IsPlainNumberUtf8(trimmed)
            && Utf8Parser.TryParse(trimmed, out decimal parsedDec, out var consumed)
            && consumed == trimmed.Length)
        {
            return FromDecimal(parsedDec);
        }

        return FromStringValue(Encoding.UTF8.GetString(bytes));
    }

    private static JoinKey FromStringValue(string s)
    {
        var span = s.AsSpan().Trim();
        var trimmed = span.Length == s.Length ? s : span.ToString();
        if (IsPlainNumber(trimmed)
            && decimal.TryParse(
                trimmed,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var parsedDec))
        {
            return FromDecimal(parsedDec);
        }

        if (EvaluationUtils.SafeTryParseDate(trimmed, out var parsedDt))
            return FromDateTime(parsedDt);

        return FromString(trimmed);
    }

    public void AddToHash(ref HashCode hash, bool caseSensitive)
    {
        switch (_kind)
        {
            case JoinKeyKind.Null:
                hash.Add<object?>(null);
                break;
            case JoinKeyKind.Decimal:
                hash.Add(_decimal);
                break;
            case JoinKeyKind.DateTime:
                hash.Add(_dateTime);
                break;
            case JoinKeyKind.Boolean:
                hash.Add(_bool);
                break;
            case JoinKeyKind.String:
                var s = (string)_obj!;
                if (!caseSensitive)
                    hash.Add(s, StringComparer.OrdinalIgnoreCase);
                else
                    hash.Add(s);
                break;
            default:
                hash.Add(_obj);
                break;
        }
    }

    public static int ComputeHashCode(int setIndex, in JoinKey key0, ReadOnlySpan<JoinKey> extraKeys, bool caseSensitive)
    {
        var hash = new HashCode();
        hash.Add(setIndex);
        key0.AddToHash(ref hash, caseSensitive);
        for (int i = 0; i < extraKeys.Length; i++)
            extraKeys[i].AddToHash(ref hash, caseSensitive);
        return hash.ToHashCode();
    }

    public static int ComputeEmptyHashCode(int setIndex) => HashCode.Combine(setIndex);

    public static bool Equals(in JoinKey a, in JoinKey b, bool caseSensitive)
    {
        if (a._kind != b._kind) return false;
        return a._kind switch
        {
            JoinKeyKind.Null => true,
            JoinKeyKind.Decimal => a._decimal == b._decimal,
            JoinKeyKind.DateTime => a._dateTime == b._dateTime,
            JoinKeyKind.Boolean => a._bool == b._bool,
            JoinKeyKind.String => string.Equals(
                (string)a._obj!,
                (string)b._obj!,
                caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase),
            _ => object.Equals(a._obj, b._obj),
        };
    }

    public long EstimateHeapBytes() => _kind switch
    {
        JoinKeyKind.Null => 0L,
        JoinKeyKind.Decimal => 32L,
        JoinKeyKind.DateTime or JoinKeyKind.Boolean => 24L,
        JoinKeyKind.String => _obj is string s ? 24L + 2L * s.Length : 0L,
        _ => Row.EstimateValueBytes(_obj),
    };

    private static ReadOnlySpan<byte> TrimAsciiWhitespace(ReadOnlySpan<byte> bytes)
    {
        int start = 0;
        int end = bytes.Length - 1;
        while (start <= end && bytes[start] <= 0x20) start++;
        while (end >= start && bytes[end] <= 0x20) end--;
        return bytes.Slice(start, end - start + 1);
    }

    // -?(0|[1-9][0-9]*)(\.[0-9]+)?
    private static bool IsPlainNumber(string s)
    {
        int i = 0;
        if (i < s.Length && s[i] == '-') i++;
        int start = i;
        while (i < s.Length && char.IsAsciiDigit(s[i])) i++;
        int digits = i - start;
        if (digits == 0 || (digits > 1 && s[start] == '0')) return false;
        if (i == s.Length) return true;
        if (s[i] != '.') return false;
        int fraction = ++i;
        while (i < s.Length && char.IsAsciiDigit(s[i])) i++;
        return i > fraction && i == s.Length;
    }

    private static bool IsPlainNumberUtf8(ReadOnlySpan<byte> s)
    {
        int i = 0;
        if (i < s.Length && s[i] == (byte)'-') i++;
        int start = i;
        while (i < s.Length && s[i] is >= (byte)'0' and <= (byte)'9') i++;
        int digits = i - start;
        if (digits == 0 || (digits > 1 && s[start] == (byte)'0')) return false;
        if (i == s.Length) return true;
        if (s[i] != (byte)'.') return false;
        int fraction = ++i;
        while (i < s.Length && s[i] is >= (byte)'0' and <= (byte)'9') i++;
        return i > fraction && i == s.Length;
    }
}
