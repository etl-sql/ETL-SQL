using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Reporting.Semantics.Runtime;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace ETL_SQL.Reporting.Renderers;

/// <summary>Semantic terminal compiler for renderer-neutral PlotPlans.</summary>
internal static class PlotPlanTerminalRenderer
{
    private static readonly string[] PointGlyphs = ["●", "◆", "▲", "■", "✚", "○", "◇"];
    private static readonly char[] Fractions = [' ', '▏', '▎', '▍', '▌', '▋', '▊', '▉'];

    public static IRenderable Render(PlotPlan plan, int width = 80)
    {
        plan.Validate();
        width = Math.Clamp(width, 40, 120);
        var facets = ResolveFacets(plan);
        var content = new List<IRenderable> { new Markup($"[grey]{Markup.Escape(plan.AccessibleSummary)}[/]") };
        if (facets.Count == 1)
        {
            content.Add(RenderFacet(plan, facets[0].Rows, width));
        }
        else
        {
            var columns = width >= 80 ? 2 : 1;
            var grid = new Grid();
            for (var index = 0; index < columns; index++) grid.AddColumn();
            for (var index = 0; index < facets.Count; index += columns)
            {
                var cells = new IRenderable[columns];
                for (var column = 0; column < columns; column++)
                {
                    if (index + column >= facets.Count) { cells[column] = new Text(""); continue; }
                    var facet = facets[index + column];
                    cells[column] = new Panel(RenderFacet(plan, facet.Rows, (width / columns) - 4))
                        .Header(Markup.Escape(facet.Label)).Border(BoxBorder.Square);
                }
                grid.AddRow(cells);
            }
            content.Add(grid);
        }
        if (LegendEnabled(plan) && (plan.Legend.Length > 1 || Style(plan, "LEGEND") is "ON" or "TRUE" or "1" || Style(plan, "LEGEND_TITLE") is not null))
        {
            var entries = LegendIsReverse(plan) ? plan.Legend.Reverse() : plan.Legend;
            var legendItems = string.Join("  ", entries.Select(entry =>
                $"[#{entry.Color.TrimStart('#')}]{PointGlyphs[entry.Order % PointGlyphs.Length]}[/] {Markup.Escape(entry.Label)} [grey]({Markup.Escape(entry.Color)})[/]"));
            var title = LegendTitle(plan);
            if (title is not null)
                content.Add(new Markup($"[bold]{Markup.Escape(title)}:[/] {legendItems}"));
            else
                content.Add(new Markup(legendItems));
        }
        var continuousColor = plan.Scales.FirstOrDefault(scale => scale.ColorRange is not null)?.ColorRange;
        if (continuousColor is not null)
        {
            var bins = continuousColor.Mid is null
                ? $"[{continuousColor.Low}]■[/] low  →  [{continuousColor.High}]■[/] high"
                : $"[{continuousColor.Low}]■[/] low  →  [{continuousColor.Mid}]■[/] midpoint  →  [{continuousColor.High}]■[/] high";
            content.Add(new Rows(new Markup(bins), new Markup($"[grey]{Markup.Escape(continuousColor.AccessibleDescription)}[/]")));
        }
        return new Panel(new Rows(content))
        {
            Header = new PanelHeader(Markup.Escape(plan.Title ?? plan.SpecId)),
            Border = BoxBorder.Rounded,
            Expand = false
        };
    }

    private static IRenderable RenderFacet(PlotPlan plan, HashSet<int> rows, int width)
    {
        var content = new List<IRenderable>();
        var activeLayers = plan.Layers.Where(item => item.Mark != MarkKind.Arc)
            .Select(layer =>
            {
                var data = layer.Data.Where(datum => rows.Contains(datum.RowIndex)).ToList();
                var series = plan.Series.FirstOrDefault(item => item.Key == layer.SeriesKey);
                var overlayType = !layer.Style.IsDefault ? layer.Style.FirstOrDefault(token => token.Name.Equals("overlayType", StringComparison.OrdinalIgnoreCase))?.Value : null;
                var rawLabel = !layer.Style.IsDefault ? layer.Style.FirstOrDefault(token => token.Name.Equals("label", StringComparison.OrdinalIgnoreCase))?.Value : null;
                var label = !string.IsNullOrWhiteSpace(rawLabel)
                    ? rawLabel
                    : overlayType switch
                    {
                        "ReferenceLine" => "Reference",
                        "ReferenceBand" => "Reference band",
                        _ => series?.Label ?? layer.Id
                    };
                var color = ResolveLayerColor(layer, plan);
                return (Layer: layer, Data: data, Series: series, Label: label, Color: color);
            })
            .Where(item => item.Data.Count > 0)
            .ToList();

        var bandLayers = activeLayers.Where(item =>
            item.Layer.Style.Any(token => token.Name.Equals("overlayType", StringComparison.OrdinalIgnoreCase) &&
                token.Value.Equals("ReferenceBand", StringComparison.OrdinalIgnoreCase))).ToList();
        activeLayers = activeLayers.Except(bandLayers).ToList();

        var rectLayers = activeLayers.Where(item => item.Layer.Mark == MarkKind.Rect).ToList();
        var continuousLayers = activeLayers.Where(item => item.Layer.Mark is MarkKind.Line or MarkKind.Area or MarkKind.Point).ToList();
        var ruleLayers = activeLayers.Where(item => item.Layer.Mark is MarkKind.Rule or MarkKind.Tick).ToList();

        if (rectLayers.Count > 0 && continuousLayers.Any(item => item.Layer.Mark is MarkKind.Line or MarkKind.Area))
        {
            content.Add(RenderCompositeBarLine(plan, rectLayers, continuousLayers, ruleLayers, width));
        }
        else if (rectLayers.Count > 0)
        {
            foreach (var item in rectLayers)
                content.Add(RenderRectangles(plan, item.Layer, item.Data, item.Label, item.Color, width));
            foreach (var item in ruleLayers)
                content.Add(RenderRule(item.Data, item.Label, item.Color));
            foreach (var item in continuousLayers)
                content.Add(item.Layer.Mark == MarkKind.Point
                    ? RenderPoints(item.Data, item.Label, item.Color, item.Series?.Order ?? 0)
                    : RenderLine(item.Data, item.Label, item.Color, width, item.Layer.Mark == MarkKind.Area));
        }
        else if (continuousLayers.Count > 0)
        {
            content.Add(RenderCompositeContinuous(plan, continuousLayers, ruleLayers, width));
        }
        else
        {
            foreach (var item in activeLayers)
            {
                content.Add(item.Layer.Mark switch
                {
                    MarkKind.Rect => RenderRectangles(plan, item.Layer, item.Data, item.Label, item.Color, width),
                    MarkKind.Line or MarkKind.Area => RenderLine(item.Data, item.Label, item.Color, width, item.Layer.Mark == MarkKind.Area),
                    MarkKind.Point => RenderPoints(item.Data, item.Label, item.Color, item.Series?.Order ?? 0),
                    MarkKind.Rule or MarkKind.Tick => RenderRule(item.Data, item.Label, item.Color),
                    _ => RenderFallback(plan.Fallback)
                });
            }
        }

        foreach (var band in bandLayers)
            content.Add(RenderReferenceBand(band.Data, band.Label, band.Color));

        var arcLayers = plan.Layers.Where(layer => layer.Mark == MarkKind.Arc)
            .Select(layer => (Layer: layer, Data: layer.Data.Where(datum => rows.Contains(datum.RowIndex)).ToList()))
            .Where(item => item.Data.Count > 0).ToList();
        if (arcLayers.Count > 0) content.Add(RenderArcs(plan, arcLayers, width));
        return content.Count == 0 ? RenderFallback(plan.Fallback) : new Rows(content);
    }

