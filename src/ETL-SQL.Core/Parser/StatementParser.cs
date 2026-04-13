using System.Collections.Generic;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Core.Parser
{
    /// <summary>
    /// Recursive descent parser for statements in the ETL-SQL language.
    /// Handles everything from SELECT/INSERT to DOCKER and SEND_EMAIL.
    /// This file contains the core entry point and shared utilities.
    /// Functional parsing logic is split into partial classes:
    /// - StatementParser.Data.cs (DML/DDL)
    /// - StatementParser.Flow.cs (Conditionals/Loops/Blocks)
    /// - StatementParser.System.cs (Transactions/Variables/Metadata)
    /// - StatementParser.Extensions.cs (Docker/Email/FileOps/Lineage/Wait/Parallel)
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
            while (_parser.Match(TokenType.COLUMN_TAG)) { /* skip tags between statements */ }

            if (_parser.Match(TokenType.WITH)) return ParseStatementWithCte();
            if (_parser.Match(TokenType.CREATE)) return ParseCreate();
            if (_parser.Match(TokenType.ALTER)) return ParseAlter();
            if (_parser.Match(TokenType.EXPLAIN)) return ParseExplain();
            if (_parser.Match(TokenType.DROP)) return ParseDrop();
            if (_parser.Match(TokenType.CLEAR)) return ParseClear();
            if (_parser.Match(TokenType.TRUNCATE)) return ParseTruncate();
            if (_parser.Match(TokenType.DELETE)) return ParseDelete();
            if (_parser.Match(TokenType.DECLARE)) return ParseDeclare();
            if (_parser.Match(TokenType.RUN)) return ParseRun();
            if (_parser.Match(TokenType.SET)) 
            {
                if (_parser.Match(TokenType.PROFILING) || _parser.Match(TokenType.PROFILE)) return ParseSetProfiling();
                if (_parser.Match(TokenType.WHAT_IF)) return ParseSetWhatIf();
                if (_parser.Match(TokenType.SHOW_PASSWORD)) return ParseSetShowPassword();
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
            if (_parser.Match(TokenType.WAIT)) return ParseWait();
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
            if (_parser.Match(TokenType.LINT)) return ParseLint();
            if (_parser.Match(TokenType.REQUIRE)) return ParseRequireVersion();

            if (_parser.Match(TokenType.SEND_EMAIL))
            {
                if (_parser.Current.Type == TokenType.LPAREN) return ParseSendEmail(false);
                return ParseSendEmail(true);
            }
            if (_parser.Match(TokenType.SEND))
            {
                if (_parser.Current.Type == TokenType.FILE)
                {
                    _parser.Advance(); // consume FILE
                    return ParseFileTransfer(FileTransferType.Send, true);
                }
                if (_parser.Current.Type == TokenType.EMAIL)
                {
                    _parser.Advance(); // consume EMAIL
                    return ParseSendEmail(true);
                }
            }

            if (_parser.Match(TokenType.RECEIVE))
            {
                if (_parser.Current.Type == TokenType.FILE)
                {
                    _parser.Advance(); // consume FILE
                    return ParseFileTransfer(FileTransferType.Receive, true);
                }
            }

            if (_parser.Match(TokenType.FILE_SEND) || _parser.Match(TokenType.SEND_FILE)) return ParseFileTransfer(FileTransferType.Send, false);
            if (_parser.Match(TokenType.FILE_RECEIVE) || _parser.Match(TokenType.RECEIVE_FILE)) return ParseFileTransfer(FileTransferType.Receive, false);

            if (_parser.Match(TokenType.COPY) || _parser.Match(TokenType.MOVE) || _parser.Match(TokenType.RENAME) || 
                _parser.Match(TokenType.DELETE) || _parser.Match(TokenType.COMPRESS) || 
                _parser.Match(TokenType.ENCRYPT) || _parser.Match(TokenType.DECRYPT))
            {
                var opToken = _parser.Previous;
                if (_parser.Match(TokenType.FILE)) return ParseFileOperation(opToken);
                if (_parser.Match(TokenType.DIRECTORY)) return ParseDirectoryOperation(opToken);
                _parser.Backtrack(); // Backtrack to the operation token if not followed by FILE/DIRECTORY
            }

            if (_parser.Current.Type == TokenType.COPY_FILE || _parser.Current.Type == TokenType.MOVE_FILE || 
                _parser.Current.Type == TokenType.RENAME_FILE || _parser.Current.Type == TokenType.DELETE_FILE ||
                _parser.Current.Type == TokenType.COMPRESS_FILE || _parser.Current.Type == TokenType.ENCRYPT_FILE || _parser.Current.Type == TokenType.DECRYPT_FILE)
            {
                return ParseFileOperation(_parser.Advance());
            }

            if (_parser.Current.Type == TokenType.CREATE_DIRECTORY || _parser.Current.Type == TokenType.DELETE_DIRECTORY ||
                _parser.Current.Type == TokenType.RENAME_DIRECTORY || _parser.Current.Type == TokenType.MOVE_DIRECTORY ||
                _parser.Current.Type == TokenType.COPY_DIRECTORY || _parser.Current.Type == TokenType.DELETE_DIRECTORY_CONTENTS ||
                _parser.Current.Type == TokenType.COMPRESS_DIRECTORY || _parser.Current.Type == TokenType.ENCRYPT_DIRECTORY || _parser.Current.Type == TokenType.DECRYPT_DIRECTORY)
            {
                return ParseDirectoryOperation(_parser.Advance());
            }

            if (_parser.Match(TokenType.DOCKER)) return ParseDocker();
            if (_parser.Match(TokenType.CLOSE_DOCKER)) return ParseDockerClose();
            if (_parser.Match(TokenType.START_DOCKER)) return ParseDockerAction(DockerAction.Start);
            if (_parser.Match(TokenType.STOP_DOCKER)) return ParseDockerAction(DockerAction.Stop);
            if (_parser.Match(TokenType.PAUSE_DOCKER)) return ParseDockerAction(DockerAction.Pause);

            throw new SyntaxException($"Unexpected token type {_parser.Current.Type} ('{_parser.Current.Value}') at start of statement", _parser.Current.Line, _parser.Current.Column);
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
                   type == TokenType.RUN || type == TokenType.USE || type == TokenType.DOCKER || type == TokenType.HELP ||
                   type == TokenType.SEND_EMAIL || type == TokenType.SEND_FILE || type == TokenType.FILE_SEND ||
                   type == TokenType.RECEIVE_FILE || type == TokenType.FILE_RECEIVE || type == TokenType.WITH;
        }

        /// <summary>
        /// Captures a raw SQL block between BEGIN and END keywords.
        /// Handles nesting of BEGIN/END.
        /// </summary>
        public string CaptureRawBlock()
        {
            int depth = 1;
            string raw = "";
            while (depth > 0 && _parser.Current.Type != TokenType.EOF)
            {
                if (_parser.Current.Type == TokenType.BEGIN) depth++;
                if (_parser.Current.Type == TokenType.END) depth--;
                if (depth == 0) break;
                
                var t = _parser.Advance();
                string val = t.Type == TokenType.STRING ? $"'{t.Value.Replace("'", "''")}'" : t.Value;
                raw += val;
                
                var next = _parser.Current.Type;
                bool needsSpace = true;
                if (next == TokenType.DOT || next == TokenType.COMMA || next == TokenType.LPAREN || next == TokenType.RPAREN || next == TokenType.SEMICOLON) needsSpace = false;
                if (t.Type == TokenType.DOT || t.Type == TokenType.LPAREN) needsSpace = false;
                
                if (needsSpace) raw += " ";
            }

            if (depth > 0)
            {
                throw new SyntaxException("Unbalanced BEGIN/END in EXECUTE block", _parser.Current.Line, _parser.Current.Column);
            }

            _parser.Consume(TokenType.END, "Expected END");
            return raw.Trim();
        }
        /// <summary>
        /// Determines if a token type is a contextual keyword that can be treated as an identifier.
        /// </summary>
        private static bool IsContextualKeyword(TokenType type)
        {
            // Most keywords are contextual in ETL-SQL to allow them as identifiers where not ambiguous.
            // Star and operators/literal types are generally not contextual keywords.
            return type < TokenType.STAR && type != TokenType.IDENTIFIER && type != TokenType.VARIABLE && type != TokenType.STRING && type != TokenType.NUMBER;
        }
    }
}
