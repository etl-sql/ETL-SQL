using System.Collections.Immutable;

namespace ETL_SQL.Core;

public enum AdvancedChartMarkKind { Rect, Line, Area, Point, Rule, Arc, Text }
public enum AdvancedChartDataKind { Quantitative, Temporal, Nominal, Ordinal }
public enum AdvancedChartChannel { X, Y, Y2, Color, Size, Shape, Theta, Radius, Text, Tooltip, Detail }
public enum AdvancedChartAxisRole { None, Primary, Secondary }
public enum AdvancedChartSortDirection { Source, Ascending, Descending }
public enum AdvancedChartScaleKind { Linear, Logarithmic, Time, Band, Point, Ordinal, Identity }
public enum AdvancedChartCoordinateKind { Cartesian, TransposedCartesian, Polar }
public enum AdvancedChartResolutionMode { Shared, Independent }
public enum AdvancedChartConditionChannel { Color, Opacity, Size, Shape, Text }

public sealed record AdvancedChartEncoding : AstNode
{
    public required AdvancedChartChannel Channel { get; init; }
    public required string Field { get; init; }
    public required AdvancedChartDataKind DataKind { get; init; }
    public string? Scale { get; init; }
    public AdvancedChartAxisRole Axis { get; init; }
    public AdvancedChartSortDirection Sort { get; init; }
    public string? Format { get; init; }
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
}

public sealed record AdvancedChartCoordinate : AstNode
{
    public required AdvancedChartCoordinateKind Kind { get; init; }
    public decimal? StartAngle { get; init; }
    public decimal? EndAngle { get; init; }
    public decimal? InnerRadius { get; init; }
}

public sealed record AdvancedChartFacet : AstNode
{
    public string? RowField { get; init; }
    public string? ColumnField { get; init; }
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
    public ImmutableArray<AdvancedChartLayer> Layers { get; init; } = [];
    public AdvancedChartFacet? Facet { get; init; }
    public AdvancedChartResolution Resolution { get; init; } = new();
    public override string ToSql() => Formatting.AstSerializer.Format(this);
}
