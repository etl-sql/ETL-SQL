using System;
using ETL_SQL.Core;

namespace ETL_SQL.Reporting.Semantics.Runtime;

/// <summary>
/// Explicit mappings between the Core advanced-chart AST enums and the renderer-neutral contract enums.
/// </summary>
/// <remarks>
/// The two enum families are deliberately separate: the AST belongs to Core (tier 0) and the chart
/// contract belongs to <c>Reporting.Contracts</c> (tier 1), so neither can own the other's members.
/// Every bridge here is an explicit arm-per-member switch instead of <c>Enum.Parse(value.ToString())</c>,
/// which turned a renamed or added member into a runtime <c>ArgumentException</c> inside a rendered
/// report. <c>AdvancedChartEnumBridgeParityTests</c> asserts the families stay member-for-member aligned.
/// </remarks>
internal static class AdvancedChartEnumBridge
{
    internal static MarkKind Mark(AdvancedChartMarkKind value) => value switch
    {
        AdvancedChartMarkKind.Rect => MarkKind.Rect,
        AdvancedChartMarkKind.Line => MarkKind.Line,
        AdvancedChartMarkKind.Area => MarkKind.Area,
        AdvancedChartMarkKind.Point => MarkKind.Point,
        AdvancedChartMarkKind.Rule => MarkKind.Rule,
        AdvancedChartMarkKind.Arc => MarkKind.Arc,
        AdvancedChartMarkKind.Text => MarkKind.Text,
        AdvancedChartMarkKind.Tick => MarkKind.Tick,
        _ => throw Unmapped(value)
    };

    internal static AdvancedChartMarkKind Mark(MarkKind value) => value switch
    {
        MarkKind.Rect => AdvancedChartMarkKind.Rect,
        MarkKind.Line => AdvancedChartMarkKind.Line,
        MarkKind.Area => AdvancedChartMarkKind.Area,
        MarkKind.Point => AdvancedChartMarkKind.Point,
        MarkKind.Rule => AdvancedChartMarkKind.Rule,
        MarkKind.Arc => AdvancedChartMarkKind.Arc,
        MarkKind.Text => AdvancedChartMarkKind.Text,
        MarkKind.Tick => AdvancedChartMarkKind.Tick,
        _ => throw Unmapped(value)
    };

    internal static FieldChannel Channel(AdvancedChartChannel value) => value switch
    {
        AdvancedChartChannel.X => FieldChannel.X,
        AdvancedChartChannel.X2 => FieldChannel.X2,
        AdvancedChartChannel.XStart => FieldChannel.XStart,
        AdvancedChartChannel.XEnd => FieldChannel.XEnd,
        AdvancedChartChannel.XOffset => FieldChannel.XOffset,
        AdvancedChartChannel.Y => FieldChannel.Y,
        AdvancedChartChannel.Y2 => FieldChannel.Y2,
        AdvancedChartChannel.YStart => FieldChannel.YStart,
        AdvancedChartChannel.YEnd => FieldChannel.YEnd,
        AdvancedChartChannel.YOffset => FieldChannel.YOffset,
        AdvancedChartChannel.Color => FieldChannel.Color,
        AdvancedChartChannel.Size => FieldChannel.Size,
        AdvancedChartChannel.Shape => FieldChannel.Shape,
        AdvancedChartChannel.Theta => FieldChannel.Theta,
        AdvancedChartChannel.Radius => FieldChannel.Radius,
        AdvancedChartChannel.Text => FieldChannel.Text,
        AdvancedChartChannel.Tooltip => FieldChannel.Tooltip,
        AdvancedChartChannel.Detail => FieldChannel.Detail,
        _ => throw Unmapped(value)
    };

