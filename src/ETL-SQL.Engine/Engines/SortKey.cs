using System;
using System.Buffers.Text;
using System.Globalization;
using System.Text;
using System.Text.Json;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Engines;

internal enum SortKeyKind : byte
{
    Null = 0,
    Decimal = 1,
    DateTime = 2,
    Boolean = 3,
    String = 4,
    Bytes = 5,
    Other = 6,
}

/// <summary>
/// Unboxed tagged value container for normalized sort keys. Holds <see cref="decimal"/>,
/// <see cref="DateTime"/>, and <see cref="bool"/> inline so external sort entries and
/// key comparisons avoid per-row boxing and key-array allocations.
/// </summary>
internal readonly struct SortKey
{
    private readonly decimal _decimal;
    private readonly DateTime _dateTime;
    private readonly object? _obj;
    private readonly SortKeyKind _kind;
    private readonly bool _bool;

    public static SortKey Null => default;

    public SortKeyKind Kind => _kind;
    public bool IsNull => _kind == SortKeyKind.Null;
    public decimal DecimalValue => _decimal;
    public DateTime DateTimeValue => _dateTime;
    public bool BooleanValue => _bool;
    public string? StringValue => _obj as string;
    public byte[]? BytesValue => _obj as byte[];
    public object? RawObject => _obj;

    private SortKey(SortKeyKind kind, decimal dec = 0m, DateTime dt = default, bool b = false, object? obj = null)
    {
        _kind = kind;
        _decimal = dec;
        _dateTime = dt;
        _bool = b;
        _obj = obj;
    }

    public static SortKey FromDecimal(decimal value) => new(SortKeyKind.Decimal, dec: value);
    public static SortKey FromDateTime(DateTime value) => new(SortKeyKind.DateTime, dt: value);
    public static SortKey FromBoolean(bool value) => new(SortKeyKind.Boolean, b: value);
    public static SortKey FromString(string value) => new(SortKeyKind.String, obj: value);
    public static SortKey FromBytes(byte[] value) => new(SortKeyKind.Bytes, obj: value);
    public static SortKey FromOther(object value) => new(SortKeyKind.Other, obj: value);

    /// <summary>
    /// Normalizes a runtime cell value into a <see cref="SortKey"/> matching
    /// <see cref="CompoundKey.NormalizeValue"/> without boxing numeric, date, or boolean values.
    /// </summary>
    public static SortKey FromValue(object? val)
    {
        if (val == null || val == DBNull.Value) return Null;

        if (val is decimal dec) return FromDecimal(dec);
        if (val is int i) return FromDecimal(i);
        if (val is long l) return FromDecimal(l);
        if (val is bool b) return FromBoolean(b);
        if (val is DateTime dt) return FromDateTime(dt);
        if (val is DateOnly dateOnly) return FromDateTime(dateOnly.ToDateTime(TimeOnly.MinValue));
        if (val is DateTimeOffset dto) return FromDateTime(dto.DateTime);
        if (val is double d) return FromDecimal((decimal)d);
        if (val is float f) return FromDecimal((decimal)f);
        if (val is byte[] bytes) return FromBytes(bytes);

        if (val is string s)
            return FromStringValue(s);

        if (val is JsonElement)
            return FromNormalizedObject(CompoundKey.NormalizeValue(val));

        var normalized = CompoundKey.NormalizeValue(val);
        return FromNormalizedObject(normalized);
    }

    /// <summary>
    /// Extracts an unboxed <see cref="SortKey"/> directly from a <see cref="ColumnBatch"/> cell,
    /// avoiding boxing for numeric, date, and boolean columns.
    /// </summary>
    public static SortKey FromBatchColumn(ColumnBatch batch, int columnIndex, int rowIndex)
    {
        var column = batch.Columns[columnIndex];
        if (column.IsNull(rowIndex)) return Null;

        return column switch
        {
            ColumnBuffer<long> longs => FromDecimal(longs.Values.Span[rowIndex]),
            ColumnBuffer<int> ints => FromDecimal(ints.Values.Span[rowIndex]),
            ColumnBuffer<short> shorts => FromDecimal(shorts.Values.Span[rowIndex]),
            ColumnBuffer<byte> bytes => FromDecimal(bytes.Values.Span[rowIndex]),
            ColumnBuffer<decimal> decimals => FromDecimal(decimals.Values.Span[rowIndex]),
            ColumnBuffer<double> doubles => FromDoubleValue(doubles.Values.Span[rowIndex]),
            ColumnBuffer<bool> booleans => FromBoolean(booleans.Values.Span[rowIndex]),
            ColumnBuffer<DateTime> dates => FromDateTime(dates.Values.Span[rowIndex]),
            ColumnBuffer<DateOnly> dateOnlies => FromDateTime(dateOnlies.Values.Span[rowIndex].ToDateTime(TimeOnly.MinValue)),
            ColumnBuffer<DateTimeOffset> offsets => FromDateTime(offsets.Values.Span[rowIndex].DateTime),
            Utf8ColumnBuffer utf8 => FromUtf8Column(utf8, rowIndex),
            _ => FromValue(RowPacker.ReadBatchValue(batch, columnIndex, rowIndex)),
        };
    }

    private static SortKey FromDoubleValue(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return FromOther(value);
        try
        {
            return FromDecimal((decimal)value);
        }
        catch (OverflowException)
        {
            return FromOther(value);
        }
    }

    private static SortKey FromUtf8Column(Utf8ColumnBuffer utf8, int rowIndex)
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

        return FromStringValue(Encoding.UTF8.GetString(bytes));
    }

    private static SortKey FromStringValue(string s)
    {
        var span = s.AsSpan().Trim();
        if (HasDigit(span))
        {
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
        }

        // Text keeps its spaces: the in-memory sort compares the original string, so a spilled
        // sort keyed on the trimmed text put 'hello' before '  WORLD  '. Trimming is only for
        // recognizing a number or a date above.
        return FromString(s);
    }

    private static SortKey FromNormalizedObject(object? normalized) => normalized switch
    {
        null => Null,
        decimal dec => FromDecimal(dec),
        DateTime dt => FromDateTime(dt),
        bool b => FromBoolean(b),
        string s => FromString(s),
        byte[] bytes => FromBytes(bytes),
        _ => FromOther(normalized),
    };

    /// <summary>Boxes the key when an <see cref="object"/> value is required (e.g. spill stamping).</summary>
    public object? ToObject() => _kind switch
    {
        SortKeyKind.Null => null,
        SortKeyKind.Decimal => _decimal,
        SortKeyKind.DateTime => _dateTime,
        SortKeyKind.Boolean => _bool,
        SortKeyKind.String or SortKeyKind.Bytes or SortKeyKind.Other => _obj,
        _ => null,
    };

    /// <summary>Additional managed-heap bytes referenced beyond the inline struct payload.</summary>
    public long EstimateHeapBytes() => _kind switch
    {
        SortKeyKind.Null or SortKeyKind.Decimal or SortKeyKind.DateTime or SortKeyKind.Boolean => 0L,
        SortKeyKind.String => _obj is string s ? 24L + 2L * s.Length : 0L,
        SortKeyKind.Bytes => _obj is byte[] b ? 24L + b.Length : 0L,
        _ => Row.EstimateValueBytes(_obj),
    };

    /// <summary>
    /// Compares two non-null sort keys in ascending order using
    /// <see cref="EvaluationUtils.CompareConstants"/> semantics.
    /// </summary>
    public static int CompareNonNull(in SortKey a, in SortKey b, bool caseSensitive)
    {
        if (a._kind == b._kind)
        {
            switch (a._kind)
            {
                case SortKeyKind.Decimal:
                    return a._decimal.CompareTo(b._decimal);
                case SortKeyKind.DateTime:
                    return a._dateTime.CompareTo(b._dateTime);
                case SortKeyKind.Boolean:
                    return a._bool.CompareTo(b._bool);
                case SortKeyKind.Bytes:
                    return CompareBytes((byte[])a._obj!, (byte[])b._obj!);
                case SortKeyKind.String:
                    {
                        var sa = (string)a._obj!;
                        var sb = (string)b._obj!;
                        if (!HasDigit(sa) && !HasDigit(sb))
                        {
                            return string.Compare(
                                sa,
                                sb,
                                caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
                        }
                        return EvaluationUtils.CompareConstants(sa, sb, caseSensitive);
                    }
                default:
                    return EvaluationUtils.CompareConstants(a._obj, b._obj, caseSensitive);
            }
        }

        if (a._kind == SortKeyKind.Bytes || b._kind == SortKeyKind.Bytes)
            return a._kind == SortKeyKind.Bytes ? 1 : -1;

        if ((a._kind == SortKeyKind.String && HasDigit((string)a._obj!))
            || (b._kind == SortKeyKind.String && HasDigit((string)b._obj!))
            || a._kind == SortKeyKind.Other
            || b._kind == SortKeyKind.Other)
        {
            return EvaluationUtils.CompareConstants(a.ToObject(), b.ToObject(), caseSensitive);
        }

        // Numbers (Decimal) < Dates (DateTime) < Text/Boolean.
        if (a._kind == SortKeyKind.Decimal) return -1;
        if (b._kind == SortKeyKind.Decimal) return 1;
        if (a._kind == SortKeyKind.DateTime) return -1;
        if (b._kind == SortKeyKind.DateTime) return 1;

        return EvaluationUtils.CompareConstants(a.ToObject(), b.ToObject(), caseSensitive);
    }

    /// <summary>Compares two sort keys according to <paramref name="clause"/> (ASC/DESC and NULLS FIRST/LAST).</summary>
    public static int Compare(in SortKey a, in SortKey b, OrderByClause clause, bool caseSensitive)
    {
        if (a.IsNull || b.IsNull)
        {
            if (clause.NullsFirst is { } first)
                return a.IsNull == b.IsNull ? 0 : a.IsNull == first ? -1 : 1;

            int nullAsc = a.IsNull == b.IsNull ? 0 : a.IsNull ? -1 : 1;
            return clause.Descending ? -nullAsc : nullAsc;
        }

        int asc = CompareNonNull(a, b, caseSensitive);
        return clause.Descending ? -asc : asc;
    }

    internal static bool HasDigit(ReadOnlySpan<char> text)
    {
        foreach (var c in text)
            if (c is >= '0' and <= '9') return true;
        return false;
    }

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

    private static int CompareBytes(byte[] ba, byte[] bb)
    {
        int minLen = Math.Min(ba.Length, bb.Length);
        for (int i = 0; i < minLen; i++)
        {
            int cmp = ba[i].CompareTo(bb[i]);
            if (cmp != 0) return cmp;
        }
        return ba.Length.CompareTo(bb.Length);
    }
}
