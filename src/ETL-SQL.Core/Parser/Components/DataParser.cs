using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Core.Parser.Components
{
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

        public Statement ParseCreate(Token startToken)
        {
            bool orAlter = false;
            if (Match(TokenType.OR))
            {
                Consume(TokenType.ALTER, "Expected ALTER after CREATE OR");
                orAlter = true;
            }
            var mode = orAlter ? ObjectCreationMode.CreateOrAlter : ObjectCreationMode.Create;

            if (Match(TokenType.CONNECTION)) return ParseCreateConnection(startToken, mode);
            if (Match(TokenType.TABLE))
            {
                if (orAlter) throw new SyntaxException("CREATE OR ALTER is not supported for TABLE.", _parser.Current.Line, _parser.Current.Column);
                return ParseCreateTable(startToken);
            }
            if (Match(TokenType.PROCEDURE)) return ParseCreateProcedure(startToken, mode);
            if (Match(TokenType.FUNCTION))  return ParseCreateFunction(startToken, mode);
            if (Match(TokenType.JOB))
            {
                var stmt = ParseCreateJob(startToken);
                return orAlter ? (Statement)(stmt with { IsOrAlter = true }) : stmt;
            }

            if (Match(TokenType.DIRECTORY))
            {
                var path = ParseExpression();
                Expression? overwrite = null;
                if (Match(TokenType.WITH)) overwrite = ParseWithOverwrite();
                Match(TokenType.SEMICOLON);
                return new DirectoryOperationStatement(DirectoryOpType.Create, path, null, overwrite) { Line = startToken.Line, Column = startToken.Column };
            }

            if (_parser.Current.Type == TokenType.UNIQUE || _parser.Current.Type == TokenType.INDEX)
            {
                if (orAlter) throw new SyntaxException("CREATE OR ALTER is not supported for INDEX.", _parser.Current.Line, _parser.Current.Column);
                bool isUnique = Match(TokenType.UNIQUE);
                return ParseCreateIndex(startToken, isUnique);
            }

            if (Match(TokenType.SSH_KEY_PAIR))
            {
                if (orAlter) throw new SyntaxException("CREATE OR ALTER is not supported for SSH_KEY_PAIR.", _parser.Current.Line, _parser.Current.Column);
                return ParseCreateSshKeyPair(startToken);
            }

            if (Match(TokenType.PGP_KEY_PAIR))
            {
                if (orAlter) throw new SyntaxException("CREATE OR ALTER is not supported for PGP_KEY_PAIR.", _parser.Current.Line, _parser.Current.Column);
                return ParseCreatePgpKeyPair(startToken);
            }

            if (Match(TokenType.SETS))
            {
                if (orAlter) throw new SyntaxException("CREATE OR ALTER is not supported for SETS.", _parser.Current.Line, _parser.Current.Column);
                return ParseCreateSets(startToken);
            }

            // Report-SQL
            if (Match(TokenType.VISUAL))     return _parent.ReportParser.ParseCreateVisual(startToken, mode);
            if (Match(TokenType.PAGE))       return _parent.ReportParser.ParseCreatePage(startToken, mode);
            if (Match(TokenType.DATASET))    return _parent.ReportParser.ParseCreateDataset(startToken, mode);
            if (Match(TokenType.CONTAINER))  return _parent.ReportParser.ParseCreateContainer(startToken, mode);
            if (Match(TokenType.NAVIGATION)) return _parent.ReportParser.ParseCreateNavigation(startToken, mode);
            if (Match(TokenType.STYLE))      return _parent.ReportParser.ParseCreateStyle(startToken, mode);
            if (Match(TokenType.BUTTON))     return _parent.ReportParser.ParseCreateButton(startToken, mode);
            if (Match(TokenType.TEMPLATE))   return _parent.ReportParser.ParseCreateTemplate(startToken, mode);
            if (Match(TokenType.THEME))      return _parent.ReportParser.ParseCreateTheme(startToken, mode);

            // Portal admin
            if (Match(TokenType.USER))       return _parent.PortalParser.ParseCreateUser(startToken);
            if (Match(TokenType.GROUP))      return _parent.PortalParser.ParseCreateGroup(startToken);
            if (Match(TokenType.FOLDER))     return _parent.PortalParser.ParseCreateFolder(startToken);
            if (Match(TokenType.REFRESH))    return _parent.PortalParser.ParseCreateRefreshJob(startToken);
            if (Match(TokenType.SUBSCRIPTION)) return _parent.PortalParser.ParseCreateSubscription(startToken);
            if (Match(TokenType.SHARE))      return _parent.PortalParser.ParseCreateShareLink(startToken);
            if (Match(TokenType.EMBED))      return _parent.PortalParser.ParseCreateEmbedToken(startToken);
            if (Match(TokenType.SAVED))      return _parent.PortalParser.ParseCreateSavedView(startToken);
            if (Match(TokenType.ALERT))      return _parent.PortalParser.ParseCreateAlert(startToken);

            throw new SyntaxException("Expected CONNECTION, TABLE, PROCEDURE, FUNCTION, INDEX, SETS, SSH_KEY_PAIR, VISUAL, PAGE, DATASET, CONTAINER, NAVIGATION, STYLE, BUTTON, TEMPLATE, or THEME after CREATE", _parser.Current.Line, _parser.Current.Column);
        }

        public Statement ParseAlter(Token startToken)
        {
            if (Match(TokenType.CONNECTION)) return ParseAlterConnection(startToken);
            if (Match(TokenType.PROCEDURE))  return ParseCreateProcedure(startToken, ObjectCreationMode.Alter);
            if (Match(TokenType.FUNCTION))   return ParseCreateFunction(startToken, ObjectCreationMode.Alter);
            if (Match(TokenType.TABLE))      return ParseAlterTable(startToken);

            // Report-SQL
            if (Match(TokenType.VISUAL))     return _parent.ReportParser.ParseAlterReportObject(ReportObjectType.Visual);
            if (Match(TokenType.PAGE))       return _parent.ReportParser.ParseAlterReportObject(ReportObjectType.Page);
            if (Match(TokenType.CONTAINER))  return _parent.ReportParser.ParseAlterReportObject(ReportObjectType.Container);
            if (Match(TokenType.STYLE))      return _parent.ReportParser.ParseAlterReportObject(ReportObjectType.Style);
            if (Match(TokenType.NAVIGATION)) return _parent.ReportParser.ParseAlterReportObject(ReportObjectType.Navigation);
            if (Match(TokenType.DATASET))
            {
                if (_parser.Current.Type == TokenType.STRING_LITERAL)
                    return _parent.PortalParser.ParseAlterDataset(startToken);
                return _parent.ReportParser.ParseAlterReportObject(ReportObjectType.Dataset);
            }
            if (Match(TokenType.TEMPLATE))   return _parent.ReportParser.ParseAlterReportObject(ReportObjectType.Template);

            // Orchestrator job management
            if (Match(TokenType.JOB)) return ParseAlterJob(startToken);

            // Portal admin
            if (Match(TokenType.USER))         return _parent.PortalParser.ParseAlterUser(startToken);
            if (Match(TokenType.FOLDER))       return _parent.PortalParser.ParseAlterFolder(startToken);
            if (Match(TokenType.REPORT))       return _parent.PortalParser.ParseAlterReport(startToken);
            if (Match(TokenType.SUBSCRIPTION)) return _parent.PortalParser.ParseAlterSubscription(startToken);

            throw new SyntaxException("Expected CONNECTION, PROCEDURE, FUNCTION, TABLE, JOB, or REPORT object after ALTER", _parser.Current.Line, _parser.Current.Column);
        }

        /// <summary>
        /// ALTER JOB &lt;name&gt; [ON SCHEDULE EVERY n unit [AT 'time']] [AS &lt;statement&gt;];
        /// At least one of schedule or script must be provided.
        /// </summary>
        private Statement ParseAlterJob(Token startToken)
        {
            var jobName = ConsumeIdentifier("Expected job name after ALTER JOB").Value;

            ScheduleInfo? schedule = null;
            Statement? script = null;

            // Form 1: ALTER JOB name ON SCHEDULE EVERY n unit [AT 'time'] [AS script]
            if (Match(TokenType.ON))
            {
                Consume(TokenType.SCHEDULE, "Expected SCHEDULE after ON");
                schedule = ParseSchedule();
                if (Match(TokenType.AS))
                    script = _parser.ParseStatement();
            }
            // Form 2: ALTER JOB name SET SCHEDULE = EVERY n unit [AT 'time']
            else if (Match(TokenType.SET))
            {
                if (MatchIdentifier("SCHEDULE") || Match(TokenType.SCHEDULE))
                {
                    Match(TokenType.EQUALS);
                    schedule = ParseSchedule();
                }
                else if (Match(TokenType.AS))
                {
                    script = _parser.ParseStatement();
                }
                else
                    throw new SyntaxException("Expected SCHEDULE or AS after ALTER JOB ... SET", _parser.Current.Line, _parser.Current.Column);
            }
            // Form 3: ALTER JOB name AS script  (replace script only)
            else if (Match(TokenType.AS))
            {
                script = _parser.ParseStatement();
            }
            else
            {
                throw new SyntaxException(
                    "Expected ON SCHEDULE, SET, or AS after ALTER JOB name",
                    _parser.Current.Line, _parser.Current.Column);
            }

            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
            return new AlterJobStatement(jobName, schedule, script) { Line = startToken.Line, Column = startToken.Column };
        }

        public Statement ParseDrop(Token startToken)
        {
            bool ifExists = false;
            if (Match(TokenType.TABLE))
            {
                if (Match(TokenType.IF)) { Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
                var target = ParseTableReference(false);
                if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
                return new DropTableStatement(target, ifExists) { Line = startToken.Line, Column = startToken.Column };
            }
            else if (Match(TokenType.CONNECTION))
            {
                if (Match(TokenType.IF)) { Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
                var name = ConsumeIdentifier("Expected connection name").Value;
                if (!ifExists && Match(TokenType.IF)) { Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
                if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
                return new DropConnectionStatement(name, ifExists) { Line = startToken.Line, Column = startToken.Column };
            }
            else if (Match(TokenType.PROCEDURE))
            {
                if (Match(TokenType.IF)) { Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
                var name = ConsumeIdentifier("Expected procedure name").Value;
                if (!ifExists && Match(TokenType.IF)) { Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
                if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
                return new DropProcedureStatement(name, ifExists) { Line = startToken.Line, Column = startToken.Column };
            }
            else if (Match(TokenType.FUNCTION))
            {
                if (Match(TokenType.IF)) { Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
                var name = ConsumeIdentifier("Expected function name").Value;
                if (!ifExists && Match(TokenType.IF)) { Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
                if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
                return new DropFunctionStatement(name, ifExists) { Line = startToken.Line, Column = startToken.Column };
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
                if (!ifExists && Match(TokenType.IF)) { Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
                if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
                return new DropIndexStatement(idxName, target, ifExists) { Line = startToken.Line, Column = startToken.Column };
            }
            else if (Match(TokenType.SETS))
            {
                if (Match(TokenType.IF)) { Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
                Consume(TokenType.BANG, "Expected '!' before set name in DROP SETS");
                var name = ConsumeIdentifier("Expected set name after '!'").Value;
                if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
                return new DropSetsStatement(name, ifExists) { Line = startToken.Line, Column = startToken.Column };
            }
            else if (Match(TokenType.VISUAL) || (Match(TokenType.IDENTIFIER) && _parser.Current.Value.Equals("CHART", StringComparison.OrdinalIgnoreCase)))
            {
                if (Match(TokenType.IF)) { Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
                var name = ConsumeIdentifier("Expected visual name").Value;
                if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
                return new DropReportObjectStatement { ObjectType = ReportObjectType.Visual, Name = name, IfExists = ifExists, Line = startToken.Line, Column = startToken.Column };
            }
            else if (Match(TokenType.PAGE))
            {
                if (Match(TokenType.IF)) { Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
                var name = ConsumeIdentifier("Expected page name").Value;
                if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
                return new DropReportObjectStatement { ObjectType = ReportObjectType.Page, Name = name, IfExists = ifExists, Line = startToken.Line, Column = startToken.Column };
            }
            else if (Match(TokenType.CONTAINER))
            {
                if (Match(TokenType.IF)) { Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
                var name = ConsumeIdentifier("Expected container name").Value;
                if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
                return new DropReportObjectStatement { ObjectType = ReportObjectType.Container, Name = name, IfExists = ifExists, Line = startToken.Line, Column = startToken.Column };
            }
            else if (Match(TokenType.STYLE))
            {
                if (Match(TokenType.IF)) { Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
                var name = ConsumeIdentifier("Expected style name").Value;
                if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
                return new DropReportObjectStatement { ObjectType = ReportObjectType.Style, Name = name, IfExists = ifExists, Line = startToken.Line, Column = startToken.Column };
            }
            else if (Match(TokenType.NAVIGATION))
            {
                if (Match(TokenType.IF)) { Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
                var name = ConsumeIdentifier("Expected navigation name").Value;
                if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
                return new DropReportObjectStatement { ObjectType = ReportObjectType.Navigation, Name = name, IfExists = ifExists, Line = startToken.Line, Column = startToken.Column };
            }
            else if (Match(TokenType.DATASET))
            {
                if (_parser.Current.Type == TokenType.STRING_LITERAL)
                    return _parent.PortalParser.ParseDropDataset(startToken);
                if (Match(TokenType.IF)) { Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
                var name = ConsumeIdentifier("Expected dataset name").Value;
                if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
                return new DropReportObjectStatement { ObjectType = ReportObjectType.Dataset, Name = name, IfExists = ifExists, Line = startToken.Line, Column = startToken.Column };
            }
            else if (Match(TokenType.TEMPLATE))
            {
                if (Match(TokenType.IF)) { Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
                var name = ConsumeIdentifier("Expected template name").Value;
                if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
                return new DropReportObjectStatement { ObjectType = ReportObjectType.Template, Name = name, IfExists = ifExists, Line = startToken.Line, Column = startToken.Column };
            }
            else if (Match(TokenType.THEME))
            {
                if (Match(TokenType.IF)) { Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
                var name = ConsumeIdentifier("Expected theme name").Value;
                if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
                return new DropReportObjectStatement { ObjectType = ReportObjectType.Theme, Name = name, IfExists = ifExists, Line = startToken.Line, Column = startToken.Column };
            }
            else if (Match(TokenType.JOB))
            {
                if (Match(TokenType.IF)) { Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
                var name = ConsumeIdentifier("Expected job name to drop").Value;
                if (!ifExists && Match(TokenType.IF)) { Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
                if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
                return new DropJobStatement(name, ifExists) { Line = startToken.Line, Column = startToken.Column };
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
            if (Match(TokenType.FILE))
            {
                bool ifExists = false;
                if (Match(TokenType.IF)) { Consume(TokenType.EXISTS, "Expected EXISTS after IF"); ifExists = true; }
                var source = ParseExpression();
                Match(TokenType.SEMICOLON);
                return new FileOperationStatement(FileOpType.Delete, source, ifExists: ifExists) { Line = startToken.Line, Column = startToken.Column };
            }
            if (Match(TokenType.DIRECTORY))
            {
                bool ifExists = false;
                if (Match(TokenType.IF)) { Consume(TokenType.EXISTS, "Expected EXISTS after IF"); ifExists = true; }
                var path = ParseExpression();
                Match(TokenType.SEMICOLON);
                return new DirectoryOperationStatement(DirectoryOpType.Delete, path, ifExists: ifExists) { Line = startToken.Line, Column = startToken.Column };
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
                return new InsertStatement(targetTable, columns, rows) { Line = startToken.Line, Column = startToken.Column, Output = output };
            }
            else
            {
                if (_parser.Current.Type == TokenType.EXEC || _parser.Current.Type == TokenType.EXECUTE)
                {
                    Advance();
                    var exec = _parent.ExtensionParser.ParseExecute();
                    if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
                    return new InsertStatement(targetTable, columns, exec) { Line = startToken.Line, Column = startToken.Column, Output = output };
                }

                var query = _parser.ParseQuery();
                if (_parser.Current.Type == TokenType.SEMICOLON) Advance();
                return new InsertStatement(targetTable, columns, query) { Line = startToken.Line, Column = startToken.Column, Output = output };
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

            var whenMatched    = new List<MergeMatchedClause>();
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

            if (Match(TokenType.ON))
            {
                var typeToken = Advance();
                connectionType = typeToken.Value;
                // Deprecation handled by DeprecatedConnectionSyntaxRule

                bool hasParen = Match(TokenType.LPAREN);
                if (hasParen && _parser.Current.Type == TokenType.RPAREN)
                    target = new LiteralExpression("", TokenType.STRING_LITERAL);
                else if (!hasParen && (_parser.Current.Type == TokenType.WITH || _parser.Current.Type == TokenType.SEMICOLON))
                    target = null;
                else
                    target = ParseExpression();
                if (hasParen) Consume(TokenType.RPAREN, "Expected ')' after target string");
            }

            Dictionary<string, Expression>? options = null;
            if (Match(TokenType.WITH))
            {
                options = new Dictionary<string, Expression>(StringComparer.OrdinalIgnoreCase);
                Consume(TokenType.LPAREN, "Expected '(' after WITH");
                while (_parser.Current.Type != TokenType.RPAREN && _parser.Current.Type != TokenType.EOF)
                {
                    string key = Advance().Value;
                    Consume(TokenType.EQUALS, "Expected '=' after option key");
                    var val = ParseExpression();
                    options[key] = val;
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
            var name = ConsumeIdentifier("Expected connection name").Value;
            string? connectionType = null;
            Expression? target = null;

            if (Match(TokenType.ON) || mode == ObjectCreationMode.Create)
            {
                if (Match(TokenType.TYPE))
                {
                    connectionType = Advance().Value;
                    Consume(TokenType.TARGET, "Expected TARGET after connection type");
                    target = ParseExpression();
                }
                else
                {
                    var typeToken = Advance();
                    connectionType = typeToken.Value;
                    // Deprecation handled by DeprecatedConnectionSyntaxRule

                    bool hasParen = Match(TokenType.LPAREN);
                    if (hasParen && _parser.Current.Type == TokenType.RPAREN)
                        target = new LiteralExpression("", TokenType.STRING_LITERAL);
                    else if (!hasParen && (_parser.Current.Type == TokenType.WITH || _parser.Current.Type == TokenType.SEMICOLON))
                        target = null;
                    else
                        target = ParseExpression();

                    if (hasParen) Consume(TokenType.RPAREN, "Expected ')' after target string");
                }
            }

            Dictionary<string, Expression>? options = null;
            if (Match(TokenType.WITH))
            {
                options = new Dictionary<string, Expression>(StringComparer.OrdinalIgnoreCase);
                Consume(TokenType.LPAREN, "Expected '(' after WITH clause");
                while (_parser.Current.Type != TokenType.RPAREN && _parser.Current.Type != TokenType.EOF)
                {
                    string key = Advance().Value;
                    Consume(TokenType.EQUALS, "Expected '=' after option key");
                    var val = ParseExpression();
                    options[key] = val;
                    if (!Match(TokenType.COMMA)) break;
                }
                Consume(TokenType.RPAREN, "Expected ')' at end of WITH options");
            }

            Consume(TokenType.SEMICOLON, "Expected ';' at the end of CREATE CONNECTION");
            return new CreateConnectionStatement(name, connectionType, target, options, mode) { Line = startToken.Line, Column = startToken.Column };
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
                    _parser.Current.Type == TokenType.UNIQUE  ||
                    _parser.Current.Type == TokenType.CHECK   ||
                    _parser.Current.Type == TokenType.FOREIGN)
                {
                    tableConstraints.Add(ParseTableConstraint(constraintName));
                }
                else
                {
                    var colName  = ConsumeIdentifier("Expected column name or constraint").Value;
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
                                dataType += Consume(TokenType.NUMBER, "Expected scale").Value;
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
                        else if (Match(TokenType.UNIQUE))  { colDef.IsUnique = true; }
                        else if (Match(TokenType.NOT))     { Consume(TokenType.NULL, "Expected NULL after NOT"); colDef.IsNullable = false; }
                        else if (Match(TokenType.NULL))    { colDef.IsNullable = true; }
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
                if (Match(TokenType.COMMA)) { bits = ParseExpression();
                    if (Match(TokenType.COMMA)) { algorithm = ParseExpression();
                        if (Match(TokenType.COMMA)) { passphrase = ParseExpression();
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
                            case "BITS":       bits = val;       break;
                            case "ALGORITHM":  algorithm = val;  break;
                            case "PASSPHRASE": passphrase = val; break;
                            case "COMMENT":    comment = val;    break;
                            default: throw new SyntaxException($"Unknown SSH_KEY_PAIR option: {key}", keyToken.Line, keyToken.Column);
                        }
                        if (!Match(TokenType.COMMA)) { Consume(TokenType.RPAREN, "Expected ')' or ','"); break; }
                    }
                }
            }

            Match(TokenType.SEMICOLON);
            return new CreateSshKeyPairStatement(path!, bits, algorithm, passphrase, comment) { Line = startToken.Line, Column = startToken.Column };
        }

        private CreateJobStatement ParseCreateJob(Token startToken)
        {
            var jobName = ConsumeIdentifier("Expected job name").Value;
            Consume(TokenType.ON, "Expected ON after job name");
            Consume(TokenType.SCHEDULE, "Expected SCHEDULE after ON");
            var schedule = ParseSchedule();

            int maxRetries = 0;
            int retryDelay = 30;

            if (Match(TokenType.WITH))
            {
                Consume(TokenType.LPAREN, "Expected '(' after WITH");
                while (!Match(TokenType.RPAREN) && _parser.Current.Type != TokenType.EOF)
                {
                    var keyTok = Advance();
                    string key = keyTok.Value.ToUpperInvariant();
                    Consume(TokenType.EQUALS, "Expected '=' after option key");
                    
                    var valExpr = ParseExpression();
                    if (valExpr is LiteralExpression lit && lit.Type == TokenType.NUMBER)
                    {
                        int val = (int)(Convert.ChangeType(lit.Value, typeof(int)) ?? 0);
                        if (key == "MAX_RETRIES") maxRetries = val;
                        else if (key == "RETRY_DELAY" || key == "RETRY_DELAY_SECONDS") retryDelay = val;
                        else throw new SyntaxException($"Unknown JOB option: {key}", keyTok.Line, keyTok.Column);
                    }
                    else
                    {
                        throw new SyntaxException($"Expected numeric literal for JOB option {key}", keyTok.Line, keyTok.Column);
                    }

                    if (!Match(TokenType.COMMA)) { Consume(TokenType.RPAREN, "Expected ')' or ','"); break; }
                }
            }

            Consume(TokenType.AS, "Expected AS before job script");
            var script = _parser.ParseStatement();
            return new CreateJobStatement(jobName, schedule, script, maxRetries, retryDelay) { Line = startToken.Line, Column = startToken.Column };
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
                            case "BITS":       bits = val;       break;
                            case "IDENTITY":   identity = val;   break;
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
}
