using ETL_SQL.Common;
using ETL_SQL.Core;

namespace ETL_SQL.Core.Parser;
public interface IParser
{
    Token Current { get; }
    Token Peek { get; }
    Token Peek2 { get; }
    Token Previous { get; }
    int LastTokenEndLine { get; }
    int LastTokenEndColumn { get; }
    Token LookAhead(int distance);
    Token Advance();
    bool Match(TokenType type);
    Token Consume(TokenType type, string message);
    Token ConsumeIdentifier(string message);
    Statement ParseStatement();
    Statement ParseQuery();
    Expression ParseExpression();
    string ParseType();
    TableReference ParseTableReference(bool allowFunction = true, bool allowWithClause = true, bool allowAlias = true);
    SelectColumn ParseSelectColumn();
    OutputClause ParseOutputClause();
    bool IsIdentifier(Token token);
    bool IsDataType(TokenType type);
    void ParseMetadataTags(string tagContent, Dictionary<string, string> metadata);
    List<JoinClause> ParseJoins();
    void Backtrack();
    string CaptureRawBlock();
}
