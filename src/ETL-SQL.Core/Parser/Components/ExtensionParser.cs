using System;
using System.Collections.Generic;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Core.Parser.Components;

public class ExtensionParser : ParserComponent
{
    public ExtensionParser(IParser parser, StatementParser parent) : base(parser, parent) { }

    public Statement ParseSendEmail(bool isSqlStyle)
    {
        var startToken = _parser.Previous;
        Expression? to = null, from = null, subject = null, body = null, connectionName = null;
        var attachments = new List<Expression>();
        var cc = new List<Expression>();
        var bcc = new List<Expression>();

        if (!isSqlStyle && _parser.Current.Type != TokenType.LPAREN) isSqlStyle = true;

        if (isSqlStyle)
        {
            if (_parser.Current.Type == TokenType.LPAREN)
                throw new SyntaxException("Function-style SEND EMAIL has been retired. Use SEND EMAIL TO ... FROM ... SUBJECT ... BODY ... AT <connection>.", startToken.Line, startToken.Column);

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

            if (to == null) throw new SyntaxException("SEND EMAIL requires a TO clause", _parser.Current.Line, _parser.Current.Column);
            if (from == null) throw new SyntaxException("SEND EMAIL requires a FROM clause", _parser.Current.Line, _parser.Current.Column);
            if (subject == null) throw new SyntaxException("SEND EMAIL requires a SUBJECT clause", _parser.Current.Line, _parser.Current.Column);
            if (body == null) throw new SyntaxException("SEND EMAIL requires a BODY clause", _parser.Current.Line, _parser.Current.Column);
            if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
        }
        else
        {
            Consume(TokenType.LPAREN, "Expected '('");
            connectionName = ParseExpression(); Consume(TokenType.COMMA, "Expected ','");
            to = ParseExpression(); Consume(TokenType.COMMA, "Expected ','");
            from = ParseExpression(); Consume(TokenType.COMMA, "Expected ','");
            subject = ParseExpression(); Consume(TokenType.COMMA, "Expected ','");
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
            IsSqlStyle = isSqlStyle,
            Cc = cc,
            Bcc = bcc,
            Attachments = attachments,
            Line = startToken.Line,
            Column = startToken.Column
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
            if (_parser.Current.Type == TokenType.LPAREN)
            {
                var replacement = type == FileTransferType.Send
                    ? "SEND FILE <local> TO <remote> AT <connection>"
                    : "RECEIVE FILE FROM <remote> TO <local> AT <connection>";
                throw new SyntaxException($"Function-style {(type == FileTransferType.Send ? "SEND FILE" : "RECEIVE FILE")} has been retired. Use {replacement}.", startToken.Line, startToken.Column);
            }

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
                else if (localPath == null && type == FileTransferType.Send && (!LanguageMetadata.IsKeyword(_parser.Current.Value) || _parser.Current.Type == TokenType.STRING_LITERAL))
                    localPath = ParseExpression();
                else if (connectionName == null && _parser.Current.Type == TokenType.IDENTIFIER && !LanguageMetadata.IsKeyword(_parser.Current.Value))
                    connectionName = Advance().Value;
                else if (remotePath == null && (!LanguageMetadata.IsKeyword(_parser.Current.Value) || _parser.Current.Type == TokenType.STRING_LITERAL))
                    remotePath = ParseExpression();
                else if (localPath == null && type == FileTransferType.Receive && (!LanguageMetadata.IsKeyword(_parser.Current.Value) || _parser.Current.Type == TokenType.STRING_LITERAL))
                    localPath = ParseExpression();
                else if (Match(TokenType.LF) || Match(TokenType.CR) || Match(TokenType.CRLF)) { continue; }
                else break;
            }

            if (localPath == null && type == FileTransferType.Send) throw new SyntaxException("Local source path is mandatory for SEND FILE", _parser.Current.Line, _parser.Current.Column);
            if (remotePath == null && type == FileTransferType.Receive) throw new SyntaxException("Remote source path is mandatory for RECEIVE FILE", _parser.Current.Line, _parser.Current.Column);
            if (connectionName == null) throw new SyntaxException("Connection name (using AT) is mandatory", _parser.Current.Line, _parser.Current.Column);
            if (remotePath == null && type == FileTransferType.Send) throw new SyntaxException("Remote destination path (using TO) is mandatory for SEND FILE", _parser.Current.Line, _parser.Current.Column);
            if (localPath == null && type == FileTransferType.Receive) throw new SyntaxException("Local destination path is mandatory for RECEIVE FILE", _parser.Current.Line, _parser.Current.Column);
            if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
        }
        else
        {
            Consume(TokenType.LPAREN, "Expected '('");
            if (type == FileTransferType.Send)
            {
                localPath = ParseExpression(); Consume(TokenType.COMMA, "Expected ',' after local path");
                connectionName = ConsumeIdentifier("Expected connection name").Value; Consume(TokenType.COMMA, "Expected ',' after connection name");
                remotePath = ParseExpression();
            }
            else
            {
                connectionName = ConsumeIdentifier("Expected connection name").Value; Consume(TokenType.COMMA, "Expected ',' after connection name");
                remotePath = ParseExpression(); Consume(TokenType.COMMA, "Expected ',' after remote path");
                localPath = ParseExpression();
            }
            if (Match(TokenType.COMMA)) overwrite = ParseExpression();
            Consume(TokenType.RPAREN, "Expected ')'");
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
        if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
        return stmt;
    }

