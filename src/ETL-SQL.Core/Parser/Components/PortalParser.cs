using System;
using System.Collections.Generic;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Core.Parser.Components
{
    /// <summary>
    /// Parses portal admin statements that appear inside EXECUTE portal BEGIN…END blocks.
    /// Every method here is called after the leading keyword has already been consumed.
    /// </summary>
    public class PortalParser : ParserComponent
    {
        public PortalParser(IParser parser, StatementParser parent) : base(parser, parent) { }

        // ── Users ─────────────────────────────────────────────────────────────

        // CREATE USER 'username' WITH (EMAIL=..., PASSWORD=..., ROLE=...[, FIRST_NAME=..., LAST_NAME=...])
        public Statement ParseCreateUser(Token start)
        {
            string username = ConsumeStringLiteral("Expected username string literal");
            Consume(TokenType.WITH, "Expected WITH after username");
            Consume(TokenType.LPAREN, "Expected '('");

            string? email = null, role = null, firstName = null, lastName = null;
            Expression? password = null;

            ParseOptionList(() =>
            {
                string key = Advance().Value;
                Consume(TokenType.EQUALS, "Expected '='");
                switch (key.ToUpperInvariant())
                {
                    case "EMAIL":      email     = ConsumeStringLiteral("Expected email string literal");   break;
                    case "PASSWORD":   password  = ParseExpression();                  break;
                    case "ROLE":       role      = Advance().Value;                    break;
                    case "FIRST_NAME": firstName = ConsumeStringLiteral("Expected first name string literal"); break;
                    case "LAST_NAME":  lastName  = ConsumeStringLiteral("Expected last name string literal");  break;
                    default: ParseExpression(); break; // skip unknown
                }
            });

            if (email    is null) throw new SyntaxException("CREATE USER requires EMAIL", start.Line, start.Column);
            if (password is null) throw new SyntaxException("CREATE USER requires PASSWORD", start.Line, start.Column);
            role ??= "Viewer";

            return new CreatePortalUserStatement(username, email, password, role, firstName, lastName)
            { Line = start.Line, Column = start.Column };
        }

        // ALTER USER 'username' SET ROLE=... | DISABLE | ENABLE | SET PASSWORD=...
        public Statement ParseAlterUser(Token start)
        {
            string username = ConsumeStringLiteral("Expected username string literal");
            Consume(TokenType.SET, "Expected SET");

            string? newRole = null, newEmail = null;
            Expression? newPassword = null;
            bool? setActive = null;

            // Allow multiple SET clauses separated by commas
            do
            {
                if (Match(TokenType.DISABLE)) { setActive = false; continue; }
                if (Match(TokenType.ENABLE))  { setActive = true;  continue; }

                string key = Advance().Value;
                Consume(TokenType.EQUALS, "Expected '='");
                switch (key.ToUpperInvariant())
                {
                    case "ROLE":     newRole     = Advance().Value;           break;
                    case "EMAIL":    newEmail    = ConsumeStringLiteral("Expected email string literal"); break;
                    case "PASSWORD": newPassword = ParseExpression();          break;
                    default: ParseExpression(); break;
                }
            } while (Match(TokenType.COMMA));

            return new AlterPortalUserStatement(username, newRole, newEmail, setActive, newPassword)
            { Line = start.Line, Column = start.Column };
        }

        // DROP USER 'username' [CASCADE]
        public Statement ParseDropUser(Token start)
        {
            string username = ConsumeStringLiteral("Expected username string literal");
            bool cascade = MatchIdentifier("CASCADE");
            return new DropPortalUserStatement(username, cascade)
            { Line = start.Line, Column = start.Column };
        }

        // ── Groups ────────────────────────────────────────────────────────────

        // CREATE GROUP 'name' [WITH (DESCRIPTION=...)]
        public Statement ParseCreateGroup(Token start)
        {
            string name = ConsumeStringLiteral("Expected group name string literal");
            string? description = null;
            if (Match(TokenType.WITH))
            {
                Consume(TokenType.LPAREN, "Expected '('");
                ParseOptionList(() =>
                {
                    string key = Advance().Value;
                    Consume(TokenType.EQUALS, "Expected '='");
                    if (key.Equals("DESCRIPTION", StringComparison.OrdinalIgnoreCase))
                        description = ConsumeStringLiteral("Expected description string literal");
                    else
                        ParseExpression();
                });
            }
            return new CreatePortalGroupStatement(name, description)
            { Line = start.Line, Column = start.Column };
        }

        // DROP GROUP 'name' [CASCADE]
        public Statement ParseDropGroup(Token start)
        {
            string name = ConsumeStringLiteral("Expected group name string literal");
            bool cascade = MatchIdentifier("CASCADE");
            return new DropPortalGroupStatement(name, cascade)
            { Line = start.Line, Column = start.Column };
        }

        // ADD USER 'username' TO GROUP 'groupname'
        public Statement ParseAddUserToGroup(Token start)
        {
            // Arrived after ADD USER
            string username = ConsumeStringLiteral("Expected username string literal");
            Consume(TokenType.TO, "Expected TO");
            Consume(TokenType.GROUP, "Expected GROUP");
            string group = ConsumeStringLiteral("Expected group name string literal");
            return new AddUserToPortalGroupStatement(username, group)
            { Line = start.Line, Column = start.Column };
        }

        // ── Folders ───────────────────────────────────────────────────────────

        // CREATE FOLDER '/path'
        public Statement ParseCreateFolder(Token start)
        {
            string path = ConsumeStringLiteral("Expected folder path string literal");
            return new CreatePortalFolderStatement(path)
            { Line = start.Line, Column = start.Column };
        }

        // DROP FOLDER '/path' [CASCADE]
        public Statement ParseDropFolder(Token start)
        {
            string path = ConsumeStringLiteral("Expected folder path string literal");
            bool cascade = MatchIdentifier("CASCADE");
            return new DropPortalFolderStatement(path, cascade)
            { Line = start.Line, Column = start.Column };
        }

        // ── Permissions ───────────────────────────────────────────────────────

        // GRANT READ|EXECUTE|MANAGE ON FOLDER '/path' TO GROUP 'name'
        public Statement ParseGrant(Token start)
        {
            var perm = ParseFolderPermission();
            Consume(TokenType.ON, "Expected ON");
            Consume(TokenType.FOLDER, "Expected FOLDER");
            string path = ConsumeStringLiteral("Expected folder path string literal");
            Consume(TokenType.TO, "Expected TO");
            Consume(TokenType.GROUP, "Expected GROUP");
            string group = ConsumeStringLiteral("Expected group name string literal");
            return new GrantPortalPermissionStatement(path, group, perm)
            { Line = start.Line, Column = start.Column };
        }

        // REVOKE READ|EXECUTE|MANAGE ON FOLDER '/path' FROM GROUP 'name'
        public Statement ParseRevoke(Token start)
        {
            var perm = ParseFolderPermission();
            Consume(TokenType.ON, "Expected ON");
            Consume(TokenType.FOLDER, "Expected FOLDER");
            string path = ConsumeStringLiteral("Expected folder path string literal");
            Consume(TokenType.FROM, "Expected FROM");
            Consume(TokenType.GROUP, "Expected GROUP");
            string group = ConsumeStringLiteral("Expected group name string literal");
            return new RevokePortalPermissionStatement(path, group, perm)
            { Line = start.Line, Column = start.Column };
        }

        // ── Reports ───────────────────────────────────────────────────────────

        // PUBLISH REPORT 'name' FROM '/scripts/x.rptsql' IN FOLDER '/Finance' [WITH (DESCRIPTION=...)]
        public Statement ParsePublishReport(Token start)
        {
            Consume(TokenType.REPORT, "Expected REPORT");
            string name = ConsumeStringLiteral("Expected report name string literal");
            ConsumeIdentifierValue("FROM", "Expected FROM");
            string scriptPath = ConsumeStringLiteral("Expected script path string literal");
            Consume(TokenType.IN, "Expected IN");
            Consume(TokenType.FOLDER, "Expected FOLDER");
            string folder = ConsumeStringLiteral("Expected folder path string literal");
            string? description = null;
            if (Match(TokenType.WITH))
            {
                Consume(TokenType.LPAREN, "Expected '('");
                ParseOptionList(() =>
                {
                    string key = Advance().Value;
                    Consume(TokenType.EQUALS, "Expected '='");
                    if (key.Equals("DESCRIPTION", StringComparison.OrdinalIgnoreCase))
                        description = ConsumeStringLiteral("Expected description string literal");
                    else
                        ParseExpression();
                });
            }
            return new PublishPortalReportStatement(name, scriptPath, folder, description)
            { Line = start.Line, Column = start.Column };
        }

        // ALTER REPORT 'name' SET FOLDER='/new' | DESCRIPTION='...'
        public Statement ParseAlterReport(Token start)
        {
            Consume(TokenType.REPORT, "Expected REPORT");
            string name = ConsumeStringLiteral("Expected report name string literal");
            Consume(TokenType.SET, "Expected SET");
            string? newFolder = null, newDescription = null;
            do
            {
                string key = Advance().Value;
                Consume(TokenType.EQUALS, "Expected '='");
                switch (key.ToUpperInvariant())
                {
                    case "FOLDER":      newFolder      = ConsumeStringLiteral("Expected folder path string literal"); break;
                    case "DESCRIPTION": newDescription = ConsumeStringLiteral("Expected description string literal"); break;
                    default: ParseExpression(); break;
                }
            } while (Match(TokenType.COMMA));
            return new AlterPortalReportStatement(name, newFolder, newDescription)
            { Line = start.Line, Column = start.Column };
        }

        // DROP REPORT 'name' [CASCADE]
        public Statement ParseDropReport(Token start)
        {
            Consume(TokenType.REPORT, "Expected REPORT");
            string name = ConsumeStringLiteral("Expected report name string literal");
            bool cascade = MatchIdentifier("CASCADE");
            return new DropPortalReportStatement(name, cascade)
            { Line = start.Line, Column = start.Column };
        }

        // ── Refresh Jobs ──────────────────────────────────────────────────────

        // CREATE REFRESH JOB FOR REPORT 'name' SCHEDULE '0 2 * * *' AT orch
        public Statement ParseCreateRefreshJob(Token start)
        {
            // Arrived after CREATE REFRESH
            Consume(TokenType.JOB, "Expected JOB");
            Consume(TokenType.FOR, "Expected FOR");
            Consume(TokenType.REPORT, "Expected REPORT");
            string report = ConsumeStringLiteral("Expected report name string literal");
            ConsumeIdentifierValue("SCHEDULE", "Expected SCHEDULE");
            string schedule = ConsumeStringLiteral("Expected cron expression string literal");
            Consume(TokenType.AT, "Expected AT");
            string alias = Advance().Value;
            return new CreatePortalRefreshJobStatement(report, schedule, alias)
            { Line = start.Line, Column = start.Column };
        }

        // REFRESH REPORT 'name'
        public Statement ParseRefreshReport(Token start)
        {
            string report = ConsumeStringLiteral("Expected report name string literal");
            Match(TokenType.SEMICOLON);
            return new RefreshPortalReportStatement(report)
            { Line = start.Line, Column = start.Column };
        }

        // DROP REFRESH JOB FOR REPORT 'name'
        public Statement ParseDropRefreshJob(Token start)
        {
            // Arrived after DROP REFRESH
            Consume(TokenType.JOB, "Expected JOB");
            Consume(TokenType.FOR, "Expected FOR");
            Consume(TokenType.REPORT, "Expected REPORT");
            string report = ConsumeStringLiteral("Expected report name string literal");
            return new DropPortalRefreshJobStatement(report)
            { Line = start.Line, Column = start.Column };
        }

        // ── Snapshots ─────────────────────────────────────────────────────────

        // DROP SNAPSHOT FOR REPORT 'name'
        public Statement ParseDropSnapshot(Token start)
        {
            ConsumeIdentifierValue("SNAPSHOT", "Expected SNAPSHOT");
            Consume(TokenType.FOR, "Expected FOR");
            Consume(TokenType.REPORT, "Expected REPORT");
            string report = ConsumeStringLiteral("Expected report name string literal");
            return new DropPortalSnapshotStatement(report)
            { Line = start.Line, Column = start.Column };
        }

        // REBUILD SNAPSHOT FOR REPORT 'name'
        public Statement ParseRebuildSnapshot(Token start)
        {
            ConsumeIdentifierValue("SNAPSHOT", "Expected SNAPSHOT");
            Consume(TokenType.FOR, "Expected FOR");
            Consume(TokenType.REPORT, "Expected REPORT");
            string report = ConsumeStringLiteral("Expected report name string literal");
            return new RebuildPortalSnapshotStatement(report)
            { Line = start.Line, Column = start.Column };
        }

        // ── Subscriptions ─────────────────────────────────────────────────────

        // CREATE SUBSCRIPTION ['name'] FOR REPORT '/path' DELIVER TO 'user'|GROUP 'group'
        //   SCHEDULE '0 8 * * MON'|ON REFRESH FORMAT PDF|CSV|BOTH AT smtp-alias
        //   [PARAMETERS (@var = 'value', ...)]
        public Statement ParseCreateSubscription(Token start)
        {
            // Optional subscription name (string literal before FOR)
            string? name = null;
            if (_parser.Current.Type == TokenType.STRING_LITERAL)
                name = Advance().Value;

            Consume(TokenType.FOR, "Expected FOR");
            Consume(TokenType.REPORT, "Expected REPORT");
            string reportPath = ConsumeStringLiteral("Expected report path string literal");
            ConsumeIdentifierValue("DELIVER", "Expected DELIVER");
            Consume(TokenType.TO, "Expected TO");

            bool isGroup = Match(TokenType.GROUP);
            string recipient = ConsumeStringLiteral("Expected recipient string literal");

            string? schedule = null;
            bool onRefresh = false;
            if (Match(TokenType.SCHEDULE))
                schedule = ConsumeStringLiteral("Expected cron expression string literal");
            else if (Match(TokenType.ON))
            {
                Consume(TokenType.REFRESH, "Expected REFRESH");
                onRefresh = true;
            }

            Consume(TokenType.FORMAT, "Expected FORMAT");
            var format = ParseSubscriptionFormat();
            Consume(TokenType.AT, "Expected AT");
            string smtpAlias = Advance().Value;

            var parameters = ParseSubscriptionParameters();

            Match(TokenType.SEMICOLON);
            return new CreatePortalSubscriptionStatement(
                reportPath, recipient, isGroup, schedule, onRefresh, format, smtpAlias, name, parameters)
            { Line = start.Line, Column = start.Column };
        }

        // ALTER SUBSCRIPTION <id> SET SCHEDULE='...'|ENABLE|DISABLE|FORMAT=...|SMTP='...'|PARAMETERS(...)
        public Statement ParseAlterSubscription(Token start)
        {
            int id = ParseIntLiteral("Expected subscription id");
            Consume(TokenType.SET, "Expected SET");

            string? newSchedule = null;
            bool? setActive = null;
            PortalSubscriptionFormat? newFormat = null;
            string? newSmtpAlias = null;
            IReadOnlyList<SubscriptionParameter>? parameters = null;

            do
            {
                if (Match(TokenType.ENABLE))  { setActive = true;  continue; }
                if (Match(TokenType.DISABLE)) { setActive = false; continue; }
                if (MatchIdentifier("PARAMETERS"))
                {
                    parameters = ParseSubscriptionParameterList();
                    continue;
                }
                string key = Advance().Value;
                Consume(TokenType.EQUALS, "Expected '='");
                if (key.Equals("SCHEDULE", StringComparison.OrdinalIgnoreCase))
                    newSchedule = ConsumeStringLiteral("Expected cron expression string literal");
                else if (key.Equals("FORMAT", StringComparison.OrdinalIgnoreCase))
                    newFormat = ParseSubscriptionFormat();
                else if (key.Equals("SMTP", StringComparison.OrdinalIgnoreCase))
                    newSmtpAlias = ConsumeStringLiteral("Expected SMTP alias string literal");
                else
                    ParseExpression();
            } while (Match(TokenType.COMMA));

            Match(TokenType.SEMICOLON);
            return new AlterPortalSubscriptionStatement(id, newSchedule, setActive, newFormat, newSmtpAlias, parameters)
            { Line = start.Line, Column = start.Column };
        }

        // DROP SUBSCRIPTION <id>
        public Statement ParseDropSubscription(Token start)
        {
            int id = ParseIntLiteral("Expected subscription id");
            return new DropPortalSubscriptionStatement(id)
            { Line = start.Line, Column = start.Column };
        }

        // ── Session / Service ─────────────────────────────────────────────────

        // DISCONNECT USER 'username'
        public Statement ParseDisconnectUser(Token start)
        {
            Consume(TokenType.USER, "Expected USER");
            string username = ConsumeStringLiteral("Expected username string literal");
            return new DisconnectPortalUserStatement(username)
            { Line = start.Line, Column = start.Column };
        }

        // REVOKE TOKENS FOR USER 'username'
        public Statement ParseRevokeTokens(Token start)
        {
            Consume(TokenType.TOKENS, "Expected TOKENS");
            Consume(TokenType.FOR, "Expected FOR");
            Consume(TokenType.USER, "Expected USER");
            string username = ConsumeStringLiteral("Expected username string literal");
            return new RevokePortalTokensStatement(username)
            { Line = start.Line, Column = start.Column };
        }

        // RESTART PORTAL
        public Statement ParseRestartPortal(Token start)
        {
            Consume(TokenType.PORTAL, "Expected PORTAL");
            return new RestartPortalStatement { Line = start.Line, Column = start.Column };
        }

        // SHUTDOWN PORTAL
        public Statement ParseShutdownPortal(Token start)
        {
            Consume(TokenType.PORTAL, "Expected PORTAL");
            return new ShutdownPortalStatement { Line = start.Line, Column = start.Column };
        }

        // ── SHOW commands ─────────────────────────────────────────────────────

        // SHOW USERS | SHOW REPORTS IN FOLDER '/path' | SHOW ACTIVE SESSIONS
        public Statement ParseShowPortal(Token start)
        {
            if (Match(TokenType.USER) || MatchIdentifier("USERS"))
                return new ShowPortalUsersStatement { Line = start.Line, Column = start.Column };

            if (Match(TokenType.REPORT) || MatchIdentifier("REPORTS"))
            {
                string? folder = null;
                if (Match(TokenType.IN))
                {
                    Consume(TokenType.FOLDER, "Expected FOLDER");
                    folder = ConsumeStringLiteral("Expected folder path string literal");
                }
                return new ShowPortalReportsStatement(folder)
                { Line = start.Line, Column = start.Column };
            }

            if (Match(TokenType.ACTIVE))
            {
                ConsumeIdentifierValue("SESSIONS", "Expected SESSIONS");
                return new ShowActivePortalSessionsStatement { Line = start.Line, Column = start.Column };
            }

            throw new SyntaxException("Expected USERS, REPORTS, or ACTIVE SESSIONS after SHOW",
                start.Line, start.Column);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private string ConsumeStringLiteral(string message)
        {
            if (_parser.Current.Type == TokenType.STRING_LITERAL)
                return _parser.Advance().Value;
            throw new SyntaxException(message, _parser.Current.Line, _parser.Current.Column);
        }

        private void ConsumeIdentifierValue(string value, string message)
        {
            if (_parser.Current.Type == TokenType.IDENTIFIER &&
                _parser.Current.Value.Equals(value, StringComparison.OrdinalIgnoreCase))
            {
                _parser.Advance();
                return;
            }
            // Also allow matching as a registered keyword token
            var tok = _parser.Current;
            if (tok.Value.Equals(value, StringComparison.OrdinalIgnoreCase))
            {
                _parser.Advance();
                return;
            }
            throw new SyntaxException(message, tok.Line, tok.Column);
        }

        private void ParseOptionList(Action parseOne)
        {
            do { parseOne(); } while (Match(TokenType.COMMA));
            Consume(TokenType.RPAREN, "Expected ')'");
        }

        private PortalFolderPermission ParseFolderPermission()
        {
            var tok = _parser.Advance();
            return tok.Value.ToUpperInvariant() switch
            {
                "READ"    => PortalFolderPermission.Read,
                "EXECUTE" => PortalFolderPermission.Execute,
                "MANAGE"  => PortalFolderPermission.Manage,
                _ => throw new SyntaxException(
                    $"Expected READ, EXECUTE, or MANAGE, got '{tok.Value}'",
                    tok.Line, tok.Column)
            };
        }

        private PortalSubscriptionFormat ParseSubscriptionFormat()
        {
            var tok = _parser.Advance();
            return tok.Value.ToUpperInvariant() switch
            {
                "PDF"  => PortalSubscriptionFormat.Pdf,
                "CSV"  => PortalSubscriptionFormat.Csv,
                "BOTH" => PortalSubscriptionFormat.Both,
                _ => throw new SyntaxException(
                    $"Expected PDF, CSV, or BOTH, got '{tok.Value}'",
                    tok.Line, tok.Column)
            };
        }

        private int ParseIntLiteral(string message)
        {
            var tok = _parser.Current;
            if (tok.Type == TokenType.NUMBER && int.TryParse(tok.Value, out int id))
            {
                _parser.Advance();
                return id;
            }
            throw new SyntaxException(message, tok.Line, tok.Column);
        }

        // Parses PARAMETERS (...) if present; returns empty list otherwise.
        private IReadOnlyList<SubscriptionParameter> ParseSubscriptionParameters()
        {
            if (!MatchIdentifier("PARAMETERS"))
                return Array.Empty<SubscriptionParameter>();
            return ParseSubscriptionParameterList();
        }

        // Parses the ( @var = 'val', ... ) body. Returns empty list for PARAMETERS ().
        private IReadOnlyList<SubscriptionParameter> ParseSubscriptionParameterList()
        {
            Consume(TokenType.LPAREN, "Expected '(' after PARAMETERS");
            var result = new List<SubscriptionParameter>();
            while (_parser.Current.Type != TokenType.RPAREN && _parser.Current.Type != TokenType.EOF)
            {
                if (_parser.Current.Type != TokenType.VARIABLE)
                    throw new SyntaxException(
                        $"Expected @parameter name, got '{_parser.Current.Value}'",
                        _parser.Current.Line, _parser.Current.Column);
                string paramName = Advance().Value;
                Consume(TokenType.EQUALS, $"Expected '=' after {paramName}");
                string paramValue = ConsumeStringLiteral($"Expected string literal value for {paramName}");
                result.Add(new SubscriptionParameter(paramName, paramValue));
                if (!Match(TokenType.COMMA)) break;
            }
            Consume(TokenType.RPAREN, "Expected ')' to close PARAMETERS");
            return result;
        }
    }
}
