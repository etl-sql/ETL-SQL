using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.IO;
using System.Threading.Tasks;
using Spectre.Console;

using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Common;
using ETL_SQL.Core.Parser;

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
        public static Dictionary<string, AliasInfo> ParseAliases(string script) => AliasScanner.Scan(script);

        /// <summary>Applies Spectre.Console markup to a single line for syntax highlighting in the TUI.</summary>
        /// <param name="line">The raw script line.</param>
        /// <param name="aliases">Optional pre-scanned aliases for semantic highlighting.</param>
        /// <returns>A string with Spectre.Console color tags.</returns>
        public static string HighlightLine(string line, IDictionary<string, AliasInfo>? aliases = null)
        {
            if (string.IsNullOrWhiteSpace(line)) return line;
            
            var result = new System.Text.StringBuilder();
            // Regex to capture comments, strings, identifiers, whitespace, and symbols
            var matches = Regex.Matches(line, @"(--.*|\/\*[\s\S]*?\*\/|'[^']*'|""[^""]*""|\[[^\]]*\]|[#@\w\.]+|\s+|[^\w\s])");
            
            foreach (Match m in matches)
            {
                var word = m.Value;
                
                // 1. Comments (with Tag Support)
                if (word.StartsWith("--"))
                {
                    result.Append($"[grey70]{Markup.Escape(word)}[/]");
                    continue;
                }
                if (word.StartsWith("/*"))
                {
                    // Check for tags: /* @tag: value; */
                    var tagRegex = new Regex(@"(@[a-zA-Z0-9_]+):\s*([^;]+);", RegexOptions.Compiled);
                    var tagMatches = tagRegex.Matches(word);

                    if (tagMatches.Count > 0)
                    {
                        int lastPos = 0;
                        foreach (Match tm in tagMatches)
                        {
                            // Text before the tag
                            string pre = word.Substring(lastPos, tm.Index - lastPos);
                            if (pre.Length > 0) result.Append($"[grey70]{Markup.Escape(pre)}[/]");

                            // @tag (Purple)
                            result.Append($"[mediumpurple]{Markup.Escape(tm.Groups[1].Value)}[/]");
                            result.Append($"[grey70]:[/] ");

                            // value (Orange)
                            result.Append($"[darkorange3]{Markup.Escape(tm.Groups[2].Value.Trim())}[/]");
                            result.Append($"[grey70];[/]");

                            lastPos = tm.Index + tm.Length;
                        }
                        // Text after the last tag
                        if (lastPos < word.Length) result.Append($"[grey70]{Markup.Escape(word.Substring(lastPos))}[/]");
                    }
                    else
                    {
                        result.Append($"[grey70]{Markup.Escape(word)}[/]");
                    }
                    continue;
                }

                // 2. Strings (Orange)
                if (word.StartsWith("'") || word.StartsWith("\""))
                {
                    result.Append($"[darkorange3]{Markup.Escape(word)}[/]");
                }
                // 3. Brackets (Identified objects)
                else if (word.StartsWith("["))
                {
                    result.Append($"[cyan]{Markup.Escape(word)}[/]");
                }
                // 4. Special internal keywords (Docker, etc)
                else if (word.Equals("DOCKER", StringComparison.OrdinalIgnoreCase) || word.Contains("CONNECTION_STRING", StringComparison.OrdinalIgnoreCase))
                {
                    result.Append($"[orange1]{word}[/]");
                }
                // 5. Categorized Keywords
                else if (LanguageMetadata.DmlKeywords.Contains(word))
                    result.Append($"[bold blue]{word}[/]");
                else if (LanguageMetadata.DdlKeywords.Contains(word))
                    result.Append($"[bold plum1]{word}[/]");
                else if (LanguageMetadata.ControlFlowKeywords.Contains(word))
                    result.Append($"[bold gold1]{word}[/]");
                else if (LanguageMetadata.JoinKeywords.Contains(word))
                    result.Append($"[bold springgreen3]{word}[/]");
                else if (LanguageMetadata.OperatorKeywords.Contains(word))
                    result.Append($"[bold plum3]{word}[/]");
                else if (LanguageMetadata.Keywords.Contains(word))
                    result.Append($"[blue]{word}[/]");
                // 6. Data Types
                else if (LanguageMetadata.IsDataType(word))
                    result.Append($"[mediumpurple]{word}[/]");
                // 7. Functions
                else if (LanguageMetadata.IsFunction(word))
                    result.Append($"[yellow]{word}[/]");
                // 8. Variables
                else if (word.StartsWith("@"))
                    result.Append($"[green]{word}[/]");
                // 9. Aliases & Tables
                else if (aliases != null && aliases.TryGetValue(word, out var info))
                {
                    if (info.HasExplicitAlias && word.Equals(info.Alias, StringComparison.OrdinalIgnoreCase))
                        result.Append($"[purple]{word}[/]");
                    else if (!info.HasExplicitAlias || word.Equals(info.TableName, StringComparison.OrdinalIgnoreCase))
                        result.Append($"[cyan]{word}[/]");
                    else
                        result.Append(Markup.Escape(word));
                }
                // 10. Default
                else
                    result.Append(Markup.Escape(word));
            }
            return result.ToString();
        }


        /// <summary>Asynchronously generates a list of suggestions for a given prefix and script context.</summary>
        /// <param name="prefix">The word prefix at the cursor.</param>
        /// <param name="fullScript">The full content of the editor buffer.</param>
        /// <param name="connections">The active set of data sources.</param>
        /// <returns>A list of prioritized suggestions.</returns>
        public static async Task<List<Suggestion>> GetSuggestionsAsync(string prefix, string fullScript, IDictionary<string, IDataSource> connections, ILogger? logger = null)
        {
            var context = new SuggestionContext
            {
                Prefix = prefix ?? "",
                FullScript = fullScript,
                ScriptBefore = fullScript, // In a real editor, this would be text up to cursor
                Connections = connections,
                Aliases = ParseAliases(fullScript),
                VirtualSchemas = ParseVirtualSchemas(fullScript),
                Logger = logger
            };

            var engine = new SuggestionEngine();
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
                prefix = prefix?.Replace("\\", "/") ?? "";
                string dir = ".";
                string searchPattern = prefix;

                if (prefix.Contains("/"))
                {
                    int lastSlash = prefix.LastIndexOf('/');
                    dir = prefix.Substring(0, lastSlash);
                    if (string.IsNullOrEmpty(dir)) dir = "/"; // Root
                    
                    // On Windows, if it's like "C:", Directory.Exists(dir) might fail without a slash
                    if (dir.Length == 2 && dir[1] == ':' && char.IsLetter(dir[0])) dir += "/";

                    searchPattern = prefix.Substring(lastSlash + 1);
                }

                if (Directory.Exists(dir))
                {
                    // Special case: if search pattern is empty, we want all files in the dir
                    var entries = string.IsNullOrEmpty(searchPattern) 
                        ? Directory.GetFileSystemEntries(dir) 
                        : Directory.GetFileSystemEntries(dir, searchPattern + "*");

                    foreach (var entry in entries)
                    {
                        var name = Path.GetFileName(entry);
                        if (name.StartsWith("$") || 
                            name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("Documents and Settings", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("Config.Msi", StringComparison.OrdinalIgnoreCase)) continue;

                        bool isDir = Directory.Exists(entry);
                        var result = (dir == "." ? "" : dir + (dir.EndsWith("/") ? "" : "/")) + name;
                        if (isDir) result += "/";

                        // Strip leading ./ for aesthetics
                        if (result.StartsWith("./")) result = result.Substring(2);
                        
                        suggestions.Add(result);
                    }
                }
            }
            catch (Exception ex) { logger?.Debug($"[ETLSuggestEngine.GetFileSuggestions] File system error for prefix '{prefix}': {ex.Message}"); }
            return suggestions.OrderBy(s => s.EndsWith("/") ? 0 : 1).ThenBy(s => s).ToList();
        }

        public static bool IsKeyword(string word) => LanguageMetadata.IsKeyword(word);
    }
}
