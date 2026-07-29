using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;

namespace ETL_SQL.Core.Parser.Components;

public class DataParser : ParserComponent
{
    public DataParser(IParser parser, StatementParser parent) : base(parser, parent) { }

    public Statement ParseGenerate(Token startToken)
    {
        if (Match(TokenType.SECRET) || (_parser.Current.Type == TokenType.IDENTIFIER && _parser.Current.Value.Equals("JWT_SECRET", StringComparison.OrdinalIgnoreCase)))
        {
            if (_parser.Previous.Type == TokenType.IDENTIFIER) Advance(); // Consume JWT_SECRET if it was an identifier
            if (Match(TokenType.SEMICOLON)) { }
            return new GenerateJwtSecretStatement { Line = startToken.Line, Column = startToken.Column };
        }

        if (Match(TokenType.CALENDAR) || (_parser.Current.Type == TokenType.IDENTIFIER && _parser.Current.Value.Equals("CALENDAR", StringComparison.OrdinalIgnoreCase)))
        {
            if (_parser.Previous.Type == TokenType.IDENTIFIER) Advance();
            Consume(TokenType.FROM, "Expected 'FROM' after GENERATE CALENDAR");
            var startDate = ParseExpression();
            Consume(TokenType.TO, "Expected 'TO' after start date");
            var endDate = ParseExpression();
            Consume(TokenType.INTO, "Expected 'INTO' after end date");
            var calTarget = _parser.ParseTableReference(allowFunction: false, allowWithClause: false, allowAlias: false);
            if (Match(TokenType.SEMICOLON)) { }
            return new GenerateCalendarStatement(startDate, endDate, calTarget) { Line = startToken.Line, Column = startToken.Column };
        }

        var rowCount = ParseExpression();

        Consume(TokenType.ROWS, "Expected 'ROWS' after count");
        Consume(TokenType.INTO, "Expected 'INTO' after ROWS");
        var target = _parser.ParseTableReference(allowFunction: false, allowWithClause: false, allowAlias: false);

        Dictionary<string, Expression>? options = null;
        if (Match(TokenType.WITH))
        {
            options = new Dictionary<string, Expression>(StringComparer.OrdinalIgnoreCase);
            Consume(TokenType.LPAREN, "Expected '(' after WITH");
            while (!Match(TokenType.RPAREN))
            {
                var keyTok = Advance();
                Consume(TokenType.EQUALS, "Expected '='");
                var valTok = ParseExpression();
                options[keyTok.Value] = valTok;
                if (!Match(TokenType.COMMA))
                {
                    Consume(TokenType.RPAREN, "Expected ')' or ','");
                    break;
                }
            }
        }

        Consume(TokenType.AS, "Expected 'AS' before generation rules");
        Consume(TokenType.LPAREN, "Expected '(' before rules list");

        var rules = new List<GenerateRule>();
        while (_parser.Current.Type != TokenType.RPAREN && _parser.Current.Type != TokenType.EOF)
        {
            var colName = ConsumeIdentifier("Expected column name").Value;
            Consume(TokenType.EQUALS, "Expected '='");
            var ruleStr = Consume(TokenType.STRING_LITERAL, "Expected rule string (e.g. 'SEQUENCE(...)')").Value;
            rules.Add(new GenerateRule(colName, ruleStr.Trim('\'', '\"')));
            if (!Match(TokenType.COMMA)) break;
        }
        Consume(TokenType.RPAREN, "Expected ')' after rules list");

        if (_parser.Current.Type == TokenType.SEMICOLON) Advance();

        return new GenerateStatement(rowCount, target, rules, options)
        {
            Line = startToken.Line,
            Column = startToken.Column
        };
    }

    public Statement ParseCompare(Token startToken)
    {
        if (Match(TokenType.DATASETS) || (_parser.Current.Type == TokenType.IDENTIFIER && _parser.Current.Value.Equals("DATASETS", StringComparison.OrdinalIgnoreCase)))
        {
            if (_parser.Previous.Type == TokenType.IDENTIFIER) Advance();
        }

        var sourceTable = _parser.ParseTableReference(allowFunction: false, allowWithClause: false, allowAlias: false);
        Consume(TokenType.WITH, "Expected 'WITH' after source table name");
        var baselineTable = _parser.ParseTableReference(allowFunction: false, allowWithClause: false, allowAlias: false);

        Consume(TokenType.KEY, "Expected 'KEY' keyword in COMPARE DATASETS statement");
        Consume(TokenType.LPAREN, "Expected '(' after KEY");

        var keyCols = new List<string>();
        while (_parser.Current.Type != TokenType.RPAREN && _parser.Current.Type != TokenType.EOF)
        {
            var keyCol = ConsumeIdentifier("Expected column name in KEY list").Value;
            keyCols.Add(keyCol);
            if (!Match(TokenType.COMMA)) break;
        }
        Consume(TokenType.RPAREN, "Expected ')' after KEY column list");

        List<string>? excludeCols = null;
        if (Match(TokenType.EXCLUDE))
        {
            Consume(TokenType.LPAREN, "Expected '(' after EXCLUDE");
            excludeCols = new List<string>();
            while (_parser.Current.Type != TokenType.RPAREN && _parser.Current.Type != TokenType.EOF)
            {
                var exCol = ConsumeIdentifier("Expected column name in EXCLUDE list").Value;
                excludeCols.Add(exCol);
                if (!Match(TokenType.COMMA)) break;
            }
            Consume(TokenType.RPAREN, "Expected ')' after EXCLUDE column list");
        }

        Consume(TokenType.INTO, "Expected 'INTO' before target diff table name");
        var targetTable = _parser.ParseTableReference(allowFunction: false, allowWithClause: false, allowAlias: false);

        if (Match(TokenType.SEMICOLON)) { }

        return new CompareDatasetsStatement(sourceTable, baselineTable, keyCols, excludeCols, targetTable)
        {
            Line = startToken.Line,
            Column = startToken.Column
        };
    }



    public Statement ParseTransform(Token startToken)
    {
        var targetTable = _parser.ParseTableReference(allowFunction: false, allowWithClause: false, allowAlias: false);
        TableReference? sourceTable = null;
        if (Match(TokenType.FROM))
        {
            sourceTable = _parser.ParseTableReference(allowFunction: false, allowWithClause: false, allowAlias: false);
        }
        Consume(TokenType.USING, "Expected 'USING' after FROM source");
        Token algorithmTok;
        if (_parser.Current.Type < TokenType.STAR)
        {
            algorithmTok = Advance();
        }
        else
        {
            algorithmTok = ConsumeIdentifier("Expected algorithm name after USING");
        }
        var algorithm = algorithmTok.Value;

        var options = new Dictionary<string, Expression>(StringComparer.OrdinalIgnoreCase);
        Consume(TokenType.LPAREN, $"Expected '(' after {algorithm}");
        while (!Match(TokenType.RPAREN))
        {
            var keyTok = ConsumeIdentifier("Expected parameter name");
            Consume(TokenType.EQUALS, "Expected '='");
            var valTok = ParseExpression();
            options[keyTok.Value] = valTok;
            if (!Match(TokenType.COMMA))
            {
                Consume(TokenType.RPAREN, "Expected ')' or ','");
                break;
            }
        }
        Match(TokenType.SEMICOLON);

        return new TransformStatement(targetTable, sourceTable, algorithm, options)
        {
            Line = startToken.Line,
            Column = startToken.Column
        };
    }

    private static string ExpressionToOptionString(Expression expression, string optionName)
    {
        return expression switch
        {
            LiteralExpression { Value: string s } => s,
            IdentifierExpression c => c.Name,
            _ => throw new SyntaxException($"{optionName} must be a string literal or identifier", expression.Line, expression.Column)
        };
    }

