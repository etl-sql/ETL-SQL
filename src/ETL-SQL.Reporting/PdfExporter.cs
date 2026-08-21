using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;
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
        private static readonly Color _greyDark2 = Color.FromRgb(0x61, 0x61, 0x61);
        private static readonly Color _greyDark1 = Color.FromRgb(0x75, 0x75, 0x75);
        private static readonly Color _greyLight3 = Color.FromRgb(0xF5, 0xF5, 0xF5);
        private static readonly Color _greyLight2 = Color.FromRgb(0xEE, 0xEE, 0xEE);
        private static readonly Color _greyMedium = Color.FromRgb(0x9E, 0x9E, 0x9E);
        private static readonly Color _redDark2 = Color.FromRgb(0xC6, 0x28, 0x28);

        private const double ContentWidthPt = 500.0;
        private const int SvgNativeWidth = 600;
        private const int SvgNativeHeight = 350;
        private static readonly object _fontInitLock = new();
        private static bool _fontsInitialized;

        private readonly SvgChartRenderer _svg = new();

        public byte[] Export(ReportManifest manifest)
            => ExportAsync(manifest).GetAwaiter().GetResult();

        public async Task<byte[]> ExportAsync(ReportManifest manifest, CancellationToken cancellationToken = default)
        {
            EnsureFontsInitialized();

            var tempFiles = new List<string>();
            try
            {
                var document = await BuildDocumentAsync(manifest, tempFiles, cancellationToken);
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

        private static void EnsureFontsInitialized()
        {
            if (_fontsInitialized)
                return;

            lock (_fontInitLock)
            {
                if (_fontsInitialized)
                    return;

                // PDFsharp resolves fonts by name. On Linux containers the Windows
                // font names it expects ("Arial", plus the "Courier New" predefined
                // error font) don't exist, so register a resolver that maps every
                // requested face to an available OS sans-serif TrueType file. This
                // makes PDF export work with no Microsoft fonts installed.
                GlobalFontSettings.FontResolver ??= new ReportFontResolver();

                _fontsInitialized = true;
            }
        }

        /// <summary>
        /// Maps every requested family/face to an available sans-serif TrueType file
        /// (DejaVu Sans on Linux, Arial on Windows) so PDF export needs no MS fonts.
        /// </summary>
        private sealed class ReportFontResolver : IFontResolver
        {
            private const string Regular = "report-sans";
            private const string Bold = "report-sans-bold";

            private static readonly string[] RegularCandidates =
            {
                "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
                @"C:\Windows\Fonts\arial.ttf",
                "/Library/Fonts/Arial.ttf",
                "/System/Library/Fonts/Supplemental/Arial.ttf",
            };

            private static readonly string[] BoldCandidates =
            {
                "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
                @"C:\Windows\Fonts\arialbd.ttf",
                "/Library/Fonts/Arial Bold.ttf",
                "/System/Library/Fonts/Supplemental/Arial Bold.ttf",
            };

            private static readonly Dictionary<string, byte[]> _fontCache = new();
            private static readonly object _cacheLock = new();

            public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
                => new FontResolverInfo(isBold ? Bold : Regular);

            public byte[] GetFont(string faceName)
            {
                lock (_cacheLock)
                {
                    if (_fontCache.TryGetValue(faceName, out var cached))
                        return cached;

                    var candidates = faceName == Bold ? BoldCandidates : RegularCandidates;
                    foreach (var path in candidates)
                    {
                        if (File.Exists(path))
                        {
                            var bytes = ReadFontBytes(path);
                            _fontCache[faceName] = bytes;
                            return bytes;
                        }
                    }

                    throw new InvalidOperationException(
                        "No usable TrueType font found for PDF export. Install a base font " +
                        "(e.g. 'fonts-dejavu-core' on Linux).");
                }
            }

            private static byte[] ReadFontBytes(string path)
            {
                return File.ReadAllBytes(path);
            }
        }

        private async Task<Document> BuildDocumentAsync(ReportManifest manifest, List<string> tempFiles, CancellationToken cancellationToken)
        {
            var document = new Document();
            var style = document.Styles["Normal"]!;
            style.Font.Name = "Arial";
            style.Font.Size = Unit.FromPoint(10);

            var section = document.AddSection();
            ApplyPageSetup(section, null, manifest);

            // ── Report header ─────────────────────────────────────────────────
            var titlePara = section.AddParagraph(
                manifest.Title ?? Path.GetFileNameWithoutExtension(manifest.Source));
            titlePara.Format.Font.Size = Unit.FromPoint(20);
            titlePara.Format.Font.Bold = true;

            if (!string.IsNullOrWhiteSpace(manifest.Description))
            {
                var descPara = section.AddParagraph(manifest.Description);
                descPara.Format.SpaceBefore = Unit.FromPoint(4);
                descPara.Format.Font.Color = _greyDark2;
            }

            if (manifest.Parameters.Count > 0)
            {
                var paramPara = section.AddParagraph();
                paramPara.Format.SpaceBefore = Unit.FromPoint(8);
                paramPara.Format.Font.Size = Unit.FromPoint(9);
                paramPara.Format.Font.Color = _greyDark1;
                paramPara.AddFormattedText("Export State / Active Filters:", TextFormat.Bold);
                paramPara.AddLineBreak();

                foreach (var (key, value) in manifest.Parameters)
                {
                    paramPara.AddText($"• {key} = {value}");
                    paramPara.AddLineBreak();
                }
            }

            var sep = section.AddParagraph();
            sep.Format.SpaceBefore = Unit.FromPoint(10);
            sep.Format.SpaceAfter = Unit.FromPoint(10);
            sep.Format.Borders.Bottom.Width = Unit.FromPoint(1);
            sep.Format.Borders.Bottom.Color = _greyLight2;

            // ── Visuals ───────────────────────────────────────────────────────
            if (manifest.Pages.Count > 0)
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var page in manifest.Pages)
                {
                    var pageSection = document.AddSection();
                    ApplyPageSetup(pageSection, page.PrintLayout, manifest);

                    foreach (var (_, vName) in page.SlotMap.OrderBy(kv => kv.Key))
                    {
                        if (!seen.Add(vName)) continue;
                        var v = manifest.Visuals.FirstOrDefault(x => string.Equals(x.Name, vName, StringComparison.OrdinalIgnoreCase));
                        if (v != null)
                            await RenderVisualAsync(pageSection, v, manifest, tempFiles, cancellationToken);
                    }
                }

                // Any loose visuals
                foreach (var v in manifest.Visuals)
                {
                    if (seen.Add(v.Name))
                        await RenderVisualAsync(section, v, manifest, tempFiles, cancellationToken);
                }
            }
            else
            {
                foreach (var visual in manifest.Visuals)
                    await RenderVisualAsync(section, visual, manifest, tempFiles, cancellationToken);
            }

            return document;
        }

        private static void ApplyPageSetup(Section section, PageLayoutDefinitionManifest? layout, ReportManifest manifest)
        {
            if (layout == null)
            {
                section.PageSetup.PageFormat = PageFormat.A4;
                section.PageSetup.TopMargin = Unit.FromPoint(36);
                section.PageSetup.BottomMargin = Unit.FromPoint(36);
                section.PageSetup.LeftMargin = Unit.FromPoint(36);
                section.PageSetup.RightMargin = Unit.FromPoint(36);
                AddFooter(section, manifest);
                return;
            }

            if (string.Equals(layout.PageSize, "Letter", StringComparison.OrdinalIgnoreCase))
                section.PageSetup.PageFormat = PageFormat.Letter;
            else if (string.Equals(layout.PageSize, "Legal", StringComparison.OrdinalIgnoreCase))
                section.PageSetup.PageFormat = PageFormat.Legal;
            else if (string.Equals(layout.PageSize, "A3", StringComparison.OrdinalIgnoreCase))
                section.PageSetup.PageFormat = PageFormat.A3;
            else
                section.PageSetup.PageFormat = PageFormat.A4;

            if (string.Equals(layout.Orientation, "Landscape", StringComparison.OrdinalIgnoreCase))
                section.PageSetup.Orientation = Orientation.Landscape;

            if (layout.MarginTop.HasValue) section.PageSetup.TopMargin = Unit.FromInch((double)layout.MarginTop.Value);
            if (layout.MarginBottom.HasValue) section.PageSetup.BottomMargin = Unit.FromInch((double)layout.MarginBottom.Value);
            if (layout.MarginLeft.HasValue) section.PageSetup.LeftMargin = Unit.FromInch((double)layout.MarginLeft.Value);
            if (layout.MarginRight.HasValue) section.PageSetup.RightMargin = Unit.FromInch((double)layout.MarginRight.Value);

            AddFooter(section, manifest);
        }

        private static void AddFooter(Section section, ReportManifest manifest)
        {
            var footer = section.Footers.Primary;
            var para = footer.AddParagraph();
            para.Format.Alignment = ParagraphAlignment.Center;
            para.Format.Font.Size = Unit.FromPoint(8);
            para.Format.Font.Color = _greyDark1;

            para.AddText($"Generated: {manifest.BuiltAt:yyyy-MM-dd HH:mm} UTC   |   Page ");
            para.AddPageField();
            para.AddText(" of ");
            para.AddNumPagesField();
        }



        private async Task RenderVisualAsync(Section section, VisualManifest v, ReportManifest manifest, List<string> tempFiles, CancellationToken cancellationToken)
        {
            if (v.PrintLayout?.ExcludeFromPrint == true) return;

            var heading = section.AddParagraph(v.Name);
            if (v.PrintLayout?.PageBreakBefore == true)
                heading.Format.PageBreakBefore = true;
            heading.Format.SpaceBefore = Unit.FromPoint(16);
            heading.Format.Font.Size = Unit.FromPoint(13);
            heading.Format.Font.Bold = true;

            if (v.Error != null)
            {
                var errPara = section.AddParagraph($"Error: {v.Error}");
                errPara.Format.SpaceBefore = Unit.FromPoint(4);
                errPara.Format.Font.Color = _redDark2;
                return;
            }

            switch (v.VisualType.ToUpperInvariant())
            {
                case "TABLE": RenderTable(section, v); break;
                case "CARD": RenderCard(section, v); break;
                case "TEXT": RenderText(section, v); break;
                case "IMAGE": await RenderImageAsync(section, v, tempFiles, cancellationToken); break;

                // Filter/input controls: render the selection that was in effect at
                // export time, so the reader knows how the report was filtered.
                case "SLICER":
                case "MULTISELECT":
                case "DATEPICKER":
                case "RELDATEPICKER":
                case "SLIDER":
                case "SEARCH":
                case "NUMBERBOX":
                case "CHECKBOX":
                case "DROPDOWN":
                    RenderFilter(section, v, manifest);
                    break;

                default:
                    await RenderChartAsync(section, v, tempFiles, cancellationToken);
                    break;
            }

            if (v.PrintLayout?.PageBreakAfter == true)
            {
                var brPara = section.AddParagraph();
                brPara.Format.PageBreakBefore = true;
            }
        }

        private static void RenderFilter(Section section, VisualManifest v, ReportManifest manifest)
        {
            // Parameter name: a SET_PARAMETER action, else an options key.
            var display = ReportVisualContent.ResolveFilterDisplay(v, manifest);

            var p = section.AddParagraph();
            p.Format.SpaceBefore = Unit.FromPoint(4);
            var kicker = p.AddFormattedText($"{v.VisualType.ToLowerInvariant()} filter — selected: ", TextFormat.Italic);
            kicker.Color = _greyDark1;
            p.AddFormattedText(display, TextFormat.Bold);
        }

        private async Task RenderChartAsync(Section section, VisualManifest v, List<string> tempFiles, CancellationToken cancellationToken)
        {
            // Migrated visuals render from PlotPlan without loading server-side V8. Non-migrated
            // visuals retain the compatibility SSR path until their capability-matrix phase.
            var svgStr = UsesNativePlotPlanRendering(v)
                ? _svg.Render(v)
                : await EChartsSsrRenderer.Shared.RenderSvgAsync(v, cancellationToken: cancellationToken) ?? _svg.Render(v);
            if (svgStr != null)
            {
                var png = SvgToPng(svgStr);
                if (png.Length > 0)
                {
                    var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png");
                    await File.WriteAllBytesAsync(tmp, png, cancellationToken);
                    tempFiles.Add(tmp);
                    var img = section.AddImage(tmp);
                    img.Width = Unit.FromPoint(ContentWidthPt);
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
                nd.Format.Font.Color = _greyMedium;
            }
        }

        internal static bool UsesNativePlotPlanRendering(VisualManifest visual) => visual.PlotPlan is not null;

        private static void RenderTable(Section section, VisualManifest v)
        {
            if (v.Columns.Count == 0)
            {
                var nd = section.AddParagraph("No data");
                nd.Format.SpaceBefore = Unit.FromPoint(4);
                nd.Format.Font.Italic = true;
                nd.Format.Font.Color = _greyMedium;
                return;
            }

            section.AddParagraph(); // visual gap before table

            int cap = v.Rows.Count;

            // Size columns proportionally to their content so wide text columns get
            // more room and short ones (ids, codes) don't force everything to wrap.
            // Weights are clamped so a single very-long column can't starve the rest.
            var weights = new double[v.Columns.Count];
            int sample = Math.Min(cap, 50);
            for (int ci = 0; ci < v.Columns.Count; ci++)
            {
                int maxLen = (v.Columns[ci] ?? "").Length;
                for (int i = 0; i < sample; i++)
                {
                    var row = v.Rows[i];
                    if (ci < row.Count) maxLen = Math.Max(maxLen, FormatCell(row[ci]).Length);
                }
                weights[ci] = Math.Clamp(maxLen, 4, 40);
            }
            double totalWeight = weights.Sum();

            var table = section.AddTable();

            if (v.PrintLayout?.KeepTogether == true)
                table.KeepTogether = true;

            for (int ci = 0; ci < v.Columns.Count; ci++)
                table.AddColumn(Unit.FromPoint(ContentWidthPt * weights[ci] / totalWeight));

            var header = table.AddRow();
            header.HeadingFormat = true; // Repeat header on new pages
            header.Shading.Color = _greyLight3;
            for (int ci = 0; ci < v.Columns.Count; ci++)
            {
                var p = header.Cells[ci].AddParagraph(v.Columns[ci]);
                p.Format.Font.Bold = true;
                p.Format.Font.Size = Unit.FromPoint(9);
            }

            for (int i = 0; i < cap; i++)
            {
                var row = v.Rows[i];
                var dRow = table.AddRow();
                dRow.Borders.Bottom.Width = Unit.FromPoint(0.5);
                dRow.Borders.Bottom.Color = _greyLight2;
                for (int ci = 0; ci < v.Columns.Count; ci++)
                {
                    var text = FormatCell(ci < row.Count ? row[ci] : "");
                    dRow.Cells[ci].AddParagraph(text).Format.Font.Size = Unit.FromPoint(9);
                }
            }

        }

        private static string FormatCell(string? raw) => ReportCellFormatter.FormatCellForPdf(raw);

        private static void RenderCard(Section section, VisualManifest v)
        {
            if (v.Rows.Count > 0 && v.Rows[0].Count > 0)
            {
                var label = v.Columns.Count > 0 ? v.Columns[0] : v.Name;
                var value = v.Rows[0][0] ?? "";

                var labelPara = section.AddParagraph(label);
                labelPara.Format.SpaceBefore = Unit.FromPoint(8);
                labelPara.Format.Font.Size = Unit.FromPoint(9);
                labelPara.Format.Font.Color = _greyDark1;

                var valuePara = section.AddParagraph(value);
                valuePara.Format.Font.Size = Unit.FromPoint(22);
                valuePara.Format.Font.Bold = true;
            }
            else
            {
                var nd = section.AddParagraph("No data");
                nd.Format.SpaceBefore = Unit.FromPoint(4);
                nd.Format.Font.Italic = true;
                nd.Format.Font.Color = _greyMedium;
            }
        }

        private static void RenderText(Section section, VisualManifest v)
        {
            var textContent = ReportVisualContent.ResolveTextContent(v);
            if (string.IsNullOrWhiteSpace(textContent)) return;

            foreach (var (text, heading) in MarkdownToLines(textContent))
            {
                var p = section.AddParagraph(text);
                p.Format.SpaceBefore = Unit.FromPoint(heading ? 8 : 2);
                p.Format.Font.Size = Unit.FromPoint(heading ? 12 : 10);
                p.Format.Font.Bold = heading;
            }
        }

        // Lightweight markdown → lines for PDF: headings become bold larger lines and
        // bold/code markers are stripped. (Tables render as their raw "| a | b |" rows.)
        private static IEnumerable<(string Text, bool Heading)> MarkdownToLines(string md)
        {
            foreach (var raw in md.Replace("\r\n", "\n").Split('\n'))
            {
                var line = raw.TrimEnd();
                if (line.Length == 0) continue;
                bool heading = line.StartsWith("#", StringComparison.Ordinal);
                if (heading) line = line.TrimStart('#', ' ');
                line = line.Replace("**", "").Replace("`", "");
                yield return (line, heading);
            }
        }

        private static async Task RenderImageAsync(Section section, VisualManifest v, List<string> tempFiles, CancellationToken cancellationToken)
        {
            var src = v.Options.GetValueOrDefault("SRC") ?? v.Options.GetValueOrDefault("src");
            var path = string.IsNullOrWhiteSpace(src) ? null : await DataUriToTempImageAsync(src!, tempFiles, cancellationToken);
            if (path != null)
            {
                var img = section.AddImage(path);
                img.Width = Unit.FromPoint(ContentWidthPt);
                img.LockAspectRatio = true;
            }
            else
            {
                var nd = section.AddParagraph(string.IsNullOrWhiteSpace(src)
                    ? "No image source." : "[Image could not be embedded in the PDF]");
                nd.Format.SpaceBefore = Unit.FromPoint(4);
                nd.Format.Font.Italic = true;
                nd.Format.Font.Color = _greyDark1;
            }
        }

        // Decodes a data: URI to a temp image file (SVG rasterised at native aspect,
        // base64 raster written as-is). Remote URLs are skipped (no network during export).
        private static async Task<string?> DataUriToTempImageAsync(string src, List<string> tempFiles, CancellationToken cancellationToken)
        {
            if (!src.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return null;
            int comma = src.IndexOf(',');
            if (comma < 0) return null;

            var meta = src.Substring(5, comma - 5);
            var payload = src.Substring(comma + 1);
            bool isBase64 = meta.Contains("base64", StringComparison.OrdinalIgnoreCase);
            bool isSvg = meta.Contains("svg", StringComparison.OrdinalIgnoreCase);

            byte[] bytes;
            string ext;
            try
            {
                if (isSvg)
                {
                    var svg = isBase64
                        ? Encoding.UTF8.GetString(Convert.FromBase64String(payload))
                        : Uri.UnescapeDataString(payload);
                    bytes = RasterizeSvg(svg, preserveAspect: true);
                    ext = "png";
                }
                else if (isBase64)
                {
                    bytes = Convert.FromBase64String(payload);
                    ext = meta.Contains("jpeg", StringComparison.OrdinalIgnoreCase)
                       || meta.Contains("jpg", StringComparison.OrdinalIgnoreCase) ? "jpg" : "png";
                }
                else return null;
            }
            catch { return null; }

            if (bytes.Length == 0) return null;
            var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + "." + ext);
            await File.WriteAllBytesAsync(tmp, bytes, cancellationToken);
            tempFiles.Add(tmp);
            return tmp;
        }

        // Charts rasterise into a fixed 600x350 frame (their designed aspect).
        internal static byte[] SvgToPng(string svgContent) => RasterizeSvg(svgContent, preserveAspect: false);

        private static byte[] RasterizeSvg(string svgContent, bool preserveAspect)
        {
            using var svg = new SKSvg();
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(svgContent));
            if (svg.Load(stream) == null) return Array.Empty<byte>();

            var bounds = svg.Picture!.CullRect;

            int outW, outH;
            float scaleX, scaleY;
            if (preserveAspect && bounds.Width > 0 && bounds.Height > 0)
            {
                const float maxW = 1200f; // render at native aspect, capped for size
                float scale = bounds.Width > maxW ? maxW / bounds.Width : 1f;
                outW = Math.Max(1, (int)Math.Ceiling(bounds.Width * scale));
                outH = Math.Max(1, (int)Math.Ceiling(bounds.Height * scale));
                scaleX = scaleY = scale;
            }
            else
            {
                outW = SvgNativeWidth;
                outH = SvgNativeHeight;
                scaleX = bounds.Width > 0 ? SvgNativeWidth / bounds.Width : 1f;
                scaleY = bounds.Height > 0 ? SvgNativeHeight / bounds.Height : 1f;
            }

            var info = new SKImageInfo(outW, outH);
            using var surface = SKSurface.Create(info);
            if (surface == null) return Array.Empty<byte>();

            surface.Canvas.Clear(SKColors.White);
            surface.Canvas.Save();
            surface.Canvas.Scale(scaleX, scaleY);
            surface.Canvas.DrawPicture(svg.Picture);
            surface.Canvas.Restore();

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data?.ToArray() ?? Array.Empty<byte>();
        }
    }
}