    private static IRenderable RenderCompositeContinuous(
        PlotPlan plan,
        IReadOnlyList<(ResolvedMarkLayer Layer, List<ResolvedDatum> Data, ResolvedSeries? Series, string Label, string Color)> layers,
        IReadOnlyList<(ResolvedMarkLayer Layer, List<ResolvedDatum> Data, ResolvedSeries? Series, string Label, string Color)> ruleLayers,
        int width)
    {
        var allNumeric = layers.SelectMany(l => l.Data.SelectMany(d => new[] {
            Value(d),
            PlotPlanResolver.Number(Channel(d, FieldChannel.ConfidenceLow) ?? ChartValue.Null()),
            PlotPlanResolver.Number(Channel(d, FieldChannel.ConfidenceHigh) ?? ChartValue.Null())
        })).Where(v => v.HasValue).Select(v => v!.Value).ToList();
        var ruleNumeric = ruleLayers.SelectMany(r => r.Data.Select(Value)).Where(v => v.HasValue).Select(v => v!.Value).ToList();
        var combinedNumeric = allNumeric.Concat(ruleNumeric).ToList();
        if (combinedNumeric.Count == 0) return new Markup("[grey]all values are gaps[/]");
        var min = combinedNumeric.Min();
        var max = combinedNumeric.Max();
        if (min == max) max = min + 1m;

        var allX = layers.SelectMany(l => l.Data.Select(XValue)).Where(v => v.HasValue).Select(v => v!.Value).ToList();
        var hasNumericX = allX.Count > 0;
        var minX = hasNumericX ? allX.Min() : 0m;
        var maxX = hasNumericX ? allX.Max() : 1m;
        if (minX == maxX) maxX = minX + 1m;

        var canvas = new BrailleCanvas(Math.Clamp(width - 16, 16, 48), 7);
        var headerParts = new List<string>();

        foreach (var item in layers)
        {
            var values = item.Data.Select(Value).ToList();
            var xValues = item.Data.Select(XValue).ToList();
            var ansi = SafeAnsiColor(item.Color);
            headerParts.Add($"[{ansi}]●[/] [bold]{Markup.Escape(item.Label)}[/]");

            (int X, int Y)? previous = null;
            for (var index = 0; index < item.Data.Count; index++)
            {
                if (item.Data[index].IsGap || !values[index].HasValue) { previous = null; continue; }
                int x;
                if (hasNumericX && xValues[index].HasValue)
                {
                    x = (int)((xValues[index]!.Value - minX) / (maxX - minX) * (canvas.DotWidth - 1));
                }
                else
                {
                    x = item.Data.Count == 1 ? 0 : index * (canvas.DotWidth - 1) / (item.Data.Count - 1);
                }
                x = Math.Clamp(x, 0, canvas.DotWidth - 1);
                var y = canvas.DotHeight - 1 - (int)((values[index]!.Value - min) / (max - min) * (canvas.DotHeight - 1));
                y = Math.Clamp(y, 0, canvas.DotHeight - 1);

                if (item.Layer.Mark == MarkKind.Point)
                {
                    canvas.Set(x, y, ansi);
                }
                else
                {
                    if (previous.HasValue) canvas.Line(previous.Value.X, previous.Value.Y, x, y, ansi);
                    else canvas.Set(x, y, ansi);
                    if (item.Layer.Mark == MarkKind.Area)
                    {
                        var cLow = PlotPlanResolver.Number(Channel(item.Data[index], FieldChannel.ConfidenceLow) ?? ChartValue.Null());
                        var cHigh = PlotPlanResolver.Number(Channel(item.Data[index], FieldChannel.ConfidenceHigh) ?? ChartValue.Null());
                        if (cLow.HasValue && cHigh.HasValue)
                        {
                            var yL = canvas.DotHeight - 1 - (int)((cLow.Value - min) / (max - min) * (canvas.DotHeight - 1));
                            var yH = canvas.DotHeight - 1 - (int)((cHigh.Value - min) / (max - min) * (canvas.DotHeight - 1));
                            yL = Math.Clamp(yL, 0, canvas.DotHeight - 1);
                            yH = Math.Clamp(yH, 0, canvas.DotHeight - 1);
                            for (var fill = Math.Min(yL, yH); fill <= Math.Max(yL, yH); fill++)
                                canvas.Set(x, fill, ansi);
                        }
                        else
                        {
                            for (var fill = y + 1; fill < canvas.DotHeight; fill += 2) canvas.Set(x, fill, ansi);
                        }
                    }
                }
                previous = (x, y);
            }
        }

        foreach (var rule in ruleLayers)
        {
            var ansi = SafeAnsiColor(rule.Color);
            headerParts.Add($"[{ansi}]──[/] [bold]{Markup.Escape(rule.Label)}[/]");
            var val = rule.Data.Select(Value).FirstOrDefault(v => v.HasValue);
            if (val.HasValue)
            {
                var y = canvas.DotHeight - 1 - (int)((val.Value - min) / (max - min) * (canvas.DotHeight - 1));
                if (y >= 0 && y < canvas.DotHeight)
                {
                    for (var x = 0; x < canvas.DotWidth; x += 2)
                        canvas.Set(x, y, ansi);
                }
            }
        }

        var totalGaps = layers.Sum(l => l.Data.Count(d => d.IsGap));
        var isScatter = layers.All(l => l.Layer.Mark == MarkKind.Point);
        var isArea = layers.Any(l => l.Layer.Mark == MarkKind.Area);
        var typeLabel = isScatter ? "point glyph ●" : isArea ? "Braille area" : "Braille line";
        var headerSuffix = isScatter ? $" [grey]({typeLabel})[/]" : $" [grey]({typeLabel}; {totalGaps} gaps)[/]";
        var axisRange = hasNumericX
            ? $"[grey]X: {minX.ToString(CultureInfo.InvariantCulture)} … {maxX.ToString(CultureInfo.InvariantCulture)}  │  Y: {min.ToString(CultureInfo.InvariantCulture)} … {max.ToString(CultureInfo.InvariantCulture)}[/]"
            : $"[grey]{min.ToString(CultureInfo.InvariantCulture)} … {max.ToString(CultureInfo.InvariantCulture)}[/]";

        var errorDetails = layers.SelectMany(l => l.Data.Select(d =>
        {
            var detail = ConfidenceIntervalDetail(d) ?? ErrorBarDetail(d);
            return detail != null ? $"{Label(d)}: {detail}" : null;
        })).Where(d => d != null).ToList();

        var contentRows = new List<IRenderable>
        {
            new Markup(string.Join("  ", headerParts) + headerSuffix),
            canvas.ToRenderableWithAxis(min, max),
            new Markup(axisRange)
        };
        if (errorDetails.Count > 0)
        {
            contentRows.Add(new Markup($"[grey]{Markup.Escape(string.Join(", ", errorDetails))}[/]"));
        }

        return new Rows(contentRows);
    }