    public Statement ParseFileOperation(Token startToken)
    {
        FileOpType type = startToken.Type switch
        {
            TokenType.COPY_FILE or TokenType.COPY => FileOpType.Copy,
            TokenType.MOVE_FILE or TokenType.MOVE => FileOpType.Move,
            TokenType.RENAME_FILE or TokenType.RENAME => FileOpType.Rename,
            TokenType.DELETE_FILE or TokenType.DELETE => FileOpType.Delete,
            TokenType.COMPRESS_FILE or TokenType.COMPRESS => FileOpType.Compress,
            TokenType.DECOMPRESS_FILE or TokenType.DECOMPRESS => FileOpType.Decompress,
            TokenType.ENCRYPT_FILE or TokenType.ENCRYPT => FileOpType.Encrypt,
            TokenType.DECRYPT_FILE or TokenType.DECRYPT => FileOpType.Decrypt,
            _ => throw new SyntaxException($"Unexpected file operation: {startToken.Type}", startToken.Line, startToken.Column)
        };

        Expression? source = null, dest = null, overwrite = null, password = null, keyFile = null, pgpKey = null;
        Expression? dateSuffix = null, suffixSeparator = null;
        string? connectionName = null;
        bool isFunctionStyle = Match(TokenType.LPAREN);
        if (isFunctionStyle)
            throw new SyntaxException($"Function-style {startToken.Value} has been retired. Use {startToken.Value} FILE ...", startToken.Line, startToken.Column);
        bool destinationIsDirectory = false;

        bool ifExists = false;
        if (isFunctionStyle)
        {
            source = ParseExpression();
            if (Match(TokenType.COMMA)) dest = ParseExpression();
            if (Match(TokenType.COMMA))
            {
                if (type == FileOpType.Encrypt || type == FileOpType.Decrypt)
                {
                    password = ParseExpression();
                    if (Match(TokenType.COMMA)) overwrite = ParseExpression();
                    if (Match(TokenType.COMMA)) keyFile = ParseExpression();
                    if (Match(TokenType.COMMA)) pgpKey = ParseExpression();
                }
                else
                {
                    overwrite = ParseExpression();
                    if (Match(TokenType.COMMA)) password = ParseExpression();
                    if (Match(TokenType.COMMA)) keyFile = ParseExpression();
                    if (Match(TokenType.COMMA)) pgpKey = ParseExpression();
                }
            }
            if (Match(TokenType.COMMA))
            {
                var ifExistsExpr = ParseExpression();
                if (ifExistsExpr is LiteralExpression lit && lit.Value is bool b) ifExists = b;
            }
            Consume(TokenType.RPAREN, "Expected ')' after arguments");
        }
        else
        {
            while (true)
            {
                if (Match(TokenType.TO))
                {
                    destinationIsDirectory = Match(TokenType.DIRECTORY);
                    dest = ParseExpression();
                }
                else if (Match(TokenType.PASSWORD))
                {
                    if (_parser.Current.Type == TokenType.LPAREN) { Advance(); password = ParseExpression(); Consume(TokenType.RPAREN, "Expected ')' after PASSWORD value"); }
                    else password = ParseExpression();
                }
                else if (Match(TokenType.KEYFILE)) { keyFile = ParseExpression(); }
                else if (Match(TokenType.PGP_KEY)) { pgpKey = ParseExpression(); }
                else if (Match(TokenType.WITH)) { ParseFileOperationOptions(ref overwrite, ref dateSuffix, ref suffixSeparator); }
                else if (Match(TokenType.AT)) { connectionName = ConsumeIdentifier("Expected connection name after AT").Value; }
                else if (source == null && (!LanguageMetadata.IsKeyword(_parser.Current.Value) || _parser.Current.Type == TokenType.STRING_LITERAL))
                    source = ParseExpression();
                else if (Match(TokenType.IF)) { Consume(TokenType.EXISTS, "Expected EXISTS after IF"); ifExists = true; }
                else if (Match(TokenType.LF) || Match(TokenType.CR) || Match(TokenType.CRLF)) { continue; }
                else break;
            }
            if (source == null) throw new SyntaxException($"Source path is mandatory for {startToken.Value}", _parser.Current.Line, _parser.Current.Column);
        }

        if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
        return new FileOperationStatement(type, source, dest, overwrite, password, keyFile, pgpKey, ifExists, connectionName, dateSuffix, suffixSeparator, destinationIsDirectory) { Line = startToken.Line, Column = startToken.Column };
    }

