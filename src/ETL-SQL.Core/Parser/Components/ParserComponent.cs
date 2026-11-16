using System.Collections.Generic;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Core.Parser.Components;

public abstract class ParserComponent
{
    protected readonly IParser _parser;
    protected readonly StatementParser _parent;

    protected ParserComponent(IParser parser, StatementParser parent)
    {
        _parser = parser;
        _parent = parent;
    }

    protected Token Advance() => _parser.Advance();
    protected bool Match(TokenType type) => _parser.Match(type);
    protected Token Consume(TokenType type, string message) => _parser.Consume(type, message);
    protected Token ConsumeIdentifier(string message) => _parser.ConsumeIdentifier(message);
    protected Expression ParseExpression() => _parser.ParseExpression();
    protected TableReference ParseTableReference(bool allowFunction = true, bool allowWithClause = true, bool allowAlias = true) => _parser.ParseTableReference(allowFunction, allowWithClause, allowAlias);

    protected bool MatchIdentifier(string value)
    {
        if (_parser.Current.Type == TokenType.IDENTIFIER &&
            _parser.Current.Value.Equals(value, System.StringComparison.OrdinalIgnoreCase))
        {
            _parser.Advance();
            return true;
        }
        return false;
    }

    protected bool IsIdentifier(string value) =>
        _parser.Current.Type == TokenType.IDENTIFIER &&
        _parser.Current.Value.Equals(value, System.StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Parses <c>( KEY = expr, ... )</c> after WITH. A key the statement does not know is a syntax
    /// error naming the accepted keys; it used to be parsed and dropped, so a misspelt or unsupported
    /// option ran the statement as if it were absent (ROADMAP FO02). A repeated key keeps the last value.
    /// </summary>
    protected Dictionary<string, Expression> ParseWithOptions(string statement, params string[] accepted)
    {
        Consume(TokenType.LPAREN, "Expected '(' after WITH");
        var options = new Dictionary<string, Expression>(System.StringComparer.OrdinalIgnoreCase);
        while (_parser.Current.Type != TokenType.RPAREN && _parser.Current.Type != TokenType.EOF)
        {
            var key = Advance();
            if (System.Array.FindIndex(accepted, a => a.Equals(key.Value, System.StringComparison.OrdinalIgnoreCase)) < 0)
                throw new SyntaxException(
                    $"{statement} has no WITH option '{key.Value}'. Accepted options: {string.Join(", ", accepted)}.",
                    key.Line, key.Column);
            Consume(TokenType.EQUALS, "Expected '=' after option name");
            options[key.Value] = ParseExpression();
            if (!Match(TokenType.COMMA)) break;
        }
        Consume(TokenType.RPAREN, "Expected ')' after WITH options");
        return options;
    }
}
