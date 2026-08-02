using System;
using System.Linq;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Tests.Core;
using Xunit;

namespace ETL_SQL.Tests
{
    public class PortalAdminSyntaxTests
    {
        [Fact]
        public void RefreshReport_ParsesCanonicalCommandVerb()
        {
            var script = TestHelpers.Parse("REFRESH REPORT 'Monthly Sales';");
            var stmt = Assert.IsType<RefreshPortalReportStatement>(Assert.Single(script.Statements));

            Assert.Equal("Monthly Sales", stmt.ReportName);
        }

        [Fact]
        public void TriggerRefresh_IsNotAccepted()
        {
            var script = TestHelpers.Parse("TRIGGER REFRESH FOR REPORT 'Monthly Sales';");

            Assert.Contains(script.Diagnostics, d =>
                d.Severity == DiagnosticSeverity.Error &&
                d.Message.Contains("TRIGGER", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void PortalObjectNames_RequireStringLiterals()
        {
            var script = TestHelpers.Parse(
                "CREATE USER chuck WITH (EMAIL = 'chuck@example.com', PASSWORD = @password);");

            Assert.Contains(script.Diagnostics, d =>
                d.Severity == DiagnosticSeverity.Error &&
                d.Message.Contains("string literal", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void PublishReport_RequiresStringLiteralPathsAndNames()
        {
            var script = TestHelpers.Parse(
                "PUBLISH REPORT MonthlySales FROM reports/monthly.rptsql IN FOLDER /Finance;");

            Assert.Contains(script.Diagnostics, d =>
                d.Severity == DiagnosticSeverity.Error &&
                d.Message.Contains("string literal", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void PortalPromotion_ParsesExplicitCatalogOwnership()
        {
            var script = TestHelpers.Parse("""
                CREATE FOLDER '/Finance' WITH (CATALOG_OWNER = 'alice');
                PUBLISH REPORT 'Monthly Sales' FROM 'reports/monthly.rptsql' IN FOLDER '/Finance'
                    WITH (DESCRIPTION = 'Monthly close', CATALOG_OWNER = 'alice');
                """);

            Assert.Empty(script.Diagnostics);
            var folder = Assert.IsType<CreatePortalFolderStatement>(script.Statements[0]);
            var report = Assert.IsType<PublishPortalReportStatement>(script.Statements[1]);
            Assert.Equal("alice", folder.CatalogOwner);
            Assert.Equal("alice", report.CatalogOwner);
            Assert.Contains("CATALOG_OWNER = 'alice'", folder.ToSql());
            Assert.Contains("CATALOG_OWNER = 'alice'", report.ToSql());
        }

        [Theory]
        [InlineData(
            "CREATE REFRESH JOB FOR REPORT 'Monthly Sales' SCHEDULE '0 2 * * *' AT orch;",
            "CREATE JOB")]
        [InlineData(
            "DROP REFRESH JOB FOR REPORT 'Monthly Sales';",
            "DROP JOB")]
        public void RetiredRefreshJobLanguage_IsRejectedWithItsReplacement(string sql, string expectedFix)
        {
            var script = TestHelpers.Parse(sql);

            var diagnostic = Assert.Single(script.Diagnostics);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Contains(expectedFix, diagnostic.Message, StringComparison.Ordinal);
            Assert.Empty(script.Statements);
        }

        [Fact]
        public void PortalDatasetRefresh_UsesCatalogNameAndFolder()
        {
            var script = TestHelpers.Parse("REFRESH DATASET 'Sales Summary' IN FOLDER '/Finance';");
            var stmt = Assert.IsType<RefreshPortalDatasetStatement>(Assert.Single(script.Statements));

            Assert.Equal("Sales Summary", stmt.DatasetName);
            Assert.Equal("/Finance", stmt.FolderPath);
        }

        [Fact]
        public void PortalDatasetAlter_ParsesAccessAndTtl()
        {
            var script = TestHelpers.Parse(
                "ALTER DATASET 'Sales Summary' IN FOLDER '/Finance' SET ACCESS = PUBLIC, TTL = '2h';");
            var stmt = Assert.IsType<AlterPortalDatasetStatement>(Assert.Single(script.Statements));

            Assert.Equal("Sales Summary", stmt.DatasetName);
            Assert.Equal("/Finance", stmt.FolderPath);
            Assert.Equal("Public", stmt.AccessLevel);
            Assert.Equal("2h", stmt.Ttl);
        }

        [Fact]
        public void PortalDatasetPermissions_ParseDatasetAclCommands()
        {
            var grantScript = TestHelpers.Parse(
                "GRANT EDITOR ON DATASET 'Sales Summary' IN FOLDER '/Finance' TO GROUP 'Analysts';");
            var grant = Assert.IsType<GrantPortalDatasetPermissionStatement>(Assert.Single(grantScript.Statements));

            Assert.Equal(PortalDatasetPermission.Editor, grant.Permission);
            Assert.Equal("Sales Summary", grant.DatasetName);
            Assert.Equal("/Finance", grant.FolderPath);
            Assert.Equal("Analysts", grant.GroupName);

            var revokeScript = TestHelpers.Parse(
                "REVOKE EDITOR ON DATASET 'Sales Summary' IN FOLDER '/Finance' FROM GROUP 'Analysts';");
            var revoke = Assert.IsType<RevokePortalDatasetPermissionStatement>(Assert.Single(revokeScript.Statements));

            Assert.Equal(PortalDatasetPermission.Editor, revoke.Permission);
            Assert.Equal("Sales Summary", revoke.DatasetName);
            Assert.Equal("/Finance", revoke.FolderPath);
            Assert.Equal("Analysts", revoke.GroupName);

            var refreshScript = TestHelpers.Parse(
                "GRANT REFRESH ON DATASET 'Sales Summary' IN FOLDER '/Finance' TO GROUP 'Operators';");
            var refresh = Assert.IsType<GrantPortalDatasetPermissionStatement>(
                Assert.Single(refreshScript.Statements));

            Assert.Equal(PortalDatasetPermission.Refresh, refresh.Permission);
            Assert.Equal("Operators", refresh.GroupName);
        }

        [Fact]
        public void SmtpConnection_UsesTheOrdinaryConnectorGrammar()
        {
            // SMTP is a normal connector; the Portal is addressed by the enclosing
            // EXECUTE <portal> BEGIN ... END block, not by a second statement family.
            var script = TestHelpers.Parse(
                "CREATE CONNECTION corporate AS SMTP(HOST = 'smtp.corp.local', PORT = 2525, " +
                "USERNAME = 'mailer', PASSWORD = 'SECRET:smtp_password', " +
                "DEFAULT_FROM = 'reports@corp.local', USE_SSL = FALSE);");
            var stmt = Assert.IsType<CreateConnectionStatement>(Assert.Single(script.Statements));

            Assert.Equal("corporate", stmt.ConnectionName);
            Assert.Equal("SMTP", stmt.ConnectionType, ignoreCase: true);
            Assert.NotNull(stmt.Options);
            Assert.True(stmt.Options!.ContainsKey("DEFAULT_FROM"));
            Assert.True(stmt.Options.ContainsKey("PASSWORD"));
        }

        /// <summary>
        /// The retired form must name its replacement. It differed in three ways at once — string
        /// alias vs identifier, WITH vs AS, FROM_ADDRESS vs DEFAULT_FROM — so "unexpected token"
        /// would leave the reader guessing at all three.
        /// </summary>
        [Theory]
        [InlineData(
            "CREATE SMTP CONNECTION 'corporate' WITH (HOST = 'smtp.corp.local');",
            "CREATE CONNECTION <alias> AS SMTP")]
        [InlineData(
            "DROP SMTP CONNECTION 'corporate';",
            "DROP CONNECTION [IF EXISTS] <alias>")]
        public void RetiredPortalSmtpLanguage_IsRejectedWithItsReplacement(string sql, string expectedFix)
        {
            var script = TestHelpers.Parse(sql);

            var diagnostic = Assert.Single(script.Diagnostics);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Contains(expectedFix, diagnostic.Message, StringComparison.Ordinal);
            Assert.Empty(script.Statements);
        }

        [Fact]
        public void ShowSmtpConnections_IsRejectedWithItsReplacement()
        {
            var script = TestHelpers.Parse("SHOW SMTP CONNECTIONS;");

            var diagnostic = Assert.Single(script.Diagnostics);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Contains("eng.connections", diagnostic.Message, StringComparison.Ordinal);
            Assert.Empty(script.Statements);
        }

        [Fact]
        public void PortalDatasetNames_RequireStringLiterals()
        {
            var script = TestHelpers.Parse("REFRESH DATASET SalesSummary IN FOLDER '/Finance';");

            Assert.Contains(script.Diagnostics, d =>
                d.Severity == DiagnosticSeverity.Error &&
                d.Message.Contains("&dataset", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void PortalReportAlterAndDrop_ParseCanonicalCommands()
        {
            var alterScript = TestHelpers.Parse(
                "ALTER REPORT 'Monthly Sales' SET FOLDER = '/Archive', DESCRIPTION = 'Archived';");
            var alter = Assert.IsType<AlterPortalReportStatement>(Assert.Single(alterScript.Statements));
            Assert.Equal("Monthly Sales", alter.ReportName);
            Assert.Equal("/Archive", alter.NewFolder);
            Assert.Equal("Archived", alter.NewDescription);

            var dropScript = TestHelpers.Parse("DROP REPORT 'Monthly Sales' CASCADE;");
            var drop = Assert.IsType<DropPortalReportStatement>(Assert.Single(dropScript.Statements));
            Assert.Equal("Monthly Sales", drop.ReportName);
            Assert.True(drop.Cascade);
        }

        [Fact]
        public void PortalFavorites_ParseScriptCommands()
        {
            var favoriteScript = TestHelpers.Parse("FAVORITE REPORT 'Monthly Sales';");
            var favorite = Assert.IsType<FavoritePortalReportStatement>(Assert.Single(favoriteScript.Statements));
            Assert.Equal("Monthly Sales", favorite.ReportName);
            Assert.Null(favorite.Username);

            var unfavoriteScript = TestHelpers.Parse("UNFAVORITE REPORT 'Monthly Sales' FOR USER 'chuck';");
            var unfavorite = Assert.IsType<UnfavoritePortalReportStatement>(Assert.Single(unfavoriteScript.Statements));
            Assert.Equal("Monthly Sales", unfavorite.ReportName);
            Assert.Equal("chuck", unfavorite.Username);
        }

        [Theory]
        [InlineData("SHOW REPORT HISTORY 'Monthly Sales' INTO #history;")]
        [InlineData("SHOW REPORT DEPENDENCIES 'Monthly Sales' INTO #deps;")]
        [InlineData("SHOW FAVORITES FOR USER 'chuck' LIMIT 25 INTO #favorites;")]
        [InlineData("SHOW RECENT REPORTS LIMIT 10 INTO #recent;")]
        [InlineData("SHOW CATALOG SEARCH 'sales' LIMIT 20 INTO #catalog;")]
        [InlineData("SHOW EFFECTIVE PERMISSIONS FOR REPORT 'Monthly Sales' INTO #perms;")]
        [InlineData("SHOW PORTAL USAGE METRICS FOR 30 DAYS INTO #usage;")]
        [InlineData("SHOW PORTAL OPERATIONAL METRICS INTO #ops;")]
        public void PortalShowCommands_RetiredCommands_ReturnsDiagnostics(string sql)
        {
            var script = TestHelpers.Parse(sql);
            Assert.Empty(script.Statements);
            var diag = Assert.Single(script.Diagnostics);
            Assert.Contains("retired", diag.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ValidateReportScript_ParsesIntoCapture()
        {
            var script = TestHelpers.Parse("VALIDATE REPORT SCRIPT 'reports/monthly_sales.rptsql' INTO #validation;");
            var validate = Assert.IsType<ValidatePortalReportStatement>(Assert.Single(script.Statements));

            Assert.Equal("reports/monthly_sales.rptsql", validate.ScriptPath);
            Assert.Equal("#validation", validate.IntoTable);
        }

        [Fact]
        public void PortalShareLinks_ParseCreateShowAndRevoke()
        {
            var createScript = TestHelpers.Parse(
                "CREATE SHARE LINK 'External Review' FOR REPORT 'Monthly Sales' EXPIRES '2026-12-31T23:59:59Z' INTO #share;");
            var create = Assert.IsType<CreatePortalShareLinkStatement>(Assert.Single(createScript.Statements));
            Assert.Equal("External Review", create.Name);
            Assert.Equal("Monthly Sales", create.ReportName);
            Assert.Equal("2026-12-31T23:59:59Z", create.ExpiresAt);
            Assert.Equal("#share", create.IntoTable);

            var revokeScript = TestHelpers.Parse("REVOKE SHARE LINK 'External Review' FOR REPORT 'Monthly Sales';");
            var revoke = Assert.IsType<RevokePortalShareLinkStatement>(Assert.Single(revokeScript.Statements));
            Assert.Equal("External Review", revoke.Name);
            Assert.Equal("Monthly Sales", revoke.ReportName);
        }

        [Theory]
        [InlineData(
            "CREATE SHARE LINK 'External Review' FOR REPORT 'Monthly Sales' EXPIRES '2026-12-31T23:59:59Z' INTO #share;",
            "CREATE SHARE LINK 'External Review' FOR REPORT 'Monthly Sales' EXPIRES '2026-12-31T23:59:59Z' INTO #share;")]
        [InlineData(
            "REVOKE SHARE LINK 'External Review' FOR REPORT 'Monthly Sales';",
            "REVOKE SHARE LINK 'External Review' FOR REPORT 'Monthly Sales';")]
        [InlineData(
            "CREATE EMBED TOKEN 'Intranet' FOR REPORT 'Monthly Sales' EXPIRES '2026-12-31T23:59:59Z' INTO #embed;",
            "CREATE EMBED TOKEN 'Intranet' FOR REPORT 'Monthly Sales' EXPIRES '2026-12-31T23:59:59Z' INTO #embed;")]
        [InlineData(
            "REVOKE EMBED TOKEN 'Intranet' FOR REPORT 'Monthly Sales';",
            "REVOKE EMBED TOKEN 'Intranet' FOR REPORT 'Monthly Sales';")]
        public void PortalAnonymousAccessStatements_RoundTripWithCanonicalExpires(string sql, string expected)
        {
            var script = TestHelpers.Parse(sql);
            var statement = Assert.Single(script.Statements);

            var serialized = statement.ToSql();
            Assert.Equal(expected, serialized);
            Assert.DoesNotContain("UNKNOWN STATEMENT", serialized, StringComparison.Ordinal);

            var reparsed = TestHelpers.Parse(serialized);
            Assert.Empty(reparsed.Diagnostics);
            Assert.Equal(serialized, Assert.Single(reparsed.Statements).ToSql());
        }

        [Theory]
        [InlineData(
            "CREATE SHARE LINK FOR REPORT 'Monthly Sales' EXPIRES '2026-12-31T23:59:59Z';",
            "CREATE SHARE LINK '<name>' FOR REPORT")]
        [InlineData(
            "CREATE EMBED TOKEN FOR REPORT 'Monthly Sales' NAME 'Intranet';",
            "CREATE EMBED TOKEN '<name>' FOR REPORT")]
        public void PortalAnonymousAccessCreate_RequiresNamedResources(string sql, string expectedFix)
        {
            var script = TestHelpers.Parse(sql);

            var diagnostic = Assert.Single(script.Diagnostics);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Contains(expectedFix, diagnostic.Message, StringComparison.Ordinal);
            Assert.Empty(script.Statements);
        }

        [Theory]
        [InlineData(
            "CREATE SHARE LINK 'External Review' FOR REPORT 'Monthly Sales' EXPIRES_AT '2026-12-31T23:59:59Z';",
            "EXPIRES '<timestamp>'")]
        [InlineData(
            "CREATE EMBED TOKEN 'Intranet' FOR REPORT 'Monthly Sales' EXPIRES_AT '2026-12-31T23:59:59Z';",
            "EXPIRES '<timestamp>'")]
        public void PortalAnonymousAccessCreate_RejectsExpiresAtAlias(string sql, string expectedFix)
        {
            var script = TestHelpers.Parse(sql);

            var diagnostic = Assert.Single(script.Diagnostics);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Contains(expectedFix, diagnostic.Message, StringComparison.Ordinal);
            Assert.Empty(script.Statements);
        }

        [Fact]
        public void PortalAnonymousAccess_KeywordInventoryOnlyAdvertisesExpires()
        {
            Assert.Contains("EXPIRES", LanguageMetadata.Keywords);
            Assert.DoesNotContain("EXPIRES_AT", LanguageMetadata.Keywords);
        }

        [Fact]
        public void PortalEmbedTokensSavedViewsAndAlerts_ParseScriptCommands()
        {
            var embedScript = TestHelpers.Parse(
                "CREATE EMBED TOKEN 'Intranet' FOR REPORT 'Monthly Sales' EXPIRES '2026-12-31T23:59:59Z' INTO #embed;");
            var embed = Assert.IsType<CreatePortalEmbedTokenStatement>(Assert.Single(embedScript.Statements));
            Assert.Equal("Monthly Sales", embed.ReportName);
            Assert.Equal("Intranet", embed.Name);
            Assert.Equal("#embed", embed.IntoTable);

            var savedScript = TestHelpers.Parse(
                "CREATE SAVED VIEW 'West Coast' FOR REPORT 'Monthly Sales' DEFAULT PARAMETERS (@region = 'West') INTO #view;");
            var saved = Assert.IsType<CreatePortalSavedViewStatement>(Assert.Single(savedScript.Statements));
            Assert.Equal("Monthly Sales", saved.ReportName);
            Assert.Equal("West Coast", saved.Name);
            Assert.True(saved.IsDefault);
            Assert.Single(saved.Parameters);

            var alertScript = TestHelpers.Parse(
                "CREATE OR REPLACE ALERT RevenueFloor FOR REPORT 'Monthly Sales' WHEN VISUAL Revenue >= 1000 WITH (DESCRIPTION = 'Revenue floor');");
            var alert = Assert.IsType<CreatePortalAlertStatement>(Assert.Single(alertScript.Statements));
            Assert.Equal("Monthly Sales", alert.ReportName);
            Assert.Equal("RevenueFloor", alert.Name);
            Assert.Equal("Revenue", alert.VisualName);
            Assert.Equal(">=", alert.Operator);
            Assert.Equal(1000m, alert.Threshold);
            Assert.Equal("Revenue floor", alert.Metadata.Description);
            Assert.Equal(ObjectCreationMode.CreateOrReplace, alert.Mode);

            var linkScript = TestHelpers.Parse(
                "ALTER ALERT RevenueFloor ADD NOTIFICATION orch_admin.FinanceOps;");
            var link = Assert.IsType<AlterPortalAlertNotificationStatement>(
                Assert.Single(linkScript.Statements));
            Assert.Equal("RevenueFloor", link.AlertName);
            Assert.Equal(PortalAlertAttachmentAction.Add, link.Action);
            Assert.Equal("orch_admin", link.Notification.OrchestratorAlias);
            Assert.Equal("FinanceOps", link.Notification.NotificationName);

            var disabledAlertScript = TestHelpers.Parse("DISABLE ALERT RevenueFloor;");
            var disabledAlert = Assert.IsType<SetPortalAlertEnabledStatement>(
                Assert.Single(disabledAlertScript.Statements));
            Assert.False(disabledAlert.IsEnabled);
        }

        [Fact]
        public void PortalEmbedTokensSavedViewsAndAlerts_ParseShowDropAndRevoke()
        {
            var revokeEmbedScript = TestHelpers.Parse("REVOKE EMBED TOKEN 'Intranet' FOR REPORT 'Monthly Sales';");
            var revokeEmbed = Assert.IsType<RevokePortalEmbedTokenStatement>(Assert.Single(revokeEmbedScript.Statements));
            Assert.Equal("Intranet", revokeEmbed.Name);
            Assert.Equal("Monthly Sales", revokeEmbed.ReportName);

            var dropViewScript = TestHelpers.Parse("DROP SAVED VIEW 'West Coast' FOR REPORT 'Monthly Sales';");
            var dropView = Assert.IsType<DropPortalSavedViewStatement>(Assert.Single(dropViewScript.Statements));
            Assert.Equal("West Coast", dropView.Name);

            var dropAlertScript = TestHelpers.Parse("DROP ALERT IF EXISTS RevenueFloor;");
            var dropAlert = Assert.IsType<DropPortalAlertStatement>(Assert.Single(dropAlertScript.Statements));
            Assert.Equal("RevenueFloor", dropAlert.Name);
            Assert.True(dropAlert.IfExists);
        }

        [Fact]
        public void RetiredInlineAlertDelivery_ReportsReplacement()
        {
            var script = TestHelpers.Parse(
                "CREATE ALERT 'Revenue Floor' FOR REPORT 'Monthly Sales' WHEN VISUAL 'Revenue' >= 1000 DELIVER TO 'ops@example.com' AT smtp;");
            var diagnostic = Assert.Single(script.Diagnostics);

            Assert.Contains("ALTER ALERT", diagnostic.Message);
            Assert.Contains("ADD NOTIFICATION", diagnostic.Message);
        }

        [Fact]
        public void PortalPromotionPattern_ParsesSetsPublishAlterGrantsAndRefresh()
        {
            var script = TestHelpers.Parse(@"
DECLARE @PortalEnvironment STRING = 'PROD';

CREATE SETS !PROD
BEGIN
    @PortalEnvironment = 'PROD';
    SET WITH_PROMPT ON;
END;

USE SETS !PROD;

CREATE FOLDER '/Finance';
GRANT EXECUTE ON FOLDER '/Finance' TO GROUP 'FinanceAnalysts';

PUBLISH REPORT 'Monthly Sales'
    FROM 'reports/prod/monthly_sales.rptsql'
    IN FOLDER '/Finance'
    WITH (DESCRIPTION = 'Certified monthly revenue by region');

ALTER REPORT 'Monthly Sales'
    SET DESCRIPTION = 'Certified monthly revenue by region';

CREATE SCHEDULE MonthlySalesMorning ON '0 6 * * *';
CREATE JOB MonthlySalesRefresh FOR REPORT '/Finance/Monthly Sales';
ALTER JOB MonthlySalesRefresh ADD SCHEDULE MonthlySalesMorning;
REFRESH REPORT 'Monthly Sales';");

            Assert.DoesNotContain(script.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
            Assert.Contains(script.Statements, s => s is CreateSetsStatement);
            Assert.Contains(script.Statements, s => s is UseSetsStatement);
            Assert.Contains(script.Statements, s => s is CreatePortalFolderStatement);
            Assert.Contains(script.Statements, s => s is GrantPortalPermissionStatement);
            Assert.Contains(script.Statements, s => s is PublishPortalReportStatement);
            Assert.Contains(script.Statements, s => s is AlterPortalReportStatement);
            Assert.Contains(script.Statements, s => s is CreateScheduleStatement);
            Assert.Contains(script.Statements, s => s is CreateJobStatement);
            Assert.Contains(script.Statements, s => s is AlterJobAttachmentStatement);
            Assert.Contains(script.Statements, s => s is RefreshPortalReportStatement);
        }
    }
}
