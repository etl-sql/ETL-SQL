using System;
using System.Collections.Generic;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Common;

namespace ETL_SQL.Core.Parser
{
    public partial class StatementParser
    {
        private Statement ParseSendEmail(bool isSqlStyle)
        {
            var startToken = _parser.Previous;
            Expression? to = null;
            Expression? from = null;
            Expression? subject = null;
            Expression? body = null;
            Expression? connectionName = null;
            List<Expression> attachments = new();
            List<Expression> cc = new();
            List<Expression> bcc = new();

            // Support legacy SEND_EMAIL followed by keywords (SQL style)
            if (!isSqlStyle && _parser.Current.Type != TokenType.LPAREN)
            {
                isSqlStyle = true;
            }

            if (isSqlStyle)
            {
                while (true)
                {
                    if (_parser.Match(TokenType.TO)) 
                    {
                        to = _parser.ParseExpression();
                    }
                    else if (_parser.Match(TokenType.FROM)) 
                    {
                        from = _parser.ParseExpression();
                    }
                    else if (_parser.Match(TokenType.SUBJECT)) 
                    {
                        subject = _parser.ParseExpression();
                    }
                    else if (_parser.Match(TokenType.BODY)) 
                    {
                        body = _parser.ParseExpression();
                    }
                    else if (_parser.Match(TokenType.CC))
                    {
                        if (_parser.Match(TokenType.LBRACKET))
                        {
                            if (_parser.Current.Type != TokenType.RBRACKET)
                            {
                                cc.Add(_parser.ParseExpression());
                                while (_parser.Match(TokenType.COMMA)) cc.Add(_parser.ParseExpression());
                            }
                            _parser.Consume(TokenType.RBRACKET, "Expected ']'");
                        }
                        else
                        {
                            cc.Add(_parser.ParseExpression());
                            while (_parser.Match(TokenType.COMMA)) cc.Add(_parser.ParseExpression());
                        }
                    }
                    else if (_parser.Match(TokenType.BCC))
                    {
                        if (_parser.Match(TokenType.LBRACKET))
                        {
                            if (_parser.Current.Type != TokenType.RBRACKET)
                            {
                                bcc.Add(_parser.ParseExpression());
                                while (_parser.Match(TokenType.COMMA)) bcc.Add(_parser.ParseExpression());
                            }
                            _parser.Consume(TokenType.RBRACKET, "Expected ']'");
                        }
                        else
                        {
                            bcc.Add(_parser.ParseExpression());
                            while (_parser.Match(TokenType.COMMA)) bcc.Add(_parser.ParseExpression());
                        }
                    }
                    else if (_parser.Match(TokenType.ATTACH))
                    {
                        if (_parser.Match(TokenType.LBRACKET))
                        {
                            if (_parser.Current.Type != TokenType.RBRACKET)
                            {
                                attachments.Add(_parser.ParseExpression());
                                while (_parser.Match(TokenType.COMMA)) attachments.Add(_parser.ParseExpression());
                            }
                            _parser.Consume(TokenType.RBRACKET, "Expected ']'");
                        }
                        else
                        {
                            attachments.Add(_parser.ParseExpression());
                            while (_parser.Match(TokenType.COMMA)) attachments.Add(_parser.ParseExpression());
                        }
                    }
                    else if (_parser.Match(TokenType.AT))
                    {
                        connectionName = _parser.ParseExpression();
                    }
                    else if (_parser.Match(TokenType.LF) || _parser.Match(TokenType.CR) || _parser.Match(TokenType.CRLF))
                    {
                        continue;
                    }
                    else 
                    {
                        break;
                    }
                }
                
                if (to == null) throw new SyntaxException("Email TO is mandatory", _parser.Current.Line, _parser.Current.Column);
                if (from == null) throw new SyntaxException("Email FROM is mandatory", _parser.Current.Line, _parser.Current.Column);
                if (subject == null) throw new SyntaxException("Email SUBJECT is mandatory", _parser.Current.Line, _parser.Current.Column);
                if (body == null) throw new SyntaxException("Email BODY is mandatory", _parser.Current.Line, _parser.Current.Column);
                
                if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
            }
            else // Function style
            {
                _parser.Consume(TokenType.LPAREN, "Expected '('");
                connectionName = _parser.ParseExpression();
                _parser.Consume(TokenType.COMMA, "Expected ','");
                to = _parser.ParseExpression();
                _parser.Consume(TokenType.COMMA, "Expected ','");
                from = _parser.ParseExpression();
                _parser.Consume(TokenType.COMMA, "Expected ','");
                subject = _parser.ParseExpression();
                _parser.Consume(TokenType.COMMA, "Expected ','");
                body = _parser.ParseExpression();

                if (_parser.Match(TokenType.COMMA)) // CC
                {
                    if (_parser.Match(TokenType.LBRACKET))
                    {
                        if (_parser.Current.Type != TokenType.RBRACKET)
                        {
                            cc.Add(_parser.ParseExpression());
                            while (_parser.Match(TokenType.COMMA)) cc.Add(_parser.ParseExpression());
                        }
                        _parser.Consume(TokenType.RBRACKET, "Expected ']'");
                    }
                    else if (_parser.Current.Type != TokenType.COMMA && _parser.Current.Type != TokenType.RPAREN)
                    {
                        cc.Add(_parser.ParseExpression());
                    }
                }

                if (_parser.Match(TokenType.COMMA)) // BCC
                {
                    if (_parser.Match(TokenType.LBRACKET))
                    {
                        if (_parser.Current.Type != TokenType.RBRACKET)
                        {
                            bcc.Add(_parser.ParseExpression());
                            while (_parser.Match(TokenType.COMMA)) bcc.Add(_parser.ParseExpression());
                        }
                        _parser.Consume(TokenType.RBRACKET, "Expected ']'");
                    }
                    else if (_parser.Current.Type != TokenType.COMMA && _parser.Current.Type != TokenType.RPAREN)
                    {
                        bcc.Add(_parser.ParseExpression());
                    }
                }

                if (_parser.Match(TokenType.COMMA)) // Attachments
                {
                    if (_parser.Match(TokenType.LBRACKET))
                    {
                        if (_parser.Current.Type != TokenType.RBRACKET)
                        {
                            attachments.Add(_parser.ParseExpression());
                            while (_parser.Match(TokenType.COMMA)) attachments.Add(_parser.ParseExpression());
                        }
                        _parser.Consume(TokenType.RBRACKET, "Expected ']'");
                    }
                    else if (_parser.Current.Type != TokenType.COMMA && _parser.Current.Type != TokenType.RPAREN)
                    {
                        attachments.Add(_parser.ParseExpression());
                    }
                }

                _parser.Consume(TokenType.RPAREN, "Expected ')'");
            }

            var stmt = new EmailStatement(to, from, subject, body, connectionName)
            {
                IsSqlStyle = isSqlStyle,
                Cc = cc,
                Bcc = bcc,
                Attachments = attachments,
                Line = startToken.Line,
                Column = startToken.Column
            };

            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
            return stmt;
        }

