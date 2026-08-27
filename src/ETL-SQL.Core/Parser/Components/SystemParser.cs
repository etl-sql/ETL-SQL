using System;
using System.Collections.Generic;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Reporting;

namespace ETL_SQL.Core.Parser.Components;

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
            bool isInput = Match(TokenType.INPUT);
            bool isOutput = Match(TokenType.OUTPUT);
            bool isRequired = Match(TokenType.REQUIRED);

            Expression? initialValue = null;
            if (Match(TokenType.EQUALS)) initialValue = ParseExpression();

            if (!isSensitive) isSensitive = Match(TokenType.PASSWORD);
            if (!isInput) isInput = Match(TokenType.INPUT);
            if (!isOutput) isOutput = Match(TokenType.OUTPUT);
            if (!isRequired) isRequired = Match(TokenType.REQUIRED);

            Dictionary<string, string>? metadata = null;
            while (Match(TokenType.COLUMN_TAG))
            {
                if (metadata == null) metadata = new(StringComparer.OrdinalIgnoreCase);
                _parser.ParseMetadataTags(_parser.Previous.Value, metadata);
            }

            bool isSecret = type != null && type.Equals("SECRET", StringComparison.OrdinalIgnoreCase);

            var stmt = new DeclareStatement(varToken.Value, type ?? "", initialValue, isSensitive, isInput, isOutput, isRequired, metadata)
            {
                Line = varToken.Line,
                Column = varToken.Column,
                EndLine = _parser.LastTokenEndLine,
                EndColumn = _parser.LastTokenEndColumn,
                IsSensitive = isSensitive,
                IsSecret = isSecret,
                IsInput = isInput,
                IsOutput = isOutput,
                IsRequired = isRequired
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

    public Statement ParseSetPersist()
    {
        bool enabled;
        if (Match(TokenType.ON)) enabled = true;
        else if (Match(TokenType.OFF)) enabled = false;
        else throw new SyntaxException("Expected ON or OFF after SET PERSIST", _parser.Current.Line, _parser.Current.Column);
        Match(TokenType.SEMICOLON);
        return new SetPersistStatement { Enabled = enabled };
    }

    public Statement ParseSetShowPassword()
    {
        var startToken = _parser.Previous;
        var enabled = ParseOptionalEqualsOnOff($"SET {startToken.Value}");
        Match(TokenType.SEMICOLON);
        return new SetShowPasswordStatement(enabled);
    }

    public Statement ParseSetAllowPlaintextSecrets()
    {
        var enabled = ParseOptionalEqualsOnOff("SET ALLOW_PLAINTEXT_SECRETS");
        Match(TokenType.SEMICOLON);
        return new SetAllowPlaintextSecretsStatement(enabled);
    }

    public Statement ParseSetNoSaveSensitive()
    {
        var enabled = ParseOptionalEqualsOnOff("SET NO_SAVE_SENSITIVE");
        Match(TokenType.SEMICOLON);
        return new SetNoSaveSensitiveStatement(enabled);
    }

    public Statement ParseSetNoSaveConnection()
    {
        var enabled = ParseOptionalEqualsOnOff("SET NO_SAVE_CONNECTION");
        Match(TokenType.SEMICOLON);
        return new SetNoSaveConnectionStatement(enabled);
    }

    public Statement ParseSetConnectionEncryption()
    {
        var enabled = ParseOptionalEqualsOnOff("SET CONNECTION_ENCRYPTION");
        Match(TokenType.SEMICOLON);
        return new SetConnectionEncryptionStatement(enabled);
    }

    private bool ParseOptionalEqualsOnOff(string settingName)
    {
        Match(TokenType.EQUALS);
        if (Match(TokenType.ON)) return true;
        if (Match(TokenType.OFF)) return false;
        throw new SyntaxException($"Expected ON or OFF after {settingName}", _parser.Current.Line, _parser.Current.Column);
    }

    public Statement ParseSetThreshold(ThresholdType type)
    {
        var startToken = _parser.Previous;
        Expression value;

        if (Match(TokenType.ON)) value = new LiteralExpression(true, TokenType.TRUE);
        else if (Match(TokenType.OFF)) value = new LiteralExpression(false, TokenType.FALSE);
        else
        {
            Consume(TokenType.EQUALS, $"Expected '=', ON, or OFF after SET {startToken.Value}");
            value = ParseExpression();
        }

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
            if (Match(TokenType.EQUALS))
            {
                var expr = ParseExpression();
                if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
                return new SetThresholdStatement(ThresholdType.MaxFileOperations, expr) { Line = startToken.Line, Column = startToken.Column };
            }

            if (startToken.Value == "ALLOW_FILE_OPERATIONS")
                throw new SyntaxException("Expected '=' after ALLOW_FILE_OPERATIONS", startToken.Line, startToken.Column);

            overrideType = SecurityOverride.LargeFileCount;
        }
        else if (val == "ALLOW_RECURSIVE_LAYERS" || (val.StartsWith("ALLOW_RECURSIVE_GREATER_THAN_") && val.EndsWith("_LAYERS")))
        {
            if (Match(TokenType.EQUALS))
            {
                var expr = ParseExpression();
                if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
                return new SetThresholdStatement(ThresholdType.MaxRecursiveDepth, expr) { Line = startToken.Line, Column = startToken.Column };
            }

            if (startToken.Value == "ALLOW_RECURSIVE_LAYERS")
                throw new SyntaxException("Expected '=' after ALLOW_RECURSIVE_LAYERS", startToken.Line, startToken.Column);

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
            var keyToken = _parser.Current;
            Advance();
            // COMPAT_BREAK: 0.19
            // Closed key set. An unrecognised key used to parse and then evaporate in the handler, so a
            // typo produced a report that looked configured and was not.
            if (!ReportMetadataKeys.IsKnown(key))
                throw new SyntaxException(ReportMetadataKeys.UnknownKeyMessage(keyToken.Value), keyToken.Line, keyToken.Column);
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
            Consume(TokenType.LPAREN, "Expected '(' after WITH in RUN SCRIPT");
            while (_parser.Current.Type != TokenType.RPAREN && _parser.Current.Type != TokenType.EOF)
            {
                var nameToken = Consume(TokenType.VARIABLE, "Expected parameter name starting with '@'");
                Consume(TokenType.EQUALS, "Expected '=' after parameter name in RUN SCRIPT WITH");
                var value = ParseExpression();
                bool isOutput = Match(TokenType.OUTPUT);
                parameters.Add(new RunScriptParameter(nameToken.Value, value, isOutput));
                if (!Match(TokenType.COMMA)) break;
            }
            Consume(TokenType.RPAREN, "Expected ')' to close RUN SCRIPT WITH parameter list");
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
            if (MatchIdentifier("PROMPT"))
            {
                if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
                return new UsePasswordStatement(prompt: true) { Line = startToken.Line, Column = startToken.Column };
            }

            if (Match(TokenType.EQUALS))
            {
                // Allow optional '='
            }
            var password = Consume(TokenType.STRING_LITERAL, "Expected password string after USE PASSWORD").Value;
            if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
            return new UsePasswordStatement(password) { Line = startToken.Line, Column = startToken.Column };
        }
        if (Match(TokenType.DATASET))
        {
            // &name tokenised as a single IDENTIFIER (Lexer includes & in identifier reads)
            var tok = ConsumeIdentifier("Expected &datasetName after USE DATASET");
            if (!tok.Value.StartsWith("&"))
                throw new SyntaxException("USE DATASET names must use the &dataset form", tok.Line, tok.Column);
            var dsName = tok.Value;
            if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
            return new UseDatasetStatement { DatasetName = dsName, Line = startToken.Line, Column = startToken.Column };
        }
        throw new SyntaxException("Expected DOCKER, SETS, PASSWORD, or DATASET after USE", _parser.Current.Line, _parser.Current.Column);
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
        if (Match(TokenType.PROFILE) || Match(TokenType.PROFILING))
            throw new SyntaxException("SHOW PROFILE has been retired. Use SELECT * FROM eng.profile.", startToken.Line, startToken.Column);

        if (Match(TokenType.JOB))
            throw new SyntaxException("SHOW JOB, SHOW JOB HISTORY, and SHOW JOB STATE have been retired. Use SELECT * FROM eng.jobs, eng.job_history, or eng.job_state.", startToken.Line, startToken.Column);

        if (Match(TokenType.JOBS))
            throw new SyntaxException("SHOW JOBS has been retired. Use SELECT * FROM eng.jobs.", startToken.Line, startToken.Column);

        if (MatchIdentifier("HOST"))
            throw new SyntaxException("SHOW HOST METRICS has been retired. Use SELECT * FROM eng.host_metrics.", startToken.Line, startToken.Column);

        if (MatchIdentifier("PUBLISHED") || MatchIdentifier("BUNDLES") || MatchIdentifier("BUNDLE"))
            throw new SyntaxException("SHOW BUNDLE commands have been retired. Use SELECT * FROM eng.bundles, eng.bundle_files, or eng.bundle_dependencies.", startToken.Line, startToken.Column);

        if (Match(TokenType.CONNECTIONS) || Match(TokenType.CONNECTION))
            throw new SyntaxException("SHOW CONNECTIONS and SHOW CONNECTION CONFIG have been retired. Use SELECT * FROM eng.connections or eng.connection_config.", startToken.Line, startToken.Column);

        if (Match(TokenType.TABLES))
            throw new SyntaxException("SHOW TABLES has been retired. Use SELECT * FROM eng.tables.", startToken.Line, startToken.Column);

        if (Match(TokenType.VIEW) || MatchIdentifier("VIEWS"))
            throw new SyntaxException("SHOW VIEWS has been retired. Use SELECT * FROM eng.views.", startToken.Line, startToken.Column);

        if (Match(TokenType.COLUMNS) || Match(TokenType.SCHEMA) || MatchIdentifier("SCHEMA"))
            throw new SyntaxException("SHOW COLUMNS and SHOW SCHEMA have been retired. Use SELECT * FROM eng.columns WHERE table_name = '<table>'.", startToken.Line, startToken.Column);

        if (Match(TokenType.VARIABLES) || (_parser.Current.Type == TokenType.LOCAL && _parser.Peek.Type == TokenType.VARIABLES))
            throw new SyntaxException("SHOW VARIABLES has been retired. Use SELECT * FROM eng.variables.", startToken.Line, startToken.Column);

        if (Match(TokenType.SCRIPT))
        {
            if (Match(TokenType.TAGS) || Match(TokenType.TAG))
                throw new SyntaxException("SHOW SCRIPT TAGS has been retired. Use SELECT ... FROM eng.tags.", startToken.Line, startToken.Column);
            throw new SyntaxException("Expected a supported SHOW SCRIPT subcommand", _parser.Current.Line, _parser.Current.Column);
        }

        if (Match(TokenType.TAGS))
            throw new SyntaxException("SHOW TAGS has been retired. Use SELECT ... FROM eng.tags.", startToken.Line, startToken.Column);

        if (Match(TokenType.TAG))
            throw new SyntaxException("SHOW TAG VALUE has been retired. Use SELECT ... FROM eng.tags.", startToken.Line, startToken.Column);

        if (Match(TokenType.LINEAGE))
        {
            bool isExport = false;
            string targetTableToken = "";
            int offset = 0;
            while (_parser.LookAhead(offset).Type != TokenType.SEMICOLON && _parser.LookAhead(offset).Type != TokenType.EOF)
            {
                var tok = _parser.LookAhead(offset);
                if (tok.Type == TokenType.EXPORT || tok.Type == TokenType.TO)
                {
                    isExport = true;
                }
                if (tok.Type == TokenType.FOR)
                {
                    targetTableToken = _parser.LookAhead(offset + 1).Value;
                }
                offset++;
            }

            if (isExport)
            {
                var replacement = string.IsNullOrEmpty(targetTableToken)
                    ? "EXPORT LINEAGE AS OPENLINEAGE"
                    : "EXPORT LINEAGE FOR <target>";
                throw new SyntaxException($"SHOW LINEAGE ... EXPORT has been retired because it writes a file. Use {replacement}.", startToken.Line, startToken.Column);
            }

            throw new SyntaxException("SHOW LINEAGE has been retired. Use SELECT * FROM eng.lineage or eng.lineage_history.", startToken.Line, startToken.Column);
        }

        if (MatchIdentifier("PROTECTED"))
            throw new SyntaxException("SHOW PROTECTED DATA has been retired. Use SELECT * FROM eng.protected_data or eng.protected_data_suggestions.", startToken.Line, startToken.Column);

        if (MatchIdentifier("DATA") && MatchIdentifier("QUALITY"))
            throw new SyntaxException("SHOW DATA QUALITY RULES has been retired. Use SELECT * FROM eng.data_quality_rules.", startToken.Line, startToken.Column);

        if (Match(TokenType.VERSION))
            throw new SyntaxException("SHOW VERSION has been retired. Use SELECT * FROM eng.version.", startToken.Line, startToken.Column);

        if (Match(TokenType.SAFE))
        {
            Consume(TokenType.ZONES, "Expected ZONES after SHOW SAFE");
            throw new SyntaxException("SHOW SAFE ZONES has been retired. Use SELECT * FROM eng.safe_zones.", startToken.Line, startToken.Column);
        }

        if (Match(TokenType.SESSIONS))
            throw new SyntaxException("SHOW SESSIONS has been retired. Use SELECT * FROM eng.sessions.", startToken.Line, startToken.Column);

        if (MatchIdentifier("LOCKS"))
            throw new SyntaxException("SHOW LOCKS has been retired. Use SELECT * FROM eng.locks.", startToken.Line, startToken.Column);

        if (Match(TokenType.USER) || MatchIdentifier("USERS"))
            throw new SyntaxException("SHOW PORTAL USERS has been retired. Use SELECT * FROM <conn>.eng.users.", startToken.Line, startToken.Column);

        if (Match(TokenType.REPORT) || MatchIdentifier("REPORTS") || MatchIdentifier("RECENT"))
            throw new SyntaxException("SHOW REPORT commands have been retired. Use SELECT * FROM <conn>.eng.reports, eng.report_history, eng.report_dependencies, or eng.recent_reports.", startToken.Line, startToken.Column);

        if (Match(TokenType.FAVORITE) || MatchIdentifier("FAVORITES"))
            throw new SyntaxException("SHOW PORTAL FAVORITES has been retired. Use SELECT * FROM <conn>.eng.favorites.", startToken.Line, startToken.Column);

        if (Match(TokenType.SHARE))
            throw new SyntaxException("SHOW PORTAL SHARE LINKS has been retired. Use SELECT * FROM <conn>.eng.share_links.", startToken.Line, startToken.Column);

        if (Match(TokenType.EMBED))
            throw new SyntaxException("SHOW PORTAL EMBED TOKENS has been retired. Use SELECT * FROM <conn>.eng.embed_tokens.", startToken.Line, startToken.Column);

        if (Match(TokenType.SAVED))
            throw new SyntaxException("SHOW PORTAL SAVED VIEWS has been retired. Use SELECT * FROM <conn>.eng.saved_views.", startToken.Line, startToken.Column);

        if (Match(TokenType.ALERT) || MatchIdentifier("ALERTS"))
            throw new SyntaxException("SHOW PORTAL ALERTS has been retired. Use SELECT * FROM <conn>.eng.alerts.", startToken.Line, startToken.Column);

        if (Match(TokenType.SMTP))
            throw new SyntaxException("SHOW SMTP CONNECTIONS has been retired. Use SELECT * FROM eng.connections or eng.connection_config.", startToken.Line, startToken.Column);

        if (Match(TokenType.CATALOG))
            throw new SyntaxException("SHOW CATALOG SEARCH has been retired. Use SELECT * FROM <conn>.eng.catalog_search('query').", startToken.Line, startToken.Column);

        if (Match(TokenType.EFFECTIVE))
            throw new SyntaxException("SHOW EFFECTIVE PERMISSIONS has been retired. Use SELECT * FROM <conn>.eng.effective_permissions.", startToken.Line, startToken.Column);

        if (Match(TokenType.PORTAL) || Match(TokenType.USAGE) || Match(TokenType.ACTIVE))
            throw new SyntaxException("SHOW PORTAL, SHOW USAGE, and SHOW ACTIVE commands have been retired. Use SELECT * FROM <conn>.eng.usage_metrics, eng.operational_metrics, eng.audit, or eng.active_sessions.", startToken.Line, startToken.Column);

        if (Match(TokenType.DATASET) || Match(TokenType.DATASETS) || MatchIdentifier("DATASETS"))
            throw new SyntaxException("SHOW DATASETS has been retired. Use SELECT * FROM eng.tables.", startToken.Line, startToken.Column);

        throw new SyntaxException("Expected PROFILE, JOBS, JOB HISTORY, CONNECTIONS, TABLES, COLUMNS, VARIABLES, SCRIPT TAGS, TAGS, VERSION, LINEAGE, LINEAGE HISTORY, or DATASETS after SHOW", startToken.Line, startToken.Column);
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
        else if (Match(TokenType.LESS_EQUALS)) op = "<=";
        else if (Match(TokenType.LESS_THAN)) op = "<";
        else if (Match(TokenType.EQUALS)) op = "=";
        else throw new SyntaxException("Expected operator (>=, >, <=, <, or =) after REQUIRE VERSION", _parser.Current.Line, _parser.Current.Column);
        var version = Consume(TokenType.STRING_LITERAL, "Expected version string literal after REQUIRE operator").Value.Trim('\'', '\"');
        if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
        return new RequireVersionStatement(op, version) { Line = startToken.Line, Column = startToken.Column };
    }
}
