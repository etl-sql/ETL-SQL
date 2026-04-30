using System;
using System.Collections.Generic;
using System.Linq;

namespace ETL_SQL.ReportBuilder.Renderers
{
    internal class SpecializedRenderer : RendererBase
    {
        public string RenderScatter(VisualManifest v)
        {
            var xCol = FindRole(v, "x") ?? (v.Columns.Count > 0 ? v.Columns[0] : null);
            var yCol = FindRole(v, "y") ?? (v.Columns.Count > 1 ? v.Columns[1] : null);

            int xi = xCol != null ? v.Columns.FindIndex(c => string.Equals(c, xCol, StringComparison.OrdinalIgnoreCase)) : 0;
            int yi = yCol != null ? v.Columns.FindIndex(c => string.Equals(c, yCol, StringComparison.OrdinalIgnoreCase)) : 1;

            var data = v.Rows.Select(r => new[]
            {
                ToDouble(xi >= 0 && xi < r.Count ? r[xi] : null) ?? 0.0,
                ToDouble(yi >= 0 && yi < r.Count ? r[yi] : null) ?? 0.0
            }).ToList();

            var series = new List<object> { new { type = "scatter", name = v.Name, data } };
            return Serialize(new
            {
                title = TitleOpt(v),
                tooltip = new { trigger = "item" },
                xAxis = new { },
                yAxis = new { },
                series = ApplyCommonSeriesOptions(v, series, stacked: false, smooth: false)
            });
        }

        public string RenderHeatMap(VisualManifest v)
        {
            var xCol = FindRole(v, "x") ?? (v.Columns.Count > 0 ? v.Columns[0] : null);
            var yCol = FindRole(v, "y") ?? (v.Columns.Count > 1 ? v.Columns[1] : null);
            var valC = FindRole(v, "value") ?? (v.Columns.Count > 2 ? v.Columns[2] : null);

            int xi = xCol != null ? v.Columns.FindIndex(c => string.Equals(c, xCol, StringComparison.OrdinalIgnoreCase)) : 0;
            int yi = yCol != null ? v.Columns.FindIndex(c => string.Equals(c, yCol, StringComparison.OrdinalIgnoreCase)) : 1;
            int vi = valC != null ? v.Columns.FindIndex(c => string.Equals(c, valC, StringComparison.OrdinalIgnoreCase)) : 2;

            var xCats = v.Rows.Select(r => xi >= 0 && xi < r.Count ? r[xi]?.ToString() ?? "" : "").Distinct().ToList();
            var yCats = v.Rows.Select(r => yi >= 0 && yi < r.Count ? r[yi]?.ToString() ?? "" : "").Distinct().ToList();
            var xMap = xCats.Select((c, i) => (c, i)).ToDictionary(t => t.c, t => t.i);
            var yMap = yCats.Select((c, i) => (c, i)).ToDictionary(t => t.c, t => t.i);

            var values = v.Rows
                .Select(r => new[]
                {
                    (double)(xMap.TryGetValue(xi >= 0 && xi < r.Count ? r[xi]?.ToString() ?? "" : "", out var x2) ? x2 : 0),
                    (double)(yMap.TryGetValue(yi >= 0 && yi < r.Count ? r[yi]?.ToString() ?? "" : "", out var y2) ? y2 : 0),
                    ToDouble(vi >= 0 && vi < r.Count ? r[vi] : null) ?? 0.0
                })
                .ToList();

            double maxVal = values.Count > 0 ? values.Max(p => p[2]) : 1;

            return Serialize(new
            {
                title = TitleOpt(v),
                tooltip = new { trigger = "item" },
                visualMap = new { min = 0, max = maxVal, calculable = true, orient = "horizontal", left = "center", bottom = "15%" },
                xAxis = new { type = "category", data = xCats },
                yAxis = new { type = "category", data = yCats },
                series = new[] { new { type = "heatmap", data = values, label = new { show = true } } }
            });
        }

