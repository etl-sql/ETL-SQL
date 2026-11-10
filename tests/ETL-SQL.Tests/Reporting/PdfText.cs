using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace ETL_SQL.Tests.Reporting;

/// <summary>
/// Reads the literal strings drawn on PDF pages (Tj operands): enough to ask whether text was
/// printed, not where. The renderer can split a run after a hyphen, so "SO-0001" may read back as
/// "SO- 0001"; match with an optional space.
/// </summary>
internal static class PdfText
{
    public static PdfDocument Open(byte[] pdf) => PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);

    public static List<string> Pages(byte[] pdf)
    {
        using var document = Open(pdf);
        return Enumerable.Range(0, document.PageCount).Select(index => Page(document.Pages[index])).ToList();
    }

    /// <summary>Raster images placed on each page (image XObjects in the page resources).</summary>
    public static List<int> ImagesPerPage(byte[] pdf)
    {
        using var document = Open(pdf);
        return Enumerable.Range(0, document.PageCount).Select(index =>
        {
            var xObjects = document.Pages[index].Resources?.Elements.GetDictionary("/XObject");
            if (xObjects is null) return 0;
            return xObjects.Elements.Keys.Count(key =>
                (xObjects.Elements.GetReference(key)?.Value as PdfDictionary ?? xObjects.Elements.GetDictionary(key))
                    ?.Elements.GetName("/Subtype") == "/Image");
        }).ToList();
    }

    public static string Page(PdfPage page)
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
