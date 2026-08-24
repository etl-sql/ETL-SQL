using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ETL_SQL.Core;

namespace ETL_SQL.LSP;

/// <summary>
/// Shared knowledge of author bookmarks for the editor features that need it: completion of a
/// bookmark identifier where one is expected, hover documentation for a declared bookmark, and
/// rename across every site a bookmark identifier can appear.
///
/// A bookmark identifier appears in exactly three places — <c>CREATE BOOKMARK Name</c>,
/// <c>APPLY_BOOKMARK(Name)</c>, and <c>DROP BOOKMARK Name</c> — which is what makes a complete rename
/// possible from the text alone. The references a bookmark makes to *other* objects (its PAGE, its
/// STATE object names, its parameters) are kept safe by <c>BookmarkValidationRule</c>, which reports a
/// stale reference as a diagnostic the moment the target is renamed or deleted.
/// </summary>
public static class BookmarkSymbols
{
    /// <summary>Every bookmark declared in a parsed script, in source order.</summary>
    public static IReadOnlyList<CreateBookmarkStatement> Declared(Script? script) =>
        script?.Statements.OfType<CreateBookmarkStatement>().ToList() ?? [];

    /// <summary>
    /// True when the text immediately before the cursor is a position that expects a bookmark
    /// identifier, so completion offers the declared bookmarks rather than the generic word list.
    /// </summary>
    public static bool ExpectsBookmarkName(string scriptBefore)
    {
        if (string.IsNullOrEmpty(scriptBefore)) return false;
        // Only the tail matters, and bounding it keeps the scan cheap on large scripts.
        var tail = scriptBefore.Length > 200 ? scriptBefore[^200..] : scriptBefore;
        return Regex.IsMatch(tail, @"APPLY_BOOKMARK\s*\(\s*[A-Za-z0-9_]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            || Regex.IsMatch(tail, @"\bDROP\s+BOOKMARK\s+(?:IF\s+EXISTS\s+)?[A-Za-z0-9_]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>Finds a declared bookmark by name, case-insensitively.</summary>
    public static CreateBookmarkStatement? Find(Script? script, string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? null
            : Declared(script).FirstOrDefault(b => b.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Renders the hover/completion documentation for one bookmark.</summary>
    public static string Describe(CreateBookmarkStatement bookmark)
    {
        var md = new StringBuilder();
        md.Append("#### Bookmark `").Append(bookmark.Name).Append('`');
        if (bookmark.IsDefault) md.Append(" _(report default)_");
        md.AppendLine().AppendLine();

        if (bookmark.Title is not null)
            md.Append("- **Title**: ").AppendLine(bookmark.Title.ToSql());
        if (!string.IsNullOrWhiteSpace(bookmark.PageName))
            md.Append("- **Page**: `").Append(bookmark.PageName).AppendLine("`");

        if (bookmark.Parameters.Count > 0)
        {
            md.AppendLine("- **Parameters**:");
            foreach (var p in bookmark.Parameters)
                md.Append("  - `").Append(p.ParameterName).Append("` = `").Append(p.Value.ToSql()).AppendLine("`");
        }

        if (bookmark.StateEntries.Count > 0)
        {
            md.AppendLine("- **State**:");
            foreach (var s in bookmark.StateEntries)
                md.Append("  - `").Append(s.ObjectKey).Append("` = ").AppendLine(s.On ? "ON" : "OFF");
        }

        md.AppendLine().Append("Apply with `APPLY_BOOKMARK(").Append(bookmark.Name).Append(")`.");
        return md.ToString();
    }

    /// <summary>
    /// Every offset in <paramref name="text"/> at which the identifier of <paramref name="name"/> is
    /// written as a bookmark reference. Matching is anchored to the three declaration/reference forms
    /// so a same-named column, variable, or string literal is never rewritten.
    /// </summary>
    public static List<int> ReferenceOffsets(string text, string name)
    {
        var escaped = Regex.Escape(name);
        string[] patterns =
        [
            $@"\bCREATE\s+BOOKMARK\s+(?<name>{escaped})\b",
            $@"\bAPPLY_BOOKMARK\s*\(\s*(?<name>{escaped})\s*\)",
            $@"\bDROP\s+BOOKMARK\s+(?:IF\s+EXISTS\s+)?(?<name>{escaped})\b"
        ];
        return patterns
            .SelectMany(pattern => Regex.Matches(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                .Select(match => match.Groups["name"].Index))
            .Distinct()
            .Order()
            .ToList();
    }
}
