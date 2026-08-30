using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Quality;

namespace ETL_SQL.Core.Parser.Components;

public class FlowParser : ParserComponent
{
    public FlowParser(IParser parser, StatementParser parent) : base(parser, parent) { }

    public BlockStatement ParseBlock()
    {
        var stmts = new List<Statement>();
        while (_parser.Current.Type != TokenType.END && _parser.Current.Type != TokenType.EOF)
        {
            // Tolerate empty statements, mirroring the top-level Parse() loop. Most statement
            // parsers consume their own trailing ';', but some (e.g. PUBLISH BUNDLE and other
            // orchestrator/portal meta-statements) leave it; at top level that ';' is skipped
            // here, so blocks must skip it too — otherwise it reaches ParseStatement as an
            // "Unexpected SEMICOLON at start of statement" inside BEGIN/TRY/loop bodies.
            if (_parser.Match(TokenType.SEMICOLON)) continue;
            stmts.Add(_parser.ParseStatement());
        }
        Consume(TokenType.END, "Expected END to close BEGIN block");
        Match(TokenType.SEMICOLON); // optional trailing ; after END (e.g. nested WHILE/IF-ELSE)
        return new BlockStatement(stmts);
    }

    public Statement ParseTryCatch()
    {
        var tryBody = ParseBlock();
        if (_parser.Current.Value.Equals("TRY", StringComparison.OrdinalIgnoreCase))
            Advance();
        if (Match(TokenType.SEMICOLON)) { /* optional after END TRY */ }

        Consume(TokenType.BEGIN, "Expected BEGIN after END TRY");
        Consume(TokenType.CATCH, "Expected CATCH after BEGIN");
        var catchBody = ParseBlock();
        if (_parser.Current.Value.Equals("CATCH", StringComparison.OrdinalIgnoreCase))
            Advance();
        if (Match(TokenType.SEMICOLON)) { /* optional after END CATCH */ }

        return new TryCatchStatement(tryBody, catchBody);
    }

    public Statement ParseIf(Token startToken)
    {
        var condition = ParseExpression();
        var ifBody = _parser.ParseStatement();

        List<ElseIfClause>? elseIfClauses = null;
        Statement? elseBody = null;

        while (Match(TokenType.ELSE))
        {
            if (Match(TokenType.IF))
            {
                var elseIfCondition = ParseExpression();
                var elseIfBody = _parser.ParseStatement();
                if (elseIfClauses == null) elseIfClauses = new List<ElseIfClause>();
                elseIfClauses.Add(new ElseIfClause(elseIfCondition, elseIfBody));
            }
            else
            {
                elseBody = _parser.ParseStatement();
                break;
            }
        }

        return new IfStatement(condition, ifBody, elseIfClauses, elseBody)
        {
            Line = startToken.Line,
            Column = startToken.Column,
            EndLine = _parser.LastTokenEndLine,
            EndColumn = _parser.LastTokenEndColumn
        };
    }

    public Statement ParseWhile(Token startToken)
    {
        var condition = ParseExpression();
        var body = _parser.ParseStatement();
        return new WhileStatement(condition, body)
        {
            Line = startToken.Line,
            Column = startToken.Column,
            EndLine = _parser.LastTokenEndLine,
            EndColumn = _parser.LastTokenEndColumn
        };
    }