    /// <summary>
    /// The reverse channel bridge. <see cref="FieldChannel"/> is the wider family — it also carries the
    /// statistical/financial channels used by named presets and the facet channels — so members with no
    /// <c>CUSTOM</c> grammar counterpart map to null rather than throwing.
    /// </summary>
    internal static AdvancedChartChannel? Channel(FieldChannel value) => value switch
    {
        FieldChannel.X => AdvancedChartChannel.X,
        FieldChannel.X2 => AdvancedChartChannel.X2,
        FieldChannel.XStart => AdvancedChartChannel.XStart,
        FieldChannel.XEnd => AdvancedChartChannel.XEnd,
        FieldChannel.XOffset => AdvancedChartChannel.XOffset,
        FieldChannel.Y => AdvancedChartChannel.Y,
        FieldChannel.Y2 => AdvancedChartChannel.Y2,
        FieldChannel.YStart => AdvancedChartChannel.YStart,
        FieldChannel.YEnd => AdvancedChartChannel.YEnd,
        FieldChannel.YOffset => AdvancedChartChannel.YOffset,
        FieldChannel.Color => AdvancedChartChannel.Color,
        FieldChannel.Size => AdvancedChartChannel.Size,
        FieldChannel.Shape => AdvancedChartChannel.Shape,
        FieldChannel.Theta => AdvancedChartChannel.Theta,
        FieldChannel.Radius => AdvancedChartChannel.Radius,
        FieldChannel.Text => AdvancedChartChannel.Text,
        FieldChannel.Tooltip => AdvancedChartChannel.Tooltip,
        FieldChannel.Detail => AdvancedChartChannel.Detail,
        FieldChannel.Low or FieldChannel.Q1 or FieldChannel.Median or FieldChannel.Q3 or FieldChannel.High or
            FieldChannel.Open or FieldChannel.Close or FieldChannel.Row or FieldChannel.Column or FieldChannel.Wrap => null,
        _ => throw Unmapped(value)
    };

    internal static DataSemanticKind DataKind(AdvancedChartDataKind value) => value switch
    {
        AdvancedChartDataKind.Quantitative => DataSemanticKind.Quantitative,
        AdvancedChartDataKind.Temporal => DataSemanticKind.Temporal,
        AdvancedChartDataKind.Nominal => DataSemanticKind.Nominal,
        AdvancedChartDataKind.Ordinal => DataSemanticKind.Ordinal,
        _ => throw Unmapped(value)
    };

    internal static AdvancedChartDataKind DataKind(DataSemanticKind value) => value switch
    {
        DataSemanticKind.Quantitative => AdvancedChartDataKind.Quantitative,
        DataSemanticKind.Temporal => AdvancedChartDataKind.Temporal,
        DataSemanticKind.Nominal => AdvancedChartDataKind.Nominal,
        DataSemanticKind.Ordinal => AdvancedChartDataKind.Ordinal,
        _ => throw Unmapped(value)
    };

    internal static ScaleKind Scale(AdvancedChartScaleKind value) => value switch
    {
        AdvancedChartScaleKind.Linear => ScaleKind.Linear,
        AdvancedChartScaleKind.Logarithmic => ScaleKind.Logarithmic,
        AdvancedChartScaleKind.Time => ScaleKind.Time,
        AdvancedChartScaleKind.Band => ScaleKind.Band,
        AdvancedChartScaleKind.Point => ScaleKind.Point,
        AdvancedChartScaleKind.Ordinal => ScaleKind.Ordinal,
        AdvancedChartScaleKind.Identity => ScaleKind.Identity,
        _ => throw Unmapped(value)
    };

    internal static CoordinateKind Coordinate(AdvancedChartCoordinateKind value) => value switch
    {
        AdvancedChartCoordinateKind.Cartesian => CoordinateKind.Cartesian,
        AdvancedChartCoordinateKind.TransposedCartesian => CoordinateKind.TransposedCartesian,
        AdvancedChartCoordinateKind.Polar => CoordinateKind.Polar,
        _ => throw Unmapped(value)
    };

