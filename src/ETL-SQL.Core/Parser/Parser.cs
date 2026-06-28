using System;
using System.Collections.Generic;

using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Core.Parser;
/// <summary>
/// Recursive descent parser for the ETL-SQL language.
/// Orchestrates expression and statement parsing.
/// </summary>
public class Parser : IParser
{
    private readonly List<Token> _tokens;
    private int _position;
    private readonly string _source;
    private readonly ExpressionParser _expressionParser;
    private readonly StatementParser _statementParser;

    private static readonly HashSet<TokenType> IdentifierTokens = new()
    {
        TokenType.IDENTIFIER, TokenType.LINEAGE, TokenType.FILE, TokenType.DIRECTORY,
        TokenType.SFTP, TokenType.FTP_CONN, TokenType.FLATFILE, TokenType.JSON, TokenType.XML,
        TokenType.EXCEL, TokenType.AZURE_BLOB, TokenType.SYSDATE, TokenType.CURRENT_TIMESTAMP,
        TokenType.CURRENT_DATE, TokenType.CURRENT_TIME, TokenType.YEAR, TokenType.MONTH,
        TokenType.DAY, TokenType.HOUR, TokenType.MINUTE, TokenType.SECOND,
        TokenType.TELEMETRY, TokenType.POSITION, TokenType.FORMAT, TokenType.TARGET,
        TokenType.TYPE, TokenType.VERSION, TokenType.SOURCE, TokenType.MATCHED,
        TokenType.TABLE, TokenType.TAG, TokenType.VALUE, TokenType.BITS,
        TokenType.ALGORITHM, TokenType.PASSPHRASE, TokenType.COMMENT, TokenType.DATE,
        TokenType.GETDATE, TokenType.RETURNS, TokenType.CONFIG, TokenType.CLOSE,
        TokenType.MIN, TokenType.MAX, TokenType.SUM, TokenType.AVG, TokenType.COUNT,
        TokenType.EVERY,
        TokenType.STEP, TokenType.TOP, TokenType.BOTTOM, TokenType.LEFT, TokenType.RIGHT,
        TokenType.ASC, TokenType.DESC, TokenType.FIRST, TokenType.NEXT, TokenType.ONLY,
        TokenType.NO, TokenType.OTHERS,
        TokenType.ENABLE, TokenType.DISABLE, TokenType.TRIGGER, TokenType.EXPORT
    };


