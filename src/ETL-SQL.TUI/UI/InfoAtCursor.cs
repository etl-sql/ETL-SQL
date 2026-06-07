using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ETL_SQL.Core;
using ETL_SQL.Core.Functions;
using ETL_SQL.Core.Interfaces;
using ETL_SQL.Analysis.Lineage;

namespace ETL_SQL.TUI.UI
{
    /// <summary>
    /// Builds the "info at cursor" content shown by Shift+F1 (help) and Shift+F2 (lineage).
    /// Reuses the same registries/lineage the language server's HoverProvider uses, but
    /// returns markdown-ish plain text for the terminal overlay. Word detection and
    /// composition are pure so they can be unit-tested without a console.
    /// </summary>
    public static class InfoAtCursor
    {
        private static bool IsWordChar(char c) =>
            char.IsLetterOrDigit(c) || c == '_' || c == '@' || c == '#' || c == '&';

        /// <summary>The identifier/keyword under (or immediately before) the 0-based column.</summary>
        public static string? WordAt(string line, int col)
        {
            if (string.IsNullOrEmpty(line)) return null;
            int start = Math.Clamp(col, 0, line.Length);
            int end = start;
            while (start > 0 && IsWordChar(line[start - 1])) start--;
            while (end < line.Length && IsWordChar(line[end])) end++;
            return start < end ? line.Substring(start, end - start) : null;
        }

        /// <summary>Function help (preferred) or keyword help for the word at the cursor.</summary>
        public static string? BuildHelp(string lineText, int cursorCol0,
            IFunctionRegistry? functions, ILanguageHelpRegistry? help, out string title)
        {
            title = "Help";
            string? word = WordAt(lineText, cursorCol0);
            if (string.IsNullOrEmpty(word)) return null;
            title = word;

            string? fn = functions?.GetHelp(word);
            string? kw = fn == null ? help?.GetHelp(word) : null;
            string? text = fn ?? kw;
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }

        /// <summary>Matches the cursor to a lineage entry by source span, else by identifier.</summary>
        private static LineageEntry? MatchEntry(IList<LineageEntry> list, string lineText, int cursorLine0, int cursorCol0)
        {
            int line1 = cursorLine0 + 1;
            int col1 = cursorCol0 + 1;
            var entry = list.FirstOrDefault(e =>
                (line1 > e.Line || (line1 == e.Line && col1 >= e.Column)) &&
                (line1 < e.EndLine || (line1 == e.EndLine && col1 <= e.EndColumn)));
            if (entry != null) return entry;

            string? word = WordAt(lineText, cursorCol0);
            if (string.IsNullOrEmpty(word)) return null;
            bool Eq(string? a) => string.Equals(a, word, StringComparison.OrdinalIgnoreCase);
            return list.FirstOrDefault(e =>
                Eq(e.TargetColumn) || Eq(e.TargetTable) || e.SourceColumns.Any(Eq) || e.SourceTables.Any(Eq));
        }

        /// <summary>Lineage text for the matched entry, or null when nothing matches the cursor.</summary>
        public static string? BuildLineageFromEntries(IEnumerable<LineageEntry> entries,
            string lineText, int cursorLine0, int cursorCol0, out string title)
        {
            title = "Lineage";
            var list = entries as IList<LineageEntry> ?? entries.ToList();
            var entry = MatchEntry(list, lineText, cursorLine0, cursorCol0);
            if (entry == null) return null;
            title = !string.IsNullOrEmpty(entry.TargetColumn) ? entry.TargetColumn! : entry.TargetTable;
            return BuildLineageText(entry);
        }

