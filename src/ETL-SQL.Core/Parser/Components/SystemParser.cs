using System;
using System.Collections.Generic;
using ETL_SQL.Core.Common.Exceptions;

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

            Consume(TokenType.EQUALS, "Expected '=' after USE PASSWORD");
            var password = Consume(TokenType.STRING_LITERAL, "Expected password string").Value;
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
        else if (Match(TokenType.JOBS)) stmt = new ShowJobsStatement();
        else if (MatchIdentifier("PUBLISHED"))
        {
            ConsumeIdentifierValue("BUNDLES", "Expected BUNDLES after SHOW PUBLISHED");
            stmt = new ShowPublishedBundlesStatement();
        }
        else if (MatchIdentifier("BUNDLES"))
        {
            stmt = new ShowPublishedBundlesStatement { IsAlias = true };
        }
        else if (MatchIdentifier("BUNDLE"))
        {
            if (MatchIdentifier("VERSIONS"))
            {
                var bundleName = Consume(TokenType.STRING_LITERAL, "Expected bundle name string after SHOW BUNDLE VERSIONS").Value;
                stmt = new ShowBundleVersionsStatement(bundleName);
            }
            else if (Match(TokenType.FILES) || MatchIdentifier("FILES"))
            {
                var bundleName = Consume(TokenType.STRING_LITERAL, "Expected bundle name string after SHOW BUNDLE FILES").Value;
                Consume(TokenType.VERSION, "Expected VERSION after bundle name");
                var version = int.Parse(Consume(TokenType.NUMBER, "Expected bundle version number").Value);
                stmt = new ShowBundleFilesStatement(bundleName, version);
            }
            else if (MatchIdentifier("DEPENDENCIES"))
            {
                var bundleName = Consume(TokenType.STRING_LITERAL, "Expected bundle name string after SHOW BUNDLE DEPENDENCIES").Value;
                Consume(TokenType.VERSION, "Expected VERSION after bundle name");
                var version = int.Parse(Consume(TokenType.NUMBER, "Expected bundle version number").Value);
                stmt = new ShowBundleDependenciesStatement(bundleName, version);
            }
            else
            {
                throw new SyntaxException("Expected VERSIONS, FILES, or DEPENDENCIES after SHOW BUNDLE", _parser.Current.Line, _parser.Current.Column);
            }
        }
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
        else if (Match(TokenType.VIEW) || MatchIdentifier("VIEWS"))
        {
            stmt = new ShowViewsStatement();
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
            stmt = ParseShowLineage(startToken);
        }
        else if (Match(TokenType.VERSION)) stmt = new ShowVersionStatement();
        else if (Match(TokenType.SAFE))
        {
            Consume(TokenType.ZONES, "Expected ZONES after SHOW SAFE");
            stmt = new ShowSafeZonesStatement();
        }
        else if (Match(TokenType.SESSIONS)) stmt = new ShowSessionsStatement();
        else if (MatchIdentifier("LOCKS")) stmt = new ShowLocksStatement();
        // Portal admin SHOW commands
        else if (Match(TokenType.USER) || MatchIdentifier("USERS"))
            stmt = new ShowPortalUsersStatement();
        else if (Match(TokenType.REPORT))
        {
            if (Match(TokenType.HISTORY))
            {
                var reportName = Consume(TokenType.STRING_LITERAL, "Expected report name string literal after SHOW REPORT HISTORY").Value;
                stmt = new ShowPortalReportHistoryStatement(reportName);
            }
            else if (MatchIdentifier("DEPENDENCIES"))
            {
                var reportName = Consume(TokenType.STRING_LITERAL, "Expected report name string literal after SHOW REPORT DEPENDENCIES").Value;
                stmt = new ShowPortalReportDependenciesStatement(reportName);
            }
            else
            {
                var reportName = Consume(TokenType.STRING_LITERAL, "Expected report name string literal after SHOW REPORT").Value;
                stmt = new ShowPortalReportStatement(reportName);
            }
        }
        else if (MatchIdentifier("REPORTS"))
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
        else if (Match(TokenType.FAVORITE) || MatchIdentifier("FAVORITES"))
        {
            string? username = null;
            if (Match(TokenType.FOR))
            {
                Consume(TokenType.USER, "Expected USER after SHOW FAVORITES FOR");
                username = Consume(TokenType.STRING_LITERAL, "Expected username string literal").Value;
            }
            stmt = new ShowPortalFavoritesStatement(username, ParseOptionalLimit());
        }
        else if (Match(TokenType.SHARE))
        {
            if (!Match(TokenType.LINK) && !MatchIdentifier("LINKS"))
                throw new SyntaxException("Expected LINK or LINKS after SHOW SHARE", _parser.Current.Line, _parser.Current.Column);
            Consume(TokenType.FOR, "Expected FOR after SHOW SHARE LINK");
            Consume(TokenType.REPORT, "Expected REPORT");
            var reportName = Consume(TokenType.STRING_LITERAL, "Expected report name string literal").Value;
            stmt = new ShowPortalShareLinksStatement(reportName);
        }
        else if (Match(TokenType.EMBED))
        {
            if (!Match(TokenType.TOKEN) && !Match(TokenType.TOKENS) && !MatchIdentifier("TOKENS"))
                throw new SyntaxException("Expected TOKEN or TOKENS after SHOW EMBED", _parser.Current.Line, _parser.Current.Column);
            Consume(TokenType.FOR, "Expected FOR after SHOW EMBED TOKENS");
            Consume(TokenType.REPORT, "Expected REPORT");
            var reportName = Consume(TokenType.STRING_LITERAL, "Expected report name string literal").Value;
            stmt = new ShowPortalEmbedTokensStatement(reportName);
        }
        else if (Match(TokenType.SAVED))
        {
            if (!Match(TokenType.VIEW) && !MatchIdentifier("VIEWS"))
                throw new SyntaxException("Expected VIEW or VIEWS after SHOW SAVED", _parser.Current.Line, _parser.Current.Column);
            Consume(TokenType.FOR, "Expected FOR after SHOW SAVED VIEWS");
            Consume(TokenType.REPORT, "Expected REPORT");
            var reportName = Consume(TokenType.STRING_LITERAL, "Expected report name string literal").Value;
            stmt = new ShowPortalSavedViewsStatement(reportName);
        }
        else if (Match(TokenType.ALERT) || MatchIdentifier("ALERTS"))
        {
            Consume(TokenType.FOR, "Expected FOR after SHOW ALERTS");
            Consume(TokenType.REPORT, "Expected REPORT");
            var reportName = Consume(TokenType.STRING_LITERAL, "Expected report name string literal").Value;
            stmt = new ShowPortalAlertsStatement(reportName);
        }
        else if (Match(TokenType.SMTP))
        {
            if (!Match(TokenType.CONNECTION) && !Match(TokenType.CONNECTIONS))
                throw new SyntaxException("Expected CONNECTIONS after SHOW SMTP", _parser.Current.Line, _parser.Current.Column);
            stmt = new ShowPortalSmtpConnectionsStatement();
        }
        else if (MatchIdentifier("RECENT"))
        {
            ConsumeIdentifierValue("REPORTS", "Expected REPORTS after SHOW RECENT");
            stmt = new ShowPortalRecentReportsStatement(ParseOptionalLimit());
        }
        else if (Match(TokenType.CATALOG))
        {
            Consume(TokenType.SEARCH, "Expected SEARCH after SHOW CATALOG");
            var query = Consume(TokenType.STRING_LITERAL, "Expected catalog search string literal").Value;
            stmt = new SearchPortalCatalogStatement(query, ParseOptionalLimit());
        }
        else if (Match(TokenType.EFFECTIVE))
        {
            Consume(TokenType.PERMISSIONS, "Expected PERMISSIONS after SHOW EFFECTIVE");
            Consume(TokenType.FOR, "Expected FOR after SHOW EFFECTIVE PERMISSIONS");
            string targetType;
            if (Match(TokenType.USER))
                targetType = "USER";
            else if (Match(TokenType.REPORT))
                targetType = "REPORT";
            else if (Match(TokenType.FOLDER))
                targetType = "FOLDER";
            else
                throw new SyntaxException("Expected USER, REPORT, or FOLDER after SHOW EFFECTIVE PERMISSIONS FOR", _parser.Current.Line, _parser.Current.Column);

            var target = Consume(TokenType.STRING_LITERAL, "Expected target string literal").Value;
            stmt = new ShowEffectivePortalPermissionsStatement(targetType, target);
        }
        else if (Match(TokenType.PORTAL))
        {
            if (Match(TokenType.USAGE))
            {
                Match(TokenType.METRICS);
                stmt = new ShowPortalUsageMetricsStatement(ParseOptionalDays());
            }
            else if (MatchIdentifier("OPERATIONAL"))
            {
                Match(TokenType.METRICS);
                stmt = new ShowPortalOperationalMetricsStatement();
            }
            else
                throw new SyntaxException("Expected USAGE or OPERATIONAL after SHOW PORTAL", _parser.Current.Line, _parser.Current.Column);
        }
        else if (Match(TokenType.USAGE))
        {
            Match(TokenType.METRICS);
            stmt = new ShowPortalUsageMetricsStatement(ParseOptionalDays());
        }
        else if (Match(TokenType.ACTIVE))
        {
            if (Match(TokenType.SESSIONS) || MatchIdentifier("SESSIONS"))
                stmt = new ShowActivePortalSessionsStatement();
            else
                throw new ETL_SQL.Core.Common.Exceptions.SyntaxException("Expected SESSIONS after SHOW ACTIVE", _parser.Current.Line, _parser.Current.Column);
        }
        else if (Match(TokenType.DATASET) || MatchIdentifier("DATASETS"))
            stmt = new ShowDatasetsStatement();

        if (stmt == null)
            throw new SyntaxException("Expected PROFILE, JOBS, JOB HISTORY, CONNECTIONS, TABLES, COLUMNS, VARIABLES, SCRIPT TAGS, TAGS, VERSION, LINEAGE, LINEAGE HISTORY, or DATASETS after SHOW", _parser.Current.Line, _parser.Current.Column);

        if ((stmt is ShowJobsStatement || stmt is ShowJobHistoryStatement || stmt is ShowPublishedBundlesStatement ||
             stmt is ShowBundleVersionsStatement || stmt is ShowBundleFilesStatement || stmt is ShowBundleDependenciesStatement) && Match(TokenType.AT))
        {
            var atConn = ConsumeIdentifier("Expected connection name after AT").Value;
            stmt = stmt switch
            {
                ShowJobsStatement j => j with { At = atConn },
                ShowJobHistoryStatement h => h with { At = atConn },
                ShowPublishedBundlesStatement b => b with { At = atConn },
                ShowBundleVersionsStatement v => v with { At = atConn },
                ShowBundleFilesStatement f => f with { At = atConn },
                ShowBundleDependenciesStatement d => d with { At = atConn },
                _ => stmt
            };
        }

        if (Match(TokenType.INTO))
        {
            var tempTable = ConsumeIdentifier("Expected temporary table name after INTO").Value;
            if (!tempTable.StartsWith("#"))
                throw new SyntaxException("SHOW ... INTO target must be a temporary table starting with '#'", _parser.Current.Line, _parser.Current.Column);

            stmt = stmt switch
            {
                ShowProfileStatement sps => sps with { IntoTable = tempTable },
                ShowJobHistoryStatement sjh => sjh with { IntoTable = tempTable },
                ShowVariablesStatement v => v with { IntoTable = tempTable },
                ShowConnectionsStatement c => c with { IntoTable = tempTable },
                ShowConnectionConfigStatement cc => cc with { IntoTable = tempTable },
                ShowScriptTagsStatement st => st with { IntoTable = tempTable },
                ShowJobsStatement j => j with { IntoTable = tempTable },
                ShowTablesStatement sts => sts with { IntoTable = tempTable },
                ShowViewsStatement sv => sv with { IntoTable = tempTable },
                ShowColumnsStatement scols => scols with { IntoTable = tempTable },
                ShowTagsStatement stag => stag with { IntoTable = tempTable },
                ShowTagValueStatement stv => stv with { IntoTable = tempTable },
                ShowVersionStatement svs => svs with { IntoTable = tempTable },
                ShowSafeZonesStatement ssz => ssz with { IntoTable = tempTable },
                ShowSessionsStatement sess => sess with { IntoTable = tempTable },
                ShowLocksStatement sls => sls with { IntoTable = tempTable },
                ShowDatasetsStatement sds => sds with { IntoTable = tempTable },
                ShowPublishedBundlesStatement spb => spb with { IntoTable = tempTable },
                ShowBundleVersionsStatement sbv => sbv with { IntoTable = tempTable },
                ShowBundleFilesStatement sbf => sbf with { IntoTable = tempTable },
                ShowBundleDependenciesStatement sbd => sbd with { IntoTable = tempTable },
                ShowPortalReportsStatement sprs => sprs with { IntoTable = tempTable },
                ShowPortalReportStatement spr => spr with { IntoTable = tempTable },
                ShowPortalReportHistoryStatement sprh => sprh with { IntoTable = tempTable },
                ShowPortalReportDependenciesStatement sprd => sprd with { IntoTable = tempTable },
                ShowPortalShareLinksStatement spsl => spsl with { IntoTable = tempTable },
                ShowPortalEmbedTokensStatement spet => spet with { IntoTable = tempTable },
                ShowPortalSavedViewsStatement spsv => spsv with { IntoTable = tempTable },
                ShowPortalAlertsStatement spa => spa with { IntoTable = tempTable },
                ShowPortalFavoritesStatement spf => spf with { IntoTable = tempTable },
                ShowPortalRecentReportsStatement sprr => sprr with { IntoTable = tempTable },
                SearchPortalCatalogStatement spc => spc with { IntoTable = tempTable },
                ShowEffectivePortalPermissionsStatement sepp => sepp with { IntoTable = tempTable },
                ShowPortalUsageMetricsStatement spum => spum with { IntoTable = tempTable },
                ShowPortalOperationalMetricsStatement spom => spom with { IntoTable = tempTable },
                ShowActivePortalSessionsStatement saps => saps with { IntoTable = tempTable },
                ShowPortalSmtpConnectionsStatement ssmtp => ssmtp with { IntoTable = tempTable },
                LineageStatement lin => lin with { IntoTable = tempTable },
                ShowLineageHistoryForTableStatement slht => slht with { IntoTable = tempTable },
                ShowLineageHistoryForTagStatement slhg => slhg with { IntoTable = tempTable },
                ShowLineageHistoryForJobStatement slhj => slhj with { IntoTable = tempTable },
                _ => stmt
            };
        }

        if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
        return stmt with { Line = startToken.Line, Column = startToken.Column };
    }

    private int? ParseOptionalLimit()
    {
        if (!Match(TokenType.LIMIT))
            return null;

        return ParsePositiveInt("Expected positive integer after LIMIT");
    }

    private int? ParseOptionalDays()
    {
        if (!Match(TokenType.FOR))
            return null;

        int days = ParsePositiveInt("Expected positive integer after FOR");
        if (Match(TokenType.DAY))
            return days;
        if (_parser.Current.Type == TokenType.IDENTIFIER &&
            _parser.Current.Value.Equals("DAYS", StringComparison.OrdinalIgnoreCase))
        {
            Advance();
            return days;
        }

        throw new SyntaxException("Expected DAY or DAYS after usage metrics range", _parser.Current.Line, _parser.Current.Column);
    }

    private int ParsePositiveInt(string message)
    {
        var tok = Consume(TokenType.NUMBER, message);
        if (!int.TryParse(tok.Value, out int value) || value <= 0)
            throw new SyntaxException(message, tok.Line, tok.Column);
        return value;
    }

    private void ConsumeIdentifierValue(string value, string message)
    {
        var tok = _parser.Current;
        if (tok.Value.Equals(value, StringComparison.OrdinalIgnoreCase))
        {
            Advance();
            return;
        }

        throw new SyntaxException(message, tok.Line, tok.Column);
    }

    private Statement ParseShowLineage(Token startToken)
    {
        if (Match(TokenType.HISTORY))
            return ParseShowLineageHistory(startToken);

        return ParseShowLineageCore(startToken);
    }

    private Statement ParseShowLineageHistory(Token startToken)
    {
        Consume(TokenType.FOR, "Expected FOR after SHOW LINEAGE HISTORY");
        if (Match(TokenType.TABLE))
        {
            var tableName = ConsumeIdentifier("Expected table name after SHOW LINEAGE HISTORY FOR TABLE").Value;
            string? at = Match(TokenType.AT) ? ConsumeIdentifier("Expected connection name after AT").Value : null;
            var limit = ParseOptionalLimit();
            return new ShowLineageHistoryForTableStatement { TableName = tableName, At = at, Limit = limit };
        }
        if (Match(TokenType.TAG))
        {
            var tagKey = ConsumeIdentifier("Expected tag key after SHOW LINEAGE HISTORY FOR TAG").Value;
            string? tagValue = null;
            if (Match(TokenType.EQUALS))
                tagValue = Consume(TokenType.STRING_LITERAL, "Expected string value after =").Value;
            string? at = Match(TokenType.AT) ? ConsumeIdentifier("Expected connection name after AT").Value : null;
            var limit = ParseOptionalLimit();
            return new ShowLineageHistoryForTagStatement { TagKey = tagKey, TagValue = tagValue, At = at, Limit = limit };
        }
        if (Match(TokenType.JOB))
        {
            var jobName = ConsumeIdentifier("Expected job name after SHOW LINEAGE HISTORY FOR JOB").Value;
            string? at = Match(TokenType.AT) ? ConsumeIdentifier("Expected connection name after AT").Value : null;
            var limit = ParseOptionalLimit();
            return new ShowLineageHistoryForJobStatement { JobName = jobName, At = at, Limit = limit };
        }
        throw new SyntaxException("Expected TABLE, TAG, or JOB after SHOW LINEAGE HISTORY FOR", _parser.Current.Line, _parser.Current.Column);
    }

    private LineageStatement ParseShowLineageCore(Token startToken)
    {
        TableReference? targetTable = null;
        string? columnName = null;
        string? exportPath = null;
        bool exportAsOpenLineage = false;

        if (Match(TokenType.FOR))
        {
            if (Match(TokenType.REPORT))
            {
                var reportName = ConsumeIdentifier("Expected report name after SHOW LINEAGE FOR REPORT").Value;
                targetTable = new TableReference("report:" + reportName);
            }
            else if (Match(TokenType.DATASET))
            {
                var datasetName = ConsumeIdentifier("Expected dataset name after SHOW LINEAGE FOR DATASET").Value;
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

        if (_parser.Current.Type == TokenType.EXPORT)
        {
            Advance();
            if (Match(TokenType.AS))
            {
                var format = ConsumeIdentifier("Expected export format after AS").Value;
                if (!format.Equals("OPENLINEAGE", StringComparison.OrdinalIgnoreCase))
                    throw new SyntaxException("Expected OPENLINEAGE after AS", _parser.Previous.Line, _parser.Previous.Column);
                exportAsOpenLineage = true;
            }
            Consume(TokenType.TO, "Expected TO after lineage export format");
            exportPath = Consume(TokenType.STRING_LITERAL, "Expected file path after TO").Value;
        }
        else if (Match(TokenType.TO))
        {
            exportPath = Consume(TokenType.STRING_LITERAL, "Expected file path after TO").Value;
        }

        return new LineageStatement(targetTable, columnName, exportPath, exportAsOpenLineage)
        {
            Line = startToken.Line,
            Column = startToken.Column
        };
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
