using System;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using Xunit;

namespace ETL_SQL.Tests.Core.Parsing
{
    /// <summary>
    /// <c>SCHEDULE</c> and <c>NOTIFICATION</c> as peer entities to <c>JOB</c>, and the
    /// <c>ALTER JOB … ADD|REMOVE</c> attachments that link them.
    /// </summary>
    public class CatalogStatementTests
    {
        private static Script Parse(string sql) => new Parser(new Lexer(sql).Tokenize(), sql).Parse();

        private static T ParseOne<T>(string sql) where T : Statement
        {
            var script = Parse(sql);
            Assert.Empty(script.Diagnostics);
            return Assert.IsType<T>(Assert.Single(script.Statements));
        }

        // ── CREATE SCHEDULE ───────────────────────────────────────────────────────

        [Fact]
        public void CreateSchedule_ParsesCronAndTimeZone()
        {
            var stmt = ParseOne<CreateScheduleStatement>(
                "CREATE SCHEDULE NightlyTrigger ON '0 2 * * *' AT TIME ZONE 'America/New_York';");

            Assert.Equal("NightlyTrigger", stmt.Name);
            Assert.Equal("0 2 * * *", stmt.Cron);
            Assert.Equal("America/New_York", stmt.TimeZone);
            Assert.Equal(ObjectCreationMode.Create, stmt.Mode);
        }

        /// <summary>
        /// A missing zone is null in the AST, not defaulted here — the handler resolves the configured
        /// default and stores it, so that the answer is fixed at creation instead of being re-read at
        /// every fire.
        /// </summary>
        [Fact]
        public void CreateSchedule_LeavesAnAbsentTimeZoneNull()
        {
            Assert.Null(ParseOne<CreateScheduleStatement>("CREATE SCHEDULE T ON '0 2 * * *';").TimeZone);
        }

        /// <summary>
        /// <c>AT TIME ZONE</c> must not be mistaken for a target-connection <c>AT</c>. The two-token
        /// lookahead that the expression parser already uses settles it.
        /// </summary>
        [Fact]
        public void CreateSchedule_TimeZoneClauseDoesNotCollideWithTargetAt()
        {
            var stmt = ParseOne<CreateScheduleStatement>("CREATE SCHEDULE T ON '0 2 * * *' AT TIME ZONE 'UTC';");
            Assert.Equal("UTC", stmt.TimeZone);
        }

        [Theory]
        [InlineData("CREATE OR ALTER SCHEDULE T ON '0 2 * * *';", ObjectCreationMode.CreateOrAlter)]
        [InlineData("CREATE OR REPLACE SCHEDULE T ON '0 2 * * *';", ObjectCreationMode.CreateOrReplace)]
        public void CreateSchedule_CarriesTheCreationMode(string sql, ObjectCreationMode expected)
        {
            Assert.Equal(expected, ParseOne<CreateScheduleStatement>(sql).Mode);
        }

        [Fact]
        public void CreateSchedule_ParsesPresentationMetadata()
        {
            var stmt = ParseOne<CreateScheduleStatement>(
                "CREATE SCHEDULE T ON '0 2 * * *' WITH (DISPLAY_NAME = 'Overnight', DESCRIPTION = 'Batch window', TEAM = 'Finance');");

            Assert.Equal("Overnight", stmt.Metadata.DisplayName);
            Assert.Equal("Batch window", stmt.Metadata.Description);
            Assert.Equal("Finance", stmt.Metadata.Options!["TEAM"]);
        }

        [Fact]
        public void CreateSchedule_RequiresACronExpression()
        {
            var diagnostic = Assert.Single(Parse("CREATE SCHEDULE T;").Diagnostics);
            Assert.Contains("Expected ON", diagnostic.Message, StringComparison.Ordinal);
        }

        // ── CREATE NOTIFICATION ───────────────────────────────────────────────────

