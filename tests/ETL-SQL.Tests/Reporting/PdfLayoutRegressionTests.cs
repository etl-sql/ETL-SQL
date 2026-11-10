using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ETL_SQL.Reporting;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace ETL_SQL.Tests.Reporting;

/// <summary>
/// The report layout evidence matrix (scripts/Test-ReportLayoutEvidence.ps1). Each case exports a
/// PDF, reads the claim it is named for back out of the file, and keeps the PDF for review when
/// ETL_SQL_EVIDENCE_DIR is set. A case that only checked the byte count would keep a PDF of the
/// wrong shape as evidence that the shape is right.
/// </summary>
[Trait("Category", "LayoutRegressionEvidence")]
public sealed class PdfLayoutRegressionTests
{
    [Theory]
    [InlineData("Letter", "Portrait", 612.0, 792.0)]
    [InlineData("Letter", "Landscape", 792.0, 612.0)]
    [InlineData("A4", "Portrait", 595.3, 841.9)]
    [InlineData("A4", "Landscape", 841.9, 595.3)]
    public async Task PdfLayout_PageSizeAndOrientation(string pageSize, string orientation, double width, double height)
    {
        var manifest = Paginated(pageSize, orientation, Table("data", 1));

        var pdf = await new PdfExporter().ExportAsync(manifest);

        using var document = Open(pdf);
        var (actualWidth, actualHeight) = EffectiveSize(document.Pages[0]);
        Assert.True(Math.Abs(actualWidth - width) < 1 && Math.Abs(actualHeight - height) < 1,
            $"{pageSize} {orientation} printed as {actualWidth:0.#} x {actualHeight:0.#} pt; expected {width} x {height}.");
        RetainEvidence($"PageSize_{pageSize}_{orientation}", pdf);
    }

    [Fact]
    public async Task PdfLayout_HeaderAndFooterBandsPrintOnEveryPage()
    {
        var manifest = Paginated("Letter", "Portrait",
            Band("page_header", "Quarterly orders", "HEADER"),
            Table("orders", 400),
            Band("page_footer", "Page {{PAGE}} of {{PAGES}}", "FOOTER"));

        var pdf = await new PdfExporter().ExportAsync(manifest);

        using var document = Open(pdf);
        Assert.True(document.PageCount > 1, "The detail table should run to several pages.");
        for (var index = 0; index < document.PageCount; index++)
        {
            var text = PageText(document.Pages[index]);
            Assert.Contains("Quarterly orders", text, StringComparison.Ordinal);
            Assert.Matches($@"Page ?{index + 1} ?of ?{document.PageCount}", text);
        }
        RetainEvidence("HeadersFooters", pdf);
    }

    [Fact]
    public async Task PdfLayout_OversizedTableContinuesOntoLaterPages()
    {
        var manifest = Paginated("Letter", "Portrait", Table("orders", 400));

        var pdf = await new PdfExporter().ExportAsync(manifest);

        using var document = Open(pdf);
        Assert.True(document.PageCount > 1, $"A 400-row table printed on {document.PageCount} page.");
        var pages = Enumerable.Range(0, document.PageCount).Select(index => PageText(document.Pages[index])).ToList();
        // The renderer breaks a run after a hyphen, so the reader can see "SO- 0001".
        Assert.Matches(@"SO- ?0001\b", pages[0]);
        Assert.Matches(@"SO- ?0400\b", pages[^1]);
        RetainEvidence("OversizedContent", pdf);
    }

    [Fact]
    public async Task PdfLayout_MixedScriptTextExports()
    {
        // Glyph coverage for the non-Latin scripts is judged by eye from the retained PDF; the text
        // reader here only decodes the Latin run.
        var manifest = Paginated("A4", "Portrait", new VisualManifest
        {
            Name = "greeting",
            VisualType = "TEXT",
            Options = new Dictionary<string, string> { ["CONTENT"] = "Hello, 你好, こんにちは, مرحبا" },
        });

        var pdf = await new PdfExporter().ExportAsync(manifest);

        using var document = Open(pdf);
        Assert.Equal(1, document.PageCount);
        Assert.Contains("Hello", PageText(document.Pages[0]), StringComparison.Ordinal);
        RetainEvidence("Fonts", pdf);
    }

    private static void RetainEvidence(string scenarioName, byte[] pdfBytes)
    {
        var dir = Environment.GetEnvironmentVariable("ETL_SQL_EVIDENCE_DIR");
        if (string.IsNullOrEmpty(dir)) return; // Only retain when requested by the lane script

        var platform = OperatingSystem.IsWindows() ? "win" : "linux";
        File.WriteAllBytes(Path.Combine(dir, $"{scenarioName}_{platform}.pdf"), pdfBytes);
    }

    private static ReportManifest Paginated(string pageSize, string orientation, params VisualManifest[] visuals)
    {
        var page = new PageManifest
        {
            Name = "Layout",
            Mode = "PAGINATED",
            Structure = string.Join(" / ", visuals.Select((_, index) => (char)('A' + index))),
            PrintLayout = new PageLayoutDefinitionManifest { PageSize = pageSize, Orientation = orientation },
        };
        for (var index = 0; index < visuals.Length; index++)
            page.SlotMap[((char)('A' + index)).ToString()] = visuals[index].Name;

        return new ReportManifest { Title = "Layout evidence", Visuals = visuals.ToList(), Pages = [page] };
    }

    private static VisualManifest Table(string name, int rows) => new()
    {
        Name = name,
        VisualType = "TABLE",
        Columns = ["Territory", "Reference", "Amount"],
        Rows = Enumerable.Range(1, rows).Select(row => new List<string?> { $"Zone {row % 4}", $"SO-{row:0000}", $"{row * 37}.00" }).ToList(),
    };

    private static VisualManifest Band(string name, string content, string band) => new()
    {
        Name = name,
        VisualType = "TEXT",
        Options = new Dictionary<string, string> { ["CONTENT"] = content },
        PrintLayout = new PrintLayoutOverrideManifest { Band = band },
    };

    private static PdfDocument Open(byte[] pdf) => PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);

    /// <summary>The page as a viewer shows it: a page rotated a quarter turn swaps its sides.</summary>
    private static (double Width, double Height) EffectiveSize(PdfPage page)
    {
        var width = page.Width.Point;
        var height = page.Height.Point;
        return page.Rotate % 180 == 90 ? (height, width) : (width, height);
    }

    /// <summary>The literal strings drawn on a page (Tj operands), enough to ask whether text was printed.</summary>
    private static string PageText(PdfPage page)
    {
        var raw = new List<byte>();
        var contents = page.Contents;
        for (var index = 0; index < contents.Elements.Count; index++)
        {
            var stream = contents.Elements.GetDictionary(index)?.Stream;
            if (stream is null) continue;
            var bytes = stream.Value;
            if (bytes.Length > 2 && bytes[0] == 0x78)
            {
                using var input = new MemoryStream(bytes);
                using var inflate = new ZLibStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                inflate.CopyTo(output);
                raw.AddRange(output.ToArray());
            }
            else
            {
                raw.AddRange(bytes);
            }
        }

        var content = Encoding.Latin1.GetString(raw.ToArray());
        var text = new StringBuilder();
        foreach (Match match in Regex.Matches(content, @"\((?<text>(?:\\.|[^\\()])*)\)\s*Tj"))
            text.Append(match.Groups["text"].Value).Append(' ');
        return Regex.Replace(text.ToString(), @"\s+", " ");
    }
}
