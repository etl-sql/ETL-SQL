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
            Expression? message = null;
            if (_parser.Current.Type != TokenType.SEMICOLON && _parser.Current.Type != TokenType.EOF && _parser.Current.Type != TokenType.END && _parser.Current.Type != TokenType.CATCH)
            {
                message = _parser.ParseExpression();
            }
            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
            return new ThrowStatement(message);
        }
    }
}
