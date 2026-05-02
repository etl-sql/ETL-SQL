using System;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Tests.Core;
using Xunit;

namespace ETL_SQL.Tests
{
    /// <summary>
    /// Parser tests for Phase 2 Subscription SQL Syntax:
    /// CREATE SUBSCRIPTION with optional name and PARAMETERS clause,
    /// ALTER SUBSCRIPTION with expanded SET options.
    /// </summary>
    public class SubscriptionSyntaxTests
    {
        // ── CREATE SUBSCRIPTION — name omitted ───────────────────────────────────

        [Fact]
        public void Create_NoName_ParsesCorrectly()
        {
            var stmt = ParseCreate(
                "CREATE SUBSCRIPTION FOR REPORT '/rpt/daily.etlsql' " +
                "DELIVER TO 'user@corp.com' SCHEDULE 'DAILY' FORMAT PDF AT my_smtp;");

            Assert.Null(stmt.Name);
            Assert.Equal("/rpt/daily.etlsql", stmt.ReportPath);
            Assert.Equal("user@corp.com", stmt.Recipient);
            Assert.False(stmt.IsGroup);
            Assert.Equal("DAILY", stmt.Schedule);
            Assert.False(stmt.OnRefresh);
            Assert.Equal(PortalSubscriptionFormat.Pdf, stmt.Format);
            Assert.Equal("my_smtp", stmt.SmtpAlias);
            Assert.Empty(stmt.Parameters);
        }

        // ── CREATE SUBSCRIPTION — with name ─────────────────────────────────────

        [Fact]
        public void Create_WithName_ParsesName()
        {
            var stmt = ParseCreate(
                "CREATE SUBSCRIPTION 'Weekly Finance' FOR REPORT '/rpt/finance.etlsql' " +
                "DELIVER TO 'team@corp.com' SCHEDULE 'WEEKLY' FORMAT CSV AT smtp1;");

            Assert.Equal("Weekly Finance", stmt.Name);
            Assert.Equal("/rpt/finance.etlsql", stmt.ReportPath);
            Assert.Equal("WEEKLY", stmt.Schedule);
            Assert.Equal(PortalSubscriptionFormat.Csv, stmt.Format);
            Assert.Empty(stmt.Parameters);
        }

        // ── CREATE SUBSCRIPTION — GROUP recipient ────────────────────────────────

        [Fact]
        public void Create_GroupRecipient_ParsesIsGroup()
        {
            var stmt = ParseCreate(
                "CREATE SUBSCRIPTION FOR REPORT '/rpt/x.etlsql' " +
                "DELIVER TO GROUP 'finance_team' SCHEDULE 'MONTHLY' FORMAT BOTH AT smtp1;");

            Assert.True(stmt.IsGroup);
            Assert.Equal("finance_team", stmt.Recipient);
        }

        // ── CREATE SUBSCRIPTION — ON REFRESH ────────────────────────────────────

        [Fact]
        public void Create_OnRefresh_ParsesCorrectly()
        {
            var stmt = ParseCreate(
                "CREATE SUBSCRIPTION FOR REPORT '/rpt/live.etlsql' " +
                "DELIVER TO 'alert@corp.com' ON REFRESH FORMAT PDF AT smtp2;");

            Assert.True(stmt.OnRefresh);
            Assert.Null(stmt.Schedule);
        }

        // ── CREATE SUBSCRIPTION — PARAMETERS clause ──────────────────────────────

        [Fact]
        public void Create_WithParameters_ParsesParameterList()
        {
            var stmt = ParseCreate(
                "CREATE SUBSCRIPTION 'Daily EMEA' FOR REPORT '/rpt/sales.etlsql' " +
                "DELIVER TO 'mgr@corp.com' SCHEDULE 'DAILY' FORMAT PDF AT smtp1 " +
                "PARAMETERS (@dateFrom = 'D-30', @region = 'EMEA');");

            Assert.Equal("Daily EMEA", stmt.Name);
            Assert.Equal(2, stmt.Parameters.Count);
            Assert.Equal("@dateFrom", stmt.Parameters[0].Name);
            Assert.Equal("D-30", stmt.Parameters[0].Value);
            Assert.Equal("@region", stmt.Parameters[1].Name);
            Assert.Equal("EMEA", stmt.Parameters[1].Value);
        }

        [Fact]
        public void Create_EmptyParameters_ParsesEmptyList()
        {
            var stmt = ParseCreate(
                "CREATE SUBSCRIPTION FOR REPORT '/rpt/x.etlsql' " +
                "DELIVER TO 'u@c.com' SCHEDULE 'DAILY' FORMAT PDF AT smtp1 " +
                "PARAMETERS ();");

            Assert.Empty(stmt.Parameters);
        }

