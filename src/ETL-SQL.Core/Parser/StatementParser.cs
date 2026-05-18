using System.Collections.Generic;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser.Components;

namespace ETL_SQL.Core.Parser
{
    /// <summary>
    /// Recursive descent parser for ETL-SQL statements. Acts as a thin dispatcher
    /// that delegates to specialized component classes by domain.
    /// </summary>
    public class StatementParser
    {
        private readonly IParser _parser;

        internal DataParser      DataParser      { get; }
        internal FlowParser      FlowParser      { get; }
        internal SystemParser    SystemParser    { get; }
        internal ExtensionParser ExtensionParser { get; }
        internal ReportParser    ReportParser    { get; }
        internal PortalParser    PortalParser    { get; }

        public StatementParser(IParser parser)
        {
            _parser        = parser;
            DataParser      = new DataParser(parser, this);
            FlowParser      = new FlowParser(parser, this);
            SystemParser    = new SystemParser(parser, this);
            ExtensionParser = new ExtensionParser(parser, this);
            ReportParser    = new ReportParser(parser, this);
            PortalParser    = new PortalParser(parser, this);
            InitializeDispatchMap();
        }

        private readonly Dictionary<TokenType, System.Func<Statement>> _dispatchMap = new();

        private void InitializeDispatchMap()
        {
            _dispatchMap[TokenType.WITH]          = ParseStatementWithCte;
            _dispatchMap[TokenType.CREATE]        = () => { var t = _parser.Previous; return DataParser.ParseCreate(t); };
            _dispatchMap[TokenType.ALTER]         = () => { var t = _parser.Previous; return DataParser.ParseAlter(t); };
            _dispatchMap[TokenType.EXPLAIN]       = () => { var t = _parser.Previous; return SystemParser.ParseExplain(t); };
            _dispatchMap[TokenType.DROP]          = () => { var t = _parser.Previous; return DataParser.ParseDrop(t); };
            _dispatchMap[TokenType.CLEAR]         = () => { var t = _parser.Previous; return SystemParser.ParseClear(t); };
            _dispatchMap[TokenType.TRUNCATE]      = () => { var t = _parser.Previous; return DataParser.ParseTruncate(t); };
            _dispatchMap[TokenType.GENERATE]      = () => { var t = _parser.Previous; return DataParser.ParseGenerate(t); };
            _dispatchMap[TokenType.DELETE]        = () => { var t = _parser.Previous; return DataParser.ParseDelete(t); };
            _dispatchMap[TokenType.DECLARE]       = () => SystemParser.ParseDeclare();
            _dispatchMap[TokenType.RUN]           = () => { var t = _parser.Previous; return SystemParser.ParseRun(t); };
            _dispatchMap[TokenType.SHOW]          = () => { var t = _parser.Previous; return SystemParser.ParseShow(t); };
            _dispatchMap[TokenType.COMMIT]        = () => SystemParser.ParseCommitTransaction();
            _dispatchMap[TokenType.ROLLBACK]      = () => SystemParser.ParseRollbackTransaction();
            _dispatchMap[TokenType.IF]            = () => { var t = _parser.Previous; return FlowParser.ParseIf(t); };
            _dispatchMap[TokenType.WHILE]         = () => { var t = _parser.Previous; return FlowParser.ParseWhile(t); };
            _dispatchMap[TokenType.FOREACH]       = () => FlowParser.ParseForeach();
            _dispatchMap[TokenType.INSERT]        = () => { var t = _parser.Previous; return DataParser.ParseInsert(t); };
            _dispatchMap[TokenType.UPDATE]        = () => { var t = _parser.Previous; return DataParser.ParseUpdate(t); };
            _dispatchMap[TokenType.MERGE]         = () => { var t = _parser.Previous; return DataParser.ParseMerge(t); };
            _dispatchMap[TokenType.PRINT]         = () => SystemParser.ParsePrint();
            _dispatchMap[TokenType.WAITFOR]       = () => { var t = _parser.Previous; return ExtensionParser.ParseWaitFor(t); };
            _dispatchMap[TokenType.WAIT]          = () => { var t = _parser.Previous; return ExtensionParser.ParseWait(t); };
            _dispatchMap[TokenType.RAISEERROR]    = () => FlowParser.ParseRaiseError();
            _dispatchMap[TokenType.ASSERT]        = () => { var t = _parser.Previous; return FlowParser.ParseAssert(t); };
            _dispatchMap[TokenType.EXPECT]        = () => { var t = _parser.Previous; return FlowParser.ParseExpectSchema(t); };
            _dispatchMap[TokenType.PARALLEL]      = () => { var t = _parser.Previous; return FlowParser.ParseParallel(t); };
            _dispatchMap[TokenType.THROW]         = () => FlowParser.ParseThrow();
            _dispatchMap[TokenType.RETURN]        = () => FlowParser.ParseReturn();
            _dispatchMap[TokenType.BREAK]         = () => FlowParser.ParseBreak();
            _dispatchMap[TokenType.CONTINUE]      = () => FlowParser.ParseContinue();
            _dispatchMap[TokenType.GO]            = () => ParseGo();
            _dispatchMap[TokenType.HELP]          = () => SystemParser.ParseHelp();
            _dispatchMap[TokenType.USE]           = () => { var t = _parser.Previous; return SystemParser.ParseUse(t); };
            _dispatchMap[TokenType.BULK]          = () => { var t = _parser.Previous; return DataParser.ParseBulkInsert(t); };
            _dispatchMap[TokenType.LINT]          = () => { var t = _parser.Previous; return ExtensionParser.ParseLint(t); };
            _dispatchMap[TokenType.REQUIRE]       = () => { var t = _parser.Previous; return SystemParser.ParseRequireVersion(t); };
            _dispatchMap[TokenType.START]         = () => ExtensionParser.ParseDockerVerb(DockerAction.Start);
            _dispatchMap[TokenType.STOP]          = () => ExtensionParser.ParseDockerVerb(DockerAction.Stop);
            _dispatchMap[TokenType.PAUSE]         = () => ExtensionParser.ParseDockerVerb(DockerAction.Pause);
            _dispatchMap[TokenType.CLOSE]         = () => ExtensionParser.ParseDockerVerb(DockerAction.Close);
            _dispatchMap[TokenType.DOCKER]        = () => ExtensionParser.ParseDockerVerb(DockerAction.Start); // Fallback
            _dispatchMap[TokenType.STYLE]         = () => { var t = _parser.Previous; return ReportParser.ParseStyleStatement(t); };
            _dispatchMap[TokenType.EXPORT]        = () => { var t = _parser.Previous; return ParseExportReport(t); };

            // Portal admin statements (valid inside EXECUTE portal BEGIN…END)
            _dispatchMap[TokenType.GRANT]        = () => { var t = _parser.Previous; return PortalParser.ParseGrant(t); };
            _dispatchMap[TokenType.REVOKE]       = () => { var t = _parser.Previous; return PortalParser.ParseRevoke(t); };
            _dispatchMap[TokenType.PUBLISH]      = () => { var t = _parser.Previous; return PortalParser.ParsePublishReport(t); };
            _dispatchMap[TokenType.FAVORITE]     = () => { var t = _parser.Previous; return PortalParser.ParseFavoriteReport(t, favorite: true); };
            _dispatchMap[TokenType.UNFAVORITE]   = () => { var t = _parser.Previous; return PortalParser.ParseFavoriteReport(t, favorite: false); };
            _dispatchMap[TokenType.VALIDATE]     = () => { var t = _parser.Previous; return PortalParser.ParseValidateReport(t); };
            _dispatchMap[TokenType.DISCONNECT]   = () => { var t = _parser.Previous; return PortalParser.ParseDisconnectUser(t); };
            _dispatchMap[TokenType.RESTART]      = () => { var t = _parser.Previous; return PortalParser.ParseRestartPortal(t); };
            _dispatchMap[TokenType.SHUTDOWN]     = () => { var t = _parser.Previous; return PortalParser.ParseShutdownPortal(t); };
            _dispatchMap[TokenType.REBUILD]      = () => { var t = _parser.Previous; return PortalParser.ParseRebuildSnapshot(t); };
            _dispatchMap[TokenType.ADD]          = () =>
            {
                var t = _parser.Previous;
                if (_parser.Match(TokenType.USER)) return PortalParser.ParseAddUserToGroup(t);
                throw new ETL_SQL.Core.Common.Exceptions.SyntaxException(
                    $"Unexpected ADD target '{_parser.Current.Value}'",
                    _parser.Current.Line, _parser.Current.Column);
            };
            _dispatchMap[TokenType.TOKENS]       = () => { var t = _parser.Previous; return PortalParser.ParseRevokeTokens(t); };
            _dispatchMap[TokenType.REFRESH]      = () =>
            {
                var t = _parser.Previous;
                if (_parser.Match(TokenType.REPORT)) return PortalParser.ParseRefreshReport(t);
                if (_parser.Current.Type == TokenType.DATASET && _parser.Peek.Type == TokenType.STRING_LITERAL)
                {
                    _parser.Advance();
                    return PortalParser.ParseRefreshDataset(t);
                }
                return DataParser.ParseRefreshDataset(t);
            };
        }