        [Fact]
        public void CreateNotification_ParsesConnectionAndRecipient()
        {
            var stmt = ParseOne<CreateNotificationStatement>(
                "CREATE NOTIFICATION OpsAlert USING local_mail TO 'ops@example.com';");

            Assert.Equal("OpsAlert", stmt.Name);
            Assert.Equal("local_mail", stmt.ConnectionName);
            Assert.Equal("ops@example.com", stmt.Recipient);
        }

        /// <summary>A webhook has no recipient — the URL is part of the connection.</summary>
        [Fact]
        public void CreateNotification_AllowsAnAbsentRecipient()
        {
            var stmt = ParseOne<CreateNotificationStatement>("CREATE NOTIFICATION Slack USING team_hook;");
            Assert.Null(stmt.Recipient);
        }

        [Fact]
        public void CreateNotification_RequiresAConnection()
        {
            var diagnostic = Assert.Single(Parse("CREATE NOTIFICATION OpsAlert;").Diagnostics);
            Assert.Contains("USING", diagnostic.Message, StringComparison.Ordinal);
        }

        // ── ALTER ─────────────────────────────────────────────────────────────────

        [Fact]
        public void AlterSchedule_PatchesOnlyWhatIsNamed()
        {
            var stmt = ParseOne<AlterCatalogObjectStatement>("ALTER SCHEDULE T SET CRON = '0 3 * * *';");

            Assert.Equal(CatalogObjectKind.Schedule, stmt.Kind);
            Assert.Equal("0 3 * * *", stmt.Cron);
            Assert.Null(stmt.TimeZone);
        }

        [Fact]
        public void AlterSchedule_SetsTheTimeZone()
        {
            Assert.Equal("UTC", ParseOne<AlterCatalogObjectStatement>("ALTER SCHEDULE T SET TIME ZONE 'UTC';").TimeZone);
        }

        [Fact]
        public void AlterNotification_SetsRecipientAndConnection()
        {
            var stmt = ParseOne<AlterCatalogObjectStatement>(
                "ALTER NOTIFICATION OpsAlert SET TO 'infra@example.com' SET USING backup_mail;");

            Assert.Equal(CatalogObjectKind.Notification, stmt.Kind);
            Assert.Equal("infra@example.com", stmt.Recipient);
            Assert.Equal("backup_mail", stmt.ConnectionName);
        }

        /// <summary>
        /// An ALTER with no SET clause would parse to a statement that changes nothing and reports
        /// success — the same silent no-op the report-object work removed.
        /// </summary>
        [Theory]
        [InlineData("ALTER SCHEDULE T;")]
        [InlineData("ALTER NOTIFICATION N;")]
        public void Alter_WithoutASetClause_IsRejected(string sql)
        {
            var diagnostic = Assert.Single(Parse(sql).Diagnostics);
            Assert.Contains("at least one SET clause", diagnostic.Message, StringComparison.Ordinal);
        }

        // ── Attachments ───────────────────────────────────────────────────────────

        [Theory]
        [InlineData("ALTER JOB Nightly ADD SCHEDULE T;", JobAttachmentAction.Add)]
        [InlineData("ALTER JOB Nightly REMOVE SCHEDULE T;", JobAttachmentAction.Remove)]
        public void AlterJob_AttachesAndDetachesASchedule(string sql, JobAttachmentAction action)
        {
            var stmt = ParseOne<AlterJobAttachmentStatement>(sql);

            Assert.Equal(action, stmt.Action);
            Assert.Equal(CatalogObjectKind.Schedule, stmt.Kind);
            Assert.Equal("Nightly", stmt.JobName);
            Assert.Equal("T", stmt.TargetName);
            Assert.Null(stmt.Trigger);
        }

        [Theory]
        [InlineData("SUCCESS")]
        [InlineData("FAILURE")]
        [InlineData("COMPLETION")]
        public void AlterJob_AttachesANotificationForEachOutcome(string trigger)
        {
            var stmt = ParseOne<AlterJobAttachmentStatement>(
                $"ALTER JOB Nightly ADD NOTIFICATION OpsAlert ON {trigger};");

            Assert.Equal(CatalogObjectKind.Notification, stmt.Kind);
            Assert.Equal(trigger, stmt.Trigger, ignoreCase: true);
        }

