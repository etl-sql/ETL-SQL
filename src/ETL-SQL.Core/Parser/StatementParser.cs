using System;
using System.Collections.Generic;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Core.Parser
{
    /// <summary>
    /// Recursive descent parser for statements in the ETL-SQL language.
    /// Handles everything from SELECT/INSERT to DOCKER and SEND_EMAIL.
    /// </summary>
    public partial class StatementParser
    {
        private readonly IParser _parser;

        /// <summary>
        /// Initializes a new instance of the <see cref="StatementParser"/> class.
        /// </summary>
        /// <param name="parser">The parent parser instance for token access.</param>
        public StatementParser(IParser parser)
        {
            _parser = parser;
        }

        /// <summary>
        /// Parses a single statement from the current token stream.
        /// Identifies the statement type by matching keywords (CREATE, SELECT, IF, etc.).
        /// </summary>
        /// <returns>A <see cref="Statement"/> object representing the parsed structure.</returns>
        public Statement ParseStatement()
        {
            if (_parser.Match(TokenType.WITH)) return ParseStatementWithCte();
            if (_parser.Match(TokenType.CREATE)) return ParseCreate();
            if (_parser.Match(TokenType.ALTER)) return ParseAlter();
            if (_parser.Match(TokenType.EXPLAIN)) return ParseExplain();
            if (_parser.Match(TokenType.DROP)) return ParseDrop();
            if (_parser.Match(TokenType.TRUNCATE)) return ParseTruncate();
            if (_parser.Match(TokenType.DELETE)) return ParseDelete();
            if (_parser.Match(TokenType.DECLARE)) return ParseDeclare();
            if (_parser.Match(TokenType.RUN)) return ParseRun();
            if (_parser.Match(TokenType.SET)) 
            {
                if (_parser.Match(TokenType.PROFILING) || _parser.Match(TokenType.PROFILE)) return ParseSetProfiling();
                return ParseSetVariable();
            }
            if (_parser.Match(TokenType.SHOW)) return ParseShow();
            if (_parser.Match(TokenType.BEGIN))
            {
                if (_parser.Match(TokenType.TRY)) return ParseTryCatch();
                if (_parser.Match(TokenType.TRANSACTION) || _parser.Match(TokenType.TRAN)) return ParseBeginTransaction();
                return ParseBlock();
            }
            if (_parser.Match(TokenType.COMMIT)) return ParseCommitTransaction();
            if (_parser.Match(TokenType.ROLLBACK)) return ParseRollbackTransaction();
            if (_parser.Match(TokenType.IF)) return ParseIf();
            if (_parser.Match(TokenType.WHILE)) return ParseWhile();
            if (_parser.Match(TokenType.FOR))
            {
                if (_parser.Match(TokenType.EACH)) return ParseForeach();
                return ParseFor();
            }
            if (_parser.Match(TokenType.FOREACH)) return ParseForeach();
            if (_parser.Match(TokenType.SELECT)) { _parser.Backtrack(); return _parser.ParseQuery(); }
            if (_parser.Match(TokenType.INSERT)) return ParseInsert();
            if (_parser.Match(TokenType.UPDATE)) return ParseUpdate();
            if (_parser.Match(TokenType.MERGE)) return ParseMerge();
            if (_parser.Match(TokenType.PRINT)) return ParsePrint();
            if (_parser.Match(TokenType.WAITFOR)) return ParseWaitFor();
            if (_parser.Match(TokenType.RAISEERROR)) return ParseRaiseError();
            if (_parser.Match(TokenType.EXEC) || _parser.Match(TokenType.EXECUTE)) return ParseExecute();
            if (_parser.Match(TokenType.PARALLEL)) return ParseParallel();
            if (_parser.Match(TokenType.THROW)) return ParseThrow();
            if (_parser.Match(TokenType.RETURN)) return ParseReturn();
            if (_parser.Match(TokenType.BREAK)) return ParseBreak();
            if (_parser.Match(TokenType.CONTINUE)) return ParseContinue();
            if (_parser.Match(TokenType.HELP)) return ParseHelp();
            if (_parser.Match(TokenType.USE)) return ParseUse();
            if (_parser.Match(TokenType.BULK)) return ParseBulkInsert();
            if (_parser.Match(TokenType.LINEAGE)) return ParseLineage();
            if (_parser.Match(TokenType.SEND_EMAIL)) return ParseSendEmail();
            if (_parser.Match(TokenType.SEND_FILE)) return ParseFileTransfer(FileTransferType.Send);
            if (_parser.Match(TokenType.RECEIVE_FILE)) return ParseFileTransfer(FileTransferType.Receive);
            if (_parser.Match(TokenType.START_DOCKER)) return ParseDockerAction(DockerAction.Start);
            if (_parser.Match(TokenType.STOP_DOCKER)) return ParseDockerAction(DockerAction.Stop);
            if (_parser.Match(TokenType.PAUSE_DOCKER)) return ParseDockerAction(DockerAction.Pause);
            if (_parser.Match(TokenType.CLOSE_DOCKER)) return ParseDockerClose();
            if (_parser.Match(TokenType.DOCKER)) return ParseDocker(); // Support legacy or generic DOCKER
            if (_parser.Match(TokenType.LINT)) return ParseLint();
            
            // Legacy/Contextual Docker support for <alias> START, etc.
            if (_parser.Current.Type == TokenType.IDENTIFIER)
            {
                var alias = _parser.Advance().Value;
                if (_parser.Match(TokenType.CLOSE))
                {
                    if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
                    return new DockerActionStatement(alias, DockerAction.Close);
                }
                if (_parser.Match(TokenType.STOP))
                {
                    if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
                    return new DockerActionStatement(alias, DockerAction.Stop);
                }
                if (_parser.Match(TokenType.START))
                {
                    if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
                    return new DockerActionStatement(alias, DockerAction.Start);
                }
                if (_parser.Match(TokenType.PAUSE))
                {
                    if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
                    return new DockerActionStatement(alias, DockerAction.Pause);
                }
                _parser.Backtrack(); // Not a Docker action
            }
            
            if (_parser.Current.Type == TokenType.COPY_FILE || _parser.Current.Type == TokenType.MOVE_FILE || 
                _parser.Current.Type == TokenType.RENAME_FILE || _parser.Current.Type == TokenType.DELETE_FILE || 
                _parser.Current.Type == TokenType.COMPRESS_FILE || _parser.Current.Type == TokenType.ENCRYPT_FILE ||
                _parser.Current.Type == TokenType.DECRYPT_FILE)
                return ParseFileOperation();

            if (_parser.Current.Type == TokenType.CREATE_DIRECTORY || _parser.Current.Type == TokenType.DELETE_DIRECTORY || 
                _parser.Current.Type == TokenType.RENAME_DIRECTORY || _parser.Current.Type == TokenType.MOVE_DIRECTORY ||
                _parser.Current.Type == TokenType.COPY_DIRECTORY || _parser.Current.Type == TokenType.DELETE_DIRECTORY_CONTENTS)
                return ParseDirectoryOperation();

            throw new SyntaxException($"Unexpected token {_parser.Current.Type} ('{_parser.Current.Value}')", _parser.Current.Line, _parser.Current.Column);
        }

        private Statement ParseHelp()
        {
            string? topic = null;
            string? subTopic = null;

            if (_parser.Current.Type == TokenType.IDENTIFIER || IsContextualKeyword(_parser.Current.Type))
            {
                topic = _parser.Advance().Value;
                if (_parser.Current.Type == TokenType.IDENTIFIER || IsContextualKeyword(_parser.Current.Type))
                {
                    subTopic = _parser.Advance().Value;
                }
            }

            _parser.Match(TokenType.SEMICOLON);
            return new HelpStatement(topic, subTopic);
        }


        private Statement ParseUse()
        {
            var startToken = _parser.Previous;
            if (_parser.Match(TokenType.DOCKER))
            {
                _parser.Consume(TokenType.LPAREN, "Expected '(' after DOCKER");
                var imageName = _parser.ParseExpression();
                _parser.Consume(TokenType.RPAREN, "Expected ')' after image name");
                
                string? alias = null;
                if (_parser.Match(TokenType.AS))
                {
                    alias = _parser.ConsumeIdentifier("Expected alias after AS").Value;
                }

                if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
                return new DockerStatement(imageName, alias) { Line = startToken.Line, Column = startToken.Column };
            }
            throw new SyntaxException("Expected DOCKER after USE", _parser.Current.Line, _parser.Current.Column);
        }

        private bool IsContextualKeyword(TokenType type)
        {
            return type == TokenType.CONNECTION || type == TokenType.FUNCTION || type == TokenType.PROCEDURE || type == TokenType.TABLE ||
                   type == TokenType.FILE || type == TokenType.JSON || type == TokenType.XML || type == TokenType.EXCEL ||
                   type == TokenType.MSSQL || type == TokenType.ORACLE || type == TokenType.POSTGRES || type == TokenType.MOCKDB;
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

        private Statement ParseFileOperation()
        {
            var startToken = _parser.Advance();
            FileOpType type = startToken.Type switch
            {
                TokenType.COPY_FILE => FileOpType.Copy,
                TokenType.MOVE_FILE => FileOpType.Move,
                TokenType.RENAME_FILE => FileOpType.Rename,
                TokenType.DELETE_FILE => FileOpType.Delete,
                TokenType.COMPRESS_FILE => FileOpType.Compress,
                TokenType.ENCRYPT_FILE => FileOpType.Encrypt,
                TokenType.DECRYPT_FILE => FileOpType.Decrypt,
                _ => throw new SyntaxException($"Unexpected file operation: {startToken.Type}", startToken.Line, startToken.Column)
            };

            _parser.Consume(TokenType.LPAREN, "Expected '(' after file operation");
            var source = _parser.ParseExpression();
            Expression? dest = null;
            if (_parser.Match(TokenType.COMMA))
            {
                dest = _parser.ParseExpression();
            }
            _parser.Consume(TokenType.RPAREN, "Expected ')' after arguments");
            _parser.Match(TokenType.SEMICOLON);

            return new FileOperationStatement(type, source, dest) { Line = startToken.Line, Column = startToken.Column };
        }

        private Statement ParseDirectoryOperation()
        {
            var startToken = _parser.Advance();
            DirectoryOpType type = startToken.Type switch
            {
                TokenType.CREATE_DIRECTORY => DirectoryOpType.Create,
                TokenType.DELETE_DIRECTORY => DirectoryOpType.Delete,
                TokenType.RENAME_DIRECTORY => DirectoryOpType.Rename,
                TokenType.MOVE_DIRECTORY => DirectoryOpType.Move,
                TokenType.COPY_DIRECTORY => DirectoryOpType.Copy,
                TokenType.DELETE_DIRECTORY_CONTENTS => DirectoryOpType.DeleteContents,
                _ => throw new SyntaxException($"Unexpected directory operation: {startToken.Type}", startToken.Line, startToken.Column)
            };

            _parser.Consume(TokenType.LPAREN, "Expected '(' after directory operation");
            var path = _parser.ParseExpression();
            Expression? extra = null;
            if (_parser.Match(TokenType.COMMA))
            {
                extra = _parser.ParseExpression();
            }
            _parser.Consume(TokenType.RPAREN, "Expected ')' after arguments");
            _parser.Match(TokenType.SEMICOLON);

            return new DirectoryOperationStatement(type, path, extra) { Line = startToken.Line, Column = startToken.Column };
        }

        private Statement ParseBeginTransaction()
        {
            string? name = null;
            if (_parser.Current.Type == TokenType.IDENTIFIER) name = _parser.Advance().Value;
            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
            return new BeginTransactionStatement(name);
        }

        private Statement ParseCommitTransaction()
        {
            if (_parser.Match(TokenType.TRANSACTION) || _parser.Match(TokenType.TRAN)) { }
            string? name = null;
            if (_parser.Current.Type == TokenType.IDENTIFIER) name = _parser.Advance().Value;
            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
            return new CommitTransactionStatement(name);
        }

        private Statement ParseRollbackTransaction()
        {
            if (_parser.Match(TokenType.TRANSACTION) || _parser.Match(TokenType.TRAN)) { }
            string? name = null;
            if (_parser.Current.Type == TokenType.IDENTIFIER) name = _parser.Advance().Value;
            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
            return new RollbackTransactionStatement(name);
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

        private Statement ParsePrint()
        {
            bool hasParen = _parser.Match(TokenType.LPAREN);
            var message = _parser.ParseExpression();
            Expression? showTimestamp = null;
            Expression? format = null;

            if (_parser.Match(TokenType.COMMA))
            {
                showTimestamp = _parser.ParseExpression();
                if (_parser.Match(TokenType.COMMA))
                {
                    format = _parser.ParseExpression();
                }
            }

            if (hasParen)
            {
                _parser.Consume(TokenType.RPAREN, "Expected ')' after PRINT arguments");
            }

            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();

            return new PrintStatement(message, showTimestamp, format);
        }

        private Statement ParseWaitFor()
        {
            var startToken = _parser.Previous;
            _parser.Consume(TokenType.DELAY, "Expected DELAY after WAITFOR");
            var delayExpr = _parser.ParseExpression();
            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
            return new WaitForStatement(delayExpr) { Line = startToken.Line, Column = startToken.Column };
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

        private Statement ParseExecute()
        {
            var startToken = _parser.Current;
            if (_parser.Match(TokenType.LPAREN))
            {
                // Check if it's a block or an expression
                if (IsStatementStart(_parser.Current.Type))
                {
                    var statements = new List<Statement>();
                    while (_parser.Current.Type != TokenType.RPAREN && _parser.Current.Type != TokenType.EOF)
                    {
                        statements.Add(ParseStatement());
                    }
                    _parser.Consume(TokenType.RPAREN, "Expected ')' after EXEC SQL block");
                    
                    Expression? blockConnName = null;
                    if (_parser.Match(TokenType.AT))
                    {
                        blockConnName = _parser.ParseExpression();
                    }
                    
                    if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
                    
                    if (blockConnName == null)
                    {
                         // If no AT, it's just a local block? Unusual for EXECUTE (...) so maybe error or just block
                         return new BlockStatement(statements);
                    }
                    return new ExecuteRemoteBlockStatement(blockConnName, new BlockStatement(statements));
                }
                else
                {
                    var sqlExpr = _parser.ParseExpression();
                    _parser.Consume(TokenType.RPAREN, "Expected ')' after EXEC SQL expression");
                    
                    Expression? execConnName = null;
                    if (_parser.Match(TokenType.AT))
                    {
                        execConnName = _parser.ParseExpression();
                    }

                    TableReference? execIntoTable = null;
                    if (_parser.Match(TokenType.INTO))
                    {
                        execIntoTable = ParseTableReference(false);
                    }

                    List<Expression>? execParameters = null;
                    if (_parser.Match(TokenType.WITH))
                    {
                        _parser.Consume(TokenType.LPAREN, "Expected '(' after WITH");
                        execParameters = new List<Expression>();
                        if (_parser.Current.Type != TokenType.RPAREN)
                        {
                            execParameters.Add(_parser.ParseExpression());
                            while (_parser.Match(TokenType.COMMA))
                            {
                                execParameters.Add(_parser.ParseExpression());
                            }
                        }
                        _parser.Consume(TokenType.RPAREN, "Expected ')' after WITH parameters");
                    }

                    if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
                    return new ExecStatement(sqlExpr, execConnName, execIntoTable, execParameters);
                }
            }

            var identifierExpr = _parser.ParseExpression(); // Could be connection name or proc name
            
            TableReference? remoteIntoTable = null;
            if (_parser.Match(TokenType.INTO))
            {
                remoteIntoTable = ParseTableReference(false);
            }

            List<Expression>? remoteParameters = null;
            if (_parser.Match(TokenType.WITH))
            {
                _parser.Consume(TokenType.LPAREN, "Expected '(' after WITH");
                remoteParameters = new List<Expression>();
                if (_parser.Current.Type != TokenType.RPAREN)
                {
                    remoteParameters.Add(_parser.ParseExpression());
                    while (_parser.Match(TokenType.COMMA))
                    {
                        remoteParameters.Add(_parser.ParseExpression());
                    }
                }
                _parser.Consume(TokenType.RPAREN, "Expected ')' after WITH parameters");
            }

            if (_parser.Current.Type == TokenType.BEGIN)
            {
                // Native SQL pushdown
                bool unbalanced = false;
                string sqlText = "";
                try
                {
                    sqlText = _parser.CaptureRawBlock();
                }
                catch (SyntaxException ex)
                {
                    Console.Error.WriteLine($"LSP: {ex.Message}");
                    unbalanced = true;
                }

                if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
                return new ExecutePushdownStatement(identifierExpr, sqlText, remoteIntoTable, remoteParameters) { Line = startToken.Line, Column = startToken.Column, HasUnbalancedBlocks = unbalanced };
            }

            // Single statement remote execution: EXECUTE c CREATE TABLE...
            // If the next token is a known statement start (and it's not a comma/semicolon)
            if (IsStatementStart(_parser.Current.Type))
            {
                var stmt = ParseStatement();
                // We wrap this in ExecuteRemoteBlockStatement for backwards compatibility if it's a parsed statement
                var block = new BlockStatement(new List<Statement> { stmt });
                return new ExecuteRemoteBlockStatement(identifierExpr, block);
            }

            // Procedure/Script call: EXECUTE 'path' @p1 INPUT, @p2 OUTPUT
            string procedureName = identifierExpr.ToSql();
            // Clean up quotes if it's a string literal
            if (identifierExpr is LiteralExpression lit && lit.Value is string s) procedureName = s;

            var procParameters = new List<ExecuteParameter>();
            if (_parser.Current.Type != TokenType.SEMICOLON && _parser.Current.Type != TokenType.EOF && _parser.Current.Type != TokenType.END)
            {
                procParameters.Add(ParseExecuteParameter());
                while (_parser.Match(TokenType.COMMA))
                {
                    procParameters.Add(ParseExecuteParameter());
                }
            }

            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
            return new ExecuteStatement(procedureName, procParameters);
        }

        private ExecuteParameter ParseExecuteParameter()
        {
            var expr = _parser.ParseExpression();
            bool isOutput = _parser.Match(TokenType.OUTPUT);
            bool isInput = _parser.Match(TokenType.INPUT);
            return new ExecuteParameter(expr, isOutput, isInput);
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

        private Statement ParseCreate()
        {
            // We need a way to get the token BEFORE the current one if it was just matched.
            // I'll use _parser.Previous.
            var startToken = _parser.Previous; 
            bool orAlter = false;
            if (_parser.Match(TokenType.OR))
            {
                _parser.Consume(TokenType.ALTER, "Expected ALTER after CREATE OR");
                orAlter = true;
            }
            var mode = orAlter ? ObjectCreationMode.CreateOrAlter : ObjectCreationMode.Create;

            if (_parser.Match(TokenType.CONNECTION)) return ParseCreateConnection(startToken, mode);
            if (_parser.Match(TokenType.TABLE)) 
            {
                if (orAlter) throw new SyntaxException("CREATE OR ALTER is not supported for TABLE.", _parser.Current.Line, _parser.Current.Column);
                return ParseCreateTable(startToken);
            }
            if (_parser.Match(TokenType.PROCEDURE)) return ParseCreateProcedure(startToken, mode);
            if (_parser.Match(TokenType.FUNCTION)) return ParseCreateFunction(startToken, mode);
            if (_parser.Match(TokenType.JOB))
            {
                if (orAlter) throw new SyntaxException("CREATE OR ALTER is not supported for JOB. Use DROP and CREATE.", _parser.Current.Line, _parser.Current.Column);
                return ParseCreateJob(startToken);
            }
            
            if (_parser.Current.Type == TokenType.UNIQUE || _parser.Current.Type == TokenType.INDEX)
            {
                if (orAlter) throw new SyntaxException("CREATE OR ALTER is not supported for INDEX.", _parser.Current.Line, _parser.Current.Column);
                bool isUnique = _parser.Match(TokenType.UNIQUE);
                return ParseCreateIndex(startToken, isUnique);
            }
            throw new SyntaxException("Expected CONNECTION, TABLE, PROCEDURE, FUNCTION or INDEX after CREATE", _parser.Current.Line, _parser.Current.Column);
        }

        private Statement ParseAlter()
        {
            var startToken = _parser.Previous;
            var mode = ObjectCreationMode.Alter;

            if (_parser.Match(TokenType.CONNECTION)) return ParseCreateConnection(startToken, mode);
            if (_parser.Match(TokenType.PROCEDURE)) return ParseCreateProcedure(startToken, mode);
            if (_parser.Match(TokenType.FUNCTION)) return ParseCreateFunction(startToken, mode);

            if (_parser.Match(TokenType.TABLE)) return ParseAlterTable(startToken);
            throw new SyntaxException("Expected CONNECTION, PROCEDURE, FUNCTION, or TABLE after ALTER", _parser.Current.Line, _parser.Current.Column);
        }

        private Statement ParseCreateProcedure(Token startToken, ObjectCreationMode mode = ObjectCreationMode.Create)
        {
            var name = _parser.ConsumeIdentifier("Expected procedure name").Value;
            var parameters = ParseParameterDefinitions();
            
            _parser.Consume(TokenType.AS, "Expected 'AS' after procedure definition");
            var body = _parser.ParseStatement();
            
            if (_parser.Match(TokenType.SEMICOLON)) { /* skipped */ }

            return new CreateProcedureStatement(name, parameters, body, mode) { Line = startToken.Line, Column = startToken.Column };
        }

        private Statement ParseCreateFunction(Token startToken, ObjectCreationMode mode = ObjectCreationMode.Create)
        {
            var name = _parser.ConsumeIdentifier("Expected function name").Value;
            var parameters = ParseParameterDefinitions();
            
            _parser.Consume(TokenType.RETURNS, "Expected 'RETURNS' after function definition");
            var returnType = _parser.ParseType();
            
            _parser.Consume(TokenType.AS, "Expected 'AS' after function return type");
            var body = _parser.ParseStatement();
            
            if (_parser.Match(TokenType.SEMICOLON)) { /* skipped */ }

            return new CreateFunctionStatement(name, parameters, returnType, body, mode) { Line = startToken.Line, Column = startToken.Column };
        }

        private Statement ParseAlterTable(Token startToken)
        {
            var targetTable = ParseTableReference();
            AlterTableActionType action;
            ColumnDefinition? newColumn = null;
            string? columnToDelete = null;
            string? oldColumnName = null;
            string? newColumnName = null;

            if (_parser.Match(TokenType.ADD))
            {
                action = AlterTableActionType.ADD;
                var colName = _parser.ConsumeIdentifier("Expected column name").Value;
                string dataType = "NVARCHAR(MAX)";
                if (_parser.IsIdentifier(_parser.Current))
                {
                    dataType = _parser.Advance().Value;
                    if (_parser.Match(TokenType.LPAREN))
                    {
                        dataType += "(" + _parser.Consume(TokenType.NUMBER, "Expected length").Value + ")";
                        _parser.Consume(TokenType.RPAREN, "Expected ')'");
                    }
                }
                
                Expression? defaultExpr = null;
                if (_parser.Match(TokenType.DEFAULT))
                {
                    defaultExpr = _parser.ParseExpression();
                }
                
                newColumn = new ColumnDefinition(colName, dataType, false, defaultExpr);
            }
            else if (_parser.Match(TokenType.DROP))
            {
                _parser.Match(TokenType.COLUMN); // Optional COLUMN keyword
                action = AlterTableActionType.DROP_COLUMN;
                columnToDelete = _parser.ConsumeIdentifier("Expected column name to drop").Value;
            }
            else if (_parser.Match(TokenType.RENAME))
            {
                _parser.Match(TokenType.COLUMN); // Optional COLUMN keyword
                action = AlterTableActionType.RENAME_COLUMN;
                oldColumnName = _parser.ConsumeIdentifier("Expected column name to rename").Value;
                _parser.Consume(TokenType.TO, "Expected 'TO' after old column name");
                newColumnName = _parser.ConsumeIdentifier("Expected new column name").Value;
            }
            else
            {
                throw new SyntaxException("Expected ADD, DROP, or RENAME after ALTER TABLE", _parser.Current.Line, _parser.Current.Column);
            }

            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();

            return new AlterTableStatement(targetTable, action, newColumn, columnToDelete, oldColumnName, newColumnName)
            {
                Line = startToken.Line,
                Column = startToken.Column
            };
        }

        private List<ParameterDefinition> ParseParameterDefinitions()
        {
            var parameters = new List<ParameterDefinition>();
            if (_parser.Match(TokenType.LPAREN))
            {
                if (_parser.Current.Type != TokenType.RPAREN)
                {
                    do
                    {
                        var pName = _parser.Consume(TokenType.VARIABLE, "Expected parameter name starting with '@'").Value;
                        var pType = _parser.ParseType();
                        parameters.Add(new ParameterDefinition(pName, pType));
                    } while (_parser.Match(TokenType.COMMA));
                }
                _parser.Consume(TokenType.RPAREN, "Expected ')' after parameter list");
            }
            return parameters;
        }

        private Statement ParseCreateConnection(Token startToken, ObjectCreationMode mode)
        {
            var name = _parser.ConsumeIdentifier("Expected connection name").Value;

            if (_parser.Match(TokenType.ON)) { /* ON is optional */ }

            string connectionType;
            Expression target;

            if (_parser.Match(TokenType.TYPE))
            {
                connectionType = _parser.Advance().Value;
                _parser.Consume(TokenType.TARGET, "Expected TARGET after connection type");
                target = _parser.ParseExpression();
            }
            else
            {
                var typeToken = _parser.Advance();
                connectionType = typeToken.Value;
                
                if (typeToken.Type == TokenType.FILE)
                {
                    throw new SyntaxException("Connection type 'FILE' is deprecated. Please use 'FLATFILE' instead.", typeToken.Line, typeToken.Column);
                }

                bool hasParen = _parser.Match(TokenType.LPAREN);
                if (hasParen && _parser.Current.Type == TokenType.RPAREN)
                {
                    target = new LiteralExpression("", TokenType.STRING);
                }
                else
                {
                    target = _parser.ParseExpression();
                }
                if (hasParen) _parser.Consume(TokenType.RPAREN, "Expected ')' after target string");
            }

            Dictionary<string, string>? options = null;
            if (_parser.Match(TokenType.WITH))
            {
                options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _parser.Consume(TokenType.LPAREN, "Expected '(' after WITH clause");
                while (_parser.Current.Type != TokenType.RPAREN && _parser.Current.Type != TokenType.EOF)
                {
                    string key = _parser.Advance().Value;
                    _parser.Consume(TokenType.EQUALS, "Expected '=' after option key");
                    string val = _parser.Advance().Value;
                    options[key] = val;
                    if (!_parser.Match(TokenType.COMMA))
                    {
                        break;
                    }
                }
                _parser.Consume(TokenType.RPAREN, "Expected ')' at end of WITH options");
            }

            _parser.Consume(TokenType.SEMICOLON, "Expected ';' at the end of CREATE CONNECTION");

            return new CreateConnectionStatement(name, connectionType, target, options, mode)
            {
                Line = startToken.Line,
                Column = startToken.Column
            };
        }

        private Statement ParseCreateTable(Token startToken)
        {
            bool ifNotExists = false;
            if (_parser.Match(TokenType.IF))
            {
                _parser.Consume(TokenType.NOT, "Expected 'NOT' after 'IF'");
                _parser.Consume(TokenType.EXISTS, "Expected 'EXISTS' after 'NOT'");
                ifNotExists = true;
            }

            var targetTable = ParseTableReference(false);
            _parser.Consume(TokenType.LPAREN, "Expected '(' after table name");

            var columns = new List<ColumnDefinition>();
            var tableConstraints = new List<TableConstraint>();

            while (_parser.Current.Type != TokenType.RPAREN && _parser.Current.Type != TokenType.EOF)
            {
                // Check if it's a table-level constraint
                string? constraintName = null;
                if (_parser.Match(TokenType.CONSTRAINT))
                {
                    constraintName = _parser.ConsumeIdentifier("Expected constraint name").Value;
                }

                if (_parser.Current.Type == TokenType.PRIMARY || 
                    _parser.Current.Type == TokenType.UNIQUE || 
                    _parser.Current.Type == TokenType.CHECK || 
                    _parser.Current.Type == TokenType.FOREIGN)
                {
                    var tc = ParseTableConstraint(constraintName);
                    tableConstraints.Add(tc);
                }
                else
                {
                    // It's a column definition
                    var colName = _parser.ConsumeIdentifier("Expected column name or constraint").Value;
                    string dataType = "NVARCHAR(MAX)"; // Default type

                    if (_parser.IsIdentifier(_parser.Current))
                    {
                        dataType = _parser.Advance().Value;
                        if (_parser.Match(TokenType.LPAREN))
                        {
                            dataType += "(";
                            dataType += _parser.Consume(TokenType.NUMBER, "Expected length").Value;
                            if (_parser.Match(TokenType.COMMA))
                            {
                                dataType += ",";
                                dataType += _parser.Consume(TokenType.NUMBER, "Expected scale").Value;
                            }
                            dataType += ")";
                            _parser.Consume(TokenType.RPAREN, "Expected ')'");
                        }
                    }

                    Dictionary<string, string>? metadata = null;
                    while (_parser.Match(TokenType.COLUMN_TAG))
                    {
                        if (metadata == null) metadata = new(StringComparer.OrdinalIgnoreCase);
                        _parser.ParseMetadataTags(_parser.Previous.Value, metadata);
                    }
                    var colDef = new ColumnDefinition(colName, dataType, false, null, metadata);

                    // Parse column constraints
                    while (true)
                    {
                        if (_parser.Match(TokenType.IDENTITY))
                        {
                            // Identity handled here for now
                        }
                        else if (_parser.Match(TokenType.PRIMARY))
                        {
                            _parser.Consume(TokenType.KEY, "Expected KEY after PRIMARY");
                            colDef.IsPrimaryKey = true;
                        }
                        else if (_parser.Match(TokenType.UNIQUE))
                        {
                            colDef.IsUnique = true;
                        }
                        else if (_parser.Match(TokenType.NOT))
                        {
                            _parser.Consume(TokenType.NULL, "Expected NULL after NOT");
                            colDef.IsNullable = false;
                        }
                        else if (_parser.Match(TokenType.NULL))
                        {
                            colDef.IsNullable = true;
                        }
                        else if (_parser.Match(TokenType.CHECK))
                        {
                            _parser.Consume(TokenType.LPAREN, "Expected '(' after CHECK");
                            colDef.CheckConstraint = _parser.ParseExpression();
                            _parser.Consume(TokenType.RPAREN, "Expected ')' after check expression");
                        }
                        else if (_parser.Match(TokenType.DEFAULT))
                        {
                            colDef.DefaultExpression = _parser.ParseExpression();
                        }
                        else if (_parser.Match(TokenType.REFERENCES))
                        {
                            colDef.ForeignKey = ParseForeignKeyReference();
                        }
                        else
                        {
                            break;
                        }
                    }
                    columns.Add(colDef);
                }

                if (!_parser.Match(TokenType.COMMA))
                {
                    break;
                }
            }

            _parser.Consume(TokenType.RPAREN, "Expected ')' at end of CREATE TABLE");
            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();

            var stmt = new CreateTableStatement(targetTable, ifNotExists, columns)
            {
                Line = startToken.Line,
                Column = startToken.Column
            };
            stmt.TableConstraints.AddRange(tableConstraints);
            return stmt;
        }

        private TableConstraint ParseTableConstraint(string? constraintName)
        {
            if (_parser.Match(TokenType.PRIMARY))
            {
                _parser.Consume(TokenType.KEY, "Expected KEY after PRIMARY");
                _parser.Consume(TokenType.LPAREN, "Expected '(' after PRIMARY KEY");
                var columns = ParseIdentifierList();
                _parser.Consume(TokenType.RPAREN, "Expected ')' after column list");
                return new TablePrimaryKeyConstraint(columns) { ConstraintName = constraintName };
            }
            if (_parser.Match(TokenType.UNIQUE))
            {
                _parser.Consume(TokenType.LPAREN, "Expected '(' after UNIQUE");
                var columns = ParseIdentifierList();
                _parser.Consume(TokenType.RPAREN, "Expected ')' after column list");
                return new TableUniqueConstraint(columns) { ConstraintName = constraintName };
            }
            if (_parser.Match(TokenType.CHECK))
            {
                _parser.Consume(TokenType.LPAREN, "Expected '(' after CHECK");
                var expr = _parser.ParseExpression();
                _parser.Consume(TokenType.RPAREN, "Expected ')' after check expression");
                return new TableCheckConstraint(expr) { ConstraintName = constraintName };
            }
            if (_parser.Match(TokenType.FOREIGN))
            {
                _parser.Consume(TokenType.KEY, "Expected KEY after FOREIGN");
                _parser.Consume(TokenType.LPAREN, "Expected '(' after FOREIGN KEY");
                var columns = ParseIdentifierList();
                _parser.Consume(TokenType.RPAREN, "Expected ')' after column list");
                
                _parser.Consume(TokenType.REFERENCES, "Expected REFERENCES keyword");
                var destTable = ParseTableReference(false);
                _parser.Consume(TokenType.LPAREN, "Expected '(' after reference table name");
                var refCols = ParseIdentifierList();
                _parser.Consume(TokenType.RPAREN, "Expected ')' after reference table columns");
                var reference = new ForeignKeyReference(destTable, refCols);
                
                return new TableForeignKeyConstraint(columns, reference) { ConstraintName = constraintName };
            }
            throw new SyntaxException($"Unexpected token {_parser.Current.Type} in table constraint", _parser.Current.Line, _parser.Current.Column);
        }

        private ForeignKeyReference ParseForeignKeyReference()
        {
            var table = ParseTableReference(false);
            _parser.Consume(TokenType.LPAREN, "Expected '(' after table name in REFERENCES");
            var columns = ParseIdentifierList();
            _parser.Consume(TokenType.RPAREN, "Expected ')' after column list in REFERENCES");
            return new ForeignKeyReference(table, columns);
        }

        private List<string> ParseIdentifierList()
        {
            var list = new List<string>();
            while (true)
            {
                list.Add(_parser.ConsumeIdentifier("Expected identifier").Value);
                if (!_parser.Match(TokenType.COMMA)) break;
            }
            return list;
        }

        private Statement ParseCreateIndex(Token startToken, bool isUnique)
        {
            _parser.Consume(TokenType.INDEX, "Expected INDEX");

            var indexName = _parser.ConsumeIdentifier("Expected index name").Value;
            _parser.Consume(TokenType.ON, "Expected 'ON' after index name");
            var targetTable = ParseTableReference(false);

            _parser.Consume(TokenType.LPAREN, "Expected '(' before column list");
            var columns = new List<string>();
            columns.Add(_parser.ConsumeIdentifier("Expected column name").Value);
            while (_parser.Match(TokenType.COMMA))
            {
                columns.Add(_parser.ConsumeIdentifier("Expected column name").Value);
            }
            _parser.Consume(TokenType.RPAREN, "Expected ')' after column list");

            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();

            return new CreateIndexStatement(indexName, targetTable, columns, isUnique)
            {
                Line = startToken.Line,
                Column = startToken.Column
            };
        }

        private Statement ParseStatementWithCte()
        {
            var startToken = _parser.Previous; // WITH
            bool isRecursive = _parser.Match(TokenType.RECURSIVE);

            var ctes = ParseCtes();
            var stmt = ParseStatement();
            
            stmt.Ctes = ctes;
            if (stmt is SelectStatement select && isRecursive)
            {
                select.IsRecursive = true;
            }
            
            return stmt;
        }

        private List<CteDefinition> ParseCtes()
        {
            var ctes = new List<CteDefinition>();
            do
            {
                // Use a helper to consume identifier even if it matches a keyword
                string name;
                if (_parser.Current.Type == TokenType.IDENTIFIER || LanguageMetadata.IsKeyword(_parser.Current.Value))
                {
                    name = _parser.Advance().Value;
                }
                else
                {
                    throw new SyntaxException("Expected CTE name", _parser.Current.Line, _parser.Current.Column);
                }

                _parser.Consume(TokenType.AS, "Expected 'AS' after CTE name");
                _parser.Consume(TokenType.LPAREN, "Expected '(' before CTE query");
                var subq = _parser.ParseQuery();
                _parser.Consume(TokenType.RPAREN, "Expected ')' after CTE query");
                ctes.Add(new CteDefinition(name, subq));
            } while (_parser.Match(TokenType.COMMA));
            return ctes;
        }

        private Statement ParseExplain()
        {
            var startToken = _parser.Previous;
            var stmt = _parser.ParseStatement();
            return new ExplainStatement(stmt) { Line = startToken.Line, Column = startToken.Column };
        }

        private Statement ParseDrop()
        {
            var startToken = _parser.Previous;
            
            bool ifExists = false;
            if (_parser.Match(TokenType.TABLE))
            {
                if (_parser.Match(TokenType.IF)) { _parser.Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
                var target = ParseTableReference(false);
                if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
                return new DropTableStatement(target, ifExists) { Line = startToken.Line, Column = startToken.Column };
            }
            else if (_parser.Match(TokenType.CONNECTION))
            {
                if (_parser.Match(TokenType.IF)) { _parser.Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
                var name = _parser.ConsumeIdentifier("Expected connection name").Value;
                if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
                return new DropConnectionStatement(name, ifExists) { Line = startToken.Line, Column = startToken.Column };
            }
            else if (_parser.Match(TokenType.PROCEDURE))
            {
                if (_parser.Match(TokenType.IF)) { _parser.Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
                var name = _parser.ConsumeIdentifier("Expected procedure name").Value;
                if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
                return new DropProcedureStatement(name, ifExists) { Line = startToken.Line, Column = startToken.Column };
            }
            else if (_parser.Match(TokenType.FUNCTION))
            {
                if (_parser.Match(TokenType.IF)) { _parser.Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
                var name = _parser.ConsumeIdentifier("Expected function name").Value;
                if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
                return new DropFunctionStatement(name, ifExists) { Line = startToken.Line, Column = startToken.Column };
            }
            else if (_parser.Match(TokenType.INDEX))
            {
                if (_parser.Match(TokenType.IF)) { _parser.Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
                var name = _parser.ConsumeIdentifier("Expected index name").Value;
                TableReference? target = null;
                if (_parser.Match(TokenType.ON)) target = ParseTableReference();
                if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
                return new DropIndexStatement(name, target, ifExists) { Line = startToken.Line, Column = startToken.Column };
            }
            
            throw new SyntaxException("Expected TABLE, CONNECTION, PROCEDURE, FUNCTION or INDEX after DROP", _parser.Current.Line, _parser.Current.Column);
        }

        private Statement ParseDelete()
        {
            var startToken = _parser.Previous;
            _parser.Match(TokenType.FROM);
            var targetTable = ParseTableReference(false);

            OutputClause? output = null;
            if (_parser.Match(TokenType.OUTPUT))
            {
                output = _parser.ParseOutputClause();
            }
            
            Expression? whereClause = null;
            if (_parser.Match(TokenType.WHERE))
            {
                whereClause = _parser.ParseExpression();
            }

            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();

            return new DeleteStatement(targetTable, whereClause)
            {
                Line = startToken.Line,
                Column = startToken.Column,
                Output = output
            };
        }

        private Statement ParseTruncate()
        {
            var startToken = _parser.Previous;
            _parser.Consume(TokenType.TABLE, "Expected 'TABLE' after 'TRUNCATE'");
            var targetTable = ParseTableReference(false);

            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();

            return new TruncateTableStatement(targetTable)
            {
                Line = startToken.Line,
                Column = startToken.Column
            };
        }
 
        private Statement ParseDeclare()
        {
            var startToken = _parser.Previous;
            var declares = new List<Statement>();

            do
            {
                var varToken = _parser.Consume(TokenType.VARIABLE, "Expected variable name starting with '@'");
                // Modified: Make type optional, defaulting to ANY if not provided (fixes syntax error in common user scripts)
                string type = "ANY";
                if (_parser.IsIdentifier(_parser.Current))
                {
                    type = _parser.ParseType();
                }
                
                bool isSensitive = _parser.Match(TokenType.PASSWORD);
                bool isInput = _parser.Match(TokenType.INPUT);
                bool isOutput = _parser.Match(TokenType.OUTPUT);

                Expression? initialValue = null;
                if (_parser.Match(TokenType.EQUALS))
                {
                    initialValue = _parser.ParseExpression();
                }

                if (!isSensitive) isSensitive = _parser.Match(TokenType.PASSWORD);
                if (!isInput) isInput = _parser.Match(TokenType.INPUT);
                if (!isOutput) isOutput = _parser.Match(TokenType.OUTPUT);

                Dictionary<string, string>? metadata = null;
                while (_parser.Match(TokenType.COLUMN_TAG))
                {
                    if (metadata == null) metadata = new(StringComparer.OrdinalIgnoreCase);
                    _parser.ParseMetadataTags(_parser.Previous.Value, metadata);
                }

                var stmt = new DeclareStatement(varToken.Value, type, initialValue, isSensitive, isInput, isOutput, metadata) 
                { 
                    Line = varToken.Line, 
                    Column = varToken.Column,
                    EndLine = _parser.LastTokenEndLine,
                    EndColumn = _parser.LastTokenEndColumn,
                    IsSensitive = isSensitive,
                    IsInput = isInput,
                    IsOutput = isOutput
                };
                
                declares.Add(stmt);

            } while (_parser.Match(TokenType.COMMA));

            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();

            if (declares.Count == 1) return declares[0];
            return new BlockStatement(declares) { Line = startToken.Line, Column = startToken.Column, EndLine = _parser.LastTokenEndLine, EndColumn = _parser.LastTokenEndColumn };
        }

        private Statement ParseRun()
        {
            var startToken = _parser.Previous;
            _parser.Consume(TokenType.SCRIPT, "Expected 'SCRIPT' after 'RUN'");
            var pathExpr = _parser.ParseExpression();
            
            var parameters = new Dictionary<string, Expression>(StringComparer.OrdinalIgnoreCase);
            if (_parser.Match(TokenType.WITH))
            {
                _parser.Consume(TokenType.LPAREN, "Expected '(' after WITH");
                while (_parser.Current.Type != TokenType.RPAREN && _parser.Current.Type != TokenType.EOF)
                {
                    var nameToken = _parser.Consume(TokenType.VARIABLE, "Expected parameter name starting with '@'");
                    _parser.Consume(TokenType.EQUALS, "Expected '='");
                    var value = _parser.ParseExpression();
                    parameters[nameToken.Value] = value;
                    if (!_parser.Match(TokenType.COMMA)) break;
                }
                _parser.Consume(TokenType.RPAREN, "Expected ')' after parameters");
            }
            
            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
            return new RunScriptStatement(pathExpr, parameters) { Line = startToken.Line, Column = startToken.Column };
        }

        private Statement ParseSetVariable()
        {
            var startToken = _parser.Previous;
            var varToken = _parser.Consume(TokenType.VARIABLE, "Expected variable name starting with '@'");
            _parser.Consume(TokenType.EQUALS, "Expected '=' in SET statement");
            var expr = _parser.ParseExpression();

            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();

            return new SetVariableStatement(varToken.Value, expr)
            {
                Line = startToken.Line,
                Column = startToken.Column
            };
        }

        private Statement ParseInsert()
        {
            var startToken = _parser.Previous;
            _parser.Match(TokenType.INTO);
            var targetTable = ParseTableReference(false);

            List<string>? columns = null;
            if (_parser.Match(TokenType.LPAREN))
            {
                columns = new List<string>();
                columns.Add(_parser.ConsumeIdentifier("Expected column name").Value);
                while (_parser.Match(TokenType.COMMA))
                {
                    columns.Add(_parser.ConsumeIdentifier("Expected column name").Value);
                }
                _parser.Consume(TokenType.RPAREN, "Expected ')' after column list");
            }

            OutputClause? output = null;
            if (_parser.Match(TokenType.OUTPUT))
            {
                output = _parser.ParseOutputClause();
            }

            var rowList = new List<List<Expression>>();
            bool foundValues = false;
            while (_parser.Match(TokenType.VALUES))
            {
                foundValues = true;
                do
                {
                    while (_parser.Match(TokenType.COLUMN_TAG)) { } // Robustly skip metadata tags between rows
                    _parser.Consume(TokenType.LPAREN, "Expected '(' for VALUES row");
                    var values = new List<Expression>();
                    values.Add(_parser.ParseExpression());
                    while (_parser.Match(TokenType.COMMA))
                    {
                        values.Add(_parser.ParseExpression());
                    }
                    _parser.Consume(TokenType.RPAREN, "Expected ')' after VALUES list");
                    rowList.Add(values);
                    while (_parser.Match(TokenType.COLUMN_TAG)) { } // Robustly skip metadata tags after row
                } while (_parser.Match(TokenType.COMMA));
            }

            if (foundValues)
            {
                if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();

                return new InsertStatement(targetTable, columns, rowList)
                {
                    Line = startToken.Line,
                    Column = startToken.Column,
                    EndLine = _parser.LastTokenEndLine,
                    EndColumn = _parser.LastTokenEndColumn,
                    Output = output
                };
            }

            if (_parser.Current.Type == TokenType.SELECT || (_parser.Current.Type == TokenType.LPAREN && _parser.Peek.Type == TokenType.SELECT))
            {
                bool hasParen = _parser.Match(TokenType.LPAREN);
                var selectQuery = _parser.ParseQuery();
                if (hasParen) _parser.Consume(TokenType.RPAREN, "Expected ')' after subquery in INSERT");
                if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();

                return new InsertStatement(targetTable, columns, selectQuery)
                {
                    Line = startToken.Line,
                    Column = startToken.Column,
                    EndLine = _parser.LastTokenEndLine,
                    EndColumn = _parser.LastTokenEndColumn,
                    Output = output
                };
            }

            if (_parser.Current.Type == TokenType.EXEC || _parser.Current.Type == TokenType.EXECUTE)
            {
                _parser.Advance(); // EXEC or EXECUTE
                var pushdownStmt = ParseExecute();
                if (pushdownStmt is not ExecutePushdownStatement)
                {
                    throw new SyntaxException("Only native EXECUTE ... BEGIN ... END blocks are supported as an INSERT source.", _parser.Current.Line, _parser.Current.Column);
                }
                if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();

                return new InsertStatement(targetTable, columns, pushdownStmt)
                {
                    Line = startToken.Line,
                    Column = startToken.Column,
                    EndLine = _parser.LastTokenEndLine,
                    EndColumn = _parser.LastTokenEndColumn,
                    Output = output
                };
            }

            throw new SyntaxException($"Expected SELECT, VALUES or EXECUTE for INSERT payload", _parser.Current.Line, _parser.Current.Column);
        }

        private Statement ParseUpdate()
        {
            var startToken = _parser.Previous;
            var targetTable = ParseTableReference(false);

            _parser.Consume(TokenType.SET, "Expected 'SET' after table name in UPDATE");

            var assignments = new List<Assignment>();
            assignments.Add(ParseAssignment());
            while (_parser.Match(TokenType.COMMA))
            {
                assignments.Add(ParseAssignment());
            }

            OutputClause? output = null;
            if (_parser.Match(TokenType.OUTPUT))
            {
                output = _parser.ParseOutputClause();
            }

            TableReference? fromTable = null;
            List<JoinClause>? joins = null;
            if (_parser.Match(TokenType.FROM))
            {
                fromTable = _parser.ParseTableReference();
                joins = _parser.ParseJoins();
            }

            Expression? whereClause = null;
            if (_parser.Match(TokenType.WHERE))
            {
                whereClause = _parser.ParseExpression();
            }

            if (_parser.Current.Type == TokenType.SEMICOLON)
            {
                _parser.Advance();
            }

            return new UpdateStatement(targetTable, assignments, whereClause)
            {
                Line = startToken.Line,
                Column = startToken.Column,
                EndLine = _parser.LastTokenEndLine,
                EndColumn = _parser.LastTokenEndColumn,
                Output = output,
                FromTable = fromTable,
                Joins = joins
            };
        }

        private Assignment ParseAssignment()
        {
            var t = _parser.Current;
            var colName = _parser.ConsumeIdentifier("Expected column name for SET clause").Value;
            _parser.Consume(TokenType.EQUALS, "Expected '=' after column name in SET clause");
            var expr = _parser.ParseExpression();
            return new Assignment(colName, expr) { Line = t.Line, Column = t.Column };
        }

        private Statement ParseMerge()
        {
            var startToken = _parser.Previous; // MERGE
            _parser.Match(TokenType.INTO);
            var targetTable = ParseTableReference(false);

            _parser.Consume(TokenType.USING, "Expected 'USING' in MERGE statement");
            var sourceTable = ParseTableReference(false);

            _parser.Consume(TokenType.ON, "Expected 'ON' in MERGE statement");
            var onCondition = _parser.ParseExpression();

            var mergeStmt = new MergeStatement(targetTable, sourceTable, onCondition);

            while (_parser.Match(TokenType.WHEN))
            {
                bool matched = _parser.Match(TokenType.MATCHED);
                bool notMatched = false;
                bool bySource = false;
                bool byTarget = false;

                if (!matched)
                {
                    _parser.Consume(TokenType.NOT, "Expected 'MATCHED' or 'NOT MATCHED'");
                    _parser.Consume(TokenType.MATCHED, "Expected 'MATCHED' after 'NOT'");
                    notMatched = true;
                    if (_parser.Match(TokenType.BY))
                    {
                        if (_parser.Match(TokenType.TARGET)) byTarget = true;
                        else if (_parser.Match(TokenType.SOURCE)) bySource = true;
                        else throw new SyntaxException("Expected 'TARGET' or 'SOURCE' after 'NOT MATCHED BY'", _parser.Current.Line, _parser.Current.Column);
                    }
                    else
                    {
                        byTarget = true;
                    }
                }

                Expression? condition = null;
                if (_parser.Match(TokenType.AND))
                {
                    condition = _parser.ParseExpression();
                }

                _parser.Consume(TokenType.THEN, "Expected 'THEN' after WHEN clause");

                if (_parser.Match(TokenType.UPDATE))
                {
                    _parser.Consume(TokenType.SET, "Expected 'SET' after UPDATE in MERGE");
                    var assignments = new List<Assignment>();
                    assignments.Add(ParseAssignment());
                    while (_parser.Match(TokenType.COMMA)) assignments.Add(ParseAssignment());
                    
                    var clause = new MergeActionClause(MergeActionType.UPDATE, condition, updateAssignments: assignments);
                    if (matched) mergeStmt.MatchedClauses.Add(clause);
                    else if (bySource) mergeStmt.NotMatchedBySourceClauses.Add(clause);
                    else throw new SyntaxException("UPDATE only allowed in MATCHED or NOT MATCHED BY SOURCE", _parser.Current.Line, _parser.Current.Column);
                }
                else if (_parser.Match(TokenType.INSERT))
                {
                    List<string>? columns = null;
                    if (_parser.Match(TokenType.LPAREN))
                    {
                        columns = new List<string>();
                        columns.Add(_parser.ConsumeIdentifier("Expected column name").Value);
                        while (_parser.Match(TokenType.COMMA)) columns.Add(_parser.ConsumeIdentifier("Expected column name").Value);
                        _parser.Consume(TokenType.RPAREN, "Expected ')'");
                    }
                    _parser.Consume(TokenType.VALUES, "Expected 'VALUES' in MERGE INSERT");
                    _parser.Consume(TokenType.LPAREN, "Expected '(' for values");
                    var values = new List<Expression>();
                    values.Add(_parser.ParseExpression());
                    while (_parser.Match(TokenType.COMMA)) values.Add(_parser.ParseExpression());
                    _parser.Consume(TokenType.RPAREN, "Expected ')'");

                    var clause = new MergeActionClause(MergeActionType.INSERT, condition, insertColumns: columns, insertValues: values);
                    if (notMatched && byTarget) mergeStmt.NotMatchedByTargetClauses.Add(clause);
                    else throw new SyntaxException("INSERT only allowed in NOT MATCHED [BY TARGET]", _parser.Current.Line, _parser.Current.Column);
                }
                else if (_parser.Match(TokenType.DELETE))
                {
                    var clause = new MergeActionClause(MergeActionType.DELETE, condition);
                    if (matched) mergeStmt.MatchedClauses.Add(clause);
                    else if (bySource) mergeStmt.NotMatchedBySourceClauses.Add(clause);
                    else throw new SyntaxException("DELETE only allowed in MATCHED or NOT MATCHED BY SOURCE", _parser.Current.Line, _parser.Current.Column);
                }
                else
                {
                    throw new SyntaxException("Expected UPDATE, INSERT, or DELETE action in MERGE", _parser.Current.Line, _parser.Current.Column);
                }
            }

            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();

            mergeStmt.Line = startToken.Line;
            mergeStmt.Column = startToken.Column;
            return mergeStmt;
        }

        private TableReference ParseTableReference(bool allowFunction = true)
        {
            return _parser.ParseTableReference(allowFunction);
        }
        private bool IsStatementStart(TokenType type)
        {
            return type == TokenType.SELECT || type == TokenType.INSERT || type == TokenType.UPDATE || 
                   type == TokenType.DELETE || type == TokenType.MERGE || type == TokenType.CREATE || type == TokenType.DROP || 
                   type == TokenType.ALTER || type == TokenType.DECLARE || type == TokenType.SET ||
                   type == TokenType.IF || type == TokenType.WHILE || type == TokenType.BEGIN ||
                   type == TokenType.PRINT || type == TokenType.EXEC || type == TokenType.EXECUTE ||
                   type == TokenType.RUN || type == TokenType.USE || type == TokenType.DOCKER || type == TokenType.HELP;
        }

        private Statement ParseSetProfiling()
        {
            var enabled = true;
            if (_parser.Match(TokenType.ON)) enabled = true;
            else if (_parser.Match(TokenType.OFF)) enabled = false;
            else throw new SyntaxException($"Expected ON or OFF after SET PROFILE", _parser.Current.Line, _parser.Current.Column);

            if (_parser.Match(TokenType.SEMICOLON)) { }
            return new SetProfilingStatement { Enabled = enabled };
        }

        private Statement ParseShow()
        {
            if (_parser.Match(TokenType.PROFILE) || _parser.Match(TokenType.PROFILING))
            {
                if (_parser.Match(TokenType.SEMICOLON)) { }
                return new ShowProfileStatement();
            }
            if (_parser.Match(TokenType.JOB))
            {
                _parser.Consume(TokenType.HISTORY, "Expected HISTORY after SHOW JOB");
                string? jobName = null;
                if (_parser.Current.Type == TokenType.IDENTIFIER)
                {
                    jobName = _parser.Advance().Value;
                }
                if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
                return new ShowJobHistoryStatement(jobName);
            }
            if (_parser.Match(TokenType.JOBS))
            {
                if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
                return new ShowJobsStatement();
            }
            if (_parser.Match(TokenType.LINEAGE))
            {
                _parser.Match(TokenType.FOR);
                var targetTable = _parser.ParseTableReference();
                if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
                return new LineageStatement(targetTable);
            }
            throw new SyntaxException($"Expected PROFILE, JOB HISTORY, JOBS or LINEAGE after SHOW", _parser.Current.Line, _parser.Current.Column);
        }

        private Statement ParseCreateJob(Token startToken)
        {
            var jobName = _parser.ConsumeIdentifier("Expected job name").Value;
            _parser.Consume(TokenType.ON, "Expected ON after job name");
            _parser.Consume(TokenType.SCHEDULE, "Expected SCHEDULE after ON");
            
            var schedule = ParseSchedule();
            
            _parser.Consume(TokenType.AS, "Expected AS before job script");
            var script = ParseStatement();
            
            return new CreateJobStatement(jobName, schedule, script) { Line = startToken.Line, Column = startToken.Column };
        }

        private ScheduleInfo ParseSchedule()
        {
            _parser.Consume(TokenType.EVERY, "Expected EVERY in SCHEDULE");
            var intervalToken = _parser.Consume(TokenType.NUMBER, "Expected interval number");
            int interval = int.Parse(intervalToken.Value);
            
            var unitToken = _parser.Advance();
            string unit = unitToken.Value.ToUpper();
            if (unit != "SECOND" && unit != "SECONDS" && unit != "MINUTE" && unit != "MINUTES" && 
                unit != "HOUR" && unit != "HOURS" && unit != "DAY" && unit != "DAYS")
            {
                throw new SyntaxException($"Unexpected schedule unit: {unit}", unitToken.Line, unitToken.Column);
            }
            
            // Normalize unit
            if (unit.EndsWith("S")) unit = unit.Substring(0, unit.Length - 1);

            string? atTime = null;
            if (_parser.Match(TokenType.AT))
            {
                atTime = _parser.Consume(TokenType.STRING, "Expected time string (e.g. '02:00') after AT").Value;
            }

            return new ScheduleInfo(interval, unit, atTime);
        }

        private Statement ParseBulkInsert()
        {
            var startToken = _parser.Previous;
            // BULK is consumed, check for INSERT or LOAD
            if (!_parser.Match(TokenType.INSERT) && !_parser.Match(TokenType.LOAD))
            {
                throw new SyntaxException("Expected INSERT or LOAD after BULK", _parser.Current.Line, _parser.Current.Column);
            }

            var targetTable = ParseTableReference(false);

            List<string>? columns = null;
            if (_parser.Match(TokenType.LPAREN))
            {
                columns = new List<string>();
                columns.Add(_parser.ConsumeIdentifier("Expected column name").Value);
                while (_parser.Match(TokenType.COMMA))
                {
                    columns.Add(_parser.ConsumeIdentifier("Expected column name").Value);
                }
                _parser.Consume(TokenType.RPAREN, "Expected ')' after column list");
            }

            _parser.Consume(TokenType.FROM, "Expected FROM after table name");
            var pathToken = _parser.Consume(TokenType.STRING, "Expected file path as string");

            var options = new Dictionary<string, Expression>(StringComparer.OrdinalIgnoreCase);
            if (_parser.Match(TokenType.WITH))
            {
                _parser.Consume(TokenType.LPAREN, "Expected '(' after WITH");
                while (_parser.Current.Type != TokenType.RPAREN && _parser.Current.Type != TokenType.EOF)
                {
                    string optionName = _parser.Current.Value;
                    
                    // Accept any of our new bulk keywords or an identifier
                    if (_parser.Match(TokenType.BATCHSIZE) || 
                        _parser.Match(TokenType.MAXERRORS) || 
                        _parser.Match(TokenType.FIELDTERMINATOR) || 
                        _parser.Match(TokenType.ROWTERMINATOR) || 
                        _parser.Match(TokenType.FIRSTROW) ||
                        _parser.Match(TokenType.DATA_SOURCE) ||
                        _parser.Match(TokenType.FORMAT) ||
                        _parser.Match(TokenType.IDENTIFIER))
                    {
                        // optionName captured
                    }
                    else
                    {
                        throw new SyntaxException($"Unexpected bulk option: {_parser.Current.Value}", _parser.Current.Line, _parser.Current.Column);
                    }

                    _parser.Consume(TokenType.EQUALS, "Expected '='");
                    var value = _parser.ParseExpression();
                    options[optionName] = value;

                    if (!_parser.Match(TokenType.COMMA)) break;
                }
                _parser.Consume(TokenType.RPAREN, "Expected ')' after WITH options");
            }

            if (_parser.Match(TokenType.SEMICOLON)) { }

            var stmt = new BulkInsertStatement(targetTable, pathToken.Value, options, columns)
            {
                Line = startToken.Line,
                Column = startToken.Column
            };

            // Parse metadata tags if any
            while (_parser.Match(TokenType.COLUMN_TAG))
            {
                var tag = _parser.Previous.Value;
                if (tag.StartsWith("/*") && tag.EndsWith("*/"))
                {
                    var content = tag.Substring(2, tag.Length - 4).Trim();
                    var parts = content.Split(';', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var p in parts)
                    {
                        var kv = p.Split(':');
                        if (kv.Length == 2)
                        {
                            var key = kv[0].Trim().TrimStart('@');
                            var val = kv[1].Trim();
                            stmt.Metadata[key] = val;
                        }
                    }
                }
            }

            return stmt;
        }
        private Statement ParseLineage()
        {
            var startToken = _parser.Previous;
            _parser.Match(TokenType.LPAREN); // Optional ( for LINEAGE(tbl)
            var targetTable = _parser.ParseTableReference();
            
            string? columnName = null;
            if (_parser.Match(TokenType.COMMA))
            {
                columnName = _parser.ConsumeIdentifier("Expected column name after comma").Value;
            }
            
            _parser.Match(TokenType.RPAREN); // Optional )

            string? exportPath = null;
            if (_parser.Match(TokenType.TO))
            {
                var pathToken = _parser.Consume(TokenType.STRING, "Expected file path after TO");
                exportPath = pathToken.Value;
            }

            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();

            return new LineageStatement(targetTable, columnName, exportPath)
            {
                Line = startToken.Line,
                Column = startToken.Column
            };
        }

        private Statement ParseFileTransfer(FileTransferType type)
        {
            var startToken = _parser.Previous;
            Expression localPath;
            string connectionName;
            Expression remotePath;

            if (type == FileTransferType.Send)
            {
                localPath = _parser.ParseExpression();
                _parser.Consume(TokenType.COMMA, "Expected ',' after local path");
                connectionName = _parser.ConsumeIdentifier("Expected connection name").Value;
                _parser.Consume(TokenType.COMMA, "Expected ',' after connection name");
                remotePath = _parser.ParseExpression();
            }
            else // Receive
            {
                connectionName = _parser.ConsumeIdentifier("Expected connection name").Value;
                _parser.Consume(TokenType.COMMA, "Expected ',' after connection name");
                remotePath = _parser.ParseExpression();
                _parser.Consume(TokenType.COMMA, "Expected ',' after remote path");
                localPath = _parser.ParseExpression();
            }

            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
            return new FileTransferStatement
            {
                Type = type,
                LocalPath = localPath,
                ConnectionName = connectionName,
                RemotePath = remotePath,
                Line = startToken.Line,
                Column = startToken.Column
            };
        }
        private Statement ParseDockerClose()
        {
            var startToken = _parser.Previous;
            Expression? imageName = null;
            string? alias = null;
            
            if (_parser.Current.Type != TokenType.SEMICOLON) 
            {
                // Differentiate between Identifier (Alias) and Expression (ImageName)
                // If it's a string literal, it's definitely an Expression
                if (_parser.Current.Type == TokenType.STRING)
                {
                    imageName = _parser.ParseExpression();
                }
                else if (_parser.Current.Type == TokenType.IDENTIFIER)
                {
                    alias = _parser.ConsumeIdentifier("Expected alias").Value;
                }
            }

            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
            return new DockerCloseStatement(imageName, alias) { Line = startToken.Line, Column = startToken.Column };
        }

        private Statement ParseDocker()
        {
            if (_parser.Match(TokenType.CLOSE))
            {
                return ParseDockerClose();
            }
            
            // Legacy DOCKER <alias> <action>
            var startToken = _parser.Previous;
            var alias = _parser.ConsumeIdentifier("Expected container alias").Value;
            var actionStr = _parser.ConsumeIdentifier("Expected action (START, STOP, PAUSE, RESUME, CLOSE)").Value.ToUpperInvariant();
            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();

            var action = actionStr switch
            {
                "START" => DockerAction.Start,
                "STOP" => DockerAction.Stop,
                "PAUSE" => DockerAction.Pause,
                "RESUME" => DockerAction.Resume,
                "CLOSE" => DockerAction.Close,
                _ => throw new SyntaxException($"Unknown Docker action: {actionStr}", startToken.Line, startToken.Column)
            };

            if (action == DockerAction.Close) return new DockerCloseStatement(null, alias) { Line = startToken.Line, Column = startToken.Column };
            return new DockerActionStatement(alias, action) { Line = startToken.Line, Column = startToken.Column };
        }

        private Statement ParseDockerAction(DockerAction action)
        {
            var startToken = _parser.Previous;
            var alias = _parser.ConsumeIdentifier("Expected container alias").Value;
            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
            return new DockerActionStatement(alias, action) { Line = startToken.Line, Column = startToken.Column };
        }

        private Statement ParseSendEmail()
        {
            _parser.Consume(TokenType.TO, "Expected TO after SEND_EMAIL");
            var to = _parser.ParseExpression();

            _parser.Consume(TokenType.SUBJECT, "Expected SUBJECT");
            var subject = _parser.ParseExpression();

            _parser.Consume(TokenType.BODY, "Expected BODY");
            var body = _parser.ParseExpression();

            var stmt = new EmailStatement(to, subject, body);

            while (true)
            {
                if (_parser.Match(TokenType.CC))
                {
                    stmt.Cc ??= new List<Expression>();
                    stmt.Cc.Add(_parser.ParseExpression());
                    while (_parser.Match(TokenType.COMMA)) stmt.Cc.Add(_parser.ParseExpression());
                }
                else if (_parser.Match(TokenType.BCC))
                {
                    stmt.Bcc ??= new List<Expression>();
                    stmt.Bcc.Add(_parser.ParseExpression());
                    while (_parser.Match(TokenType.COMMA)) stmt.Bcc.Add(_parser.ParseExpression());
                }
                else if (_parser.Match(TokenType.ATTACH))
                {
                    stmt.Attachments ??= new List<Expression>();
                    stmt.Attachments.Add(_parser.ParseExpression());
                    while (_parser.Match(TokenType.COMMA)) stmt.Attachments.Add(_parser.ParseExpression());
                }
                else if (_parser.Match(TokenType.AT))
                {
                    stmt.ConnectionName = _parser.ParseExpression();
                }
                else break;
            }

            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
            return stmt;
        }

        private Statement ParseLint()
        {
            var token = _parser.Previous; // Match already advanced
            string? path = null;
            if (_parser.Match(TokenType.STRING))
            {
                path = _parser.Previous.Value;
            }
            _parser.Match(TokenType.SEMICOLON);
            return new LintStatement(path) { Line = token.Line, Column = token.Column };
        }
    }
}
