using System;
using System.Collections.Generic;
using System.IO;
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

        // Customizable settings (personalities)
        public string KeywordCasing { get; set; } = "upper"; // "upper", "lower", "pascal", "preserve"
        public int LineWidth { get; set; } = 100;
        public bool IndentJoins { get; set; } = false;
        public bool OnClauseOnNewLine { get; set; } = true;
        public bool CaseWhenThenNewLine { get; set; } = false;
        public bool BreakoutWindowFunctions { get; set; } = true;
        public string CommaPlacement { get; set; } = "trailing"; // "trailing" or "leading"

        public static FormatterOptions LoadFromFile(string? startFilePath)
        {
            var options = new FormatterOptions();
            if (string.IsNullOrEmpty(startFilePath)) return options;

            try
            {
                string? currentDir = Path.GetDirectoryName(startFilePath);
                while (!string.IsNullOrEmpty(currentDir))
                {
                    string configPath = Path.Combine(currentDir, ".etlsqlformat.json");
                    if (File.Exists(configPath))
                    {
                        string json = File.ReadAllText(configPath);
                        var loaded = System.Text.Json.JsonSerializer.Deserialize<FormatterOptions>(json, new System.Text.Json.JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });
                        if (loaded != null)
                        {
                            // Sync legacy setting with casing setting
                            if (loaded.KeywordCasing.Equals("upper", StringComparison.OrdinalIgnoreCase))
                                loaded.UpperCaseKeywords = true;
                            else if (loaded.KeywordCasing.Equals("lower", StringComparison.OrdinalIgnoreCase) || loaded.KeywordCasing.Equals("pascal", StringComparison.OrdinalIgnoreCase))
                                loaded.UpperCaseKeywords = false;
                            
                            // Sync comma placement setting with legacy setting
                            if (loaded.CommaPlacement.Equals("leading", StringComparison.OrdinalIgnoreCase))
                                loaded.LeadingCommas = true;
                            else if (loaded.CommaPlacement.Equals("trailing", StringComparison.OrdinalIgnoreCase))
                                loaded.LeadingCommas = false;

                            return loaded;
                        }
                    }
                    currentDir = Path.GetDirectoryName(currentDir);
                }
            }
            catch
            {
                // Fallback to defaults
            }

            return options;
        }
    }

    /// <summary>
    /// Provides formatting capabilities for ETL-SQL scripts.
    /// </summary>
    public static class SqlFormatter
    {
        private static readonly string[] ClauseKeywords = {
            "SELECT", "INTO", "FROM", "WHERE", "GROUP BY", "HAVING", "ORDER BY",
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
            return Format(script, options, 0);
        }

        public static string Format(string script, FormatterOptions? options, int baseIndentLevel)
        {
            if (string.IsNullOrWhiteSpace(script)) return script;
            options ??= new FormatterOptions();

            // Sync legacy options with standard options
            if (options.KeywordCasing.Equals("upper", StringComparison.OrdinalIgnoreCase) && !options.UpperCaseKeywords)
            {
                options.KeywordCasing = "preserve"; // Fallback if custom unset
            }
            if (options.LeadingCommas)
            {
                options.CommaPlacement = "leading";
            }
            else if (options.CommaPlacement.Equals("leading", StringComparison.OrdinalIgnoreCase))
            {
                options.LeadingCommas = true;
            }

            // Normalize line endings and cleanup
            string input = script.Replace("\r\n", "\n").Replace("\r", "\n");

            // Extract clauses (ignoring those inside nested scopes/parentheses)
            var clauses = ParseClauses(input);

            var sb = new StringBuilder();
            bool firstStatement = true;
            string baseIndent = new string(' ', baseIndentLevel * options.IndentSize);

            foreach (var clause in clauses)
            {
                if (string.IsNullOrEmpty(clause.Content) && clause.Name == "START") continue;

                string name = clause.Name;
                string content = clause.Content;

                if (name == "START")
                {
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        var formattedContent = FormatCaseStatements(content, options, baseIndentLevel);
                        formattedContent = FormatParentheses(formattedContent, options, baseIndentLevel);
                        sb.AppendLine(baseIndent + formattedContent.Trim());
                    }
                    continue;
                }

                // Add empty line before major statement starts (SELECT, INSERT, etc.)
                // if it's not the very first statement in the script.
                string[] statementStarters = { "SELECT", "INSERT", "UPDATE", "DELETE", "CREATE", "MERGE", "DECLARE", "SET", "EXEC", "EXECUTE", "IF", "WHILE", "FOR", "FOREACH" };
                if (statementStarters.Contains(name) && !firstStatement && sb.Length > 0 && !sb.ToString().EndsWith("\n\n"))
                {
                    sb.AppendLine();
                }
                if (statementStarters.Contains(name)) firstStatement = false;

                FormatClause(sb, name, content, options, baseIndentLevel);
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

            int parenthesisDepth = 0;
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

                if (token == "(")
                {
                    parenthesisDepth++;
                    currentContent.Append(token);
                    i++;
                    continue;
                }
                else if (token == ")")
                {
                    parenthesisDepth = Math.Max(0, parenthesisDepth - 1);
                    currentContent.Append(token);
                    i++;
                    continue;
                }

                // Only detect clause boundaries at depth 0
                if (parenthesisDepth == 0)
                {
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
                            if (tokens[j] == "(" || tokens[j] == ")") { match = false; break; }
                            if (!string.Equals(tokens[j].Trim(), kwParts[k], StringComparison.OrdinalIgnoreCase)) { match = false; break; }
                            k++; j++;
                        }

                        if (match && k == kwParts.Length)
                        {
                            clauses.Add((currentClause, currentContent.ToString().Trim()));
                            
                            // Preserve case or force normalize
                            var matchedTokens = tokens.Skip(i).Take(j - i).Where(t => !string.IsNullOrWhiteSpace(t));
                            currentClause = string.Join(" ", matchedTokens);
                            
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
                        currentClause = token;
                        currentContent.Clear();
                        i++;
                        continue;
                    }
                }

                if (token == ";")
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

        private static void FormatClause(StringBuilder sb, string name, string content, FormatterOptions options, int baseIndentLevel)
        {
            string formattedName = FormatKeyword(name, options);

            string baseIndent = new string(' ', baseIndentLevel * options.IndentSize);
            string indent = new string(' ', options.IndentSize);
            string padding = options.RightAlignKeywords ? new string(' ', 15) : "";

            // Sub-clauses that should be indented (when not right-aligning)
            string[] subClauses = {
                "USING", "WHEN MATCHED THEN UPDATE", "WHEN MATCHED THEN DELETE",
                "WHEN NOT MATCHED THEN INSERT", "WHEN NOT MATCHED BY SOURCE THEN UPDATE",
                "WHEN NOT MATCHED BY SOURCE THEN DELETE"
            };
            bool isSubClause = subClauses.Contains(name.ToUpper()) && !options.RightAlignKeywords;
            bool isJoinClause = name.ToUpper().Contains("JOIN");
            string joinIndent = new string(' ', (baseIndentLevel + (options.IndentJoins ? 1 : 0)) * options.IndentSize);

            if (sb.Length > 0 && sb[sb.Length - 1] != '\n') sb.AppendLine();

            if (options.RightAlignKeywords)
            {
                sb.Append(formattedName.PadLeft(15) + " ");
            }
            else
            {
                if (isJoinClause)
                {
                    sb.Append(joinIndent + formattedName);
                }
                else if (isSubClause)
                {
                    sb.Append(baseIndent + indent + formattedName);
                }
                else
                {
                    sb.Append(baseIndent + formattedName);
                }
            }

            string nameUpper = name.ToUpper();
            if (nameUpper == "SELECT" || nameUpper == "ORDER BY" || nameUpper == "GROUP BY")
            {
                // Handle SELECT DISTINCT, SELECT TOP N
                var words = Regex.Split(content, @"(\s+)").Where(w => !string.IsNullOrWhiteSpace(w)).ToList();
                int idx = 0;
                while (idx < words.Count && (words[idx].ToUpper() == "DISTINCT" || words[idx].ToUpper().StartsWith("TOP")))
                {
                    sb.Append(" " + FormatKeyword(words[idx], options));
                    idx++;
                }

                var remaining = string.Join(" ", words.Skip(idx));
                remaining = FormatCaseStatements(remaining, options, baseIndentLevel + 1);
                remaining = FormatParentheses(remaining, options, baseIndentLevel + 1);

                var columns = SplitByCommaOutsideParens(remaining);

                if (columns.Count > 0)
                {
                    string colPadding = options.RightAlignKeywords ? new string(' ', 16) : "";

                    if (options.CommaPlacement.Equals("leading", StringComparison.OrdinalIgnoreCase))
                    {
                        sb.AppendLine();
                        sb.Append(colPadding + baseIndent + indent + " " + columns[0].Trim());
                        for (int j = 1; j < columns.Count; j++)
                        {
                            sb.AppendLine();
                            sb.Append(colPadding + baseIndent + indent + "," + columns[j].Trim());
                        }
                    }
                    else
                    {
                        sb.AppendLine();
                        for (int j = 0; j < columns.Count; j++)
                        {
                            sb.Append(colPadding + baseIndent + indent + columns[j].Trim());
                            if (j < columns.Count - 1) sb.Append(",");
                            sb.AppendLine();
                        }
                    }
                }
                if (!options.CommaPlacement.Equals("leading", StringComparison.OrdinalIgnoreCase)) sb.Length -= Environment.NewLine.Length; // Remove last newline
                sb.AppendLine();
            }
            else if (nameUpper == "WHERE" || nameUpper == "HAVING")
            {
                string formattedContent = FormatCaseStatements(content, options, baseIndentLevel + 1);
                formattedContent = FormatParentheses(formattedContent, options, baseIndentLevel + 1);

                string trimmedContent = formattedContent.Trim();
                bool startsWithOneOne = Regex.IsMatch(trimmedContent, @"^1\s*=\s*1\b", RegexOptions.IgnoreCase);

                if (startsWithOneOne)
                {
                    var subParts = SplitByAndOrOutsideParens(trimmedContent);
                    string firstPart = subParts[0].Trim();
                    
                    sb.Append(" " + firstPart);
                    sb.AppendLine();

                    string wherePadding = options.RightAlignKeywords ? new string(' ', 16) : "";
                    for (int k = 1; k < subParts.Count; k++)
                    {
                        string part = subParts[k];
                        string trimmed = part.Trim();
                        if (string.Equals(trimmed, "AND", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(trimmed, "OR", StringComparison.OrdinalIgnoreCase))
                        {
                            string formattedOp = FormatKeyword(trimmed, options);
                            sb.Append(wherePadding + baseIndent + indent + formattedOp + " ");
                        }
                        else if (!string.IsNullOrEmpty(trimmed))
                        {
                            sb.AppendLine(trimmed);
                        }
                    }
                }
                else
                {
                    sb.AppendLine();
                    var subParts = SplitByAndOrOutsideParens(formattedContent);
                    bool first = true;
                    string wherePadding = options.RightAlignKeywords ? new string(' ', 16) : "";

                    foreach (var part in subParts)
                    {
                        string trimmed = part.Trim();
                        if (string.Equals(trimmed, "AND", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(trimmed, "OR", StringComparison.OrdinalIgnoreCase))
                        {
                            string formattedOp = FormatKeyword(trimmed, options);
                            sb.Append(wherePadding + baseIndent + indent + formattedOp + " ");
                        }
                        else if (!string.IsNullOrEmpty(trimmed))
                        {
                            if (first) sb.Append(wherePadding + baseIndent + indent);
                            sb.AppendLine(trimmed);
                            first = false;
                        }
                    }
                }
            }
            else if (isJoinClause)
            {
                string formattedContent = FormatCaseStatements(content, options, baseIndentLevel + 1);
                formattedContent = FormatParentheses(formattedContent, options, baseIndentLevel + 1);

                int onIdx = formattedContent.IndexOf(" ON ", StringComparison.OrdinalIgnoreCase);
                if (onIdx >= 0)
                {
                    string tablePart = formattedContent.Substring(0, onIdx).Trim();
                    string conditionPart = formattedContent.Substring(onIdx + 4).Trim();
                    sb.Append(" " + tablePart);

                    string formattedOn = FormatKeyword("ON", options) + " ";
                    
                    if (options.OnClauseOnNewLine)
                    {
                        sb.AppendLine();
                        string joinPadding = options.RightAlignKeywords ? new string(' ', 16) : "";
                        string onIndent = new string(' ', (baseIndentLevel + (options.IndentJoins ? 1 : 0) + 1) * options.IndentSize);
                        sb.Append(joinPadding + onIndent + formattedOn + conditionPart);
                    }
                    else
                    {
                        sb.Append(" " + formattedOn + conditionPart);
                    }
                }
                else
                {
                    sb.Append(" " + formattedContent);
                }
                sb.AppendLine();
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(content))
                {
                    string formattedContent = FormatCaseStatements(content, options, baseIndentLevel);
                    formattedContent = FormatParentheses(formattedContent, options, baseIndentLevel);

                    if (options.RightAlignKeywords)
                    {
                        sb.AppendLine(formattedContent.Trim());
                    }
                    else
                    {
                        if (isSubClause)
                        {
                            sb.AppendLine(formattedContent.Trim());
                        }
                        else
                        {
                            sb.AppendLine(" " + formattedContent.Trim());
                        }
                    }
                }
                else
                {
                    sb.AppendLine();
                }
            }
        }

        private static string FormatParentheses(string text, FormatterOptions options, int currentIndentLevel)
        {
            if (string.IsNullOrEmpty(text)) return text;

            var sb = new StringBuilder();
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];

                if (c == '(')
                {
                    // Find matching ')'
                    int depth = 1;
                    int j = i + 1;
                    while (j < text.Length && depth > 0)
                    {
                        if (text[j] == '(') depth++;
                        else if (text[j] == ')') depth--;
                        j++;
                    }

                    if (depth == 0)
                    {
                        string inner = text.Substring(i + 1, j - i - 2).Trim();
                        bool startsWithOneZero = Regex.IsMatch(inner, @"^1\s*=\s*0\b", RegexOptions.IgnoreCase);

                        string formattedInner = FormatInnerParentheses(inner, options, currentIndentLevel);

                        string baseIndent = new string(' ', currentIndentLevel * options.IndentSize);

                        if (startsWithOneZero)
                        {
                            sb.Append("(" + formattedInner + "\n" + baseIndent + ")");
                        }
                        else if (formattedInner.Contains("\n") || formattedInner.Length > options.LineWidth)
                        {
                            sb.AppendLine("(");
                            sb.Append(formattedInner);
                            sb.AppendLine();
                            sb.Append(baseIndent + ")");
                        }
                        else
                        {
                            sb.Append("(" + formattedInner + ")");
                        }

                        i = j;
                        continue;
                    }
                }

                sb.Append(c);
                i++;
            }

            return sb.ToString();
        }

        private static readonly string[] SubqueryKeywords = { "SELECT", "DECLARE", "INSERT", "UPDATE", "DELETE", "MERGE", "WITH" };

        private static string FormatInnerParentheses(string inner, FormatterOptions options, int currentIndentLevel)
        {
            if (string.IsNullOrWhiteSpace(inner)) return inner;

            // Check if it's a 1=0 special clause
            bool startsWithOneZero = Regex.IsMatch(inner.Trim(), @"^1\s*=\s*0\b", RegexOptions.IgnoreCase);
            if (startsWithOneZero)
            {
                return FormatOneZeroClause(inner, options, currentIndentLevel);
            }

            // Check if it's a subquery (contains SELECT/FROM/WHERE)
            bool isSubQuery = ContainsKeywordAtDepthZero(inner, SubqueryKeywords);

            if (isSubQuery)
            {
                return Format(inner, options, currentIndentLevel + 1);
            }

            // Check if it's a window function OVER clause (contains PARTITION BY or ORDER BY)
            bool isWindow = Regex.IsMatch(inner, @"\b(PARTITION\s+BY|ORDER\s+BY)\b", RegexOptions.IgnoreCase);
            if (isWindow && options.BreakoutWindowFunctions)
            {
                return FormatWindowClause(inner, options, currentIndentLevel + 1);
            }

            // Check if it's a parameter list (contains commas outside nested parens)
            var parts = SplitByCommaOutsideParens(inner);
            if (parts.Count > 1)
            {
                string nextIndent = new string(' ', (currentIndentLevel + 1) * options.IndentSize);

                var sb = new StringBuilder();
                if (options.CommaPlacement.Equals("leading", StringComparison.OrdinalIgnoreCase))
                {
                    string firstItem = FormatParentheses(parts[0].Trim(), options, currentIndentLevel + 1);
                    sb.Append(nextIndent + " " + firstItem);
                    for (int k = 1; k < parts.Count; k++)
                    {
                        string item = FormatParentheses(parts[k].Trim(), options, currentIndentLevel + 1);
                        sb.AppendLine();
                        sb.Append(nextIndent + "," + item);
                    }
                }
                else
                {
                    for (int k = 0; k < parts.Count; k++)
                    {
                        string item = FormatParentheses(parts[k].Trim(), options, currentIndentLevel + 1);
                        sb.Append(nextIndent + item);
                        if (k < parts.Count - 1) sb.Append(",");
                        if (k < parts.Count - 1) sb.AppendLine();
                    }
                }
                return sb.ToString();
            }

            return FormatParentheses(inner, options, currentIndentLevel);
        }

        private static string FormatWindowClause(string inner, FormatterOptions options, int currentIndentLevel)
        {
            string nextIndent = new string(' ', currentIndentLevel * options.IndentSize);
            var parts = Regex.Split(inner, @"\b(PARTITION\s+BY|ORDER\s+BY)\b", RegexOptions.IgnoreCase);
            var sb = new StringBuilder();
            bool first = true;

            for (int k = 0; k < parts.Length; k++)
            {
                string part = parts[k].Trim();
                if (string.IsNullOrEmpty(part)) continue;

                string upperPart = part.ToUpper();
                if (upperPart.StartsWith("PARTITION BY") || upperPart.StartsWith("ORDER BY"))
                {
                    string formattedKw = FormatKeyword(part, options);
                    if (!first) sb.AppendLine();
                    sb.Append(nextIndent + formattedKw + " ");
                    first = false;
                }
                else
                {
                    sb.Append(part);
                }
            }
            return sb.ToString();
        }

        private static string FormatCaseStatements(string text, FormatterOptions options, int currentIndentLevel)
        {
            if (string.IsNullOrEmpty(text)) return text;

            var sb = new StringBuilder();
            int i = 0;
            while (i < text.Length)
            {
                if (i + 4 <= text.Length &&
                    string.Equals(text.Substring(i, 4), "CASE", StringComparison.OrdinalIgnoreCase) &&
                    (i == 0 || !char.IsLetterOrDigit(text[i - 1]) && text[i - 1] != '_') &&
                    (i + 4 == text.Length || !char.IsLetterOrDigit(text[i + 4]) && text[i + 4] != '_'))
                {
                    int depth = 1;
                    int j = i + 4;
                    while (j < text.Length && depth > 0)
                    {
                        if (j + 4 <= text.Length &&
                            string.Equals(text.Substring(j, 4), "CASE", StringComparison.OrdinalIgnoreCase) &&
                            !char.IsLetterOrDigit(text[j - 1]) && text[j - 1] != '_' &&
                            (j + 4 == text.Length || !char.IsLetterOrDigit(text[j + 4]) && text[j + 4] != '_'))
                        {
                            depth++;
                            j += 4;
                        }
                        else if (j + 3 <= text.Length &&
                                 string.Equals(text.Substring(j, 3), "END", StringComparison.OrdinalIgnoreCase) &&
                                 !char.IsLetterOrDigit(text[j - 1]) && text[j - 1] != '_' &&
                                 (j + 3 == text.Length || !char.IsLetterOrDigit(text[j + 3]) && text[j + 3] != '_'))
                        {
                            depth--;
                            if (depth == 0) break;
                            j += 3;
                        }
                        else
                        {
                            j++;
                        }
                    }

                    if (depth == 0)
                    {
                        string inner = text.Substring(i + 4, j - i - 4).Trim();
                        string formattedCase = FormatCaseInner(inner, options, currentIndentLevel);

                        string baseIndent = new string(' ', currentIndentLevel * options.IndentSize);
                        string formattedCaseKw = FormatKeyword("CASE", options);
                        string formattedEndKw = FormatKeyword("END", options);

                        sb.AppendLine(formattedCaseKw);
                        sb.Append(formattedCase);
                        sb.AppendLine();
                        sb.Append(baseIndent + formattedEndKw);

                        i = j + 3;
                        continue;
                    }
                }
                sb.Append(text[i]);
                i++;
            }
            return sb.ToString();
        }

        private static string FormatCaseInner(string inner, FormatterOptions options, int currentIndentLevel)
        {
            string nextIndent = new string(' ', (currentIndentLevel + 1) * options.IndentSize);
            string thenIndent = new string(' ', (currentIndentLevel + 2) * options.IndentSize);

            var tokens = Regex.Split(inner, @"\b(WHEN|ELSE)\b", RegexOptions.IgnoreCase);
            var sb = new StringBuilder();
            bool first = true;

            for (int k = 0; k < tokens.Length; k++)
            {
                string token = tokens[k].Trim();
                if (string.IsNullOrEmpty(token)) continue;

                string upperToken = token.ToUpper();
                if (upperToken == "WHEN")
                {
                    k++;
                    if (k >= tokens.Length) break;
                    string whenBody = tokens[k].Trim();

                    int thenIdx = whenBody.IndexOf(" THEN ", StringComparison.OrdinalIgnoreCase);
                    if (thenIdx >= 0)
                    {
                        string condition = whenBody.Substring(0, thenIdx).Trim();
                        string action = whenBody.Substring(thenIdx + 6).Trim();

                        string formattedWhen = FormatKeyword("WHEN", options);
                        string formattedThen = FormatKeyword("THEN", options);

                        if (!first) sb.AppendLine();
                        sb.Append(nextIndent + formattedWhen + " " + condition);

                        if (options.CaseWhenThenNewLine)
                        {
                            sb.AppendLine();
                            sb.Append(thenIndent + formattedThen + " " + action);
                        }
                        else
                        {
                            sb.Append(" " + formattedThen + " " + action);
                        }
                        first = false;
                    }
                }
                else if (upperToken == "ELSE")
                {
                    k++;
                    if (k >= tokens.Length) break;
                    string elseBody = tokens[k].Trim();
                    string formattedElse = FormatKeyword("ELSE", options);

                    if (!first) sb.AppendLine();
                    sb.Append(nextIndent + formattedElse + " " + elseBody);
                    first = false;
                }
            }
            return sb.ToString();
        }

        private static string ToPascalCase(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var words = text.Split(' ');
            return string.Join(" ", words.Select(w => w.Length > 0 ? char.ToUpper(w[0]) + w.Substring(1).ToLower() : w));
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

        private static string FormatKeyword(string word, FormatterOptions options)
        {
            if (string.IsNullOrEmpty(word)) return word;
            if (options.KeywordCasing.Equals("upper", StringComparison.OrdinalIgnoreCase))
                return word.ToUpper();
            if (options.KeywordCasing.Equals("lower", StringComparison.OrdinalIgnoreCase))
                return word.ToLower();
            if (options.KeywordCasing.Equals("pascal", StringComparison.OrdinalIgnoreCase))
                return ToPascalCase(word);
            return word; // Preserve
        }

        private static bool ContainsKeywordAtDepthZero(string text, string[] keywords)
        {
            if (string.IsNullOrEmpty(text)) return false;

            int depth = 0;
            var wordSb = new StringBuilder();
            bool inSingleLineComment = false;
            bool inMultiLineComment = false;
            bool inString = false;
            char stringChar = '\0';

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (inSingleLineComment)
                {
                    if (c == '\n') inSingleLineComment = false;
                    continue;
                }
                if (inMultiLineComment)
                {
                    if (c == '*' && i + 1 < text.Length && text[i + 1] == '/')
                    {
                        inMultiLineComment = false;
                        i++;
                    }
                    continue;
                }
                if (inString)
                {
                    if (c == stringChar)
                    {
                        if (c == '\'' && i + 1 < text.Length && text[i + 1] == '\'')
                        {
                            i++;
                        }
                        else
                        {
                            inString = false;
                        }
                    }
                    continue;
                }

                if (c == '-' && i + 1 < text.Length && text[i + 1] == '-')
                {
                    inSingleLineComment = true;
                    i++;
                    continue;
                }
                if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
                {
                    inMultiLineComment = true;
                    i++;
                    continue;
                }
                if (c == '\'' || c == '"')
                {
                    inString = true;
                    stringChar = c;
                    continue;
                }

                if (c == '(')
                {
                    depth++;
                    wordSb.Clear();
                    continue;
                }
                if (c == ')')
                {
                    depth = Math.Max(0, depth - 1);
                    wordSb.Clear();
                    continue;
                }

                if (depth == 0)
                {
                    if (char.IsLetter(c))
                    {
                        wordSb.Append(c);
                    }
                    else
                    {
                        if (wordSb.Length > 0)
                        {
                            string word = wordSb.ToString().ToUpper();
                            if (keywords.Contains(word)) return true;
                            wordSb.Clear();
                        }
                    }
                }
            }

            if (wordSb.Length > 0)
            {
                string word = wordSb.ToString().ToUpper();
                if (keywords.Contains(word)) return true;
            }

            return false;
        }

        private static string FormatOneZeroClause(string inner, FormatterOptions options, int currentIndentLevel)
        {
            var parts = SplitByAndOrOutsideParens(inner);
            var sb = new StringBuilder();

            sb.Append(parts[0].Trim());

            string nextIndent = new string(' ', (currentIndentLevel + 1) * options.IndentSize);

            for (int i = 1; i < parts.Count; i++)
            {
                string part = parts[i].Trim();
                if (string.IsNullOrEmpty(part)) continue;

                if (string.Equals(part, "OR", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(part, "AND", StringComparison.OrdinalIgnoreCase))
                {
                    string formattedOp = FormatKeyword(part, options);
                    sb.AppendLine();
                    sb.Append(nextIndent + formattedOp + " ");
                }
                else
                {
                    sb.Append(part);
                }
            }

            return sb.ToString();
        }

        private static List<string> SplitByAndOrOutsideParens(string input)
        {
            var result = new List<string>();
            var current = new StringBuilder();
            int depth = 0;
            bool inSingleLineComment = false;
            bool inMultiLineComment = false;
            bool inString = false;
            char stringChar = '\0';

            int i = 0;
            while (i < input.Length)
            {
                char c = input[i];

                if (inSingleLineComment)
                {
                    current.Append(c);
                    if (c == '\n') inSingleLineComment = false;
                    i++;
                    continue;
                }
                if (inMultiLineComment)
                {
                    current.Append(c);
                    if (c == '*' && i + 1 < input.Length && input[i + 1] == '/')
                    {
                        inMultiLineComment = false;
                        current.Append('/');
                        i += 2;
                    }
                    else
                    {
                        i++;
                    }
                    continue;
                }
                if (inString)
                {
                    current.Append(c);
                    if (c == stringChar)
                    {
                        if (c == '\'' && i + 1 < input.Length && input[i + 1] == '\'')
                        {
                            current.Append('\'');
                            i += 2;
                        }
                        else
                        {
                            inString = false;
                            i++;
                        }
                    }
                    else
                    {
                        i++;
                    }
                    continue;
                }

                if (c == '-' && i + 1 < input.Length && input[i + 1] == '-')
                {
                    inSingleLineComment = true;
                    current.Append("--");
                    i += 2;
                    continue;
                }
                if (c == '/' && i + 1 < input.Length && input[i + 1] == '*')
                {
                    inMultiLineComment = true;
                    current.Append("/*");
                    i += 2;
                    continue;
                }
                if (c == '\'' || c == '"')
                {
                    inString = true;
                    stringChar = c;
                    current.Append(c);
                    i++;
                    continue;
                }

                if (c == '(')
                {
                    depth++;
                    current.Append(c);
                    i++;
                    continue;
                }
                if (c == ')')
                {
                    depth = Math.Max(0, depth - 1);
                    current.Append(c);
                    i++;
                    continue;
                }

                if (depth == 0)
                {
                    bool isAnd = IsWordAt(input, i, "AND");
                    bool isOr = IsWordAt(input, i, "OR");
                    if (isAnd || isOr)
                    {
                        string word = isAnd ? "AND" : "OR";
                        result.Add(current.ToString());
                        current.Clear();
                        result.Add(word);
                        i += word.Length;
                        continue;
                    }
                }

                current.Append(c);
                i++;
            }

            if (current.Length > 0)
            {
                result.Add(current.ToString());
            }

            return result;
        }

        private static bool IsWordAt(string text, int index, string word)
        {
            if (index + word.Length > text.Length) return false;
            if (index > 0 && (char.IsLetterOrDigit(text[index - 1]) || text[index - 1] == '_')) return false;
            for (int i = 0; i < word.Length; i++)
            {
                if (char.ToUpper(text[index + i]) != word[i]) return false;
            }
            int nextIndex = index + word.Length;
            if (nextIndex < text.Length && (char.IsLetterOrDigit(text[nextIndex]) || text[nextIndex] == '_')) return false;

            return true;
        }
    }
}