        public Statement ParseStatement()
        {
            while (_parser.Match(TokenType.COLUMN_TAG)) { /* skip tags between statements */ }

            var type = _parser.Current.Type;

            if (_dispatchMap.TryGetValue(type, out var handler))
            {
                _parser.Advance();
                return handler();
            }

            if (_parser.Match(TokenType.SET)) return ParseSetDispatch();

            if (type == TokenType.BEGIN)
            {
                _parser.Advance();
                if (_parser.Match(TokenType.TRY)) return FlowParser.ParseTryCatch();
                if (_parser.Match(TokenType.TRANSACTION) || _parser.Match(TokenType.TRAN)) return SystemParser.ParseBeginTransaction();
                return FlowParser.ParseBlock();
            }

            if (_parser.Match(TokenType.FOR))
            {
                if (_parser.Match(TokenType.EACH)) return FlowParser.ParseForeach();
                return FlowParser.ParseFor();
            }

            if (type == TokenType.SELECT) return _parser.ParseQuery();

            if (_parser.Match(TokenType.EXEC) || _parser.Match(TokenType.EXECUTE)) return ExtensionParser.ParseExecute();

            if (_parser.Match(TokenType.SEND_EMAIL))
            {
                if (_parser.Current.Type == TokenType.LPAREN) return ExtensionParser.ParseSendEmail(false);
                return ExtensionParser.ParseSendEmail(true);
            }

            if (_parser.Match(TokenType.SEND))
            {
                if (_parser.Current.Type == TokenType.FILE)
                {
                    _parser.Advance();
                    return ExtensionParser.ParseFileTransfer(FileTransferType.Send, true);
                }
                if (_parser.Current.Type == TokenType.EMAIL)
                {
                    _parser.Advance();
                    return ExtensionParser.ParseSendEmail(true);
                }
            }

            if (_parser.Match(TokenType.RECEIVE))
            {
                if (_parser.Current.Type == TokenType.FILE)
                {
                    _parser.Advance();
                    return ExtensionParser.ParseFileTransfer(FileTransferType.Receive, true);
                }
            }

            if (type == TokenType.FILE_SEND || type == TokenType.SEND_FILE) { _parser.Advance(); return ExtensionParser.ParseFileTransfer(FileTransferType.Send, false); }
            if (type == TokenType.FILE_RECEIVE || type == TokenType.RECEIVE_FILE) { _parser.Advance(); return ExtensionParser.ParseFileTransfer(FileTransferType.Receive, false); }

            if (_parser.Match(TokenType.KILL)) return DataParser.ParseKillJob(_parser.Previous);

            if (type == TokenType.COPY || type == TokenType.MOVE || type == TokenType.RENAME ||
                type == TokenType.DELETE || type == TokenType.COMPRESS || type == TokenType.DECOMPRESS ||
                type == TokenType.ENCRYPT || type == TokenType.DECRYPT)
            {
                var opToken = _parser.Advance();
                if (_parser.Match(TokenType.FILE)) return ExtensionParser.ParseFileOperation(opToken);
                if (_parser.Match(TokenType.DIRECTORY)) return ExtensionParser.ParseDirectoryOperation(opToken);
                _parser.Backtrack();
            }

            if (type == TokenType.COPY_FILE || type == TokenType.MOVE_FILE ||
                type == TokenType.RENAME_FILE || type == TokenType.DELETE_FILE ||
                type == TokenType.COMPRESS_FILE || type == TokenType.DECOMPRESS_FILE || type == TokenType.ENCRYPT_FILE || type == TokenType.DECRYPT_FILE)
            {
                return ExtensionParser.ParseFileOperation(_parser.Advance());
            }

            if (type == TokenType.CREATE_DIRECTORY || type == TokenType.DELETE_DIRECTORY ||
                type == TokenType.RENAME_DIRECTORY || type == TokenType.MOVE_DIRECTORY ||
                type == TokenType.COPY_DIRECTORY || type == TokenType.DELETE_DIRECTORY_CONTENTS ||
                type == TokenType.COMPRESS_DIRECTORY || type == TokenType.ENCRYPT_DIRECTORY || type == TokenType.DECRYPT_DIRECTORY)
            {
                return ExtensionParser.ParseDirectoryOperation(_parser.Advance());
            }


            throw new SyntaxException($"Unexpected token type {_parser.Current.Type} ('{_parser.Current.Value}') at start of statement", _parser.Current.Line, _parser.Current.Column);
        }

