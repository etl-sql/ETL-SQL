using System.Text.Json.Serialization;

namespace ETL_SQL.Reporting.Semantics;

/// <summary>Resolved connected geometry kept separately from raw source channels.</summary>
public sealed record ResolvedConnectionCoordinates(
    decimal X,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? Y = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? YStart = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? YEnd = null)
{
    public static ResolvedConnectionCoordinates? FromRaw(ResolvedDatum datum, bool ribbon, bool zero)
    {
        ChartValue? Value(FieldChannel channel)
        {
            foreach (var value in datum.Channels)
                if (value.Channel == channel) return value.Value;
            return null;
        }
        static decimal? Number(ChartValue? value) => value?.Kind switch
        {
            ChartValueKind.Integer => value.Integer,
            ChartValueKind.Decimal => value.Decimal,
            ChartValueKind.FloatingPoint => (decimal?)value.FloatingPoint,
            _ => null
        };
        if (Number(Value(FieldChannel.X)) is not { } x) return null;
        if (ribbon)
            return Number(Value(FieldChannel.YStart)) is { } start && Number(Value(FieldChannel.YEnd)) is { } end
                ? new(x, YStart: start, YEnd: end) : null;
        var rawY = Value(FieldChannel.Y);
        var y = zero && rawY?.Kind == ChartValueKind.Null ? 0m : Number(rawY);
        return y is { } resolvedY ? new(x, resolvedY) : null;
    }
}