    public Statement ParseCreate(Token startToken)
    {
        bool orAlter = false, orReplace = false;
        if (Match(TokenType.OR))
        {
            if (Match(TokenType.ALTER)) orAlter = true;
            else if (Match(TokenType.REPLACE)) orReplace = true;
            else throw new SyntaxException("Expected ALTER or REPLACE after CREATE OR", _parser.Current.Line, _parser.Current.Column);
        }
        var mode = orAlter ? ObjectCreationMode.CreateOrAlter
                 : orReplace ? ObjectCreationMode.CreateOrReplace
                 : ObjectCreationMode.Create;

        if (Match(TokenType.CONNECTION))
        {
            RejectUnsupportedCreateIfNotExists("CONNECTION");
            return ParseCreateConnection(startToken, mode);
        }
        if (Match(TokenType.TABLE))
        {
            RejectUnsupportedCreateMode(mode, "TABLE", ObjectCreationMode.CreateOrReplace);
            var ct = (CreateTableStatement)ParseCreateTable(startToken);
            return orReplace ? ct with { OrReplace = true } : ct;
        }
        if (Match(TokenType.PROCEDURE))
        {
            RejectUnsupportedCreateIfNotExists("PROCEDURE");
            return ParseCreateProcedure(startToken, mode);
        }
        if (Match(TokenType.FUNCTION))
        {
            RejectUnsupportedCreateIfNotExists("FUNCTION");
            return ParseCreateFunction(startToken, mode);
        }
        if (Match(TokenType.VIEW))
        {
            RejectUnsupportedCreateIfNotExists("VIEW");
            return ParseCreateView(startToken, mode);
        }
        if (Match(TokenType.JOB))
        {
            RejectUnsupportedCreateIfNotExists("JOB");
            return ParseCreateJob(startToken, mode);
        }

        // Scheduler catalog. Both creation modes are supported: an exported configuration script
        // must converge when replayed, which is the whole reason these objects keep a stable name.
        if (Match(TokenType.SCHEDULE))
        {
            RejectUnsupportedCreateIfNotExists("SCHEDULE");
            return _parent.CatalogParser.ParseCreateSchedule(startToken, mode);
        }
        if (_parent.CatalogParser.IsNotificationKeyword())
        {
            Advance();
            RejectUnsupportedCreateIfNotExists("NOTIFICATION");
            return _parent.CatalogParser.ParseCreateNotification(startToken, mode);
        }

        if (Match(TokenType.DIRECTORY))
        {
            RejectUnsupportedCreateMode(mode, "DIRECTORY");
            RejectUnsupportedCreateIfNotExists("DIRECTORY");
            var path = ParseExpression();
            Expression? overwrite = null;
            if (Match(TokenType.WITH)) overwrite = ParseWithOverwrite();
            string? connectionName = null;
            if (Match(TokenType.AT)) { connectionName = ConsumeIdentifier("Expected connection name after AT").Value; }
            Match(TokenType.SEMICOLON);
            return new DirectoryOperationStatement(DirectoryOpType.Create, path, null, overwrite, connectionName: connectionName) { Line = startToken.Line, Column = startToken.Column };
        }

        if (_parser.Current.Type == TokenType.UNIQUE || _parser.Current.Type == TokenType.INDEX)
        {
            RejectUnsupportedCreateMode(mode, "INDEX");
            bool isUnique = Match(TokenType.UNIQUE);
            return ParseCreateIndex(startToken, isUnique);
        }

        if (Match(TokenType.SSH_KEY_PAIR))
        {
            RejectUnsupportedCreateMode(mode, "SSH_KEY_PAIR");
            RejectUnsupportedCreateIfNotExists("SSH_KEY_PAIR");
            return ParseCreateSshKeyPair(startToken);
        }

        if (Match(TokenType.PGP_KEY_PAIR))
        {
            RejectUnsupportedCreateMode(mode, "PGP_KEY_PAIR");
            RejectUnsupportedCreateIfNotExists("PGP_KEY_PAIR");
            return ParseCreatePgpKeyPair(startToken);
        }

        if (Match(TokenType.SETS))
        {
            RejectUnsupportedCreateMode(mode, "SETS");
            RejectUnsupportedCreateIfNotExists("SETS");
            return ParseCreateSets(startToken);
        }

        if (Match(TokenType.TAG))
        {
            RejectUnsupportedCreateMode(mode, "TAG");
            RejectUnsupportedCreateIfNotExists("TAG");
            throw new SyntaxException("CREATE TAG has been retired. Use INSERT TAG FOR TABLE <table> [COLUMN <column>] (...).", startToken.Line, startToken.Column);
        }

        if (Match(TokenType.LINEAGE))
        {
            RejectUnsupportedCreateMode(mode, "LINEAGE");
            RejectUnsupportedCreateIfNotExists("LINEAGE");
            throw new SyntaxException("CREATE LINEAGE has been retired. Use INSERT LINEAGE FOR TABLE <table> FROM <source>.", startToken.Line, startToken.Column);
        }

        // Report-SQL
        if (Match(TokenType.VISUAL))
        {
            RejectUnsupportedCreateIfNotExists("VISUAL");
            return _parent.ReportParser.ParseCreateVisual(startToken, mode);
        }
        if (Match(TokenType.PAGE))
        {
            RejectUnsupportedCreateIfNotExists("PAGE");
            return _parent.ReportParser.ParseCreatePage(startToken, mode);
        }
        if (Match(TokenType.DATASET))
        {
            RejectUnsupportedCreateIfNotExists("DATASET");
            return _parent.ReportParser.ParseCreateDataset(startToken, mode);
        }
        if (Match(TokenType.CONTAINER))
        {
            RejectUnsupportedCreateIfNotExists("CONTAINER");
            return _parent.ReportParser.ParseCreateContainer(startToken, mode);
        }
        if (Match(TokenType.NAVIGATION))
        {
            RejectUnsupportedCreateIfNotExists("NAVIGATION");
            return _parent.ReportParser.ParseCreateNavigation(startToken, mode);
        }
        if (Match(TokenType.STYLE))
        {
            RejectUnsupportedCreateIfNotExists("STYLE");
            return _parent.ReportParser.ParseCreateStyle(startToken, mode);
        }
        if (Match(TokenType.BUTTON))
        {
            RejectUnsupportedCreateIfNotExists("BUTTON");
            return _parent.ReportParser.ParseCreateButton(startToken, mode);
        }
        if (Match(TokenType.TEMPLATE))
        {
            RejectUnsupportedCreateIfNotExists("TEMPLATE");
            return _parent.ReportParser.ParseCreateTemplate(startToken, mode);
        }
        if (Match(TokenType.THEME))
        {
            RejectUnsupportedCreateIfNotExists("THEME");
            return _parent.ReportParser.ParseCreateTheme(startToken, mode);
        }

        // Portal admin
        if (Match(TokenType.USER))
        {
            RejectUnsupportedCreateMode(mode, "USER");
            RejectUnsupportedCreateIfNotExists("USER");
            return _parent.PortalParser.ParseCreateUser(startToken);
        }
        if (Match(TokenType.GROUP))
        {
            RejectUnsupportedCreateMode(mode, "GROUP");
            RejectUnsupportedCreateIfNotExists("GROUP");
            return _parent.PortalParser.ParseCreateGroup(startToken);
        }
        if (Match(TokenType.FOLDER))
        {
            RejectUnsupportedCreateMode(mode, "FOLDER");
            RejectUnsupportedCreateIfNotExists("FOLDER");
            return _parent.PortalParser.ParseCreateFolder(startToken);
        }
        if (Match(TokenType.REFRESH))
        {
            RejectUnsupportedCreateIfNotExists("REFRESH");
            return _parent.PortalParser.ParseCreateRefreshJob(startToken);
        }
        if (Match(TokenType.SUBSCRIPTION))
        {
            RejectUnsupportedCreateMode(mode, "SUBSCRIPTION");
            RejectUnsupportedCreateIfNotExists("SUBSCRIPTION");
            return _parent.PortalParser.ParseCreateSubscription(startToken);
        }
        if (Match(TokenType.SHARE))
        {
            RejectUnsupportedCreateMode(mode, "SHARE LINK");
            RejectUnsupportedCreateIfNotExists("SHARE LINK");
            return _parent.PortalParser.ParseCreateShareLink(startToken);
        }
        if (Match(TokenType.EMBED))
        {
            RejectUnsupportedCreateMode(mode, "EMBED TOKEN");
            RejectUnsupportedCreateIfNotExists("EMBED TOKEN");
            return _parent.PortalParser.ParseCreateEmbedToken(startToken);
        }
        if (Match(TokenType.SAVED))
        {
            RejectUnsupportedCreateMode(mode, "SAVED VIEW");
            RejectUnsupportedCreateIfNotExists("SAVED VIEW");
            return _parent.PortalParser.ParseCreateSavedView(startToken);
        }
        if (Match(TokenType.ALERT))
        {
            RejectUnsupportedCreateIfNotExists("ALERT");
            return _parent.PortalParser.ParseCreateAlert(startToken, mode);
        }
        if (Match(TokenType.SMTP))
        {
            RejectUnsupportedCreateIfNotExists("SMTP");
            return _parent.PortalParser.ParseCreateSmtpConnection(startToken);
        }

        throw new SyntaxException("Expected CONNECTION, TABLE, PROCEDURE, FUNCTION, VIEW, INDEX, SETS, SSH_KEY_PAIR, VISUAL, PAGE, DATASET, CONTAINER, NAVIGATION, STYLE, BUTTON, TEMPLATE, or THEME after CREATE", _parser.Current.Line, _parser.Current.Column);
    }

    private void RejectUnsupportedCreateMode(
        ObjectCreationMode mode,
        string objectKind,
        params ObjectCreationMode[] additionallyAllowed)
    {
        if (mode == ObjectCreationMode.Create
            || additionallyAllowed.Contains(mode))
        {
            return;
        }

        var modifier = mode switch
        {
            ObjectCreationMode.CreateOrAlter => "ALTER",
            ObjectCreationMode.CreateOrReplace => "REPLACE",
            _ => mode.ToString().ToUpperInvariant()
        };
        throw new SyntaxException(
            $"CREATE OR {modifier} is not supported for {objectKind}.",
            _parser.Current.Line,
            _parser.Current.Column);
    }

    private void RejectUnsupportedCreateIfNotExists(string objectKind)
    {
        if (_parser.Current.Type != TokenType.IF || _parser.Peek.Type != TokenType.NOT)
            return;

        throw new SyntaxException(
            $"CREATE IF NOT EXISTS is not supported for {objectKind}.",
            _parser.Current.Line,
            _parser.Current.Column);
    }

    public Statement ParseAlter(Token startToken)
    {
        if (Match(TokenType.CONNECTION)) return ParseAlterConnection(startToken);
        if (Match(TokenType.PROCEDURE)) return ParseCreateProcedure(startToken, ObjectCreationMode.Alter);
        if (Match(TokenType.FUNCTION)) return ParseCreateFunction(startToken, ObjectCreationMode.Alter);
        if (Match(TokenType.VIEW)) return ParseCreateView(startToken, ObjectCreationMode.Alter);
        if (Match(TokenType.TABLE)) return ParseAlterTable(startToken);

        // Report-SQL
        if (Match(TokenType.VISUAL)) return _parent.ReportParser.ParseAlterReportObject(ReportObjectType.Visual);
        if (Match(TokenType.PAGE)) return _parent.ReportParser.ParseAlterReportObject(ReportObjectType.Page);
        if (Match(TokenType.CONTAINER)) return _parent.ReportParser.ParseAlterReportObject(ReportObjectType.Container);
        if (Match(TokenType.BUTTON)) return _parent.ReportParser.ParseAlterReportObject(ReportObjectType.Button);
        if (Match(TokenType.STYLE)) return _parent.ReportParser.ParseAlterReportObject(ReportObjectType.Style);
        if (Match(TokenType.NAVIGATION)) return _parent.ReportParser.ParseAlterReportObject(ReportObjectType.Navigation);
        // THEME has no ALTER — it is refused inside ParseAlterReportObject, which names the
        // CREATE OR REPLACE replacement. Routing it here rather than letting it fall through to the
        // generic "expected object kind" error is the difference between being told what to write
        // and being told the parser did not recognise the word.
        if (Match(TokenType.THEME)) return _parent.ReportParser.ParseAlterReportObject(ReportObjectType.Theme);
        if (Match(TokenType.DATASET))
        {
            if (_parser.Current.Type == TokenType.STRING_LITERAL)
                return _parent.PortalParser.ParseAlterDataset(startToken);
            if (_parser.Current.Type == TokenType.IDENTIFIER && !_parser.Current.Value.StartsWith("&", StringComparison.Ordinal))
                throw new SyntaxException("ALTER DATASET names must use the &dataset form for local/report datasets; quoted names are Portal dataset identities.", _parser.Current.Line, _parser.Current.Column);
            return _parent.ReportParser.ParseAlterReportObject(ReportObjectType.Dataset);
        }
        if (Match(TokenType.TEMPLATE)) return _parent.ReportParser.ParseAlterReportObject(ReportObjectType.Template);

        // Orchestrator job management
        if (Match(TokenType.JOB)) return ParseAlterJob(startToken);
        if (Match(TokenType.SCHEDULE)) return _parent.CatalogParser.ParseAlterSchedule(startToken);
        if (_parent.CatalogParser.IsNotificationKeyword())
        {
            Advance();
            return _parent.CatalogParser.ParseAlterNotification(startToken);
        }

        // Portal admin
        if (Match(TokenType.USER)) return _parent.PortalParser.ParseAlterUser(startToken);
        if (Match(TokenType.FOLDER)) return _parent.PortalParser.ParseAlterFolder(startToken);
        if (Match(TokenType.REPORT)) return _parent.PortalParser.ParseAlterReport(startToken);
        if (Match(TokenType.SUBSCRIPTION)) return _parent.PortalParser.ParseAlterSubscription(startToken);
        if (Match(TokenType.ALERT)) return _parent.PortalParser.ParseAlterAlert(startToken);

        throw new SyntaxException("Expected CONNECTION, PROCEDURE, FUNCTION, VIEW, TABLE, JOB, or REPORT object after ALTER", _parser.Current.Line, _parser.Current.Column);
    }

