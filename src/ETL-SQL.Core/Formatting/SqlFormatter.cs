using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ETL_SQL.Core.Formatting
{
    /// <summary>
    /// Configuration options for the SQL formatter.
    /// </summary>
    public class FormatterOptions
    {
        public bool LeadingCommas { get; set; } = true;
        public bool RightAlignKeywords { get; set; } = false;
        public int IndentSize { get; set; } = 4;
        public bool UpperCaseKeywords { get; set; } = true;
    }

    /// <summary>
    /// Provides formatting capabilities for ETL-SQL scripts.
    /// </summary>
    public static class SqlFormatter
    {
        private static readonly string[] ClauseKeywords = {
            "SELECT", "FROM", "WHERE", "GROUP BY", "HAVING", "ORDER BY", 
            "INSERT", "UPDATE", "DELETE", "CREATE", "JOIN", "LEFT JOIN", 
            "RIGHT JOIN", "INNER JOIN", "OUTER JOIN", "EXEC", "EXECUTE", "WITH",
            "DECLARE", "SET", "PRINT", "RUN", "IF", "WHILE", "FOR", "FOREACH", 
            "BEGIN", "COMMIT", "ROLLBACK", "RAISEERROR", "THROW", "RETURN",
            "TRUNCATE", "DROP", "ALTER", "MERGE", "MERGE INTO", "USING", 
            "WHEN MATCHED THEN UPDATE", "WHEN MATCHED THEN DELETE", 
            "WHEN NOT MATCHED THEN INSERT", "WHEN NOT MATCHED BY SOURCE THEN UPDATE", 
            "WHEN NOT MATCHED BY SOURCE THEN DELETE",
            "BULK INSERT", "LINEAGE", "SEND_EMAIL", "SEND_FILE", "RECEIVE_FILE", "DOCKER", "USE", "EXPLAIN"
        };

        private static readonly string[] MultiWordKeywords = ClauseKeywords.Where(k => k.Contains(" ")).OrderByDescending(k => k.Length).ToArray();

        /// <summary>
        /// Formats the input script for improved readability.
        /// </summary>
        public static string Format(string script, FormatterOptions? options = null)
        {
            if (string.IsNullOrWhiteSpace(script)) return script;
            options ??= new FormatterOptions();

            // Normalize line endings and cleanup
            string input = script.Replace("\r\n", "\n").Replace("\r", "\n");
            
            // Extract clauses
            var clauses = ParseClauses(input);
            
            var sb = new StringBuilder();
            foreach (var clause in clauses)
            {
                if (string.IsNullOrEmpty(clause.Content) && clause.Name == "START") continue;
                
                string name = clause.Name;
                string content = clause.Content;
                
                if (name == "START")
                {
                    if (!string.IsNullOrWhiteSpace(content)) sb.AppendLine(content.Trim());
                    continue;
                }

                FormatClause(sb, name, content, options);
            }

            return sb.ToString().TrimEnd();
        }

        private static List<(string Name, string Content)> ParseClauses(string input)
        {
            var clauses = new List<(string Name, string Content)>();
            string currentClause = "START";
            var currentContent = new StringBuilder();
            
            // Split by tokens but preserve separators
            var tokens = Regex.Split(input, @"(\s+|[(),;])").Where(t => t.Length > 0).ToArray();
            
            int i = 0;
            while (i < tokens.Length)
            {
                string token = tokens[i];
                if (string.IsNullOrWhiteSpace(token))
                {
                    currentContent.Append(token);
                    i++;
                    continue;
                }

                // Check for multi-word keywords first
                bool foundMulti = false;
                foreach (var kw in MultiWordKeywords)
                {
                    var kwParts = kw.Split(' ');
                    bool match = true;
                    int k = 0;
                    int j = i;
                    while (k < kwParts.Length && j < tokens.Length)
                    {
                        if (string.IsNullOrWhiteSpace(tokens[j])) { j++; continue; }
                        if (!string.Equals(tokens[j].Trim(), kwParts[k], StringComparison.OrdinalIgnoreCase)) { match = false; break; }
                        k++; j++;
                    }

                    if (match && k == kwParts.Length)
                    {
                        clauses.Add((currentClause, currentContent.ToString().Trim()));
                        currentClause = kw.ToUpper();
                        currentContent.Clear();
                        i = j;
                        foundMulti = true;
                        break;
                    }
                }

                if (foundMulti) continue;

                string trimmedUpper = token.Trim().ToUpper();
                if (ClauseKeywords.Contains(trimmedUpper))
                {
                    clauses.Add((currentClause, currentContent.ToString().Trim()));
                    currentClause = trimmedUpper;
                    currentContent.Clear();
                    i++;
                }
                else if (token == ";")
                {
                    currentContent.Append(token);
                    clauses.Add((currentClause, currentContent.ToString().Trim()));
                    currentClause = "START";
                    currentContent.Clear();
                    i++;
                }
                else
                {
                    currentContent.Append(token);
                    i++;
                }
            }
            clauses.Add((currentClause, currentContent.ToString().Trim()));
            return clauses;
        }

        private static void FormatClause(StringBuilder sb, string name, string content, FormatterOptions options)
        {
            string formattedName = options.UpperCaseKeywords ? name.ToUpper() : name;
            string indent = new string(' ', options.IndentSize);
            string padding = options.RightAlignKeywords ? new string(' ', 15) : "";
            
            // Sub-clauses that should be indented (when not right-aligning)
            // Note: FROM is only indented when it's part of BULK INSERT or similar
            // For now, we'll indent these specifically.
            string[] subClauses = { 
                "USING", "WHEN MATCHED THEN UPDATE", "WHEN MATCHED THEN DELETE", 
                "WHEN NOT MATCHED THEN INSERT", "WHEN NOT MATCHED BY SOURCE THEN UPDATE", 
                "WHEN NOT MATCHED BY SOURCE THEN DELETE"
            };
            bool isSubClause = subClauses.Contains(name) && !options.RightAlignKeywords;

            if (isSubClause)
            {
                // Only add newline if not already at the start of a line
                if (sb.Length > 0 && sb[sb.Length - 1] != '\n') sb.AppendLine();
                sb.Append(padding + indent + formattedName + " ");
            }
            else
            {
                if (sb.Length > 0 && sb[sb.Length - 1] != '\n') sb.AppendLine();
                if (options.RightAlignKeywords)
                {
                    sb.Append(formattedName.PadLeft(15) + " ");
                }
                else
                {
                    sb.Append(formattedName);
                }
            }

            if (name == "SELECT" || name == "ORDER BY" || name == "GROUP BY")
            {
                // Handle SELECT DISTINCT, SELECT TOP N
                var words = Regex.Split(content, @"(\s+)").Where(w => !string.IsNullOrWhiteSpace(w)).ToList();
                int idx = 0;
                while (idx < words.Count && (words[idx].ToUpper() == "DISTINCT" || words[idx].ToUpper().StartsWith("TOP")))
                {
                    sb.Append(" " + (options.UpperCaseKeywords ? words[idx].ToUpper() : words[idx]));
                    idx++;
                }
                
                var remaining = string.Join(" ", words.Skip(idx));
                var columns = SplitByCommaOutsideParens(remaining);
                
                if (columns.Count > 0)
                {
                    string colPadding = options.RightAlignKeywords ? new string(' ', 16) : "";
                    
                    if (options.LeadingCommas)
                    {
                        sb.AppendLine();
                        sb.Append(colPadding + indent + " " + columns[0].Trim());
                        for (int j = 1; j < columns.Count; j++)
                        {
                            sb.AppendLine();
                            sb.Append(colPadding + indent + "," + columns[j].Trim());
                        }
                    }
                    else
                    {
                        sb.AppendLine();
                        for (int j = 0; j < columns.Count; j++)
                        {
                            sb.Append(colPadding + indent + columns[j].Trim());
                            if (j < columns.Count - 1) sb.Append(",");
                            sb.AppendLine();
                        }
                    }
                }
                if (!options.LeadingCommas) sb.Length -= Environment.NewLine.Length; // Remove last newline
                sb.AppendLine();
            }
            else if (name == "WHERE" || name == "HAVING")
            {
                sb.AppendLine();
                var subParts = Regex.Split(content, @"(\bAND\b|\bOR\b)", RegexOptions.IgnoreCase);
                bool first = true;
                string wherePadding = options.RightAlignKeywords ? new string(' ', 16) : "";

                foreach (var part in subParts)
                {
                    string trimmed = part.Trim();
                    if (string.Equals(trimmed, "AND", StringComparison.OrdinalIgnoreCase) || 
                        string.Equals(trimmed, "OR", StringComparison.OrdinalIgnoreCase))
                    {
                        sb.Append(wherePadding + indent + (options.UpperCaseKeywords ? trimmed.ToUpper() : trimmed) + " ");
                    }
                    else if (!string.IsNullOrEmpty(trimmed))
                    {
                        if (first) sb.Append(wherePadding + indent);
                        sb.AppendLine(trimmed);
                        first = false;
                    }
                }
            }
            else if (name.Contains("JOIN"))
            {
                int onIdx = content.IndexOf(" ON ", StringComparison.OrdinalIgnoreCase);
                if (onIdx >= 0)
                {
                    string tablePart = content.Substring(0, onIdx).Trim();
                    string conditionPart = content.Substring(onIdx + 4).Trim();
                    sb.Append(" " + tablePart);
                    sb.AppendLine();
                    
                    string joinPadding = options.RightAlignKeywords ? new string(' ', 16) : "";
                    sb.Append(joinPadding + indent + (options.UpperCaseKeywords ? "ON " : "on ") + conditionPart);
                }
                else
                {
                    sb.Append(" " + content);
                }
                sb.AppendLine();
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(content))
                {
                    if (options.RightAlignKeywords)
                    {
                        sb.AppendLine(content.Trim());
                    }
                    else
                    {
                        if (isSubClause)
                        {
                             // Already appended indent + formattedName + " "
                             sb.AppendLine(content.Trim());
                        }
                        else
                        {
                            sb.AppendLine(" " + content.Trim());
                        }
                    }
                }
                else
                {
                    sb.AppendLine();
                }
            }
        }

        private static List<string> SplitByCommaOutsideParens(string input)
        {
            var result = new List<string>();
            var current = new StringBuilder();
            int depth = 0;

            foreach (char c in input)
            {
                if (c == '(') depth++;
                else if (c == ')') depth--;

                if (c == ',' && depth == 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
            
            if (current.Length > 0) result.Add(current.ToString());
            return result;
        }
    }
}
