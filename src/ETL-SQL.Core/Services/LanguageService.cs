using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.IO;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Interfaces;
using ETL_SQL.Common;

namespace ETL_SQL.Core.Services
{
    public enum SuggestionType
    {
        Keyword,
        Function,
        Table,
        Column,
        Variable,
        Alias,
        OptionName,
        OptionValue,
        Path,
        Connection
    }

    public record Suggestion(string Text, SuggestionType Type, int Priority = 100, string? Documentation = null);

    public class SuggestionContext
    {
        public string Prefix { get; set; } = "";
        public string FullScript { get; set; } = "";
        public string ScriptBefore { get; set; } = "";
        public string? DocumentUri { get; set; }
        public ILogger? Logger { get; set; }

        public IDictionary<string, AliasInfo> Aliases { get; set; } = new Dictionary<string, AliasInfo>(StringComparer.OrdinalIgnoreCase);
        public IDictionary<string, List<string>> VirtualSchemas { get; set; } = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    }

    public interface ILanguageService
    {
        Task<List<Suggestion>> GetSuggestionsAsync(SuggestionContext context);
    }

    public class LanguageService : ILanguageService
    {
        private readonly IMetadataManager _metadata;
        private readonly Core.Interfaces.ILanguageHelpRegistry? _helpRegistry;

        public LanguageService(IMetadataManager metadata, Core.Interfaces.ILanguageHelpRegistry? helpRegistry = null)
        {
            _metadata = metadata;
            _helpRegistry = helpRegistry;
        }

