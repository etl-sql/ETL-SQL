using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using ETL_SQL.Core;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Reporting.Semantics.Runtime;

namespace ETL_SQL.Reporting.Renderers;

internal sealed class PlotPlanSvgRenderer
{
    private const decimal Left = 60m;
    private const decimal Right = 20m;
    private const decimal Top = 40m;
    private const decimal Bottom = 60m;

    private sealed record OverlayLabel(decimal EndpointX, decimal EndpointY, string Text, string Color, int ZIndex);
    private sealed record PositionedOverlayLabel(OverlayLabel Label, decimal Y);
    private sealed record ArcLabel(decimal AnchorX, decimal AnchorY, decimal ElbowX, decimal PreferredY, string Text, bool IsRight);
    private sealed record PositionedArcLabel(ArcLabel Label, decimal Y);
    private sealed record SmartLabel(int RowIndex, decimal X, decimal Y, string Text, string Color, int Priority, decimal FontSize = 9m);
    private sealed record LabelBox(decimal Left, decimal Top, decimal Right, decimal Bottom);

    public string Render(PlotPlan plan)
    {
        plan.Validate();
        var width = plan.Bounds.Width;
        var height = plan.Bounds.Height;
        var builder = new StringBuilder();
        builder.AppendLine($"<svg xmlns='http://www.w3.org/2000/svg' width='{N(width)}' height='{N(height)}' viewBox='0 0 {N(width)} {N(height)}' role='img' aria-labelledby='{Esc(plan.SpecId)}-title {Esc(plan.SpecId)}-desc' font-family='sans-serif'>");
        builder.AppendLine($"<title id='{Esc(plan.SpecId)}-title'>{Esc(plan.Title ?? plan.SpecId)}</title>");
        builder.AppendLine($"<desc id='{Esc(plan.SpecId)}-desc'>{Esc(plan.AccessibleSummary)}</desc>");
        if (Style(plan, "MICRO_CHART") is { } micro)
        {
            if (micro.Equals("PROGRESS", StringComparison.OrdinalIgnoreCase)) RenderMicroProgress(builder, plan);
            else RenderMicroSparkline(builder, plan);
            builder.AppendLine("</svg>");
            return builder.ToString();
        }

        builder.AppendLine($"<rect width='{N(width)}' height='{N(height)}' fill='{Esc(SafePaint(Style(plan, "BACKGROUND"), "white"))}'/>");
        builder.AppendLine($"<text x='{N(width / 2)}' y='17' text-anchor='middle' font-size='13' font-weight='bold' fill='#333'>{Esc(plan.Title ?? plan.SpecId)}</text>");

        var standardLayout = plan.Layers.Select(layer => LayerStyle(layer, "layout")).FirstOrDefault(value => value is not null);
        if (standardLayout is not null)
        {
            if (standardLayout.Equals("gauge", StringComparison.OrdinalIgnoreCase)) RenderGauge(builder, plan);
            else if (standardLayout.Equals("funnel", StringComparison.OrdinalIgnoreCase)) RenderFunnel(builder, plan);
            else if (standardLayout.Equals("heatmap", StringComparison.OrdinalIgnoreCase)) RenderHeatMap(builder, plan);
            else if (standardLayout.Equals("boxplot", StringComparison.OrdinalIgnoreCase)) RenderBoxPlot(builder, plan);
            else if (standardLayout.Equals("waterfall", StringComparison.OrdinalIgnoreCase)) RenderWaterfall(builder, plan);
            else if (standardLayout.Equals("candlestick", StringComparison.OrdinalIgnoreCase)) RenderCandlestick(builder, plan);
            else if (standardLayout.Equals("gantt", StringComparison.OrdinalIgnoreCase)) RenderGantt(builder, plan);
            else if (standardLayout.Equals("radar", StringComparison.OrdinalIgnoreCase)) RenderRadar(builder, plan);
            else throw new InvalidOperationException($"Unsupported native standard layout '{standardLayout}'.");
            builder.AppendLine("</svg>");
            return builder.ToString();
        }

        if (plan.Coordinate?.Kind == CoordinateKind.Geographic)
        {
            RenderGeographic(builder, plan);
            builder.AppendLine("</svg>");
            return builder.ToString();
        }

        if (!plan.Facets.IsDefaultOrEmpty && !plan.Layers.Any(layer => layer.Mark == MarkKind.Arc))
        {
            foreach (var facet in plan.Facets)
            {
                var rows = facet.RowIndices.ToHashSet();
                var label = string.Join(" / ", new[] { facet.RowLabel, facet.ColumnLabel }.Where(value => !string.IsNullOrEmpty(value)));
                var panel = plan with
                {
                    Title = label,
                    Bounds = new PlotBounds(0, 0, facet.Bounds.Width, facet.Bounds.Height),
                    CartesianViewport = facet.CartesianViewport is null ? null : facet.CartesianViewport with
                    {
                        X = facet.CartesianViewport.X - facet.Bounds.X,
                        Y = facet.CartesianViewport.Y - facet.Bounds.Y
                    },
                    Scales = facet.Scales,
                    Layers = plan.Layers.Select(layer => layer with { Data = layer.Data.Where(datum => rows.Contains(datum.RowIndex)).ToImmutableArray() }).ToImmutableArray(),
                    Facets = []
                };
                var nested = new PlotPlanSvgRenderer().Render(panel);
                builder.AppendLine($"<g transform='translate({N(facet.Bounds.X)},{N(facet.Bounds.Y)})'>{nested}</g>");
            }
            builder.AppendLine("</svg>");
            return builder.ToString();
        }

        if (plan.Layers.Any(layer => layer.Mark == MarkKind.Arc)) RenderArcs(builder, plan);
        else if (plan.CartesianViewport is { } viewport)
        {
            builder.AppendLine($"<g class='plot-aspect-viewport' transform='translate({N(viewport.X)},{N(viewport.Y)})'>");
            RenderCartesian(builder, plan with
            {
                Bounds = new PlotBounds(0m, 0m, viewport.Width, viewport.Height),
                CartesianViewport = null
            });
            builder.AppendLine("</g>");
        }
        else RenderCartesian(builder, plan);
        builder.AppendLine("</svg>");
        return builder.ToString();
    }

    private const decimal BaseLeftMargin = 60m;
    private const decimal BaseRightMargin = 20m;
    private const decimal MinimumPlotWidth = 100m;

    internal readonly record struct CartesianPlotArea(
        decimal Left,
        decimal Top,
        decimal Width,
        decimal Height)
    {
        public decimal Right => Left + Width;
        public decimal Bottom => Top + Height;
    }

    internal sealed record SeriesLabelPlacement(
        string SeriesKey,
        string FullLabel,
        decimal EndpointX,
        decimal EndpointY,
        string PreferredSide,
        string Color,
        int Order
    );

    private static bool HasRenderablePoints(ResolvedMarkLayer layer, ResolvedScale? xScale)
    {
        if (layer.Data.IsDefaultOrEmpty) return false;
        for (var i = 0; i < layer.Data.Length; i++)
        {
            var d = layer.Data[i];
            if (d.IsGap) continue;
            var v = PlotPlanResolver.Number(Channel(d, FieldChannel.Y) ?? Channel(d, FieldChannel.Y2) ?? ChartValue.Null());
            if (!v.HasValue) continue;
            if (xScale is not null && xScale.Kind is ScaleKind.Linear or ScaleKind.Logarithmic)
            {
                var xv = PlotPlanResolver.Number(Channel(d, FieldChannel.X) ?? ChartValue.Null());
                if (!xv.HasValue) continue;
            }
            return true;
        }
        return false;
    }

    private static void RenderCartesian(StringBuilder builder, PlotPlan plan)
    {
        if (plan.Coordinate?.Kind == CoordinateKind.TransposedCartesian)
        {
            RenderTransposedCartesian(builder, plan);
            return;
        }

        var totalWidth = plan.Bounds.Width;
        var minPlotWidth = totalWidth >= 180m ? MinimumPlotWidth : Math.Max(30m, totalWidth - 40m);

        var hasRightLegend = LegendEnabled(plan) && LegendPosition(plan) == "RIGHT" && plan.Legend.Length > 1;
        var legendWidth = hasRightLegend ? 120m : 0m;
        var hasLeftLegend = LegendEnabled(plan) && LegendPosition(plan) == "LEFT" && plan.Legend.Length > 1;
        var leftLegendWidth = hasLeftLegend ? 120m : 0m;

        var seriesLabelsEnabled = IsEnabled(plan.Style, "SERIES_LABELS");
        var seriesLabelsPos = (Style(plan, "SERIES_LABELS:POSITION") ?? "END").Trim().ToUpperInvariant();
        var isStartPos = seriesLabelsPos == "START";

        var xScale = plan.Scales.FirstOrDefault(scale => scale.Channel == FieldChannel.X);
        var categories = xScale?.Categories ?? ImmutableArray<string>.Empty;
        var yScale = plan.Scales.FirstOrDefault(scale => scale.Channel == FieldChannel.Y);
        var y2Scale = plan.Scales.FirstOrDefault(scale => scale.Channel == FieldChannel.Y2);

        var maxSeriesLabelWidth = 0m;
        if (seriesLabelsEnabled)
        {
            var lineCandidates = plan.Layers
                .Where(layer => layer.Mark == MarkKind.Line && LayerStyle(layer, "overlayType") is null && HasRenderablePoints(layer, xScale))
                .Select(layer =>
                {
                    var series = plan.Series.FirstOrDefault(item => item.Key.Equals(layer.SeriesKey ?? layer.Id, StringComparison.Ordinal));
                    return series?.Label ?? layer.SeriesKey ?? layer.Id;
                })
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .Select(label => Math.Max(30m, label!.Length * 5.6m + 14m))
                .ToList();
            if (lineCandidates.Count > 0)
            {
                maxSeriesLabelWidth = lineCandidates.Max();
            }
        }

        var maxOverlayWidth = plan.Layers
            .Select(layer => LayerStyle(layer, "overlayType") is null ? null : LayerStyle(layer, "label"))
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Select(label => Math.Min(140m, label!.Length * 5.4m + 8m))
            .DefaultIfEmpty(0m)
            .Max();

        var remainingWidth = Math.Max(0m, totalWidth - minPlotWidth - BaseLeftMargin - BaseRightMargin - legendWidth - leftLegendWidth);

        var plotLeft = BaseLeftMargin + leftLegendWidth;
        if (seriesLabelsEnabled && isStartPos && maxSeriesLabelWidth > 0m)
        {
            var startGutter = Math.Min(maxSeriesLabelWidth, remainingWidth);
            plotLeft = BaseLeftMargin + startGutter;
            remainingWidth = Math.Max(0m, remainingWidth - startGutter);
        }

        var desiredRightSide = 0m;
        if (seriesLabelsEnabled && !isStartPos && maxSeriesLabelWidth > 0m)
            desiredRightSide = Math.Max(desiredRightSide, maxSeriesLabelWidth);
        if (maxOverlayWidth > 0m)
            desiredRightSide = Math.Max(desiredRightSide, maxOverlayWidth + 14m);

        var sideLabelsGutter = Math.Min(desiredRightSide, remainingWidth);
        var plotRight = BaseRightMargin + legendWidth + sideLabelsGutter;

        var plotWidth = Math.Max(minPlotWidth, totalWidth - plotLeft - plotRight);
        var plotHeight = plan.Bounds.Height - Top - Bottom;

        CartesianPlotArea area = new(plotLeft, Top, plotWidth, plotHeight);

        var overlayLabels = new List<OverlayLabel>();
        var smartLabels = new List<SmartLabel>();
        var seriesLabelPlacements = new List<SeriesLabelPlacement>();

        if (AxisLineEnabled(plan, "y"))
            builder.AppendLine($"<line class='plot-axis-line' x1='{N(area.Left)}' y1='{N(area.Top)}' x2='{N(area.Left)}' y2='{N(area.Bottom)}' stroke='#bbb'/>");
        if (AxisLineEnabled(plan, "x"))
            builder.AppendLine($"<line class='plot-axis-line' x1='{N(area.Left)}' y1='{N(area.Bottom)}' x2='{N(area.Right)}' y2='{N(area.Bottom)}' stroke='#bbb'/>");

        var rectLayers = plan.Layers.Where(layer => layer.Mark == MarkKind.Rect && LayerStyle(layer, "overlayType") is null).ToList();
        var lineLayers = plan.Layers.Where(layer => layer.Mark == MarkKind.Line &&
            LayerStyle(layer, "overlayType") is null).ToList();
        var showLabels = IsEnabled(plan.Style, "DATA_LABELS");

        if (yScale is not null && IsEnabledByDefault(plan.Style, "GRID_LINES"))
        {
            decimal? previousGridValue = null;
            foreach (var tick in yScale.Ticks)
            {
                var value = PlotPlanResolver.Number(tick.Value) ?? 0m;
                var y = MapY(value, yScale, area.Height);
                RenderGridLine(builder, plan, area.Left, y, area.Right, y);
                if (previousGridValue.HasValue && IsEnabled(plan.Style, "MINOR_GRID_LINES"))
                {
                    var minorY = MapY((previousGridValue.Value + value) / 2m, yScale, area.Height);
                    RenderGridLine(builder, plan, area.Left, minorY, area.Right, minorY, minor: true);
                }
                previousGridValue = value;
            }
        }

        if (yScale is not null && IsEnabled(plan.Style, "ZERO_LINE"))
        {
            var (minimum, maximum) = Domain(yScale);
            if (minimum <= 0m && maximum >= 0m)
            {
                var zeroY = MapY(0m, yScale, area.Height);
                var color = SafePaint(Style(plan, "ZERO_LINE_COLOR"), "#6b7280");
                var width = SafeLineWidth(Style(plan, "ZERO_LINE_WIDTH"), "1.5");
                var dash = DashAttribute(Style(plan, "ZERO_LINE_DASH"));
                builder.AppendLine($"<line class='plot-zero-line' x1='{N(area.Left)}' y1='{N(zeroY)}' x2='{N(area.Right)}' y2='{N(zeroY)}' stroke='{Esc(color)}' stroke-width='{width}'{dash}/>");
            }
        }

        var clipId = $"{plan.SpecId}-plot-clip";
        builder.AppendLine($"<defs><clipPath id='{Esc(clipId)}'><rect x='{N(area.Left)}' y='{N(area.Top)}' width='{N(area.Width)}' height='{N(area.Height)}'/></clipPath></defs>");
        builder.AppendLine($"<g clip-path='url(#{Esc(clipId)})'>");

        var hasPrimaryPoints = plan.Layers.Any(layer => layer.Mark == MarkKind.Point && LayerStyle(layer, "overlayType") is null);
        foreach (var layer in plan.Layers
            .OrderBy(item => LayerStyle(item, "overlayType") == "ReferenceBand" ? -1
                : hasPrimaryPoints && item.Mark == MarkKind.Point && LayerStyle(item, "overlayType") is null ? 1 : 0)
            .ThenBy(item => item.ZIndex))
        {
            var color = SafePaint(LayerStyle(layer, "color"),
                plan.Palette.FirstOrDefault(item => item.SeriesKey == layer.SeriesKey)?.Color ?? "#5470c6");
            var overlayType = LayerStyle(layer, "overlayType");
            if (overlayType is not null)
                builder.AppendLine($"<g class='plot-overlay' data-overlay-type='{Esc(overlayType)}' data-z-index='{layer.ZIndex}'>");
            switch (layer.Mark)
            {
                case MarkKind.Rect:
                    if (overlayType == "ReferenceBand")
                        RenderReferenceBand(builder, layer, area, yScale, overlayLabels);
                    else if (IsBoxPlotLayer(layer))
                        RenderBoxPlotLayer(builder, layer, categories, area, yScale, color);
                    else if (IsCandlestickLayer(layer))
                        RenderCandlestickLayer(builder, layer, categories, area, yScale);
                    else
                        RenderRects(builder, plan, layer, rectLayers, layer.Stack != StackMode.None, categories.Length, area, xScale, yScale, color, showLabels);
                    break;
                case MarkKind.Line:
                    var lineScale = layer.Data.Any(datum => Channel(datum, FieldChannel.Y2) is not null) ? y2Scale ?? yScale : yScale;
                    if (layer.Stack != StackMode.None && overlayType is null)
                        RenderStackedLine(builder, plan, layer, lineLayers, categories.Length, area, xScale, lineScale, color, showLabels, seriesLabelPlacements);
                    else
                        RenderLine(builder, plan, layer, categories.Length, area, xScale, lineScale, color, showLabels, overlayLabels, smartLabels, seriesLabelPlacements);
                    break;
                case MarkKind.Area:
                    RenderArea(builder, layer, categories.Length, area, xScale, yScale, color);
                    break;
                case MarkKind.Point:
                    var pointScale = layer.Data.Any(datum => Channel(datum, FieldChannel.Y2) is not null)
                        ? y2Scale ?? yScale
                        : yScale;
                    RenderPoints(builder, plan, layer, categories.Length, area, xScale, pointScale, color, smartLabels);
                    break;
                case MarkKind.Rule:
                    RenderRule(builder, layer, area, xScale, yScale, overlayLabels);
                    break;
                case MarkKind.Text:
                    RenderText(layer, categories.Length, area, xScale,
                        layer.Data.Any(datum => Channel(datum, FieldChannel.Y2) is not null) ? y2Scale ?? yScale : yScale,
                        color, smartLabels);
                    break;
                case MarkKind.Tick:
                    RenderTicks(builder, plan, layer, categories.Length, area, xScale, yScale, color);
                    break;
            }
            if (overlayType is not null) builder.AppendLine("</g>");
        }
        builder.AppendLine("</g>");
        RenderSmartLabels(builder, plan, smartLabels, area);
        var sideLabelsRight = totalWidth - BaseRightMargin - legendWidth;
        RenderSideLabels(builder, area, overlayLabels, seriesLabelPlacements, sideLabelsGutter, sideLabelsRight);

        if (categories.Length > 0)
            RenderHorizontalCategoryAxisLabels(builder, categories, area, xScale);
        else if (xScale is not null && Continuous(xScale))
        {
            for (var index = 0; index < xScale.Ticks.Length; index++)
            {
                var tick = xScale.Ticks[index];
                var x = MapX(PlotPlanResolver.Number(tick.Value) ?? 0m, xScale, area);
                builder.AppendLine($"<line x1='{N(x)}' y1='{N(area.Bottom)}' x2='{N(x)}' y2='{N(area.Bottom + 4m)}' stroke='#bbb'/>");
                if (!SkipTickLabel(xScale, index))
                {
                    var angle = AxisLabelAngle(xScale.LabelRotation, 0);
                    var y = area.Bottom + 16m;
                    var rotation = angle == 0 ? string.Empty : $" transform='rotate(-{angle} {N(x)} {N(y)})'";
                    builder.AppendLine($"<text class='plot-axis-label' data-axis-index='{index}' x='{N(x)}' y='{N(y)}' text-anchor='{(angle == 0 ? "middle" : "end")}' font-size='9' fill='#666'{rotation}>{Esc(tick.Label)}</text>");
                }
            }
            RenderHorizontalMinorTicks(builder, xScale, area);
        }
        if (yScale is not null)
        {
            for (var index = 0; index < yScale.Ticks.Length; index++)
            {
                var tick = yScale.Ticks[index];
                var y = MapY(PlotPlanResolver.Number(tick.Value) ?? 0m, yScale, area.Height);
                builder.AppendLine($"<line x1='{N(area.Left - 4)}' y1='{N(y)}' x2='{N(area.Left)}' y2='{N(y)}' stroke='#bbb'/>");
                if (!SkipTickLabel(yScale, index))
                    builder.AppendLine($"<text x='{N(area.Left - 6)}' y='{N(y + 4)}' text-anchor='end' font-size='9' fill='#666'>{Esc(tick.Label)}</text>");
            }
            RenderVerticalMinorTicks(builder, yScale, area.Height, area.Left, -4m);
        }
        if (y2Scale is not null)
        {
            for (var index = 0; index < y2Scale.Ticks.Length; index++)
            {
                var tick = y2Scale.Ticks[index];
                var y = MapY(PlotPlanResolver.Number(tick.Value) ?? 0m, y2Scale, area.Height);
                builder.AppendLine($"<line x1='{N(area.Right)}' y1='{N(y)}' x2='{N(area.Right + 4m)}' y2='{N(y)}' stroke='#bbb'/>");
                if (!SkipTickLabel(y2Scale, index))
                    builder.AppendLine($"<text x='{N(area.Right + 6m)}' y='{N(y + 4m)}' font-size='9' fill='#666'>{Esc(tick.Label)}</text>");
            }
            RenderVerticalMinorTicks(builder, y2Scale, area.Height, area.Right, 4m);
        }

        var xTitle = Style(plan, "axis:x:label") ?? Style(plan, "axis:x:title");
        var yTitle = Style(plan, "axis:y:label") ?? Style(plan, "axis:y:title");
        if (!string.IsNullOrWhiteSpace(xTitle))
            builder.AppendLine($"<text x='{N(area.Left + area.Width / 2m)}' y='{N(plan.Bounds.Height - 8m)}' text-anchor='middle' font-size='10' fill='#444'>{Esc(xTitle)}</text>");
        if (!string.IsNullOrWhiteSpace(yTitle))
            builder.AppendLine($"<text x='12' y='{N(Top + area.Height / 2m)}' text-anchor='middle' font-size='10' fill='#444' transform='rotate(-90 12 {N(Top + area.Height / 2m)})'>{Esc(yTitle)}</text>");

        RenderLegend(builder, plan);
    }