        private Statement ParseFileTransfer(FileTransferType type, bool isSqlStyle)
        {
            var startToken = _parser.Previous;
            Expression? localPath = null;
            string? connectionName = null;
            Expression? remotePath = null;
            Expression? overwrite = null;

            if (!isSqlStyle && _parser.Current.Type != TokenType.LPAREN)
            {
                isSqlStyle = true;
            }

            if (isSqlStyle)
            {
                while (true)
                {
                    if (_parser.Match(TokenType.FROM))
                    {
                        if (type == FileTransferType.Send) throw new SyntaxException("FROM is not valid for SEND FILE. Use source path directly or TO for destination.", _parser.Current.Line, _parser.Current.Column);
                        remotePath = _parser.ParseExpression();
                    }
                    else if (_parser.Match(TokenType.TO))
                    {
                        if (type == FileTransferType.Send) remotePath = _parser.ParseExpression();
                        else localPath = _parser.ParseExpression();
                    }
                    else if (_parser.Match(TokenType.AT))
                    {
                        connectionName = _parser.ConsumeIdentifier("Expected connection name after AT").Value;
                    }
                    else if (_parser.Match(TokenType.WITH))
                    {
                        overwrite = ParseWithOverwrite();
                    }
                    else if (_parser.Match(TokenType.COMMA))
                    {
                        continue;
                    }
                    else if (localPath == null && type == FileTransferType.Send && (!LanguageMetadata.IsKeyword(_parser.Current.Value) || _parser.Current.Type == TokenType.STRING))
                    {
                        localPath = _parser.ParseExpression();
                    }
                    else if (connectionName == null && _parser.Current.Type == TokenType.IDENTIFIER && !LanguageMetadata.IsKeyword(_parser.Current.Value))
                    {
                        connectionName = _parser.Advance().Value;
                    }
                    else if (remotePath == null && (!LanguageMetadata.IsKeyword(_parser.Current.Value) || _parser.Current.Type == TokenType.STRING))
                    {
                        remotePath = _parser.ParseExpression();
                    }
                    else if (localPath == null && type == FileTransferType.Receive && (!LanguageMetadata.IsKeyword(_parser.Current.Value) || _parser.Current.Type == TokenType.STRING))
                    {
                        localPath = _parser.ParseExpression();
                    }
                    else if (_parser.Match(TokenType.LF) || _parser.Match(TokenType.CR) || _parser.Match(TokenType.CRLF))
                    {
                        continue;
                    }
                    else 
                    {
                        break;
                    }
                }

                if (localPath == null && type == FileTransferType.Send) throw new SyntaxException("Local source path is mandatory for SEND FILE", _parser.Current.Line, _parser.Current.Column);
                if (remotePath == null && type == FileTransferType.Receive) throw new SyntaxException("Remote source path is mandatory for RECEIVE FILE", _parser.Current.Line, _parser.Current.Column);
                if (connectionName == null) throw new SyntaxException("Connection name (using AT) is mandatory", _parser.Current.Line, _parser.Current.Column);
                if (remotePath == null && type == FileTransferType.Send) throw new SyntaxException("Remote destination path (using TO) is mandatory for SEND FILE", _parser.Current.Line, _parser.Current.Column);
                if (localPath == null && type == FileTransferType.Receive) throw new SyntaxException("Local destination path is mandatory for RECEIVE FILE", _parser.Current.Line, _parser.Current.Column);
                
                if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
            }
            else // Function style
            {
                _parser.Consume(TokenType.LPAREN, "Expected '('");
                
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

                if (_parser.Match(TokenType.COMMA))
                {
                    overwrite = _parser.ParseExpression();
                }
                _parser.Consume(TokenType.RPAREN, "Expected ')'");
            }

            var stmt = new FileTransferStatement
            {
                Type = type,
                LocalPath = localPath!,
                ConnectionName = connectionName!,
                RemotePath = remotePath!,
                Overwrite = overwrite,
                IsSqlStyle = isSqlStyle,
                Line = startToken.Line,
                Column = startToken.Column
            };

            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
            return stmt;
        }