    public Statement ParseFor()
    {
        var startToken = _parser.Previous;
        var varToken = Consume(TokenType.VARIABLE, "Expected variable name starting with '@' for FOR loop");

        // FOR @row IN (query) — result-set iteration, same semantics as FOREACH @row IN (subquery)
        if (Match(TokenType.IN))
        {
            var listExpr = ParseExpression();
            var body = _parser.ParseStatement();
            return new ForeachStatement(varToken.Value, listExpr, body)
            {
                Line = startToken.Line,
                Column = startToken.Column,
                EndLine = _parser.LastTokenEndLine,
                EndColumn = _parser.LastTokenEndColumn
            };
        }

        // FOR @i [= start] TO end [STEP n] — numeric range iteration
        Expression startExpr;
        bool isImplicit = false;
        if (Match(TokenType.EQUALS))
        {
            startExpr = ParseExpression();
            Consume(TokenType.TO, "Expected TO in FOR loop limits");
        }
        else if (Match(TokenType.TO))
        {
            // Implicit start at 1
            startExpr = new LiteralExpression(1m, TokenType.NUMBER) { Line = varToken.Line, Column = varToken.Column };
            isImplicit = true;
        }
        else
        {
            throw new SyntaxException("Expected '=' or 'IN' in FOR loop", _parser.Current.Line, _parser.Current.Column);
        }
        var endExpr = ParseExpression();
        Expression? stepExpr = null;
        if (Match(TokenType.STEP)) stepExpr = ParseExpression();
        var rangeBody = _parser.ParseStatement();
        return new ForStatement(varToken.Value, startExpr, endExpr, stepExpr, rangeBody)
        {
            IsStartImplicit = isImplicit,
            Line = startToken.Line,
            Column = startToken.Column,
            EndLine = _parser.LastTokenEndLine,
            EndColumn = _parser.LastTokenEndColumn
        };
    }

    public Statement ParseForeach()
    {
        var startToken = _parser.Previous;
        var varToken = Consume(TokenType.VARIABLE, "Expected variable name starting with '@' for FOREACH loop");
        Consume(TokenType.IN, "Expected IN for FOREACH loop parameter");
        var listExpr = ParseExpression();
        var body = _parser.ParseStatement();
        return new ForeachStatement(varToken.Value, listExpr, body)
        {
            Line = startToken.Line,
            Column = startToken.Column,
            EndLine = _parser.LastTokenEndLine,
            EndColumn = _parser.LastTokenEndColumn
        };
    }

    public Statement ParseParallel(Token startToken)
    {
        int concurrencyLimit = 0;
        if (Match(TokenType.LPAREN))
        {
            concurrencyLimit = int.Parse(Consume(TokenType.NUMBER, "Expected concurrency limit number after '('").Value);
            Consume(TokenType.RPAREN, "Expected ')' after concurrency limit");
        }

        // PARALLEL [n] FOR @i = start TO end [STEP n] BEGIN...END
        if (Match(TokenType.FOR))
        {
            var varToken = Consume(TokenType.VARIABLE, "Expected loop variable after PARALLEL FOR");
            Expression startExpr;
            bool isImplicit = false;
            if (Match(TokenType.EQUALS))
            {
                startExpr = ParseExpression();
                Consume(TokenType.TO, "Expected TO in PARALLEL FOR range");
            }
            else if (Match(TokenType.TO))
            {
                startExpr = new LiteralExpression(1m, TokenType.NUMBER) { Line = varToken.Line, Column = varToken.Column };
                isImplicit = true;
            }
            else
            {
                throw new SyntaxException("Expected '=' or 'TO' in PARALLEL FOR range", _parser.Current.Line, _parser.Current.Column);
            }
            var endExpr = ParseExpression();
            Expression? stepExpr = null;
            if (Match(TokenType.STEP)) stepExpr = ParseExpression();
            Consume(TokenType.BEGIN, "Expected BEGIN after PARALLEL FOR range");
            var forBody = ParseBlock();
            return new ParallelForStatement(varToken.Value, startExpr, endExpr, stepExpr, forBody, concurrencyLimit)
            {
                IsStartImplicit = isImplicit,
                Line = startToken.Line,
                Column = startToken.Column
            };
        }

        Consume(TokenType.BEGIN, "Expected BEGIN after PARALLEL");
        var body = ParseBlock();
        return new ParallelStatement(body, concurrencyLimit) { Line = startToken.Line, Column = startToken.Column };
    }

    public Statement ParseReturn()
    {
        Expression? returnValue = null;
        if (_parser.Current.Type != TokenType.SEMICOLON && _parser.Current.Type != TokenType.EOF &&
            _parser.Current.Type != TokenType.END && _parser.Current.Type != TokenType.CATCH)
            returnValue = ParseExpression();
        if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
        return new ReturnStatement(returnValue);
    }

