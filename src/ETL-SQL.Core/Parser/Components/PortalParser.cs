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

        // ALTER FOLDER '/path' RENAME TO 'new-name' | SET PARENT = '/new-parent'
        public Statement ParseAlterFolder(Token start)
        {
            string path = ConsumeStringLiteral("Expected folder path string literal");
            string? newName = null, newParentPath = null;
            do
            {
                if (MatchIdentifier("RENAME"))
                {
                    Consume(TokenType.TO, "Expected TO after RENAME");
                    newName = ConsumeStringLiteral("Expected new folder name string literal");
                }
                else if (Match(TokenType.SET))
                {
                    string key = Advance().Value.ToUpperInvariant();
                    Consume(TokenType.EQUALS, "Expected '='");
                    if (key == "PARENT")
                        newParentPath = ConsumeStringLiteral("Expected parent path string literal");
                    else
                        throw new SyntaxException($"Unknown ALTER FOLDER option: {key}", _parser.Current.Line, _parser.Current.Column);
                }
                else break;
            } while (Match(TokenType.COMMA));
            Match(TokenType.SEMICOLON);
            return new AlterPortalFolderStatement(path, newName, newParentPath)
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
        // GRANT VIEWER|EDITOR|OWNER ON DATASET 'name' IN FOLDER '/path' TO GROUP 'name'
        public Statement ParseGrant(Token start)
        {
            var permissionToken = _parser.Advance();
            Consume(TokenType.ON, "Expected ON");

            if (Match(TokenType.FOLDER))
            {
                var perm = ParseFolderPermission(permissionToken);
                string path = ConsumeStringLiteral("Expected folder path string literal");
                Consume(TokenType.TO, "Expected TO");
                Consume(TokenType.GROUP, "Expected GROUP");
                string group = ConsumeStringLiteral("Expected group name string literal");
                return new GrantPortalPermissionStatement(path, group, perm)
                { Line = start.Line, Column = start.Column };
            }

            if (Match(TokenType.DATASET))
            {
                var perm = ParseDatasetPermission(permissionToken);
                var dataset = ParsePortalDatasetReference();
                Consume(TokenType.TO, "Expected TO");
                Consume(TokenType.GROUP, "Expected GROUP");
                string group = ConsumeStringLiteral("Expected group name string literal");
                return new GrantPortalDatasetPermissionStatement(dataset.Name, dataset.FolderPath, group, perm)
                { Line = start.Line, Column = start.Column };
            }

            throw new SyntaxException("Expected FOLDER or DATASET after ON", _parser.Current.Line, _parser.Current.Column);
        }

        // REVOKE READ|EXECUTE|MANAGE ON FOLDER '/path' FROM GROUP 'name'
        // REVOKE VIEWER|EDITOR|OWNER ON DATASET 'name' IN FOLDER '/path' FROM GROUP 'name'
        public Statement ParseRevoke(Token start)
        {
            if (Match(TokenType.SHARE))
            {
                Consume(TokenType.LINK, "Expected LINK after REVOKE SHARE");
                string token = ConsumeStringLiteral("Expected share-link token string literal");
                Match(TokenType.SEMICOLON);
                return new RevokePortalShareLinkStatement(token)
                { Line = start.Line, Column = start.Column };
            }

            if (Match(TokenType.EMBED))
            {
                Consume(TokenType.TOKEN, "Expected TOKEN after REVOKE EMBED");
                string token = ConsumeStringLiteral("Expected embed token string literal");
                Match(TokenType.SEMICOLON);
                return new RevokePortalEmbedTokenStatement(token)
                { Line = start.Line, Column = start.Column };
            }

            if (Match(TokenType.TOKENS))
            {
                Consume(TokenType.FOR, "Expected FOR");
                Consume(TokenType.USER, "Expected USER");
                string username = ConsumeStringLiteral("Expected username string literal");
                Match(TokenType.SEMICOLON);
                return new RevokePortalTokensStatement(username)
                { Line = start.Line, Column = start.Column };
            }

            var permissionToken = _parser.Advance();
            Consume(TokenType.ON, "Expected ON");

            if (Match(TokenType.FOLDER))
            {
                var perm = ParseFolderPermission(permissionToken);
                string path = ConsumeStringLiteral("Expected folder path string literal");
                Consume(TokenType.FROM, "Expected FROM");
                Consume(TokenType.GROUP, "Expected GROUP");
                string group = ConsumeStringLiteral("Expected group name string literal");
                return new RevokePortalPermissionStatement(path, group, perm)
                { Line = start.Line, Column = start.Column };
            }

            if (Match(TokenType.DATASET))
            {
                var perm = ParseDatasetPermission(permissionToken);
                var dataset = ParsePortalDatasetReference();
                Consume(TokenType.FROM, "Expected FROM");
                Consume(TokenType.GROUP, "Expected GROUP");
                string group = ConsumeStringLiteral("Expected group name string literal");
                return new RevokePortalDatasetPermissionStatement(dataset.Name, dataset.FolderPath, group, perm)
                { Line = start.Line, Column = start.Column };
            }

            throw new SyntaxException("Expected FOLDER or DATASET after ON", _parser.Current.Line, _parser.Current.Column);
        }

        // ALTER DATASET 'name' IN FOLDER '/path' SET ACCESS = PUBLIC|PRIVATE, TTL = '2h'
        public Statement ParseAlterDataset(Token start)
        {
            var dataset = ParsePortalDatasetReference();
            Consume(TokenType.SET, "Expected SET");

            string? accessLevel = null;
            string? ttl = null;
            do
            {
                string key = Advance().Value;
                Consume(TokenType.EQUALS, "Expected '='");
                switch (key.ToUpperInvariant())
                {
                    case "ACCESS":
                    case "ACCESS_LEVEL":
                        accessLevel = ParsePortalDatasetAccessLevel();
                        break;
                    case "TTL":
                        ttl = ConsumeStringLiteral("Expected TTL string literal");
                        break;
                    default:
                        ParseExpression();
                        break;
                }
            } while (Match(TokenType.COMMA));

            Match(TokenType.SEMICOLON);
            return new AlterPortalDatasetStatement(dataset.Name, dataset.FolderPath, accessLevel, ttl)
            { Line = start.Line, Column = start.Column };
        }

        // REFRESH DATASET 'name' IN FOLDER '/path'
        public Statement ParseRefreshDataset(Token start)
        {
            var dataset = ParsePortalDatasetReference();
            Match(TokenType.SEMICOLON);
            return new RefreshPortalDatasetStatement(dataset.Name, dataset.FolderPath)
            { Line = start.Line, Column = start.Column };
        }

        // DROP DATASET 'name' IN FOLDER '/path'
        public Statement ParseDropDataset(Token start)
        {
            var dataset = ParsePortalDatasetReference();
            Match(TokenType.SEMICOLON);
            return new DropPortalDatasetStatement(dataset.Name, dataset.FolderPath)
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
            string name = ConsumeStringLiteral("Expected report name string literal");
            bool cascade = MatchIdentifier("CASCADE");
            return new DropPortalReportStatement(name, cascade)
            { Line = start.Line, Column = start.Column };
        }

        // FAVORITE REPORT 'name' [FOR USER 'username']
        // UNFAVORITE REPORT 'name' [FOR USER 'username']
        public Statement ParseFavoriteReport(Token start, bool favorite)
        {
            Consume(TokenType.REPORT, "Expected REPORT");
            string report = ConsumeStringLiteral("Expected report name string literal");
            string? username = null;
            if (Match(TokenType.FOR))
            {
                Consume(TokenType.USER, "Expected USER");
                username = ConsumeStringLiteral("Expected username string literal");
            }

            Match(TokenType.SEMICOLON);
            Statement stmt = favorite
                ? new FavoritePortalReportStatement(report, username)
                : new UnfavoritePortalReportStatement(report, username);
            return stmt with { Line = start.Line, Column = start.Column };
        }

        // VALIDATE REPORT SCRIPT 'path' [INTO #validation]
        public Statement ParseValidateReport(Token start)
        {
            Consume(TokenType.REPORT, "Expected REPORT");
            Consume(TokenType.SCRIPT, "Expected SCRIPT");
            string scriptPath = ConsumeStringLiteral("Expected report script path string literal");
            string? intoTable = null;
            if (Match(TokenType.INTO))
                intoTable = ConsumeTempTableName("VALIDATE REPORT SCRIPT ... INTO target must be a temporary table starting with '#'");
            Match(TokenType.SEMICOLON);
            return new ValidatePortalReportStatement(scriptPath, intoTable)
            { Line = start.Line, Column = start.Column };
        }

        // PUBLISH BUNDLE 'name' FROM 'sourcePath' ENTRY 'entryPath' [WITH (...)]
        public Statement ParsePublishBundle(Token start)
        {
            string bundleName = ConsumeStringLiteral("Expected bundle name string literal");
            ConsumeIdentifierValue("FROM", "Expected FROM");
            Expression sourcePath = ParseExpression();
            ConsumeIdentifierValue("ENTRY", "Expected ENTRY");
            string entryPath = ConsumeStringLiteral("Expected entry path string literal");

            BundleSecretMode passwordMode = BundleSecretMode.None;
            string? password = null;
            string encryptionMode = "MACHINE";
            string? keyFile = null;
            string? description = null;

            if (Match(TokenType.WITH))
            {
                Consume(TokenType.LPAREN, "Expected '('");
                ParseOptionList(() =>
                {
                    string key = Advance().Value;
                    Consume(TokenType.EQUALS, "Expected '='");
                    switch (key.ToUpperInvariant())
                    {
                        case "PASSWORD":
                            if (MatchIdentifier("PROMPT"))
                            {
                                passwordMode = BundleSecretMode.Prompt;
                            }
                            else
                            {
                                passwordMode = BundleSecretMode.Literal;
                                password = ConsumeStringLiteral("Expected password string literal");
                            }
                            break;
                        case "ENCRYPT":
                            if (_parser.Current.Type == TokenType.IDENTIFIER)
                                encryptionMode = Advance().Value;
                            else
                                encryptionMode = ConsumeStringLiteral("Expected encryption mode");
                            break;
                        case "KEYFILE":
                            keyFile = ConsumeStringLiteral("Expected keyfile path string literal");
                            break;
                        case "DESCRIPTION":
                            description = ConsumeStringLiteral("Expected description string literal");
                            break;
                        default:
                            ParseExpression();
                            break;
                    }
                });
            }

            return new PublishBundleStatement(
                bundleName,
                sourcePath,
                entryPath,
                passwordMode,
                password,
                encryptionMode,
                keyFile,
                description)
            {
                Line = start.Line,
                Column = start.Column
            };
        }

        // VALIDATE BUNDLE 'name' FROM 'sourcePath' ENTRY 'entryPath' [WITH (...)]
        public Statement ParseValidateBundle(Token start)
        {
            string bundleName = ConsumeStringLiteral("Expected bundle name string literal");
            ConsumeIdentifierValue("FROM", "Expected FROM");
            Expression sourcePath = ParseExpression();
            ConsumeIdentifierValue("ENTRY", "Expected ENTRY");
            string entryPath = ConsumeStringLiteral("Expected entry path string literal");

            BundleSecretMode passwordMode = BundleSecretMode.None;
            string? password = null;

            if (Match(TokenType.WITH))
            {
                Consume(TokenType.LPAREN, "Expected '('");
                ParseOptionList(() =>
                {
                    string key = Advance().Value;
                    Consume(TokenType.EQUALS, "Expected '='");
                    switch (key.ToUpperInvariant())
                    {
                        case "PASSWORD":
                            if (MatchIdentifier("PROMPT"))
                            {
                                passwordMode = BundleSecretMode.Prompt;
                            }
                            else
                            {
                                passwordMode = BundleSecretMode.Literal;
                                password = ConsumeStringLiteral("Expected password string literal");
                            }
                            break;
                        default:
                            ParseExpression();
                            break;
                    }
                });
            }

            return new ValidateBundleStatement(
                bundleName,
                sourcePath,
                entryPath,
                passwordMode,
                password)
            {
                Line = start.Line,
                Column = start.Column
            };
        }

        // CREATE SHARE LINK FOR REPORT 'name' [EXPIRES '2026-12-31T23:59:59Z'] [INTO #share]
        public Statement ParseCreateShareLink(Token start)
        {
            Consume(TokenType.LINK, "Expected LINK after CREATE SHARE");
            Consume(TokenType.FOR, "Expected FOR");
            Consume(TokenType.REPORT, "Expected REPORT");
            string report = ConsumeStringLiteral("Expected report name string literal");
            string? expiresAt = null;
            if (Match(TokenType.EXPIRES))
                expiresAt = ConsumeStringLiteral("Expected expiration timestamp string literal");
            string? intoTable = null;
            if (Match(TokenType.INTO))
                intoTable = ConsumeTempTableName("CREATE SHARE LINK ... INTO target must be a temporary table starting with '#'");

            Match(TokenType.SEMICOLON);
            return new CreatePortalShareLinkStatement(report, expiresAt, intoTable)
            { Line = start.Line, Column = start.Column };
        }

        // CREATE EMBED TOKEN FOR REPORT 'name' [NAME 'label'] [EXPIRES 'timestamp'] [INTO #embed]
        public Statement ParseCreateEmbedToken(Token start)
        {
            Consume(TokenType.TOKEN, "Expected TOKEN after CREATE EMBED");
            Consume(TokenType.FOR, "Expected FOR");
            Consume(TokenType.REPORT, "Expected REPORT");
            string report = ConsumeStringLiteral("Expected report name string literal");
            string? name = null;
            string? expiresAt = null;
            if (MatchIdentifier("NAME"))
                name = ConsumeStringLiteral("Expected embed token name string literal");
            if (Match(TokenType.EXPIRES))
                expiresAt = ConsumeStringLiteral("Expected expiration timestamp string literal");
            string? intoTable = null;
            if (Match(TokenType.INTO))
                intoTable = ConsumeTempTableName("CREATE EMBED TOKEN ... INTO target must be a temporary table starting with '#'");
            Match(TokenType.SEMICOLON);
            return new CreatePortalEmbedTokenStatement(report, name, expiresAt, intoTable)
            { Line = start.Line, Column = start.Column };
        }

        // CREATE SAVED VIEW 'name' FOR REPORT 'report' [DEFAULT] [PARAMETERS (...)] [INTO #view]
        public Statement ParseCreateSavedView(Token start)
        {
            Consume(TokenType.VIEW, "Expected VIEW after CREATE SAVED");
            string name = ConsumeStringLiteral("Expected saved view name string literal");
            Consume(TokenType.FOR, "Expected FOR");
            Consume(TokenType.REPORT, "Expected REPORT");
            string report = ConsumeStringLiteral("Expected report name string literal");
            bool isDefault = Match(TokenType.DEFAULT);
            var parameters = MatchIdentifier("PARAMETERS")
                ? ParseSubscriptionParameterList()
                : new List<SubscriptionParameter>();
            string? intoTable = null;
            if (Match(TokenType.INTO))
                intoTable = ConsumeTempTableName("CREATE SAVED VIEW ... INTO target must be a temporary table starting with '#'");
            Match(TokenType.SEMICOLON);
            return new CreatePortalSavedViewStatement(report, name, parameters, isDefault, intoTable)
            { Line = start.Line, Column = start.Column };
        }

        // CREATE ALERT 'name' FOR REPORT 'report' WHEN VISUAL 'Card' >= 100 [DELIVER TO 'addr'] [AT smtp]
        public Statement ParseCreateAlert(Token start)
        {
            string name = ConsumeStringLiteral("Expected alert name string literal");
            Consume(TokenType.FOR, "Expected FOR");
            Consume(TokenType.REPORT, "Expected REPORT");
            string report = ConsumeStringLiteral("Expected report name string literal");
            Consume(TokenType.WHEN, "Expected WHEN");
            Consume(TokenType.VISUAL, "Expected VISUAL");
            string visual = ConsumeStringLiteral("Expected visual name string literal");
            string op = Advance().Value;
            if (op is not (">" or ">=" or "<" or "<=" or "=" or "!=" or "<>"))
                throw new SyntaxException("Expected alert comparison operator", _parser.Previous.Line, _parser.Previous.Column);
            decimal threshold = ParseDecimalLiteral("Expected numeric alert threshold");
            string? recipient = null;
            string? smtpAlias = null;
            if (MatchIdentifier("DELIVER"))
            {
                Consume(TokenType.TO, "Expected TO");
                recipient = ConsumeStringLiteral("Expected alert recipient string literal");
            }
            if (Match(TokenType.AT))
                smtpAlias = Advance().Value;
            Match(TokenType.SEMICOLON);
            return new CreatePortalAlertStatement(report, name, visual, op == "<>" ? "!=" : op, threshold, recipient, smtpAlias)
            { Line = start.Line, Column = start.Column };
        }

        // DROP SAVED VIEW 'name' FOR REPORT 'report'
        public Statement ParseDropSavedView(Token start)
        {
            Consume(TokenType.VIEW, "Expected VIEW after DROP SAVED");
            string name = ConsumeStringLiteral("Expected saved view name string literal");
            Consume(TokenType.FOR, "Expected FOR");
            Consume(TokenType.REPORT, "Expected REPORT");
            string report = ConsumeStringLiteral("Expected report name string literal");
            Match(TokenType.SEMICOLON);
            return new DropPortalSavedViewStatement(report, name)
            { Line = start.Line, Column = start.Column };
        }

        // DROP ALERT 'name' FOR REPORT 'report'
        public Statement ParseDropAlert(Token start)
        {
            string name = ConsumeStringLiteral("Expected alert name string literal");
            Consume(TokenType.FOR, "Expected FOR");
            Consume(TokenType.REPORT, "Expected REPORT");
            string report = ConsumeStringLiteral("Expected report name string literal");
            Match(TokenType.SEMICOLON);
            return new DropPortalAlertStatement(report, name)
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

        private string ConsumeTempTableName(string message)
        {
            var tempTable = ConsumeIdentifier("Expected temporary table name after INTO").Value;
            if (!tempTable.StartsWith("#"))
                throw new SyntaxException(message, _parser.Previous.Line, _parser.Previous.Column);
            return tempTable;
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

        private (string Name, string FolderPath) ParsePortalDatasetReference()
        {
            string name = ConsumeStringLiteral("Expected dataset name string literal");
            Consume(TokenType.IN, "Expected IN");
            Consume(TokenType.FOLDER, "Expected FOLDER");
            string folder = ConsumeStringLiteral("Expected folder path string literal");
            return (name, folder);
        }

        private PortalFolderPermission ParseFolderPermission(Token tok)
        {
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

        private PortalDatasetPermission ParseDatasetPermission(Token tok)
        {
            return tok.Value.ToUpperInvariant() switch
            {
                "VIEWER" => PortalDatasetPermission.Viewer,
                "EDITOR" => PortalDatasetPermission.Editor,
                "OWNER"  => PortalDatasetPermission.Owner,
                _ => throw new SyntaxException(
                    $"Expected VIEWER, EDITOR, or OWNER, got '{tok.Value}'",
                    tok.Line, tok.Column)
            };
        }

        private string ParsePortalDatasetAccessLevel()
        {
            var tok = _parser.Advance();
            return tok.Value.ToUpperInvariant() switch
            {
                "PUBLIC"  => "Public",
                "PRIVATE" => "Private",
                _ => throw new SyntaxException(
                    $"Expected PUBLIC or PRIVATE, got '{tok.Value}'",
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

        private decimal ParseDecimalLiteral(string message)
        {
            var tok = _parser.Current;
            if (tok.Type == TokenType.NUMBER && decimal.TryParse(tok.Value, out var value))
            {
                _parser.Advance();
                return value;
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