        private Statement ParseFileOperation(Token startToken)
        {
            FileOpType type = startToken.Type switch
            {
                TokenType.COPY_FILE or TokenType.COPY => FileOpType.Copy,
                TokenType.MOVE_FILE or TokenType.MOVE => FileOpType.Move,
                TokenType.RENAME_FILE or TokenType.RENAME => FileOpType.Rename,
                TokenType.DELETE_FILE or TokenType.DELETE => FileOpType.Delete,
                TokenType.COMPRESS_FILE or TokenType.COMPRESS => FileOpType.Compress,
                TokenType.ENCRYPT_FILE or TokenType.ENCRYPT => FileOpType.Encrypt,
                TokenType.DECRYPT_FILE or TokenType.DECRYPT => FileOpType.Decrypt,
                _ => throw new SyntaxException($"Unexpected file operation: {startToken.Type}", startToken.Line, startToken.Column)
            };

            Expression? source = null;
            Expression? dest = null;
            Expression? overwrite = null;

            bool isFunctionStyle = _parser.Match(TokenType.LPAREN);

            if (isFunctionStyle)
            {
                source = _parser.ParseExpression();
                if (_parser.Match(TokenType.COMMA))
                {
                    dest = _parser.ParseExpression();
                }
                if (_parser.Match(TokenType.COMMA))
                {
                    overwrite = _parser.ParseExpression();
                }
                _parser.Consume(TokenType.RPAREN, "Expected ')' after arguments");
            }
            else // SQL style
            {
                while (true)
                {
                    if (_parser.Match(TokenType.TO))
                    {
                        dest = _parser.ParseExpression();
                    }
                    else if (_parser.Match(TokenType.WITH))
                    {
                        overwrite = ParseWithOverwrite();
                    }
                    else if (source == null && (!LanguageMetadata.IsKeyword(_parser.Current.Value) || _parser.Current.Type == TokenType.STRING))
                    {
                        source = _parser.ParseExpression();
                    }
                    else if (_parser.Match(TokenType.LF) || _parser.Match(TokenType.CR) || _parser.Match(TokenType.CRLF))
                    {
                        continue;
                    }
                    else
                    {
                        break;
                    }
                }

                if (source == null) throw new SyntaxException($"Source path is mandatory for {startToken.Value}", _parser.Current.Line, _parser.Current.Column);
            }

            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();

            return new FileOperationStatement(type, source, dest, overwrite) { Line = startToken.Line, Column = startToken.Column };
        }

