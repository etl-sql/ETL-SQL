using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ETL_SQL.Reporting;
using PdfSharp.Pdf.IO;
using Xunit;

namespace ETL_SQL.Tests.Reporting;

/// <summary>
/// Proves a paginated report exports as a PDF with the pages it describes.
///
/// <para>Every other PDF test in this repo asserts that the bytes start with <c>%PDF</c> and are
/// longer than a hundred — which a one-page document containing an error message also satisfies.
/// "Multi-page export" is the whole point of a paginated report, so it is measured here: the page
/// count is read back out of the produced file, and an explicit break has to change it.</para>
/// </summary>
[Trait("Category", "Reporting")]
public sealed class PaginatedPdfExportTests
{
    /// <summary>Pages in a produced PDF, read from the file itself rather than inferred.</summary>
    private static int PageCount(byte[] pdf)
    {
        using var stream = new MemoryStream(pdf);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        return document.PageCount;
    }

    /// <summary>
    /// A detail table whose column headings share no word with its data, so a test that looks for a
    /// heading on a page cannot be satisfied by a row that happens to contain the same text.
    /// </summary>
    private static VisualManifest DetailTable(string name, int rows) => new()
    {
        Name = name,
        VisualType = "TABLE",
        Columns = ["Territory", "Reference", "Amount"],
        Rows = Enumerable.Range(1, rows)
            .Select(row => new List<string?> { $"Zone {row % 4}", $"SO-{row:0000}", $"{row * 37}.00" })
            .ToList(),
    };

    /// <summary>
    /// One declared page carrying the visuals, mapped into slots the way the parse reports them —
    /// a visual the page does not place is not on the page, and would not print.
    /// </summary>
    private static ReportManifest PaginatedManifest(IEnumerable<VisualManifest> visuals)
    {
        var list = visuals.ToList();
        var page = new PageManifest
        {
            Name = "Detail",
            Mode = "PAGINATED",
            Structure = string.Join(" ", list.Select((_, index) => (char)('A' + index))),
            PrintLayout = new PageLayoutDefinitionManifest { PageSize = "Letter", Orientation = "PORTRAIT" },
        };
        for (var index = 0; index < list.Count; index++)
            page.SlotMap[((char)('A' + index)).ToString()] = list[index].Name;

        return new ReportManifest
        {
            Title = "Order detail",
            Source = "orders.rptsql",
            Visuals = list,
            Pages = [page],
        };
    }

    /// <summary>
    /// The text drawn on one page, read out of its content stream.
    ///
    /// <para>PDF text lives in <c>(...) Tj</c> and <c>[...] TJ</c> operators inside a stream that is
    /// deflate-compressed, so it is inflated first. This is deliberately shallow — it recovers the
    /// strings, not their positions — which is all that is needed to ask whether a heading was drawn
    /// on a page at all.</para>
    /// </summary>
    private static string PageText(PdfSharp.Pdf.PdfPage page)
    {
        var raw = new List<byte>();
        var contents = page.Contents;
        for (var index = 0; index < contents.Elements.Count; index++)
        {
            var stream = contents.Elements.GetDictionary(index)?.Stream;
            if (stream is null) continue;
            var bytes = stream.Value;
            var filter = stream.Value.Length > 2 && bytes[0] == 0x78;
            if (filter)
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
        return text.ToString();
    }

    [Fact]
    public async Task ATableSplitAcrossPages_RepeatsItsColumnHeadingsOnEachOne()
    {
        // A continued table whose headings stop at the first page break leaves every later page a
        // grid of unlabelled numbers. MigraDoc repeats them only because the header row is marked as
        // one, which nothing asserted.
        var manifest = PaginatedManifest([DetailTable("orders", 400)]);

        var pdf = await new ReportPdfExporter().ExportAsync(manifest, PdfExportOptions.Static);

        using var stream = new MemoryStream(pdf);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        Assert.True(document.PageCount > 1);

        for (var index = 0; index < document.PageCount; index++)
        {
            var text = PageText(document.Pages[index]);
            Assert.True(text.Contains("Territory", StringComparison.Ordinal),
                $"Page {index + 1} of the split table carries no column headings.");
        }
    }

    [Fact]
    public async Task ADetailTableLongerThanOnePage_ExportsAsSeveralPages()
    {
        // 400 rows cannot fit on one Letter page at any plausible row height, so a single-page
        // result would mean rows were dropped rather than flowed.
        var manifest = PaginatedManifest([DetailTable("orders", 400)]);

        var pdf = await new ReportPdfExporter().ExportAsync(manifest, PdfExportOptions.Static);

        Assert.Equal([0x25, 0x50, 0x44, 0x46], pdf[..4]);
        var pages = PageCount(pdf);
        Assert.True(pages > 1, $"A 400-row detail table exported onto {pages} page(s).");
    }

    [Fact]
    public async Task AnExplicitBreakBetweenBands_StartsANewPage()
    {
        // Two short tables fit together on one page; the break is the only reason they would not.
        var together = PaginatedManifest([DetailTable("first", 3), DetailTable("second", 3)]);
        var broken = PaginatedManifest([
            DetailTable("first", 3),
            new VisualManifest
            {
                Name = "second",
                VisualType = "TABLE",
                Columns = ["Territory", "Reference", "Amount"],
                Rows = DetailTable("second", 3).Rows,
                PrintLayout = new PrintLayoutOverrideManifest { PageBreakBefore = true },
            },
        ]);

        var exporter = new ReportPdfExporter();
        var withoutBreak = PageCount(await exporter.ExportAsync(together, PdfExportOptions.Static));
        var withBreak = PageCount(await exporter.ExportAsync(broken, PdfExportOptions.Static));

        Assert.Equal(1, withoutBreak);
        Assert.Equal(2, withBreak);
    }

    [Fact]
    public async Task AVisualExcludedFromPrint_DoesNotReachTheExport()
    {
        var manifest = PaginatedManifest([
            DetailTable("printed", 3),
            new VisualManifest
            {
                Name = "screen_only",
                VisualType = "TABLE",
                Columns = ["Territory"],
                Rows = [["North"]],
                PrintLayout = new PrintLayoutOverrideManifest { ExcludeFromPrint = true, PageBreakBefore = true },
            },
        ]);

        // The excluded visual carries a page break; if it were rendered the document would grow.
        var pdf = await new ReportPdfExporter().ExportAsync(manifest, PdfExportOptions.Static);

        Assert.Equal(1, PageCount(pdf));
    }
}
