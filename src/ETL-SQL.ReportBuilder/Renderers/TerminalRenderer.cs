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