        private Statement ParseDirectoryOperation(Token startToken)
        {
            DirectoryOpType type = startToken.Type switch
            {
                TokenType.CREATE_DIRECTORY => DirectoryOpType.Create,
                TokenType.DELETE_DIRECTORY or TokenType.DELETE => DirectoryOpType.Delete,
                TokenType.RENAME_DIRECTORY or TokenType.RENAME => DirectoryOpType.Rename,
                TokenType.MOVE_DIRECTORY or TokenType.MOVE => DirectoryOpType.Move,
                TokenType.COPY_DIRECTORY or TokenType.COPY => DirectoryOpType.Copy,
                TokenType.DELETE_DIRECTORY_CONTENTS => DirectoryOpType.DeleteContents,
                TokenType.COMPRESS_DIRECTORY or TokenType.COMPRESS => DirectoryOpType.Compress,
                TokenType.ENCRYPT_DIRECTORY or TokenType.ENCRYPT => DirectoryOpType.Encrypt,
                TokenType.DECRYPT_DIRECTORY or TokenType.DECRYPT => DirectoryOpType.Decrypt,
                _ => throw new SyntaxException($"Unexpected directory operation: {startToken.Type}", startToken.Line, startToken.Column)
            };

            Expression? path = null;
            Expression? extra = null;
            Expression? overwrite = null;

            bool isFunctionStyle = _parser.Match(TokenType.LPAREN);

            if (isFunctionStyle)
            {
                path = _parser.ParseExpression();
                if (_parser.Match(TokenType.COMMA))
                {
                    extra = _parser.ParseExpression();
                }
                if (_parser.Match(TokenType.COMMA))
                {
                    overwrite = _parser.ParseExpression();
                }
                _parser.Consume(TokenType.RPAREN, "Expected ')' after arguments");
            }
            else // SQL style
            {
                while (true)
                {
                    if (_parser.Match(TokenType.TO))
                    {
                        extra = _parser.ParseExpression();
                    }
                    else if (_parser.Match(TokenType.WITH))
                    {
                        overwrite = ParseWithOverwrite();
                    }
                    else if (path == null && (!LanguageMetadata.IsKeyword(_parser.Current.Value) || _parser.Current.Type == TokenType.STRING))
                    {
                        path = _parser.ParseExpression();
                    }
                    else if (_parser.Match(TokenType.LF) || _parser.Match(TokenType.CR) || _parser.Match(TokenType.CRLF))
                    {
                        continue;
                    }
                    else
                    {
                        break;
                    }
                }

                if (path == null) throw new SyntaxException($"Path is mandatory for {startToken.Value}", _parser.Current.Line, _parser.Current.Column);
            }

            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();

            return new DirectoryOperationStatement(type, path, extra, overwrite) { Line = startToken.Line, Column = startToken.Column };
        }

        private Expression? ParseWithOverwrite()
        {
            _parser.Consume(TokenType.LPAREN, "Expected '(' after WITH");
            Expression? overwrite = null;
            while (_parser.Current.Type != TokenType.RPAREN && _parser.Current.Type != TokenType.EOF)
            {
                string key = _parser.Advance().Value;
                _parser.Consume(TokenType.EQUALS, "Expected '=' after option name");
                if (string.Equals(key, "OVERWRITE", StringComparison.OrdinalIgnoreCase))
                {
                    overwrite = _parser.ParseExpression();
                }
                else
                {
                    _parser.ParseExpression();
                }

                if (!_parser.Match(TokenType.COMMA)) break;
            }
            _parser.Consume(TokenType.RPAREN, "Expected ')' after WITH options");
            return overwrite;
        }

        private Expression? ParseWithRecursive()
        {
            _parser.Consume(TokenType.LPAREN, "Expected '(' after WITH");
            Expression? recursive = null;
            while (_parser.Current.Type != TokenType.RPAREN && _parser.Current.Type != TokenType.EOF)
            {
                string key = _parser.Advance().Value;
                _parser.Consume(TokenType.EQUALS, "Expected '=' after option name");
                if (string.Equals(key, "RECURSIVE", StringComparison.OrdinalIgnoreCase))
                {
                    recursive = _parser.ParseExpression();
                }
                else
                {
                    _parser.ParseExpression();
                }

                if (!_parser.Match(TokenType.COMMA)) break;
            }
            _parser.Consume(TokenType.RPAREN, "Expected ')' after WITH options");
            return recursive;
        }

