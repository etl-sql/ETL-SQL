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
                    content.Add(RenderVisual(visual));
                    content.Add(new Text("\n")); // Spacer
                }
            }

            return new Rows(content);
        }

        /// <summary>
        /// Renders a single visual based on its type.
        /// </summary>
        public static IRenderable RenderVisual(VisualManifest visual)
        {
            try 
            {
                return visual.VisualType.ToUpperInvariant() switch
                {
                    "HBAR" or "HORIZONTALBAR" => RenderBarChart(visual),
                    "BAR" => RenderVerticalBarChart(visual),
                    "CARD" => RenderCard(visual),
                    "TABLE" => RenderTable(visual),
                    "TEXT" => RenderText(visual),
                    "GAUGE" => RenderGauge(visual),
                    "BOXPLOT" => RenderBoxPlot(visual),
                    "WATERFALL" => RenderWaterfall(visual),
                    _ => RenderPlaceholder(visual)
                };
            }
            catch (Exception ex)
            {
                return new Panel(new Text($"Error rendering {visual.Name}: {ex.Message}", new Style(Color.Red)))
                    .Header(visual.Name)
                    .Border(BoxBorder.Rounded);
            }
        }

        private static IRenderable RenderGauge(VisualManifest visual)
        {
            var title = visual.Options.GetValueOrDefault("TITLE", visual.Name);
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
                Header = new PanelHeader(title),
                Border = BoxBorder.Rounded,
                Expand = false,
                Padding = new Padding(1, 0, 1, 0)
            };
        }

        private static IRenderable RenderBoxPlot(VisualManifest visual)
        {
            var title = visual.Options.GetValueOrDefault("TITLE", visual.Name);
            var rows = visual.Rows;
            if (rows.Count == 0) return RenderPlaceholder(visual);

            var content = new List<IRenderable>();
            foreach (var row in rows)
            {
                if (row.Count < 5) continue;
                string label = row.Count > 5 ? row[0] : "Data";
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
                .Header(title)
                .Border(BoxBorder.Rounded);
        }

        private static IRenderable RenderWaterfall(VisualManifest visual)
        {
            var title = visual.Options.GetValueOrDefault("TITLE", visual.Name);
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
                .Header(title)
                .Border(BoxBorder.Rounded);
        }

        private static IRenderable RenderVerticalBarChart(VisualManifest visual)
        {
            var title = visual.Options.GetValueOrDefault("title", visual.Name);
            return new Panel(new Text("Vertical BAR charts are not currently supported in TUI previews.\nPlease use HBAR for horizontal terminal charts or view via Web Dashboard.", new Style(Color.Grey)))
                .Header(title)
                .Border(BoxBorder.Rounded)
                .Padding(1, 1, 1, 1);
        }

        private static IRenderable RenderBarChart(VisualManifest visual)
        {
            var title = visual.Options.GetValueOrDefault("title", visual.Name);
            var chart = new BarChart()
                .Label($"[bold]{title}[/]")
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
                    .Header(title)
                    .Border(BoxBorder.Rounded)
                    .Padding(1, 1, 1, 1);
            }

            return new Panel(chart).Border(BoxBorder.Rounded).Padding(1, 1, 1, 1);
        }

        private static IRenderable RenderCard(VisualManifest visual)
        {
            var value = visual.Rows.FirstOrDefault()?.FirstOrDefault() ?? "N/A";
            var label = visual.Options.GetValueOrDefault("TITLE", visual.Name);

            var panel = new Panel(Align.Center(new Markup($"[bold yellow]{value}[/]"), VerticalAlignment.Middle))
            {
                Header = new PanelHeader(label),
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
                .Title($"[bold]{visual.Name}[/]")
                .Border(TableBorder.Rounded);

            foreach (var col in visual.Columns)
            {
                table.AddColumn(new TableColumn($"[blue]{col}[/]").Centered());
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
                .Header(visual.Name)
                .Border(BoxBorder.None)
                .Padding(1, 0, 1, 0);
        }

        private static IRenderable RenderPlaceholder(VisualManifest visual)
        {
            return new Panel(new Text($"Visual Type '{visual.VisualType}' is not currently supported in TUI previews.\nUse the Web Dashboard or Markdown export for full rendering.", new Style(Color.Grey)))
                .Header($"{visual.Name} (Unsupported)")
                .Border(BoxBorder.Square)
                .Padding(1, 1, 1, 1);
        }

        private static Color GetColorForIndex(int index)
        {
            var colors = new[] { Color.Blue, Color.Green, Color.Yellow, Color.Red, Color.Purple, Color.Cyan, Color.Orange1, Color.Teal };
            return colors[index % colors.Length];
        }
    }
}
