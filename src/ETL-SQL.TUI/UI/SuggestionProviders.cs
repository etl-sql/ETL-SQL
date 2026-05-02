using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.IO;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Common;
using System.Threading.Tasks;

namespace ETL_SQL.TUI.UI
{
    /// <summary>Categorizes the type of an autocomplete suggestion for UI prioritization and styling.</summary>
    public enum SuggestionType
    {
        Keyword,
        Function,
        Table,
        Column,
        Alias,
        Variable,
        FilePath,
        OptionName,
        OptionValue,
        Connection
    }

    /// <summary>Represents a single autocomplete suggestion.</summary>
    /// <param name="Text">The text to insert.</param>
    /// <param name="Type">The category of the suggestion.</param>
    public record Suggestion(string Text, SuggestionType Type);

    /// <summary>Contains the full script context required for generating suggestions.</summary>
    public class SuggestionContext
    {
        public string Prefix { get; set; } = "";
        public string FullScript { get; set; } = "";
        public string ScriptBefore { get; set; } = "";
        public IDictionary<string, IDataSource> Connections { get; set; } = new Dictionary<string, IDataSource>();
        public IDictionary<string, AliasInfo> Aliases { get; set; } = new Dictionary<string, AliasInfo>();
        public IDictionary<string, List<string>> VirtualSchemas { get; set; } = new Dictionary<string, List<string>>();
        public ILogger? Logger { get; set; }
    }

    /// <summary>Interface for components that provide autocomplete suggestions.</summary>
    public interface ISuggestionProvider
    {
        /// <summary>Asynchronously generates suggestions based on the provided context.</summary>
        Task<IEnumerable<Suggestion>> GetSuggestionsAsync(SuggestionContext context);
    }

    public class KeywordProvider : ISuggestionProvider
    {
        public Task<IEnumerable<Suggestion>> GetSuggestionsAsync(SuggestionContext context)
        {
            var results = new List<Suggestion>();
            if (string.IsNullOrEmpty(context.Prefix) || context.Prefix == "*" || context.Prefix.EndsWith(".*"))
                return Task.FromResult<IEnumerable<Suggestion>>(results);

            var keywords = LanguageMetadata.GetAllKeywords();
            results.AddRange(keywords.Where(k => k.StartsWith(context.Prefix, StringComparison.OrdinalIgnoreCase))
                                     .Select(k => new Suggestion(k, SuggestionType.Keyword)));
            
            results.AddRange(LanguageMetadata.Functions.Where(f => f.StartsWith(context.Prefix, StringComparison.OrdinalIgnoreCase))
                                                       .Select(f => new Suggestion(f, SuggestionType.Function)));
            
            results.AddRange(LanguageMetadata.DataTypes.Where(d => d.StartsWith(context.Prefix, StringComparison.OrdinalIgnoreCase))
                                                       .Select(d => new Suggestion(d, SuggestionType.Keyword))); 
            return Task.FromResult<IEnumerable<Suggestion>>(results);
        }
    }

