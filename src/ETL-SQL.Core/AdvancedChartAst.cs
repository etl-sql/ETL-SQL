using System.Collections.Immutable;

namespace ETL_SQL.Core;

public enum AdvancedChartMarkKind { Rect, Line, Area, Point, Rule, Arc, Text, Tick }
public enum AdvancedChartDataKind { Quantitative, Temporal, Nominal, Ordinal }
public enum AdvancedChartChannel
{
    X, X2, XStart, XEnd, XOffset,
    Y, Y2, YStart, YEnd, YOffset,
    Low, Q1, Median, Q3, High, Open, Close,
    Color, Size, Shape, Theta, Radius, Text, Tooltip, Detail
}
public enum AdvancedChartAxisRole { None, Primary, Secondary }
public enum AdvancedChartSortDirection { Source, Ascending, Descending }
public enum AdvancedChartScaleKind { Linear, Logarithmic, Time, Band, Point, Ordinal, Identity }
public enum AdvancedChartCoordinateKind { Cartesian, TransposedCartesian, Polar }
public enum AdvancedChartResolutionMode { Shared, Independent }
public enum AdvancedChartConditionChannel { Color, Opacity, Size, Shape, Text }
public enum AdvancedChartBindingSourceKind { Field, Datum, Value }
public enum AdvancedChartStackMode { None, Zero, Normalize }
public enum AdvancedChartPositionKind { Identity, Jitter, Nudge }
public enum AdvancedChartPositionUnit { Data, Band, Em }
public enum AdvancedChartTickOrientation { Auto, Horizontal, Vertical }
public enum AdvancedChartColorRangeKind { Gradient, Diverging }

public static class AdvancedChartScaleInference
{
    public static AdvancedChartScaleKind? Infer(AdvancedChartChannel channel, AdvancedChartDataKind dataKind,
        AdvancedChartMarkKind mark) => channel switch
        {
            AdvancedChartChannel.X or AdvancedChartChannel.X2 or AdvancedChartChannel.XStart or AdvancedChartChannel.XEnd or
            AdvancedChartChannel.Y or AdvancedChartChannel.Y2 or AdvancedChartChannel.YStart or AdvancedChartChannel.YEnd or
            AdvancedChartChannel.Low or AdvancedChartChannel.Q1 or AdvancedChartChannel.Median or AdvancedChartChannel.Q3 or
            AdvancedChartChannel.High or AdvancedChartChannel.Open or AdvancedChartChannel.Close => dataKind switch
            {
                AdvancedChartDataKind.Quantitative => AdvancedChartScaleKind.Linear,
                AdvancedChartDataKind.Temporal => AdvancedChartScaleKind.Time,
                AdvancedChartDataKind.Nominal or AdvancedChartDataKind.Ordinal when mark is AdvancedChartMarkKind.Rect or AdvancedChartMarkKind.Tick => AdvancedChartScaleKind.Band,
                AdvancedChartDataKind.Nominal or AdvancedChartDataKind.Ordinal when mark is AdvancedChartMarkKind.Point or AdvancedChartMarkKind.Line => AdvancedChartScaleKind.Point,
                _ => null
            },
            AdvancedChartChannel.Color => dataKind == AdvancedChartDataKind.Quantitative ? AdvancedChartScaleKind.Linear : AdvancedChartScaleKind.Ordinal,
            AdvancedChartChannel.Shape => dataKind is AdvancedChartDataKind.Nominal or AdvancedChartDataKind.Ordinal ? AdvancedChartScaleKind.Ordinal : null,
            AdvancedChartChannel.Size => dataKind == AdvancedChartDataKind.Quantitative ? AdvancedChartScaleKind.Linear : null,
            AdvancedChartChannel.Theta => dataKind is AdvancedChartDataKind.Nominal or AdvancedChartDataKind.Ordinal ? AdvancedChartScaleKind.Ordinal : AdvancedChartScaleKind.Linear,
            AdvancedChartChannel.Radius => dataKind == AdvancedChartDataKind.Quantitative ? AdvancedChartScaleKind.Linear : null,
            AdvancedChartChannel.XOffset or AdvancedChartChannel.YOffset => dataKind is AdvancedChartDataKind.Nominal or AdvancedChartDataKind.Ordinal ? AdvancedChartScaleKind.Band : null,
            _ => null
        };
}

