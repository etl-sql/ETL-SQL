using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Formatting;
using ETL_SQL.Core.Parser;
using ETL_SQL.Reporting;
using PdfSharp.Pdf.IO;
using Xunit;

namespace ETL_SQL.Tests.Reporting;

/// <summary>
/// Page headers and footers: a TEXT or IMAGE visual marked <c>PRINT_LAYOUT (BAND = HEADER|FOOTER)</c>
/// prints on every physical page, and its text resolves page numbers, the date, the title, report
/// parameters, and its own data.
///
/// <para>Before this, the Studio helper wrote an ordinary visual with <c>{{PAGE}} of {{PAGES}}</c> in
/// it. It printed once, where it sat in the layout, and the tokens printed literally: nothing in the
/// engine had ever resolved them.</para>
/// </summary>
[Trait("Category", "Reporting")]
public sealed class PageBandTests
{
    // ── The language ─────────────────────────────────────────────────────────

    private static CreateVisualStatement ParseVisual(string script)
    {
        var parser = new Parser(new Lexer(script).Tokenize());
        return Assert.IsType<CreateVisualStatement>(parser.ParseStatement());
    }

    [Theory]
    [InlineData("HEADER", "HEADER")]
    [InlineData("FOOTER", "FOOTER")]
    [InlineData("'footer'", "FOOTER")]
    public void ATextVisualCanBeABand(string written, string band)
    {
        var visual = ParseVisual($"CREATE VISUAL page_no AS TEXT (CONTENT = 'Page {{{{PAGE}}}}', PRINT_LAYOUT (BAND = {written}));");

        Assert.Equal(band, visual.PrintLayout?.Band);
    }

    [Fact]
    public void AnImageVisualCanBeABand()
    {
        var visual = ParseVisual("CREATE VISUAL logo AS IMAGE (OPTIONS (SRC = 'data:image/png;base64,AA=='), PRINT_LAYOUT (BAND = HEADER));");

        Assert.Equal("HEADER", visual.PrintLayout?.Band);
    }

    /// <summary>The formatter is the first thing an author runs; a band it dropped would print in the body.</summary>
    [Fact]
    public void ABandSurvivesTheFormatter()
    {
        const string script = "CREATE VISUAL page_no AS TEXT (CONTENT = 'x', PRINT_LAYOUT (KEEP_TOGETHER = ON, BAND = FOOTER));";

        var formatted = SqlFormatter.Format(script, new FormatterOptions());

        Assert.Contains("BAND = FOOTER", formatted, System.StringComparison.Ordinal);
        Assert.Equal("FOOTER", ParseVisual(formatted).PrintLayout?.Band);
    }

    [Fact]
    public void OnlyAHeaderOrAFooterIsABand()
    {
        var error = Assert.ThrowsAny<SyntaxException>(() =>
            ParseVisual("CREATE VISUAL x AS TEXT (CONTENT = 'x', PRINT_LAYOUT (BAND = SIDEBAR));"));

        Assert.Contains("HEADER or FOOTER", error.Message, System.StringComparison.Ordinal);
    }

    /// <summary>A chart in a page header would repeat a whole chart on every sheet; only text and images are bands.</summary>
    [Fact]
    public void OnlyTextAndImageVisualsAreBands()
    {
        var error = Assert.ThrowsAny<SyntaxException>(() =>
            ParseVisual("CREATE VISUAL total AS CARD (SOURCE = #t, MAPPINGS (VALUE = Amount), PRINT_LAYOUT (BAND = HEADER));"));

        Assert.Contains("TEXT or IMAGE", error.Message, System.StringComparison.Ordinal);
    }

    // ── The text ─────────────────────────────────────────────────────────────

