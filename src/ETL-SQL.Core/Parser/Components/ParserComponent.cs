using System.Collections.Generic;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Core.Parser.Components
{
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

        // Shared helpers used by both DataParser and ExtensionParser
        protected Expression? ParseWithOverwrite()
        {
            Consume(TokenType.LPAREN, "Expected '(' after WITH");
            Expression? overwrite = null;
            while (_parser.Current.Type != TokenType.RPAREN && _parser.Current.Type != TokenType.EOF)
            {
                string key = Advance().Value;
                Consume(TokenType.EQUALS, "Expected '=' after option name");
                if (System.StringComparer.OrdinalIgnoreCase.Equals(key, "OVERWRITE"))
                    overwrite = ParseExpression();
                else
                    ParseExpression();
                if (!Match(TokenType.COMMA)) break;
            }
            Consume(TokenType.RPAREN, "Expected ')' after WITH options");
            return overwrite;
        }

        protected Expression? ParseWithRecursive()
        {
            Consume(TokenType.LPAREN, "Expected '(' after WITH");
            Expression? recursive = null;
            while (_parser.Current.Type != TokenType.RPAREN && _parser.Current.Type != TokenType.EOF)
            {
                string key = Advance().Value;
                Consume(TokenType.EQUALS, "Expected '=' after option name");
                if (System.StringComparer.OrdinalIgnoreCase.Equals(key, "RECURSIVE"))
                    recursive = ParseExpression();
                else
                    ParseExpression();
                if (!Match(TokenType.COMMA)) break;
            }
            Consume(TokenType.RPAREN, "Expected ')' after WITH options");
            return recursive;
        }
    }
}