        private Statement ParseSetDispatch()
        {
            if (_parser.Match(TokenType.PROFILING) || _parser.Match(TokenType.PROFILE)) return SystemParser.ParseSetProfiling();
            if (_parser.Match(TokenType.WHAT_IF)) return SystemParser.ParseSetWhatIf();
            if (_parser.Match(TokenType.SHOW_PASSWORD)) return SystemParser.ParseSetShowPassword();
            if (_parser.Match(TokenType.JOIN_SPILL_THRESHOLD)) return SystemParser.ParseSetThreshold(ThresholdType.JoinSpill);
            if (_parser.Match(TokenType.TEMP_TABLE_SPILL_THRESHOLD)) return SystemParser.ParseSetThreshold(ThresholdType.TempTableSpill);
            if (_parser.Match(TokenType.WINDOW_SPILL_THRESHOLD)) return SystemParser.ParseSetThreshold(ThresholdType.WindowSpill);
            if (_parser.Match(TokenType.EXTERNAL_HASH_PARTITIONS)) return SystemParser.ParseSetThreshold(ThresholdType.ExternalHashPartitions);
            if (_parser.Match(TokenType.EXTERNAL_SORT_CHUNK_SIZE)) return SystemParser.ParseSetThreshold(ThresholdType.ExternalSortChunkSize);
            if (_parser.Match(TokenType.BATCHSIZE)) return SystemParser.ParseSetThreshold(ThresholdType.BatchSize);
            if (_parser.Match(TokenType.MAX_RECURSIVE_DEPTH)) return SystemParser.ParseSetThreshold(ThresholdType.MaxRecursiveDepth);
            if (_parser.Match(TokenType.MAX_IN_MEMORY_BATCHES)) return SystemParser.ParseSetThreshold(ThresholdType.MaxInMemoryBatches);
            if (_parser.Match(TokenType.FOREACH_PAGE_SIZE)) return SystemParser.ParseSetThreshold(ThresholdType.ForeachPageSize);
            if (_parser.Match(TokenType.MAX_MESSAGES)) return SystemParser.ParseSetThreshold(ThresholdType.MaxMessages);
            if (_parser.Match(TokenType.MAX_FILE_OPERATIONS)) return SystemParser.ParseSetThreshold(ThresholdType.MaxFileOperations);
            if (_parser.Match(TokenType.MAX_PARALLEL_DEGREE)) return SystemParser.ParseSetThreshold(ThresholdType.MaxParallelDegree);
            if (_parser.Match(TokenType.MAX_STRING_RESULT_SIZE)) return SystemParser.ParseSetThreshold(ThresholdType.MaxStringResultSize);
            if (_parser.Match(TokenType.REGEX_MATCH_TIMEOUT)) return SystemParser.ParseSetThreshold(ThresholdType.RegexMatchTimeout);
            if (_parser.Match(TokenType.MAX_GROUPING_SETS) || _parser.Match(TokenType.SET_CUBE_LIMIT)) return SystemParser.ParseSetThreshold(ThresholdType.MaxGroupingSets);
            if (_parser.Match(TokenType.MAX_SESSION_SIZE)) return SystemParser.ParseSetThreshold(ThresholdType.MaxSessionSize);
            if (_parser.Match(TokenType.MAX_LAST_RESULT_ROWS)) return SystemParser.ParseSetThreshold(ThresholdType.MaxLastResultRows);
            if (_parser.Match(TokenType.MAX_GENERATE_ROWS)) return SystemParser.ParseSetThreshold(ThresholdType.MaxGenerateRows);
            if (_parser.Match(TokenType.MAX_SMTP_EMAILS_PER_SCRIPT)) return SystemParser.ParseSetThreshold(ThresholdType.MaxSmtpEmailsPerScript);
            if (_parser.Match(TokenType.MAX_INTERNAL_OPERATIONS)) return SystemParser.ParseSetThreshold(ThresholdType.MaxInternalOperations);
            if (_parser.Match(TokenType.TELEMETRY)) return SystemParser.ParseSetThreshold(ThresholdType.Telemetry);
            if (_parser.Match(TokenType.INTERACTIVE_MODE)) return SystemParser.ParseSetThreshold(ThresholdType.InteractiveMode);
            if (_parser.Match(TokenType.CASE_SENSITIVE)) return SystemParser.ParseSetThreshold(ThresholdType.CaseSensitive);
            if (_parser.Match(TokenType.LINEAGE)) return SystemParser.ParseSetThreshold(ThresholdType.Lineage);
            if (_parser.Match(TokenType.PERSIST)) return SystemParser.ParseSetPersist();


            if (_parser.Match(TokenType.SPILL_ENCRYPTION)) return SystemParser.ParseSetSpillOption(SpillOptionType.Encryption);
            if (_parser.Match(TokenType.SPILL_COMPRESSION)) return SystemParser.ParseSetSpillOption(SpillOptionType.Compression);

            if (_parser.Match(TokenType.TEMPLATE_PATH)) return SystemParser.ParseSetTemplatePath();
            if (_parser.Match(TokenType.REPORT)) return SystemParser.ParseSetReportMetadata();
            if (_parser.Match(TokenType.WEEK_START_DAY)) return SystemParser.ParseSetWeekStartDay();
            if (_parser.Match(TokenType.SCRIPT_HASH_POLICY)) return SystemParser.ParseSetScriptHashPolicy();

            if (_parser.Current.Type == TokenType.IDENTIFIER && _parser.Current.Value.StartsWith("ALLOW_", System.StringComparison.OrdinalIgnoreCase))
            {
                _parser.Advance();
                return SystemParser.ParseSetSecurityOverride();
            }

            return SystemParser.ParseSetVariable();
        }