    private static IRenderable RenderCompositeBarLine(
        PlotPlan plan,
        IReadOnlyList<(ResolvedMarkLayer Layer, List<ResolvedDatum> Data, ResolvedSeries? Series, string Label, string Color)> rectLayers,
        IReadOnlyList<(ResolvedMarkLayer Layer, List<ResolvedDatum> Data, ResolvedSeries? Series, string Label, string Color)> lineLayers,
        IReadOnlyList<(ResolvedMarkLayer Layer, List<ResolvedDatum> Data, ResolvedSeries? Series, string Label, string Color)> ruleLayers,
        int width)
    {
        var primaryRect = rectLayers[0];
        var categories = primaryRect.Data.Select(Label).ToList();
        var allValues = rectLayers.SelectMany(l => l.Data.Select(Value))
            .Concat(rectLayers.Where(l => IsRangedRect(l.Layer, l.Data)).SelectMany(l => l.Data.Select(IntervalStart)))
            .Concat(lineLayers.SelectMany(l => l.Data.Select(Value)))
            .Concat(lineLayers.SelectMany(l => l.Data.Select(d => PlotPlanResolver.Number(Channel(d, FieldChannel.ConfidenceLow) ?? ChartValue.Null()))))
            .Concat(lineLayers.SelectMany(l => l.Data.Select(d => PlotPlanResolver.Number(Channel(d, FieldChannel.ConfidenceHigh) ?? ChartValue.Null()))))
            .Concat(ruleLayers.SelectMany(r => r.Data.Select(Value)))
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToList();

        if (allValues.Count == 0) return new Markup("[grey]all values are gaps[/]");
        var min = Math.Min(0m, allValues.Min());
        var max = allValues.Max();
        if (min == max) max = min + 1m;

        var cellW = Math.Clamp((width - 16) / 2, 16, 48);
        var canvas = new BrailleCanvas(cellW, 8);
        var headerParts = new List<string>();

        var catCount = Math.Max(1, categories.Count);
        var slotWidth = (decimal)canvas.DotWidth / catCount;
        var barWidthDots = Math.Max(2, (int)(slotWidth * 0.55m));

        foreach (var rect in rectLayers)
        {
            var ansi = SafeAnsiColor(rect.Color);
            headerParts.Add($"[{ansi}]■[/] [bold]{Markup.Escape(rect.Label)}[/]");
            var values = rect.Data.Select(Value).ToList();
            var ranged = IsRangedRect(rect.Layer, rect.Data);
            for (var index = 0; index < rect.Data.Count; index++)
            {
                var val = values[index];
                if (!val.HasValue || rect.Data[index].IsGap) continue;
                var slotCenter = (index + 0.5m) * slotWidth;
                var xStart = Math.Max(0, (int)(slotCenter - barWidthDots / 2m));
                var xEnd = Math.Min(canvas.DotWidth - 1, xStart + barWidthDots - 1);
                var baseline = ranged ? IntervalStart(rect.Data[index]) ?? 0m : 0m;
                var baseY = canvas.DotHeight - 1 - (int)((baseline - min) / (max - min) * (canvas.DotHeight - 1));
                var topY = canvas.DotHeight - 1 - (int)((val.Value - min) / (max - min) * (canvas.DotHeight - 1));
                var y0 = Math.Min(baseY, topY);
                var y1 = Math.Max(baseY, topY);
                for (var x = xStart; x <= xEnd; x++)
                {
                    for (var y = y0; y <= y1; y++)
                        canvas.Set(x, y, ansi);
                }
            }
        }

        foreach (var line in lineLayers)
        {
            var ansi = SafeAnsiColor(line.Color);
            headerParts.Add($"[{ansi}]●[/] [bold]{Markup.Escape(line.Label)}[/]");
            var values = line.Data.Select(Value).ToList();
            (int X, int Y)? previous = null;
            for (var index = 0; index < line.Data.Count; index++)
            {
                var val = values[index];
                if (!val.HasValue || line.Data[index].IsGap) { previous = null; continue; }
                var slotCenter = (index + 0.5m) * slotWidth;
                var x = Math.Clamp((int)slotCenter, 0, canvas.DotWidth - 1);
                var y = canvas.DotHeight - 1 - (int)((val.Value - min) / (max - min) * (canvas.DotHeight - 1));
                if (previous.HasValue) canvas.Line(previous.Value.X, previous.Value.Y, x, y, ansi);
                else canvas.Set(x, y, ansi);
                previous = (x, y);
            }
        }

        foreach (var rule in ruleLayers)
        {
            var ansi = SafeAnsiColor(rule.Color);
            headerParts.Add($"[{ansi}]──[/] [bold]{Markup.Escape(rule.Label)}[/]");
            var val = rule.Data.Select(Value).FirstOrDefault(v => v.HasValue);
            if (val.HasValue)
            {
                var y = canvas.DotHeight - 1 - (int)((val.Value - min) / (max - min) * (canvas.DotHeight - 1));
                if (y >= 0 && y < canvas.DotHeight)
                {
                    for (var x = 0; x < canvas.DotWidth; x += 2)
                        canvas.Set(x, y, ansi);
                }
            }
        }

        var catAxis = string.Join("    ", categories.Select(c => Truncate(c, 8)));

        var errorDetails = rectLayers.Concat(lineLayers).SelectMany(l => l.Data.Select(d =>
        {
            var detail = ConfidenceIntervalDetail(d) ?? ErrorBarDetail(d);
            return detail != null ? $"{Label(d)}: {detail}" : null;
        })).Where(d => d != null).ToList();

        var contentRows = new List<IRenderable>
        {
            new Markup(string.Join("  ", headerParts)),
            canvas.ToRenderableWithAxis(min, max),
            new Markup($"[grey]{min.ToString(CultureInfo.InvariantCulture)} … {max.ToString(CultureInfo.InvariantCulture)}[/]  [bold]{Markup.Escape(catAxis)}[/]")
        };
        if (errorDetails.Count > 0)
        {
            contentRows.Add(new Markup($"[grey]{Markup.Escape(string.Join(", ", errorDetails))}[/]"));
        }

        return new Rows(contentRows);
    }

