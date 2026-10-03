using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json.Serialization;

namespace ETL_SQL.Reporting.Semantics;

/// <summary>A row-owned symbol and optional annotation at a complete semantic cross-section.</summary>
public sealed record ResolvedConnectionDecoration(decimal X, decimal Y, decimal Radius, string Shape,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ChartValue? Text)
{
    public static bool IsDecorationChannel(ConditionalEncodingChannel channel) => channel is
        ConditionalEncodingChannel.Size or ConditionalEncodingChannel.Shape or ConditionalEncodingChannel.Text;

    public static bool IsSupportedShape(string? shape) => shape?.Trim().ToUpperInvariant() is
        "CIRCLE" or "SQUARE" or "TRIANGLE" or "DIAMOND" or "CROSS" or "STAR";

    public static void ValidateEncoding(ResolvedEncodingValue encoding)
    {
        encoding.Value.Validate();
        if (encoding.Channel == ConditionalEncodingChannel.Size && encoding.Value.Kind is not
            (ChartValueKind.Null or ChartValueKind.Integer or ChartValueKind.Decimal or ChartValueKind.FloatingPoint))
            throw new InvalidDataException("Connected CONDITIONS SIZE requires a numeric or null value.");
        if (encoding.Channel == ConditionalEncodingChannel.Shape && encoding.Value.Kind != ChartValueKind.Null &&
            (encoding.Value.Kind != ChartValueKind.Text || !IsSupportedShape(encoding.Value.Text)))
            throw new InvalidDataException("Connected CONDITIONS SHAPE requires CIRCLE, SQUARE, TRIANGLE, DIAMOND, CROSS, STAR, or null.");
    }

    public static ResolvedConnectionDecoration? FromRaw(ResolvedDatum datum, ResolvedMarkLayer layer, ImmutableArray<StyleToken> plotStyle)
    {
        if (!datum.Encodings.IsDefaultOrEmpty)
        {
            if (datum.Encodings.Any(value => !Enum.IsDefined(value.Channel)) || datum.Encodings.Select(value => value.Channel).Distinct().Count() != datum.Encodings.Length)
                throw new InvalidDataException("Connected row encodings require unique supported channels.");
            foreach (var value in datum.Encodings) ValidateEncoding(value);
        }
        if (ResolvedMarkConnection.Coordinates(datum, layer.AreaRibbon) is not { } coordinates) return null;
        string? Style(ImmutableArray<StyleToken> tokens, string name) => tokens.IsDefaultOrEmpty ? null :
            tokens.FirstOrDefault(token => token.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;
        var defaultSize = Style(plotStyle, "SYMBOL_SIZE") ?? Style(layer.Style, "SYMBOL_SIZE");
        var radius = decimal.TryParse(defaultSize, NumberStyles.Number, CultureInfo.InvariantCulture, out var size) && size > 0m ? Math.Clamp(size, 2m, 30m) : 3m;
        var shape = Style(layer.Style, "symbolShape") ?? Style(plotStyle, "SYMBOL_SHAPE");
        ChartValue? text = null;
        if (!datum.Encodings.IsDefaultOrEmpty)
            foreach (var value in datum.Encodings)
            {
                if (value.Channel == ConditionalEncodingChannel.Size)
                    radius = value.Value.Kind switch
                    {
                        ChartValueKind.Integer => Math.Clamp(value.Value.Integer!.Value, 2L, 30L),
                        ChartValueKind.Decimal => Math.Clamp(value.Value.Decimal!.Value, 2m, 30m),
                        ChartValueKind.FloatingPoint => (decimal)Math.Clamp(value.Value.FloatingPoint!.Value, 2d, 30d),
                        _ => radius
                    };
                if (value.Channel == ConditionalEncodingChannel.Shape && value.Value.Kind != ChartValueKind.Null) shape = value.Value.Text;
                if (value.Channel == ConditionalEncodingChannel.Text) text = value.Value;
            }
        return new(coordinates.X, (layer.AreaRibbon ? coordinates.YEnd : coordinates.Y)!.Value, radius,
            IsSupportedShape(shape) ? shape!.Trim().ToUpperInvariant() : "CIRCLE", text);
    }
}
