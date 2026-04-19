using System;
using System.Collections.Generic;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Core.Parser.Components
{
    public class ExtensionParser : ParserComponent
    {
        public ExtensionParser(IParser parser, StatementParser parent) : base(parser, parent) { }

        public Statement ParseSendEmail(bool isSqlStyle)
        {
            var startToken = _parser.Previous;
            Expression? to = null, from = null, subject = null, body = null, connectionName = null;
            var attachments = new List<Expression>();
            var cc  = new List<Expression>();
            var bcc = new List<Expression>();

            if (!isSqlStyle && _parser.Current.Type != TokenType.LPAREN) isSqlStyle = true;

            if (isSqlStyle)
            {
                while (true)
                {
                    if (Match(TokenType.TO)) { to = ParseExpression(); }
                    else if (Match(TokenType.FROM)) { from = ParseExpression(); }
                    else if (Match(TokenType.SUBJECT)) { subject = ParseExpression(); }
                    else if (Match(TokenType.BODY)) { body = ParseExpression(); }
                    else if (Match(TokenType.CC))
                    {
                        if (Match(TokenType.LBRACKET))
                        {
                            if (_parser.Current.Type != TokenType.RBRACKET) { cc.Add(ParseExpression()); while (Match(TokenType.COMMA)) cc.Add(ParseExpression()); }
                            Consume(TokenType.RBRACKET, "Expected ']'");
                        }
                        else { cc.Add(ParseExpression()); while (Match(TokenType.COMMA)) cc.Add(ParseExpression()); }
                    }
                    else if (Match(TokenType.BCC))
                    {
                        if (Match(TokenType.LBRACKET))
                        {
                            if (_parser.Current.Type != TokenType.RBRACKET) { bcc.Add(ParseExpression()); while (Match(TokenType.COMMA)) bcc.Add(ParseExpression()); }
                            Consume(TokenType.RBRACKET, "Expected ']'");
                        }
                        else { bcc.Add(ParseExpression()); while (Match(TokenType.COMMA)) bcc.Add(ParseExpression()); }
                    }
                    else if (Match(TokenType.ATTACH))
                    {
                        if (Match(TokenType.LBRACKET))
                        {
                            if (_parser.Current.Type != TokenType.RBRACKET) { attachments.Add(ParseExpression()); while (Match(TokenType.COMMA)) attachments.Add(ParseExpression()); }
                            Consume(TokenType.RBRACKET, "Expected ']'");
                        }
                        else { attachments.Add(ParseExpression()); while (Match(TokenType.COMMA)) attachments.Add(ParseExpression()); }
                    }
                    else if (Match(TokenType.AT)) { connectionName = ParseExpression(); }
                    else if (Match(TokenType.LF) || Match(TokenType.CR) || Match(TokenType.CRLF)) { continue; }
                    else break;
                }

                if (to == null)      throw new SyntaxException("Email TO is mandatory", _parser.Current.Line, _parser.Current.Column);
                if (from == null)    throw new SyntaxException("Email FROM is mandatory", _parser.Current.Line, _parser.Current.Column);
                if (subject == null) throw new SyntaxException("Email SUBJECT is mandatory", _parser.Current.Line, _parser.Current.Column);
                if (body == null)    throw new SyntaxException("Email BODY is mandatory", _parser.Current.Line, _parser.Current.Column);
                if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
            }
            else
            {
                Consume(TokenType.LPAREN, "Expected '('");
                connectionName = ParseExpression(); Consume(TokenType.COMMA, "Expected ','");
                to = ParseExpression();             Consume(TokenType.COMMA, "Expected ','");
                from = ParseExpression();           Consume(TokenType.COMMA, "Expected ','");
                subject = ParseExpression();        Consume(TokenType.COMMA, "Expected ','");
                body = ParseExpression();

                if (Match(TokenType.COMMA))
                {
                    if (Match(TokenType.LBRACKET))
                    {
                        if (_parser.Current.Type != TokenType.RBRACKET) { cc.Add(ParseExpression()); while (Match(TokenType.COMMA)) cc.Add(ParseExpression()); }
                        Consume(TokenType.RBRACKET, "Expected ']'");
                    }
                    else if (_parser.Current.Type != TokenType.COMMA && _parser.Current.Type != TokenType.RPAREN)
                        cc.Add(ParseExpression());
                }
                if (Match(TokenType.COMMA))
                {
                    if (Match(TokenType.LBRACKET))
                    {
                        if (_parser.Current.Type != TokenType.RBRACKET) { bcc.Add(ParseExpression()); while (Match(TokenType.COMMA)) bcc.Add(ParseExpression()); }
                        Consume(TokenType.RBRACKET, "Expected ']'");
                    }
                    else if (_parser.Current.Type != TokenType.COMMA && _parser.Current.Type != TokenType.RPAREN)
                        bcc.Add(ParseExpression());
                }
                if (Match(TokenType.COMMA))
                {
                    if (Match(TokenType.LBRACKET))
                    {
                        if (_parser.Current.Type != TokenType.RBRACKET) { attachments.Add(ParseExpression()); while (Match(TokenType.COMMA)) attachments.Add(ParseExpression()); }
                        Consume(TokenType.RBRACKET, "Expected ']'");
                    }
                    else if (_parser.Current.Type != TokenType.COMMA && _parser.Current.Type != TokenType.RPAREN)
                        attachments.Add(ParseExpression());
                }
                Consume(TokenType.RPAREN, "Expected ')'");
            }

            var stmt = new EmailStatement(to, from, subject, body, connectionName)
            {
                IsSqlStyle  = isSqlStyle,
                Cc          = cc,
                Bcc         = bcc,
                Attachments = attachments,
                Line        = startToken.Line,
                Column      = startToken.Column
            };
            if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
            return stmt;
        }

        public Statement ParseFileTransfer(FileTransferType type, bool isSqlStyle)
        {
            var startToken = _parser.Previous;
            Expression? localPath = null;
            string? connectionName = null;
            Expression? remotePath = null;
            Expression? overwrite = null;

            if (!isSqlStyle && _parser.Current.Type != TokenType.LPAREN) isSqlStyle = true;

            if (isSqlStyle)
            {
                while (true)
                {
                    if (Match(TokenType.FROM))
                    {
                        if (type == FileTransferType.Send) throw new SyntaxException("FROM is not valid for SEND FILE.", _parser.Current.Line, _parser.Current.Column);
                        remotePath = ParseExpression();
                    }
                    else if (Match(TokenType.TO))
                    {
                        if (type == FileTransferType.Send) remotePath = ParseExpression();
                        else localPath = ParseExpression();
                    }
                    else if (Match(TokenType.AT)) { connectionName = ConsumeIdentifier("Expected connection name after AT").Value; }
                    else if (Match(TokenType.WITH)) { overwrite = ParseWithOverwrite(); }
                    else if (Match(TokenType.COMMA)) { continue; }
                    else if (localPath == null && type == FileTransferType.Send && (!LanguageMetadata.IsKeyword(_parser.Current.Value) || _parser.Current.Type == TokenType.STRING))
                        localPath = ParseExpression();
                    else if (connectionName == null && _parser.Current.Type == TokenType.IDENTIFIER && !LanguageMetadata.IsKeyword(_parser.Current.Value))
                        connectionName = Advance().Value;
                    else if (remotePath == null && (!LanguageMetadata.IsKeyword(_parser.Current.Value) || _parser.Current.Type == TokenType.STRING))
                        remotePath = ParseExpression();
                    else if (localPath == null && type == FileTransferType.Receive && (!LanguageMetadata.IsKeyword(_parser.Current.Value) || _parser.Current.Type == TokenType.STRING))
                        localPath = ParseExpression();
                    else if (Match(TokenType.LF) || Match(TokenType.CR) || Match(TokenType.CRLF)) { continue; }
                    else break;
                }

                if (localPath == null && type == FileTransferType.Send)    throw new SyntaxException("Local source path is mandatory for SEND FILE", _parser.Current.Line, _parser.Current.Column);
                if (remotePath == null && type == FileTransferType.Receive) throw new SyntaxException("Remote source path is mandatory for RECEIVE FILE", _parser.Current.Line, _parser.Current.Column);
                if (connectionName == null) throw new SyntaxException("Connection name (using AT) is mandatory", _parser.Current.Line, _parser.Current.Column);
                if (remotePath == null && type == FileTransferType.Send)   throw new SyntaxException("Remote destination path (using TO) is mandatory for SEND FILE", _parser.Current.Line, _parser.Current.Column);
                if (localPath == null && type == FileTransferType.Receive)  throw new SyntaxException("Local destination path is mandatory for RECEIVE FILE", _parser.Current.Line, _parser.Current.Column);
                if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
            }
            else
            {
                Consume(TokenType.LPAREN, "Expected '('");
                if (type == FileTransferType.Send)
                {
                    localPath = ParseExpression();       Consume(TokenType.COMMA, "Expected ',' after local path");
                    connectionName = ConsumeIdentifier("Expected connection name").Value; Consume(TokenType.COMMA, "Expected ',' after connection name");
                    remotePath = ParseExpression();
                }
                else
                {
                    connectionName = ConsumeIdentifier("Expected connection name").Value; Consume(TokenType.COMMA, "Expected ',' after connection name");
                    remotePath = ParseExpression();      Consume(TokenType.COMMA, "Expected ',' after remote path");
                    localPath = ParseExpression();
                }
                if (Match(TokenType.COMMA)) overwrite = ParseExpression();
                Consume(TokenType.RPAREN, "Expected ')'");
            }

            var stmt = new FileTransferStatement
            {
                Type           = type,
                LocalPath      = localPath!,
                ConnectionName = connectionName!,
                RemotePath     = remotePath!,
                Overwrite      = overwrite,
                IsSqlStyle     = isSqlStyle,
                Line           = startToken.Line,
                Column         = startToken.Column
            };
            if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
            return stmt;
        }

        public Statement ParseFileOperation(Token startToken)
        {
            FileOpType type = startToken.Type switch
            {
                TokenType.COPY_FILE or TokenType.COPY       => FileOpType.Copy,
                TokenType.MOVE_FILE or TokenType.MOVE       => FileOpType.Move,
                TokenType.RENAME_FILE or TokenType.RENAME   => FileOpType.Rename,
                TokenType.DELETE_FILE or TokenType.DELETE   => FileOpType.Delete,
                TokenType.COMPRESS_FILE or TokenType.COMPRESS => FileOpType.Compress,
                TokenType.ENCRYPT_FILE or TokenType.ENCRYPT => FileOpType.Encrypt,
                TokenType.DECRYPT_FILE or TokenType.DECRYPT => FileOpType.Decrypt,
                _ => throw new SyntaxException($"Unexpected file operation: {startToken.Type}", startToken.Line, startToken.Column)
            };

            Expression? source = null, dest = null, overwrite = null, password = null;
            bool isFunctionStyle = Match(TokenType.LPAREN);

            if (isFunctionStyle)
            {
                source = ParseExpression();
                if (Match(TokenType.COMMA)) dest = ParseExpression();
                if (Match(TokenType.COMMA)) overwrite = ParseExpression();
                if (Match(TokenType.COMMA)) password = ParseExpression();
                Consume(TokenType.RPAREN, "Expected ')' after arguments");
            }
            else
            {
                while (true)
                {
                    if (Match(TokenType.TO)) { dest = ParseExpression(); }
                    else if (Match(TokenType.PASSWORD))
                    {
                        if (_parser.Current.Type == TokenType.LPAREN) { Advance(); password = ParseExpression(); Consume(TokenType.RPAREN, "Expected ')' after PASSWORD value"); }
                        else password = ParseExpression();
                    }
                    else if (Match(TokenType.WITH)) { overwrite = ParseWithOverwrite(); }
                    else if (source == null && (!LanguageMetadata.IsKeyword(_parser.Current.Value) || _parser.Current.Type == TokenType.STRING))
                        source = ParseExpression();
                    else if (Match(TokenType.LF) || Match(TokenType.CR) || Match(TokenType.CRLF)) { continue; }
                    else break;
                }
                if (source == null) throw new SyntaxException($"Source path is mandatory for {startToken.Value}", _parser.Current.Line, _parser.Current.Column);
            }

            if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
            return new FileOperationStatement(type, source, dest, overwrite, password) { Line = startToken.Line, Column = startToken.Column };
        }

        public Statement ParseDirectoryOperation(Token startToken)
        {
            DirectoryOpType type = startToken.Type switch
            {
                TokenType.CREATE_DIRECTORY                          => DirectoryOpType.Create,
                TokenType.DELETE_DIRECTORY or TokenType.DELETE      => DirectoryOpType.Delete,
                TokenType.RENAME_DIRECTORY or TokenType.RENAME      => DirectoryOpType.Rename,
                TokenType.MOVE_DIRECTORY or TokenType.MOVE          => DirectoryOpType.Move,
                TokenType.COPY_DIRECTORY or TokenType.COPY          => DirectoryOpType.Copy,
                TokenType.DELETE_DIRECTORY_CONTENTS                 => DirectoryOpType.DeleteContents,
                TokenType.COMPRESS_DIRECTORY or TokenType.COMPRESS  => DirectoryOpType.Compress,
                TokenType.ENCRYPT_DIRECTORY or TokenType.ENCRYPT    => DirectoryOpType.Encrypt,
                TokenType.DECRYPT_DIRECTORY or TokenType.DECRYPT    => DirectoryOpType.Decrypt,
                _ => throw new SyntaxException($"Unexpected directory operation: {startToken.Type}", startToken.Line, startToken.Column)
            };

            Expression? path = null, extra = null, overwrite = null, password = null;
            bool isFunctionStyle = Match(TokenType.LPAREN);

            if (isFunctionStyle)
            {
                path = ParseExpression();
                if (Match(TokenType.COMMA)) extra = ParseExpression();
                if (Match(TokenType.COMMA)) overwrite = ParseExpression();
                if (Match(TokenType.COMMA)) password = ParseExpression();
                Consume(TokenType.RPAREN, "Expected ')' after arguments");
            }
            else
            {
                while (true)
                {
                    if (Match(TokenType.TO)) { extra = ParseExpression(); }
                    else if (Match(TokenType.PASSWORD))
                    {
                        if (_parser.Current.Type == TokenType.LPAREN) { Advance(); password = ParseExpression(); Consume(TokenType.RPAREN, "Expected ')' after PASSWORD value"); }
                        else password = ParseExpression();
                    }
                    else if (Match(TokenType.WITH)) { overwrite = ParseWithOverwrite(); }
                    else if (path == null && (!LanguageMetadata.IsKeyword(_parser.Current.Value) || _parser.Current.Type == TokenType.STRING))
                        path = ParseExpression();
                    else if (Match(TokenType.LF) || Match(TokenType.CR) || Match(TokenType.CRLF)) { continue; }
                    else break;
                }
                if (path == null) throw new SyntaxException($"Path is mandatory for {startToken.Value}", _parser.Current.Line, _parser.Current.Column);
            }

            if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
            return new DirectoryOperationStatement(type, path, extra, overwrite, null, password) { Line = startToken.Line, Column = startToken.Column };
        }

        public Statement ParseDockerClose(Token startToken)
        {
            Expression? imageName = null;
            string? alias = null;
            bool hasParen = Match(TokenType.LPAREN);
            if (_parser.Current.Type != TokenType.SEMICOLON && _parser.Current.Type != TokenType.RPAREN)
            {
                if (_parser.Current.Type == TokenType.STRING) imageName = ParseExpression();
                else if (_parser.Current.Type == TokenType.IDENTIFIER) alias = ConsumeIdentifier("Expected alias").Value;
            }
            if (hasParen) Consume(TokenType.RPAREN, "Expected ')'");
            if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
            return new DockerCloseStatement(imageName, alias) { Line = startToken.Line, Column = startToken.Column };
        }

        public Statement ParseDocker()
        {
            if (Match(TokenType.CLOSE)) return ParseDockerClose(_parser.Previous);
            var startToken = _parser.Previous;
            var alias = ConsumeIdentifier("Expected container alias").Value;
            var actionStr = ConsumeIdentifier("Expected action (START, STOP, PAUSE, RESUME, CLOSE)").Value.ToUpperInvariant();
            if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
            var action = actionStr switch
            {
                "START"  => DockerAction.Start,
                "STOP"   => DockerAction.Stop,
                "PAUSE"  => DockerAction.Pause,
                "RESUME" => DockerAction.Resume,
                "CLOSE"  => DockerAction.Close,
                _ => throw new SyntaxException($"Unknown Docker action: {actionStr}", startToken.Line, startToken.Column)
            };
            if (action == DockerAction.Close) return new DockerCloseStatement(null, alias) { Line = startToken.Line, Column = startToken.Column };
            return new DockerActionStatement(alias, action) { Line = startToken.Line, Column = startToken.Column };
        }

        public Statement ParseDockerAction(DockerAction action)
        {
            var startToken = _parser.Previous;
            bool hasParen = Match(TokenType.LPAREN);
            var alias = ConsumeIdentifier("Expected container alias").Value;
            if (hasParen) Consume(TokenType.RPAREN, "Expected ')'");
            if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
            return new DockerActionStatement(alias, action) { Line = startToken.Line, Column = startToken.Column };
        }

        public Statement ParseLineage(Token startToken)
        {
            Match(TokenType.LPAREN);
            var targetTable = _parser.ParseTableReference();
            string? columnName = null;
            if (Match(TokenType.COMMA)) columnName = ConsumeIdentifier("Expected column name after comma").Value;
            Match(TokenType.RPAREN);
            string? exportPath = null;
            if (Match(TokenType.TO)) exportPath = Consume(TokenType.STRING, "Expected file path after TO").Value;
            if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
            return new LineageStatement(targetTable, columnName, exportPath) { Line = startToken.Line, Column = startToken.Column };
        }

        public Statement ParseLint(Token startToken)
        {
            string? path = null;
            if (Match(TokenType.STRING)) path = _parser.Previous.Value;
            Match(TokenType.SEMICOLON);
            return new LintStatement(path) { Line = startToken.Line, Column = startToken.Column };
        }

        public Statement ParseWaitFor(Token startToken)
        {
            if (Match(TokenType.LPAREN))
            {
                if (Match(TokenType.DELAY))
                {
                    var e = ParseExpression(); Consume(TokenType.RPAREN, "Expected ')'");
                    if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
                    return new WaitForStatement(e, WaitType.Delay) { Line = startToken.Line, Column = startToken.Column };
                }
                if (Match(TokenType.TIME))
                {
                    var e = ParseExpression(); Consume(TokenType.RPAREN, "Expected ')'");
                    if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
                    return new WaitForStatement(e, WaitType.Time) { Line = startToken.Line, Column = startToken.Column };
                }
                var condition = ParseExpression();
                Consume(TokenType.RPAREN, "Expected ')' after WAITFOR condition");
                if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
                return new WaitForStatement(condition, WaitType.Until) { Line = startToken.Line, Column = startToken.Column };
            }

            WaitType type = WaitType.Delay;
            if (Match(TokenType.TIME)) type = WaitType.Time;
            else Consume(TokenType.DELAY, "Expected DELAY, TIME, or (condition) after WAITFOR");
            var expr = ParseExpression();
            if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
            return new WaitForStatement(expr, type) { Line = startToken.Line, Column = startToken.Column };
        }

        public Statement ParseWait(Token startToken)
        {
            Consume(TokenType.UNTIL, "Expected UNTIL after WAIT");
            var condition = ParseExpression();
            if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
            return new WaitForStatement(condition, WaitType.Until) { Line = startToken.Line, Column = startToken.Column };
        }

        public Statement ParseExecute()
        {
            var startToken = _parser.Current;
            if (Match(TokenType.LPAREN))
            {
                if (_parent.IsStatementStart(_parser.Current.Type))
                {
                    var statements = new List<Statement>();
                    while (_parser.Current.Type != TokenType.RPAREN && _parser.Current.Type != TokenType.EOF)
                        statements.Add(_parser.ParseStatement());
                    Consume(TokenType.RPAREN, "Expected ')' after EXEC SQL block");

                    Expression? blockConnName = null;
                    if (Match(TokenType.AT)) blockConnName = ParseExpression();
                    if (_parser.Current.Type == TokenType.SEMICOLON) Advance();

                    if (blockConnName == null) return new BlockStatement(statements);
                    return new ExecuteRemoteBlockStatement(blockConnName, new BlockStatement(statements));
                }
                else
                {
                    var sqlExpr = ParseExpression();
                    Consume(TokenType.RPAREN, "Expected ')' after EXEC SQL expression");

                    Expression? execConnName = null;
                    if (Match(TokenType.AT)) execConnName = ParseExpression();

                    TableReference? execIntoTable = null;
                    if (Match(TokenType.INTO)) execIntoTable = ParseTableReference(false);

                    List<Expression>? execParameters = null;
                    if (Match(TokenType.WITH))
                    {
                        Consume(TokenType.LPAREN, "Expected '(' after WITH");
                        execParameters = new List<Expression>();
                        if (_parser.Current.Type != TokenType.RPAREN)
                        {
                            execParameters.Add(ParseExpression());
                            while (Match(TokenType.COMMA)) execParameters.Add(ParseExpression());
                        }
                        Consume(TokenType.RPAREN, "Expected ')' after WITH parameters");
                    }

                    if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
                    return new ExecStatement(sqlExpr, execConnName, execIntoTable, execParameters);
                }
            }

            var identifierExpr = ParseExpression();

            TableReference? remoteIntoTable = null;
            if (Match(TokenType.INTO)) remoteIntoTable = ParseTableReference(false);

            List<Expression>? remoteParameters = null;
            if (Match(TokenType.WITH))
            {
                Consume(TokenType.LPAREN, "Expected '(' after WITH");
                remoteParameters = new List<Expression>();
                if (_parser.Current.Type != TokenType.RPAREN)
                {
                    remoteParameters.Add(ParseExpression());
                    while (Match(TokenType.COMMA)) remoteParameters.Add(ParseExpression());
                }
                Consume(TokenType.RPAREN, "Expected ')' after WITH parameters");
            }

            if (_parser.Current.Type == TokenType.BEGIN)
            {
                bool unbalanced = false;
                string sqlText = "";
                try { sqlText = _parser.CaptureRawBlock(); }
                catch (SyntaxException) { unbalanced = true; }
                if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
                return new ExecutePushdownStatement(identifierExpr, sqlText, remoteIntoTable, remoteParameters) { Line = startToken.Line, Column = startToken.Column, HasUnbalancedBlocks = unbalanced };
            }

            if (_parent.IsStatementStart(_parser.Current.Type))
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
                                Advance();
                                var tempName = Advance().Value;
                                remoteIntoTable = new TableReference(tempName);
                                continue;
                            }
                            var t = Advance();
                            string val = t.Type == TokenType.STRING ? $"'{t.Value.Replace("'", "''")}'" : t.Value;
                            sqlText += val;
                            var next = _parser.Current.Type;
                            bool needsSpace = next != TokenType.DOT && next != TokenType.COMMA && next != TokenType.LPAREN && next != TokenType.RPAREN && next != TokenType.SEMICOLON
                                           && t.Type != TokenType.DOT && t.Type != TokenType.LPAREN;
                            if (needsSpace) sqlText += " ";
                        }
                        if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
                        return new ExecutePushdownStatement(identifierExpr, sqlText.Trim(), remoteIntoTable, remoteParameters) { Line = startToken.Line, Column = startToken.Column };
                    }
                }

                var stmt = _parser.ParseStatement();
                return new ExecuteRemoteBlockStatement(identifierExpr, new BlockStatement(new List<Statement> { stmt }));
            }

            string procedureName = identifierExpr.ToSql();
            if (identifierExpr is LiteralExpression lit && lit.Value is string s) procedureName = s;

            var procParameters = new List<ExecuteParameter>();
            if (_parser.Current.Type != TokenType.SEMICOLON && _parser.Current.Type != TokenType.EOF && _parser.Current.Type != TokenType.END)
            {
                procParameters.Add(ParseExecuteParameter());
                while (Match(TokenType.COMMA)) procParameters.Add(ParseExecuteParameter());
            }

            if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
            return new ExecuteStatement(procedureName, procParameters);
        }

        private ExecuteParameter ParseExecuteParameter()
        {
            var expr = ParseExpression();
            bool isOutput = Match(TokenType.OUTPUT);
            bool isInput  = Match(TokenType.INPUT);
            return new ExecuteParameter(expr, isOutput, isInput);
        }
    }
}
