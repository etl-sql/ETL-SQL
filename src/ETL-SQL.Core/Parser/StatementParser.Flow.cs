using System;
using System.Collections.Generic;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Core.Parser
{
    public partial class StatementParser
    {
        private BlockStatement ParseBlock()
        {
            var stmts = new List<Statement>();
            while (_parser.Current.Type != TokenType.END && _parser.Current.Type != TokenType.EOF)
            {
                stmts.Add(_parser.ParseStatement());
            }
            _parser.Consume(TokenType.END, "Expected END to close BEGIN block");
            
            if (_parser.Current.Type == TokenType.TRY || _parser.Current.Type == TokenType.CATCH) _parser.Advance();

            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
            return new BlockStatement(stmts);
        }

        private Statement ParseTryCatch()
        {
            var tryBody = ParseBlock();
            
            _parser.Consume(TokenType.BEGIN, "Expected BEGIN after END TRY");
            _parser.Consume(TokenType.CATCH, "Expected CATCH after BEGIN");
            
            var catchBody = ParseBlock();
            
            return new TryCatchStatement(tryBody, catchBody);
        }

        private Statement ParseIf()
        {
            var startToken = _parser.Previous; // IF already consumed
            var condition = _parser.ParseExpression();
            var ifBody = _parser.ParseStatement();

            List<ElseIfClause>? elseIfClauses = null;
            Statement? elseBody = null;

            while (_parser.Match(TokenType.ELSE))
            {
                if (_parser.Match(TokenType.IF))
                {
                    var elseIfCondition = _parser.ParseExpression();
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

        private Statement ParseWhile()
        {
            var startToken = _parser.Previous; // WHILE already consumed
            var condition = _parser.ParseExpression();
            var body = _parser.ParseStatement();
            return new WhileStatement(condition, body)
            {
                Line = startToken.Line,
                Column = startToken.Column,
                EndLine = _parser.LastTokenEndLine,
                EndColumn = _parser.LastTokenEndColumn
            };
        }

        private Statement ParseFor()
        {
            var startToken = _parser.Previous; // FOR already consumed
            var varToken = _parser.Consume(TokenType.VARIABLE, "Expected variable name starting with '@' for FOR loop");
            _parser.Consume(TokenType.EQUALS, "Expected '=' in FOR loop assignment");
            var startExpr = _parser.ParseExpression();
            _parser.Consume(TokenType.TO, "Expected TO in FOR loop limits");
            var endExpr = _parser.ParseExpression();
            
            Expression? stepExpr = null;
            if (_parser.Match(TokenType.STEP))
            {
                stepExpr = _parser.ParseExpression();
            }

            var body = _parser.ParseStatement();
            return new ForStatement(varToken.Value, startExpr, endExpr, stepExpr, body)
            {
                Line = startToken.Line,
                Column = startToken.Column,
                EndLine = _parser.LastTokenEndLine,
                EndColumn = _parser.LastTokenEndColumn
            };
        }

        private Statement ParseForeach()
        {
            var startToken = _parser.Previous; // FOREACH already consumed
            var varToken = _parser.Consume(TokenType.VARIABLE, "Expected variable name starting with '@' for FOREACH loop");
            _parser.Consume(TokenType.IN, "Expected IN for FOREACH loop parameter");
            var listExpr = _parser.ParseExpression();
            
            var body = _parser.ParseStatement();
            return new ForeachStatement(varToken.Value, listExpr, body)
            {
                Line = startToken.Line,
                Column = startToken.Column,
                EndLine = _parser.LastTokenEndLine,
                EndColumn = _parser.LastTokenEndColumn
            };
        }

        private Statement ParseParallel()
        {
            var startToken = _parser.Previous;
            int concurrencyLimit = 0;
            if (_parser.Match(TokenType.LPAREN))
            {
                var limitToken = _parser.Consume(TokenType.NUMBER, "Expected concurrency limit number after '('");
                concurrencyLimit = int.Parse(limitToken.Value);
                _parser.Consume(TokenType.RPAREN, "Expected ')' after concurrency limit");
            }
            _parser.Consume(TokenType.BEGIN, "Expected BEGIN after PARALLEL");
            var body = ParseBlock(); // This consumes END
            return new ParallelStatement(body, concurrencyLimit) { Line = startToken.Line, Column = startToken.Column };
        }

        private Statement ParseReturn()
        {
            Expression? returnValue = null;
            if (_parser.Current.Type != TokenType.SEMICOLON && _parser.Current.Type != TokenType.EOF && _parser.Current.Type != TokenType.END && _parser.Current.Type != TokenType.CATCH)
            {
                returnValue = _parser.ParseExpression();
            }

            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
            return new ReturnStatement(returnValue);
        }

        private Statement ParseBreak()
        {
            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
            return new BreakStatement();
        }

        private Statement ParseContinue()
        {
            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
            return new ContinueStatement();
        }

        private Statement ParseRaiseError()
        {
            _parser.Consume(TokenType.LPAREN, "Expected '(' after RAISEERROR");
            var message = _parser.ParseExpression();
            _parser.Consume(TokenType.COMMA, "Expected severity after RAISEERROR message");
            var severity = _parser.ParseExpression();
            
            Expression? location = null;
            List<Expression>? parameters = null;

            if (_parser.Match(TokenType.COMMA))
            {
                location = _parser.ParseExpression();
                while (_parser.Match(TokenType.COMMA))
                {
                    if (parameters == null) parameters = new List<Expression>();
                    parameters.Add(_parser.ParseExpression());
                }
            }

            _parser.Consume(TokenType.RPAREN, "Expected ')' after RAISEERROR arguments");
            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();

            return new RaiseErrorStatement(message, severity, location, parameters);
        }

        private Statement ParseThrow()
        {
            Expression? errorNumber = null;
            Expression? message = null;
            Expression? state = null;

            if (_parser.Current.Type != TokenType.SEMICOLON && _parser.Current.Type != TokenType.EOF && _parser.Current.Type != TokenType.END && _parser.Current.Type != TokenType.CATCH)
            {
                // SQL Server THROW syntax: THROW [ (error_number, message, state) ]
                // It can be a bare expression (old behavior) or 3 comma-separated expressions.
                
                errorNumber = _parser.ParseExpression();

                if (_parser.Match(TokenType.COMMA))
                {
                    message = _parser.ParseExpression();
                    _parser.Consume(TokenType.COMMA, "Expected comma after THROW message expression");
                    state = _parser.ParseExpression();
                }
                else
                {
                    // If only one expression is provided, we treat it as the message for backward compatibility/simplicity
                    // though strictly T-SQL requires 0 or 3.
                    message = errorNumber;
                    errorNumber = null;
                }
            }
            
            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
            return new ThrowStatement(errorNumber, message, state);
        }

        private Statement ParseAssert()
        {
            var startToken = _parser.Previous; // ASSERT already consumed
            var condition = _parser.ParseExpression();
            Expression? message = null;

            if (_parser.Match(TokenType.COMMA))
            {
                message = _parser.ParseExpression();
            }

            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();

            return new AssertStatement(condition, message)
            {
                Line = startToken.Line,
                Column = startToken.Column
            };
        }

        private Statement ParseExpectSchema()
        {
            var startToken = _parser.Previous; // EXPECT already consumed
            _parser.Consume(TokenType.SCHEMA, "Expected SCHEMA after EXPECT");

            var target = _parser.ConsumeIdentifier("Expected table or connection name after EXPECT SCHEMA").Value;
            if (!target.StartsWith("#") && _parser.Current.Type == TokenType.IDENTIFIER)
            {
                // allow INFORMATION_SCHEMA-style dot notation if ever needed — noop for now
            }

            _parser.Consume(TokenType.LPAREN, "Expected '(' after target name in EXPECT SCHEMA");

            var columns = new List<ExpectedSchemaColumn>();
            while (_parser.Current.Type != TokenType.RPAREN && _parser.Current.Type != TokenType.EOF)
            {
                var colName = _parser.ConsumeIdentifier("Expected column name").Value;

                // Parse data type — mirrors CREATE TABLE column parsing
                string dataType = "VARCHAR";
                if (_parser.IsIdentifier(_parser.Current))
                {
                    dataType = _parser.Advance().Value;
                    if (_parser.Match(TokenType.LPAREN))
                    {
                        dataType += "(" + _parser.Consume(TokenType.NUMBER, "Expected length").Value;
                        if (_parser.Match(TokenType.COMMA))
                            dataType += "," + _parser.Consume(TokenType.NUMBER, "Expected scale").Value;
                        dataType += ")";
                        _parser.Consume(TokenType.RPAREN, "Expected ')' after type length");
                    }
                }

                bool notNull = false;
                if (_parser.Match(TokenType.NOT))
                {
                    _parser.Consume(TokenType.NULL, "Expected NULL after NOT in column definition");
                    notNull = true;
                }

                columns.Add(new ExpectedSchemaColumn { ColumnName = colName, DataType = dataType, NotNull = notNull });
                _parser.Match(TokenType.COMMA);
            }
            _parser.Consume(TokenType.RPAREN, "Expected ')' to close EXPECT SCHEMA column list");

            // Optional: ON DRIFT WARN
            bool warnOnDrift = false;
            if (_parser.Match(TokenType.ON))
            {
                if (_parser.Current.Type == TokenType.IDENTIFIER &&
                    _parser.Current.Value.Equals("DRIFT", StringComparison.OrdinalIgnoreCase))
                {
                    _parser.Advance(); // consume DRIFT
                    if (_parser.Current.Type == TokenType.IDENTIFIER &&
                        _parser.Current.Value.Equals("WARN", StringComparison.OrdinalIgnoreCase))
                    {
                        _parser.Advance(); // consume WARN
                        warnOnDrift = true;
                    }
                }
            }

            _parser.Match(TokenType.SEMICOLON);

            return new ExpectSchemaStatement
            {
                Target      = target,
                Columns     = columns,
                WarnOnDrift = warnOnDrift,
                Line        = startToken.Line,
                Column      = startToken.Column
            };
        }
    }
}
