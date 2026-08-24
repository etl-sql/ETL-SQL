using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Semantics.Runtime;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace ETL_SQL.Reporting.Renderers
{
    /// <summary>
    /// Converts a <see cref="ReportManifest"/> into Spectre.Console renderable objects.
    /// Used for terminal-based report previews in TUI and CLI.
    /// </summary>
    public static class TerminalRenderer
    {
        private static readonly Color[] ChartColors = new[]
        {
            Color.Blue, Color.Green, Color.Yellow, Color.Red,
            Color.Purple, Color.Cyan, Color.Orange1, Color.Teal
        };
        /// <summary>
        /// Renders a full report page into a collection of Spectre.Console renderables.
        /// </summary>
        public static IRenderable RenderPage(PageManifest page, ReportManifest manifest)
        {
            var content = new List<IRenderable>();

            // Page Header
            if (!string.IsNullOrEmpty(page.Title))
            {
                var rule = new Rule($"[bold blue]{page.Title}[/]");
                rule.Justification = Justify.Left;
                content.Add(rule);
            }
            if (!string.IsNullOrEmpty(page.Subtitle))
            {
                content.Add(new Markup($"[italic grey]{page.Subtitle}[/]\n"));
            }

            var visualNames = page.SlotMap.Values.Distinct().ToList();
            if (visualNames.Count == 0)
            {
                visualNames = manifest.Visuals.Select(v => v.Name).ToList();
            }

            foreach (var vName in visualNames)
            {
                var item = RenderSlotItem(vName, manifest);
                if (item != null)
                {
                    content.Add(item);
                    content.Add(new Text("\n")); // Spacer
                }
            }

            return new Rows(content);
        }

        private static IRenderable? RenderSlotItem(string name, ReportManifest manifest)
        {
            // 1. Check if it's a visual
            var visual = manifest.Visuals?.FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));
            if (visual != null)
            {
                return RenderVisual(visual, manifest);
            }

            // 2. Check if it's a container
            var container = manifest.Containers?.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
            if (container != null)
            {
                return RenderContainer(container, manifest);
            }

            // 3. Check if it's a button
            var button = manifest.Buttons?.FirstOrDefault(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));
            if (button != null)
            {
                return RenderButton(button);
            }

            return null;
        }

        private static IRenderable RenderContainer(ContainerManifest container, ReportManifest manifest)
        {
            var content = new List<IRenderable>();

            // Container Title
            var title = string.IsNullOrEmpty(container.Title) ? container.Name : container.Title;
            if (!string.IsNullOrEmpty(title))
            {
                content.Add(new Rule($"[bold cyan]▪ {Markup.Escape(title)} ▪[/]") { Justification = Justify.Left });
                content.Add(new Text("\n"));
            }

            if (container.SlotMap != null)
            {
                var sortedSlots = container.SlotMap.OrderBy(kv => kv.Key).Select(kv => kv.Value).Distinct();
                foreach (var childName in sortedSlots)
                {
                    var childRenderable = RenderSlotItem(childName, manifest);
                    if (childRenderable != null)
                    {
                        content.Add(childRenderable);
                        content.Add(new Text("\n"));
                    }
                }
            }

            var borderColor = Color.Cyan;
            return new Panel(new Rows(content))
            {
                Header = new PanelHeader($"[cyan]{Markup.Escape(container.ContainerType)}: {Markup.Escape(title)}[/]"),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(borderColor)
            };
        }

        private static IRenderable RenderButton(ButtonManifest button)
        {
            var title = string.IsNullOrEmpty(button.Title) ? button.Name : button.Title;
            return new Panel(Align.Center(new Markup($"[bold white]{Markup.Escape(title)}[/]")))
            {
                Width = Math.Min(35, title.Length + 8),
                Height = 3,
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Blue)
            };
        }

        /// <summary>
        /// Renders a single visual based on its type.
        /// </summary>
        public static IRenderable RenderVisual(VisualManifest visual, ReportManifest? manifest = null)
        {
            try
            {
                return WithDetailNotice(visual, RenderVisualBody(visual, manifest));
            }
            catch (Exception ex)
            {
                return new Panel(new Text($"Error rendering {Markup.Escape(visual.Name)}: {Markup.Escape(ex.Message)}", new Style(Color.Red)))
                    .Header(Markup.Escape(visual.Name))
                    .Border(BoxBorder.Rounded);
            }
        }

        /// <summary>
        /// Appends the detail-surface summary beneath a visual. A terminal cannot hover, so
        /// the notice describes the detail rather than implying the interaction exists.
        /// </summary>
        private static IRenderable WithDetailNotice(VisualManifest visual, IRenderable body)
        {
            var detail = DetailSurfaceProjection.Describe(visual.Tooltip);
            return detail == null
                ? body
                : new Rows(body, new Markup($"[italic grey]{Markup.Escape(detail)}[/]"));
        }

        private static IRenderable RenderVisualBody(VisualManifest visual, ReportManifest? manifest)
        {
            if (visual.PlotPlan is not null)
                return PlotPlanTerminalRenderer.Render(visual.PlotPlan);

            return visual.VisualType.ToUpperInvariant() switch
            {
                "HBAR" or "HORIZONTALBAR" => RenderBarChart(visual),
                "BAR" => RenderVerticalBarChart(visual),
                "PIE" or "DONUT" => RenderBreakdownChart(visual),
                "CARD" => RenderCard(visual),
                "TABLE" => RenderTable(visual),
                "TEXT" => RenderText(visual),
                "GAUGE" => RenderGauge(visual),
                "BOXPLOT" => RenderBoxPlot(visual),
                "WATERFALL" => RenderWaterfall(visual),
                "LINE" => RenderLineChart(visual),
                "SCATTER" => RenderScatterPlot(visual),
                "HEATMAP" => RenderHeatMap(visual),
                "SLICER" or "DATEPICKER" or "RELDATEPICKER" or "REDATEPICKER" or "SLIDER" or "MULTISELECT" or "SEARCH" => RenderSlicer(visual, manifest),
                "BUBBLE" => RenderBubbleChart(visual),
                "FUNNEL" => RenderFunnelChart(visual),
                "GANTT" => RenderGanttChart(visual),
                "CANDLESTICK" => RenderCandlestickChart(visual),
                "TRELLIS" => RenderTrellisChart(visual, manifest),
                "MATRIX" => RenderMatrixChart(visual),
                "CHECKBOX" => RenderCheckbox(visual, manifest),
                "TEXTBOX" => RenderTextbox(visual, manifest),
                "NUMBERBOX" => RenderNumberbox(visual, manifest),
                "MAP" => RenderSemanticFallback(visual),
                "IMAGE" => RenderImagePlaceholder(visual),
                "COMBO" => RenderComboPlaceholder(visual),
                "TREEMAP" => RenderSemanticFallback(visual),
                "RADAR" => RenderRadarPlaceholder(visual),
                "SANKEY" => RenderSemanticFallback(visual),
                "SUNBURST" => RenderSemanticFallback(visual),
                "NETWORK" => RenderSemanticFallback(visual),
                _ => RenderPlaceholder(visual)
            };
        }

        private static IRenderable RenderSemanticFallback(VisualManifest visual)
        {
            var fallback = visual.SemanticFallback ?? VisualSemanticFallbackBuilder.Build(visual);
            return new Panel(PlotPlanTerminalRenderer.RenderFallback(fallback))
            {
                Header = new PanelHeader(Markup.Escape(fallback.Heading)),
                Border = BoxBorder.Rounded,
                Expand = false
            };
        }

        private static IRenderable RenderLineChart(VisualManifest visual)
        {
            var title = GetVisualTitle(visual);
            var rows = visual.Rows;
            if (rows.Count < 2) return RenderPlaceholder(visual);

            // Column 0 is X (numeric or index); columns 1..n are numeric Y series.
            int seriesCount = Math.Max(0, rows.Max(r => r.Count) - 1);
            if (seriesCount < 1) return RenderPlaceholder(visual);

            var series = new List<List<(double x, double y)>>();
            for (int s = 0; s < seriesCount; s++) series.Add(new List<(double, double)>());
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                double x = i;
                if (row.Count > 0 && double.TryParse(row[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var xv)) x = xv;
                for (int s = 0; s < seriesCount; s++)
                {
                    int col = s + 1;
                    if (col < row.Count && double.TryParse(row[col], NumberStyles.Any, CultureInfo.InvariantCulture, out var yv))
                        series[s].Add((x, yv));
                }
            }

            var all = series.SelectMany(p => p).ToList();
            if (all.Count < 2) return RenderPlaceholder(visual);

            double minX = all.Min(p => p.x), maxX = all.Max(p => p.x);
            double minY = all.Min(p => p.y), maxY = all.Max(p => p.y);
            if (maxX == minX) maxX += 1;
            if (maxY == minY) maxY += 1;

            var canvas = new BrailleCanvas(50, 12);
            int dw = canvas.DotWidth, dh = canvas.DotHeight;
            for (int s = 0; s < series.Count; s++)
            {
                var pts = series[s];
                string color = ColorNameForIndex(s);
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    int x0 = (int)((pts[i].x - minX) / (maxX - minX) * (dw - 1));
                    int y0 = (int)((pts[i].y - minY) / (maxY - minY) * (dh - 1));
                    int x1 = (int)((pts[i + 1].x - minX) / (maxX - minX) * (dw - 1));
                    int y1 = (int)((pts[i + 1].y - minY) / (maxY - minY) * (dh - 1));
                    canvas.Line(x0, dh - 1 - y0, x1, dh - 1 - y1, color); // invert Y (0 = top)
                }
            }

            var body = new Rows(
                new Markup($"[grey]{Markup.Escape(FormatNum(maxY))}[/]"),
                canvas.ToRenderable(),
                new Markup($"[grey]{Markup.Escape(FormatNum(minY))}   x: {Markup.Escape(FormatNum(minX))}…{Markup.Escape(FormatNum(maxX))}[/]"));

            return new Panel(body)
            {
                Header = new PanelHeader(Markup.Escape(title)),
                Border = BoxBorder.Rounded,
                Expand = false
            };
        }

        private static IRenderable RenderScatterPlot(VisualManifest visual)
        {
            var title = GetVisualTitle(visual);
            var rows = visual.Rows;
            if (rows.Count == 0) return RenderPlaceholder(visual);

            var points = new List<(double x, double y)>();
            foreach (var row in rows)
            {
                if (row.Count > 1 &&
                    double.TryParse(row[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var xVal) &&
                    double.TryParse(row[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var yVal))
                {
                    points.Add((xVal, yVal));
                }
            }

            if (points.Count == 0) return RenderPlaceholder(visual);

            double minX = points.Min(p => p.x);
            double maxX = points.Max(p => p.x);
            double minY = points.Min(p => p.y);
            double maxY = points.Max(p => p.y);
            if (maxX == minX) maxX += 1;
            if (maxY == minY) maxY += 1;

            int width = 50;
            int height = 12;
            var canvas = new Canvas(width, height);

            foreach (var p in points)
            {
                int x = (int)((p.x - minX) / (maxX - minX) * (width - 1));
                int y = (int)((p.y - minY) / (maxY - minY) * (height - 1));
                canvas.SetPixel(x, height - 1 - y, Color.Green);
            }
            return new Panel(canvas)
            {
                Header = new PanelHeader(Markup.Escape(title)),
                Border = BoxBorder.Rounded,
                Expand = false
            };
        }

        private static IRenderable RenderHeatMap(VisualManifest visual)
        {
            var title = GetVisualTitle(visual);
            var rows = visual.Rows;
            if (rows.Count == 0) return RenderPlaceholder(visual);

            // HeatMap is usually a grid: RowLabel, Col1, Col2, ...
            var table = new Table().Border(TableBorder.None);
            table.AddColumn(""); // Labels
            for (int i = 1; i < visual.Columns.Count; i++)
            {
                table.AddColumn(new TableColumn($"[grey]{Markup.Escape(visual.Columns[i])}[/]").Centered());
            }

            double maxVal = 0;
            foreach (var row in rows)
            {
                for (int i = 1; i < row.Count; i++)
                {
                    if (double.TryParse(row[i], NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) maxVal = Math.Max(maxVal, v);
                }
            }
            if (maxVal == 0) maxVal = 1;

            foreach (var row in rows)
            {
                var displayRow = new List<IRenderable>();
                displayRow.Add(new Text(row.Count > 0 ? row[0] ?? "" : ""));

                for (int i = 1; i < row.Count; i++)
                {
                    if (double.TryParse(row[i], NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
                    {
                        double pct = v / maxVal;
                        // Blue-to-Red palette
                        int r = (int)(pct * 255);
                        int b = (int)((1 - pct) * 255);
                        var color = new Color((byte)r, 0, (byte)b);

                        displayRow.Add(new Text("██", new Style(color)));
                    }
                    else displayRow.Add(new Text("  "));
                }
                table.AddRow(displayRow.ToArray());
            }

            return new Panel(table)
            {
                Header = new PanelHeader(Markup.Escape(title)),
                Border = BoxBorder.Rounded,
                Expand = false
            };
        }

        private static void DrawLine(Canvas canvas, int x0, int y0, int x1, int y1, Color color)
        {
            int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy, e2;

            while (true)
            {
                if (x0 >= 0 && x0 < canvas.Width && y0 >= 0 && y0 < canvas.Height)
                    canvas.SetPixel(x0, y0, color);

                if (x0 == x1 && y0 == y1) break;
                e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        private static IRenderable RenderGauge(VisualManifest visual)
        {
            var title = GetVisualTitle(visual);
            var firstRow = visual.Rows.Count > 0 ? visual.Rows[0] : null;
            var valueStr = firstRow != null && firstRow.Count > 0 ? firstRow[0] ?? "0" : "0";
            if (!double.TryParse(valueStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double value)) value = 0;

            double min = double.TryParse(visual.Options.GetValueOrDefault("MIN"), NumberStyles.Any, CultureInfo.InvariantCulture, out var minVal) ? minVal : 0;
            double max = double.TryParse(visual.Options.GetValueOrDefault("MAX"), NumberStyles.Any, CultureInfo.InvariantCulture, out var maxVal) ? maxVal : 100;

            double pct = (value - min) / (max - min);
            pct = Math.Clamp(pct, 0, 1);

            int width = 30;
            int filled = (int)(width * pct);

            // Zero-allocation progress bar creation
            char[] chars = new char[width];
            Array.Fill(chars, '█', 0, filled);
            Array.Fill(chars, '░', filled, width - filled);
            string bar = new string(chars);

            Color color = Color.Green;
            if (pct > 0.7) color = Color.Yellow;
            if (pct > 0.9) color = Color.Red;

            var content = new Rows(
                new Text($"{value} / {max}", new Style(foreground: color, decoration: Decoration.Bold)).Centered(),
                new Text(bar, new Style(color))
            );

            return new Panel(content)
            {
                Header = new PanelHeader(Markup.Escape(title)),
                Border = BoxBorder.Rounded,
                Expand = false,
                Padding = new Padding(1, 0, 1, 0)
            };
        }

        private static IRenderable RenderBoxPlot(VisualManifest visual)
        {
            var title = GetVisualTitle(visual);
            var rows = visual.Rows;
            if (rows.Count == 0) return RenderPlaceholder(visual);

            var content = new List<IRenderable>();
            foreach (var row in rows)
            {
                if (row.Count < 5) continue;
                string label = (row.Count > 5 ? row[0] : "Data") ?? "Data";
                int offset = row.Count > 5 ? 1 : 0;

                if (double.TryParse(row[offset], NumberStyles.Any, CultureInfo.InvariantCulture, out double min) &&
                    double.TryParse(row[offset + 1], NumberStyles.Any, CultureInfo.InvariantCulture, out double q1) &&
                    double.TryParse(row[offset + 2], NumberStyles.Any, CultureInfo.InvariantCulture, out double med) &&
                    double.TryParse(row[offset + 3], NumberStyles.Any, CultureInfo.InvariantCulture, out double q3) &&
                    double.TryParse(row[offset + 4], NumberStyles.Any, CultureInfo.InvariantCulture, out double max))
                {
                    // Simple ASCII BoxPlot:  |---[  |  ]---|
                    // We scale this to ~40 chars
                    double range = max - min;
                    if (range == 0) range = 1;
                    int width = 40;

                    int pMin = 0;
                    int pQ1 = (int)((q1 - min) / range * width);
                    int pMed = (int)((med - min) / range * width);
                    int pQ3 = (int)((q3 - min) / range * width);
                    int pMax = width;

                    char[] line = new string(' ', width + 1).ToCharArray();
                    for (int i = 0; i <= width; i++)
                    {
                        if (i == pMin || i == pMax) line[i] = '|';
                        else if (i > pMin && i < pQ1) line[i] = '-';
                        else if (i > pQ3 && i < pMax) line[i] = '-';
                        else if (i == pQ1 || i == pQ3) line[i] = '['; // Simplified
                        else if (i == pMed) line[i] = '┃';
                        else if (i > pQ1 && i < pQ3) line[i] = '█';
                    }
                    // Adjust brackets
                    line[pQ3] = ']';

                    content.Add(new Text($"{label.PadRight(15)} {new string(line)} ({min:N0} - {max:N0})"));
                }
            }

            return new Panel(new Rows(content))
                .Header(Markup.Escape(title))
                .Border(BoxBorder.Rounded);
        }

        private static IRenderable RenderWaterfall(VisualManifest visual)
        {
            var title = GetVisualTitle(visual);
            var rows = visual.Rows;
            if (rows.Count == 0) return RenderPlaceholder(visual);

            var content = new List<IRenderable>();

            // Recalculate max properly (removed dead maxVal calculation and LINQ)
            double rolling = 0;
            double absoluteMax = 0;
            foreach (var r in rows)
            {
                double v = 0;
                if (r.Count > 1) double.TryParse(r[1], NumberStyles.Any, CultureInfo.InvariantCulture, out v);
                rolling += v;
                absoluteMax = Math.Max(absoluteMax, Math.Abs(rolling));
                absoluteMax = Math.Max(absoluteMax, Math.Abs(rolling - v));
            }
            if (absoluteMax == 0) absoluteMax = 1;

            int fullWidth = 50;
            double currentSum = 0;

            foreach (var row in rows)
            {
                string label = row.Count > 0 ? row[0] ?? "Item" : "Item";
                double val = 0;
                if (row.Count > 1) double.TryParse(row[1], NumberStyles.Any, CultureInfo.InvariantCulture, out val);

                double start = currentSum;
                double end = currentSum + val;
                currentSum = end;

                int pStart = (int)(Math.Min(start, end) / absoluteMax * fullWidth);
                int pEnd = (int)(Math.Max(start, end) / absoluteMax * fullWidth);
                int pLen = Math.Max(1, pEnd - pStart);

                string indent = new string(' ', pStart);
                string bar = new string('█', pLen);
                Color color = val >= 0 ? Color.Green : Color.Red;
                if (label.ToUpperInvariant().Contains("TOTAL")) color = Color.Blue;

                content.Add(new Text($"{label.PadRight(15)} {indent}", Style.Plain));
                content.Add(new Text(bar, new Style(color)));
                content.Add(new Text($" ({val:+N0;-N0;0})", new Style(Color.Grey)));
                content.Add(new Text("\n"));
            }

            return new Panel(new Rows(content))
                .Header(Markup.Escape(title))
                .Border(BoxBorder.Rounded);
        }

        private static IRenderable RenderVerticalBarChart(VisualManifest visual)
        {
            var title = GetVisualTitle(visual);
            var rows = visual.Rows;
            if (rows.Count == 0) return RenderPlaceholder(visual);

            var data = new List<(string label, double value)>();
            foreach (var row in rows)
            {
                var label = row.Count > 0 ? row[0] ?? "" : "";
                if (row.Count > 1 && double.TryParse(row[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
                {
                    data.Add((label, val));
                }
            }

            if (data.Count == 0) return RenderPlaceholder(visual);

            double maxVal = data.Max(d => d.value);
            if (maxVal == 0) maxVal = 1;

            int height = 10;
            var table = new Table().Border(TableBorder.None).HideHeaders();
            for (int i = 0; i < data.Count; i++) table.AddColumn(new TableColumn("").Padding(0, 0, 1, 0));

            // One-eighth block elements give the bar tops fractional resolution.
            string[] eighthBlocks = { "▁", "▂", "▃", "▄", "▅", "▆", "▇", "█" };
            for (int h = height; h >= 1; h--)
            {
                var displayRow = new List<IRenderable>();
                for (int i = 0; i < data.Count; i++)
                {
                    double eighths = data[i].value / maxVal * height * 8.0; // total filled eighths
                    double cellBottom = (h - 1) * 8.0;
                    var style = new Style(GetColorForIndex(i));
                    if (eighths >= h * 8.0)
                        displayRow.Add(new Text("██", style));
                    else if (eighths > cellBottom)
                    {
                        int e = Math.Clamp((int)Math.Ceiling(eighths - cellBottom), 1, 8);
                        displayRow.Add(new Text(eighthBlocks[e - 1] + eighthBlocks[e - 1], style));
                    }
                    else
                        displayRow.Add(new Text("  "));
                }
                table.AddRow(displayRow.ToArray());
            }

            // Labels (truncated to 2 chars for fitting)
            var labelRow = data.Select(d => new Text(d.label.Length > 2 ? d.label[..2] : d.label.PadRight(2))).Cast<IRenderable>().ToArray();
            table.AddRow(labelRow);

            return new Panel(table)
            {
                Header = new PanelHeader(Markup.Escape(title)),
                Border = BoxBorder.Rounded,
                Expand = false
            };
        }

        private static IRenderable RenderBreakdownChart(VisualManifest visual)
        {
            var title = GetVisualTitle(visual);
            var rows = visual.Rows;
            if (rows.Count == 0) return RenderPlaceholder(visual);

            var chart = new BreakdownChart() { Width = 60 };
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var label = row.Count > 0 ? row[0] ?? "Unknown" : "Unknown";
                if (row.Count > 1 && double.TryParse(row[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
                {
                    chart.AddItem(label, val, GetColorForIndex(i));
                }
            }

            return new Panel(chart)
            {
                Header = new PanelHeader(Markup.Escape(title)),
                Border = BoxBorder.Rounded,
                Expand = false
            };
        }

        private static IRenderable RenderBarChart(VisualManifest visual)
        {
            var title = GetVisualTitle(visual);
            var chart = new BarChart()
                .Label($"[bold]{Markup.Escape(title)}[/]")
                .Width(80);

            // Resolve columns via mappings or defaults
            var labelCol = visual.Options.GetValueOrDefault("mapping:label") ?? visual.Options.GetValueOrDefault("mapping:x");
            var valueCol = visual.Options.GetValueOrDefault("mapping:value") ?? visual.Options.GetValueOrDefault("mapping:y");

            int labelIdx = 0;
            int valueIdx = 1;

            if (labelCol != null) labelIdx = visual.Columns.FindIndex(c => c.Equals(labelCol, StringComparison.OrdinalIgnoreCase));
            if (valueCol != null) valueIdx = visual.Columns.FindIndex(c => c.Equals(valueCol, StringComparison.OrdinalIgnoreCase));

            if (labelIdx < 0) labelIdx = 0;
            if (valueIdx < 0) valueIdx = 1;

            bool hasItems = false;
            int rowIndex = 0;
            foreach (var row in visual.Rows)
            {
                if (row.Count > Math.Max(labelIdx, valueIdx))
                {
                    var label = row[labelIdx] ?? "Unknown";
                    if (double.TryParse(row[valueIdx], NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
                    {
                        chart.AddItem(label, val, GetColorForIndex(rowIndex));
                        hasItems = true;
                    }
                }
                rowIndex++;
            }

            if (!hasItems)
            {
                return new Panel(new Text("No data available for this chart.", new Style(Color.Grey)))
                    .Header(Markup.Escape(title))
                    .Border(BoxBorder.Rounded)
                    .Padding(1, 1, 1, 1);
            }

            return new Panel(chart).Border(BoxBorder.Rounded).Padding(1, 1, 1, 1);
        }

        private static IRenderable RenderCard(VisualManifest visual)
        {
            var raw = visual.Rows.FirstOrDefault()?.FirstOrDefault();
            var value = string.IsNullOrEmpty(raw) ? "N/A" : FormatNumericCell(raw);
            var label = GetVisualTitle(visual);

            var micro = visual.MicroCharts?.FirstOrDefault(item => item.Role == "card.sparkline");
            var content = micro is null
                ? $"[bold yellow]{Markup.Escape(value)}[/]"
                : $"[bold yellow]{Markup.Escape(value)}[/]\n[grey]{Markup.Escape(micro.PlainText)}[/]";
            var panel = new Panel(Align.Center(new Markup(content), VerticalAlignment.Middle))
            {
                Header = new PanelHeader(Markup.Escape(label)),
                Border = BoxBorder.Double,
                Padding = new Padding(2, 1, 2, 1),
                Width = 40,
                Expand = false
            };

            return panel;
        }

        private static IRenderable RenderTable(VisualManifest visual)
        {
            var table = new Table()
                .Title($"[bold]{Markup.Escape(visual.Name)}[/]")
                .Border(TableBorder.Rounded);

            foreach (var col in visual.Columns)
            {
                table.AddColumn(new TableColumn($"[blue]{Markup.Escape(col)}[/]").Centered());
            }

            for (var rowIndex = 0; rowIndex < visual.Rows.Count; rowIndex++)
            {
                var row = visual.Rows[rowIndex];
                table.AddRow(Enumerable.Range(0, visual.Columns.Count).Select(columnIndex =>
                {
                    var micro = visual.MicroCharts?.FirstOrDefault(item => item.Role == "table.cell" &&
                        item.RowIndex == rowIndex && item.ColumnIndex == columnIndex);
                    return Markup.Escape(micro?.PlainText ?? FormatNumericCell(columnIndex < row.Count ? row[columnIndex] : null));
                }).ToArray());
            }

            return table;
        }

        private static IRenderable RenderText(VisualManifest visual)
        {
            string markdown = visual.DefaultValue ?? "";
            return new Panel(RenderMarkdownText(markdown))
            {
                Header = new PanelHeader(Markup.Escape(GetVisualTitle(visual))),
                Border = BoxBorder.None,
                Padding = new Padding(1, 0, 1, 0)
            };
        }

        private static IRenderable RenderPlaceholder(VisualManifest visual)
        {
            return new Panel(new Text($"Visual Type '{Markup.Escape(visual.VisualType)}' is not currently supported in TUI previews.\nUse the Web Dashboard or Markdown export for full rendering.", new Style(Color.Grey)))
                .Header($"{Markup.Escape(visual.Name)} (Unsupported)")
                .Border(BoxBorder.Square)
                .Padding(1, 1, 1, 1);
        }

        private static IRenderable RenderSlicer(VisualManifest visual, ReportManifest? manifest = null)
        {
            var title = GetVisualTitle(visual);
            var rows = visual.Rows;
            if (rows.Count == 0) return RenderPlaceholder(visual);

            // Determine current selected value if manifest is available
            string? selectedValue = null;
            var setParamAction = visual.Actions.FirstOrDefault(a => a.Type == "SET_PARAMETER");
            var pName = setParamAction?.ParameterName ?? "none";
            if (manifest != null && !string.IsNullOrEmpty(pName))
            {
                if (manifest.Parameters.TryGetValue(pName, out selectedValue)) { }
                else if (pName.StartsWith("@") && manifest.Parameters.TryGetValue(pName.Substring(1), out selectedValue)) { }
                else if (!pName.StartsWith("@") && manifest.Parameters.TryGetValue("@" + pName, out selectedValue)) { }

                if (selectedValue != null)
                {
                    selectedValue = selectedValue.Trim().Replace("'", "").Replace("\"", "");
                }
            }

            // A slicer in terminal shows options as selectable tags
            var items = rows.Select(r => r.FirstOrDefault()?.Trim() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList();

            // Highlight options (simulated in TUI preview)
            var tags = items.Select(i =>
            {
                bool isSelected = selectedValue != null && i.Equals(selectedValue, StringComparison.OrdinalIgnoreCase);
                var style = isSelected ? "white on green" : "white on blue";
                return $"[{style}] {Markup.Escape(i)} [/]";
            });

            var markup = string.Join("  ", tags);

            // If we have a selected value, add it to the footer for visibility
            var debugInfo = $"[grey]Param: {pName} | Value: {Markup.Escape(selectedValue ?? "NULL")}[/]";
            var content = new Rows(
                new Markup(markup),
                new Text("\n"),
                new Markup(debugInfo)
            );

            return new Panel(content)
            {
                Header = new PanelHeader(Markup.Escape(title)),
                Border = BoxBorder.Rounded,
                Expand = false,
                Padding = new Padding(1, 0, 1, 0)
            };
        }

        private static Color GetColorForIndex(int index)
        {
            return ChartColors[index % ChartColors.Length];
        }

        // Markup-token names parallel to ChartColors, for braille rendering.
        private static readonly string[] ChartColorNames =
            { "blue", "green", "yellow", "red", "purple", "cyan", "orange1", "teal" };
        private static string ColorNameForIndex(int index) => ChartColorNames[index % ChartColorNames.Length];

        private static string FormatNum(double v) =>
            Math.Abs(v) >= 1000 ? v.ToString("N0", CultureInfo.InvariantCulture) : v.ToString("0.##", CultureInfo.InvariantCulture);

        /// <summary>Rounds a numeric cell to thousands-separated, ≤2 decimals; passes non-numbers through.</summary>
        public static string FormatNumericCell(string? raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw ?? "";
            if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
                return v.ToString("#,##0.##", CultureInfo.InvariantCulture);
            return raw;
        }

        private static string GetVisualTitle(VisualManifest visual)
        {
            // Priority: Options["TITLE"] -> Styles["TITLE"] -> visual.Name
            if (visual.Options.TryGetValue("TITLE", out var optTitle) && !string.IsNullOrEmpty(optTitle))
                return optTitle;

            if (visual.Styles != null && visual.Styles.TryGetValue("TITLE", out var styleTitle) && !string.IsNullOrEmpty(styleTitle))
                return styleTitle;

            return visual.Name;
        }

        private static IRenderable RenderBubbleChart(VisualManifest visual)
        {
            var title = GetVisualTitle(visual);
            var rows = visual.Rows;
            if (rows.Count == 0) return RenderPlaceholder(visual);

            // Set up a standard 50x12 grid canvas as approved in plan
            var canvas = new Canvas(50, 12);

            // X, Y and Size values
            var points = new List<(double x, double y, double size)>();
            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;
            double maxSize = double.MinValue;

            foreach (var row in rows)
            {
                if (row.Count > 2 &&
                    double.TryParse(row[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var xVal) &&
                    double.TryParse(row[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var yVal) &&
                    double.TryParse(row[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var sizeVal))
                {
                    points.Add((xVal, yVal, sizeVal));
                    minX = Math.Min(minX, xVal);
                    maxX = Math.Max(maxX, xVal);
                    minY = Math.Min(minY, yVal);
                    maxY = Math.Max(maxY, yVal);
                    maxSize = Math.Max(maxSize, sizeVal);
                }
            }

            if (points.Count == 0) return RenderPlaceholder(visual);

            // Scale protection
            if (maxX == minX) maxX += 1;
            if (maxY == minY) maxY += 1;
            if (maxSize == 0) maxSize = 1;

            // Draw bubbles as solid circles on canvas using Spectre
            for (int i = 0; i < points.Count; i++)
            {
                var pt = points[i];
                int cx = (int)((pt.x - minX) / (maxX - minX) * 48) + 1;
                int cy = 10 - (int)((pt.y - minY) / (maxY - minY) * 10);
                double normSize = pt.size / maxSize;
                int radius = (int)(normSize * 3);
                if (radius < 1) radius = 1;

                Color color = GetColorForIndex(i);

                // Draw circle using distance formula
                for (int dy = -radius; dy <= radius; dy++)
                {
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        if (dx * dx + dy * dy <= radius * radius)
                        {
                            int px = cx + dx;
                            int py = cy + dy;
                            if (px >= 0 && px < 50 && py >= 0 && py < 12)
                            {
                                canvas.SetPixel(px, py, color);
                            }
                        }
                    }
                }
            }

            return new Panel(canvas)
            {
                Header = new PanelHeader(Markup.Escape(title)),
                Border = BoxBorder.Rounded,
                Expand = false
            };
        }

        private static IRenderable RenderFunnelChart(VisualManifest visual)
        {
            var title = GetVisualTitle(visual);
            var rows = visual.Rows;
            if (rows.Count == 0) return RenderPlaceholder(visual);

            var content = new List<IRenderable>();
            double maxVal = 0;
            var stages = new List<(string label, double value)>();

            foreach (var row in rows)
            {
                var label = row.Count > 0 ? row[0] ?? "Stage" : "Stage";
                double val = 0;
                if (row.Count > 1) double.TryParse(row[1], NumberStyles.Any, CultureInfo.InvariantCulture, out val);
                stages.Add((label, val));
                maxVal = Math.Max(maxVal, val);
            }

            if (maxVal == 0) maxVal = 1;
            int maxWidth = 40;

            for (int i = 0; i < stages.Count; i++)
            {
                var stage = stages[i];
                int barWidth = (int)(stage.value / maxVal * maxWidth);
                if (barWidth < 1 && stage.value > 0) barWidth = 1;
                int leftPadding = (maxWidth - barWidth) / 2;

                string indent = new string(' ', leftPadding);
                string bar = new string('█', barWidth);
                Color color = GetColorForIndex(i);

                content.Add(new Text($"{stage.label.PadRight(15)} {indent}", Style.Plain));
                content.Add(new Text(bar, new Style(color)));
                content.Add(new Text($" ({stage.value:N0})", new Style(Color.Grey)));
                content.Add(new Text("\n"));
            }

            return new Panel(new Rows(content))
            {
                Header = new PanelHeader(Markup.Escape(title)),
                Border = BoxBorder.Rounded,
                Expand = false
            };
        }

        private static IRenderable RenderGanttChart(VisualManifest visual)
        {
            var title = GetVisualTitle(visual);
            var rows = visual.Rows;
            if (rows.Count == 0) return RenderPlaceholder(visual);

            var content = new List<IRenderable>();
            var tasks = new List<(string name, double start, double end)>();
            double minTime = double.MaxValue, maxTime = double.MinValue;

            foreach (var row in rows)
            {
                var name = row.Count > 0 ? row[0] ?? "Task" : "Task";
                double start = 0, end = 0;
                if (row.Count > 1) double.TryParse(row[1], NumberStyles.Any, CultureInfo.InvariantCulture, out start);
                if (row.Count > 2) double.TryParse(row[2], NumberStyles.Any, CultureInfo.InvariantCulture, out end);

                tasks.Add((name, start, end));
                minTime = Math.Min(minTime, start);
                maxTime = Math.Max(maxTime, end);
            }

            if (maxTime == minTime) maxTime += 1;
            int timelineWidth = 30;

            for (int i = 0; i < tasks.Count; i++)
            {
                var task = tasks[i];
                int pStart = (int)((task.start - minTime) / (maxTime - minTime) * timelineWidth);
                int pEnd = (int)((task.end - minTime) / (maxTime - minTime) * timelineWidth);
                pStart = Math.Clamp(pStart, 0, timelineWidth);
                pEnd = Math.Clamp(pEnd, 0, timelineWidth);
                int pLen = Math.Max(1, pEnd - pStart);

                string before = new string(' ', pStart);
                string bar = new string('█', pLen);
                string after = new string(' ', timelineWidth - pStart - pLen);

                Color color = GetColorForIndex(i);

                content.Add(new Text($"{task.name.PadRight(15)} │", Style.Plain));
                content.Add(new Text(before, Style.Plain));
                content.Add(new Text(bar, new Style(color)));
                content.Add(new Text(after, Style.Plain));
                content.Add(new Text($"│ ({task.start:N0} to {task.end:N0})", new Style(Color.Grey)));
                content.Add(new Text("\n"));
            }

            return new Panel(new Rows(content))
            {
                Header = new PanelHeader(Markup.Escape(title)),
                Border = BoxBorder.Rounded,
                Expand = false
            };
        }

        private static IRenderable RenderCandlestickChart(VisualManifest visual)
        {
            var title = GetVisualTitle(visual);
            var rows = visual.Rows;
            if (rows.Count == 0) return RenderPlaceholder(visual);

            var canvas = new Canvas(50, 12);
            var candles = new List<(double open, double high, double low, double close)>();
            double minVal = double.MaxValue, maxVal = double.MinValue;

            foreach (var row in rows)
            {
                int offset = row.Count > 4 ? 1 : 0;
                if (row.Count > offset + 3 &&
                    double.TryParse(row[offset], NumberStyles.Any, CultureInfo.InvariantCulture, out var o) &&
                    double.TryParse(row[offset + 1], NumberStyles.Any, CultureInfo.InvariantCulture, out var h) &&
                    double.TryParse(row[offset + 2], NumberStyles.Any, CultureInfo.InvariantCulture, out var l) &&
                    double.TryParse(row[offset + 3], NumberStyles.Any, CultureInfo.InvariantCulture, out var c))
                {
                    candles.Add((o, h, l, c));
                    minVal = Math.Min(minVal, l);
                    maxVal = Math.Max(maxVal, h);
                }
            }

            if (candles.Count == 0) return RenderPlaceholder(visual);

            if (maxVal == minVal) maxVal += 1;
            int numCandles = Math.Min(candles.Count, 24);
            int candleSpacing = 50 / numCandles;

            for (int i = 0; i < numCandles; i++)
            {
                var candle = candles[i];
                int cx = i * candleSpacing + (candleSpacing / 2);

                int yHigh = 10 - (int)((candle.high - minVal) / (maxVal - minVal) * 10);
                int yLow = 10 - (int)((candle.low - minVal) / (maxVal - minVal) * 10);
                int yOpen = 10 - (int)((candle.open - minVal) / (maxVal - minVal) * 10);
                int yClose = 10 - (int)((candle.close - minVal) / (maxVal - minVal) * 10);

                yHigh = Math.Clamp(yHigh, 0, 11);
                yLow = Math.Clamp(yLow, 0, 11);
                yOpen = Math.Clamp(yOpen, 0, 11);
                yClose = Math.Clamp(yClose, 0, 11);

                Color color = candle.close >= candle.open ? Color.Green : Color.Red;

                for (int y = Math.Min(yHigh, yLow); y <= Math.Max(yHigh, yLow); y++)
                {
                    canvas.SetPixel(cx, y, Color.Grey);
                }

                for (int y = Math.Min(yOpen, yClose); y <= Math.Max(yOpen, yClose); y++)
                {
                    canvas.SetPixel(cx, y, color);
                    if (cx > 0) canvas.SetPixel(cx - 1, y, color);
                    if (cx < 49) canvas.SetPixel(cx + 1, y, color);
                }
            }

            return new Panel(canvas)
            {
                Header = new PanelHeader(Markup.Escape(title)),
                Border = BoxBorder.Rounded,
                Expand = false
            };
        }

        private static IRenderable RenderTrellisChart(VisualManifest visual, ReportManifest? manifest)
        {
            var title = GetVisualTitle(visual);
            var rows = visual.Rows;
            if (rows.Count == 0) return RenderPlaceholder(visual);

            int facetIdx = 0;
            if (visual.Options.TryGetValue("FACET", out var facetCol) || visual.Options.TryGetValue("FACET_COLUMN", out facetCol))
            {
                int colIdx = visual.Columns.FindIndex(c => c.Equals(facetCol, StringComparison.OrdinalIgnoreCase));
                if (colIdx >= 0) facetIdx = colIdx;
            }

            var grouped = new Dictionary<string, List<List<string?>>>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                var fVal = row.Count > facetIdx ? row[facetIdx] ?? "Other" : "Other";
                if (!grouped.TryGetValue(fVal, out var gRows))
                {
                    gRows = new List<List<string?>>();
                    grouped[fVal] = gRows;
                }
                gRows.Add(row);
            }

            var grid = new Table().Border(TableBorder.None).HideHeaders();
            grid.AddColumn(new TableColumn("Col1"));
            grid.AddColumn(new TableColumn("Col2"));

            var subVisuals = new List<IRenderable>();
            foreach (var pair in grouped)
            {
                var subManifest = new VisualManifest
                {
                    Name = $"{visual.Name} ({pair.Key})",
                    VisualType = "BAR",
                    Columns = visual.Columns,
                    Rows = pair.Value,
                    Options = visual.Options,
                    Styles = visual.Styles
                };

                var subRender = RenderVerticalBarChart(subManifest);
                subVisuals.Add(new Panel(subRender)
                {
                    Header = new PanelHeader(Markup.Escape(pair.Key)),
                    Border = BoxBorder.Rounded,
                    Padding = new Padding(1, 0, 1, 0)
                });
            }

            for (int i = 0; i < subVisuals.Count; i += 2)
            {
                if (i + 1 < subVisuals.Count)
                    grid.AddRow(subVisuals[i], subVisuals[i + 1]);
                else
                    grid.AddRow(subVisuals[i], new Text(""));
            }

            return new Panel(grid)
            {
                Header = new PanelHeader(Markup.Escape(title)),
                Border = BoxBorder.Rounded,
                Expand = false
            };
        }

        private static IRenderable RenderMatrixChart(VisualManifest visual)
        {
            var title = GetVisualTitle(visual);
            var rows = visual.Rows;
            if (rows.Count == 0) return RenderPlaceholder(visual);

            var table = new Table();
            foreach (var col in visual.Columns)
            {
                table.AddColumn(new TableColumn($"[blue]{Markup.Escape(col)}[/]").Centered());
            }

            foreach (var row in rows)
            {
                var displayRow = new List<IRenderable>();
                for (int i = 0; i < row.Count; i++)
                {
                    var cell = row[i] ?? "";
                    if (i == 0)
                    {
                        if (cell.Contains(">"))
                        {
                            var parts = cell.Split('>');
                            string indent = new string(' ', (parts.Length - 1) * 3);
                            displayRow.Add(new Markup($"{indent}[grey][[+]] [/]{Markup.Escape(parts.Last().Trim())}"));
                        }
                        else
                        {
                            displayRow.Add(new Markup($"[bold green][[-]] [/]{Markup.Escape(cell)}"));
                        }
                    }
                    else
                    {
                        displayRow.Add(new Text(cell));
                    }
                }
                table.AddRow(displayRow.ToArray());
            }

            return new Panel(table)
            {
                Header = new PanelHeader(Markup.Escape(title)),
                Border = BoxBorder.Rounded,
                Expand = false
            };
        }

        private static IRenderable RenderCheckbox(VisualManifest visual, ReportManifest? manifest)
        {
            var title = GetVisualTitle(visual);
            bool isChecked = false;

            var pName = visual.Actions.FirstOrDefault(a => a.Type == "SET_PARAMETER")?.ParameterName ?? "none";
            if (manifest != null && !string.IsNullOrEmpty(pName))
            {
                if (manifest.Parameters.TryGetValue(pName, out var pVal) ||
                    manifest.Parameters.TryGetValue(pName.StartsWith("@") ? pName.Substring(1) : "@" + pName, out pVal))
                {
                    isChecked = pVal.Trim().Equals("TRUE", StringComparison.OrdinalIgnoreCase) || pVal.Trim().Equals("1");
                }
            }

            var checkMarkup = isChecked ? "[bold green][[X]][/]" : "[grey][[ ]][/]";
            var content = new Markup($"{checkMarkup} [white]{Markup.Escape(title)}[/]");

            return new Panel(content)
            {
                Border = BoxBorder.Rounded,
                Expand = false,
                Padding = new Padding(1, 0, 1, 0)
            };
        }

        private static IRenderable RenderTextbox(VisualManifest visual, ReportManifest? manifest)
        {
            var title = GetVisualTitle(visual);
            string currentVal = visual.DefaultValue ?? "";

            var pName = visual.Actions.FirstOrDefault(a => a.Type == "SET_PARAMETER")?.ParameterName ?? "none";
            if (manifest != null && !string.IsNullOrEmpty(pName))
            {
                if (manifest.Parameters.TryGetValue(pName, out var pVal) ||
                    manifest.Parameters.TryGetValue(pName.StartsWith("@") ? pName.Substring(1) : "@" + pName, out pVal))
                {
                    currentVal = pVal.Trim('\'', '"');
                }
            }

            var content = new Markup($"[blue]{Markup.Escape(title)}:[/] [grey][[[/] {Markup.Escape(currentVal.PadRight(20))} [grey]]][/]");
            return new Panel(content)
            {
                Border = BoxBorder.Rounded,
                Expand = false,
                Padding = new Padding(1, 0, 1, 0)
            };
        }

        private static IRenderable RenderNumberbox(VisualManifest visual, ReportManifest? manifest)
        {
            var title = GetVisualTitle(visual);
            string currentVal = visual.DefaultValue ?? "0";

            var pName = visual.Actions.FirstOrDefault(a => a.Type == "SET_PARAMETER")?.ParameterName ?? "none";
            if (manifest != null && !string.IsNullOrEmpty(pName))
            {
                if (manifest.Parameters.TryGetValue(pName, out var pVal) ||
                    manifest.Parameters.TryGetValue(pName.StartsWith("@") ? pName.Substring(1) : "@" + pName, out pVal))
                {
                    currentVal = pVal;
                }
            }

            double min = visual.Min ?? 0;
            double max = visual.Max ?? 100;

            var content = new Markup($"[blue]{Markup.Escape(title)}:[/] [grey][[[/] {currentVal.PadRight(10)} [grey]]][/] [grey](Min: {min}, Max: {max})[/]");
            return new Panel(content)
            {
                Border = BoxBorder.Rounded,
                Expand = false,
                Padding = new Padding(1, 0, 1, 0)
            };
        }

        private static IRenderable RenderMapPlaceholder(VisualManifest visual)
        {
            var table = new Table().Border(TableBorder.None).HideHeaders();
            table.AddColumn("Bullet");
            table.AddColumn("Value");

            int limit = 0;
            foreach (var row in visual.Rows)
            {
                if (limit++ >= 5) break;
                var region = row.Count > 0 ? row[0] ?? "Unknown" : "Unknown";
                var val = row.Count > 1 ? row[1] ?? "0" : "0";
                table.AddRow($"[grey]•[/] {Markup.Escape(region)}", $"[bold cyan]{val}[/]");
            }

            var content = new Rows(
                new Markup("🗺️ [bold yellow]Region Map[/] [italic red](Cannot display map in terminal mode)[/]"),
                new Text("\nTop Regions:"),
                table
            );

            return new Panel(content)
            {
                Header = new PanelHeader(Markup.Escape(GetVisualTitle(visual))),
                Border = BoxBorder.Rounded,
                Padding = new Padding(1, 0, 1, 0)
            };
        }

        private static IRenderable RenderImagePlaceholder(VisualManifest visual)
        {
            var src = visual.DefaultValue ?? "N/A";
            var alt = visual.Options.GetValueOrDefault("ALT") ?? "N/A";

            var content = new Rows(
                new Markup("🖼️ [bold yellow]Image Visual[/] [italic red](Cannot display image in terminal mode)[/]"),
                new Markup($"[grey]Source:[/] [cyan]{Markup.Escape(src)}[/]"),
                new Markup($"[grey]Alt:[/] [cyan]{Markup.Escape(alt)}[/]")
            );

            return new Panel(content)
            {
                Header = new PanelHeader(Markup.Escape(GetVisualTitle(visual))),
                Border = BoxBorder.Double,
                Padding = new Padding(1, 0, 1, 0)
            };
        }

        private static IRenderable RenderComboPlaceholder(VisualManifest visual)
        {
            var title = GetVisualTitle(visual);
            var subBar = new VisualManifest
            {
                Name = $"{visual.Name} (Bars)",
                VisualType = "BAR",
                Columns = visual.Columns,
                Rows = visual.Rows,
                Options = visual.Options,
                Styles = visual.Styles
            };

            var subLine = new VisualManifest
            {
                Name = $"{visual.Name} (Lines)",
                VisualType = "LINE",
                Columns = visual.Columns,
                Rows = visual.Rows,
                Options = visual.Options,
                Styles = visual.Styles
            };

            var content = new Rows(
                new Markup("📊 [bold yellow]Combo View[/] [grey](Stacked Bar + Line ASCII preview)[/]"),
                new Text("\n"),
                RenderVerticalBarChart(subBar),
                new Text("\n"),
                RenderLineChart(subLine)
            );

            return new Panel(content)
            {
                Header = new PanelHeader(Markup.Escape(title)),
                Border = BoxBorder.Rounded,
                Padding = new Padding(1, 0, 1, 0)
            };
        }

        private static IRenderable RenderTreemapPlaceholder(VisualManifest visual)
        {
            var title = GetVisualTitle(visual);
            var rows = visual.Rows;

            var content = new List<IRenderable>
            {
                new Markup("🌳 [bold yellow]Treemap Visual[/] [grey](Flat hierarchy breakdown)[/]"),
                new Text("\n")
            };

            var table = new Table().Border(TableBorder.None).HideHeaders();
            table.AddColumn("Item");
            table.AddColumn("Value");

            int limit = 0;
            foreach (var r in rows)
            {
                if (limit++ >= 5) break;
                var cat = r.Count > 0 ? r[0] ?? "Other" : "Other";
                var val = r.Count > 1 ? r[1] ?? "0" : "0";
                table.AddRow($"[grey]•[/] {Markup.Escape(cat)}", $"[bold green]{val}[/]");
            }
            content.Add(table);

            return new Panel(new Rows(content))
            {
                Header = new PanelHeader(Markup.Escape(title)),
                Border = BoxBorder.Rounded,
                Padding = new Padding(1, 0, 1, 0)
            };
        }

        private static IRenderable RenderRadarPlaceholder(VisualManifest visual)
        {
            var title = GetVisualTitle(visual);
            var table = new Table();
            table.AddColumn(new TableColumn("[blue]Dimension[/]").Centered());
            table.AddColumn(new TableColumn("[blue]Score / Value[/]").Centered());

            foreach (var r in visual.Rows)
            {
                var dim = r.Count > 0 ? r[0] ?? "Metric" : "Metric";
                var val = r.Count > 1 ? r[1] ?? "0" : "0";
                table.AddRow(Markup.Escape(dim), val);
            }

            var content = new Rows(
                new Markup("🕸️ [bold yellow]Radar Dimensions Summary[/] [grey](Tabular overview)[/]"),
                new Text("\n"),
                table
            );

            return new Panel(content)
            {
                Header = new PanelHeader(Markup.Escape(title)),
                Border = BoxBorder.Rounded,
                Padding = new Padding(1, 0, 1, 0)
            };
        }

        private static IRenderable RenderSankeyPlaceholder(VisualManifest visual)
        {
            var title = GetVisualTitle(visual);
            var content = new List<IRenderable>
            {
                new Markup("🌊 [bold yellow]Sankey Flow Overview[/] [grey](Direct connections)[/]"),
                new Text("\n")
            };

            int limit = 0;
            foreach (var r in visual.Rows)
            {
                if (limit++ >= 5) break;
                var src = r.Count > 0 ? r[0] ?? "Source" : "Source";
                var dst = r.Count > 1 ? r[1] ?? "Target" : "Target";
                var flow = r.Count > 2 ? r[2] ?? "0" : "0";
                content.Add(new Markup($"  [grey]•[/] {Markup.Escape(src)} ──▶ {Markup.Escape(dst)} [bold cyan]({flow})[/]\n"));
            }

            return new Panel(new Rows(content))
            {
                Header = new PanelHeader(Markup.Escape(title)),
                Border = BoxBorder.Rounded,
                Padding = new Padding(1, 0, 1, 0)
            };
        }

        private static IRenderable RenderSunburstPlaceholder(VisualManifest visual)
        {
            var title = GetVisualTitle(visual);
            var content = new List<IRenderable>
            {
                new Markup("☀️ [bold yellow]Sunburst Hierarchy[/] [grey](Flat tree list)[/]"),
                new Text("\n")
            };

            int limit = 0;
            foreach (var r in visual.Rows)
            {
                if (limit++ >= 5) break;
                var path = r.Count > 0 ? r[0] ?? "Root" : "Root";
                var val = r.Count > 1 ? r[1] ?? "0" : "0";

                var cleanPath = path.Replace(">", " ──▶ ");
                content.Add(new Markup($"  [grey]•[/] {cleanPath} [bold green]({val})[/]\n"));
            }

            return new Panel(new Rows(content))
            {
                Header = new PanelHeader(Markup.Escape(title)),
                Border = BoxBorder.Rounded,
                Padding = new Padding(1, 0, 1, 0)
            };
        }

        private static IRenderable RenderNetworkPlaceholder(VisualManifest visual)
        {
            var title = GetVisualTitle(visual);
            var content = new Rows(
                new Markup("🕸️ [bold yellow]Network Topology Metrics[/]"),
                new Text("\n"),
                new Markup($"  [grey]Total Graph Rows:[/] [cyan]{visual.Rows.Count}[/]"),
                new Markup($"  [grey]Schema Columns:[/] [cyan]{string.Join(", ", visual.Columns.Select(Markup.Escape))}[/]")
            );

            return new Panel(content)
            {
                Header = new PanelHeader(Markup.Escape(title)),
                Border = BoxBorder.Rounded,
                Padding = new Padding(1, 0, 1, 0)
            };
        }

        private static IRenderable RenderMarkdownText(string markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown)) return new Text("");

            var lines = markdown.Split('\n');
            var result = new List<IRenderable>();

            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd('\r');
                var trimmed = line.TrimStart();

                if (trimmed.StartsWith("#"))
                {
                    int level = 0;
                    while (level < trimmed.Length && trimmed[level] == '#') level++;
                    var text = trimmed.Substring(level).Trim();

                    string style = level switch
                    {
                        1 => "bold blue underline",
                        2 => "bold blue",
                        3 => "bold cyan",
                        _ => "bold"
                    };

                    result.Add(new Markup($"[{style}]{Markup.Escape(text)}[/]"));
                    result.Add(new Text("\n"));
                    continue;
                }

                if (trimmed == "---" || trimmed == "___" || trimmed == "***")
                {
                    result.Add(new Rule().RuleStyle("grey"));
                    continue;
                }

                bool isBullet = false;
                if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
                {
                    isBullet = true;
                    trimmed = trimmed.Substring(2);
                }

                string parsed = ParseInlineMarkdown(trimmed);

                if (isBullet)
                {
                    result.Add(new Markup($"  [grey]•[/] {parsed}"));
                }
                else
                {
                    result.Add(new Markup(parsed));
                }
                result.Add(new Text("\n"));
            }

            return new Rows(result);
        }

        private static string ParseInlineMarkdown(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            string escaped = Markup.Escape(text);

            escaped = System.Text.RegularExpressions.Regex.Replace(escaped, @"\*\*(.*?)\*\*", "[bold]$1[/]");
            escaped = System.Text.RegularExpressions.Regex.Replace(escaped, @"__(.*?)__", "[bold]$1[/]");
            escaped = System.Text.RegularExpressions.Regex.Replace(escaped, @"\*(.*?)\*", "[italic]$1[/]");
            escaped = System.Text.RegularExpressions.Regex.Replace(escaped, @"_(.*?)_", "[italic]$1[/]");
            escaped = System.Text.RegularExpressions.Regex.Replace(escaped, @"`(.*?)`", "[yellow]$1[/]");
            escaped = System.Text.RegularExpressions.Regex.Replace(escaped, @"\[(.*?)\]\((.*?)\)", "[link=$2]$1[/]");

            return escaped;
        }
    }
}
