using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Services;

namespace ETL_SQL.Analysis.Linting.Grammar;

public class TokenWalker
{
    private readonly GrammarStateTree _tree;
    private Dictionary<StateNode, Dictionary<string, object>> _stateBags = new();
    private Dictionary<string, object> _currentStateBag = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<StateNode> ActiveStates { get; private set; } = new();
    public Dictionary<string, object> StateBag => _currentStateBag;

    public IReadOnlyDictionary<string, object> GetStateBag(StateNode state) =>
        _stateBags.TryGetValue(state, out var bag)
            ? bag
            : new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

    public TokenWalker(GrammarStateTree tree)
    {
        _tree = tree;
        Reset();
    }

    public void Reset()
    {
        ActiveStates.Clear();
        ActiveStates.Add(_tree.Root);
        _currentStateBag = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        _stateBags = new Dictionary<StateNode, Dictionary<string, object>>
        {
            [_tree.Root] = _currentStateBag
        };
    }

    /// <summary>
    /// Processes a token and transitions the active states.
    /// Returns true if at least one state transitioned successfully, false if a syntax error occurred.
    /// </summary>
    public bool Consume(Token token)
    {
        if (token.Type == TokenType.EOF) return true;
        if (token.Type == TokenType.SEMICOLON)
        {
            Reset();
            return true;
        }

        var nextStates = new HashSet<StateNode>();
        var nextStateBags = new Dictionary<StateNode, Dictionary<string, object>>();

        foreach (var state in ActiveStates)
        {
            var sourceBag = _stateBags.TryGetValue(state, out var existingBag)
                ? existingBag
                : new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            // Check transitions from this state
            foreach (var transition in state.Transitions)
            {
                _currentStateBag = sourceBag;
                if (transition.Matches(token, this))
                {
                    nextStates.Add(transition.Target);
                    var branchBag = new Dictionary<string, object>(sourceBag, StringComparer.OrdinalIgnoreCase);
                    _currentStateBag = branchBag;
                    transition.OnTransition?.Invoke(token, this);
                    nextStateBags.TryAdd(transition.Target, branchBag);
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
                    var startBag = new Dictionary<string, object>(sourceBag, StringComparer.OrdinalIgnoreCase)
                    {
                        ["StartKeyword"] = token.Value
                    };
                    nextStateBags.TryAdd(startNode, startBag);
                }
            }
        }

        if (!nextStates.Any())
        {
            return false;
        }

        ActiveStates = nextStates;
        _stateBags = nextStateBags;
        _currentStateBag = _stateBags.Values.FirstOrDefault()
            ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
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
                        catch when (!GrammarDiagnostics.StrictMode)
                        {
                            // A misbehaving custom suggestion provider must not break completion in
                            // production; in strict/test mode it rethrows so the bug is visible.
                        }
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
