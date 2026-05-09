using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ETL_SQL.Reporting
{
    /// <summary>
    /// Exports a <see cref="ReportManifest"/> to a PDF byte array using QuestPDF.
    /// Charts are rendered as SVG via <see cref="SvgChartRenderer"/>.
    /// No headless browser required.
    /// </summary>
    public class PdfExporter
    {
        private readonly SvgChartRenderer _svg = new();

        public byte[] Export(ReportManifest manifest)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(36, Unit.Point);
                    page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(10));

                    page.Content().Column(col =>
                    {
                        // ── Report header ────────────────────────────────────
                        col.Item()
                            .Text(manifest.Title ?? Path.GetFileNameWithoutExtension(manifest.Source))
                            .FontSize(20).Bold();

                        if (!string.IsNullOrWhiteSpace(manifest.Description))
                            col.Item().PaddingTop(4).Text(manifest.Description)
                                .FontColor(Colors.Grey.Darken2);

                        col.Item().PaddingTop(4)
                            .Text($"Generated: {manifest.BuiltAt:yyyy-MM-dd HH:mm} UTC")
                            .FontSize(8).FontColor(Colors.Grey.Darken1);

                        col.Item().PaddingVertical(10)
                            .LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

                        // ── Visuals ──────────────────────────────────────────
                        foreach (var visual in GetVisualsInOrder(manifest))
                            RenderVisual(col, visual);
                    });
                });
            }).GeneratePdf();
        }

        private static IEnumerable<VisualManifest> GetVisualsInOrder(ReportManifest manifest)
        {
            if (manifest.Pages.Count == 0) return manifest.Visuals;

            var seen   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<VisualManifest>();

            foreach (var page in manifest.Pages)
            {
                foreach (var (_, vName) in page.SlotMap.OrderBy(kv => kv.Key))
                {
                    if (!seen.Add(vName)) continue;
                    var v = manifest.Visuals.FirstOrDefault(x =>
                        string.Equals(x.Name, vName, StringComparison.OrdinalIgnoreCase));
                    if (v != null) result.Add(v);
                }
            }

            foreach (var v in manifest.Visuals)
                if (seen.Add(v.Name)) result.Add(v);

            return result;
        }

        private void RenderVisual(ColumnDescriptor col, VisualManifest v)
        {
            col.Item().PaddingTop(16).Text(v.Name).FontSize(13).Bold();

            if (v.Error != null)
            {
                col.Item().PaddingTop(4).Text($"Error: {v.Error}").FontColor(Colors.Red.Darken2);
                return;
            }

            switch (v.VisualType.ToUpperInvariant())
            {
                case "TABLE":
                    RenderTable(col, v);
                    break;
                case "CARD":
                    RenderCard(col, v);
                    break;
                case "TEXT":
                    RenderText(col, v);
                    break;
                case "SLICER":
                    col.Item().PaddingTop(4).Text("[Slicer — interactive only]")
                        .FontColor(Colors.Grey.Darken1).Italic();
                    break;
                default:
                    RenderChart(col, v);
                    break;
            }
        }

        private void RenderChart(ColumnDescriptor col, VisualManifest v)
        {
            var svgStr = _svg.Render(v);
            if (svgStr != null)
            {
                // Scale SVG (native 600×350) to fit A4 content width (~500pt)
                col.Item().PaddingTop(8).Width(500).Height(292).Svg(svgStr);
            }
            else if (v.Rows.Count > 0)
            {
                RenderTable(col, v);
            }
            else
            {
                col.Item().PaddingTop(4).Text("No data").Italic().FontColor(Colors.Grey.Medium);
            }
        }

        private static void RenderTable(ColumnDescriptor col, VisualManifest v)
        {
            if (v.Columns.Count == 0)
            {
                col.Item().PaddingTop(4).Text("No data").Italic().FontColor(Colors.Grey.Medium);
                return;
            }

            int cap = Math.Min(v.Rows.Count, 500);

            col.Item().PaddingTop(8).Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    foreach (var _ in v.Columns)
                        cols.RelativeColumn();
                });

                table.Header(header =>
                {
                    foreach (var colName in v.Columns)
                    {
                        header.Cell()
                            .Background(Colors.Grey.Lighten3)
                            .Padding(3)
                            .Text(colName).Bold().FontSize(9);
                    }
                });

                for (int i = 0; i < cap; i++)
                {
                    var row = v.Rows[i];
                    for (int ci = 0; ci < v.Columns.Count; ci++)
                    {
                        string cell = ci < row.Count ? row[ci] ?? "" : "";
                        table.Cell()
                            .BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2)
                            .Padding(3)
                            .Text(cell).FontSize(9);
                    }
                }

                if (v.Rows.Count > cap)
                {
                    table.Cell()
                        .ColumnSpan((uint)v.Columns.Count)
                        .Padding(3)
                        .Text($"… {v.Rows.Count - cap:N0} more rows not shown")
                        .Italic().FontSize(8).FontColor(Colors.Grey.Darken1);
                }
            });
        }

        private static void RenderCard(ColumnDescriptor col, VisualManifest v)
        {
            if (v.Rows.Count > 0 && v.Rows[0].Count > 0)
            {
                var label = v.Columns.Count > 0 ? v.Columns[0] : v.Name;
                var value = v.Rows[0][0] ?? "";
                col.Item().PaddingTop(8).Column(inner =>
                {
                    inner.Item().Text(label).FontSize(9).FontColor(Colors.Grey.Darken1);
                    inner.Item().Text(value).FontSize(22).Bold();
                });
            }
            else
            {
                col.Item().PaddingTop(4).Text("No data").Italic().FontColor(Colors.Grey.Medium);
            }
        }

        private static void RenderText(ColumnDescriptor col, VisualManifest v)
        {
            v.Options.TryGetValue("VALUE", out var textContent);
            if (!string.IsNullOrWhiteSpace(textContent))
                col.Item().PaddingTop(8).Text(textContent).FontSize(10);
        }
    }
}