    private static IRenderable RenderRectangles(PlotPlan plan, ResolvedMarkLayer layer, IReadOnlyList<ResolvedDatum> data, string series, string color, int width)
    {
        if (IsRangedRect(layer, data)) return RenderRangedRectangles(data, series, color, width);
        var isHeatmap = data.Count > 0 &&
            data.Select(d => DisplayChannel(d, FieldChannel.X)).Where(s => !string.IsNullOrEmpty(s)).Distinct().Count() > 1 &&
            data.Select(d => DisplayChannel(d, FieldChannel.Y)).Where(s => !string.IsNullOrEmpty(s)).Distinct().Count() > 1 &&
            data.All(d => !PlotPlanResolver.Number(Channel(d, FieldChannel.Y) ?? ChartValue.Null()).HasValue);

        if (isHeatmap)
        {
            return RenderHeatmapMatrix(plan, data, series, width);
        }

        var values = data.Select(Value).ToList();
        var maximum = values.Where(value => value.HasValue).Select(value => Math.Abs(value!.Value)).DefaultIfEmpty(1m).Max();
        if (maximum == 0m) maximum = 1m;
        var labelWidth = Math.Clamp(data.Select(Label).DefaultIfEmpty("").Max(label => label.Length), 6, Math.Max(6, width / 3));
        var barWidth = Math.Max(8, width - labelWidth - 18);

        var hasSeries = data.Any(d => DisplayChannel(d, FieldChannel.Color) != null || (!d.Encodings.IsDefaultOrEmpty && d.Encodings.Any(e => e.Channel == ConditionalEncodingChannel.Color)));
        if (hasSeries)
        {
            var groups = data.GroupBy(Label).ToList();
            var subRows = new List<IRenderable>
            {
                new Markup($"[bold]{Markup.Escape(series)}[/] [grey](grouped fractional bars)[/]")
            };
            foreach (var group in groups)
            {
                subRows.Add(new Markup($"[bold]{Markup.Escape(group.Key)}[/]"));
                foreach (var datum in group)
                {
                    var seriesName = DisplayChannel(datum, FieldChannel.Color)
                        ?? (datum.Encodings.FirstOrDefault(e => e.Channel == ConditionalEncodingChannel.Color) is { } enc ? PlotPlanResolver.Display(enc.Value) : null)
                        ?? "default";
                    var seriesColor = plan.Series.FirstOrDefault(s => s.Key == seriesName)?.Color
                        ?? plan.Palette.FirstOrDefault(p => p.SeriesKey == seriesName)?.Color
                        ?? color;
                    var seriesAnsi = SafeAnsiColor(seriesColor);
                    var subLabel = Truncate(seriesName, labelWidth).PadRight(labelWidth);
                    if (datum.IsGap)
                    {
                        subRows.Add(new Markup($"  {Markup.Escape(subLabel)} [grey]gap[/]"));
                        continue;
                    }
                    var value = Value(datum) ?? 0m;
                    var bar = FractionalBar(Math.Abs(value) / maximum, barWidth);
                    var sign = value < 0m ? "◀" : value == 0m ? "│" : "▶";
                    var errorInfo = ConfidenceIntervalDetail(datum) ?? ErrorBarDetail(datum);
                    subRows.Add(new Markup($"  {Markup.Escape(subLabel)} [{seriesAnsi}]{sign}{bar}[/] {Markup.Escape(DisplayValue(datum))}{(errorInfo != null ? $" [grey]({errorInfo})[/]" : "")}"));
                }
            }
            return new Rows(subRows);
        }

        var ansi = SafeAnsiColor(color);
        var rows = data.Select(datum =>
        {
            var label = Truncate(Label(datum), labelWidth).PadRight(labelWidth);
            if (datum.IsGap) return (IRenderable)new Markup($"{Markup.Escape(label)} [grey]gap[/]");
            var value = Value(datum) ?? 0m;
            var bar = FractionalBar(Math.Abs(value) / maximum, barWidth);
            var sign = value < 0m ? "◀" : value == 0m ? "│" : "▶";
            var errorInfo = ConfidenceIntervalDetail(datum) ?? ErrorBarDetail(datum);
            return new Markup($"{Markup.Escape(label)} [{ansi}]{sign}{bar}[/] {Markup.Escape(DisplayValue(datum))}{(errorInfo != null ? $" [grey]({errorInfo})[/]" : "")}");
        }).ToList();
        rows.Insert(0, new Markup($"[bold]{Markup.Escape(series)}[/] [grey](fractional bars)[/]"));
        return new Rows(rows);
    }