    public Statement ParseBreak()
    {
        if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
        return new BreakStatement();
    }

    public Statement ParseContinue()
    {
        if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
        return new ContinueStatement();
    }

    public Statement ParseRaiseError()
    {
        Consume(TokenType.LPAREN, "Expected '(' after RAISEERROR");
        var message = ParseExpression();
        Consume(TokenType.COMMA, "Expected severity after RAISEERROR message");
        var severity = ParseExpression();

        Expression? location = null;
        List<Expression>? parameters = null;
        if (Match(TokenType.COMMA))
        {
            location = ParseExpression();
            while (Match(TokenType.COMMA))
            {
                if (parameters == null) parameters = new List<Expression>();
                parameters.Add(ParseExpression());
            }
        }

        Consume(TokenType.RPAREN, "Expected ')' after RAISEERROR arguments");
        if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
        return new RaiseErrorStatement(message, severity, location, parameters);
    }

    public Statement ParseThrow()
    {
        Expression? errorNumber = null;
        Expression? message = null;
        Expression? state = null;

        if (_parser.Current.Type != TokenType.SEMICOLON && _parser.Current.Type != TokenType.EOF &&
            _parser.Current.Type != TokenType.END && _parser.Current.Type != TokenType.CATCH)
        {
            errorNumber = ParseExpression();
            if (Match(TokenType.COMMA))
            {
                message = ParseExpression();
                Consume(TokenType.COMMA, "Expected comma after THROW message expression");
                state = ParseExpression();
            }
            else
            {
                message = errorNumber;
                errorNumber = null;
            }
        }

        if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
        return new ThrowStatement(errorNumber, message, state);
    }

    public Statement ParseAssert(Token startToken)
    {
        if (_parser.Current.Type == TokenType.TABLE || IsContextualWord(_parser.Current, "TABLE"))
        {
            Advance(); // TABLE
            return ParseAssertTable(startToken);
        }

        // ASSERT JOB <name> (...) asserts on the run's own metrics. JOB is a keyword token, but
        // only treat it as the ASSERT JOB form when a job name follows — so a bare `ASSERT job = 1`
        // over a column named "job" still parses as a boolean assertion.
        if (_parser.Current.Type == TokenType.JOB
            && (_parser.IsIdentifier(_parser.Peek) || _parser.Peek.Type == TokenType.STRING_LITERAL))
        {
            Advance(); // JOB
            return ParseAssertJob(startToken);
        }

        var condition = ParseExpression();
        Expression? message = null;
        if (Match(TokenType.COMMA)) message = ParseExpression();
        if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
        return new AssertStatement(condition, message) { Line = startToken.Line, Column = startToken.Column };
    }

