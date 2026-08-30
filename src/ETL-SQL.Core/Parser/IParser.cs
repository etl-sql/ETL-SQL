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
    int LastTokenEndOffset { get; }
    Token LookAhead(int distance);
    Token Advance();
    bool Match(TokenType type);
    Token Consume(TokenType type, string message);
    Token ConsumeIdentifier(string message);
    Statement ParseStatement();
    Statement ParseQuery();
    Statement ParseDuckPivotStatement();
    Statement ParseDuckUnpivotStatement();
    Expression ParseExpression();

    /// <summary>Parses at comparison precedence, stopping below <c>AND</c>/<c>OR</c>.</summary>
    Expression ParseExpressionNoLogical();

    /// <summary>Parses at additive precedence, the level SQL <c>BETWEEN</c> bounds use.</summary>
    Expression ParseExpressionTerm();

    /// <summary>
    /// The text spanned by a token range, as written. Falls back to rebuilding from the tokens
    /// themselves when the parser was handed tokens with no source, so a construct that quotes
    /// itself back (a data-quality rule, for one) still reads correctly.
    /// </summary>
    string SliceSource(Token start, Token end);
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
