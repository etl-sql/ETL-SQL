using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using Spectre.Console;

namespace ETL_SQL.TUI.UI
{
    /// <summary>
    /// Provides static engine logic for generating autocomplete suggestions, highlight lines, and parsing script metadata (aliases, virtual schemas).
    /// </summary>
    public class ETLSuggestEngine
    {
        /// <summary>Parses the script to identify temporary tables and CTEs (Virtual Schemas).</summary>
        /// <param name="script">The ETL-SQL script text.</param>
        /// <returns>A dictionary of table names and their column lists.</returns>
        public static Dictionary<string, List<string>> ParseVirtualSchemas(string script)
        {
            var schemas = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            // 1. Handle SELECT ... INTO #temp
            var selectIntoMatches = Regex.Matches(script, @"SELECT\s+(.*?)\bINTO\s+(#?\w+)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            foreach (Match m in selectIntoMatches)
            {
                var columnsPart = m.Groups[1].Value;
                var tableName = m.Groups[2].Value;

                var columns = new List<string>();
                // This is a naive split by comma, ignoring nested commas in parens/functions for now. 
                // A full AST parse would be better but static analysis is faster.
                var colSpecs = Regex.Split(columnsPart, @",(?![^(]*\))");
                foreach (var spec in colSpecs)
                {
                    var trimmed = spec.Trim();
                    // Match alias (AS name) or simple identifier or field access
                    var colMatch = Regex.Match(trimmed, @"(?:\w+\.)?(\w+|\*)$|(?:\bAS\s+)(\w+)$", RegexOptions.IgnoreCase);
                    if (colMatch.Success)
                    {
                        var col = !string.IsNullOrEmpty(colMatch.Groups[2].Value) ? colMatch.Groups[2].Value : colMatch.Groups[1].Value;
                        if (col != "*") columns.Add(col.Trim('[', ']', '\"', '\''));
                    }
                }
                schemas[tableName] = columns;
            }

            // 2. Handle CREATE TABLE #temp (col1 type, col2 type)
            var createTableMatches = Regex.Matches(script, @"CREATE\s+TABLE\s+(#\w+)\s*\((.*?)\)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            foreach (Match m in createTableMatches)
            {
                var tableName = m.Groups[1].Value;
                var colsPart = m.Groups[2].Value;
                // Match identifiers that appear at start or after a comma
                var colNames = Regex.Matches(colsPart, @"(?:^|,)\s*([#\w]+|\[[^\]]+\]|""[^""]+"")", RegexOptions.IgnoreCase)
                    .Cast<Match>().Select(cm => cm.Groups[1].Value.Trim('[', ']', '\"', '\'')).ToList();
                if (colNames.Any()) schemas[tableName] = colNames;
            }

            return schemas;
        }

        /// <summary>Scans the script for table aliases and their base table mappings.</summary>
        public static Dictionary<string, AliasInfo> ParseAliases(string script, int cursorOffset = -1) => AliasScanner.Scan(script, cursorOffset);

        /// <summary>Applies Spectre.Console markup to a script line for syntax highlighting in the TUI.</summary>
        /// <param name="fullLine">The full raw script line (not just the visible part).</param>
        /// <param name="scrollCol">The horizontal scroll offset.</param>
        /// <param name="width">The width of the visible editor area.</param>
        /// <param name="startsInMultiline">Whether the line starts inside a multiline comment.</param>
        /// <param name="endsInMultiline">Output: whether the line ends inside a multiline comment.</param>
        /// <param name="aliases">Optional pre-scanned aliases for semantic highlighting.</param>
        /// <returns>A string with Spectre.Console color tags, clipped to the visible area.</returns>
        public static string HighlightLine(string fullLine, int scrollCol, int width, bool startsInMultiline, out bool endsInMultiline, IDictionary<string, AliasInfo>? aliases = null)
        {
            endsInMultiline = startsInMultiline;
            if (string.IsNullOrEmpty(fullLine)) return "";

            var theme = TuiTheme.Instance.Syntax;
            var tokens = new List<(int Start, int Length, string MarkupPrefix, bool IsSecret)>();
            int pos = 0;
            string lastWord = "";

            while (pos < fullLine.Length)
            {
                if (endsInMultiline)
                {
                    int endIdx = fullLine.IndexOf("*/", pos);
                    if (endIdx >= 0)
                    {
                        TokenizeComment(fullLine.Substring(pos, endIdx + 2 - pos), pos, tokens);
                        pos = endIdx + 2;
                        endsInMultiline = false;
                    }
                    else
                    {
                        TokenizeComment(fullLine.Substring(pos), pos, tokens);
                        pos = fullLine.Length;
                    }
                    continue;
                }

                // Main tokenization regex
                var match = Regex.Match(fullLine.Substring(pos), @"^(--.*|\/\*[\s\S]*?\*\/|\/\*[\s\S]*|'[^']*'|""[^""]*""|\[[^\]]*\]|[#@\w\.]+|\s+|.)");
                if (!match.Success)
                {
                    tokens.Add((pos, 1, "", false));
                    pos++;
                    continue;
                }

                string word = match.Value;
                bool isSecret = false;

                if (word.StartsWith("--"))
                {
                    TokenizeComment(word, pos, tokens);
                }
                else if (word.StartsWith("/*"))
                {
                    TokenizeComment(word, pos, tokens);
                    if (!word.EndsWith("*/")) endsInMultiline = true;
                }
                else if (word.StartsWith("'") || word.StartsWith("\""))
                {
                    if (lastWord == "PASSWORD" || lastWord == "API_KEY" || lastWord == "SECRET" || lastWord == "CONNECTION_STRING")
                    {
                        isSecret = true;
                    }
                    tokens.Add((pos, word.Length, theme.String, isSecret));
                }
                else if (word.StartsWith("["))
                {
                    tokens.Add((pos, word.Length, theme.Bracket, false));
                }
                else if (word.Equals("DOCKER", StringComparison.OrdinalIgnoreCase) || word.Contains("CONNECTION_STRING", StringComparison.OrdinalIgnoreCase))
                {
                    tokens.Add((pos, word.Length, theme.Docker, false));
                }
                else if (LanguageMetadata.DmlKeywords.Contains(word.ToUpper()))
                    tokens.Add((pos, word.Length, theme.DmlKeyword, false));
                else if (LanguageMetadata.DdlKeywords.Contains(word.ToUpper()))
                    tokens.Add((pos, word.Length, theme.DdlKeyword, false));
                else if (LanguageMetadata.ControlFlowKeywords.Contains(word.ToUpper()))
                    tokens.Add((pos, word.Length, theme.ControlFlow, false));
                else if (LanguageMetadata.JoinKeywords.Contains(word.ToUpper()))
                    tokens.Add((pos, word.Length, theme.JoinKeyword, false));
                else if (LanguageMetadata.OperatorKeywords.Contains(word.ToUpper()))
                    tokens.Add((pos, word.Length, theme.OperatorKeyword, false));
                else if (LanguageMetadata.Keywords.Contains(word.ToUpper()))
                    tokens.Add((pos, word.Length, theme.OtherKeyword, false));
                else if (LanguageMetadata.IsDataType(word))
                    tokens.Add((pos, word.Length, theme.DataType, false));
                else if (LanguageMetadata.IsFunction(word))
                    tokens.Add((pos, word.Length, theme.Function, false));
                else if (word.StartsWith("@"))
                    tokens.Add((pos, word.Length, theme.Variable, false));
                else if (aliases != null && aliases.TryGetValue(word, out var info))
                {
                    if (info.HasExplicitAlias && word.Equals(info.Alias, StringComparison.OrdinalIgnoreCase))
                        tokens.Add((pos, word.Length, theme.Alias, false));
                    else if (!info.HasExplicitAlias || word.Equals(info.TableName, StringComparison.OrdinalIgnoreCase))
                        tokens.Add((pos, word.Length, theme.Table, false));
                    else
                        tokens.Add((pos, word.Length, "", false));
                }
                else
                {
                    tokens.Add((pos, word.Length, "", false));
                }

                // Update last word state for sensitive field detection
                string trimmed = word.Trim();
                if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith("--") && !trimmed.StartsWith("/"))
                {
                    // Only update lastWord if it's a potential identifier/keyword
                    if (Regex.IsMatch(trimmed, @"^[a-zA-Z_][a-zA-Z0-9_]*$"))
                    {
                        lastWord = trimmed.ToUpperInvariant();
                    }
                }

                pos += word.Length;
            }

            // Clip and build markup
            var result = new StringBuilder();
            int visibleEnd = scrollCol + width;

            foreach (var t in tokens)
            {
                int tokenEnd = t.Start + t.Length;
                if (tokenEnd <= scrollCol || t.Start >= visibleEnd) continue;

                int clipStart = Math.Max(t.Start, scrollCol);
                int clipEnd = Math.Min(tokenEnd, visibleEnd);

                string visiblePart;
                if (t.IsSecret && t.Length >= 2)
                {
                    // Mask everything between the quotes, preserving length for cursor stability
                    var sb = new StringBuilder();
                    for (int i = clipStart; i < clipEnd; i++)
                    {
                        if (i == t.Start || i == tokenEnd - 1) sb.Append(fullLine[i]);
                        else sb.Append('*');
                    }
                    visiblePart = sb.ToString();
                }
                else
                {
                    visiblePart = fullLine.Substring(clipStart, clipEnd - clipStart);
                }

                if (!string.IsNullOrEmpty(t.MarkupPrefix)) result.Append($"[{t.MarkupPrefix}]");
                result.Append(Markup.Escape(visiblePart));
                if (!string.IsNullOrEmpty(t.MarkupPrefix)) result.Append("[/]");
            }

            return result.ToString();
        }

        private static void TokenizeComment(string text, int offset, List<(int Start, int Length, string MarkupPrefix, bool IsSecret)> tokens)
        {
            var tagRegex = new Regex(@"(@\w+):\s*([^;*]+)(;)?", RegexOptions.Compiled);
            var matches = tagRegex.Matches(text);
            var theme = TuiTheme.Instance.Syntax;

            if (matches.Count == 0)
            {
                tokens.Add((offset, text.Length, theme.Comment, false));
                return;
            }

            int lastPos = 0;
            foreach (Match m in matches)
            {
                if (m.Index > lastPos)
                    tokens.Add((offset + lastPos, m.Index - lastPos, theme.Comment, false));

                // @tag
                tokens.Add((offset + m.Index, m.Groups[1].Length, theme.CommentTag, false));

                // :
                tokens.Add((offset + m.Index + m.Groups[1].Length, 1, theme.Comment, false));

                // value
                int valueStart = m.Groups[2].Index;
                tokens.Add((offset + valueStart, m.Groups[2].Length, theme.CommentValue, false));

                // ;
                if (m.Groups[3].Success)
                    tokens.Add((offset + m.Groups[3].Index, 1, theme.Comment, false));

                lastPos = m.Index + m.Length;
            }

            if (lastPos < text.Length)
                tokens.Add((offset + lastPos, text.Length - lastPos, theme.Comment, false));
        }


        /// <summary>Asynchronously generates a list of suggestions for a given prefix and script context.</summary>
        /// <param name="prefix">The word prefix at the cursor.</param>
        /// <param name="fullScript">The full content of the editor buffer.</param>
        /// <param name="connections">The active set of data sources.</param>
        /// <returns>A list of prioritized suggestions.</returns>
        public static async Task<List<Suggestion>> GetSuggestionsAsync(string prefix, string fullScript, IDictionary<string, IDataSource> connections, MetadataManager? tuiMetadataManager = null, ILogger? logger = null, Core.Interfaces.ILanguageHelpRegistry? helpRegistry = null)
        {
            var scriptBefore = fullScript;
            if (!string.IsNullOrEmpty(prefix) && fullScript.EndsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                scriptBefore = fullScript.Substring(0, fullScript.Length - prefix.Length);
            }

            var context = new SuggestionContext
            {
                Prefix = prefix ?? "",
                FullScript = fullScript,
                ScriptBefore = scriptBefore,
                Connections = connections,
                Aliases = ParseAliases(fullScript),
                VirtualSchemas = ParseVirtualSchemas(fullScript),
                Logger = logger
            };

            var engine = new SuggestionEngine(helpRegistry, tuiMetadataManager);
            return await engine.GetSuggestionsAsync(context);
        }

        /// <summary>Generates a list of file and directory paths matching the given prefix.</summary>
        /// <param name="prefix">The partial path prefix.</param>
        /// <returns>A list of matching paths, with directories ending in '/'.</returns>
        public static List<string> GetFileSuggestions(string prefix, ILogger? logger = null)
        {
            var suggestions = new List<string>();
            try
            {
                // Normalize slashes for processing
                prefix = prefix?.Replace("\\", "/") ?? "";
                string dir = ".";
                string searchPattern = prefix;

                if (prefix.Contains("/"))
                {
                    int lastSlash = prefix.LastIndexOf('/');
                    dir = prefix.Substring(0, lastSlash);

                    // Root resolution
                    if (string.IsNullOrEmpty(dir))
                    {
                        dir = "/";
                    }
                    else if (dir.Length == 2 && dir[1] == ':' && char.IsLetter(dir[0]))
                    {
                        // "C:" -> "C:/"
                        dir += "/";
                    }

                    searchPattern = prefix.Substring(lastSlash + 1);
                }
                else if (prefix.Length == 2 && prefix[1] == ':' && char.IsLetter(prefix[0]))
                {
                    // User typed "C:", treat as root of C
                    dir = prefix + "/";
                    searchPattern = "";
                }

                // Security/OS Hardening: On Windows, "/" alone refers to current drive root.
                // We ensure Directory.Exists works consistently.
                if (Directory.Exists(dir))
                {
                    var entries = string.IsNullOrEmpty(searchPattern)
                        ? Directory.GetFileSystemEntries(dir)
                        : Directory.GetFileSystemEntries(dir, searchPattern + "*");

                    foreach (var entry in entries)
                    {
                        var name = Path.GetFileName(entry);
                        if (string.IsNullOrEmpty(name)) continue;

                        // Filter out noise
                        if (name.StartsWith("$") ||
                            name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("Documents and Settings", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("Config.Msi", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("pagefile.sys", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("hiberfil.sys", StringComparison.OrdinalIgnoreCase)) continue;

                        bool isDir = Directory.Exists(entry);

                        // Construct the suggested path relative to what the user typed
                        string result;
                        if (dir == "." || dir == "./")
                        {
                            result = name;
                        }
                        else
                        {
                            var separator = dir.EndsWith("/") ? "" : "/";
                            result = dir + separator + name;
                        }

                        if (isDir) result += "/";

                        // Strip leading ./ for aesthetics
                        if (result.StartsWith("./")) result = result.Substring(2);

                        suggestions.Add(result);
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Debug($"[ETLSuggestEngine.GetFileSuggestions] File system error for prefix '{prefix}': {ex.Message}");
            }
            return suggestions.OrderBy(s => s.EndsWith("/") ? 0 : 1).ThenBy(s => s).ToList();
        }

        public static bool IsKeyword(string word) => LanguageMetadata.IsKeyword(word);
    }
}