    private Statement ParseAssertTable(Token startToken)
    {
        var actualRef = ParseTableReference(allowFunction: false, allowWithClause: false, allowAlias: false);
        string actualTable = actualRef.ToString();

        if (IsContextualWord(_parser.Current, "MATCHES") || IsContextualWord(_parser.Current, "EQUALS"))
        {
            Advance();
        }
        else
        {
            throw new SyntaxException($"Expected 'MATCHES' after ASSERT TABLE {actualTable}", _parser.Current.Line, _parser.Current.Column);
        }

        var expectedRef = ParseTableReference(allowFunction: false, allowWithClause: false, allowAlias: false);
        string expectedTable = expectedRef.ToString();

        bool ignoreOrder = false;
        decimal? tolerance = null;
        List<string>? ignoreColumns = null;
        Expression? message = null;
        var options = new Dictionary<string, Expression>(StringComparer.OrdinalIgnoreCase);

        if (IsContextualWord(_parser.Current, "WITH"))
        {
            Advance();
            Consume(TokenType.LPAREN, "Expected '(' after ASSERT TABLE ... WITH");
            while (_parser.Current.Type != TokenType.RPAREN && _parser.Current.Type != TokenType.EOF)
            {
                var optToken = ConsumeIdentifier("Expected option name in ASSERT TABLE ... WITH clause");
                var optName = optToken.Value.ToUpperInvariant();
                Consume(TokenType.EQUALS, $"Expected '=' after option '{optName}' in ASSERT TABLE WITH clause");
                var expr = ParseExpression();
                options[optName] = expr;

                switch (optName)
                {
                    case "IGNORE_ORDER":
                        if (expr is LiteralExpression litOrder && litOrder.Value is bool bOrder)
                            ignoreOrder = bOrder;
                        else if (expr is IdentifierExpression idOrder && bool.TryParse(idOrder.Name, out var bParsed))
                            ignoreOrder = bParsed;
                        break;
                    case "TOLERANCE":
                        if (expr is LiteralExpression litTol && litTol.Value != null)
                            tolerance = Convert.ToDecimal(litTol.Value, CultureInfo.InvariantCulture);
                        break;
                    case "IGNORE_COLUMNS":
                        if (expr is LiteralExpression litCols && litCols.Value is string colStr)
                            ignoreColumns = colStr.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
                        else if (expr is ListExpression listExpr)
                        {
                            ignoreColumns = listExpr.Items
                                .Select(item => item is LiteralExpression l && l.Value is string s ? s : item.ToString() ?? "")
                                .Where(s => !string.IsNullOrWhiteSpace(s))
                                .ToList();
                        }
                        break;
                    case "MESSAGE":
                        message = expr;
                        break;
                }

                if (!Match(TokenType.COMMA)) break;
            }
            Consume(TokenType.RPAREN, "Expected ')' to close ASSERT TABLE WITH clause");
        }

        if (Match(TokenType.COMMA) && message == null)
        {
            message = ParseExpression();
        }

        if (_parser.Current.Type == TokenType.SEMICOLON) Advance();

        return new AssertTableStatement(
            actualTable,
            expectedTable,
            ignoreOrder,
            tolerance,
            ignoreColumns,
            message,
            options.Count > 0 ? options : null)
        {
            Line = startToken.Line,
            Column = startToken.Column
        };
    }

    /// <summary>
    /// Parses <c>ASSERT JOB &lt;name&gt; (&lt;predicate&gt;, …) [ON FAILURE &lt;action&gt;]…;</c> where an
    /// action is <c>WARN</c>, <c>NOTIFY &lt;notification&gt;</c>, or <c>THROW</c> — the same stacked
    /// <c>ON FAILURE</c> blocks a rule-carrying SELECT takes, drawn from the same vocabulary.
    /// <c>NOTIFY</c>, <c>FAILURE</c>, and <c>WARN</c> are matched as contextual identifiers,
    /// mirroring the other trailing action clauses.
    /// </summary>
    private Statement ParseAssertJob(Token startToken)
    {
        var jobName = _parser.Current.Type == TokenType.STRING_LITERAL
            ? Advance().Value
            : ConsumeIdentifier("Expected a job name after ASSERT JOB").Value;

        Consume(TokenType.LPAREN, "Expected '(' with at least one metric predicate after ASSERT JOB <name>");
        var predicates = new List<JobMetricPredicate>();
        do
        {
            if (_parser.Current.Type == TokenType.RPAREN) break;
            predicates.Add(ParseJobMetricPredicate());
        } while (Match(TokenType.COMMA));
        Consume(TokenType.RPAREN, "Expected ')' to close the ASSERT JOB predicate list");

        if (predicates.Count == 0)
            throw new SyntaxException("ASSERT JOB requires at least one metric predicate",
                startToken.Line, startToken.Column);

        if (IsContextualWord(_parser.Current, "WITH"))
            throw new SyntaxException(
                "ASSERT JOB takes no WITH options. WITH (FAIL_ON_WARN = TRUE) is now the predicate "
                + "WARN_PERCENT = 0 with ON FAILURE THROW, so exactly one clause decides whether the "
                + "run fails.",
                _parser.Current.Line, _parser.Current.Column);

        var actions = ParseAssertJobActions();

        if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
        return new AssertJobStatement(jobName, predicates, actions)
        {
            Line = startToken.Line,
            Column = startToken.Column
        };
    }

