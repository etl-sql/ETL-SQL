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
        builder.AppendLine($"<text x='{N(width / 2)}' y='22' text-anchor='middle' font-size='13' font-weight='bold' fill='#333'>{Esc(plan.Title ?? plan.SpecId)}</text>");

        if (plan.Layers.Any(layer => layer.Mark == MarkKind.Arc)) RenderArcs(builder, plan);
        else RenderCartesian(builder, plan);
        builder.AppendLine("</svg>");
        return builder.ToString();
    }

    private static void RenderCartesian(StringBuilder builder, PlotPlan plan)
    {
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
                    RenderRects(builder, layer, rectLayers, stacked, categories.Length, plotWidth, plotHeight, yScale, color, showLabels);
                    break;
                case MarkKind.Line:
                    RenderLine(builder, layer, categories.Length, plotWidth, plotHeight,
                        layer.Data.Any(datum => Channel(datum, FieldChannel.Y2) is not null) ? y2Scale ?? yScale : yScale, color, showLabels);
                    break;
                case MarkKind.Area:
                    RenderArea(builder, layer, categories.Length, plotWidth, plotHeight, yScale, color);
                    break;
                case MarkKind.Point:
                    RenderPoints(builder, layer, plotWidth, plotHeight, xScale, yScale, color);
                    break;
                case MarkKind.Rule:
                    RenderRule(builder, layer, plotWidth, plotHeight, yScale);
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

        if (plan.Legend.Length > 1)
        {
            var legendX = Left;
            foreach (var entry in plan.Legend)
            {
                builder.AppendLine($"<rect x='{N(legendX)}' y='27' width='9' height='9' fill='{Esc(SafePaint(entry.Color, "#5470c6"))}'/>");
                builder.AppendLine($"<text x='{N(legendX + 13m)}' y='35' font-size='9' fill='#444'>{Esc(entry.Label)}</text>");
                legendX += Math.Max(65m, entry.Label.Length * 6m + 25m);
            }
        }
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
        var points = new List<(decimal X, decimal Y, decimal Value)>();
        var denominator = Math.Max(1, layer.Data.Length - 1);
        for (var index = 0; index < layer.Data.Length; index++)
        {
            var datum = layer.Data[index];
            var value = PlotPlanResolver.Number(Channel(datum, FieldChannel.Y) ?? ChartValue.Null());
            if (datum.IsGap || !value.HasValue) continue;
            var x = pad + (width - 2m * pad) * index / denominator;
            var (minimum, maximum) = Domain(yScale);
            var y = pad + (height - 2m * pad) - (value.Value - minimum) / (maximum - minimum) * (height - 2m * pad);
            points.Add((x, Math.Clamp(y, pad, height - pad), value.Value));
        }
        if (points.Count == 0) return;
        if (layer.Mark == MarkKind.Rect)
        {
            var slot = (width - 2m * pad) / Math.Max(1, layer.Data.Length);
            var baseline = height - pad;
            foreach (var point in points)
                builder.AppendLine($"<rect x='{N(point.X - slot * .35m)}' y='{N(point.Y)}' width='{N(Math.Max(1m, slot * .7m))}' height='{N(Math.Max(1m, baseline - point.Y))}' rx='1' fill='{Esc(color)}'/>");
            return;
        }
        var path = "M " + string.Join(" L ", points.Select(point => $"{N(point.X)} {N(point.Y)}"));
        if (layer.Mark == MarkKind.Area)
            builder.AppendLine($"<path d='{path} L {N(points[^1].X)} {N(height - pad)} L {N(points[0].X)} {N(height - pad)} Z' fill='{Esc(color)}' fill-opacity='.22'/>");
        builder.AppendLine($"<path d='{path}' fill='none' stroke='{Esc(color)}' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'/>");
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
        var points = layer.Data.Select((datum, index) => new
        {
            Datum = datum,
            X = Left + plotWidth * index / denominator,
            Value = PlotPlanResolver.Number(Channel(datum, FieldChannel.Y) ?? ChartValue.Null())
        }).Where(item => !item.Datum.IsGap && item.Value.HasValue)
          .Select(item => (item.X, Y: MapY(item.Value!.Value, scale, plotHeight))).ToList();
        if (points.Count < 2) return;
        var path = "M " + string.Join(" L ", points.Select(point => $"{N(point.X)} {N(point.Y)}"));
        builder.AppendLine($"<path d='{path} L {N(points[^1].X)} {N(Top + plotHeight)} L {N(points[0].X)} {N(Top + plotHeight)} Z' fill='{Esc(color)}' fill-opacity='.2'/>");
        builder.AppendLine($"<path d='{path}' fill='none' stroke='{Esc(color)}' stroke-width='2'/>");
    }

    private static void RenderRects(StringBuilder builder, ResolvedMarkLayer layer, IReadOnlyList<ResolvedMarkLayer> layers, bool stacked,
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
            builder.AppendLine($"<rect x='{N(x)}' y='{N(Math.Min(startY, endY))}' width='{N(Math.Max(1m, barWidth - 1m))}' height='{N(Math.Max(1m, Math.Abs(startY - endY)))}' fill='{Esc(color)}'/>");
            if (showLabels)
                builder.AppendLine($"<text x='{N(x + barWidth / 2m)}' y='{N(Math.Min(startY, endY) - 3m)}' text-anchor='middle' font-size='9' fill='#444'>{N(value.Value)}</text>");
        }
    }

    private static void RenderLine(StringBuilder builder, ResolvedMarkLayer layer, int categoryCount,
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
            builder.AppendLine($"<circle cx='{N(x)}' cy='{N(y)}' r='3' fill='{Esc(color)}'/>");
            if (showLabels)
                builder.AppendLine($"<text x='{N(x)}' y='{N(y - 6m)}' text-anchor='middle' font-size='9' fill='#444'>{N(value.Value)}</text>");
        }
        Flush();
    }

    private static void RenderPoints(StringBuilder builder, ResolvedMarkLayer layer, decimal plotWidth,
        decimal plotHeight, ResolvedScale? xScale, ResolvedScale? yScale, string color)
    {
        if (xScale is null || yScale is null) return;
        foreach (var datum in layer.Data.Where(item => !item.IsGap))
        {
            var xValue = PlotPlanResolver.Number(Channel(datum, FieldChannel.X) ?? ChartValue.Null());
            var yValue = PlotPlanResolver.Number(Channel(datum, FieldChannel.Y) ?? ChartValue.Null());
            if (!xValue.HasValue || !yValue.HasValue) continue;
            builder.AppendLine($"<circle cx='{N(MapX(xValue.Value, xScale, plotWidth))}' cy='{N(MapY(yValue.Value, yScale, plotHeight))}' r='4' fill='{Esc(color)}'/>");
        }
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

    private static void RenderArcs(StringBuilder builder, PlotPlan plan)
    {
        var layer = plan.Layers.First(item => item.Mark == MarkKind.Arc);
        var items = layer.Data.Where(datum => !datum.IsGap).Select(datum => new
        {
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
            var color = plan.Palette.FirstOrDefault(item => item.SeriesKey == items[index].Label)?.Color ?? "#5470c6";
            string path;
            if (inner > 0)
            {
                var innerEnd = Point(cx, cy, inner, end);
                var innerStart = Point(cx, cy, inner, angle);
                path = $"M {outerStart} A {N(outer)} {N(outer)} 0 {large} 1 {outerEnd} L {innerEnd} A {N(inner)} {N(inner)} 0 {large} 0 {innerStart} Z";
            }
            else path = $"M {N(cx)} {N(cy)} L {outerStart} A {N(outer)} {N(outer)} 0 {large} 1 {outerEnd} Z";
            builder.AppendLine($"<path d='{path}' fill='{Esc(color)}' stroke='white' stroke-width='2'/>");
            angle = end;
        }
    }

    private static decimal MapX(decimal value, ResolvedScale scale, decimal plotWidth)
    {
        var (minimum, maximum) = Domain(scale);
        return Left + (value - minimum) / (maximum - minimum) * plotWidth;
    }

    private static decimal MapY(decimal value, ResolvedScale scale, decimal plotHeight)
    {
        var (minimum, maximum) = Domain(scale);
        return Top + plotHeight - (value - minimum) / (maximum - minimum) * plotHeight;
    }

    private static (decimal Minimum, decimal Maximum) Domain(ResolvedScale scale)
    {
        var minimum = scale.Domain.Length > 0 ? PlotPlanResolver.Number(scale.Domain[0]) ?? 0m : 0m;
        var maximum = scale.Domain.Length > 1 ? PlotPlanResolver.Number(scale.Domain[^1]) ?? minimum + 1m : minimum + 1m;
        return maximum == minimum ? (minimum, minimum + 1m) : (minimum, maximum);
    }

    private static ChartValue? Channel(ResolvedDatum datum, FieldChannel channel) => datum.Channels.FirstOrDefault(item => item.Channel == channel)?.Value;
    private static bool IsEnabled(ImmutableArray<StyleToken> tokens, string name)
    {
        var value = tokens.FirstOrDefault(token => token.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;
        return value is not null && !value.Equals("OFF", StringComparison.OrdinalIgnoreCase) &&
            !value.Equals("FALSE", StringComparison.OrdinalIgnoreCase) && value != "0";
    }
    private static string? Style(PlotPlan plan, string name)
        => plan.Style.FirstOrDefault(token => token.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;
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
    private static string Point(decimal cx, decimal cy, decimal radius, double angle) => $"{N(cx + radius * (decimal)Math.Cos(angle))} {N(cy + radius * (decimal)Math.Sin(angle))}";
    private static string N(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length] + "…";
    private static string Esc(string value) => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("'", "&apos;").Replace("\"", "&quot;");
}