    /// <summary>ALTER JOB &lt;name&gt; SET TARGET = '…' | SET (job options).</summary>
    private Statement ParseAlterJob(Token startToken)
    {
        var jobName = ConsumeIdentifier("Expected job name after ALTER JOB").Value;

        // Attachments come first: ADD/REMOVE link a job to a named SCHEDULE or NOTIFICATION, which
        // is a different operation from editing the job's own definition below.
        if (Match(TokenType.ADD))
            return _parent.CatalogParser.ParseAlterJobAttachment(startToken, jobName, JobAttachmentAction.Add);
        if (MatchIdentifier("REMOVE"))
            return _parent.CatalogParser.ParseAlterJobAttachment(startToken, jobName, JobAttachmentAction.Remove);

        if (_parser.Current.Type is TokenType.ON or TokenType.AS)
            throw new SyntaxException(
                "ALTER JOB ... ON SCHEDULE / AS has been retired. Use ALTER SCHEDULE ... SET CRON " +
                "for cadence changes, ALTER JOB ... SET TARGET for executable changes, and " +
                "ALTER JOB ... ADD|REMOVE SCHEDULE for links.",
                _parser.Current.Line, _parser.Current.Column);

        Consume(TokenType.SET, "Expected ADD, REMOVE, or SET after ALTER JOB name");
        string? targetPath = null, displayName = null, description = null;
        int? maxRetries = null, retryDelay = null;
        Dictionary<string, string>? options = null;

        if (Match(TokenType.TARGET) || MatchIdentifier("TARGET"))
        {
            Match(TokenType.EQUALS);
            targetPath = Consume(TokenType.STRING_LITERAL, "Expected a quoted path after SET TARGET").Value;
        }
        else if (Match(TokenType.LPAREN))
        {
            while (!Match(TokenType.RPAREN) && _parser.Current.Type != TokenType.EOF)
            {
                var keyToken = Advance();
                var key = keyToken.Value.ToUpperInvariant();
                Consume(TokenType.EQUALS, "Expected '=' after ALTER JOB option");
                var expression = ParseExpression();

                if (key is "MAX_RETRIES" or "RETRY_DELAY")
                {
                    if (expression is not LiteralExpression { Type: TokenType.NUMBER } number)
                        throw new SyntaxException($"Expected numeric literal for JOB option {key}", keyToken.Line, keyToken.Column);
                    var value = (int)(Convert.ChangeType(number.Value, typeof(int)) ?? 0);
                    if (value < 0)
                        throw new SyntaxException($"JOB option {key} cannot be negative", keyToken.Line, keyToken.Column);
                    if (key == "MAX_RETRIES") maxRetries = value; else retryDelay = value;
                }
                else
                {
                    var value = expression switch
                    {
                        LiteralExpression { Value: string s } => s,
                        IdentifierExpression identifier => identifier.Name,
                        _ => throw new SyntaxException(
                            $"Expected a string literal or identifier for JOB option {key}",
                            keyToken.Line, keyToken.Column)
                    };
                    if (key == "DISPLAY_NAME") displayName = value;
                    else if (key == "DESCRIPTION") description = value;
                    else
                    {
                        options ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        options[key] = value;
                    }
                }

                if (!Match(TokenType.COMMA))
                {
                    Consume(TokenType.RPAREN, "Expected ')' or ',' after ALTER JOB option");
                    break;
                }
            }
        }
        else
            throw new SyntaxException(
                "Expected TARGET or '(' after ALTER JOB ... SET. Use SET TARGET = '<path>' or SET (MAX_RETRIES = ...).",
                _parser.Current.Line, _parser.Current.Column);

        if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
        return new AlterJobStatement(
            jobName,
            targetPath,
            maxRetries,
            retryDelay,
            new CatalogObjectOptions
            {
                DisplayName = displayName,
                Description = description,
                Options = options
            })
        {
            Line = startToken.Line,
            Column = startToken.Column
        };
    }

    /// <summary>
    /// Rejects the retired post-name existence modifier (<c>DROP VIEW v IF EXISTS</c>) and points at
    /// the canonical spelling. Existence modifiers occupy one position — before the object name — for
    /// every object kind.
    /// </summary>
    /// <remarks>
    /// Reporting here rather than letting the tokens fall through matters: <c>ParseDrop</c> would
    /// return a complete statement and the trailing <c>IF EXISTS</c> would fail as the *next*
    /// statement, pointing at the wrong line with an unrelated message.
    /// <para>
    /// The <c>EXISTS</c> lookahead is required, not defensive. The statement terminator is optional,
    /// so a bare <c>DROP VIEW v</c> may legally be followed by an <c>IF</c> statement; only
    /// <c>IF EXISTS</c> is unambiguously the retired form.
    /// </para>
    /// </remarks>
    private void RejectTrailingIfExists(string objectKind, string name)
    {
        if (_parser.Current.Type != TokenType.IF || _parser.Peek.Type != TokenType.EXISTS) return;

        throw new SyntaxException(
            $"IF EXISTS must come before the object name. Use 'DROP {objectKind} IF EXISTS {name}'.",
            _parser.Current.Line, _parser.Current.Column);
    }

