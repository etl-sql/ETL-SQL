using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ETL_SQL.Core;
using ETL_SQL.Core.Functions;
using ETL_SQL.Core.Interfaces;

namespace ETL_SQL.TUI.UI
{
    /// <summary>
    /// Builds the "info at cursor" content (function/keyword help + lineage) shown by
    /// Shift+F1. Reuses the same registries the language server's HoverProvider uses, but
    /// returns plain text for the terminal overlay. Word detection and composition are pure
    /// so they can be unit-tested without a console.
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

        /// <summary>
        /// Composes help (function, else keyword) and lineage for the cursor position.
        /// <paramref name="cursorLine0"/>/<paramref name="cursorCol0"/> are 0-based; lineage
        /// entries are 1-based (as produced by the engine). Returns null when nothing matches.
        /// </summary>
        public static string? Build(
            string lineText, int cursorLine0, int cursorCol0,
            IFunctionRegistry? functions, ILanguageHelpRegistry? help,
            IEnumerable<LineageEntry>? lineage,
            out string title)
        {
            title = "Info";
            var sections = new List<string>();

            string? word = WordAt(lineText, cursorCol0);
            if (!string.IsNullOrEmpty(word))
            {
                title = word;
                string? fn = functions?.GetHelp(word);
                string? kw = fn == null ? help?.GetHelp(word) : null;
                string? text = fn ?? kw;
                if (!string.IsNullOrWhiteSpace(text)) sections.Add(text.Trim());
            }

            if (lineage != null)
            {
                int line1 = cursorLine0 + 1;
                int col1 = cursorCol0 + 1;
                var entry = lineage.FirstOrDefault(e =>
                    (line1 > e.Line || (line1 == e.Line && col1 >= e.Column)) &&
                    (line1 < e.EndLine || (line1 == e.EndLine && col1 <= e.EndColumn)));

                if (entry != null)
                {
                    string lin = BuildLineageText(entry);
                    if (!string.IsNullOrWhiteSpace(lin)) sections.Add(lin);
                    if (string.IsNullOrEmpty(word) && !string.IsNullOrEmpty(entry.TargetColumn))
                        title = entry.TargetColumn!;
                }
            }

            return sections.Count > 0 ? string.Join("\n\n──────────\n\n", sections) : null;
        }

        private static string BuildLineageText(LineageEntry e)
        {
            var sb = new StringBuilder();
            string target = e.TargetColumn != null ? $"{e.TargetTable}.{e.TargetColumn}" : e.TargetTable;
            sb.AppendLine($"Lineage: {target}");
            if (!string.IsNullOrEmpty(e.Operation)) sb.AppendLine($"Operation: {e.Operation}");
            if (e.SourceTables.Count > 0) sb.AppendLine($"Sources: {string.Join(", ", e.SourceTables.Distinct())}");
            if (!string.IsNullOrEmpty(e.Description)) sb.AppendLine($"Description: {e.Description}");
            if (!string.IsNullOrEmpty(e.DerivedFromDescriptions)) sb.AppendLine($"Derived from: {e.DerivedFromDescriptions}");
            foreach (var m in e.Metadata)
                if (!m.Key.Equals("d", StringComparison.OrdinalIgnoreCase))
                    sb.AppendLine($"  {m.Key}: {m.Value}");
            return sb.ToString().TrimEnd();
        }
    }
}