    private static void RenderTransposedCartesian(StringBuilder builder, PlotPlan plan)
    {
        var plotWidth = plan.Bounds.Width - Left - Right;
        var plotHeight = plan.Bounds.Height - Top - Bottom;
        var bandScale = plan.Scales.FirstOrDefault(scale => scale.Channel == FieldChannel.X);
        var categories = bandScale?.Categories ?? [];
        var rectLayers = plan.Layers.Where(layer => layer.Mark == MarkKind.Rect && LayerStyle(layer, "overlayType") is null).ToList();
        var stacked = rectLayers.Any(layer => layer.Stack != StackMode.None);
        var (slot, outerOffset) = CategoryLayout(categories.Length, plotHeight, bandScale);
        var showLabels = IsEnabled(plan.Style, "DATA_LABELS");
        var isGrouped = rectLayers.Count > 1 && rectLayers.Any(l => LayerStyle(l, "series") is not null);
        if (AxisLineEnabled(plan, "x"))
            builder.AppendLine($"<line class='plot-axis-line' x1='{N(Left)}' y1='{N(Top)}' x2='{N(Left)}' y2='{N(Top + plotHeight)}' stroke='#bbb'/>");
        if (AxisLineEnabled(plan, "y"))
            builder.AppendLine($"<line class='plot-axis-line' x1='{N(Left)}' y1='{N(Top + plotHeight)}' x2='{N(Left + plotWidth)}' y2='{N(Top + plotHeight)}' stroke='#bbb'/>");
        if (IsEnabledByDefault(plan.Style, "GRID_LINES"))
        {
            var gridScale = plan.Scales.FirstOrDefault(item => item.Channel == FieldChannel.Y);
            if (gridScale is not null)
            {
                decimal? previousGridValue = null;
                foreach (var tick in gridScale.Ticks)
                {
                    var value = PlotPlanResolver.Number(tick.Value) ?? 0m;
                    var x = MapHorizontal(value, gridScale, plotWidth);
                    RenderGridLine(builder, plan, x, Top, x, Top + plotHeight);
                    if (previousGridValue.HasValue && IsEnabled(plan.Style, "MINOR_GRID_LINES"))
                    {
                        var minorX = MapHorizontal((previousGridValue.Value + value) / 2m, gridScale, plotWidth);
                        RenderGridLine(builder, plan, minorX, Top, minorX, Top + plotHeight, minor: true);
                    }
                    previousGridValue = value;
                }
            }
        }
        var transposedValueScale = plan.Scales.FirstOrDefault(item => item.Channel == FieldChannel.Y);
        if (transposedValueScale is not null && IsEnabled(plan.Style, "ZERO_LINE"))
        {
            var (minimum, maximum) = Domain(transposedValueScale);
            if (minimum <= 0m && maximum >= 0m)
            {
                var zeroX = MapHorizontal(0m, transposedValueScale, plotWidth);
                var color = SafePaint(Style(plan, "ZERO_LINE_COLOR"), "#6b7280");
                var width = SafeLineWidth(Style(plan, "ZERO_LINE_WIDTH"), "1.5");
                var dash = DashAttribute(Style(plan, "ZERO_LINE_DASH"));
                builder.AppendLine($"<line class='plot-zero-line' x1='{N(zeroX)}' y1='{N(Top)}' x2='{N(zeroX)}' y2='{N(Top + plotHeight)}' stroke='{Esc(color)}' stroke-width='{width}'{dash}/>");
            }
        }
        var overlayLabels = new List<OverlayLabel>();
        foreach (var layer in plan.Layers
            .OrderBy(layer => LayerStyle(layer, "overlayType") == "ReferenceBand" ? -1 : 0)
            .ThenBy(layer => layer.ZIndex))
        {
            var scale = layer.Data.Any(datum => Channel(datum, FieldChannel.Y2) is not null)
                ? plan.Scales.FirstOrDefault(item => item.Channel == FieldChannel.Y2)
                : plan.Scales.FirstOrDefault(item => item.Channel == FieldChannel.Y);
            if (scale is null) continue;

            var overlayType = LayerStyle(layer, "overlayType");
            if (layer.Mark == MarkKind.Rect && overlayType == "ReferenceBand")
            {
                builder.AppendLine($"<g class='plot-overlay' data-overlay-type='ReferenceBand' data-z-index='{layer.ZIndex}'>");
                RenderTransposedReferenceBand(builder, layer, scale, plotWidth, plotHeight, overlayLabels);
                builder.AppendLine("</g>");
                continue;
            }
            if (layer.Mark == MarkKind.Rule && overlayType is not null)
            {
                builder.AppendLine($"<g class='plot-overlay' data-overlay-type='{Esc(overlayType)}' data-z-index='{layer.ZIndex}'>");
                var ruleColor = SafePaint(LayerStyle(layer, "color") ?? LayerStyle(layer, "fill") ?? LayerStyle(layer, "stroke"), "#888888");
                var strokeWidth = LayerStyle(layer, "stroke_width") ?? LayerStyle(layer, "width") ?? "2";
                var dashAttributes = LineStyleAttributes(LayerStyle(layer, "lineStyle"));
                var ruleClass = overlayType == "ReferenceLine" ? " class='plot-reference-line'" : string.Empty;
                var val = layer.Data.Select(datum => Channel(datum, FieldChannel.Y)).FirstOrDefault(item => item is not null && item.Kind != ChartValueKind.Null);
                var num = val is not null ? PlotPlanResolver.Number(val) : null;
                if (num.HasValue)
                {
                    var x = MapHorizontal(num.Value, scale, plotWidth);
                    builder.AppendLine($"<line{ruleClass} x1='{N(x)}' y1='{N(Top)}' x2='{N(x)}' y2='{N(Top + plotHeight)}' stroke='{Esc(ruleColor)}' stroke-width='{Esc(strokeWidth)}'{dashAttributes}/>");
                    var label = LayerStyle(layer, "label");
                    if (!string.IsNullOrWhiteSpace(label))
                        overlayLabels.Add(new OverlayLabel(x, Top + 15m, label, ruleColor, layer.ZIndex));
                }
                builder.AppendLine("</g>");
                continue;
            }

            var defaultColor = plan.Palette.FirstOrDefault(item => item.SeriesKey == layer.SeriesKey)?.Color ?? "#5470c6";
            var color = SafePaint(LayerStyle(layer, "fill") ?? LayerStyle(layer, "color"), defaultColor);
            var layerOpacity = decimal.TryParse(LayerStyle(layer, "opacity"), NumberStyles.Any, CultureInfo.InvariantCulture, out var o) ? o : 1m;
            var errorBarStyle = LayerStyle(layer, "errorBarStyle") ?? LayerStyle(layer, "ERROR_BAR_STYLE") ?? LayerStyle(layer, "error_bar_style") ?? "CAPS";
            var hasCaps = !errorBarStyle.Equals("NO_CAPS", StringComparison.OrdinalIgnoreCase);
            var points = new List<string>();
            var pointCoordinates = new List<(decimal X, decimal Y)>();
            for (var index = 0; index < layer.Data.Length; index++)
            {
                var datum = layer.Data[index];
                var value = PlotPlanResolver.Number(Channel(datum, FieldChannel.Y) ?? Channel(datum, FieldChannel.Y2) ?? ChartValue.Null());
                // Transposed marks read the value axis horizontally; an author-supplied Y_START/Y_END pair
                // is the whole extent, so such a datum carries no plain Y and must not be discarded here.
                var rangeStart = layer.Mark == MarkKind.Rect && layer.Stack == StackMode.None
                    ? PositionNumber(Channel(datum, FieldChannel.YStart)) : null;
                var rangeEnd = layer.Mark == MarkKind.Rect && layer.Stack == StackMode.None
                    ? PositionNumber(Channel(datum, FieldChannel.YEnd)) : null;
                var rangedY = rangeStart.HasValue && rangeEnd.HasValue;
                value ??= rangedY ? rangeEnd : null;
                if (datum.IsGap || !value.HasValue) continue;
                var x = MapHorizontal(value.Value, scale, plotWidth) + datum.DisplayOffsetX;
                var y = Top + outerOffset + slot * (index + .5m) + datum.DisplayOffsetY;
                var datumColor = EncodingText(datum, ConditionalEncodingChannel.Color) is { } candidate ? SafePaint(candidate, color) : color;
                var datumOpacity = EncodingNumber(datum, ConditionalEncodingChannel.Opacity) ?? layerOpacity;
                if (layer.Mark == MarkKind.Rect)
                {
                    var layerIndex = Math.Max(0, rectLayers.IndexOf(layer));
                    var start = rangedY
                        ? rangeStart!.Value
                        : layer.Stack != StackMode.None
                            ? PlotPlanResolver.Number(Channel(datum, FieldChannel.YStart) ?? ChartValue.From(0m)) ?? 0m
                            : 0m;
                    var end = rangedY
                        ? rangeEnd!.Value
                        : layer.Stack != StackMode.None
                            ? PlotPlanResolver.Number(Channel(datum, FieldChannel.YEnd) ?? ChartValue.From(start + value.Value)) ?? start + value.Value
                            : start + value.Value;
                    var startX = MapHorizontal(start, scale, plotWidth) + datum.DisplayOffsetX;
                    var endX = MapHorizontal(end, scale, plotWidth) + datum.DisplayOffsetX;
                    var groupHeight = slot * layer.BandSize;
                    decimal barHeight;
                    decimal top;
                    if (stacked)
                    {
                        barHeight = groupHeight;
                        top = y - groupHeight / 2m;
                    }
                    else if (isGrouped)
                    {
                        var seriesGap = UnitStyle(plan, "SERIES_GAP");
                        var gapHeight = seriesGap.HasValue
                            ? groupHeight * seriesGap.Value / (rectLayers.Count + seriesGap.Value * (rectLayers.Count - 1))
                            : 0m;
                        barHeight = seriesGap.HasValue
                            ? (groupHeight - gapHeight * (rectLayers.Count - 1)) / Math.Max(1, rectLayers.Count)
                            : groupHeight / Math.Max(1, rectLayers.Count);
                        top = y - groupHeight / 2m + layerIndex * (barHeight + gapHeight);
                    }
                    else if (rectLayers.Count > 1 && layer.BandSize < .75m)
                    {
                        barHeight = groupHeight * 0.42m;
                        top = y - barHeight / 2m;
                    }
                    else
                    {
                        barHeight = groupHeight;
                        top = y - groupHeight / 2m;
                    }
                    var drawHeight = Math.Max(1m, barHeight - (UnitStyle(plan, "SERIES_GAP").HasValue ? 0m : 1m));
                    // Transposing swaps the axes, so an X_START/X_END span becomes the vertical extent and
                    // replaces the category band this datum would otherwise occupy.
                    var spanStart = layer.Stack == StackMode.None ? PositionNumber(Channel(datum, FieldChannel.XStart)) : null;
                    var spanEnd = layer.Stack == StackMode.None ? PositionNumber(Channel(datum, FieldChannel.XEnd)) : null;
                    var rangedBand = spanStart.HasValue && spanEnd.HasValue && bandScale is not null && Continuous(bandScale);
                    if (rangedBand)
                    {
                        var firstY = MapVertical(spanStart!.Value, bandScale!, plotHeight) + datum.DisplayOffsetY;
                        var secondY = MapVertical(spanEnd!.Value, bandScale!, plotHeight) + datum.DisplayOffsetY;
                        top = Math.Min(firstY, secondY);
                        drawHeight = Math.Max(1m, Math.Abs(secondY - firstY));
                    }
                    var barLeft = Math.Min(startX, endX);
                    var barWidth = Math.Max(1m, Math.Abs(endX - startX));
                    var rectLabel = FormatDataLabel(rangedY ? end : value.Value, DataFormat(plan));
                    var rectTitle = rangedY
                        ? $"{FormatDataLabel(start, DataFormat(plan))} to {FormatDataLabel(end, DataFormat(plan))}"
                        : rectLabel;
                    var rangedClass = rangedY || rangedBand ? " class='plot-range-rect'" : string.Empty;
                    var extent = rangedY || rangedBand ? string.Empty : ExtentAttributes(layer);
                    builder.AppendLine($"<rect{rangedClass} x='{N(barLeft)}' y='{N(top)}' width='{N(barWidth)}' height='{N(drawHeight)}' fill='{Esc(datumColor)}' fill-opacity='{N(Math.Clamp(datumOpacity, 0m, 1m))}' data-row-index='{datum.RowIndex}'{extent}><title>{Esc(rectTitle)}</title></rect>");
                    var errorLow = PlotPlanResolver.Number(Channel(datum, FieldChannel.ErrorLow) ?? ChartValue.Null());
                    var errorHigh = PlotPlanResolver.Number(Channel(datum, FieldChannel.ErrorHigh) ?? ChartValue.Null());
                    if (errorLow.HasValue && errorHigh.HasValue)
                    {
                        var lowX = MapHorizontal(errorLow.Value, scale, plotWidth) + datum.DisplayOffsetX;
                        var highX = MapHorizontal(errorHigh.Value, scale, plotWidth) + datum.DisplayOffsetX;
                        var capHeight = Math.Max(2m, Math.Min(drawHeight * 0.3m, 6m));
                        builder.AppendLine($"<g class='plot-error-bar' data-row-index='{datum.RowIndex}'>");
                        builder.AppendLine($"<line class='plot-error-bar-stem' x1='{N(lowX)}' y1='{N(y)}' x2='{N(highX)}' y2='{N(y)}' stroke='{Esc(datumColor)}' stroke-width='1.5'/>");
                        if (hasCaps)
                        {
                            builder.AppendLine($"<line class='plot-error-bar-cap' x1='{N(lowX)}' y1='{N(y - capHeight)}' x2='{N(lowX)}' y2='{N(y + capHeight)}' stroke='{Esc(datumColor)}' stroke-width='1.5'/>");
                            builder.AppendLine($"<line class='plot-error-bar-cap' x1='{N(highX)}' y1='{N(y - capHeight)}' x2='{N(highX)}' y2='{N(y + capHeight)}' stroke='{Esc(datumColor)}' stroke-width='1.5'/>");
                        }
                        builder.AppendLine("</g>");
                    }
                    if (showLabels)
                    {
                        var position = Style(plan, "DATA_LABELS:POSITION") ?? "OUTSIDE_RIGHT";
                        var positive = end >= start;
                        var labelX = endX + (positive ? 4m : -4m);
                        var anchor = positive ? "start" : "end";
                        var labelColor = SafePaint(Style(plan, "DATA_LABELS:COLOR"), "#333");
                        if (position.Contains("INSIDE", StringComparison.OrdinalIgnoreCase))
                        {
                            var ratio = position.Contains("MIDDLE", StringComparison.OrdinalIgnoreCase) ? .5m
                                : position.Contains("LEFT", StringComparison.OrdinalIgnoreCase) || position.Contains("BOTTOM", StringComparison.OrdinalIgnoreCase) ? .12m
                                : .88m;
                            labelX = startX + (endX - startX) * ratio;
                            anchor = "middle";
                            labelColor = SafePaint(Style(plan, "DATA_LABELS:COLOR"), "white");
                        }
                        RenderDataLabelBackground(builder, plan, datum.RowIndex, labelX, y + 3m, anchor, FontSize(Style(plan, "DATA_LABELS:FONT_SIZE")), rectLabel);
                        builder.AppendLine($"<text x='{N(labelX)}' y='{N(y + 3m)}' text-anchor='{anchor}' font-size='{Esc(Style(plan, "DATA_LABELS:FONT_SIZE") ?? "9")}' fill='{Esc(labelColor)}'>{Esc(rectLabel)}</text>");
                    }
                }
                else if (layer.Mark == MarkKind.Rule)
                {
                    var ruleHeight = slot * .75m;
                    var y1 = y - ruleHeight / 2m;
                    var y2 = y + ruleHeight / 2m;
                    var strokeWidth = LayerStyle(layer, "stroke_width") ?? LayerStyle(layer, "width") ?? "3";
                    builder.AppendLine($"<line x1='{N(x)}' y1='{N(y1)}' x2='{N(x)}' y2='{N(y2)}' stroke='{Esc(datumColor)}' stroke-width='{Esc(strokeWidth)}'/>");
                }
                else if (layer.Mark == MarkKind.Text)
                {
                    var text = EncodingText(datum, ConditionalEncodingChannel.Text)
                        ?? (Channel(datum, FieldChannel.Text) is { } textValue ? PlotPlanResolver.Display(textValue) : null);
                    if (!string.IsNullOrEmpty(text))
                        builder.AppendLine($"<text x='{N(x)}' y='{N(y - 5m)}' text-anchor='middle' font-size='10' fill='{Esc(datumColor)}'>{Esc(text)}</text>");
                }
                else if (layer.Mark == MarkKind.Point)
                {
                    var errorLow = PlotPlanResolver.Number(Channel(datum, FieldChannel.ErrorLow) ?? ChartValue.Null());
                    var errorHigh = PlotPlanResolver.Number(Channel(datum, FieldChannel.ErrorHigh) ?? ChartValue.Null());
                    if (errorLow.HasValue && errorHigh.HasValue)
                    {
                        var lowX = MapHorizontal(errorLow.Value, scale, plotWidth) + datum.DisplayOffsetX;
                        var highX = MapHorizontal(errorHigh.Value, scale, plotWidth) + datum.DisplayOffsetX;
                        const decimal capHeight = 4m;
                        builder.AppendLine($"<g class='plot-error-bar' data-row-index='{datum.RowIndex}'>");
                        builder.AppendLine($"<line class='plot-error-bar-stem' x1='{N(lowX)}' y1='{N(y)}' x2='{N(highX)}' y2='{N(y)}' stroke='{Esc(datumColor)}' stroke-width='1.5'/>");
                        if (hasCaps)
                        {
                            builder.AppendLine($"<line class='plot-error-bar-cap' x1='{N(lowX)}' y1='{N(y - capHeight)}' x2='{N(lowX)}' y2='{N(y + capHeight)}' stroke='{Esc(datumColor)}' stroke-width='1.5'/>");
                            builder.AppendLine($"<line class='plot-error-bar-cap' x1='{N(highX)}' y1='{N(y - capHeight)}' x2='{N(highX)}' y2='{N(y + capHeight)}' stroke='{Esc(datumColor)}' stroke-width='1.5'/>");
                        }
                        builder.AppendLine("</g>");
                    }
                    RenderPointSymbol(builder, PointShape(plan, layer, datum), x, y, 3m,
                        datumColor, "plot-point", datum.RowIndex, null, PointStrokeAttributes(layer));
                }
                if (layer.Mark == MarkKind.Line && IsEnabledByDefault(plan.Style, "SYMBOLS"))
                    RenderPointSymbol(builder, PointShape(plan, layer, datum), x, y, 3m,
                        datumColor, "plot-line-symbol", datum.RowIndex, FormatDataLabel(value.Value, DataFormat(plan)),
                        PointStrokeAttributes(layer));
                if (layer.Mark is MarkKind.Line or MarkKind.Area)
                {
                    points.Add($"{N(x)} {N(y)}");
                    pointCoordinates.Add((x, y));
                }
            }
            if (layer.Mark == MarkKind.Line && points.Count > 1)
                builder.AppendLine($"<path d='M {string.Join(" L ", points)}' fill='none' stroke='{Esc(color)}' stroke-width='{LineWidth(layer, "2")}'/>");
            else if (layer.Mark == MarkKind.Area)
            {
                var isConfidence = layer.Data.Any(datum => Channel(datum, FieldChannel.ConfidenceLow) is not null || Channel(datum, FieldChannel.ConfidenceHigh) is not null);
                if (isConfidence)
                {
                    var ribbonSegments = new List<(List<(decimal X, decimal Y)> Upper, List<(decimal X, decimal Y)> Lower)>
                        { (new(), new()) };
                    for (var i = 0; i < layer.Data.Length; i++)
                    {
                        var datum = layer.Data[i];
                        var lowVal = PositionNumber(Channel(datum, FieldChannel.ConfidenceLow));
                        var highVal = PositionNumber(Channel(datum, FieldChannel.ConfidenceHigh));
                        if (datum.IsGap || !lowVal.HasValue || !highVal.HasValue)
                        {
                            if (ribbonSegments[^1].Upper.Count > 0) ribbonSegments.Add((new(), new()));
                            continue;
                        }
                        var y = Top + outerOffset + slot * (i + .5m) + datum.DisplayOffsetY;
                        ribbonSegments[^1].Upper.Add((MapHorizontal(highVal.Value, scale, plotWidth), y));
                        ribbonSegments[^1].Lower.Add((MapHorizontal(lowVal.Value, scale, plotWidth), y));
                    }
                    foreach (var segment in ribbonSegments.Where(segment => segment.Upper.Count > 1))
                    {
                        var path = $"M {string.Join(" L ", segment.Upper.Select(p => $"{N(p.X)} {N(p.Y)}"))} " +
                            $"L {string.Join(" L ", segment.Lower.AsEnumerable().Reverse().Select(p => $"{N(p.X)} {N(p.Y)}"))} Z";
                        builder.AppendLine($"<path class='plot-confidence-band' d='{path}' fill='{Esc(color)}' fill-opacity='.2' stroke='{Esc(color)}' stroke-width='1'/>");
                    }
                }
                else if (points.Count > 1)
                {
                    var baseline = MapHorizontal(0m, scale, plotWidth);
                    builder.AppendLine($"<path d='M {N(baseline)} {N(pointCoordinates[0].Y)} L {string.Join(" L ", points)} L {N(baseline)} {N(pointCoordinates[^1].Y)} Z' fill='{Esc(color)}' fill-opacity='0.25' stroke='{Esc(color)}' stroke-width='2'/>");
                }
            }
        }
        RenderVerticalCategoryAxisLabels(builder, categories, plotHeight, bandScale);
        var xTitle = Style(plan, "axis:x:label");
        var yTitle = Style(plan, "axis:y:label");
        if (!string.IsNullOrWhiteSpace(yTitle))
            builder.AppendLine($"<text x='{N(Left + plotWidth / 2m)}' y='{N(plan.Bounds.Height - 8m)}' text-anchor='middle' font-size='10' fill='#444'>{Esc(yTitle)}</text>");
        if (!string.IsNullOrWhiteSpace(xTitle))
            builder.AppendLine($"<text x='12' y='{N(Top + plotHeight / 2m)}' text-anchor='middle' font-size='10' fill='#444' transform='rotate(-90 12 {N(Top + plotHeight / 2m)})'>{Esc(xTitle)}</text>");
        RenderOverlayLabels(builder, overlayLabels, plotHeight);
        RenderLegend(builder, plan);
    }

    private static bool LegendEnabled(PlotPlan plan)
    {
        var value = Style(plan, "LEGEND");
        return value is null || (!value.Equals("OFF", StringComparison.OrdinalIgnoreCase) &&
            !value.Equals("FALSE", StringComparison.OrdinalIgnoreCase) && value != "0");
    }

    private static string LegendPosition(PlotPlan plan)
    {
        var position = Style(plan, "LEGEND_POSITION");
        if (!string.IsNullOrWhiteSpace(position)) return position.ToUpperInvariant();
        position = Style(plan, "LEGEND");
        return position is "TOP" or "BOTTOM" or "LEFT" or "RIGHT" or "INSIDE"
            ? position.ToUpperInvariant()
            : "BOTTOM";
    }

    private static string LegendAnchor(PlotPlan plan)
    {
        var anchor = Style(plan, "LEGEND_ANCHOR");
        return anchor?.ToUpperInvariant() switch
        {
            "TOP_LEFT" => "TOP_LEFT",
            "BOTTOM_LEFT" => "BOTTOM_LEFT",
            "BOTTOM_RIGHT" => "BOTTOM_RIGHT",
            _ => "TOP_RIGHT"
        };
    }

    private static bool LegendIsVertical(PlotPlan plan, string position)
    {
        var orientation = Style(plan, "LEGEND_ORIENTATION");
        if (!string.IsNullOrWhiteSpace(orientation))
        {
            if (orientation.Equals("VERTICAL", StringComparison.OrdinalIgnoreCase)) return true;
            if (orientation.Equals("HORIZONTAL", StringComparison.OrdinalIgnoreCase)) return false;
        }
        return position is "LEFT" or "RIGHT" or "INSIDE";
    }

    private static bool LegendIsReverse(PlotPlan plan)
    {
        var reverse = Style(plan, "LEGEND_REVERSE");
        return reverse is not null && (reverse.Equals("ON", StringComparison.OrdinalIgnoreCase) ||
            reverse.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || reverse == "1");
    }

    private static int? LegendColumns(PlotPlan plan)
    {
        var colStr = Style(plan, "LEGEND_COLUMNS");
        if (int.TryParse(colStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cols) && cols > 0)
            return cols;
        return null;
    }

    private static string? LegendTitle(PlotPlan plan)
    {
        var title = Style(plan, "LEGEND_TITLE");
        if (string.IsNullOrWhiteSpace(title) || title.Equals("NONE", StringComparison.OrdinalIgnoreCase)) return null;
        return title;
    }

    private static void RenderLegend(StringBuilder builder, PlotPlan plan)
    {
        if (!LegendEnabled(plan)) return;
        var continuous = plan.Scales.FirstOrDefault(scale => scale.ColorRange is not null);
        if (continuous?.ColorRange is { } range)
        {
            var gradientId = $"{plan.SpecId}-continuous-color";
            var colorbarX = plan.Bounds.Width - 140m;
            var colorbarY = 28m;
            builder.AppendLine($"<defs><linearGradient id='{Esc(gradientId)}'><stop offset='0%' stop-color='{Esc(range.Low)}'/>{(range.Mid is null ? string.Empty : $"<stop offset='{N(ColorMidOffset(continuous, range) * 100m)}%' stop-color='{Esc(range.Mid)}'/>")}<stop offset='100%' stop-color='{Esc(range.High)}'/></linearGradient></defs>");
            builder.AppendLine($"<rect class='plot-colorbar' x='{N(colorbarX)}' y='{N(colorbarY)}' width='110' height='9' fill='url(#{Esc(gradientId)})'><title>{Esc(range.AccessibleDescription)}</title></rect>");
            builder.AppendLine($"<text x='{N(colorbarX)}' y='{N(colorbarY + 19m)}' font-size='8' fill='#555'>{Esc(continuous.Ticks[0].Label)}</text>");
            builder.AppendLine($"<text x='{N(colorbarX + 110m)}' y='{N(colorbarY + 19m)}' text-anchor='end' font-size='8' fill='#555'>{Esc(continuous.Ticks[^1].Label)}</text>");
            return;
        }

        if (plan.Legend.Length == 0) return;
        var explicitOn = Style(plan, "LEGEND") is "ON" or "TRUE" or "1";
        var legendTitle = LegendTitle(plan);
        if (plan.Legend.Length <= 1 && !explicitOn && legendTitle is null) return;

        var entries = LegendIsReverse(plan)
            ? plan.Legend.Reverse().ToArray()
            : plan.Legend.ToArray();

        var fontSizeStr = Style(plan, "LEGEND_FONT_SIZE") ?? "9";
        var fontColor = Style(plan, "LEGEND_FONT_COLOR") ?? "#444";
        var fontWeight = Style(plan, "LEGEND_FONT_WEIGHT");
        var weightAttr = !string.IsNullOrWhiteSpace(fontWeight) ? $" font-weight='{Esc(fontWeight)}'" : "";

        decimal fontSizeVal = 9m;
        var trimmedSize = fontSizeStr.Trim().TrimEnd('p', 'x', 'P', 'X', 't', 'T');
        if (decimal.TryParse(trimmedSize, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedSize) && parsedSize > 0)
            fontSizeVal = parsedSize;

        var rowHeight = Math.Max(16m, fontSizeVal + 7m);
        var position = LegendPosition(plan);
        var isInside = position == "INSIDE";
        var anchor = isInside ? LegendAnchor(plan) : "TOP_RIGHT";
        var isVertical = LegendIsVertical(plan, position);
        var columns = LegendColumns(plan);

        if (isInside)
        {
            RenderInsideLegend(builder, plan, entries, legendTitle, fontSizeStr, fontSizeVal, fontColor, fontWeight, weightAttr, rowHeight, anchor, isVertical, columns);
            return;
        }

        if (isVertical)
        {
            RenderVerticalLegend(builder, plan, entries, legendTitle, fontSizeStr, fontSizeVal, fontColor, fontWeight, weightAttr, rowHeight, position, columns);
            return;
        }

        RenderHorizontalLegend(builder, plan, entries, legendTitle, fontSizeStr, fontSizeVal, fontColor, fontWeight, weightAttr, rowHeight, position, columns);
    }

