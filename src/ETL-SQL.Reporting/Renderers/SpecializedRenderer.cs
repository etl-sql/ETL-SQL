using System;
using System.Collections.Generic;
using System.Linq;

namespace ETL_SQL.Reporting.Renderers
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
            var styleUpper = style?.ToUpperInvariant();
            bool isProgress = styleUpper == "PROGRESS";
            bool isSemiCircle = styleUpper == "SEMI_CIRCLE";
            bool isRing = styleUpper == "RING";

            var series = new Dictionary<string, object>
            {
                ["type"] = "gauge",
                ["min"] = gaugeMin,
                ["max"] = gaugeMax,
                ["data"] = new[] { new { value, name } },
                ["detail"] = new { valueAnimation = true, formatter = "{value}" }
            };

            if (isProgress)
            {
                series["progress"] = new { show = true };
            }
            else if (isSemiCircle)
            {
                series["startAngle"] = 180;
                series["endAngle"] = 0;
                series["center"] = new[] { "50%", "75%" };
                series["radius"] = "100%";
                series["progress"] = new { show = true, width = 18 };
                series["axisLine"] = new { lineStyle = new { width = 18 } };
                series["pointer"] = new { show = true, length = "80%", width = 3 };
                series["axisTick"] = new { show = false };
                series["splitLine"] = new { show = false };
                series["axisLabel"] = new { show = false };
                series["detail"] = new { offsetCenter = new[] { "0", "-10%" }, valueAnimation = true, formatter = "{value}" };
            }
            else if (isRing)
            {
                series["startAngle"] = 90;
                series["endAngle"] = -270;
                series["pointer"] = new { show = false };
                series["progress"] = new { show = true, overlap = false, roundCap = true, clip = false };
                series["axisLine"] = new { lineStyle = new { width = 15 } };
                series["splitLine"] = new { show = false };
                series["axisTick"] = new { show = false };
                series["axisLabel"] = new { show = false };
                series["detail"] = new { show = true, formatter = "{value}%", offsetCenter = new[] { 0, 0 } };
            }

            return Serialize(new
            {
                title = TitleOpt(v),
                tooltip = new { formatter = "{b}: {c}" },
                series = new[] { series }
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

        // ── BUBBLE ────────────────────────────────────────────────────────────
        // Expected mappings: X, Y, SIZE (optional), LABEL (optional).
        // Without SIZE mapping, falls back to the third column; if absent, uniform size 20.

        public string RenderBubble(VisualManifest v)
        {
            var xCol    = FindRole(v, "x")     ?? (v.Columns.Count > 0 ? v.Columns[0] : null);
            var yCol    = FindRole(v, "y")     ?? (v.Columns.Count > 1 ? v.Columns[1] : null);
            var sizeCol = FindRole(v, "size")  ?? (v.Columns.Count > 2 ? v.Columns[2] : null);
            var lblCol  = FindRole(v, "label") ?? (v.Columns.Count > 3 ? v.Columns[3] : null);

            int xi = ColIdx(v, xCol);
            int yi = ColIdx(v, yCol);
            int si = ColIdx(v, sizeCol);
            int li = ColIdx(v, lblCol);

            double maxSize = si >= 0 ? v.Rows.Max(r => ToDouble(r.Count > si ? r[si] : null) ?? 0.0) : 1;
            if (maxSize == 0) maxSize = 1;

            // Encode [x, y, scaledSize, rawSize, name] per point.
            // ECharts scatter uses symbolSize as a function — the client reads index 2 for display size.
            var data = v.Rows.Select(r =>
            {
                double x       = ToDouble(xi >= 0 && xi < r.Count ? r[xi] : null) ?? 0.0;
                double y       = ToDouble(yi >= 0 && yi < r.Count ? r[yi] : null) ?? 0.0;
                double rawSize = si >= 0 && si < r.Count ? ToDouble(r[si]) ?? 0.0 : 0.0;
                double scaled  = si >= 0 ? rawSize / maxSize * 60 + 5 : 20.0;
                string name    = li >= 0 && li < r.Count ? r[li]?.ToString() ?? "" : "";
                return (object)new { value = new object[] { x, y, scaled, rawSize }, name };
            }).ToList();

            // symbolSize is a client-side function; embed a marker so report-runtime.js knows to apply it.
            return Serialize(new
            {
                title   = TitleOpt(v),
                tooltip = new { trigger = "item" },
                xAxis   = new { },
                yAxis   = new { },
                __bubbleSymbolSize = true,   // signal to client to wire symbolSize function
                series  = new[] { new { type = "scatter", name = v.Name, data } }
            });
        }

        // ── RADAR ─────────────────────────────────────────────────────────────
        // Expected data shape: first column = series name (one row per series),
        // remaining columns = metric values matching indicator labels.
        // OPTIONS: MIN (default 0), MAX (default auto).

        public string RenderRadar(VisualManifest v)
        {
            if (v.Columns.Count < 2)
                return Serialize(new { title = TitleOpt(v) });

            double radarMax = 100;
            if (v.Options.TryGetValue("MAX", out var maxStr) && double.TryParse(maxStr, out var mx)) radarMax = mx;
            else if (v.Rows.Count > 0)
            {
                double autoMax = v.Rows
                    .SelectMany(r => r.Skip(1))
                    .Select(val => ToDouble(val) ?? 0.0)
                    .DefaultIfEmpty(0)
                    .Max();
                radarMax = autoMax > 0 ? autoMax * 1.1 : 100;
            }

            double radarMin = 0;
            if (v.Options.TryGetValue("MIN", out var minStr) && double.TryParse(minStr, out var mn)) radarMin = mn;

            var indicators = v.Columns.Skip(1).Select(c => (object)new { name = c, max = radarMax, min = radarMin }).ToList();

            var seriesData = v.Rows.Select(r =>
            {
                var name   = r.Count > 0 ? r[0]?.ToString() ?? "" : "";
                var values = r.Skip(1).Select(val => ToDouble(val) ?? 0.0).ToList();
                return (object)new { name, value = values };
            }).ToList();

            return Serialize(new
            {
                title  = TitleOpt(v),
                legend = LegendOpt(v),
                radar  = new { indicator = indicators },
                series = new[] { new { type = "radar", name = v.Name, data = seriesData } }
            });
        }

        // ── CANDLESTICK ───────────────────────────────────────────────────────
        // Expected mappings: X (date/label), OPEN, HIGH, LOW, CLOSE.
        // Falls back to first five columns in that order if mappings are absent.

        public string RenderCandlestick(VisualManifest v)
        {
            var xCol     = FindRole(v, "x")     ?? (v.Columns.Count > 0 ? v.Columns[0] : null);
            var openCol  = FindRole(v, "open")  ?? (v.Columns.Count > 1 ? v.Columns[1] : null);
            var highCol  = FindRole(v, "high")  ?? (v.Columns.Count > 2 ? v.Columns[2] : null);
            var lowCol   = FindRole(v, "low")   ?? (v.Columns.Count > 3 ? v.Columns[3] : null);
            var closeCol = FindRole(v, "close") ?? (v.Columns.Count > 4 ? v.Columns[4] : null);

            int xi  = ColIdx(v, xCol);
            int oi  = ColIdx(v, openCol);
            int hi  = ColIdx(v, highCol);
            int li  = ColIdx(v, lowCol);
            int ci  = ColIdx(v, closeCol);

            var categories = v.Rows.Select(r => xi >= 0 && xi < r.Count ? r[xi]?.ToString() ?? "" : "").ToList();
            var ohlc = v.Rows.Select(r => new[]
            {
                ToDouble(oi >= 0 && oi < r.Count ? r[oi] : null) ?? 0.0,
                ToDouble(ci >= 0 && ci < r.Count ? r[ci] : null) ?? 0.0,
                ToDouble(li >= 0 && li < r.Count ? r[li] : null) ?? 0.0,
                ToDouble(hi >= 0 && hi < r.Count ? r[hi] : null) ?? 0.0
            }).ToList();

            v.Options.TryGetValue("COLOR_UP",   out var colorUp);
            v.Options.TryGetValue("COLOR_DOWN", out var colorDown);

            return Serialize(new
            {
                title   = TitleOpt(v),
                tooltip = new { trigger = "axis", axisPointer = new { type = "cross" } },
                xAxis   = new { type = "category", data = categories, boundaryGap = true, axisLine = new { onZero = false } },
                yAxis   = new { scale = true },
                series  = new[]
                {
                    new
                    {
                        type      = "candlestick",
                        name      = v.Name,
                        data      = ohlc,
                        itemStyle = new
                        {
                            color        = colorUp   ?? "#ec0000",
                            color0       = colorDown ?? "#00da3c",
                            borderColor  = colorUp   ?? "#8A0000",
                            borderColor0 = colorDown ?? "#008F28"
                        }
                    }
                }
            });
        }

        // ── GANTT ─────────────────────────────────────────────────────────────
        // Expected mappings: Y (Task/Category), START (Date/Time), END (Date/Time), COLOR (optional).
        // If mappings are absent, uses columns 0 (Y), 1 (Start), 2 (End).

        public string RenderGantt(VisualManifest v)
        {
            var yCol     = FindRole(v, "y")      ?? FindRole(v, "label") ?? (v.Columns.Count > 0 ? v.Columns[0] : null);
            var startCol = FindRole(v, "start")  ?? FindRole(v, "x")     ?? (v.Columns.Count > 1 ? v.Columns[1] : null);
            var endCol   = FindRole(v, "end")    ?? FindRole(v, "x2")    ?? (v.Columns.Count > 2 ? v.Columns[2] : null);
            var colorCol = FindRole(v, "color")  ?? (v.Columns.Count > 3 ? v.Columns[3] : null);

            int yi = ColIdx(v, yCol);
            int si = ColIdx(v, startCol);
            int ei = ColIdx(v, endCol);
            int ci = ColIdx(v, colorCol);

            // Get unique categories for the Y axis
            var categories = v.Rows
                .Select(r => yi >= 0 && yi < r.Count ? r[yi]?.ToString() ?? "" : "")
                .Distinct()
                .ToList();
            
            var catMap = categories.Select((c, i) => (c, i)).ToDictionary(t => t.c, t => t.i);

            // Encode [categoryIndex, start, end, label, color]
            var data = v.Rows.Select(r =>
            {
                string catName = yi >= 0 && yi < r.Count ? r[yi]?.ToString() ?? "" : "";
                int catIdx     = catMap.TryGetValue(catName, out var idx) ? idx : 0;
                var startVal   = si >= 0 && si < r.Count ? r[si] : null;
                var endVal     = ei >= 0 && ei < r.Count ? r[ei] : null;
                var color      = ci >= 0 && ci < r.Count ? r[ci]?.ToString() : null;

                return new object[] 
                { 
                    catIdx, 
                    startVal, 
                    endVal, 
                    catName, 
                    color ?? GetColor(v, "primary") ?? "#5470c6" 
                };
            }).ToList();

            return Serialize(new
            {
                title   = TitleOpt(v),
                tooltip = new { trigger = "item" },
                grid    = new { left = "10%", right = "5%", bottom = "10%", containLabel = true },
                xAxis   = new { type = "time" },
                yAxis   = new { type = "category", data = categories, inverse = true },
                __ganttRenderItem = true, // signal to client to wire custom renderItem
                series  = new[]
                {
                    new
                    {
                        type = "custom",
                        name = v.Name,
                        data = data,
                        encode = new { x = new[] { 1, 2 }, y = 0 }
                    }
                }
            });
        }

        private static int ColIdx(VisualManifest v, string? col) =>
            col != null ? v.Columns.FindIndex(c => string.Equals(c, col, StringComparison.OrdinalIgnoreCase)) : -1;
    }
}
