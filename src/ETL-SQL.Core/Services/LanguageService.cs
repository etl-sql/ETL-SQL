using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.IO;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;

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
        Connection,
        Snippet
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
        private readonly Core.Functions.IFunctionRegistry? _functionRegistry;

        public LanguageService(IMetadataManager metadata, Core.Interfaces.ILanguageHelpRegistry? helpRegistry = null, Core.Functions.IFunctionRegistry? functionRegistry = null)
        {
            _metadata = metadata;
            _helpRegistry = helpRegistry;
            _functionRegistry = functionRegistry;
        }

        public async Task<List<Suggestion>> GetSuggestionsAsync(SuggestionContext context)
        {
            try
            {
                if (context.Aliases.Count == 0 && !string.IsNullOrEmpty(context.FullScript))
                    context.Aliases = AliasScanner.Scan(context.FullScript, context.ScriptBefore.Length);

                var allSuggestions = new List<Suggestion>();
                allSuggestions.AddRange(GetFilePathSuggestions(context));
                allSuggestions.AddRange(await GetPatternSuggestionsAsync(context));
                allSuggestions.AddRange(await GetWithClauseSuggestionsAsync(context));
                allSuggestions.AddRange(await GetDatabaseSchemaSuggestionsAsync(context));
                allSuggestions.AddRange(await GetAliasColumnSuggestionsAsync(context));
                allSuggestions.AddRange(GetConnectionNameSuggestions(context));
                allSuggestions.AddRange(GetKeywordSuggestions(context));
                allSuggestions.AddRange(GetVariableSuggestions(context));
                allSuggestions.AddRange(GetGotoLabelSuggestions(context));

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
                    for (int i = 0; i < finalResults.Count; i++)
                    {
                        var s = finalResults[i];
                        string? help = null;

                        if (s.Type == SuggestionType.Keyword)
                        {
                            help = _helpRegistry.GetHelp(s.Text);
                        }
                        else if (s.Type == SuggestionType.Function)
                        {
                            help = _helpRegistry.GetHelp("FUNCTION", s.Text);
                        }
                        else if (s.Type == SuggestionType.Connection)
                        {
                            help = _helpRegistry.GetHelp("CONNECTION", s.Text);
                        }

                        if (help != null)
                        {
                            finalResults[i] = s with { Documentation = help };
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
            try { return Regex.Matches(context.ScriptBefore, @"(@\w+)").Cast<Match>().Select(m => new Suggestion(m.Value, SuggestionType.Variable)).Distinct().ToList(); }
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
                if (last.Text.Equals("CREATE", StringComparison.OrdinalIgnoreCase)) results.AddRange(new[] { "CONNECTION", "TABLE", "VIEW", "VISUAL", "PAGE", "DATASET", "STYLE", "CONTAINER", "NAVIGATION", "JOB", "DIRECTORY", "PROCEDURE", "FUNCTION", "INDEX", "TAG", "LINEAGE", "SETS" }.Select(k => new Suggestion(k, SuggestionType.Keyword, Priority: 0)));
                else if (last.Text.Equals("SHOW", StringComparison.OrdinalIgnoreCase)) results.AddRange(new[] { "DATASETS", "VIEWS", "JOBS", "JOB", "CONNECTIONS", "TABLES", "COLUMNS", "VARIABLES", "VERSION", "LINEAGE", "TAGS", "PROFILE", "ACTIVE" }.Select(k => new Suggestion(k, SuggestionType.Keyword, Priority: 0)));
                else if (last.Text.Equals("USE", StringComparison.OrdinalIgnoreCase)) results.AddRange(new[] { "DATASET", "DOCKER", "SETS", "PASSWORD" }.Select(k => new Suggestion(k, SuggestionType.Keyword, Priority: 0)));
                else if (last.Text.Equals("AS", StringComparison.OrdinalIgnoreCase) && prev2?.Text.Equals("CONNECTION", StringComparison.OrdinalIgnoreCase) == true) results.AddRange(_metadata.GetRegisteredNames().Select(c => new Suggestion(c, SuggestionType.Connection, Priority: 0)));
                else if (last.Text.Equals("FROM", StringComparison.OrdinalIgnoreCase) || last.Text.Equals("JOIN", StringComparison.OrdinalIgnoreCase) || last.Text.Equals("INTO", StringComparison.OrdinalIgnoreCase) || last.Text.Equals("UPDATE", StringComparison.OrdinalIgnoreCase))
                {
                    var conns = _metadata.GetConnections(context.DocumentUri);
                    results.AddRange(conns.Select(c => new Suggestion(c.Name, SuggestionType.Connection, Priority: 1)));
                    foreach (var conn in conns) 
                    { 
                        try 
                        { 
                            var tables = await _metadata.GetTablesAsync(conn.Name, context.DocumentUri); 
                            
                            if (context.Prefix.StartsWith($"{conn.Name}.", StringComparison.OrdinalIgnoreCase))
                            {
                                var prefRest = context.Prefix.Substring(conn.Name.Length + 1);
                                var filtered = tables.Where(t => t.StartsWith(prefRest, StringComparison.OrdinalIgnoreCase));
                                results.AddRange(filtered.Select(t => new Suggestion($"{conn.Name}.{t}", SuggestionType.Table, Priority: 2)));
                            }
                            else
                            {
                                var filteredTables = tables.Where(t => string.IsNullOrEmpty(context.Prefix) || t.StartsWith(context.Prefix, StringComparison.OrdinalIgnoreCase)).ToList(); 
                                results.AddRange(filteredTables.Select(t => new Suggestion(t, SuggestionType.Table, Priority: 5))); 
                                results.AddRange(filteredTables.Select(t => new Suggestion($"{conn.Name}.{t}", SuggestionType.Table, Priority: 8))); 
                            }
                        } catch {} 
                    }
                }
                else if (last.Text.Equals("SET", StringComparison.OrdinalIgnoreCase)) results.AddRange(new[] { "WHAT_IF", "PROFILING", "REPORT", "BATCH_SIZE", "STRICT_SCHEMA", "MAX_ERRORS" }.Select(k => new Suggestion(k, SuggestionType.Keyword, Priority: 0)));

                // Standard governance tag completions: triggered when cursor is inside /* @... */
                if (context.Prefix.StartsWith("@", StringComparison.Ordinal))
                {
                    var tagPrefix = context.Prefix.TrimStart('@');
                    results.AddRange(ETL_SQL.Common.LanguageMetadata.StandardTags
                        .Where(t => tagPrefix.Length == 0 || t.StartsWith(tagPrefix, StringComparison.OrdinalIgnoreCase))
                        .Select(t => new Suggestion("@" + t, SuggestionType.Keyword, Priority: 0,
                            Documentation: GetTagDocumentation(t))));
                }
            } catch {}
            return results;
        }

        private static string? GetTagDocumentation(string tag) => tag switch
        {
            "pii"               => "**@pii** `true|false` — Personal Identifiable Information. Inherits `true` from any source column.",
            "phi"               => "**@phi** `true|false` — Protected Health Information (HIPAA).",
            "pci"               => "**@pci** `true|false` — Payment Card data (PCI-DSS).",
            "sensitive"         => "**@sensitive** `true|false` — Sensitive data requiring access controls.",
            "classification"    => "**@classification** `Public|Internal|Confidential|Restricted` — Data classification tier.",
            "encrypted_at_rest" => "**@encrypted_at_rest** `true|false` — Column is stored encrypted.",
            "owner"             => "**@owner** `team or person` — Accountable owner of this data.",
            "domain"            => "**@domain** `Finance|HR|Sales|...` — Business domain.",
            "steward"           => "**@steward** `name` — Person responsible for data quality.",
            "contact"           => "**@contact** `email or handle` — Point of contact for questions.",
            "freshness"         => "**@freshness** `daily|hourly|real-time|...` — How often this data is refreshed.",
            "sla"               => "**@sla** `4h|T+1|...` — Delivery SLA.",
            "quality"           => "**@quality** `high|medium|low|unverified` — Confidence in data quality.",
            "nullable"          => "**@nullable** `true|false` — Whether this column can contain NULLs.",
            "d"                 => "**@d** — Human-readable description (inherits to derived columns as DerivedFromDescriptions).",
            "example"           => "**@example** `sample value` — Representative example value.",
            "unit"              => "**@unit** `USD|ms|rows|...` — Unit of measurement.",
            "format"            => "**@format** `YYYY-MM-DD|E.164|...` — Expected format or pattern.",
            "source_system"     => "**@source_system** `Salesforce|SAP|...` — Originating system.",
            "source_table"      => "**@source_table** `dbo.Orders` — Originating table.",
            "source_column"     => "**@source_column** `cust_id` — Original column name in the source system before any ETL renaming.",
            "load_pattern"      => "**@load_pattern** `full_load|incremental|streaming` — How data is loaded.",
            _                   => null
        };

        private async Task<List<Suggestion>> GetWithClauseSuggestionsAsync(SuggestionContext context)
        {
            var results = new List<Suggestion>();
            try
            {
                var tokens = GetLastTokens(context.ScriptBefore, 12);
                string? connectorType = null;

                // New syntax: CREATE/ALTER CONNECTION name AS TYPE(target?, opts...)
                var asIdx = tokens.FindLastIndex(t => t.Text.Equals("AS", StringComparison.OrdinalIgnoreCase));
                if (asIdx >= 0 && asIdx + 2 < tokens.Count && tokens[asIdx + 2].Text == "(")
                {
                    bool hasConnectionBefore = tokens.Take(asIdx).Any(t => t.Text.Equals("CONNECTION", StringComparison.OrdinalIgnoreCase));
                    if (hasConnectionBefore)
                        connectorType = tokens[asIdx + 1].Text;
                }

                // Legacy syntax: ON TYPE ... WITH (opts)
                if (connectorType == null)
                {
                    var withIdx = tokens.FindLastIndex(t => t.Text.Equals("WITH", StringComparison.OrdinalIgnoreCase));
                    if (withIdx >= 0)
                    {
                        var onIdx = tokens.FindLastIndex(t => t.Text.Equals("ON", StringComparison.OrdinalIgnoreCase));
                        if (onIdx >= 0 && onIdx < withIdx && onIdx + 1 < tokens.Count)
                            connectorType = tokens[onIdx + 1].Text;
                    }
                }

                if (connectorType == null) return results;

                var connector = _metadata.GetConnector(connectorType);
                if (connector == null) return results;
                var options = connector.GetSupportedOptions();

                var last = tokens.LastOrDefault();
                if (last == null) return results;

                // Case 1: We are at ( or , or just after them (prefix is empty) -> Suggest option names
                if (last.Text == "(" || last.Text == "," || (string.IsNullOrEmpty(context.Prefix) && (last.Text == "(" || last.Text == ",")))
                {
                    results.AddRange(options.Keys.Select(o => new Suggestion(o, SuggestionType.OptionName, Priority: 0)));
                }
                // Case 2: We are typing an option name -> last is the prefix, prev is ( or ,
                else if (tokens.Count > 1 && (tokens[tokens.Count - 2].Text == "(" || tokens[tokens.Count - 2].Text == ","))
                {
                    results.AddRange(options.Keys.Select(o => new Suggestion(o, SuggestionType.OptionName, Priority: 0)));
                }
                // Case 3: We are at = -> Suggest option values
                else if (last.Text == "=")
                {
                    var optName = tokens.Count > 1 ? tokens[tokens.Count - 2].Text : "";
                    if (options.TryGetValue(optName, out var values))
                        results.AddRange(values.Select(v => new Suggestion(v, SuggestionType.OptionValue, Priority: 0)));
                }
                // Case 4: We are typing an option value -> last is the prefix, prev is =
                else if (tokens.Count > 1 && tokens[tokens.Count - 2].Text == "=")
                {
                    var optName = tokens.Count > 2 ? tokens[tokens.Count - 3].Text : "";
                    if (options.TryGetValue(optName, out var values))
                        results.AddRange(values.Select(v => new Suggestion(v, SuggestionType.OptionValue, Priority: 0)));
                }
            }
            catch { }
            return results;
        }

        private List<Suggestion> GetConnectionNameSuggestions(SuggestionContext context)
        {
            try 
            { 
                return _metadata.GetConnections(context.DocumentUri)
                    .Select(c => new Suggestion(c.Name, SuggestionType.Connection, Priority: 150))
                    .ToList(); 
            }
            catch { return new List<Suggestion>(); }
        }

        private async Task<List<Suggestion>> GetDatabaseSchemaSuggestionsAsync(SuggestionContext context)
        {
            var results = new List<Suggestion>();
            try
            {
                if (context.Prefix.Contains("."))
                {
                    var parts = context.Prefix.Split('.');
                    var connName = parts[0];
                    var tables = await _metadata.GetTablesAsync(connName, context.DocumentUri);
                    results.AddRange(tables.Where(t => t.StartsWith(parts[1], StringComparison.OrdinalIgnoreCase)).Select(t => new Suggestion($"{connName}.{t}", SuggestionType.Table, Priority: 0)));
                }
            } catch {}
            return results;
        }

        private async Task<List<Suggestion>> GetAliasColumnSuggestionsAsync(SuggestionContext context)
        {
            var results = new List<Suggestion>();
            try
            {
                if (string.IsNullOrEmpty(context.Prefix) || context.Prefix == "*")
                {
                    var allCols = new List<string>();
                    foreach (var info in context.Aliases.Values.Distinct())
                    {
                        var cols = await _metadata.GetColumnsAsync(info.ConnectionName ?? info.TableName, info.BaseTableName ?? info.TableName, context.DocumentUri);
                        string? prefixAlias = context.Prefix == "*" ? "" : null;

                        if (cols.Any())
                        {
                            string prefix = "";
                            if (string.IsNullOrEmpty(prefixAlias))
                            {
                                // If multiple tables are involved in the statement, we must use prefixes to avoid ambiguity
                                if (context.Aliases.Count > 1)
                                {
                                    if (info.HasExplicitAlias)
                                    {
                                        prefix = info.Alias!;
                                    }
                                    else
                                    {
                                        // Use base table name (strip connection if present)
                                        prefix = info.BaseTableName ?? info.TableName;
                                    }
                                }
                                // Single table: use full qualified name if table is a connection-qualified reference (e.g. m.Users → m.Users.col)
                                else if (info.ConnectionName != null)
                                    prefix = info.TableName;
                            }
                            else
                            {
                                prefix = prefixAlias;
                            }
                                
                            string qualifiedCols = string.Join(", ", cols.Select(c => string.IsNullOrEmpty(prefix) ? c : $"{prefix}.{c}"));
                            allCols.Add(qualifiedCols);
                        }
                    }

                    if (allCols.Any())
                    {
                        results.Add(new Suggestion(string.Join(", ", allCols), SuggestionType.Column, Priority: 0));
                    }
                    return results;
                }

                if (context.Prefix.Contains("."))
                {
                    var parts = context.Prefix.Split('.');
                    var alias = parts[0];
                    var pref = parts[1];

                    if (context.Aliases.TryGetValue(alias, out var infoAlias))
                    {
                        var cols = await _metadata.GetColumnsAsync(infoAlias.ConnectionName ?? infoAlias.TableName, infoAlias.BaseTableName ?? infoAlias.TableName, context.DocumentUri);
                        
                        if (pref == "*")
                        {
                            if (cols.Any())
                            {
                                string expansion = string.Join(", ", cols.Select(c => $"{alias}.{c.Trim('[', ']', '\"', '\'')}"));
                                results.Add(new Suggestion(expansion, SuggestionType.Column, Priority: 0));
                            }
                        }
                        else
                        {
                            results.AddRange(cols.Where(c => c.StartsWith(pref, StringComparison.OrdinalIgnoreCase))
                                               .Select(c => new Suggestion($"{alias}.{c.Trim('[', ']', '\"', '\'')}", SuggestionType.Column)));
                            
                            if (context.VirtualSchemas.TryGetValue(infoAlias.TableName, out var vCols))
                            {
                                results.AddRange(vCols.Where(c => c.StartsWith(pref, StringComparison.OrdinalIgnoreCase))
                                                      .Select(c => new Suggestion($"{alias}.{c.Trim('[', ']', '\"', '\'')}", SuggestionType.Column)));
                            }
                        }
                    }
                    else
                    {
                        // Check if it's a direct connection reference
                        var conns = _metadata.GetConnections(context.DocumentUri);
                        var match = conns.FirstOrDefault(c => c.Name.Equals(alias, StringComparison.OrdinalIgnoreCase));
                        if (match != null)
                        {
                            // Avoid suggesting columns if we are likely in a FROM/JOIN context where tables are expected
                            var lastTokens = GetLastTokens(context.ScriptBefore, 5);
                            bool inTableContext = lastTokens.Any(t => t.Text.Equals("FROM", StringComparison.OrdinalIgnoreCase) || 
                                                                    t.Text.Equals("JOIN", StringComparison.OrdinalIgnoreCase) ||
                                                                    t.Text.Equals("INTO", StringComparison.OrdinalIgnoreCase) ||
                                                                    t.Text.Equals("UPDATE", StringComparison.OrdinalIgnoreCase));

                            if (!inTableContext)
                            {
                                var cols = await _metadata.GetColumnsAsync(match.Name, match.Name, context.DocumentUri);
                                results.AddRange(cols.Where(c => c.StartsWith(pref, StringComparison.OrdinalIgnoreCase))
                                                   .Select(c => new Suggestion($"{alias}.{c.Trim('[', ']', '\"', '\'')}", SuggestionType.Column)));
                            }
                        }
                    }
                }
            } catch {}
            return results;
        }

        private List<TokenInfo> GetLastTokens(string text, int count)
        {
            var matches = Regex.Matches(text, @"(--.*|\/\*[\s\S]*?\*\/|'[^']*'|""[^""]*""|\[[^\]]*\]|@?\w+|==|!=|<=|>=|[=<>+\-*/().,;])")
                .Cast<Match>()
                .Select(m => new TokenInfo(m.Value))
                .ToList();
            return matches.Skip(Math.Max(0, matches.Count - count)).ToList();
        }
        private record TokenInfo(string Text) { public bool IsKeyword => LanguageMetadata.IsKeyword(Text); }

        private List<Suggestion> GetGotoLabelSuggestions(SuggestionContext context)
        {
            var results = new List<Suggestion>();
            if (Regex.IsMatch(context.ScriptBefore, @"\bGOTO\s+\w*$", RegexOptions.IgnoreCase))
            {
                var lexer = new Lexer(context.FullScript);
                try
                {
                    var tokens = lexer.Tokenize();
                    var parser = new ETL_SQL.Core.Parser.Parser(tokens, context.FullScript);
                    var script = parser.Parse();
                    var labels = new List<string>();
                    TraverseLabels(script, labels);
                    foreach (var label in labels.Distinct())
                    {
                        results.Add(new Suggestion(label, SuggestionType.Keyword, 1));
                    }
                }
                catch
                {
                }
            }
            return results;
        }

        private void TraverseLabels(AstNode node, List<string> labels)
        {
            if (node == null) return;
            if (node is SectionLabelStatement label)
            {
                labels.Add(label.LabelName);
            }
            else if (node is Script script)
            {
                foreach (var stmt in script.Statements) TraverseLabels(stmt, labels);
            }
            else if (node is BlockStatement block)
            {
                foreach (var stmt in block.Statements) TraverseLabels(stmt, labels);
            }
            else if (node is WhileStatement @while)
            {
                TraverseLabels(@while.Body, labels);
            }
            else if (node is ForStatement @for)
            {
                TraverseLabels(@for.Body, labels);
            }
            else if (node is ForeachStatement @foreach)
            {
                TraverseLabels(@foreach.Body, labels);
            }
            else if (node is IfStatement @if)
            {
                TraverseLabels(@if.IfBody, labels);
                if (@if.ElseIfClauses != null)
                {
                    foreach (var elseif in @if.ElseIfClauses) TraverseLabels(elseif.Body, labels);
                }
                if (@if.ElseBody != null) TraverseLabels(@if.ElseBody, labels);
            }
            else if (node is TryCatchStatement tc)
            {
                TraverseLabels(tc.TryBody, labels);
                TraverseLabels(tc.CatchBody, labels);
            }
            else if (node is ParallelStatement p)
            {
                TraverseLabels(p.Body, labels);
            }
        }
    }
}
