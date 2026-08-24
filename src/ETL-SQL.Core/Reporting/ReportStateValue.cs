using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Core.Reporting;

/// <summary>
/// The kind of a typed value carried by a resolved report-state envelope. Kept deliberately small:
/// bookmark/saved-view parameter values are typed scalar literals, never arbitrary expressions.
/// </summary>
public enum ReportStateValueKind
{
    Null,
    String,
    Number,
    Boolean
}

/// <summary>
/// A single typed scalar value inside a <see cref="ResolvedReportState"/>. Preserves the author's
/// declared type (number, boolean, string, or null) through parsing, serialization, formatter
/// round-trips, parameter refresh, and snapshot replay. Serializes as a bare JSON token
/// (<c>2026</c>, <c>true</c>, <c>"West"</c>, <c>null</c>) — never as a quoted string for a numeric,
/// boolean, or null value.
/// </summary>
[JsonConverter(typeof(ReportStateValueJsonConverter))]
public sealed class ReportStateValue : IEquatable<ReportStateValue>
{
    public ReportStateValueKind Kind { get; }
    public string? StringValue { get; }
    public decimal? NumberValue { get; }
    public bool? BooleanValue { get; }

    private ReportStateValue(ReportStateValueKind kind, string? s, decimal? n, bool? b)
    {
        Kind = kind;
        StringValue = s;
        NumberValue = n;
        BooleanValue = b;
    }

    public static readonly ReportStateValue Null = new(ReportStateValueKind.Null, null, null, null);
    public static ReportStateValue FromString(string value) => new(ReportStateValueKind.String, value, null, null);
    public static ReportStateValue FromNumber(decimal value) => new(ReportStateValueKind.Number, null, value, null);
    public static ReportStateValue FromBoolean(bool value) => new(ReportStateValueKind.Boolean, null, null, value);

    /// <summary>Maps a parsed literal to a typed value, preserving its declared kind.</summary>
    public static ReportStateValue FromLiteral(LiteralExpression literal) => FromObject(literal.Value, literal.Type);

    /// <summary>Maps a runtime-evaluated CLR value to a typed value.</summary>
    public static ReportStateValue FromObject(object? value, TokenType? sourceType = null)
    {
        switch (value)
        {
            case null:
                return Null;
            case bool b:
                return FromBoolean(b);
            case string s:
                // ON/OFF/TRUE/FALSE literals arrive as bool from the lexer; a raw string stays a string.
                return FromString(s);
            case decimal d:
                return FromNumber(d);
            case double dbl:
                return FromNumber((decimal)dbl);
            case float f:
                return FromNumber((decimal)f);
            case long l:
                return FromNumber(l);
            case int i:
                return FromNumber(i);
            case short sh:
                return FromNumber(sh);
            case byte by:
                return FromNumber(by);
            default:
                return FromString(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
        }
    }

    /// <summary>
    /// Best-effort typing of a legacy raw string (e.g. from an existing <c>ParametersJson</c> dictionary,
    /// which stored every value as a string). Numeric and boolean-looking strings are recovered so a
    /// legacy view converges on the same typed envelope as a freshly-saved one.
    /// </summary>
    public static ReportStateValue FromLegacyString(string? raw)
    {
        if (raw == null) return Null;
        if (raw.Length == 0) return FromString(string.Empty);
        if (bool.TryParse(raw, out var b)) return FromBoolean(b);
        // Only treat as a number when the invariant round-trip is exact; this preserves leading-zero
        // codes ("007"), phone numbers, and thousands-separated strings as strings rather than numbers.
        if (decimal.TryParse(raw, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var d)
            && d.ToString(CultureInfo.InvariantCulture) == raw)
        {
            return FromNumber(d);
        }
        return FromString(raw);
    }

    /// <summary>Canonical string projection used where the runtime/engine expects a string parameter value.</summary>
    public string ToCanonicalString() => Kind switch
    {
        ReportStateValueKind.Null => string.Empty,
        ReportStateValueKind.String => StringValue ?? string.Empty,
        ReportStateValueKind.Number => NumberValue!.Value.ToString(CultureInfo.InvariantCulture),
        ReportStateValueKind.Boolean => BooleanValue!.Value ? "TRUE" : "FALSE",
        _ => string.Empty
    };

    /// <summary>Renders the value as it would appear in Report-SQL source (for formatter round-trips).</summary>
    public string ToSqlLiteral() => Kind switch
    {
        ReportStateValueKind.Null => "NULL",
        ReportStateValueKind.String => $"'{(StringValue ?? string.Empty).Replace("'", "''")}'",
        ReportStateValueKind.Number => NumberValue!.Value.ToString(CultureInfo.InvariantCulture),
        ReportStateValueKind.Boolean => BooleanValue!.Value ? "TRUE" : "FALSE",
        _ => "NULL"
    };

    public bool Equals(ReportStateValue? other)
    {
        if (other is null) return false;
        if (Kind != other.Kind) return false;
        return Kind switch
        {
            ReportStateValueKind.Null => true,
            ReportStateValueKind.String => StringValue == other.StringValue,
            ReportStateValueKind.Number => NumberValue == other.NumberValue,
            ReportStateValueKind.Boolean => BooleanValue == other.BooleanValue,
            _ => false
        };
    }

    public override bool Equals(object? obj) => Equals(obj as ReportStateValue);

    public override int GetHashCode() => Kind switch
    {
        ReportStateValueKind.String => HashCode.Combine(Kind, StringValue),
        ReportStateValueKind.Number => HashCode.Combine(Kind, NumberValue),
        ReportStateValueKind.Boolean => HashCode.Combine(Kind, BooleanValue),
        _ => Kind.GetHashCode()
    };

    public override string ToString() => ToCanonicalString();
}

/// <summary>Reads/writes a <see cref="ReportStateValue"/> as a bare JSON token so typed values survive round trips.</summary>
public sealed class ReportStateValueJsonConverter : JsonConverter<ReportStateValue>
{
    // Handle null so a null token round-trips to ReportStateValue.Null instead of a CLR null,
    // both at the top level and as a dictionary/collection value in the envelope.
    public override bool HandleNull => true;

    public override ReportStateValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return ReportStateValue.Null;
            case JsonTokenType.True:
                return ReportStateValue.FromBoolean(true);
            case JsonTokenType.False:
                return ReportStateValue.FromBoolean(false);
            case JsonTokenType.Number:
                return ReportStateValue.FromNumber(reader.GetDecimal());
            case JsonTokenType.String:
                return ReportStateValue.FromString(reader.GetString() ?? string.Empty);
            default:
                throw new JsonException("Report-state parameter values must be JSON scalars.");
        }
    }

    public override void Write(Utf8JsonWriter writer, ReportStateValue value, JsonSerializerOptions options)
    {
        switch (value.Kind)
        {
            case ReportStateValueKind.Null:
                writer.WriteNullValue();
                break;
            case ReportStateValueKind.Boolean:
                writer.WriteBooleanValue(value.BooleanValue!.Value);
                break;
            case ReportStateValueKind.Number:
                writer.WriteNumberValue(value.NumberValue!.Value);
                break;
            default:
                writer.WriteStringValue(value.StringValue ?? string.Empty);
                break;
        }
    }
}
