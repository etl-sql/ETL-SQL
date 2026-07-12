using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ETL_SQL.Analysis.Linting.Grammar;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Services;

namespace ETL_SQL.Analysis.Services;

public class GrammarLanguageService : LanguageService
{
    private static readonly HashSet<string> TableLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "<table_source>", "<join_table>", "<temp_table>", "<target_table>", "<source_table>", "<table_name>"
    };

    // Keywords that remain legal inside a free-form expression/value position. When the cursor
    // is at an expression spot we drop statement-structural keywords (FROM, WHERE, INSERT, JOIN,
    // …) but keep these so completions like NOT / IN / EXISTS / CASE / CAST still surface.
    // NOTE (see TODO "Grammar-tree suggestions & SQL fuzzer hardening"): this allowlist may need
    // widening after interactive UX verification in tools/ui-sandbox.
    // Wildcard positions that expect a bare name/identifier (a table, column, connection, variable, or
    // CTE name) rather than an arbitrary expression. Operator/value keywords are noise at these
    // positions, so — unlike true expression wildcards — they must not enable expression-keyword
    // retention. (The actual table/column/etc. candidates are supplied by semantic injection.)
    private static readonly HashSet<string> NamePlaceholderLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "<table_source>", "<join_table>", "<temp_table>", "<target_table>", "<source_table>", "<table_name>",
        "<connection_name>", "<connector_type>", "<column_name>", "<variable_name>", "<variable>", "<cte_name>"
    };

    private static readonly HashSet<string> ExpressionKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "AND", "OR", "NOT", "LIKE", "ILIKE", "ESCAPE", "IN", "EXISTS", "BETWEEN", "IS", "NULL",
        "CASE", "WHEN", "THEN", "ELSE", "END", "CAST", "CONVERT", "DISTINCT",
        "TRUE", "FALSE", "INTERVAL", "OVER", "PARTITION", "NULLS", "ASC", "DESC"
    };

    private readonly GrammarStateTree _grammarTree;

    public GrammarLanguageService(IMetadataManager metadata, Core.Interfaces.ILanguageHelpRegistry? helpRegistry = null, Core.Functions.IFunctionRegistry? functionRegistry = null)
        : base(metadata, helpRegistry, functionRegistry)
    {
        _grammarTree = DefaultGrammar.Build(metadata);
    }

    public override async Task<List<Suggestion>> GetSuggestionsAsync(SuggestionContext context)
    {
        var suggestions = await base.GetSuggestionsAsync(context);

        try
        {
            var walker = RunWalker(context);

            // Collect the keywords grammatically offered at the cursor and detect whether a
            // free-form expression/identifier is also legal here. Unlike the previous
            // all-or-nothing gate (where a single wildcard transition disabled ALL keyword
            // narrowing, dumping the full alphabetical list in every expression position), the
            // decision below is per-suggestion: expression positions still drop irrelevant
            // statement keywords while retaining functions and operator/value keywords.
            var offeredKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool allowsExpression = false;
            foreach (var state in walker.ActiveStates)
            {
                foreach (var transition in state.Transitions)
                {
                    // A free-form transition (an <expression>/<value> position) means an expression
                    // is legal here; a specific keyword transition contributes an offered keyword. A
                    // name/identifier wildcard (table/column/… position) is neither — it wants a name,
                    // not operator keywords.
                    if (transition.IsWildcard)
                    {
                        if (!NamePlaceholderLabels.Contains(transition.Label ?? string.Empty))
                        {
                            allowsExpression = true;
                        }
                    }
                    else if (transition.Label != null)
                    {
                        offeredKeywords.Add(transition.Label);
                    }
                }
            }

            suggestions = suggestions.Where(s =>
            {
                // Always keep session/system tags and non-keyword categories (tables, columns,
                // variables, connections, snippets) — those are narrowed by semantic injection.
                if (s.Text.StartsWith("@")) return true;
                switch (s.Type)
                {
                    case SuggestionType.Keyword:
                        return offeredKeywords.Contains(s.Text) ||
                               (allowsExpression && ExpressionKeywords.Contains(s.Text));
                    case SuggestionType.Function:
                        // Functions belong wherever an expression is legal.
                        return allowsExpression || offeredKeywords.Contains(s.Text);
                    default:
                        return true;
                }
            }).ToList();
        }
        catch (Exception ex) when (!GrammarDiagnostics.StrictMode)
        {
            context.Logger?.Error($"GrammarLanguageService.GetSuggestionsAsync error: {ex.Message}");
        }

        return suggestions;
    }


    protected override async Task<List<Suggestion>> GetPatternSuggestionsAsync(SuggestionContext context)
    {
        var results = new List<Suggestion>();
        try
        {
            // 1. Process text and run walker
            var walker = RunWalker(context);

            // 2. Add base grammar-tree suggestions
            results.AddRange(walker.GetSuggestions(context));

            // 3. Extract semantic expectations
            var expectations = EvaluateExpectations(walker);

            // 4. Bind and inject semantic suggestions
            await InjectSemanticSuggestionsAsync(results, expectations, context);
        }
        catch (Exception ex) when (!GrammarDiagnostics.StrictMode)
        {
            context.Logger?.Error($"GrammarLanguageService error: {ex.Message}");
        }

        return results;
    }

    private TokenWalker RunWalker(SuggestionContext context)
    {
        string textToTokenize = context.ScriptBefore;
        if (!string.IsNullOrEmpty(context.Prefix) && textToTokenize.EndsWith(context.Prefix, StringComparison.OrdinalIgnoreCase))
        {
            textToTokenize = textToTokenize.Substring(0, textToTokenize.Length - context.Prefix.Length);
        }

        var tokens = new Lexer(textToTokenize).Tokenize();

        // The walker resets to Root at every semicolon, so only the tokens after the last statement
        // terminator affect the active states at the cursor. Walking just the current statement gives
        // identical results while avoiding an O(n) re-walk of the whole document on each keystroke
        // (which was O(n^2) over a typing session).
        int start = 0;
        for (int i = tokens.Count - 1; i >= 0; i--)
        {
            if (tokens[i].Type == TokenType.SEMICOLON)
            {
                start = i + 1;
                break;
            }
        }

        var walker = new TokenWalker(_grammarTree);
        for (int i = start; i < tokens.Count; i++)
        {
            if (tokens[i].Type == TokenType.EOF) break;
            walker.Consume(tokens[i]);
        }

        return walker;
    }

    private class SemanticExpectations
    {
        public bool ExpectsTable { get; set; }
        public bool ExpectsConnection { get; set; }
        public bool ExpectsVariable { get; set; }
        public bool ExpectsColumn { get; set; }
    }

    private SemanticExpectations EvaluateExpectations(TokenWalker walker)
    {
        var exp = new SemanticExpectations();
        foreach (var state in walker.ActiveStates)
        {
            foreach (var transition in state.Transitions)
            {
                var label = transition.Label ?? "";
                if (TableLabels.Contains(label))
                {
                    exp.ExpectsTable = true;
                }

                if (label.Equals("<connection_name>", StringComparison.OrdinalIgnoreCase) ||
                    transition.SuggestType == SuggestionType.Connection)
                {
                    exp.ExpectsConnection = true;
                }

                if (label.Equals("<variable_name>", StringComparison.OrdinalIgnoreCase) ||
                    label.Equals("<variable>", StringComparison.OrdinalIgnoreCase) ||
                    transition.SuggestType == SuggestionType.Variable)
                {
                    exp.ExpectsVariable = true;
                }

                if (label.Equals("<column_name>", StringComparison.OrdinalIgnoreCase) ||
                    label.Equals("<column>", StringComparison.OrdinalIgnoreCase) ||
                    transition.SuggestType == SuggestionType.Column)
                {
                    exp.ExpectsColumn = true;
                }
            }
        }

        return exp;
    }

    private async Task InjectSemanticSuggestionsAsync(List<Suggestion> results, SemanticExpectations exp, SuggestionContext context)
    {
        List<ConnectionInfo>? conns = null;

        if (exp.ExpectsConnection || exp.ExpectsTable)
        {
            conns = _metadata.GetConnections(context.DocumentUri);
        }

        if (exp.ExpectsConnection && conns != null)
        {
            results.AddRange(conns.Select(c => new Suggestion(c.Name, SuggestionType.Connection, Priority: 10)));
        }

        if (exp.ExpectsTable && conns != null)
        {
            await InjectTableSuggestionsAsync(results, conns, context);
        }

        if (exp.ExpectsVariable)
        {
            results.AddRange(GetVariableSuggestions(context));
        }

        if (exp.ExpectsColumn)
        {
            await InjectColumnSuggestionsAsync(results, context);
        }
    }

    private async Task InjectTableSuggestionsAsync(List<Suggestion> results, List<ConnectionInfo> conns, SuggestionContext context)
    {
        results.AddRange(conns.Select(c => new Suggestion(c.Name, SuggestionType.Connection, Priority: 10)));

        var tempTables = Regex.Matches(context.FullScript, @"(#\w+)")
            .Cast<Match>()
            .Select(m => m.Value)
            .Distinct();
        results.AddRange(tempTables.Select(t => new Suggestion(t, SuggestionType.Table, Priority: 15)));

        foreach (var conn in conns)
        {
            try
            {
                var tables = await _metadata.GetTablesAsync(conn.Name, context.DocumentUri);

                if (context.Prefix.StartsWith($"{conn.Name}.", StringComparison.OrdinalIgnoreCase))
                {
                    var prefRest = context.Prefix.Substring(conn.Name.Length + 1);
                    var filtered = tables.Where(t => t.StartsWith(prefRest, StringComparison.OrdinalIgnoreCase));
                    results.AddRange(filtered.Select(t => new Suggestion($"{conn.Name}.{t}", SuggestionType.Table, Priority: 5)));
                }
                else
                {
                    var filteredTables = tables.Where(t => string.IsNullOrEmpty(context.Prefix) || t.StartsWith(context.Prefix, StringComparison.OrdinalIgnoreCase));
                    results.AddRange(filteredTables.Select(t => new Suggestion(t, SuggestionType.Table, Priority: 20)));
                    results.AddRange(filteredTables.Select(t => new Suggestion($"{conn.Name}.{t}", SuggestionType.Table, Priority: 25)));
                }
            }
            catch (Exception ex) when (!GrammarDiagnostics.StrictMode)
            {
                context.Logger?.Error($"GrammarLanguageService: table suggestions for '{conn.Name}' failed: {ex.Message}");
            }
        }
    }

    private async Task InjectColumnSuggestionsAsync(List<Suggestion> results, SuggestionContext context)
    {
        foreach (var info in context.Aliases.Values.Distinct())
        {
            try
            {
                var cols = await _metadata.GetColumnsAsync(info.ConnectionName ?? info.TableName, info.BaseTableName ?? info.TableName, context.DocumentUri);
                foreach (var col in cols)
                {
                    results.Add(new Suggestion(col, SuggestionType.Column, Priority: 5));
                    if (info.HasExplicitAlias)
                    {
                        results.Add(new Suggestion($"{info.Alias}.{col}", SuggestionType.Column, Priority: 10));
                    }
                }
            }
            catch (Exception ex) when (!GrammarDiagnostics.StrictMode)
            {
                context.Logger?.Error($"GrammarLanguageService: column suggestions for '{info.TableName}' failed: {ex.Message}");
            }
        }
    }
}

