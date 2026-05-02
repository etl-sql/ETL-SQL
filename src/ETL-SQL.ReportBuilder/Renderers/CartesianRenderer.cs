using System;
using System.Collections.Generic;
using System.Linq;

namespace ETL_SQL.ReportBuilder.Renderers
{
    internal class CartesianRenderer : RendererBase
    {
        private readonly OverlayRenderer _overlay = new();

        public string Render(VisualManifest visual, string seriesType)
        {
            var (xLabels, series) = ExtractCartesianSeries(visual, seriesType);
            bool stacked = IsOn(visual.Options.GetValueOrDefault("STACKED"));
            bool smooth = IsOn(visual.Options.GetValueOrDefault("SMOOTH")) && seriesType == "line";
            _overlay.AppendOverlaySeries(visual, series, xLabels, horizontal: false);
            return Serialize(new
            {
                title = TitleOpt(visual),
                tooltip = new { trigger = "axis" },
                legend = LegendOpt(visual),
                xAxis = BuildAxisOpts(visual, "x", "category", xLabels),
                yAxis = BuildAxisOpts(visual, "y", "value"),
                series = ApplyCommonSeriesOptions(visual, series, stacked, smooth)
            });
        }

        public string RenderHorizontalBar(VisualManifest v)
        {
            var (labels, series) = ExtractCartesianSeries(v, "bar");
            _overlay.AppendOverlaySeries(v, series, labels, horizontal: true);
            return Serialize(new
            {
                title = TitleOpt(v),
                tooltip = new { trigger = "axis" },
                legend = LegendOpt(v),
                xAxis = BuildAxisOpts(v, "y", "value"),
                yAxis = BuildAxisOpts(v, "x", "category", labels),
                series = ApplyCommonSeriesOptions(v, series, stacked: IsOn(v.Options.GetValueOrDefault("STACKED")), smooth: false)
            });
        }