    public class AliasColumnProvider : ISuggestionProvider
    {
        public async Task<IEnumerable<Suggestion>> GetSuggestionsAsync(SuggestionContext context)
        {
            var results = new List<Suggestion>();
            if (string.IsNullOrEmpty(context.Prefix)) return results;

            if (context.Prefix == "*")
            {
                var allCols = new List<string>();
                foreach (var info in context.Aliases.Values)
                {
                    IEnumerable<string>? tableCols = null;
                    if (context.Connections.TryGetValue(info.TableName, out var dsAll)) tableCols = await dsAll.GetColumnsAsync();
                    else if (context.VirtualSchemas.TryGetValue(info.TableName, out var vcolsAll)) tableCols = vcolsAll;
                    else if (info.TableName.Contains("."))
                    {
                        var partsTable = info.TableName.Split('.');
                        if (context.Connections.TryGetValue(partsTable[0], out var dbDS) && dbDS is IDatabaseSource db)
                        {
                            tableCols = await db.GetColumnsAsync(partsTable[1]);
                        }
                    }
                    else
                    {
                        // Final fallback: Check if the name IS a table inside any of the connections
                        // This handles JOIN Orders where Orders is in the only defined connection.
                        foreach (var ds in context.Connections.Values.OfType<IDatabaseSource>())
                        {
                            var tables = await ds.GetTablesAsync();
                            if (tables.Contains(info.TableName, StringComparer.OrdinalIgnoreCase))
                            {
                                tableCols = await ds.GetColumnsAsync(info.TableName);
                                break;
                            }
                        }
                    }
                    
                    if (tableCols != null)
                    {
                        var aliasStr = info.HasExplicitAlias ? info.Alias : info.TableName;
                        allCols.AddRange(tableCols.Select(c => $"{aliasStr}.{c.Trim('[', ']', '\"', '\'')}"));
                    }
                }
                
                if (allCols.Any())
                {
                    results.Add(new Suggestion(string.Join(", ", allCols.Distinct()), SuggestionType.Column));
                    return results;
                }
            }

            if (context.Prefix.EndsWith(".*"))
            {
                var aliasForStar = context.Prefix.Substring(0, context.Prefix.Length - 2);
                IEnumerable<string>? colsForStar = null;
                if (context.Aliases.TryGetValue(aliasForStar, out var infoStar))
                {
                    if (context.Connections.TryGetValue(infoStar.TableName, out var dsStar)) colsForStar = await dsStar.GetColumnsAsync();
                    else if (context.VirtualSchemas.TryGetValue(infoStar.TableName, out var vcolsStar)) colsForStar = vcolsStar;
                    else if (infoStar.TableName.Contains("."))
                    {
                        var partsStar = infoStar.TableName.Split('.');
                        if (context.Connections.TryGetValue(partsStar[0], out var dbDS) && dbDS is IDatabaseSource db)
                        {
                            colsForStar = await db.GetColumnsAsync(partsStar[1]);
                        }
                    }
                }
                else if (context.Connections.TryGetValue(aliasForStar, out var connDSStar))
                {
                    colsForStar = await connDSStar.GetColumnsAsync();
                }

                if (colsForStar != null)
                {
                    var joined = string.Join(", ", colsForStar.Select(c => $"{aliasForStar}.{c.Trim('[', ']', '\"', '\'')}"));
                    results.Add(new Suggestion(joined, SuggestionType.Column));
                    return results;
                }
            }

            if (!context.Prefix.Contains(".")) return results;
            
            var parts = context.Prefix.Split('.');
            var aliasName = parts[0];
            try
            {
                if (context.Aliases.TryGetValue(aliasName, out var info))
                {
                    if (context.Connections.TryGetValue(info.TableName, out var ds))
                    {
                        var cols = await ds.GetColumnsAsync();
                        results.AddRange(cols.Select(c => new Suggestion($"{aliasName}.{c.Trim('[', ']', '\"', '\'')}", SuggestionType.Column)));
                    }
                    else if (context.VirtualSchemas.TryGetValue(info.TableName, out var vcols))
                    {
                        results.AddRange(vcols.Select(c => new Suggestion($"{aliasName}.{c}", SuggestionType.Column)));
                    }
                    else if (info.TableName.Contains("."))
                    {
                        var partsTable = info.TableName.Split('.');
                        if (context.Connections.TryGetValue(partsTable[0], out var dbDS) && dbDS is IDatabaseSource db)
                        {
                            var cols = await db.GetColumnsAsync(partsTable[1]);
                            results.AddRange(cols.Select(c => new Suggestion($"{aliasName}.{c.Trim('[', ']', '\"', '\'')}", SuggestionType.Column)));
                        }
                    }
                }

                if (context.Connections.TryGetValue(aliasName, out var connDS))
                {
                    var cols = await connDS.GetColumnsAsync();
                    results.AddRange(cols.Select(c => new Suggestion($"{aliasName}.{c.Trim('[', ']', '\"', '\'')}", SuggestionType.Column)));
                }
            }
            catch (Exception ex) { context.Logger?.Debug($"[AliasColumnProvider] Column resolution error: {ex.Message}"); }

            return results;
        }
    }

