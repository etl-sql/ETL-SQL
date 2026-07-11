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

        private static readonly string[] MockTables = { "Users", "Products", "Sales", "Employees" };
        private static readonly string[] MockColumns = { "UserID", "UserName", "Email", "ProductID", "Price", "Quantity", "Total", "EmpID", "Salary", "ProductName" };
        private static readonly string[] MockVariables = { "@myVar", "@id", "@name", "@price" };

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
            const int maxSteps = 60;

            while (stepCount++ < maxSteps)
            {
                if (currentState.Transitions.Count == 0)
                {
                    break;
                }

                // Pick a transition randomly
                var transition = currentState.Transitions[_rng.Next(currentState.Transitions.Count)];
                var token = GenerateTokenFor(transition);

                if (token != null)
                {
                    tokens.Add(token with { Line = 1, Column = tokens.Count + 1, EndLine = 1, EndColumn = tokens.Count + 1 + token.Value.Length });
                    currentState = transition.Target;

                    // If we reach root and have a decent query size, we can stop
                    if (currentState == _tree.Root && tokens.Count > 5 && _rng.Next(2) == 0)
                    {
                        break;
                    }
                }
                else
                {
                    // Try to find any other transition that we can generate a token for
                    var shuffledTransitions = currentState.Transitions.OrderBy(_ => _rng.Next()).ToList();
                    bool found = false;
                    foreach (var altTransition in shuffledTransitions)
                    {
                        var altToken = GenerateTokenFor(altTransition);
                        if (altToken != null)
                        {
                            tokens.Add(altToken with { Line = 1, Column = tokens.Count + 1, EndLine = 1, EndColumn = tokens.Count + 1 + altToken.Value.Length });
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

            // Append EOF token
            tokens.Add(new Token(TokenType.EOF, "", 1, tokens.Count + 1, 1, tokens.Count + 1));
            return tokens;
        }

        private Token? GenerateTokenFor(StateTransition transition)
        {
            var label = transition.Label;
            if (!string.IsNullOrEmpty(label))
            {
                if (label.Equals("<table_source>", StringComparison.OrdinalIgnoreCase) ||
                    label.Equals("<join_table>", StringComparison.OrdinalIgnoreCase))
                {
                    var table = "src." + MockTables[_rng.Next(MockTables.Length)];
                    var tok = new Token(TokenType.IDENTIFIER, table, 0, 0, 0, 0);
                    if (transition.Condition(tok)) return tok;
                }
                else if (label.Equals("<column_name>", StringComparison.OrdinalIgnoreCase))
                {
                    var col = MockColumns[_rng.Next(MockColumns.Length)];
                    var tok = new Token(TokenType.IDENTIFIER, col, 0, 0, 0, 0);
                    if (transition.Condition(tok)) return tok;
                }
                else if (label.Equals("<connection_name>", StringComparison.OrdinalIgnoreCase))
                {
                    var tok = new Token(TokenType.IDENTIFIER, "src", 0, 0, 0, 0);
                    if (transition.Condition(tok)) return tok;
                }
                else if (label.Equals("<variable_name>", StringComparison.OrdinalIgnoreCase))
                {
                    var val = MockVariables[_rng.Next(MockVariables.Length)];
                    var tok = new Token(TokenType.VARIABLE, val, 0, 0, 0, 0);
                    if (transition.Condition(tok)) return tok;
                }
                else if (label.Equals("<expression>", StringComparison.OrdinalIgnoreCase) ||
                         label.Equals("<value>", StringComparison.OrdinalIgnoreCase) ||
                         label.Equals("<sets_assignment_token>", StringComparison.OrdinalIgnoreCase))
                {
                    string val = _rng.Next(4) switch
                    {
                        0 => "100",
                        1 => "'User_1'",
                        2 => "Price",
                        _ => "TRUE"
                    };
                    var tok = new Token(TokenType.IDENTIFIER, val, 0, 0, 0, 0);
                    if (transition.Condition(tok)) return tok;
                }
                else if (label.Equals("<time_expression>", StringComparison.OrdinalIgnoreCase))
                {
                    var tok = new Token(TokenType.STRING_LITERAL, "'00:00:05'", 0, 0, 0, 0);
                    if (transition.Condition(tok)) return tok;
                }

                var resolvedType = ResolveTokenType(label);
                if (resolvedType != null)
                {
                    var tok = new Token(resolvedType.Value, label, 0, 0, 0, 0);
                    if (transition.Condition(tok)) return tok;
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
                if (tok != null && transition.Condition(tok)) return tok;
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
                    return probe;
                }
            }

            return null;
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
