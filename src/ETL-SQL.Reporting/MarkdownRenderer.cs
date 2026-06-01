using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ETL_SQL.Reporting
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
            string heading = manifest.Title ?? "Report";
            if (manifest.TitleIsMarkdown)
                sb.AppendLine($"# {heading}");
            else
                sb.AppendLine($"# {EscapeCell(heading)}");
            sb.AppendLine();
            if (manifest.Description != null)
            {
                sb.AppendLine(manifest.Description);
                sb.AppendLine();
            }
            if (manifest.Source != null) sb.Append($"*Source: {manifest.Source} | ");
            else sb.Append("*");
            sb.AppendLine($"Generated: {manifest.BuiltAt:yyyy-MM-dd HH:mm:ss} UTC*");
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
                    RenderVisual(sb, visual, manifest);
            }

            return sb.ToString();
        }

        private void RenderPage(StringBuilder sb, PageManifest page, ReportManifest manifest)
        {
            string heading = page.Title ?? page.Name;
            if (page.TitleIsMarkdown)
                sb.AppendLine($"## {heading}");
            else
                sb.AppendLine($"## {EscapeCell(heading)}");

            if (!string.IsNullOrEmpty(page.Subtitle))
            {
                if (page.SubtitleIsMarkdown) sb.AppendLine(page.Subtitle);
                else sb.AppendLine($"*{EscapeCell(page.Subtitle)}*");
            }
            sb.AppendLine();

            // Emit items referenced in slot map in slot order
            foreach (var (_, itemName) in page.SlotMap.OrderBy(kv => kv.Key))
            {
                var visual = manifest.Visuals.FirstOrDefault(v =>
                    string.Equals(v.Name, itemName, StringComparison.OrdinalIgnoreCase));
                if (visual != null)
                {
                    RenderVisual(sb, visual, manifest);
                }
                else
                {
                    var container = manifest.Containers?.FirstOrDefault(c =>
                        string.Equals(c.Name, itemName, StringComparison.OrdinalIgnoreCase));
                    if (container != null)
                        RenderContainer(sb, container, manifest);
                }
            }
        }

        private void RenderContainer(StringBuilder sb, ContainerManifest container, ReportManifest manifest)
        {
            // For Markdown, we just emit inner visuals in slot order.
            if (!string.IsNullOrEmpty(container.Title))
            {
                if (container.TitleIsMarkdown) sb.AppendLine($"### {container.Title}");
                else sb.AppendLine($"### {EscapeCell(container.Title)}");
                sb.AppendLine();
            }

            if (container.SlotMap != null)
            {
                foreach (var (_, itemName) in container.SlotMap.OrderBy(kv => kv.Key))
                {
                    var visual = manifest.Visuals.FirstOrDefault(v =>
                        string.Equals(v.Name, itemName, StringComparison.OrdinalIgnoreCase));
                    if (visual != null)
                    {
                        RenderVisual(sb, visual, manifest);
                    }
                    else
                    {
                        var nested = manifest.Containers?.FirstOrDefault(c =>
                            string.Equals(c.Name, itemName, StringComparison.OrdinalIgnoreCase));
                        if (nested != null)
                            RenderContainer(sb, nested, manifest);
                    }
                }
            }
        }

        private void RenderVisual(StringBuilder sb, VisualManifest v, ReportManifest manifest)
        {
            string heading = v.Options.TryGetValue("title", out var t) ? t : v.Name;
            if (v.TitleIsMarkdown)
                sb.AppendLine($"### {heading}");
            else
                sb.AppendLine($"### {EscapeCell(heading)}");

            if (v.Options.TryGetValue("subtitle", out var st))
            {
                if (v.SubtitleIsMarkdown) sb.AppendLine(st);
                else sb.AppendLine($"*{EscapeCell(st)}*");
            }
            sb.AppendLine();

            switch (v.VisualType.ToUpperInvariant())
            {
                case "TABLE":
                    RenderTable(sb, v);
                    break;

                case "CARD":
                    RenderCard(sb, v);
                    break;

                // Filter/input controls: show the selection in effect at export time.
                case "SLICER":
                case "MULTISELECT":
                case "DATEPICKER":
                case "RELDATEPICKER":
                case "SLIDER":
                case "SEARCH":
                case "NUMBERBOX":
                case "CHECKBOX":
                case "DROPDOWN":
                    RenderFilter(sb, v, manifest);
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
                        
                        if (v.IsMarkdown) sb.AppendLine(textContent);
                        else sb.AppendLine(EscapeCell(textContent));

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
                    // Embed SVG image for static export. Prefer real ECharts (SSR);
                    // fall back to the static renderer when there's no option or SSR fails.
                    var svgStr = EChartsSsrRenderer.Shared.RenderSvg(v) ?? _svg.Render(v);
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
                var cells = v.Columns.Select((_, ci) => ci < row.Count ? EscapeCell(ReportCellFormatter.FormatCell(row[ci])) : "");
                sb.AppendLine("| " + string.Join(" | ", cells) + " |");
            }

            if (v.Rows.Count > 1000)
                sb.AppendLine($"*… {v.Rows.Count - 1000:N0} more rows not shown.*");

            sb.AppendLine();
        }

        private static void RenderFilter(StringBuilder sb, VisualManifest v, ReportManifest manifest)
        {
            var paramName = v.Actions
                .FirstOrDefault(a => string.Equals(a.Type, "SET_PARAMETER", StringComparison.OrdinalIgnoreCase))
                ?.ParameterName;
            if (string.IsNullOrEmpty(paramName))
                paramName = v.Options.GetValueOrDefault("PARAMETER")
                         ?? v.Options.GetValueOrDefault("parameter")
                         ?? v.Options.GetValueOrDefault("data-parameter");

            string? value = null;
            if (!string.IsNullOrEmpty(paramName))
                manifest.Parameters.TryGetValue(paramName, out value);

            var display = string.IsNullOrWhiteSpace(value) ? "(all)" : value!;
            sb.AppendLine($"*{EscapeCell(v.VisualType.ToLowerInvariant())} filter — selected:* **{EscapeCell(display)}**");
            sb.AppendLine();
        }

        private void RenderCard(StringBuilder sb, VisualManifest v)
        {
            // Use Mappings if available (VisualBuilder uses lowercase for these keys)
            v.Options.TryGetValue("mapping:label", out var labelMapping);
            v.Options.TryGetValue("mapping:value", out var valueMapping);

            var row = v.Rows.FirstOrDefault();
            string label = labelMapping ?? v.Name;
            string value = "No data";

            if (row != null)
            {
                int labelIdx = v.Columns.FindIndex(c => string.Equals(c, labelMapping, StringComparison.OrdinalIgnoreCase));
                int valueIdx = v.Columns.FindIndex(c => string.Equals(c, valueMapping, StringComparison.OrdinalIgnoreCase));

                if (labelIdx >= 0 && row.Count > labelIdx)
                    label = row[labelIdx] ?? label;
                
                var rawValue = (valueIdx >= 0 && row.Count > valueIdx) ? row[valueIdx] : row.FirstOrDefault();
                value = rawValue ?? "0";
            }

            sb.AppendLine($"> **{EscapeCell(label)}:** {EscapeCell(value)}");
            sb.AppendLine();
        }

        private static string EscapeCell(string s) =>
            s.Replace("|", "\\|").Replace("\n", " ").Replace("\r", "");
    }
}
