using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Services;

namespace ETL_SQL.Analysis.Linting.Grammar;

public class TokenWalker
{
    private readonly GrammarStateTree _tree;
    public HashSet<StateNode> ActiveStates { get; private set; } = new();
    public Dictionary<string, object> StateBag { get; } = new(StringComparer.OrdinalIgnoreCase);

    public TokenWalker(GrammarStateTree tree)
    {
        _tree = tree;
        Reset();
    }

    public void Reset()
    {
        ActiveStates.Clear();
        ActiveStates.Add(_tree.Root);
        StateBag.Clear();
    }

    /// <summary>
    /// Processes a token and transitions the active states.
    /// Returns true if at least one state transitioned successfully, false if a syntax error occurred.
    /// </summary>
    public bool Consume(Token token)
    {
        if (token.Type == TokenType.EOF) return true;

        var nextStates = new HashSet<StateNode>();

        foreach (var state in ActiveStates)
        {
            // Check transitions from this state
            foreach (var transition in state.Transitions)
            {
                if (transition.Condition(token))
                {
                    nextStates.Add(transition.Target);
                    transition.OnTransition?.Invoke(token, this);
                }
            }

            // Also check Root transitions if we are starting a new statement or if we are at Root
            if (state == _tree.Root)
            {
                var startNode = _tree.GetStartNode(token.Value);
                if (startNode != null)
                {
                    nextStates.Add(startNode);
                    // For start node keywords, we can also store the starting keyword in StateBag
                    StateBag["StartKeyword"] = token.Value;
                }
            }
        }

        if (!nextStates.Any())
        {
            return false;
        }

        ActiveStates = nextStates;
        return true;
    }

    /// <summary>
    /// Returns a list of suggestion candidates based on the next valid transitions.
    /// </summary>
    public List<Suggestion> GetSuggestions(SuggestionContext context)
    {
        var suggestions = new List<Suggestion>();

        foreach (var state in ActiveStates)
        {
            foreach (var transition in state.Transitions)
            {
                if (transition.SuggestType != null)
                {
                    if (transition.CustomSuggestionsProvider != null)
                    {
                        try
                        {
                            var values = transition.CustomSuggestionsProvider(context);
                            foreach (var val in values)
                            {
                                suggestions.Add(new Suggestion(val, transition.SuggestType.Value, Priority: 50));
                            }
                        }
                        catch { }
                    }
                    else if (transition.Label != null)
                    {
                        suggestions.Add(new Suggestion(transition.Label, transition.SuggestType.Value, Priority: 40));
                    }
                }
            }
        }

        return suggestions;
    }
}