    public class FilePathProvider : ISuggestionProvider
    {
        public Task<IEnumerable<Suggestion>> GetSuggestionsAsync(SuggestionContext context)
        {
            try
            {
                int len = Math.Max(0, context.FullScript.Length - context.Prefix.Length);
                var isInsideFileParen = Regex.IsMatch(context.FullScript.Substring(0, len), @"\b(FILE|CSV|FLATFILE|DATABASE|EXCEL|JSON|XML)\s*\(\s*['""]?[^'""\)]*$", RegexOptions.IgnoreCase);
                bool isFilePathContext = context.Prefix.Contains("/") || context.Prefix.Contains("\\") || isInsideFileParen;
                
                if (!isFilePathContext) return Task.FromResult(Enumerable.Empty<Suggestion>());

                return Task.FromResult(ETLSuggestEngine.GetFileSuggestions(context.Prefix, context.Logger).Select(s => new Suggestion(s, SuggestionType.FilePath)));
            }
            catch (Exception ex) { context.Logger?.Debug($"[FilePathProvider] File path suggestion error: {ex.Message}"); return Task.FromResult(Enumerable.Empty<Suggestion>()); }
        }
    }

    public class DatabaseSchemaProvider : ISuggestionProvider
    {
        public async Task<IEnumerable<Suggestion>> GetSuggestionsAsync(SuggestionContext context)
        {
            var results = new List<Suggestion>();
            try
            {
                // Rule 4: Prefer live connections from the context cache.
                // This ensures "m." works even for connections created in previous REPL turns
                // or loaded from session state.
                foreach (var kvp in context.Connections)
                {
                    var connName = kvp.Key;
                    var dataSource = kvp.Value;
                    
                    try 
                    {
                        var tables = (await dataSource.GetTablesAsync()).ToList();
                        results.AddRange(tables.Select(t => new Suggestion($"{connName}.{t}", SuggestionType.Table)));
                        
                        if (context.Prefix.StartsWith($"{connName}.", StringComparison.OrdinalIgnoreCase))
                        {
                            var tablePref = context.Prefix.Substring(connName.Length + 1);
                            if (tablePref.Contains(".") && dataSource is IDatabaseSource dbSource)
                            {
                                var partsPref = tablePref.Split('.');
                                if (tables.Any(t => t.Equals(partsPref[0], StringComparison.OrdinalIgnoreCase)))
                                {
                                    results.AddRange((await dbSource.GetColumnsAsync(partsPref[0])).Select(c => new Suggestion($"{connName}.{partsPref[0]}.{c}", SuggestionType.Column)));
                                }
                            }
                            else 
                            {
                                results.AddRange(tables.Where(t => t.StartsWith(tablePref, StringComparison.OrdinalIgnoreCase)).Select(t => new Suggestion($"{connName}.{t}", SuggestionType.Table)));
                            }
                        }
                    }
                    catch (Exception ex) { context.Logger?.Debug($"[DatabaseSchemaProvider] Connection error for '{connName}': {ex.Message}"); }
                }

                // Fallback to Regex for connections defined in the current script but not yet executed
                var connMatches = Regex.Matches(context.FullScript, @"CREATE\s+CONNECTION\s+(\w+)\s+ON\s+(\w+)", RegexOptions.IgnoreCase);
                foreach (Match m in connMatches)
                {
                    var connName = m.Groups[1].Value;
                    if (context.Connections.ContainsKey(connName)) continue; // Already handled by live cache
                    
                    var type = m.Groups[2].Value;
                    results.Add(new Suggestion(connName, SuggestionType.Connection));
                }
            }
            catch (Exception ex) { context.Logger?.Debug($"[DatabaseSchemaProvider] Schema discovery error: {ex.Message}"); }
            return results;
        }
    }

