using System;
using System.Collections.Generic;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Core.Parser.Components
{
    public class SystemParser : ParserComponent
    {
        public SystemParser(IParser parser, StatementParser parent) : base(parser, parent) { }

        public Statement ParseDeclare()
        {
            var startToken = _parser.Previous;
            var declares = new List<Statement>();

            do
            {
                var varToken = Consume(TokenType.VARIABLE, "Expected variable name starting with '@'");
                string? type = null;
                bool isSensitive = false;

                if (_parser.IsIdentifier(_parser.Current)) 
                {
                    type = _parser.ParseType();
                    if (type != null && (type.Equals("SENSITIVE", StringComparison.OrdinalIgnoreCase) || 
                        type.Equals("SECRET", StringComparison.OrdinalIgnoreCase) ||
                        type.Equals("ENCRYPTED", StringComparison.OrdinalIgnoreCase)))
                    {
                        isSensitive = true;
                    }
                }

                if (Match(TokenType.PASSWORD)) isSensitive = true;
                bool isInput  = Match(TokenType.INPUT);
                bool isOutput = Match(TokenType.OUTPUT);

                Expression? initialValue = null;
                if (Match(TokenType.EQUALS)) initialValue = ParseExpression();

                if (!isSensitive) isSensitive = Match(TokenType.PASSWORD);
                if (!isInput)  isInput  = Match(TokenType.INPUT);
                if (!isOutput) isOutput = Match(TokenType.OUTPUT);

                Dictionary<string, string>? metadata = null;
                while (Match(TokenType.COLUMN_TAG))
                {
                    if (metadata == null) metadata = new(StringComparer.OrdinalIgnoreCase);
                    _parser.ParseMetadataTags(_parser.Previous.Value, metadata);
                }

                bool isSecret = type != null && type.Equals("SECRET", StringComparison.OrdinalIgnoreCase);

                var stmt = new DeclareStatement(varToken.Value, type ?? "", initialValue, isSensitive, isInput, isOutput, metadata)
                {
                    Line        = varToken.Line,
                    Column      = varToken.Column,
                    EndLine     = _parser.LastTokenEndLine,
                    EndColumn   = _parser.LastTokenEndColumn,
                    IsSensitive = isSensitive,
                    IsSecret    = isSecret,
                    IsInput     = isInput,
                    IsOutput    = isOutput
                };
                declares.Add(stmt);
            } while (Match(TokenType.COMMA));

            if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
            if (declares.Count == 1) return declares[0];
            return new BlockStatement(declares) { Line = startToken.Line, Column = startToken.Column, EndLine = _parser.LastTokenEndLine, EndColumn = _parser.LastTokenEndColumn };
        }

        public Statement ParseSetVariable()
        {
            var startToken = _parser.Previous; // The 'SET' token
            
            Expression target;
            if (_parser.Current.Type == TokenType.VARIABLE)
            {
                var varToken = Advance();
                target = new VariableExpression(varToken.Value) { Line = varToken.Line, Column = varToken.Column };
                while (Match(TokenType.DOT))
                {
                    if (!_parser.IsIdentifier(_parser.Current) && !ETL_SQL.Common.LanguageMetadata.IsKeyword(_parser.Current.Value))
                        throw new SyntaxException("Expected member name after '.'", _parser.Current.Line, _parser.Current.Column);
                    var member = Advance();
                    target = new MemberAccessExpression(target, member.Value) { Line = varToken.Line, Column = varToken.Column, EndLine = _parser.LastTokenEndLine, EndColumn = _parser.LastTokenEndColumn };
                }
            }
            else
            {
                target = ParseExpression();
            }
            
            if (target is not VariableExpression && target is not MemberAccessExpression)
                throw new SyntaxException("The left-hand side of a SET statement must be a variable or a variable property.", startToken.Line, startToken.Column);

            Consume(TokenType.EQUALS, "Expected '=' in SET statement");
            var expr = ParseExpression();
            if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
            
            return new SetVariableStatement(target, expr) { Line = startToken.Line, Column = startToken.Column };
        }

        public Statement ParseSetProfiling()
        {
            bool enabled;
            if (Match(TokenType.ON)) enabled = true;
            else if (Match(TokenType.OFF)) enabled = false;
            else throw new SyntaxException("Expected ON or OFF after SET PROFILE", _parser.Current.Line, _parser.Current.Column);
            Match(TokenType.SEMICOLON);
            return new SetProfilingStatement { Enabled = enabled };
        }

        public Statement ParseSetWhatIf()
        {
            bool enabled;
            if (Match(TokenType.ON)) enabled = true;
            else if (Match(TokenType.OFF)) enabled = false;
            else throw new SyntaxException("Expected ON or OFF after SET WHAT_IF", _parser.Current.Line, _parser.Current.Column);
            Match(TokenType.SEMICOLON);
            return new SetWhatIfStatement { Enabled = enabled };
        }

        public Statement ParseSetShowPassword()
        {
            bool enabled;
            if (Match(TokenType.ON)) enabled = true;
            else if (Match(TokenType.OFF)) enabled = false;
            else throw new SyntaxException("Expected ON or OFF after SET SHOW_PASSWORD", _parser.Current.Line, _parser.Current.Column);
            Match(TokenType.SEMICOLON);
            return new SetShowPasswordStatement(enabled);
        }

        public Statement ParseSetThreshold(ThresholdType type)
        {
            var startToken = _parser.Previous;
            Consume(TokenType.EQUALS, $"Expected '=' after SET {startToken.Value}");
            var value = ParseExpression();
            if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
            return new SetThresholdStatement(type, value) { Line = startToken.Line, Column = startToken.Column };
        }

        public Statement ParseSetSecurityOverride()
        {
            var startToken = _parser.Previous;
            string val = startToken.Value.ToUpperInvariant();
            SecurityOverride overrideType;

            if (val == "ALLOW_FILE_TYPE_ACCESS")
            {
                if (Match(TokenType.EQUALS))
                {
                    var expr = ParseExpression();
                    if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
                    return new SetSecurityOverrideStatement(SecurityOverride.FileTypeExtension, true, expr) { Line = startToken.Line, Column = startToken.Column };
                }
                overrideType = SecurityOverride.FileTypeAccess;
            }
            else if (val == "ALLOW_FILE_OPERATIONS" || (val.StartsWith("ALLOW_GREATER_THAN_") && val.EndsWith("_FILE")))
            {
                if (Match(TokenType.EQUALS) || val == "ALLOW_FILE_OPERATIONS")
                {
                    if (startToken.Value == "ALLOW_FILE_OPERATIONS" && !Match(TokenType.EQUALS))
                         throw new SyntaxException("Expected '=' after ALLOW_FILE_OPERATIONS", startToken.Line, startToken.Column);
                    
                    var expr = ParseExpression();
                    if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
                    return new SetThresholdStatement(ThresholdType.MaxFileOperations, expr) { Line = startToken.Line, Column = startToken.Column };
                }
                overrideType = SecurityOverride.LargeFileCount;
            }
            else if (val == "ALLOW_RECURSIVE_LAYERS" || (val.StartsWith("ALLOW_RECURSIVE_GREATER_THAN_") && val.EndsWith("_LAYERS")))
            {
                if (Match(TokenType.EQUALS) || val == "ALLOW_RECURSIVE_LAYERS")
                {
                    if (startToken.Value == "ALLOW_RECURSIVE_LAYERS" && !Match(TokenType.EQUALS))
                         throw new SyntaxException("Expected '=' after ALLOW_RECURSIVE_LAYERS", startToken.Line, startToken.Column);

                    var expr = ParseExpression();
                    if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
                    return new SetThresholdStatement(ThresholdType.MaxRecursiveDepth, expr) { Line = startToken.Line, Column = startToken.Column };
                }
                overrideType = SecurityOverride.DeepRecursion;
            }
            else if (val == "ALLOW_LARGE_STRING_RESULTS")
                overrideType = SecurityOverride.LargeStringResults;
            else
                throw new SyntaxException($"Unknown security override: {startToken.Value}", startToken.Line, startToken.Column);

            bool enabled;
            if (Match(TokenType.ON)) enabled = true;
            else if (Match(TokenType.OFF)) enabled = false;
            else throw new SyntaxException($"Expected ON or OFF after SET {startToken.Value}", _parser.Current.Line, _parser.Current.Column);
            Match(TokenType.SEMICOLON);
            return new SetSecurityOverrideStatement(overrideType, enabled);
        }

        public Statement ParseSetSpillOption(SpillOptionType option)
        {
            bool enabled;
            if (Match(TokenType.ON)) enabled = true;
            else if (Match(TokenType.OFF)) enabled = false;
            else throw new SyntaxException($"Expected ON or OFF after SET SPILL_{option.ToString().ToUpperInvariant()}", _parser.Current.Line, _parser.Current.Column);
            Match(TokenType.SEMICOLON);
            return new SetSpillOptionStatement(option, enabled);
        }

        public Statement ParseSetReportMetadata()
        {
            var startToken = _parser.Previous;
            string key;
            if (Match(TokenType.TITLE)) key = "TITLE";
            else if (Match(TokenType.DESCRIPTION)) key = "DESCRIPTION";
            else if (Match(TokenType.CSS)) key = "CSS";
            else if (Match(TokenType.JS)) key = "JS";
            else if (Match(TokenType.HEAD)) key = "HEAD";
            else if (Match(TokenType.BODY)) key = "BODY";
            else if (Match(TokenType.FOOTER)) key = "FOOTER";
            else if (Match(TokenType.FAVICON)) key = "FAVICON";
            else if (Match(TokenType.LOGO)) key = "LOGO";
            else if (Match(TokenType.BACKGROUND)) key = "BACKGROUND";
            else if (Match(TokenType.THEME)) key = "THEME";
            else if (Match(TokenType.NAVIGATION) || Match(TokenType.NAV_OVERRIDE)) key = "NAVIGATION";
            else if (_parser.Current.Type == TokenType.IDENTIFIER)
            {
                key = _parser.Current.Value.ToUpperInvariant();
                Advance();
            }
            else
                throw new SyntaxException("Expected report metadata key after SET REPORT", _parser.Current.Line, _parser.Current.Column);

            Consume(TokenType.EQUALS, $"Expected '=' after SET REPORT {key}");
            var valueToken = Consume(TokenType.STRING_LITERAL, $"Expected string value after SET REPORT {key} =");
            Match(TokenType.SEMICOLON);
            return new SetReportMetadataStatement { Key = key, Value = valueToken.Value };
        }

        public Statement ParseSetWeekStartDay()
        {
            var startToken = _parser.Previous;
            Consume(TokenType.EQUALS, "Expected '=' after SET WEEK_START_DAY");
            var dayToken = Consume(TokenType.STRING_LITERAL, "Expected day name string after SET WEEK_START_DAY =");
            Match(TokenType.SEMICOLON);
            return new SetWeekStartDayStatement(dayToken.Value) { Line = startToken.Line, Column = startToken.Column };
        }

        public Statement ParseSetScriptHashPolicy()
        {
            var startToken = _parser.Previous;
            Consume(TokenType.EQUALS, "Expected '=' after SET SCRIPT_HASH_POLICY");
            var policyToken = Consume(TokenType.STRING_LITERAL, "Expected 'Warn' or 'Block' after SET SCRIPT_HASH_POLICY =");
            Match(TokenType.SEMICOLON);
            return new SetScriptHashPolicyStatement(policyToken.Value) { Line = startToken.Line, Column = startToken.Column };
        }

        public Statement ParseSetTemplatePath()
        {
            var startToken = _parser.Previous;
            Consume(TokenType.EQUALS, "Expected '=' after SET TEMPLATE_PATH");
            var pathExpr = ParseExpression();
            if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
            return new SetTemplatePathStatement(pathExpr) { Line = startToken.Line, Column = startToken.Column };
        }

        public Statement ParseRun(Token startToken)
        {
            Consume(TokenType.SCRIPT, "Expected 'SCRIPT' after 'RUN'");
            var pathExpr = ParseExpression();
            var parameters = new List<RunScriptParameter>();
            if (Match(TokenType.WITH))
            {
                Consume(TokenType.LPAREN, "Expected '(' after WITH");
                while (_parser.Current.Type != TokenType.RPAREN && _parser.Current.Type != TokenType.EOF)
                {
                    var nameToken = Consume(TokenType.VARIABLE, "Expected parameter name starting with '@'");
                    Consume(TokenType.EQUALS, "Expected '='");
                    var value = ParseExpression();
                    bool isOutput = Match(TokenType.OUTPUT);
                    parameters.Add(new RunScriptParameter(nameToken.Value, value, isOutput));
                    if (!Match(TokenType.COMMA)) break;
                }
                Consume(TokenType.RPAREN, "Expected ')' after parameters");
            }
            if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
            return new RunScriptStatement(pathExpr, parameters) { Line = startToken.Line, Column = startToken.Column };
        }

        public Statement ParseUse(Token startToken)
        {
            if (Match(TokenType.DOCKER))
            {
                Consume(TokenType.LPAREN, "Expected '(' after DOCKER");
                var imageName = ParseExpression();
                Consume(TokenType.RPAREN, "Expected ')' after image name");
                string? alias = null;
                if (Match(TokenType.AS)) alias = ConsumeIdentifier("Expected alias after AS").Value;
                if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
                return new DockerStatement(imageName, alias) { Line = startToken.Line, Column = startToken.Column };
            }
            if (Match(TokenType.SETS))
            {
                Consume(TokenType.BANG, "Expected '!' before set name in USE SETS");
                var name = ConsumeIdentifier("Expected set name after '!'").Value;
                if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
                return new UseSetsStatement(name) { Line = startToken.Line, Column = startToken.Column };
            }
            if (Match(TokenType.PASSWORD))
            {
                Consume(TokenType.EQUALS, "Expected '=' after USE PASSWORD");
                var password = Consume(TokenType.STRING_LITERAL, "Expected password string").Value;
                if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
                return new UsePasswordStatement(password) { Line = startToken.Line, Column = startToken.Column };
            }
            throw new SyntaxException("Expected DOCKER, SETS, or PASSWORD after USE", _parser.Current.Line, _parser.Current.Column);
        }

        public Statement ParseClear(Token startToken)
        {
            bool isPlural = Match(TokenType.SESSIONS);
            if (!isPlural) Consume(TokenType.SESSION, "Expected SESSION or SESSIONS after CLEAR");

            ClearSessionMode mode = ClearSessionMode.Current;
            Expression? sessionId = null;

            if (_parser.Current.Type != TokenType.SEMICOLON && _parser.Current.Type != TokenType.EOF)
            {
                if (Match(TokenType.ALL))
                    mode = ClearSessionMode.All;
                else if (_parser.Current.Type == TokenType.IDENTIFIER && _parser.Current.Value.Equals("STALE", StringComparison.OrdinalIgnoreCase))
                {
                    Advance();
                    mode = ClearSessionMode.Stale;
                }
                else
                {
                    sessionId = ParseExpression();
                    mode = ClearSessionMode.Single;
                }
            }

            if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
            return new ClearSessionStatement(mode, sessionId) { Line = startToken.Line, Column = startToken.Column };
        }

        public Statement ParseHelp()
        {
            string? topic = null;
            string? subTopic = null;
            if (_parser.Current.Type != TokenType.SEMICOLON && _parser.Current.Type != TokenType.EOF)
            {
                topic = Advance().Value;
                if (_parser.Current.Type != TokenType.SEMICOLON && _parser.Current.Type != TokenType.EOF)
                    subTopic = Advance().Value;
            }
            Match(TokenType.SEMICOLON);
            return new HelpStatement(topic, subTopic);
        }

        public Statement ParseShow(Token startToken)
        {
            Statement? stmt = null;

            if (Match(TokenType.PROFILE) || Match(TokenType.PROFILING))
                stmt = new ShowProfileStatement();
            else if (Match(TokenType.JOB))
            {
                if (Match(TokenType.HISTORY))
                {
                    string? jobName = null;
                    if (_parser.Current.Type == TokenType.IDENTIFIER || _parser.Current.Type == TokenType.STRING_LITERAL)
                        jobName = Advance().Value;
                    stmt = new ShowJobHistoryStatement(jobName);
                }
                else
                    throw new SyntaxException("Expected HISTORY after SHOW JOB", _parser.Current.Line, _parser.Current.Column);
            }
            else if (Match(TokenType.JOBS))       stmt = new ShowJobsStatement();
            else if (Match(TokenType.CONNECTIONS)) stmt = new ShowConnectionsStatement();
            else if (Match(TokenType.CONNECTION))
            {
                var connName = ConsumeIdentifier("Expected connection name after SHOW CONNECTION").Value;
                Consume(TokenType.CONFIG, "Expected CONFIG after connection name");
                stmt = new ShowConnectionConfigStatement(connName);
            }
            else if (Match(TokenType.TABLES))
            {
                string? connName = null;
                if (Match(TokenType.ON)) connName = ConsumeIdentifier("Expected connection name after ON").Value;
                stmt = new ShowTablesStatement(connName);
            }
            else if (Match(TokenType.COLUMNS))
            {
                Consume(TokenType.FOR, "Expected FOR after SHOW COLUMNS");
                stmt = new ShowColumnsStatement(ParseTableReference());
            }
            else if (Match(TokenType.VARIABLES) || (_parser.Current.Type == TokenType.LOCAL && _parser.Peek.Type == TokenType.VARIABLES))
            {
                bool localOnly = false;
                if (Match(TokenType.LOCAL)) { localOnly = true; Consume(TokenType.VARIABLES, "Expected VARIABLES after SHOW LOCAL"); }
                stmt = new ShowVariablesStatement(localOnly);
            }
            else if (Match(TokenType.SCRIPT))
            {
                // SHOW SCRIPT TAGS [INTO #temp]
                if (Match(TokenType.TAGS) || Match(TokenType.TAG))
                    stmt = new ShowScriptTagsStatement();
                else
                    throw new SyntaxException("Expected TAGS after SHOW SCRIPT", _parser.Current.Line, _parser.Current.Column);
            }
            else if (Match(TokenType.TAGS))
            {
                // SHOW TAGS FOR SCRIPT | SHOW TAGS FOR TABLE <name> [COLUMN <col>]
                Consume(TokenType.FOR, "Expected FOR after SHOW TAGS");
                if (Match(TokenType.SCRIPT))
                {
                    stmt = new ShowScriptTagsStatement();
                }
                else
                {
                    Consume(TokenType.TABLE, "Expected TABLE after FOR");
                    var tableName = ConsumeIdentifier("Expected table name").Value;
                    string? columnName = null;
                    if (Match(TokenType.COLUMN)) columnName = ConsumeIdentifier("Expected column name").Value;
                    stmt = new ShowTagsStatement(tableName, columnName);
                }
            }
            else if (Match(TokenType.TAG))
            {
                Consume(TokenType.VALUE, "Expected VALUE after SHOW TAG");
                Consume(TokenType.FOR, "Expected FOR after SHOW TAG VALUE");
                Consume(TokenType.TABLE, "Expected TABLE after FOR");
                var tableName = ConsumeIdentifier("Expected table name").Value;
                string? columnName = null;
                if (Match(TokenType.COLUMN)) columnName = ConsumeIdentifier("Expected column name").Value;
                Consume(TokenType.WITH, "Expected WITH after table/column");
                Consume(TokenType.TAG, "Expected TAG after WITH");
                var tagName = ConsumeIdentifier("Expected tag name").Value;
                stmt = new ShowTagValueStatement(tableName, tagName, columnName);
            }
            else if (Match(TokenType.LINEAGE))
            {
                Match(TokenType.FOR);
                stmt = new LineageStatement(_parser.ParseTableReference());
            }
            else if (Match(TokenType.VERSION)) stmt = new ShowVersionStatement();
            else if (Match(TokenType.SAFE))
            {
                Consume(TokenType.ZONES, "Expected ZONES after SHOW SAFE");
                stmt = new ShowSafeZonesStatement();
            }
            else if (Match(TokenType.SESSIONS)) stmt = new ShowSessionsStatement();
            // Portal admin SHOW commands
            else if (Match(TokenType.USER) || MatchIdentifier("USERS"))
                stmt = new ShowPortalUsersStatement();
            else if (Match(TokenType.REPORT) || MatchIdentifier("REPORTS"))
            {
                string? folder = null;
                if (Match(TokenType.IN))
                {
                    Consume(TokenType.FOLDER, "Expected FOLDER");
                    folder = _parser.Current.Type == TokenType.STRING_LITERAL
                        ? Advance().Value
                        : ConsumeIdentifier("Expected folder path").Value;
                }
                stmt = new ShowPortalReportsStatement(folder);
            }
            else if (Match(TokenType.ACTIVE))
            {
                if (Match(TokenType.SESSIONS) || MatchIdentifier("SESSIONS"))
                    stmt = new ShowActivePortalSessionsStatement();
                else
                    throw new ETL_SQL.Core.Common.Exceptions.SyntaxException("Expected SESSIONS after SHOW ACTIVE", _parser.Current.Line, _parser.Current.Column);
            }

            if (stmt == null)
                throw new SyntaxException("Expected PROFILE, JOBS, JOB HISTORY, CONNECTIONS, TABLES, COLUMNS, VARIABLES, SCRIPT TAGS, TAGS, VERSION or LINEAGE after SHOW", _parser.Current.Line, _parser.Current.Column);

            if (Match(TokenType.INTO))
            {
                var tempTable = ConsumeIdentifier("Expected temporary table name after INTO").Value;
                if (!tempTable.StartsWith("#"))
                    throw new SyntaxException("SHOW ... INTO target must be a temporary table starting with '#'", _parser.Current.Line, _parser.Current.Column);

                stmt = stmt switch
                {
                    ShowProfileStatement sps     => sps with { IntoTable = tempTable },
                    ShowJobHistoryStatement sjh   => sjh with { IntoTable = tempTable },
                    ShowVariablesStatement v     => v with { IntoTable = tempTable },
                    ShowConnectionsStatement c   => c with { IntoTable = tempTable },
                    ShowConnectionConfigStatement cc => cc with { IntoTable = tempTable },
                    ShowScriptTagsStatement st   => st with { IntoTable = tempTable },
                    ShowJobsStatement j          => j with { IntoTable = tempTable },
                    ShowTablesStatement sts      => sts with { IntoTable = tempTable },
                    ShowColumnsStatement scols   => scols with { IntoTable = tempTable },
                    ShowTagsStatement stag       => stag with { IntoTable = tempTable },
                    ShowTagValueStatement stv    => stv with { IntoTable = tempTable },
                    ShowVersionStatement svs     => svs with { IntoTable = tempTable },
                    ShowSafeZonesStatement ssz   => ssz with { IntoTable = tempTable },
                    ShowSessionsStatement sess   => sess with { IntoTable = tempTable },
                    _                            => stmt
                };
            }

            if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
            return stmt with { Line = startToken.Line, Column = startToken.Column };
        }

        public Statement ParseExplain(Token startToken)
        {
            bool isAnalyze = Match(TokenType.ANALYZE);
            var stmt = _parser.ParseStatement();
            TableReference? intoTable = null;
            if (Match(TokenType.INTO))
            {
                var tempTable = ConsumeIdentifier("Expected temporary table name after INTO").Value;
                if (!tempTable.StartsWith("#"))
                    throw new SyntaxException("EXPLAIN ... INTO target must be a temporary table starting with '#'", _parser.Current.Line, _parser.Current.Column);
                intoTable = new TableReference(tempTable);
            }
            return new ExplainStatement(stmt, isAnalyze, intoTable) { Line = startToken.Line, Column = startToken.Column };
        }

        public Statement ParsePrint()
        {
            bool hasParen = Match(TokenType.LPAREN);
            var args = new List<Expression>();
            args.Add(ParseExpression());
            while (Match(TokenType.COMMA))
            {
                args.Add(ParseExpression());
            }
            if (hasParen) Consume(TokenType.RPAREN, "Expected ')' after PRINT arguments");
            if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
            return new PrintStatement(args);
        }

        public Statement ParseBeginTransaction()
        {
            string? name = null;
            if (_parser.IsIdentifier(_parser.Current)) name = Advance().Value;
            if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
            return new BeginTransactionStatement(name);
        }

        public Statement ParseCommitTransaction()
        {
            if (Match(TokenType.TRANSACTION) || Match(TokenType.TRAN)) { }
            string? name = null;
            if (_parser.IsIdentifier(_parser.Current)) name = Advance().Value;
            if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
            return new CommitTransactionStatement(name);
        }

        public Statement ParseRollbackTransaction()
        {
            if (Match(TokenType.TRANSACTION) || Match(TokenType.TRAN)) { }
            string? name = null;
            if (_parser.IsIdentifier(_parser.Current)) name = Advance().Value;
            if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
            return new RollbackTransactionStatement(name);
        }

        public Statement ParseRequireVersion(Token startToken)
        {
            Match(TokenType.VERSION);
            string op = ">=";
            if (Match(TokenType.GREATER_EQUALS)) op = ">=";
            else if (Match(TokenType.GREATER_THAN)) op = ">";
            else if (Match(TokenType.EQUALS)) op = "=";
            else throw new SyntaxException("Expected operator (>=, >, or =) after REQUIRE VERSION", _parser.Current.Line, _parser.Current.Column);
            var version = Consume(TokenType.STRING_LITERAL, "Expected version string literal after REQUIRE operator").Value.Trim('\'', '\"');
            if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
            return new RequireVersionStatement(op, version) { Line = startToken.Line, Column = startToken.Column };
        }
    }
}