    private static string? ConfidenceIntervalDetail(ResolvedDatum datum)
    {
        var low = Channel(datum, FieldChannel.ConfidenceLow);
        var high = Channel(datum, FieldChannel.ConfidenceHigh);
        if (low is null || high is null || low.Kind == ChartValueKind.Null || high.Kind == ChartValueKind.Null)
            return null;
        var lowDisplay = DisplayChannel(datum, FieldChannel.ConfidenceLow) ?? PlotPlanResolver.Number(low)?.ToString(CultureInfo.InvariantCulture);
        var highDisplay = DisplayChannel(datum, FieldChannel.ConfidenceHigh) ?? PlotPlanResolver.Number(high)?.ToString(CultureInfo.InvariantCulture);
        return $"confidence {lowDisplay} to {highDisplay}";
    }

    /// <summary>True when the author supplied both interval endpoints; resolver-computed stack endpoints do not count.</summary>
    private static string? ErrorBarDetail(ResolvedDatum datum)
    {
        var low = Channel(datum, FieldChannel.ErrorLow);
        var high = Channel(datum, FieldChannel.ErrorHigh);
        if (low is null || high is null || low.Kind == ChartValueKind.Null || high.Kind == ChartValueKind.Null)
            return null;
        var lowDisplay = DisplayChannel(datum, FieldChannel.ErrorLow) ?? PlotPlanResolver.Number(low)?.ToString(CultureInfo.InvariantCulture);
        var highDisplay = DisplayChannel(datum, FieldChannel.ErrorHigh) ?? PlotPlanResolver.Number(high)?.ToString(CultureInfo.InvariantCulture);
        return $"error {lowDisplay} to {highDisplay}";
    }

    private static bool IsRangedRect(ResolvedMarkLayer layer, IReadOnlyList<ResolvedDatum> data) =>
        layer.Mark == MarkKind.Rect && layer.Stack == StackMode.None &&
        data.Any(datum => IntervalStart(datum).HasValue && IntervalEnd(datum).HasValue);

    private static decimal? IntervalStart(ResolvedDatum datum) =>
        PlotPlanResolver.Number(Channel(datum, FieldChannel.YStart) ?? ChartValue.Null());

    private static decimal? IntervalEnd(ResolvedDatum datum) =>
        PlotPlanResolver.Number(Channel(datum, FieldChannel.YEnd) ?? ChartValue.Null());

    /// <summary>Renders ranged rectangles as offset spans so both endpoints survive in a terminal.</summary>
    private static IRenderable RenderRangedRectangles(IReadOnlyList<ResolvedDatum> data, string series, string color, int width)
    {
        var spans = data.Select(datum => (Datum: datum, Start: IntervalStart(datum), End: IntervalEnd(datum)))
            .Where(item => !item.Datum.IsGap && item.Start.HasValue && item.End.HasValue)
            .Select(item => (item.Datum, Start: Math.Min(item.Start!.Value, item.End!.Value), End: Math.Max(item.Start!.Value, item.End!.Value)))
            .ToList();
        if (spans.Count == 0) return new Markup("[grey]all values are gaps[/]");
        var minimum = spans.Min(item => item.Start);
        var maximum = spans.Max(item => item.End);
        if (minimum == maximum) maximum = minimum + 1m;
        var labelWidth = Math.Clamp(data.Select(Label).DefaultIfEmpty("").Max(label => label.Length), 6, Math.Max(6, width / 3));
        var barWidth = Math.Max(8, width - labelWidth - 22);
        var ansi = SafeAnsiColor(color);
        var rows = new List<IRenderable>
        {
            new Markup($"[bold]{Markup.Escape(series)}[/] [grey](ranged bars {minimum.ToString(CultureInfo.InvariantCulture)} … {maximum.ToString(CultureInfo.InvariantCulture)})[/]")
        };
        foreach (var datum in data)
        {
            var label = Truncate(Label(datum), labelWidth).PadRight(labelWidth);
            var start = IntervalStart(datum);
            var end = IntervalEnd(datum);
            if (datum.IsGap || !start.HasValue || !end.HasValue)
            {
                rows.Add(new Markup($"{Markup.Escape(label)} [grey]gap[/]"));
                continue;
            }
            var low = Math.Min(start.Value, end.Value);
            var high = Math.Max(start.Value, end.Value);
            var lead = Math.Clamp((int)Math.Round((low - minimum) / (maximum - minimum) * barWidth), 0, barWidth);
            var fill = Math.Clamp((int)Math.Round((high - low) / (maximum - minimum) * barWidth), 1, barWidth - lead);
            rows.Add(new Markup($"{Markup.Escape(label)} {new string(' ', lead)}[{ansi}]{new string('█', fill)}[/] " +
                $"{Markup.Escape(start.Value.ToString(CultureInfo.InvariantCulture))} to {Markup.Escape(end.Value.ToString(CultureInfo.InvariantCulture))}"));
        }
        return new Rows(rows);
    }