    public class WithClauseProvider : ISuggestionProvider
    {
        public Task<IEnumerable<Suggestion>> GetSuggestionsAsync(SuggestionContext context)
        {
            var results = new List<Suggestion>();
            try
            {
                // Check if we are inside a WITH (...) block
                var scriptBefore = context.ScriptBefore;
                int lastWith = scriptBefore.LastIndexOf("WITH", StringComparison.OrdinalIgnoreCase);
                if (lastWith < 0) return Task.FromResult<IEnumerable<Suggestion>>(results);

                string afterWith = scriptBefore.Substring(lastWith);
                int openParen = afterWith.IndexOf('(');
                int closeParen = afterWith.IndexOf(')');

                // We must be between ( and ) or after ( if no ) exists yet
                if (openParen >= 0 && (closeParen < 0 || openParen > closeParen))
                {
                    // 1. Dynamic Option Discovery: Find the connector type
                    // Reverse scan for "ON <Type>" before the WITH
                    string beforeWith = scriptBefore.Substring(0, lastWith);
                    var onMatch = Regex.Match(beforeWith, @"\bON\s+(\w+)\b", RegexOptions.IgnoreCase | RegexOptions.RightToLeft);
                    
                    if (onMatch.Success)
                    {
                        string connectorType = onMatch.Groups[1].Value.ToUpperInvariant();
                        var connector = ConnectorRegistry.Instance!.GetConnector(connectorType);
                        if (connector != null)
                        {
                            var supportedOptions = connector.GetSupportedOptions();
                            var usedOptions = Regex.Matches(afterWith, @"\b(\w+)\s*=", RegexOptions.IgnoreCase)
                                .Cast<Match>().Select(m => m.Groups[1].Value.ToUpperInvariant()).ToHashSet();

                            results.AddRange(supportedOptions.Keys
                                .Where(o => !usedOptions.Contains(o.ToUpperInvariant()))
                                .Select(o => new Suggestion(o, SuggestionType.OptionName)));
                        }
                    }

                    // 2. Option Value Suggestions: After <OptionName> =
                    var lineBefore = context.ScriptBefore.TrimEnd();
                    var optionValueMatch = Regex.Match(lineBefore, @"\b(\w+)\s*=\s*['""]?(\w*)$", RegexOptions.IgnoreCase);
                    if (optionValueMatch.Success)
                    {
                        string optionName = optionValueMatch.Groups[1].Value.ToUpperInvariant();
                        
                        // Check plugin values first
                        var pluginValues = ConnectorRegistry.Instance!.GetAllConnectorOptionValues();
                        if (pluginValues.TryGetValue(optionName, out var values))
                        {
                            results.AddRange(values.Select(v => new Suggestion(v, SuggestionType.OptionValue)));
                        }

                        // Add common defaults for standard options
                        if (optionName == "FORMAT" || optionName == "TYPE")
                        {
                            results.AddRange(new[] { "CSV", "JSON", "XML", "PARQUET", "AVRO", "EXCEL", "FLATFILE" }.Select(v => new Suggestion(v, SuggestionType.OptionValue)));
                        }
                        else if (optionName == "DELIMITER" || optionName == "FIELDTERMINATOR")
                        {
                            results.AddRange(new[] { "COMMA", "PIPE", "TAB", "SEMICOLON" }.Select(v => new Suggestion(v, SuggestionType.OptionValue)));
                        }
                        else if (optionName == "HEADER" || optionName == "FIRSTROW" || optionName == "STRICT_SCHEMA" || optionName == "TRUSTED_CONNECTION")
                        {
                            results.AddRange(new[] { "TRUE", "FALSE" }.Select(v => new Suggestion(v, SuggestionType.OptionValue)));
                        }
                    }
                }
            }
            catch (Exception ex) { context.Logger?.Debug($"[WithClauseProvider] Option discovery error: {ex.Message}"); }
            return Task.FromResult<IEnumerable<Suggestion>>(results);
        }
    }