    private static void RenderInsideLegend(
        StringBuilder builder,
        PlotPlan plan,
        LegendEntry[] entries,
        string? legendTitle,
        string fontSizeStr,
        decimal fontSizeVal,
        string fontColor,
        string? fontWeight,
        string weightAttr,
        decimal rowHeight,
        string anchor,
        bool isVertical,
        int? columns)
    {
        var titleWeight = !string.IsNullOrWhiteSpace(fontWeight) ? $" font-weight='{Esc(fontWeight)}'" : " font-weight='bold'";
        int cols = columns ?? (isVertical ? 1 : Math.Min(entries.Length, 4));
        int rows = (int)Math.Ceiling((double)entries.Length / cols);

        decimal maxLabelWidth = entries.Length > 0
            ? entries.Max(e => Math.Max(50m, e.Label.Length * (fontSizeVal * 0.65m) + 25m))
            : 65m;
        if (legendTitle is not null)
            maxLabelWidth = Math.Max(maxLabelWidth, legendTitle.Length * (fontSizeVal * 0.7m) + 12m);

        decimal boxWidth = cols * maxLabelWidth + 16m;
        decimal titleHeight = legendTitle is not null ? rowHeight : 0m;
        decimal boxHeight = rows * rowHeight + titleHeight + 12m;

        decimal plotAreaLeft = Left;
        decimal plotAreaTop = Top;
        decimal plotAreaRight = plan.Bounds.Width - Right;
        decimal plotAreaBottom = plan.Bounds.Height - Bottom;

        decimal boxX = anchor switch
        {
            "TOP_LEFT" or "BOTTOM_LEFT" => plotAreaLeft + 8m,
            _ => plotAreaRight - boxWidth - 8m
        };

        decimal boxY = anchor switch
        {
            "BOTTOM_LEFT" or "BOTTOM_RIGHT" => plotAreaBottom - boxHeight - 8m,
            _ => plotAreaTop + 8m
        };

        builder.AppendLine("<g class='plot-legend plot-legend-inside'>");
        builder.AppendLine($"<rect class='plot-legend-bg' x='{N(boxX)}' y='{N(boxY)}' width='{N(boxWidth)}' height='{N(boxHeight)}' fill='white' fill-opacity='0.88' stroke='#e5e7eb' stroke-width='1' rx='4'/>");

        decimal contentX = boxX + 8m;
        decimal currentY = boxY + 6m + fontSizeVal;

        if (legendTitle is not null)
        {
            builder.AppendLine($"<text class='plot-legend-title' x='{N(contentX)}' y='{N(currentY)}' font-size='{Esc(fontSizeStr)}' fill='{Esc(fontColor)}'{titleWeight}>{Esc(legendTitle)}</text>");
            currentY += rowHeight;
        }

        for (int i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            int col = i % cols;
            int row = i / cols;
            decimal entryX = contentX + col * maxLabelWidth;
            decimal entryY = currentY + row * rowHeight;

            builder.AppendLine($"<rect x='{N(entryX)}' y='{N(entryY - 8m)}' width='9' height='9' fill='{Esc(SafePaint(entry.Color, "#5470c6"))}'/>");
            builder.AppendLine($"<text x='{N(entryX + 13m)}' y='{N(entryY)}' font-size='{Esc(fontSizeStr)}' fill='{Esc(fontColor)}'{weightAttr}>{Esc(entry.Label)}</text>");
        }

        builder.AppendLine("</g>");
    }

    private static void RenderVerticalLegend(
        StringBuilder builder,
        PlotPlan plan,
        LegendEntry[] entries,
        string? legendTitle,
        string fontSizeStr,
        decimal fontSizeVal,
        string fontColor,
        string? fontWeight,
        string weightAttr,
        decimal rowHeight,
        string position,
        int? columns)
    {
        var titleWeight = !string.IsNullOrWhiteSpace(fontWeight) ? $" font-weight='{Esc(fontWeight)}'" : " font-weight='bold'";
        decimal x = position == "RIGHT" ? plan.Bounds.Width - 105m : (position == "LEFT" ? 8m : Left);
        decimal y = position == "TOP" ? 29m : (position == "BOTTOM" ? plan.Bounds.Height - 12m - (entries.Length - 1) * rowHeight : Top);

        if (legendTitle is not null)
        {
            builder.AppendLine($"<text class='plot-legend-title' x='{N(x)}' y='{N(y)}' font-size='{Esc(fontSizeStr)}' fill='{Esc(fontColor)}'{titleWeight}>{Esc(legendTitle)}</text>");
            y += rowHeight;
        }

        int cols = columns ?? 1;
        decimal colWidth = cols > 1
            ? Math.Max(70m, entries.Max(e => e.Label.Length * (fontSizeVal * 0.65m) + 25m))
            : 0m;

        for (int i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            int col = i % cols;
            int row = i / cols;
            decimal entryX = cols > 1 ? x + col * colWidth : x;
            decimal entryY = y + row * rowHeight;

            builder.AppendLine($"<rect x='{N(entryX)}' y='{N(entryY - 8m)}' width='9' height='9' fill='{Esc(SafePaint(entry.Color, "#5470c6"))}'/>");
            builder.AppendLine($"<text x='{N(entryX + 13m)}' y='{N(entryY)}' font-size='{Esc(fontSizeStr)}' fill='{Esc(fontColor)}'{weightAttr}>{Esc(entry.Label)}</text>");
        }
    }

    private static void RenderHorizontalLegend(
        StringBuilder builder,
        PlotPlan plan,
        LegendEntry[] entries,
        string? legendTitle,
        string fontSizeStr,
        decimal fontSizeVal,
        string fontColor,
        string? fontWeight,
        string weightAttr,
        decimal rowHeight,
        string position,
        int? columns)
    {
        var titleWeight = !string.IsNullOrWhiteSpace(fontWeight) ? $" font-weight='{Esc(fontWeight)}'" : " font-weight='bold'";
        decimal startX = position == "RIGHT" ? plan.Bounds.Width - 105m : (position == "LEFT" ? 8m : Left);
        decimal maxX = position == "RIGHT" ? plan.Bounds.Width - 10m : (position == "LEFT" ? Left : plan.Bounds.Width - Right);
        decimal availableWidth = Math.Max(100m, maxX - startX);

        if (columns.HasValue)
        {
            int cols = columns.Value;
            int totalRows = (int)Math.Ceiling((double)entries.Length / cols);
            decimal colWidth = Math.Max(65m, availableWidth / cols);
            decimal startY = position == "TOP" ? 29m : (position == "BOTTOM" ? plan.Bounds.Height - 12m - (totalRows - 1) * rowHeight : Top);

            if (legendTitle is not null)
            {
                builder.AppendLine($"<text class='plot-legend-title' x='{N(startX)}' y='{N(startY)}' font-size='{Esc(fontSizeStr)}' fill='{Esc(fontColor)}'{titleWeight}>{Esc(legendTitle)}</text>");
                startY += rowHeight;
            }

            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                int col = i % cols;
                int row = i / cols;
                decimal entryX = startX + col * colWidth;
                decimal entryY = startY + row * rowHeight;

                builder.AppendLine($"<rect x='{N(entryX)}' y='{N(entryY - 8m)}' width='9' height='9' fill='{Esc(SafePaint(entry.Color, "#5470c6"))}'/>");
                builder.AppendLine($"<text x='{N(entryX + 13m)}' y='{N(entryY)}' font-size='{Esc(fontSizeStr)}' fill='{Esc(fontColor)}'{weightAttr}>{Esc(entry.Label)}</text>");
            }
            return;
        }

        decimal curX = startX;
        decimal titleWidth = legendTitle is not null ? legendTitle.Length * (fontSizeVal * 0.7m) + 12m : 0m;
        curX += titleWidth;
        int simRows = 1;
        for (int i = 0; i < entries.Length; i++)
        {
            decimal itemWidth = Math.Max(65m, entries[i].Label.Length * 6m + 25m);
            if (curX + itemWidth > maxX && curX > startX)
            {
                simRows++;
                curX = startX;
            }
            curX += itemWidth;
        }

        decimal renderStartY = position == "TOP" ? 29m : (position == "BOTTOM" ? plan.Bounds.Height - 12m - (simRows - 1) * rowHeight : Top);
        curX = startX;
        decimal renderCurrentY = renderStartY;

        if (legendTitle is not null)
        {
            builder.AppendLine($"<text class='plot-legend-title' x='{N(curX)}' y='{N(renderCurrentY)}' font-size='{Esc(fontSizeStr)}' fill='{Esc(fontColor)}'{titleWeight}>{Esc(legendTitle)}:</text>");
            curX += titleWidth;
        }

        for (int i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            decimal itemWidth = Math.Max(65m, entry.Label.Length * 6m + 25m);
            if (curX + itemWidth > maxX && curX > startX)
            {
                curX = startX;
                renderCurrentY += rowHeight;
            }

            decimal entryX = curX;
            decimal entryY = renderCurrentY;

            builder.AppendLine($"<rect x='{N(entryX)}' y='{N(entryY - 8m)}' width='9' height='9' fill='{Esc(SafePaint(entry.Color, "#5470c6"))}'/>");
            builder.AppendLine($"<text x='{N(entryX + 13m)}' y='{N(entryY)}' font-size='{Esc(fontSizeStr)}' fill='{Esc(fontColor)}'{weightAttr}>{Esc(entry.Label)}</text>");

            curX += itemWidth;
        }
    }

    private static decimal MapHorizontal(decimal value, ResolvedScale scale, decimal plotWidth)
    {
        var (minimum, maximum) = Domain(scale);
        var ratio = Ratio(value, minimum, maximum, scale.Kind);
        return Left + (scale.Reverse ? 1m - ratio : ratio) * plotWidth;
    }

    private static void RenderMicroSparkline(StringBuilder builder, PlotPlan plan)
    {
        var width = plan.Bounds.Width;
        var height = plan.Bounds.Height;
        var pad = 3m;
        var color = SafePaint(Style(plan, "COLOR"), "#5470c6");
        var layer = plan.Layers.FirstOrDefault();
        var yScale = plan.Scales.FirstOrDefault(scale => scale.Channel == FieldChannel.Y);
        if (layer is null || yScale is null) return;
        var segments = new List<List<(decimal X, decimal Y, decimal Value)>> { new() };
        var denominator = Math.Max(1, layer.Data.Length - 1);
        for (var index = 0; index < layer.Data.Length; index++)
        {
            var datum = layer.Data[index];
            var value = PlotPlanResolver.Number(Channel(datum, FieldChannel.Y) ?? ChartValue.Null());
            if (datum.IsGap || !value.HasValue)
            {
                if (segments[^1].Count > 0) segments.Add(new());
                continue;
            }
            var x = pad + (width - 2m * pad) * index / denominator;
            var (minimum, maximum) = Domain(yScale);
            var y = pad + (height - 2m * pad) - (value.Value - minimum) / (maximum - minimum) * (height - 2m * pad);
            segments[^1].Add((x, Math.Clamp(y, pad, height - pad), value.Value));
        }
        var points = segments.SelectMany(segment => segment).ToList();
        if (points.Count == 0) return;
        if (layer.Mark == MarkKind.Rect)
        {
            var slot = (width - 2m * pad) / Math.Max(1, layer.Data.Length);
            var baseline = height - pad;
            foreach (var point in points)
                builder.AppendLine($"<rect x='{N(point.X - slot * .35m)}' y='{N(point.Y)}' width='{N(Math.Max(1m, slot * .7m))}' height='{N(Math.Max(1m, baseline - point.Y))}' rx='1' fill='{Esc(color)}'/>");
            return;
        }
        foreach (var segment in segments.Where(segment => segment.Count > 0))
        {
            var path = "M " + string.Join(" L ", segment.Select(point => $"{N(point.X)} {N(point.Y)}"));
            if (layer.Mark == MarkKind.Area && segment.Count > 1)
                builder.AppendLine($"<path d='{path} L {N(segment[^1].X)} {N(height - pad)} L {N(segment[0].X)} {N(height - pad)} Z' fill='{Esc(color)}' fill-opacity='.22'/>");
            if (segment.Count > 1)
                builder.AppendLine($"<path d='{path}' fill='none' stroke='{Esc(color)}' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'/>");
        }
        builder.AppendLine($"<circle cx='{N(points[0].X)}' cy='{N(points[0].Y)}' r='2' fill='{Esc(color)}'/>");
        if (points.Count > 1) builder.AppendLine($"<circle cx='{N(points[^1].X)}' cy='{N(points[^1].Y)}' r='2' fill='{Esc(color)}'/>");
    }

    private static void RenderMicroProgress(StringBuilder builder, PlotPlan plan)
    {
        var width = plan.Bounds.Width;
        var height = plan.Bounds.Height;
        var scale = plan.Scales.FirstOrDefault(item => item.Channel == FieldChannel.Y);
        var value = plan.Layers.SelectMany(layer => layer.Data)
            .Select(datum => PlotPlanResolver.Number(Channel(datum, FieldChannel.Y) ?? ChartValue.Null()))
            .FirstOrDefault(item => item.HasValue) ?? 0m;
        var (minimum, maximum) = scale is null ? (0m, 1m) : Domain(scale);
        var ratio = Math.Clamp((value - minimum) / (maximum - minimum), 0m, 1m);
        var color = SafePaint(Style(plan, "COLOR"), "#3ba272");
        builder.AppendLine($"<rect x='1' y='3' width='{N(width - 2m)}' height='{N(Math.Max(1m, height - 6m))}' rx='{N(Math.Min(5m, height / 4m))}' fill='#e5e7eb'/>");
        builder.AppendLine($"<rect x='1' y='3' width='{N(Math.Max(0m, (width - 2m) * ratio))}' height='{N(Math.Max(1m, height - 6m))}' rx='{N(Math.Min(5m, height / 4m))}' fill='{Esc(color)}'/>");
    }

    private static void RenderArea(StringBuilder builder, ResolvedMarkLayer layer, int categoryCount,
        in CartesianPlotArea area, ResolvedScale? xScale, ResolvedScale? scale, string color)
    {
        if (scale is null || layer.Data.IsDefaultOrEmpty) return;
        var isConfidence = layer.Data.Any(datum => Channel(datum, FieldChannel.ConfidenceLow) is not null || Channel(datum, FieldChannel.ConfidenceHigh) is not null);
        var ribbon = isConfidence || layer.Data.Any(datum => Channel(datum, FieldChannel.YStart) is not null || Channel(datum, FieldChannel.YEnd) is not null);
        if (ribbon)
        {
            var ribbonSegments = new List<(List<(decimal X, decimal Y)> Upper, List<(decimal X, decimal Y)> Lower)>
                { (new(), new()) };
            for (var index = 0; index < layer.Data.Length; index++)
            {
                var datum = layer.Data[index];
                var start = PositionNumber(Channel(datum, isConfidence ? FieldChannel.ConfidenceLow : FieldChannel.YStart));
                var end = PositionNumber(Channel(datum, isConfidence ? FieldChannel.ConfidenceHigh : FieldChannel.YEnd));
                if (datum.IsGap || !start.HasValue || !end.HasValue)
                {
                    if (ribbonSegments[^1].Upper.Count > 0) ribbonSegments.Add((new(), new()));
                    continue;
                }
                var x = CategoryX(index, categoryCount, area, xScale) + datum.DisplayOffsetX;
                ribbonSegments[^1].Upper.Add((x, MapY(end.Value, scale, area.Height) + datum.DisplayOffsetY));
                ribbonSegments[^1].Lower.Add((x, MapY(start.Value, scale, area.Height) + datum.DisplayOffsetY));
            }
            foreach (var segment in ribbonSegments.Where(segment => segment.Upper.Count > 1))
            {
                var path = $"M {string.Join(" L ", segment.Upper.Select(point => $"{N(point.X)} {N(point.Y)}"))} " +
                    $"L {string.Join(" L ", segment.Lower.AsEnumerable().Reverse().Select(point => $"{N(point.X)} {N(point.Y)}"))} Z";
                var ribbonClass = isConfidence ? "plot-confidence-band" : "plot-ribbon";
                builder.AppendLine($"<path class='{ribbonClass}' d='{path}' fill='{Esc(color)}' fill-opacity='.2' stroke='{Esc(color)}' stroke-width='1'/>");
            }
            return;
        }
        var segments = new List<List<(decimal X, decimal Y)>> { new() };
        for (var index = 0; index < layer.Data.Length; index++)
        {
            var datum = layer.Data[index];
            var value = PlotPlanResolver.Number(Channel(datum, FieldChannel.Y) ?? ChartValue.Null());
            if (datum.IsGap || !value.HasValue)
            {
                if (segments[^1].Count > 0) segments.Add(new());
                continue;
            }
            segments[^1].Add((CategoryX(index, categoryCount, area, xScale) + datum.DisplayOffsetX,
                MapY(value.Value, scale, area.Height) + datum.DisplayOffsetY));
        }
        foreach (var points in segments.Where(segment => segment.Count > 1))
        {
            var path = "M " + string.Join(" L ", points.Select(point => $"{N(point.X)} {N(point.Y)}"));
            builder.AppendLine($"<path d='{path} L {N(points[^1].X)} {N(area.Bottom)} L {N(points[0].X)} {N(area.Bottom)} Z' fill='{Esc(color)}' fill-opacity='.2'/>");
            builder.AppendLine($"<path d='{path}' fill='none' stroke='{Esc(color)}' stroke-width='2'/>");
        }
    }

    /// <summary>
    /// Publishes the layer's resolved value extent onto the mark itself. A browser needs to know
    /// which dimension of a rectangle carries its value to draw a proportional selection over it;
    /// reading that off the mark keeps it from inferring geometry out of a chart's type name.
    /// </summary>
    private static string ExtentAttributes(ResolvedMarkLayer layer) => layer.ExtentAxis switch
    {
        MarkExtentAxis.Y => " data-extent-axis='y' data-extent-anchor='" + Anchor(layer.ExtentAnchor) + "'",
        MarkExtentAxis.X => " data-extent-axis='x' data-extent-anchor='" + Anchor(layer.ExtentAnchor) + "'",
        _ => string.Empty
    };

    private static string Anchor(MarkExtentAnchor anchor) => anchor == MarkExtentAnchor.End ? "end" : "start";

    private static void RenderRects(StringBuilder builder, PlotPlan plan, ResolvedMarkLayer layer, IReadOnlyList<ResolvedMarkLayer> layers, bool stacked,
        int categoryCount, in CartesianPlotArea area, ResolvedScale? xScale, ResolvedScale? scale, string color, bool showLabels)
    {
        if (scale is null) return;
        // A non-stacked layer whose author supplied X_START/X_END owns its horizontal extent outright and
        // needs no category band. A stacked layer's endpoints are resolver-computed, so it keeps the band.
        var rangedX = !stacked && xScale is not null && Continuous(xScale) &&
            layer.Data.Any(datum => Channel(datum, FieldChannel.XStart) is not null && Channel(datum, FieldChannel.XEnd) is not null);
        if (categoryCount == 0 && !rangedX) return;
        var layerIndex = Enumerable.Range(0, layers.Count).First(index => ReferenceEquals(layers[index], layer));
        var layerCount = Math.Max(1, layers.Count);
        var (slot, outerOffset) = CategoryLayout(categoryCount, area.Width, xScale);
        var groupWidth = slot * layer.BandSize;
        var seriesGap = !stacked && layerCount > 1 ? UnitStyle(plan, "SERIES_GAP") : null;
        var gapWidth = seriesGap.HasValue
            ? groupWidth * seriesGap.Value / (layerCount + seriesGap.Value * (layerCount - 1))
            : 0m;
        var barWidth = stacked ? groupWidth : seriesGap.HasValue
            ? (groupWidth - gapWidth * (layerCount - 1)) / layerCount
            : groupWidth / layerCount;
        // Loop-invariant: style lookups are linear scans over the token array and the extent
        // attributes depend only on the layer, so both resolve once per layer, not once per mark.
        var dataFormat = DataFormat(plan);
        var extentAttributes = ExtentAttributes(layer);
        var labelPosition = showLabels ? Style(plan, "DATA_LABELS:POSITION") ?? "OUTSIDE" : "OUTSIDE";
        var labelColorToken = showLabels ? Style(plan, "DATA_LABELS:COLOR") : null;
        var colorScale = ColorScale(plan);
        var errorBarStyle = LayerStyle(layer, "errorBarStyle") ?? LayerStyle(layer, "ERROR_BAR_STYLE") ?? LayerStyle(layer, "error_bar_style") ?? "CAPS";
        var hasCaps = !errorBarStyle.Equals("NO_CAPS", StringComparison.OrdinalIgnoreCase);
        for (var index = 0; index < layer.Data.Length; index++)
        {
            var datum = layer.Data[index];
            if (datum.IsGap) continue;
            var value = PlotPlanResolver.Number(Channel(datum, FieldChannel.Y) ?? Channel(datum, FieldChannel.Y2) ?? ChartValue.Null());
            var rangeStart = stacked ? null : PositionNumber(Channel(datum, FieldChannel.YStart));
            var rangeEnd = stacked ? null : PositionNumber(Channel(datum, FieldChannel.YEnd));
            var rangedY = rangeStart.HasValue && rangeEnd.HasValue;
            if (!rangedY && !value.HasValue) continue;
            var start = rangedY
                ? rangeStart!.Value
                : stacked
                    ? PlotPlanResolver.Number(Channel(datum, FieldChannel.YStart) ?? ChartValue.From(0m)) ?? 0m
                    : 0m;
            var end = rangedY
                ? rangeEnd!.Value
                : stacked
                    ? PlotPlanResolver.Number(Channel(datum, FieldChannel.YEnd) ?? ChartValue.From(start + value!.Value)) ?? start + value!.Value
                    : start + value!.Value;
            var startY = MapY(start, scale, area.Height) + datum.DisplayOffsetY;
            var endY = MapY(end, scale, area.Height) + datum.DisplayOffsetY;
            var x = area.Left + outerOffset + slot * index + (slot - groupWidth) / 2m +
                (stacked ? 0m : layerIndex * (barWidth + gapWidth)) + datum.DisplayOffsetX;
            var width = Math.Max(1m, barWidth - (seriesGap.HasValue ? 0m : 1m));
            if (rangedX)
            {
                var spanStart = PositionNumber(Channel(datum, FieldChannel.XStart));
                var spanEnd = PositionNumber(Channel(datum, FieldChannel.XEnd));
                if (!spanStart.HasValue || !spanEnd.HasValue) continue;
                var first = MapX(spanStart.Value, xScale!, area) + datum.DisplayOffsetX;
                var second = MapX(spanEnd.Value, xScale!, area) + datum.DisplayOffsetX;
                x = Math.Min(first, second);
                width = Math.Max(1m, Math.Abs(second - first));
            }
            var datumColor = ResolveDatumColor(colorScale, datum, color);
            var top = Math.Min(startY, endY);
            var barHeight = Math.Max(1m, Math.Abs(endY - startY));
            var errorLow = PlotPlanResolver.Number(Channel(datum, FieldChannel.ErrorLow) ?? ChartValue.Null());
            var errorHigh = PlotPlanResolver.Number(Channel(datum, FieldChannel.ErrorHigh) ?? ChartValue.Null());
            if (errorLow.HasValue && errorHigh.HasValue)
            {
                var lowY = MapY(errorLow.Value, scale, area.Height) + datum.DisplayOffsetY;
                var highY = MapY(errorHigh.Value, scale, area.Height) + datum.DisplayOffsetY;
                var centerX = x + width / 2m;
                const decimal capWidth = 4m;
                builder.AppendLine($"<g class='plot-error-bar' data-row-index='{datum.RowIndex}'>");
                builder.AppendLine($"<line class='plot-error-bar-stem' x1='{N(centerX)}' y1='{N(lowY)}' x2='{N(centerX)}' y2='{N(highY)}' stroke='{Esc(datumColor)}' stroke-width='1.5'/>");
                if (hasCaps)
                {
                    builder.AppendLine($"<line class='plot-error-bar-cap' x1='{N(centerX - capWidth)}' y1='{N(lowY)}' x2='{N(centerX + capWidth)}' y2='{N(lowY)}' stroke='{Esc(datumColor)}' stroke-width='1.5'/>");
                    builder.AppendLine($"<line class='plot-error-bar-cap' x1='{N(centerX - capWidth)}' y1='{N(highY)}' x2='{N(centerX + capWidth)}' y2='{N(highY)}' stroke='{Esc(datumColor)}' stroke-width='1.5'/>");
                }
                builder.AppendLine("</g>");
            }
            var rangedClass = rangedY || rangedX ? " class='plot-range-rect'" : string.Empty;
            var rxAttr = rangedY || rangedX ? string.Empty : " rx='1'";
            var title = rangedY
                ? $"{FormatDataLabel(start, dataFormat)} to {FormatDataLabel(end, dataFormat)}"
                : FormatDataLabel(value ?? (end - start), dataFormat);
            builder.AppendLine($"<rect{rangedClass} x='{N(x)}' y='{N(top)}' width='{N(width)}' height='{N(barHeight)}'{rxAttr} fill='{Esc(datumColor)}'{extentAttributes} data-row-index='{datum.RowIndex}'><title>{Esc(title)}</title></rect>");
            if (showLabels)
            {
                var label = FormatDataLabel(value ?? (end - start), dataFormat);
                var position = labelPosition.ToUpperInvariant();
                var labelX = x + width / 2m;
                var labelY = top - 4m;
                var anchor = "middle";
                var labelColor = SafePaint(labelColorToken, "#444");
                if (position.Contains("OUTSIDE", StringComparison.OrdinalIgnoreCase))
                {
                    labelY = (end >= start ? top - 4m : top + barHeight + 12m);
                }
                else if (position.Contains("TOP", StringComparison.OrdinalIgnoreCase))
                {
                    labelY = top + 13m;
                    labelColor = SafePaint(labelColorToken, "white");
                }
                else if (position.Contains("LEFT", StringComparison.OrdinalIgnoreCase))
                {
                    labelX = x - 3m;
                    labelY = top + barHeight / 2m + 3m;
                    anchor = "end";
                }
                else if (position.Contains("RIGHT", StringComparison.OrdinalIgnoreCase))
                {
                    labelX = x + width + 3m;
                    labelY = top + barHeight / 2m + 3m;
                    anchor = "start";
                }
                RenderDataLabelBackground(builder, plan, datum.RowIndex, labelX, labelY, anchor, FontSize(Style(plan, "DATA_LABELS:FONT_SIZE")), label);
                builder.AppendLine($"<text x='{N(labelX)}' y='{N(labelY)}' text-anchor='{anchor}' font-size='{Esc(Style(plan, "DATA_LABELS:FONT_SIZE") ?? "9")}' fill='{Esc(labelColor)}' font-weight='{Esc(Style(plan, "DATA_LABELS:FONT_WEIGHT") ?? "normal")}'>{Esc(label)}</text>");
            }
        }
    }

    private static void RenderTicks(StringBuilder builder, PlotPlan plan, ResolvedMarkLayer layer, int categoryCount,
        in CartesianPlotArea area, ResolvedScale? xScale, ResolvedScale? yScale, string color)
    {
        if (categoryCount == 0 || yScale is null) return;
        var (slot, _) = CategoryLayout(categoryCount, area.Width, xScale);
        var length = slot * layer.BandSize;
        var strokeWidth = Math.Clamp(layer.TickThickness * 8m, 1m, 8m);
        var colorScale = ColorScale(plan);
        for (var index = 0; index < layer.Data.Length; index++)
        {
            var datum = layer.Data[index];
            var value = PlotPlanResolver.Number(Channel(datum, FieldChannel.Y) ?? ChartValue.Null());
            if (datum.IsGap || !value.HasValue) continue;
            var x = CategoryX(index, categoryCount, area, xScale) + datum.DisplayOffsetX;
            var y = MapY(value.Value, yScale, area.Height) + datum.DisplayOffsetY;
            var vertical = layer.TickOrientation == TickOrientation.Vertical;
            var x1 = vertical ? x : x - length / 2m;
            var x2 = vertical ? x : x + length / 2m;
            var y1 = vertical ? y - Math.Min(length, area.Height / Math.Max(1, categoryCount)) / 2m : y;
            var y2 = vertical ? y + Math.Min(length, area.Height / Math.Max(1, categoryCount)) / 2m : y;
            var datumColor = ResolveDatumColor(colorScale, datum, color);
            builder.AppendLine($"<line class='plot-tick' x1='{N(x1)}' y1='{N(y1)}' x2='{N(x2)}' y2='{N(y2)}' stroke='{Esc(datumColor)}' stroke-width='{N(strokeWidth)}' data-row-index='{datum.RowIndex}'><title>{Esc(PlotPlanResolver.Display(Channel(datum, FieldChannel.Tooltip) ?? ChartValue.From(value.Value)))}</title></line>");
        }
    }

    private static void RenderLine(StringBuilder builder, PlotPlan plan, ResolvedMarkLayer layer, int categoryCount,
        in CartesianPlotArea area, ResolvedScale? xScale, ResolvedScale? scale, string color, bool showLabels,
        ICollection<OverlayLabel> overlayLabels, ICollection<SmartLabel> smartLabels,
        ICollection<SeriesLabelPlacement> seriesLabelPlacements)
    {
        if (scale is null || layer.Data.IsDefaultOrEmpty) return;
        var lineStyle = LayerStyle(layer, "lineStyle");
        var dashAttributes = LineStyleAttributes(lineStyle);
        var isOverlay = LayerStyle(layer, "overlayType") is not null;
        var smooth = IsEnabled(plan.Style, "SMOOTH") && !isOverlay;
        var strokeWidth = isOverlay ? "3" : LineWidth(layer, "2");
        var overlayType = LayerStyle(layer, "overlayType");
        var lineClass = overlayType == "Forecast" ? " class='plot-forecast-line'" : string.Empty;
        (decimal X, decimal Y)? firstPoint = null;
        (decimal X, decimal Y)? lastPoint = null;
        var segment = new List<(decimal X, decimal Y)>();

        var firstRenderableIndex = -1;
        var lastRenderableIndex = -1;
        for (var i = 0; i < layer.Data.Length; i++)
        {
            var d = layer.Data[i];
            if (d.IsGap) continue;
            var v = PlotPlanResolver.Number(Channel(d, FieldChannel.Y) ?? Channel(d, FieldChannel.Y2) ?? ChartValue.Null());
            if (!v.HasValue) continue;
            if (xScale is not null && xScale.Kind is ScaleKind.Linear or ScaleKind.Logarithmic)
            {
                var xv = PlotPlanResolver.Number(Channel(d, FieldChannel.X) ?? ChartValue.Null());
                if (!xv.HasValue) continue;
            }
            if (firstRenderableIndex < 0) firstRenderableIndex = i;
            lastRenderableIndex = i;
        }

        var seriesLabelsEnabled = !isOverlay && IsEnabled(plan.Style, "SERIES_LABELS");
        var seriesLabelsPos = (Style(plan, "SERIES_LABELS:POSITION") ?? "END").Trim().ToUpperInvariant();
        var isStartPos = seriesLabelsPos == "START";
        var seriesLabelTargetIndex = isStartPos ? firstRenderableIndex : lastRenderableIndex;

        void Flush()
        {
            if (segment.Count > 1) builder.AppendLine($"<path{lineClass} d='{PathData(segment, smooth)}' fill='none' stroke='{Esc(color)}' stroke-width='{strokeWidth}' stroke-linejoin='round' stroke-linecap='round'{dashAttributes}/>");
            segment.Clear();
        }
        for (var index = 0; index < layer.Data.Length; index++)
        {
            var datum = layer.Data[index];
            var value = PlotPlanResolver.Number(Channel(datum, FieldChannel.Y) ?? Channel(datum, FieldChannel.Y2) ?? ChartValue.Null());
            if (datum.IsGap || !value.HasValue) { Flush(); continue; }
            var xValue = PlotPlanResolver.Number(Channel(datum, FieldChannel.X) ?? ChartValue.Null());
            var x = xScale is not null && xScale.Kind is ScaleKind.Linear or ScaleKind.Logarithmic && xValue.HasValue
                ? MapX(xValue.Value, xScale, area)
                : CategoryX(index, categoryCount, area, xScale);
            x += datum.DisplayOffsetX;
            var y = MapY(value.Value, scale, area.Height) + datum.DisplayOffsetY;
            if (index == firstRenderableIndex) firstPoint = (x, y);
            if (index == lastRenderableIndex) lastPoint = (x, y);
            segment.Add((x, y));
            if (((isOverlay && overlayType != "Forecast") || IsEnabledByDefault(plan.Style, "SYMBOLS")) &&
                (!isOverlay || !plan.Layers.Any(candidate => candidate.Mark == MarkKind.Point && LayerStyle(candidate, "overlayType") is null)))
                RenderPointSymbol(builder, isOverlay ? null : PointShape(plan, layer, datum),
                    x, y, isOverlay ? 4m : 3m, color, isOverlay ? "plot-overlay-point" : "plot-line-symbol",
                    datum.RowIndex, FormatDataLabel(value.Value, DataFormat(plan)),
                    isOverlay ? " stroke='white' stroke-width='1.5'" : PointStrokeAttributes(layer));
            if (showLabels && (!seriesLabelsEnabled || index != seriesLabelTargetIndex))
                smartLabels.Add(new SmartLabel(datum.RowIndex, x, y,
                    FormatDataLabel(value.Value, DataFormat(plan)),
                    SafePaint(Style(plan, "DATA_LABELS:COLOR"), "#444"),
                    120 + layer.ZIndex,
                    FontSize(Style(plan, "DATA_LABELS:FONT_SIZE"))));
        }
        Flush();
        var overlayLabel = LayerStyle(layer, "label");
        if (isOverlay && lastPoint.HasValue && !string.IsNullOrWhiteSpace(overlayLabel))
            overlayLabels.Add(new OverlayLabel(lastPoint.Value.X, lastPoint.Value.Y, overlayLabel, color, layer.ZIndex));
        if (seriesLabelsEnabled)
        {
            var point = isStartPos ? firstPoint : lastPoint;
            if (point.HasValue)
            {
                var series = plan.Series.FirstOrDefault(item => item.Key.Equals(layer.SeriesKey ?? layer.Id, StringComparison.Ordinal));
                var label = series?.Label ?? layer.SeriesKey ?? layer.Id;
                var seriesColor = SafePaint(series?.Color, color);
                var order = series?.Order ?? layer.ZIndex;
                seriesLabelPlacements.Add(new SeriesLabelPlacement(
                    series?.Key ?? layer.SeriesKey ?? layer.Id,
                    label,
                    point.Value.X,
                    point.Value.Y,
                    isStartPos ? "START" : "END",
                    seriesColor,
                    order
                ));
            }
        }
    }

    private static void RenderStackedLine(StringBuilder builder, PlotPlan plan, ResolvedMarkLayer layer,
        IReadOnlyList<ResolvedMarkLayer> layers, int categoryCount, in CartesianPlotArea area,
        ResolvedScale? xScale, ResolvedScale? scale, string color, bool showLabels,
        ICollection<SeriesLabelPlacement> seriesLabelPlacements)
    {
        if (scale is null || layer.Data.IsDefaultOrEmpty) return;
        var topPoints = new List<(decimal X, decimal Y)>();
        var basePoints = new List<(decimal X, decimal Y)>();
        (decimal X, decimal Y)? firstPoint = null;
        (decimal X, decimal Y)? lastPoint = null;

        var firstRenderableIndex = -1;
        var lastRenderableIndex = -1;
        for (var i = 0; i < layer.Data.Length; i++)
        {
            var d = layer.Data[i];
            if (d.IsGap) continue;
            var v = PlotPlanResolver.Number(Channel(d, FieldChannel.Y) ?? ChartValue.Null());
            if (!v.HasValue) continue;
            if (firstRenderableIndex < 0) firstRenderableIndex = i;
            lastRenderableIndex = i;
        }

        var seriesLabelsEnabled = IsEnabled(plan.Style, "SERIES_LABELS");
        var seriesLabelsPos = (Style(plan, "SERIES_LABELS:POSITION") ?? "END").Trim().ToUpperInvariant();
        var isStartPos = seriesLabelsPos == "START";
        var seriesLabelTargetIndex = isStartPos ? firstRenderableIndex : lastRenderableIndex;

        void Flush()
        {
            if (topPoints.Count > 1)
            {
                var areaPath = $"M {string.Join(" L ", topPoints.Select(point => $"{N(point.X)} {N(point.Y)}"))} " +
                    $"L {string.Join(" L ", basePoints.AsEnumerable().Reverse().Select(point => $"{N(point.X)} {N(point.Y)}"))} Z";
                builder.AppendLine($"<path class='plot-stacked-area' data-series='{Esc(layer.SeriesKey ?? layer.Id)}' d='{areaPath}' fill='{Esc(color)}' fill-opacity='.28'/>");
                builder.AppendLine($"<path d='{PathData(topPoints, false)}' fill='none' stroke='{Esc(color)}' stroke-width='{LineWidth(layer, "2.5")}' stroke-linejoin='round' stroke-linecap='round'/>");
            }
            topPoints.Clear();
            basePoints.Clear();
        }

        for (var index = 0; index < layer.Data.Length; index++)
        {
            var datum = layer.Data[index];
            var value = PlotPlanResolver.Number(Channel(datum, FieldChannel.Y) ?? ChartValue.Null());
            if (datum.IsGap || !value.HasValue) { Flush(); continue; }
            var start = PlotPlanResolver.Number(Channel(datum, FieldChannel.YStart) ?? ChartValue.From(0m)) ?? 0m;
            var end = PlotPlanResolver.Number(Channel(datum, FieldChannel.YEnd) ?? ChartValue.From(start + value.Value)) ?? start + value.Value;
            var x = CategoryX(index, categoryCount, area, xScale) + datum.DisplayOffsetX;
            var topY = MapY(end, scale, area.Height) + datum.DisplayOffsetY;
            if (index == firstRenderableIndex) firstPoint = (x, topY);
            if (index == lastRenderableIndex) lastPoint = (x, topY);
            topPoints.Add((x, topY));
            basePoints.Add((x, MapY(start, scale, area.Height) + datum.DisplayOffsetY));
            if (IsEnabledByDefault(plan.Style, "SYMBOLS"))
                RenderPointSymbol(builder, PointShape(plan, layer, datum), x, topY, 3m,
                    color, "plot-line-symbol", datum.RowIndex, FormatDataLabel(value.Value, DataFormat(plan)),
                    PointStrokeAttributes(layer));
            if (showLabels && (!seriesLabelsEnabled || index != seriesLabelTargetIndex))
            {
                RenderDataLabelBackground(builder, plan, datum.RowIndex, x, topY - 6m, "middle",
                    FontSize(Style(plan, "DATA_LABELS:FONT_SIZE")),
                    FormatDataLabel(value.Value, DataFormat(plan)));
                builder.AppendLine($"<text x='{N(x)}' y='{N(topY - 6m)}' text-anchor='middle' font-size='{Esc(Style(plan, "DATA_LABELS:FONT_SIZE") ?? "9")}' fill='{Esc(SafePaint(Style(plan, "DATA_LABELS:COLOR"), "#444"))}'>{Esc(FormatDataLabel(value.Value, DataFormat(plan)))}</text>");
            }
        }
        Flush();
        if (seriesLabelsEnabled)
        {
            var point = isStartPos ? firstPoint : lastPoint;
            if (point.HasValue)
            {
                var series = plan.Series.FirstOrDefault(item => item.Key.Equals(layer.SeriesKey ?? layer.Id, StringComparison.Ordinal));
                var label = series?.Label ?? layer.SeriesKey ?? layer.Id;
                var seriesColor = SafePaint(series?.Color, color);
                var order = series?.Order ?? layer.ZIndex;
                seriesLabelPlacements.Add(new SeriesLabelPlacement(
                    series?.Key ?? layer.SeriesKey ?? layer.Id,
                    label,
                    point.Value.X,
                    point.Value.Y,
                    isStartPos ? "START" : "END",
                    seriesColor,
                    order
                ));
            }
        }
    }

    private static decimal CategoryX(int index, int categoryCount, decimal plotWidth, ResolvedScale? scale = null, decimal plotLeft = Left)
    {
        var (slot, outerOffset) = CategoryLayout(categoryCount, plotWidth, scale);
        return plotLeft + outerOffset + slot * (index + .5m);
    }

    private static decimal CategoryX(int index, int categoryCount, in CartesianPlotArea area, ResolvedScale? scale = null) =>
        CategoryX(index, categoryCount, area.Width, scale, area.Left);

    private static (decimal Slot, decimal OuterOffset) CategoryLayout(int categoryCount, decimal length, ResolvedScale? scale)
    {
        var padding = scale?.Kind == ScaleKind.Band ? Math.Clamp(scale.OuterPadding, 0m, 1m) : 0m;
        var slot = length / Math.Max(1m, categoryCount + 2m * padding);
        return (slot, slot * padding);
    }

    private static string PathData(IReadOnlyList<(decimal X, decimal Y)> points, bool smooth)
    {
        if (points.Count == 0) return string.Empty;
        if (!smooth || points.Count < 3)
            return $"M {string.Join(" L ", points.Select(point => $"{N(point.X)} {N(point.Y)}"))}";
        var builder = new StringBuilder($"M {N(points[0].X)} {N(points[0].Y)}");
        for (var index = 0; index < points.Count - 1; index++)
        {
            var before = index == 0 ? points[index] : points[index - 1];
            var start = points[index];
            var end = points[index + 1];
            var after = index + 2 < points.Count ? points[index + 2] : end;
            var control1 = (start.X + (end.X - before.X) / 6m, start.Y + (end.Y - before.Y) / 6m);
            var control2 = (end.X - (after.X - start.X) / 6m, end.Y - (after.Y - start.Y) / 6m);
            builder.Append($" C {N(control1.Item1)} {N(control1.Item2)} {N(control2.Item1)} {N(control2.Item2)} {N(end.X)} {N(end.Y)}");
        }
        return builder.ToString();
    }

    private static void RenderPoints(StringBuilder builder, PlotPlan plan, ResolvedMarkLayer layer, int categoryCount,
        in CartesianPlotArea area, ResolvedScale? xScale, ResolvedScale? yScale, string color,
        ICollection<SmartLabel> smartLabels)
    {
        if (xScale is null || yScale is null) return;
        var minimumSize = 0m;
        var maximumSize = 0m;
        var sawSize = false;
        for (var i = 0; i < layer.Data.Length; i++)
        {
            var size = Numeric(layer.Data[i], FieldChannel.Size);
            if (!size.HasValue) continue;
            if (!sawSize)
            {
                minimumSize = size.Value;
                maximumSize = size.Value;
                sawSize = true;
            }
            else
            {
                if (size.Value < minimumSize) minimumSize = size.Value;
                if (size.Value > maximumSize) maximumSize = size.Value;
            }
        }
        var minRadius = 4m;
        var maxRadius = 22m;
        var minBubbleStr = Style(plan, "MIN_BUBBLE_SIZE") ?? LayerStyle(layer, "MIN_BUBBLE_SIZE");
        var maxBubbleStr = Style(plan, "MAX_BUBBLE_SIZE") ?? LayerStyle(layer, "MAX_BUBBLE_SIZE");
        var sizeRangeStr = Style(plan, "SIZE_RANGE") ?? LayerStyle(layer, "SIZE_RANGE");
        if (!string.IsNullOrWhiteSpace(sizeRangeStr) && NamedVisualChartLowerer.TryParseSizeRange(sizeRangeStr, out var srMin, out var srMax))
        {
            minRadius = srMin;
            maxRadius = srMax;
        }
        if (!string.IsNullOrWhiteSpace(minBubbleStr) && decimal.TryParse(minBubbleStr, NumberStyles.Number, CultureInfo.InvariantCulture, out var mb))
        {
            minRadius = mb;
        }
        if (!string.IsNullOrWhiteSpace(maxBubbleStr) && decimal.TryParse(maxBubbleStr, NumberStyles.Number, CultureInfo.InvariantCulture, out var xb))
        {
            maxRadius = xb;
        }

        var showLabels = IsEnabled(plan.Style, "DATA_LABELS");
        var labelFormat = DataFormat(plan);
        var labelFontSize = FontSize(Style(plan, "DATA_LABELS:FONT_SIZE"));
        var labelColor = SafePaint(Style(plan, "DATA_LABELS:COLOR"), "#444");
        var colorScale = ColorScale(plan);
        var overlayType = LayerStyle(layer, "overlayType");
        var isOverlay = overlayType is not null;
        var pointClass = overlayType == "ForecastAnomaly"
            ? "plot-anomaly-marker"
            : isOverlay ? "plot-overlay-point" : "plot-point";
        var errorBarStyle = LayerStyle(layer, "errorBarStyle") ?? LayerStyle(layer, "ERROR_BAR_STYLE") ?? LayerStyle(layer, "error_bar_style") ?? "CAPS";
        var hasCaps = !errorBarStyle.Equals("NO_CAPS", StringComparison.OrdinalIgnoreCase);
        builder.AppendLine($"<g class='plot-points' data-layer='{Esc(layer.Id)}'>");
        for (var index = 0; index < layer.Data.Length; index++)
        {
            var datum = layer.Data[index];
            var yValue = PlotPlanResolver.Number(Channel(datum, FieldChannel.Y) ?? Channel(datum, FieldChannel.Y2) ?? ChartValue.Null());
            var xChannel = Channel(datum, FieldChannel.X) ?? ChartValue.Null();
            if (datum.IsGap || !yValue.HasValue) continue;
            var radius = NormalizePointRadius(Numeric(datum, FieldChannel.Size), minimumSize, maximumSize, minRadius, maxRadius);
            var datumColor = ResolveDatumColor(colorScale, datum, color);
            var opacity = EncodingNumber(datum, ConditionalEncodingChannel.Opacity) ?? 1m;
            var label = Channel(datum, FieldChannel.Text) is { } labelValue ? PlotPlanResolver.Display(labelValue) : null;
            decimal x;
            if (!xScale.Categories.IsDefaultOrEmpty)
            {
                var category = DisplayChannel(datum, FieldChannel.X);
                var categoryIndex = category is null ? -1 : xScale.Categories.IndexOf(category);
                if (categoryIndex < 0) continue;
                x = CategoryX(categoryIndex, categoryCount, area, xScale);
            }
            else
            {
                var xValue = PlotPlanResolver.Number(xChannel);
                if (!xValue.HasValue) continue;
                x = MapX(xValue.Value, xScale, area);
            }
            x += datum.DisplayOffsetX;
            var y = MapY(yValue.Value, yScale, area.Height) + datum.DisplayOffsetY;
            var errorLow = PlotPlanResolver.Number(Channel(datum, FieldChannel.ErrorLow) ?? ChartValue.Null());
            var errorHigh = PlotPlanResolver.Number(Channel(datum, FieldChannel.ErrorHigh) ?? ChartValue.Null());
            if (errorLow.HasValue && errorHigh.HasValue)
            {
                var lowY = MapY(errorLow.Value, yScale, area.Height) + datum.DisplayOffsetY;
                var highY = MapY(errorHigh.Value, yScale, area.Height) + datum.DisplayOffsetY;
                const decimal capWidth = 4m;
                builder.AppendLine($"<g class='plot-error-bar' data-row-index='{datum.RowIndex}'>");
                builder.AppendLine($"<line class='plot-error-bar-stem' x1='{N(x)}' y1='{N(lowY)}' x2='{N(x)}' y2='{N(highY)}' stroke='{Esc(datumColor)}' stroke-width='1.5'/>");
                if (hasCaps)
                {
                    builder.AppendLine($"<line class='plot-error-bar-cap' x1='{N(x - capWidth)}' y1='{N(lowY)}' x2='{N(x + capWidth)}' y2='{N(lowY)}' stroke='{Esc(datumColor)}' stroke-width='1.5'/>");
                    builder.AppendLine($"<line class='plot-error-bar-cap' x1='{N(x - capWidth)}' y1='{N(highY)}' x2='{N(x + capWidth)}' y2='{N(highY)}' stroke='{Esc(datumColor)}' stroke-width='1.5'/>");
                }
                builder.AppendLine("</g>");
            }
            RenderPointSymbol(builder, PointShape(plan, layer, datum), x, y, Math.Clamp(radius, 0.5m, Math.Max(30m, maxRadius)),
                datumColor, pointClass, datum.RowIndex, label,
                OpacityAttribute(opacity) + PointStrokeAttributes(layer));
            if (showLabels)
                smartLabels.Add(new SmartLabel(datum.RowIndex, x, y,
                    label ?? FormatDataLabel(yValue.Value, labelFormat),
                    labelColor,
                    100 + layer.ZIndex,
                    labelFontSize));
        }
        builder.AppendLine("</g>");
    }

    private static string? PointShape(PlotPlan plan, ResolvedMarkLayer layer, ResolvedDatum datum) =>
        EncodingText(datum, ConditionalEncodingChannel.Shape)
        ?? DisplayChannel(datum, FieldChannel.Shape)
        ?? LayerStyle(layer, "symbolShape")
        ?? Style(plan, "SYMBOL_SHAPE");

    private static string PointStrokeAttributes(ResolvedMarkLayer layer)
    {
        var color = LayerStyle(layer, "SYMBOL_STROKE_COLOR");
        if (!PointMarkerStroke.IsPortableColor(color)) return string.Empty;

        var width = PointMarkerStroke.TryNormalizeWidth(LayerStyle(layer, "SYMBOL_STROKE_WIDTH"), out var normalized)
            ? normalized
            : "1";
        return $" stroke='{Esc(color!)}' stroke-width='{width}'";
    }

    private static string LineWidth(ResolvedMarkLayer layer, string fallback) =>
        LineSeriesWidth.TryNormalize(LayerStyle(layer, "LINE_WIDTH"), out var width) ? width : fallback;

    private static void RenderPointSymbol(
        StringBuilder builder,
        string? shape,
        decimal x,
        decimal y,
        decimal radius,
        string color,
        string cssClass,
        int rowIndex,
        string? title,
        string extraAttributes = "")
    {
        var normalized = PointShapeVocabulary.NormalizeOrDefault(shape);
        var shapeAttribute = shape is null ? string.Empty : $" data-symbol-shape='{normalized}'";
        var common = $"class='{cssClass}'{shapeAttribute} fill='{Esc(color)}'{extraAttributes} data-row-index='{rowIndex}'";
        var content = string.IsNullOrWhiteSpace(title) ? string.Empty : $"<title>{Esc(title)}</title>";
        switch (normalized)
        {
            case "SQUARE":
                builder.AppendLine($"<rect {common} x='{N(x - radius)}' y='{N(y - radius)}' width='{N(radius * 2m)}' height='{N(radius * 2m)}'>{content}</rect>");
                break;
            case "TRIANGLE":
                builder.AppendLine($"<polygon {common} points='{N(x)},{N(y - radius)} {N(x + radius)},{N(y + radius)} {N(x - radius)},{N(y + radius)}'>{content}</polygon>");
                break;
            case "DIAMOND":
                builder.AppendLine($"<polygon {common} points='{N(x)},{N(y - radius)} {N(x + radius)},{N(y)} {N(x)},{N(y + radius)} {N(x - radius)},{N(y)}'>{content}</polygon>");
                break;
            case "CROSS":
                var arm = radius / 3m;
                builder.AppendLine($"<polygon {common} points='{N(x - radius)},{N(y - arm)} {N(x - arm)},{N(y - arm)} {N(x - arm)},{N(y - radius)} {N(x + arm)},{N(y - radius)} {N(x + arm)},{N(y - arm)} {N(x + radius)},{N(y - arm)} {N(x + radius)},{N(y + arm)} {N(x + arm)},{N(y + arm)} {N(x + arm)},{N(y + radius)} {N(x - arm)},{N(y + radius)} {N(x - arm)},{N(y + arm)} {N(x - radius)},{N(y + arm)}'>{content}</polygon>");
                break;
            case "STAR":
                builder.AppendLine($"<polygon {common} points='{StarPoints(x, y, radius)}'>{content}</polygon>");
                break;
            default:
                builder.AppendLine($"<circle {common} cx='{N(x)}' cy='{N(y)}' r='{N(radius)}'>{content}</circle>");
                break;
        }
    }

    private static string StarPoints(decimal x, decimal y, decimal radius)
    {
        var points = new string[10];
        for (var index = 0; index < points.Length; index++)
        {
            var angle = -Math.PI / 2d + index * Math.PI / 5d;
            var pointRadius = index % 2 == 0 ? radius : radius * .45m;
            var pointX = x + pointRadius * (decimal)Math.Cos(angle);
            var pointY = y + pointRadius * (decimal)Math.Sin(angle);
            points[index] = $"{N(pointX)},{N(pointY)}";
        }
        return string.Join(" ", points);
    }

    private static void RenderFunnel(StringBuilder builder, PlotPlan plan)
    {
        var data = plan.Layers.First().Data.Where(datum => !datum.IsGap).ToList();
        var values = data.Select(datum => Math.Max(0m, PlotPlanResolver.Number(Channel(datum, FieldChannel.Y) ?? ChartValue.Null()) ?? 0m)).ToList();
        var maximum = values.DefaultIfEmpty(1m).Max();
        if (maximum <= 0m) maximum = 1m;
        var showValues = IsEnabled(plan.Style, "DATA_LABELS");
        const decimal labelGutter = 150m;
        var availableWidth = Math.Max(120m, plan.Bounds.Width - Left - Right - labelGutter);
        var center = Left + availableWidth / 2m;
        var labelX = Left + availableWidth + 18m;
        var top = Top + 8m;
        var rowHeight = Math.Max(18m, (plan.Bounds.Height - top - 25m) / Math.Max(1, data.Count));
        for (var index = 0; index < data.Count; index++)
        {
            var currentWidth = availableWidth * values[index] / maximum;
            var nextWidth = index + 1 < values.Count ? availableWidth * values[index + 1] / maximum : currentWidth * .72m;
            var y1 = top + index * rowHeight;
            var y2 = y1 + rowHeight - 2m;
            var color = plan.Palette.ElementAtOrDefault(index)?.Color ?? DefaultColor(index);
            var points = $"{N(center - currentWidth / 2m)},{N(y1)} {N(center + currentWidth / 2m)},{N(y1)} {N(center + nextWidth / 2m)},{N(y2)} {N(center - nextWidth / 2m)},{N(y2)}";
            var label = DisplayChannel(data[index], FieldChannel.X) ?? $"Stage {index + 1}";
            builder.AppendLine($"<polygon points='{points}' fill='{Esc(color)}' data-row-index='{data[index].RowIndex}'><title>{Esc(label)}: {N(values[index])}</title></polygon>");
            var midY = y1 + rowHeight / 2m;
            var rightEdge = center + (currentWidth + nextWidth) / 4m;
            builder.AppendLine($"<path d='M {N(rightEdge + 3m)} {N(midY)} H {N(labelX - 6m)}' fill='none' stroke='#9ca3af' stroke-width='1'/>");
            var fullLabel = showValues ? $"{label} · {N(values[index])}" : label;
            if (showValues)
            {
                RenderDataLabelBackground(builder, plan, data[index].RowIndex, labelX, midY + 4m, "start", 10m, fullLabel);
            }
            builder.AppendLine($"<text x='{N(labelX)}' y='{N(midY + 4m)}' text-anchor='start' font-size='10' fill='#374151'>{Esc(fullLabel)}</text>");
        }
    }

    private static void RenderGauge(StringBuilder builder, PlotPlan plan)
    {
        var datum = plan.Layers.First().Data.FirstOrDefault(item => !item.IsGap);
        if (datum is null) return;
        var value = PlotPlanResolver.Number(Channel(datum, FieldChannel.Radius) ?? ChartValue.Null()) ?? 0m;
        var scale = plan.Scales.FirstOrDefault(item => item.Channel == FieldChannel.Radius);
        var minimum = scale?.Domain.Length > 0 ? PlotPlanResolver.Number(scale.Domain[0]) ?? 0m : 0m;
        var maximum = scale?.Domain.Length > 1 ? PlotPlanResolver.Number(scale.Domain[^1]) ?? 100m : 100m;
        var ratio = maximum <= minimum ? 0m : Math.Clamp((value - minimum) / (maximum - minimum), 0m, 1m);
        var style = (Style(plan, "GAUGE_STYLE") ?? "PROGRESS").Trim().Replace('-', '_').ToUpperInvariant();
        var color = SafePaint(Style(plan, "COLOR"), plan.Palette.FirstOrDefault()?.Color ?? "#5470c6");
        var label = DisplayChannel(datum, FieldChannel.Text);
        var goal = PlotPlanResolver.Number(Channel(datum, FieldChannel.Detail) ?? ChartValue.Null());
        if (style == "BAR")
        {
            RenderGaugeBar(builder, plan, value, ratio, color, label, minimum, maximum, goal);
            return;
        }
        if (style == "RING")
        {
            RenderGaugeRing(builder, plan, value, ratio, color, label, minimum, maximum, goal);
            return;
        }

        var cx = plan.Bounds.Width / 2m;
        var semiCircle = style is "SEMI_CIRCLE" or "SEMICIRCLE" or "NEEDLE";
        var cy = semiCircle ? plan.Bounds.Height * .68m : plan.Bounds.Height * .53m;
        var radius = semiCircle
            ? Math.Min(plan.Bounds.Width * .34m, plan.Bounds.Height * .42m)
            : Math.Min(plan.Bounds.Width * .28m, plan.Bounds.Height * .34m);
        var start = semiCircle ? Math.PI : Math.PI * .75d;
        var end = semiCircle ? Math.PI * 2d : Math.PI * 2.25d;
        var valueEnd = start + (end - start) * (double)ratio;
        builder.AppendLine($"<path d='{ArcPath(cx, cy, radius, start, end)}' fill='none' stroke='#e5e7eb' stroke-width='24' stroke-linecap='round'/>");
        builder.AppendLine($"<path class='plot-gauge-value' data-gauge-style='{Esc(style)}' d='{ArcPath(cx, cy, radius, start, valueEnd)}' fill='none' stroke='{Esc(color)}' stroke-width='24' stroke-linecap='round'/>");
        if (style == "NEEDLE")
        {
            var needle = PointCoordinates(cx, cy, radius - 14m, valueEnd);
            builder.AppendLine($"<line class='plot-gauge-needle' x1='{N(cx)}' y1='{N(cy)}' x2='{N(needle.X)}' y2='{N(needle.Y)}' stroke='#374151' stroke-width='3'/><circle cx='{N(cx)}' cy='{N(cy)}' r='6' fill='#374151'/>");
        }
        if (goal.HasValue)
        {
            var goalRatio = maximum <= minimum ? 0m : Math.Clamp((goal.Value - minimum) / (maximum - minimum), 0m, 1m);
            var goalAngle = start + (end - start) * (double)goalRatio;
            var goalInner = PointCoordinates(cx, cy, radius - 18m, goalAngle);
            var goalOuter = PointCoordinates(cx, cy, radius + 18m, goalAngle);
            builder.AppendLine($"<line class='plot-gauge-goal' x1='{N(goalInner.X)}' y1='{N(goalInner.Y)}' x2='{N(goalOuter.X)}' y2='{N(goalOuter.Y)}' stroke='#111827' stroke-width='3'><title>Goal: {N(goal.Value)}</title></line>");
        }
        var valueY = semiCircle ? cy - 18m : cy + 5m;
        builder.AppendLine($"<text class='plot-gauge-value-label' x='{N(cx)}' y='{N(valueY)}' text-anchor='middle' font-size='18' font-weight='bold' fill='#333'>{N(value)}</text>");
        if (!string.IsNullOrWhiteSpace(label)) builder.AppendLine($"<text x='{N(cx)}' y='{N(valueY + 18m)}' text-anchor='middle' font-size='10' fill='#666'>{Esc(label)}</text>");
        if (semiCircle)
        {
            builder.AppendLine($"<text x='{N(cx - radius)}' y='{N(cy + 20m)}' text-anchor='middle' font-size='8' fill='#6b7280'>{N(minimum)}</text>");
            builder.AppendLine($"<text x='{N(cx + radius)}' y='{N(cy + 20m)}' text-anchor='middle' font-size='8' fill='#6b7280'>{N(maximum)}</text>");
        }
    }

    private static void RenderGaugeRing(StringBuilder builder, PlotPlan plan, decimal value, decimal ratio, string color,
        string? label, decimal minimum, decimal maximum, decimal? goal)
    {
        var cx = plan.Bounds.Width / 2m;
        var cy = plan.Bounds.Height * .53m;
        var radius = Math.Min(plan.Bounds.Width, plan.Bounds.Height) * .31m;
        var circumference = 2m * (decimal)Math.PI * radius;
        builder.AppendLine($"<circle cx='{N(cx)}' cy='{N(cy)}' r='{N(radius)}' fill='none' stroke='#e5e7eb' stroke-width='24'/>");
        builder.AppendLine($"<circle class='plot-gauge-value' data-gauge-style='RING' cx='{N(cx)}' cy='{N(cy)}' r='{N(radius)}' fill='none' stroke='{Esc(color)}' stroke-width='24' stroke-linecap='round' stroke-dasharray='{N(circumference * ratio)} {N(circumference)}' transform='rotate(-90 {N(cx)} {N(cy)})'/>");
        if (goal.HasValue)
        {
            var goalRatio = maximum <= minimum ? 0m : Math.Clamp((goal.Value - minimum) / (maximum - minimum), 0m, 1m);
            var goalPoint = PointCoordinates(cx, cy, radius, -Math.PI / 2d + 2d * Math.PI * (double)goalRatio);
            builder.AppendLine($"<circle class='plot-gauge-goal' cx='{N(goalPoint.X)}' cy='{N(goalPoint.Y)}' r='4' fill='#111827'><title>Goal: {N(goal.Value)}</title></circle>");
        }
        builder.AppendLine($"<text class='plot-gauge-value-label' x='{N(cx)}' y='{N(cy + 5m)}' text-anchor='middle' font-size='20' font-weight='bold' fill='#333'>{N(value)}</text>");
        if (!string.IsNullOrWhiteSpace(label)) builder.AppendLine($"<text x='{N(cx)}' y='{N(cy + 23m)}' text-anchor='middle' font-size='10' fill='#666'>{Esc(label)}</text>");
    }

    private static void RenderGaugeBar(StringBuilder builder, PlotPlan plan, decimal value, decimal ratio, string color,
        string? label, decimal minimum, decimal maximum, decimal? goal)
    {
        var x = 62m;
        var width = plan.Bounds.Width - 124m;
        var y = plan.Bounds.Height * .48m;
        builder.AppendLine($"<rect x='{N(x)}' y='{N(y)}' width='{N(width)}' height='24' rx='12' fill='#e5e7eb'/>");
        builder.AppendLine($"<rect class='plot-gauge-value' data-gauge-style='BAR' x='{N(x)}' y='{N(y)}' width='{N(width * ratio)}' height='24' rx='12' fill='{Esc(color)}'/>");
        if (goal.HasValue)
        {
            var goalRatio = maximum <= minimum ? 0m : Math.Clamp((goal.Value - minimum) / (maximum - minimum), 0m, 1m);
            var goalX = x + width * goalRatio;
            builder.AppendLine($"<line class='plot-gauge-goal' x1='{N(goalX)}' y1='{N(y - 7m)}' x2='{N(goalX)}' y2='{N(y + 31m)}' stroke='#111827' stroke-width='3'><title>Goal: {N(goal.Value)}</title></line>");
        }
        builder.AppendLine($"<text class='plot-gauge-value-label' x='{N(plan.Bounds.Width / 2m)}' y='{N(y - 18m)}' text-anchor='middle' font-size='20' font-weight='bold' fill='#333'>{N(value)}</text>");
        if (!string.IsNullOrWhiteSpace(label)) builder.AppendLine($"<text x='{N(plan.Bounds.Width / 2m)}' y='{N(y + 52m)}' text-anchor='middle' font-size='10' fill='#666'>{Esc(label)}</text>");
        builder.AppendLine($"<text x='{N(x)}' y='{N(y + 42m)}' text-anchor='start' font-size='8' fill='#6b7280'>{N(minimum)}</text>");
        builder.AppendLine($"<text x='{N(x + width)}' y='{N(y + 42m)}' text-anchor='end' font-size='8' fill='#6b7280'>{N(maximum)}</text>");
    }

    private static void RenderHeatMap(StringBuilder builder, PlotPlan plan)
    {
        var layer = plan.Layers.First();
        var xCategories = plan.Scales.FirstOrDefault(scale => scale.Channel == FieldChannel.X)?.Categories ?? [];
        var yCategories = plan.Scales.FirstOrDefault(scale => scale.Channel == FieldChannel.Y)?.Categories ?? [];
        var valueScale = plan.Scales.FirstOrDefault(scale => scale.Channel == FieldChannel.Size);
        if (xCategories.IsDefaultOrEmpty || yCategories.IsDefaultOrEmpty || valueScale is null) return;
        var minimum = PlotPlanResolver.Number(valueScale.Domain[0]) ?? 0m;
        var maximum = PlotPlanResolver.Number(valueScale.Domain[^1]) ?? 1m;
        var plotWidth = plan.Bounds.Width - Left - Right;
        var plotHeight = plan.Bounds.Height - Top - Bottom;
        var cellWidth = plotWidth / xCategories.Length;
        var cellHeight = plotHeight / yCategories.Length;

        // Color resolution
        var range = valueScale.ColorRange;
        var lowColor = SafePaint(range?.Low ?? Style(plan, "COLOR_LOW") ?? Style(plan, "COLOR:low") ?? Style(plan, "COLOR:min"), "#dbeafe");
        var highColor = SafePaint(range?.High ?? Style(plan, "COLOR_HIGH") ?? Style(plan, "COLOR:high") ?? Style(plan, "COLOR:max"), "#1d4ed8");
        var midColor = range?.Mid ?? Style(plan, "COLOR_MID") ?? Style(plan, "COLOR:mid");
        var midpointStr = Style(plan, "MIDPOINT");
        decimal? midpoint = range?.Midpoint;
        if (midpoint is null && !string.IsNullOrWhiteSpace(midpointStr) && decimal.TryParse(midpointStr, NumberStyles.Number, CultureInfo.InvariantCulture, out var mp))
        {
            midpoint = mp;
        }
        var nullColor = SafePaint(range?.NullColor ?? Style(plan, "NULL_COLOR"), "#f1f5f9");
        var isDiverging = (range?.Kind == ColorRangeKind.Diverging) || midColor is not null || midpoint.HasValue;
        if (isDiverging && midColor is null)
        {
            midColor = "#ffffff";
        }

        // Cell border resolution
        var cellBorderOpt = Style(plan, "CELL_BORDER") ?? LayerStyle(layer, "CELL_BORDER");
        var cellBorder = !string.Equals(cellBorderOpt, "OFF", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(cellBorderOpt, "FALSE", StringComparison.OrdinalIgnoreCase);
        var cellBorderColor = Style(plan, "CELL_BORDER_COLOR") ?? LayerStyle(layer, "CELL_BORDER_COLOR");
        var cellBorderWidthStr = Style(plan, "CELL_BORDER_WIDTH") ?? LayerStyle(layer, "CELL_BORDER_WIDTH");
        var borderWidth = decimal.TryParse(cellBorderWidthStr, NumberStyles.Number, CultureInfo.InvariantCulture, out var bw) && bw >= 0m ? bw : 1m;

        var showLabels = IsEnabled(plan.Style, "DATA_LABELS");

        string GetColor(decimal v)
        {
            var ratio = maximum <= minimum ? 1m : Math.Clamp((v - minimum) / (maximum - minimum), 0m, 1m);
            if (isDiverging && midColor is not null)
            {
                var middle = midpoint.HasValue && maximum > minimum
                    ? Math.Clamp((midpoint.Value - minimum) / (maximum - minimum), 0m, 1m)
                    : 0.5m;
                return ratio <= middle
                    ? InterpolateColor(lowColor, midColor, middle == 0m ? 0m : ratio / middle)
                    : InterpolateColor(midColor, highColor, middle == 1m ? 1m : (ratio - middle) / (1m - middle));
            }
            return InterpolateColor(lowColor, highColor, ratio);
        }

        // Index data cells by coordinate
        var cellMap = new Dictionary<(int X, int Y), (ResolvedDatum Datum, decimal? Value)>();
        foreach (var datum in layer.Data.Where(item => !item.IsGap))
        {
            var x = DisplayChannel(datum, FieldChannel.X);
            var y = DisplayChannel(datum, FieldChannel.Y);
            var xIndex = x is null ? -1 : xCategories.IndexOf(x);
            var yIndex = y is null ? -1 : yCategories.IndexOf(y);
            if (xIndex < 0 || yIndex < 0) continue;
            var value = PlotPlanResolver.Number(Channel(datum, FieldChannel.Size) ?? ChartValue.Null());
            cellMap[(xIndex, yIndex)] = (datum, value);
        }

        var w = cellBorder ? Math.Max(1m, cellWidth - borderWidth) : cellWidth;
        var h = cellBorder ? Math.Max(1m, cellHeight - borderWidth) : cellHeight;
        var strokeAttr = cellBorder && !string.IsNullOrWhiteSpace(cellBorderColor)
            ? $" stroke='{Esc(cellBorderColor)}' stroke-width='{N(borderWidth)}'"
            : string.Empty;

        for (var yIndex = 0; yIndex < yCategories.Length; yIndex++)
        {
            for (var xIndex = 0; xIndex < xCategories.Length; xIndex++)
            {
                var cellX = Left + xIndex * cellWidth;
                var cellY = Top + yIndex * cellHeight;
                var xName = xCategories[xIndex];
                var yName = yCategories[yIndex];

                if (cellMap.TryGetValue((xIndex, yIndex), out var cell) && cell.Value.HasValue)
                {
                    var val = cell.Value.Value;
                    var cellFill = GetColor(val);
                    var datum = cell.Datum;
                    builder.AppendLine($"<rect class='plot-heat-cell' x='{N(cellX)}' y='{N(cellY)}' width='{N(w)}' height='{N(h)}' fill='{Esc(cellFill)}'{strokeAttr} data-row-index='{datum.RowIndex}'><title>{Esc(xName)} / {Esc(yName)}: {N(val)}</title></rect>");
                    if (showLabels)
                    {
                        var valText = N(val);
                        RenderDataLabelBackground(builder, plan, datum.RowIndex, cellX + cellWidth / 2m, cellY + cellHeight / 2m + 4m, "middle", 9m, valText);
                        var ratio = maximum <= minimum ? 1m : Math.Clamp((val - minimum) / (maximum - minimum), 0m, 1m);
                        builder.AppendLine($"<text x='{N(cellX + cellWidth / 2m)}' y='{N(cellY + cellHeight / 2m + 4m)}' text-anchor='middle' font-size='9' fill='{(ratio > .55m ? "white" : "#1f2937")}'>{valText}</text>");
                    }
                }
                else
                {
                    var rowIndexAttr = cell.Datum is not null ? $" data-row-index='{cell.Datum.RowIndex}'" : string.Empty;
                    builder.AppendLine($"<rect class='plot-heat-cell plot-heat-cell-null' x='{N(cellX)}' y='{N(cellY)}' width='{N(w)}' height='{N(h)}' fill='{Esc(nullColor)}'{strokeAttr}{rowIndexAttr}><title>{Esc(xName)} / {Esc(yName)}: (null)</title></rect>");
                }
            }
        }

        for (var index = 0; index < xCategories.Length; index++)
            builder.AppendLine($"<text x='{N(Left + (index + .5m) * cellWidth)}' y='{N(Top + plotHeight + 16m)}' text-anchor='middle' font-size='9' fill='#666'>{Esc(Truncate(xCategories[index], 12))}</text>");
        for (var index = 0; index < yCategories.Length; index++)
            builder.AppendLine($"<text x='{N(Left - 6m)}' y='{N(Top + (index + .5m) * cellHeight + 4m)}' text-anchor='end' font-size='9' fill='#666'>{Esc(Truncate(yCategories[index], 12))}</text>");
    }

    private static void RenderBoxPlot(StringBuilder builder, PlotPlan plan)
    {
        var layer = plan.Layers.First();
        var scale = plan.Scales.First(item => item.Channel == FieldChannel.Y);
        var categories = plan.Scales.First(item => item.Channel == FieldChannel.X).Categories;
        var plotWidth = plan.Bounds.Width - Left - Right;
        var plotHeight = plan.Bounds.Height - Top - Bottom;
        var slot = plotWidth / Math.Max(1, categories.Length);
        builder.AppendLine($"<line x1='{N(Left)}' y1='{N(Top)}' x2='{N(Left)}' y2='{N(Top + plotHeight)}' stroke='#bbb'/><line x1='{N(Left)}' y1='{N(Top + plotHeight)}' x2='{N(Left + plotWidth)}' y2='{N(Top + plotHeight)}' stroke='#bbb'/>");
        foreach (var tick in scale.Ticks)
        {
            var y = MapY(PlotPlanResolver.Number(tick.Value) ?? 0m, scale, plotHeight);
            builder.AppendLine($"<line x1='{N(Left)}' y1='{N(y)}' x2='{N(Left + plotWidth)}' y2='{N(y)}' stroke='#e5e7eb'/><text x='{N(Left - 6m)}' y='{N(y + 4m)}' text-anchor='end' font-size='9' fill='#666'>{Esc(tick.Label)}</text>");
        }
        foreach (var datum in layer.Data)
        {
            var low = Numeric(datum, FieldChannel.Low); var q1 = Numeric(datum, FieldChannel.Q1);
            var median = Numeric(datum, FieldChannel.Median); var q3 = Numeric(datum, FieldChannel.Q3); var high = Numeric(datum, FieldChannel.High);
            if (low is null || q1 is null || median is null || q3 is null || high is null) continue;
            var category = DisplayChannel(datum, FieldChannel.X);
            var categoryIndex = category is null ? -1 : categories.IndexOf(category);
            if (categoryIndex < 0) continue;
            var x = Left + slot * (categoryIndex + .5m);
            var boxWidth = slot * .48m;
            var lowY = MapY(low.Value, scale, plotHeight); var q1Y = MapY(q1.Value, scale, plotHeight);
            var medianY = MapY(median.Value, scale, plotHeight); var q3Y = MapY(q3.Value, scale, plotHeight); var highY = MapY(high.Value, scale, plotHeight);
            var boxColor = SafePaint(Style(plan, "COLOR:" + category), "#93c5fd");
            var borderColor = SafePaint(Style(plan, "COLOR:" + category), "#2563eb");
            builder.AppendLine($"<g data-row-index='{datum.RowIndex}'><title>{Esc(category!)}: {N(low.Value)}, {N(q1.Value)}, {N(median.Value)}, {N(q3.Value)}, {N(high.Value)}</title><line x1='{N(x)}' y1='{N(highY)}' x2='{N(x)}' y2='{N(lowY)}' stroke='#374151'/><line x1='{N(x - boxWidth / 4m)}' y1='{N(highY)}' x2='{N(x + boxWidth / 4m)}' y2='{N(highY)}' stroke='#374151'/><line x1='{N(x - boxWidth / 4m)}' y1='{N(lowY)}' x2='{N(x + boxWidth / 4m)}' y2='{N(lowY)}' stroke='#374151'/><rect x='{N(x - boxWidth / 2m)}' y='{N(q3Y)}' width='{N(boxWidth)}' height='{N(Math.Max(1m, q1Y - q3Y))}' fill='{Esc(boxColor)}' stroke='{Esc(borderColor)}'/><line x1='{N(x - boxWidth / 2m)}' y1='{N(medianY)}' x2='{N(x + boxWidth / 2m)}' y2='{N(medianY)}' stroke='#1e3a8a' stroke-width='2'/></g>");
            builder.AppendLine($"<text x='{N(x)}' y='{N(Top + plotHeight + 16m)}' text-anchor='middle' font-size='9' fill='#666'>{Esc(Truncate(category!, 12))}</text>");
        }
        var xTitle = Style(plan, "axis:x:label");
        var yTitle = Style(plan, "axis:y:label");
        if (!string.IsNullOrWhiteSpace(xTitle))
            builder.AppendLine($"<text x='{N(Left + plotWidth / 2m)}' y='{N(plan.Bounds.Height - 8m)}' text-anchor='middle' font-size='10' fill='#444'>{Esc(xTitle)}</text>");
        if (!string.IsNullOrWhiteSpace(yTitle))
            builder.AppendLine($"<text x='12' y='{N(Top + plotHeight / 2m)}' text-anchor='middle' font-size='10' fill='#444' transform='rotate(-90 12 {N(Top + plotHeight / 2m)})'>{Esc(yTitle)}</text>");
    }

    private static void RenderWaterfall(StringBuilder builder, PlotPlan plan)
    {
        var layer = plan.Layers.First();
        var scale = plan.Scales.First(item => item.Channel == FieldChannel.Y);
        var categories = plan.Scales.First(item => item.Channel == FieldChannel.X).Categories;
        var plotWidth = plan.Bounds.Width - Left - Right;
        var plotHeight = plan.Bounds.Height - Top - Bottom;
        var slot = plotWidth / Math.Max(1, layer.Data.Length);
        decimal? previousEndY = null;
        for (var index = 0; index < layer.Data.Length; index++)
        {
            var datum = layer.Data[index];
            var start = Numeric(datum, FieldChannel.YStart); var end = Numeric(datum, FieldChannel.YEnd);
            if (start is null || end is null) continue;
            var startY = MapY(start.Value, scale, plotHeight); var endY = MapY(end.Value, scale, plotHeight);
            var x = Left + slot * index + slot * .15m; var width = slot * .7m;
            var totalText = DisplayChannel(datum, FieldChannel.Detail);
            var total = Math.Abs(start.Value) < .000001m && totalText is not null &&
                (totalText.Equals("true", StringComparison.OrdinalIgnoreCase) || totalText == "1");
            var color = total ? "#2980b9" : end.Value >= start.Value ? "#27ae60" : "#e74c3c";
            if (previousEndY.HasValue) builder.AppendLine($"<line x1='{N(x - slot * .15m)}' y1='{N(previousEndY.Value)}' x2='{N(x)}' y2='{N(previousEndY.Value)}' stroke='#9ca3af' stroke-dasharray='3 2'/>");
            builder.AppendLine($"<rect x='{N(x)}' y='{N(Math.Min(startY, endY))}' width='{N(width)}' height='{N(Math.Max(1m, Math.Abs(startY - endY)))}' fill='{color}' data-row-index='{datum.RowIndex}'><title>{Esc(categories[index])}: {N(end.Value - start.Value)}</title></rect>");
            builder.AppendLine($"<text x='{N(x + width / 2m)}' y='{N(Top + plotHeight + 16m)}' text-anchor='middle' font-size='9' fill='#666'>{Esc(Truncate(categories[index], 10))}</text>");
            previousEndY = endY;
        }
    }

    private static void RenderCandlestick(StringBuilder builder, PlotPlan plan)
    {
        var layer = plan.Layers.First();
        var scale = plan.Scales.First(item => item.Channel == FieldChannel.Y);
        var categories = plan.Scales.First(item => item.Channel == FieldChannel.X).Categories;
        var plotWidth = plan.Bounds.Width - Left - Right;
        var plotHeight = plan.Bounds.Height - Top - Bottom;
        var slot = plotWidth / Math.Max(1, layer.Data.Length);
        for (var index = 0; index < layer.Data.Length; index++)
        {
            var datum = layer.Data[index];
            var open = Numeric(datum, FieldChannel.Open); var close = Numeric(datum, FieldChannel.Close);
            var low = Numeric(datum, FieldChannel.Low); var high = Numeric(datum, FieldChannel.High);
            if (open is null || close is null || low is null || high is null) continue;
            var x = Left + slot * (index + .5m); var bodyWidth = slot * .55m;
            var openY = MapY(open.Value, scale, plotHeight); var closeY = MapY(close.Value, scale, plotHeight);
            var lowY = MapY(low.Value, scale, plotHeight); var highY = MapY(high.Value, scale, plotHeight);
            var rising = close.Value >= open.Value;
            var color = rising ? SafePaint(Style(plan, "COLOR_UP"), "#26a69a") : SafePaint(Style(plan, "COLOR_DOWN"), "#ef5350");
            builder.AppendLine($"<g data-row-index='{datum.RowIndex}'><title>{Esc(categories[index])}: O {N(open.Value)}, H {N(high.Value)}, L {N(low.Value)}, C {N(close.Value)}</title><line x1='{N(x)}' y1='{N(highY)}' x2='{N(x)}' y2='{N(lowY)}' stroke='{Esc(color)}'/><rect x='{N(x - bodyWidth / 2m)}' y='{N(Math.Min(openY, closeY))}' width='{N(bodyWidth)}' height='{N(Math.Max(1m, Math.Abs(openY - closeY)))}' fill='{Esc(color)}' stroke='{Esc(color)}'/></g>");
            builder.AppendLine($"<text x='{N(x)}' y='{N(Top + plotHeight + 16m)}' text-anchor='middle' font-size='9' fill='#666'>{Esc(Truncate(categories[index], 10))}</text>");
        }
    }

    private static void RenderGantt(StringBuilder builder, PlotPlan plan)
    {
        var data = plan.Layers.SelectMany(layer => layer.Data).Where(datum => !datum.IsGap)
            .GroupBy(datum => datum.RowIndex).Select(group => group.First()).OrderBy(datum => datum.RowIndex).ToList();
        var xScale = plan.Scales.First(scale => scale.Channel == FieldChannel.X);
        var yCategories = plan.Scales.First(scale => scale.Channel == FieldChannel.Y).Categories;
        var temporal = xScale.Domain.Select(TemporalNumber).Where(value => value.HasValue).Select(value => value!.Value).ToList();
        if (temporal.Count == 0 || yCategories.IsDefaultOrEmpty) return;
        var minimum = temporal.Min(); var maximum = temporal.Max();
        if (maximum <= minimum) maximum = minimum + 1m;
        var plotWidth = plan.Bounds.Width - Left - Right;
        var plotHeight = plan.Bounds.Height - Top - Bottom;
        var slot = plotHeight / yCategories.Length;
        var tasks = new Dictionary<string, (decimal StartX, decimal EndX, decimal Y)>(StringComparer.OrdinalIgnoreCase);
        builder.AppendLine("<defs><marker id='gantt-arrow' markerWidth='7' markerHeight='7' refX='6' refY='3.5' orient='auto'><path d='M0,0 L7,3.5 L0,7 Z' fill='#6b7280'/></marker></defs>");
        foreach (var datum in data)
        {
            var label = DisplayChannel(datum, FieldChannel.Y) ?? $"Task {datum.RowIndex + 1}";
            var start = TemporalNumber(Channel(datum, FieldChannel.X));
            var end = TemporalNumber(Channel(datum, FieldChannel.X2));
            var row = yCategories.IndexOf(label);
            if (!start.HasValue || !end.HasValue || row < 0) continue;
            var startX = Left + (start.Value - minimum) / (maximum - minimum) * plotWidth;
            var endX = Left + (end.Value - minimum) / (maximum - minimum) * plotWidth;
            var y = Top + slot * (row + .5m);
            var color = SafePaint(DisplayChannel(datum, FieldChannel.Color), "#5470c6");
            var milestone = DisplayChannel(datum, FieldChannel.Shape) is { } flag && (flag == "1" || flag.Equals("true", StringComparison.OrdinalIgnoreCase));
            if (milestone || Math.Abs(endX - startX) < 1m)
            {
                var size = Math.Min(9m, slot * .32m);
                builder.AppendLine($"<path d='M {N(startX)} {N(y - size)} L {N(startX + size)} {N(y)} L {N(startX)} {N(y + size)} L {N(startX - size)} {N(y)} Z' fill='{Esc(color)}' data-row-index='{datum.RowIndex}'><title>{Esc(label)}</title></path>");
            }
            else
            {
                var height = slot * .58m;
                builder.AppendLine($"<rect x='{N(Math.Min(startX, endX))}' y='{N(y - height / 2m)}' width='{N(Math.Max(2m, Math.Abs(endX - startX)))}' height='{N(height)}' rx='2' fill='{Esc(color)}' data-row-index='{datum.RowIndex}'><title>{Esc(label)}: {Esc(DisplayChannel(datum, FieldChannel.X) ?? "")} – {Esc(DisplayChannel(datum, FieldChannel.X2) ?? "")}</title></rect>");
                if (Numeric(datum, FieldChannel.Size) is { } progress)
                    builder.AppendLine($"<rect x='{N(Math.Min(startX, endX))}' y='{N(y - height / 2m)}' width='{N(Math.Max(0m, Math.Abs(endX - startX) * Math.Clamp(progress / 100m, 0m, 1m)))}' height='{N(height)}' rx='2' fill='#111827' fill-opacity='.28'/>");
            }
            builder.AppendLine($"<text x='{N(Left - 6m)}' y='{N(y + 4m)}' text-anchor='end' font-size='9' fill='#4b5563'>{Esc(Truncate(label, 16))}</text>");
            tasks[label] = (startX, endX, y);
        }
        foreach (var datum in data)
        {
            var label = DisplayChannel(datum, FieldChannel.Y);
            var predecessor = DisplayChannel(datum, FieldChannel.Detail);
            if (label is null || predecessor is null || !tasks.TryGetValue(label, out var target) || !tasks.TryGetValue(predecessor, out var source)) continue;
            var elbow = Math.Min(target.StartX - 4m, source.EndX + 12m);
            builder.AppendLine($"<path d='M {N(source.EndX)} {N(source.Y)} L {N(elbow)} {N(source.Y)} L {N(elbow)} {N(target.Y)} L {N(target.StartX)} {N(target.Y)}' fill='none' stroke='#6b7280' stroke-width='1.5' marker-end='url(#gantt-arrow)'/>");
        }
    }

    private static void RenderRadar(StringBuilder builder, PlotPlan plan)
    {
        var dimensions = plan.Scales.First(scale => scale.Channel == FieldChannel.Theta).Categories;
        var radiusScale = plan.Scales.First(scale => scale.Channel == FieldChannel.Radius);
        if (dimensions.Length < 3) return;
        var minimum = PlotPlanResolver.Number(radiusScale.Domain[0]) ?? 0m;
        var maximum = PlotPlanResolver.Number(radiusScale.Domain[^1]) ?? 1m;
        var cx = plan.Bounds.Width / 2m; var cy = plan.Bounds.Height / 2m + 10m;
        var outer = Math.Min(plan.Bounds.Width, plan.Bounds.Height) / 2m - 58m;
        for (var ring = 1; ring <= 4; ring++)
        {
            var ringRadius = outer * ring / 4m;
            var points = Enumerable.Range(0, dimensions.Length)
                .Select(index => Point(cx, cy, ringRadius, -Math.PI / 2d + 2d * Math.PI * index / dimensions.Length));
            builder.AppendLine($"<polygon points='{string.Join(" ", points)}' fill='none' stroke='#d1d5db'/>");
        }
        for (var index = 0; index < dimensions.Length; index++)
        {
            var angle = -Math.PI / 2d + 2d * Math.PI * index / dimensions.Length;
            var edge = PointCoordinates(cx, cy, outer, angle);
            var label = PointCoordinates(cx, cy, outer + 18m, angle);
            builder.AppendLine($"<line x1='{N(cx)}' y1='{N(cy)}' x2='{N(edge.X)}' y2='{N(edge.Y)}' stroke='#e5e7eb'/>");
            builder.AppendLine($"<text x='{N(label.X)}' y='{N(label.Y + 3m)}' text-anchor='middle' font-size='9' fill='#4b5563'>{Esc(Truncate(dimensions[index], 13))}</text>");
        }
        foreach (var layer in plan.Layers)
        {
            var points = layer.Data.Select((datum, index) =>
            {
                var value = Numeric(datum, FieldChannel.Radius) ?? minimum;
                var ratio = maximum <= minimum ? 0m : Math.Clamp((value - minimum) / (maximum - minimum), 0m, 1m);
                return Point(cx, cy, outer * ratio, -Math.PI / 2d + 2d * Math.PI * index / dimensions.Length);
            }).ToArray();
            var color = plan.Palette.FirstOrDefault(item => item.SeriesKey == layer.SeriesKey)?.Color ?? "#5470c6";
            builder.AppendLine($"<polygon points='{string.Join(" ", points)}' fill='{Esc(color)}' fill-opacity='.18' stroke='{Esc(color)}' stroke-width='{LineWidth(layer, "2")}' data-row-index='{layer.Data.FirstOrDefault()?.RowIndex ?? 0}'><title>{Esc(layer.SeriesKey ?? layer.Id)}</title></polygon>");
        }
        RenderLegend(builder, plan);
    }

    private static decimal? TemporalNumber(ChartValue? value) => value?.Kind switch
    {
        ChartValueKind.Date => value.Date?.DayNumber,
        ChartValueKind.Time => value.Time.HasValue ? value.Time.Value.Ticks : null,
        ChartValueKind.LocalDateTime => value.LocalDateTime.HasValue ? value.LocalDateTime.Value.Ticks : null,
        ChartValueKind.OffsetDateTime => value.OffsetDateTime.HasValue ? value.OffsetDateTime.Value.UtcTicks : null,
        ChartValueKind.Integer => value.Integer,
        ChartValueKind.FloatingPoint => (decimal?)value.FloatingPoint,
        ChartValueKind.Decimal => value.Decimal,
        ChartValueKind.Text when DateTimeOffset.TryParse(value.Text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) => parsed.UtcTicks,
        _ => null
    };

    private static decimal? PositionNumber(ChartValue? value) => value is null
        ? null
        : PlotPlanResolver.Number(value) ?? TemporalNumber(value);

    private static decimal? Numeric(ResolvedDatum datum, FieldChannel channel) =>
        PlotPlanResolver.Number(Channel(datum, channel) ?? ChartValue.Null());

    private static string ArcPath(decimal cx, decimal cy, decimal radius, double start, double end)
    {
        var first = PointCoordinates(cx, cy, radius, start);
        var last = PointCoordinates(cx, cy, radius, end);
        var large = Math.Abs(end - start) > Math.PI ? 1 : 0;
        var sweep = end >= start ? 1 : 0;
        return $"M {N(first.X)} {N(first.Y)} A {N(radius)} {N(radius)} 0 {large} {sweep} {N(last.X)} {N(last.Y)}";
    }

    private static decimal NormalizePointRadius(decimal? value, decimal minimum, decimal maximum, decimal minRadius = 4m, decimal maxRadius = 22m)
    {
        if (!value.HasValue) return minRadius;
        if (maximum <= minimum) return (minRadius + maxRadius) / 2m;
        return minRadius + ((value.Value - minimum) / (maximum - minimum) * (maxRadius - minRadius));
    }

    private static void RenderRule(StringBuilder builder, ResolvedMarkLayer layer, in CartesianPlotArea area,
        ResolvedScale? xScale, ResolvedScale? yScale, ICollection<OverlayLabel> overlayLabels)
    {
        var xVal = layer.Data.Select(datum => Channel(datum, FieldChannel.X)).FirstOrDefault(item => item is not null && item.Kind != ChartValueKind.Null);
        var yVal = layer.Data.Select(datum => Channel(datum, FieldChannel.Y)).FirstOrDefault(item => item is not null && item.Kind != ChartValueKind.Null);
        var color = SafePaint(LayerStyle(layer, "color") ?? LayerStyle(layer, "fill") ?? LayerStyle(layer, "stroke"), "#888888");
        var label = LayerStyle(layer, "label");
        var strokeWidth = LayerStyle(layer, "stroke_width") ?? LayerStyle(layer, "width") ?? "2";
        var dashAttributes = LineStyleAttributes(LayerStyle(layer, "lineStyle"));

        var ranged = false;
        for (var index = 0; index < layer.Data.Length; index++)
        {
            var datum = layer.Data[index];
            var yStart = PositionNumber(Channel(datum, FieldChannel.YStart));
            var yEnd = PositionNumber(Channel(datum, FieldChannel.YEnd));
            var xStart = PositionNumber(Channel(datum, FieldChannel.XStart) ?? Channel(datum, FieldChannel.X));
            var xEnd = PositionNumber(Channel(datum, FieldChannel.XEnd) ?? Channel(datum, FieldChannel.X2));
            if (xStart.HasValue && xEnd.HasValue && yStart.HasValue && yEnd.HasValue && xScale is not null && yScale is not null)
            {
                builder.AppendLine($"<line class='plot-range-rule' x1='{N(MapX(xStart.Value, xScale, area))}' y1='{N(MapY(yStart.Value, yScale, area.Height))}' x2='{N(MapX(xEnd.Value, xScale, area))}' y2='{N(MapY(yEnd.Value, yScale, area.Height))}' stroke='{Esc(color)}' stroke-width='{Esc(strokeWidth)}'{dashAttributes} data-row-index='{datum.RowIndex}'/>");
                ranged = true;
                continue;
            }
            if (yStart.HasValue && yEnd.HasValue && yScale is not null)
            {
                var x = CategoryX(index, Math.Max(1, layer.Data.Length), area, xScale);
                builder.AppendLine($"<line class='plot-range-rule' x1='{N(x)}' y1='{N(MapY(yStart.Value, yScale, area.Height))}' x2='{N(x)}' y2='{N(MapY(yEnd.Value, yScale, area.Height))}' stroke='{Esc(color)}' stroke-width='{Esc(strokeWidth)}'{dashAttributes} data-row-index='{datum.RowIndex}'/>");
                ranged = true;
            }
            if (xStart.HasValue && xEnd.HasValue && xScale is not null)
            {
                var y = area.Top + area.Height * (index + .5m) / Math.Max(1, layer.Data.Length);
                builder.AppendLine($"<line class='plot-range-rule' x1='{N(MapX(xStart.Value, xScale, area))}' y1='{N(y)}' x2='{N(MapX(xEnd.Value, xScale, area))}' y2='{N(y)}' stroke='{Esc(color)}' stroke-width='{Esc(strokeWidth)}'{dashAttributes} data-row-index='{datum.RowIndex}'/>");
                ranged = true;
            }
        }
        if (ranged) return;

        var overlayType = LayerStyle(layer, "overlayType");
        var ruleClass = overlayType == "ReferenceLine"
            ? " class='plot-reference-line'"
            : string.Empty;

        if (yVal is not null && yScale is not null)
        {
            var yNum = PlotPlanResolver.Number(yVal);
            if (yNum.HasValue)
            {
                var y = MapY(yNum.Value, yScale, area.Height);
                builder.AppendLine($"<line{ruleClass} x1='{N(area.Left)}' y1='{N(y)}' x2='{N(area.Right)}' y2='{N(y)}' stroke='{Esc(color)}' stroke-width='{Esc(strokeWidth)}'{dashAttributes}/>");
                if (!string.IsNullOrWhiteSpace(label))
                    overlayLabels.Add(new OverlayLabel(area.Right, y, label, color, layer.ZIndex));
            }
        }
        else if (xVal is not null && xScale is not null)
        {
            var xNum = PlotPlanResolver.Number(xVal);
            if (xNum.HasValue)
            {
                var x = MapX(xNum.Value, xScale, area);
                builder.AppendLine($"<line{ruleClass} x1='{N(x)}' y1='{N(area.Top)}' x2='{N(x)}' y2='{N(area.Bottom)}' stroke='{Esc(color)}' stroke-width='{Esc(strokeWidth)}'{dashAttributes}/>");
            }
        }
    }

    private static void RenderReferenceBand(StringBuilder builder, ResolvedMarkLayer layer,
        in CartesianPlotArea area, ResolvedScale? scale, ICollection<OverlayLabel> overlayLabels)
    {
        if (scale is null || layer.Data.IsDefaultOrEmpty) return;
        var low = PositionNumber(Channel(layer.Data[0], FieldChannel.YStart));
        var high = PositionNumber(Channel(layer.Data[0], FieldChannel.YEnd));
        if (!low.HasValue || !high.HasValue) return;
        var firstY = MapY(low.Value, scale, area.Height);
        var secondY = MapY(high.Value, scale, area.Height);
        var top = Math.Min(firstY, secondY);
        var height = Math.Max(1m, Math.Abs(secondY - firstY));
        var color = SafePaint(LayerStyle(layer, "color"), "#94a3b8");
        builder.AppendLine($"<rect class='plot-reference-band' x='{N(area.Left)}' y='{N(top)}' width='{N(area.Width)}' height='{N(height)}' fill='{Esc(color)}' fill-opacity='.18'><title>{N(low.Value)} to {N(high.Value)}</title></rect>");
        var label = LayerStyle(layer, "label");
        if (!string.IsNullOrWhiteSpace(label))
            overlayLabels.Add(new OverlayLabel(area.Right, top + height / 2m, label, color, layer.ZIndex));
    }

    private static void RenderTransposedReferenceBand(StringBuilder builder, ResolvedMarkLayer layer,
        ResolvedScale scale, decimal plotWidth, decimal plotHeight, ICollection<OverlayLabel> overlayLabels)
    {
        if (layer.Data.IsDefaultOrEmpty) return;
        var low = PositionNumber(Channel(layer.Data[0], FieldChannel.YStart));
        var high = PositionNumber(Channel(layer.Data[0], FieldChannel.YEnd));
        if (!low.HasValue || !high.HasValue) return;
        var firstX = MapHorizontal(low.Value, scale, plotWidth);
        var secondX = MapHorizontal(high.Value, scale, plotWidth);
        var left = Math.Min(firstX, secondX);
        var width = Math.Max(1m, Math.Abs(secondX - firstX));
        var color = SafePaint(LayerStyle(layer, "color"), "#94a3b8");
        builder.AppendLine($"<rect class='plot-reference-band' x='{N(left)}' y='{N(Top)}' width='{N(width)}' height='{N(plotHeight)}' fill='{Esc(color)}' fill-opacity='.18'><title>{N(low.Value)} to {N(high.Value)}</title></rect>");
        var label = LayerStyle(layer, "label");
        if (!string.IsNullOrWhiteSpace(label))
            overlayLabels.Add(new OverlayLabel(secondX, Top + 15m, label, color, layer.ZIndex));
    }

    private sealed record PositionedSideLabel(
        string Kind,
        string SeriesKey,
        string FullLabel,
        decimal EndpointX,
        decimal EndpointY,
        string Color,
        int Order,
        decimal PreferredY,
        decimal TargetY
    );

    private static void RenderSideLabels(
        StringBuilder builder,
        CartesianPlotArea area,
        IReadOnlyCollection<OverlayLabel> overlayLabels,
        IReadOnlyCollection<SeriesLabelPlacement> seriesLabels,
        decimal sideLabelsGutter,
        decimal sideLabelsRight)
    {
        var areaTop = area.Top;
        var areaBottom = area.Bottom;
        var areaLeft = area.Left;

        var startLabels = seriesLabels.Where(s => s.PreferredSide == "START")
            .OrderBy(s => s.EndpointY)
            .ThenBy(s => s.Order)
            .ToList();

        if (startLabels.Count > 0)
        {
            const decimal lineHeight = 18m;
            var positionedStart = startLabels
                .Select(s => (Placement: s, Y: Math.Clamp(s.EndpointY, areaTop + 10m, areaBottom - 4m)))
                .ToList();
            for (var i = 1; i < positionedStart.Count; i++)
            {
                if (positionedStart[i].Y < positionedStart[i - 1].Y + lineHeight)
                    positionedStart[i] = (positionedStart[i].Placement, positionedStart[i - 1].Y + lineHeight);
            }
            var overflowStart = positionedStart[^1].Y - (areaBottom - 4m);
            if (overflowStart > 0m)
            {
                for (var i = 0; i < positionedStart.Count; i++)
                    positionedStart[i] = (positionedStart[i].Placement, positionedStart[i].Y - overflowStart);
            }

            foreach (var placed in positionedStart)
            {
                var item = placed.Placement;
                var y = placed.Y;
                const decimal charWidth = 6.0m;
                var labelX = item.EndpointX - 7m;
                var availableWidth = Math.Max(20m, labelX - 2m);
                var fullWidth = item.FullLabel.Length * charWidth;
                string displayText;
                decimal textWidth;
                if (fullWidth <= availableWidth)
                {
                    displayText = item.FullLabel;
                    textWidth = fullWidth;
                }
                else
                {
                    var maxChars = Math.Max(3, (int)((availableWidth - 8m) / charWidth));
                    displayText = item.FullLabel.Length > maxChars ? item.FullLabel[..maxChars] + "…" : item.FullLabel;
                    textWidth = Math.Min(availableWidth, displayText.Length * charWidth);
                }
                var x = Math.Max(2m + textWidth, labelX);
                if (x >= item.EndpointX) x = item.EndpointX - 2m;
                builder.AppendLine($"<text class='plot-series-label' data-series='{Esc(item.SeriesKey)}' data-series-label='{Esc(item.FullLabel)}' title='{Esc(item.FullLabel)}' x='{N(x)}' y='{N(y + 3m)}' text-anchor='end' font-size='9' font-weight='600' fill='{Esc(item.Color)}'>{Esc(displayText)}</text>");
            }
        }

        var rightItems = new List<PositionedSideLabel>();
        foreach (var ol in overlayLabels)
        {
            rightItems.Add(new PositionedSideLabel(
                "Overlay",
                string.Empty,
                ol.Text,
                ol.EndpointX,
                ol.EndpointY,
                ol.Color,
                ol.ZIndex,
                ol.EndpointY,
                Math.Clamp(ol.EndpointY, areaTop + 10m, areaBottom - 4m)
            ));
        }
        foreach (var sl in seriesLabels.Where(s => s.PreferredSide == "END"))
        {
            rightItems.Add(new PositionedSideLabel(
                "Series",
                sl.SeriesKey,
                sl.FullLabel,
                sl.EndpointX,
                sl.EndpointY,
                sl.Color,
                sl.Order,
                sl.EndpointY,
                Math.Clamp(sl.EndpointY, areaTop + 10m, areaBottom - 4m)
            ));
        }

        if (rightItems.Count == 0) return;

        rightItems = rightItems.OrderBy(item => item.PreferredY)
            .ThenBy(item => item.Kind)
            .ThenBy(item => item.Order)
            .ToList();

        const decimal spacing = 18m;
        for (var i = 1; i < rightItems.Count; i++)
        {
            var minimumY = rightItems[i - 1].TargetY + spacing;
            if (rightItems[i].TargetY < minimumY)
                rightItems[i] = rightItems[i] with { TargetY = minimumY };
        }
        var overflowRight = rightItems[^1].TargetY - (areaBottom - 4m);
        if (overflowRight > 0m)
        {
            for (var i = 0; i < rightItems.Count; i++)
                rightItems[i] = rightItems[i] with { TargetY = rightItems[i].TargetY - overflowRight };
        }

        var maxAllowedRightWidth = Math.Max(0m, sideLabelsGutter - 14m);
        foreach (var item in rightItems)
        {
            if (item.Kind == "Overlay")
            {
                var labelX = item.EndpointX + 10m;
                const decimal overlayCharWidth = 5.4m;
                var availableTextWidth = Math.Max(0m, maxAllowedRightWidth - 8m);
                var fullTextWidth = item.FullLabel.Length * overlayCharWidth;
                var displayText = item.FullLabel;
                if (fullTextWidth > availableTextWidth)
                {
                    var maxChars = Math.Max(0, (int)(availableTextWidth / overlayCharWidth) - 1);
                    displayText = maxChars == 0
                        ? "…"
                        : item.FullLabel[..Math.Min(item.FullLabel.Length, maxChars)] + "…";
                }
                var labelWidth = Math.Min(maxAllowedRightWidth, displayText.Length * overlayCharWidth + 8m);
                var centerY = item.TargetY - 3.5m;
                builder.AppendLine($"<path class='plot-overlay-label-leader' d='M {N(item.EndpointX + 3m)} {N(item.EndpointY)} L {N(labelX - 3m)} {N(centerY)}' fill='none' stroke='{Esc(item.Color)}' stroke-width='1'/>");
                builder.AppendLine($"<rect class='plot-overlay-label-bg' x='{N(labelX - 3m)}' y='{N(item.TargetY - 11m)}' width='{N(labelWidth)}' height='14' rx='2' fill='white' fill-opacity='.94'/>");
                builder.AppendLine($"<text class='plot-overlay-label' title='{Esc(item.FullLabel)}' x='{N(labelX)}' y='{N(item.TargetY)}' text-anchor='start' font-size='9' font-weight='600' fill='{Esc(item.Color)}'>{Esc(displayText)}</text>");
            }
            else
            {
                const decimal charWidth = 6.0m;
                var labelX = item.EndpointX + 7m;
                var availableWidth = Math.Max(0m, sideLabelsRight - labelX - 2m);
                var fullWidth = item.FullLabel.Length * charWidth;
                string displayText;
                decimal textWidth;
                if (fullWidth <= availableWidth)
                {
                    displayText = item.FullLabel;
                    textWidth = fullWidth;
                }
                else
                {
                    var maxChars = Math.Max(0, (int)(availableWidth / charWidth) - 1);
                    displayText = maxChars == 0
                        ? "…"
                        : item.FullLabel[..Math.Min(item.FullLabel.Length, maxChars)] + "…";
                    textWidth = Math.Min(availableWidth, displayText.Length * charWidth);
                }
                if (labelX + textWidth > sideLabelsRight - 2m)
                    labelX = Math.Max(item.EndpointX + 2m, sideLabelsRight - textWidth - 2m);
                builder.AppendLine($"<text class='plot-series-label' data-series='{Esc(item.SeriesKey)}' data-series-label='{Esc(item.FullLabel)}' title='{Esc(item.FullLabel)}' x='{N(labelX)}' y='{N(item.TargetY + 3m)}' text-anchor='start' font-size='9' font-weight='600' fill='{Esc(item.Color)}'>{Esc(displayText)}</text>");
            }
        }
    }

    private static void RenderOverlayLabels(StringBuilder builder, IReadOnlyCollection<OverlayLabel> labels, decimal plotHeight)
    {
        if (labels.Count == 0) return;
        const decimal lineHeight = 15m;
        var positioned = labels.OrderBy(label => label.EndpointY).ThenBy(label => label.ZIndex)
            .Select(label => new PositionedOverlayLabel(label,
                Math.Clamp(label.EndpointY - 7m, Top + 10m, Top + plotHeight - 3m)))
            .ToList();
        for (var index = 1; index < positioned.Count; index++)
        {
            var minimumY = positioned[index - 1].Y + lineHeight;
            if (positioned[index].Y < minimumY) positioned[index] = positioned[index] with { Y = minimumY };
        }
        var overflow = positioned[^1].Y - (Top + plotHeight - 3m);
        if (overflow > 0m)
            for (var index = 0; index < positioned.Count; index++)
                positioned[index] = positioned[index] with { Y = positioned[index].Y - overflow };

        foreach (var item in positioned)
        {
            var labelX = item.Label.EndpointX + 10m;
            var labelWidth = Math.Min(150m, item.Label.Text.Length * 5.4m + 8m);
            var centerY = item.Y - 3.5m;
            builder.AppendLine($"<path class='plot-overlay-label-leader' d='M {N(item.Label.EndpointX + 3m)} {N(item.Label.EndpointY)} L {N(labelX - 3m)} {N(centerY)}' fill='none' stroke='{Esc(item.Label.Color)}' stroke-width='1'/>");
            builder.AppendLine($"<rect class='plot-overlay-label-bg' x='{N(labelX - 3m)}' y='{N(item.Y - 11m)}' width='{N(labelWidth)}' height='14' rx='2' fill='white' fill-opacity='.94'/>");
            builder.AppendLine($"<text class='plot-overlay-label' x='{N(labelX)}' y='{N(item.Y)}' text-anchor='start' font-size='9' font-weight='600' fill='{Esc(item.Label.Color)}'>{Esc(item.Label.Text)}</text>");
        }
    }

    private static string LineStyleAttributes(string? lineStyle) => lineStyle?.ToUpperInvariant() switch
    {
        "DOTTED" => " stroke-dasharray='1 5'",
        "DASHED" => " stroke-dasharray='7 5'",
        _ => string.Empty
    };

    private static bool AxisLineEnabled(PlotPlan plan, string axis)
    {
        var value = Style(plan, $"axis:{axis}:axis_line");
        return value is null || !value.Equals("OFF", StringComparison.OrdinalIgnoreCase) &&
            !value.Equals("FALSE", StringComparison.OrdinalIgnoreCase) && value != "0";
    }

    private static void RenderGridLine(StringBuilder builder, PlotPlan plan,
        decimal x1, decimal y1, decimal x2, decimal y2, bool minor = false)
    {
        var color = SafePaint(Style(plan, "GRID_LINE_COLOR"), "#e5e7eb");
        var width = SafeLineWidth(Style(plan, "GRID_LINE_WIDTH"), "1");
        var dash = DashAttribute(Style(plan, "GRID_LINE_DASH"));
        var opacity = minor ? " stroke-opacity='.55'" : string.Empty;
        builder.AppendLine($"<line class='{(minor ? "plot-minor-grid-line" : "plot-grid-line")}' x1='{N(x1)}' y1='{N(y1)}' x2='{N(x2)}' y2='{N(y2)}' stroke='{Esc(color)}' stroke-width='{width}'{dash}{opacity}/>");
    }

    private static string SafeLineWidth(string? value, string fallback) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var width)
            ? N(Math.Clamp(width, .1m, 10m))
            : fallback;

    private static string DashAttribute(string? value) => LineStyleAttributes(value);

    private static decimal FontSize(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, 7m, 24m)
            : 9m;

    private static void RenderSmartLabels(
        StringBuilder builder,
        PlotPlan plan,
        IReadOnlyCollection<SmartLabel> labels,
        decimal plotWidth,
        decimal plotHeight,
        decimal plotLeft = Left) =>
        RenderSmartLabels(builder, plan, labels, new CartesianPlotArea(plotLeft, Top, plotWidth, plotHeight));

    /// <summary>
    /// Deterministic priority-aware placement shared by explicit TEXT layers and data labels.
    /// Higher-priority author TEXT wins, lower-priority labels try stable offsets, and labels that
    /// cannot fit remain in the SVG description instead of overflowing the viewBox.
    /// </summary>
    private static void RenderSmartLabels(
        StringBuilder builder,
        PlotPlan plan,
        IReadOnlyCollection<SmartLabel> labels,
        in CartesianPlotArea area)
    {
        if (labels.Count == 0) return;
        var occupied = new List<LabelBox>();
        var offsets = new (decimal X, decimal Y)[]
        {
            (0m, -7m), (7m, -13m), (-7m, -13m), (9m, 8m), (-9m, 8m), (0m, 17m)
        };
        foreach (var label in labels
                     .OrderByDescending(item => item.Priority)
                     .ThenBy(item => item.RowIndex)
                     .ThenBy(item => item.Text, StringComparer.Ordinal))
        {
            var labelWidth = Math.Min(160m, Math.Max(8m, label.Text.Length * label.FontSize * .56m));
            var labelHeight = label.FontSize + 4m;
            (decimal X, decimal Y, LabelBox Box)? placed = null;
            foreach (var offset in offsets)
            {
                var x = Math.Clamp(label.X + offset.X, area.Left + labelWidth / 2m, area.Right - labelWidth / 2m);
                var y = Math.Clamp(label.Y + offset.Y, area.Top + labelHeight, area.Bottom - 2m);
                var box = new LabelBox(x - labelWidth / 2m, y - labelHeight, x + labelWidth / 2m, y + 2m);
                if (occupied.All(existing => !Overlaps(box, existing)))
                {
                    placed = (x, y, box);
                    break;
                }
            }

            if (placed is null)
            {
                builder.AppendLine($"<desc class='plot-smart-label-occluded' data-row-index='{label.RowIndex}'>{Esc(label.Text)}</desc>");
                continue;
            }

            occupied.Add(placed.Value.Box);
            var moved = Math.Abs(placed.Value.X - label.X) > 1m || Math.Abs(placed.Value.Y - label.Y) > 8m;
            if (moved && IsEnabled(plan.Style, "DATA_LABELS:LEADER_LINE"))
            {
                var leaderColor = SafePaint(Style(plan, "DATA_LABELS:LEADER_LINE:COLOR"), label.Color);
                var leaderDash = LeaderLineDash(Style(plan, "DATA_LABELS:LEADER_LINE:STYLE"));
                builder.AppendLine($"<path class='plot-smart-label-leader' data-row-index='{label.RowIndex}' d='M {N(label.X)} {N(label.Y)} L {N(placed.Value.X)} {N(placed.Value.Y - label.FontSize / 2m)}' fill='none' stroke='{Esc(leaderColor)}' stroke-width='.75'{leaderDash}/>");
            }
            RenderDataLabelBackground(builder, plan, label.RowIndex, placed.Value.X, placed.Value.Y, "middle", label.FontSize, label.Text);
            builder.AppendLine($"<text class='plot-smart-label' data-row-index='{label.RowIndex}' data-priority='{label.Priority}' x='{N(placed.Value.X)}' y='{N(placed.Value.Y)}' text-anchor='middle' font-size='{N(label.FontSize)}' fill='{Esc(label.Color)}'>{Esc(label.Text)}</text>");
        }
    }

    private static bool IsBoxPlotLayer(ResolvedMarkLayer layer) =>
        layer.Data.Any(datum => Channel(datum, FieldChannel.Q1) is not null || Channel(datum, FieldChannel.Median) is not null || Channel(datum, FieldChannel.Q3) is not null);

    private static bool IsCandlestickLayer(ResolvedMarkLayer layer) =>
        layer.Data.Any(datum => Channel(datum, FieldChannel.Open) is not null || Channel(datum, FieldChannel.Close) is not null);

    private static void RenderBoxPlotLayer(StringBuilder builder, ResolvedMarkLayer layer, ImmutableArray<string> categories,
        in CartesianPlotArea area, ResolvedScale? scale, string color)
    {
        if (scale is null) return;
        var slot = area.Width / Math.Max(1, categories.Length);
        builder.AppendLine($"<g class='plot-boxplot' data-layer='{Esc(layer.Id)}'>");
        foreach (var datum in layer.Data)
        {
            var low = Numeric(datum, FieldChannel.Low); var q1 = Numeric(datum, FieldChannel.Q1);
            var median = Numeric(datum, FieldChannel.Median); var q3 = Numeric(datum, FieldChannel.Q3); var high = Numeric(datum, FieldChannel.High);
            if (low is null || q1 is null || median is null || q3 is null || high is null) continue;
            var category = DisplayChannel(datum, FieldChannel.X);
            var index = category is null ? -1 : categories.IndexOf(category);
            if (index < 0) continue;
            var x = area.Left + slot * (index + .5m); var width = slot * layer.BandSize * .65m;
            var lowY = MapY(low.Value, scale, area.Height); var q1Y = MapY(q1.Value, scale, area.Height);
            var medianY = MapY(median.Value, scale, area.Height); var q3Y = MapY(q3.Value, scale, area.Height); var highY = MapY(high.Value, scale, area.Height);
            builder.AppendLine($"<g data-row-index='{datum.RowIndex}'><title>{Esc(category!)}: low {N(low.Value)}, Q1 {N(q1.Value)}, median {N(median.Value)}, Q3 {N(q3.Value)}, high {N(high.Value)}</title><line x1='{N(x)}' y1='{N(highY)}' x2='{N(x)}' y2='{N(lowY)}' stroke='{Esc(color)}'/><line x1='{N(x - width / 4m)}' y1='{N(highY)}' x2='{N(x + width / 4m)}' y2='{N(highY)}' stroke='{Esc(color)}'/><line x1='{N(x - width / 4m)}' y1='{N(lowY)}' x2='{N(x + width / 4m)}' y2='{N(lowY)}' stroke='{Esc(color)}'/><rect x='{N(x - width / 2m)}' y='{N(q3Y)}' width='{N(width)}' height='{N(Math.Max(1m, q1Y - q3Y))}' fill='{Esc(color)}' fill-opacity='.35' stroke='{Esc(color)}'/><line x1='{N(x - width / 2m)}' y1='{N(medianY)}' x2='{N(x + width / 2m)}' y2='{N(medianY)}' stroke='{Esc(color)}' stroke-width='2'/></g>");
        }
        builder.AppendLine("</g>");
    }

    private static void RenderCandlestickLayer(StringBuilder builder, ResolvedMarkLayer layer, ImmutableArray<string> categories,
        in CartesianPlotArea area, ResolvedScale? scale)
    {
        if (scale is null) return;
        var slot = area.Width / Math.Max(1, categories.Length);
        builder.AppendLine($"<g class='plot-candlestick' data-layer='{Esc(layer.Id)}'>");
        foreach (var datum in layer.Data)
        {
            var open = Numeric(datum, FieldChannel.Open); var close = Numeric(datum, FieldChannel.Close);
            var low = Numeric(datum, FieldChannel.Low); var high = Numeric(datum, FieldChannel.High);
            if (open is null || close is null || low is null || high is null) continue;
            var category = DisplayChannel(datum, FieldChannel.X);
            var index = category is null ? -1 : categories.IndexOf(category);
            if (index < 0) continue;
            var x = area.Left + slot * (index + .5m); var width = slot * layer.BandSize * .55m;
            var openY = MapY(open.Value, scale, area.Height); var closeY = MapY(close.Value, scale, area.Height);
            var lowY = MapY(low.Value, scale, area.Height); var highY = MapY(high.Value, scale, area.Height);
            var color = close.Value >= open.Value ? "#26a69a" : "#ef5350";
            builder.AppendLine($"<g data-row-index='{datum.RowIndex}' data-extent-axis='y'><title>{Esc(category!)}: O {N(open.Value)}, H {N(high.Value)}, L {N(low.Value)}, C {N(close.Value)}</title><line x1='{N(x)}' y1='{N(highY)}' x2='{N(x)}' y2='{N(lowY)}' stroke='{color}'/><rect x='{N(x - width / 2m)}' y='{N(Math.Min(openY, closeY))}' width='{N(width)}' height='{N(Math.Max(1m, Math.Abs(openY - closeY)))}' fill='{color}' stroke='{color}'/></g>");
        }
        builder.AppendLine("</g>");
    }

    private static void RenderGeographic(StringBuilder builder, PlotPlan plan)
    {
        var geography = plan.Geography ?? throw new InvalidOperationException("Geographic PlotPlan has no resolved geometry.");
        var plotWidth = plan.Bounds.Width - Left - Right;
        var plotHeight = plan.Bounds.Height - Top - Bottom;
        var regionRows = plan.Layers.Where(layer => layer.Mark == MarkKind.Rect)
            .SelectMany(layer => layer.Data.Select(datum => (Layer: layer, Datum: datum)))
            .Where(item => !item.Datum.IsGap && !string.IsNullOrWhiteSpace(DisplayChannel(item.Datum, FieldChannel.Region)))
            .GroupBy(item => DisplayChannel(item.Datum, FieldChannel.Region)!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var colorValues = regionRows.Values.Select(item => Number(Channel(item.Datum, FieldChannel.Color)))
            .Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        var colorMin = colorValues.DefaultIfEmpty(0m).Min();
        var colorMax = colorValues.DefaultIfEmpty(1m).Max();

        builder.AppendLine("<g class='plot-geographic-regions'>");
        foreach (var feature in geography.Features)
        {
            regionRows.TryGetValue(feature.Key, out var match);
            var numeric = match.Datum is null ? null : Number(Channel(match.Datum, FieldChannel.Color));
            var fill = numeric.HasValue ? GeographicRamp(numeric.Value, colorMin, colorMax) : "#e5e7eb";
            foreach (var ring in feature.Rings)
            {
                var path = string.Join(" ", ring.Select((point, index) =>
                {
                    var projected = ProjectGeographic(point.Longitude, point.Latitude, geography.Projection, plotWidth, plotHeight);
                    return $"{(index == 0 ? "M" : "L")} {N(projected.X)} {N(projected.Y)}";
                })) + " Z";
                var row = match.Datum is null ? string.Empty : $" data-row-index='{match.Datum.RowIndex}'";
                builder.AppendLine($"<path class='plot-geographic-region'{row} d='{path}' fill='{fill}' stroke='#94a3b8' stroke-width='.45'><title>{Esc(feature.Key)}{(numeric.HasValue ? $": {N(numeric.Value)}" : string.Empty)}</title></path>");
            }
        }
        builder.AppendLine("</g>");

        foreach (var layer in plan.Layers.Where(layer => layer.Mark == MarkKind.Line))
        {
            var routes = layer.Data.Where(datum => !datum.IsGap)
                .GroupBy(datum => DisplayChannel(datum, FieldChannel.Route) ?? string.Empty, StringComparer.Ordinal)
                .Where(group => group.Key.Length > 0).Take(501).ToArray();
            if (routes.Length > 500) throw new InvalidOperationException("Geographic chart exceeds the 500 route limit.");
            foreach (var route in routes)
            {
                var points = route.Select(datum => GeographicDatum(datum, geography.Projection, plotWidth, plotHeight))
                    .Where(point => point.HasValue).Select(point => point!.Value).ToArray();
                if (points.Length < 2) continue;
                var path = string.Join(" ", points.Select((point, index) => $"{(index == 0 ? "M" : "L")} {N(point.X)} {N(point.Y)}"));
                builder.AppendLine($"<path class='plot-geographic-route' data-row-index='{route.First().RowIndex}' d='{path}' fill='none' stroke='{Esc(SafePaint(LayerStyle(layer, "stroke") ?? LayerStyle(layer, "color"), "#2563eb"))}' stroke-width='{LineWidth(layer, "1.5")}'><title>{Esc(route.Key)}</title></path>");
            }
        }

        var labels = new List<SmartLabel>();
        var pointCount = 0;
        foreach (var layer in plan.Layers.Where(layer => layer.Mark is MarkKind.Point or MarkKind.Text))
            foreach (var datum in layer.Data.Where(datum => !datum.IsGap))
            {
                if (++pointCount > 20000) throw new InvalidOperationException("Geographic chart exceeds the 20,000 point/label limit.");
                var point = GeographicDatum(datum, geography.Projection, plotWidth, plotHeight);
                if (!point.HasValue) continue;
                var text = DisplayChannel(datum, FieldChannel.Text) ?? DisplayChannel(datum, FieldChannel.Region) ?? $"Row {datum.RowIndex + 1}";
                if (layer.Mark == MarkKind.Point)
                    RenderPointSymbol(builder, PointShape(plan, layer, datum), point.Value.X, point.Value.Y, 4m,
                        SafePaint(LayerStyle(layer, "fill") ?? LayerStyle(layer, "color"), "#dc2626"),
                        "plot-geographic-point", datum.RowIndex, text, PointStrokeAttributes(layer));
                else labels.Add(new SmartLabel(datum.RowIndex, point.Value.X, point.Value.Y, text,
                    SafePaint(LayerStyle(layer, "color"), "#1f2937"), 200 + layer.ZIndex));
            }
        RenderSmartLabels(builder, plan, labels, plotWidth, plotHeight);
    }

    private static (decimal X, decimal Y)? GeographicDatum(ResolvedDatum datum, GeographicProjectionKind projection, decimal width, decimal height)
    {
        var longitude = Number(Channel(datum, FieldChannel.Longitude));
        var latitude = Number(Channel(datum, FieldChannel.Latitude));
        if (!longitude.HasValue || !latitude.HasValue || longitude is < -180m or > 180m || latitude is < -90m or > 90m) return null;
        return ProjectGeographic(longitude.Value, latitude.Value, projection, width, height);
    }

    private static (decimal X, decimal Y) ProjectGeographic(decimal longitude, decimal latitude, GeographicProjectionKind projection, decimal width, decimal height)
    {
        var x = Left + (longitude + 180m) / 360m * width;
        decimal normalizedY;
        if (projection == GeographicProjectionKind.Mercator)
        {
            var clamped = Math.Clamp((double)latitude, -85.05112878d, 85.05112878d);
            var radians = clamped * Math.PI / 180d;
            normalizedY = (decimal)(.5d - Math.Log(Math.Tan(Math.PI / 4d + radians / 2d)) / (2d * Math.PI));
        }
        else normalizedY = (90m - latitude) / 180m;
        return (x, Top + normalizedY * height);
    }

    private static string GeographicRamp(decimal value, decimal minimum, decimal maximum)
    {
        var t = maximum <= minimum ? 1m : Math.Clamp((value - minimum) / (maximum - minimum), 0m, 1m);
        return $"#{(int)(219m - 190m * t):X2}{(int)(234m - 156m * t):X2}{(int)(254m - 38m * t):X2}";
    }

    private static bool Overlaps(LabelBox left, LabelBox right) =>
        left.Left < right.Right + 2m && left.Right + 2m > right.Left
        && left.Top < right.Bottom + 2m && left.Bottom + 2m > right.Top;

    private static void RenderHorizontalCategoryAxisLabels(
        StringBuilder builder,
        ImmutableArray<string> categories,
        in CartesianPlotArea area,
        ResolvedScale? scale)
    {
        var (step, outerOffset) = CategoryLayout(categories.Length, area.Width, scale);
        var longest = categories.Max(category => Math.Min(18, category.Length));
        var estimatedWidth = longest * 5.2m;
        var crowded = estimatedWidth > step;
        var stride = scale?.LabelSkip is { } labelSkip ? Math.Max(1, labelSkip + 1)
            : crowded ? Math.Max(1, (int)Math.Ceiling(estimatedWidth / Math.Max(1m, step * 1.7m))) : 1;
        var hidden = new List<string>();
        for (var index = 0; index < categories.Length; index++)
        {
            if (index % stride != 0 && index != categories.Length - 1)
            {
                hidden.Add(categories[index]);
                continue;
            }
            var x = area.Left + outerOffset + step * (index + .5m);
            var y = area.Bottom + 16m + (crowded && index % 2 == 1 ? 10m : 0m);
            var angle = AxisLabelAngle(scale?.LabelRotation, crowded ? 35 : 0);
            var rotation = angle == 0 ? string.Empty : $" transform='rotate(-{angle} {N(x)} {N(y)})'";
            builder.AppendLine($"<text class='plot-axis-label' data-axis-index='{index}' x='{N(x)}' y='{N(y)}' text-anchor='{(angle == 0 ? "middle" : "end")}' font-size='9' fill='#666'{rotation}>{Esc(Truncate(categories[index], crowded ? 18 : 12))}</text>");
        }
        if (hidden.Count > 0)
            builder.AppendLine($"<desc class='plot-axis-label-occluded'>Additional categories: {Esc(string.Join(", ", hidden))}</desc>");
    }

    private static void RenderVerticalCategoryAxisLabels(
        StringBuilder builder,
        ImmutableArray<string> categories,
        decimal plotHeight,
        ResolvedScale? scale)
    {
        if (categories.IsDefaultOrEmpty) return;
        var (slot, outerOffset) = CategoryLayout(categories.Length, plotHeight, scale);
        var stride = scale?.LabelSkip is { } labelSkip ? Math.Max(1, labelSkip + 1)
            : slot >= 12m ? 1 : Math.Max(1, (int)Math.Ceiling(12m / Math.Max(1m, slot)));
        var hidden = new List<string>();
        for (var index = 0; index < categories.Length; index++)
        {
            if (index % stride != 0 && index != categories.Length - 1)
            {
                hidden.Add(categories[index]);
                continue;
            }
            var y = Math.Clamp(Top + outerOffset + slot * (index + .5m) + 3m, Top + 9m, Top + plotHeight - 1m);
            builder.AppendLine($"<text class='plot-axis-label' data-axis-index='{index}' x='{N(Left - 6m)}' y='{N(y)}' text-anchor='end' font-size='9' fill='#666'>{Esc(Truncate(categories[index], 12))}</text>");
        }
        if (hidden.Count > 0)
            builder.AppendLine($"<desc class='plot-axis-label-occluded'>Additional categories: {Esc(string.Join(", ", hidden))}</desc>");
    }

    private static void RenderText(ResolvedMarkLayer layer, int categoryCount,
        in CartesianPlotArea area, ResolvedScale? xScale, ResolvedScale? scale,
        string color, ICollection<SmartLabel> smartLabels)
    {
        if (scale is null) return;
        for (var index = 0; index < layer.Data.Length; index++)
        {
            var datum = layer.Data[index];
            var value = PlotPlanResolver.Number(Channel(datum, FieldChannel.Y) ?? Channel(datum, FieldChannel.Y2) ?? ChartValue.Null());
            if (datum.IsGap || !value.HasValue) continue;
            var text = EncodingText(datum, ConditionalEncodingChannel.Text)
                ?? (Channel(datum, FieldChannel.Text) is { } textValue ? PlotPlanResolver.Display(textValue) : null);
            if (string.IsNullOrEmpty(text)) continue;
            var xValue = PlotPlanResolver.Number(Channel(datum, FieldChannel.X) ?? ChartValue.Null());
            var x = xScale is not null && Continuous(xScale) && xValue.HasValue
                ? MapX(xValue.Value, xScale, area)
                : CategoryX(index, categoryCount, area, xScale);
            x += datum.DisplayOffsetX;
            var y = MapY(value.Value, scale, area.Height) + datum.DisplayOffsetY;
            smartLabels.Add(new SmartLabel(datum.RowIndex, x, y, text, color, 200 + layer.ZIndex, 10m));
        }
    }

    private sealed record ArcItem(ResolvedDatum Datum, string Label, decimal Value, bool IsOther = false);

    private static void RenderArcs(StringBuilder builder, PlotPlan plan)
    {
        var layer = plan.Layers.First(item => item.Mark == MarkKind.Arc);
        var rawItems = layer.Data.Where(datum => !datum.IsGap).Select(datum => new ArcItem(
            datum,
            PlotPlanResolver.Display(Channel(datum, FieldChannel.Theta) ?? ChartValue.From("")),
            PlotPlanResolver.Number(Channel(datum, FieldChannel.Radius) ?? ChartValue.Null()) ?? 0m
        )).Where(item => item.Value > 0).ToList();

        // 1. Sort order: SOURCE (default), VALUE_DESC, VALUE_ASC, ALPHA
        var sort = (Style(plan, "SORT") ?? Style(plan, "AXIS_SORT"))?.ToUpperInvariant();
        var items = sort switch
        {
            "VALUE_DESC" or "VALUE" => rawItems.OrderByDescending(x => x.Value).ThenBy(x => x.Label, StringComparer.Ordinal).ToList(),
            "VALUE_ASC" => rawItems.OrderBy(x => x.Value).ThenBy(x => x.Label, StringComparer.Ordinal).ToList(),
            "ALPHA" => rawItems.OrderBy(x => x.Label, StringComparer.OrdinalIgnoreCase).ToList(),
            _ => rawItems
        };

        var total = items.Sum(item => item.Value);
        if (total <= 0) return;

        // 2. Minimum slice threshold / "Other" rollup
        var minSlicePctStr = Style(plan, "MIN_SLICE_PCT");
        var otherLabel = Style(plan, "OTHER_LABEL") ?? "Other";
        if (!string.IsNullOrWhiteSpace(minSlicePctStr) &&
            decimal.TryParse(minSlicePctStr.TrimEnd('%'), NumberStyles.Number, CultureInfo.InvariantCulture, out var minPct) &&
            minPct > 0m)
        {
            var thresholdRatio = minPct >= 1m ? minPct / 100m : minPct;
            var kept = new List<ArcItem>();
            decimal otherTotal = 0m;
            ResolvedDatum? sampleDatum = null;
            foreach (var item in items)
            {
                if (item.Value / total < thresholdRatio)
                {
                    otherTotal += item.Value;
                    sampleDatum ??= item.Datum;
                }
                else
                {
                    kept.Add(item);
                }
            }
            if (otherTotal > 0m && kept.Count > 0)
            {
                kept.Add(new ArcItem(sampleDatum ?? items[0].Datum, otherLabel, otherTotal, true));
                items = kept;
            }
        }

        var legendPosition = LegendPosition(plan);
        var rightReserve = LegendEnabled(plan) && plan.Legend.Length > 1 && legendPosition == "RIGHT" ? 125m : 16m;
        var leftReserve = LegendEnabled(plan) && plan.Legend.Length > 1 && legendPosition == "LEFT" ? 125m : 16m;
        var showLabels = IsEnabled(plan.Style, "DATA_LABELS");
        var labelGutter = showLabels ? 88m : 16m;
        var chartLeft = leftReserve;
        var chartRight = plan.Bounds.Width - rightReserve;
        var cx = (chartLeft + chartRight) / 2m;
        var bottomReserve = LegendEnabled(plan) && plan.Legend.Length > 1 && legendPosition == "BOTTOM" ? 34m : 16m;
        var chartTop = 32m;
        var chartBottom = plan.Bounds.Height - bottomReserve;
        var cy = (chartTop + chartBottom) / 2m;
        var outer = Math.Max(20m, Math.Min((chartRight - chartLeft - labelGutter * 2m) / 2m, (chartBottom - chartTop - 16m) / 2m));
        var inner = (plan.Coordinate?.InnerRadius ?? 0m) * outer;
        var roseMode = IsEnabled(plan.Style, "ROSE_MODE");
        var maximum = items.Max(item => item.Value);
        var labels = new List<ArcLabel>();

        // 3. Start angle: default 12 o'clock (-90° in standard SVG radians)
        var startAngleDeg = plan.Coordinate?.StartAngle;
        if (!startAngleDeg.HasValue)
        {
            var saStr = Style(plan, "START_ANGLE")?.Trim().TrimEnd('°', 'd', 'e', 'g');
            if (decimal.TryParse(saStr, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedSa))
                startAngleDeg = parsedSa;
        }
        var angle = -Math.PI / 2d + (double)(startAngleDeg ?? 0m) * Math.PI / 180d;

        // 4. Slice borders
        var borderColor = Style(plan, "SLICE_BORDER_COLOR") ?? "white";
        var borderWidthStr = Style(plan, "SLICE_BORDER_WIDTH");
        var borderWidth = 2m;
        if (!string.IsNullOrWhiteSpace(borderWidthStr) &&
            decimal.TryParse(borderWidthStr.TrimEnd('p', 'x', 'P', 'X'), NumberStyles.Number, CultureInfo.InvariantCulture, out var bw))
        {
            borderWidth = bw;
        }
        var hasBorder = borderWidth > 0m && !borderColor.Equals("NONE", StringComparison.OrdinalIgnoreCase) && !borderColor.Equals("TRANSPARENT", StringComparison.OrdinalIgnoreCase);
        var strokeAttr = hasBorder
            ? $"stroke='{Esc(borderColor)}' stroke-width='{N(borderWidth)}'"
            : "stroke='none' stroke-width='0'";

        // 5. Slice explosion / pull-out
        var explodeSlice = Style(plan, "EXPLODE");
        var explodeAllStr = Style(plan, "EXPLODE_ALL");
        var explodeDistance = 10m;
        var hasExplodeAll = false;
        if (!string.IsNullOrWhiteSpace(explodeAllStr))
        {
            if (decimal.TryParse(explodeAllStr, NumberStyles.Number, CultureInfo.InvariantCulture, out var dist))
            {
                explodeDistance = dist;
                hasExplodeAll = dist > 0m;
            }
            else if (explodeAllStr.Equals("ON", StringComparison.OrdinalIgnoreCase) || explodeAllStr.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
            {
                hasExplodeAll = true;
            }
        }
        var explodeDistStr = Style(plan, "EXPLODE_DISTANCE");
        if (!string.IsNullOrWhiteSpace(explodeDistStr) &&
            decimal.TryParse(explodeDistStr, NumberStyles.Number, CultureInfo.InvariantCulture, out var ed))
        {
            explodeDistance = ed;
        }

        for (var index = 0; index < items.Count; index++)
        {
            var sweep = roseMode ? 2d * Math.PI / items.Count : 2d * Math.PI * (double)(items[index].Value / total);
            var end = angle + sweep;
            var large = sweep > Math.PI ? 1 : 0;
            var sliceOuter = roseMode ? Math.Max(inner + 2m, outer * (decimal)Math.Sqrt((double)(items[index].Value / maximum))) : outer;

            var isExploded = hasExplodeAll ||
                (!string.IsNullOrWhiteSpace(explodeSlice) && string.Equals(items[index].Label, explodeSlice, StringComparison.OrdinalIgnoreCase));
            var sliceDx = 0m;
            var sliceDy = 0m;
            if (isExploded)
            {
                var midAngle = angle + sweep / 2d;
                sliceDx = explodeDistance * (decimal)Math.Cos(midAngle);
                sliceDy = explodeDistance * (decimal)Math.Sin(midAngle);
            }
            var sliceCx = cx + sliceDx;
            var sliceCy = cy + sliceDy;

            var outerStart = Point(sliceCx, sliceCy, sliceOuter, angle);
            var outerEnd = Point(sliceCx, sliceCy, sliceOuter, end);
            var defaultColor = items[index].IsOther
                ? "#9ca3af"
                : (plan.Palette.FirstOrDefault(item => item.SeriesKey == items[index].Label)?.Color ?? "#5470c6");
            var color = EncodingText(items[index].Datum, ConditionalEncodingChannel.Color) is { } candidate ? SafePaint(candidate, defaultColor) : defaultColor;
            string path;
            if (inner > 0)
            {
                var innerEnd = Point(sliceCx, sliceCy, inner, end);
                var innerStart = Point(sliceCx, sliceCy, inner, angle);
                path = $"M {outerStart} A {N(sliceOuter)} {N(sliceOuter)} 0 {large} 1 {outerEnd} L {innerEnd} A {N(inner)} {N(inner)} 0 {large} 0 {innerStart} Z";
            }
            else path = $"M {N(sliceCx)} {N(sliceCy)} L {outerStart} A {N(sliceOuter)} {N(sliceOuter)} 0 {large} 1 {outerEnd} Z";

            var sliceClass = isExploded ? "plot-arc-slice plot-arc-exploded" : "plot-arc-slice";
            builder.AppendLine($"<path class='{sliceClass}' d='{path}' fill='{Esc(color)}' {strokeAttr} data-row-index='{items[index].Datum.RowIndex}'><title>{Esc(items[index].Label)}: {N(items[index].Value)}</title></path>");

            if (showLabels)
            {
                var midpoint = angle + sweep / 2d;
                var label = $"{items[index].Label}: {FormatDataLabel(items[index].Value, DataFormat(plan), total)}";
                var anchor = PointCoordinates(sliceCx, sliceCy, sliceOuter + 2m, midpoint);
                var elbow = PointCoordinates(sliceCx, sliceCy, outer + 11m, midpoint);
                labels.Add(new ArcLabel(anchor.X, anchor.Y, elbow.X, elbow.Y, label, Math.Cos(midpoint) >= 0d));
            }
            angle = end;
        }
        RenderArcLabels(builder, plan, labels, cx, outer, chartTop, chartBottom);
        RenderArcCenter(builder, plan, total, cx, cy, inner);
        RenderLegend(builder, plan);
    }

    private static void RenderArcLabels(StringBuilder builder, PlotPlan plan, IReadOnlyCollection<ArcLabel> labels,
        decimal cx, decimal outer, decimal chartTop, decimal chartBottom)
    {
        if (labels.Count == 0) return;
        const decimal lineHeight = 14m;
        foreach (var side in new[] { false, true })
        {
            var positioned = labels.Where(label => label.IsRight == side)
                .OrderBy(label => label.PreferredY)
                .Select(label => new PositionedArcLabel(label, Math.Clamp(label.PreferredY, chartTop + 8m, chartBottom - 4m)))
                .ToList();
            for (var index = 1; index < positioned.Count; index++)
            {
                var minimumY = positioned[index - 1].Y + lineHeight;
                if (positioned[index].Y < minimumY) positioned[index] = positioned[index] with { Y = minimumY };
            }
            if (positioned.Count > 0)
            {
                var overflow = positioned[^1].Y - (chartBottom - 4m);
                if (overflow > 0m)
                    for (var index = 0; index < positioned.Count; index++)
                        positioned[index] = positioned[index] with { Y = positioned[index].Y - overflow };
            }
            foreach (var item in positioned)
            {
                var textX = cx + (item.Label.IsRight ? outer + 20m : -outer - 20m);
                var lineEndX = textX + (item.Label.IsRight ? -3m : 3m);
                // COMPAT_BREAK: 0.19 — arc-label leaders now default OFF and require explicit opt-in.
                if (IsEnabled(plan.Style, "DATA_LABELS:LEADER_LINE"))
                {
                    var leaderColor = SafePaint(Style(plan, "DATA_LABELS:LEADER_LINE:COLOR"), "#9ca3af");
                    var leaderDash = LeaderLineDash(Style(plan, "DATA_LABELS:LEADER_LINE:STYLE"));
                    builder.AppendLine($"<path class='plot-arc-label-leader' d='M {N(item.Label.AnchorX)} {N(item.Label.AnchorY)} L {N(item.Label.ElbowX)} {N(item.Y)} L {N(lineEndX)} {N(item.Y)}' fill='none' stroke='{Esc(leaderColor)}' stroke-width='1'{leaderDash}/>");
                }
                RenderDataLabelBackground(builder, plan, null, textX, item.Y + 3m,
                    item.Label.IsRight ? "start" : "end", FontSize(Style(plan, "DATA_LABELS:FONT_SIZE")), item.Label.Text);
                builder.AppendLine($"<text class='plot-arc-label' x='{N(textX)}' y='{N(item.Y + 3m)}' text-anchor='{(item.Label.IsRight ? "start" : "end")}' font-size='{Esc(Style(plan, "DATA_LABELS:FONT_SIZE") ?? "9")}' fill='{Esc(SafePaint(Style(plan, "DATA_LABELS:COLOR"), "#333"))}'>{Esc(item.Label.Text)}</text>");
            }
        }
    }

    private static void RenderArcCenter(StringBuilder builder, PlotPlan plan, decimal total, decimal cx, decimal cy, decimal inner)
    {
        if (inner <= 0m) return;
        var centerLabel = Style(plan, "CENTER_LABEL");
        var centerValue = Style(plan, "CENTER_VALUE");
        if (string.IsNullOrWhiteSpace(centerLabel) && string.IsNullOrWhiteSpace(centerValue)) return;
        if (!string.IsNullOrWhiteSpace(centerValue))
        {
            centerValue = centerValue.Equals("TOTAL", StringComparison.OrdinalIgnoreCase)
                ? N(total)
                : centerValue.Replace("{total}", N(total), StringComparison.OrdinalIgnoreCase);
            builder.AppendLine($"<text class='plot-arc-center-value' x='{N(cx)}' y='{N(cy + (string.IsNullOrWhiteSpace(centerLabel) ? 6m : 1m))}' text-anchor='middle' font-size='18' font-weight='700' fill='#1f2937'>{Esc(centerValue)}</text>");
        }
        if (!string.IsNullOrWhiteSpace(centerLabel))
            builder.AppendLine($"<text class='plot-arc-center-label' x='{N(cx)}' y='{N(cy + (string.IsNullOrWhiteSpace(centerValue) ? 4m : 18m))}' text-anchor='middle' font-size='9' fill='#6b7280'>{Esc(centerLabel)}</text>");
    }

    private static decimal MapX(decimal value, ResolvedScale scale, decimal plotWidth, decimal plotLeft = Left)
    {
        var (minimum, maximum) = Domain(scale);
        var ratio = Ratio(value, minimum, maximum, scale.Kind);
        return plotLeft + (scale.Reverse ? 1m - ratio : ratio) * plotWidth;
    }

    private static decimal MapX(decimal value, ResolvedScale scale, in CartesianPlotArea area) =>
        MapX(value, scale, area.Width, area.Left);

    /// <summary>Maps a transposed positional value onto the vertical axis in category order, top to bottom.</summary>
    private static decimal MapVertical(decimal value, ResolvedScale scale, decimal plotHeight)
    {
        var (minimum, maximum) = Domain(scale);
        var ratio = Ratio(value, minimum, maximum, scale.Kind);
        return Top + (scale.Reverse ? 1m - ratio : ratio) * plotHeight;
    }

    private static decimal MapY(decimal value, ResolvedScale scale, decimal plotHeight)
    {
        var (minimum, maximum) = Domain(scale);
        var ratio = Ratio(value, minimum, maximum, scale.Kind);
        return Top + plotHeight - (scale.Reverse ? 1m - ratio : ratio) * plotHeight;
    }

    /// <summary>True when a resolved scale carries a continuous domain that interval endpoints can map onto.</summary>
    private static bool Continuous(ResolvedScale scale) =>
        scale.Kind is ScaleKind.Linear or ScaleKind.Logarithmic or ScaleKind.Time;

    private static (decimal Minimum, decimal Maximum) Domain(ResolvedScale scale)
    {
        var minimum = scale.Domain.Length > 0 ? PositionNumber(scale.Domain[0]) ?? 0m : 0m;
        var maximum = scale.Domain.Length > 1 ? PositionNumber(scale.Domain[^1]) ?? minimum + 1m : minimum + 1m;
        return maximum == minimum ? (minimum, minimum + 1m) : (minimum, maximum);
    }
    private static decimal Ratio(decimal value, decimal minimum, decimal maximum, ScaleKind kind)
    {
        if (kind != ScaleKind.Logarithmic) return (value - minimum) / (maximum - minimum);
        if (value <= 0m || minimum <= 0m || maximum <= 0m) return 0m;
        var minLog = Math.Log10((double)minimum);
        var maxLog = Math.Log10((double)maximum);
        return (decimal)((Math.Log10((double)value) - minLog) / (maxLog - minLog));
    }

    private static readonly SearchValues<char> SvgEscapeChars = SearchValues.Create("&<>'\"");

    private static decimal? Number(ChartValue? value) => value is null ? null : PlotPlanResolver.Number(value);

    private static ChartValue? Channel(ResolvedDatum datum, FieldChannel channel)
    {
        var channels = datum.Channels;
        for (var i = 0; i < channels.Length; i++)
        {
            if (channels[i].Channel == channel)
                return channels[i].Value;
        }
        return null;
    }

    private static string? DisplayChannel(ResolvedDatum datum, FieldChannel channel)
    {
        var channels = datum.Channels;
        for (var i = 0; i < channels.Length; i++)
        {
            if (channels[i].Channel == channel)
                return channels[i].DisplayValue ?? PlotPlanResolver.Display(channels[i].Value);
        }
        return null;
    }

    private static ChartValue? Encoding(ResolvedDatum datum, ConditionalEncodingChannel channel)
    {
        if (datum.Encodings.IsDefault) return null;
        var encodings = datum.Encodings;
        for (var i = 0; i < encodings.Length; i++)
        {
            if (encodings[i].Channel == channel)
                return encodings[i].Value;
        }
        return null;
    }

    private static string? EncodingText(ResolvedDatum datum, ConditionalEncodingChannel channel) => Encoding(datum, channel) is { } value
        ? PlotPlanResolver.Display(value) : null;

    private static decimal? EncodingNumber(ResolvedDatum datum, ConditionalEncodingChannel channel) => Encoding(datum, channel) is { } value
        ? PlotPlanResolver.Number(value) : null;

    private static string FormatDataLabel(decimal value, string? format, decimal? percentageBase = null)
    {
        if (string.IsNullOrWhiteSpace(format)) return N(value);
        var normalized = format.Trim().ToUpperInvariant();
        if (normalized.Length >= 2 && normalized[0] is 'N' or 'C' or 'P' &&
            int.TryParse(normalized[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var decimals))
        {
            var amount = normalized[0] == 'P'
                ? percentageBase is > 0m ? value / percentageBase.Value * 100m : value
                : value;
            var text = amount.ToString($"N{Math.Clamp(decimals, 0, 6)}", CultureInfo.InvariantCulture);
            return normalized[0] == 'C' ? "$" + text : normalized[0] == 'P' ? text + "%" : text;
        }
        return N(value);
    }

    private static bool IsEnabled(ImmutableArray<StyleToken> tokens, string name)
    {
        if (tokens.IsDefault) return false;
        for (var i = 0; i < tokens.Length; i++)
        {
            if (tokens[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                var value = tokens[i].Value;
                return value is not null && !value.Equals("OFF", StringComparison.OrdinalIgnoreCase) &&
                    !value.Equals("FALSE", StringComparison.OrdinalIgnoreCase) && value != "0";
            }
        }
        return false;
    }

    private static int AxisLabelAngle(string? value, int automatic) => value?.ToUpperInvariant() switch
    {
        "0" => 0,
        "45" => 45,
        "90" => 90,
        _ => automatic
    };

    private static bool SkipTickLabel(ResolvedScale scale, int index) =>
        scale.LabelSkip is { } skip && skip > 0 && index % (skip + 1) != 0 && index != scale.Ticks.Length - 1;

    private static void RenderHorizontalMinorTicks(StringBuilder builder, ResolvedScale scale, decimal width, decimal y, decimal plotLeft = Left)
    {
        if (!scale.MinorTicks) return;
        foreach (var value in MinorTickValues(scale))
        {
            var x = MapX(value, scale, width, plotLeft);
            builder.AppendLine($"<line class='plot-minor-tick' x1='{N(x)}' y1='{N(y)}' x2='{N(x)}' y2='{N(y + 2m)}' stroke='#bbb'/>");
        }
    }

    private static void RenderHorizontalMinorTicks(StringBuilder builder, ResolvedScale scale, in CartesianPlotArea area)
    {
        if (!scale.MinorTicks) return;
        foreach (var value in MinorTickValues(scale))
        {
            var x = MapX(value, scale, area);
            builder.AppendLine($"<line class='plot-minor-tick' x1='{N(x)}' y1='{N(area.Bottom)}' x2='{N(x)}' y2='{N(area.Bottom + 2m)}' stroke='#bbb'/>");
        }
    }

    private static void RenderVerticalMinorTicks(StringBuilder builder, ResolvedScale scale, decimal height, decimal x, decimal length)
    {
        if (!scale.MinorTicks) return;
        foreach (var value in MinorTickValues(scale))
        {
            var y = MapY(value, scale, height);
            builder.AppendLine($"<line class='plot-minor-tick' x1='{N(x)}' y1='{N(y)}' x2='{N(x + length / 2m)}' y2='{N(y)}' stroke='#bbb'/>");
        }
    }

    private static IEnumerable<decimal> MinorTickValues(ResolvedScale scale)
    {
        for (var index = 1; index < scale.Ticks.Length; index++)
        {
            var previous = PlotPlanResolver.Number(scale.Ticks[index - 1].Value);
            var current = PlotPlanResolver.Number(scale.Ticks[index].Value);
            if (previous.HasValue && current.HasValue) yield return (previous.Value + current.Value) / 2m;
        }
    }

    private static bool IsEnabledByDefault(ImmutableArray<StyleToken> tokens, string name)
    {
        if (tokens.IsDefault) return true;
        for (var i = 0; i < tokens.Length; i++)
        {
            if (!tokens[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            var value = tokens[i].Value;
            return value is null || !value.Equals("OFF", StringComparison.OrdinalIgnoreCase) &&
                !value.Equals("FALSE", StringComparison.OrdinalIgnoreCase) && value != "0";
        }
        return true;
    }

    private static string? Style(PlotPlan plan, string name)
    {
        var tokens = plan.Style;
        if (tokens.IsDefault) return null;
        for (var i = 0; i < tokens.Length; i++)
        {
            if (tokens[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return tokens[i].Value;
        }
        return null;
    }

    private static string? DataFormat(PlotPlan plan) => Style(plan, "DATA_LABELS:FORMAT") ?? Style(plan, "FORMAT");

    private static string? LayerStyle(ResolvedMarkLayer layer, string name)
    {
        var tokens = layer.Style;
        if (tokens.IsDefault) return null;
        for (var i = 0; i < tokens.Length; i++)
        {
            if (tokens[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return tokens[i].Value;
        }
        return null;
    }

    private static ResolvedScale? ColorScale(PlotPlan plan)
    {
        var scales = plan.Scales;
        for (var i = 0; i < scales.Length; i++)
        {
            if (scales[i].Channel == FieldChannel.Color && scales[i].ColorRange is not null)
                return scales[i];
        }
        return null;
    }

    private static decimal? UnitStyle(PlotPlan plan, string name)
    {
        var value = Style(plan, name);
        if (value is null) return null;
        if (!decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var number) || number is < 0m or > 1m)
            throw new InvalidOperationException($"{name} must be between zero and one.");
        return number;
    }

    private static string ResolveDatumColor(ResolvedScale? scale, ResolvedDatum datum, string fallback)
    {
        if (EncodingText(datum, ConditionalEncodingChannel.Color) is { } conditional)
            return SafePaint(conditional, fallback);
        if (scale?.ColorRange is not { } range) return fallback;
        var raw = Channel(datum, FieldChannel.Color);
        var value = raw is null ? null : PlotPlanResolver.Number(raw);
        if (!value.HasValue) return range.NullColor;
        var (minimum, maximum) = Domain(scale);
        var ratio = Math.Clamp(Ratio(value.Value, minimum, maximum, scale.Kind), 0m, 1m);
        if (range.Kind == ColorRangeKind.Diverging && range.Mid is not null)
        {
            var middle = ColorMidOffset(scale, range);
            return ratio <= middle
                ? InterpolateColor(range.Low, range.Mid, middle == 0m ? 0m : ratio / middle)
                : InterpolateColor(range.Mid, range.High, middle == 1m ? 1m : (ratio - middle) / (1m - middle));
        }
        return InterpolateColor(range.Low, range.High, ratio);
    }

    private static string OpacityAttribute(decimal opacity)
    {
        var clamped = Math.Clamp(opacity, 0m, 1m);
        return clamped == 1m ? string.Empty : $" fill-opacity='{N(clamped)}'";
    }

    private static decimal ColorMidOffset(ResolvedScale scale, ResolvedColorRange range)
    {
        if (range.Midpoint is not { } midpoint) return .5m;
        var (minimum, maximum) = Domain(scale);
        return Math.Clamp(Ratio(midpoint, minimum, maximum, scale.Kind), 0m, 1m);
    }

    private static int ParseHexByte(ReadOnlySpan<char> span) =>
        int.Parse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static string InterpolateColor(string low, string high, decimal ratio)
    {
        ratio = Math.Clamp(ratio, 0m, 1m);
        if (low.Length != 7 || high.Length != 7 || low[0] != '#' || high[0] != '#') return high;
        var rLow = ParseHexByte(low.AsSpan(1, 2));
        var gLow = ParseHexByte(low.AsSpan(3, 2));
        var bLow = ParseHexByte(low.AsSpan(5, 2));

        var rHigh = ParseHexByte(high.AsSpan(1, 2));
        var gHigh = ParseHexByte(high.AsSpan(3, 2));
        var bHigh = ParseHexByte(high.AsSpan(5, 2));

        static int Mix(int first, int second, decimal amount) =>
            (int)Math.Round(first + (second - first) * amount, MidpointRounding.AwayFromZero);

        return $"#{Mix(rLow, rHigh, ratio):X2}{Mix(gLow, gHigh, ratio):X2}{Mix(bLow, bHigh, ratio):X2}";
    }

    private static string SafePaint(string? candidate, string fallback)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return fallback;
        var value = candidate.Trim();
        if (value.Length is 4 or 7 && value[0] == '#')
        {
            var isHex = true;
            for (var i = 1; i < value.Length; i++)
            {
                if (!Uri.IsHexDigit(value[i])) { isHex = false; break; }
            }
            if (isHex) return value;
        }
        return value.ToLowerInvariant() switch
        {
            "white" or "black" or "transparent" => value.ToLowerInvariant(),
            "red" => "#dc2626",
            "blue" => "#2563eb",
            "cyan" => "#0891b2",
            "green" => "#16a34a",
            "orange" => "#ea580c",
            "purple" => "#9333ea",
            "yellow" => "#ca8a04",
            "gray" or "grey" => "#6b7280",
            _ => fallback
        };
    }

    private static string InterpolatePaint(string from, string to, decimal ratio)
    {
        if (from.Length != 7 || to.Length != 7 || from[0] != '#' || to[0] != '#') return to;
        ratio = Math.Clamp(ratio, 0m, 1m);
        var rFrom = ParseHexByte(from.AsSpan(1, 2));
        var gFrom = ParseHexByte(from.AsSpan(3, 2));
        var bFrom = ParseHexByte(from.AsSpan(5, 2));

        var rTo = ParseHexByte(to.AsSpan(1, 2));
        var gTo = ParseHexByte(to.AsSpan(3, 2));
        var bTo = ParseHexByte(to.AsSpan(5, 2));

        var red = (int)Math.Round(rFrom + (rTo - rFrom) * ratio);
        var green = (int)Math.Round(gFrom + (gTo - gFrom) * ratio);
        var blue = (int)Math.Round(bFrom + (bTo - bFrom) * ratio);
        return $"#{red:X2}{green:X2}{blue:X2}";
    }

    private static string DefaultColor(int index) => new[] { "#5470c6", "#91cc75", "#fac858", "#ee6666", "#73c0de", "#3ba272", "#fc8452" }[Math.Abs(index) % 7];
    private static (decimal X, decimal Y) PointCoordinates(decimal cx, decimal cy, decimal radius, double angle) =>
        (cx + radius * (decimal)Math.Cos(angle), cy + radius * (decimal)Math.Sin(angle));
    private static string Point(decimal cx, decimal cy, decimal radius, double angle) => $"{N(cx + radius * (decimal)Math.Cos(angle))} {N(cy + radius * (decimal)Math.Sin(angle))}";
    private static string N(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length] + "…";
    private static decimal FontSize(string? candidate, decimal fallback = 9m)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return fallback;
        var trimmed = candidate.Trim();
        if (trimmed.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^2];
        return decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out var size) && size > 0m
            ? size
            : fallback;
    }

    private static string LeaderLineDash(string? style) => style?.ToUpperInvariant() switch
    {
        "DASHED" => " stroke-dasharray='4 3'",
        _ => string.Empty
    };

    private static (decimal Width, string Style, string Color, string Dash)? ParseLabelBorder(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 3) return null;

        decimal? width = null;
        string? style = null;
        string? color = null;

        foreach (var part in parts)
        {
            if (part.EndsWith("px", StringComparison.OrdinalIgnoreCase) &&
                decimal.TryParse(part[..^2], NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedWidth) &&
                parsedWidth is > 0m and <= 12m)
            {
                if (width.HasValue) return null;
                width = parsedWidth;
            }
            else if (part.Equals("solid", StringComparison.OrdinalIgnoreCase) ||
                     part.Equals("dashed", StringComparison.OrdinalIgnoreCase) ||
                     part.Equals("dotted", StringComparison.OrdinalIgnoreCase))
            {
                if (style is not null) return null;
                style = part.ToLowerInvariant();
            }
            else
            {
                if (color is not null) return null;
                var safe = SafePaint(part, string.Empty);
                if (string.IsNullOrEmpty(safe)) return null;
                color = safe;
            }
        }

        if (!width.HasValue || style is null || color is null) return null;

        var dash = style switch
        {
            "dashed" => " stroke-dasharray='4 3'",
            "dotted" => " stroke-dasharray='2 2'",
            _ => string.Empty
        };

        return (width.Value, style, color, dash);
    }

    private static void RenderDataLabelBackground(
        StringBuilder builder,
        PlotPlan plan,
        int? rowIndex,
        decimal x,
        decimal y,
        string anchor,
        decimal fontSize,
        string text)
    {
        var background = Style(plan, "DATA_LABELS:LABEL_BACKGROUND");
        var border = Style(plan, "DATA_LABELS:LABEL_BORDER");
        var safeBg = !string.IsNullOrWhiteSpace(background) ? SafePaint(background, string.Empty) : null;
        if (string.IsNullOrEmpty(safeBg)) safeBg = null;
        var parsedBorder = ParseLabelBorder(border);

        if (safeBg is null && parsedBorder is null) return;

        var rowAttr = rowIndex.HasValue ? $" data-row-index='{rowIndex.Value}'" : string.Empty;
        const decimal padX = 4m;
        const decimal padY = 2m;
        var textWidth = Math.Min(200m, Math.Max(8m, text.Length * fontSize * .58m));
        var rectWidth = textWidth + padX * 2m;
        var rectHeight = fontSize + padY * 2m + 2m;
        var rectY = y - fontSize - padY;
        decimal rectX;
        if (anchor.Equals("middle", StringComparison.OrdinalIgnoreCase))
            rectX = x - textWidth / 2m - padX;
        else if (anchor.Equals("end", StringComparison.OrdinalIgnoreCase))
            rectX = x - textWidth - padX;
        else
            rectX = x - padX;

        var bgFill = safeBg ?? "none";
        var borderStroke = parsedBorder.HasValue
            ? $" stroke='{Esc(parsedBorder.Value.Color)}' stroke-width='{N(parsedBorder.Value.Width)}'{parsedBorder.Value.Dash}"
            : " stroke='none'";
        builder.AppendLine($"<rect class='plot-data-label-bg'{rowAttr} x='{N(rectX)}' y='{N(rectY)}' width='{N(rectWidth)}' height='{N(rectHeight)}' rx='2' fill='{Esc(bgFill)}'{borderStroke}/>");
    }

    private static string Esc(string value)
    {
        if (string.IsNullOrEmpty(value) || value.AsSpan().IndexOfAny(SvgEscapeChars) < 0)
            return value;
        return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("'", "&apos;").Replace("\"", "&quot;");
    }
}