        /// <summary>
        /// The outcome is required rather than defaulted. "Notify me about this job" has no single
        /// obvious meaning, and picking one would deliver on outcomes nobody asked for.
        /// </summary>
        [Fact]
        public void AlterJob_NotificationWithoutAnOutcome_IsRejected()
        {
            var diagnostic = Assert.Single(Parse("ALTER JOB Nightly ADD NOTIFICATION OpsAlert;").Diagnostics);

            Assert.Contains("SUCCESS, FAILURE, or COMPLETION", diagnostic.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void AlterJob_RejectsAnUnknownOutcome()
        {
            var diagnostic = Assert.Single(
                Parse("ALTER JOB Nightly ADD NOTIFICATION OpsAlert ON MAYBE;").Diagnostics);

            Assert.Contains("SUCCESS, FAILURE, or COMPLETION", diagnostic.Message, StringComparison.Ordinal);
        }

        /// <summary>
        /// The attachment forms must not swallow the job's own ALTER grammar, which is a different
        /// operation on a different object.
        /// </summary>
        [Fact]
        public void AlterJob_StillParsesTheJobsOwnDefinition()
        {
            var script = Parse("ALTER JOB Nightly ON SCHEDULE EVERY 2 HOURS;");
            Assert.Empty(script.Diagnostics);
            Assert.IsType<AlterJobStatement>(Assert.Single(script.Statements));
        }

        // ── DROP / ENABLE / DISABLE ───────────────────────────────────────────────

        [Theory]
        [InlineData("DROP SCHEDULE T;", CatalogObjectKind.Schedule, false)]
        [InlineData("DROP SCHEDULE IF EXISTS T;", CatalogObjectKind.Schedule, true)]
        [InlineData("DROP NOTIFICATION N;", CatalogObjectKind.Notification, false)]
        [InlineData("DROP NOTIFICATION IF EXISTS N;", CatalogObjectKind.Notification, true)]
        public void Drop_ParsesWithExistenceModifierBeforeTheName(
            string sql, CatalogObjectKind kind, bool ifExists)
        {
            var stmt = ParseOne<DropCatalogObjectStatement>(sql);

            Assert.Equal(kind, stmt.Kind);
            Assert.Equal(ifExists, stmt.IfExists);
        }

        /// <summary>
        /// <c>DROP</c> dispatch used to test the legacy CHART alias with
        /// <c>Match(TokenType.IDENTIFIER) &amp;&amp; value == "CHART"</c>, where the Match consumed *any*
        /// identifier before the value check rejected it. Every identifier-dispatched DROP after that
        /// branch therefore failed with an error pointing past the word already eaten. CHART itself
        /// must keep working, and so must the kinds that follow it.
        /// </summary>
        [Fact]
        public void Drop_DoesNotConsumeAnIdentifierWhileTestingTheChartAlias()
        {
            var chart = Assert.IsType<DropReportObjectStatement>(
                Assert.Single(Parse("DROP CHART RevenueChart;").Statements));
            Assert.Equal(ReportObjectType.Visual, chart.ObjectType);
            Assert.Equal("RevenueChart", chart.Name);

            // The kind dispatched by identifier further down the chain still resolves.
            Assert.IsType<DropCatalogObjectStatement>(
                Assert.Single(Parse("DROP NOTIFICATION OpsAlert;").Statements));
        }

        /// <summary>Consistent with the sixteen DROP kinds that already reject the post-name form.</summary>
        [Fact]
        public void Drop_RejectsTrailingIfExists()
        {
            var diagnostic = Assert.Single(Parse("DROP SCHEDULE T IF EXISTS;").Diagnostics);

            Assert.Contains("IF EXISTS must come before the object name", diagnostic.Message, StringComparison.Ordinal);
            Assert.Contains("DROP SCHEDULE IF EXISTS T", diagnostic.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("ENABLE SCHEDULE T;", CatalogObjectKind.Schedule, true)]
        [InlineData("DISABLE SCHEDULE T;", CatalogObjectKind.Schedule, false)]
        [InlineData("ENABLE NOTIFICATION N;", CatalogObjectKind.Notification, true)]
        [InlineData("DISABLE NOTIFICATION N;", CatalogObjectKind.Notification, false)]
        public void EnableDisable_AppliesToCatalogObjects(string sql, CatalogObjectKind kind, bool enabled)
        {
            var stmt = ParseOne<SetCatalogObjectEnabledStatement>(sql);

            Assert.Equal(kind, stmt.Kind);
            Assert.Equal(enabled, stmt.IsEnabled);
        }

        /// <summary>ENABLE/DISABLE JOB must keep working alongside the catalog forms.</summary>
        [Fact]
        public void EnableDisable_StillAppliesToJobs()
        {
            Assert.IsType<EnableJobStatement>(Assert.Single(Parse("ENABLE JOB Nightly;").Statements));
            Assert.IsType<DisableJobStatement>(Assert.Single(Parse("DISABLE JOB Nightly;").Statements));
        }

        // ── Round-trip ────────────────────────────────────────────────────────────

        /// <summary>
        /// The formatter is what <c>ConfigurationExportService</c> emits, and an export must replay
        /// into what it describes. A statement that cannot round-trip cannot be exported honestly.
        /// </summary>
        [Theory]
        [InlineData("CREATE SCHEDULE T ON '0 2 * * *';")]
        [InlineData("CREATE SCHEDULE T ON '0 2 * * *' AT TIME ZONE 'America/New_York';")]
        [InlineData("CREATE OR REPLACE SCHEDULE T ON '0 2 * * *';")]
        [InlineData("CREATE SCHEDULE T ON '0 2 * * *' WITH (DISPLAY_NAME = 'Overnight');")]
        [InlineData("CREATE NOTIFICATION N USING local_mail;")]
        [InlineData("CREATE OR ALTER NOTIFICATION N USING local_mail TO 'ops@example.com';")]
        [InlineData("ALTER SCHEDULE T SET CRON = '0 3 * * *';")]
        [InlineData("ALTER NOTIFICATION N SET TO 'ops@example.com';")]
        [InlineData("ALTER JOB J ADD SCHEDULE T;")]
        [InlineData("ALTER JOB J REMOVE NOTIFICATION N ON FAILURE;")]
        [InlineData("DROP SCHEDULE IF EXISTS T;")]
        [InlineData("DROP NOTIFICATION N;")]
        [InlineData("ENABLE SCHEDULE T;")]
        [InlineData("DISABLE NOTIFICATION N;")]
        public void Statements_RoundTripThroughTheFormatter(string sql)
        {
            var formatted = Assert.Single(Parse(sql).Statements).ToSql();

            // It re-parses...
            var reparsed = Parse(formatted);
            Assert.Empty(reparsed.Diagnostics);

            // ...and formatting the result again is stable, so the export is a fixed point.
            Assert.Equal(formatted, Assert.Single(reparsed.Statements).ToSql());
        }

        /// <summary>
        /// A quoted value containing an apostrophe must survive the round trip rather than truncating
        /// the statement at the first quote.
        /// </summary>
        [Fact]
        public void RoundTrip_EscapesEmbeddedQuotes()
        {
            var stmt = Assert.Single(
                Parse("CREATE SCHEDULE T ON '0 2 * * *' WITH (DESCRIPTION = 'Finance''s window');").Statements);

            var reparsed = Parse(stmt.ToSql());
            Assert.Empty(reparsed.Diagnostics);
            Assert.Equal("Finance's window",
                Assert.IsType<CreateScheduleStatement>(Assert.Single(reparsed.Statements)).Metadata.Description);
        }
    }
}