        public string RenderGauge(VisualManifest v)
        {
            var valueCol = FindRole(v, "value") ?? (v.Columns.Count > 0 ? v.Columns[0] : null);
            var labelCol = FindRole(v, "label") ?? (v.Columns.Count > 1 ? v.Columns[1] : null);
            var maxCol = FindRole(v, "max");

            int vi = valueCol != null ? v.Columns.FindIndex(c => string.Equals(c, valueCol, StringComparison.OrdinalIgnoreCase)) : 0;
            int li = labelCol != null ? v.Columns.FindIndex(c => string.Equals(c, labelCol, StringComparison.OrdinalIgnoreCase)) : -1;
            int mi = maxCol != null ? v.Columns.FindIndex(c => string.Equals(c, maxCol, StringComparison.OrdinalIgnoreCase)) : -1;

            var firstRow = v.Rows.Count > 0 ? v.Rows[0] : null;
            var value = firstRow != null ? ToDouble(vi >= 0 && vi < firstRow.Count ? firstRow[vi] : null) ?? 0.0 : 0.0;
            var name = firstRow != null && li >= 0 && li < firstRow.Count ? firstRow[li]?.ToString() ?? "" : (labelCol ?? "");

            double gaugeMin = 0, gaugeMax = 100;
            if (v.Options.TryGetValue("MIN", out var minStr) && double.TryParse(minStr, out var mn)) gaugeMin = mn;
            if (v.Options.TryGetValue("MAX", out var maxStr) && double.TryParse(maxStr, out var mx)) gaugeMax = mx;
            else if (firstRow != null && mi >= 0 && mi < firstRow.Count) gaugeMax = ToDouble(firstRow[mi]) ?? 100.0;

            v.Options.TryGetValue("GAUGE_STYLE", out var style);
            bool isProgress = style?.ToUpperInvariant() == "PROGRESS";

            return Serialize(new
            {
                title = TitleOpt(v),
                tooltip = new { formatter = "{b}: {c}" },
                series = new[]
                {
                    new { type = "gauge", min = gaugeMin, max = gaugeMax,
                          progress = new { show = isProgress },
                          detail = new { valueAnimation = true, formatter = "{value:.1f}" },
                          data = new[] { new { value, name } } }
                }
            });
        }

        public string RenderFunnel(VisualManifest v)
        {
            var labelCol = FindRole(v, "label") ?? (v.Columns.Count > 0 ? v.Columns[0] : null);
            var valueCol = FindRole(v, "value") ?? (v.Columns.Count > 1 ? v.Columns[1] : null);

            int li = labelCol != null ? v.Columns.FindIndex(c => string.Equals(c, labelCol, StringComparison.OrdinalIgnoreCase)) : 0;
            int vi = valueCol != null ? v.Columns.FindIndex(c => string.Equals(c, valueCol, StringComparison.OrdinalIgnoreCase)) : 1;

            var data = v.Rows.Select(r => (object)new
            {
                name = li >= 0 && li < r.Count ? r[li]?.ToString() ?? "" : "",
                value = ToDouble(vi >= 0 && vi < r.Count ? r[vi] : null) ?? 0.0
            }).ToList();

            return Serialize(new
            {
                title = TitleOpt(v),
                tooltip = new { trigger = "item", formatter = "{a} <br/>{b}: {c}" },
                legend = LegendOpt(v),
                series = new[] { new { type = "funnel", name = v.Name, label = new { show = true, position = "inside" }, data } }
            });
        }

        public string RenderWaterfall(VisualManifest v)
        {
            var xCol = FindRole(v, "x") ?? (v.Columns.Count > 0 ? v.Columns[0] : null);
            var yCol = FindRole(v, "y") ?? (v.Columns.Count > 1 ? v.Columns[1] : null);

            int xi = xCol != null ? v.Columns.FindIndex(c => string.Equals(c, xCol, StringComparison.OrdinalIgnoreCase)) : 0;
            int yi = yCol != null ? v.Columns.FindIndex(c => string.Equals(c, yCol, StringComparison.OrdinalIgnoreCase)) : 1;

            var categories = v.Rows.Select(r => xi >= 0 && xi < r.Count ? r[xi]?.ToString() ?? "" : "").ToList();
            var rawVals = v.Rows.Select(r => ToDouble(yi >= 0 && yi < r.Count ? r[yi] : null) ?? 0.0).ToList();

            var bases = new List<double>();
            var deltas = new List<object>();
            double running = 0;
            foreach (var val in rawVals)
            {
                bases.Add(val >= 0 ? running : running + val);
                var color = val >= 0 ? (GetColor(v, "positive") ?? "#5cb85c") : (GetColor(v, "negative") ?? "#d9534f");
                deltas.Add(new { value = Math.Abs(val), itemStyle = new { color } });
                running += val;
            }

            return Serialize(new
            {
                title = TitleOpt(v),
                tooltip = new { trigger = "axis", axisPointer = new { type = "shadow" } },
                xAxis = new { type = "category", data = categories },
                yAxis = BuildAxisOpts(v, "y", "value"),
                series = new object[]
                {
                    new { type = "bar", stack = "total", itemStyle = new { color = "transparent" }, emphasis = new { itemStyle = new { color = "transparent" } }, data = bases },
                    new { type = "bar", stack = "total", name = v.Name, data = deltas }
                }
            });
        }
    }
}