    internal static ScaleResolutionMode Resolution(AdvancedChartResolutionMode value) => value switch
    {
        AdvancedChartResolutionMode.Shared => ScaleResolutionMode.Shared,
        AdvancedChartResolutionMode.Independent => ScaleResolutionMode.Independent,
        _ => throw Unmapped(value)
    };

    internal static AxisRole Axis(AdvancedChartAxisRole value) => value switch
    {
        AdvancedChartAxisRole.None => AxisRole.None,
        AdvancedChartAxisRole.Primary => AxisRole.Primary,
        AdvancedChartAxisRole.Secondary => AxisRole.Secondary,
        _ => throw Unmapped(value)
    };

    /// <summary>
    /// Sort direction is the one family that is not name-aligned: the grammar's <c>SOURCE</c> means
    /// "leave the source order alone", which the contract expresses as <c>None</c>.
    /// </summary>
    internal static SortDirection Sort(AdvancedChartSortDirection value) => value switch
    {
        AdvancedChartSortDirection.Source => SortDirection.None,
        AdvancedChartSortDirection.Ascending => SortDirection.Ascending,
        AdvancedChartSortDirection.Descending => SortDirection.Descending,
        _ => throw Unmapped(value)
    };

    internal static StackMode Stack(AdvancedChartStackMode value) => value switch
    {
        AdvancedChartStackMode.None => StackMode.None,
        AdvancedChartStackMode.Zero => StackMode.Zero,
        AdvancedChartStackMode.Normalize => StackMode.Normalize,
        _ => throw Unmapped(value)
    };

    internal static TickOrientation Tick(AdvancedChartTickOrientation value) => value switch
    {
        AdvancedChartTickOrientation.Auto => TickOrientation.Auto,
        AdvancedChartTickOrientation.Horizontal => TickOrientation.Horizontal,
        AdvancedChartTickOrientation.Vertical => TickOrientation.Vertical,
        _ => throw Unmapped(value)
    };

    internal static PositionAdjustmentKind Position(AdvancedChartPositionKind value) => value switch
    {
        AdvancedChartPositionKind.Identity => PositionAdjustmentKind.Identity,
        AdvancedChartPositionKind.Jitter => PositionAdjustmentKind.Jitter,
        AdvancedChartPositionKind.Nudge => PositionAdjustmentKind.Nudge,
        _ => throw Unmapped(value)
    };

    internal static PositionAdjustmentUnit Unit(AdvancedChartPositionUnit value) => value switch
    {
        AdvancedChartPositionUnit.Data => PositionAdjustmentUnit.Data,
        AdvancedChartPositionUnit.Band => PositionAdjustmentUnit.Band,
        AdvancedChartPositionUnit.Em => PositionAdjustmentUnit.Em,
        _ => throw Unmapped(value)
    };

    internal static ConditionalEncodingChannel Condition(AdvancedChartConditionChannel value) => value switch
    {
        AdvancedChartConditionChannel.Color => ConditionalEncodingChannel.Color,
        AdvancedChartConditionChannel.Opacity => ConditionalEncodingChannel.Opacity,
        AdvancedChartConditionChannel.Size => ConditionalEncodingChannel.Size,
        AdvancedChartConditionChannel.Shape => ConditionalEncodingChannel.Shape,
        AdvancedChartConditionChannel.Text => ConditionalEncodingChannel.Text,
        _ => throw Unmapped(value)
    };

    internal static ColorRangeKind ColorRange(AdvancedChartColorRangeKind value) => value switch
    {
        AdvancedChartColorRangeKind.Gradient => ColorRangeKind.Gradient,
        AdvancedChartColorRangeKind.Diverging => ColorRangeKind.Diverging,
        _ => throw Unmapped(value)
    };

    private static InvalidOperationException Unmapped<T>(T value) where T : struct, Enum =>
        new($"Advanced chart bridge has no mapping for {typeof(T).Name}.{value}.");
}
