using System;
using System.Collections.Generic;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Services;

namespace ETL_SQL.Analysis.Linting.Grammar;

public class StateTransition
{
    public Func<Token, bool> Condition { get; }
    public StateNode Target { get; }
    public string? Label { get; }
    public SuggestionType? SuggestType { get; }
    public Func<SuggestionContext, IEnumerable<string>>? CustomSuggestionsProvider { get; }
    public Action<Token, TokenWalker>? OnTransition { get; }
    public Func<Token, TokenWalker, bool>? ContextCondition { get; }

    public StateTransition(
        Func<Token, bool> condition,
        StateNode target,
        string? label = null,
        SuggestionType? suggestType = null,
        Func<SuggestionContext, IEnumerable<string>>? customSuggestionsProvider = null,
        Action<Token, TokenWalker>? onTransition = null,
        Func<Token, TokenWalker, bool>? contextCondition = null)
    {
        Condition = condition;
        Target = target;
        Label = label;
        SuggestType = suggestType;
        CustomSuggestionsProvider = customSuggestionsProvider;
        OnTransition = onTransition;
        ContextCondition = contextCondition;
    }

    public bool Matches(Token token, TokenWalker walker) =>
        Condition(token) && (ContextCondition?.Invoke(token, walker) ?? true);
}
