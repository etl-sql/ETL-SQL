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
            InitializeDispatchMap();
        }

        private readonly Dictionary<TokenType, System.Func<Statement>> _dispatchMap = new();

        private void InitializeDispatchMap()
        {
            _dispatchMap[TokenType.WITH] = ParseStatementWithCte;
            _dispatchMap[TokenType.CREATE] = ParseCreate;
            _dispatchMap[TokenType.ALTER] = ParseAlter;
            _dispatchMap[TokenType.EXPLAIN] = ParseExplain;
            _dispatchMap[TokenType.DROP] = ParseDrop;
            _dispatchMap[TokenType.CLEAR] = ParseClear;
            _dispatchMap[TokenType.TRUNCATE] = ParseTruncate;
            _dispatchMap[TokenType.DELETE] = ParseDelete;
            _dispatchMap[TokenType.DECLARE] = ParseDeclare;
            _dispatchMap[TokenType.RUN] = ParseRun;
            _dispatchMap[TokenType.SHOW] = ParseShow;
            _dispatchMap[TokenType.COMMIT] = ParseCommitTransaction;
            _dispatchMap[TokenType.ROLLBACK] = ParseRollbackTransaction;
            _dispatchMap[TokenType.IF] = ParseIf;
            _dispatchMap[TokenType.WHILE] = ParseWhile;
            _dispatchMap[TokenType.FOREACH] = ParseForeach;
            _dispatchMap[TokenType.INSERT] = ParseInsert;
            _dispatchMap[TokenType.UPDATE] = ParseUpdate;
            _dispatchMap[TokenType.MERGE] = ParseMerge;
            _dispatchMap[TokenType.PRINT] = ParsePrint;
            _dispatchMap[TokenType.WAITFOR] = ParseWaitFor;
            _dispatchMap[TokenType.WAIT] = ParseWait;
            _dispatchMap[TokenType.RAISEERROR] = ParseRaiseError;
            _dispatchMap[TokenType.ASSERT] = ParseAssert;
            _dispatchMap[TokenType.EXPECT] = ParseExpectSchema;
            _dispatchMap[TokenType.PARALLEL] = ParseParallel;
            _dispatchMap[TokenType.THROW] = ParseThrow;
            _dispatchMap[TokenType.RETURN] = ParseReturn;
            _dispatchMap[TokenType.BREAK] = ParseBreak;
            _dispatchMap[TokenType.CONTINUE] = ParseContinue;
            _dispatchMap[TokenType.HELP] = ParseHelp;
            _dispatchMap[TokenType.USE] = ParseUse;
            _dispatchMap[TokenType.BULK] = ParseBulkInsert;
            _dispatchMap[TokenType.LINEAGE] = ParseLineage;
            _dispatchMap[TokenType.LINT] = ParseLint;
            _dispatchMap[TokenType.REQUIRE] = ParseRequireVersion;
            _dispatchMap[TokenType.DOCKER] = ParseDocker;
            _dispatchMap[TokenType.CLOSE_DOCKER] = ParseDockerClose;
        }

        /// <summary>
        /// Parses a single statement from the current token stream.
        /// Identifies the statement type by matching keywords (CREATE, SELECT, IF, etc.).
        /// </summary>
        /// <returns>A <see cref="Statement"/> object representing the parsed structure.</returns>
        public Statement ParseStatement()
        {
            while (_parser.Match(TokenType.COLUMN_TAG)) { /* skip tags between statements */ }

            var type = _parser.Current.Type;

            // 1. Direct dispatch for fixed keywords
            if (_dispatchMap.TryGetValue(type, out var handler))
            {
                _parser.Advance();
                return handler();
            }

            // 2. Complex/Conditional dispatch
            if (_parser.Match(TokenType.SET)) return ParseSetDispatch();
            
            if (type == TokenType.BEGIN)
            {
                _parser.Advance();
                if (_parser.Match(TokenType.TRY)) return ParseTryCatch();
                if (_parser.Match(TokenType.TRANSACTION) || _parser.Match(TokenType.TRAN)) return ParseBeginTransaction();
                return ParseBlock();
            }

            if (_parser.Match(TokenType.FOR))
            {
                if (_parser.Match(TokenType.EACH)) return ParseForeach();
                return ParseFor();
            }

            if (type == TokenType.SELECT) { return _parser.ParseQuery(); }

            if (_parser.Match(TokenType.EXEC) || _parser.Match(TokenType.EXECUTE)) return ParseExecute();

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

            if (type == TokenType.FILE_SEND || type == TokenType.SEND_FILE) { _parser.Advance(); return ParseFileTransfer(FileTransferType.Send, false); }
            if (type == TokenType.FILE_RECEIVE || type == TokenType.RECEIVE_FILE) { _parser.Advance(); return ParseFileTransfer(FileTransferType.Receive, false); }

            if (type == TokenType.COPY || type == TokenType.MOVE || type == TokenType.RENAME || 
                type == TokenType.DELETE || type == TokenType.COMPRESS || 
                type == TokenType.ENCRYPT || type == TokenType.DECRYPT)
            {
                var opToken = _parser.Advance();
                if (_parser.Match(TokenType.FILE)) return ParseFileOperation(opToken);
                if (_parser.Match(TokenType.DIRECTORY)) return ParseDirectoryOperation(opToken);
                _parser.Backtrack(); 
            }

            if (type == TokenType.COPY_FILE || type == TokenType.MOVE_FILE || 
                type == TokenType.RENAME_FILE || type == TokenType.DELETE_FILE ||
                type == TokenType.COMPRESS_FILE || type == TokenType.ENCRYPT_FILE || type == TokenType.DECRYPT_FILE)
            {
                return ParseFileOperation(_parser.Advance());
            }

            if (type == TokenType.CREATE_DIRECTORY || type == TokenType.DELETE_DIRECTORY ||
                type == TokenType.RENAME_DIRECTORY || type == TokenType.MOVE_DIRECTORY ||
                type == TokenType.COPY_DIRECTORY || type == TokenType.DELETE_DIRECTORY_CONTENTS ||
                type == TokenType.COMPRESS_DIRECTORY || type == TokenType.ENCRYPT_DIRECTORY || type == TokenType.DECRYPT_DIRECTORY)
            {
                return ParseDirectoryOperation(_parser.Advance());
            }

            if (_parser.Match(TokenType.START_DOCKER)) return ParseDockerAction(DockerAction.Start);
            if (_parser.Match(TokenType.STOP_DOCKER)) return ParseDockerAction(DockerAction.Stop);
            if (_parser.Match(TokenType.PAUSE_DOCKER)) return ParseDockerAction(DockerAction.Pause);

            throw new SyntaxException($"Unexpected token type {_parser.Current.Type} ('{_parser.Current.Value}') at start of statement", _parser.Current.Line, _parser.Current.Column);
        }

        private Statement ParseSetDispatch()
        {
            if (_parser.Match(TokenType.PROFILING) || _parser.Match(TokenType.PROFILE)) return ParseSetProfiling();
            if (_parser.Match(TokenType.WHAT_IF)) return ParseSetWhatIf();
            if (_parser.Match(TokenType.SHOW_PASSWORD)) return ParseSetShowPassword();
            if (_parser.Match(TokenType.JOIN_SPILL_THRESHOLD)) return ParseSetThreshold(ThresholdType.JoinSpill);
            if (_parser.Match(TokenType.WINDOW_SPILL_THRESHOLD)) return ParseSetThreshold(ThresholdType.WindowSpill);
            if (_parser.Match(TokenType.EXTERNAL_HASH_PARTITIONS)) return ParseSetThreshold(ThresholdType.ExternalHashPartitions);
            if (_parser.Match(TokenType.EXTERNAL_SORT_CHUNK_SIZE)) return ParseSetThreshold(ThresholdType.ExternalSortChunkSize);
            if (_parser.Match(TokenType.BATCHSIZE)) return ParseSetThreshold(ThresholdType.BatchSize);
            if (_parser.Match(TokenType.MAX_RECURSIVE_DEPTH)) return ParseSetThreshold(ThresholdType.MaxRecursiveDepth);
            if (_parser.Match(TokenType.MAX_IN_MEMORY_BATCHES)) return ParseSetThreshold(ThresholdType.MaxInMemoryBatches);
            if (_parser.Match(TokenType.FOREACH_PAGE_SIZE)) return ParseSetThreshold(ThresholdType.ForeachPageSize);
            if (_parser.Match(TokenType.MAX_MESSAGES)) return ParseSetThreshold(ThresholdType.MaxMessages);
            if (_parser.Match(TokenType.MAX_FILE_OPERATIONS)) return ParseSetThreshold(ThresholdType.MaxFileOperations);
            if (_parser.Match(TokenType.MAX_PARALLEL_DEGREE)) return ParseSetThreshold(ThresholdType.MaxParallelDegree);
            if (_parser.Match(TokenType.MAX_STRING_RESULT_SIZE)) return ParseSetThreshold(ThresholdType.MaxStringResultSize);
            if (_parser.Match(TokenType.REGEX_MATCH_TIMEOUT)) return ParseSetThreshold(ThresholdType.RegexMatchTimeout);
            if (_parser.Match(TokenType.MAX_GROUPING_SETS) || _parser.Match(TokenType.SET_CUBE_LIMIT)) return ParseSetThreshold(ThresholdType.MaxGroupingSets);
            if (_parser.Match(TokenType.MAX_SESSION_SIZE)) return ParseSetThreshold(ThresholdType.MaxSessionSize);
            if (_parser.Match(TokenType.TELEMETRY)) return ParseSetThreshold(ThresholdType.Telemetry);
            
            if (_parser.Match(TokenType.SPILL_ENCRYPTION)) return ParseSetSpillOption(SpillOptionType.Encryption);
            if (_parser.Match(TokenType.SPILL_COMPRESSION)) return ParseSetSpillOption(SpillOptionType.Compression);

            if ( _parser.Match(TokenType.TEMPLATE_PATH)) return ParseSetTemplatePath();
            if (_parser.Match(TokenType.REPORT)) return ParseSetReportMetadata();

            if (_parser.Current.Type == TokenType.IDENTIFIER && _parser.Current.Value.StartsWith("ALLOW_", StringComparison.OrdinalIgnoreCase))
            {
                _parser.Advance();
                return ParseSetSecurityOverride();
            }

            return ParseSetVariable();
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
            // Structural/Reserved keywords that should NEVER be identifiers in ETL-SQL
            if (type <= TokenType.DECLARE || type == TokenType.BEGIN || type == TokenType.END || 
                type == TokenType.COMMIT || type == TokenType.ROLLBACK || type == TokenType.TRANSACTION ||
                type == TokenType.IF || type == TokenType.WHILE || type == TokenType.TRY || type == TokenType.CATCH)
                return false;

            // Report-SQL and Overlay tokens are always contextual
            if (type >= TokenType.VISUAL && type <= TokenType.COLOR) return true;

            // Everything else before STAR is generally a safe contextual keyword (functions, secondary keywords)
            return type < TokenType.STAR;
        }
    }
}
