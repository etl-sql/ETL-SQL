using System;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
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
        public void PortalAliases_RemainIdentifiers()
        {
            var script = TestHelpers.Parse(
                "CREATE REFRESH JOB FOR REPORT 'Monthly Sales' SCHEDULE '0 2 * * *' AT orch;");
            var stmt = Assert.IsType<CreatePortalRefreshJobStatement>(Assert.Single(script.Statements));

            Assert.Equal("orch", stmt.OrchestratorAlias);
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
        public void ShowSmtpConnections_StillParses()
        {
            var show = TestHelpers.Parse("SHOW SMTP CONNECTIONS;");
            var showStmt = Assert.IsType<ShowPortalSmtpConnectionsStatement>(Assert.Single(show.Statements));
            Assert.Null(showStmt.IntoTable);

            var showInto = TestHelpers.Parse("SHOW SMTP CONNECTIONS INTO #smtp;");
            var showIntoStmt = Assert.IsType<ShowPortalSmtpConnectionsStatement>(Assert.Single(showInto.Statements));
            Assert.Equal("#smtp", showIntoStmt.IntoTable);
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

        [Fact]
        public void PortalShowCommands_ParseHistoryDependenciesAndFavorites()
        {
            var historyScript = TestHelpers.Parse("SHOW REPORT HISTORY 'Monthly Sales' INTO #history;");
            var history = Assert.IsType<ShowPortalReportHistoryStatement>(Assert.Single(historyScript.Statements));
            Assert.Equal("Monthly Sales", history.ReportName);
            Assert.Equal("#history", history.IntoTable);

            var dependenciesScript = TestHelpers.Parse("SHOW REPORT DEPENDENCIES 'Monthly Sales' INTO #deps;");
            var dependencies = Assert.IsType<ShowPortalReportDependenciesStatement>(Assert.Single(dependenciesScript.Statements));
            Assert.Equal("Monthly Sales", dependencies.ReportName);
            Assert.Equal("#deps", dependencies.IntoTable);

            var favoritesScript = TestHelpers.Parse("SHOW FAVORITES FOR USER 'chuck' LIMIT 25 INTO #favorites;");
            var favorites = Assert.IsType<ShowPortalFavoritesStatement>(Assert.Single(favoritesScript.Statements));
            Assert.Equal("chuck", favorites.Username);
            Assert.Equal(25, favorites.Limit);
            Assert.Equal("#favorites", favorites.IntoTable);
        }

        [Fact]
        public void PortalShowCommands_ParseCatalogPermissionsAndUsage()
        {
            var recentScript = TestHelpers.Parse("SHOW RECENT REPORTS LIMIT 10 INTO #recent;");
            var recent = Assert.IsType<ShowPortalRecentReportsStatement>(Assert.Single(recentScript.Statements));
            Assert.Equal(10, recent.Limit);
            Assert.Equal("#recent", recent.IntoTable);

            var searchScript = TestHelpers.Parse("SHOW CATALOG SEARCH 'sales' LIMIT 20 INTO #catalog;");
            var search = Assert.IsType<SearchPortalCatalogStatement>(Assert.Single(searchScript.Statements));
            Assert.Equal("sales", search.Query);
            Assert.Equal(20, search.Limit);
            Assert.Equal("#catalog", search.IntoTable);

            var permissionsScript = TestHelpers.Parse("SHOW EFFECTIVE PERMISSIONS FOR REPORT 'Monthly Sales' INTO #perms;");
            var permissions = Assert.IsType<ShowEffectivePortalPermissionsStatement>(Assert.Single(permissionsScript.Statements));
            Assert.Equal("REPORT", permissions.TargetType);
            Assert.Equal("Monthly Sales", permissions.Target);
            Assert.Equal("#perms", permissions.IntoTable);

            var usageScript = TestHelpers.Parse("SHOW PORTAL USAGE METRICS FOR 30 DAYS INTO #usage;");
            var usage = Assert.IsType<ShowPortalUsageMetricsStatement>(Assert.Single(usageScript.Statements));
            Assert.Equal(30, usage.Days);
            Assert.Equal("#usage", usage.IntoTable);

            var operationalScript = TestHelpers.Parse("SHOW PORTAL OPERATIONAL METRICS INTO #ops;");
            var operational = Assert.IsType<ShowPortalOperationalMetricsStatement>(Assert.Single(operationalScript.Statements));
            Assert.Equal("#ops", operational.IntoTable);
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
                "CREATE SHARE LINK FOR REPORT 'Monthly Sales' EXPIRES '2026-12-31T23:59:59Z' INTO #share;");
            var create = Assert.IsType<CreatePortalShareLinkStatement>(Assert.Single(createScript.Statements));
            Assert.Equal("Monthly Sales", create.ReportName);
            Assert.Equal("2026-12-31T23:59:59Z", create.ExpiresAt);
            Assert.Equal("#share", create.IntoTable);

            var showScript = TestHelpers.Parse("SHOW SHARE LINKS FOR REPORT 'Monthly Sales' INTO #shares;");
            var show = Assert.IsType<ShowPortalShareLinksStatement>(Assert.Single(showScript.Statements));
            Assert.Equal("Monthly Sales", show.ReportName);
            Assert.Equal("#shares", show.IntoTable);

            var revokeScript = TestHelpers.Parse("REVOKE SHARE LINK 'abc123';");
            var revoke = Assert.IsType<RevokePortalShareLinkStatement>(Assert.Single(revokeScript.Statements));
            Assert.Equal("abc123", revoke.Token);
        }

        [Fact]
        public void PortalEmbedTokensSavedViewsAndAlerts_ParseScriptCommands()
        {
            var embedScript = TestHelpers.Parse(
                "CREATE EMBED TOKEN FOR REPORT 'Monthly Sales' NAME 'Intranet' EXPIRES '2026-12-31T23:59:59Z' INTO #embed;");
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
                "CREATE ALERT 'Revenue Floor' FOR REPORT 'Monthly Sales' WHEN VISUAL 'Revenue' >= 1000 DELIVER TO 'ops@example.com' AT smtp;");
            var alert = Assert.IsType<CreatePortalAlertStatement>(Assert.Single(alertScript.Statements));
            Assert.Equal("Monthly Sales", alert.ReportName);
            Assert.Equal("Revenue Floor", alert.Name);
            Assert.Equal("Revenue", alert.VisualName);
            Assert.Equal(">=", alert.Operator);
            Assert.Equal(1000m, alert.Threshold);
            Assert.Equal("ops@example.com", alert.Recipient);
            Assert.Equal("smtp", alert.SmtpAlias);

            var disabledAlertScript = TestHelpers.Parse(
                "CREATE ALERT 'Disabled Alert' FOR REPORT 'Monthly Sales' " +
                "WHEN VISUAL 'Revenue' >= 1000 AT smtp DISABLE;");
            var disabledAlert = Assert.IsType<CreatePortalAlertStatement>(
                Assert.Single(disabledAlertScript.Statements));
            Assert.False(disabledAlert.IsActive);
        }

        [Fact]
        public void PortalEmbedTokensSavedViewsAndAlerts_ParseShowDropAndRevoke()
        {
            var showEmbedScript = TestHelpers.Parse("SHOW EMBED TOKENS FOR REPORT 'Monthly Sales' INTO #embed;");
            var showEmbed = Assert.IsType<ShowPortalEmbedTokensStatement>(Assert.Single(showEmbedScript.Statements));
            Assert.Equal("#embed", showEmbed.IntoTable);

            var showViewsScript = TestHelpers.Parse("SHOW SAVED VIEWS FOR REPORT 'Monthly Sales' INTO #views;");
            var showViews = Assert.IsType<ShowPortalSavedViewsStatement>(Assert.Single(showViewsScript.Statements));
            Assert.Equal("#views", showViews.IntoTable);

            var showAlertsScript = TestHelpers.Parse("SHOW ALERTS FOR REPORT 'Monthly Sales' INTO #alerts;");
            var showAlerts = Assert.IsType<ShowPortalAlertsStatement>(Assert.Single(showAlertsScript.Statements));
            Assert.Equal("#alerts", showAlerts.IntoTable);

            var revokeEmbedScript = TestHelpers.Parse("REVOKE EMBED TOKEN 'embed123';");
            var revokeEmbed = Assert.IsType<RevokePortalEmbedTokenStatement>(Assert.Single(revokeEmbedScript.Statements));
            Assert.Equal("embed123", revokeEmbed.Token);

            var dropViewScript = TestHelpers.Parse("DROP SAVED VIEW 'West Coast' FOR REPORT 'Monthly Sales';");
            var dropView = Assert.IsType<DropPortalSavedViewStatement>(Assert.Single(dropViewScript.Statements));
            Assert.Equal("West Coast", dropView.Name);

            var dropAlertScript = TestHelpers.Parse("DROP ALERT 'Revenue Floor' FOR REPORT 'Monthly Sales';");
            var dropAlert = Assert.IsType<DropPortalAlertStatement>(Assert.Single(dropAlertScript.Statements));
            Assert.Equal("Revenue Floor", dropAlert.Name);
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

CREATE REFRESH JOB FOR REPORT 'Monthly Sales' SCHEDULE '0 6 * * *' AT orch;
REFRESH REPORT 'Monthly Sales';");

            Assert.DoesNotContain(script.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
            Assert.Contains(script.Statements, s => s is CreateSetsStatement);
            Assert.Contains(script.Statements, s => s is UseSetsStatement);
            Assert.Contains(script.Statements, s => s is CreatePortalFolderStatement);
            Assert.Contains(script.Statements, s => s is GrantPortalPermissionStatement);
            Assert.Contains(script.Statements, s => s is PublishPortalReportStatement);
            Assert.Contains(script.Statements, s => s is AlterPortalReportStatement);
            Assert.Contains(script.Statements, s => s is CreatePortalRefreshJobStatement);
            Assert.Contains(script.Statements, s => s is RefreshPortalReportStatement);
        }
    }
}
