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
                    // A transition that accepts both an identifier and a string token is a
                    // free-form wildcard (an <expression>/<value> position).
                    var idToken = new Token(TokenType.IDENTIFIER, "___wildcard_test_1___", 0, 0, 0, 0);
                    var strToken = new Token(TokenType.STRING, "___wildcard_test_2___", 0, 0, 0, 0);
                    if (transition.Matches(idToken, walker) && transition.Matches(strToken, walker))
                    {
                        allowsExpression = true;
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
        catch (Exception ex)
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
        catch (Exception ex)
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
        var walker = new TokenWalker(_grammarTree);

        foreach (var token in tokens)
        {
            if (token.Type == TokenType.EOF) break;
            walker.Consume(token);
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
            catch (Exception ex)
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
            catch (Exception ex)
            {
                context.Logger?.Error($"GrammarLanguageService: column suggestions for '{info.TableName}' failed: {ex.Message}");
            }
        }
    }
}