    private static IRenderable RenderHeatmapMatrix(PlotPlan plan, IReadOnlyList<ResolvedDatum> data, string series, int width)
    {
        var xCats = data.Select(d => DisplayChannel(d, FieldChannel.X) ?? "").Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();
        var yCats = data.Select(d => DisplayChannel(d, FieldChannel.Y) ?? "").Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();

        var numericValues = data.Select(d =>
            PlotPlanResolver.Number(Channel(d, FieldChannel.Color) ?? Channel(d, FieldChannel.Text) ?? Channel(d, FieldChannel.Size) ?? ChartValue.Null()))
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToList();

        var minVal = numericValues.Count > 0 ? numericValues.Min() : 0m;
        var maxVal = numericValues.Count > 0 ? numericValues.Max() : 1m;
        if (minVal == maxVal) maxVal = minVal + 1m;

        var table = new Table().Border(TableBorder.Rounded).Title($"[bold]{Markup.Escape(series)}[/] [grey](2D Heatmap Grid)[/]");
        table.AddColumn(new TableColumn("").LeftAligned());
        foreach (var xCat in xCats)
        {
            table.AddColumn(new TableColumn($"[bold]{Markup.Escape(xCat)}[/]").Centered());
        }

        foreach (var yCat in yCats)
        {
            var cells = new List<IRenderable> { new Markup($"[bold]{Markup.Escape(yCat)}[/]") };
            foreach (var xCat in xCats)
            {
                var datum = data.FirstOrDefault(d => DisplayChannel(d, FieldChannel.X) == xCat && DisplayChannel(d, FieldChannel.Y) == yCat);
                if (datum == null || datum.IsGap)
                {
                    cells.Add(new Markup("[grey]--[/]"));
                    continue;
                }
                var val = PlotPlanResolver.Number(Channel(datum, FieldChannel.Color) ?? Channel(datum, FieldChannel.Text) ?? Channel(datum, FieldChannel.Size) ?? ChartValue.Null()) ?? 0m;
                var ratio = (double)Math.Clamp((val - minVal) / (maxVal - minVal), 0m, 1m);

                int r = (int)(ratio * 220 + 35);
                int g = (int)((1 - Math.Abs(ratio - 0.5) * 2) * 160 + 20);
                int b = (int)((1 - ratio) * 220 + 35);
                var hexColor = $"#{r:X2}{g:X2}{b:X2}";

                var blockChar = ratio switch
                {
                    < 0.25 => "░░",
                    < 0.50 => "▒▒",
                    < 0.75 => "▓▓",
                    _ => "██"
                };

                cells.Add(new Markup($"[{hexColor}]{blockChar}[/] {val.ToString("G", CultureInfo.InvariantCulture)}"));
            }
            table.AddRow(cells.ToArray());
        }

        return new Rows(
            table,
            new Markup($"[grey]Scale: {minVal.ToString(CultureInfo.InvariantCulture)} (░ low) … {maxVal.ToString(CultureInfo.InvariantCulture)} (█ high)[/]"));
    }

    private static IRenderable RenderLine(IReadOnlyList<ResolvedDatum> data, string series, string color, int width, bool area)
    {
        var isConfidence = data.Any(d => Channel(d, FieldChannel.ConfidenceLow) is not null || Channel(d, FieldChannel.ConfidenceHigh) is not null);
        var lowValues = isConfidence ? data.Select(d => PlotPlanResolver.Number(Channel(d, FieldChannel.ConfidenceLow) ?? ChartValue.Null())).ToList() : null;
        var highValues = isConfidence ? data.Select(d => PlotPlanResolver.Number(Channel(d, FieldChannel.ConfidenceHigh) ?? ChartValue.Null())).ToList() : null;
        var values = data.Select(Value).ToList();
        var numeric = (isConfidence
            ? lowValues!.Concat(highValues!)
            : values).Where(value => value.HasValue).Select(value => value!.Value).ToList();
        if (numeric.Count == 0) return new Markup($"[bold]{Markup.Escape(series)}[/]: [grey]all values are gaps[/]");
        var min = numeric.Min(); var max = numeric.Max();
        if (min == max) max = min + 1m;
        var canvas = new BrailleCanvas(Math.Clamp(width - 16, 16, 48), 7);
        var ansi = SafeAnsiColor(color);
        (int X, int Y)? previous = null;
        for (var index = 0; index < data.Count; index++)
        {
            if (isConfidence)
            {
                if (data[index].IsGap || !lowValues![index].HasValue || !highValues![index].HasValue) continue;
                var x = data.Count == 1 ? 0 : index * (canvas.DotWidth - 1) / (data.Count - 1);
                var yL = canvas.DotHeight - 1 - (int)((lowValues[index]!.Value - min) / (max - min) * (canvas.DotHeight - 1));
                var yH = canvas.DotHeight - 1 - (int)((highValues[index]!.Value - min) / (max - min) * (canvas.DotHeight - 1));
                yL = Math.Clamp(yL, 0, canvas.DotHeight - 1);
                yH = Math.Clamp(yH, 0, canvas.DotHeight - 1);
                for (var fill = Math.Min(yL, yH); fill <= Math.Max(yL, yH); fill++)
                    canvas.Set(x, fill, ansi);
                continue;
            }
            if (data[index].IsGap || !values[index].HasValue) { previous = null; continue; }
            var ptX = data.Count == 1 ? 0 : index * (canvas.DotWidth - 1) / (data.Count - 1);
            var ptY = canvas.DotHeight - 1 - (int)((values[index]!.Value - min) / (max - min) * (canvas.DotHeight - 1));
            if (previous.HasValue) canvas.Line(previous.Value.X, previous.Value.Y, ptX, ptY, ansi);
            else canvas.Set(ptX, ptY, ansi);
            if (area) for (var fill = ptY + 1; fill < canvas.DotHeight; fill += 2) canvas.Set(ptX, fill, ansi);
            previous = (ptX, ptY);
        }
        var gaps = data.Count(datum => datum.IsGap);
        var areaLabel = isConfidence ? "Confidence interval" : area ? "Braille area" : "Braille line";
        return new Rows(
            new Markup($"[{ansi}]●[/] [bold]{Markup.Escape(series)}[/] [grey]({areaLabel}; {gaps} gaps)[/]"),
            canvas.ToRenderableWithAxis(min, max),
            new Markup($"[grey]{min.ToString(CultureInfo.InvariantCulture)} … {max.ToString(CultureInfo.InvariantCulture)}[/]"));
    }

    private static readonly char[] SparklineBlocks = [' ', ' ', '▂', '▃', '▄', '▅', '▆', '▇', '█'];

    public static string Sparkline(IEnumerable<decimal?> values, string color = "#5470c6")
    {
        var list = values.ToList();
        var numeric = list.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        if (numeric.Count == 0) return "";
        var min = numeric.Min();
        var max = numeric.Max();
        var span = max == min ? 1m : max - min;
        var ansi = SafeAnsiColor(color);
        var sb = new StringBuilder();
        sb.Append($"[{ansi}]");
        foreach (var val in list)
        {
            if (!val.HasValue) { sb.Append(' '); continue; }
            var ratio = (val.Value - min) / span;
            var index = Math.Clamp((int)Math.Round(ratio * 7m) + 1, 1, 8);
            sb.Append(SparklineBlocks[index]);
        }
        sb.Append("[/]");
        return sb.ToString();
    }