        public bool IsStatementStart(TokenType type)
        {
            return type == TokenType.SELECT || type == TokenType.INSERT || type == TokenType.UPDATE ||
                   type == TokenType.DELETE || type == TokenType.MERGE || type == TokenType.CREATE || type == TokenType.DROP ||
                   type == TokenType.ALTER || type == TokenType.DECLARE || type == TokenType.SET ||
                   type == TokenType.IF || type == TokenType.WHILE || type == TokenType.BEGIN ||
                   type == TokenType.PRINT || type == TokenType.EXEC || type == TokenType.EXECUTE ||
                   type == TokenType.RUN || type == TokenType.USE || type == TokenType.DOCKER || type == TokenType.HELP ||
                   type == TokenType.START || type == TokenType.STOP || type == TokenType.PAUSE || type == TokenType.CLOSE ||
                   type == TokenType.SEND_EMAIL || type == TokenType.SEND_FILE || type == TokenType.FILE_SEND ||
                   type == TokenType.RECEIVE_FILE || type == TokenType.FILE_RECEIVE || type == TokenType.WITH ||
                   type == TokenType.STYLE || type == TokenType.COMPRESS || type == TokenType.DECOMPRESS ||
                   type == TokenType.COMPRESS_FILE || type == TokenType.DECOMPRESS_FILE ||
                   type == TokenType.COMPRESS_DIRECTORY || type == TokenType.DECOMPRESS_DIRECTORY;
        }