    private static readonly HashSet<TokenType> DataTypeTokens = new()
    {
        TokenType.TIME, TokenType.JSON, TokenType.XML, TokenType.DATETIME, TokenType.CHAR,
        TokenType.INT, TokenType.INTEGER, TokenType.BIGINT, TokenType.SMALLINT, TokenType.TINYINT,
        TokenType.BIT, TokenType.BOOLEAN, TokenType.BOOL, TokenType.DECIMAL, TokenType.NUMERIC,
        TokenType.MONEY, TokenType.SMALLMONEY, TokenType.FLOAT, TokenType.REAL, TokenType.DOUBLE,
        TokenType.DATE, TokenType.DATETIME2, TokenType.SMALLDATETIME, TokenType.DATETIMEOFFSET,
        TokenType.TIMESTAMP, TokenType.VARCHAR, TokenType.NCHAR, TokenType.NVARCHAR,
        TokenType.TEXT, TokenType.NTEXT, TokenType.BINARY, TokenType.VARBINARY, TokenType.IMAGE,
        TokenType.UNIQUEIDENTIFIER, TokenType.UUID, TokenType.GUID, TokenType.GEOMETRY,
        TokenType.GEOGRAPHY, TokenType.HIERARCHYID, TokenType.VARIANT, TokenType.SQL_VARIANT,
        TokenType.ANY, TokenType.TABLE, TokenType.STRING, TokenType.SENSITIVE, TokenType.SECRET,
        TokenType.VARCHAR2, TokenType.MINMAX, TokenType.MARKDOWN, TokenType.PATH, TokenType.RELDATE
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="Parser"/> class with the specified tokens.
    /// </summary>
    /// <param name="tokens">The list of tokens to parse.</param>
    public Parser(List<Token> tokens, string source = "")
    {
        _tokens = tokens;
        _source = source;
        _position = 0;
        _expressionParser = new ExpressionParser(this);
        _statementParser = new StatementParser(this);
    }

    public Token Current => _position < _tokens.Count ? _tokens[_position] : _tokens[^1];
    public Token Peek => _position + 1 < _tokens.Count ? _tokens[_position + 1] : _tokens[^1];
    public Token Peek2 => _position + 2 < _tokens.Count ? _tokens[_position + 2] : _tokens[^1];
    public Token Previous => _position > 0 ? _tokens[_position - 1] : _tokens[0];

    public Token LookAhead(int distance)
    {
        int pos = _position + distance;
        if (pos < 0) return _tokens[0];
        if (pos >= _tokens.Count) return _tokens[^1];
        return _tokens[pos];
    }

    public int LastTokenEndLine { get; private set; }
    public int LastTokenEndColumn { get; private set; }

    public void Backtrack()
    {
        if (_position > 0) _position--;
        // Minimal backtracking won't break simple range tracking for now.
        // In a better parser, we'd store a history of end positions.
    }

    public Token Advance()
    {
        var token = Current;
        if (token.Type != TokenType.EOF)
        {
            LastTokenEndLine = token.EndLine;
            LastTokenEndColumn = token.EndColumn;
            _position++;
        }
        return token;
    }

    public bool Match(TokenType type)
    {
        if (Current.Type == type)
        {
            Advance();
            return true;
        }
        return false;
    }

    public Token Consume(TokenType type, string message)
    {
        if (Current.Type == type)
        {
            var t = Advance();
            return t;
        }
        throw new SyntaxException(message, Current.Line, Current.Column);
    }

    public Token ConsumeIdentifier(string message)
    {
        if (IsIdentifier(Current)) return Advance();
        throw new SyntaxException(message, Current.Line, Current.Column);
    }

    public bool IsIdentifier(Token token)
    {
        // Context-aware keyword handling for common join/set keywords used as identifiers/aliases.
        // This allows patterns like "FROM #SmallTable inner" while preserving "INNER JOIN".
        if ((token.Type >= TokenType.JOIN && token.Type <= TokenType.INTERSECT) || IsJoinOrSetWord(token.Value))
        {
            // JOIN, UNION, EXCEPT, INTERSECT are strictly reserved as clause starters.
            if (token.Type == TokenType.JOIN || token.Type == TokenType.UNION ||
                token.Type == TokenType.EXCEPT || token.Type == TokenType.INTERSECT ||
                token.Value.Equals("JOIN", StringComparison.OrdinalIgnoreCase) ||
                token.Value.Equals("UNION", StringComparison.OrdinalIgnoreCase) ||
                token.Value.Equals("EXCEPT", StringComparison.OrdinalIgnoreCase) ||
                token.Value.Equals("INTERSECT", StringComparison.OrdinalIgnoreCase))
                return false;

            // Join modifiers (INNER, LEFT, etc.) can be aliases if they are not followed
            // by tokens that unambiguously start a JOIN clause.
            var next = Peek;
            if (next.Type == TokenType.JOIN || next.Type == TokenType.HASH ||
                next.Type == TokenType.LOOP || next.Type == TokenType.MERGE ||
                next.Type == TokenType.APPLY || next.Type == TokenType.OUTER ||
                next.Type == TokenType.INNER || next.Type == TokenType.LEFT ||
                next.Type == TokenType.RIGHT || next.Type == TokenType.FULL ||
                next.Type == TokenType.CROSS || next.Type == TokenType.SEMI ||
                next.Type == TokenType.ANTI ||
                IsJoinStarterWord(next.Value))
                return false;

            return true;
        }

        if (IdentifierTokens.Contains(token.Type)) return true;
        if (token.Type == TokenType.EOF) return false;

        // Connector types from metadata are always valid identifiers
        if (LanguageMetadata.IsConnectorType(token.Value)) return true;

        // Data types are allowed as identifiers in many contexts
        if (IsDataType(token.Type)) return true;

        // Report-SQL and overlay tokens are contextual: reserved inside their own clauses,
        // but should be allowed as identifiers/function names elsewhere.
        if (token.Type >= TokenType.VISUAL && token.Type <= TokenType.ICON_SET)
            return true;


        // Symbols and operators should never be identifiers.
        if (token.Type >= TokenType.STAR && token.Type < TokenType.VISUAL) return false;

        // Specific keyword exceptions
        var val = token.Value;
        if (val.Equals("VALUE", StringComparison.OrdinalIgnoreCase)) return true;
        if (val.Equals("EMAIL", StringComparison.OrdinalIgnoreCase)) return true;

        if (!IsKeyword(val)) return true;

        return false;
    }

    private static bool IsJoinOrSetWord(string value)
        => value.Equals("JOIN", StringComparison.OrdinalIgnoreCase)
        || value.Equals("INNER", StringComparison.OrdinalIgnoreCase)
        || value.Equals("LEFT", StringComparison.OrdinalIgnoreCase)
        || value.Equals("RIGHT", StringComparison.OrdinalIgnoreCase)
        || value.Equals("OUTER", StringComparison.OrdinalIgnoreCase)
        || value.Equals("FULL", StringComparison.OrdinalIgnoreCase)
        || value.Equals("CROSS", StringComparison.OrdinalIgnoreCase)
        || value.Equals("APPLY", StringComparison.OrdinalIgnoreCase)
        || value.Equals("SEMI", StringComparison.OrdinalIgnoreCase)
        || value.Equals("ANTI", StringComparison.OrdinalIgnoreCase)
        || value.Equals("HASH", StringComparison.OrdinalIgnoreCase)
        || value.Equals("LOOP", StringComparison.OrdinalIgnoreCase)
        || value.Equals("MERGE", StringComparison.OrdinalIgnoreCase)
        || value.Equals("UNION", StringComparison.OrdinalIgnoreCase)
        || value.Equals("EXCEPT", StringComparison.OrdinalIgnoreCase)
        || value.Equals("MINUS", StringComparison.OrdinalIgnoreCase)
        || value.Equals("INTERSECT", StringComparison.OrdinalIgnoreCase);

    private static bool IsJoinStarterWord(string value)
        => value.Equals("JOIN", StringComparison.OrdinalIgnoreCase)
        || value.Equals("INNER", StringComparison.OrdinalIgnoreCase)
        || value.Equals("LEFT", StringComparison.OrdinalIgnoreCase)
        || value.Equals("RIGHT", StringComparison.OrdinalIgnoreCase)
        || value.Equals("OUTER", StringComparison.OrdinalIgnoreCase)
        || value.Equals("FULL", StringComparison.OrdinalIgnoreCase)
        || value.Equals("CROSS", StringComparison.OrdinalIgnoreCase)
        || value.Equals("APPLY", StringComparison.OrdinalIgnoreCase)
        || value.Equals("HASH", StringComparison.OrdinalIgnoreCase)
        || value.Equals("LOOP", StringComparison.OrdinalIgnoreCase)
        || value.Equals("MERGE", StringComparison.OrdinalIgnoreCase)
        || value.Equals("SEMI", StringComparison.OrdinalIgnoreCase)
        || value.Equals("ANTI", StringComparison.OrdinalIgnoreCase);

    public bool IsDataType(TokenType type) => DataTypeTokens.Contains(type);

    /// <summary>
    /// Parses the entire token stream into a <see cref="Script"/> object.
    /// </summary>
    /// <returns>A script containing the parsed statements and any diagnostics.</returns>
    public Script Parse()
    {
        var script = new Script();

        // Capture script metadata from header comments (/* @tag: v */ or -- @tag: v)
        while (Current.Type == TokenType.COLUMN_TAG)
        {
            var content = Advance().Value;
            // Normalize embedded newlines to semicolons so ParseMetadataTags handles
            // both block-comment multi-line format and semicolon-separated inline format
            var normalized = content.Replace('\r', ';').Replace('\n', ';');
            ParseMetadataTags(normalized, script.Metadata);
        }

        while (Current.Type != TokenType.EOF)
        {
            try
            {
                if (Match(TokenType.SEMICOLON)) continue;
                if (Current.Type == TokenType.COLUMN_TAG) { Advance(); continue; } // Skip inline tags

                script.Statements.Add(ParseStatement());
            }
            catch (SyntaxException ex)
            {
                script.Diagnostics.Add(new Diagnostic(ex.Message, ex.Line, ex.Column, DiagnosticSeverity.Error, "SYNTAX"));
                // Error recovery: skip to next semicolon or EOF
                while (Current.Type != TokenType.EOF && Current.Type != TokenType.SEMICOLON)
                {
                    Advance();
                }
                if (Current.Type == TokenType.SEMICOLON) Advance();
            }
            catch (Exception ex)
            {
                script.Diagnostics.Add(new Diagnostic(ex.Message, Current.Line, Current.Column, DiagnosticSeverity.Error, "INTERNAL"));
                // Fallback for unexpected errors
                if (Current.Type != TokenType.EOF) Advance();
            }
        }
        ValidateGotoScoping(script);
        return script;
    }

    /// <summary>Parses a single statement from the current position.</summary>
    public Statement ParseStatement()
    {
        return _statementParser.ParseStatement();
    }


    /// <summary>Parses a top-level query (SELECT or set operation).</summary>
    public Statement ParseQuery()
    {
        if (!Match(TokenType.SELECT)) throw new SyntaxException("Expected SELECT", Current.Line, Current.Column);
        var left = ParseSelectBody();

        while (Current.Type == TokenType.UNION || Current.Type == TokenType.EXCEPT || Current.Type == TokenType.INTERSECT || IsMinusKeyword())
        {
            var op = SetOpType.UNION;
            bool byName = false;
            if (Match(TokenType.UNION))
            {
                op = Match(TokenType.ALL) ? SetOpType.UNION_ALL : SetOpType.UNION;
                if (Current.Type == TokenType.BY) { Advance(); ConsumeWord("NAME", "Expected 'NAME' after 'BY'"); byName = true; }
            }
            else if (Match(TokenType.EXCEPT))
            {
                op = SetOpType.EXCEPT;
            }
            else if (Match(TokenType.INTERSECT))
            {
                op = SetOpType.INTERSECT;
            }
            else // MINUS — Oracle/DuckDB alias for EXCEPT
            {
                Advance();
                op = SetOpType.EXCEPT;
            }

            Consume(TokenType.SELECT, "Expected SELECT after set operator");
            var right = ParseSelectBody();
            left = new SetOperationStatement(left, op, right) { ByName = byName };
        }

        // Global semicolon consumption for top level queries
        if (Current.Type == TokenType.SEMICOLON) Advance();
        return left;
    }

    public string CaptureRawBlock()
    {
        // Assumes Current is BEGIN
        var startToken = Consume(TokenType.BEGIN, "Expected BEGIN");
        int depth = 1;
        var tokens = new List<Token>();

        while (depth > 0 && Current.Type != TokenType.EOF)
        {
            if (Current.Type == TokenType.BEGIN) depth++;
            else if (Current.Type == TokenType.END) depth--;

            if (depth == 0) break;
            tokens.Add(Advance());
        }

        var endToken = Consume(TokenType.END, "Expected END");

        if (!string.IsNullOrEmpty(_source))
            return ReconstructFromSource(startToken, endToken);

        // Fallback: reconstruct from tokens if source is missing
        return string.Join(" ", tokens.Select(t => t.Type == TokenType.STRING_LITERAL ? $"'{t.Value.Replace("'", "''")}'" : t.Value));
    }

    private string ReconstructFromSource(Token startToken, Token endToken)
    {
        if (string.IsNullOrEmpty(_source)) return "";
        int start = startToken.EndOffset;
        int end = endToken.Offset;
        if (start < 0 || end > _source.Length || start > end) return "";
        return _source.Substring(start, end - start).Trim();
    }

    private Statement ParseSelect()
    {
        return ParseQuery();
    }

    private Statement ParseSelectBody()
    {
        var startToken = Current;
        bool isDistinct = Match(TokenType.DISTINCT);
        if (!isDistinct) Match(TokenType.ALL);
        Expression? topCount = null;
        bool isTopPercent = false;
        bool withTies = false;
        if (Match(TokenType.TOP))
        {
            bool hasParen = Match(TokenType.LPAREN);
            if (Current.Type == TokenType.NUMBER)
            {
                var t = Advance();
                topCount = new LiteralExpression(decimal.Parse(t.Value), TokenType.NUMBER) { Line = t.Line, Column = t.Column };
            }
            else if (Current.Type == TokenType.VARIABLE)
            {
                var t = Advance();
                topCount = new VariableExpression(t.Value) { Line = t.Line, Column = t.Column };
            }
            else throw new SyntaxException("Expected number or variable after TOP", Current.Line, Current.Column);

            if (hasParen) Consume(TokenType.RPAREN, "Expected ')' after TOP");

            if (Match(TokenType.PERCENT)) isTopPercent = true;
            if (Match(TokenType.WITH))
            {
                Consume(TokenType.TIES, "Expected 'TIES' after 'WITH' in TOP clause");
                withTies = true;
            }
        }

        // SELECT col1, col2, ... [INTO table] FROM table [WHERE cond]
        var columns = new List<SelectColumn>();
        TableReference? intoTable = null;

        columns.Add(ParseSelectColumn());
        while (Match(TokenType.COMMA))
        {
            if (AtClauseEnd()) break; // tolerate a trailing comma
            columns.Add(ParseSelectColumn());
        }

        if (Match(TokenType.INTO))
        {
            intoTable = ParseTableReference(allowAlias: false);
        }

        // Riverside: removed swallowed catch block to expose errors.
        TableReference? fromTable = null;
        var preJoins = new List<JoinClause>();
        if (Match(TokenType.FROM))
        {
            bool parenthesizedFrom = false;
            if (Current.Type == TokenType.LPAREN && Peek.Type != TokenType.SELECT && Peek.Type != TokenType.VALUES)
            {
                parenthesizedFrom = true;
                Consume(TokenType.LPAREN, "Expected '('");
            }

            fromTable = ParseTableReference();
            // SQL-89 comma-separated multi-table FROM (implicit CROSS JOINs).
            // `, LATERAL (<subquery>)` is the comma form of CROSS APPLY.
            while (Current.Type == TokenType.COMMA)
            {
                Advance();
                var commaJoinType = Match(TokenType.LATERAL) ? "CROSS APPLY" : "CROSS JOIN";
                preJoins.Add(new JoinClause(commaJoinType, ParseTableReference(), new LiteralExpression(true, TokenType.NUMBER)));
            }

            if (parenthesizedFrom)
            {
                preJoins.AddRange(ParseJoins());
                Consume(TokenType.RPAREN, "Expected ')' after parenthesized FROM/JOIN list");
            }
        }
        else
        {
            // Internal "Dual" style table for empty FROM
            fromTable = new TableReference("DUAL");
        }

        var joins = preJoins;
        joins.AddRange(ParseJoins());

        Expression? whereClause = null;
        if (Match(TokenType.WHERE))
        {
            whereClause = ParseExpression();
        }

        List<Expression>? groupBy = null;
        GroupingSetClause? groupingSet = null;
        bool isGroupByAll = false;
        if (Match(TokenType.GROUP))
        {
            Consume(TokenType.BY, "Expected 'BY' after 'GROUP'");
            if (Current.Type == TokenType.ROLLUP)
            {
                Advance();
                Consume(TokenType.LPAREN, "Expected '(' after ROLLUP");
                var cols = ParseCommaSeparatedExpressions();
                Consume(TokenType.RPAREN, "Expected ')' after ROLLUP columns");
                groupingSet = new GroupingSetClause(GroupingSetType.Rollup, new List<List<Expression>> { cols });
                groupBy = cols; // plain fallback columns (engine will expand)
            }
            else if (Current.Type == TokenType.CUBE)
            {
                Advance();
                Consume(TokenType.LPAREN, "Expected '(' after CUBE");
                var cols = ParseCommaSeparatedExpressions();
                Consume(TokenType.RPAREN, "Expected ')' after CUBE columns");
                groupingSet = new GroupingSetClause(GroupingSetType.Cube, new List<List<Expression>> { cols });
                groupBy = cols;
            }
            else if (Current.Type == TokenType.GROUPING && Peek.Type == TokenType.SETS)
            {
                Advance(); // GROUPING
                Advance(); // SETS
                Consume(TokenType.LPAREN, "Expected '(' after GROUPING SETS");
                var sets = new List<List<Expression>>();
                do
                {
                    Consume(TokenType.LPAREN, "Expected '(' for each grouping set");
                    var setExprs = new List<Expression>();
                    if (Current.Type != TokenType.RPAREN)
                    {
                        setExprs.Add(ParseExpression());
                        while (Match(TokenType.COMMA)) setExprs.Add(ParseExpression());
                    }
                    Consume(TokenType.RPAREN, "Expected ')' to close grouping set");
                    sets.Add(setExprs);
                } while (Match(TokenType.COMMA));
                Consume(TokenType.RPAREN, "Expected ')' to close GROUPING SETS");
                groupingSet = new GroupingSetClause(GroupingSetType.GroupingSets, sets);
                // Collect all distinct expressions from the sets as the plain groupBy for compatibility
                groupBy = sets.SelectMany(s => s).Distinct().ToList();
            }
            else if (Current.Type == TokenType.ALL)
            {
                Advance(); // GROUP BY ALL — engine expands to all non-aggregate select expressions
                isGroupByAll = true;
            }
            else
            {
                groupBy = new List<Expression>();
                groupBy.Add(ResolvePositionalReference(ParseExpression(), columns, "GROUP BY"));
                while (Match(TokenType.COMMA))
                {
                    if (AtClauseEnd()) break; // tolerate a trailing comma
                    groupBy.Add(ResolvePositionalReference(ParseExpression(), columns, "GROUP BY"));
                }
            }
        }

        Expression? havingClause = null;
        if (Match(TokenType.HAVING))
        {
            havingClause = ParseExpression();
        }

        Expression? qualifyClause = null;
        if (Match(TokenType.QUALIFY))
        {
            qualifyClause = ParseExpression();
        }

        List<OrderByClause>? orderBy = null;
        bool orderByAll = false;
        bool orderByAllDesc = false;
        if (Current.Type == TokenType.ORDER)
        {
            Advance(); // ORDER
            Consume(TokenType.BY, "Expected BY after ORDER");
            if (Current.Type == TokenType.ALL)
            {
                Advance(); // ORDER BY ALL — engine expands to every output column
                orderByAll = true;
                if (Match(TokenType.DESC)) orderByAllDesc = true;
                else Match(TokenType.ASC);
            }
            else
            {
                orderBy = new List<OrderByClause>();
                do
                {
                    var orderExpr = ResolvePositionalReference(ParseExpression(), columns, "ORDER BY");
                    bool descending = false;
                    if (Current.Type == TokenType.DESC)
                    {
                        descending = true;
                        Advance();
                    }
                    else if (Current.Type == TokenType.ASC)
                    {
                        Advance(); // optional ASC
                    }
                    orderBy.Add(new OrderByClause(orderExpr, descending));
                }
                while (Match(TokenType.COMMA) && !AtClauseEnd());
            }
        }

        Expression? limitCount = null;
        if (Match(TokenType.LIMIT))
        {
            if (Current.Type == TokenType.NUMBER)
            {
                var t = Advance();
                limitCount = new LiteralExpression(decimal.Parse(t.Value), TokenType.NUMBER) { Line = t.Line, Column = t.Column };
            }
            else if (Current.Type == TokenType.VARIABLE)
            {
                var t = Advance();
                limitCount = new VariableExpression(t.Value) { Line = t.Line, Column = t.Column };
            }
            else throw new SyntaxException("Expected number or variable after LIMIT", Current.Line, Current.Column);
        }

        Expression? offset = null;
        if (Match(TokenType.OFFSET))
        {
            offset = ParseExpression();
            if (!Match(TokenType.ROWS)) Match(TokenType.ROW);
        }

        if (Match(TokenType.FETCH))
        {
            if (limitCount != null)
                throw new SyntaxException("Cannot combine LIMIT and FETCH in the same SELECT", Current.Line, Current.Column);

            if (!Match(TokenType.FIRST)) Consume(TokenType.NEXT, "Expected FIRST or NEXT after FETCH");
            limitCount = ParseExpression();
            if (!Match(TokenType.ROWS)) Consume(TokenType.ROW, "Expected ROW or ROWS after FETCH count");
            Consume(TokenType.ONLY, "Expected ONLY after FETCH ROWS");
        }

        var selectStmt = new SelectStatement(columns, intoTable, fromTable, joins, whereClause, groupBy, havingClause, orderBy)
        {
            Line = startToken.Line,
            Column = startToken.Column,
            EndLine = LastTokenEndLine,
            EndColumn = LastTokenEndColumn,
            IsDistinct = isDistinct,
            TopCount = topCount,
            IsTopPercent = isTopPercent,
            WithTies = withTies,
            LimitCount = limitCount,
            Offset = offset,
            GroupingSet = groupingSet,
            QualifyClause = qualifyClause,
            GroupByAll = isGroupByAll,
            OrderByAll = orderByAll,
            OrderByAllDescending = orderByAllDesc
        };

        if (Match(TokenType.FOR))
        {
            selectStmt = selectStmt with
            {
                ForClause = ParseForClause(),
                EndLine = LastTokenEndLine,
                EndColumn = LastTokenEndColumn
            };
        }

        return selectStmt;
    }

    /// <summary>
    /// Resolves a positional reference (a bare integer literal) in GROUP BY / ORDER BY to the
    /// corresponding expression in the SELECT list (1-based). Non-integer expressions are returned
    /// unchanged, so <c>GROUP BY 1 + 1</c> remains an arithmetic expression rather than a position.
    /// </summary>
    private Expression ResolvePositionalReference(Expression expr, List<SelectColumn> columns, string clause)
    {
        if (expr is not LiteralExpression lit || lit.Type != TokenType.NUMBER
            || lit.Value is not decimal d || d != decimal.Truncate(d))
        {
            return expr;
        }

        if (columns.Any(c => c.Expression is StarExpression || (c.Expression is IdentifierExpression id && (id.Name == "*" || id.Name.EndsWith(".*")))))
        {
            throw new SyntaxException($"{clause} positional reference cannot be used when the SELECT list contains a star projection.", expr.Line, expr.Column);
        }

        int ordinal = (int)d;
        if (ordinal < 1 || ordinal > columns.Count)
        {
            throw new SyntaxException($"{clause} position {ordinal} is out of range (1..{columns.Count}).", expr.Line, expr.Column);
        }

        return columns[ordinal - 1].Expression;
    }

    private List<Expression> ParseCommaSeparatedExpressions()
    {
        var result = new List<Expression>();
        if (Current.Type != TokenType.RPAREN)
        {
            result.Add(ParseExpression());
            while (Match(TokenType.COMMA)) result.Add(ParseExpression());
        }
        return result;
    }

    private ForClause ParseForClause()
    {
        ForType type;
        if (Match(TokenType.JSON)) type = ForType.JSON;
        else if (Match(TokenType.XML)) type = ForType.XML;
        else throw new SyntaxException("Expected JSON or XML after FOR", Current.Line, Current.Column);

        ForMode mode = ForMode.PATH;
        if (Match(TokenType.PATH)) mode = ForMode.PATH;
        else if (Match(TokenType.AUTO)) mode = ForMode.AUTO;
        else if (Match(TokenType.RAW)) mode = ForMode.RAW;
        else if (Match(TokenType.EXPLICIT)) mode = ForMode.EXPLICIT;

        string? rootName = null;
        bool includeNulls = false;
        bool withoutWrapper = false;
        bool useElements = false;

        while (Match(TokenType.COMMA) ||
               Current.Type == TokenType.ROOT ||
               Current.Type == TokenType.INCLUDE_NULL_VALUES ||
               Current.Type == TokenType.WITHOUT_ARRAY_WRAPPER ||
               Current.Type == TokenType.ELEMENTS)
        {
            if (Match(TokenType.ROOT))
            {
                if (Match(TokenType.LPAREN))
                {
                    rootName = Consume(TokenType.STRING_LITERAL, "Expected root name string").Value;
                    Consume(TokenType.RPAREN, "Expected ')' after root name");
                }
                else
                {
                    // Some dialects allow ROOT without parens or with a string immediately
                    if (Current.Type == TokenType.STRING_LITERAL) rootName = Advance().Value;
                    else rootName = "root";
                }
            }
            else if (Match(TokenType.INCLUDE_NULL_VALUES))
            {
                includeNulls = true;
            }
            else if (Match(TokenType.WITHOUT_ARRAY_WRAPPER))
            {
                withoutWrapper = true;
            }
            else if (Match(TokenType.ELEMENTS))
            {
                useElements = true;
            }
            else if (Previous.Type != TokenType.COMMA)
            {
                break;
            }
        }

        return new ForClause(type, mode, rootName)
        {
            IncludeNullValues = includeNulls,
            WithoutArrayWrapper = withoutWrapper,
            UseElements = useElements
        };
    }

    /// <summary>True when the current token is the word <c>MINUS</c> (Oracle/DuckDB alias for EXCEPT).</summary>
    private bool IsMinusKeyword() => Current.Value.Equals("MINUS", StringComparison.OrdinalIgnoreCase)
        && (Current.Type == TokenType.IDENTIFIER || Current.Type == TokenType.MINUS);

    private Statement ParseSetOperation(Statement left)
    {
        SetOpType op = SetOpType.UNION;
        bool byName = false;
        if (Match(TokenType.UNION))
        {
            op = Match(TokenType.ALL) ? SetOpType.UNION_ALL : SetOpType.UNION;
            if (Current.Type == TokenType.BY) { Advance(); ConsumeWord("NAME", "Expected 'NAME' after 'BY'"); byName = true; }
        }
        else if (Match(TokenType.EXCEPT)) op = SetOpType.EXCEPT;
        else if (Match(TokenType.INTERSECT)) op = SetOpType.INTERSECT;
        else if (IsMinusKeyword()) { Advance(); op = SetOpType.EXCEPT; }

        Consume(TokenType.SELECT, "Expected 'SELECT' after UNION/EXCEPT/INTERSECT/MINUS");
        var right = ParseSelectBody(); // Parse only the body
        var setStmt = new SetOperationStatement(left, op, right) { ByName = byName };

        // Recursively check for more set operations
        if (Current.Type == TokenType.UNION || Current.Type == TokenType.EXCEPT || Current.Type == TokenType.INTERSECT || IsMinusKeyword())
        {
            return ParseSetOperation(setStmt);
        }

        return setStmt;
    }

    public string ParseType()
    {
        var startToken = Current;
        string typeName = ConsumeIdentifier("Expected type name").Value;
        if (Match(TokenType.LPAREN))
        {
            if (Current.Type == TokenType.NUMBER)
            {
                typeName += "(" + Consume(TokenType.NUMBER, "Expected length").Value;
                if (Match(TokenType.COMMA))
                {
                    typeName += "," + Consume(TokenType.NUMBER, "Expected scale").Value;
                }
                Consume(TokenType.RPAREN, "Expected ')'");
                typeName += ")";
            }
            else if (IsIdentifier(Current) || Current.Type == TokenType.MAX)
            {
                if (Current.Type == TokenType.MAX)
                {
                    typeName += "(MAX)";
                    Advance();
                }
                else
                {
                    // Recurse to handle nested parameterized types: LIST(VARCHAR(200)), LIST(DECIMAL(10,2))
                    string innerType = ParseType();
                    typeName += "(" + innerType + ")";
                }
                Consume(TokenType.RPAREN, "Expected ')' after type parameter");
            }
            else
            {
                Consume(TokenType.RPAREN, "Expected ')' for empty type parameters");
                typeName += "()";
            }
        }
        return typeName;
    }

    public TableReference ParseTableReference(bool allowFunction = true, bool allowWithClause = true, bool allowAlias = true)
    {
        var t = Current;

        TableReference tableRef;

        // Subquery Support: (SELECT ...) [AS] Alias
        if (Match(TokenType.LPAREN))
        {
            if (Current.Type == TokenType.SELECT)
            {
                var subquery = ParseQuery();
                Consume(TokenType.RPAREN, "Expected ')' after subquery in FROM/JOIN");

                string alias = "";
                if (Match(TokenType.AS))
                {
                    alias = ConsumeIdentifier("Expected alias after AS for subquery").Value;
                }
                else if (Current.Type == TokenType.IDENTIFIER)
                {
                    alias = Advance().Value;
                }
                else
                {
                    alias = "Sub_" + new Random().Next(1000, 9999);
                }

                tableRef = new TableReference("SUBQUERY", null, null, null, alias, subquery);
            }
            else if (Current.Type == TokenType.VALUES)
            {
                Advance();
                var rows = new List<List<Expression>>();
                do
                {
                    Consume(TokenType.LPAREN, "Expected '(' before VALUES row");
                    var values = new List<Expression>();
                    values.Add(ParseExpression());
                    while (Match(TokenType.COMMA))
                    {
                        values.Add(ParseExpression());
                    }
                    Consume(TokenType.RPAREN, "Expected ')' after VALUES row");
                    rows.Add(values);
                } while (Match(TokenType.COMMA));

                Consume(TokenType.RPAREN, "Expected ')' after VALUES table constructor");

                string alias;
                if (Match(TokenType.AS))
                {
                    alias = ConsumeIdentifier("Expected alias after AS for VALUES table constructor").Value;
                }
                else if (IsIdentifier(Current) && !Current.Value.Equals("MATCH_RECOGNIZE", StringComparison.OrdinalIgnoreCase))
                {
                    alias = Advance().Value;
                }
                else
                {
                    throw new SyntaxException("Expected alias after VALUES table constructor", Current.Line, Current.Column);
                }

                List<string>? columnAliases = null;
                if (Match(TokenType.LPAREN))
                {
                    columnAliases = new List<string>();
                    columnAliases.Add(ConsumeIdentifier("Expected column alias in VALUES table constructor").Value);
                    while (Match(TokenType.COMMA))
                    {
                        columnAliases.Add(ConsumeIdentifier("Expected column alias in VALUES table constructor").Value);
                    }
                    Consume(TokenType.RPAREN, "Expected ')' after VALUES column aliases");
                }

                tableRef = new TableReference("VALUES", null, null, null, alias, valuesRows: rows, columnAliases: columnAliases);
            }
            else
            {
                throw new SyntaxException("Expected SELECT or VALUES after '(' in FROM/JOIN table reference", Current.Line, Current.Column);
            }
        }
        else
        {
            var parts = new List<string>();

            if (Current.Type == TokenType.VARIABLE)
            {
                parts.Add(Consume(TokenType.VARIABLE, "Expected variable table reference").Value);
            }
            else if (Current.Type == TokenType.STRING_LITERAL)
            {
                parts.Add(Consume(TokenType.STRING_LITERAL, "Expected string for table reference").Value);
            }
            else
            {
                parts.Add(ConsumeIdentifier("Expected identifier for table reference").Value);
            }

            while (Match(TokenType.DOT))
            {
                // After a dot in a table reference (schema.table, conn.table), any word token is valid —
                // keywords like TABLE, INDEX, etc. are commonly used as table names.
                if (Current.Type < TokenType.STAR)
                    parts.Add(Advance().Value);
                else
                    break;
            }

            // Capture table-level metadata if available before alias
            var tableMetadata = _expressionParser.ParseMetadataTags();

            if (allowFunction && Match(TokenType.LPAREN))
            {
                var funcName = parts[^1];
                var funcCall = funcName.Equals("JSON_TABLE", StringComparison.OrdinalIgnoreCase)
                    ? ParseJsonTableFunctionCall(funcName, t)
                    : ParseTableFunctionCall(funcName, t);

                if (parts.Count == 4) tableRef = new TableReference(parts[3], parts[2], parts[1], parts[0], functionCall: funcCall);
                else if (parts.Count == 3) tableRef = new TableReference(parts[2], parts[1], null, parts[0], functionCall: funcCall);
                else if (parts.Count == 2) tableRef = new TableReference(parts[1], null, null, parts[0], functionCall: funcCall);
                else tableRef = new TableReference(funcName, functionCall: funcCall);
            }
            else if (parts.Count == 4)
            {
                tableRef = new TableReference(parts[3], parts[2], parts[1], parts[0]);
            }
            else if (parts.Count == 3)
            {
                tableRef = new TableReference(parts[2], parts[1], null, parts[0]);
            }
            else if (parts.Count == 2)
            {
                tableRef = new TableReference(parts[1], null, null, parts[0]);
            }
            else
            {
                tableRef = new TableReference(parts[0]);
            }

            // Optional Alias
            if (allowAlias)
            {
                if (Match(TokenType.AS))
                {
                    tableRef = new TableReference(tableRef.TableName, tableRef.SchemaName, tableRef.DatabaseName, tableRef.ConnectionName, ConsumeIdentifier("Expected alias after AS").Value, tableRef.Subquery, tableRef.FunctionCall, tableRef.ValuesRows, tableRef.ColumnAliases);
                    // Tags after alias: mytable AS c /* @tag: val */
                    var aliasMetadata = _expressionParser.ParseMetadataTags();
                    foreach (var tag in aliasMetadata) tableRef.Metadata[tag.Key] = tag.Value;
                }
                else if (IsIdentifier(Current) && !Current.Value.Equals("MATCH_RECOGNIZE", StringComparison.OrdinalIgnoreCase))
                {
                    // Implicit alias
                    tableRef = new TableReference(tableRef.TableName, tableRef.SchemaName, tableRef.DatabaseName, tableRef.ConnectionName, Advance().Value, tableRef.Subquery, tableRef.FunctionCall, tableRef.ValuesRows, tableRef.ColumnAliases);
                    var aliasMetadata = _expressionParser.ParseMetadataTags();
                    foreach (var tag in aliasMetadata) tableRef.Metadata[tag.Key] = tag.Value;
                }
            }

            // Merge table-level metadata
            if (tableMetadata != null)
            {
                foreach (var tag in tableMetadata) tableRef.Metadata[tag.Key] = tag.Value;
            }
        }

        tableRef = tableRef with
        {
            Line = t.Line,
            Column = t.Column,
            EndLine = LastTokenEndLine,
            EndColumn = LastTokenEndColumn
        };

        // Handle table-level operators
        while (Current.Type == TokenType.PIVOT || Current.Type == TokenType.UNPIVOT || Current.Value.Equals("MATCH_RECOGNIZE", StringComparison.OrdinalIgnoreCase))
        {
            if (Match(TokenType.PIVOT)) tableRef.TableOperators.Add(ParsePivotClause());
            else if (Match(TokenType.UNPIVOT)) tableRef.TableOperators.Add(ParseUnpivotClause());
            else if (MatchWord("MATCH_RECOGNIZE")) tableRef.TableOperators.Add(ParseMatchRecognizeClause());
            tableRef = tableRef with
            {
                EndLine = LastTokenEndLine,
                EndColumn = LastTokenEndColumn
            };
        }

        // Handle WITH (...) options
        if (allowWithClause && Match(TokenType.WITH))
        {
            Consume(TokenType.LPAREN, "Expected '(' after WITH");
            while (Current.Type != TokenType.RPAREN && Current.Type != TokenType.EOF)
            {
                // Permissive key: allow identifiers or any keyword (needed for MOCKDB(COLUMNS=...))
                string key;
                if (IsIdentifier(Current) || LanguageMetadata.IsKeyword(Current.Value))
                    key = Advance().Value;
                else
                    throw new SyntaxException("Expected option key", Current.Line, Current.Column);
                Match(TokenType.EQUALS);
                var val = _expressionParser.ParseExpression();
                tableRef.Options[key] = val;
                if (!Match(TokenType.COMMA)) break;
            }
            Consume(TokenType.RPAREN, "Expected ')' to close WITH options");
            tableRef = tableRef with
            {
                EndLine = LastTokenEndLine,
                EndColumn = LastTokenEndColumn
            };
        }

        // Handle SQLite INDEXED BY / NOT INDEXED clauses and ignore them
        if (Current.Type == TokenType.NOT && Peek.Type == TokenType.IDENTIFIER && Peek.Value.Equals("INDEXED", StringComparison.OrdinalIgnoreCase))
        {
            Advance(); // consume NOT
            Advance(); // consume INDEXED
            tableRef = tableRef with
            {
                EndLine = LastTokenEndLine,
                EndColumn = LastTokenEndColumn
            };
        }
        else if (Current.Type == TokenType.IDENTIFIER && Current.Value.Equals("INDEXED", StringComparison.OrdinalIgnoreCase) && Peek.Type == TokenType.BY)
        {
            Advance(); // consume INDEXED
            Advance(); // consume BY
            // Parse the index name
            if (IsIdentifier(Current) || LanguageMetadata.IsKeyword(Current.Value) || Current.Type == TokenType.STRING_LITERAL)
            {
                Advance(); // consume index name
            }
            tableRef = tableRef with
            {
                EndLine = LastTokenEndLine,
                EndColumn = LastTokenEndColumn
            };
        }

        return tableRef;
    }

    private FunctionCallExpression ParseTableFunctionCall(string funcName, Token startToken)
    {
        var args = new List<Expression>();
        if (!Match(TokenType.RPAREN))
        {
            args.Add(_expressionParser.ParseExpression());
            while (Match(TokenType.COMMA))
            {
                args.Add(_expressionParser.ParseExpression());
            }
            Consume(TokenType.RPAREN, "Expected ')' after function arguments");
        }
        return new FunctionCallExpression(funcName, args) { Line = startToken.Line, Column = startToken.Column };
    }

    private FunctionCallExpression ParseJsonTableFunctionCall(string funcName, Token startToken)
    {
        var args = new List<Expression>();
        JsonTableSpec? spec = null;

        if (!Match(TokenType.RPAREN))
        {
            args.Add(_expressionParser.ParseExpression());
            if (Match(TokenType.COMMA))
            {
                args.Add(_expressionParser.ParseExpression());
            }

            if (Match(TokenType.COLUMNS))
            {
                Consume(TokenType.LPAREN, "Expected '(' after JSON_TABLE COLUMNS");
                var columns = new List<JsonTableColumnSpec>();
                if (Current.Type != TokenType.RPAREN)
                {
                    columns.Add(ParseJsonTableColumnSpec());
                    while (Match(TokenType.COMMA))
                    {
                        columns.Add(ParseJsonTableColumnSpec());
                    }
                }
                Consume(TokenType.RPAREN, "Expected ')' after JSON_TABLE COLUMNS list");
                spec = new JsonTableSpec(columns);
            }
            else
            {
                while (Match(TokenType.COMMA))
                {
                    args.Add(_expressionParser.ParseExpression());
                }
            }

            Consume(TokenType.RPAREN, "Expected ')' after JSON_TABLE arguments");
        }

        return new FunctionCallExpression(funcName, args) { Line = startToken.Line, Column = startToken.Column, JsonTable = spec };
    }

    private JsonTableColumnSpec ParseJsonTableColumnSpec()
    {
        string name = ConsumeIdentifier("Expected JSON_TABLE column name").Value;

        if (Match(TokenType.FOR))
        {
            ConsumeWord("ORDINALITY", "Expected ORDINALITY after FOR in JSON_TABLE column");
            return new JsonTableColumnSpec(name, "INT", null, ForOrdinality: true);
        }

        string? typeName = null;
        if (Current.Type != TokenType.PATH && Current.Type != TokenType.EXISTS)
        {
            typeName = ConsumeIdentifier("Expected JSON_TABLE column type").Value;
            if (Match(TokenType.LPAREN))
            {
                var parts = new List<string>();
                if (Current.Type != TokenType.RPAREN)
                {
                    parts.Add(Advance().Value);
                    while (Match(TokenType.COMMA)) parts.Add(Advance().Value);
                }
                Consume(TokenType.RPAREN, "Expected ')' after JSON_TABLE type arguments");
                typeName += $"({string.Join(", ", parts)})";
            }
        }

        bool exists = Match(TokenType.EXISTS);
        Consume(TokenType.PATH, "Expected PATH in JSON_TABLE column");
        var path = _expressionParser.ParseExpression();

        Expression? defaultOnEmpty = null;
        Expression? defaultOnError = null;
        while (Match(TokenType.DEFAULT))
        {
            var defaultValue = _expressionParser.ParseExpression();
            Consume(TokenType.ON, "Expected ON after JSON_TABLE DEFAULT value");
            if (MatchWord("EMPTY")) defaultOnEmpty = defaultValue;
            else if (MatchWord("ERROR")) defaultOnError = defaultValue;
            else throw new SyntaxException("Expected EMPTY or ERROR after JSON_TABLE DEFAULT ... ON", Current.Line, Current.Column);
        }

        return new JsonTableColumnSpec(name, typeName, path, Exists: exists, DefaultOnEmpty: defaultOnEmpty, DefaultOnError: defaultOnError);
    }

    private bool MatchWord(string word)
    {
        if (Current.Value.Equals(word, StringComparison.OrdinalIgnoreCase))
        {
            Advance();
            return true;
        }
        return false;
    }

    private void ConsumeWord(string word, string message)
    {
        if (!MatchWord(word)) throw new SyntaxException(message, Current.Line, Current.Column);
    }

    private PivotClause ParsePivotClause()
    {
        Consume(TokenType.LPAREN, "Expected '(' after PIVOT");
        var aggFunc = ConsumeIdentifier("Expected aggregate function name").Value;
        Consume(TokenType.LPAREN, "Expected '(' after aggregate function");

        // Support both COUNT(*) and COUNT(Col)
        string aggCol = "";
        if (Match(TokenType.STAR)) aggCol = "*";
        else aggCol = ConsumeIdentifier("Expected aggregate column name").Value;

        Consume(TokenType.RPAREN, "Expected ')' after aggregate column");
        Consume(TokenType.FOR, "Expected 'FOR' in PIVOT clause");
        var pivotCol = ConsumeIdentifier("Expected pivot column name").Value;
        Consume(TokenType.IN, "Expected 'IN' in PIVOT clause");
        Consume(TokenType.LPAREN, "Expected '(' before pivot values");
        var values = new List<Expression>();
        values.Add(ParseExpression());
        while (Match(TokenType.COMMA))
        {
            values.Add(ParseExpression());
        }
        Consume(TokenType.RPAREN, "Expected ')' after pivot values");
        Consume(TokenType.RPAREN, "Expected ')' to close PIVOT clause");

        string? alias = null;
        if (Match(TokenType.AS)) alias = ConsumeIdentifier("Expected alias after PIVOT").Value;
        else if (Current.Type == TokenType.IDENTIFIER && !IsKeyword(Current.Value)) alias = Advance().Value;

        return new PivotClause(aggFunc, aggCol, pivotCol, values) { Alias = alias };
    }

    private UnpivotClause ParseUnpivotClause()
    {
        Consume(TokenType.LPAREN, "Expected '(' after UNPIVOT");
        var valCol = ConsumeIdentifier("Expected value column name").Value;
        Consume(TokenType.FOR, "Expected 'FOR' in UNPIVOT clause");
        var nameCol = ConsumeIdentifier("Expected name column name").Value;
        Consume(TokenType.IN, "Expected 'IN' in UNPIVOT clause");
        Consume(TokenType.LPAREN, "Expected '(' before unpivot columns");
        var cols = new List<string>();
        cols.Add(ConsumeIdentifier("Expected column name").Value);
        while (Match(TokenType.COMMA))
        {
            cols.Add(ConsumeIdentifier("Expected column name").Value);
        }
        Consume(TokenType.RPAREN, "Expected ')' after unpivot columns");
        Consume(TokenType.RPAREN, "Expected ')' to close UNPIVOT clause");

        string? alias = null;
        if (Match(TokenType.AS)) alias = ConsumeIdentifier("Expected alias after UNPIVOT").Value;
        else if (Current.Type == TokenType.IDENTIFIER && !IsKeyword(Current.Value)) alias = Advance().Value;

        return new UnpivotClause(valCol, nameCol, cols) { Alias = alias };
    }

    private SelectStatement BuildSelectStar(TableReference source, Token start)
    {
        var columns = new List<SelectColumn> { new SelectColumn(new IdentifierExpression("*"), null, null) };
        return new SelectStatement(columns, null, source, new List<JoinClause>(), null)
        {
            Line = start.Line,
            Column = start.Column,
            EndLine = LastTokenEndLine,
            EndColumn = LastTokenEndColumn
        };
    }

    private PivotAggregate ParsePivotAggregate()
    {
        var func = ConsumeIdentifier("Expected aggregate function name in USING").Value;
        Consume(TokenType.LPAREN, "Expected '(' after aggregate function");
        string? col = Match(TokenType.STAR) ? "*" : ConsumeIdentifier("Expected aggregate column or '*'").Value;
        Consume(TokenType.RPAREN, "Expected ')' after aggregate argument");
        string? alias = null;
        if (Match(TokenType.AS)) alias = ConsumeIdentifier("Expected alias after AS").Value;
        return new PivotAggregate(func, col, alias);
    }

    /// <summary>DuckDB statement form: PIVOT &lt;src&gt; ON &lt;cols&gt; [IN (&lt;vals&gt;)] USING &lt;aggs&gt; [GROUP BY &lt;cols&gt;].</summary>
    public Statement ParseDuckPivotStatement()
    {
        var start = Current;
        Consume(TokenType.PIVOT, "Expected 'PIVOT'");
        var source = ParseTableReference();
        Consume(TokenType.ON, "Expected 'ON' after PIVOT source table");

        var onCols = new List<string> { ConsumeIdentifier("Expected pivot column name after ON").Value };
        while (Match(TokenType.COMMA)) onCols.Add(ConsumeIdentifier("Expected pivot column name").Value);

        List<Expression>? inValues = null;
        if (Match(TokenType.IN))
        {
            if (onCols.Count > 1)
                throw new SyntaxException("PIVOT ... IN (...) is only supported with a single ON column; omit IN for dynamic discovery with multiple ON columns.", start.Line, start.Column);
            Consume(TokenType.LPAREN, "Expected '(' after IN");
            inValues = new List<Expression> { ParseExpression() };
            while (Match(TokenType.COMMA)) inValues.Add(ParseExpression());
            Consume(TokenType.RPAREN, "Expected ')' after pivot values");
        }

        Consume(TokenType.USING, "Expected 'USING' with one or more aggregates in PIVOT");
        var aggregates = new List<PivotAggregate> { ParsePivotAggregate() };
        while (Match(TokenType.COMMA)) aggregates.Add(ParsePivotAggregate());

        List<string>? groupBy = null;
        if (Match(TokenType.GROUP))
        {
            Consume(TokenType.BY, "Expected 'BY' after 'GROUP'");
            groupBy = new List<string> { ConsumeIdentifier("Expected GROUP BY column").Value };
            while (Match(TokenType.COMMA)) groupBy.Add(ConsumeIdentifier("Expected GROUP BY column").Value);
        }

        source.TableOperators.Add(new DuckPivotClause(onCols, inValues, aggregates, groupBy));
        return BuildSelectStar(source, start);
    }

    /// <summary>DuckDB statement form: UNPIVOT &lt;src&gt; ON &lt;cols | COLUMNS(* EXCLUDE (...))&gt; INTO NAME &lt;n&gt; VALUE &lt;v&gt;.</summary>
    public Statement ParseDuckUnpivotStatement()
    {
        var start = Current;
        Consume(TokenType.UNPIVOT, "Expected 'UNPIVOT'");
        var source = ParseTableReference();
        Consume(TokenType.ON, "Expected 'ON' after UNPIVOT source table");

        bool allExcept = false;
        List<string>? excludeCols = null;
        var onCols = new List<string>();
        if (Match(TokenType.COLUMNS))
        {
            Consume(TokenType.LPAREN, "Expected '(' after COLUMNS");
            Consume(TokenType.STAR, "Expected '*' in COLUMNS(*)");
            allExcept = true;
            excludeCols = new List<string>();
            if (Match(TokenType.EXCLUDE))
            {
                Consume(TokenType.LPAREN, "Expected '(' after EXCLUDE");
                excludeCols.Add(ConsumeIdentifier("Expected excluded column name").Value);
                while (Match(TokenType.COMMA)) excludeCols.Add(ConsumeIdentifier("Expected excluded column name").Value);
                Consume(TokenType.RPAREN, "Expected ')' after EXCLUDE list");
            }
            Consume(TokenType.RPAREN, "Expected ')' to close COLUMNS(...)");
        }
        else
        {
            onCols.Add(ConsumeIdentifier("Expected column name after ON").Value);
            while (Match(TokenType.COMMA)) onCols.Add(ConsumeIdentifier("Expected column name").Value);
        }

        Consume(TokenType.INTO, "Expected 'INTO' in UNPIVOT");
        ConsumeWord("NAME", "Expected 'NAME' after INTO");
        var nameCol = ConsumeIdentifier("Expected name column").Value;
        Consume(TokenType.VALUE, "Expected 'VALUE' after the name column");
        var valueCol = ConsumeIdentifier("Expected value column").Value;

        source.TableOperators.Add(new UnpivotClause(valueCol, nameCol, onCols)
        {
            AllColumnsExcept = allExcept,
            ExcludeColumns = excludeCols
        });
        return BuildSelectStar(source, start);
    }

    private MatchRecognizeClause ParseMatchRecognizeClause()
    {
        var clause = new MatchRecognizeClause();
        Consume(TokenType.LPAREN, "Expected '(' after MATCH_RECOGNIZE");

        while (Current.Type != TokenType.RPAREN && Current.Type != TokenType.EOF)
        {
            if (Match(TokenType.PARTITION))
            {
                Consume(TokenType.BY, "Expected BY after PARTITION in MATCH_RECOGNIZE");
                clause.PartitionBy.Add(ParseExpression());
                while (Match(TokenType.COMMA)) clause.PartitionBy.Add(ParseExpression());
            }
            else if (Match(TokenType.ORDER))
            {
                Consume(TokenType.BY, "Expected BY after ORDER in MATCH_RECOGNIZE");
                clause.OrderBy.AddRange(ParseOrderByList());
            }
            else if (MatchWord("MEASURES"))
            {
                clause.Measures.Add(ParseMeasureColumn());
                while (Match(TokenType.COMMA)) clause.Measures.Add(ParseMeasureColumn());
            }
            else if (MatchWord("ONE"))
            {
                Consume(TokenType.ROW, "Expected ROW after ONE in MATCH_RECOGNIZE");
                ConsumeWord("PER", "Expected PER after ONE ROW in MATCH_RECOGNIZE");
                ConsumeWord("MATCH", "Expected MATCH after ONE ROW PER in MATCH_RECOGNIZE");
                clause.AllRowsPerMatch = false;
            }
            else if (Match(TokenType.ALL))
            {
                Consume(TokenType.ROWS, "Expected ROWS after ALL in MATCH_RECOGNIZE");
                ConsumeWord("PER", "Expected PER after ALL ROWS in MATCH_RECOGNIZE");
                ConsumeWord("MATCH", "Expected MATCH after ALL ROWS PER in MATCH_RECOGNIZE");
                clause.AllRowsPerMatch = true;
            }
            else if (MatchWord("AFTER"))
            {
                ConsumeWord("MATCH", "Expected MATCH after AFTER in MATCH_RECOGNIZE");
                ConsumeWord("SKIP", "Expected SKIP after AFTER MATCH in MATCH_RECOGNIZE");
                while (!Current.Value.Equals("PATTERN", StringComparison.OrdinalIgnoreCase) && !Current.Value.Equals("DEFINE", StringComparison.OrdinalIgnoreCase) && Current.Type != TokenType.RPAREN && Current.Type != TokenType.EOF)
                {
                    Advance();
                }
            }
            else if (MatchWord("PATTERN"))
            {
                clause.Pattern = ParsePatternText();
            }
            else if (MatchWord("DEFINE"))
            {
                ParseMatchDefinitions(clause);
            }
            else
            {
                throw new SyntaxException("Unexpected token in MATCH_RECOGNIZE clause", Current.Line, Current.Column);
            }
        }

        Consume(TokenType.RPAREN, "Expected ')' after MATCH_RECOGNIZE");
        if (Match(TokenType.AS)) clause.Alias = ConsumeIdentifier("Expected alias after MATCH_RECOGNIZE AS").Value;
        else if (IsIdentifier(Current)) clause.Alias = Advance().Value;
        return clause;
    }

    private List<OrderByClause> ParseOrderByList()
    {
        var orderBy = new List<OrderByClause>();
        do
        {
            var orderExpr = ParseExpression();
            bool descending = false;
            if (Match(TokenType.DESC)) descending = true;
            else Match(TokenType.ASC);
            orderBy.Add(new OrderByClause(orderExpr, descending));
        }
        while (Match(TokenType.COMMA));
        return orderBy;
    }

    private SelectColumn ParseMeasureColumn()
    {
        var expr = ParseExpression();
        string? alias = null;
        if (Match(TokenType.AS)) alias = ConsumeIdentifier("Expected alias after MATCH_RECOGNIZE measure").Value;
        return new SelectColumn(expr, alias);
    }

    private string ParsePatternText()
    {
        Consume(TokenType.LPAREN, "Expected '(' after PATTERN");
        var parts = new List<string>();
        int depth = 1;
        while (depth > 0 && Current.Type != TokenType.EOF)
        {
            if (Current.Type == TokenType.LPAREN)
            {
                depth++;
                parts.Add(Advance().Value);
            }
            else if (Current.Type == TokenType.RPAREN)
            {
                depth--;
                if (depth > 0) parts.Add(Advance().Value);
                else Advance();
            }
            else
            {
                parts.Add(Advance().Value);
            }
        }
        return string.Join(" ", parts);
    }

    private void ParseMatchDefinitions(MatchRecognizeClause clause)
    {
        do
        {
            string name = ConsumeIdentifier("Expected pattern variable in MATCH_RECOGNIZE DEFINE").Value;
            Consume(TokenType.AS, "Expected AS in MATCH_RECOGNIZE DEFINE");
            clause.Definitions[name] = ParseExpression();
        }
        while (Match(TokenType.COMMA));
    }

    public OutputClause ParseOutputClause()
    {
        var columns = new List<SelectColumn>();
        do
        {
            columns.Add(ParseSelectColumn());
        } while (Match(TokenType.COMMA));

        TableReference? intoTable = null;
        if (Match(TokenType.INTO))
        {
            intoTable = ParseTableReference();
        }

        return new OutputClause(columns, intoTable);
    }

    // Pre-computed static set derived from LanguageMetadata — single source of truth, zero allocation per call.
    // Supplemented with a few parser-specific tokens that must never be treated as implicit aliases.
    private static readonly HashSet<string> _reservedKeywords = BuildReservedKeywords();

    private static HashSet<string> BuildReservedKeywords()
    {
        var set = new HashSet<string>(ETL_SQL.Common.LanguageMetadata.GetAllKeywords(), StringComparer.OrdinalIgnoreCase);
        // Parser-level tokens not in LanguageMetadata (internal SQL keywords used during parsing)
        set.Add("INSERTED"); set.Add("DELETED"); set.Add("ACTION");
        return set;
    }

    private bool IsKeyword(string value) => _reservedKeywords.Contains(value);

    /// <summary>True when the current token closes a comma-separated clause list (used to tolerate trailing commas).</summary>
    private bool AtClauseEnd() => Current.Type is TokenType.FROM or TokenType.INTO or TokenType.WHERE
        or TokenType.GROUP or TokenType.HAVING or TokenType.QUALIFY or TokenType.ORDER
        or TokenType.LIMIT or TokenType.OFFSET or TokenType.FETCH or TokenType.FOR
        or TokenType.UNION or TokenType.EXCEPT or TokenType.INTERSECT
        or TokenType.RPAREN or TokenType.SEMICOLON or TokenType.EOF;

    public SelectColumn ParseSelectColumn()
    {
        Expression expr;
        if (Current.Type == TokenType.STAR)
        {
            var t = Advance();
            expr = ParseStarModifiers(null, t);
        }
        else
        {
            expr = ParseExpression();
        }

        string? alias = null;
        if (Match(TokenType.AS))
        {
            if (IsIdentifier(Current) || Current.Type == TokenType.STRING_LITERAL)
            {
                alias = Advance().Value;
            }
            else
            {
                throw new SyntaxException($"Expected alias after AS", Current.Line, Current.Column);
            }
        }
        else if (IsIdentifier(Current))
        {
            alias = Advance().Value;
        }

        Dictionary<string, string>? metadata = null;
        while (Match(TokenType.COLUMN_TAG))
        {
            if (metadata == null) metadata = new(StringComparer.OrdinalIgnoreCase);
            ParseMetadataTags(Previous.Value, metadata);
        }

        var col = new SelectColumn(expr, alias, metadata)
        {
            Line = expr.Line,
            Column = expr.Column,
            EndLine = LastTokenEndLine,
            EndColumn = LastTokenEndColumn
        };
        return col;
    }

    /// <summary>
    /// Parses optional star modifiers after a consumed <c>*</c>: <c>EXCLUDE (cols)</c>,
    /// <c>REPLACE (expr AS col)</c>, <c>RENAME (col AS new)</c> (in that order, DuckDB/Snowflake style).
    /// Returns a plain <c>*</c> identifier when no modifiers are present.
    /// </summary>
    private Expression ParseStarModifiers(string? qualifier, Token starToken)
    {
        List<string>? exclude = null;
        List<(string, Expression)>? replace = null;
        List<(string, string)>? rename = null;

        if (Current.Type == TokenType.EXCLUDE)
        {
            Advance();
            Consume(TokenType.LPAREN, "Expected '(' after EXCLUDE");
            exclude = new List<string> { ConsumeIdentifier("Expected column name in EXCLUDE").Value };
            while (Match(TokenType.COMMA))
            {
                if (Current.Type == TokenType.RPAREN) break;
                exclude.Add(ConsumeIdentifier("Expected column name in EXCLUDE").Value);
            }
            Consume(TokenType.RPAREN, "Expected ')' after EXCLUDE list");
        }

        if (Current.Type == TokenType.REPLACE)
        {
            Advance();
            Consume(TokenType.LPAREN, "Expected '(' after REPLACE");
            replace = new List<(string, Expression)>();
            do
            {
                if (Current.Type == TokenType.RPAREN) break;
                var rexpr = ParseExpression();
                Consume(TokenType.AS, "Expected 'AS' in REPLACE (expression AS column)");
                var rcol = ConsumeIdentifier("Expected column name after AS in REPLACE").Value;
                replace.Add((rcol, rexpr));
            } while (Match(TokenType.COMMA));
            Consume(TokenType.RPAREN, "Expected ')' after REPLACE list");
        }

        if (Current.Type == TokenType.RENAME)
        {
            Advance();
            Consume(TokenType.LPAREN, "Expected '(' after RENAME");
            rename = new List<(string, string)>();
            do
            {
                if (Current.Type == TokenType.RPAREN) break;
                var from = ConsumeIdentifier("Expected column name in RENAME").Value;
                Consume(TokenType.AS, "Expected 'AS' in RENAME (column AS new_name)");
                var to = ConsumeIdentifier("Expected new name after AS in RENAME").Value;
                rename.Add((from, to));
            } while (Match(TokenType.COMMA));
            Consume(TokenType.RPAREN, "Expected ')' after RENAME list");
        }

        if (exclude == null && replace == null && rename == null)
        {
            var starName = qualifier != null ? $"{qualifier}.*" : "*";
            return new IdentifierExpression(starName) { Line = starToken.Line, Column = starToken.Column };
        }

        return new StarExpression(qualifier, exclude ?? new List<string>(), replace ?? new List<(string, Expression)>(), rename ?? new List<(string, string)>())
        {
            Line = starToken.Line,
            Column = starToken.Column,
            EndLine = LastTokenEndLine,
            EndColumn = LastTokenEndColumn
        };
    }

    public void ParseMetadataTags(string tagContent, Dictionary<string, string> metadata)
    {
        // Expected format: @tag: value; @tag2: value2;
        var parts = tagContent.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (!part.StartsWith("@")) continue;

            var colonIndex = part.IndexOf(':');
            if (colonIndex > 0)
            {
                var tagName = part.Substring(1, colonIndex - 1).Trim();
                var tagValue = part.Substring(colonIndex + 1).Trim();
                metadata[tagName] = tagValue;
            }
            else
            {
                // Handle @tag (boolean existence or just key name)
                metadata[part.Substring(1).Trim()] = "true";
            }
        }
    }

    public Expression ParseExpression() => _expressionParser.ParseExpression();

    public List<JoinClause> ParseJoins()
    {
        var joins = new List<JoinClause>();
        while (Current.Type == TokenType.JOIN || Current.Type == TokenType.INNER || Current.Type == TokenType.LEFT ||
               Current.Type == TokenType.RIGHT || Current.Type == TokenType.OUTER || Current.Type == TokenType.FULL ||
               Current.Type == TokenType.CROSS || Current.Type == TokenType.APPLY || Current.Type == TokenType.SEMI ||
               Current.Type == TokenType.ANTI || Current.Type == TokenType.HASH || Current.Type == TokenType.LOOP || Current.Type == TokenType.MERGE ||
               Current.Type == TokenType.FUZZY || Current.Type == TokenType.ASOF)
        {
            string joinType = "INNER";
            JoinHint hint = JoinHint.None;

            if (Match(TokenType.ASOF))
            {
                // ASOF JOIN / ASOF [LEFT] JOIN — nearest-match temporal/inequality join.
                joinType = "ASOF";
                if (Match(TokenType.LEFT)) { Match(TokenType.OUTER); joinType = "ASOF LEFT"; }
                else Match(TokenType.INNER);
                Consume(TokenType.JOIN, "Expected 'JOIN' after ASOF");
            }
            else if (Match(TokenType.FUZZY))
            {
                joinType = "FUZZY";
                Consume(TokenType.JOIN, "Expected 'JOIN' after FUZZY");
            }
            else if (Match(TokenType.INNER))
            {
                joinType = "INNER";
                if (Match(TokenType.HASH)) hint = JoinHint.Hash;
                else if (Match(TokenType.LOOP)) hint = JoinHint.Loop;
                else if (Match(TokenType.MERGE)) hint = JoinHint.Merge;
                Consume(TokenType.JOIN, "Expected 'JOIN' after INNER [hint]");
            }
            else if (Match(TokenType.LEFT))
            {
                joinType = "LEFT"; Match(TokenType.OUTER);
                if (Match(TokenType.SEMI)) { joinType = "LEFT SEMI"; Consume(TokenType.JOIN, "Expected 'JOIN' after LEFT SEMI"); }
                else if (Match(TokenType.ANTI)) { joinType = "LEFT ANTI"; Consume(TokenType.JOIN, "Expected 'JOIN' after LEFT ANTI"); }
                else if (Match(TokenType.FUZZY)) { joinType = "LEFT FUZZY"; Consume(TokenType.JOIN, "Expected 'JOIN' after LEFT FUZZY"); }
                else
                {
                    if (Match(TokenType.HASH)) hint = JoinHint.Hash;
                    else if (Match(TokenType.LOOP)) hint = JoinHint.Loop;
                    else if (Match(TokenType.MERGE)) hint = JoinHint.Merge;
                    Consume(TokenType.JOIN, "Expected 'JOIN'");
                }
            }
            else if (Match(TokenType.RIGHT))
            {
                joinType = "RIGHT"; Match(TokenType.OUTER);
                if (Match(TokenType.HASH)) hint = JoinHint.Hash;
                else if (Match(TokenType.LOOP)) hint = JoinHint.Loop;
                else if (Match(TokenType.MERGE)) hint = JoinHint.Merge;
                Consume(TokenType.JOIN, "Expected 'JOIN'");
            }
            else if (Match(TokenType.FULL))
            {
                joinType = "FULL"; Match(TokenType.OUTER);
                if (Match(TokenType.HASH)) hint = JoinHint.Hash;
                else if (Match(TokenType.LOOP)) hint = JoinHint.Loop;
                else if (Match(TokenType.MERGE)) hint = JoinHint.Merge;
                Consume(TokenType.JOIN, "Expected 'JOIN'");
            }
            else if (Match(TokenType.HASH))
            {
                hint = JoinHint.Hash;
                Consume(TokenType.JOIN, "Expected 'JOIN' after HASH");
            }
            else if (Match(TokenType.LOOP))
            {
                hint = JoinHint.Loop;
                Consume(TokenType.JOIN, "Expected 'JOIN' after LOOP");
            }
            else if (Match(TokenType.MERGE))
            {
                hint = JoinHint.Merge;
                Consume(TokenType.JOIN, "Expected 'JOIN' after MERGE");
            }
            else if (Match(TokenType.CROSS))
            {
                if (Match(TokenType.APPLY)) joinType = "CROSS APPLY";
                else if (Match(TokenType.JOIN)) joinType = "CROSS JOIN";
                else throw new SyntaxException("Expected JOIN or APPLY after CROSS", Current.Line, Current.Column);
            }
            else if (Match(TokenType.OUTER))
            {
                if (Match(TokenType.APPLY)) joinType = "OUTER APPLY";
                else { joinType = "LEFT"; Consume(TokenType.JOIN, "Expected 'JOIN' after 'OUTER'"); }
            }
            else if (Match(TokenType.SEMI))
            {
                joinType = "SEMI";
                Consume(TokenType.JOIN, "Expected 'JOIN' after 'SEMI'");
            }
            else if (Match(TokenType.ANTI))
            {
                joinType = "ANTI";
                Consume(TokenType.JOIN, "Expected 'JOIN' after 'ANTI'");
            }
            else if (Match(TokenType.APPLY)) { joinType = "CROSS APPLY"; }
            else if (Match(TokenType.JOIN)) { joinType = "INNER"; }

            // LATERAL is the ANSI/DuckDB spelling of APPLY: `JOIN LATERAL`/`CROSS JOIN LATERAL`
            // behave as CROSS APPLY, `LEFT JOIN LATERAL` as OUTER APPLY. The correlated right side
            // is parsed identically; an explicit `ON <predicate>` (if present) is carried through.
            if (Match(TokenType.LATERAL))
            {
                joinType = joinType switch
                {
                    "LEFT" => "OUTER APPLY",
                    "INNER" or "CROSS JOIN" => "CROSS APPLY",
                    _ => throw new SyntaxException($"LATERAL is only supported with [INNER] JOIN, LEFT JOIN, or CROSS JOIN (got '{joinType}').", Current.Line, Current.Column)
                };
            }

            var joinTable = ParseTableReference();
            Expression? onCondition = null;
            int? keepBest = null;
            if (!joinType.Contains("APPLY") && joinType != "CROSS JOIN")
            {
                Consume(TokenType.ON, $"Expected 'ON' after {joinType} JOIN table");
                onCondition = ParseExpression();
            }
            else if (joinType.Contains("APPLY") && Match(TokenType.ON))
            {
                // LATERAL (or APPLY) with an explicit join predicate, e.g. `LEFT JOIN LATERAL (...) ON true`.
                onCondition = ParseExpression();
            }
            else
            {
                // CROSS JOIN, or APPLY without an explicit predicate.
                onCondition = new LiteralExpression(true, TokenType.NUMBER);
            }

            // FUZZY JOIN: optional KEEP BEST <n>
            if (joinType.Contains("FUZZY") && Current.Type == TokenType.KEEP)
            {
                Advance(); // consume KEEP
                // BEST is an identifier token (not a reserved word)
                if (Current.Value?.Equals("BEST", StringComparison.OrdinalIgnoreCase) == true) Advance();
                if (int.TryParse(Current.Value, out var kb) && kb > 0) { keepBest = kb; Advance(); }
                else throw new SyntaxException("Expected a positive integer after KEEP BEST", Current.Line, Current.Column);
            }

            joins.Add(new JoinClause(joinType, joinTable, onCondition!, hint, keepBest));
        }
        return joins;
    }

    private void ValidateGotoScoping(Script script)
    {
        var labels = new Dictionary<string, (SectionLabelStatement Label, List<AstNode> Ancestors)>(StringComparer.OrdinalIgnoreCase);
        var gotos = new List<(GotoStatement Goto, List<AstNode> Ancestors)>();

        TraverseAstForScoping(script, new List<AstNode>(), labels, gotos, script);

        // Build map of top-level statements to batch index
        var topLevelStatementToBatch = new Dictionary<Statement, int>();
        int currentBatch = 0;
        foreach (var stmt in script.Statements)
        {
            if (stmt is GoStatement)
            {
                currentBatch++;
            }
            else
            {
                topLevelStatementToBatch[stmt] = currentBatch;
            }
        }

        foreach (var item in gotos)
        {
            var gotoStmt = item.Goto;
            var gotoAncestors = item.Ancestors;

            if (!labels.TryGetValue(gotoStmt.LabelName, out var target))
            {
                script.Diagnostics.Add(new Diagnostic($"Label '{gotoStmt.LabelName}' is not defined in this script.", gotoStmt.Line, gotoStmt.Column, DiagnosticSeverity.Error, "SEMANTIC"));
                continue;
            }

            var labelStmt = target.Label;
            var labelAncestors = target.Ancestors;

            // Ensure they are in the same batch
            Statement GetTopLevelStatement(Statement s, List<AstNode> ancestors)
            {
                if (ancestors.Count <= 1) return s;
                return (Statement)ancestors[1];
            }

            var gotoTop = GetTopLevelStatement(gotoStmt, gotoAncestors);
            var labelTop = GetTopLevelStatement(labelStmt, labelAncestors);
            if (topLevelStatementToBatch.TryGetValue(gotoTop, out var gotoBatch) &&
                topLevelStatementToBatch.TryGetValue(labelTop, out var labelBatch) &&
                gotoBatch != labelBatch)
            {
                script.Diagnostics.Add(new Diagnostic($"GOTO cannot jump across GO batch boundaries to label '{gotoStmt.LabelName}'.", gotoStmt.Line, gotoStmt.Column, DiagnosticSeverity.Error, "SEMANTIC"));
                continue;
            }

            // Find lowest common ancestor
            int commonCount = 0;
            int minLen = Math.Min(gotoAncestors.Count, labelAncestors.Count);
            while (commonCount < minLen && gotoAncestors[commonCount] == labelAncestors[commonCount])
            {
                commonCount++;
            }

            // Check nodes in the label's path after the common prefix
            for (int i = commonCount; i < labelAncestors.Count; i++)
            {
                var parent = labelAncestors[i - 1];
                if (parent is WhileStatement || parent is ForStatement || parent is ForeachStatement || parent is IfStatement || parent is TryCatchStatement || parent is ParallelStatement)
                {
                    string typeStr = parent.GetType().Name.Replace("Statement", "");
                    script.Diagnostics.Add(new Diagnostic($"GOTO cannot jump into nested {typeStr} block to label '{gotoStmt.LabelName}'.", gotoStmt.Line, gotoStmt.Column, DiagnosticSeverity.Error, "SEMANTIC"));
                    break;
                }
            }
        }
    }

    private void TraverseAstForScoping(AstNode node, List<AstNode> ancestors, Dictionary<string, (SectionLabelStatement Label, List<AstNode> Ancestors)> labels, List<(GotoStatement Goto, List<AstNode> Ancestors)> gotos, Script script)
    {
        if (node == null) return;

        var currentAncestors = new List<AstNode>(ancestors);

        if (node is SectionLabelStatement label)
        {
            if (labels.ContainsKey(label.LabelName))
            {
                script.Diagnostics.Add(new Diagnostic($"Duplicate label '{label.LabelName}' defined in script.", label.Line, label.Column, DiagnosticSeverity.Error, "SEMANTIC"));
            }
            else
            {
                label.IsTopLevel = !currentAncestors.Any(a => a is WhileStatement || a is ForStatement || a is ForeachStatement || a is IfStatement || a is TryCatchStatement || a is ParallelStatement);
                labels[label.LabelName] = (label, currentAncestors);
            }
        }
        else if (node is GotoStatement @goto)
        {
            gotos.Add((@goto, currentAncestors));
        }

        currentAncestors.Add(node);

        if (node is Script s)
        {
            foreach (var stmt in s.Statements) TraverseAstForScoping(stmt, currentAncestors, labels, gotos, script);
        }
        else if (node is BlockStatement block)
        {
            foreach (var stmt in block.Statements) TraverseAstForScoping(stmt, currentAncestors, labels, gotos, script);
        }
        else if (node is WhileStatement @while)
        {
            TraverseAstForScoping(@while.Condition, currentAncestors, labels, gotos, script);
            TraverseAstForScoping(@while.Body, currentAncestors, labels, gotos, script);
        }
        else if (node is ForStatement @for)
        {
            TraverseAstForScoping(@for.StartValue, currentAncestors, labels, gotos, script);
            TraverseAstForScoping(@for.EndValue, currentAncestors, labels, gotos, script);
            if (@for.StepValue != null) TraverseAstForScoping(@for.StepValue, currentAncestors, labels, gotos, script);
            TraverseAstForScoping(@for.Body, currentAncestors, labels, gotos, script);
        }
        else if (node is ForeachStatement @foreach)
        {
            TraverseAstForScoping(@foreach.ListExpression, currentAncestors, labels, gotos, script);
            TraverseAstForScoping(@foreach.Body, currentAncestors, labels, gotos, script);
        }
        else if (node is IfStatement @if)
        {
            TraverseAstForScoping(@if.Condition, currentAncestors, labels, gotos, script);
            TraverseAstForScoping(@if.IfBody, currentAncestors, labels, gotos, script);
            if (@if.ElseIfClauses != null)
            {
                foreach (var elseif in @if.ElseIfClauses) TraverseAstForScoping(elseif.Body, currentAncestors, labels, gotos, script);
            }
            if (@if.ElseBody != null) TraverseAstForScoping(@if.ElseBody, currentAncestors, labels, gotos, script);
        }
        else if (node is TryCatchStatement tc)
        {
            TraverseAstForScoping(tc.TryBody, currentAncestors, labels, gotos, script);
            TraverseAstForScoping(tc.CatchBody, currentAncestors, labels, gotos, script);
        }
        else if (node is ParallelStatement p)
        {
            TraverseAstForScoping(p.Body, currentAncestors, labels, gotos, script);
        }
    }
}