    private static IRenderable RenderPoints(IReadOnlyList<ResolvedDatum> data, string series, string color, int seriesOrder)
    {
        var glyph = PointGlyphs[Math.Abs(seriesOrder) % PointGlyphs.Length];
        var ansi = SafeAnsiColor(color);
        var rows = data.Select(datum =>
        {
            if (datum.IsGap) return (IRenderable)new Markup($"[grey]○ {Markup.Escape(Label(datum))}: gap[/]");
            var errorInfo = ConfidenceIntervalDetail(datum) ?? ErrorBarDetail(datum);
            return new Markup($"[{ansi}]{glyph}[/] {Markup.Escape(Label(datum))}: {Markup.Escape(DisplayValue(datum))}{(errorInfo != null ? $" [grey]({errorInfo})[/]" : "")}");
        });
        return new Rows(new[] { (IRenderable)new Markup($"[bold]{Markup.Escape(series)}[/] [grey](point glyph {glyph})[/]") }.Concat(rows));
    }

    private static IRenderable RenderReferenceBand(IReadOnlyList<ResolvedDatum> data, string label, string color)
    {
        var datum = data.FirstOrDefault();
        var low = datum is null ? null : Channel(datum, FieldChannel.YStart);
        var high = datum is null ? null : Channel(datum, FieldChannel.YEnd);
        var lowText = low is null ? "?" : PlotPlanResolver.Display(low);
        var highText = high is null ? "?" : PlotPlanResolver.Display(high);
        var ansi = SafeAnsiColor(color);
        return new Markup($"[{ansi}]████████[/] [bold]{Markup.Escape(label)}[/]: [{ansi}]{Markup.Escape(lowText)} to {Markup.Escape(highText)}[/]");
    }

    private static IRenderable RenderRule(IReadOnlyList<ResolvedDatum> data, string label, string color)
    {
        var value = data.Select(DisplayValue).FirstOrDefault(item => !string.IsNullOrEmpty(item)) ?? "reference";
        var ansi = SafeAnsiColor(color);
        return new Markup($"[{ansi}]────────[/] [bold]{Markup.Escape(label)}[/]: [{ansi}]{Markup.Escape(value)}[/]");
    }

    private static string ResolveLayerColor(ResolvedMarkLayer layer, PlotPlan plan)
    {
        var styleColor = layer.Style.FirstOrDefault(token => token.Name.Equals("color", StringComparison.OrdinalIgnoreCase))?.Value;
        if (!string.IsNullOrWhiteSpace(styleColor)) return styleColor;
        var series = plan.Series.FirstOrDefault(item => item.Key == layer.SeriesKey);
        if (!string.IsNullOrWhiteSpace(series?.Color)) return series.Color;
        var paletteColor = plan.Palette.FirstOrDefault(item => item.SeriesKey == layer.SeriesKey)?.Color;
        if (!string.IsNullOrWhiteSpace(paletteColor)) return paletteColor;
        return "#5470c6";
    }

