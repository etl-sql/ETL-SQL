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