    public class PatternProvider : ISuggestionProvider
    {
        public async Task<IEnumerable<Suggestion>> GetSuggestionsAsync(SuggestionContext context)
        {
            var results = new List<Suggestion>();
            try
            {
                // TokenWindow logic: Get last 5 tokens before cursor
                var tokens = GetLastTokens(context.ScriptBefore, 5);
                if (!tokens.Any()) return results;

                var last = tokens.Last();
                var prev1 = tokens.Count > 1 ? tokens[tokens.Count - 2] : null;
                var prev2 = tokens.Count > 2 ? tokens[tokens.Count - 3] : null;

                // 1. CREATE [TOPIC]
                if (last.Text.Equals("CREATE", StringComparison.OrdinalIgnoreCase))
                {
                    results.AddRange(new[] { "CONNECTION", "TABLE", "VISUAL", "PAGE", "DATASET", "STYLE", "CONTAINER", "NAVIGATION", "JOB", "DIRECTORY", "PROCEDURE", "FUNCTION", "INDEX" }
                        .Select(k => new Suggestion(k, SuggestionType.Keyword)));
                }
                // 2. CREATE CONNECTION [name] ON
                else if (last.Text.Equals("ON", StringComparison.OrdinalIgnoreCase) && prev2?.Text.Equals("CONNECTION", StringComparison.OrdinalIgnoreCase) == true)
                {
                    results.AddRange(ConnectorRegistry.Instance!.GetRegisteredNames().Select(c => new Suggestion(c, SuggestionType.Connection)));
                }
                // 3. FROM / JOIN / INTO / UPDATE
                else if (last.Text.Equals("FROM", StringComparison.OrdinalIgnoreCase) || 
                         last.Text.Equals("JOIN", StringComparison.OrdinalIgnoreCase) || 
                         last.Text.Equals("INTO", StringComparison.OrdinalIgnoreCase) || 
                         last.Text.Equals("UPDATE", StringComparison.OrdinalIgnoreCase))
                {
                    results.AddRange(context.Connections.Keys.Select(c => new Suggestion(c, SuggestionType.Connection)));
                    results.AddRange(context.VirtualSchemas.Keys.Select(v => new Suggestion(v, SuggestionType.Table)));
                    foreach (var conn in context.Connections.Values)
                    {
                        if (conn is IDatabaseSource db)
                        {
                            try { results.AddRange((await db.GetTablesAsync()).Select(t => new Suggestion(t, SuggestionType.Table))); } catch { }
                            try { results.AddRange((await db.GetViewsAsync()).Select(v => new Suggestion(v, SuggestionType.Table))); } catch { }
                        }
                    }
                }
                // 4. SET [TOPIC]
                else if (last.Text.Equals("SET", StringComparison.OrdinalIgnoreCase))
                {
                    results.AddRange(new[] { "WHAT_IF", "PROFILING", "REPORT", "BATCH_SIZE", "STRICT_SCHEMA", "MAX_ERRORS" }
                        .Select(k => new Suggestion(k, SuggestionType.Keyword)));
                }
                // 5. SET WHAT_IF / PROFILING
                else if (last.Text.Equals("WHAT_IF", StringComparison.OrdinalIgnoreCase) || last.Text.Equals("PROFILING", StringComparison.OrdinalIgnoreCase))
                {
                    results.AddRange(new[] { "ON", "OFF" }.Select(k => new Suggestion(k, SuggestionType.Keyword)));
                }
                // 6. Generic "After Keyword" fallback (similar to old ContextAwareProvider)
                else if (last.IsKeyword)
                {
                    // Fallback to basic connector/table/alias suggestions if no specific pattern matched
                    results.AddRange(context.Connections.Keys.Select(c => new Suggestion(c, SuggestionType.Connection)));
                    results.AddRange(context.VirtualSchemas.Keys.Select(v => new Suggestion(v, SuggestionType.Table)));
                    results.AddRange(context.Aliases.Keys.Select(a => new Suggestion(a, SuggestionType.Alias)));
                }
            }
            catch (Exception ex) { context.Logger?.Debug($"[PatternProvider] Error: {ex.Message}"); }
            return results;
        }

        private List<TokenInfo> GetLastTokens(string text, int count)
        {
            // Simple tokenization for suggestions (not a full Lexer run)
            var matches = Regex.Matches(text, @"(@?\w+|==|!=|<=|>=|[=<>+\-*/().,;])")
                .Cast<Match>()
                .Select(m => new TokenInfo(m.Value))
                .ToList();

            return matches.Skip(Math.Max(0, matches.Count - count)).ToList();
        }