    public Statement ParseDrop(Token startToken)
    {
        bool ifExists = false;
        if (Match(TokenType.TABLE))
        {
            if (Match(TokenType.IF)) { Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
            var target = ParseTableReference(false);
            RejectTrailingIfExists("TABLE", target.TableName);
            if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
            return new DropTableStatement(target, ifExists) { Line = startToken.Line, Column = startToken.Column };
        }
        else if (Match(TokenType.CONNECTION))
        {
            if (Match(TokenType.IF)) { Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
            var name = ConsumeIdentifier("Expected connection name").Value;
            RejectTrailingIfExists("CONNECTION", name);
            if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
            return new DropConnectionStatement(name, ifExists) { Line = startToken.Line, Column = startToken.Column };
        }
        else if (Match(TokenType.PROCEDURE))
        {
            if (Match(TokenType.IF)) { Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
            var name = ConsumeIdentifier("Expected procedure name").Value;
            RejectTrailingIfExists("PROCEDURE", name);
            if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
            return new DropProcedureStatement(name, ifExists) { Line = startToken.Line, Column = startToken.Column };
        }
        else if (Match(TokenType.FUNCTION))
        {
            if (Match(TokenType.IF)) { Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
            var name = ConsumeIdentifier("Expected function name").Value;
            RejectTrailingIfExists("FUNCTION", name);
            if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
            return new DropFunctionStatement(name, ifExists) { Line = startToken.Line, Column = startToken.Column };
        }
        else if (Match(TokenType.VIEW))
        {
            if (Match(TokenType.IF)) { Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
            var name = ConsumeIdentifier("Expected view name").Value;
            RejectTrailingIfExists("VIEW", name);
            if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
            return new DropViewStatement(name, ifExists) { Line = startToken.Line, Column = startToken.Column };
        }
        else if (Match(TokenType.INDEX))
        {
            if (Match(TokenType.IF)) { Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
            var idxName = ConsumeIdentifier("Expected index name").Value;
            TableReference? target = null;
            // Support dotted notation: DROP INDEX TableName.IndexName
            if (Match(TokenType.DOT))
            {
                var indexPart = ConsumeIdentifier("Expected index name after '.'").Value;
                target = new TableReference(idxName);
                idxName = indexPart;
            }
            else if (Match(TokenType.ON)) target = ParseTableReference();
            RejectTrailingIfExists("INDEX", idxName);
            if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
            return new DropIndexStatement(idxName, target, ifExists) { Line = startToken.Line, Column = startToken.Column };
        }
        else if (Match(TokenType.SETS))
        {
            if (Match(TokenType.IF)) { Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
            Consume(TokenType.BANG, "Expected '!' before set name in DROP SETS");
            var name = ConsumeIdentifier("Expected set name after '!'").Value;
            RejectTrailingIfExists("SETS", "!" + name);
            if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
            return new DropSetsStatement(name, ifExists) { Line = startToken.Line, Column = startToken.Column };
        }
        // CHART is a legacy alias for VISUAL. It is tested without consuming: the previous spelling
        // was `Match(TokenType.VISUAL) || (Match(TokenType.IDENTIFIER) && …Equals("CHART"))`, where
        // the second Match consumed *any* identifier before the value check rejected it — leaving the
        // parser a token ahead, so every later identifier-dispatched DROP reported a syntax error
        // pointing past the word it had already eaten.
        else if (Match(TokenType.VISUAL) || MatchIdentifier("CHART"))
        {
            if (Match(TokenType.IF)) { Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
            var name = ConsumeIdentifier("Expected visual name").Value;
            RejectTrailingIfExists("VISUAL", name);
            if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
            return new DropReportObjectStatement { ObjectType = ReportObjectType.Visual, Name = name, IfExists = ifExists, Line = startToken.Line, Column = startToken.Column };
        }
        else if (Match(TokenType.PAGE))
        {
            if (Match(TokenType.IF)) { Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
            var name = ConsumeIdentifier("Expected page name").Value;
            RejectTrailingIfExists("PAGE", name);
            if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
            return new DropReportObjectStatement { ObjectType = ReportObjectType.Page, Name = name, IfExists = ifExists, Line = startToken.Line, Column = startToken.Column };
        }
        else if (Match(TokenType.CONTAINER))
        {
            if (Match(TokenType.IF)) { Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
            var name = ConsumeIdentifier("Expected container name").Value;
            RejectTrailingIfExists("CONTAINER", name);
            if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
            return new DropReportObjectStatement { ObjectType = ReportObjectType.Container, Name = name, IfExists = ifExists, Line = startToken.Line, Column = startToken.Column };
        }
        else if (Match(TokenType.STYLE))
        {
            if (Match(TokenType.IF)) { Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
            var name = ConsumeIdentifier("Expected style name").Value;
            RejectTrailingIfExists("STYLE", name);
            if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
            return new DropReportObjectStatement { ObjectType = ReportObjectType.Style, Name = name, IfExists = ifExists, Line = startToken.Line, Column = startToken.Column };
        }
        else if (Match(TokenType.BUTTON))
        {
            if (Match(TokenType.IF)) { Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
            var name = ConsumeIdentifier("Expected button name").Value;
            RejectTrailingIfExists("BUTTON", name);
            if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
            return new DropReportObjectStatement { ObjectType = ReportObjectType.Button, Name = name, IfExists = ifExists, Line = startToken.Line, Column = startToken.Column };
        }
        else if (Match(TokenType.NAVIGATION))
        {
            if (Match(TokenType.IF)) { Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
            var name = ConsumeIdentifier("Expected navigation name").Value;
            RejectTrailingIfExists("NAVIGATION", name);
            if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
            return new DropReportObjectStatement { ObjectType = ReportObjectType.Navigation, Name = name, IfExists = ifExists, Line = startToken.Line, Column = startToken.Column };
        }
        else if (Match(TokenType.DATASET))
        {
            if (_parser.Current.Type == TokenType.STRING_LITERAL)
                return _parent.PortalParser.ParseDropDataset(startToken);
            if (Match(TokenType.IF)) { Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
            if (_parser.Current.Type == TokenType.STRING_LITERAL)
                throw new SyntaxException(
                    "DROP DATASET IF EXISTS is only supported for local/report dataset names. " +
                    "Portal datasets use DROP DATASET 'name' IN FOLDER '/path'.",
                    _parser.Current.Line,
                    _parser.Current.Column);
            var name = ConsumeIdentifier("Expected dataset name").Value;
            if (!name.StartsWith("&", StringComparison.Ordinal))
                throw new SyntaxException("DROP DATASET names must use the &dataset form", _parser.Previous.Line, _parser.Previous.Column);
            RejectTrailingIfExists("DATASET", name);
            if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
            return new DropReportObjectStatement { ObjectType = ReportObjectType.Dataset, Name = name, IfExists = ifExists, Line = startToken.Line, Column = startToken.Column };
        }
        else if (Match(TokenType.TEMPLATE))
        {
            if (Match(TokenType.IF)) { Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
            var name = ConsumeIdentifier("Expected template name").Value;
            RejectTrailingIfExists("TEMPLATE", name);
            if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
            return new DropReportObjectStatement { ObjectType = ReportObjectType.Template, Name = name, IfExists = ifExists, Line = startToken.Line, Column = startToken.Column };
        }
        else if (Match(TokenType.THEME))
        {
            if (Match(TokenType.IF)) { Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
            var name = ConsumeIdentifier("Expected theme name").Value;
            RejectTrailingIfExists("THEME", name);
            if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
            return new DropReportObjectStatement { ObjectType = ReportObjectType.Theme, Name = name, IfExists = ifExists, Line = startToken.Line, Column = startToken.Column };
        }
        else if (Match(TokenType.JOB))
        {
            if (Match(TokenType.IF)) { Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
            var name = ConsumeIdentifier("Expected job name to drop").Value;
            RejectTrailingIfExists("JOB", name);
            if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
            return new DropJobStatement(name, ifExists) { Line = startToken.Line, Column = startToken.Column };
        }
        else if (Match(TokenType.SCHEDULE))
        {
            return _parent.CatalogParser.ParseDropCatalogObject(startToken, CatalogObjectKind.Schedule);
        }
        else if (_parent.CatalogParser.IsNotificationKeyword())
        {
            Advance();
            return _parent.CatalogParser.ParseDropCatalogObject(startToken, CatalogObjectKind.Notification);
        }

        // Portal admin
        if (Match(TokenType.USER))
            return _parent.PortalParser.ParseDropUser(startToken);
        if (Match(TokenType.GROUP))
            return _parent.PortalParser.ParseDropGroup(startToken);
        if (Match(TokenType.FOLDER))
            return _parent.PortalParser.ParseDropFolder(startToken);
        if (Match(TokenType.REPORT))
            return _parent.PortalParser.ParseDropReport(startToken);
        if (Match(TokenType.REFRESH))
            return _parent.PortalParser.ParseDropRefreshJob(startToken);
        if (Match(TokenType.SUBSCRIPTION))
            return _parent.PortalParser.ParseDropSubscription(startToken);
        if (Match(TokenType.SAVED))
            return _parent.PortalParser.ParseDropSavedView(startToken);
        if (Match(TokenType.ALERT))
            return _parent.PortalParser.ParseDropAlert(startToken);
        if (Match(TokenType.SMTP))
            return _parent.PortalParser.ParseDropSmtpConnection(startToken);
        if (_parser.Current.Type == TokenType.IDENTIFIER &&
            _parser.Current.Value.Equals("SNAPSHOT", StringComparison.OrdinalIgnoreCase))
            return _parent.PortalParser.ParseDropSnapshot(startToken);

        throw new SyntaxException("Expected TABLE, CONNECTION, PROCEDURE, FUNCTION, INDEX, SETS, or REPORT object after DROP", _parser.Current.Line, _parser.Current.Column);
    }

    public Statement ParseKillJob(Token startToken)
    {
        Consume(TokenType.JOB, "Expected 'JOB' after 'KILL'");
        var jobIdExpr = ParseExpression();
        if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
        return new KillJobStatement(jobIdExpr) { Line = startToken.Line, Column = startToken.Column };
    }

    public Statement ParseRefreshDataset(Token startToken)
    {
        Consume(TokenType.DATASET, "Expected DATASET after REFRESH");
        var tok = ConsumeIdentifier("Expected &datasetName after REFRESH DATASET");
        if (!tok.Value.StartsWith("&"))
            throw new SyntaxException("REFRESH DATASET names must use the &dataset form", tok.Line, tok.Column);
        var dsName = tok.Value;
        if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
        return new RefreshDatasetStatement { DatasetName = dsName, Line = startToken.Line, Column = startToken.Column };
    }

    public Statement ParseTruncate(Token startToken)
    {
        Consume(TokenType.TABLE, "Expected 'TABLE' after 'TRUNCATE'");
        var targetTable = ParseTableReference(false);
        if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
        return new TruncateTableStatement(targetTable) { Line = startToken.Line, Column = startToken.Column };
    }

    public Statement ParseDelete(Token startToken)
    {
        if (Match(TokenType.TAG))
            return ParseDeleteTag(startToken);
        if (Match(TokenType.LINEAGE))
            return ParseDeleteLineage(startToken);

        if (Match(TokenType.FILE))
        {
            bool ifExists = false;
            if (Match(TokenType.IF)) { Consume(TokenType.EXISTS, "Expected EXISTS after IF"); ifExists = true; }
            var source = ParseExpression();
            string? connectionName = null;
            if (Match(TokenType.AT)) { connectionName = ConsumeIdentifier("Expected connection name after AT").Value; }
            Match(TokenType.SEMICOLON);
            return new FileOperationStatement(FileOpType.Delete, source, ifExists: ifExists, connectionName: connectionName) { Line = startToken.Line, Column = startToken.Column };
        }
        if (Match(TokenType.DIRECTORY))
        {
            bool ifExists = false;
            if (Match(TokenType.IF)) { Consume(TokenType.EXISTS, "Expected EXISTS after IF"); ifExists = true; }
            var path = ParseExpression();
            string? connectionName = null;
            if (Match(TokenType.AT)) { connectionName = ConsumeIdentifier("Expected connection name after AT").Value; }
            Match(TokenType.SEMICOLON);
            return new DirectoryOperationStatement(DirectoryOpType.Delete, path, ifExists: ifExists, connectionName: connectionName) { Line = startToken.Line, Column = startToken.Column };
        }
        if (Match(TokenType.DIRECTORY_CONTENTS))
        {
            var path = ParseExpression();
            Expression? recursive = null;
            if (Match(TokenType.WITH)) recursive = ParseWithRecursive();
            Match(TokenType.SEMICOLON);
            return new DirectoryOperationStatement(DirectoryOpType.DeleteContents, path, null, null, recursive) { Line = startToken.Line, Column = startToken.Column };
        }


        Match(TokenType.FROM);
        var targetTable = ParseTableReference(false);

        OutputClause? output = null;
        if (Match(TokenType.OUTPUT)) output = _parser.ParseOutputClause();

        Expression? whereClause = null;
        if (Match(TokenType.WHERE)) whereClause = ParseExpression();

        if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
        return new DeleteStatement(targetTable, whereClause) { Line = startToken.Line, Column = startToken.Column, Output = output };
    }

    public Statement ParseInsert(Token startToken)
    {
        if (_parser.Current.Type == TokenType.TAG)
        {
            Advance();
            return ParseInsertTag(startToken);
        }
        if (_parser.Current.Type == TokenType.LINEAGE)
        {
            Advance();
            return ParseInsertLineage(startToken);
        }

        bool isReplace = startToken.Type == TokenType.REPLACE;
        if (startToken.Type == TokenType.INSERT)
        {
            if (_parser.Current.Type == TokenType.OR && _parser.Peek.Type == TokenType.REPLACE)
            {
                Match(TokenType.OR);
                Match(TokenType.REPLACE);
                isReplace = true;
            }
        }

        Match(TokenType.INTO);
        var targetTable = ParseTableReference(false);

        List<string>? columns = null;
        if (Match(TokenType.LPAREN))
        {
            columns = ParseIdentifierList();
            Consume(TokenType.RPAREN, "Expected ')' after column list");
        }

        OutputClause? output = null;
        if (Match(TokenType.OUTPUT)) output = _parser.ParseOutputClause();

        if (Match(TokenType.VALUES))
        {
            var rows = new List<List<Expression>>();
            do
            {
                do
                {
                    Consume(TokenType.LPAREN, "Expected '(' before values list");
                    var values = new List<Expression>();
                    do { values.Add(ParseExpression()); } while (Match(TokenType.COMMA));
                    Consume(TokenType.RPAREN, "Expected ')' after values list");
                    while (Match(TokenType.COLUMN_TAG)) { }
                    rows.Add(values);
                } while (Match(TokenType.COMMA));
            } while (Match(TokenType.VALUES));

            if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
            return new InsertStatement(targetTable, columns, rows) { Line = startToken.Line, Column = startToken.Column, Output = output, IsReplace = isReplace };
        }
        else
        {
            if (_parser.Current.Type == TokenType.EXEC || _parser.Current.Type == TokenType.EXECUTE)
            {
                Advance();
                var exec = _parent.ExtensionParser.ParseExecute();
                if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
                return new InsertStatement(targetTable, columns, exec) { Line = startToken.Line, Column = startToken.Column, Output = output, IsReplace = isReplace };
            }

            var query = _parser.ParseQuery();
            if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
            return new InsertStatement(targetTable, columns, query) { Line = startToken.Line, Column = startToken.Column, Output = output, IsReplace = isReplace };
        }
    }

    private string ParseAssignmentTarget()
    {
        var first = ConsumeIdentifier("Expected column name").Value;
        if (Match(TokenType.DOT)) return ConsumeIdentifier("Expected column name").Value;
        return first;
    }

    public Statement ParseUpdate(Token startToken)
    {
        if (_parser.Current.Type == TokenType.TAG)
        {
            Advance();
            return ParseUpdateTag(startToken);
        }

        var targetTable = ParseTableReference(false);
        Consume(TokenType.SET, "Expected 'SET' in UPDATE statement");

        var assignments = new List<Assignment>();
        do
        {
            var col = ParseAssignmentTarget();
            Consume(TokenType.EQUALS, "Expected '=' in assignment");
            var expr = ParseExpression();
            assignments.Add(new Assignment(col, expr));
        } while (Match(TokenType.COMMA));

        OutputClause? output = null;
        if (Match(TokenType.OUTPUT)) output = _parser.ParseOutputClause();

        TableReference? fromTable = null;
        if (Match(TokenType.FROM)) fromTable = ParseTableReference(false);

        var joins = _parser.ParseJoins();

        Expression? whereClause = null;
        if (Match(TokenType.WHERE)) whereClause = ParseExpression();

        if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
        return new UpdateStatement(targetTable, assignments, whereClause)
        {
            Line = startToken.Line,
            Column = startToken.Column,
            Output = output,
            FromTable = fromTable,
            Joins = joins
        };
    }

    public Statement ParseMerge(Token startToken)
    {
        Match(TokenType.INTO);
        var targetTable = ParseTableReference(false);
        if (Match(TokenType.AS)) Advance();
        string? targetAlias = null;
        if (_parser.Current.Type == TokenType.IDENTIFIER) targetAlias = Advance().Value;

        Consume(TokenType.USING, "Expected USING in MERGE");
        var sourceTable = ParseTableReference(false);
        if (Match(TokenType.AS)) Advance();
        string? sourceAlias = null;
        if (_parser.Current.Type == TokenType.IDENTIFIER) sourceAlias = Advance().Value;

        Consume(TokenType.ON, "Expected ON in MERGE");
        var onClause = ParseExpression();

        var whenMatched = new List<MergeMatchedClause>();
        var whenNotMatched = new List<MergeNotMatchedClause>();

        while (Match(TokenType.WHEN))
        {
            if (Match(TokenType.MATCHED))
            {
                Expression? andExpr = null;
                if (Match(TokenType.AND)) andExpr = ParseExpression();
                Consume(TokenType.THEN, "Expected THEN");
                if (Match(TokenType.UPDATE))
                {
                    Consume(TokenType.SET, "Expected SET after UPDATE");
                    var assignments = new List<Assignment>();
                    do
                    {
                        var col = ParseAssignmentTarget();
                        Consume(TokenType.EQUALS, "Expected '='");
                        var expr = ParseExpression();
                        assignments.Add(new Assignment(col, expr));
                    } while (Match(TokenType.COMMA));
                    whenMatched.Add(new MergeUpdateClause(andExpr, assignments));
                }
                else if (Match(TokenType.DELETE))
                {
                    whenMatched.Add(new MergeDeleteClause(andExpr));
                }
            }
            else
            {
                Consume(TokenType.NOT, "Expected NOT MATCHED");
                Consume(TokenType.MATCHED, "Expected MATCHED");

                var option = MergeSourceOrTarget.Target;
                if (Match(TokenType.BY))
                {
                    if (Match(TokenType.SOURCE)) option = MergeSourceOrTarget.Source;
                    else if (Match(TokenType.TARGET)) option = MergeSourceOrTarget.Target;
                }

                Expression? andExpr = null;
                if (Match(TokenType.AND)) andExpr = ParseExpression();

                Consume(TokenType.THEN, "Expected THEN in MERGE clause");

                if (Match(TokenType.INSERT))
                {
                    List<string>? cols = null;
                    if (Match(TokenType.LPAREN))
                    {
                        cols = ParseIdentifierList();
                        Consume(TokenType.RPAREN, "Expected ')'");
                    }
                    Consume(TokenType.VALUES, "Expected VALUES");
                    Consume(TokenType.LPAREN, "Expected '('");
                    var vals = new List<Expression>();
                    do { vals.Add(ParseExpression()); } while (Match(TokenType.COMMA));
                    Consume(TokenType.RPAREN, "Expected ')'");
                    whenNotMatched.Add(new MergeInsertClause(andExpr, cols, vals, option));
                }
                else if (Match(TokenType.UPDATE))
                {
                    Consume(TokenType.SET, "Expected SET");
                    var assignments = new List<Assignment>();
                    do
                    {
                        var col = ParseAssignmentTarget();
                        Consume(TokenType.EQUALS, "Expected '='");
                        var expr = ParseExpression();
                        assignments.Add(new Assignment(col, expr));
                    } while (Match(TokenType.COMMA));
                    whenNotMatched.Add(new MergeNotMatchedClause(andExpr, option) { ActionType = MergeActionType.UPDATE, UpdateAssignments = assignments });
                }
                else if (Match(TokenType.DELETE))
                {
                    whenNotMatched.Add(new MergeNotMatchedClause(andExpr, option) { ActionType = MergeActionType.DELETE });
                }
            }
        }

        OutputClause? output = null;
        if (Match(TokenType.OUTPUT)) output = _parser.ParseOutputClause();

        if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
        return new MergeStatement(targetTable, targetAlias, sourceTable, sourceAlias, onClause, whenMatched, whenNotMatched, output)
        {
            Line = startToken.Line,
            Column = startToken.Column
        };
    }

    public Statement ParseBulkInsert(Token startToken)
    {
        if (!Match(TokenType.INSERT) && !Match(TokenType.LOAD))
            throw new SyntaxException("Expected INSERT or LOAD after BULK", _parser.Current.Line, _parser.Current.Column);

        Match(TokenType.INTO);
        var targetTable = ParseTableReference(false);
        List<string>? columns = null;
        if (Match(TokenType.LPAREN))
        {
            columns = ParseIdentifierList();
            Consume(TokenType.RPAREN, "Expected ')' after column list");
        }
        Consume(TokenType.FROM, "Expected FROM in BULK INSERT");
        var sourceFile = ParseExpression();

        Dictionary<string, Expression>? options = null;
        if (Match(TokenType.WITH))
        {
            options = new Dictionary<string, Expression>(StringComparer.OrdinalIgnoreCase);
            Consume(TokenType.LPAREN, "Expected '(' after WITH");
            while (!Match(TokenType.RPAREN))
            {
                var keyTok = Advance();
                Consume(TokenType.EQUALS, "Expected '='");
                var val = ParseExpression();
                options[keyTok.Value] = val;
                if (!Match(TokenType.COMMA))
                {
                    Consume(TokenType.RPAREN, "Expected ')' or ','");
                    break;
                }
            }
        }

        if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
        return new BulkInsertStatement(targetTable, sourceFile is LiteralExpression lit ? lit.Value?.ToString() ?? "" : sourceFile.ToSql().Trim('\''), options ?? new(), columns)
        {
            Line = startToken.Line,
            Column = startToken.Column
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    public Statement ParseAlterConnection(Token startToken)
    {
        var name = ConsumeIdentifier("Expected connection name after ALTER CONNECTION").Value;
        string? connectionType = null;
        Expression? target = null;
        Dictionary<string, Expression>? options = null;

        if (Match(TokenType.AS))
        {
            // Full replacement: ALTER CONNECTION name AS TYPE(options_or_string)
            connectionType = Advance().Value;
            Consume(TokenType.LPAREN, "Expected '(' after connection type");

            if (_parser.Current.Type != TokenType.RPAREN)
            {
                if (_parser.Peek.Type == TokenType.EQUALS)
                {
                    options = new Dictionary<string, Expression>(StringComparer.OrdinalIgnoreCase);
                    while (_parser.Current.Type != TokenType.RPAREN && _parser.Current.Type != TokenType.EOF)
                    {
                        string key = Advance().Value;
                        Consume(TokenType.EQUALS, "Expected '=' after option name in ALTER CONNECTION");
                        options[key] = ParseConnectionOptionValue();
                        if (!Match(TokenType.COMMA)) break;
                    }
                }
                else
                {
                    target = ParseExpression();
                    if (Match(TokenType.COMMA))
                    {
                        options = new Dictionary<string, Expression>(StringComparer.OrdinalIgnoreCase);
                        while (_parser.Current.Type != TokenType.RPAREN && _parser.Current.Type != TokenType.EOF)
                        {
                            string key = Advance().Value;
                            Consume(TokenType.EQUALS, "Expected '=' after option name in ALTER CONNECTION");
                            options[key] = ParseConnectionOptionValue();
                            if (!Match(TokenType.COMMA)) break;
                        }
                    }
                }
            }

            Consume(TokenType.RPAREN, "Expected ')' to close ALTER CONNECTION");
        }
        else if (Match(TokenType.WITH))
        {
            // Partial update: ALTER CONNECTION name WITH(opts) — merges into existing, no type change
            options = new Dictionary<string, Expression>(StringComparer.OrdinalIgnoreCase);
            Consume(TokenType.LPAREN, "Expected '(' after WITH");
            while (_parser.Current.Type != TokenType.RPAREN && _parser.Current.Type != TokenType.EOF)
            {
                string key = Advance().Value;
                Consume(TokenType.EQUALS, "Expected '=' after option key");
                options[key] = ParseConnectionOptionValue();
                if (!Match(TokenType.COMMA)) break;
            }
            Consume(TokenType.RPAREN, "Expected ')' at end of WITH options");
        }

        Consume(TokenType.SEMICOLON, "Expected ';' at end of ALTER CONNECTION");
        return new AlterConnectionStatement(name, connectionType, target, options) { Line = startToken.Line, Column = startToken.Column };
    }

    public Statement ParseCreateProcedure(Token startToken, ObjectCreationMode mode = ObjectCreationMode.Create)
    {
        var name = ConsumeIdentifier("Expected procedure name").Value;
        var parameters = ParseParameterDefinitions();
        Consume(TokenType.AS, "Expected 'AS' after procedure definition");
        var body = _parser.ParseStatement();
        Match(TokenType.SEMICOLON);
        return new CreateProcedureStatement(name, parameters, body, mode) { Line = startToken.Line, Column = startToken.Column };
    }

    public Statement ParseCreateFunction(Token startToken, ObjectCreationMode mode = ObjectCreationMode.Create)
    {
        var name = ConsumeIdentifier("Expected function name").Value;
        var parameters = ParseParameterDefinitions();
        Consume(TokenType.RETURNS, "Expected 'RETURNS' after function definition");
        var returnType = _parser.ParseType();
        Consume(TokenType.AS, "Expected 'AS' after function return type");
        var body = _parser.ParseStatement();
        Match(TokenType.SEMICOLON);
        return new CreateFunctionStatement(name, parameters, returnType, body, mode) { Line = startToken.Line, Column = startToken.Column };
    }

    public Statement ParseCreateView(Token startToken, ObjectCreationMode mode = ObjectCreationMode.Create)
    {
        var name = ConsumeIdentifier("Expected view name").Value;
        Consume(TokenType.AS, "Expected 'AS' after view name");
        var query = _parser.ParseStatement();
        if (query is not SelectStatement && query is not SetOperationStatement)
            throw new SyntaxException("CREATE VIEW requires a SELECT query after AS", query.Line, query.Column);
        Match(TokenType.SEMICOLON);
        return new CreateViewStatement(name, query, mode) { Line = startToken.Line, Column = startToken.Column };
    }

    private Statement ParseAlterTable(Token startToken)
    {
        var targetTable = ParseTableReference();
        AlterTableActionType action;
        ColumnDefinition? newColumn = null;
        string? columnToDelete = null;
        string? oldColumnName = null;
        string? newColumnName = null;

        if (Match(TokenType.ADD))
        {
            action = AlterTableActionType.ADD;
            var colName = ConsumeIdentifier("Expected column name").Value;
            string dataType = "NVARCHAR(MAX)";
            if (_parser.IsIdentifier(_parser.Current))
            {
                dataType = Advance().Value;
                if (Match(TokenType.LPAREN))
                {
                    dataType += "(";
                    if (Match(TokenType.MAX)) dataType += "MAX";
                    else dataType += Consume(TokenType.NUMBER, "Expected length").Value;
                    dataType += ")";
                    Consume(TokenType.RPAREN, "Expected ')'");
                }
            }
            Expression? defaultExpr = null;
            if (Match(TokenType.DEFAULT)) defaultExpr = ParseExpression();
            newColumn = new ColumnDefinition(colName, dataType, false, defaultExpr);
        }
        else if (Match(TokenType.DROP))
        {
            Match(TokenType.COLUMN);
            action = AlterTableActionType.DROP_COLUMN;
            columnToDelete = ConsumeIdentifier("Expected column name to drop").Value;
        }
        else if (Match(TokenType.RENAME))
        {
            Match(TokenType.COLUMN);
            action = AlterTableActionType.RENAME_COLUMN;
            oldColumnName = ConsumeIdentifier("Expected column name to rename").Value;
            Consume(TokenType.TO, "Expected 'TO' after old column name");
            newColumnName = ConsumeIdentifier("Expected new column name").Value;
        }
        else
        {
            throw new SyntaxException("Expected ADD, DROP, or RENAME after ALTER TABLE", _parser.Current.Line, _parser.Current.Column);
        }

        if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
        return new AlterTableStatement(targetTable, action, newColumn, columnToDelete, oldColumnName, newColumnName)
        {
            Line = startToken.Line,
            Column = startToken.Column
        };
    }

    private List<ParameterDefinition> ParseParameterDefinitions()
    {
        var parameters = new List<ParameterDefinition>();
        if (Match(TokenType.LPAREN))
        {
            if (_parser.Current.Type != TokenType.RPAREN)
            {
                do
                {
                    var pName = Consume(TokenType.VARIABLE, "Expected parameter name starting with '@'").Value;
                    var pType = _parser.ParseType();
                    parameters.Add(new ParameterDefinition(pName, pType));
                } while (Match(TokenType.COMMA));
            }
            Consume(TokenType.RPAREN, "Expected ')' after parameter list");
        }
        else
        {
            // Support SQL Server bare-style: CREATE PROCEDURE Name @param1 INT, @param2 INT AS ...
            while (_parser.Current.Type == TokenType.VARIABLE)
            {
                var pName = Consume(TokenType.VARIABLE, "Expected parameter name starting with '@'").Value;
                var pType = _parser.ParseType();
                parameters.Add(new ParameterDefinition(pName, pType));
                if (!Match(TokenType.COMMA)) break;
            }
        }
        return parameters;
    }

    private Statement ParseCreateConnection(Token startToken, ObjectCreationMode mode)
    {
        var name = ConsumeIdentifier("Expected connection name after CREATE CONNECTION").Value;
        if (name.Equals("eng", StringComparison.OrdinalIgnoreCase))
            throw new SyntaxException("eng is reserved for the engine catalog schema and cannot be used as a connection name.", startToken.Line, startToken.Column);

        Consume(TokenType.AS, "Expected AS after connection name in CREATE CONNECTION");
        var connectionType = Advance().Value;
        Consume(TokenType.LPAREN, "Expected '(' after connection type in CREATE CONNECTION");

        Expression? target = null;
        Dictionary<string, Expression>? options = null;

        if (_parser.Current.Type != TokenType.RPAREN)
        {
            // A positional target starts with: a string literal, a variable (@var), a function call
            // (peek is '('), or a member access (peek is '.'). Everything else is a named option.
            // If it's a bare identifier NOT followed by '=', '(' or '.', it's a malformed named
            // option ("key 'val'" instead of "key = 'val'") — we let Consume(EQUALS) report it.
            bool isPositionalStart = _parser.Current.Type == TokenType.STRING_LITERAL
                || _parser.Current.Type == TokenType.VARIABLE
                || _parser.Peek.Type == TokenType.LPAREN
                || _parser.Peek.Type == TokenType.DOT;

            if (isPositionalStart)
            {
                // Positional target first: AS TYPE('str') or AS TYPE(@var) or AS TYPE(func())
                target = ParseExpression();
                if (Match(TokenType.COMMA))
                {
                    options = new Dictionary<string, Expression>(StringComparer.OrdinalIgnoreCase);
                    while (_parser.Current.Type != TokenType.RPAREN && _parser.Current.Type != TokenType.EOF)
                    {
                        string key = Advance().Value;
                        Consume(TokenType.EQUALS, "Expected '=' after option name in CREATE CONNECTION");
                        options[key] = ParseConnectionOptionValue();
                        if (!Match(TokenType.COMMA)) break;
                    }
                }
            }
            else
            {
                // All named options: AS TYPE(KEY=val, ...) — or malformed "key 'val'" which
                // falls here so Consume(EQUALS) produces the correct error message.
                options = new Dictionary<string, Expression>(StringComparer.OrdinalIgnoreCase);
                while (_parser.Current.Type != TokenType.RPAREN && _parser.Current.Type != TokenType.EOF)
                {
                    string key = Advance().Value;
                    Consume(TokenType.EQUALS, "Expected '=' after option name in CREATE CONNECTION");
                    options[key] = ParseConnectionOptionValue();
                    if (!Match(TokenType.COMMA)) break;
                }
            }
        }

        Consume(TokenType.RPAREN, "Expected ')' to close CREATE CONNECTION");
        Consume(TokenType.SEMICOLON, "Expected ';' at the end of CREATE CONNECTION");
        return new CreateConnectionStatement(name, connectionType, target, options, mode) { Line = startToken.Line, Column = startToken.Column };
    }

    // Unquoted SECRET:name / ENC:... would otherwise surface as an unrelated syntax error;
    // quoted 'SECRET:name' is the canonical form (see SMESecretManagementAdministrationHardening.md §5).
    private Expression ParseConnectionOptionValue()
    {
        if (_parser.Peek.Type == TokenType.COLON
            && (_parser.Current.Type == TokenType.SECRET
                || _parser.Current.Value.Equals("ENC", StringComparison.OrdinalIgnoreCase)
                || _parser.Current.Value.Equals("DPAPI", StringComparison.OrdinalIgnoreCase)))
        {
            var prefix = _parser.Current.Value.ToUpperInvariant();
            throw new SyntaxException(
                $"{prefix}: references must be quoted strings — write '{prefix}:...' (single quotes) as the option value.",
                _parser.Current.Line, _parser.Current.Column);
        }

        return ParseExpression();
    }

    /// <summary>
    /// Parses a table/column name that may be a variable (e.g. @r.tbl) or a static reference.
    /// Variables (and their member-access chains) are returned as expressions to be evaluated at
    /// runtime; static names are canonicalized to the same full-path string SELECT ... INTO uses
    /// to key lineage, so explicitly-created tags inherit onto derived columns downstream.
    /// </summary>
    private Expression ParseLineageNameExpression(bool tableLevel)
    {
        if (_parser.Current.Type == TokenType.VARIABLE)
        {
            var varToken = Advance();
            Expression target = new VariableExpression(varToken.Value) { Line = varToken.Line, Column = varToken.Column };
            while (Match(TokenType.DOT))
            {
                if (!_parser.IsIdentifier(_parser.Current) && !ETL_SQL.Common.LanguageMetadata.IsKeyword(_parser.Current.Value))
                    throw new SyntaxException("Expected member name after '.'", _parser.Current.Line, _parser.Current.Column);
                var member = Advance();
                target = new MemberAccessExpression(target, member.Value) { Line = varToken.Line, Column = varToken.Column, EndLine = _parser.LastTokenEndLine, EndColumn = _parser.LastTokenEndColumn };
            }
            return target;
        }

        if (tableLevel)
        {
            var tableRef = ParseTableReference(allowFunction: false, allowWithClause: false, allowAlias: false);
            var canonical = tableRef.GetSourceTables().FirstOrDefault() ?? tableRef.TableName;
            return new LiteralExpression(canonical, TokenType.STRING_LITERAL) { Line = tableRef.Line, Column = tableRef.Column };
        }

        var idTok = ConsumeIdentifier("Expected column name");
        return new LiteralExpression(idTok.Value, TokenType.STRING_LITERAL) { Line = idTok.Line, Column = idTok.Column };
    }

    /// <summary>INSERT TAG FOR TABLE &lt;table&gt; [COLUMN &lt;col&gt;] (key = expr, ...)</summary>
    private Statement ParseInsertTag(Token startToken)
    {
        Consume(TokenType.FOR, "Expected FOR after INSERT TAG");
        Consume(TokenType.TABLE, "Expected TABLE after INSERT TAG FOR");
        var tableExpr = ParseLineageNameExpression(tableLevel: true);

        Expression? columnExpr = null;
        if (Match(TokenType.COLUMN)) columnExpr = ParseLineageNameExpression(tableLevel: false);

        var tags = ParseTagAssignments(startToken, "INSERT TAG");
        return new CreateTagStatement(tableExpr, columnExpr, tags) { Line = startToken.Line, Column = startToken.Column };
    }

    /// <summary>UPDATE TAG FOR TABLE &lt;table&gt; [COLUMN &lt;col&gt;] (key = expr, ...)</summary>
    private Statement ParseUpdateTag(Token startToken)
    {
        Consume(TokenType.FOR, "Expected FOR after UPDATE TAG");
        Consume(TokenType.TABLE, "Expected TABLE after UPDATE TAG FOR");
        var tableExpr = ParseLineageNameExpression(tableLevel: true);

        Expression? columnExpr = null;
        if (Match(TokenType.COLUMN)) columnExpr = ParseLineageNameExpression(tableLevel: false);

        var tags = ParseTagAssignments(startToken, "UPDATE TAG");
        return new CreateTagStatement(tableExpr, columnExpr, tags) { Line = startToken.Line, Column = startToken.Column };
    }

    /// <summary>DELETE TAG FOR TABLE &lt;table&gt; [COLUMN &lt;col&gt;] (key, ...)</summary>
    private Statement ParseDeleteTag(Token startToken)
    {
        Consume(TokenType.FOR, "Expected FOR after DELETE TAG");
        Consume(TokenType.TABLE, "Expected TABLE after DELETE TAG FOR");
        var tableExpr = ParseLineageNameExpression(tableLevel: true);

        Expression? columnExpr = null;
        if (Match(TokenType.COLUMN)) columnExpr = ParseLineageNameExpression(tableLevel: false);

        Consume(TokenType.LPAREN, "Expected '(' to begin the tag list in DELETE TAG");
        var tagNames = new List<string>();
        while (_parser.Current.Type != TokenType.RPAREN && _parser.Current.Type != TokenType.EOF)
        {
            tagNames.Add(Advance().Value);
            if (!Match(TokenType.COMMA)) break;
        }
        Consume(TokenType.RPAREN, "Expected ')' to close DELETE TAG");
        Consume(TokenType.SEMICOLON, "Expected ';' at the end of DELETE TAG");

        if (tagNames.Count == 0)
            throw new SyntaxException("DELETE TAG requires at least one tag name.", startToken.Line, startToken.Column);

        return new DeleteTagStatement(tableExpr, columnExpr, tagNames) { Line = startToken.Line, Column = startToken.Column };
    }

    /// <summary>DELETE LINEAGE FOR TABLE &lt;table&gt;</summary>
    private Statement ParseDeleteLineage(Token startToken)
    {
        Consume(TokenType.FOR, "Expected FOR after DELETE LINEAGE");
        Consume(TokenType.TABLE, "Expected TABLE after DELETE LINEAGE FOR");
        var tableExpr = ParseLineageNameExpression(tableLevel: true);
        Consume(TokenType.SEMICOLON, "Expected ';' at the end of DELETE LINEAGE");
        return new DeleteLineageStatement(tableExpr) { Line = startToken.Line, Column = startToken.Column };
    }

    private Dictionary<string, Expression> ParseTagAssignments(Token startToken, string statementName)
    {
        Consume(TokenType.LPAREN, $"Expected '(' to begin the tag list in {statementName}");
        var tags = new Dictionary<string, Expression>(StringComparer.OrdinalIgnoreCase);
        while (_parser.Current.Type != TokenType.RPAREN && _parser.Current.Type != TokenType.EOF)
        {
            string key = Advance().Value;
            Consume(TokenType.EQUALS, $"Expected '=' after tag name in {statementName}");
            tags[key] = ParseExpression();
            if (!Match(TokenType.COMMA)) break;
        }
        Consume(TokenType.RPAREN, $"Expected ')' to close {statementName}");
        Consume(TokenType.SEMICOLON, $"Expected ';' at the end of {statementName}");

        if (tags.Count == 0)
            throw new SyntaxException($"{statementName} requires at least one 'key = value' assignment.", startToken.Line, startToken.Column);

        return tags;
    }

    /// <summary>INSERT LINEAGE FOR TABLE &lt;table&gt; FROM &lt;openlineage-json file or string&gt;</summary>
    private Statement ParseInsertLineage(Token startToken)
    {
        Consume(TokenType.FOR, "Expected FOR after INSERT LINEAGE");
        Consume(TokenType.TABLE, "Expected TABLE after INSERT LINEAGE FOR");
        var tableExpr = ParseLineageNameExpression(tableLevel: true);
        Consume(TokenType.FROM, "Expected FROM after the table name in INSERT LINEAGE");
        var source = ParseExpression();
        Consume(TokenType.SEMICOLON, "Expected ';' at the end of INSERT LINEAGE");
        return new CreateLineageStatement(tableExpr, source) { Line = startToken.Line, Column = startToken.Column };
    }

    private Statement ParseCreateTable(Token startToken)
    {
        bool ifNotExists = false;
        if (Match(TokenType.IF))
        {
            Consume(TokenType.NOT, "Expected 'NOT' after 'IF'");
            Consume(TokenType.EXISTS, "Expected 'EXISTS' after 'NOT'");
            ifNotExists = true;
        }

        var targetTable = ParseTableReference(false);
        Consume(TokenType.LPAREN, "Expected '(' after table name");

        var columns = new List<ColumnDefinition>();
        var tableConstraints = new List<TableConstraint>();

        while (_parser.Current.Type != TokenType.RPAREN && _parser.Current.Type != TokenType.EOF)
        {
            string? constraintName = null;
            if (Match(TokenType.CONSTRAINT))
                constraintName = ConsumeIdentifier("Expected constraint name").Value;

            if (_parser.Current.Type == TokenType.PRIMARY ||
                _parser.Current.Type == TokenType.UNIQUE ||
                _parser.Current.Type == TokenType.CHECK ||
                _parser.Current.Type == TokenType.FOREIGN)
            {
                tableConstraints.Add(ParseTableConstraint(constraintName));
            }
            else
            {
                var colName = ConsumeIdentifier("Expected column name or constraint").Value;
                string dataType = "NVARCHAR(MAX)";

                if (_parser.IsIdentifier(_parser.Current))
                {
                    dataType = Advance().Value;
                    if (Match(TokenType.LPAREN))
                    {
                        dataType += "(";
                        if (Match(TokenType.MAX)) dataType += "MAX";
                        else dataType += Consume(TokenType.NUMBER, "Expected length").Value;

                        if (Match(TokenType.COMMA))
                        {
                            dataType += ",";
                            // A sign constraint stands where a scale would go: INT(5,+) is five
                            // positive-only digits, INT(5,-) five negative-only digits. Scale and
                            // sign are mutually exclusive — a scale implies a signed decimal.
                            if (Match(TokenType.PLUS)) dataType += "+";
                            else if (Match(TokenType.MINUS)) dataType += "-";
                            else dataType += Consume(TokenType.NUMBER, "Expected scale, '+', or '-'").Value;
                        }
                        dataType += ")";
                        Consume(TokenType.RPAREN, "Expected ')'");
                    }
                }

                Dictionary<string, string>? metadata = null;
                while (Match(TokenType.COLUMN_TAG))
                {
                    if (metadata == null) metadata = new(StringComparer.OrdinalIgnoreCase);
                    _parser.ParseMetadataTags(_parser.Previous.Value, metadata);
                }
                var colDef = new ColumnDefinition(colName, dataType, false, null, metadata);

                while (true)
                {
                    if (Match(TokenType.IDENTITY)) { }
                    else if (Match(TokenType.PRIMARY)) { Consume(TokenType.KEY, "Expected KEY after PRIMARY"); colDef.IsPrimaryKey = true; }
                    else if (Match(TokenType.UNIQUE)) { colDef.IsUnique = true; }
                    else if (Match(TokenType.NOT)) { Consume(TokenType.NULL, "Expected NULL after NOT"); colDef.IsNullable = false; }
                    else if (Match(TokenType.NULL)) { colDef.IsNullable = true; }
                    else if (Match(TokenType.CHECK))
                    {
                        Consume(TokenType.LPAREN, "Expected '(' after CHECK");
                        colDef.CheckConstraint = ParseExpression();
                        Consume(TokenType.RPAREN, "Expected ')' after check expression");
                    }
                    else if (Match(TokenType.DEFAULT)) { colDef.DefaultExpression = ParseExpression(); }
                    else if (Match(TokenType.REFERENCES)) { colDef.ForeignKey = ParseForeignKeyReference(); }
                    else break;
                }

                while (Match(TokenType.COLUMN_TAG))
                    _parser.ParseMetadataTags(_parser.Previous.Value, colDef.Metadata);

                columns.Add(colDef);
            }

            if (!Match(TokenType.COMMA)) break;
        }

        Consume(TokenType.RPAREN, "Expected ')' at end of CREATE TABLE");
        if (_parser.Current.Type == TokenType.SEMICOLON) Advance();

        var stmt = new CreateTableStatement(targetTable, ifNotExists, columns) { Line = startToken.Line, Column = startToken.Column };
        stmt.TableConstraints.AddRange(tableConstraints);
        return stmt;
    }

    private TableConstraint ParseTableConstraint(string? constraintName)
    {
        if (Match(TokenType.PRIMARY))
        {
            Consume(TokenType.KEY, "Expected KEY after PRIMARY");
            Consume(TokenType.LPAREN, "Expected '(' after PRIMARY KEY");
            var cols = ParseIdentifierList();
            Consume(TokenType.RPAREN, "Expected ')' after column list");
            return new TablePrimaryKeyConstraint(cols) { ConstraintName = constraintName };
        }
        if (Match(TokenType.UNIQUE))
        {
            Consume(TokenType.LPAREN, "Expected '(' after UNIQUE");
            var cols = ParseIdentifierList();
            Consume(TokenType.RPAREN, "Expected ')' after column list");
            return new TableUniqueConstraint(cols) { ConstraintName = constraintName };
        }
        if (Match(TokenType.CHECK))
        {
            Consume(TokenType.LPAREN, "Expected '(' after CHECK");
            var expr = ParseExpression();
            Consume(TokenType.RPAREN, "Expected ')' after check expression");
            return new TableCheckConstraint(expr) { ConstraintName = constraintName };
        }
        if (Match(TokenType.FOREIGN))
        {
            Consume(TokenType.KEY, "Expected KEY after FOREIGN");
            Consume(TokenType.LPAREN, "Expected '(' after FOREIGN KEY");
            var cols = ParseIdentifierList();
            Consume(TokenType.RPAREN, "Expected ')' after column list");
            Consume(TokenType.REFERENCES, "Expected REFERENCES keyword");
            var destTable = ParseTableReference(false);
            Consume(TokenType.LPAREN, "Expected '(' after reference table name");
            var refCols = ParseIdentifierList();
            Consume(TokenType.RPAREN, "Expected ')' after reference table columns");
            return new TableForeignKeyConstraint(cols, new ForeignKeyReference(destTable, refCols)) { ConstraintName = constraintName };
        }
        throw new SyntaxException($"Unexpected token {_parser.Current.Type} in table constraint", _parser.Current.Line, _parser.Current.Column);
    }

    public ForeignKeyReference ParseForeignKeyReference()
    {
        var table = ParseTableReference(false);
        Consume(TokenType.LPAREN, "Expected '(' after table name in REFERENCES");
        var cols = ParseIdentifierList();
        Consume(TokenType.RPAREN, "Expected ')' after column list in REFERENCES");
        return new ForeignKeyReference(table, cols);
    }

    private List<string> ParseIdentifierList()
    {
        var list = new List<string>();
        while (true)
        {
            list.Add(ConsumeIdentifier("Expected identifier").Value);
            if (!Match(TokenType.COMMA)) break;
        }
        return list;
    }

    private Statement ParseCreateIndex(Token startToken, bool isUnique)
    {
        Consume(TokenType.INDEX, "Expected INDEX");
        RejectUnsupportedCreateIfNotExists("INDEX");
        var indexName = ConsumeIdentifier("Expected index name").Value;
        Consume(TokenType.ON, "Expected 'ON' after index name");
        var targetTable = ParseTableReference(false);
        Consume(TokenType.LPAREN, "Expected '(' before index columns");
        var columns = new List<string> { ConsumeIdentifier("Expected column name").Value };
        Match(TokenType.ASC); Match(TokenType.DESC); // optional sort direction per column
        while (Match(TokenType.COMMA))
        {
            columns.Add(ConsumeIdentifier("Expected column name").Value);
            Match(TokenType.ASC); Match(TokenType.DESC);
        }
        Consume(TokenType.RPAREN, "Expected ')' after column list");
        if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
        return new CreateIndexStatement(indexName, targetTable, columns, isUnique) { Line = startToken.Line, Column = startToken.Column };
    }

    private Statement ParseCreateSshKeyPair(Token startToken)
    {
        Expression? path = null;
        Expression? bits = null;
        Expression? algorithm = null;
        Expression? passphrase = null;
        Expression? comment = null;

        if (Match(TokenType.LPAREN))
        {
            path = ParseExpression();
            if (Match(TokenType.COMMA))
            {
                bits = ParseExpression();
                if (Match(TokenType.COMMA))
                {
                    algorithm = ParseExpression();
                    if (Match(TokenType.COMMA))
                    {
                        passphrase = ParseExpression();
                        if (Match(TokenType.COMMA)) comment = ParseExpression();
                    }
                }
            }
            Consume(TokenType.RPAREN, "Expected ')' after arguments");
        }
        else
        {
            path = ParseExpression();
            if (Match(TokenType.WITH))
            {
                Consume(TokenType.LPAREN, "Expected '(' after WITH");
                while (!Match(TokenType.RPAREN))
                {
                    var keyToken = Advance();
                    string key = keyToken.Value.ToUpperInvariant();
                    Consume(TokenType.EQUALS, "Expected '='");
                    var val = ParseExpression();
                    switch (key)
                    {
                        case "BITS": bits = val; break;
                        case "ALGORITHM": algorithm = val; break;
                        case "PASSPHRASE": passphrase = val; break;
                        case "COMMENT": comment = val; break;
                        default: throw new SyntaxException($"Unknown SSH_KEY_PAIR option: {key}", keyToken.Line, keyToken.Column);
                    }
                    if (!Match(TokenType.COMMA)) { Consume(TokenType.RPAREN, "Expected ')' or ','"); break; }
                }
            }
        }

        Match(TokenType.SEMICOLON);
        return new CreateSshKeyPairStatement(path!, bits, algorithm, passphrase, comment) { Line = startToken.Line, Column = startToken.Column };
    }

    private CreateJobStatement ParseCreateJob(Token startToken, ObjectCreationMode mode)
    {
        var jobName = ConsumeIdentifier("Expected job name").Value;

        if (_parser.Current.Type == TokenType.ON)
            throw new SyntaxException(
                "CREATE JOB ... ON SCHEDULE EVERY ... AS ... has been retired. " +
                "Create a named cron schedule with CREATE SCHEDULE, create the executable with " +
                "CREATE JOB ... FOR SCRIPT '<path>', then attach it with ALTER JOB ... ADD SCHEDULE.",
                _parser.Current.Line, _parser.Current.Column);

        Consume(TokenType.FOR, "Expected FOR SCRIPT '<path>' or FOR REPORT '<path>' after the job name");
        JobTargetKind targetKind;
        if (Match(TokenType.SCRIPT)) targetKind = JobTargetKind.Script;
        else if (Match(TokenType.REPORT)) targetKind = JobTargetKind.Report;
        else
            throw new SyntaxException(
                "Expected SCRIPT or REPORT after FOR in CREATE JOB. A job must name exactly one executable target.",
                _parser.Current.Line, _parser.Current.Column);

        var targetPath = Consume(
            TokenType.STRING_LITERAL,
            $"Expected a quoted {(targetKind == JobTargetKind.Script ? "script" : "report")} path after FOR {targetKind.ToString().ToUpperInvariant()}"
        ).Value;

        int? maxRetries = null;
        int? retryDelay = null;
        string? displayName = null;
        string? description = null;
        Dictionary<string, string>? options = null;

        if (Match(TokenType.WITH))
        {
            Consume(TokenType.LPAREN, "Expected '(' after WITH");
            while (!Match(TokenType.RPAREN) && _parser.Current.Type != TokenType.EOF)
            {
                var keyTok = Advance();
                string key = keyTok.Value.ToUpperInvariant();
                Consume(TokenType.EQUALS, "Expected '=' after option key");

                var valExpr = ParseExpression();
                if (key is "MAX_RETRIES" or "RETRY_DELAY")
                {
                    if (valExpr is not LiteralExpression { Type: TokenType.NUMBER } lit)
                        throw new SyntaxException($"Expected numeric literal for JOB option {key}", keyTok.Line, keyTok.Column);
                    int val = (int)(Convert.ChangeType(lit.Value, typeof(int)) ?? 0);
                    if (val < 0)
                        throw new SyntaxException($"JOB option {key} cannot be negative", keyTok.Line, keyTok.Column);
                    if (key == "MAX_RETRIES") maxRetries = val;
                    else retryDelay = val;
                }
                else
                {
                    var value = valExpr switch
                    {
                        LiteralExpression { Value: string s } => s,
                        IdentifierExpression identifier => identifier.Name,
                        _ => throw new SyntaxException(
                            $"Expected a string literal or identifier for JOB option {key}",
                            keyTok.Line, keyTok.Column)
                    };
                    if (key == "DISPLAY_NAME") displayName = value;
                    else if (key == "DESCRIPTION") description = value;
                    else
                    {
                        options ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        options[key] = value;
                    }
                }

                if (!Match(TokenType.COMMA)) { Consume(TokenType.RPAREN, "Expected ')' or ','"); break; }
            }
        }

        if (_parser.Current.Type == TokenType.AS)
            throw new SyntaxException(
                "Inline AS <statement> job bodies have been retired. Put the statements in a .etlsql file " +
                "and use CREATE JOB ... FOR SCRIPT '<path>'.",
                _parser.Current.Line, _parser.Current.Column);
        if (_parser.Current.Type == TokenType.FOR)
            throw new SyntaxException(
                "FOR SCRIPT and FOR REPORT are mutually exclusive; CREATE JOB accepts exactly one target.",
                _parser.Current.Line, _parser.Current.Column);

        Match(TokenType.SEMICOLON);
        return new CreateJobStatement(
            jobName,
            targetKind,
            targetPath,
            maxRetries,
            retryDelay,
            new CatalogObjectOptions
            {
                DisplayName = displayName,
                Description = description,
                Options = options
            },
            mode)
        {
            Line = startToken.Line,
            Column = startToken.Column
        };
    }

    private ScheduleInfo ParseSchedule()
    {
        Consume(TokenType.EVERY, "Expected EVERY in SCHEDULE");
        int interval = int.Parse(Consume(TokenType.NUMBER, "Expected interval number").Value);
        var unitToken = Advance();
        string unit = unitToken.Value.ToUpper();
        if (unit != "SECOND" && unit != "SECONDS" && unit != "MINUTE" && unit != "MINUTES" &&
            unit != "HOUR" && unit != "HOURS" && unit != "DAY" && unit != "DAYS")
            throw new SyntaxException($"Unexpected schedule unit: {unit}", unitToken.Line, unitToken.Column);
        if (unit.EndsWith("S")) unit = unit.Substring(0, unit.Length - 1);
        string? atTime = null;
        if (Match(TokenType.AT))
            atTime = Consume(TokenType.STRING_LITERAL, "Expected time string (e.g. '02:00') after AT").Value;
        return new ScheduleInfo(interval, unit, atTime);
    }

    private Statement ParseCreateSets(Token startToken)
    {
        Consume(TokenType.BANG, "Expected '!' before set name in CREATE SETS");
        var name = ConsumeIdentifier("Expected set name after '!'").Value;
        Consume(TokenType.BEGIN, "Expected BEGIN after set name");

        var assignments = new List<SetsAssignment>();
        bool withPrompt = false;

        while (_parser.Current.Type != TokenType.END && _parser.Current.Type != TokenType.EOF)
        {
            if (_parser.Current.Type == TokenType.SET)
            {
                Advance();
                ConsumeIdentifier("Expected WITH_PROMPT after SET");
                if (Match(TokenType.ON)) withPrompt = true;
                else if (Match(TokenType.OFF)) withPrompt = false;
                Match(TokenType.SEMICOLON);
                continue;
            }
            if (_parser.Current.Type != TokenType.VARIABLE) break;
            var varName = Advance().Value.TrimStart('@');
            Consume(TokenType.EQUALS, "Expected '='");
            var valueExpr = ParseExpression();
            assignments.Add(new SetsAssignment(varName, valueExpr));
            Match(TokenType.COMMA);
            Match(TokenType.SEMICOLON);
        }

        Consume(TokenType.END, "Expected END");
        Match(TokenType.SEMICOLON);
        return new CreateSetsStatement(name, assignments, withPrompt) { Line = startToken.Line, Column = startToken.Column };
    }
    private Statement ParseCreatePgpKeyPair(Token startToken)
    {
        var startPos = _parser.Current;
        Expression? path = null, bits = null, identity = null, passphrase = null;

        bool isFunctionStyle = Match(TokenType.LPAREN);
        if (isFunctionStyle)
        {
            path = ParseExpression();
            if (Match(TokenType.COMMA)) bits = ParseExpression();
            if (Match(TokenType.COMMA)) identity = ParseExpression();
            if (Match(TokenType.COMMA)) passphrase = ParseExpression();
            Consume(TokenType.RPAREN, "Expected ')' after arguments");
        }
        else
        {
            path = ParseExpression();
            if (Match(TokenType.WITH))
            {
                Consume(TokenType.LPAREN, "Expected '(' after WITH");
                while (!Match(TokenType.RPAREN))
                {
                    var keyToken = Advance();
                    string key = keyToken.Value.ToUpperInvariant();
                    Consume(TokenType.EQUALS, "Expected '='");
                    var val = ParseExpression();
                    switch (key)
                    {
                        case "BITS": bits = val; break;
                        case "IDENTITY": identity = val; break;
                        case "PASSPHRASE": passphrase = val; break;
                        default: throw new SyntaxException($"Unknown PGP_KEY_PAIR option: {key}", keyToken.Line, keyToken.Column);
                    }
                    if (!Match(TokenType.COMMA)) { Consume(TokenType.RPAREN, "Expected ')' or ','"); break; }
                }
            }
        }

        Match(TokenType.SEMICOLON);
        return new CreatePgpKeyPairStatement(path!, bits, identity, passphrase) { Line = startToken.Line, Column = startToken.Column };
    }
}
