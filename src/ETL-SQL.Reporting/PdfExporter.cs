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
    public class PdfExporter(ETL_SQL.Common.ILogger? logger = null)
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
                var failures = new List<Exception>();
                foreach (var tmp in tempFiles)
                {
                    try { DeleteTemporaryImage(tmp); }
                    catch (Exception ex)
                    {
                        logger?.Warning("PDF image cleanup failed for {Path}: {Error}", tmp, ex.Message);
                        failures.Add(new IOException($"PDF temporary image remains at '{tmp}'.", ETL_SQL.Core.Common.SecretRedactor.RedactException(ex)));
                    }
                }
                if (failures.Count > 0)
                    throw new AggregateException("PDF image cleanup failed; retained files require operator cleanup.", failures);
            }
        }

        protected virtual Task WriteTemporaryImageAsync(string path, byte[] bytes, CancellationToken cancellationToken)
            => File.WriteAllBytesAsync(path, bytes, cancellationToken);

        protected virtual void DeleteTemporaryImage(string path)
        {
            if (File.Exists(path)) File.Delete(path);
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

            // Page bands print in every section's header or footer, never in the body. Images are
            // written to temp files once, here, because every section repeats them.
            var bands = await CollectBandsAsync(manifest, tempFiles, cancellationToken);

            var section = document.AddSection();
            // The heading block shares the first declared page's paper. It used to be laid out with
            // the A4 defaults and then followed by a *new* section per page — so a Letter landscape
            // report exported an A4 portrait sheet carrying nothing but its title, and every page
            // count was one higher than the report described.
            ApplyPageSetup(section, manifest.Pages.FirstOrDefault()?.PrintLayout, manifest, bands);

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
                var first = true;
                foreach (var page in manifest.Pages)
                {
                    // The first declared page continues the section the heading is already on; each
                    // later one opens its own, which is what a second declared page means.
                    var pageSection = section;
                    if (first)
                    {
                        first = false;
                    }
                    else
                    {
                        pageSection = document.AddSection();
                        ApplyPageSetup(pageSection, page.PrintLayout, manifest, bands);
                    }

                    foreach (var (_, vName) in page.SlotMap.OrderBy(kv => kv.Key))
                    {
                        if (!seen.Add(vName)) continue;
                        var v = manifest.Visuals.FirstOrDefault(x => string.Equals(x.Name, vName, StringComparison.OrdinalIgnoreCase));
                        if (v != null && !IsBand(v))
                            await RenderVisualAsync(pageSection, v, manifest, tempFiles, cancellationToken);
                    }
                }

                // Any loose visuals
                foreach (var v in manifest.Visuals)
                {
                    if (seen.Add(v.Name) && !IsBand(v))
                        await RenderVisualAsync(section, v, manifest, tempFiles, cancellationToken);
                }
            }
            else
            {
                foreach (var visual in manifest.Visuals.Where(visual => !IsBand(visual)))
                    await RenderVisualAsync(section, visual, manifest, tempFiles, cancellationToken);
            }

            return document;
        }

        private static bool IsBand(VisualManifest visual) => visual.PrintLayout?.Band is not null;

        /// <summary>A page band ready to print: its visual, and for an image the file it was written to.</summary>
        private sealed record PageBand(VisualManifest Visual, string? ImagePath);

        private sealed record PageBands(IReadOnlyList<PageBand> Headers, IReadOnlyList<PageBand> Footers);

        private async Task<PageBands> CollectBandsAsync(ReportManifest manifest, List<string> tempFiles, CancellationToken cancellationToken)
        {
            var headers = new List<PageBand>();
            var footers = new List<PageBand>();
            foreach (var visual in manifest.Visuals.Where(visual => IsBand(visual) && visual.PrintLayout?.ExcludeFromPrint != true))
            {
                string? image = null;
                if (string.Equals(visual.VisualType, "IMAGE", StringComparison.OrdinalIgnoreCase))
                {
                    var src = visual.Options.GetValueOrDefault("SRC") ?? visual.Options.GetValueOrDefault("src");
                    // An image that cannot be embedded is left out of the band: a placeholder repeated
                    // on every page would be worse than the logo simply being absent.
                    image = string.IsNullOrWhiteSpace(src) ? null : await DataUriToTempImageAsync(src, tempFiles, cancellationToken);
                    if (image is null) continue;
                }

                var band = new PageBand(visual, image);
                if (string.Equals(visual.PrintLayout!.Band, "HEADER", StringComparison.OrdinalIgnoreCase)) headers.Add(band);
                else footers.Add(band);
            }

            return new PageBands(headers, footers);
        }

        /// <summary>
        /// Draws the report's bands into one section's header and footer. A declared footer replaces the
        /// built-in "Generated … Page X of Y" line; a report that declares none keeps it.
        /// </summary>
        private static void AddBands(Section section, ReportManifest manifest, PageBands bands)
        {
            foreach (var band in bands.Headers) AddBand(section.Headers.Primary, band, manifest);
            if (bands.Footers.Count == 0)
            {
                AddFooter(section, manifest);
                return;
            }

            foreach (var band in bands.Footers) AddBand(section.Footers.Primary, band, manifest);
        }

        private static void AddBand(HeaderFooter area, PageBand band, ReportManifest manifest)
        {
            if (band.ImagePath is { } image)
            {
                var picture = area.AddImage(image);
                picture.Height = Unit.FromPoint(BandImageHeightPt);
                picture.LockAspectRatio = true;
                return;
            }

            var text = ReportVisualContent.ResolveTextContent(band.Visual);
            if (string.IsNullOrWhiteSpace(text)) return;

            var para = area.AddParagraph();
            para.Format.Font.Size = Unit.FromPoint(9);
            para.Format.Font.Color = _greyDark1;
            para.Format.Alignment = (band.Visual.Options.GetValueOrDefault("ALIGN") ?? band.Visual.Options.GetValueOrDefault("align"))
                ?.Trim('\'').ToLowerInvariant() switch
            {
                "center" => ParagraphAlignment.Center,
                "right" => ParagraphAlignment.Right,
                _ => ParagraphAlignment.Left,
            };
            AddTemplated(para, ReportTextTemplate.Segments(text.Replace("**", "").Replace("`", ""), band.Visual, manifest));
        }

        /// <summary>Resolved text into a paragraph, with live page fields where the page numbers go.</summary>
        private static void AddTemplated(Paragraph para, IReadOnlyList<ReportTextSegment> segments)
        {
            foreach (var segment in segments)
            {
                switch (segment.Kind)
                {
                    case ReportTextSegmentKind.PageNumber: para.AddPageField(); break;
                    case ReportTextSegmentKind.PageCount: para.AddNumPagesField(); break;
                    default: para.AddText(segment.Text); break;
                }
            }
        }

        private const double BandDistancePt = 18;
        private const double BandImageHeightPt = 28;
        private const double BandTextHeightPt = 13;

        /// <summary>
        /// Keeps the body clear of the bands. MigraDoc draws a header from <c>HeaderDistance</c> and the
        /// body from <c>TopMargin</c>, so a margin smaller than the header overlapped the first line of
        /// every page with it.
        /// </summary>
        private static void ReserveBandSpace(Section section, PageBands bands)
        {
            static double Height(IReadOnlyList<PageBand> list) =>
                list.Sum(band => band.ImagePath is null ? BandTextHeightPt : BandImageHeightPt + 2);

            if (bands.Headers.Count > 0)
            {
                section.PageSetup.HeaderDistance = Unit.FromPoint(BandDistancePt);
                var needed = BandDistancePt + Height(bands.Headers) + 8;
                if (section.PageSetup.TopMargin.Point < needed) section.PageSetup.TopMargin = Unit.FromPoint(needed);
            }

            if (bands.Footers.Count > 0)
            {
                section.PageSetup.FooterDistance = Unit.FromPoint(BandDistancePt);
                var needed = BandDistancePt + Height(bands.Footers) + 8;
                if (section.PageSetup.BottomMargin.Point < needed) section.PageSetup.BottomMargin = Unit.FromPoint(needed);
            }
        }

        private static void ApplyPageSetup(Section section, PageLayoutDefinitionManifest? layout, ReportManifest manifest, PageBands bands)
        {
            if (layout == null)
            {
                section.PageSetup.PageFormat = PageFormat.A4;
                section.PageSetup.TopMargin = Unit.FromPoint(36);
                section.PageSetup.BottomMargin = Unit.FromPoint(36);
                section.PageSetup.LeftMargin = Unit.FromPoint(36);
                section.PageSetup.RightMargin = Unit.FromPoint(36);
                ReserveBandSpace(section, bands);
                AddBands(section, manifest, bands);
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

            ReserveBandSpace(section, bands);
            AddBands(section, manifest, bands);
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
                case "TABLE": await RenderTableAsync(section, v, tempFiles, cancellationToken); break;
                case "CARD": await RenderCardAsync(section, v, tempFiles, cancellationToken); break;
                case "TEXT": RenderText(section, v, manifest); break;
                case "HTML": RenderHtmlFallback(section, v); break;
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

            // A page cannot be hovered, so a detail surface is described rather than
            // expanded — and never in words that imply the interaction is available here.
            var detail = DetailSurfaceProjection.Describe(v.Tooltip);
            if (detail != null)
            {
                var detailPara = section.AddParagraph(detail);
                detailPara.Format.SpaceBefore = Unit.FromPoint(4);
                detailPara.Format.Font.Size = Unit.FromPoint(8);
                detailPara.Format.Font.Italic = true;
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

        private static void RenderHtmlFallback(Section section, VisualManifest visual)
        {
            var paragraph = section.AddParagraph(visual.HtmlFallback
                ?? visual.SemanticFallback?.Summary
                ?? visual.Name);
            paragraph.Format.SpaceBefore = Unit.FromPoint(4);
        }

        private async Task RenderChartAsync(Section section, VisualManifest v, List<string> tempFiles, CancellationToken cancellationToken)
        {
            var svgStr = _svg.Render(v);
            if (svgStr != null)
            {
                var png = SvgToPng(svgStr);
                if (png.Length > 0)
                {
                    var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png");
                    tempFiles.Add(tmp);
                    await WriteTemporaryImageAsync(tmp, png, cancellationToken);
                    var img = section.AddImage(tmp);
                    img.Width = Unit.FromPoint(ContentWidthPt);
                    img.LockAspectRatio = true;
                }
            }
            else if (v.Rows.Count > 0)
            {
                await RenderTableAsync(section, v, tempFiles, cancellationToken);
            }
            else
            {
                var nd = section.AddParagraph("No data");
                nd.Format.SpaceBefore = Unit.FromPoint(4);
                nd.Format.Font.Italic = true;
                nd.Format.Font.Color = _greyMedium;
            }
        }

        internal static bool UsesNativePlotPlanRendering(VisualManifest visual) =>
            visual.PlotPlan is not null || visual.NativeSvg is not null;


        private async Task RenderTableAsync(Section section, VisualManifest v, List<string> tempFiles, CancellationToken cancellationToken)
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
                var meta = v.ColumnMeta != null && ci < v.ColumnMeta.Count ? v.ColumnMeta[ci] : null;
                if (meta?.Width.HasValue == true)
                {
                    weights[ci] = Math.Clamp(meta.Width.Value, 4, 400);
                }
                else
                {
                    int maxLen = (v.Columns[ci] ?? "").Length;
                    for (int i = 0; i < sample; i++)
                    {
                        var row = v.Rows[i];
                        if (ci < row.Count) maxLen = Math.Max(maxLen, FormatCell(row[ci]).Length);
                    }
                    weights[ci] = Math.Clamp(maxLen, 4, 40);
                }
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

            var totalPos = (v.SummaryData?.TotalPosition ?? (v.Options.TryGetValue("TOTAL_POSITION", out var tp) ? tp : "BOTTOM")).ToUpperInvariant();

            void AddSummaryRow()
            {
                if (v.SummaryData?.GrandTotals == null) return;
                var sRow = table.AddRow();
                sRow.Shading.Color = _greyLight2;
                sRow.Borders.Top.Width = Unit.FromPoint(1);
                sRow.Borders.Bottom.Width = Unit.FromPoint(1);
                for (int ci = 0; ci < v.Columns.Count; ci++)
                {
                    var col = v.Columns[ci];
                    var val = v.SummaryData.GrandTotals.TryGetValue(col, out var gt) ? gt : "";
                    var p = sRow.Cells[ci].AddParagraph(FormatCell(val));
                    p.Format.Font.Bold = true;
                    p.Format.Font.Size = Unit.FromPoint(9);
                }
            }

            if (totalPos == "TOP") AddSummaryRow();

            for (int i = 0; i < cap; i++)
            {
                var row = v.Rows[i];
                var dRow = table.AddRow();
                dRow.Borders.Bottom.Width = Unit.FromPoint(0.5);
                dRow.Borders.Bottom.Color = _greyLight2;
                for (int ci = 0; ci < v.Columns.Count; ci++)
                {
                    var micro = v.MicroCharts?.FirstOrDefault(item => item.Role == "table.cell" && item.RowIndex == i && item.ColumnIndex == ci);
                    if (micro is not null)
                    {
                        var imagePath = await WriteMicroChartPngAsync(micro, tempFiles, cancellationToken);
                        if (imagePath is not null)
                        {
                            var image = dRow.Cells[ci].AddImage(imagePath);
                            image.Width = Unit.FromPoint(72);
                            image.LockAspectRatio = true;
                            continue;
                        }
                    }
                    var text = micro?.PlainText ?? FormatCell(ci < row.Count ? row[ci] : "");
                    dRow.Cells[ci].AddParagraph(text).Format.Font.Size = Unit.FromPoint(9);
                }
            }

            if (totalPos == "BOTTOM") AddSummaryRow();
        }

        private static string FormatCell(string? raw) => ReportCellFormatter.FormatCellForPdf(raw);

        private async Task RenderCardAsync(Section section, VisualManifest v, List<string> tempFiles, CancellationToken cancellationToken)
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
                var valueColor = v.RowFontStyles?.FirstOrDefault()
                    ?? v.RowStyles?.FirstOrDefault()
                    ?? (v.Options.TryGetValue("value_color", out var vc) ? vc : null)
                    ?? (v.Styles != null && v.Styles.TryGetValue("VALUE_COLOR", out var sc) ? sc : null);
                var parsedColor = TryParseColor(valueColor);
                if (parsedColor.HasValue)
                {
                    valuePara.Format.Font.Color = parsedColor.Value;
                }
                var micro = v.MicroCharts?.FirstOrDefault(item => item.Role == "card.sparkline");
                if (micro is not null)
                {
                    var imagePath = await WriteMicroChartPngAsync(micro, tempFiles, cancellationToken);
                    if (imagePath is not null)
                    {
                        var image = section.AddImage(imagePath);
                        image.Width = Unit.FromPoint(120);
                        image.LockAspectRatio = true;
                    }
                }
            }
            else
            {
                var nd = section.AddParagraph("No data");
                nd.Format.SpaceBefore = Unit.FromPoint(4);
                nd.Format.Font.Italic = true;
                nd.Format.Font.Color = _greyMedium;
            }
        }

        private async Task<string?> WriteMicroChartPngAsync(MicroChartManifest micro, List<string> tempFiles, CancellationToken cancellationToken)
        {
            var png = SvgToPng(micro.Svg);
            if (png.Length == 0) return null;
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png");
            tempFiles.Add(path);
            await WriteTemporaryImageAsync(path, png, cancellationToken);
            return path;
        }

        private static void RenderText(Section section, VisualManifest v, ReportManifest manifest)
        {
            var textContent = ReportVisualContent.ResolveTextContent(v);
            if (string.IsNullOrWhiteSpace(textContent)) return;

            // The same template the browser fills in, so the page and the screen say the same thing.
            foreach (var (text, heading) in MarkdownToLines(textContent))
            {
                var p = section.AddParagraph();
                AddTemplated(p, ReportTextTemplate.Segments(text, v, manifest));
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

        private async Task RenderImageAsync(Section section, VisualManifest v, List<string> tempFiles, CancellationToken cancellationToken)
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
        private async Task<string?> DataUriToTempImageAsync(string src, List<string> tempFiles, CancellationToken cancellationToken)
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
            tempFiles.Add(tmp);
            await WriteTemporaryImageAsync(tmp, bytes, cancellationToken);
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

        private static Color? TryParseColor(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return null;
            var clean = hex.Trim().Trim('\'', '"').TrimStart('#');
            if (clean.Length == 6 && uint.TryParse(clean, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            {
                return Color.FromRgb((byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));
            }
            if (clean.Length == 3 && uint.TryParse(clean, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
            {
                byte r = (byte)(Convert.ToByte(clean[0].ToString(), 16) * 17);
                byte g = (byte)(Convert.ToByte(clean[1].ToString(), 16) * 17);
                byte b = (byte)(Convert.ToByte(clean[2].ToString(), 16) * 17);
                return Color.FromRgb(r, g, b);
            }
            return null;
        }
    }
}
