using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
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

    private static void RenderCartesian(StringBuilder builder, PlotPlan plan)
    {
        if (plan.Coordinate?.Kind == CoordinateKind.TransposedCartesian)
        {
            RenderTransposedCartesian(builder, plan);
            return;
        }
        var overlayLabelWidth = plan.Layers
            .Select(layer => LayerStyle(layer, "overlayType") is null ? null : LayerStyle(layer, "label"))
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Select(label => Math.Min(150m, label!.Length * 5.4m + 8m))
            .DefaultIfEmpty(0m)
            .Max();
        var plotRight = LegendPosition(plan) == "RIGHT" && plan.Legend.Length > 1 ? 130m : Right;
        if (overlayLabelWidth > 0m) plotRight = Math.Max(plotRight, Right + overlayLabelWidth + 18m);
        var plotWidth = plan.Bounds.Width - Left - plotRight;
        var plotHeight = plan.Bounds.Height - Top - Bottom;
        var overlayLabels = new List<OverlayLabel>();
        builder.AppendLine($"<line x1='{N(Left)}' y1='{N(Top)}' x2='{N(Left)}' y2='{N(Top + plotHeight)}' stroke='#bbb'/>");
        builder.AppendLine($"<line x1='{N(Left)}' y1='{N(Top + plotHeight)}' x2='{N(Left + plotWidth)}' y2='{N(Top + plotHeight)}' stroke='#bbb'/>");
        var xScale = plan.Scales.FirstOrDefault(scale => scale.Channel == FieldChannel.X);
        var categories = xScale?.Categories ?? ImmutableArray<string>.Empty;
        var yScale = plan.Scales.FirstOrDefault(scale => scale.Channel == FieldChannel.Y);
        var y2Scale = plan.Scales.FirstOrDefault(scale => scale.Channel == FieldChannel.Y2);
        var rectLayers = plan.Layers.Where(layer => layer.Mark == MarkKind.Rect).ToList();
        var lineLayers = plan.Layers.Where(layer => layer.Mark == MarkKind.Line &&
            LayerStyle(layer, "overlayType") is null).ToList();
        var showLabels = IsEnabled(plan.Style, "DATA_LABELS");

        if (yScale is not null)
        {
            foreach (var tick in yScale.Ticks)
            {
                var y = MapY(PlotPlanResolver.Number(tick.Value) ?? 0m, yScale, plotHeight);
                builder.AppendLine($"<line x1='{N(Left)}' y1='{N(y)}' x2='{N(Left + plotWidth)}' y2='{N(y)}' stroke='#e5e7eb'/>");
            }
        }

        var clipId = $"{plan.SpecId}-plot-clip";
        builder.AppendLine($"<defs><clipPath id='{Esc(clipId)}'><rect x='{N(Left)}' y='{N(Top)}' width='{N(plotWidth)}' height='{N(plotHeight)}'/></clipPath></defs>");
        builder.AppendLine($"<g clip-path='url(#{Esc(clipId)})'>");

        var hasPrimaryPoints = plan.Layers.Any(layer => layer.Mark == MarkKind.Point && LayerStyle(layer, "overlayType") is null);
        foreach (var layer in plan.Layers
            .OrderBy(item => hasPrimaryPoints && item.Mark == MarkKind.Point && LayerStyle(item, "overlayType") is null ? 1 : 0)
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
                    RenderRects(builder, plan, layer, rectLayers, layer.Stack != StackMode.None, categories.Length, plotWidth, plotHeight, xScale, yScale, color, showLabels);
                    break;
                case MarkKind.Line:
                    var lineScale = layer.Data.Any(datum => Channel(datum, FieldChannel.Y2) is not null) ? y2Scale ?? yScale : yScale;
                    if (layer.Stack != StackMode.None && overlayType is null)
                        RenderStackedLine(builder, plan, layer, lineLayers, categories.Length, plotWidth, plotHeight, lineScale, color, showLabels);
                    else
                        RenderLine(builder, plan, layer, categories.Length, plotWidth, plotHeight, xScale, lineScale, color, showLabels, overlayLabels);
                    break;
                case MarkKind.Area:
                    RenderArea(builder, layer, categories.Length, plotWidth, plotHeight, yScale, color);
                    break;
                case MarkKind.Point:
                    var pointScale = layer.Data.Any(datum => Channel(datum, FieldChannel.Y2) is not null)
                        ? y2Scale ?? yScale
                        : yScale;
                    RenderPoints(builder, plan, layer, categories.Length, plotWidth, plotHeight, xScale, pointScale, color);
                    break;
                case MarkKind.Rule:
                    RenderRule(builder, layer, plotWidth, plotHeight, xScale, yScale, overlayLabels);
                    break;
                case MarkKind.Text:
                    RenderText(builder, layer, categories.Length, plotWidth, plotHeight,
                        layer.Data.Any(datum => Channel(datum, FieldChannel.Y2) is not null) ? y2Scale ?? yScale : yScale, color);
                    break;
                case MarkKind.Tick:
                    RenderTicks(builder, plan, layer, categories.Length, plotWidth, plotHeight, yScale, color);
                    break;
            }
            if (overlayType is not null) builder.AppendLine("</g>");
        }
        builder.AppendLine("</g>");
        RenderOverlayLabels(builder, overlayLabels, plotHeight);

        if (categories.Length > 0)
        {
            var step = plotWidth / categories.Length;
            for (var index = 0; index < categories.Length; index++)
                builder.AppendLine($"<text x='{N(Left + step * (index + 0.5m))}' y='{N(Top + plotHeight + 16)}' text-anchor='middle' font-size='9' fill='#666'>{Esc(Truncate(categories[index], 12))}</text>");
        }
        if (yScale is not null)
        {
            foreach (var tick in yScale.Ticks)
            {
                var y = MapY(PlotPlanResolver.Number(tick.Value) ?? 0m, yScale, plotHeight);
                builder.AppendLine($"<line x1='{N(Left - 4)}' y1='{N(y)}' x2='{N(Left)}' y2='{N(y)}' stroke='#bbb'/>");
                builder.AppendLine($"<text x='{N(Left - 6)}' y='{N(y + 4)}' text-anchor='end' font-size='9' fill='#666'>{Esc(tick.Label)}</text>");
            }
        }
        if (y2Scale is not null)
        {
            foreach (var tick in y2Scale.Ticks)
            {
                var y = MapY(PlotPlanResolver.Number(tick.Value) ?? 0m, y2Scale, plotHeight);
                builder.AppendLine($"<line x1='{N(Left + plotWidth)}' y1='{N(y)}' x2='{N(Left + plotWidth + 4m)}' y2='{N(y)}' stroke='#bbb'/>");
                builder.AppendLine($"<text x='{N(Left + plotWidth + 6m)}' y='{N(y + 4m)}' font-size='9' fill='#666'>{Esc(tick.Label)}</text>");
            }
        }

        var xTitle = Style(plan, "axis:x:label") ?? Style(plan, "axis:x:title");
        var yTitle = Style(plan, "axis:y:label") ?? Style(plan, "axis:y:title");
        if (!string.IsNullOrWhiteSpace(xTitle))
            builder.AppendLine($"<text x='{N(Left + plotWidth / 2m)}' y='{N(plan.Bounds.Height - 8m)}' text-anchor='middle' font-size='10' fill='#444'>{Esc(xTitle)}</text>");
        if (!string.IsNullOrWhiteSpace(yTitle))
            builder.AppendLine($"<text x='12' y='{N(Top + plotHeight / 2m)}' text-anchor='middle' font-size='10' fill='#444' transform='rotate(-90 12 {N(Top + plotHeight / 2m)})'>{Esc(yTitle)}</text>");

        RenderLegend(builder, plan);
    }

    private static void RenderTransposedCartesian(StringBuilder builder, PlotPlan plan)
    {
        var plotWidth = plan.Bounds.Width - Left - Right;
        var plotHeight = plan.Bounds.Height - Top - Bottom;
        var bandScale = plan.Scales.FirstOrDefault(scale => scale.Channel == FieldChannel.X);
        var categories = bandScale?.Categories ?? [];
        var rectLayers = plan.Layers.Where(layer => layer.Mark == MarkKind.Rect).ToList();
        var stacked = rectLayers.Any(layer => layer.Stack != StackMode.None);
        var slot = plotHeight / Math.Max(1, categories.Length);
        var showLabels = IsEnabled(plan.Style, "DATA_LABELS");
        var isGrouped = rectLayers.Count > 1 && rectLayers.Any(l => LayerStyle(l, "series") is not null);
        builder.AppendLine($"<line x1='{N(Left)}' y1='{N(Top)}' x2='{N(Left)}' y2='{N(Top + plotHeight)}' stroke='#bbb'/>");
        builder.AppendLine($"<line x1='{N(Left)}' y1='{N(Top + plotHeight)}' x2='{N(Left + plotWidth)}' y2='{N(Top + plotHeight)}' stroke='#bbb'/>");
        foreach (var layer in plan.Layers)
        {
            var scale = layer.Data.Any(datum => Channel(datum, FieldChannel.Y2) is not null)
                ? plan.Scales.FirstOrDefault(item => item.Channel == FieldChannel.Y2)
                : plan.Scales.FirstOrDefault(item => item.Channel == FieldChannel.Y);
            if (scale is null) continue;
            var defaultColor = plan.Palette.FirstOrDefault(item => item.SeriesKey == layer.SeriesKey)?.Color ?? "#5470c6";
            var color = SafePaint(LayerStyle(layer, "fill") ?? LayerStyle(layer, "color"), defaultColor);
            var layerOpacity = decimal.TryParse(LayerStyle(layer, "opacity"), NumberStyles.Any, CultureInfo.InvariantCulture, out var o) ? o : 1m;
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
                var y = Top + slot * (index + .5m) + datum.DisplayOffsetY;
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
                        barHeight = groupHeight / Math.Max(1, rectLayers.Count);
                        top = y - groupHeight / 2m + layerIndex * barHeight;
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
                    var drawHeight = Math.Max(1m, barHeight - 1m);
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
                    if (showLabels)
                        builder.AppendLine($"<text x='{N(endX + (end >= start ? 4m : -4m))}' y='{N(y + 3m)}' text-anchor='{(end >= start ? "start" : "end")}' font-size='{Esc(Style(plan, "DATA_LABELS:FONT_SIZE") ?? "9")}' fill='{Esc(SafePaint(Style(plan, "DATA_LABELS:COLOR"), "#333"))}'>{Esc(rectLabel)}</text>");
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
                    builder.AppendLine($"<circle cx='{N(x)}' cy='{N(y)}' r='3' fill='{Esc(datumColor)}'/>");
                }
                if (layer.Mark is MarkKind.Line or MarkKind.Area)
                {
                    points.Add($"{N(x)} {N(y)}");
                    pointCoordinates.Add((x, y));
                }
            }
            if (layer.Mark == MarkKind.Line && points.Count > 1)
                builder.AppendLine($"<path d='M {string.Join(" L ", points)}' fill='none' stroke='{Esc(color)}' stroke-width='2'/>");
            else if (layer.Mark == MarkKind.Area && points.Count > 1)
            {
                var baseline = MapHorizontal(0m, scale, plotWidth);
                builder.AppendLine($"<path d='M {N(baseline)} {N(pointCoordinates[0].Y)} L {string.Join(" L ", points)} L {N(baseline)} {N(pointCoordinates[^1].Y)} Z' fill='{Esc(color)}' fill-opacity='0.25' stroke='{Esc(color)}' stroke-width='2'/>");
            }
        }
        for (var index = 0; index < categories.Length; index++)
            builder.AppendLine($"<text x='{N(Left - 6m)}' y='{N(Top + slot * (index + .5m) + 3m)}' text-anchor='end' font-size='9' fill='#666'>{Esc(Truncate(categories[index], 12))}</text>");
        var xTitle = Style(plan, "axis:x:label");
        var yTitle = Style(plan, "axis:y:label");
        if (!string.IsNullOrWhiteSpace(yTitle))
            builder.AppendLine($"<text x='{N(Left + plotWidth / 2m)}' y='{N(plan.Bounds.Height - 8m)}' text-anchor='middle' font-size='10' fill='#444'>{Esc(yTitle)}</text>");
        if (!string.IsNullOrWhiteSpace(xTitle))
            builder.AppendLine($"<text x='12' y='{N(Top + plotHeight / 2m)}' text-anchor='middle' font-size='10' fill='#444' transform='rotate(-90 12 {N(Top + plotHeight / 2m)})'>{Esc(xTitle)}</text>");
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
        return position is "TOP" or "BOTTOM" or "LEFT" or "RIGHT"
            ? position.ToUpperInvariant()
            : "BOTTOM";
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
        if (plan.Legend.Length <= 1) return;
        var position = LegendPosition(plan);
        var vertical = position is "LEFT" or "RIGHT";
        var x = position == "RIGHT" ? plan.Bounds.Width - 105m : Left;
        var y = position == "TOP" ? 29m : plan.Bounds.Height - 12m;
        if (position == "LEFT") x = 8m;
        for (var index = 0; index < plan.Legend.Length; index++)
        {
            var entry = plan.Legend[index];
            var entryX = vertical ? x : x;
            var entryY = vertical ? Top + index * 16m : y;
            builder.AppendLine($"<rect x='{N(entryX)}' y='{N(entryY - 8m)}' width='9' height='9' fill='{Esc(SafePaint(entry.Color, "#5470c6"))}'/>");
            builder.AppendLine($"<text x='{N(entryX + 13m)}' y='{N(entryY)}' font-size='9' fill='#444'>{Esc(entry.Label)}</text>");
            if (!vertical) x += Math.Max(65m, entry.Label.Length * 6m + 25m);
        }
    }

    private static decimal MapHorizontal(decimal value, ResolvedScale scale, decimal plotWidth)
    {
        var (minimum, maximum) = Domain(scale);
        return Left + Ratio(value, minimum, maximum, scale.Kind) * plotWidth;
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
        decimal plotWidth, decimal plotHeight, ResolvedScale? scale, string color)
    {
        if (scale is null || layer.Data.IsDefaultOrEmpty) return;
        var ribbon = layer.Data.Any(datum => Channel(datum, FieldChannel.YStart) is not null || Channel(datum, FieldChannel.YEnd) is not null);
        if (ribbon)
        {
            var ribbonSegments = new List<(List<(decimal X, decimal Y)> Upper, List<(decimal X, decimal Y)> Lower)>
                { (new(), new()) };
            for (var index = 0; index < layer.Data.Length; index++)
            {
                var datum = layer.Data[index];
                var start = PositionNumber(Channel(datum, FieldChannel.YStart));
                var end = PositionNumber(Channel(datum, FieldChannel.YEnd));
                if (datum.IsGap || !start.HasValue || !end.HasValue)
                {
                    if (ribbonSegments[^1].Upper.Count > 0) ribbonSegments.Add((new(), new()));
                    continue;
                }
                var x = CategoryX(index, categoryCount, plotWidth) + datum.DisplayOffsetX;
                ribbonSegments[^1].Upper.Add((x, MapY(end.Value, scale, plotHeight) + datum.DisplayOffsetY));
                ribbonSegments[^1].Lower.Add((x, MapY(start.Value, scale, plotHeight) + datum.DisplayOffsetY));
            }
            foreach (var segment in ribbonSegments.Where(segment => segment.Upper.Count > 1))
            {
                var path = $"M {string.Join(" L ", segment.Upper.Select(point => $"{N(point.X)} {N(point.Y)}"))} " +
                    $"L {string.Join(" L ", segment.Lower.AsEnumerable().Reverse().Select(point => $"{N(point.X)} {N(point.Y)}"))} Z";
                builder.AppendLine($"<path class='plot-ribbon' d='{path}' fill='{Esc(color)}' fill-opacity='.2' stroke='{Esc(color)}' stroke-width='1'/>");
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
            segments[^1].Add((CategoryX(index, categoryCount, plotWidth) + datum.DisplayOffsetX,
                MapY(value.Value, scale, plotHeight) + datum.DisplayOffsetY));
        }
        foreach (var points in segments.Where(segment => segment.Count > 1))
        {
            var path = "M " + string.Join(" L ", points.Select(point => $"{N(point.X)} {N(point.Y)}"));
            builder.AppendLine($"<path d='{path} L {N(points[^1].X)} {N(Top + plotHeight)} L {N(points[0].X)} {N(Top + plotHeight)} Z' fill='{Esc(color)}' fill-opacity='.2'/>");
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
        int categoryCount, decimal plotWidth, decimal plotHeight, ResolvedScale? xScale, ResolvedScale? scale, string color, bool showLabels)
    {
        if (scale is null) return;
        // A non-stacked layer whose author supplied X_START/X_END owns its horizontal extent outright and
        // needs no category band. A stacked layer's endpoints are resolver-computed, so it keeps the band.
        var rangedX = !stacked && xScale is not null && Continuous(xScale) &&
            layer.Data.Any(datum => Channel(datum, FieldChannel.XStart) is not null && Channel(datum, FieldChannel.XEnd) is not null);
        if (categoryCount == 0 && !rangedX) return;
        var layerIndex = Enumerable.Range(0, layers.Count).First(index => ReferenceEquals(layers[index], layer));
        var layerCount = Math.Max(1, layers.Count);
        var slot = plotWidth / Math.Max(1, categoryCount);
        var groupWidth = slot * layer.BandSize;
        var barWidth = stacked ? groupWidth : groupWidth / layerCount;
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
            var startY = MapY(start, scale, plotHeight) + datum.DisplayOffsetY;
            var endY = MapY(end, scale, plotHeight) + datum.DisplayOffsetY;
            var x = Left + slot * index + (slot - groupWidth) / 2m + (stacked ? 0m : layerIndex * barWidth) + datum.DisplayOffsetX;
            var width = Math.Max(1m, barWidth - 1m);
            if (rangedX)
            {
                var spanStart = PositionNumber(Channel(datum, FieldChannel.XStart));
                var spanEnd = PositionNumber(Channel(datum, FieldChannel.XEnd));
                if (!spanStart.HasValue || !spanEnd.HasValue) continue;
                var first = MapX(spanStart.Value, xScale!, plotWidth) + datum.DisplayOffsetX;
                var second = MapX(spanEnd.Value, xScale!, plotWidth) + datum.DisplayOffsetX;
                x = Math.Min(first, second);
                width = Math.Max(1m, Math.Abs(second - first));
            }
            var datumColor = ResolveDatumColor(plan, datum, color);
            var opacity = EncodingNumber(datum, ConditionalEncodingChannel.Opacity) ?? 1m;
            var top = Math.Min(startY, endY);
            var barHeight = Math.Max(1m, Math.Abs(startY - endY));
            var label = FormatDataLabel(rangedY ? end : value!.Value, DataFormat(plan));
            var title = rangedY
                ? $"{FormatDataLabel(start, DataFormat(plan))} to {FormatDataLabel(end, DataFormat(plan))}"
                : label;
            var rangedClass = rangedY || rangedX ? " class='plot-range-rect'" : string.Empty;
            var extent = rangedY || rangedX ? string.Empty : ExtentAttributes(layer);
            builder.AppendLine($"<rect{rangedClass} x='{N(x)}' y='{N(top)}' width='{N(width)}' height='{N(barHeight)}' fill='{Esc(datumColor)}' fill-opacity='{N(Math.Clamp(opacity, 0m, 1m))}' data-row-index='{datum.RowIndex}'{extent}><title>{Esc(title)}</title></rect>");
            if (showLabels)
            {
                var position = Style(plan, "DATA_LABELS:POSITION") ?? "OUTSIDE";
                var labelColor = SafePaint(Style(plan, "DATA_LABELS:COLOR"), "#444");
                var labelSize = Style(plan, "DATA_LABELS:FONT_SIZE") ?? "9";
                var labelWeight = Style(plan, "DATA_LABELS:FONT_WEIGHT");
                var labelX = x + width / 2m;
                var labelY = top - 3m;
                var anchor = "middle";
                if (position.Contains("RIGHT", StringComparison.OrdinalIgnoreCase))
                {
                    labelX = x + width + 3m;
                    labelY = top + barHeight / 2m + 3m;
                    anchor = "start";
                }
                else if (position.Contains("INSIDE", StringComparison.OrdinalIgnoreCase))
                {
                    labelY = end >= start ? top + 13m : top + barHeight - 4m;
                    labelColor = SafePaint(Style(plan, "DATA_LABELS:COLOR"), "white");
                }
                builder.AppendLine($"<text x='{N(labelX)}' y='{N(labelY)}' text-anchor='{anchor}' font-size='{Esc(labelSize)}' fill='{Esc(labelColor)}'{(string.IsNullOrWhiteSpace(labelWeight) ? string.Empty : $" font-weight='{Esc(labelWeight)}'")}>{Esc(label)}</text>");
            }
        }
    }

    private static void RenderTicks(StringBuilder builder, PlotPlan plan, ResolvedMarkLayer layer, int categoryCount,
        decimal plotWidth, decimal plotHeight, ResolvedScale? yScale, string color)
    {
        if (categoryCount == 0 || yScale is null) return;
        var slot = plotWidth / categoryCount;
        var length = slot * layer.BandSize;
        var strokeWidth = Math.Clamp(layer.TickThickness * 8m, 1m, 8m);
        for (var index = 0; index < layer.Data.Length; index++)
        {
            var datum = layer.Data[index];
            var value = PlotPlanResolver.Number(Channel(datum, FieldChannel.Y) ?? ChartValue.Null());
            if (datum.IsGap || !value.HasValue) continue;
            var x = CategoryX(index, categoryCount, plotWidth) + datum.DisplayOffsetX;
            var y = MapY(value.Value, yScale, plotHeight) + datum.DisplayOffsetY;
            var vertical = layer.TickOrientation == TickOrientation.Vertical;
            var x1 = vertical ? x : x - length / 2m;
            var x2 = vertical ? x : x + length / 2m;
            var y1 = vertical ? y - Math.Min(length, plotHeight / Math.Max(1, categoryCount)) / 2m : y;
            var y2 = vertical ? y + Math.Min(length, plotHeight / Math.Max(1, categoryCount)) / 2m : y;
            var datumColor = ResolveDatumColor(plan, datum, color);
            builder.AppendLine($"<line class='plot-tick' x1='{N(x1)}' y1='{N(y1)}' x2='{N(x2)}' y2='{N(y2)}' stroke='{Esc(datumColor)}' stroke-width='{N(strokeWidth)}' data-row-index='{datum.RowIndex}'><title>{Esc(PlotPlanResolver.Display(Channel(datum, FieldChannel.Tooltip) ?? ChartValue.From(value.Value)))}</title></line>");
        }
    }

    private static void RenderLine(StringBuilder builder, PlotPlan plan, ResolvedMarkLayer layer, int categoryCount,
        decimal plotWidth, decimal plotHeight, ResolvedScale? xScale, ResolvedScale? scale, string color, bool showLabels,
        ICollection<OverlayLabel> overlayLabels)
    {
        if (scale is null || layer.Data.IsDefaultOrEmpty) return;
        var lineStyle = LayerStyle(layer, "lineStyle");
        var dashAttributes = LineStyleAttributes(lineStyle);
        var isOverlay = LayerStyle(layer, "overlayType") is not null;
        var smooth = IsEnabled(plan.Style, "SMOOTH") && !isOverlay;
        var strokeWidth = isOverlay ? "3" : "2";
        (decimal X, decimal Y)? lastPoint = null;
        var segment = new List<(decimal X, decimal Y)>();
        void Flush()
        {
            if (segment.Count > 1) builder.AppendLine($"<path d='{PathData(segment, smooth)}' fill='none' stroke='{Esc(color)}' stroke-width='{strokeWidth}' stroke-linejoin='round' stroke-linecap='round'{dashAttributes}/>");
            segment.Clear();
        }
        for (var index = 0; index < layer.Data.Length; index++)
        {
            var datum = layer.Data[index];
            var value = PlotPlanResolver.Number(Channel(datum, FieldChannel.Y) ?? Channel(datum, FieldChannel.Y2) ?? ChartValue.Null());
            if (datum.IsGap || !value.HasValue) { Flush(); continue; }
            var xValue = PlotPlanResolver.Number(Channel(datum, FieldChannel.X) ?? ChartValue.Null());
            var x = xScale is not null && xScale.Kind is ScaleKind.Linear or ScaleKind.Logarithmic && xValue.HasValue
                ? MapX(xValue.Value, xScale, plotWidth)
                : CategoryX(index, categoryCount, plotWidth);
            x += datum.DisplayOffsetX;
            var y = MapY(value.Value, scale, plotHeight) + datum.DisplayOffsetY;
            lastPoint = (x, y);
            segment.Add((x, y));
            if (!isOverlay || !plan.Layers.Any(candidate => candidate.Mark == MarkKind.Point && LayerStyle(candidate, "overlayType") is null))
                builder.AppendLine($"<circle cx='{N(x)}' cy='{N(y)}' r='{(isOverlay ? "4" : "3")}' fill='{Esc(color)}'{(isOverlay ? " stroke='white' stroke-width='1.5'" : string.Empty)} data-row-index='{datum.RowIndex}'><title>{Esc(FormatDataLabel(value.Value, DataFormat(plan)))}</title></circle>");
            if (showLabels)
                builder.AppendLine($"<text x='{N(x)}' y='{N(y - 6m)}' text-anchor='middle' font-size='{Esc(Style(plan, "DATA_LABELS:FONT_SIZE") ?? "9")}' fill='{Esc(SafePaint(Style(plan, "DATA_LABELS:COLOR"), "#444"))}'>{Esc(FormatDataLabel(value.Value, DataFormat(plan)))}</text>");
        }
        Flush();
        var overlayLabel = LayerStyle(layer, "label");
        if (isOverlay && lastPoint.HasValue && !string.IsNullOrWhiteSpace(overlayLabel))
            overlayLabels.Add(new OverlayLabel(lastPoint.Value.X, lastPoint.Value.Y, overlayLabel, color, layer.ZIndex));
    }

    private static void RenderStackedLine(StringBuilder builder, PlotPlan plan, ResolvedMarkLayer layer,
        IReadOnlyList<ResolvedMarkLayer> layers, int categoryCount, decimal plotWidth, decimal plotHeight,
        ResolvedScale? scale, string color, bool showLabels)
    {
        if (scale is null || layer.Data.IsDefaultOrEmpty) return;
        var topPoints = new List<(decimal X, decimal Y)>();
        var basePoints = new List<(decimal X, decimal Y)>();

        void Flush()
        {
            if (topPoints.Count > 1)
            {
                var area = $"M {string.Join(" L ", topPoints.Select(point => $"{N(point.X)} {N(point.Y)}"))} " +
                    $"L {string.Join(" L ", basePoints.AsEnumerable().Reverse().Select(point => $"{N(point.X)} {N(point.Y)}"))} Z";
                builder.AppendLine($"<path class='plot-stacked-area' data-series='{Esc(layer.SeriesKey ?? layer.Id)}' d='{area}' fill='{Esc(color)}' fill-opacity='.28'/>");
                builder.AppendLine($"<path d='{PathData(topPoints, false)}' fill='none' stroke='{Esc(color)}' stroke-width='2.5' stroke-linejoin='round' stroke-linecap='round'/>");
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
            var x = CategoryX(index, categoryCount, plotWidth) + datum.DisplayOffsetX;
            var topY = MapY(end, scale, plotHeight) + datum.DisplayOffsetY;
            topPoints.Add((x, topY));
            basePoints.Add((x, MapY(start, scale, plotHeight) + datum.DisplayOffsetY));
            builder.AppendLine($"<circle cx='{N(x)}' cy='{N(topY)}' r='3' fill='{Esc(color)}' data-row-index='{datum.RowIndex}'><title>{Esc(FormatDataLabel(value.Value, DataFormat(plan)))}</title></circle>");
            if (showLabels)
                builder.AppendLine($"<text x='{N(x)}' y='{N(topY - 6m)}' text-anchor='middle' font-size='{Esc(Style(plan, "DATA_LABELS:FONT_SIZE") ?? "9")}' fill='{Esc(SafePaint(Style(plan, "DATA_LABELS:COLOR"), "#444"))}'>{Esc(FormatDataLabel(value.Value, DataFormat(plan)))}</text>");
        }
        Flush();
    }

    private static decimal CategoryX(int index, int categoryCount, decimal plotWidth) =>
        Left + plotWidth * (index + .5m) / Math.Max(1, categoryCount);

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
        decimal plotWidth, decimal plotHeight, ResolvedScale? xScale, ResolvedScale? yScale, string color)
    {
        if (xScale is null || yScale is null) return;
        var sizes = layer.Data.Select(datum => PlotPlanResolver.Number(Channel(datum, FieldChannel.Size) ?? ChartValue.Null()))
            .Where(value => value.HasValue).Select(value => value!.Value).ToList();
        var minimumSize = sizes.DefaultIfEmpty(0m).Min();
        var maximumSize = sizes.DefaultIfEmpty(0m).Max();
        foreach (var datum in layer.Data.Where(item => !item.IsGap))
        {
            var xChannel = Channel(datum, FieldChannel.X) ?? ChartValue.Null();
            var yValue = PlotPlanResolver.Number(Channel(datum, FieldChannel.Y) ??
                Channel(datum, FieldChannel.Y2) ?? ChartValue.Null());
            if (!yValue.HasValue) continue;
            var datumColor = ResolveDatumColor(plan, datum, color);
            var rawSize = PlotPlanResolver.Number(Channel(datum, FieldChannel.Size) ?? ChartValue.Null());
            var radius = EncodingNumber(datum, ConditionalEncodingChannel.Size)
                ?? NormalizePointRadius(rawSize, minimumSize, maximumSize);
            var opacity = EncodingNumber(datum, ConditionalEncodingChannel.Opacity) ?? 1m;
            var label = Channel(datum, FieldChannel.Text) is { } labelValue ? PlotPlanResolver.Display(labelValue) : null;
            decimal x;
            if (!xScale.Categories.IsDefaultOrEmpty)
            {
                var category = DisplayChannel(datum, FieldChannel.X);
                var categoryIndex = category is null ? -1 : xScale.Categories.IndexOf(category);
                if (categoryIndex < 0) continue;
                x = CategoryX(categoryIndex, categoryCount, plotWidth);
            }
            else
            {
                var xValue = PlotPlanResolver.Number(xChannel);
                if (!xValue.HasValue) continue;
                x = MapX(xValue.Value, xScale, plotWidth);
            }
            x += datum.DisplayOffsetX;
            var y = MapY(yValue.Value, yScale, plotHeight) + datum.DisplayOffsetY;
            builder.AppendLine($"<circle class='plot-point' cx='{N(x)}' cy='{N(y)}' r='{N(Math.Clamp(radius, 1m, 30m))}' fill='{Esc(datumColor)}' fill-opacity='{N(Math.Clamp(opacity, 0m, 1m))}' stroke='white' stroke-width='1.5' data-row-index='{datum.RowIndex}'>{(string.IsNullOrWhiteSpace(label) ? string.Empty : $"<title>{Esc(label)}</title>")}</circle>");
            if (IsEnabled(plan.Style, "DATA_LABELS"))
                builder.AppendLine($"<text x='{N(x + 5m)}' y='{N(y - 5m)}' font-size='{Esc(Style(plan, "DATA_LABELS:FONT_SIZE") ?? "9")}' fill='{Esc(SafePaint(Style(plan, "DATA_LABELS:COLOR"), "#444"))}'>{Esc(label ?? FormatDataLabel(yValue.Value, Style(plan, "DATA_LABELS:FORMAT")))}</text>");
        }
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
            builder.AppendLine($"<text x='{N(labelX)}' y='{N(midY + 4m)}' text-anchor='start' font-size='10' fill='#374151'>{Esc(showValues ? $"{label} · {N(values[index])}" : label)}</text>");
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
        var lowColor = SafePaint(Style(plan, "COLOR:min"), "#dbeafe");
        var highColor = SafePaint(Style(plan, "COLOR:max"), "#1d4ed8");
        var showLabels = IsEnabled(plan.Style, "DATA_LABELS");
        foreach (var datum in layer.Data.Where(item => !item.IsGap))
        {
            var x = DisplayChannel(datum, FieldChannel.X);
            var y = DisplayChannel(datum, FieldChannel.Y);
            var xIndex = x is null ? -1 : xCategories.IndexOf(x);
            var yIndex = y is null ? -1 : yCategories.IndexOf(y);
            var value = PlotPlanResolver.Number(Channel(datum, FieldChannel.Size) ?? ChartValue.Null());
            if (xIndex < 0 || yIndex < 0 || !value.HasValue) continue;
            var ratio = maximum <= minimum ? 1m : Math.Clamp((value.Value - minimum) / (maximum - minimum), 0m, 1m);
            var cellX = Left + xIndex * cellWidth;
            var cellY = Top + yIndex * cellHeight;
            builder.AppendLine($"<rect class='plot-heat-cell' x='{N(cellX)}' y='{N(cellY)}' width='{N(cellWidth - 1m)}' height='{N(cellHeight - 1m)}' fill='{Esc(InterpolatePaint(lowColor, highColor, ratio))}' data-row-index='{datum.RowIndex}'><title>{Esc(x!)} / {Esc(y!)}: {N(value.Value)}</title></rect>");
            if (showLabels)
                builder.AppendLine($"<text x='{N(cellX + cellWidth / 2m)}' y='{N(cellY + cellHeight / 2m + 4m)}' text-anchor='middle' font-size='9' fill='{(ratio > .55m ? "white" : "#1f2937")}'>{N(value.Value)}</text>");
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
            builder.AppendLine($"<polygon points='{string.Join(" ", points)}' fill='{Esc(color)}' fill-opacity='.18' stroke='{Esc(color)}' stroke-width='2' data-row-index='{layer.Data.FirstOrDefault()?.RowIndex ?? 0}'><title>{Esc(layer.SeriesKey ?? layer.Id)}</title></polygon>");
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

    private static decimal NormalizePointRadius(decimal? value, decimal minimum, decimal maximum)
    {
        if (!value.HasValue) return 4m;
        if (maximum <= minimum) return 10m;
        return 4m + ((value.Value - minimum) / (maximum - minimum) * 18m);
    }

    private static void RenderRule(StringBuilder builder, ResolvedMarkLayer layer, decimal plotWidth,
        decimal plotHeight, ResolvedScale? xScale, ResolvedScale? yScale, ICollection<OverlayLabel> overlayLabels)
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
                builder.AppendLine($"<line class='plot-range-rule' x1='{N(MapX(xStart.Value, xScale, plotWidth))}' y1='{N(MapY(yStart.Value, yScale, plotHeight))}' x2='{N(MapX(xEnd.Value, xScale, plotWidth))}' y2='{N(MapY(yEnd.Value, yScale, plotHeight))}' stroke='{Esc(color)}' stroke-width='{Esc(strokeWidth)}'{dashAttributes} data-row-index='{datum.RowIndex}'/>");
                ranged = true;
                continue;
            }
            if (yStart.HasValue && yEnd.HasValue && yScale is not null)
            {
                var x = CategoryX(index, Math.Max(1, layer.Data.Length), plotWidth);
                builder.AppendLine($"<line class='plot-range-rule' x1='{N(x)}' y1='{N(MapY(yStart.Value, yScale, plotHeight))}' x2='{N(x)}' y2='{N(MapY(yEnd.Value, yScale, plotHeight))}' stroke='{Esc(color)}' stroke-width='{Esc(strokeWidth)}'{dashAttributes} data-row-index='{datum.RowIndex}'/>");
                ranged = true;
            }
            if (xStart.HasValue && xEnd.HasValue && xScale is not null)
            {
                var y = Top + plotHeight * (index + .5m) / Math.Max(1, layer.Data.Length);
                builder.AppendLine($"<line class='plot-range-rule' x1='{N(MapX(xStart.Value, xScale, plotWidth))}' y1='{N(y)}' x2='{N(MapX(xEnd.Value, xScale, plotWidth))}' y2='{N(y)}' stroke='{Esc(color)}' stroke-width='{Esc(strokeWidth)}'{dashAttributes} data-row-index='{datum.RowIndex}'/>");
                ranged = true;
            }
        }
        if (ranged) return;

        if (yVal is not null && yScale is not null)
        {
            var yNum = PlotPlanResolver.Number(yVal);
            if (yNum.HasValue)
            {
                var y = MapY(yNum.Value, yScale, plotHeight);
                builder.AppendLine($"<line x1='{N(Left)}' y1='{N(y)}' x2='{N(Left + plotWidth)}' y2='{N(y)}' stroke='{Esc(color)}' stroke-width='{Esc(strokeWidth)}'{dashAttributes}/>");
                if (!string.IsNullOrWhiteSpace(label))
                    overlayLabels.Add(new OverlayLabel(Left + plotWidth, y, label, color, layer.ZIndex));
            }
        }
        else if (xVal is not null && xScale is not null)
        {
            var xNum = PlotPlanResolver.Number(xVal);
            if (xNum.HasValue)
            {
                var x = MapX(xNum.Value, xScale, plotWidth);
                builder.AppendLine($"<line x1='{N(x)}' y1='{N(Top)}' x2='{N(x)}' y2='{N(Top + plotHeight)}' stroke='{Esc(color)}' stroke-width='{Esc(strokeWidth)}'{dashAttributes}/>");
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

    private static void RenderText(StringBuilder builder, ResolvedMarkLayer layer, int categoryCount,
        decimal plotWidth, decimal plotHeight, ResolvedScale? scale, string color)
    {
        if (scale is null) return;
        var denominator = Math.Max(1, categoryCount - 1);
        for (var index = 0; index < layer.Data.Length; index++)
        {
            var datum = layer.Data[index];
            var value = PlotPlanResolver.Number(Channel(datum, FieldChannel.Y) ?? Channel(datum, FieldChannel.Y2) ?? ChartValue.Null());
            if (datum.IsGap || !value.HasValue) continue;
            var text = EncodingText(datum, ConditionalEncodingChannel.Text)
                ?? (Channel(datum, FieldChannel.Text) is { } textValue ? PlotPlanResolver.Display(textValue) : null);
            if (string.IsNullOrEmpty(text)) continue;
            var x = Left + plotWidth * index / denominator + datum.DisplayOffsetX;
            var y = MapY(value.Value, scale, plotHeight) + datum.DisplayOffsetY;
            builder.AppendLine($"<text x='{N(x)}' y='{N(y)}' text-anchor='middle' font-size='10' fill='{Esc(color)}'>{Esc(text)}</text>");
        }
    }

    private static void RenderArcs(StringBuilder builder, PlotPlan plan)
    {
        var layer = plan.Layers.First(item => item.Mark == MarkKind.Arc);
        var items = layer.Data.Where(datum => !datum.IsGap).Select(datum => new
        {
            Datum = datum,
            Label = PlotPlanResolver.Display(Channel(datum, FieldChannel.Theta) ?? ChartValue.From("")),
            Value = PlotPlanResolver.Number(Channel(datum, FieldChannel.Radius) ?? ChartValue.Null()) ?? 0m
        }).Where(item => item.Value > 0).ToList();
        var total = items.Sum(item => item.Value);
        if (total <= 0) return;
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
        var angle = -Math.PI / 2d;
        for (var index = 0; index < items.Count; index++)
        {
            var sweep = roseMode ? 2d * Math.PI / items.Count : 2d * Math.PI * (double)(items[index].Value / total);
            var end = angle + sweep;
            var large = sweep > Math.PI ? 1 : 0;
            var sliceOuter = roseMode ? Math.Max(inner + 2m, outer * (decimal)Math.Sqrt((double)(items[index].Value / maximum))) : outer;
            var outerStart = Point(cx, cy, sliceOuter, angle);
            var outerEnd = Point(cx, cy, sliceOuter, end);
            var defaultColor = plan.Palette.FirstOrDefault(item => item.SeriesKey == items[index].Label)?.Color ?? "#5470c6";
            var color = EncodingText(items[index].Datum, ConditionalEncodingChannel.Color) is { } candidate ? SafePaint(candidate, defaultColor) : defaultColor;
            string path;
            if (inner > 0)
            {
                var innerEnd = Point(cx, cy, inner, end);
                var innerStart = Point(cx, cy, inner, angle);
                path = $"M {outerStart} A {N(sliceOuter)} {N(sliceOuter)} 0 {large} 1 {outerEnd} L {innerEnd} A {N(inner)} {N(inner)} 0 {large} 0 {innerStart} Z";
            }
            else path = $"M {N(cx)} {N(cy)} L {outerStart} A {N(sliceOuter)} {N(sliceOuter)} 0 {large} 1 {outerEnd} Z";
            builder.AppendLine($"<path d='{path}' fill='{Esc(color)}' stroke='white' stroke-width='2' data-row-index='{items[index].Datum.RowIndex}'><title>{Esc(items[index].Label)}: {N(items[index].Value)}</title></path>");
            if (showLabels)
            {
                var midpoint = angle + sweep / 2d;
                var label = $"{items[index].Label}: {FormatDataLabel(items[index].Value, DataFormat(plan), total)}";
                var anchor = PointCoordinates(cx, cy, sliceOuter + 2m, midpoint);
                var elbow = PointCoordinates(cx, cy, outer + 11m, midpoint);
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
                builder.AppendLine($"<path class='plot-arc-label-leader' d='M {N(item.Label.AnchorX)} {N(item.Label.AnchorY)} L {N(item.Label.ElbowX)} {N(item.Y)} L {N(lineEndX)} {N(item.Y)}' fill='none' stroke='#9ca3af' stroke-width='1'/>");
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

    private static decimal MapX(decimal value, ResolvedScale scale, decimal plotWidth)
    {
        var (minimum, maximum) = Domain(scale);
        return Left + Ratio(value, minimum, maximum, scale.Kind) * plotWidth;
    }

    /// <summary>Maps a transposed positional value onto the vertical axis in category order, top to bottom.</summary>
    private static decimal MapVertical(decimal value, ResolvedScale scale, decimal plotHeight)
    {
        var (minimum, maximum) = Domain(scale);
        return Top + Ratio(value, minimum, maximum, scale.Kind) * plotHeight;
    }

    private static decimal MapY(decimal value, ResolvedScale scale, decimal plotHeight)
    {
        var (minimum, maximum) = Domain(scale);
        return Top + plotHeight - Ratio(value, minimum, maximum, scale.Kind) * plotHeight;
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

    private static ChartValue? Channel(ResolvedDatum datum, FieldChannel channel) => datum.Channels.FirstOrDefault(item => item.Channel == channel)?.Value;
    private static string? DisplayChannel(ResolvedDatum datum, FieldChannel channel)
    {
        var value = datum.Channels.FirstOrDefault(item => item.Channel == channel);
        return value is null ? null : value.DisplayValue ?? PlotPlanResolver.Display(value.Value);
    }
    private static ChartValue? Encoding(ResolvedDatum datum, ConditionalEncodingChannel channel) => datum.Encodings.IsDefault
        ? null : datum.Encodings.FirstOrDefault(item => item.Channel == channel)?.Value;
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
        var value = tokens.FirstOrDefault(token => token.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;
        return value is not null && !value.Equals("OFF", StringComparison.OrdinalIgnoreCase) &&
            !value.Equals("FALSE", StringComparison.OrdinalIgnoreCase) && value != "0";
    }
    private static string? Style(PlotPlan plan, string name)
        => plan.Style.FirstOrDefault(token => token.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;
    private static string? DataFormat(PlotPlan plan) => Style(plan, "DATA_LABELS:FORMAT") ?? Style(plan, "FORMAT");
    private static string? LayerStyle(ResolvedMarkLayer layer, string name)
        => layer.Style.FirstOrDefault(token => token.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;
    private static string ResolveDatumColor(PlotPlan plan, ResolvedDatum datum, string fallback)
    {
        if (EncodingText(datum, ConditionalEncodingChannel.Color) is { } conditional)
            return SafePaint(conditional, fallback);
        var scale = plan.Scales.FirstOrDefault(item => item.Channel == FieldChannel.Color && item.ColorRange is not null);
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

    private static decimal ColorMidOffset(ResolvedScale scale, ResolvedColorRange range)
    {
        if (range.Midpoint is not { } midpoint) return .5m;
        var (minimum, maximum) = Domain(scale);
        return Math.Clamp(Ratio(midpoint, minimum, maximum, scale.Kind), 0m, 1m);
    }

    private static string InterpolateColor(string low, string high, decimal ratio)
    {
        ratio = Math.Clamp(ratio, 0m, 1m);
        static int Component(string color, int offset) => int.Parse(color.AsSpan(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        static int Mix(int first, int second, decimal amount) =>
            (int)Math.Round(first + (second - first) * amount, MidpointRounding.AwayFromZero);
        return $"#{Mix(Component(low, 1), Component(high, 1), ratio):X2}{Mix(Component(low, 3), Component(high, 3), ratio):X2}{Mix(Component(low, 5), Component(high, 5), ratio):X2}";
    }

    private static string SafePaint(string? candidate, string fallback)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return fallback;
        var value = candidate.Trim();
        if (value.Length is 4 or 7 && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit)) return value;
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
        static int Component(string value, int offset) => Convert.ToInt32(value.Substring(offset, 2), 16);
        if (from.Length != 7 || to.Length != 7 || from[0] != '#' || to[0] != '#') return to;
        ratio = Math.Clamp(ratio, 0m, 1m);
        var red = (int)Math.Round(Component(from, 1) + (Component(to, 1) - Component(from, 1)) * ratio);
        var green = (int)Math.Round(Component(from, 3) + (Component(to, 3) - Component(from, 3)) * ratio);
        var blue = (int)Math.Round(Component(from, 5) + (Component(to, 5) - Component(from, 5)) * ratio);
        return $"#{red:X2}{green:X2}{blue:X2}";
    }
    private static string DefaultColor(int index) => new[] { "#5470c6", "#91cc75", "#fac858", "#ee6666", "#73c0de", "#3ba272", "#fc8452" }[Math.Abs(index) % 7];
    private static (decimal X, decimal Y) PointCoordinates(decimal cx, decimal cy, decimal radius, double angle) =>
        (cx + radius * (decimal)Math.Cos(angle), cy + radius * (decimal)Math.Sin(angle));
    private static string Point(decimal cx, decimal cy, decimal radius, double angle) => $"{N(cx + radius * (decimal)Math.Cos(angle))} {N(cy + radius * (decimal)Math.Sin(angle))}";
    private static string N(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length] + "…";
    private static string Esc(string value) => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("'", "&apos;").Replace("\"", "&quot;");
}
