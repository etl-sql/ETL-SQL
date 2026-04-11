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
            results.AddRange(LanguageMetadata.GetAllKeywords().Select(k => new Suggestion(k, SuggestionType.Keyword)));
            results.AddRange(LanguageMetadata.Functions.Select(f => new Suggestion(f, SuggestionType.Function)));
            results.AddRange(LanguageMetadata.DataTypes.Select(d => new Suggestion(d, SuggestionType.Keyword))); // Data types are also keywords for auto-complete
            return Task.FromResult<IEnumerable<Suggestion>>(results);
        }
    }

    public class AliasColumnProvider : ISuggestionProvider
    {
        public async Task<IEnumerable<Suggestion>> GetSuggestionsAsync(SuggestionContext context)
        {
            var results = new List<Suggestion>();
            if (string.IsNullOrEmpty(context.Prefix)) return results;

            if (context.Prefix.EndsWith(".*"))
            {
                var aliasForStar = context.Prefix.Substring(0, context.Prefix.Length - 2);
                IEnumerable<string>? colsForStar = null;
                if (context.Aliases.TryGetValue(aliasForStar, out var infoStar))
                {
                    if (context.Connections.TryGetValue(infoStar.TableName, out var dsStar)) colsForStar = await dsStar.GetColumnsAsync();
                    else if (context.VirtualSchemas.TryGetValue(infoStar.TableName, out var vcolsStar)) colsForStar = vcolsStar;
                }
                else if (context.Connections.TryGetValue(aliasForStar, out var connDSStar))
                {
                    colsForStar = await connDSStar.GetColumnsAsync();
                }
                else if (context.VirtualSchemas.TryGetValue(aliasForStar, out var vcolsDirectStar))
                {
                    colsForStar = vcolsDirectStar;
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
                else if (context.VirtualSchemas.TryGetValue(aliasName, out var vcolsDirect))
                {
                    results.AddRange(vcolsDirect.Select(c => new Suggestion($"{aliasName}.{c}", SuggestionType.Column)));
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
                // Support both quoted and unquoted connection targets, and optional targets
                var connMatches = Regex.Matches(context.FullScript, @"CREATE\s+CONNECTION\s+(\w+)\s+ON\s+(\w+)(?:\s+(?:TO|FOR)?\s*(?:'([^']*)'|(\w+)))?", RegexOptions.IgnoreCase);
                
                foreach (Match m in connMatches)
                {
                    var connName = m.Groups[1].Value;
                    var type = m.Groups[2].Value;
                    var connStr = m.Groups[3].Value;
                    if (string.IsNullOrEmpty(connStr)) connStr = m.Groups[4].Value; 
                    
                    var connector = ConnectorRegistry.Instance!.GetConnector(type);
                    if (connector != null)
                    {
                        try
                        {
                            var tables = (await connector.GetTablesAsync(connStr)).ToList();
                            results.AddRange(tables.Select(t => new Suggestion($"{connName}.{t}", SuggestionType.Table)));
                            
                            if (context.Prefix.StartsWith($"{connName}.", StringComparison.OrdinalIgnoreCase))
                            {
                                var tablePref = context.Prefix.Substring(connName.Length + 1);
                                if (tablePref.Contains("."))
                                {
                                    var partsPref = tablePref.Split('.');
                                    // If we have a full table name in the list, suggest its columns
                                    if (tables.Any(t => t.Equals(tablePref, StringComparison.OrdinalIgnoreCase)))
                                    {
                                        try { results.AddRange((await connector.GetColumnsAsync(connStr, tablePref)).Select(c => new Suggestion($"{connName}.{tablePref}.{c}", SuggestionType.Column))); }
                                        catch (Exception ex) { context.Logger?.Debug($"[DatabaseSchemaProvider] GetColumnsAsync error for {connName}.{tablePref}: {ex.Message}"); }
                                    }
                                    else if (partsPref.Length > 0 && tables.Any(t => t.StartsWith(tablePref, StringComparison.OrdinalIgnoreCase)))
                                    {
                                         // It's a partial schema/table name, suggest next level
                                         var nextLevelParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                         foreach (var t in tables)
                                         {
                                             if (t.StartsWith(tablePref, StringComparison.OrdinalIgnoreCase))
                                             {
                                                 var tParts = t.Split('.');
                                                 if (tParts.Length > partsPref.Length - 1)
                                                 {
                                                     nextLevelParts.Add(string.Join(".", tParts.Take(partsPref.Length)));
                                                 }
                                             }
                                         }
                                         results.AddRange(nextLevelParts.Select(p => new Suggestion($"{connName}.{p}", SuggestionType.Table)));
                                    }
                                }
                                else
                                {
                                    // Suggest top-level parts (either full table names or first part of multi-part names)
                                    var topLevelParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                    foreach (var t in tables)
                                    {
                                        var tParts = t.Split('.');
                                        topLevelParts.Add(tParts[0]);
                                    }
                                    results.AddRange(topLevelParts.Select(p => new Suggestion($"{connName}.{p}", SuggestionType.Table)));
                                }
                            }
                        }
                        catch (Exception ex) { context.Logger?.Debug($"[DatabaseSchemaProvider] Connector error for '{connName}': {ex.Message}"); }
                    }
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
                var upperScriptBefore = context.ScriptBefore.ToUpperInvariant();
                int withIndex = upperScriptBefore.LastIndexOf("WITH", StringComparison.OrdinalIgnoreCase);
                
                if (withIndex >= 0)
                {
                    string afterWith = upperScriptBefore.Substring(withIndex);
                    int openParen = afterWith.IndexOf('(');
                    
                    if (openParen >= 0)
                    {
                        var lastCreateConnPart = upperScriptBefore.Substring(0, withIndex);
                        var matches = Regex.Matches(lastCreateConnPart, @"ON\s+(\w+)", RegexOptions.IgnoreCase);
                        if (matches.Cast<Match>().Any())
                        {
                            string type = matches.Cast<Match>().Last().Groups[1].Value.ToUpperInvariant();
                            var connector = ConnectorRegistry.Instance!.GetConnector(type);
                            if (connector != null)
                            {
                                var options = connector.GetSupportedOptions();
                                var usedOptions = Regex.Matches(afterWith, @"(\w+)\s*=", RegexOptions.IgnoreCase)
                                    .Cast<Match>().Select(m => m.Groups[1].Value.ToUpperInvariant()).ToHashSet();
                                
                                results.AddRange(options.Keys.Where(o => !usedOptions.Contains(o.ToUpperInvariant())).Select(o => new Suggestion(o, SuggestionType.OptionName)));
                            }
                        }

                        var lineBefore = context.ScriptBefore.TrimEnd();
                        var optionMatch = Regex.Match(lineBefore, @"(\w+)\s*=\s*\w*$", RegexOptions.IgnoreCase);
                        if (optionMatch.Success)
                        {
                            string optionName = optionMatch.Groups[1].Value.ToUpperInvariant();
                            var pluginValues = ConnectorRegistry.Instance!.GetAllConnectorOptionValues();
                            if (pluginValues.TryGetValue(optionName, out var values))
                            {
                                results.AddRange(values.Select(v => new Suggestion(v, SuggestionType.OptionValue)));
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { context.Logger?.Debug($"[WithClauseProvider] Option discovery error: {ex.Message}"); }
            return Task.FromResult<IEnumerable<Suggestion>>(results);
        }
    }

    public class ContextAwareProvider : ISuggestionProvider
    {
        public async Task<IEnumerable<Suggestion>> GetSuggestionsAsync(SuggestionContext context)
        {
            var results = new List<Suggestion>();
            try
            {
                var lastTokens = Regex.Matches(context.ScriptBefore, @"\b\w+\b").Cast<Match>().Select(m => m.Value.ToUpperInvariant()).ToList();
                string? prevWord = lastTokens.Count > 0 ? lastTokens.Last() : null;

                if (prevWord == "CREATE" || prevWord == "DROP" || prevWord == "ALTER")
                {
                    results.AddRange(new[] { "TABLE", "CONNECTION", "PROCEDURE", "FUNCTION", "INDEX", "DIRECTORY" }.Select(k => new Suggestion(k, SuggestionType.Keyword)));
                }
                else if (prevWord == "ON")
                {
                    results.AddRange(ConnectorRegistry.Instance!.GetRegisteredNames().Select(c => new Suggestion(c, SuggestionType.Connection)));
                }
                else if (prevWord == "FROM" || prevWord == "JOIN" || prevWord == "INTO" || prevWord == "UPDATE")
                {
                    results.AddRange(context.Connections.Keys.Select(c => new Suggestion(c, SuggestionType.Connection)));
                    results.AddRange(context.VirtualSchemas.Keys.Select(v => new Suggestion(v, SuggestionType.Table)));
                    foreach (var conn in context.Connections.Values)
                    {
                        if (conn is IDatabaseSource db)
                        {
                            try { results.AddRange((await db.GetTablesAsync()).Select(t => new Suggestion(t, SuggestionType.Table))); } catch (Exception ex) { context.Logger?.Debug($"[ContextAwareProvider] GetTablesAsync error: {ex.Message}"); }
                            try { results.AddRange((await db.GetViewsAsync()).Select(v => new Suggestion(v, SuggestionType.Table))); } catch (Exception ex) { context.Logger?.Debug($"[ContextAwareProvider] GetViewsAsync error: {ex.Message}"); }
                        }
                    }
                    results.AddRange(context.Aliases.Keys.Select(a => new Suggestion(a, SuggestionType.Alias)));
                }
                else
                {
                    results.AddRange(context.Connections.Keys.Select(c => new Suggestion(c, SuggestionType.Connection)));
                    results.AddRange(context.VirtualSchemas.Keys.Select(v => new Suggestion(v, SuggestionType.Table)));
                    foreach (var conn in context.Connections.Values)
                    {
                        if (conn is IDatabaseSource db)
                        {
                            try { results.AddRange((await db.GetTablesAsync()).Select(t => new Suggestion(t, SuggestionType.Table))); } catch (Exception ex) { context.Logger?.Debug($"[ContextAwareProvider] GetTablesAsync error: {ex.Message}"); }
                            try { results.AddRange((await db.GetViewsAsync()).Select(v => new Suggestion(v, SuggestionType.Table))); } catch (Exception ex) { context.Logger?.Debug($"[ContextAwareProvider] GetViewsAsync error: {ex.Message}"); }
                        }
                    }
                    results.AddRange(context.Aliases.Keys.Select(a => new Suggestion(a, SuggestionType.Alias)));
                }
            }
            catch (Exception ex) { context.Logger?.Debug($"[ContextAwareProvider] Context awareness error: {ex.Message}"); }
            return results;
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
            new ContextAwareProvider(),
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
