using System;
using System.Collections.Generic;

namespace ETL_SQL.Analysis.Linting.Grammar;

public class StateNode
{
    public string Name { get; }
    public List<StateTransition> Transitions { get; } = new();

    public StateNode(string name)
    {
        Name = name;
    }

    public StateNode AddTransition(StateTransition transition)
    {
        Transitions.Add(transition);
        return this;
    }

    public StateNode AddTransitionTo(string label, StateNode target, ETL_SQL.Core.Services.SuggestionType? suggestType = null, Func<ETL_SQL.Core.Services.SuggestionContext, IEnumerable<string>>? customSuggestionsProvider = null, Action<ETL_SQL.Core.Parser.Token, TokenWalker>? onTransition = null)
    {
        Transitions.Add(new StateTransition(
            t => t.Value.Equals(label, StringComparison.OrdinalIgnoreCase),
            target,
            label,
            suggestType,
            customSuggestionsProvider,
            onTransition
        ));
        return this;
    }

    public StateNode AddTokenTransition(ETL_SQL.Core.Parser.TokenType type, StateNode target, string? label = null, ETL_SQL.Core.Services.SuggestionType? suggestType = null, Func<ETL_SQL.Core.Services.SuggestionContext, IEnumerable<string>>? customSuggestionsProvider = null, Action<ETL_SQL.Core.Parser.Token, TokenWalker>? onTransition = null)
    {
        Transitions.Add(new StateTransition(
            t => t.Type == type,
            target,
            label,
            suggestType,
            customSuggestionsProvider,
            onTransition
        ));
        return this;
    }

    public StateNode AddWildcardTransition(StateNode target, string? label = null, ETL_SQL.Core.Services.SuggestionType? suggestType = null, Func<ETL_SQL.Core.Services.SuggestionContext, IEnumerable<string>>? customSuggestionsProvider = null, Action<ETL_SQL.Core.Parser.Token, TokenWalker>? onTransition = null)
    {
        Transitions.Add(new StateTransition(
            t => t.Type != ETL_SQL.Core.Parser.TokenType.EOF,
            target,
            label,
            suggestType,
            customSuggestionsProvider,
            onTransition
        ));
        return this;
    }

    public override string ToString() => Name;
}