        public ForeignKeyReference ParseForeignKeyReference() => DataParser.ParseForeignKeyReference();

        // ── EXPORT REPORT 'path' FORMAT PDF|CSV|MARKDOWN TO 'output' ──────────
        private Statement ParseExportReport(Token t)
        {
            _parser.Consume(TokenType.REPORT, "Expected REPORT after EXPORT");
            var reportPath = _parser.ParseExpression();
            _parser.Consume(TokenType.FORMAT, "Expected FORMAT");

            string format;
            if      (_parser.Match(TokenType.PDF))      format = "PDF";
            else if (_parser.Match(TokenType.CSV))       format = "CSV";
            else if (_parser.Match(TokenType.MARKDOWN))  format = "MARKDOWN";
            else throw new ETL_SQL.Core.Common.Exceptions.SyntaxException(
                    $"Expected PDF, CSV, or MARKDOWN after FORMAT, got '{_parser.Current.Value}'",
                    _parser.Current.Line, _parser.Current.Column);

            _parser.Consume(TokenType.TO, "Expected TO");
            var outputPath = _parser.ParseExpression();
            return new ExportReportStatement(reportPath, format, outputPath)
                { Line = t.Line, Column = t.Column };
        }

        private Statement ParseStatementWithCte()
        {
            bool isRecursive = _parser.Match(TokenType.RECURSIVE);
            var ctes = ParseCtes();
            var stmt = ParseStatement();
            stmt = stmt with { Ctes = ctes };
            if (stmt is SelectStatement select && isRecursive)
                stmt = select with { IsRecursive = true };
            return stmt;
        }