        [Fact]
        public void Create_SingleParameter_Parses()
        {
            var stmt = ParseCreate(
                "CREATE SUBSCRIPTION FOR REPORT '/rpt/x.etlsql' " +
                "DELIVER TO 'u@c.com' SCHEDULE 'DAILY' FORMAT PDF AT smtp1 " +
                "PARAMETERS (@cutoff = 'M-1');");

            Assert.Single(stmt.Parameters);
            Assert.Equal("@cutoff", stmt.Parameters[0].Name);
            Assert.Equal("M-1", stmt.Parameters[0].Value);
        }

        // ── ALTER SUBSCRIPTION — schedule only ───────────────────────────────────

        [Fact]
        public void Alter_Schedule_ParsesNewSchedule()
        {
            var stmt = ParseAlter("ALTER SUBSCRIPTION 7 SET SCHEDULE = 'WEEKLY';");

            Assert.Equal(7, stmt.SubscriptionId);
            Assert.Equal("WEEKLY", stmt.NewSchedule);
            Assert.Null(stmt.SetActive);
            Assert.Null(stmt.NewFormat);
            Assert.Null(stmt.NewSmtpAlias);
            Assert.Null(stmt.Parameters);
        }

        // ── ALTER SUBSCRIPTION — ENABLE / DISABLE ────────────────────────────────

        [Fact]
        public void Alter_Enable_SetsActive()
        {
            var stmt = ParseAlter("ALTER SUBSCRIPTION 3 SET ENABLE;");
            Assert.True(stmt.SetActive);
        }

        [Fact]
        public void Alter_Disable_ClearsActive()
        {
            var stmt = ParseAlter("ALTER SUBSCRIPTION 3 SET DISABLE;");
            Assert.False(stmt.SetActive);
        }

        // ── ALTER SUBSCRIPTION — FORMAT ───────────────────────────────────────────

        [Fact]
        public void Alter_Format_ParsesNewFormat()
        {
            var stmt = ParseAlter("ALTER SUBSCRIPTION 5 SET FORMAT = CSV;");
            Assert.Equal(PortalSubscriptionFormat.Csv, stmt.NewFormat);
        }

        // ── ALTER SUBSCRIPTION — SMTP ─────────────────────────────────────────────

        [Fact]
        public void Alter_Smtp_ParsesNewAlias()
        {
            var stmt = ParseAlter("ALTER SUBSCRIPTION 5 SET SMTP = 'smtp_backup';");
            Assert.Equal("smtp_backup", stmt.NewSmtpAlias);
        }

        // ── ALTER SUBSCRIPTION — PARAMETERS replace ───────────────────────────────

        [Fact]
        public void Alter_Parameters_ReplacesAll()
        {
            var stmt = ParseAlter(
                "ALTER SUBSCRIPTION 10 SET PARAMETERS (@dateFrom = 'M-1', @region = 'APAC');");

            Assert.NotNull(stmt.Parameters);
            Assert.Equal(2, stmt.Parameters!.Count);
            Assert.Equal("@dateFrom", stmt.Parameters[0].Name);
            Assert.Equal("M-1", stmt.Parameters[0].Value);
            Assert.Equal("@region", stmt.Parameters[1].Name);
            Assert.Equal("APAC", stmt.Parameters[1].Value);
        }

        // ── ALTER SUBSCRIPTION — PARAMETERS clear ─────────────────────────────────

        [Fact]
        public void Alter_EmptyParameters_ClearsAll()
        {
            var stmt = ParseAlter("ALTER SUBSCRIPTION 10 SET PARAMETERS ();");

            Assert.NotNull(stmt.Parameters);
            Assert.Empty(stmt.Parameters!);
        }

        // ── ALTER SUBSCRIPTION — PARAMETERS absent = unchanged ────────────────────

        [Fact]
        public void Alter_NoParameters_LeavesNull()
        {
            var stmt = ParseAlter("ALTER SUBSCRIPTION 10 SET SCHEDULE = 'DAILY';");
            Assert.Null(stmt.Parameters);
        }

        // ── ALTER SUBSCRIPTION — multi-clause ────────────────────────────────────

        [Fact]
        public void Alter_MultiClause_ParsesAll()
        {
            var stmt = ParseAlter(
                "ALTER SUBSCRIPTION 2 SET SCHEDULE = 'MONTHLY', FORMAT = PDF, ENABLE, " +
                "PARAMETERS (@cutoff = 'Y-1');");

            Assert.Equal("MONTHLY", stmt.NewSchedule);
            Assert.Equal(PortalSubscriptionFormat.Pdf, stmt.NewFormat);
            Assert.True(stmt.SetActive);
            Assert.Single(stmt.Parameters!);
            Assert.Equal("@cutoff", stmt.Parameters![0].Name);
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static CreatePortalSubscriptionStatement ParseCreate(string sql)
        {
            var script = TestHelpers.Parse(sql);
            return Assert.IsType<CreatePortalSubscriptionStatement>(Assert.Single(script.Statements));
        }

        private static AlterPortalSubscriptionStatement ParseAlter(string sql)
        {
            var script = TestHelpers.Parse(sql);
            return Assert.IsType<AlterPortalSubscriptionStatement>(Assert.Single(script.Statements));
        }
    }
}
