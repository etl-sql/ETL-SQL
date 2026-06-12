using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ETL_SQL.Common;
using ETL_SQL.Core;

namespace ETL_SQL.Core.Parser
{
    public record AliasInfo(string TableName, string? Alias = null)
    {
        public string? ConnectionName { get; init; }
        public string? BaseTableName { get; init; }
        public bool HasExplicitAlias => !string.IsNullOrEmpty(Alias) && !Alias.Equals(TableName, StringComparison.OrdinalIgnoreCase);
    }

    public static class AliasScanner
    {
        public static Dictionary<string, AliasInfo> Scan(string script, int cursorOffset = -1)
        {
            var aliases = new Dictionary<string, AliasInfo>(StringComparer.OrdinalIgnoreCase);

            // Heuristic: identify the statement block containing the cursor
            // Split by GO, semicolon, or double newline
            string activeBlock = script;
            if (cursorOffset >= 0 && cursorOffset <= script.Length)
            {
                var blocks = Regex.Matches(script, @"\bGO\b|;|(?:\r?\n){2,}", RegexOptions.IgnoreCase);
                int start = 0;
                int end = script.Length;

                foreach (Match m in blocks)
                {
                    if (m.Index < cursorOffset)
                    {
                        start = m.Index + m.Length;
                    }
                    else
                    {
                        end = m.Index;
                        break;
                    }
                }
                activeBlock = script.Substring(start, end - start);
            }
            else
            {
                // Fallback to whole script if no cursor info (e.g. in tests or for global highlighting)
                activeBlock = script;
            }

            // Find FROM/JOIN and the tables following them until next major keyword or separator
            var matches = Regex.Matches(activeBlock, @"\b(FROM|JOIN)\s+([^;]+?)(?=\b(WHERE|GROUP|ORDER|HAVING|LIMIT|OFFSET|JOIN|ON|UNION|EXCEPT|INTERSECT|SELECT)\b|;|SELECT|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            foreach (Match m in matches)
            {
                var tablesPart = m.Groups[2].Value;
                var tableSpecs = tablesPart.Split(',', StringSplitOptions.RemoveEmptyEntries);
                foreach (var spec in tableSpecs)
                {
                    var trimmed = spec.Trim();
                    // Match table name and optional alias
                    // Groups: 1=Table, 2=Alias
                    var tableMatch = Regex.Match(trimmed, @"^(#?[\w\.]+|\[[^\]]+\]|\w+\s*\([^)]*\))(?:\s+(?:AS\s+)?(\w+))?$", RegexOptions.IgnoreCase);
                    if (tableMatch.Success)
                    {
                        var table = tableMatch.Groups[1].Value.Trim('[', ']');
                        var alias = tableMatch.Groups[2].Value;
                        string? connName = table.Contains('.') ? table.Split(new[] { '.' }, 2)[0] : null;
                        string? baseTable = table.Contains('.') ? table.Split(new[] { '.' }, 2)[1] : null;

                        var info = new AliasInfo(table, string.IsNullOrEmpty(alias) ? null : alias)
                        {
                            ConnectionName = connName,
                            BaseTableName = baseTable
                        };

                        string key = !string.IsNullOrEmpty(alias) ? alias : table;
                        if (!LanguageMetadata.IsKeyword(key))
                        {
                            aliases[key] = info;
                        }
                        // Also ensure the table name is in the dictionary if an alias exists
                        if (!string.IsNullOrEmpty(alias) && !LanguageMetadata.IsKeyword(table) && !aliases.ContainsKey(table))
                        {
                            aliases[table] = info;
                        }
                    }
                }
            }
            return aliases;
        }
    }
}