        private List<CteDefinition> ParseCtes()
        {
            var ctes = new List<CteDefinition>();
            do
            {
                string name;
                var nameToken = _parser.Current;
                if (_parser.Current.Type == TokenType.IDENTIFIER || LanguageMetadata.IsKeyword(_parser.Current.Value))
                    name = _parser.Advance().Value;
                else
                    throw new SyntaxException("Expected CTE name", _parser.Current.Line, _parser.Current.Column);

                List<string>? columnNames = null;
                if (_parser.Match(TokenType.LPAREN))
                {
                    columnNames = new List<string>();
                    do
                    {
                        if (_parser.Current.Type == TokenType.IDENTIFIER || LanguageMetadata.IsKeyword(_parser.Current.Value))
                            columnNames.Add(_parser.Advance().Value);
                        else
                            throw new SyntaxException("Expected column name in CTE definition", _parser.Current.Line, _parser.Current.Column);
                    } while (_parser.Match(TokenType.COMMA));
                    _parser.Consume(TokenType.RPAREN, "Expected ')' after CTE column list");
                }

                _parser.Consume(TokenType.AS, "Expected 'AS'");
                _parser.Consume(TokenType.LPAREN, "Expected '('");
                var subq = _parser.ParseQuery();
                _parser.Consume(TokenType.RPAREN, "Expected ')'");
                ctes.Add(new CteDefinition(name, subq, columnNames) { Line = nameToken.Line, Column = nameToken.Column });
            } while (_parser.Match(TokenType.COMMA));
            return ctes;
        }

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
                string val = t.Type == TokenType.STRING_LITERAL ? $"'{t.Value.Replace("'", "''")}'" : t.Value;
                raw += val;

                var next = _parser.Current.Type;
                bool needsSpace = true;
                if (next == TokenType.DOT || next == TokenType.COMMA || next == TokenType.LPAREN || next == TokenType.RPAREN || next == TokenType.SEMICOLON) needsSpace = false;
                if (t.Type == TokenType.DOT || t.Type == TokenType.LPAREN) needsSpace = false;

                if (needsSpace) raw += " ";
            }

            if (depth > 0)
                throw new SyntaxException("Unbalanced BEGIN/END in EXECUTE block", _parser.Current.Line, _parser.Current.Column);

            _parser.Consume(TokenType.END, "Expected END");
            return raw.Trim();
        }

        private Statement ParseGo()
        {
            var t = _parser.Previous;
            int count = 1;
            if (_parser.Current.Type == TokenType.NUMBER &&
                int.TryParse(_parser.Current.Value, out var n) && n > 0)
            {
                count = n;
                _parser.Advance();
            }
            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
            return new GoStatement(count) { Line = t.Line, Column = t.Column };
        }

        private static bool IsContextualKeyword(TokenType type)
        {
            if (type <= TokenType.DECLARE || type == TokenType.BEGIN || type == TokenType.END ||
                type == TokenType.COMMIT || type == TokenType.ROLLBACK || type == TokenType.TRANSACTION ||
                type == TokenType.IF || type == TokenType.WHILE || type == TokenType.TRY || type == TokenType.CATCH)
                return false;

            if (type >= TokenType.VISUAL && type <= TokenType.COLOR) return true;

            return type < TokenType.STAR;
        }
    }
}
