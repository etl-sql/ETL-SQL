using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using SkiaSharp;
using Svg.Skia;

namespace ETL_SQL.Reporting
{
    /// <summary>
    /// Exports a <see cref="ReportManifest"/> to a PDF byte array using PDFsharp + MigraDoc.
    /// Charts are rendered as SVG via <see cref="SvgChartRenderer"/> then rasterized to PNG
    /// via Svg.Skia for embedding. No headless browser required.
    /// </summary>
    public class PdfExporter
    {
        private static readonly Color _greyDark2  = Color.FromRgb(0x61, 0x61, 0x61);
        private static readonly Color _greyDark1  = Color.FromRgb(0x75, 0x75, 0x75);
        private static readonly Color _greyLight3 = Color.FromRgb(0xF5, 0xF5, 0xF5);
        private static readonly Color _greyLight2 = Color.FromRgb(0xEE, 0xEE, 0xEE);
        private static readonly Color _greyMedium = Color.FromRgb(0x9E, 0x9E, 0x9E);
        private static readonly Color _redDark2   = Color.FromRgb(0xC6, 0x28, 0x28);

        private const double ContentWidthPt  = 500.0;
        private const int    SvgNativeWidth  = 600;
        private const int    SvgNativeHeight = 350;

        private readonly SvgChartRenderer _svg = new();

        public byte[] Export(ReportManifest manifest)
        {
            var tempFiles = new List<string>();
            try
            {
                var document = BuildDocument(manifest, tempFiles);
                var renderer = new PdfDocumentRenderer { Document = document };
                renderer.RenderDocument();
                using var ms = new MemoryStream();
                renderer.PdfDocument.Save(ms);
                return ms.ToArray();
            }
            finally
            {
                foreach (var tmp in tempFiles)
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best-effort */ }
            }
        }

        private Document BuildDocument(ReportManifest manifest, List<string> tempFiles)
        {
            var document = new Document();
            var style    = document.Styles["Normal"]!;
            style.Font.Name = "Arial";
            style.Font.Size = Unit.FromPoint(10);

            var section = document.AddSection();
            section.PageSetup.PageFormat   = PageFormat.A4;
            section.PageSetup.TopMargin    = Unit.FromPoint(36);
            section.PageSetup.BottomMargin = Unit.FromPoint(36);
            section.PageSetup.LeftMargin   = Unit.FromPoint(36);
            section.PageSetup.RightMargin  = Unit.FromPoint(36);

            // ── Report header ─────────────────────────────────────────────────
            var titlePara = section.AddParagraph(
                manifest.Title ?? Path.GetFileNameWithoutExtension(manifest.Source));
            titlePara.Format.Font.Size = Unit.FromPoint(20);
            titlePara.Format.Font.Bold = true;

            if (!string.IsNullOrWhiteSpace(manifest.Description))
            {
                var descPara = section.AddParagraph(manifest.Description);
                descPara.Format.SpaceBefore = Unit.FromPoint(4);
                descPara.Format.Font.Color  = _greyDark2;
            }

            var tsPara = section.AddParagraph($"Generated: {manifest.BuiltAt:yyyy-MM-dd HH:mm} UTC");
            tsPara.Format.SpaceBefore = Unit.FromPoint(4);
            tsPara.Format.Font.Size   = Unit.FromPoint(8);
            tsPara.Format.Font.Color  = _greyDark1;

            var sep = section.AddParagraph();
            sep.Format.SpaceBefore              = Unit.FromPoint(10);
            sep.Format.SpaceAfter               = Unit.FromPoint(10);
            sep.Format.Borders.Bottom.Width     = Unit.FromPoint(1);
            sep.Format.Borders.Bottom.Color     = _greyLight2;

            // ── Visuals ───────────────────────────────────────────────────────
            foreach (var visual in GetVisualsInOrder(manifest))
                RenderVisual(section, visual, tempFiles);

            return document;
        }

        private static IEnumerable<VisualManifest> GetVisualsInOrder(ReportManifest manifest)
        {
            if (manifest.Pages.Count == 0) return manifest.Visuals;

            var seen   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<VisualManifest>();

            foreach (var page in manifest.Pages)
                foreach (var (_, vName) in page.SlotMap.OrderBy(kv => kv.Key))
                {
                    if (!seen.Add(vName)) continue;
                    var v = manifest.Visuals.FirstOrDefault(x =>
                        string.Equals(x.Name, vName, StringComparison.OrdinalIgnoreCase));
                    if (v != null) result.Add(v);
                }

            foreach (var v in manifest.Visuals)
                if (seen.Add(v.Name)) result.Add(v);

            return result;
        }

        private void RenderVisual(Section section, VisualManifest v, List<string> tempFiles)
        {
            var heading = section.AddParagraph(v.Name);
            heading.Format.SpaceBefore = Unit.FromPoint(16);
            heading.Format.Font.Size   = Unit.FromPoint(13);
            heading.Format.Font.Bold   = true;

            if (v.Error != null)
            {
                var errPara = section.AddParagraph($"Error: {v.Error}");
                errPara.Format.SpaceBefore = Unit.FromPoint(4);
                errPara.Format.Font.Color  = _redDark2;
                return;
            }

            switch (v.VisualType.ToUpperInvariant())
            {
                case "TABLE":  RenderTable(section, v);             break;
                case "CARD":   RenderCard(section, v);              break;
                case "TEXT":   RenderText(section, v);              break;
                case "SLICER":
                    var sp = section.AddParagraph("[Slicer — interactive only]");
                    sp.Format.SpaceBefore = Unit.FromPoint(4);
                    sp.Format.Font.Color  = _greyDark1;
                    sp.Format.Font.Italic = true;
                    break;
                default:
                    RenderChart(section, v, tempFiles);
                    break;
            }
        }

