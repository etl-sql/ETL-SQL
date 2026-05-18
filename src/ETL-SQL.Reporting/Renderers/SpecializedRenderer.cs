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
            var option = new Dictionary<string, object>
            {
                ["title"]   = TitleOpt(v),
                ["tooltip"] = new { trigger = "item" },
                ["xAxis"]   = new { },
                ["yAxis"]   = new { },
                ["series"]  = ApplyCommonSeriesOptions(v, series, stacked: false, smooth: false)
            };

            // SCATTER BRUSH: inject markers consumed by the JS runtime's brushSelected handler
            if (IsOn(v.Options.GetValueOrDefault("BRUSH")))
            {
                v.Options.TryGetValue("BRUSH_PARAM", out var brushParam);
                v.Options.TryGetValue("BRUSH_TYPE",  out var brushType);
                if (!string.IsNullOrEmpty(brushParam))
                {
                    option["__brushParam"] = brushParam;
                    option["__brushType"]  = (string.IsNullOrEmpty(brushType) ? "rect" : brushType).ToLowerInvariant();
                }
            }

            return Serialize(option);
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

                return new object?[]
                {
                    catIdx,
                    FormatValue(startVal),
                    FormatValue(endVal),
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

        // ── SANKEY ────────────────────────────────────────────────────────────
        // Mappings: SOURCE (or FROM), TARGET (or TO), VALUE.

        public string RenderSankey(VisualManifest v)
        {
            var srcCol = FindRole(v, "source") ?? FindRole(v, "from") ?? (v.Columns.Count > 0 ? v.Columns[0] : null);
            var tgtCol = FindRole(v, "target") ?? FindRole(v, "to")   ?? (v.Columns.Count > 1 ? v.Columns[1] : null);
            var valCol = FindRole(v, "value")  ?? (v.Columns.Count > 2 ? v.Columns[2] : null);

            int si = ColIdx(v, srcCol), ti = ColIdx(v, tgtCol), vi = ColIdx(v, valCol);

            var nodes = new HashSet<string>(StringComparer.Ordinal);
            var links = new List<object>();

            foreach (var row in v.Rows)
            {
                var src = si >= 0 && si < row.Count ? row[si] ?? "" : "";
                var tgt = ti >= 0 && ti < row.Count ? row[ti] ?? "" : "";
                var val = vi >= 0 && vi < row.Count ? ToDouble(row[vi]) ?? 1.0 : 1.0;
                if (string.IsNullOrEmpty(src) || string.IsNullOrEmpty(tgt)) continue;
                nodes.Add(src);
                nodes.Add(tgt);
                links.Add(new { source = src, target = tgt, value = val });
            }

            return Serialize(new
            {
                title   = TitleOpt(v),
                tooltip = new { trigger = "item", triggerOn = "mousemove" },
                series  = new[]
                {
                    new
                    {
                        type      = "sankey",
                        data      = nodes.Select(n => new { name = n }).ToArray(),
                        links     = links,
                        emphasis  = new { focus = "adjacency" },
                        lineStyle = new { color = "gradient", curveness = 0.5 }
                    }
                }
            });
        }

        // ── SUNBURST ──────────────────────────────────────────────────────────
        // Mappings (two modes):
        //   Implicit hierarchy: LEVEL1, LEVEL2, [LEVEL3], VALUE
        //   Explicit parent-child: LABEL (or NAME), PARENT, VALUE

        public string RenderSunburst(VisualManifest v)
        {
            var level1Col = FindRole(v, "level1");
            var level2Col = FindRole(v, "level2");
            var level3Col = FindRole(v, "level3");
            var labelCol  = FindRole(v, "label") ?? FindRole(v, "name") ?? (v.Columns.Count > 0 ? v.Columns[0] : null);
            var parentCol = FindRole(v, "parent");
            var valueCol  = FindRole(v, "value") ?? (v.Columns.Count > 0 ? v.Columns[^1] : null);

            var data = level1Col != null
                ? BuildSunburstLevels(v, level1Col, level2Col, level3Col, valueCol)
                : BuildSunburstParentChild(v, labelCol, parentCol, valueCol);

            return Serialize(new
            {
                title   = TitleOpt(v),
                tooltip = new { trigger = "item" },
                series  = new[]
                {
                    new
                    {
                        type     = "sunburst",
                        data     = data,
                        radius   = new[] { "0%", "90%" },
                        label    = new { rotate = "radial" },
                        emphasis = new { focus = "ancestor" }
                    }
                }
            });
        }

        private sealed class SunNode
        {
            public string Name { get; }
            public double Value { get; set; }
            public Dictionary<string, SunNode> Children { get; } = new(StringComparer.Ordinal);
            public SunNode(string name) { Name = name; }

            public object ToObj() => Children.Count == 0
                ? (object)new { name = Name, value = Value }
                : new { name = Name, value = Children.Values.Sum(c => c.Value), children = Children.Values.Select(c => c.ToObj()).ToArray() };
        }

        private static List<object> BuildSunburstLevels(VisualManifest v, string l1Col, string? l2Col, string? l3Col, string? valCol)
        {
            int l1i = ColIdx(v, l1Col), l2i = ColIdx(v, l2Col), l3i = ColIdx(v, l3Col), vi = ColIdx(v, valCol);
            var roots = new Dictionary<string, SunNode>(StringComparer.Ordinal);

            foreach (var row in v.Rows)
            {
                var k1  = l1i >= 0 && l1i < row.Count ? row[l1i] ?? "(blank)" : "(blank)";
                var k2  = l2i >= 0 && l2i < row.Count ? row[l2i] ?? "(blank)" : null;
                var k3  = l3i >= 0 && l3i < row.Count ? row[l3i] ?? "(blank)" : null;
                var val = vi  >= 0 && vi  < row.Count ? ToDouble(row[vi]) ?? 0.0 : 1.0;

                if (!roots.TryGetValue(k1, out var n1)) roots[k1] = n1 = new SunNode(k1);
                n1.Value += val;

                if (k2 == null) continue;
                if (!n1.Children.TryGetValue(k2, out var n2)) n1.Children[k2] = n2 = new SunNode(k2);
                n2.Value += val;

                if (k3 == null) continue;
                if (!n2.Children.TryGetValue(k3, out var n3)) n2.Children[k3] = n3 = new SunNode(k3);
                n3.Value += val;
            }

            return roots.Values.Select(n => n.ToObj()).ToList();
        }

        private static List<object> BuildSunburstParentChild(VisualManifest v, string? labelCol, string? parentCol, string? valueCol)
        {
            int li = ColIdx(v, labelCol), pi = ColIdx(v, parentCol), vi = ColIdx(v, valueCol);
            var nodes = new Dictionary<string, SunNode>(StringComparer.Ordinal);

            foreach (var row in v.Rows)
            {
                var lbl = li >= 0 && li < row.Count ? row[li] ?? "" : "";
                var par = pi >= 0 && pi < row.Count ? row[pi] ?? "" : "";
                var val = vi >= 0 && vi < row.Count ? ToDouble(row[vi]) ?? 0.0 : 1.0;
                if (string.IsNullOrEmpty(lbl)) continue;
                if (!nodes.TryGetValue(lbl, out var node)) nodes[lbl] = node = new SunNode(lbl);
                node.Value += val;
                // Store parent reference for later tree wiring
                if (!string.IsNullOrEmpty(par) && !nodes.ContainsKey(par))
                    nodes[par] = new SunNode(par);
                if (!string.IsNullOrEmpty(par))
                {
                    var parentNode = nodes[par];
                    if (!parentNode.Children.ContainsKey(lbl))
                        parentNode.Children[lbl] = node;
                }
            }

            // Return root nodes (those not referenced as children)
            var childNames = new HashSet<string>(nodes.Values.SelectMany(n => n.Children.Keys), StringComparer.Ordinal);
            return nodes.Where(kv => !childNames.Contains(kv.Key)).Select(kv => kv.Value.ToObj()).ToList();
        }

        // ── NETWORK ───────────────────────────────────────────────────────────
        // Mappings: FROM, TO, VALUE (edge weight, optional), NODE_GROUP (optional).
        // Options: REPULSION (int, default 1000), LAYOUT (FORCE|CIRCULAR, default FORCE).

        public string RenderNetwork(VisualManifest v)
        {
            var fromCol  = FindRole(v, "from")       ?? (v.Columns.Count > 0 ? v.Columns[0] : null);
            var toCol    = FindRole(v, "to")         ?? (v.Columns.Count > 1 ? v.Columns[1] : null);
            var valCol   = FindRole(v, "value");
            var groupCol = FindRole(v, "node_group") ?? FindRole(v, "group");

            int fi = ColIdx(v, fromCol), ti = ColIdx(v, toCol), vi = ColIdx(v, valCol), gi = ColIdx(v, groupCol);

            v.Options.TryGetValue("REPULSION", out var repStr);
            double repulsion = double.TryParse(repStr, out var rep) ? rep : 1000.0;
            v.Options.TryGetValue("LAYOUT", out var layoutOpt);
            string layout = (layoutOpt ?? "FORCE").ToLowerInvariant() == "circular" ? "circular" : "force";
            bool roam = !v.Options.TryGetValue("ROAM", out var roamOpt) || IsOn(roamOpt);

            var nodes = new Dictionary<string, SunNode>(StringComparer.Ordinal);
            var groups = new Dictionary<string, int>(StringComparer.Ordinal);
            var links  = new List<object>();

            foreach (var row in v.Rows)
            {
                var from  = fi >= 0 && fi < row.Count ? row[fi] ?? "" : "";
                var to    = ti >= 0 && ti < row.Count ? row[ti] ?? "" : "";
                var val   = vi >= 0 && vi < row.Count ? ToDouble(row[vi]) ?? 1.0 : 1.0;
                var group = gi >= 0 && gi < row.Count ? row[gi] ?? "" : "";

                if (!string.IsNullOrEmpty(from) && !nodes.ContainsKey(from)) nodes[from] = new SunNode(from);
                if (!string.IsNullOrEmpty(to)   && !nodes.ContainsKey(to))   nodes[to]   = new SunNode(to);
                if (!string.IsNullOrEmpty(group) && !groups.ContainsKey(group)) groups[group] = groups.Count;
                if (!string.IsNullOrEmpty(from) && !string.IsNullOrEmpty(to))
                    links.Add(new { source = from, target = to, value = val });
            }

            var categories = groups.Select(g => new { name = g.Key }).ToArray();
            var nodeData = nodes.Keys.Select(name =>
            {
                // If no group column, all nodes go ungrouped; otherwise map via the last row seen
                int cat = -1;
                if (gi >= 0)
                {
                    var row = v.Rows.FirstOrDefault(r =>
                        gi < r.Count && !string.IsNullOrEmpty(r[gi]) &&
                        ((fi >= 0 && fi < r.Count && (r[fi] ?? "") == name) ||
                         (ti >= 0 && ti < r.Count && (r[ti] ?? "") == name)));
                    if (row != null && gi < row.Count)
                        groups.TryGetValue(row[gi] ?? "", out cat);
                }
                return cat >= 0
                    ? (object)new { name, category = cat, symbolSize = 30 }
                    : new { name, symbolSize = 30 };
            }).ToArray();

            var seriesBase = new Dictionary<string, object>
            {
                ["type"]       = "graph",
                ["layout"]     = layout,
                ["data"]       = nodeData,
                ["links"]      = links,
                ["roam"]       = roam,
                ["label"]      = new { show = true, position = "right" },
                ["edgeSymbol"] = new[] { "circle", "arrow" },
                ["lineStyle"]  = new { color = "source", curveness = 0.3 },
                ["force"]      = new { repulsion = repulsion, edgeLength = new[] { 80, 200 }, layoutAnimation = false }
            };
            if (categories.Length > 0)
            {
                seriesBase["categories"] = categories;
                seriesBase["legend"]     = new { show = true };
            }

            return Serialize(new
            {
                title   = TitleOpt(v),
                tooltip = new { trigger = "item" },
                series  = new[] { seriesBase }
            });
        }

        // ── TRELLIS ───────────────────────────────────────────────────────────
        // Mappings: X, Y, FACET, COLOR (optional).
        // Options: CHART_TYPE = BAR|LINE|SCATTER (default BAR), COLUMNS = int (default 3),
        //          SHARED_AXIS = ON|OFF (default ON).

        public string RenderTrellis(VisualManifest v)
        {
            var xCol    = FindRole(v, "x")     ?? (v.Columns.Count > 0 ? v.Columns[0] : null);
            var yCol    = FindRole(v, "y")     ?? (v.Columns.Count > 1 ? v.Columns[1] : null);
            var facetCol = FindRole(v, "facet") ?? (v.Columns.Count > 2 ? v.Columns[2] : null);

            int xi = ColIdx(v, xCol), yi = ColIdx(v, yCol), fi = ColIdx(v, facetCol);

            v.Options.TryGetValue("CHART_TYPE", out var chartTypeOpt);
            string chartType = (chartTypeOpt ?? "BAR").ToLowerInvariant() switch
            {
                "line"    => "line",
                "scatter" => "scatter",
                _         => "bar"
            };

            v.Options.TryGetValue("COLUMNS", out var colsOpt);
            int numCols = int.TryParse(colsOpt, out var nc) ? Math.Clamp(nc, 1, 6) : 3;
            bool sharedAxis = !IsOn(v.Options.GetValueOrDefault("SHARED_AXIS") ?? "ON") == false;

            // Group data by facet value
            var facets = v.Rows
                .GroupBy(r => fi >= 0 && fi < r.Count ? r[fi] ?? "(blank)" : "(blank)")
                .OrderBy(g => g.Key)
                .ToList();

            int numFacets = facets.Count;
            int numRows = (int)Math.Ceiling((double)numFacets / numCols);

            double cellW = 0.9 / numCols;
            double cellH = 0.8 / numRows;
            double padX  = 0.05 / numCols;
            double padY  = 0.05 / numRows;

            var grids   = new List<object>();
            var xAxes   = new List<object>();
            var yAxes   = new List<object>();
            var series  = new List<object>();

            // Compute global Y range for shared axis
            double globalMin = double.MaxValue, globalMax = double.MinValue;
            if (sharedAxis && chartType != "scatter")
            {
                foreach (var row in v.Rows)
                {
                    var val = ToDouble(yi >= 0 && yi < row.Count ? row[yi] : null);
                    if (val.HasValue) { globalMin = Math.Min(globalMin, val.Value); globalMax = Math.Max(globalMax, val.Value); }
                }
                if (globalMin > globalMax) { globalMin = 0; globalMax = 1; }
            }

            var palette = new[] { "#5470c6", "#91cc75", "#fac858", "#ee6666", "#73c0de", "#3ba272", "#fc8452", "#9a60b4" };

            for (int i = 0; i < numFacets; i++)
            {
                var facet = facets[i];
                int col = i % numCols, row = i / numCols;

                double left   = col * (cellW + padX) + padX;
                double top    = row * (cellH + padY) + 0.08 + padY;

                grids.Add(new { left = $"{left:P0}", top = $"{top:P0}", width = $"{cellW:P0}", height = $"{cellH:P0}", containLabel = true });

                var xLabels = facet.Select(r => xi >= 0 && xi < r.Count ? r[xi] ?? "" : "").Distinct().ToList();
                var yData   = facet.Select(r => ToDouble(yi >= 0 && yi < r.Count ? r[yi] : null) ?? 0.0).ToList();

                if (chartType == "scatter")
                {
                    xAxes.Add(new { gridIndex = i, type = "value",    name = facet.Key, nameLocation = "middle", nameGap = 20 });
                    yAxes.Add(new { gridIndex = i, type = "value" });
                    var pts = facet.Select(r => new[]
                    {
                        ToDouble(xi >= 0 && xi < r.Count ? r[xi] : null) ?? 0.0,
                        ToDouble(yi >= 0 && yi < r.Count ? r[yi] : null) ?? 0.0
                    }).ToArray();
                    series.Add(new { type = "scatter", xAxisIndex = i, yAxisIndex = i, data = pts, name = facet.Key, itemStyle = new { color = palette[i % palette.Length] } });
                }
                else
                {
                    xAxes.Add(new { gridIndex = i, type = "category", data = xLabels, name = facet.Key, nameLocation = "middle", nameGap = 20 });
                    var yAxisObj = new Dictionary<string, object> { ["gridIndex"] = i };
                    if (sharedAxis) { yAxisObj["min"] = globalMin; yAxisObj["max"] = globalMax; }
                    yAxes.Add(yAxisObj);
                    series.Add(new { type = chartType, xAxisIndex = i, yAxisIndex = i, data = yData, name = facet.Key, itemStyle = new { color = palette[i % palette.Length] } });
                }
            }

            return Serialize(new
            {
                title   = TitleOpt(v),
                tooltip = new { trigger = "axis" },
                grid    = grids,
                xAxis   = xAxes,
                yAxis   = yAxes,
                series  = series
            });
        }

        // ── MATRIX (Pivot / Cross-tab) ─────────────────────────────────────────
        // Mappings: ROW = row-dimension column, COL = column-pivot column, VALUE = measure.
        //   For multiple dimensions: ROW1/ROW2/ROW3 and COL1/COL2/COL3.
        // Options: AGGREGATE = SUM|AVG|COUNT|MIN|MAX (default SUM), GRAND_TOTAL = ON|OFF.
        // Returns JSON consumed by renderMatrix() in the browser, not an ECharts option.

        public string RenderMatrix(VisualManifest v)
        {
            const string Sep = "\u001F";

            static string? AggregateCell(List<double>? vals, string agg)
            {
                if (vals == null || vals.Count == 0) return null;
                return agg switch
                {
                    "COUNT" => vals.Count.ToString(),
                    "AVG"   => (vals.Sum() / vals.Count).ToString("G6"),
                    "MIN"   => vals.Min().ToString("G6"),
                    "MAX"   => vals.Max().ToString("G6"),
                    _       => vals.Sum().ToString("G6")
                };
            }

            // Collect row-dimension columns (ROW / ROW1 / ROW2 / ROW3)
            var rowCols = new List<string>();
            var r1 = FindRole(v, "row") ?? FindRole(v, "row1");
            var r2 = FindRole(v, "row2");
            var r3 = FindRole(v, "row3");
            if (r1 != null) rowCols.Add(r1);
            if (r2 != null) rowCols.Add(r2);
            if (r3 != null) rowCols.Add(r3);
            if (rowCols.Count == 0 && v.Columns.Count > 0) rowCols.Add(v.Columns[0]);

            // Collect column-dimension columns (COL / COL1 / COL2 / COL3).
            var colCols = new List<string>();
            var c1 = FindRole(v, "col") ?? FindRole(v, "col1") ?? FindRole(v, "columns");
            var c2 = FindRole(v, "col2");
            var c3 = FindRole(v, "col3");
            if (c1 != null) colCols.Add(c1);
            if (c2 != null) colCols.Add(c2);
            if (c3 != null) colCols.Add(c3);
            if (colCols.Count == 0 && v.Columns.Count > 1) colCols.Add(v.Columns[1]);

            var valCol = FindRole(v, "value") ?? (v.Columns.Count > 2 ? v.Columns[2] : null);

            v.Options.TryGetValue("AGGREGATE", out var aggOpt);
            string agg = (aggOpt ?? "SUM").ToUpperInvariant();
            bool grandTotal = IsOn(v.Options.GetValueOrDefault("GRAND_TOTAL"));

            var rowIndices = rowCols.Select(c => ColIdx(v, c)).ToList();
            var colIndices = colCols.Select(c => ColIdx(v, c)).ToList();
            int vi = ColIdx(v, valCol);

            // Collect unique column pivot paths (sorted)
            var colKeys = v.Rows
                .Select(r => string.Join(Sep, colIndices.Select(idx => idx >= 0 && idx < r.Count ? r[idx] ?? "" : "")))
                .Distinct()
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var colParts = colKeys
                .Select(key => key.Split(Sep).ToList())
                .ToList();
            var colValues = colParts
                .Select(parts => string.Join(" / ", parts.Where(p => !string.IsNullOrEmpty(p))))
                .ToList();

            // Group raw values by (row path, column path)
            var groups = new Dictionary<string, Dictionary<string, List<double>>>(StringComparer.Ordinal);
            var rowKeyOrder = new List<string>();

            foreach (var row in v.Rows)
            {
                var rowKey = string.Join(Sep, rowIndices.Select(idx => idx >= 0 && idx < row.Count ? row[idx] ?? "" : ""));
                var colKey = string.Join(Sep, colIndices.Select(idx => idx >= 0 && idx < row.Count ? row[idx] ?? "" : ""));
                var val    = vi >= 0 && vi < row.Count ? ToDouble(row[vi]) ?? 0.0 : 1.0;

                if (!groups.ContainsKey(rowKey)) { groups[rowKey] = new Dictionary<string, List<double>>(StringComparer.Ordinal); rowKeyOrder.Add(rowKey); }
                if (!groups[rowKey].ContainsKey(colKey)) groups[rowKey][colKey] = new List<double>();
                groups[rowKey][colKey].Add(val);
            }

            // Build pivot rows
            var pivotRows = rowKeyOrder.Distinct().Select(rowKey =>
            {
                var parts = rowKey.Split(Sep);
                var cells = colKeys.Select(ck =>
                {
                    groups[rowKey].TryGetValue(ck, out var vals);
                    return AggregateCell(vals, agg);
                }).ToList();
                return parts.Concat(cells).ToList();
            }).ToList();

            // Grand total row
            List<string?>? totals = null;
            if (grandTotal)
            {
                totals = Enumerable.Repeat<string?>(null, rowCols.Count)
                    .Concat(colKeys.Select(ck =>
                    {
                        var allVals = groups.Values
                            .SelectMany(g => g.TryGetValue(ck, out var l) ? l : Enumerable.Empty<double>())
                            .ToList();
                        return AggregateCell(allVals, agg);
                    })).ToList();
            }

            return Serialize(new
            {
                __matrix     = true,
                rowHeaders   = rowCols,
                colHeaders   = colCols,
                colKeys      = colKeys,
                colParts     = colParts,
                colValues    = colValues,
                aggregate    = agg,
                rows         = pivotRows,
                grandTotals  = totals
            });
        }
    }
}
