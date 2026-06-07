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

        /// <summary>
        /// Lineage for the identifier at the cursor: by source span first, else by matching
        /// the word to a target table/column. Returns null when no entry matches.
        /// </summary>
        public static string? BuildLineageFromEntries(IEnumerable<LineageEntry> entries,
            string lineText, int cursorLine0, int cursorCol0, out string title)
        {
            title = "Lineage";
            var list = entries as IList<LineageEntry> ?? entries.ToList();

            int line1 = cursorLine0 + 1;
            int col1 = cursorCol0 + 1;
            var entry = list.FirstOrDefault(e =>
                (line1 > e.Line || (line1 == e.Line && col1 >= e.Column)) &&
                (line1 < e.EndLine || (line1 == e.EndLine && col1 <= e.EndColumn)));

            if (entry == null)
            {
                string? word = WordAt(lineText, cursorCol0);
                if (!string.IsNullOrEmpty(word))
                    entry = list.FirstOrDefault(e =>
                        string.Equals(e.TargetColumn, word, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(e.TargetTable, word, StringComparison.OrdinalIgnoreCase));
            }

            if (entry == null) return null;
            title = !string.IsNullOrEmpty(entry.TargetColumn) ? entry.TargetColumn! : entry.TargetTable;
            return BuildLineageText(entry);
        }

        /// <summary>Lineage text plus the ASCII lineage graph (needs the live tracker).</summary>
        public static string? BuildLineage(ILineageTracker? tracker,
            string lineText, int cursorLine0, int cursorCol0, out string title)
        {
            title = "Lineage";
            if (tracker == null) return null;

            var entries = tracker.GetFullLineage().ToList();
            string? text = BuildLineageFromEntries(entries, lineText, cursorLine0, cursorCol0, out title);
            if (text == null) return null;

            var sb = new StringBuilder(text);
            try
            {
                // Title is the target column (or table) we matched.
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
                    sb.Append("```text");
                    sb.Append('\n');
                    sb.Append(graph.TrimEnd());
                    sb.Append('\n');
                    sb.Append("```");
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
