using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ETL_SQL.ReportBuilder
{
    /// <summary>Embeds an SVG chart as an HTML img with a base-64 data URI.</summary>
    file static class SvgEmbed
    {
        internal static string ToDataUri(string svg)
        {
            var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
            return $"data:image/svg+xml;base64,{b64}";
        }
    }

    /// <summary>
    /// Converts a <see cref="ReportManifest"/> into a Markdown string.
    ///
    /// Chart-based visuals get an embedded <c>&lt;!-- CHART:{...} --&gt;</c> comment
    /// containing the Chart.js config JSON — processed by the VS Code preview
    /// and <c>etl-sql-report serve</c>.
    ///
    /// TABLE visuals are rendered as GFM pipe tables.
    /// CARD visuals show their value in a blockquote.
    /// SLICER visuals are omitted (interactive only; no static representation).
    /// </summary>
    public class MarkdownRenderer
    {
        private readonly SvgChartRenderer _svg = new();

        /// <summary>Renders the full manifest as a Markdown document string.</summary>
        public string Render(ReportManifest manifest)
        {
            var sb = new StringBuilder();
            string heading = manifest.Title ?? $"Report — {System.IO.Path.GetFileNameWithoutExtension(manifest.Source)}";
            sb.AppendLine($"# {heading}");
            sb.AppendLine();
            if (manifest.Description != null)
            {
                sb.AppendLine(manifest.Description);
                sb.AppendLine();
            }
            sb.AppendLine($"*Generated: {manifest.BuiltAt:yyyy-MM-dd HH:mm:ss} UTC*");
            sb.AppendLine();

            if (manifest.Pages.Count > 0)
            {
                foreach (var page in manifest.Pages)
                    RenderPage(sb, page, manifest);
            }
            else
            {
                // No pages defined — emit all visuals in definition order
                foreach (var visual in manifest.Visuals)
                    RenderVisual(sb, visual);
            }

            return sb.ToString();
        }

        private void RenderPage(StringBuilder sb, PageManifest page, ReportManifest manifest)
        {
            sb.AppendLine($"## {page.Name}");
            sb.AppendLine();

            // Emit visuals referenced in slot map in slot order
            foreach (var (_, visualName) in page.SlotMap.OrderBy(kv => kv.Key))
            {
                var visual = manifest.Visuals.FirstOrDefault(v =>
                    string.Equals(v.Name, visualName, StringComparison.OrdinalIgnoreCase));
                if (visual != null) RenderVisual(sb, visual);
            }
        }

        private void RenderVisual(StringBuilder sb, VisualManifest v)
        {
            sb.AppendLine($"### {v.Name}");
            sb.AppendLine();

            switch (v.VisualType.ToUpperInvariant())
            {
                case "TABLE":
                    RenderTable(sb, v);
                    break;

                case "CARD":
                    RenderCard(sb, v);
                    break;

                case "SLICER":
                    sb.AppendLine("*\\[Slicer — interactive only\\]*");
                    sb.AppendLine();
                    break;

                case "TEXT":
                {
                    v.Options.TryGetValue("VALUE", out var textContent);
                    v.Options.TryGetValue("ALIGN", out var align);
                    if (!string.IsNullOrWhiteSpace(textContent))
                    {
                        // Emit as a block-quote to give visual separation; honour alignment hint in HTML
                        if (align != null && !align.Equals("left", StringComparison.OrdinalIgnoreCase))
                            sb.AppendLine($"<div align='{align.ToLowerInvariant()}'>");
                        sb.AppendLine(textContent);
                        if (align != null && !align.Equals("left", StringComparison.OrdinalIgnoreCase))
                            sb.AppendLine("</div>");
                    }
                    sb.AppendLine();
                    break;
                }

                default:
                {
                    // Embed ECharts option as a comment for tooling / VS Code preview
                    if (v.ChartConfig != null)
                    {
                        sb.AppendLine($"<!-- ECHART:{v.ChartConfig} -->");
                        sb.AppendLine();
                    }
                    // Embed SVG image for static export (GitHub, PDF conversion, etc.)
                    var svgStr = _svg.Render(v);
                    if (svgStr != null)
                    {
                        var uri = SvgEmbed.ToDataUri(svgStr);
                        sb.AppendLine($"<img src=\"{uri}\" alt=\"{EscapeCell(v.Name)}\" width=\"600\" height=\"350\" />");
                        sb.AppendLine();
                    }
                    // Fallback data table for readability without images
                    if (v.Rows.Count > 0)
                        RenderTable(sb, v);
                    break;
                }
            }
        }

        private static void RenderTable(StringBuilder sb, VisualManifest v)
        {
            if (v.Columns.Count == 0) return;

            // Header
            sb.AppendLine("| " + string.Join(" | ", v.Columns.Select(EscapeCell)) + " |");
            sb.AppendLine("| " + string.Join(" | ", v.Columns.Select(_ => "---")) + " |");

            // Rows (cap at 1000 for document readability)
            int cap = Math.Min(v.Rows.Count, 1000);
            for (int i = 0; i < cap; i++)
            {
                var row = v.Rows[i];
                var cells = v.Columns.Select((_, ci) => ci < row.Count ? EscapeCell(row[ci] ?? "") : "");
                sb.AppendLine("| " + string.Join(" | ", cells) + " |");
            }

            if (v.Rows.Count > 1000)
                sb.AppendLine($"*… {v.Rows.Count - 1000:N0} more rows not shown.*");

            sb.AppendLine();
        }

        private static void RenderCard(StringBuilder sb, VisualManifest v)
        {
            // A CARD typically shows a single scalar value (first cell of first row)
            if (v.Rows.Count > 0 && v.Rows[0].Count > 0)
            {
                var label = v.Columns.Count > 0 ? v.Columns[0] : v.Name;
                var value = v.Rows[0][0] ?? "";
                sb.AppendLine($"> **{EscapeCell(label)}:** {EscapeCell(value)}");
            }
            else
            {
                sb.AppendLine("> *No data*");
            }
            sb.AppendLine();
        }

        private static string EscapeCell(string s) =>
            s.Replace("|", "\\|").Replace("\n", " ").Replace("\r", "");
    }
}