        public async Task<List<Suggestion>> GetSuggestionsAsync(SuggestionContext context)
        {
            try
            {
                if (context.Aliases.Count == 0 && !string.IsNullOrEmpty(context.FullScript))
                    context.Aliases = AliasScanner.Scan(context.FullScript);

                var allSuggestions = new List<Suggestion>();
                allSuggestions.AddRange(GetFilePathSuggestions(context));
                allSuggestions.AddRange(await GetPatternSuggestionsAsync(context));
                allSuggestions.AddRange(await GetWithClauseSuggestionsAsync(context));
                allSuggestions.AddRange(await GetDatabaseSchemaSuggestionsAsync(context));
                allSuggestions.AddRange(await GetAliasColumnSuggestionsAsync(context));
                allSuggestions.AddRange(GetKeywordSuggestions(context));
                allSuggestions.AddRange(GetVariableSuggestions(context));

                bool skipPrefixFilter = context.Prefix.EndsWith("*", StringComparison.OrdinalIgnoreCase);
                var filtered = allSuggestions
                    .Where(s => skipPrefixFilter || string.IsNullOrEmpty(context.Prefix) || s.Text.StartsWith(context.Prefix, StringComparison.OrdinalIgnoreCase)
                             || (s.Text.Contains(".") && s.Text.Split('.').Last().StartsWith(context.Prefix, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                if (filtered.Any(s => s.Type == SuggestionType.OptionValue))
                    return filtered.Where(s => s.Type == SuggestionType.OptionValue).GroupBy(s => s.Text).Select(g => g.First()).OrderBy(s => s.Text).ToList();

                var candidates = new Dictionary<string, Suggestion>(StringComparer.OrdinalIgnoreCase);
                foreach (var s in filtered)
                    if (!candidates.ContainsKey(s.Text)) candidates[s.Text] = s;

                var finalResults = candidates.Values.OrderBy(c => c.Priority).ThenBy(c => GetTypePriority(c.Type)).ThenBy(c => c.Text).ToList();

                // Enrich with documentation if help registry is available
                if (_helpRegistry != null)
                {
                    foreach (var s in finalResults.Where(x => x.Type == SuggestionType.Keyword || x.Type == SuggestionType.Function))
                    {
                        var help = _helpRegistry.GetHelp(s.Text);
                        if (help != null)
                        {
                            // Using reflection or casting if needed, but for now we just want the summary
                            // s.Documentation = help.Summary; // Suggestion is a record, so we'd need to create a new one
                        }
                    }
                }

                return finalResults;
            }
            catch (Exception ex)
            {
                context.Logger?.Error($"LanguageService error: {ex.Message}");
                return new List<Suggestion>();
            }
        }

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

        private List<Suggestion> GetKeywordSuggestions(SuggestionContext context)
        {
            var results = new List<Suggestion>();
            try
            {
                results.AddRange(LanguageMetadata.GetAllKeywords().Where(k => k.StartsWith(context.Prefix, StringComparison.OrdinalIgnoreCase)).Select(k => new Suggestion(k, SuggestionType.Keyword, Priority: 100)));
                results.AddRange(LanguageMetadata.Functions.Where(f => f.StartsWith(context.Prefix, StringComparison.OrdinalIgnoreCase)).Select(f => new Suggestion(f, SuggestionType.Function, Priority: 110)));
                results.AddRange(LanguageMetadata.DataTypes.Where(d => d.StartsWith(context.Prefix, StringComparison.OrdinalIgnoreCase)).Select(d => new Suggestion(d, SuggestionType.Keyword, Priority: 120)));
            } catch {}
            return results;
        }

        private List<Suggestion> GetVariableSuggestions(SuggestionContext context)
        {
            try { return Regex.Matches(context.FullScript, @"(@\w+)").Cast<Match>().Select(m => new Suggestion(m.Value, SuggestionType.Variable)).Distinct().ToList(); }
            catch { return new List<Suggestion>(); }
        }

        private List<Suggestion> GetFilePathSuggestions(SuggestionContext context)
        {
            var lineBefore = context.ScriptBefore.Split('\n').LastOrDefault() ?? "";
            int lastQuote = Math.Max(lineBefore.LastIndexOf('\''), lineBefore.LastIndexOf('"'));
            if (lastQuote < 0) return new List<Suggestion>();
            string afterQuote = lineBefore.Substring(lastQuote + 1);
            if (afterQuote.Count(c => c == '\'' || c == '"') % 2 != 0) return new List<Suggestion>();
            string pathPrefix = afterQuote.Replace("\\", "/");
            var results = new List<Suggestion>();
            string dir = ".";
            string searchPattern = pathPrefix;
            if (pathPrefix.Contains("/")) { int lastSlash = pathPrefix.LastIndexOf('/'); dir = pathPrefix.Substring(0, lastSlash); if (string.IsNullOrEmpty(dir)) dir = "/"; else if (dir.Length == 2 && dir[1] == ':' && char.IsLetter(dir[0])) dir += "/"; searchPattern = pathPrefix.Substring(lastSlash + 1); }
            else if (pathPrefix.Length == 2 && pathPrefix[1] == ':' && char.IsLetter(pathPrefix[0])) { dir = pathPrefix + "/"; searchPattern = ""; }
            if (Directory.Exists(dir)) { try { foreach (var entry in Directory.GetFileSystemEntries(dir, string.IsNullOrEmpty(searchPattern) ? "*" : searchPattern + "*")) { var name = Path.GetFileName(entry); if (string.IsNullOrEmpty(name) || name.StartsWith("$")) continue; string res = (dir == "." || dir == "./") ? name : (dir.EndsWith("/") ? dir + name : dir + "/" + name); if (Directory.Exists(entry)) res += "/"; if (res.StartsWith("./")) res = res.Substring(2); results.Add(new Suggestion(res, SuggestionType.Path, Priority: 0)); } } catch {} }
            return results;
        }

        private async Task<List<Suggestion>> GetPatternSuggestionsAsync(SuggestionContext context)
        {
            var results = new List<Suggestion>();
            try
            {
                var tokens = GetLastTokens(context.ScriptBefore, 5);
                if (!tokens.Any()) return results;
                var last = tokens.Last();
                var prev1 = tokens.Count > 1 ? tokens[tokens.Count - 2] : null;
                var prev2 = tokens.Count > 2 ? tokens[tokens.Count - 3] : null;
                if (last.Text.Equals("CREATE", StringComparison.OrdinalIgnoreCase)) results.AddRange(new[] { "CONNECTION", "TABLE", "VISUAL", "PAGE", "DATASET", "STYLE", "CONTAINER", "NAVIGATION", "JOB", "DIRECTORY", "PROCEDURE", "FUNCTION", "INDEX" }.Select(k => new Suggestion(k, SuggestionType.Keyword, Priority: 0)));
                else if (last.Text.Equals("ON", StringComparison.OrdinalIgnoreCase) && prev2?.Text.Equals("CONNECTION", StringComparison.OrdinalIgnoreCase) == true) results.AddRange(_metadata.GetRegisteredNames().Select(c => new Suggestion(c, SuggestionType.Connection, Priority: 0)));
                else if (last.Text.Equals("FROM", StringComparison.OrdinalIgnoreCase) || last.Text.Equals("JOIN", StringComparison.OrdinalIgnoreCase) || last.Text.Equals("INTO", StringComparison.OrdinalIgnoreCase) || last.Text.Equals("UPDATE", StringComparison.OrdinalIgnoreCase))
                {
                    var conns = _metadata.GetConnections(context.DocumentUri);
                    results.AddRange(conns.Select(c => new Suggestion(c.Name, SuggestionType.Connection, Priority: 0)));
                    foreach (var conn in conns) { try { var tables = await _metadata.GetTablesAsync(conn.Name, context.DocumentUri); var filteredTables = tables.Where(t => string.IsNullOrEmpty(context.Prefix) || t.StartsWith(context.Prefix, StringComparison.OrdinalIgnoreCase)).ToList(); results.AddRange(filteredTables.Select(t => new Suggestion(t, SuggestionType.Table, Priority: 5))); results.AddRange(filteredTables.Select(t => new Suggestion($"{conn.Name}.{t}", SuggestionType.Table, Priority: 2))); } catch {} }
                }
                else if (context.Prefix.EndsWith("*"))
                {
                    var starMatch = Regex.Match(context.Prefix, @"(?:(\w+)\.)?\*$", RegexOptions.IgnoreCase);
                    if (starMatch.Success)
                    {
                        var alias = starMatch.Groups[1].Value;
                        var tables = string.IsNullOrEmpty(alias) ? context.Aliases.Values.Distinct().ToList() : context.Aliases.Values.Where(a => (a.Alias?.Equals(alias, StringComparison.OrdinalIgnoreCase) == true) || (string.IsNullOrEmpty(a.Alias) && a.TableName.Equals(alias, StringComparison.OrdinalIgnoreCase))).Distinct().ToList();
                        var allCols = new List<string>();
                        foreach (var info in tables) { var conn = info.ConnectionName ?? info.TableName; var table = info.BaseTableName ?? info.TableName; var cols = (await _metadata.GetColumnsAsync(conn, table, context.DocumentUri)).ToList(); if (cols.Count == 0 && conn.Equals(table, StringComparison.OrdinalIgnoreCase)) { var sub = await _metadata.GetTablesAsync(conn, context.DocumentUri); foreach (var st in sub) cols.AddRange(await _metadata.GetColumnsAsync(conn, st, context.DocumentUri)); if (cols.Count == 0) cols = (await _metadata.GetColumnsAsync(conn, "", context.DocumentUri)).ToList(); } if (cols.Any()) allCols.AddRange(cols.Select(c => $"{(string.IsNullOrEmpty(info.Alias) ? info.TableName : info.Alias)}.{c}")); }
                        if (allCols.Any()) results.Add(new Suggestion(string.Join(", ", allCols.Distinct()), SuggestionType.Column, Priority: 0));
                    }
                }
                else if (last.Text.Equals("SET", StringComparison.OrdinalIgnoreCase)) results.AddRange(new[] { "WHAT_IF", "PROFILING", "REPORT", "BATCH_SIZE", "STRICT_SCHEMA", "MAX_ERRORS" }.Select(k => new Suggestion(k, SuggestionType.Keyword, Priority: 0)));
                else if (last.Text.Equals("WHAT_IF", StringComparison.OrdinalIgnoreCase) || last.Text.Equals("PROFILING", StringComparison.OrdinalIgnoreCase)) results.AddRange(new[] { "ON", "OFF" }.Select(k => new Suggestion(k, SuggestionType.Keyword, Priority: 0)));
                else if (prev1?.Text.Equals("FROM", StringComparison.OrdinalIgnoreCase) == true || prev1?.Text.Equals("JOIN", StringComparison.OrdinalIgnoreCase) == true || prev1?.Text.Equals("INTO", StringComparison.OrdinalIgnoreCase) == true || prev1?.Text.Equals("UPDATE", StringComparison.OrdinalIgnoreCase) == true)
                {
                    var conns = _metadata.GetConnections(context.DocumentUri);
                    results.AddRange(conns.Select(c => new Suggestion(c.Name, SuggestionType.Connection, Priority: 0)));
                    foreach (var conn in conns) { try { results.AddRange((await _metadata.GetTablesAsync(conn.Name, context.DocumentUri)).Select(t => new Suggestion(t, SuggestionType.Table, Priority: 2))); } catch {} }
                }
                else if (last.IsKeyword)
                {
                    var conns = _metadata.GetConnections(context.DocumentUri);
                    results.AddRange(conns.Select(c => new Suggestion(c.Name, SuggestionType.Connection, Priority: 10)));
                    results.AddRange(context.Aliases.Keys.Select(a => new Suggestion(a, SuggestionType.Alias, Priority: 12)));
                }
            } catch {}
            return results;
        }

        private async Task<List<Suggestion>> GetWithClauseSuggestionsAsync(SuggestionContext context)
        {
            var results = new List<Suggestion>();
            try
            {
                int lastWith = context.ScriptBefore.LastIndexOf("WITH", StringComparison.OrdinalIgnoreCase);
                if (lastWith < 0) return results;
                string afterWith = context.ScriptBefore.Substring(lastWith);
                int openParen = afterWith.IndexOf('(');
                int closeParen = afterWith.IndexOf(')');
                if (openParen >= 0 && (closeParen < 0 || openParen > closeParen))
                {
                    var onMatch = Regex.Match(context.ScriptBefore.Substring(0, lastWith), @"\bON\s+(\w+)\b", RegexOptions.IgnoreCase | RegexOptions.RightToLeft);
                    if (onMatch.Success) { var conn = _metadata.GetConnector(onMatch.Groups[1].Value.ToUpperInvariant()); if (conn != null) { var used = Regex.Matches(afterWith, @"\b(\w+)\s*=", RegexOptions.IgnoreCase).Cast<Match>().Select(m => m.Groups[1].Value.ToUpperInvariant()).ToHashSet(); results.AddRange(conn.GetSupportedOptions().Keys.Where(o => !used.Contains(o.ToUpperInvariant())).Select(o => new Suggestion(o, SuggestionType.OptionName))); } }
                    var valMatch = Regex.Match(context.ScriptBefore.TrimEnd(), @"\b(\w+)\s*=\s*['""]?(\w*)$", RegexOptions.IgnoreCase);
                    if (valMatch.Success) { string opt = valMatch.Groups[1].Value.ToUpperInvariant(); if (opt == "FORMAT" || opt == "TYPE") results.AddRange(new[] { "CSV", "JSON", "XML", "PARQUET", "AVRO", "EXCEL", "FLATFILE" }.Select(v => new Suggestion(v, SuggestionType.OptionValue, Priority: 0))); else if (opt == "DELIMITER" || opt == "FIELDTERMINATOR") results.AddRange(new[] { "COMMA", "PIPE", "TAB", "SEMICOLON" }.Select(v => new Suggestion(v, SuggestionType.OptionValue, Priority: 0))); else if (opt == "HEADER" || opt == "FIRSTROW" || opt == "STRICT_SCHEMA" || opt == "TRUSTED_CONNECTION") results.AddRange(new[] { "TRUE", "FALSE" }.Select(v => new Suggestion(v, SuggestionType.OptionValue, Priority: 0))); else if (opt == "TEXT_QUALIFIER" || opt == "QUALIFIER") results.AddRange(new[] { "DOUBLEQUOTE", "DOUBLEQUOTES", "SINGLEQUOTE", "NONE" }.Select(v => new Suggestion(v, SuggestionType.OptionValue, Priority: 0))); }
                }
            } catch {}
            return results;
        }

        private async Task<List<Suggestion>> GetDatabaseSchemaSuggestionsAsync(SuggestionContext context)
        {
            var results = new List<Suggestion>();
            try
            {
                results.AddRange(context.VirtualSchemas.Keys.Select(k => new Suggestion(k, SuggestionType.Table, Priority: 15)));
                var conns = _metadata.GetConnections(context.DocumentUri);
                foreach (var conn in conns)
                {
                    if (!context.Prefix.Contains(".")) results.Add(new Suggestion(conn.Name, SuggestionType.Connection, Priority: 5));
                    if (context.Prefix.StartsWith($"{conn.Name}.", StringComparison.OrdinalIgnoreCase))
                    {
                        var tables = (await _metadata.GetTablesAsync(conn.Name, context.DocumentUri)).ToList();
                        var pref = context.Prefix.Substring(conn.Name.Length + 1);
                        if (pref.Contains(".")) { var parts = pref.Split('.'); if (tables.Any(t => t.Equals(parts[0], StringComparison.OrdinalIgnoreCase))) results.AddRange((await _metadata.GetColumnsAsync(conn.Name, parts[0], context.DocumentUri)).Select(c => new Suggestion($"{conn.Name}.{parts[0]}.{c}", SuggestionType.Column, Priority: 10))); }
                        else results.AddRange(tables.Where(t => t.StartsWith(pref, StringComparison.OrdinalIgnoreCase)).Select(t => new Suggestion($"{conn.Name}.{t}", SuggestionType.Table, Priority: 8)));
                    }
                    else if (string.IsNullOrEmpty(context.Prefix) || conn.Name.StartsWith(context.Prefix, StringComparison.OrdinalIgnoreCase)) { var tables = await _metadata.GetTablesAsync(conn.Name, context.DocumentUri); results.AddRange(tables.Select(t => new Suggestion($"{conn.Name}.{t}", SuggestionType.Table, Priority: 20))); }
                }
            } catch {}
            return results;
        }

        private async Task<List<Suggestion>> GetAliasColumnSuggestionsAsync(SuggestionContext context)
        {
            var results = new List<Suggestion>();
            try
            {
                if (string.IsNullOrEmpty(context.Prefix)) return results;
                if (context.Prefix == "*")
                {
                    var allCols = new List<string>();
                    foreach (var info in context.Aliases.Values) { var cols = await _metadata.GetColumnsAsync(info.ConnectionName ?? info.TableName, info.BaseTableName ?? info.TableName, context.DocumentUri); var alias = info.HasExplicitAlias ? info.Alias : info.TableName; allCols.AddRange(cols.Select(c => $"{alias}.{c.Trim('[', ']', '\"', '\'')}")); }
                    if (allCols.Any()) { results.Add(new Suggestion(string.Join(", ", allCols.Distinct()), SuggestionType.Column)); return results; }
                }
                if (!context.Prefix.Contains(".")) return results;
                var parts = context.Prefix.Split('.');
                var aliasName = parts[0];
                if (context.Aliases.TryGetValue(aliasName, out var infoAlias))
                {
                    if (context.VirtualSchemas.TryGetValue(infoAlias.TableName, out var vCols)) results.AddRange(vCols.Select(c => new Suggestion($"{aliasName}.{c.Trim('[', ']', '\"', '\'')}", SuggestionType.Column)));
                    else { var conn = infoAlias.ConnectionName ?? infoAlias.TableName; var table = infoAlias.BaseTableName ?? infoAlias.TableName; var cols = (await _metadata.GetColumnsAsync(conn, table, context.DocumentUri)).ToList(); if (cols.Count == 0 && conn.Equals(table, StringComparison.OrdinalIgnoreCase)) { var sub = await _metadata.GetTablesAsync(conn, context.DocumentUri); foreach (var t in sub) cols.AddRange(await _metadata.GetColumnsAsync(conn, t, context.DocumentUri)); if (cols.Count == 0) cols = (await _metadata.GetColumnsAsync(conn, "", context.DocumentUri)).ToList(); } results.AddRange(cols.Select(c => new Suggestion($"{aliasName}.{c.Trim('[', ']', '\"', '\'')}", SuggestionType.Column))); }
                }
                else { var conns = _metadata.GetConnections(context.DocumentUri); if (conns.Any(c => c.Name.Equals(aliasName, StringComparison.OrdinalIgnoreCase))) { var cols = await _metadata.GetColumnsAsync(aliasName, aliasName, context.DocumentUri); var colList = cols.ToList(); if (colList.Count == 0) colList = (await _metadata.GetColumnsAsync(aliasName, "", context.DocumentUri)).ToList(); results.AddRange(colList.Select(c => new Suggestion($"{aliasName}.{c.Trim('[', ']', '\"', '\'')}", SuggestionType.Column))); } }
            } catch {}
            return results;
        }

        private List<TokenInfo> GetLastTokens(string text, int count) { var matches = Regex.Matches(text, @"(@?\w+|==|!=|<=|>=|[=<>+\-*/().,;])").Cast<Match>().Select(m => new TokenInfo(m.Value)).ToList(); return matches.Skip(Math.Max(0, matches.Count - count)).ToList(); }
        private record TokenInfo(string Text) { public bool IsKeyword => LanguageMetadata.IsKeyword(Text); }
    }
}