        public string RenderCombo(VisualManifest v)
        {
            var xCol = FindRole(v, "x") ?? (v.Columns.Count > 0 ? v.Columns[0] : null);
            var yCol = FindRole(v, "y") ?? (v.Columns.Count > 1 ? v.Columns[1] : null);
            var seriesCol = FindRole(v, "series");
            var defs = v.SeriesDefs ?? new List<SeriesDefManifest>();

            int xi = xCol != null ? v.Columns.FindIndex(c => string.Equals(c, xCol, StringComparison.OrdinalIgnoreCase)) : 0;
            int yi = yCol != null ? v.Columns.FindIndex(c => string.Equals(c, yCol, StringComparison.OrdinalIgnoreCase)) : 1;
            int si = (seriesCol != null) ? v.Columns.FindIndex(c => string.Equals(c, seriesCol, StringComparison.OrdinalIgnoreCase)) : -1;

            List<string> xLabels;
            List<object> seriesList = new();

            if (si < 0)
            {
                xLabels = v.Rows.Select(r => xi >= 0 && xi < r.Count ? r[xi]?.ToString() ?? "" : "").ToList();
                foreach (var def in defs)
                {
                    int ci = v.Columns.FindIndex(c => string.Equals(c, def.Column, StringComparison.OrdinalIgnoreCase));
                    var data = v.Rows.Select(r => (object?)(ci >= 0 && ci < r.Count ? ToDouble(r[ci]) : null)).ToList();
                    seriesList.Add(new { type = def.SeriesType.ToLowerInvariant(), name = def.Column, data });
                }
            }
            else
            {
                xLabels = SortXLabels(v.Rows.Select(r => xi >= 0 && xi < r.Count ? r[xi]?.ToString() ?? "" : "").ToList());
                var xIndex = xLabels.Select((l, i) => (l, i)).ToDictionary(t => t.l, t => t.i, StringComparer.OrdinalIgnoreCase);
                var seriesKeys = v.Rows.Select(r => si < r.Count ? r[si]?.ToString() ?? "" : "").Distinct().ToList();

                foreach (var sk in seriesKeys)
                {
                    var vals = Enumerable.Repeat<object?>(null, xLabels.Count).ToList();
                    var trimmedSk = sk.Trim();
                    foreach (var row in v.Rows)
                    {
                        var rowSk = (si < row.Count ? row[si]?.ToString() ?? "" : "").Trim();
                        if (!string.Equals(rowSk, trimmedSk, StringComparison.OrdinalIgnoreCase)) continue;
                        var xl = (xi < row.Count ? row[xi]?.ToString() ?? "" : "").Trim();
                        if (!xIndex.TryGetValue(xl, out var idx)) continue;
                        vals[idx] = ToDouble(yi < row.Count ? row[yi] : null);
                    }
                    var typeDef = defs.FirstOrDefault(d => string.Equals(d.Column, sk, StringComparison.OrdinalIgnoreCase));
                    var type = typeDef?.SeriesType?.ToLowerInvariant() ?? "line";
                    seriesList.Add(new { type, name = sk, data = vals });
                }
            }

            bool dualAxis = si < 0 && defs.Count == 2 && defs.Select(d => d.SeriesType.ToLowerInvariant()).Distinct().Count() > 1;

            if (dualAxis)
            {
                var dualSeriesList = new List<object>();
                for (int i = 0; i < seriesList.Count && i < defs.Count; i++)
                {
                    string defType = defs[i].SeriesType.ToLowerInvariant();
                    var json2 = Serialize(seriesList[i]);
                    var dict2 = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(json2)!;
                    dict2["yAxisIndex"] = string.Equals(defType, "line", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                    dualSeriesList.Add(dict2);
                }
                _overlay.AppendOverlaySeries(v, dualSeriesList, xLabels, horizontal: false);
                return Serialize(new
                {
                    title = TitleOpt(v),
                    tooltip = new { trigger = "axis" },
                    legend = LegendOpt(v),
                    xAxis = BuildAxisOpts(v, "x", "category", xLabels),
                    yAxis = new object[] { BuildAxisOpts(v, "y", "value"), new Dictionary<string, object?> { ["type"] = "value", ["position"] = "right", ["splitLine"] = new { show = false } } },
                    series = ApplyCommonSeriesOptions(v, dualSeriesList, stacked: false, smooth: false)
                });
            }

            _overlay.AppendOverlaySeries(v, seriesList, xLabels, horizontal: false);
            return Serialize(new
            {
                title = TitleOpt(v),
                tooltip = new { trigger = "axis" },
                legend = LegendOpt(v),
                xAxis = BuildAxisOpts(v, "x", "category", xLabels),
                yAxis = BuildAxisOpts(v, "y", "value"),
                series = ApplyCommonSeriesOptions(v, seriesList, stacked: false, smooth: false)
            });
        }

        private (List<string> labels, List<object> series) ExtractCartesianSeries(VisualManifest v, string seriesType)
        {
            var xCol = FindRole(v, "x") ?? (v.Columns.Count > 0 ? v.Columns[0] : null);
            var yCol = FindRole(v, "y") ?? (v.Columns.Count > 1 ? v.Columns[1] : null);
            var seriesCol = FindRole(v, "series");

            int xi = xCol != null ? v.Columns.FindIndex(c => string.Equals(c, xCol, StringComparison.OrdinalIgnoreCase)) : 0;
            int yi = yCol != null ? v.Columns.FindIndex(c => string.Equals(c, yCol, StringComparison.OrdinalIgnoreCase)) : 1;
            int si = seriesCol != null ? v.Columns.FindIndex(c => string.Equals(c, seriesCol, StringComparison.OrdinalIgnoreCase)) : -1;

            if (si < 0)
            {
                var rawPairs = v.Rows.Select(r =>
                {
                    var xLabel = xi >= 0 && xi < r.Count ? r[xi]?.ToString() ?? "" : "";
                    var num = ToDouble(yi >= 0 && yi < r.Count ? r[yi] : null);
                    var color = GetColor(v, xLabel);
                    var valObj = color != null ? (object?)new { value = num, itemStyle = new { color } } : (object?)num;
                    return new { xLabel, valObj };
                }).ToList();

                var sortedLabels = SortXLabels(rawPairs.Select(p => p.xLabel).Distinct().ToList());
                var labelIndex = sortedLabels.Select((l, i) => (l, i)).ToDictionary(t => t.l, t => t.i, StringComparer.OrdinalIgnoreCase);

                var alignedVals = Enumerable.Repeat<object?>(null, sortedLabels.Count).ToList();
                foreach (var pair in rawPairs)
                {
                    var lbl = pair.xLabel.Trim();
                    if (labelIndex.TryGetValue(lbl, out var sortedIdx)) alignedVals[sortedIdx] = pair.valObj;
                }
                return (sortedLabels, new List<object> { new { type = seriesType, name = yCol ?? v.Name, data = alignedVals } });
            }
            else
            {
                var xLabels = SortXLabels(v.Rows.Select(r => xi >= 0 && xi < r.Count ? r[xi]?.ToString() ?? "" : "").Distinct().ToList());
                var seriesKeys = v.Rows.Select(r => si < r.Count ? r[si]?.ToString() ?? "" : "").Distinct().ToList();
                var xIndex = xLabels.Select((l, i) => (l, i)).ToDictionary(t => t.l, t => t.i, StringComparer.OrdinalIgnoreCase);
                bool fillGaps = IsOn(v.Options.GetValueOrDefault("SHOW_NO_DATA_PLACEHOLDER"));
                var seriesList = new List<object>();
                foreach (var sk in seriesKeys)
                {
                    var vals = Enumerable.Repeat<object?>(fillGaps ? 0.0 : null, xLabels.Count).ToList();
                    var trimmedSk = sk.Trim();
                    foreach (var row in v.Rows)
                    {
                        var rowSk = (si < row.Count ? row[si]?.ToString() ?? "" : "").Trim();
                        if (!string.Equals(rowSk, trimmedSk, StringComparison.OrdinalIgnoreCase)) continue;
                        var xl = (xi < row.Count ? row[xi]?.ToString() ?? "" : "").Trim();
                        if (!xIndex.TryGetValue(xl, out var idx)) continue;
                        vals[idx] = ToDouble(yi < row.Count ? row[yi] : null) ?? (fillGaps ? 0.0 : null);
                    }
                    seriesList.Add(new { type = seriesType, name = sk, data = vals });
                }
                return (xLabels, seriesList);
            }
        }
    }
}
