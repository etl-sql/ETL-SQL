using System;
using System.Collections.Generic;
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
            ["legend"] = new { orient = "horizontal", bottom = "bottom", data = plan.Legend.Select(entry => entry.Label).ToArray() },
            ["series"] = series
        };

        if (!polar)
        {
            var x = plan.Scales.FirstOrDefault(scale => scale.Channel == FieldChannel.X);
            option["xAxis"] = x?.Kind is ScaleKind.Band or ScaleKind.Point or ScaleKind.Ordinal
                ? new { type = "category", data = x.Categories }
                : new { type = x?.Kind == ScaleKind.Time ? "time" : "value" };
            var yScales = plan.Scales.Where(scale => scale.Channel is FieldChannel.Y or FieldChannel.Y2).ToList();
            option["yAxis"] = yScales.Count > 1
                ? yScales.Select((_, index) => new { type = "value", position = index == 0 ? "left" : "right" }).ToArray()
                : new object[] { new { type = "value" } };
        }

        return JsonSerializer.Serialize(option, JsonOptions);
    }

    private static List<object> RenderCartesian(PlotPlan plan)
    {
        var output = new List<object>();
        var markLines = new List<object>();
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
                ["type"] = layer.Mark switch { MarkKind.Rect => "bar", MarkKind.Point => "scatter", _ => "line" },
                ["name"] = plan.Series.FirstOrDefault(item => item.Key == layer.SeriesKey)?.Label ?? layer.Id,
                ["itemStyle"] = new { color = plan.Palette.FirstOrDefault(item => item.SeriesKey == layer.SeriesKey)?.Color },
                ["connectNulls"] = false
            };
            if (layer.Mark == MarkKind.Rect && IsOn(plan, "STACKED")) series["stack"] = "total";
            if (layer.Mark == MarkKind.Line && IsOn(plan, "SMOOTH")) series["smooth"] = true;
            if (layer.Mark == MarkKind.Point)
            {
                series["data"] = layer.Data.Where(datum => !datum.IsGap).Select(datum => new object?[]
                {
                    Scalar(Channel(datum, FieldChannel.X)),
                    Scalar(Channel(datum, FieldChannel.Y) ?? Channel(datum, FieldChannel.Y2))
                }).ToArray();
            }
            else
            {
                series["data"] = layer.Data.Select(datum => datum.IsGap
                    ? null
                    : Scalar(Channel(datum, FieldChannel.Y) ?? Channel(datum, FieldChannel.Y2))).ToArray();
            }
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
            return new
            {
                name = key,
                value = Scalar(Channel(datum, FieldChannel.Radius)),
                itemStyle = new { color }
            };
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

    private static object? Number(ChartValue value) => PlotPlanResolver.Number(value);
    private static string? Style(ResolvedMarkLayer layer, string name) =>
        layer.Style.IsDefault ? null : layer.Style.FirstOrDefault(token => token.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;
    private static bool IsOn(PlotPlan plan, string name) => !plan.Style.IsDefault &&
        plan.Style.Any(token => token.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && token.Value.ToUpperInvariant() is "ON" or "TRUE" or "1");
}
