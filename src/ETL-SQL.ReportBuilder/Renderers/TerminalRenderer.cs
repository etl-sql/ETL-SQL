using System;
using System.Collections.Generic;
using System.Linq;
using Spectre.Console;
using Spectre.Console.Rendering;
using ETL_SQL.ReportBuilder;

namespace ETL_SQL.ReportBuilder.Renderers
{
    /// <summary>
    /// Converts a <see cref="ReportManifest"/> into Spectre.Console renderable objects.
    /// Used for terminal-based report previews in TUI and CLI.
    /// </summary>
    public static class TerminalRenderer
    {
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
                var visual = manifest.Visuals.FirstOrDefault(v => v.Name == vName);
                if (visual != null)
                {
                    content.Add(RenderVisual(visual, manifest));
                    content.Add(new Text("\n")); // Spacer
                }
            }

            return new Rows(content);
        }

        /// <summary>
        /// Renders a single visual based on its type.
        /// </summary>
        public static IRenderable RenderVisual(VisualManifest visual, ReportManifest? manifest = null)
        {
            try 
            {
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
                    "SLICER" or "MULTISELECT" or "DATEPICKER" or "SLIDER" or "SEARCH" => RenderSlicer(visual, manifest),
                    "TREEMAP" or "RADAR" or "BUBBLE" or "CANDLESTICK" or "MAP" => new Panel(
                        new Text($"{visual.VisualType} not supported in TUI", new Style(Color.Grey)))
                    {
                        Header = new PanelHeader(Markup.Escape(GetVisualTitle(visual))),
                        Border = BoxBorder.Rounded,
                        Expand = false
                    },
                    _ => RenderPlaceholder(visual)
                };
            }
            catch (Exception ex)
            {
                return new Panel(new Text($"Error rendering {Markup.Escape(visual.Name)}: {Markup.Escape(ex.Message)}", new Style(Color.Red)))
                    .Header(Markup.Escape(visual.Name))
                    .Border(BoxBorder.Rounded);
            }
        }

        private static IRenderable RenderLineChart(VisualManifest visual)
        {
            var title = GetVisualTitle(visual);
            var rows = visual.Rows;
            if (rows.Count < 2) return RenderPlaceholder(visual);

            // X is categorical or numeric; Y is numeric
            // For now, treat X as indices if not easily numeric
            var points = new List<(double x, double y)>();
            for (int i = 0; i < rows.Count; i++)
            {
                double x = i;
                if (double.TryParse(rows[i].FirstOrDefault() ?? "0", out var xVal)) x = xVal;
                if (double.TryParse(rows[i].ElementAtOrDefault(1) ?? "0", out var yVal))
                {
                    points.Add((x, yVal));
                }
            }

            if (points.Count < 2) return RenderPlaceholder(visual);

            double minX = points.Min(p => p.x);
            double maxX = points.Max(p => p.x);
            double minY = points.Min(p => p.y);
            double maxY = points.Max(p => p.y);
            if (maxX == minX) maxX += 1;
            if (maxY == minY) maxY += 1;

            int width = 50;
            int height = 12;
            var canvas = new Canvas(width, height) { PixelWidth = 1 };

            for (int i = 0; i < points.Count - 1; i++)
            {
                int x0 = (int)((points[i].x - minX) / (maxX - minX) * (width - 1));
                int y0 = (int)((points[i].y - minY) / (maxY - minY) * (height - 1));
                int x1 = (int)((points[i + 1].x - minX) / (maxX - minX) * (width - 1));
                int y1 = (int)((points[i + 1].y - minY) / (maxY - minY) * (height - 1));

                // Invert Y for terminal (0 is top)
                DrawLine(canvas, x0, height - 1 - y0, x1, height - 1 - y1, Color.Blue);
            }
            return new Panel(canvas)
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
                if (double.TryParse(row.FirstOrDefault() ?? "0", out var xVal) &&
                    double.TryParse(row.ElementAtOrDefault(1) ?? "0", out var yVal))
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
            var canvas = new Canvas(width, height) { PixelWidth = 1 };

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
                    if (double.TryParse(row[i], out var v)) maxVal = Math.Max(maxVal, v);
                }
            }
            if (maxVal == 0) maxVal = 1;

            foreach (var row in rows)
            {
                var displayRow = new List<IRenderable>();
                displayRow.Add(new Text(row[0] ?? ""));

                for (int i = 1; i < row.Count; i++)
                {
                    if (double.TryParse(row[i], out var v))
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
            var valueStr = visual.Rows.FirstOrDefault()?.FirstOrDefault() ?? "0";
            if (!double.TryParse(valueStr, out double value)) value = 0;

            double min = double.TryParse(visual.Options.GetValueOrDefault("MIN"), out var minVal) ? minVal : 0;
            double max = double.TryParse(visual.Options.GetValueOrDefault("MAX"), out var maxVal) ? maxVal : 100;

            double pct = (value - min) / (max - min);
            pct = Math.Clamp(pct, 0, 1);

            int width = 30;
            int filled = (int)(width * pct);
            string bar = new string('█', filled) + new string('░', width - filled);
            
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

                if (double.TryParse(row[offset], out double min) &&
                    double.TryParse(row[offset + 1], out double q1) &&
                    double.TryParse(row[offset + 2], out double med) &&
                    double.TryParse(row[offset + 3], out double q3) &&
                    double.TryParse(row[offset + 4], out double max))
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
            double currentSum = 0;
            double maxVal = rows.Max(r => {
                double.TryParse(r.Count > 1 ? r[1] : "0", out double v);
                return Math.Abs(currentSum + v);
            }); // Rough max for scaling
            
            // Recalculate max properly
            double rolling = 0;
            double absoluteMax = 0;
            foreach(var r in rows)
            {
                double.TryParse(r.Count > 1 ? r[1] : "0", out double v);
                rolling += v;
                absoluteMax = Math.Max(absoluteMax, Math.Abs(rolling));
                absoluteMax = Math.Max(absoluteMax, Math.Abs(rolling - v));
            }
            if (absoluteMax == 0) absoluteMax = 1;

            int fullWidth = 50;
            currentSum = 0;

            foreach (var row in rows)
            {
                string label = row[0] ?? "Item";
                if (!double.TryParse(row.Count > 1 ? row[1] : "0", out double val)) val = 0;

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
                var label = row.FirstOrDefault() ?? "";
                if (double.TryParse(row.ElementAtOrDefault(1) ?? "0", out var val))
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

            for (int h = height; h >= 1; h--)
            {
                var displayRow = new List<IRenderable>();
                for (int i = 0; i < data.Count; i++)
                {
                    double pct = data[i].value / maxVal;
                    int barHeight = (int)(pct * height);
                    if (h <= barHeight)
                        displayRow.Add(new Text("██", new Style(GetColorForIndex(i))));
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
                var label = rows[i].FirstOrDefault() ?? "Unknown";
                if (double.TryParse(rows[i].ElementAtOrDefault(1) ?? "0", out var val))
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
            foreach (var row in visual.Rows)
            {
                if (row.Count > Math.Max(labelIdx, valueIdx))
                {
                    var label = row[labelIdx] ?? "Unknown";
                    if (double.TryParse(row[valueIdx], out double val))
                    {
                        chart.AddItem(label, val, GetColorForIndex(visual.Rows.IndexOf(row)));
                        hasItems = true;
                    }
                }
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
            var value = visual.Rows.FirstOrDefault()?.FirstOrDefault() ?? "N/A";
            var label = GetVisualTitle(visual);

            var panel = new Panel(Align.Center(new Markup($"[bold yellow]{value}[/]"), VerticalAlignment.Middle))
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

            foreach (var row in visual.Rows)
            {
                table.AddRow(row.Select(c => c ?? "").ToArray());
            }

            return table;
        }

        private static IRenderable RenderText(VisualManifest visual)
        {
            string markdown = visual.DefaultValue ?? "";
            return new Panel(new Text(markdown))
                .Header(Markup.Escape(visual.Name))
                .Border(BoxBorder.None)
                .Padding(1, 0, 1, 0);
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
            var tags = items.Select(i => {
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
            var colors = new[] { Color.Blue, Color.Green, Color.Yellow, Color.Red, Color.Purple, Color.Cyan, Color.Orange1, Color.Teal };
            return colors[index % colors.Length];
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
    }
}