    private static string SafeAnsiColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color)) return "grey";
        var trimmed = color.Trim();
        if (trimmed.StartsWith('#')) return $"#{trimmed.TrimStart('#')}";
        return trimmed.ToLowerInvariant() switch
        {
            "red" => "red",
            "blue" => "blue",
            "green" => "green",
            "yellow" => "yellow",
            "white" => "white",
            "black" => "black",
            "grey" or "gray" => "grey",
            "orange" => "#FFA500",
            "purple" => "#800080",
            "cyan" => "#00FFFF",
            "magenta" => "#FF00FF",
            "lime" => "#00FF00",
            "pink" => "#FFC0CB",
            "brown" => "#A52A2A",
            "gold" => "#FFD700",
            "teal" => "#008080",
            "indigo" => "#4B0082",
            "violet" => "#EE82EE",
            _ => trimmed.ToLowerInvariant()
        };
    }

    private static decimal? XValue(ResolvedDatum datum) =>
        PlotPlanResolver.Number(Channel(datum, FieldChannel.X) ?? ChartValue.Null());

    private static IRenderable RenderArcs(PlotPlan plan,
        IReadOnlyList<(ResolvedMarkLayer Layer, List<ResolvedDatum> Data)> layers, int width)
    {
        var rawComponents = layers.SelectMany(item => item.Data.Select(datum =>
        {
            var datumLabel = Label(datum);
            var series = layers.Count == 1
                ? plan.Series.FirstOrDefault(candidate => candidate.Key == datumLabel)
                : plan.Series.FirstOrDefault(candidate => candidate.Key == item.Layer.SeriesKey);
            var label = layers.Count > 1 && item.Data.Count == 1 ? series?.Label ?? datumLabel : datumLabel;
            return (Datum: datum, Label: label, Color: series?.Color ?? "#808080", Value: Math.Max(0m, Value(datum) ?? 0m));
        })).ToList();

        // 1. Sort order
        var sort = (Style(plan, "SORT") ?? Style(plan, "AXIS_SORT"))?.ToUpperInvariant();
        var components = sort switch
        {
            "VALUE_DESC" or "VALUE" => rawComponents.OrderByDescending(c => c.Value).ThenBy(c => c.Label, StringComparer.Ordinal).ToList(),
            "VALUE_ASC" => rawComponents.OrderBy(c => c.Value).ThenBy(c => c.Label, StringComparer.Ordinal).ToList(),
            "ALPHA" => rawComponents.OrderBy(c => c.Label, StringComparer.OrdinalIgnoreCase).ToList(),
            _ => rawComponents
        };

        var total = components.Sum(component => component.Value);

        // 2. Minimum slice threshold / "Other" rollup
        var minSlicePctStr = Style(plan, "MIN_SLICE_PCT");
        var otherLabel = Style(plan, "OTHER_LABEL") ?? "Other";
        if (total > 0m && !string.IsNullOrWhiteSpace(minSlicePctStr) &&
            decimal.TryParse(minSlicePctStr.TrimEnd('%'), NumberStyles.Number, CultureInfo.InvariantCulture, out var minPct) &&
            minPct > 0m)
        {
            var thresholdRatio = minPct >= 1m ? minPct / 100m : minPct;
            var kept = new List<(ResolvedDatum Datum, string Label, string Color, decimal Value)>();
            decimal otherVal = 0m;
            ResolvedDatum? sampleDatum = null;
            foreach (var c in components)
            {
                if (c.Value / total < thresholdRatio)
                {
                    otherVal += c.Value;
                    sampleDatum ??= c.Datum;
                }
                else
                {
                    kept.Add(c);
                }
            }
            if (otherVal > 0m && kept.Count > 0)
            {
                kept.Add((sampleDatum ?? components[0].Datum, otherLabel, "#9ca3af", otherVal));
                components = kept;
            }
        }

        var componentWidth = Math.Max(8, width - 28);
        var rows = components.Select(component =>
        {
            var proportion = total == 0m ? 0m : component.Value / total;
            return (IRenderable)new Markup($"{Markup.Escape(Truncate(component.Label, 18).PadRight(18))} [#{component.Color.TrimStart('#')}]{FractionalBar(proportion, componentWidth)}[/] {proportion:P1} ({Markup.Escape(DisplayValue(component.Datum))})");
        });
        return new Rows(new[] { (IRenderable)new Markup("[bold]Proportional breakdown[/] [grey](proportional components)[/]") }.Concat(rows));
    }

    internal static IRenderable RenderFallback(SemanticFallback fallback)
    {
        var table = new Table().Border(TableBorder.Simple).AddColumn("Item").AddColumn(new TableColumn("Value").RightAligned()).AddColumn("Meaning");
        foreach (var item in fallback.Items.OrderBy(item => item.Order))
        {
            var indent = new string(' ', item.Level * 2);
            table.AddRow(Markup.Escape(indent + item.Label), Markup.Escape(item.Value), Markup.Escape(item.Detail ?? item.Group ?? ""));
        }
        return new Rows(new Markup($"[grey]{Markup.Escape(fallback.Summary ?? fallback.Heading)}[/]"), table);
    }

    private static List<(string Label, HashSet<int> Rows)> ResolveFacets(PlotPlan plan)
    {
        if (!plan.Facets.IsDefaultOrEmpty)
            return plan.Facets.Select(panel =>
            {
                var label = panel.RowLabel is null ? panel.ColumnLabel ?? "All data" :
                    panel.ColumnLabel is null ? panel.RowLabel : $"{panel.RowLabel} / {panel.ColumnLabel}";
                return (label, panel.RowIndices.ToHashSet());
            }).ToList();
        var source = plan.Layers.FirstOrDefault(layer => layer.Mark is not MarkKind.Rule)?.Data ?? [];
        var groups = source.GroupBy(datum =>
        {
            var row = Channel(datum, FieldChannel.Row);
            var column = Channel(datum, FieldChannel.Column) ?? Channel(datum, FieldChannel.Wrap);
            return row is null && column is null ? "All data" : $"{(row is null ? "" : PlotPlanResolver.Display(row))}{(row is not null && column is not null ? " / " : "")}{(column is null ? "" : PlotPlanResolver.Display(column))}";
        }, StringComparer.Ordinal).Select(group => (group.Key, group.Select(datum => datum.RowIndex).ToHashSet())).ToList();
        return groups.Count == 0 ? [("All data", plan.Layers.SelectMany(layer => layer.Data).Select(datum => datum.RowIndex).ToHashSet())] : groups;
    }

    private static string FractionalBar(decimal ratio, int width)
    {
        var units = Math.Clamp((int)Math.Round(ratio * width * 8m), 0, width * 8);
        return new string('█', units / 8) + (units % 8 == 0 ? "" : Fractions[units % 8].ToString());
    }

    private static string Label(ResolvedDatum datum) =>
        DisplayChannel(datum, FieldChannel.X) ?? DisplayChannel(datum, FieldChannel.Theta) ?? $"row {datum.RowIndex + 1}";
    private static decimal? Value(ResolvedDatum datum) =>
        PlotPlanResolver.Number(Channel(datum, FieldChannel.Y) ?? Channel(datum, FieldChannel.Y2) ??
            Channel(datum, FieldChannel.Radius) ?? Channel(datum, FieldChannel.Median) ??
            Channel(datum, FieldChannel.Close) ?? Channel(datum, FieldChannel.Size) ?? Channel(datum, FieldChannel.YEnd) ??
            Channel(datum, FieldChannel.ConfidenceHigh) ?? ChartValue.Null());
    private static string DisplayValue(ResolvedDatum datum)
    {
        var channel = datum.Channels.FirstOrDefault(item => item.Channel is FieldChannel.Y or FieldChannel.Y2 or FieldChannel.Radius or
            FieldChannel.Median or FieldChannel.Close or FieldChannel.Size or FieldChannel.YEnd or FieldChannel.ConfidenceHigh);
        var value = channel is null ? "" : channel.DisplayValue ?? PlotPlanResolver.Display(channel.Value);
        if (datum.Encodings.IsDefaultOrEmpty) return value;
        return value + " (" + string.Join(", ", datum.Encodings.Select(encoding =>
            $"{encoding.Channel}={PlotPlanResolver.Display(encoding.Value)}")) + ")";
    }
    private static string? DisplayChannel(ResolvedDatum datum, FieldChannel channel)
    {
        var resolved = datum.Channels.FirstOrDefault(item => item.Channel == channel);
        return resolved is null ? null : resolved.DisplayValue ?? PlotPlanResolver.Display(resolved.Value);
    }
    private static ChartValue? Channel(ResolvedDatum datum, FieldChannel channel) => datum.Channels.FirstOrDefault(item => item.Channel == channel)?.Value;
    private static string Truncate(string value, int width) => value.Length <= width ? value : value[..Math.Max(1, width - 1)] + "…";

    private static bool LegendEnabled(PlotPlan plan)
    {
        var value = Style(plan, "LEGEND");
        return value is null || (!value.Equals("OFF", StringComparison.OrdinalIgnoreCase) &&
            !value.Equals("FALSE", StringComparison.OrdinalIgnoreCase) && value != "0");
    }

    private static bool LegendIsReverse(PlotPlan plan)
    {
        var reverse = Style(plan, "LEGEND_REVERSE");
        return reverse is not null && (reverse.Equals("ON", StringComparison.OrdinalIgnoreCase) ||
            reverse.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || reverse == "1");
    }

    private static string? LegendTitle(PlotPlan plan)
    {
        var title = Style(plan, "LEGEND_TITLE");
        if (string.IsNullOrWhiteSpace(title) || title.Equals("NONE", StringComparison.OrdinalIgnoreCase)) return null;
        return title;
    }

    private static string? Style(PlotPlan plan, string name)
    {
        if (plan.Style.IsDefault) return null;
        for (var i = 0; i < plan.Style.Length; i++)
        {
            if (plan.Style[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return plan.Style[i].Value;
        }
        return null;
    }
}