    /// <summary>
    /// Parses the stacked <c>ON FAILURE &lt;action&gt;</c> blocks after an <c>ASSERT JOB</c> predicate
    /// list. <c>QUARANTINE</c> is rejected with a pointer to the SELECT clause — a job metric has no
    /// row to divert. Declaration order does not matter: the handler always notifies before it
    /// throws, because that is the only useful order.
    /// </summary>
    private List<FailureActionClause> ParseAssertJobActions()
    {
        var actions = new List<FailureActionClause>();
        while (_parser.Current.Type == TokenType.ON && IsContextualWord(_parser.Peek, "FAILURE"))
        {
            var onToken = Advance(); // ON
            Advance();               // FAILURE

            var actionToken = _parser.Current;
            FailAction action;
            string? target = null;

            if (IsContextualWord(actionToken, "ALERT"))
                throw new SyntaxException(
                    "ASSERT JOB ... ON FAILURE ALERT <connection> has been retired. " +
                    "Create a NOTIFICATION on the Orchestrator and use ON FAILURE NOTIFY <notification>.",
                    actionToken.Line, actionToken.Column);

            if (IsContextualWord(actionToken, "NOTIFY"))
            {
                Advance();
                action = FailAction.Notify;
                target = ConsumeIdentifier("Expected a notification name after ON FAILURE NOTIFY").Value;
            }
            else if (_parser.Current.Type == TokenType.THROW || IsContextualWord(actionToken, "THROW"))
            {
                Advance();
                action = FailAction.Throw;
            }
            else if (IsContextualWord(actionToken, "WARN"))
            {
                Advance();
                action = FailAction.Warn;
            }
            else if (IsContextualWord(actionToken, "QUARANTINE"))
            {
                throw new SyntaxException(
                    "QUARANTINE routes a failing row, and a job metric has no row to divert. "
                    + "Quarantine is declared on a SELECT: ON FAILURE QUARANTINE TO <table>.",
                    actionToken.Line, actionToken.Column);
            }
            else
            {
                throw new SyntaxException(
                    $"'{actionToken.Value}' is not an ASSERT JOB failure action. "
                    + "Expected WARN, NOTIFY <notification>, or THROW.",
                    actionToken.Line, actionToken.Column);
            }

            if (actions.Any(a => a.Action == action))
                throw new SyntaxException(
                    $"Duplicate ON FAILURE {action.ToString().ToUpperInvariant()} clause on ASSERT JOB",
                    onToken.Line, onToken.Column);

            actions.Add(new FailureActionClause(action, target, null)
            {
                Line = onToken.Line,
                Column = onToken.Column
            });
        }
        return actions;
    }