        /// <summary>A "what has lineage" listing, shown when the cursor doesn't match an entry.</summary>
        public static string BuildAvailableList(IEnumerable<LineageEntry> entries, string? word)
        {
            var list = entries as IList<LineageEntry> ?? entries.ToList();
            var targets = list.Where(e => !string.IsNullOrEmpty(e.TargetColumn))
                .Select(e => $"{e.TargetTable}.{e.TargetColumn}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            if (targets.Count == 0)
                targets = list.Select(e => e.TargetTable).Where(t => !string.IsNullOrEmpty(t))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();

            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(word)) sb.AppendLine($"No lineage for **{word}**.");
            sb.AppendLine("#### Identifiers with lineage");
            sb.AppendLine("Put the cursor on one of these in the script, then press Ctrl+L:");
            foreach (var t in targets.Take(60)) sb.AppendLine($"- `{t}`");
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Lineage for the cursor identifier plus the ASCII graph. When the cursor doesn't
        /// match an entry but lineage exists, returns the available-identifiers list so the
        /// user knows where to point. Returns null only when no lineage was captured at all.
        /// </summary>
        public static string? BuildLineage(ILineageTracker? tracker,
            string lineText, int cursorLine0, int cursorCol0, out string title)
        {
            title = "Lineage";
            if (tracker == null) return null;

            var entries = tracker.GetFullLineage().ToList();
            if (entries.Count == 0) return null; // nothing captured -> status message

            string? text = BuildLineageFromEntries(entries, lineText, cursorLine0, cursorCol0, out title);
            if (text == null)
            {
                title = "Lineage";
                return BuildAvailableList(entries, WordAt(lineText, cursorCol0));
            }

            var sb = new StringBuilder(text);
            try
            {
                string matched = title;
                var entry = entries.FirstOrDefault(e =>
                    string.Equals(e.TargetColumn, matched, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(e.TargetTable, matched, StringComparison.OrdinalIgnoreCase));
                string graph = new LineageGraphRenderer().Render(tracker, entry?.TargetTable, entry?.TargetColumn);
                if (!string.IsNullOrWhiteSpace(graph))
                {
                    sb.AppendLine();
                    sb.AppendLine();
                    sb.AppendLine("#### Graph");
                    sb.Append("```text\n");
                    sb.Append(graph.TrimEnd());
                    sb.Append("\n```");
                }
            }
            catch { /* graph is best-effort */ }

            return sb.ToString();
        }

        /// <summary>Combined help + lineage (used by tests; the UI shows them separately).</summary>
        public static string? Build(
            string lineText, int cursorLine0, int cursorCol0,
            IFunctionRegistry? functions, ILanguageHelpRegistry? help,
            IEnumerable<LineageEntry>? lineage, out string title)
        {
            var sections = new List<string>();
            string? h = BuildHelp(lineText, cursorCol0, functions, help, out var helpTitle);
            if (h != null) sections.Add(h);

            string? lin = null;
            string lineageTitle = "Lineage";
            if (lineage != null)
                lin = BuildLineageFromEntries(lineage, lineText, cursorLine0, cursorCol0, out lineageTitle);
            if (lin != null) sections.Add(lin);

            title = h != null ? helpTitle : (lin != null ? lineageTitle : "Info");
            return sections.Count > 0 ? string.Join("\n\n──────────\n\n", sections) : null;
        }

        private static string BuildLineageText(LineageEntry e)
        {
            var sb = new StringBuilder();
            string target = !string.IsNullOrEmpty(e.TargetColumn) ? $"{e.TargetTable}.{e.TargetColumn}" : e.TargetTable;
            sb.AppendLine($"**{target}**");
            if (!string.IsNullOrEmpty(e.Operation)) sb.AppendLine($"- Operation: {e.Operation}");
            if (e.SourceTables.Count > 0) sb.AppendLine($"- Sources: {string.Join(", ", e.SourceTables.Distinct())}");
            if (e.SourceColumns.Count > 0) sb.AppendLine($"- Source columns: {string.Join(", ", e.SourceColumns.Distinct())}");
            if (!string.IsNullOrEmpty(e.TransformationExpression)) sb.AppendLine($"- Expression: `{e.TransformationExpression}`");
            if (e.FunctionsApplied != null && e.FunctionsApplied.Count > 0) sb.AppendLine($"- Functions: {string.Join(", ", e.FunctionsApplied)}");
            if (!string.IsNullOrEmpty(e.Description)) sb.AppendLine($"- Description: {e.Description}");
            if (!string.IsNullOrEmpty(e.DerivedFromDescriptions)) sb.AppendLine($"- Derived from: {e.DerivedFromDescriptions}");
            foreach (var m in e.Metadata)
                if (!m.Key.Equals("d", StringComparison.OrdinalIgnoreCase))
                    sb.AppendLine($"- {m.Key}: {m.Value}");
            return sb.ToString().TrimEnd();
        }
    }
}
