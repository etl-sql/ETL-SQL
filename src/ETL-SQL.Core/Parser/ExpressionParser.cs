using System;
using System.Collections.Generic;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Core.Parser;
/// <summary>
/// Recursive descent parser for expressions in the ETL-SQL language.
/// Supports standard SQL operations, window functions, and custom ETL extensions.
/// </summary>
public partial class ExpressionParser
{
    private const int MaxExpressionDepth = 100;
    private readonly IParser _parser;
    private int _expressionDepth;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExpressionParser"/> class.
    /// </summary>
    /// <param name="parser">The parent parser instance for token access.</param>
    public ExpressionParser(IParser parser)
    {
        _parser = parser;
    }

    /// <summary>
    /// Consumes sequential COLUMN_TAG tokens and parses them into a metadata dictionary.
    /// </summary>
    public Dictionary<string, string> ParseMetadataTags()
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (_parser.Match(TokenType.COLUMN_TAG))
        {
            _parser.ParseMetadataTags(_parser.Previous.Value, metadata);
        }
        return metadata;
    }

    /// <summary>
    /// Parses an expression starting from the lowest precedence operator (OR).
    /// </summary>
    /// <returns>The root <see cref="Expression"/> node.</returns>
    public Expression ParseExpression()
    {
        _expressionDepth++;
        try
        {
            if (_expressionDepth > MaxExpressionDepth)
            {
                throw new SyntaxException(
                    $"Expression nesting exceeds the maximum supported depth of {MaxExpressionDepth}.",
                    _parser.Current.Line,
                    _parser.Current.Column);
            }

            return ParseArrow();
        }
        finally
        {
            _expressionDepth--;
        }
    }

    /// <summary>
    /// The => arrow conditional: <c>cond => a : b</c> lowers at parse time to
    /// <c>CASE WHEN cond THEN a ELSE b END</c>, and chains flatten into one CASE —
    /// <c>c1 => v1 : c2 => v2 : v3</c> becomes <c>CASE WHEN c1 THEN v1 WHEN c2 THEN v2 ELSE v3 END</c>.
    /// Like ?? → COALESCE and IIF → CASE, the evaluator, lineage, and pushdown all see the canonical
    /// CASE node (short-circuit, universal SQL). Lowest precedence (below OR), so
    /// <c>a OR b => x : y</c> means <c>(a OR b) => x : y</c>. The final else branch is REQUIRED:
    /// a dangling <c>expr => val</c> is a syntax error, never an implicit NULL.
    /// </summary>
    private Expression ParseArrow()
    {
        var operand = ParseOr();
        if (_parser.Current.Type != TokenType.ARROW) return operand;

        var arrowToken = _parser.Current;
        var whenClauses = new List<(Expression Condition, Expression Result)>();
        while (_parser.Match(TokenType.ARROW))
        {
            var result = ParseOr();
            whenClauses.Add((operand, result));
            _parser.Consume(TokenType.COLON,
                "Expected ':' after '=>' result — the arrow conditional requires an else branch (cond => value : else)");
            operand = ParseOr();
            // Loop: if another '=>' follows, that operand was the next WHEN condition;
            // otherwise it is the final ELSE.
        }
        return new CaseExpression(whenClauses, operand)
        { Line = arrowToken.Line, Column = arrowToken.Column, EndLine = _parser.LastTokenEndLine, EndColumn = _parser.LastTokenEndColumn };
    }

    private Expression ParseOr()
    {
        var left = ParseAnd();
        while (_parser.Match(TokenType.OR))
        {
            var op = TokenType.OR;
            var right = ParseAnd();
            left = new BinaryExpression(left, op, right) { Line = left.Line, Column = left.Column, EndLine = _parser.LastTokenEndLine, EndColumn = _parser.LastTokenEndColumn };
        }
        return left;
    }

    private Expression ParseAnd()
    {
        var left = ParseNot();
        while (_parser.Match(TokenType.AND))
        {
            var op = TokenType.AND;
            var right = ParseNot();
            left = new BinaryExpression(left, op, right) { Line = left.Line, Column = left.Column, EndLine = _parser.LastTokenEndLine, EndColumn = _parser.LastTokenEndColumn };
        }
        return left;
    }

    private Expression ParseNot()
    {
        if (_parser.Match(TokenType.NOT))
        {
            var notToken = _parser.Previous;
            if (_parser.Match(TokenType.EXISTS))
            {
                _parser.Consume(TokenType.LPAREN, "Expected '(' after EXISTS");
                var subq = _parser.ParseQuery();
                _parser.Consume(TokenType.RPAREN, "Expected ')' after subquery");
                return new ExistsExpression(subq, true) { Line = notToken.Line, Column = notToken.Column, EndLine = _parser.LastTokenEndLine, EndColumn = _parser.LastTokenEndColumn };
            }
            var expr = ParseNot();
            return new UnaryExpression(TokenType.NOT, expr) { Line = notToken.Line, Column = notToken.Column, EndLine = _parser.LastTokenEndLine, EndColumn = _parser.LastTokenEndColumn };
        }
        return ParseComparison();
    }

    private bool PeekAtTimeZone()
    {
        if (_parser.Current.Type != TokenType.AT) return false;
        return _parser.Peek.Type == TokenType.TIME && _parser.Peek2.Type == TokenType.ZONE;
    }

    private Expression ParseComparison()
    {
        var left = ParseCoalesce();
        while (_parser.Current.Type == TokenType.EQUALS || _parser.Current.Type == TokenType.NOT_EQUALS ||
               _parser.Current.Type == TokenType.LESS_THAN || _parser.Current.Type == TokenType.GREATER_THAN ||
               _parser.Current.Type == TokenType.LESS_EQUALS || _parser.Current.Type == TokenType.GREATER_EQUALS ||
               _parser.Current.Type == TokenType.IN || _parser.Current.Type == TokenType.LIKE || _parser.Current.Type == TokenType.ILIKE ||
               _parser.Current.Type == TokenType.REGEX_MATCH || _parser.Current.Type == TokenType.REGEX_IMATCH ||
               _parser.Current.Type == TokenType.IS || _parser.Current.Type == TokenType.NOT ||
               _parser.Current.Type == TokenType.BETWEEN)
        {
            bool isNot = false;
            if (_parser.Match(TokenType.NOT))
            {
                isNot = true;
                if (_parser.Current.Type == TokenType.NULL)
                {
                    var nullToken = _parser.Advance();
                    left = new IsNullExpression(left, true) { Line = nullToken.Line, Column = nullToken.Column, EndLine = _parser.LastTokenEndLine, EndColumn = _parser.LastTokenEndColumn };
                    continue;
                }
                if (_parser.Current.Type != TokenType.IN && _parser.Current.Type != TokenType.LIKE &&
                    _parser.Current.Type != TokenType.ILIKE && _parser.Current.Type != TokenType.BETWEEN)
                {
                    _parser.Backtrack();
                    break;
                }
            }

            var opToken = _parser.Advance();
            var op = opToken.Type;

            if (op == TokenType.IS)
            {
                bool not = false;
                if (_parser.Match(TokenType.NOT))
                {
                    not = true;
                }
                if (_parser.Match(TokenType.DISTINCT))
                {
                    _parser.Consume(TokenType.FROM, "Expected 'FROM' after IS [NOT] DISTINCT");
                    var rightExpr = ParseShift();
                    left = new IsDistinctFromExpression(left, rightExpr, not) { Line = opToken.Line, Column = opToken.Column, EndLine = _parser.LastTokenEndLine, EndColumn = _parser.LastTokenEndColumn };
                }
                else
                {
                    _parser.Consume(TokenType.NULL, "Expected 'NULL' or 'DISTINCT FROM' after IS [NOT]");
                    left = new IsNullExpression(left, not) { Line = opToken.Line, Column = opToken.Column, EndLine = _parser.LastTokenEndLine, EndColumn = _parser.LastTokenEndColumn };
                }
            }
            else if (op == TokenType.IN)
            {
                Expression rightExpr;
                if (_parser.Match(TokenType.LPAREN))
                {
                    if (_parser.Current.Type == TokenType.SELECT)
                    {
                        var subq = _parser.ParseQuery();
                        rightExpr = new SubqueryExpression(subq);
                    }
                    else if (_parser.Current.Type == TokenType.VARIABLE && _parser.Peek.Type == TokenType.RPAREN)
                    {
                        rightExpr = _parser.ParseExpression();
                    }
                    else
                    {
                        var inList = new List<Expression>();
                        if (_parser.Current.Type != TokenType.RPAREN)
                        {
                            inList.Add(_parser.ParseExpression());
                            while (_parser.Match(TokenType.COMMA))
                            {
                                inList.Add(_parser.ParseExpression());
                            }
                        }
                        rightExpr = new ListExpression(inList);
                    }
                    _parser.Consume(TokenType.RPAREN, "Expected ')' after IN list");
                }
                else
                {
                    rightExpr = ParseTerm();
                }
                left = new InExpression(left, rightExpr, isNot) { Line = opToken.Line, Column = opToken.Column, EndLine = _parser.LastTokenEndLine, EndColumn = _parser.LastTokenEndColumn };
            }
            else if (op == TokenType.LIKE || op == TokenType.ILIKE)
            {
                bool ilike = op == TokenType.ILIKE;
                // LIKE ANY (...) / LIKE ALL (...): match against a list of patterns (OR / AND).
                if (_parser.Current.Type == TokenType.ANY || _parser.Current.Type == TokenType.ALL)
                {
                    bool isAll = _parser.Current.Type == TokenType.ALL;
                    _parser.Advance();
                    _parser.Consume(TokenType.LPAREN, "Expected '(' after LIKE ANY/ALL");
                    var patterns = new List<Expression> { _parser.ParseExpression() };
                    while (_parser.Match(TokenType.COMMA))
                    {
                        if (_parser.Current.Type == TokenType.RPAREN) break;
                        patterns.Add(_parser.ParseExpression());
                    }
                    _parser.Consume(TokenType.RPAREN, "Expected ')' after LIKE ANY/ALL patterns");

                    Expression? combined = null;
                    foreach (var p in patterns)
                    {
                        Expression le = new LikeExpression(left, p, false, null, ilike) { Line = opToken.Line, Column = opToken.Column };
                        combined = combined == null ? le : new BinaryExpression(combined, isAll ? TokenType.AND : TokenType.OR, le);
                    }
                    combined ??= new LiteralExpression(isAll, TokenType.NUMBER); // empty list: ALL→true, ANY→false
                    left = isNot ? new UnaryExpression(TokenType.NOT, combined) { Line = opToken.Line, Column = opToken.Column } : combined;
                }
                else
                {
                    var right = ParseTerm();
                    Expression? escapeChar = null;
                    if (_parser.Match(TokenType.ESCAPE))
                    {
                        escapeChar = ParseTerm();
                    }
                    left = new LikeExpression(left, right, isNot, escapeChar, ilike) { Line = opToken.Line, Column = opToken.Column, EndLine = _parser.LastTokenEndLine, EndColumn = _parser.LastTokenEndColumn };
                }
            }
            else if (op == TokenType.BETWEEN)
            {
                var start = ParseTerm();
                _parser.Consume(TokenType.AND, "Expected 'AND' after BETWEEN start expression");
                var end = ParseTerm();
                left = new BetweenExpression(left, start, end, isNot) { Line = opToken.Line, Column = opToken.Column, EndLine = _parser.LastTokenEndLine, EndColumn = _parser.LastTokenEndColumn };
            }
            else
            {
                // Coalesce level so `x = amount ?? 0` parses as `x = (amount ?? 0)` — ?? binds
                // tighter than comparison on both sides. Strictly more accepting than ParseTerm.
                var right = ParseCoalesce();
                left = new BinaryExpression(left, op, right) { Line = opToken.Line, Column = opToken.Column, EndLine = _parser.LastTokenEndLine, EndColumn = _parser.LastTokenEndColumn };
            }
        }
        return left;
    }

    /// <summary>
    /// The ?? null-coalescing shorthand: <c>a ?? b [?? c ...]</c> lowers at parse time to the
    /// existing <c>COALESCE(a, b, c)</c> function call, so the evaluator, lineage tracking, and SQL
    /// pushdown all see plain COALESCE (universal SQL) — no new runtime semantics. Binds tighter
    /// than comparisons (<c>amount ?? 0 &gt; 5</c> means <c>(amount ?? 0) &gt; 5</c>) and looser
    /// than arithmetic (<c>a + b ?? 0</c> means <c>(a + b) ?? 0</c>). CASE/COALESCE remain the
    /// documented portable standard; this is an ETL-SQL dialect convenience.
    /// </summary>
    private Expression ParseCoalesce()
    {
        var left = ParseShift();
        if (_parser.Current.Type != TokenType.DOUBLE_QUESTION) return left;

        var opToken = _parser.Current;
        var args = new List<Expression> { left };
        while (_parser.Match(TokenType.DOUBLE_QUESTION))
            args.Add(ParseShift());
        return new FunctionCallExpression("COALESCE", args) { Line = opToken.Line, Column = opToken.Column, EndLine = _parser.LastTokenEndLine, EndColumn = _parser.LastTokenEndColumn };
    }

    /// <summary>
    /// IIF(cond, a, b) is T-SQL shorthand for <c>CASE WHEN cond THEN a ELSE b END</c>; lowering it at
    /// parse time (like ?? → COALESCE) gives it true CASE semantics — <b>short-circuit evaluation</b>
    /// (the untaken branch never runs, so <c>IIF(x = 0, 0, 1/x)</c> is safe, matching T-SQL) — and
    /// pushes down to every connector as universal CASE instead of a T-SQL-only function. Only the
    /// plain three-argument form lowers; anything else falls through to the runtime function.
    /// </summary>
    private static Expression LowerIifToCase(FunctionCallExpression call)
    {
        if (!call.FunctionName.Equals("IIF", StringComparison.OrdinalIgnoreCase)) return call;
        if (call.Arguments.Count != 3 || call.IsDistinct
            || call.Filter != null || call.Window != null || call.WithinGroupOrderBy != null) return call;

        return new CaseExpression(
            new List<(Expression Condition, Expression Result)> { (call.Arguments[0], call.Arguments[1]) },
            call.Arguments[2])
        { Line = call.Line, Column = call.Column, EndLine = call.EndLine, EndColumn = call.EndColumn };
    }

    private Expression ParseShift()
    {
        var left = ParseTerm();
        while (_parser.Current.Type == TokenType.LSHIFT || _parser.Current.Type == TokenType.RSHIFT)
        {
            var opToken = _parser.Advance();
            var op = opToken.Type;
            var right = ParseTerm();
            left = new BinaryExpression(left, op, right) { Line = opToken.Line, Column = opToken.Column, EndLine = _parser.LastTokenEndLine, EndColumn = _parser.LastTokenEndColumn };
        }
        return left;
    }

    private Expression ParseTerm()
    {
        var left = ParseFactor();
        while (_parser.Current.Type == TokenType.PLUS || _parser.Current.Type == TokenType.MINUS)
        {
            var opToken = _parser.Advance();
            var op = opToken.Type;
            var right = ParseFactor();
            left = new BinaryExpression(left, op, right) { Line = opToken.Line, Column = opToken.Column };
        }
        return left;
    }

    private Expression ParseFactor()
    {
        var left = ParseJsonAccess();
        while (_parser.Current.Type == TokenType.STAR || _parser.Current.Type == TokenType.SLASH || _parser.Current.Type == TokenType.MODULO)
        {
            var opToken = _parser.Advance();
            var op = opToken.Type;
            var right = ParseJsonAccess();
            left = new BinaryExpression(left, op, right) { Line = opToken.Line, Column = opToken.Column };
        }
        return left;
    }

    /// <summary>
    /// The -> / ->> JSON access operators (PostgreSQL/MySQL/SQLite style): <c>json -> key</c> returns
    /// the field or array element as JSON (chainable), <c>json ->> key</c> returns it as text. Both
    /// lower at parse time to the JSON_GET / JSON_GET_TEXT functions — canonical AST, so the
    /// evaluator, lineage, and pushdown see plain function calls. Left-associative and binding
    /// tighter than arithmetic: <c>a -> 'x' ->> 'y'</c> is <c>JSON_GET_TEXT(JSON_GET(a,'x'),'y')</c>.
    /// The key may be any expression — a string field name, an integer array index, or a variable.
    /// </summary>
    private Expression ParseJsonAccess()
    {
        var left = ParsePrimary();
        while (_parser.Current.Type == TokenType.JSON_ARROW || _parser.Current.Type == TokenType.JSON_ARROW_TEXT)
        {
            var opToken = _parser.Advance();
            var right = ParsePrimary();
            var fn = opToken.Type == TokenType.JSON_ARROW ? "JSON_GET" : "JSON_GET_TEXT";
            left = new FunctionCallExpression(fn, new List<Expression> { left, right })
            { Line = opToken.Line, Column = opToken.Column, EndLine = _parser.LastTokenEndLine, EndColumn = _parser.LastTokenEndColumn };
        }
        return left;
    }

    private Expression ParsePrimary()
    {
        var expr = ParsePrimaryBase();

        // Postfix: AT TIME ZONE
        while (true)
        {
            if (_parser.Current.Type == TokenType.AT)
            {
                if (PeekAtTimeZone())
                {
                    _parser.Advance(); // consume AT
                    _parser.Advance(); // consume TIME
                    _parser.Consume(TokenType.ZONE, "Expected 'ZONE' after 'AT TIME'");
                    var zone = ParsePrimary();
                    expr = new AtTimeZoneExpression(expr, zone)
                    {
                        Line = expr.Line,
                        Column = expr.Column,
                        EndLine = _parser.LastTokenEndLine,
                        EndColumn = _parser.LastTokenEndColumn
                    };
                    continue;
                }
            }
            break;
        }
        return expr;
    }

    private Expression ParsePrimaryBase()
    {
        if (_parser.Match(TokenType.EXISTS))
        {
            var t = _parser.Previous;
            _parser.Consume(TokenType.LPAREN, "Expected '(' after EXISTS");
            var subquery = _parser.ParseQuery();
            _parser.Consume(TokenType.RPAREN, "Expected ')' after subquery");
            return new ExistsExpression(subquery, false) { Line = t.Line, Column = t.Column, EndLine = _parser.LastTokenEndLine, EndColumn = _parser.LastTokenEndColumn };
        }
        if (_parser.Current.Type == TokenType.TRIM && _parser.Peek.Type == TokenType.LPAREN)
        {
            _parser.Advance();
            return ParseTrim();
        }
        if (_parser.Match(TokenType.SUBSTRING))
        {
            return ParseSubstring();
        }
        if (_parser.Match(TokenType.POSITION))
        {
            return ParsePosition();
        }
        if (_parser.Match(TokenType.EXTRACT))
        {
            return ParseExtract();
        }
        if (_parser.Match(TokenType.OVERLAY))
        {
            return ParseOverlay();
        }
        if (_parser.Match(TokenType.LBRACKET))
        {
            var items = new List<Expression>();
            if (_parser.Current.Type != TokenType.RBRACKET)
            {
                items.Add(_parser.ParseExpression());
                while (_parser.Match(TokenType.COMMA))
                {
                    items.Add(_parser.ParseExpression());
                }
            }
            _parser.Consume(TokenType.RBRACKET, "Expected ']' at the end of list expression");
            return new ListExpression(items) { Line = _parser.Previous.Line, Column = _parser.Previous.Column, EndLine = _parser.LastTokenEndLine, EndColumn = _parser.LastTokenEndColumn };
        }
        if (_parser.Match(TokenType.NUMBER))
        {
            var t = _parser.Previous;
            return new LiteralExpression(decimal.Parse(t.Value), TokenType.NUMBER) { Line = t.Line, Column = t.Column, EndLine = t.EndLine, EndColumn = t.EndColumn };
        }
        if (_parser.Match(TokenType.TRUE))
        {
            var t = _parser.Previous;
            return new LiteralExpression(true, TokenType.TRUE) { Line = t.Line, Column = t.Column, EndLine = t.EndLine, EndColumn = t.EndColumn };
        }
        if (_parser.Match(TokenType.FALSE))
        {
            var t = _parser.Previous;
            return new LiteralExpression(false, TokenType.FALSE) { Line = t.Line, Column = t.Column, EndLine = t.EndLine, EndColumn = t.EndColumn };
        }
        if (_parser.Match(TokenType.ON))
        {
            var t = _parser.Previous;
            return new LiteralExpression(true, TokenType.ON) { Line = t.Line, Column = t.Column, EndLine = t.EndLine, EndColumn = t.EndColumn };
        }
        if (_parser.Match(TokenType.OFF))
        {
            var t = _parser.Previous;
            return new LiteralExpression(false, TokenType.OFF) { Line = t.Line, Column = t.Column, EndLine = t.EndLine, EndColumn = t.EndColumn };
        }
        if (_parser.Match(TokenType.NULL))
        {
            var t = _parser.Previous;
            return new LiteralExpression(null, TokenType.NULL) { Line = t.Line, Column = t.Column, EndLine = t.EndLine, EndColumn = t.EndColumn };
        }
        if (_parser.Match(TokenType.STRING_LITERAL))
        {
            var t = _parser.Previous;
            return new LiteralExpression(t.Value, TokenType.STRING_LITERAL) { Line = t.Line, Column = t.Column, EndLine = t.EndLine, EndColumn = t.EndColumn };
        }
        if (_parser.Match(TokenType.CURRENT_TIMESTAMP) || _parser.Match(TokenType.CURRENT_DATE) || _parser.Match(TokenType.CURRENT_TIME) || _parser.Match(TokenType.SYSDATE))
        {
            var t = _parser.Previous;
            if (_parser.Match(TokenType.LPAREN))
            {
                _parser.Consume(TokenType.RPAREN, $"Expected ')' after {t.Value}");
            }
            return new FunctionCallExpression(t.Value, new List<Expression>()) { Line = t.Line, Column = t.Column, EndLine = _parser.LastTokenEndLine, EndColumn = _parser.LastTokenEndColumn };
        }
        if (_parser.Match(TokenType.CAST) || _parser.Match(TokenType.TRY_CAST))
        {
            var t = _parser.Previous;
            var funcName = t.Type == TokenType.TRY_CAST ? "TRY_CAST" : "CAST";
            _parser.Consume(TokenType.LPAREN, "Expected '(' after CAST/TRY_CAST");
            var expr = _parser.ParseExpression();
            _parser.Consume(TokenType.AS, "Expected 'AS' after expression in CAST/TRY_CAST");

            string targetType = _parser.ParseType();
            _parser.Consume(TokenType.RPAREN, "Expected ')' at end of CAST/TRY_CAST");
            return new FunctionCallExpression(funcName, new List<Expression> { expr, new LiteralExpression(targetType, TokenType.STRING_LITERAL) }) { Line = t.Line, Column = t.Column, EndLine = _parser.LastTokenEndLine, EndColumn = _parser.LastTokenEndColumn };
        }
        if (_parser.Match(TokenType.CASE))
        {
            var t = _parser.Previous;
            Expression? inputExpr = null;
            if (_parser.Current.Type != TokenType.WHEN)
            {
                inputExpr = _parser.ParseExpression();
            }

            var clauses = new List<(Expression Condition, Expression Result)>();
            Expression? elseResult = null;

            while (_parser.Match(TokenType.WHEN))
            {
                var condition = _parser.ParseExpression();
                _parser.Consume(TokenType.THEN, "Expected THEN after WHEN expression");
                var result = _parser.ParseExpression();
                clauses.Add((condition, result));
            }

            if (_parser.Match(TokenType.ELSE))
            {
                elseResult = _parser.ParseExpression();
            }

            _parser.Consume(TokenType.END, "Expected END at the conclusion of CASE statement");
            return new CaseExpression(clauses, elseResult, inputExpr) { Line = t.Line, Column = t.Column, EndLine = _parser.LastTokenEndLine, EndColumn = _parser.LastTokenEndColumn };
        }
        if (_parser.Match(TokenType.PARAMETER))
        {
            var t = _parser.Previous;
            int? index = null;
            if (t.Value.Length > 1 && int.TryParse(t.Value.Substring(1), out int idx))
            {
                index = idx;
            }
            return new ParameterExpression(t.Value, index) { Line = t.Line, Column = t.Column, EndLine = t.EndLine, EndColumn = t.EndColumn };
        }
        if (_parser.Match(TokenType.VARIABLE))
        {
            var t = _parser.Previous;
            Expression expr = new VariableExpression(t.Value) { Line = t.Line, Column = t.Column, EndLine = t.EndLine, EndColumn = t.EndColumn };
            while (_parser.Match(TokenType.DOT))
            {
                if (!_parser.IsIdentifier(_parser.Current) && !LanguageMetadata.IsKeyword(_parser.Current.Value))
                    throw new SyntaxException("Expected member name after '.'", _parser.Current.Line, _parser.Current.Column);
                var member = _parser.Advance();
                expr = new MemberAccessExpression(expr, member.Value) { Line = t.Line, Column = t.Column, EndLine = _parser.LastTokenEndLine, EndColumn = _parser.LastTokenEndColumn };
            }
            return expr;
        }

        if (_parser.IsIdentifier(_parser.Current))
        {
            var t = _parser.Advance();
            var name = t.Value;
            if (_parser.Match(TokenType.LPAREN))
            {
                bool isFuncDistinct = _parser.Match(TokenType.DISTINCT);
                if (!isFuncDistinct) _parser.Match(TokenType.ALL);
                var args = new List<Expression>();
                if (_parser.Current.Type != TokenType.RPAREN)
                {
                    if (_parser.Current.Type == TokenType.STAR)
                    {
                        var starToken = _parser.Advance();
                        args.Add(new IdentifierExpression("*") { Line = starToken.Line, Column = starToken.Column });
                    }
                    else
                    {
                        args.Add(_parser.ParseExpression());
                    }

                    while (_parser.Match(TokenType.COMMA))
                    {
                        if (_parser.Current.Type == TokenType.RPAREN) break; // tolerate trailing comma
                        if (_parser.Current.Type == TokenType.STAR)
                        {
                            var starToken = _parser.Advance();
                            args.Add(new IdentifierExpression("*") { Line = starToken.Line, Column = starToken.Column, EndLine = starToken.EndLine, EndColumn = starToken.EndColumn });
                        }
                        else
                        {
                            args.Add(_parser.ParseExpression());
                        }
                    }
                }
                // DuckDB count() shorthand: treat a zero-argument COUNT as COUNT(*).
                if (args.Count == 0 && name.Equals("COUNT", System.StringComparison.OrdinalIgnoreCase))
                {
                    args.Add(new IdentifierExpression("*") { Line = t.Line, Column = t.Column });
                }
                _parser.Consume(TokenType.RPAREN, "Expected ')' after function arguments");
                var funcCall = new FunctionCallExpression(name, args) { Line = t.Line, Column = t.Column, EndLine = _parser.LastTokenEndLine, EndColumn = _parser.LastTokenEndColumn, IsDistinct = isFuncDistinct };

                if (_parser.Match(TokenType.FILTER))
                {
                    _parser.Consume(TokenType.LPAREN, "Expected '(' after FILTER");
                    _parser.Consume(TokenType.WHERE, "Expected 'WHERE' inside FILTER");
                    funcCall.Filter = _parser.ParseExpression();
                    _parser.Consume(TokenType.RPAREN, "Expected ')' to close FILTER clause");
                }

                if (_parser.Match(TokenType.OVER))
                {
                    _parser.Consume(TokenType.LPAREN, "Expected '(' after OVER");
                    var partitionBy = new List<Expression>();
                    if (_parser.Match(TokenType.PARTITION))
                    {
                        _parser.Consume(TokenType.BY, "Expected 'BY' after 'PARTITION'");
                        partitionBy.Add(_parser.ParseExpression());
                        while (_parser.Match(TokenType.COMMA))
                        {
                            partitionBy.Add(_parser.ParseExpression());
                        }
                    }

                    var orderBy = new List<OrderByClause>();
                    if (_parser.Match(TokenType.ORDER))
                    {
                        _parser.Consume(TokenType.BY, "Expected 'BY' after 'ORDER'");
                        do
                        {
                            var orderExpr = _parser.ParseExpression();
                            bool descending = false;
                            if (_parser.Match(TokenType.DESC)) descending = true;
                            else _parser.Match(TokenType.ASC);
                            orderBy.Add(new OrderByClause(orderExpr, descending));
                        } while (_parser.Match(TokenType.COMMA));
                    }

                    // Parse Framing
                    WindowFrame? frame = null;
                    if (_parser.Match(TokenType.ROWS) || _parser.Match(TokenType.RANGE) || _parser.Match(TokenType.GROUPS))
                    {
                        var frameType = _parser.Previous.Type switch
                        {
                            TokenType.ROWS => WindowFrameType.ROWS,
                            TokenType.GROUPS => WindowFrameType.GROUPS,
                            _ => WindowFrameType.RANGE
                        };
                        if (_parser.Match(TokenType.BETWEEN))
                        {
                            var startBound = ParseFrameBound();
                            _parser.Consume(TokenType.AND, "Expected 'AND' in BETWEEN frame");
                            var endBound = ParseFrameBound();
                            frame = new WindowFrame(frameType, startBound.Type, startBound.Value, endBound.Type, endBound.Value);
                        }
                        else
                        {
                            var bound = ParseFrameBound();
                            frame = new WindowFrame(frameType, bound.Type, bound.Value);
                        }

                        if (_parser.Match(TokenType.EXCLUDE))
                        {
                            frame = frame with { Exclusion = ParseFrameExclusion() };
                        }
                    }
                    else if (orderBy.Count > 0)
                    {
                        // Standard SQL behavior: default frame for ORDER BY is RANGE BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
                        frame = new WindowFrame(WindowFrameType.RANGE, WindowFrameBoundType.UNBOUNDED_PRECEDING, null, WindowFrameBoundType.CURRENT_ROW, null);
                    }

                    _parser.Consume(TokenType.RPAREN, "Expected ')' to close OVER clause");
                    funcCall.Window = new WindowClause(partitionBy, orderBy, frame) { Line = t.Line, Column = t.Column, EndLine = _parser.LastTokenEndLine, EndColumn = _parser.LastTokenEndColumn };
                }

                if (_parser.Match(TokenType.WITHIN))
                {
                    _parser.Consume(TokenType.GROUP, "Expected 'GROUP' after 'WITHIN'");
                    _parser.Consume(TokenType.LPAREN, "Expected '(' after 'WITHIN GROUP'");
                    _parser.Consume(TokenType.ORDER, "Expected 'ORDER' inside WITHIN GROUP");
                    _parser.Consume(TokenType.BY, "Expected 'BY' after 'ORDER'");

                    var orderBy = new List<OrderByClause>();
                    do
                    {
                        var orderExpr = _parser.ParseExpression();
                        bool descending = false;
                        if (_parser.Match(TokenType.DESC)) descending = true;
                        else _parser.Match(TokenType.ASC);
                        orderBy.Add(new OrderByClause(orderExpr, descending));
                    } while (_parser.Match(TokenType.COMMA));

                    _parser.Consume(TokenType.RPAREN, "Expected ')' to close WITHIN GROUP");
                    funcCall.WithinGroupOrderBy = orderBy;
                }
                return LowerIifToCase(funcCall);
            }


            while (_parser.Match(TokenType.DOT))
            {
                if (_parser.Match(TokenType.STAR))
                {
                    name += ".*";
                    break;
                }
                if (_parser.IsIdentifier(_parser.Current) || LanguageMetadata.IsKeyword(_parser.Current.Value))
                {
                    var nextPart = _parser.Advance();
                    name += "." + nextPart.Value;
                }
                else
                {
                    // Resilience: Stop but keep the current name (ending in dot)
                    break;
                }
            }

            return new IdentifierExpression(name) { Line = t.Line, Column = t.Column, EndLine = _parser.LastTokenEndLine, EndColumn = _parser.LastTokenEndColumn };
        }

        if (_parser.Match(TokenType.LPAREN))
        {
            var parenToken = _parser.Previous;
            if (_parser.Current.Type == TokenType.SELECT)
            {
                var select = _parser.ParseQuery();
                _parser.Consume(TokenType.RPAREN, "Expected ')' after subquery");
                return new SubqueryExpression(select) { Line = parenToken.Line, Column = parenToken.Column, EndLine = _parser.LastTokenEndLine, EndColumn = _parser.LastTokenEndColumn };
            }

            var exprs = new List<Expression>();
            exprs.Add(_parser.ParseExpression());

            // Only treat as a ListExpression if a comma follows and we haven't hit the end of the group.
            // This prevents over-greedy consumption for simple parenthesized expressions.
            while (_parser.Match(TokenType.COMMA))
            {
                exprs.Add(_parser.ParseExpression());
            }

            _parser.Consume(TokenType.RPAREN, "Expected ')' after group expression");
            return exprs.Count == 1 ? exprs[0] : new ListExpression(exprs) { Line = parenToken.Line, Column = parenToken.Column, EndLine = _parser.LastTokenEndLine, EndColumn = _parser.LastTokenEndColumn };
        }

        if (_parser.Match(TokenType.MINUS))
        {
            var t = _parser.Previous;
            var expr = ParsePrimary();
            return new BinaryExpression(new LiteralExpression(0m, TokenType.NUMBER), TokenType.MINUS, expr) { Line = t.Line, Column = t.Column, EndLine = expr.EndLine, EndColumn = expr.EndColumn };
        }

        if (_parser.Match(TokenType.PLUS))
        {
            return ParsePrimary();
        }

        // Fallback: keyword tokens that are used as identifiers in expression context.
        // Symbols (STAR and above) are never identifiers. For everything else:
        //  - followed by '(' → function call (e.g. DIRECTORY(...), USE DOCKER(...))
        //  - followed by '.' → identifier prefix (e.g. DOCKER.CONNECTION_STRING)
        //  - followed by other → bare identifier (covers e.g. connector-type names used as table refs)
        // Identifier fallback: allows keywords like POWER, X_AXIS, or SOLID as identifiers/function names
        // but explicitly prevents greedy consumption of structural symbols.
        if (_parser.Current.Type < TokenType.STAR || (_parser.IsIdentifier(_parser.Current) &&
            _parser.Current.Type != TokenType.RPAREN &&
            _parser.Current.Type != TokenType.COMMA &&
            _parser.Current.Type != TokenType.SEMICOLON))
        {
            var t = _parser.Advance();
            var name = t.Value;
            if (_parser.Match(TokenType.LPAREN))
            {
                var args = new List<Expression>();
                if (_parser.Current.Type != TokenType.RPAREN)
                    args.Add(_parser.ParseExpression());
                while (_parser.Match(TokenType.COMMA))
                    args.Add(_parser.ParseExpression());
                _parser.Consume(TokenType.RPAREN, "Expected ')' after function arguments");
                return LowerIifToCase(new FunctionCallExpression(name, args) { Line = t.Line, Column = t.Column, EndLine = _parser.LastTokenEndLine, EndColumn = _parser.LastTokenEndColumn });
            }
            while (_parser.Match(TokenType.DOT))
            {
                if (_parser.Match(TokenType.STAR)) { name += ".*"; break; }
                if (_parser.IsIdentifier(_parser.Current) || LanguageMetadata.IsKeyword(_parser.Current.Value))
                    name += "." + _parser.Advance().Value;
                else break;
            }
            return new IdentifierExpression(name) { Line = t.Line, Column = t.Column, EndLine = _parser.LastTokenEndLine, EndColumn = _parser.LastTokenEndColumn };
        }

        throw new SyntaxException($"Expected expression primary but got {_parser.Current.Type} ('{_parser.Current.Value}')", _parser.Current.Line, _parser.Current.Column);
    }

    private SubstringExpression ParseSubstring()
    {
        var t = _parser.Previous;
        _parser.Consume(TokenType.LPAREN, "Expected '(' after SUBSTRING");
        var str = _parser.ParseExpression();

        Expression start;
        Expression? length = null;

        if (_parser.Match(TokenType.FROM))
        {
            start = _parser.ParseExpression();
            if (_parser.Match(TokenType.FOR))
            {
                length = _parser.ParseExpression();
            }
        }
        else
        {
            _parser.Consume(TokenType.COMMA, "Expected ',' or 'FROM' in SUBSTRING");
            start = _parser.ParseExpression();
            if (_parser.Match(TokenType.COMMA))
            {
                length = _parser.ParseExpression();
            }
        }

        _parser.Consume(TokenType.RPAREN, "Expected ')' after SUBSTRING arguments");
        return new SubstringExpression(str, start, length) { Line = t.Line, Column = t.Column, EndLine = _parser.LastTokenEndLine, EndColumn = _parser.LastTokenEndColumn };
    }

    private PositionExpression ParsePosition()
    {
        var t = _parser.Previous;
        _parser.Consume(TokenType.LPAREN, "Expected '(' after POSITION");
        var substr = ParseTerm();
        _parser.Consume(TokenType.IN, "Expected 'IN' in POSITION");
        var str = _parser.ParseExpression();
        _parser.Consume(TokenType.RPAREN, "Expected ')' after POSITION arguments");
        return new PositionExpression(substr, str) { Line = t.Line, Column = t.Column, EndLine = _parser.LastTokenEndLine, EndColumn = _parser.LastTokenEndColumn };
    }

    private ExtractExpression ParseExtract()
    {
        var t = _parser.Previous;
        _parser.Consume(TokenType.LPAREN, "Expected '(' after EXTRACT");
        var field = _parser.Advance().Value; // e.g. YEAR, MONTH
        _parser.Consume(TokenType.FROM, "Expected 'FROM' in EXTRACT");
        var source = _parser.ParseExpression();
        _parser.Consume(TokenType.RPAREN, "Expected ')' after EXTRACT arguments");
        return new ExtractExpression(field, source) { Line = t.Line, Column = t.Column, EndLine = _parser.LastTokenEndLine, EndColumn = _parser.LastTokenEndColumn };
    }

    private OverlayExpression ParseOverlay()
    {
        var t = _parser.Previous;
        _parser.Consume(TokenType.LPAREN, "Expected '(' after OVERLAY");
        var str = _parser.ParseExpression();
        _parser.Consume(TokenType.PLACING, "Expected 'PLACING' in OVERLAY");
        var overlay = _parser.ParseExpression();
        _parser.Consume(TokenType.FROM, "Expected 'FROM' in OVERLAY");
        var start = _parser.ParseExpression();
        Expression? length = null;
        if (_parser.Match(TokenType.FOR))
        {
            length = _parser.ParseExpression();
        }
        _parser.Consume(TokenType.RPAREN, "Expected ')' after OVERLAY arguments");
        return new OverlayExpression(str, overlay, start, length) { Line = t.Line, Column = t.Column, EndLine = _parser.LastTokenEndLine, EndColumn = _parser.LastTokenEndColumn };
    }

    private TrimExpression ParseTrim()
    {
        var t = _parser.Previous;
        _parser.Consume(TokenType.LPAREN, "Expected '(' after TRIM");

        TrimType type = TrimType.BOTH;
        Expression? characters = null;

        if (_parser.Match(TokenType.LEADING)) type = TrimType.LEADING;
        else if (_parser.Match(TokenType.TRAILING)) type = TrimType.TRAILING;
        else if (_parser.Match(TokenType.BOTH)) type = TrimType.BOTH;

        if (type != TrimType.BOTH || _parser.Current.Type != TokenType.FROM)
        {
            if (_parser.Current.Type != TokenType.FROM)
            {
                characters = _parser.ParseExpression();
            }
        }

        if (_parser.Match(TokenType.FROM))
        {
            var str = _parser.ParseExpression();
            _parser.Consume(TokenType.RPAREN, "Expected ')' after TRIM arguments");
            return new TrimExpression(type, characters, str) { Line = t.Line, Column = t.Column, EndLine = _parser.LastTokenEndLine, EndColumn = _parser.LastTokenEndColumn };
        }
        else
        {
            // Simple TRIM(expr) case
            var str = characters ?? _parser.ParseExpression();
            _parser.Consume(TokenType.RPAREN, "Expected ')' after TRIM");
            return new TrimExpression(TrimType.BOTH, null, str) { Line = t.Line, Column = t.Column, EndLine = _parser.LastTokenEndLine, EndColumn = _parser.LastTokenEndColumn };
        }
    }

    private WindowFrameBound ParseFrameBound()
    {
        if (_parser.Match(TokenType.CURRENT))
        {
            _parser.Consume(TokenType.ROW, "Expected 'ROW' after 'CURRENT'");
            return new WindowFrameBound(WindowFrameBoundType.CURRENT_ROW);
        }
        if (_parser.Match(TokenType.UNBOUNDED))
        {
            if (_parser.Match(TokenType.PRECEDING)) return new WindowFrameBound(WindowFrameBoundType.UNBOUNDED_PRECEDING);
            _parser.Consume(TokenType.FOLLOWING, "Expected 'FOLLOWING' after 'UNBOUNDED'");
            return new WindowFrameBound(WindowFrameBoundType.UNBOUNDED_FOLLOWING);
        }

        var val = ParseExpression();
        if (_parser.Match(TokenType.PRECEDING)) return new WindowFrameBound(WindowFrameBoundType.PRECEDING, val);
        if (_parser.Match(TokenType.FOLLOWING)) return new WindowFrameBound(WindowFrameBoundType.FOLLOWING, val);

        throw new SyntaxException("Expected PRECEDING, FOLLOWING, CURRENT ROW, or UNBOUNDED", _parser.Current.Line, _parser.Current.Column);
    }

    private WindowFrameExclusion ParseFrameExclusion()
    {
        if (_parser.Match(TokenType.CURRENT))
        {
            _parser.Consume(TokenType.ROW, "Expected ROW after EXCLUDE CURRENT");
            return WindowFrameExclusion.CurrentRow;
        }

        if (_parser.Match(TokenType.GROUP)) return WindowFrameExclusion.Group;
        if (_parser.Match(TokenType.TIES)) return WindowFrameExclusion.Ties;
        if (_parser.Match(TokenType.NO))
        {
            _parser.Consume(TokenType.OTHERS, "Expected OTHERS after EXCLUDE NO");
            return WindowFrameExclusion.NoOthers;
        }

        throw new SyntaxException("Expected CURRENT ROW, GROUP, TIES, or NO OTHERS after EXCLUDE", _parser.Current.Line, _parser.Current.Column);
    }

    private struct WindowFrameBound
    {
        public WindowFrameBoundType Type { get; }
        public Expression? Value { get; }
        public WindowFrameBound(WindowFrameBoundType type, Expression? value = null) { Type = type; Value = value; }
    }
}