    private void ParseFileOperationOptions(ref Expression? overwrite, ref Expression? dateSuffix, ref Expression? suffixSeparator)
    {
        Consume(TokenType.LPAREN, "Expected '(' after WITH");
        while (_parser.Current.Type != TokenType.RPAREN && _parser.Current.Type != TokenType.EOF)
        {
            string key = Advance().Value;
            Consume(TokenType.EQUALS, "Expected '=' after option name");
            if (System.StringComparer.OrdinalIgnoreCase.Equals(key, "OVERWRITE"))
                overwrite = ParseExpression();
            else if (System.StringComparer.OrdinalIgnoreCase.Equals(key, "DATE_SUFFIX"))
                dateSuffix = ParseExpression();
            else if (System.StringComparer.OrdinalIgnoreCase.Equals(key, "SUFFIX_SEPARATOR"))
                suffixSeparator = ParseExpression();
            else
                ParseExpression();
            if (!Match(TokenType.COMMA)) break;
        }
        Consume(TokenType.RPAREN, "Expected ')' after WITH options");
    }

    public Statement ParseDirectoryOperation(Token startToken)
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
            TokenType.DECOMPRESS_DIRECTORY or TokenType.DECOMPRESS => DirectoryOpType.Decompress,
            TokenType.ENCRYPT_DIRECTORY or TokenType.ENCRYPT => DirectoryOpType.Encrypt,
            TokenType.DECRYPT_DIRECTORY or TokenType.DECRYPT => DirectoryOpType.Decrypt,
            _ => throw new SyntaxException($"Unexpected directory operation: {startToken.Type}", startToken.Line, startToken.Column)
        };

        Expression? path = null, extra = null, overwrite = null, recursive = null, password = null, keyFile = null, pgpKey = null;
        string? connectionName = null;
        bool ifExists = false;
        bool isFunctionStyle = Match(TokenType.LPAREN);
        if (isFunctionStyle)
            throw new SyntaxException($"Function-style {startToken.Value} has been retired. Use {startToken.Value} DIRECTORY ...", startToken.Line, startToken.Column);

        if (isFunctionStyle)
        {
            path = ParseExpression();
            if (Match(TokenType.COMMA)) extra = ParseExpression();
            if (Match(TokenType.COMMA))
            {
                if (type == DirectoryOpType.Encrypt || type == DirectoryOpType.Decrypt)
                {
                    password = ParseExpression();
                    if (Match(TokenType.COMMA)) overwrite = ParseExpression();
                    if (Match(TokenType.COMMA)) recursive = ParseExpression();
                    if (Match(TokenType.COMMA)) keyFile = ParseExpression();
                    if (Match(TokenType.COMMA)) pgpKey = ParseExpression();
                }
                else
                {
                    overwrite = ParseExpression();
                    if (Match(TokenType.COMMA)) recursive = ParseExpression();
                    if (Match(TokenType.COMMA)) password = ParseExpression();
                    if (Match(TokenType.COMMA)) keyFile = ParseExpression();
                    if (Match(TokenType.COMMA)) pgpKey = ParseExpression();
                }
            }
            if (Match(TokenType.COMMA))
            {
                var ifExistsExpr = ParseExpression();
                if (ifExistsExpr is LiteralExpression lit && lit.Value is bool b) ifExists = b;
            }
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
                else if (Match(TokenType.KEYFILE)) { keyFile = ParseExpression(); }
                else if (Match(TokenType.PGP_KEY)) { pgpKey = ParseExpression(); }
                else if (MatchIdentifier("RECURSIVE")) { recursive = ParseExpression(); }
                else if (Match(TokenType.WITH))
                {
                    Consume(TokenType.LPAREN, "Expected '(' after WITH");
                    while (_parser.Current.Type != TokenType.RPAREN && _parser.Current.Type != TokenType.EOF)
                    {
                        string key = Advance().Value;
                        Consume(TokenType.EQUALS, "Expected '=' after option name");
                        if (System.StringComparer.OrdinalIgnoreCase.Equals(key, "OVERWRITE"))
                            overwrite = ParseExpression();
                        else if (System.StringComparer.OrdinalIgnoreCase.Equals(key, "RECURSIVE"))
                            recursive = ParseExpression();
                        else if (System.StringComparer.OrdinalIgnoreCase.Equals(key, "KEYFILE"))
                            keyFile = ParseExpression();
                        else if (System.StringComparer.OrdinalIgnoreCase.Equals(key, "PGP_KEY"))
                            pgpKey = ParseExpression();
                        else
                            ParseExpression();
                        if (!Match(TokenType.COMMA)) break;
                    }
                    Consume(TokenType.RPAREN, "Expected ')' after WITH options");
                }
                else if (Match(TokenType.AT)) { connectionName = ConsumeIdentifier("Expected connection name after AT").Value; }
                else if (path == null && (!LanguageMetadata.IsKeyword(_parser.Current.Value) || _parser.Current.Type == TokenType.STRING_LITERAL))
                    path = ParseExpression();
                else if (Match(TokenType.IF)) { Consume(TokenType.EXISTS, "Expected EXISTS after IF"); ifExists = true; }
                else if (Match(TokenType.LF) || Match(TokenType.CR) || Match(TokenType.CRLF)) { continue; }
                else break;
            }
            if (path == null) throw new SyntaxException($"Path is mandatory for {startToken.Value}", _parser.Current.Line, _parser.Current.Column);
        }

        if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
        return new DirectoryOperationStatement(type, path, extra, overwrite, recursive, password, keyFile, pgpKey, ifExists, connectionName) { Line = startToken.Line, Column = startToken.Column };

    }

    public Statement ParseDockerVerb(DockerAction action)
    {
        var startToken = _parser.Previous;
        DockerTargetMode mode = DockerTargetMode.LastStarted;
        string? alias = null;

        if (startToken.Type == TokenType.DOCKER)
        {
            // Legacy: DOCKER CLOSE [<alias>], DOCKER STOP, etc.
            if (Match(TokenType.START)) action = DockerAction.Start;
            else if (Match(TokenType.STOP)) action = DockerAction.Stop;
            else if (Match(TokenType.PAUSE)) action = DockerAction.Pause;
            else if (Match(TokenType.CLOSE)) action = DockerAction.Close;

            if (_parser.Current.Type == TokenType.IDENTIFIER && !LanguageMetadata.IsKeyword(_parser.Current.Value))
            {
                alias = Advance().Value;
                mode = DockerTargetMode.Single;
            }
            else if (_parser.Current.Type == TokenType.STRING_LITERAL)
            {
                alias = Advance().Value.Trim('\'', '\"');
                mode = DockerTargetMode.Single;
            }

            _parser.Match(TokenType.SEMICOLON);
            return new DockerActionStatement(action, alias, mode) { Line = startToken.Line, Column = startToken.Column };
        }

        if (Match(TokenType.ALL))
        {
            Consume(TokenType.DOCKER, "Expected DOCKER after ALL");
            mode = DockerTargetMode.All;
        }
        else if (Match(TokenType.DOCKER))
        {
            // START DOCKER [<alias>]
            if (_parser.Current.Type == TokenType.IDENTIFIER && !LanguageMetadata.IsKeyword(_parser.Current.Value))
            {
                alias = Advance().Value;
                mode = DockerTargetMode.Single;
            }
            else if (_parser.Current.Type == TokenType.STRING_LITERAL)
            {
                alias = Advance().Value.Trim('\'', '\"');
                mode = DockerTargetMode.Single;
            }
        }
        else
        {
            throw new SyntaxException($"Expected DOCKER or ALL DOCKER after {startToken.Value}", _parser.Current.Line, _parser.Current.Column);
        }

        _parser.Match(TokenType.SEMICOLON);
        return new DockerActionStatement(action, alias, mode) { Line = startToken.Line, Column = startToken.Column };
    }

    public Statement ParseLineage(Token startToken)
    {
        // Legacy parser for an earlier draft spelling. Canonical file export is EXPORT LINEAGE.
        if (_parser.Current.Type == TokenType.EXPORT)
        {
            Advance(); // consume EXPORT
            Consume(TokenType.AS, "Expected AS after EXPORT");
            var olTok = _parser.Current;
            if (!olTok.Value.Equals("OPENLINEAGE", StringComparison.OrdinalIgnoreCase))
                throw new SyntaxException("Expected OPENLINEAGE after AS", olTok.Line, olTok.Column);
            Advance();
            Consume(TokenType.TO, "Expected TO after OPENLINEAGE");
            var path = Consume(TokenType.STRING_LITERAL, "Expected file path after TO").Value;
            Match(TokenType.SEMICOLON);
            return new LineageStatement(null, null, path, exportAsOpenLineage: true) { Line = startToken.Line, Column = startToken.Column };
        }

        Match(TokenType.LPAREN);
        var targetTable = _parser.ParseTableReference(allowAlias: false);
        string? columnName = null;
        if (Match(TokenType.COMMA)) columnName = ConsumeIdentifier("Expected column name after comma").Value;
        Match(TokenType.RPAREN);

        string? exportPath = null;
        bool asOpenLineage = false;
        if (_parser.Current.Type == TokenType.EXPORT)
        {
            Advance(); // consume EXPORT
            Consume(TokenType.AS, "Expected AS after EXPORT");
            var olTok = _parser.Current;
            if (!olTok.Value.Equals("OPENLINEAGE", StringComparison.OrdinalIgnoreCase))
                throw new SyntaxException("Expected OPENLINEAGE after AS", olTok.Line, olTok.Column);
            Advance();
            Consume(TokenType.TO, "Expected TO after OPENLINEAGE");
            exportPath = Consume(TokenType.STRING_LITERAL, "Expected file path after TO").Value;
            asOpenLineage = true;
        }
        else if (Match(TokenType.TO))
        {
            exportPath = Consume(TokenType.STRING_LITERAL, "Expected file path after TO").Value;
        }

        Match(TokenType.SEMICOLON);
        return new LineageStatement(targetTable, columnName, exportPath, asOpenLineage) { Line = startToken.Line, Column = startToken.Column };
    }

    public Statement ParseExportLineage(Token startToken)
    {
        TableReference? targetTable = null;
        string? columnName = null;

        if (Match(TokenType.FOR))
        {
            if (Match(TokenType.REPORT))
            {
                var reportName = ConsumeIdentifier("Expected report name after EXPORT LINEAGE FOR REPORT").Value;
                targetTable = new TableReference("report:" + reportName);
            }
            else if (Match(TokenType.DATASET))
            {
                var datasetName = ConsumeIdentifier("Expected dataset name after EXPORT LINEAGE FOR DATASET").Value;
                targetTable = new TableReference(datasetName.StartsWith("&") ? "dataset:" + datasetName[1..] : "dataset:" + datasetName);
            }
            else
            {
                if (Match(TokenType.TABLE)) { }
                targetTable = _parser.ParseTableReference(allowAlias: false);
                if (Match(TokenType.COLUMN))
                    columnName = ConsumeIdentifier("Expected column name after COLUMN").Value;
            }
        }

        Consume(TokenType.AS, "Expected AS after EXPORT LINEAGE");
        var format = ConsumeIdentifier("Expected export format after AS").Value;
        if (!format.Equals("OPENLINEAGE", StringComparison.OrdinalIgnoreCase))
            throw new SyntaxException("Expected OPENLINEAGE after AS", _parser.Previous.Line, _parser.Previous.Column);
        Consume(TokenType.TO, "Expected TO after OPENLINEAGE");
        var path = Consume(TokenType.STRING_LITERAL, "Expected file path after TO").Value;
        Match(TokenType.SEMICOLON);

        return new LineageStatement(targetTable, columnName, path, exportAsOpenLineage: true) { Line = startToken.Line, Column = startToken.Column };
    }

    public Statement ParseLint(Token startToken)
    {
        string? path = null;
        if (Match(TokenType.STRING_LITERAL)) path = _parser.Previous.Value;
        Match(TokenType.SEMICOLON);
        return new LintStatement(path) { Line = startToken.Line, Column = startToken.Column };
    }
    public Statement ParseWaitFor(Token startToken)
    {
        if (Match(TokenType.FILE))
        {
            Consume(TokenType.UNLOCKED, "Expected UNLOCKED after WAITFOR FILE");
            return ParseWaitForFileStatement(startToken);
        }

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
            throw new SyntaxException("WAITFOR (<condition>) has been retired. Use WAIT UNTIL <condition>.", startToken.Line, startToken.Column);
        }

        WaitType type = WaitType.Delay;
        if (Match(TokenType.TIME)) type = WaitType.Time;
        else if (!Match(TokenType.DELAY)) Consume(TokenType.DELAY, "Expected DELAY or TIME after WAITFOR. Use WAIT UNTIL <condition> for condition polling.");
        var expr = ParseExpression();
        if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
        return new WaitForStatement(expr, type) { Line = startToken.Line, Column = startToken.Column };
    }

    private WaitForFileStatement ParseWaitForFileStatement(Token startToken)
    {
        var path = ParseExpression();
        Expression? timeout = null;
        Expression? pollInterval = null;

        if (Match(TokenType.WITH))
        {
            Consume(TokenType.LPAREN, "Expected '(' after WITH");
            while (_parser.Current.Type != TokenType.RPAREN && _parser.Current.Type != TokenType.EOF)
            {
                string key = Advance().Value.ToUpperInvariant();
                Consume(TokenType.EQUALS, "Expected '=' after option key");
                var val = ParseExpression();
                if (key == "TIMEOUT") timeout = val;
                else if (key == "POLL_INTERVAL_MS") pollInterval = val;
                if (!Match(TokenType.COMMA)) break;
            }
            Consume(TokenType.RPAREN, "Expected ')' after WITH options");
        }
        else
        {
            while (true)
            {
                if (Match(TokenType.TIMEOUT))
                {
                    timeout = ParseExpression();
                }
                else if (_parser.Current.Type == TokenType.IDENTIFIER && _parser.Current.Value.Equals("POLL_INTERVAL_MS", StringComparison.OrdinalIgnoreCase))
                {
                    Advance();
                    pollInterval = ParseExpression();
                }
                else if (Match(TokenType.POLL_INTERVAL_MS))
                {
                    pollInterval = ParseExpression();
                }
                else break;
            }
        }
        if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
        return new WaitForFileStatement(path, timeout, pollInterval) { Line = startToken.Line, Column = startToken.Column };
    }

    public Statement ParseWait(Token startToken)
    {
        Consume(TokenType.UNTIL, "Expected UNTIL after WAIT");
        if (Match(TokenType.FILE))
        {
            Consume(TokenType.UNLOCKED, "Expected UNLOCKED after WAIT UNTIL FILE");
            return ParseWaitForFileStatement(startToken);
        }
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
                if (Match(TokenType.INTO)) execIntoTable = ParseTableReference(allowFunction: false, allowWithClause: false);

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

        Expression identifierExpr;
        if (_parser.Current.Type == TokenType.IDENTIFIER && _parser.Peek.Type == TokenType.LPAREN)
        {
            identifierExpr = new IdentifierExpression(Advance().Value);
        }
        else
        {
            identifierExpr = ParseExpression();
        }

        TableReference? remoteIntoTable = null;
        if (Match(TokenType.INTO)) remoteIntoTable = ParseTableReference(allowFunction: false, allowWithClause: false);

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
            return new ExecutePushdownStatement(identifierExpr, sqlText, remoteIntoTable, remoteParameters)
            { Line = startToken.Line, Column = startToken.Column, HasUnbalancedBlocks = unbalanced };
        }

        if (_parser.Current.Type == TokenType.LPAREN)
        {
            _parser.Advance();
            var sqlExpr = ParseExpression();
            _parser.Consume(TokenType.RPAREN, "Expected ')' after EXEC expression");
            if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
            return new ExecStatement(sqlExpr, identifierExpr, remoteIntoTable, remoteParameters);
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
                        string val = t.Type == TokenType.STRING_LITERAL ? $"'{t.Value.Replace("'", "''")}'" : t.Value;
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
        string? name = null;

        if (expr is BinaryExpression bin && bin.Operator == TokenType.EQUALS && bin.Left is VariableExpression varExpr)
        {
            name = varExpr.Name;
            expr = bin.Right;
        }

        bool isOutput = Match(TokenType.OUTPUT);
        bool isInput = Match(TokenType.INPUT);
        return new ExecuteParameter(expr, name, isOutput, isInput);
    }

    public Statement ParseConvertFileEncoding(Token startToken)
    {
        Consume(TokenType.FILE, "Expected FILE after CONVERT");
        Consume(TokenType.ENCODING, "Expected ENCODING after CONVERT FILE");
        var source = ParseExpression();
        Consume(TokenType.TO, "Expected TO after source file path");
        var destination = ParseExpression();

        Expression? fromEncoding = null;
        Expression? toEncoding = null;
        Expression? overwrite = null;

        if (Match(TokenType.WITH))
        {
            Consume(TokenType.LPAREN, "Expected '(' after WITH");
            while (_parser.Current.Type != TokenType.RPAREN && _parser.Current.Type != TokenType.EOF)
            {
                string key = Advance().Value.ToUpperInvariant();
                Consume(TokenType.EQUALS, "Expected '=' after option key");
                var val = ParseExpression();
                if (key == "FROM_ENCODING") fromEncoding = val;
                else if (key == "TO_ENCODING") toEncoding = val;
                else if (key == "OVERWRITE") overwrite = val;
                if (!Match(TokenType.COMMA)) break;
            }
            Consume(TokenType.RPAREN, "Expected ')' after WITH options");
        }

        if (fromEncoding == null) throw new SyntaxException("FROM_ENCODING option is mandatory in CONVERT FILE ENCODING", startToken.Line, startToken.Column);
        if (toEncoding == null) throw new SyntaxException("TO_ENCODING option is mandatory in CONVERT FILE ENCODING", startToken.Line, startToken.Column);

        if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
        return new ConvertFileEncodingStatement(source, destination, fromEncoding, toEncoding, overwrite) { Line = startToken.Line, Column = startToken.Column };
    }

    public Statement ParseSplitFile(Token startToken)
    {
        Consume(TokenType.FILE, "Expected FILE after SPLIT");
        var source = ParseExpression();
        Consume(TokenType.TO, "Expected TO after source file path");
        var destDir = ParseExpression();

        Expression? limitType = null;
        Expression? limitValue = null;
        Expression? prefix = null;
        Expression? overwrite = null;

        Consume(TokenType.WITH, "Expected WITH after destination path in SPLIT FILE");
        Consume(TokenType.LPAREN, "Expected '(' after WITH");
        while (_parser.Current.Type != TokenType.RPAREN && _parser.Current.Type != TokenType.EOF)
        {
            string key = Advance().Value.ToUpperInvariant();
            Consume(TokenType.EQUALS, "Expected '=' after option key");
            var val = ParseExpression();
            if (key == "LIMIT_TYPE") limitType = val;
            else if (key == "LIMIT_VALUE") limitValue = val;
            else if (key == "PREFIX") prefix = val;
            else if (key == "OVERWRITE") overwrite = val;
            if (!Match(TokenType.COMMA)) break;
        }
        Consume(TokenType.RPAREN, "Expected ')' after WITH options");

        if (limitType == null) throw new SyntaxException("LIMIT_TYPE option is mandatory in SPLIT FILE", startToken.Line, startToken.Column);
        if (limitValue == null) throw new SyntaxException("LIMIT_VALUE option is mandatory in SPLIT FILE", startToken.Line, startToken.Column);

        if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
        return new SplitFileStatement(source, destDir, limitType, limitValue, prefix, overwrite) { Line = startToken.Line, Column = startToken.Column };
    }

    public Statement ParseMergeFiles(Token startToken)
    {
        var source = ParseExpression();
        Consume(TokenType.TO, "Expected TO after source in MERGE FILES");
        var destination = ParseExpression();

        Expression? header = null;
        Expression? overwrite = null;

        if (Match(TokenType.WITH))
        {
            Consume(TokenType.LPAREN, "Expected '(' after WITH");
            while (_parser.Current.Type != TokenType.RPAREN && _parser.Current.Type != TokenType.EOF)
            {
                string key = Advance().Value.ToUpperInvariant();
                Consume(TokenType.EQUALS, "Expected '=' after option key");
                var val = ParseExpression();
                if (key == "HEADER") header = val;
                else if (key == "OVERWRITE") overwrite = val;
                if (!Match(TokenType.COMMA)) break;
            }
            Consume(TokenType.RPAREN, "Expected ')' after WITH options");
        }

        if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
        return new MergeFilesStatement(source, destination, header, overwrite) { Line = startToken.Line, Column = startToken.Column };
    }

    public Statement ParseSyncDirectory(Token startToken)
    {
        Consume(TokenType.DIRECTORY, "Expected DIRECTORY after SYNC");
        var source = ParseExpression();
        Consume(TokenType.TO, "Expected TO after source directory path");
        var destination = ParseExpression();

        Expression? deleteExtra = null;
        Expression? overwrite = null;
        Expression? recursive = null;

        if (Match(TokenType.WITH))
        {
            Consume(TokenType.LPAREN, "Expected '(' after WITH");
            while (_parser.Current.Type != TokenType.RPAREN && _parser.Current.Type != TokenType.EOF)
            {
                string key = Advance().Value.ToUpperInvariant();
                Consume(TokenType.EQUALS, "Expected '=' after option key");
                var val = ParseExpression();
                if (key == "DELETE_EXTRA") deleteExtra = val;
                else if (key == "OVERWRITE") overwrite = val;
                else if (key == "RECURSIVE") recursive = val;
                if (!Match(TokenType.COMMA)) break;
            }
            Consume(TokenType.RPAREN, "Expected ')' after WITH options");
        }

        if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
        return new SyncDirectoryStatement(source, destination, deleteExtra, overwrite, recursive) { Line = startToken.Line, Column = startToken.Column };
    }

    public Statement ParseVerifyFileIntegrity(Token startToken)
    {
        Consume(TokenType.FILE, "Expected FILE after VERIFY");
        Consume(TokenType.INTEGRITY, "Expected INTEGRITY after VERIFY FILE");
        var source = ParseExpression();

        Expression? hashFile = null;
        Expression? expectedHash = null;
        Expression? algorithm = null;

        Consume(TokenType.WITH, "Expected WITH after file path in VERIFY FILE INTEGRITY");
        Consume(TokenType.LPAREN, "Expected '(' after WITH");
        while (_parser.Current.Type != TokenType.RPAREN && _parser.Current.Type != TokenType.EOF)
        {
            string key = Advance().Value.ToUpperInvariant();
            Consume(TokenType.EQUALS, "Expected '=' after option key");
            var val = ParseExpression();
            if (key == "HASH_FILE") hashFile = val;
            else if (key == "EXPECTED_HASH") expectedHash = val;
            else if (key == "ALGORITHM") algorithm = val;
            if (!Match(TokenType.COMMA)) break;
        }
        Consume(TokenType.RPAREN, "Expected ')' after WITH options");

        if (hashFile == null && expectedHash == null)
            throw new SyntaxException("Either HASH_FILE or EXPECTED_HASH must be specified in VERIFY FILE INTEGRITY", startToken.Line, startToken.Column);

        if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
        return new VerifyFileIntegrityStatement(source, hashFile, expectedHash, algorithm) { Line = startToken.Line, Column = startToken.Column };
    }
}
