using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ETL_SQL.Reporting;

/// <summary>What a piece of resolved text is: text to print, or a page number the printer fills in.</summary>
public enum ReportTextSegmentKind { Text, PageNumber, PageCount }

public sealed record ReportTextSegment(ReportTextSegmentKind Kind, string Text);

/// <summary>
/// Fills in the tokens a TEXT visual's content can carry.
///
/// <list type="bullet">
/// <item><c>{Column}</c> and <c>{Column FORMAT 'N2'}</c> — from the visual's own first row, the same
/// template the browser runtime has always applied. On paper it used to print as written.</item>
/// <item><c>{{CURRENT_DATE}}</c> — the day the report was built, <c>yyyy-MM-dd</c>.</item>
/// <item><c>{{TITLE}}</c> — the report's title.</item>
/// <item><c>{{@Name}}</c> — a report parameter's value.</item>
/// <item><c>{{PAGE}}</c> and <c>{{PAGES}}</c> — this physical page's number and the count. These are
/// left to whoever lays out the pages, because only it knows them.</item>
/// </list>
///
/// <para>A token that cannot be filled in is left as written, so an author sees the typo on the page
/// rather than an unexplained gap.</para>
/// </summary>
public static partial class ReportTextTemplate
{
    private const string PageToken = "{{PAGE}}";
    private const string PagesToken = "{{PAGES}}";

    [GeneratedRegex(@"\{\{\s*(?<name>@?[A-Za-z_][A-Za-z0-9_]*)\s*\}\}|\{(?<column>[A-Za-z0-9_]+)(?:\s+FORMAT\s+['""](?<format>[^'""]+)['""])?\}", RegexOptions.IgnoreCase)]
    private static partial Regex Token();

    [GeneratedRegex(@"\{\{\s*(?<page>PAGES?)\s*\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex PageTokens();

    /// <summary>The text with every token filled in except the page numbers.</summary>
    public static string Resolve(string text, VisualManifest visual, ReportManifest manifest) =>
        Token().Replace(text, match =>
        {
            if (match.Groups["name"].Success)
            {
                var name = match.Groups["name"].Value;
                switch (name.ToUpperInvariant())
                {
                    case "PAGE": return PageToken;
                    case "PAGES": return PagesToken;
                    case "CURRENT_DATE": return manifest.BuiltAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    case "TITLE": return string.IsNullOrWhiteSpace(manifest.Title) ? match.Value : manifest.Title!;
                }

                return name.StartsWith('@') && manifest.Parameters.TryGetValue(name[1..], out var parameter)
                    ? parameter
                    : match.Value;
            }

            var column = visual.Columns.FindIndex(c => string.Equals(c, match.Groups["column"].Value, StringComparison.OrdinalIgnoreCase));
            if (column < 0 || visual.Rows.Count == 0 || column >= visual.Rows[0].Count || visual.Rows[0][column] is not { } raw)
                return match.Value;

            return match.Groups["format"].Success ? Format(raw, match.Groups["format"].Value) : raw;
        });

    /// <summary>The resolved text, split where a page number goes.</summary>
    public static IReadOnlyList<ReportTextSegment> Segments(string text, VisualManifest visual, ReportManifest manifest)
    {
        var resolved = Resolve(text, visual, manifest);
        var segments = new List<ReportTextSegment>();
        var last = 0;
        foreach (Match match in PageTokens().Matches(resolved))
        {
            if (match.Index > last) segments.Add(new ReportTextSegment(ReportTextSegmentKind.Text, resolved[last..match.Index]));
            segments.Add(new ReportTextSegment(
                match.Groups["page"].Value.Length == 5 ? ReportTextSegmentKind.PageCount : ReportTextSegmentKind.PageNumber,
                string.Empty));
            last = match.Index + match.Length;
        }

        if (last < resolved.Length) segments.Add(new ReportTextSegment(ReportTextSegmentKind.Text, resolved[last..]));
        return segments;
    }

    /// <summary>The text as it prints on one page.</summary>
    public static string Render(string text, VisualManifest visual, ReportManifest manifest, int page, int pages) =>
        string.Concat(Segments(text, visual, manifest).Select(segment => segment.Kind switch
        {
            ReportTextSegmentKind.PageNumber => page.ToString(CultureInfo.InvariantCulture),
            ReportTextSegmentKind.PageCount => pages.ToString(CultureInfo.InvariantCulture),
            _ => segment.Text,
        }));

    /// <summary>
    /// A value in a .NET format — <c>N2</c>, <c>C0</c>, <c>P1</c>, or a custom pattern — as the browser's
    /// <c>formatValue</c> does for the standard ones. A value that is not a number or a date prints as it is.
    /// </summary>
    private static string Format(string raw, string format)
    {
        var culture = CultureInfo.GetCultureInfo("en-US");
        if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
            return number.ToString(format, culture);
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date.ToString(format, culture);
        return raw;
    }
}