    /// <summary>
    /// Parses one metric predicate: either <c>&lt;metric&gt; WITHIN &lt;frac&gt; OF HISTORICAL</c> or
    /// <c>&lt;metric&gt; &lt;op&gt; &lt;value&gt;</c>.
    /// </summary>
    private JobMetricPredicate ParseJobMetricPredicate()
    {
        var metricToken = _parser.Current;
        var metricName = _parser.IsIdentifier(metricToken)
            ? Advance().Value
            : throw new SyntaxException(
                "Expected a metric name (ROW_COUNT, NULL_PERCENT, FRESHNESS, QUARANTINE_PERCENT, WARN_PERCENT)",
                metricToken.Line, metricToken.Column);

        JobMetricKind metric;
        string? columnName = null;
        string? predicateTarget = null;
        switch (metricName.ToUpperInvariant())
        {
            case "ROW_COUNT":
                metric = JobMetricKind.RowCount;
                break;
            case "QUARANTINE_PERCENT":
                metric = JobMetricKind.QuarantinePercent;
                break;
            case "WARN_PERCENT":
                metric = JobMetricKind.WarnPercent;
                break;
            case "NULL_PERCENT":
                metric = JobMetricKind.NullPercent;
                Consume(TokenType.LPAREN, "Expected '(' with a column name after NULL_PERCENT");
                (predicateTarget, columnName) = ParseJobMetricColumnReference("NULL_PERCENT");
                Consume(TokenType.RPAREN, "Expected ')' after the NULL_PERCENT column");
                break;
            case "FRESHNESS":
                metric = JobMetricKind.Freshness;
                Consume(TokenType.LPAREN, "Expected '(' with a column name after FRESHNESS");
                (predicateTarget, columnName) = ParseJobMetricColumnReference("FRESHNESS");
                Consume(TokenType.RPAREN, "Expected ')' after the FRESHNESS column");
                break;
            default:
                throw new SyntaxException(
                    $"Unknown job metric '{metricName}'. Supported: ROW_COUNT, NULL_PERCENT(<col>), FRESHNESS(<col>), QUARANTINE_PERCENT, WARN_PERCENT",
                    metricToken.Line, metricToken.Column);
        }

        if (IsContextualWord(_parser.Current, "WITHIN"))
        {
            if (metric == JobMetricKind.Freshness)
                throw new SyntaxException("FRESHNESS does not support OF HISTORICAL; compare it to an interval literal",
                    metricToken.Line, metricToken.Column);

            Advance(); // WITHIN
            var tolerance = ParseSignedNumber("Expected a numeric tolerance after WITHIN");
            if (tolerance < 0)
                throw new SyntaxException("WITHIN tolerance must not be negative",
                    _parser.Previous.Line, _parser.Previous.Column);
            bool usesSigma = false;
            if (IsContextualWord(_parser.Current, "SIGMA"))
            {
                usesSigma = true;
                Advance(); // SIGMA
            }
            if (!IsContextualWord(_parser.Current, "OF"))
                throw new SyntaxException("Expected OF after the WITHIN tolerance",
                    _parser.Current.Line, _parser.Current.Column);
            Advance(); // OF
            if (!IsContextualWord(_parser.Current, "HISTORICAL"))
                throw new SyntaxException("Expected HISTORICAL after 'WITHIN <n> OF'",
                    _parser.Current.Line, _parser.Current.Column);
            Advance(); // HISTORICAL
            return new JobMetricPredicate(metric, columnName, null, null, tolerance, predicateTarget, null, usesSigma)
            {
                Line = metricToken.Line,
                Column = metricToken.Column
            };
        }

        var op = _parser.Current.Type switch
        {
            TokenType.GREATER_EQUALS => CompareOp.GreaterOrEqual,
            TokenType.LESS_EQUALS => CompareOp.LessOrEqual,
            TokenType.GREATER_THAN => CompareOp.Greater,
            TokenType.LESS_THAN => CompareOp.Less,
            TokenType.EQUALS => CompareOp.Equal,
            _ => throw new SyntaxException(
                $"Expected a comparison operator or 'WITHIN' after {metricName}",
                _parser.Current.Line, _parser.Current.Column)
        };
        Advance();

        if (metric == JobMetricKind.Freshness)
        {
            var intervalToken = Consume(TokenType.STRING_LITERAL,
                $"Expected an interval string after the {metricName} comparison");
            if (!ETL_SQL.Core.Quality.RetentionInterval.TryParse(intervalToken.Value, out var interval))
                throw new SyntaxException(
                    $"FRESHNESS interval '{intervalToken.Value}' is not valid — use '<n> MINUTES|HOURS|DAYS|WEEKS'",
                    intervalToken.Line, intervalToken.Column);

            return new JobMetricPredicate(metric, columnName, op, null, null, predicateTarget, interval)
            {
                Line = metricToken.Line,
                Column = metricToken.Column
            };
        }

        var bound = ParseSignedNumber($"Expected a numeric bound after the {metricName} comparison");

        return new JobMetricPredicate(metric, columnName, op, bound, null, predicateTarget)
        {
            Line = metricToken.Line,
            Column = metricToken.Column
        };
    }

