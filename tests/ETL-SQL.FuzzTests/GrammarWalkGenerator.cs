using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Analysis.Linting.Grammar;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Services;

namespace ETL_SQL.FuzzTests
{
    public class GrammarWalkGenerator
    {
        private readonly GrammarStateTree _tree;
        private readonly Random _rng;
        private readonly Queue<Token> _tokenQueue = new();

        private static readonly string[] MockTables = { "Users", "Products", "Sales", "Employees" };
        private static readonly string[] MockColumns = { "UserID", "UserName", "Email", "ProductID", "Price", "Quantity", "Total", "EmpID", "Salary", "ProductName" };
        private static readonly string[] MockVariables = { "@myVar", "@id", "@name", "@price" };

        private static readonly HashSet<string> AllowedStatementStarters = new(StringComparer.OrdinalIgnoreCase)
        {
            "SELECT", "INSERT", "UPDATE", "DELETE", "MERGE", "DECLARE", "SET", "BEGIN", "IF", "WHILE", "FOR", "FOREACH"
        };

        public GrammarWalkGenerator(GrammarStateTree tree, Random rng)
        {
            _tree = tree;
            _rng = rng;
        }

        public List<Token> GenerateQuery()
        {
            var tokens = new List<Token>();
            var currentState = _tree.Root;
            int stepCount = 0;
            const int maxSteps = 150; // Increased to support multi-statement scripts

            _tokenQueue.Clear();

            while (stepCount++ < maxSteps)
            {
                if (_tokenQueue.Count > 0)
                {
                    var token = _tokenQueue.Dequeue();
                    tokens.Add(token with { Line = 1, Column = tokens.Count + 1, EndLine = 1, EndColumn = tokens.Count + 1 + token.Value.Length });
                    continue;
                }

                if (currentState.Transitions.Count == 0)
                {
                    break;
                }

                // Escape hatch to end statements and avoid self-loop traps
                if (currentState != _tree.Root && tokens.Count > 4 && _rng.Next(10) < 3)
                {
                    var semi = new Token(TokenType.SEMICOLON, ";", 1, tokens.Count + 1, 1, tokens.Count + 2);
                    tokens.Add(semi);
                    currentState = _tree.Root;
                    continue;
                }

                // Pick a transition randomly, filtering statement starters at Root
                List<StateTransition> transitions;
                if (currentState == _tree.Root)
                {
                    transitions = currentState.Transitions
                        .Where(t => t.Label != null && AllowedStatementStarters.Contains(t.Label))
                        .ToList();

                    if (transitions.Count == 0)
                    {
                        transitions = currentState.Transitions;
                    }
                }
                else
                {
                    transitions = currentState.Transitions;
                }

                var transition = transitions[_rng.Next(transitions.Count)];
                
                // Try generating tokens for this transition
                bool generated = TryGenerateForTransition(transition);
                if (generated)
                {
                    if (_tokenQueue.Count > 0)
                    {
                        var token = _tokenQueue.Dequeue();
                        tokens.Add(token with { Line = 1, Column = tokens.Count + 1, EndLine = 1, EndColumn = tokens.Count + 1 + token.Value.Length });
                    }
                    currentState = transition.Target;

                    if (currentState == _tree.Root && tokens.Count > 12 && _rng.Next(2) == 0)
                    {
                        break;
                    }
                }
                else
                {
                    // Try alternative transitions
                    var shuffledTransitions = transitions.OrderBy(_ => _rng.Next()).ToList();
                    bool found = false;
                    foreach (var altTransition in shuffledTransitions)
                    {
                        if (TryGenerateForTransition(altTransition))
                        {
                            if (_tokenQueue.Count > 0)
                            {
                                var token = _tokenQueue.Dequeue();
                                tokens.Add(token with { Line = 1, Column = tokens.Count + 1, EndLine = 1, EndColumn = tokens.Count + 1 + token.Value.Length });
                            }
                            currentState = altTransition.Target;
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        break;
                    }
                }
            }

            // Flush remaining queue
            while (_tokenQueue.Count > 0)
            {
                var token = _tokenQueue.Dequeue();
                tokens.Add(token with { Line = 1, Column = tokens.Count + 1, EndLine = 1, EndColumn = tokens.Count + 1 + token.Value.Length });
            }

            tokens.Add(new Token(TokenType.EOF, "", 1, tokens.Count + 1, 1, tokens.Count + 1));
            return tokens;
        }

        private bool TryGenerateForTransition(StateTransition transition)
        {
            var label = transition.Label;
            if (!string.IsNullOrEmpty(label))
            {
                if (label.Equals("<table_source>", StringComparison.OrdinalIgnoreCase) ||
                    label.Equals("<join_table>", StringComparison.OrdinalIgnoreCase))
                {
                    var table = "src." + MockTables[_rng.Next(MockTables.Length)];
                    var tok = new Token(TokenType.IDENTIFIER, table, 0, 0, 0, 0);
                    if (transition.Condition(tok)) { _tokenQueue.Enqueue(tok); return true; }
                }
                else if (label.Equals("<column_name>", StringComparison.OrdinalIgnoreCase))
                {
                    var col = MockColumns[_rng.Next(MockColumns.Length)];
                    var tok = new Token(TokenType.IDENTIFIER, col, 0, 0, 0, 0);
                    if (transition.Condition(tok)) { _tokenQueue.Enqueue(tok); return true; }
                }
                else if (label.Equals("<connection_name>", StringComparison.OrdinalIgnoreCase))
                {
                    var tok = new Token(TokenType.IDENTIFIER, "src", 0, 0, 0, 0);
                    if (transition.Condition(tok)) { _tokenQueue.Enqueue(tok); return true; }
                }
                else if (label.Equals("<variable_name>", StringComparison.OrdinalIgnoreCase))
                {
                    var val = MockVariables[_rng.Next(MockVariables.Length)];
                    var tok = new Token(TokenType.VARIABLE, val, 0, 0, 0, 0);
                    if (transition.Condition(tok)) { _tokenQueue.Enqueue(tok); return true; }
                }
                else if (label.Equals("<expression>", StringComparison.OrdinalIgnoreCase) ||
                         label.Equals("<value>", StringComparison.OrdinalIgnoreCase) ||
                         label.Equals("<sets_assignment_token>", StringComparison.OrdinalIgnoreCase) ||
                         label.Equals("<declaration_token>", StringComparison.OrdinalIgnoreCase) ||
                         label.Equals("<expression_token>", StringComparison.OrdinalIgnoreCase))
                {
                    // Generate a recursive expression
                    var expr = GenerateExpressionTokens(3);
                    if (expr.Count > 0 && transition.Condition(expr[0]))
                    {
                        foreach (var t in expr) _tokenQueue.Enqueue(t);
                        return true;
                    }
                }
                else if (label.Equals("<time_expression>", StringComparison.OrdinalIgnoreCase))
                {
                    var tok = new Token(TokenType.STRING_LITERAL, "'00:00:05'", 0, 0, 0, 0);
                    if (transition.Condition(tok)) { _tokenQueue.Enqueue(tok); return true; }
                }

                var resolvedType = ResolveTokenType(label);
                if (resolvedType != null)
                {
                    var tok = new Token(resolvedType.Value, label, 0, 0, 0, 0);
                    if (transition.Condition(tok)) { _tokenQueue.Enqueue(tok); return true; }
                }
            }

            if (transition.SuggestType != null)
            {
                var tok = transition.SuggestType.Value switch
                {
                    SuggestionType.Table => new Token(TokenType.IDENTIFIER, "src." + MockTables[_rng.Next(MockTables.Length)], 0, 0, 0, 0),
                    SuggestionType.Column => new Token(TokenType.IDENTIFIER, MockColumns[_rng.Next(MockColumns.Length)], 0, 0, 0, 0),
                    SuggestionType.Connection => new Token(TokenType.IDENTIFIER, "src", 0, 0, 0, 0),
                    SuggestionType.Variable => new Token(TokenType.VARIABLE, MockVariables[_rng.Next(MockVariables.Length)], 0, 0, 0, 0),
                    SuggestionType.OptionName => new Token(TokenType.IDENTIFIER, "ENCRYPT", 0, 0, 0, 0),
                    SuggestionType.OptionValue => new Token(TokenType.IDENTIFIER, "OFF", 0, 0, 0, 0),
                    _ => null
                };
                if (tok != null && transition.Condition(tok)) { _tokenQueue.Enqueue(tok); return true; }
            }

            var probes = new List<Token>
            {
                new Token(TokenType.IDENTIFIER, "src.Users", 0, 0, 0, 0),
                new Token(TokenType.IDENTIFIER, "UserID", 0, 0, 0, 0),
                new Token(TokenType.VARIABLE, "@id", 0, 0, 0, 0),
                new Token(TokenType.NUMBER, "1", 0, 0, 0, 0),
                new Token(TokenType.STRING_LITERAL, "'Alice'", 0, 0, 0, 0),
                new Token(TokenType.STAR, "*", 0, 0, 0, 0),
                new Token(TokenType.COMMA, ",", 0, 0, 0, 0),
                new Token(TokenType.LPAREN, "(", 0, 0, 0, 0),
                new Token(TokenType.RPAREN, ")", 0, 0, 0, 0),
                new Token(TokenType.DOT, ".", 0, 0, 0, 0),
                new Token(TokenType.EQUALS, "=", 0, 0, 0, 0),
                new Token(TokenType.PLUS, "+", 0, 0, 0, 0),
                new Token(TokenType.MINUS, "-", 0, 0, 0, 0),
                new Token(TokenType.BANG, "!", 0, 0, 0, 0),
                new Token(TokenType.SEMICOLON, ";", 0, 0, 0, 0),
            };

            foreach (var probe in probes)
            {
                if (transition.Condition(probe))
                {
                    _tokenQueue.Enqueue(probe);
                    return true;
                }
            }

            return false;
        }

        private List<Token> GenerateExpressionTokens(int depth)
        {
            var exprTokens = new List<Token>();

            // Leaf base case
            if (depth <= 0 || _rng.Next(3) == 0)
            {
                exprTokens.Add(GenerateLeafToken());
                return exprTokens;
            }

            int choice = _rng.Next(4);
            switch (choice)
            {
                case 0: // Binary expression: left OP right
                    exprTokens.AddRange(GenerateExpressionTokens(depth - 1));
                    exprTokens.Add(GenerateOperatorToken());
                    exprTokens.AddRange(GenerateExpressionTokens(depth - 1));
                    break;

                case 1: // Function call: FUNC(expr)
                    exprTokens.Add(GenerateFunctionToken());
                    exprTokens.Add(new Token(TokenType.LPAREN, "(", 0, 0, 0, 0));
                    exprTokens.AddRange(GenerateExpressionTokens(depth - 1));
                    exprTokens.Add(new Token(TokenType.RPAREN, ")", 0, 0, 0, 0));
                    break;

                case 2: // Parenthesized: ( expr )
                    exprTokens.Add(new Token(TokenType.LPAREN, "(", 0, 0, 0, 0));
                    exprTokens.AddRange(GenerateExpressionTokens(depth - 1));
                    exprTokens.Add(new Token(TokenType.RPAREN, ")", 0, 0, 0, 0));
                    break;

                default: // Function call with 2 args: FUNC2(expr, expr)
                    exprTokens.Add(GenerateFunction2Token());
                    exprTokens.Add(new Token(TokenType.LPAREN, "(", 0, 0, 0, 0));
                    exprTokens.AddRange(GenerateExpressionTokens(depth - 1));
                    exprTokens.Add(new Token(TokenType.COMMA, ",", 0, 0, 0, 0));
                    exprTokens.AddRange(GenerateExpressionTokens(depth - 1));
                    exprTokens.Add(new Token(TokenType.RPAREN, ")", 0, 0, 0, 0));
                    break;
            }

            return exprTokens;
        }

        private Token GenerateLeafToken()
        {
            return _rng.Next(5) switch
            {
                0 => new Token(TokenType.IDENTIFIER, MockColumns[_rng.Next(MockColumns.Length)], 0, 0, 0, 0),
                1 => new Token(TokenType.NUMBER, _rng.Next(1, 100).ToString(), 0, 0, 0, 0),
                2 => new Token(TokenType.STRING_LITERAL, $"'Val_{_rng.Next(1, 10)}'", 0, 0, 0, 0),
                3 => new Token(TokenType.TRUE, "TRUE", 0, 0, 0, 0),
                _ => new Token(TokenType.FALSE, "FALSE", 0, 0, 0, 0)
            };
        }

        private Token GenerateOperatorToken()
        {
            return _rng.Next(8) switch
            {
                0 => new Token(TokenType.PLUS, "+", 0, 0, 0, 0),
                1 => new Token(TokenType.MINUS, "-", 0, 0, 0, 0),
                2 => new Token(TokenType.STAR, "*", 0, 0, 0, 0),
                3 => new Token(TokenType.EQUALS, "=", 0, 0, 0, 0),
                4 => new Token(TokenType.GREATER_THAN, ">", 0, 0, 0, 0),
                5 => new Token(TokenType.LESS_THAN, "<", 0, 0, 0, 0),
                6 => new Token(TokenType.AND, "AND", 0, 0, 0, 0),
                _ => new Token(TokenType.OR, "OR", 0, 0, 0, 0)
            };
        }

        private Token GenerateFunctionToken()
        {
            return _rng.Next(2) switch
            {
                0 => new Token(TokenType.UPPER, "UPPER", 0, 0, 0, 0),
                _ => new Token(TokenType.LOWER, "LOWER", 0, 0, 0, 0)
            };
        }

        private Token GenerateFunction2Token()
        {
            return _rng.Next(2) switch
            {
                0 => new Token(TokenType.CONCAT, "CONCAT", 0, 0, 0, 0),
                _ => new Token(TokenType.IDENTIFIER, "ISNULL", 0, 0, 0, 0)
            };
        }

        private TokenType? ResolveTokenType(string value)
        {
            if (string.Equals(value, ";", StringComparison.Ordinal)) return TokenType.SEMICOLON;
            if (string.Equals(value, ",", StringComparison.Ordinal)) return TokenType.COMMA;
            if (string.Equals(value, ".", StringComparison.Ordinal)) return TokenType.DOT;
            if (string.Equals(value, "(", StringComparison.Ordinal)) return TokenType.LPAREN;
            if (string.Equals(value, ")", StringComparison.Ordinal)) return TokenType.RPAREN;
            if (string.Equals(value, "=", StringComparison.Ordinal)) return TokenType.EQUALS;
            if (string.Equals(value, "*", StringComparison.Ordinal)) return TokenType.STAR;
            if (string.Equals(value, "!", StringComparison.Ordinal)) return TokenType.BANG;

            if (Enum.TryParse<TokenType>(value.ToUpper(), out var type))
            {
                return type;
            }

            return null;
        }
    }
}
