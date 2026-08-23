using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
        if (plan.Legend.Length > 1)
            content.Add(new Markup(string.Join("  ", plan.Legend.Select(entry =>
                $"[#{entry.Color.TrimStart('#')}]{PointGlyphs[entry.Order % PointGlyphs.Length]}[/] {Markup.Escape(entry.Label)} [grey]({Markup.Escape(entry.Color)})[/]"))));
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
                var label = layer.Style.FirstOrDefault(token => token.Name.Equals("label", StringComparison.OrdinalIgnoreCase))?.Value
                    ?? series?.Label
                    ?? layer.Id;
                var color = ResolveLayerColor(layer, plan);
                return (Layer: layer, Data: data, Series: series, Label: label, Color: color);
            })
            .Where(item => item.Data.Count > 0)
            .ToList();

        var rectLayers = activeLayers.Where(item => item.Layer.Mark == MarkKind.Rect).ToList();
        var continuousLayers = activeLayers.Where(item => item.Layer.Mark is MarkKind.Line or MarkKind.Area or MarkKind.Point).ToList();
        var ruleLayers = activeLayers.Where(item => item.Layer.Mark == MarkKind.Rule).ToList();

        if (rectLayers.Count > 0 && continuousLayers.Any(item => item.Layer.Mark is MarkKind.Line or MarkKind.Area))
        {
            content.Add(RenderCompositeBarLine(plan, rectLayers, continuousLayers, ruleLayers, width));
        }
        else if (rectLayers.Count > 0)
        {
            foreach (var item in rectLayers)
                content.Add(RenderRectangles(item.Data, item.Label, item.Color, width));
            foreach (var item in ruleLayers)
                content.Add(RenderRule(item.Data, item.Label, item.Color));
            foreach (var item in continuousLayers)
                content.Add(item.Layer.Mark == MarkKind.Point
                    ? RenderPoints(item.Data, item.Label, item.Color, item.Series?.Order ?? 0)
                    : RenderLine(item.Data, item.Label, item.Color, width, item.Layer.Mark == MarkKind.Area));
        }
        else if (continuousLayers.Any(item => item.Layer.Mark is MarkKind.Line or MarkKind.Area))
        {
            content.Add(RenderCompositeContinuous(plan, continuousLayers, ruleLayers, width));
        }
        else
        {
            foreach (var item in activeLayers)
            {
                content.Add(item.Layer.Mark switch
                {
                    MarkKind.Rect => RenderRectangles(item.Data, item.Label, item.Color, width),
                    MarkKind.Line or MarkKind.Area => RenderLine(item.Data, item.Label, item.Color, width, item.Layer.Mark == MarkKind.Area),
                    MarkKind.Point => RenderPoints(item.Data, item.Label, item.Color, item.Series?.Order ?? 0),
                    MarkKind.Rule => RenderRule(item.Data, item.Label, item.Color),
                    _ => RenderFallback(plan.Fallback)
                });
            }
        }

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
        var allNumeric = layers.SelectMany(l => l.Data.Select(Value)).Where(v => v.HasValue).Select(v => v!.Value).ToList();
        var ruleNumeric = ruleLayers.SelectMany(r => r.Data.Select(Value)).Where(v => v.HasValue).Select(v => v!.Value).ToList();
        var combinedNumeric = allNumeric.Concat(ruleNumeric).ToList();
        if (combinedNumeric.Count == 0) return new Markup("[grey]all values are gaps[/]");
        var min = combinedNumeric.Min();
        var max = combinedNumeric.Max();
        if (min == max) max = min + 1m;

        var canvas = new BrailleCanvas(Math.Clamp(width - 8, 16, 52), 7);
        var headerParts = new List<string>();

        foreach (var item in layers)
        {
            var values = item.Data.Select(Value).ToList();
            var ansi = SafeAnsiColor(item.Color);
            headerParts.Add($"[{ansi}]●[/] [bold]{Markup.Escape(item.Label)}[/]");

            (int X, int Y)? previous = null;
            for (var index = 0; index < item.Data.Count; index++)
            {
                if (item.Data[index].IsGap || !values[index].HasValue) { previous = null; continue; }
                var x = item.Data.Count == 1 ? 0 : index * (canvas.DotWidth - 1) / (item.Data.Count - 1);
                var y = canvas.DotHeight - 1 - (int)((values[index]!.Value - min) / (max - min) * (canvas.DotHeight - 1));
                if (item.Layer.Mark == MarkKind.Point)
                {
                    canvas.Set(x, y, ansi);
                }
                else
                {
                    if (previous.HasValue) canvas.Line(previous.Value.X, previous.Value.Y, x, y, ansi);
                    else canvas.Set(x, y, ansi);
                    if (item.Layer.Mark == MarkKind.Area)
                        for (var fill = y + 1; fill < canvas.DotHeight; fill += 2) canvas.Set(x, fill, ansi);
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
        var isArea = layers.Any(l => l.Layer.Mark == MarkKind.Area);
        var typeLabel = isArea ? "Braille area" : "Braille line";
        var headerSuffix = $" [grey]({typeLabel}; {totalGaps} gaps)[/]";

        return new Rows(
            new Markup(string.Join("  ", headerParts) + headerSuffix),
            canvas.ToRenderable(),
            new Markup($"[grey]{min.ToString(CultureInfo.InvariantCulture)} … {max.ToString(CultureInfo.InvariantCulture)}[/]"));
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
            .Concat(lineLayers.SelectMany(l => l.Data.Select(Value)))
            .Concat(ruleLayers.SelectMany(r => r.Data.Select(Value)))
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToList();

        if (allValues.Count == 0) return new Markup("[grey]all values are gaps[/]");
        var min = Math.Min(0m, allValues.Min());
        var max = allValues.Max();
        if (min == max) max = min + 1m;

        var cellW = Math.Clamp((width - 8) / 2, 16, 50);
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
            for (var index = 0; index < rect.Data.Count; index++)
            {
                var val = values[index];
                if (!val.HasValue || rect.Data[index].IsGap) continue;
                var slotCenter = (index + 0.5m) * slotWidth;
                var xStart = Math.Max(0, (int)(slotCenter - barWidthDots / 2m));
                var xEnd = Math.Min(canvas.DotWidth - 1, xStart + barWidthDots - 1);
                var baseY = canvas.DotHeight - 1 - (int)((0m - min) / (max - min) * (canvas.DotHeight - 1));
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

        return new Rows(
            new Markup(string.Join("  ", headerParts)),
            canvas.ToRenderable(),
            new Markup($"[grey]{min.ToString(CultureInfo.InvariantCulture)} … {max.ToString(CultureInfo.InvariantCulture)}[/]  [bold]{Markup.Escape(catAxis)}[/]"));
    }

    private static IRenderable RenderRectangles(IReadOnlyList<ResolvedDatum> data, string series, string color, int width)
    {
        var values = data.Select(Value).ToList();
        var maximum = values.Where(value => value.HasValue).Select(value => Math.Abs(value!.Value)).DefaultIfEmpty(1m).Max();
        if (maximum == 0m) maximum = 1m;
        var labelWidth = Math.Clamp(data.Select(Label).DefaultIfEmpty("").Max(label => label.Length), 6, Math.Max(6, width / 3));
        var barWidth = Math.Max(8, width - labelWidth - 18);
        var ansi = SafeAnsiColor(color);
        var rows = data.Select(datum =>
        {
            var label = Truncate(Label(datum), labelWidth).PadRight(labelWidth);
            if (datum.IsGap) return (IRenderable)new Markup($"{Markup.Escape(label)} [grey]gap[/]");
            var value = Value(datum) ?? 0m;
            var bar = FractionalBar(Math.Abs(value) / maximum, barWidth);
            var sign = value < 0m ? "◀" : value == 0m ? "│" : "▶";
            return new Markup($"{Markup.Escape(label)} [{ansi}]{sign}{bar}[/] {Markup.Escape(DisplayValue(datum))}");
        }).ToList();
        rows.Insert(0, new Markup($"[bold]{Markup.Escape(series)}[/] [grey](fractional bars)[/]"));
        return new Rows(rows);
    }

    private static IRenderable RenderLine(IReadOnlyList<ResolvedDatum> data, string series, string color, int width, bool area)
    {
        var values = data.Select(Value).ToList();
        var numeric = values.Where(value => value.HasValue).Select(value => value!.Value).ToList();
        if (numeric.Count == 0) return new Markup($"[bold]{Markup.Escape(series)}[/]: [grey]all values are gaps[/]");
        var min = numeric.Min(); var max = numeric.Max();
        if (min == max) max = min + 1m;
        var canvas = new BrailleCanvas(Math.Clamp(width - 8, 16, 52), 7);
        var ansi = SafeAnsiColor(color);
        (int X, int Y)? previous = null;
        for (var index = 0; index < data.Count; index++)
        {
            if (data[index].IsGap || !values[index].HasValue) { previous = null; continue; }
            var x = data.Count == 1 ? 0 : index * (canvas.DotWidth - 1) / (data.Count - 1);
            var y = canvas.DotHeight - 1 - (int)((values[index]!.Value - min) / (max - min) * (canvas.DotHeight - 1));
            if (previous.HasValue) canvas.Line(previous.Value.X, previous.Value.Y, x, y, ansi);
            else canvas.Set(x, y, ansi);
            if (area) for (var fill = y + 1; fill < canvas.DotHeight; fill += 2) canvas.Set(x, fill, ansi);
            previous = (x, y);
        }
        var gaps = data.Count(datum => datum.IsGap);
        return new Rows(
            new Markup($"[{ansi}]●[/] [bold]{Markup.Escape(series)}[/] [grey]({(area ? "Braille area" : "Braille line")}; {gaps} gaps)[/]"),
            canvas.ToRenderable(),
            new Markup($"[grey]{min.ToString(CultureInfo.InvariantCulture)} … {max.ToString(CultureInfo.InvariantCulture)}[/]"));
    }

    private static IRenderable RenderPoints(IReadOnlyList<ResolvedDatum> data, string series, string color, int seriesOrder)
    {
        var glyph = PointGlyphs[Math.Abs(seriesOrder) % PointGlyphs.Length];
        var ansi = SafeAnsiColor(color);
        var rows = data.Select(datum => datum.IsGap
            ? (IRenderable)new Markup($"[grey]○ {Markup.Escape(Label(datum))}: gap[/]")
            : new Markup($"[{ansi}]{glyph}[/] {Markup.Escape(Label(datum))}: {Markup.Escape(DisplayValue(datum))}"));
        return new Rows(new[] { (IRenderable)new Markup($"[bold]{Markup.Escape(series)}[/] [grey](point glyph {glyph})[/]") }.Concat(rows));
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
        return trimmed.StartsWith('#') ? $"#{trimmed.TrimStart('#')}" : trimmed.ToLowerInvariant();
    }

    private static IRenderable RenderArcs(PlotPlan plan,
        IReadOnlyList<(ResolvedMarkLayer Layer, List<ResolvedDatum> Data)> layers, int width)
    {
        var components = layers.SelectMany(item => item.Data.Select(datum =>
        {
            var datumLabel = Label(datum);
            var series = layers.Count == 1
                ? plan.Series.FirstOrDefault(candidate => candidate.Key == datumLabel)
                : plan.Series.FirstOrDefault(candidate => candidate.Key == item.Layer.SeriesKey);
            var label = layers.Count > 1 && item.Data.Count == 1 ? series?.Label ?? datumLabel : datumLabel;
            return (Datum: datum, Label: label, Color: series?.Color ?? "#808080", Value: Math.Max(0m, Value(datum) ?? 0m));
        })).ToList();
        var total = components.Sum(component => component.Value);
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
        var source = plan.Layers.FirstOrDefault(layer => layer.Mark is not MarkKind.Rule)?.Data ?? [];
        var groups = source.GroupBy(datum =>
        {
            var row = Channel(datum, FieldChannel.Row);
            var column = Channel(datum, FieldChannel.Column);
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
            Channel(datum, FieldChannel.Close) ?? Channel(datum, FieldChannel.Size) ?? Channel(datum, FieldChannel.YEnd) ?? ChartValue.Null());
    private static string DisplayValue(ResolvedDatum datum)
    {
        var channel = datum.Channels.FirstOrDefault(item => item.Channel is FieldChannel.Y or FieldChannel.Y2 or FieldChannel.Radius or
            FieldChannel.Median or FieldChannel.Close or FieldChannel.Size or FieldChannel.YEnd);
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
}
