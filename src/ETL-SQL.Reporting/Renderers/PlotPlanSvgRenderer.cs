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
        var plotWidth = plan.Bounds.Width - Left - Right;
        var plotHeight = plan.Bounds.Height - Top - Bottom;
        builder.AppendLine($"<line x1='{N(Left)}' y1='{N(Top)}' x2='{N(Left)}' y2='{N(Top + plotHeight)}' stroke='#bbb'/>");
        builder.AppendLine($"<line x1='{N(Left)}' y1='{N(Top + plotHeight)}' x2='{N(Left + plotWidth)}' y2='{N(Top + plotHeight)}' stroke='#bbb'/>");
        var xScale = plan.Scales.FirstOrDefault(scale => scale.Channel == FieldChannel.X);
        var categories = xScale?.Categories ?? ImmutableArray<string>.Empty;
        var yScale = plan.Scales.FirstOrDefault(scale => scale.Channel == FieldChannel.Y);
        var y2Scale = plan.Scales.FirstOrDefault(scale => scale.Channel == FieldChannel.Y2);
        var rectLayers = plan.Layers.Where(layer => layer.Mark == MarkKind.Rect).ToList();
        var stacked = IsEnabled(plan.Style, "STACKED");
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

        foreach (var layer in plan.Layers)
        {
            var color = plan.Palette.FirstOrDefault(item => item.SeriesKey == layer.SeriesKey)?.Color ?? "#5470c6";
            switch (layer.Mark)
            {
                case MarkKind.Rect:
                    RenderRects(builder, plan, layer, rectLayers, stacked, categories.Length, plotWidth, plotHeight, yScale, color, showLabels);
                    break;
                case MarkKind.Line:
                    RenderLine(builder, plan, layer, categories.Length, plotWidth, plotHeight,
                        layer.Data.Any(datum => Channel(datum, FieldChannel.Y2) is not null) ? y2Scale ?? yScale : yScale, color, showLabels);
                    break;
                case MarkKind.Area:
                    RenderArea(builder, layer, categories.Length, plotWidth, plotHeight, yScale, color);
                    break;
                case MarkKind.Point:
                    RenderPoints(builder, plan, layer, plotWidth, plotHeight, xScale, yScale, color);
                    break;
                case MarkKind.Rule:
                    RenderRule(builder, layer, plotWidth, plotHeight, yScale);
                    break;
                case MarkKind.Text:
                    RenderText(builder, layer, categories.Length, plotWidth, plotHeight,
                        layer.Data.Any(datum => Channel(datum, FieldChannel.Y2) is not null) ? y2Scale ?? yScale : yScale, color);
                    break;
            }
        }
        builder.AppendLine("</g>");

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

        var xTitle = Style(plan, "axis:x:title");
        var yTitle = Style(plan, "axis:y:title");
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
        var categories = plan.Scales.FirstOrDefault(scale => scale.Channel == FieldChannel.X)?.Categories ?? [];
        var rectLayers = plan.Layers.Where(layer => layer.Mark == MarkKind.Rect).ToList();
        var slot = plotHeight / Math.Max(1, categories.Length);
        var showLabels = IsEnabled(plan.Style, "DATA_LABELS");
        builder.AppendLine($"<line x1='{N(Left)}' y1='{N(Top)}' x2='{N(Left)}' y2='{N(Top + plotHeight)}' stroke='#bbb'/>");
        builder.AppendLine($"<line x1='{N(Left)}' y1='{N(Top + plotHeight)}' x2='{N(Left + plotWidth)}' y2='{N(Top + plotHeight)}' stroke='#bbb'/>");
        foreach (var layer in plan.Layers)
        {
            var scale = layer.Data.Any(datum => Channel(datum, FieldChannel.Y2) is not null)
                ? plan.Scales.FirstOrDefault(item => item.Channel == FieldChannel.Y2)
                : plan.Scales.FirstOrDefault(item => item.Channel == FieldChannel.Y);
            if (scale is null) continue;
            var color = plan.Palette.FirstOrDefault(item => item.SeriesKey == layer.SeriesKey)?.Color ?? "#5470c6";
            var points = new List<string>();
            var pointCoordinates = new List<(decimal X, decimal Y)>();
            for (var index = 0; index < layer.Data.Length; index++)
            {
                var datum = layer.Data[index];
                var value = PlotPlanResolver.Number(Channel(datum, FieldChannel.Y) ?? Channel(datum, FieldChannel.Y2) ?? ChartValue.Null());
                if (datum.IsGap || !value.HasValue) continue;
                var x = MapHorizontal(value.Value, scale, plotWidth);
                var y = Top + slot * (index + .5m);
                var datumColor = EncodingText(datum, ConditionalEncodingChannel.Color) is { } candidate ? SafePaint(candidate, color) : color;
                if (layer.Mark == MarkKind.Rect)
                {
                    var baseline = MapHorizontal(0m, scale, plotWidth);
                    var layerIndex = Math.Max(0, rectLayers.IndexOf(layer));
                    var barHeight = slot * .72m / Math.Max(1, rectLayers.Count);
                    var top = y - slot * .36m + layerIndex * barHeight;
                    var barLeft = Math.Min(x, baseline);
                    var barWidth = Math.Max(1m, Math.Abs(x - baseline));
                    builder.AppendLine($"<rect x='{N(barLeft)}' y='{N(top)}' width='{N(barWidth)}' height='{N(Math.Max(1m, barHeight - 1m))}' fill='{Esc(datumColor)}' data-row-index='{datum.RowIndex}'><title>{Esc(FormatDataLabel(value.Value, Style(plan, "DATA_LABELS:FORMAT")))}</title></rect>");
                    if (showLabels)
                        builder.AppendLine($"<text x='{N(x + 4m)}' y='{N(y + 3m)}' text-anchor='start' font-size='{Esc(Style(plan, "DATA_LABELS:FONT_SIZE") ?? "9")}' fill='{Esc(SafePaint(Style(plan, "DATA_LABELS:COLOR"), "#333"))}'>{Esc(FormatDataLabel(value.Value, Style(plan, "DATA_LABELS:FORMAT")))}</text>");
                }
                else if (layer.Mark == MarkKind.Rule)
                    builder.AppendLine($"<line x1='{N(x)}' y1='{N(Top)}' x2='{N(x)}' y2='{N(Top + plotHeight)}' stroke='{Esc(datumColor)}' stroke-dasharray='6 4'/>");
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
        if (!LegendEnabled(plan) || plan.Legend.Length <= 1) return;
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
        var denominator = Math.Max(1, categoryCount - 1);
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
            segments[^1].Add((Left + plotWidth * index / denominator, MapY(value.Value, scale, plotHeight)));
        }
        foreach (var points in segments.Where(segment => segment.Count > 1))
        {
            var path = "M " + string.Join(" L ", points.Select(point => $"{N(point.X)} {N(point.Y)}"));
            builder.AppendLine($"<path d='{path} L {N(points[^1].X)} {N(Top + plotHeight)} L {N(points[0].X)} {N(Top + plotHeight)} Z' fill='{Esc(color)}' fill-opacity='.2'/>");
            builder.AppendLine($"<path d='{path}' fill='none' stroke='{Esc(color)}' stroke-width='2'/>");
        }
    }

    private static void RenderRects(StringBuilder builder, PlotPlan plan, ResolvedMarkLayer layer, IReadOnlyList<ResolvedMarkLayer> layers, bool stacked,
        int categoryCount, decimal plotWidth, decimal plotHeight, ResolvedScale? scale, string color, bool showLabels)
    {
        if (categoryCount == 0 || scale is null) return;
        var layerIndex = Enumerable.Range(0, layers.Count).First(index => ReferenceEquals(layers[index], layer));
        var layerCount = Math.Max(1, layers.Count);
        var slot = plotWidth / categoryCount;
        var groupWidth = slot * 0.75m;
        var barWidth = stacked ? groupWidth : groupWidth / layerCount;
        for (var index = 0; index < layer.Data.Length; index++)
        {
            var datum = layer.Data[index];
            var value = PlotPlanResolver.Number(Channel(datum, FieldChannel.Y) ?? Channel(datum, FieldChannel.Y2) ?? ChartValue.Null());
            if (datum.IsGap || !value.HasValue) continue;
            var start = stacked
                ? layers.Take(layerIndex).Select(previous => index < previous.Data.Length
                    ? PlotPlanResolver.Number(Channel(previous.Data[index], FieldChannel.Y) ?? ChartValue.Null()) ?? 0m
                    : 0m).Where(previous => Math.Sign(previous) == Math.Sign(value.Value)).Sum()
                : 0m;
            var startY = MapY(start, scale, plotHeight);
            var endY = MapY(start + value.Value, scale, plotHeight);
            var x = Left + slot * index + (slot - groupWidth) / 2m + (stacked ? 0m : layerIndex * barWidth);
            var datumColor = EncodingText(datum, ConditionalEncodingChannel.Color) is { } candidate ? SafePaint(candidate, color) : color;
            var opacity = EncodingNumber(datum, ConditionalEncodingChannel.Opacity) ?? 1m;
            var top = Math.Min(startY, endY);
            var barHeight = Math.Max(1m, Math.Abs(startY - endY));
            builder.AppendLine($"<rect x='{N(x)}' y='{N(top)}' width='{N(Math.Max(1m, barWidth - 1m))}' height='{N(barHeight)}' fill='{Esc(datumColor)}' fill-opacity='{N(Math.Clamp(opacity, 0m, 1m))}' data-row-index='{datum.RowIndex}'><title>{Esc(FormatDataLabel(value.Value, Style(plan, "DATA_LABELS:FORMAT")))}</title></rect>");
            if (showLabels)
            {
                var position = Style(plan, "DATA_LABELS:POSITION") ?? "OUTSIDE";
                var labelColor = SafePaint(Style(plan, "DATA_LABELS:COLOR"), "#444");
                var labelSize = Style(plan, "DATA_LABELS:FONT_SIZE") ?? "9";
                var labelWeight = Style(plan, "DATA_LABELS:FONT_WEIGHT");
                var label = FormatDataLabel(value.Value, Style(plan, "DATA_LABELS:FORMAT"));
                var labelX = x + barWidth / 2m;
                var labelY = top - 3m;
                var anchor = "middle";
                if (position.Contains("RIGHT", StringComparison.OrdinalIgnoreCase))
                {
                    labelX = x + barWidth + 3m;
                    labelY = top + barHeight / 2m + 3m;
                    anchor = "start";
                }
                else if (position.Contains("INSIDE", StringComparison.OrdinalIgnoreCase))
                {
                    labelY = value.Value >= 0m ? top + 13m : top + barHeight - 4m;
                    labelColor = SafePaint(Style(plan, "DATA_LABELS:COLOR"), "white");
                }
                builder.AppendLine($"<text x='{N(labelX)}' y='{N(labelY)}' text-anchor='{anchor}' font-size='{Esc(labelSize)}' fill='{Esc(labelColor)}'{(string.IsNullOrWhiteSpace(labelWeight) ? string.Empty : $" font-weight='{Esc(labelWeight)}'")}>{Esc(label)}</text>");
            }
        }
    }

    private static void RenderLine(StringBuilder builder, PlotPlan plan, ResolvedMarkLayer layer, int categoryCount,
        decimal plotWidth, decimal plotHeight, ResolvedScale? scale, string color, bool showLabels)
    {
        if (scale is null || layer.Data.IsDefaultOrEmpty) return;
        var denominator = Math.Max(1, categoryCount - 1);
        var segment = new List<string>();
        void Flush()
        {
            if (segment.Count > 1) builder.AppendLine($"<path d='M {string.Join(" L ", segment.Select(point => point.Replace(',', ' ')))}' fill='none' stroke='{Esc(color)}' stroke-width='2' stroke-linejoin='round' stroke-linecap='round'/>");
            segment.Clear();
        }
        for (var index = 0; index < layer.Data.Length; index++)
        {
            var datum = layer.Data[index];
            var value = PlotPlanResolver.Number(Channel(datum, FieldChannel.Y) ?? Channel(datum, FieldChannel.Y2) ?? ChartValue.Null());
            if (datum.IsGap || !value.HasValue) { Flush(); continue; }
            var x = Left + plotWidth * index / denominator;
            var y = MapY(value.Value, scale, plotHeight);
            segment.Add($"{N(x)},{N(y)}");
            builder.AppendLine($"<circle cx='{N(x)}' cy='{N(y)}' r='3' fill='{Esc(color)}' data-row-index='{datum.RowIndex}'><title>{Esc(FormatDataLabel(value.Value, Style(plan, "DATA_LABELS:FORMAT")))}</title></circle>");
            if (showLabels)
                builder.AppendLine($"<text x='{N(x)}' y='{N(y - 6m)}' text-anchor='middle' font-size='{Esc(Style(plan, "DATA_LABELS:FONT_SIZE") ?? "9")}' fill='{Esc(SafePaint(Style(plan, "DATA_LABELS:COLOR"), "#444"))}'>{Esc(FormatDataLabel(value.Value, Style(plan, "DATA_LABELS:FORMAT")))}</text>");
        }
        Flush();
    }

    private static void RenderPoints(StringBuilder builder, PlotPlan plan, ResolvedMarkLayer layer, decimal plotWidth,
        decimal plotHeight, ResolvedScale? xScale, ResolvedScale? yScale, string color)
    {
        if (xScale is null || yScale is null) return;
        var sizes = layer.Data.Select(datum => PlotPlanResolver.Number(Channel(datum, FieldChannel.Size) ?? ChartValue.Null()))
            .Where(value => value.HasValue).Select(value => value!.Value).ToList();
        var minimumSize = sizes.DefaultIfEmpty(0m).Min();
        var maximumSize = sizes.DefaultIfEmpty(0m).Max();
        foreach (var datum in layer.Data.Where(item => !item.IsGap))
        {
            var xValue = PlotPlanResolver.Number(Channel(datum, FieldChannel.X) ?? ChartValue.Null());
            var yValue = PlotPlanResolver.Number(Channel(datum, FieldChannel.Y) ?? ChartValue.Null());
            if (!xValue.HasValue || !yValue.HasValue) continue;
            var datumColor = EncodingText(datum, ConditionalEncodingChannel.Color) is { } candidate ? SafePaint(candidate, color) : color;
            var rawSize = PlotPlanResolver.Number(Channel(datum, FieldChannel.Size) ?? ChartValue.Null());
            var radius = EncodingNumber(datum, ConditionalEncodingChannel.Size)
                ?? NormalizePointRadius(rawSize, minimumSize, maximumSize);
            var opacity = EncodingNumber(datum, ConditionalEncodingChannel.Opacity) ?? 1m;
            var label = Channel(datum, FieldChannel.Text) is { } labelValue ? PlotPlanResolver.Display(labelValue) : null;
            var x = MapX(xValue.Value, xScale, plotWidth);
            var y = MapY(yValue.Value, yScale, plotHeight);
            builder.AppendLine($"<circle cx='{N(x)}' cy='{N(y)}' r='{N(Math.Clamp(radius, 1m, 30m))}' fill='{Esc(datumColor)}' fill-opacity='{N(Math.Clamp(opacity, 0m, 1m))}' data-row-index='{datum.RowIndex}'>{(string.IsNullOrWhiteSpace(label) ? string.Empty : $"<title>{Esc(label)}</title>")}</circle>");
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
        var center = plan.Bounds.Width / 2m;
        var availableWidth = plan.Bounds.Width - Left - Right;
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
            builder.AppendLine($"<text x='{N(center)}' y='{N(y1 + rowHeight / 2m + 4m)}' text-anchor='middle' font-size='10' fill='white'>{Esc(label)} · {N(values[index])}</text>");
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
        var cx = plan.Bounds.Width / 2m;
        var cy = plan.Bounds.Height * .72m;
        var radius = Math.Min(plan.Bounds.Width * .34m, plan.Bounds.Height * .52m);
        var start = Math.PI;
        var end = 0d;
        var valueEnd = start + Math.PI * (double)ratio;
        builder.AppendLine($"<path d='{ArcPath(cx, cy, radius, start, end)}' fill='none' stroke='#e5e7eb' stroke-width='24' stroke-linecap='round'/>");
        builder.AppendLine($"<path d='{ArcPath(cx, cy, radius, start, valueEnd)}' fill='none' stroke='{Esc(plan.Palette.FirstOrDefault()?.Color ?? "#5470c6")}' stroke-width='24' stroke-linecap='round'/>");
        var needle = PointCoordinates(cx, cy, radius - 14m, valueEnd);
        builder.AppendLine($"<line x1='{N(cx)}' y1='{N(cy)}' x2='{N(needle.X)}' y2='{N(needle.Y)}' stroke='#374151' stroke-width='3'/><circle cx='{N(cx)}' cy='{N(cy)}' r='6' fill='#374151'/>");
        if (PlotPlanResolver.Number(Channel(datum, FieldChannel.Detail) ?? ChartValue.Null()) is { } goal)
        {
            var goalRatio = maximum <= minimum ? 0m : Math.Clamp((goal - minimum) / (maximum - minimum), 0m, 1m);
            var goalAngle = start + Math.PI * (double)goalRatio;
            var goalInner = PointCoordinates(cx, cy, radius - 18m, goalAngle);
            var goalOuter = PointCoordinates(cx, cy, radius + 18m, goalAngle);
            builder.AppendLine($"<line x1='{N(goalInner.X)}' y1='{N(goalInner.Y)}' x2='{N(goalOuter.X)}' y2='{N(goalOuter.Y)}' stroke='#111827' stroke-width='3'><title>Goal: {N(goal)}</title></line>");
        }
        var label = DisplayChannel(datum, FieldChannel.Text);
        builder.AppendLine($"<text x='{N(cx)}' y='{N(cy + 38m)}' text-anchor='middle' font-size='18' font-weight='bold' fill='#333'>{N(value)}</text>");
        if (!string.IsNullOrWhiteSpace(label)) builder.AppendLine($"<text x='{N(cx)}' y='{N(cy + 56m)}' text-anchor='middle' font-size='10' fill='#666'>{Esc(label)}</text>");
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
        foreach (var datum in layer.Data.Where(item => !item.IsGap))
        {
            var x = DisplayChannel(datum, FieldChannel.X);
            var y = DisplayChannel(datum, FieldChannel.Y);
            var xIndex = x is null ? -1 : xCategories.IndexOf(x);
            var yIndex = y is null ? -1 : yCategories.IndexOf(y);
            var value = PlotPlanResolver.Number(Channel(datum, FieldChannel.Size) ?? ChartValue.Null());
            if (xIndex < 0 || yIndex < 0 || !value.HasValue) continue;
            var ratio = maximum <= minimum ? 1m : Math.Clamp((value.Value - minimum) / (maximum - minimum), 0m, 1m);
            var opacity = .18m + ratio * .82m;
            var cellX = Left + xIndex * cellWidth;
            var cellY = Top + yIndex * cellHeight;
            builder.AppendLine($"<rect x='{N(cellX)}' y='{N(cellY)}' width='{N(cellWidth - 1m)}' height='{N(cellHeight - 1m)}' fill='#2563eb' fill-opacity='{N(opacity)}' data-row-index='{datum.RowIndex}'><title>{Esc(x!)} / {Esc(y!)}: {N(value.Value)}</title></rect>");
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
        decimal plotHeight, ResolvedScale? scale)
    {
        if (scale is null) return;
        var value = layer.Data.Select(datum => Channel(datum, FieldChannel.Y)).FirstOrDefault(item => item is not null && item.Kind != ChartValueKind.Null);
        if (value is null) return;
        var y = MapY(PlotPlanResolver.Number(value) ?? 0m, scale, plotHeight);
        var color = SafePaint(layer.Style.FirstOrDefault(token => token.Name.Equals("color", StringComparison.OrdinalIgnoreCase))?.Value, "#888888");
        var label = layer.Style.FirstOrDefault(token => token.Name.Equals("label", StringComparison.OrdinalIgnoreCase))?.Value;
        builder.AppendLine($"<line x1='{N(Left)}' y1='{N(y)}' x2='{N(Left + plotWidth)}' y2='{N(y)}' stroke='{Esc(color)}' stroke-width='2' stroke-dasharray='6 4'/>");
        if (!string.IsNullOrWhiteSpace(label))
            builder.AppendLine($"<text x='{N(Left + plotWidth - 3m)}' y='{N(y - 4m)}' text-anchor='end' font-size='9' fill='{Esc(color)}'>{Esc(label)}</text>");
    }

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
            var x = Left + plotWidth * index / denominator;
            var y = MapY(value.Value, scale, plotHeight);
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
        var cx = plan.Bounds.Width / 2m;
        var cy = plan.Bounds.Height / 2m;
        var outer = Math.Min(plan.Bounds.Width, plan.Bounds.Height) / 2m - 50m;
        var inner = (plan.Coordinate?.InnerRadius ?? 0m) * outer;
        var angle = -Math.PI / 2d;
        for (var index = 0; index < items.Count; index++)
        {
            var sweep = 2d * Math.PI * (double)(items[index].Value / total);
            var end = angle + sweep;
            var large = sweep > Math.PI ? 1 : 0;
            var outerStart = Point(cx, cy, outer, angle);
            var outerEnd = Point(cx, cy, outer, end);
            var defaultColor = plan.Palette.FirstOrDefault(item => item.SeriesKey == items[index].Label)?.Color ?? "#5470c6";
            var color = EncodingText(items[index].Datum, ConditionalEncodingChannel.Color) is { } candidate ? SafePaint(candidate, defaultColor) : defaultColor;
            string path;
            if (inner > 0)
            {
                var innerEnd = Point(cx, cy, inner, end);
                var innerStart = Point(cx, cy, inner, angle);
                path = $"M {outerStart} A {N(outer)} {N(outer)} 0 {large} 1 {outerEnd} L {innerEnd} A {N(inner)} {N(inner)} 0 {large} 0 {innerStart} Z";
            }
            else path = $"M {N(cx)} {N(cy)} L {outerStart} A {N(outer)} {N(outer)} 0 {large} 1 {outerEnd} Z";
            builder.AppendLine($"<path d='{path}' fill='{Esc(color)}' stroke='white' stroke-width='2' data-row-index='{items[index].Datum.RowIndex}'><title>{Esc(items[index].Label)}: {N(items[index].Value)}</title></path>");
            if (IsEnabled(plan.Style, "DATA_LABELS"))
            {
                var midpoint = angle + sweep / 2d;
                var labelRadius = inner + (outer - inner) / 2m;
                var labelPoint = PointCoordinates(cx, cy, labelRadius, midpoint);
                var label = $"{items[index].Label}: {FormatDataLabel(items[index].Value, Style(plan, "DATA_LABELS:FORMAT"), total)}";
                builder.AppendLine($"<text x='{N(labelPoint.X)}' y='{N(labelPoint.Y)}' text-anchor='middle' font-size='{Esc(Style(plan, "DATA_LABELS:FONT_SIZE") ?? "9")}' fill='{Esc(SafePaint(Style(plan, "DATA_LABELS:COLOR"), "white"))}'>{Esc(label)}</text>");
            }
            angle = end;
        }
        RenderLegend(builder, plan);
    }

    private static decimal MapX(decimal value, ResolvedScale scale, decimal plotWidth)
    {
        var (minimum, maximum) = Domain(scale);
        return Left + Ratio(value, minimum, maximum, scale.Kind) * plotWidth;
    }

    private static decimal MapY(decimal value, ResolvedScale scale, decimal plotHeight)
    {
        var (minimum, maximum) = Domain(scale);
        return Top + plotHeight - Ratio(value, minimum, maximum, scale.Kind) * plotHeight;
    }

    private static (decimal Minimum, decimal Maximum) Domain(ResolvedScale scale)
    {
        var minimum = scale.Domain.Length > 0 ? PlotPlanResolver.Number(scale.Domain[0]) ?? 0m : 0m;
        var maximum = scale.Domain.Length > 1 ? PlotPlanResolver.Number(scale.Domain[^1]) ?? minimum + 1m : minimum + 1m;
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
    private static string? LayerStyle(ResolvedMarkLayer layer, string name)
        => layer.Style.FirstOrDefault(token => token.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;
    private static string SafePaint(string? candidate, string fallback)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return fallback;
        var value = candidate.Trim();
        if (value.Length is 4 or 7 && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit)) return value;
        return value.ToLowerInvariant() switch
        {
            "white" or "black" or "transparent" => value.ToLowerInvariant(),
            _ => fallback
        };
    }
    private static string DefaultColor(int index) => new[] { "#5470c6", "#91cc75", "#fac858", "#ee6666", "#73c0de", "#3ba272", "#fc8452" }[Math.Abs(index) % 7];
    private static (decimal X, decimal Y) PointCoordinates(decimal cx, decimal cy, decimal radius, double angle) =>
        (cx + radius * (decimal)Math.Cos(angle), cy + radius * (decimal)Math.Sin(angle));
    private static string Point(decimal cx, decimal cy, decimal radius, double angle) => $"{N(cx + radius * (decimal)Math.Cos(angle))} {N(cy + radius * (decimal)Math.Sin(angle))}";
    private static string N(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length] + "…";
    private static string Esc(string value) => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("'", "&apos;").Replace("\"", "&quot;");
}