    private static ReportManifest Manifest() => new()
    {
        Title = "Order detail",
        Source = "orders.rptsql",
        BuiltAt = new System.DateTime(2026, 9, 29, 14, 5, 0, System.DateTimeKind.Utc),
        Parameters = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase) { ["Region"] = "West" },
    };

    private static VisualManifest Band(string name, string content, string band, List<string>? columns = null, List<List<string?>>? rows = null) => new()
    {
        Name = name,
        VisualType = "TEXT",
        Options = new Dictionary<string, string> { ["CONTENT"] = content },
        PrintLayout = new PrintLayoutOverrideManifest { Band = band },
        Columns = columns ?? [],
        Rows = rows ?? [],
    };

    [Fact]
    public void TheTemplateResolvesEverythingButThePageNumbers()
    {
        var band = Band("h", "{{TITLE}} · {{@Region}} · {{CURRENT_DATE}} · {Rep} · {Total FORMAT 'N2'} · Page {{PAGE}} of {{PAGES}}",
            "HEADER", ["Rep", "Total"], [["Ann", "1234.5"]]);

        var text = ReportTextTemplate.Render(ReportVisualContent.ResolveTextContent(band)!, band, Manifest(), page: 2, pages: 5);

        Assert.Equal("Order detail · West · 2026-09-29 · Ann · 1,234.50 · Page 2 of 5", text);
    }

    [Fact]
    public void ThePageNumbersAreLeftAsFieldsForThePrinter()
    {
        var band = Band("f", "Page {{PAGE}} of {{PAGES}}", "FOOTER");

        var segments = ReportTextTemplate.Segments("Page {{PAGE}} of {{PAGES}}", band, Manifest());

        Assert.Equal(
            [
                new ReportTextSegment(ReportTextSegmentKind.Text, "Page "),
                new ReportTextSegment(ReportTextSegmentKind.PageNumber, string.Empty),
                new ReportTextSegment(ReportTextSegmentKind.Text, " of "),
                new ReportTextSegment(ReportTextSegmentKind.PageCount, string.Empty),
            ],
            segments);
    }

    /// <summary>A token the report cannot fill stays visible, so the author sees the typo rather than a gap.</summary>
    [Fact]
    public void AnUnknownTokenIsLeftAsWritten()
    {
        var band = Band("h", "{{@NoSuchParam}} {Missing} {{NOT_A_TOKEN}}", "HEADER");

        Assert.Equal("{{@NoSuchParam}} {Missing} {{NOT_A_TOKEN}}",
            ReportTextTemplate.Render("{{@NoSuchParam}} {Missing} {{NOT_A_TOKEN}}", band, Manifest(), 1, 1));
    }

    // ── The printed pages ────────────────────────────────────────────────────

    private static VisualManifest DetailTable(int rows) => new()
    {
        Name = "orders",
        VisualType = "TABLE",
        Columns = ["Territory", "Reference", "Amount"],
        Rows = Enumerable.Range(1, rows).Select(row => new List<string?> { $"Zone {row % 4}", $"SO-{row:0000}", $"{row * 37}.00" }).ToList(),
    };

    private static ReportManifest Paginated(params VisualManifest[] visuals)
    {
        var manifest = Manifest();
        var page = new PageManifest
        {
            Name = "Detail",
            Mode = "PAGINATED",
            Structure = string.Join(" / ", visuals.Select((_, index) => (char)('A' + index))),
            PrintLayout = new PageLayoutDefinitionManifest { PageSize = "Letter", Orientation = "PORTRAIT" },
        };
        for (var index = 0; index < visuals.Length; index++)
            page.SlotMap[((char)('A' + index)).ToString()] = visuals[index].Name;
        manifest.Visuals = visuals.ToList();
        manifest.Pages = [page];
        return manifest;
    }

    private static async Task<List<string>> PrintedPages(ReportManifest manifest)
    {
        var pdf = await new ReportPdfExporter().ExportAsync(manifest, PdfExportOptions.Static);
        using var stream = new MemoryStream(pdf);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        return Enumerable.Range(0, document.PageCount).Select(index => PageText(document.Pages[index])).ToList();
    }

    /// <summary>The strings drawn on a page, whitespace collapsed. Same shallow read as PaginatedPdfExportTests.</summary>
    private static string PageText(PdfSharp.Pdf.PdfPage page)
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

    [Fact]
    public async Task AHeaderAndAFooterPrintOnEveryPageWithItsNumber()
    {
        var pages = await PrintedPages(Paginated(
            Band("report_header", "Orders for {{@Region}}", "HEADER"),
            DetailTable(400),
            Band("page_footer", "Page {{PAGE}} of {{PAGES}}", "FOOTER")));

        Assert.True(pages.Count > 1, "The detail table should run to several pages.");
        for (var index = 0; index < pages.Count; index++)
        {
            Assert.Contains("Orders for West", pages[index], System.StringComparison.Ordinal);
            Assert.Matches($@"Page ?{index + 1} ?of ?{pages.Count}", pages[index]);
            Assert.DoesNotContain("{{", pages[index], System.StringComparison.Ordinal);
        }
    }

    /// <summary>A band is furniture, not content: it is not also printed once in the body, heading and all.</summary>
    [Fact]
    public async Task ABandIsNotAlsoPrintedInTheBody()
    {
        var pages = await PrintedPages(Paginated(
            Band("report_header", "Orders for {{@Region}}", "HEADER"),
            DetailTable(5)));

        Assert.Single(pages);
        Assert.Single(Regex.Matches(pages[0], "Orders for West"));
        Assert.DoesNotContain("report_header", pages[0], System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADeclaredFooterReplacesTheDefaultOne()
    {
        var declared = await PrintedPages(Paginated(DetailTable(5), Band("page_footer", "Confidential", "FOOTER")));
        var undeclared = await PrintedPages(Paginated(DetailTable(5)));

        Assert.DoesNotContain("Generated:", declared[0], System.StringComparison.Ordinal);
        Assert.Contains("Confidential", declared[0], System.StringComparison.Ordinal);
        Assert.Contains("Generated:", undeclared[0], System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnImageBandPrintsOnEveryPage()
    {
        // A one-pixel PNG.
        const string png = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=";
        var logo = new VisualManifest
        {
            Name = "logo",
            VisualType = "IMAGE",
            Options = new Dictionary<string, string> { ["SRC"] = png },
            PrintLayout = new PrintLayoutOverrideManifest { Band = "HEADER" },
        };

        var pdf = await new ReportPdfExporter().ExportAsync(Paginated(logo, DetailTable(400)), PdfExportOptions.Static);
        using var stream = new MemoryStream(pdf);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);

        Assert.True(document.PageCount > 1);
        foreach (var page in document.Pages)
        {
            var images = page.Resources.Elements.GetDictionary("/XObject");
            Assert.True(images is { Elements.Count: > 0 }, "A page is missing the header image.");
        }
    }

    /// <summary>
    /// A TEXT visual's <c>{Column}</c> template was only ever filled in by the browser. On paper the
    /// braces printed as written, so the same report read differently printed than on screen.
    /// </summary>
    [Fact]
    public async Task ATextTemplateIsFilledInOnPaperToo()
    {
        var summary = new VisualManifest
        {
            Name = "summary",
            VisualType = "TEXT",
            Options = new Dictionary<string, string> { ["CONTENT"] = "Top rep: {Rep}" },
            Columns = ["Rep"],
            Rows = [["Ann"]],
        };

        var pages = await PrintedPages(Paginated(summary));

        Assert.Contains("Top rep: Ann", pages[0], System.StringComparison.Ordinal);
    }

    // ── The page preview ─────────────────────────────────────────────────────

    [Fact]
    public void ThePhysicalPagesCarryTheirBandsAndLeaveThemOutOfTheBody()
    {
        var manifest = Paginated(
            Band("report_header", "Orders for {{@Region}}", "HEADER"),
            DetailTable(400),
            Band("page_footer", "Page {{PAGE}} of {{PAGES}}", "FOOTER"));

        var pages = new PhysicalPageCompiler().Compile(manifest.Pages[0], manifest);

        Assert.True(pages.Count > 1);
        Assert.All(pages, page => Assert.DoesNotContain(page.Visuals, placed => placed.Visual.PrintLayout?.Band is not null));
        for (var index = 0; index < pages.Count; index++)
        {
            Assert.Equal(["Orders for West"], pages[index].Header);
            Assert.Equal([$"Page {index + 1} of {pages.Count}"], pages[index].Footer);
        }
    }
}
