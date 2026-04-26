using System;
using System.Collections.Generic;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Core.Parser.Components
{
    public class FlowParser : ParserComponent
    {
        public FlowParser(IParser parser, StatementParser parent) : base(parser, parent) { }

        public BlockStatement ParseBlock()
        {
            var stmts = new List<Statement>();
            while (_parser.Current.Type != TokenType.END && _parser.Current.Type != TokenType.EOF)
                stmts.Add(_parser.ParseStatement());
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
            Consume(TokenType.EQUALS, "Expected '=' in FOR loop assignment");
            var startExpr = ParseExpression();
            Consume(TokenType.TO, "Expected TO in FOR loop limits");
            var endExpr = ParseExpression();
            Expression? stepExpr = null;
            if (Match(TokenType.STEP)) stepExpr = ParseExpression();
            var body = _parser.ParseStatement();
            return new ForStatement(varToken.Value, startExpr, endExpr, stepExpr, body)
            {
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
            var condition = ParseExpression();
            Expression? message = null;
            if (Match(TokenType.COMMA)) message = ParseExpression();
            if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
            return new AssertStatement(condition, message) { Line = startToken.Line, Column = startToken.Column };
        }

        public Statement ParseExpectSchema(Token startToken)
        {
            Consume(TokenType.SCHEMA, "Expected SCHEMA after EXPECT");
            var target = ConsumeIdentifier("Expected table or connection name after EXPECT SCHEMA").Value;

            Consume(TokenType.LPAREN, "Expected '(' after target name in EXPECT SCHEMA");

            var columns = new List<ExpectedSchemaColumn>();
            while (_parser.Current.Type != TokenType.RPAREN && _parser.Current.Type != TokenType.EOF)
            {
                var colName  = ConsumeIdentifier("Expected column name").Value;
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
                Target      = target,
                Columns     = columns,
                WarnOnDrift = warnOnDrift,
                Line        = startToken.Line,
                Column      = startToken.Column
            };
        }
    }
}
