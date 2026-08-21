using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Reporting.Semantics.Runtime;

namespace ETL_SQL.Reporting.Renderers;

internal sealed class PlotPlanEChartsRenderer
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public string Render(PlotPlan plan)
    {
        plan.Validate();
        var polar = plan.Layers.Any(layer => layer.Mark == MarkKind.Arc);
        var series = polar ? RenderPolar(plan) : RenderCartesian(plan);
        var option = new Dictionary<string, object?>
        {
            ["title"] = new { text = plan.Title ?? plan.SpecId },
            ["tooltip"] = new { trigger = polar ? "item" : "axis" },
            ["legend"] = Legend(plan),
            ["series"] = series
        };

        if (!polar && !plan.Facets.IsDefaultOrEmpty)
        {
            ApplyFacets(option, plan);
            return JsonSerializer.Serialize(option, JsonOptions);
        }

        if (!polar)
        {
            var x = plan.Scales.FirstOrDefault(scale => scale.Channel == FieldChannel.X);
            var transposed = plan.Coordinate?.Kind == CoordinateKind.TransposedCartesian;
            if (transposed)
            {
                var valueAxes = plan.Scales.Where(scale => scale.Channel is FieldChannel.Y or FieldChannel.Y2).ToList();
                option["xAxis"] = valueAxes.Select((scale, index) => WithPosition(Axis(plan, scale, index), index == 0 ? "bottom" : "top")).ToArray();
                option["yAxis"] = new Dictionary<string, object?>
                {
                    ["type"] = "category",
                    ["data"] = x?.Categories ?? [],
                    ["name"] = Style(plan, "axis:x:label")
                };
                return JsonSerializer.Serialize(option, JsonOptions);
            }
            var xAxis = new Dictionary<string, object?>
            {
                ["type"] = x?.Kind is ScaleKind.Band or ScaleKind.Point or ScaleKind.Ordinal ? "category" : x?.Kind == ScaleKind.Time ? "time" : "value"
            };
            if (x?.Kind is ScaleKind.Band or ScaleKind.Point or ScaleKind.Ordinal) xAxis["data"] = x.Categories;
            else if (x is not null && x.Domain.Length > 1 && Number(x.Domain[0]) is { } xMinimum && Number(x.Domain[^1]) is { } xMaximum)
            {
                xAxis["min"] = xMinimum;
                xAxis["max"] = xMaximum;
            }
            var xLabel = Style(plan, "axis:x:label");
            if (xLabel is not null) xAxis["name"] = xLabel;
            option["xAxis"] = xAxis;
            var yScales = plan.Scales.Where(scale => scale.Channel is FieldChannel.Y or FieldChannel.Y2).ToList();
            option["yAxis"] = yScales.Select((scale, index) => Axis(plan, scale, index)).ToArray();
        }

        return JsonSerializer.Serialize(option, JsonOptions);

        static Dictionary<string, object?> WithPosition(Dictionary<string, object?> axis, string position)
        {
            axis["position"] = position;
            return axis;
        }
    }

    private static void ApplyFacets(Dictionary<string, object?> option, PlotPlan plan)
    {
        var rowCount = Math.Max(1, plan.Facets.Select(facet => facet.RowLabel).Distinct(StringComparer.Ordinal).Count());
        var columnCount = Math.Max(1, plan.Facets.Select(facet => facet.ColumnLabel).Distinct(StringComparer.Ordinal).Count());
        var grids = new List<object>();
        var xAxes = new List<object>();
        var yAxes = new List<object>();
        var allSeries = new List<object>();
        for (var panelIndex = 0; panelIndex < plan.Facets.Length; panelIndex++)
        {
            var facet = plan.Facets[panelIndex];
            var row = panelIndex / columnCount;
            var column = panelIndex % columnCount;
            grids.Add(new
            {
                left = $"{3 + column * (94m / columnCount):0.##}%",
                top = $"{10 + row * (84m / rowCount):0.##}%",
                width = $"{88m / columnCount:0.##}%",
                height = $"{72m / rowCount:0.##}%",
                containLabel = true
            });
            var x = facet.Scales.FirstOrDefault(scale => scale.Channel == FieldChannel.X);
            var xAxis = new Dictionary<string, object?>
            {
                ["type"] = x?.Kind is ScaleKind.Band or ScaleKind.Point or ScaleKind.Ordinal ? "category" : x?.Kind == ScaleKind.Time ? "time" : "value",
                ["gridIndex"] = panelIndex,
                ["name"] = string.Join(" / ", new[] { facet.RowLabel, facet.ColumnLabel }.Where(value => !string.IsNullOrEmpty(value)))
            };
            if (x?.Kind is ScaleKind.Band or ScaleKind.Point or ScaleKind.Ordinal) xAxis["data"] = x.Categories;
            xAxes.Add(xAxis);
            var panelYScales = facet.Scales.Where(scale => scale.Channel is FieldChannel.Y or FieldChannel.Y2).ToList();
            var yBase = yAxes.Count;
            foreach (var (scale, localIndex) in panelYScales.Select((value, index) => (value, index)))
            {
                var axis = Axis(plan, scale, localIndex);
                axis["gridIndex"] = panelIndex;
                yAxes.Add(axis);
            }
            var rows = facet.RowIndices.ToHashSet();
            var panelPlan = plan with
            {
                Scales = facet.Scales,
                Layers = plan.Layers.Select(layer => layer with { Data = layer.Data.Where(datum => rows.Contains(datum.RowIndex)).ToImmutableArray() }).ToImmutableArray(),
                Facets = []
            };
            foreach (var item in RenderCartesian(panelPlan).OfType<Dictionary<string, object?>>())
            {
                item["xAxisIndex"] = panelIndex;
                var localY = item.TryGetValue("yAxisIndex", out var value) && value is int index ? index : 0;
                item["yAxisIndex"] = yBase + localY;
                allSeries.Add(item);
            }
        }
        option["grid"] = grids;
        option["xAxis"] = xAxes;
        option["yAxis"] = yAxes;
        option["series"] = allSeries;
    }

    private static List<object> RenderCartesian(PlotPlan plan)
    {
        var output = new List<object>();
        var markLines = new List<object>();
        var xScale = plan.Scales.FirstOrDefault(scale => scale.Channel == FieldChannel.X);
        var transposed = plan.Coordinate?.Kind == CoordinateKind.TransposedCartesian;
        var coordinatePairs = transposed || xScale?.Kind is ScaleKind.Time or ScaleKind.Linear or ScaleKind.Logarithmic;
        foreach (var layer in plan.Layers)
        {
            if (layer.Mark == MarkKind.Rule)
            {
                var value = layer.Data.Select(datum => Channel(datum, FieldChannel.Y)).FirstOrDefault(item => item is not null);
                if (value is not null)
                    markLines.Add(new
                    {
                        yAxis = Number(value),
                        name = Style(layer, "label") ?? layer.Id,
                        lineStyle = new { type = Style(layer, "lineStyle") ?? "dashed", color = Style(layer, "color") ?? "#888888" },
                        label = new { formatter = Style(layer, "label") ?? layer.Id }
                    });
                continue;
            }

            var series = new Dictionary<string, object?>
            {
                ["type"] = layer.Mark switch { MarkKind.Rect => "bar", MarkKind.Point or MarkKind.Text => "scatter", MarkKind.Area => "line", _ => "line" },
                ["name"] = plan.Series.FirstOrDefault(item => item.Key == layer.SeriesKey)?.Label ?? layer.Id,
                ["itemStyle"] = new { color = plan.Palette.FirstOrDefault(item => item.SeriesKey == layer.SeriesKey)?.Color },
                ["connectNulls"] = false
            };
            if (layer.Mark == MarkKind.Rect && IsOn(plan, "STACKED")) series["stack"] = "total";
            if (layer.Mark == MarkKind.Area) series["areaStyle"] = new { };
            if (layer.Mark == MarkKind.Text) series["symbolSize"] = 0;
            if (layer.Mark == MarkKind.Line && IsOn(plan, "SMOOTH")) series["smooth"] = true;
            if (layer.Mark == MarkKind.Point || coordinatePairs)
            {
                series["data"] = layer.Data.Select(datum => datum.IsGap ? null : DataItem(datum, transposed
                    ? new object?[] { Scalar(Channel(datum, FieldChannel.Y) ?? Channel(datum, FieldChannel.Y2)), Scalar(Channel(datum, FieldChannel.X)) }
                    : new object?[] { Scalar(Channel(datum, FieldChannel.X)), Scalar(Channel(datum, FieldChannel.Y) ?? Channel(datum, FieldChannel.Y2)) })).ToArray();
            }
            else
            {
                series["data"] = layer.Data.Select(datum => datum.IsGap
                    ? null
                    : DataItem(datum, Scalar(Channel(datum, FieldChannel.Y) ?? Channel(datum, FieldChannel.Y2)))).ToArray();
            }
            if (IsOn(plan, "DATA_LABELS")) series["label"] = new { show = true, position = (Style(plan, "DATA_LABELS:POSITION") ?? "top").ToLowerInvariant() };
            if (layer.Data.Any(datum => Channel(datum, FieldChannel.Y2) is not null)) series["yAxisIndex"] = 1;
            output.Add(series);
        }

        if (markLines.Count > 0 && output.FirstOrDefault() is Dictionary<string, object?> first)
            first["markLine"] = new { symbol = "none", data = markLines };
        return output;
    }

    private static List<object> RenderPolar(PlotPlan plan)
    {
        var layer = plan.Layers.First(item => item.Mark == MarkKind.Arc);
        var donut = plan.Coordinate?.InnerRadius is > 0;
        var data = layer.Data.Where(datum => !datum.IsGap).Select((datum, index) =>
        {
            var label = Channel(datum, FieldChannel.Theta);
            var key = label is null ? $"Slice {index + 1}" : PlotPlanResolver.Display(label);
            var color = plan.Palette.FirstOrDefault(item => item.SeriesKey == key)?.Color;
            var item = DataItem(datum, Scalar(Channel(datum, FieldChannel.Radius)), color);
            item["name"] = key;
            return item;
        }).Cast<object>().ToList();
        return [new { type = "pie", name = plan.Title ?? plan.SpecId, radius = donut ? new[] { "40%", "70%" } : new[] { "0%", "60%" }, data }];
    }

    private static ChartValue? Channel(ResolvedDatum datum, FieldChannel channel) =>
        datum.Channels.FirstOrDefault(item => item.Channel == channel)?.Value;

    private static object? Scalar(ChartValue? value)
    {
        if (value is null || value.Kind == ChartValueKind.Null) return null;
        return value.Kind switch
        {
            ChartValueKind.Integer => value.Integer,
            ChartValueKind.FloatingPoint => value.FloatingPoint,
            ChartValueKind.Decimal => value.Decimal,
            ChartValueKind.Boolean => value.Boolean,
            _ => PlotPlanResolver.Display(value)
        };
    }

    private static Dictionary<string, object?> DataItem(ResolvedDatum datum, object? value, string? defaultColor = null)
    {
        var result = new Dictionary<string, object?> { ["value"] = value };
        var color = Encoding(datum, ConditionalEncodingChannel.Color);
        var opacity = Encoding(datum, ConditionalEncodingChannel.Opacity);
        if (color is not null || opacity is not null || defaultColor is not null)
            result["itemStyle"] = new { color = color is null ? defaultColor : Scalar(color), opacity = opacity is null ? null : Scalar(opacity) };
        if (Encoding(datum, ConditionalEncodingChannel.Size) is { } size) result["symbolSize"] = Scalar(size);
        if (Encoding(datum, ConditionalEncodingChannel.Shape) is { } shape) result["symbol"] = Scalar(shape);
        var text = Encoding(datum, ConditionalEncodingChannel.Text) ?? Channel(datum, FieldChannel.Text);
        if (text is not null) result["label"] = new { show = true, formatter = Scalar(text) };
        return result;
    }

    private static ChartValue? Encoding(ResolvedDatum datum, ConditionalEncodingChannel channel) =>
        datum.Encodings.IsDefault ? null : datum.Encodings.FirstOrDefault(item => item.Channel == channel)?.Value;

    private static object? Number(ChartValue value) => PlotPlanResolver.Number(value);
    private static Dictionary<string, object?> Axis(PlotPlan plan, ResolvedScale scale, int index)
    {
        var axis = new Dictionary<string, object?> { ["type"] = scale.Kind == ScaleKind.Logarithmic ? "log" : "value", ["position"] = index == 0 ? "left" : "right" };
        if (scale.Domain.Length > 1)
        {
            axis["min"] = Number(scale.Domain[0]);
            axis["max"] = Number(scale.Domain[^1]);
        }
        var label = Style(plan, index == 0 ? "axis:y:label" : "axis:y2:label");
        if (label is not null) axis["name"] = label;
        return axis;
    }

    private static Dictionary<string, object?> Legend(PlotPlan plan)
    {
        var position = (Style(plan, "LEGEND_POSITION") ?? Style(plan, "LEGEND") ?? "bottom").ToLowerInvariant();
        var legend = new Dictionary<string, object?> { ["data"] = plan.Legend.Select(entry => entry.Label).ToArray() };
        if (position is "left" or "right")
        {
            legend["orient"] = "vertical";
            legend[position] = position;
            legend["top"] = "middle";
        }
        else
        {
            legend["orient"] = "horizontal";
            legend[position == "top" ? "top" : "bottom"] = position == "top" ? "top" : "bottom";
        }
        return legend;
    }

    private static string? Style(PlotPlan plan, string name) =>
        plan.Style.IsDefault ? null : plan.Style.FirstOrDefault(token => token.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;
    private static string? Style(ResolvedMarkLayer layer, string name) =>
        layer.Style.IsDefault ? null : layer.Style.FirstOrDefault(token => token.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;
    private static bool IsOn(PlotPlan plan, string name) => !plan.Style.IsDefault &&
        plan.Style.Any(token => token.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && token.Value.ToUpperInvariant() is "ON" or "TRUE" or "1");
}