        private void RenderChart(Section section, VisualManifest v, List<string> tempFiles)
        {
            var svgStr = _svg.Render(v);
            if (svgStr != null)
            {
                var png = SvgToPng(svgStr);
                if (png.Length > 0)
                {
                    var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png");
                    File.WriteAllBytes(tmp, png);
                    tempFiles.Add(tmp);
                    var img = section.AddImage(tmp);
                    img.Width           = Unit.FromPoint(ContentWidthPt);
                    img.LockAspectRatio = true;
                }
            }
            else if (v.Rows.Count > 0)
            {
                RenderTable(section, v);
            }
            else
            {
                var nd = section.AddParagraph("No data");
                nd.Format.SpaceBefore = Unit.FromPoint(4);
                nd.Format.Font.Italic = true;
                nd.Format.Font.Color  = _greyMedium;
            }
        }

        private static void RenderTable(Section section, VisualManifest v)
        {
            if (v.Columns.Count == 0)
            {
                var nd = section.AddParagraph("No data");
                nd.Format.SpaceBefore = Unit.FromPoint(4);
                nd.Format.Font.Italic = true;
                nd.Format.Font.Color  = _greyMedium;
                return;
            }

            section.AddParagraph(); // visual gap before table

            int    cap      = Math.Min(v.Rows.Count, 500);
            double colWidth = ContentWidthPt / v.Columns.Count;
            var    table    = section.AddTable();

            foreach (var _ in v.Columns)
                table.AddColumn(Unit.FromPoint(colWidth));

            var header = table.AddRow();
            header.Shading.Color = _greyLight3;
            for (int ci = 0; ci < v.Columns.Count; ci++)
            {
                var p = header.Cells[ci].AddParagraph(v.Columns[ci]);
                p.Format.Font.Bold = true;
                p.Format.Font.Size = Unit.FromPoint(9);
            }

            for (int i = 0; i < cap; i++)
            {
                var row  = v.Rows[i];
                var dRow = table.AddRow();
                dRow.Borders.Bottom.Width = Unit.FromPoint(0.5);
                dRow.Borders.Bottom.Color = _greyLight2;
                for (int ci = 0; ci < v.Columns.Count; ci++)
                {
                    var text = ci < row.Count ? row[ci] ?? "" : "";
                    dRow.Cells[ci].AddParagraph(text).Format.Font.Size = Unit.FromPoint(9);
                }
            }

            if (v.Rows.Count > cap)
            {
                var moreRow = table.AddRow();
                moreRow.Cells[0].MergeRight = v.Columns.Count - 1;
                var p = moreRow.Cells[0].AddParagraph($"… {v.Rows.Count - cap:N0} more rows not shown");
                p.Format.Font.Size   = Unit.FromPoint(8);
                p.Format.Font.Italic = true;
                p.Format.Font.Color  = _greyDark1;
            }
        }

        private static void RenderCard(Section section, VisualManifest v)
        {
            if (v.Rows.Count > 0 && v.Rows[0].Count > 0)
            {
                var label = v.Columns.Count > 0 ? v.Columns[0] : v.Name;
                var value = v.Rows[0][0] ?? "";

                var labelPara = section.AddParagraph(label);
                labelPara.Format.SpaceBefore = Unit.FromPoint(8);
                labelPara.Format.Font.Size   = Unit.FromPoint(9);
                labelPara.Format.Font.Color  = _greyDark1;

                var valuePara = section.AddParagraph(value);
                valuePara.Format.Font.Size = Unit.FromPoint(22);
                valuePara.Format.Font.Bold = true;
            }
            else
            {
                var nd = section.AddParagraph("No data");
                nd.Format.SpaceBefore = Unit.FromPoint(4);
                nd.Format.Font.Italic = true;
                nd.Format.Font.Color  = _greyMedium;
            }
        }

        private static void RenderText(Section section, VisualManifest v)
        {
            v.Options.TryGetValue("VALUE", out var textContent);
            if (!string.IsNullOrWhiteSpace(textContent))
            {
                var p = section.AddParagraph(textContent);
                p.Format.SpaceBefore = Unit.FromPoint(8);
                p.Format.Font.Size   = Unit.FromPoint(10);
            }
        }

        private static byte[] SvgToPng(string svgContent)
        {
            using var svg    = new SKSvg();
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(svgContent));
            if (svg.Load(stream) == null) return Array.Empty<byte>();

            var bounds = svg.Picture!.CullRect;
            float scaleX = bounds.Width  > 0 ? SvgNativeWidth  / bounds.Width  : 1f;
            float scaleY = bounds.Height > 0 ? SvgNativeHeight / bounds.Height : 1f;

            var info = new SKImageInfo(SvgNativeWidth, SvgNativeHeight);
            using var surface = SKSurface.Create(info);
            if (surface == null) return Array.Empty<byte>();

            surface.Canvas.Clear(SKColors.White);
            surface.Canvas.Save();
            surface.Canvas.Scale(scaleX, scaleY);
            surface.Canvas.DrawPicture(svg.Picture);
            surface.Canvas.Restore();

            using var image = surface.Snapshot();
            using var data  = image.Encode(SKEncodedImageFormat.Png, 100);
            return data?.ToArray() ?? Array.Empty<byte>();
        }
    }
}