public sealed record AdvancedChartColorRange : AstNode
{
    public required AdvancedChartColorRangeKind Kind { get; init; }
    public required Expression Low { get; init; }
    public Expression? Mid { get; init; }
    public required Expression High { get; init; }
    public Expression? Midpoint { get; init; }
    public Expression? NullColor { get; init; }
}

public sealed record AdvancedChartPosition : AstNode
{
    public AdvancedChartPositionKind Kind { get; init; }
    public decimal X { get; init; }
    public decimal Y { get; init; }
    public string? KeyField { get; init; }
    public int Seed { get; init; }
    public AdvancedChartPositionUnit Unit { get; init; } = AdvancedChartPositionUnit.Band;
}

public sealed record AdvancedChartBindingSource : AstNode
{
    public required AdvancedChartBindingSourceKind Kind { get; init; }
    public string? Field { get; init; }
    public Expression? Constant { get; init; }
}

public sealed record AdvancedChartEncoding : AstNode
{
    public required AdvancedChartChannel Channel { get; init; }
    public required AdvancedChartBindingSource Source { get; init; }
    public required AdvancedChartDataKind DataKind { get; init; }
    public string? Scale { get; init; }
    public AdvancedChartAxisRole Axis { get; init; }
    public AdvancedChartSortDirection Sort { get; init; }
    public string? Format { get; init; }
    public AdvancedChartStackMode Stack { get; init; }
}

public sealed record AdvancedChartStyle(string Name, Expression Value) : AstNode;

public sealed record AdvancedChartCondition : AstNode
{
    public required AdvancedChartConditionChannel Channel { get; init; }
    public required Expression Predicate { get; init; }
    public required Expression WhenTrue { get; init; }
    public Expression? WhenFalse { get; init; }
}

public sealed record AdvancedChartLayer : AstNode
{
    public required string Name { get; init; }
    public required AdvancedChartMarkKind Mark { get; init; }
    public required int ZIndex { get; init; }
    public bool InheritEncodings { get; init; } = true;
    public decimal BandSize { get; init; } = .75m;
    public decimal TickThickness { get; init; } = .15m;
    public AdvancedChartTickOrientation TickOrientation { get; init; }
    public AdvancedChartPosition Position { get; init; } = new();
    public ImmutableArray<AdvancedChartEncoding> Encodings { get; init; } = [];
    public ImmutableArray<AdvancedChartStyle> Styles { get; init; } = [];
    public ImmutableArray<AdvancedChartCondition> Conditions { get; init; } = [];
}

public sealed record AdvancedChartScale : AstNode
{
    public required string Name { get; init; }
    public required AdvancedChartScaleKind Kind { get; init; }
    public required AdvancedChartChannel Channel { get; init; }
    public bool IncludeZero { get; init; }
    public Expression? Minimum { get; init; }
    public Expression? Maximum { get; init; }
    public AdvancedChartSortDirection Order { get; init; }
    public ImmutableArray<Expression> ExplicitOrder { get; init; } = [];
    public AdvancedChartColorRange? ColorRange { get; init; }
}

public sealed record AdvancedChartCoordinate : AstNode
{
    public required AdvancedChartCoordinateKind Kind { get; init; }
    public decimal? StartAngle { get; init; }
    public decimal? EndAngle { get; init; }
    public decimal? InnerRadius { get; init; }
    public decimal? AspectRatio { get; init; }
}

public sealed record AdvancedChartFacet : AstNode
{
    public string? RowField { get; init; }
    public string? ColumnField { get; init; }
    public string? WrapField { get; init; }
    public int? Columns { get; init; }
}

public sealed record AdvancedChartResolution : AstNode
{
    public AdvancedChartResolutionMode X { get; init; } = AdvancedChartResolutionMode.Shared;
    public AdvancedChartResolutionMode Y { get; init; } = AdvancedChartResolutionMode.Shared;
    public AdvancedChartResolutionMode Color { get; init; } = AdvancedChartResolutionMode.Shared;
}

public sealed record AdvancedChartDefinition : AstNode
{
    public required AdvancedChartCoordinate Coordinate { get; init; }
    public ImmutableArray<AdvancedChartScale> Scales { get; init; } = [];
    public ImmutableArray<AdvancedChartEncoding> Encodings { get; init; } = [];
    public ImmutableArray<AdvancedChartLayer> Layers { get; init; } = [];
    public AdvancedChartFacet? Facet { get; init; }
    public AdvancedChartResolution Resolution { get; init; } = new();
    public override string ToSql() => Formatting.AstSerializer.Format(this);
}