        private record TokenInfo(string Text)
        {
            public bool IsKeyword => LanguageMetadata.IsKeyword(Text);
        }
    }

    public class VariableProvider : ISuggestionProvider
    {
        public Task<IEnumerable<Suggestion>> GetSuggestionsAsync(SuggestionContext context)
        {
            try
            {
                var varMatches = Regex.Matches(context.FullScript, @"(@\w+)");
                return Task.FromResult<IEnumerable<Suggestion>>(varMatches.Cast<Match>().Select(m => new Suggestion(m.Value, SuggestionType.Variable)).Distinct());
            }
            catch (Exception ex) { context.Logger?.Debug($"[VariableProvider] Variable discovery error: {ex.Message}"); return Task.FromResult(Enumerable.Empty<Suggestion>()); }
        }
    }

    /// <summary>
    /// Orchestrates multiple suggestion providers to generate a final, prioritized list of completion candidates.
    /// </summary>
    public class SuggestionEngine
    {
        private readonly List<ISuggestionProvider> _providers = new()
        {
            new FilePathProvider(),
            new AliasColumnProvider(),
            new WithClauseProvider(),
            new DatabaseSchemaProvider(),
            new PatternProvider(),
            new KeywordProvider(),
            new VariableProvider()
        };

        /// <summary>Asynchronously gathers, filters, and prioritizes suggestions from all registered providers.</summary>
        /// <param name="context">The script context at the cursor position.</param>
        /// <returns>A prioritized list of suggestions.</returns>
        public async Task<List<Suggestion>> GetSuggestionsAsync(SuggestionContext context)
        {
            var allSuggestions = new List<Suggestion>();
            foreach (var provider in _providers)
            {
                try { allSuggestions.AddRange(await provider.GetSuggestionsAsync(context)); }
                catch (Exception ex) { context.Logger?.Debug($"[SuggestionEngine] Provider {provider.GetType().Name} error: {ex.Message}"); }
            }

            // When the prefix ends with ".*" (Ctrl+Space wildcard expansion), the
            // AliasColumnProvider returns a single joined suggestion like "m.col1, m.col2"
            // which does NOT start with "m.*" — skip the prefix filter in that case.
            bool skipPrefixFilter = context.Prefix.EndsWith(".*", StringComparison.OrdinalIgnoreCase);
            var filtered = allSuggestions
                .Where(s => skipPrefixFilter
                         || string.IsNullOrEmpty(context.Prefix)
                         || s.Text.StartsWith(context.Prefix, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Special case: If we have OptionValue hits, we are likely after an '=' inside WITH.
            // In this specific context, we should suppress generic keywords, tables, etc.
            if (filtered.Any(s => s.Type == SuggestionType.OptionValue))
            {
                return filtered.Where(s => s.Type == SuggestionType.OptionValue)
                    .GroupBy(s => s.Text)
                    .Select(g => g.First())
                    .OrderBy(s => s.Text)
                    .ToList();
            }

            var candidates = new Dictionary<string, Suggestion>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in filtered)
            {
                if (!candidates.ContainsKey(s.Text))
                    candidates[s.Text] = s;
            }
            
            return candidates.Values
                .OrderBy(c => GetTypePriority(c.Type))
                .ThenBy(c => c.Text)
                .ToList();
        }

        /// <summary>Assigns a sorting priority based on the suggestion type.</summary>
        private int GetTypePriority(SuggestionType type)
        {
            return type switch
            {
                SuggestionType.OptionValue => 0,
                SuggestionType.Keyword => 1,
                SuggestionType.OptionName => 2,
                SuggestionType.Connection => 3,
                SuggestionType.Table => 4,
                SuggestionType.Alias => 5,
                SuggestionType.Variable => 6,
                SuggestionType.Function => 7,
                SuggestionType.Column => 10,
                _ => 20
            };
        }
    }
}