    private (string? TargetName, string ColumnName) ParseJobMetricColumnReference(string metricName)
    {
        var first = ConsumeIdentifier($"Expected a column name inside {metricName}(...)").Value;
        if (!Match(TokenType.DOT)) return (null, first);
        var second = ConsumeIdentifier($"Expected a column name after '.' inside {metricName}(...)").Value;
        return (first, second);
    }

    private decimal ParseSignedNumber(string message)
    {
        bool negative = false;
        if (_parser.Current.Type == TokenType.MINUS) { negative = true; Advance(); }
        else if (_parser.Current.Type == TokenType.PLUS) Advance();

        var token = Consume(TokenType.NUMBER, message);
        var value = decimal.Parse(token.Value, System.Globalization.CultureInfo.InvariantCulture);
        return negative ? -value : value;
    }

    /// <summary>
    /// Matches a word by its text regardless of whether the lexer classified it as an identifier or
    /// a keyword. Several words in the ASSERT JOB grammar (WITHIN, NOTIFY, OF, HISTORICAL) are
    /// keyword tokens elsewhere in the language, so type-based matching would miss them.
    /// </summary>
    private static bool IsContextualWord(Token token, string word) =>
        token.Type != TokenType.STRING_LITERAL
        && token.Value.Equals(word, StringComparison.OrdinalIgnoreCase);

    public Statement ParseExpectSchema(Token startToken)
    {
        Consume(TokenType.SCHEMA, "Expected SCHEMA after EXPECT");
        var target = ConsumeIdentifier("Expected table or connection name after EXPECT SCHEMA").Value;

        List<ExpectedSchemaColumn>? columns = null;
        string? schemaPath = null;

        if (Match(TokenType.LPAREN))
        {
            columns = new List<ExpectedSchemaColumn>();
            while (_parser.Current.Type != TokenType.RPAREN && _parser.Current.Type != TokenType.EOF)
            {
                var colName = ConsumeIdentifier("Expected column name").Value;
                string dataType = "VARCHAR";
                if (_parser.IsIdentifier(_parser.Current))
                {
                    dataType = Advance().Value;
                    if (Match(TokenType.LPAREN))
                    {
                        dataType += "(" + Consume(TokenType.NUMBER, "Expected length").Value;
                        if (Match(TokenType.COMMA))
                            dataType += "," + Consume(TokenType.NUMBER, "Expected scale").Value;
                        dataType += ")";
                        Consume(TokenType.RPAREN, "Expected ')' after type length");
                    }
                }
                bool notNull = false;
                if (Match(TokenType.NOT)) { Consume(TokenType.NULL, "Expected NULL after NOT"); notNull = true; }
                columns.Add(new ExpectedSchemaColumn { ColumnName = colName, DataType = dataType, NotNull = notNull });
                Match(TokenType.COMMA);
            }
            Consume(TokenType.RPAREN, "Expected ')' to close EXPECT SCHEMA column list");
        }
        else if (Match(TokenType.FROM))
        {
            schemaPath = Consume(TokenType.STRING_LITERAL, "Expected JSON specification file path after FROM").Value;
        }
        else
        {
            throw new SyntaxException("Expected '(' or 'FROM' after target name in EXPECT SCHEMA", _parser.Current.Line, _parser.Current.Column);
        }

        bool warnOnDrift = false;
        if (Match(TokenType.ON))
        {
            if (_parser.Current.Type == TokenType.IDENTIFIER &&
                _parser.Current.Value.Equals("DRIFT", StringComparison.OrdinalIgnoreCase))
            {
                Advance();
                if (_parser.Current.Type == TokenType.IDENTIFIER &&
                    _parser.Current.Value.Equals("WARN", StringComparison.OrdinalIgnoreCase))
                {
                    Advance();
                    warnOnDrift = true;
                }
            }
        }

        Match(TokenType.SEMICOLON);
        return new ExpectSchemaStatement
        {
            Target = target,
            Columns = columns,
            SchemaPath = schemaPath,
            WarnOnDrift = warnOnDrift,
            Line = startToken.Line,
            Column = startToken.Column
        };
    }
}
