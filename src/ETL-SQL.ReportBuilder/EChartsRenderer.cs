using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ETL_SQL.ReportBuilder
{
    /// <summary>
    /// Converts a <see cref="VisualManifest"/> into an Apache ECharts option JSON string.
    /// Output is suitable for <c>echarts.init(div).setOption(option)</c>.
    /// Replaces ChartJsRenderer as of Phase 9.2.
    /// </summary>
    public class EChartsRenderer
    {
        private static readonly JsonSerializerOptions _json = new()
        {
            WriteIndented            = false,
            PropertyNamingPolicy     = null,
            DefaultIgnoreCondition   = JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>
        /// Returns an ECharts option JSON string, or null for non-chart visual types.
        /// </summary>
        public string? Render(VisualManifest visual) =>
            visual.VisualType.ToUpperInvariant() switch
            {
                "BAR"          => RenderCartesian(visual, "bar"),
                "LINE"         => RenderCartesian(visual, "line"),
                "HBAR"         => RenderHorizontalBar(visual),
                "PIE"          => RenderPie(visual, donut: false),
                "DONUT"        => RenderPie(visual, donut: true),
                "SCATTER"      => RenderScatter(visual),
                "BOXPLOT"      => RenderBoxPlot(visual),
                "TREEMAP"      => RenderTreemap(visual),
                "HEATMAP"      => RenderHeatMap(visual),
                "COMBO"        => RenderCombo(visual),
                "GAUGE"        => RenderGauge(visual),
                "FUNNEL"       => RenderFunnel(visual),
                "WATERFALL"    => RenderWaterfall(visual),
                _              => null   // TABLE, CARD, SLICER, TEXT — rendered client-side
            };

        // ── BAR / LINE (vertical cartesian) ────────────────────────────────────

        private string RenderCartesian(VisualManifest v, string seriesType)
        {
            var (xLabels, series) = ExtractCartesianSeries(v, seriesType);
            bool stacked = IsOn(v.Options.GetValueOrDefault("STACKED"));
            bool smooth  = IsOn(v.Options.GetValueOrDefault("SMOOTH")) && seriesType == "line";
            if (stacked || smooth)
                series = ApplySeriesFlags(series, stacked, smooth);
            AppendOverlaySeries(v, series, xLabels, horizontal: false);
            return Serialize(new
            {
                title   = TitleOpt(v),
                tooltip = new { trigger = "axis" },
                legend  = LegendOpt(v),
                xAxis   = BuildAxisOpts(v, "x", "category", xLabels),
                yAxis   = BuildAxisOpts(v, "y", "value"),
                series
            });
        }

        // ── HORIZONTAL BAR ──────────────────────────────────────────────────────

        private string RenderHorizontalBar(VisualManifest v)
        {
            var (labels, series) = ExtractCartesianSeries(v, "bar");
            if (IsOn(v.Options.GetValueOrDefault("STACKED")))
                series = ApplySeriesFlags(series, stacked: true, smooth: false);
            AppendOverlaySeries(v, series, labels, horizontal: true);
            return Serialize(new
            {
                title   = TitleOpt(v),
                tooltip = new { trigger = "axis" },
                legend  = LegendOpt(v),
                xAxis   = BuildAxisOpts(v, "x", "value"),
                yAxis   = BuildAxisOpts(v, "y", "category", labels),
                series
            });
        }

        // ── PIE / DONUT ─────────────────────────────────────────────────────────

        private string RenderPie(VisualManifest v, bool donut)
        {
            var labelCol = FindRole(v, "label") ?? (v.Columns.Count > 0 ? v.Columns[0] : null);
            var valueCol = FindRole(v, "value") ?? (v.Columns.Count > 1 ? v.Columns[1] : null);

            int li = labelCol != null ? v.Columns.IndexOf(labelCol) : 0;
            int vi = valueCol != null ? v.Columns.IndexOf(valueCol) : 1;

            var data = v.Rows.Select(r =>
            {
                var name  = li >= 0 && li < r.Count ? r[li] ?? "" : "";
                var color = GetColor(v, name);
                return color != null
                    ? (object)new { name, value = ToDouble(vi >= 0 && vi < r.Count ? r[vi] : null) ?? 0.0, itemStyle = new { color } }
                    : (object)new { name, value = ToDouble(vi >= 0 && vi < r.Count ? r[vi] : null) ?? 0.0 };
            }).ToList();

            object radius = donut ? (object)new[] { "40%", "70%" } : "60%";

            return Serialize(new
            {
                title   = TitleOpt(v),
                tooltip = new { trigger = "item" },
                legend  = LegendOpt(v),
                series  = new[] { new { type = "pie", name = v.Name, radius, data } }
            });
        }

        // ── SCATTER ─────────────────────────────────────────────────────────────

        private string RenderScatter(VisualManifest v)
        {
            var xCol = FindRole(v, "x") ?? (v.Columns.Count > 0 ? v.Columns[0] : null);
            var yCol = FindRole(v, "y") ?? (v.Columns.Count > 1 ? v.Columns[1] : null);

            int xi = xCol != null ? v.Columns.IndexOf(xCol) : 0;
            int yi = yCol != null ? v.Columns.IndexOf(yCol) : 1;

            var data = v.Rows.Select(r => new[]
            {
                ToDouble(xi >= 0 && xi < r.Count ? r[xi] : null) ?? 0.0,
                ToDouble(yi >= 0 && yi < r.Count ? r[yi] : null) ?? 0.0
            }).ToList();

            return Serialize(new
            {
                title   = TitleOpt(v),
                tooltip = new { trigger = "item" },
                xAxis   = new { },
                yAxis   = new { },
                series  = new[] { new { type = "scatter", name = v.Name, data } }
            });
        }

        // ── BOXPLOT ─────────────────────────────────────────────────────────────
        // Expected column roles: x, min, q1, median, q3, max
        // If only x + y are present, computes statistics from y values grouped by x.

        private string RenderBoxPlot(VisualManifest v)
        {
            var xCol  = FindRole(v, "x")      ?? (v.Columns.Count > 0 ? v.Columns[0] : null);
            var minC  = FindRole(v, "min");
            var q1C   = FindRole(v, "q1");
            var medC  = FindRole(v, "median");
            var q3C   = FindRole(v, "q3");
            var maxC  = FindRole(v, "max");
            var yCol  = FindRole(v, "y")      ?? (v.Columns.Count > 1 ? v.Columns[1] : null);

            int xi    = xCol != null ? v.Columns.IndexOf(xCol) : 0;
            bool hasStats = minC != null && q1C != null && medC != null && q3C != null && maxC != null;

            List<string> categories;
            List<double[]> boxData;

            if (hasStats)
            {
                int minI = v.Columns.IndexOf(minC!);
                int q1I  = v.Columns.IndexOf(q1C!);
                int medI = v.Columns.IndexOf(medC!);
                int q3I  = v.Columns.IndexOf(q3C!);
                int maxI = v.Columns.IndexOf(maxC!);

                categories = v.Rows.Select(r => xi >= 0 && xi < r.Count ? r[xi] ?? "" : "").ToList();
                boxData = v.Rows.Select(r => new[]
                {
                    ToDouble(r.ElementAtOrDefault(minI)) ?? 0,
                    ToDouble(r.ElementAtOrDefault(q1I))  ?? 0,
                    ToDouble(r.ElementAtOrDefault(medI)) ?? 0,
                    ToDouble(r.ElementAtOrDefault(q3I))  ?? 0,
                    ToDouble(r.ElementAtOrDefault(maxI)) ?? 0
                }).ToList();
            }
            else
            {
                // Compute box stats from raw x+y data
                int yi = yCol != null ? v.Columns.IndexOf(yCol) : 1;
                var groups = v.Rows
                    .GroupBy(r => xi >= 0 && xi < r.Count ? r[xi] ?? "" : "")
                    .ToList();

                categories = groups.Select(g => g.Key).ToList();
                boxData = groups.Select(g =>
                {
                    var vals = g.Select(r => ToDouble(yi >= 0 && yi < r.Count ? r[yi] : null))
                                .Where(d => d.HasValue).Select(d => d!.Value)
                                .OrderBy(x => x).ToArray();
                    if (vals.Length == 0) return new[] { 0.0, 0.0, 0.0, 0.0, 0.0 };
                    return new[]
                    {
                        vals[0],
                        Percentile(vals, 25),
                        Percentile(vals, 50),
                        Percentile(vals, 75),
                        vals[^1]
                    };
                }).ToList();
            }

            return Serialize(new
            {
                title   = TitleOpt(v),
                tooltip = new { trigger = "item" },
                xAxis   = new { type = "category", data = categories },
                yAxis   = new { type = "value" },
                series  = new[] { new { type = "boxplot", data = boxData } }
            });
        }

        // ── TREEMAP ─────────────────────────────────────────────────────────────

        private string RenderTreemap(VisualManifest v)
        {
            var nameCol  = FindRole(v, "label") ?? FindRole(v, "name") ?? (v.Columns.Count > 0 ? v.Columns[0] : null);
            var valueCol = FindRole(v, "value") ?? (v.Columns.Count > 1 ? v.Columns[1] : null);

            int ni = nameCol  != null ? v.Columns.IndexOf(nameCol)  : 0;
            int vi = valueCol != null ? v.Columns.IndexOf(valueCol) : 1;

            var data = v.Rows.Select(r => (object)new
            {
                name  = ni >= 0 && ni < r.Count ? r[ni] ?? "" : "",
                value = ToDouble(vi >= 0 && vi < r.Count ? r[vi] : null) ?? 0.0
            }).ToList();

            return Serialize(new
            {
                title   = TitleOpt(v),
                tooltip = new { trigger = "item" },
                series  = new[]
                {
                    new { type = "treemap", name = v.Name, data,
                          label = new { show = true },
                          breadcrumb = new { show = false } }
                }
            });
        }

        // ── HEATMAP ─────────────────────────────────────────────────────────────
        // Expected roles: x (category), y (category), value (numeric)

        private string RenderHeatMap(VisualManifest v)
        {
            var xCol  = FindRole(v, "x")     ?? (v.Columns.Count > 0 ? v.Columns[0] : null);
            var yCol  = FindRole(v, "y")     ?? (v.Columns.Count > 1 ? v.Columns[1] : null);
            var valC  = FindRole(v, "value") ?? (v.Columns.Count > 2 ? v.Columns[2] : null);

            int xi = xCol != null ? v.Columns.IndexOf(xCol) : 0;
            int yi = yCol != null ? v.Columns.IndexOf(yCol) : 1;
            int vi = valC != null ? v.Columns.IndexOf(valC) : 2;

            var xCats = v.Rows.Select(r => xi >= 0 && xi < r.Count ? r[xi] ?? "" : "").Distinct().ToList();
            var yCats = v.Rows.Select(r => yi >= 0 && yi < r.Count ? r[yi] ?? "" : "").Distinct().ToList();
            var xMap  = xCats.Select((c, i) => (c, i)).ToDictionary(t => t.c, t => t.i);
            var yMap  = yCats.Select((c, i) => (c, i)).ToDictionary(t => t.c, t => t.i);

            var values = v.Rows
                .Select(r => new[]
                {
                    (double)(xMap.TryGetValue(xi >= 0 && xi < r.Count ? r[xi] ?? "" : "", out var x2) ? x2 : 0),
                    (double)(yMap.TryGetValue(yi >= 0 && yi < r.Count ? r[yi] ?? "" : "", out var y2) ? y2 : 0),
                    ToDouble(vi >= 0 && vi < r.Count ? r[vi] : null) ?? 0.0
                })
                .ToList();

            double maxVal = values.Count > 0 ? values.Max(p => p[2]) : 1;

            return Serialize(new
            {
                title      = TitleOpt(v),
                tooltip    = new { trigger = "item" },
                visualMap  = new { min = 0, max = maxVal, calculable = true, orient = "horizontal", left = "center", bottom = "15%" },
                xAxis      = new { type = "category", data = xCats },
                yAxis      = new { type = "category", data = yCats },
                series     = new[]
                {
                    new { type = "heatmap", data = values, label = new { show = true } }
                }
            });
        }

        // ── COMBO (mixed bar + line) ────────────────────────────────────────────

        private string RenderCombo(VisualManifest v)
        {
            var xCol    = FindRole(v, "x") ?? (v.Columns.Count > 0 ? v.Columns[0] : null);
            int xi      = xCol != null ? v.Columns.IndexOf(xCol) : 0;
            var xLabels = v.Rows.Select(r => xi >= 0 && xi < r.Count ? r[xi] ?? "" : "").ToList();

            var seriesList = new List<object>();
            var defs = v.SeriesDefs ?? new List<SeriesDefManifest>();
            foreach (var def in defs)
            {
                int ci = v.Columns.IndexOf(def.Column);
                var data = v.Rows.Select(r => (object?)(ci >= 0 && ci < r.Count ? ToDouble(r[ci]) : null)).ToList();
                seriesList.Add(new { type = def.SeriesType.ToLowerInvariant(), name = def.Column, data });
            }

            return Serialize(new
            {
                title   = TitleOpt(v),
                tooltip = new { trigger = "axis" },
                legend  = LegendOpt(v),
                xAxis   = BuildAxisOpts(v, "x", "category", xLabels),
                yAxis   = BuildAxisOpts(v, "y", "value"),
                series  = seriesList
            });
        }

        // ── GAUGE ────────────────────────────────────────────────────────────────
        // Roles: value (required), label (optional), max (optional)
        // Options: MIN (default 0), MAX (default 100), TITLE

        private string RenderGauge(VisualManifest v)
        {
            var valueCol = FindRole(v, "value") ?? (v.Columns.Count > 0 ? v.Columns[0] : null);
            var labelCol = FindRole(v, "label") ?? (v.Columns.Count > 1 ? v.Columns[1] : null);
            var maxCol   = FindRole(v, "max");

            int vi = valueCol != null ? v.Columns.IndexOf(valueCol) : 0;
            int li = labelCol != null ? v.Columns.IndexOf(labelCol) : -1;
            int mi = maxCol   != null ? v.Columns.IndexOf(maxCol)   : -1;

            var firstRow = v.Rows.Count > 0 ? v.Rows[0] : null;
            var value    = firstRow != null ? ToDouble(vi >= 0 && vi < firstRow.Count ? firstRow[vi] : null) ?? 0.0 : 0.0;
            var name     = firstRow != null && li >= 0 && li < firstRow.Count ? firstRow[li] ?? "" : (labelCol ?? "");

            double gaugeMin = 0, gaugeMax = 100;
            if (v.Options.TryGetValue("MIN", out var minStr) && double.TryParse(minStr, out var mn)) gaugeMin = mn;
            if (v.Options.TryGetValue("MAX", out var maxStr) && double.TryParse(maxStr, out var mx)) gaugeMax = mx;
            else if (firstRow != null && mi >= 0 && mi < firstRow.Count)
            {
                gaugeMax = ToDouble(firstRow[mi]) ?? 100.0;
            }

            return Serialize(new
            {
                title   = TitleOpt(v),
                tooltip = new { formatter = "{b}: {c}" },
                series  = new[]
                {
                    new { type = "gauge", min = gaugeMin, max = gaugeMax,
                          data = new[] { new { value, name } } }
                }
            });
        }

        // ── FUNNEL ───────────────────────────────────────────────────────────────
        // Roles: label (required), value (required)

        private string RenderFunnel(VisualManifest v)
        {
            var labelCol = FindRole(v, "label") ?? (v.Columns.Count > 0 ? v.Columns[0] : null);
            var valueCol = FindRole(v, "value") ?? (v.Columns.Count > 1 ? v.Columns[1] : null);

            int li = labelCol != null ? v.Columns.IndexOf(labelCol) : 0;
            int vi = valueCol != null ? v.Columns.IndexOf(valueCol) : 1;

            var data = v.Rows.Select(r => (object)new
            {
                name  = li >= 0 && li < r.Count ? r[li] ?? "" : "",
                value = ToDouble(vi >= 0 && vi < r.Count ? r[vi] : null) ?? 0.0
            }).ToList();

            return Serialize(new
            {
                title   = TitleOpt(v),
                tooltip = new { trigger = "item", formatter = "{a} <br/>{b}: {c}" },
                legend  = LegendOpt(v),
                series  = new[]
                {
                    new { type = "funnel", name = v.Name,
                          label = new { show = true, position = "inside" },
                          data }
                }
            });
        }

        // ── WATERFALL ────────────────────────────────────────────────────────────
        // Roles: x (categories), y (values — positive increases, negative decreases)
        // Rendered as stacked bar: transparent base bar + colored delta bar

        private string RenderWaterfall(VisualManifest v)
        {
            var xCol = FindRole(v, "x") ?? (v.Columns.Count > 0 ? v.Columns[0] : null);
            var yCol = FindRole(v, "y") ?? (v.Columns.Count > 1 ? v.Columns[1] : null);

            int xi = xCol != null ? v.Columns.IndexOf(xCol) : 0;
            int yi = yCol != null ? v.Columns.IndexOf(yCol) : 1;

            var categories = v.Rows.Select(r => xi >= 0 && xi < r.Count ? r[xi] ?? "" : "").ToList();
            var rawVals    = v.Rows.Select(r => ToDouble(yi >= 0 && yi < r.Count ? r[yi] : null) ?? 0.0).ToList();

            // Compute running total for transparent base bars
            var bases  = new List<double>();
            var deltas = new List<object>();
            double running = 0;
            foreach (var val in rawVals)
            {
                bases.Add(val >= 0 ? running : running + val);
                var color = val >= 0
                    ? (GetColor(v, "positive") ?? "#5cb85c")
                    : (GetColor(v, "negative") ?? "#d9534f");
                deltas.Add(new { value = Math.Abs(val), itemStyle = new { color } });
                running += val;
            }

            return Serialize(new
            {
                title   = TitleOpt(v),
                tooltip = new { trigger = "axis", axisPointer = new { type = "shadow" } },
                xAxis   = new { type = "category", data = categories },
                yAxis   = BuildAxisOpts(v, "y", "value"),
                series  = new object[]
                {
                    new { type = "bar", stack = "total",
                          itemStyle = new { color = "transparent" },
                          emphasis  = new { itemStyle = new { color = "transparent" } },
                          data = bases },
                    new { type = "bar", stack = "total", name = v.Name, data = deltas }
                }
            });
        }

        // ── Shared helpers ──────────────────────────────────────────────────────

        private (List<string> labels, List<object> series) ExtractCartesianSeries(VisualManifest v, string seriesType)
        {
            var xCol      = FindRole(v, "x")      ?? (v.Columns.Count > 0 ? v.Columns[0] : null);
            var yCol      = FindRole(v, "y")      ?? (v.Columns.Count > 1 ? v.Columns[1] : null);
            var seriesCol = FindRole(v, "series");

            int xi = xCol      != null ? v.Columns.IndexOf(xCol)      : 0;
            int yi = yCol      != null ? v.Columns.IndexOf(yCol)      : 1;
            int si = seriesCol != null ? v.Columns.IndexOf(seriesCol) : -1;

            if (si < 0)
            {
                var labels = v.Rows.Select(r => xi >= 0 && xi < r.Count ? r[xi] ?? "" : "").ToList();
                var vals   = v.Rows.Select(r =>
                {
                    var xLabel = xi >= 0 && xi < r.Count ? r[xi] ?? "" : "";
                    var num    = ToDouble(yi >= 0 && yi < r.Count ? r[yi] : null);
                    var color  = GetColor(v, xLabel);
                    return color != null
                        ? (object?)new { value = num, itemStyle = new { color } }
                        : (object?)num;
                }).ToList();
                return (labels, new List<object> { new { type = seriesType, name = yCol ?? v.Name, data = vals } });
            }
            else
            {
                var xLabels    = v.Rows.Select(r => xi >= 0 && xi < r.Count ? r[xi] ?? "" : "").Distinct().ToList();
                var seriesKeys = v.Rows.Select(r => si < r.Count ? r[si] ?? "" : "").Distinct().ToList();
                var xIndex     = xLabels.Select((l, i) => (l, i)).ToDictionary(t => t.l, t => t.i);

                var seriesList = new List<object>();
                foreach (var sk in seriesKeys)
                {
                    var vals = Enumerable.Repeat<object?>(null, xLabels.Count).ToList();
                    foreach (var row in v.Rows)
                    {
                        if ((si < row.Count ? row[si] ?? "" : "") != sk) continue;
                        var xl = xi < row.Count ? row[xi] ?? "" : "";
                        if (!xIndex.TryGetValue(xl, out var idx)) continue;
                        vals[idx] = ToDouble(yi < row.Count ? row[yi] : null);
                    }
                    seriesList.Add(new { type = seriesType, name = sk, data = vals });
                }
                return (xLabels, seriesList);
            }
        }

        private static object TitleOpt(VisualManifest v)
        {
            var text = v.Options.GetValueOrDefault("TITLE", v.Name);
            return new { text };
        }

        private static Dictionary<string, object> LegendOpt(VisualManifest v)
        {
            v.Options.TryGetValue("LEGEND_POSITION", out var pos);
            return (pos ?? "bottom").ToLowerInvariant() switch
            {
                "left"  => new Dictionary<string, object> { ["orient"] = "vertical",   ["left"]   = "left",   ["top"] = "middle" },
                "right" => new Dictionary<string, object> { ["orient"] = "vertical",   ["right"]  = "right",  ["top"] = "middle" },
                "top"   => new Dictionary<string, object> { ["orient"] = "horizontal", ["top"]    = "top"    },
                _       => new Dictionary<string, object> { ["orient"] = "horizontal", ["bottom"] = "bottom" }
            };
        }

        /// <summary>Builds an axis option dictionary; includes min/max/label only when explicitly set.</summary>
        private static Dictionary<string, object?> BuildAxisOpts(
            VisualManifest v, string axis, string type, object? data = null)
        {
            var opts = new Dictionary<string, object?> { ["type"] = type };
            if (data != null) opts["data"] = data;
            var axisUpper = axis.ToUpperInvariant();
            if (v.Options.TryGetValue($"AXIS:{axisUpper}:LABEL", out var label))
                opts["name"] = label;
            if (v.Options.TryGetValue($"AXIS:{axisUpper}:MIN", out var min))
                opts["min"] = ParseAxisBound(min);
            if (v.Options.TryGetValue($"AXIS:{axisUpper}:MAX", out var max))
                opts["max"] = ParseAxisBound(max);
            return opts;
        }

        private static bool IsOn(string? val) =>
            val?.ToUpperInvariant() is "ON" or "TRUE" or "1";

        private static List<object> ApplySeriesFlags(List<object> series, bool stacked, bool smooth)
        {
            return series.Select(s =>
            {
                var json = JsonSerializer.Serialize(s, _json);
                var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(json)!;
                if (stacked) dict["stack"] = "total";
                if (smooth)  dict["smooth"] = (object)true;
                return (object)dict;
            }).ToList();
        }

        private static object ParseAxisBound(string s) =>
            double.TryParse(s, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out var d)
                ? (object)d : s;

        private static string? GetColor(VisualManifest v, string key) =>
            v.Options.TryGetValue("COLOR:" + key.ToUpperInvariant(), out var c) ? c : null;

        private static string? FindRole(VisualManifest v, string role)
        {
            v.Options.TryGetValue("MAPPING:" + role.ToUpperInvariant(), out var col);
            return col;
        }

        private static double? ToDouble(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            return double.TryParse(s, System.Globalization.NumberStyles.Any,
                                   System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;
        }

        private static double Percentile(double[] sorted, double p)
        {
            if (sorted.Length == 0) return 0;
            double idx = (p / 100.0) * (sorted.Length - 1);
            int lo = (int)idx;
            int hi = Math.Min(lo + 1, sorted.Length - 1);
            return sorted[lo] + (idx - lo) * (sorted[hi] - sorted[lo]);
        }

        // ── Overlay rendering ───────────────────────────────────────────────────
        // GOAL / AVERAGE → markLine entries on the first data series.
        // MOVING_AVG / LINEAR / EXPONENTIAL / LOGARITHMIC / POWER / POLYNOMIAL
        //   → additional computed line series appended after data series.

        private void AppendOverlaySeries(VisualManifest v, List<object> series,
            List<string> xLabels, bool horizontal)
        {
            if (v.Overlays == null || v.Overlays.Count == 0) return;

            var yCol  = FindRole(v, "y") ?? (v.Columns.Count > 1 ? v.Columns[1] : null);
            int yi    = yCol != null ? v.Columns.IndexOf(yCol) : 1;
            var yVals = v.Rows.Select(r => ToDouble(yi >= 0 && yi < r.Count ? r[yi] : null) ?? 0.0).ToList();

            var markLines = new List<object>();
            var extraSeries = new List<object>();

            foreach (var ov in v.Overlays)
            {
                var ls    = EChartsLineStyle(ov.LineStyle);
                var color = ov.Color ?? "#888888";
                var label = ov.Label ?? ov.OverlayType;

                switch (ov.OverlayType)
                {
                    case "Goal":
                        var axis = horizontal ? "xAxis" : "yAxis";
                        markLines.Add(new Dictionary<string, object?>
                        {
                            [axis]  = ov.Parameter ?? 0,
                            ["name"] = label,
                            ["lineStyle"] = new { type = ls, color },
                            ["label"] = new { formatter = label, color }
                        });
                        break;

                    case "Average":
                        markLines.Add(new
                        {
                            type = "average", name = label,
                            lineStyle = new { type = ls, color },
                            label = new { formatter = label, color }
                        });
                        break;

                    case "MovingAvg":
                        int window = (int)(ov.Parameter ?? 3);
                        var maVals = ComputeMovingAverage(yVals, window);
                        extraSeries.Add(new
                        {
                            type = "line", name = label,
                            data = maVals.Select((d, i) => (object?)(d.HasValue ? d : null)).ToList(),
                            smooth = true, symbol = "none",
                            lineStyle = new { type = ls, color },
                            itemStyle = new { color },
                            tooltip = new { valueFormatter = (object?)null }
                        });
                        break;

                    case "Linear":
                        var linVals = ComputeLinearRegression(yVals);
                        extraSeries.Add(new
                        {
                            type = "line", name = label,
                            data = linVals.Select(d => (object?)d).ToList(),
                            symbol = "none",
                            lineStyle = new { type = ls, color },
                            itemStyle = new { color }
                        });
                        break;

                    case "Exponential":
                        var expVals = ComputeExponentialFit(yVals);
                        extraSeries.Add(new
                        {
                            type = "line", name = label,
                            data = expVals.Select(d => (object?)d).ToList(),
                            symbol = "none",
                            lineStyle = new { type = ls, color },
                            itemStyle = new { color }
                        });
                        break;

                    case "Logarithmic":
                        var logVals = ComputeLogarithmicFit(yVals);
                        extraSeries.Add(new
                        {
                            type = "line", name = label,
                            data = logVals.Select(d => (object?)d).ToList(),
                            symbol = "none",
                            lineStyle = new { type = ls, color },
                            itemStyle = new { color }
                        });
                        break;

                    case "Power":
                        var powVals = ComputePowerFit(yVals);
                        extraSeries.Add(new
                        {
                            type = "line", name = label,
                            data = powVals.Select(d => (object?)d).ToList(),
                            symbol = "none",
                            lineStyle = new { type = ls, color },
                            itemStyle = new { color }
                        });
                        break;

                    case "Polynomial":
                        int degree = (int)(ov.Parameter ?? 2);
                        var polyVals = ComputePolynomialFit(yVals, degree);
                        extraSeries.Add(new
                        {
                            type = "line", name = label,
                            data = polyVals.Select(d => (object?)d).ToList(),
                            smooth = true, symbol = "none",
                            lineStyle = new { type = ls, color },
                            itemStyle = new { color }
                        });
                        break;
                }
            }

            // Attach markLine entries to the first data series
            if (markLines.Count > 0 && series.Count > 0)
            {
                var first = series[0];
                // Merge markLine into existing anonymous object by rebuilding with markLine property
                var merged = MergeMarkLine(first, markLines);
                series[0] = merged;
            }

            series.AddRange(extraSeries);
        }

        private static object MergeMarkLine(object series, List<object> markLineData)
        {
            var json  = JsonSerializer.Serialize(series, _json);
            var dict  = JsonSerializer.Deserialize<Dictionary<string, object>>(json)!;
            dict["markLine"] = new { data = markLineData, silent = true };
            return dict;
        }

        private static string EChartsLineStyle(string style) => style switch
        {
            "solid"  => "solid",
            "dotted" => "dotted",
            _        => "dashed"
        };

        // ── Regression / smoothing math ─────────────────────────────────────────

        private static List<double?> ComputeMovingAverage(List<double> y, int window)
        {
            var result = new List<double?>(y.Count);
            for (int i = 0; i < y.Count; i++)
            {
                if (i < window - 1) { result.Add(null); continue; }
                result.Add(y.Skip(i - window + 1).Take(window).Average());
            }
            return result;
        }

        private static List<double> ComputeLinearRegression(List<double> y)
        {
            int n = y.Count;
            if (n < 2) return y.ToList();
            var x  = Enumerable.Range(0, n).Select(i => (double)i).ToList();
            double xMean = x.Average(), yMean = y.Average();
            double num = x.Zip(y, (xi, yi) => (xi - xMean) * (yi - yMean)).Sum();
            double den = x.Sum(xi => (xi - xMean) * (xi - xMean));
            double slope = den == 0 ? 0 : num / den;
            double intercept = yMean - slope * xMean;
            return x.Select(xi => slope * xi + intercept).ToList();
        }

        private static List<double> ComputeExponentialFit(List<double> y)
        {
            int n = y.Count;
            if (n < 2) return y.ToList();
            // ln(y) = a + bx  →  y = e^a * e^(bx)
            var x    = Enumerable.Range(0, n).Select(i => (double)i).ToList();
            var logY = y.Select(yi => yi > 0 ? Math.Log(yi) : 0.0).ToList();
            double xMean = x.Average(), lyMean = logY.Average();
            double num = x.Zip(logY, (xi, li) => (xi - xMean) * (li - lyMean)).Sum();
            double den = x.Sum(xi => (xi - xMean) * (xi - xMean));
            double b = den == 0 ? 0 : num / den;
            double a = lyMean - b * xMean;
            return x.Select(xi => Math.Exp(a + b * xi)).ToList();
        }

        private static List<double> ComputeLogarithmicFit(List<double> y)
        {
            int n = y.Count;
            if (n < 2) return y.ToList();
            // y = a + b*ln(x+1)
            var x    = Enumerable.Range(0, n).Select(i => (double)i).ToList();
            var logX = x.Select(xi => Math.Log(xi + 1)).ToList();
            double lxMean = logX.Average(), yMean = y.Average();
            double num = logX.Zip(y, (lxi, yi) => (lxi - lxMean) * (yi - yMean)).Sum();
            double den = logX.Sum(lxi => (lxi - lxMean) * (lxi - lxMean));
            double b = den == 0 ? 0 : num / den;
            double a = yMean - b * lxMean;
            return logX.Select(lxi => a + b * lxi).ToList();
        }

        private static List<double> ComputePowerFit(List<double> y)
        {
            int n = y.Count;
            if (n < 2) return y.ToList();
            // ln(y) = a + b*ln(x+1)
            var x    = Enumerable.Range(0, n).Select(i => (double)i).ToList();
            var logX = x.Select(xi => Math.Log(xi + 1)).ToList();
            var logY = y.Select(yi => yi > 0 ? Math.Log(yi) : 0.0).ToList();
            double lxMean = logX.Average(), lyMean = logY.Average();
            double num = logX.Zip(logY, (lxi, lyi) => (lxi - lxMean) * (lyi - lyMean)).Sum();
            double den = logX.Sum(lxi => (lxi - lxMean) * (lxi - lxMean));
            double b = den == 0 ? 0 : num / den;
            double a = lyMean - b * lxMean;
            return logX.Select(lxi => Math.Exp(a + b * lxi)).ToList();
        }

        private static List<double> ComputePolynomialFit(List<double> y, int degree)
        {
            int n = y.Count;
            degree = Math.Min(degree, n - 1);
            if (degree < 1) return ComputeLinearRegression(y);

            // Vandermonde matrix least-squares via normal equations (small datasets only)
            var x  = Enumerable.Range(0, n).Select(i => (double)i).ToArray();
            int d  = degree + 1;
            var A  = new double[d, d];
            var b2 = new double[d];

            for (int i = 0; i < d; i++)
            {
                for (int j = 0; j < d; j++)
                    A[i, j] = x.Sum(xi => Math.Pow(xi, i + j));
                b2[i] = x.Zip(y, (xi, yi) => yi * Math.Pow(xi, i)).Sum();
            }

            var coeffs = SolveLinearSystem(A, b2, d);
            return x.Select(xi => coeffs.Select((c, k) => c * Math.Pow(xi, k)).Sum()).ToList();
        }

        private static double[] SolveLinearSystem(double[,] A, double[] b, int n)
        {
            // Gaussian elimination with partial pivoting
            var M = (double[,])A.Clone();
            var r = (double[])b.Clone();
            for (int col = 0; col < n; col++)
            {
                int pivot = col;
                for (int row = col + 1; row < n; row++)
                    if (Math.Abs(M[row, col]) > Math.Abs(M[pivot, col])) pivot = row;
                for (int k = 0; k < n; k++) (M[col, k], M[pivot, k]) = (M[pivot, k], M[col, k]);
                (r[col], r[pivot]) = (r[pivot], r[col]);
                if (Math.Abs(M[col, col]) < 1e-12) continue;
                for (int row = col + 1; row < n; row++)
                {
                    double f = M[row, col] / M[col, col];
                    for (int k = col; k < n; k++) M[row, k] -= f * M[col, k];
                    r[row] -= f * r[col];
                }
            }
            var x = new double[n];
            for (int i = n - 1; i >= 0; i--)
            {
                x[i] = r[i];
                for (int j = i + 1; j < n; j++) x[i] -= M[i, j] * x[j];
                if (Math.Abs(M[i, i]) > 1e-12) x[i] /= M[i, i];
            }
            return x;
        }

        private static string Serialize(object obj) =>
            JsonSerializer.Serialize(obj, _json);
    }
}
