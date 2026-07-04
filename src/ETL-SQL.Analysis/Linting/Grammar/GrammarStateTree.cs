using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Analysis.Linting.Grammar;

public class GrammarStateTree
{
    private readonly Dictionary<string, StateNode> _startNodes = new(StringComparer.OrdinalIgnoreCase);

    public StateNode Root { get; } = new("ROOT");

    public void RegisterStartNode(string keyword, StateNode node)
    {
        _startNodes[keyword] = node;
        
        // Also connect the Root node to this start node using a transition
        Root.Transitions.Add(new StateTransition(
            t => t.Value.Equals(keyword, StringComparison.OrdinalIgnoreCase),
            node,
            keyword
        ));
    }

    public StateNode? GetStartNode(string keyword)
    {
        if (_startNodes.TryGetValue(keyword, out var node))
        {
            return node;
        }
        return null;
    }

    /// <summary>
    /// Validates if a sequence of tokens conforms to the grammar rules defined in the tree.
    /// Supports partial snippets by finding a matching start node for the first non-EOF token.
    /// </summary>
    public bool ValidateSequence(IEnumerable<Token> tokens, out string? errorMessage)
    {
        errorMessage = null;
        var tokenList = tokens.Where(t => t.Type != TokenType.EOF).ToList();
        if (!tokenList.Any())
        {
            return true;
        }

        var firstToken = tokenList[0];
        StateNode? current = GetStartNode(firstToken.Value);

        if (current == null)
        {
            // Fallback to checking Root transitions directly
            current = Root;
            errorMessage = $"Unknown starting keyword or token '{firstToken.Value}'.";
            return false;
        }

        // We already matched the first token by getting the start node, so start walking from the second token
        for (int i = 1; i < tokenList.Count; i++)
        {
            var token = tokenList[i];
            var transition = current.Transitions.FirstOrDefault(t => t.Condition(token));

            if (transition == null)
            {
                var expected = string.Join(", ", current.Transitions.Select(t => t.Label ?? t.Target.Name).Distinct());
                errorMessage = $"Line {token.Line}, Col {token.Column}: Unexpected token '{token.Value}'. Expected one of: {expected}.";
                return false;
            }

            current = transition.Target;
        }

        return true;
    }
}