        private Statement ParseDockerClose()
        {
            var startToken = _parser.Previous;
            Expression? imageName = null;
            string? alias = null;
            
            bool hasParen = _parser.Match(TokenType.LPAREN);
            
            if (_parser.Current.Type != TokenType.SEMICOLON && _parser.Current.Type != TokenType.RPAREN) 
            {
                if (_parser.Current.Type == TokenType.STRING)
                {
                    imageName = _parser.ParseExpression();
                }
                else if (_parser.Current.Type == TokenType.IDENTIFIER)
                {
                    alias = _parser.ConsumeIdentifier("Expected alias").Value;
                }
            }

            if (hasParen) _parser.Consume(TokenType.RPAREN, "Expected ')'");

            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
            
            return new DockerCloseStatement(imageName, alias) { Line = startToken.Line, Column = startToken.Column };
        }

        private Statement ParseDocker()
        {
            if (_parser.Match(TokenType.CLOSE))
            {
                return ParseDockerClose();
            }
            
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
            bool hasParen = _parser.Match(TokenType.LPAREN);
            var alias = _parser.ConsumeIdentifier("Expected container alias").Value;
            if (hasParen) _parser.Consume(TokenType.RPAREN, "Expected ')'");
            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
            return new DockerActionStatement(alias, action) { Line = startToken.Line, Column = startToken.Column };
        }

