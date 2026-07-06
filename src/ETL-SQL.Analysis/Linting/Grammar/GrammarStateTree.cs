using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Analysis.Linting.Grammar;

public class GrammarStateTree
{
    private readonly Dictionary<string, StateNode> _startNodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly bool _requireParserAcceptance;

    public GrammarStateTree(bool requireParserAcceptance = false)
    {
        _requireParserAcceptance = requireParserAcceptance;
    }

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
    public bool ValidateSequence(IEnumerable<Token> tokens, out string? errorMessage, bool requireComplete = true)
    {
        errorMessage = null;
        var sourceTokens = tokens.ToList();
        var tokenList = sourceTokens.Where(t => t.Type != TokenType.EOF && t.Type != TokenType.SEMICOLON).ToList();
        if (!tokenList.Any())
        {
            return true;
        }

        var walker = new TokenWalker(this);
        var firstToken = tokenList[0];
        StateNode? startNode = GetStartNode(firstToken.Value);

        int startIndex = 0;
        if (startNode != null)
        {
            walker.ActiveStates.Clear();
            walker.ActiveStates.Add(startNode);
            startIndex = 1; // Already matched first token via start node
        }

        for (int i = startIndex; i < tokenList.Count; i++)
        {
            var token = tokenList[i];
            bool success = walker.Consume(token);
            if (!success)
            {
                var expected = string.Join(", ", walker.ActiveStates.SelectMany(s => s.Transitions).Select(t => t.Label ?? t.Target.Name).Distinct());
                errorMessage = $"Line {token.Line}, Col {token.Column}: Unexpected token '{token.Value}'. Expected one of: {expected}.";
                return false;
            }
        }

        if (_requireParserAcceptance && requireComplete)
        {
            var parserTokens = sourceTokens.Where(t => t.Type != TokenType.EOF).ToList();
            var last = parserTokens.LastOrDefault() ?? tokenList[^1];
            if (parserTokens.Count == 0 || parserTokens[^1].Type != TokenType.SEMICOLON)
            {
                parserTokens.Add(new Token(TokenType.SEMICOLON, ";", last.EndLine, last.EndColumn, last.EndLine, last.EndColumn + 1));
            }
            parserTokens.Add(new Token(TokenType.EOF, string.Empty, last.EndLine, last.EndColumn + 1, last.EndLine, last.EndColumn + 1));

            var parsed = new Parser(parserTokens).Parse();
            var diagnostic = parsed.Diagnostics.FirstOrDefault(d => d.Severity == ETL_SQL.Core.Common.DiagnosticSeverity.Error);
            if (diagnostic != null)
            {
                errorMessage = $"Production parser rejected the sequence: {diagnostic.Message}";
                return false;
            }
        }

        return true;
    }
}
