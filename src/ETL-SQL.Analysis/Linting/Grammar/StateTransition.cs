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

    private readonly bool? _explicitWildcard;
    private bool? _computedWildcard;

    public StateTransition(
        Func<Token, bool> condition,
        StateNode target,
        string? label = null,
        SuggestionType? suggestType = null,
        Func<SuggestionContext, IEnumerable<string>>? customSuggestionsProvider = null,
        Action<Token, TokenWalker>? onTransition = null,
        Func<Token, TokenWalker, bool>? contextCondition = null,
        bool? isWildcard = null)
    {
        Condition = condition;
        Target = target;
        Label = label;
        SuggestType = suggestType;
        CustomSuggestionsProvider = customSuggestionsProvider;
        OnTransition = onTransition;
        ContextCondition = contextCondition;
        _explicitWildcard = isWildcard;
    }

    public bool Matches(Token token, TokenWalker walker) =>
        Condition(token) && (ContextCondition?.Invoke(token, walker) ?? true);

    /// <summary>
    /// True when this is a free-form transition that accepts an arbitrary identifier/value (an
    /// &lt;expression&gt;/&lt;value&gt; position), as opposed to a specific keyword or token match.
    /// Used by completion to decide whether an expression is legal at the cursor. When a wildcard
    /// intent was declared at construction it is honored; otherwise it is derived from the
    /// <see cref="Condition"/> alone — never the <see cref="ContextCondition"/>, so transient walker
    /// state cannot corrupt the classification (the failure mode of the old ad-hoc string probe).
    /// </summary>
    public bool IsWildcard
    {
        get
        {
            if (_explicitWildcard.HasValue)
            {
                return _explicitWildcard.Value;
            }
            _computedWildcard ??= ComputeIsWildcard();
            return _computedWildcard.Value;
        }
    }

    private bool ComputeIsWildcard()
    {
        // A free-form transition accepts both an arbitrary identifier token and an arbitrary string
        // token. Specific keyword/token transitions reject one or both of these probes. The probe
        // values are deliberately non-word (leading underscore) so conditions like IsWord(t.Value) —
        // used by identifier/name positions such as <connection_name> — are not misclassified as
        // free-form expression positions.
        var idToken = new Token(TokenType.IDENTIFIER, "___wildcard_probe_1___", 0, 0, 0, 0);
        var strToken = new Token(TokenType.STRING, "___wildcard_probe_2___", 0, 0, 0, 0);
        return Condition(idToken) && Condition(strToken);
    }
}