        private Statement ParseLineage()
        {
            var startToken = _parser.Previous;
            _parser.Match(TokenType.LPAREN); 
            var targetTable = _parser.ParseTableReference();
            
            string? columnName = null;
            if (_parser.Match(TokenType.COMMA))
            {
                columnName = _parser.ConsumeIdentifier("Expected column name after comma").Value;
            }
            
            _parser.Match(TokenType.RPAREN); 

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

        private Statement ParseLint()
        {
            var token = _parser.Previous; 
            string? path = null;
            if (_parser.Match(TokenType.STRING))
            {
                path = _parser.Previous.Value;
            }
            _parser.Match(TokenType.SEMICOLON);
            return new LintStatement(path) { Line = token.Line, Column = token.Column };
        }

        private Statement ParseWaitFor()
        {
            var startToken = _parser.Previous;
            WaitType type = WaitType.Delay;
            if (_parser.Match(TokenType.TIME))
            {
                type = WaitType.Time;
            }
            else
            {
                _parser.Consume(TokenType.DELAY, "Expected DELAY or TIME after WAITFOR");
            }
            
            var expr = _parser.ParseExpression();
            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
            return new WaitForStatement(expr, type) { Line = startToken.Line, Column = startToken.Column };
        }

        private Statement ParseExecute()
        {
            var startToken = _parser.Current;
            if (_parser.Match(TokenType.LPAREN))
            {
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

            var identifierExpr = _parser.ParseExpression(); 
            
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
                bool unbalanced = false;
                string sqlText = "";
                try
                {
                    sqlText = _parser.CaptureRawBlock();
                }
                catch (SyntaxException)
                {
                    unbalanced = true;
                }

                if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
                return new ExecutePushdownStatement(identifierExpr, sqlText, remoteIntoTable, remoteParameters) { Line = startToken.Line, Column = startToken.Column, HasUnbalancedBlocks = unbalanced };
            }

            if (IsStatementStart(_parser.Current.Type))
            {
                if (_parser.Current.Type == TokenType.SELECT)
                {
                    bool hasIntoTemp = false;
                    int i = 1;
                    while (true)
                    {
                        var tok = _parser.LookAhead(i++);
                        if (tok.Type == TokenType.EOF || tok.Type == TokenType.SEMICOLON || tok.Type == TokenType.FROM) break;
                        if (tok.Type == TokenType.INTO)
                        {
                            var next = _parser.LookAhead(i);
                            if (next.Value.StartsWith("#")) hasIntoTemp = true;
                            break;
                        }
                    }

                    if (hasIntoTemp)
                    {
                        string sqlText = "";
                        while (_parser.Current.Type != TokenType.SEMICOLON && _parser.Current.Type != TokenType.EOF && _parser.Current.Type != TokenType.END)
                        {
                            if (_parser.Current.Type == TokenType.INTO && _parser.LookAhead(1).Value.StartsWith("#"))
                            {
                                _parser.Advance(); 
                                var tempName = _parser.Advance().Value;
                                remoteIntoTable = new TableReference(tempName);
                                continue;
                            }

                            var t = _parser.Advance();
                            string val = t.Type == TokenType.STRING ? $"'{t.Value.Replace("'", "''")}'" : t.Value;
                            sqlText += val;
                            
                            var next = _parser.Current.Type;
                            bool needsSpace = true;
                            if (next == TokenType.DOT || next == TokenType.COMMA || next == TokenType.LPAREN || next == TokenType.RPAREN || next == TokenType.SEMICOLON) needsSpace = false;
                            if (t.Type == TokenType.DOT || t.Type == TokenType.LPAREN) needsSpace = false;
                            
                            if (needsSpace) sqlText += " ";
                        }
                        if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
                        return new ExecutePushdownStatement(identifierExpr, sqlText.Trim(), remoteIntoTable, remoteParameters) { Line = startToken.Line, Column = startToken.Column };
                    }
                }

                var stmt = ParseStatement();
                var block = new BlockStatement(new List<Statement> { stmt });
                return new ExecuteRemoteBlockStatement(identifierExpr, block);
            }

            string procedureName = identifierExpr.ToSql();
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
            
            if (unit.EndsWith("S")) unit = unit.Substring(0, unit.Length - 1);

            string? atTime = null;
            if (_parser.Match(TokenType.AT))
            {
                atTime = _parser.Consume(TokenType.STRING, "Expected time string (e.g. '02:00') after AT").Value;
            }

            return new ScheduleInfo(interval, unit, atTime);
        }

        private Statement ParseCreateSets(Token startToken)
        {
            _parser.Consume(TokenType.BANG, "Expected '!' before set name in CREATE SETS");
            var name = _parser.ConsumeIdentifier("Expected set name after '!'").Value;

            _parser.Consume(TokenType.BEGIN, "Expected BEGIN after set name");

            var assignments = new List<SetsAssignment>();
            bool withPrompt = false;

            while (_parser.Current.Type != TokenType.END && _parser.Current.Type != TokenType.EOF)
            {
                if (_parser.Current.Type == TokenType.SET)
                {
                    _parser.Advance();
                    _parser.ConsumeIdentifier("Expected WITH_PROMPT after SET");
                    if (_parser.Match(TokenType.ON)) withPrompt = true;
                    else if (_parser.Match(TokenType.OFF)) withPrompt = false;
                    _parser.Match(TokenType.SEMICOLON);
                    continue;
                }

                if (_parser.Current.Type != TokenType.VARIABLE) break;
                var varName = _parser.Advance().Value.TrimStart('@');
                _parser.Consume(TokenType.EQUALS, "Expected '='");
                var valueExpr = _parser.ParseExpression();
                assignments.Add(new SetsAssignment(varName, valueExpr));

                _parser.Match(TokenType.COMMA);
                _parser.Match(TokenType.SEMICOLON);
            }

            _parser.Consume(TokenType.END, "Expected END");
            _parser.Match(TokenType.SEMICOLON);

            return new CreateSetsStatement(name, assignments, withPrompt) { Line = startToken.Line, Column = startToken.Column };
        }

        private Statement ParseStatementWithCte()
        {
            var startToken = _parser.Previous; 
            bool isRecursive = _parser.Match(TokenType.RECURSIVE);

            var ctes = ParseCtes();
            var stmt = ParseStatement();
            
            stmt = stmt with { Ctes = ctes };
            if (stmt is SelectStatement select && isRecursive)
            {
                stmt = select with { IsRecursive = true };
            }
            
            return stmt;
        }

        private List<CteDefinition> ParseCtes()
        {
            var ctes = new List<CteDefinition>();
            do
            {
                string name;
                if (_parser.Current.Type == TokenType.IDENTIFIER || LanguageMetadata.IsKeyword(_parser.Current.Value))
                {
                    name = _parser.Advance().Value;
                }
                else
                {
                    throw new SyntaxException("Expected CTE name", _parser.Current.Line, _parser.Current.Column);
                }

                _parser.Consume(TokenType.AS, "Expected 'AS'");
                _parser.Consume(TokenType.LPAREN, "Expected '('");
                var subq = _parser.ParseQuery();
                _parser.Consume(TokenType.RPAREN, "Expected ')'");
                ctes.Add(new CteDefinition(name, subq));
            } while (_parser.Match(TokenType.COMMA));
            return ctes;
        }
    }
}
