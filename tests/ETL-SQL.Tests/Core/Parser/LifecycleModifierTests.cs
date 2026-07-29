using System;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using Xunit;

namespace ETL_SQL.Tests.Core.Parsing
{
    /// <summary>
    /// Lifecycle modifiers are part of the object contract. Unsupported pairs must be rejected at
    /// parse time rather than parsed as a plain CREATE and left for handlers to interpret.
    /// </summary>
    public sealed class LifecycleModifierTests
    {
        private static Script Parse(string sql) => new Parser(new Lexer(sql).Tokenize(), sql).Parse();

        [Theory]
        [InlineData("CREATE OR ALTER TABLE #t (id INT);", "CREATE OR ALTER is not supported for TABLE.")]
        [InlineData("CREATE OR ALTER INDEX ix ON #t (id);", "CREATE OR ALTER is not supported for INDEX.")]
        [InlineData("CREATE OR REPLACE INDEX ix ON #t (id);", "CREATE OR REPLACE is not supported for INDEX.")]
        [InlineData("CREATE OR ALTER UNIQUE INDEX ix ON #t (id);", "CREATE OR ALTER is not supported for INDEX.")]
        [InlineData("CREATE OR REPLACE UNIQUE INDEX ix ON #t (id);", "CREATE OR REPLACE is not supported for INDEX.")]
        [InlineData("CREATE OR ALTER DIRECTORY 'out';", "CREATE OR ALTER is not supported for DIRECTORY.")]
        [InlineData("CREATE OR REPLACE DIRECTORY 'out';", "CREATE OR REPLACE is not supported for DIRECTORY.")]
        [InlineData("CREATE OR ALTER SSH_KEY_PAIR 'keys/id_rsa';", "CREATE OR ALTER is not supported for SSH_KEY_PAIR.")]
        [InlineData("CREATE OR REPLACE SSH_KEY_PAIR 'keys/id_rsa';", "CREATE OR REPLACE is not supported for SSH_KEY_PAIR.")]
        [InlineData("CREATE OR ALTER PGP_KEY_PAIR 'keys/pgp.asc';", "CREATE OR ALTER is not supported for PGP_KEY_PAIR.")]
        [InlineData("CREATE OR REPLACE PGP_KEY_PAIR 'keys/pgp.asc';", "CREATE OR REPLACE is not supported for PGP_KEY_PAIR.")]
        [InlineData("CREATE OR ALTER SETS !regions BEGIN @r = 'North' END;", "CREATE OR ALTER is not supported for SETS.")]
        [InlineData("CREATE OR REPLACE SETS !regions BEGIN @r = 'North' END;", "CREATE OR REPLACE is not supported for SETS.")]
        [InlineData("CREATE OR ALTER TAG FOR TABLE #t WITH (owner = 'Ops');", "CREATE OR ALTER is not supported for TAG.")]
        [InlineData("CREATE OR REPLACE TAG FOR TABLE #t WITH (owner = 'Ops');", "CREATE OR REPLACE is not supported for TAG.")]
        [InlineData("CREATE OR ALTER LINEAGE FOR TABLE #t FROM 'lineage.json';", "CREATE OR ALTER is not supported for LINEAGE.")]
        [InlineData("CREATE OR REPLACE LINEAGE FOR TABLE #t FROM 'lineage.json';", "CREATE OR REPLACE is not supported for LINEAGE.")]
        [InlineData("CREATE OR ALTER USER 'alice' WITH (EMAIL='alice@example.com', PASSWORD='x');", "CREATE OR ALTER is not supported for USER.")]
        [InlineData("CREATE OR REPLACE USER 'alice' WITH (EMAIL='alice@example.com', PASSWORD='x');", "CREATE OR REPLACE is not supported for USER.")]
        [InlineData("CREATE OR ALTER GROUP 'Analysts';", "CREATE OR ALTER is not supported for GROUP.")]
        [InlineData("CREATE OR REPLACE GROUP 'Analysts';", "CREATE OR REPLACE is not supported for GROUP.")]
        [InlineData("CREATE OR ALTER FOLDER '/Finance';", "CREATE OR ALTER is not supported for FOLDER.")]
        [InlineData("CREATE OR REPLACE FOLDER '/Finance';", "CREATE OR REPLACE is not supported for FOLDER.")]
        [InlineData("CREATE OR ALTER SUBSCRIPTION FOR REPORT 'Daily Sales' TO 'ops@example.com' SCHEDULE 'Daily';", "CREATE OR ALTER is not supported for SUBSCRIPTION.")]
        [InlineData("CREATE OR REPLACE SUBSCRIPTION FOR REPORT 'Daily Sales' TO 'ops@example.com' SCHEDULE 'Daily';", "CREATE OR REPLACE is not supported for SUBSCRIPTION.")]
        [InlineData("CREATE OR ALTER SHARE LINK FOR REPORT 'Daily Sales';", "CREATE OR ALTER is not supported for SHARE LINK.")]
        [InlineData("CREATE OR REPLACE SHARE LINK FOR REPORT 'Daily Sales';", "CREATE OR REPLACE is not supported for SHARE LINK.")]
        [InlineData("CREATE OR ALTER EMBED TOKEN FOR REPORT 'Daily Sales';", "CREATE OR ALTER is not supported for EMBED TOKEN.")]
        [InlineData("CREATE OR REPLACE EMBED TOKEN FOR REPORT 'Daily Sales';", "CREATE OR REPLACE is not supported for EMBED TOKEN.")]
        [InlineData("CREATE OR ALTER SAVED VIEW 'Default' FOR REPORT 'Daily Sales' PARAMETERS ();", "CREATE OR ALTER is not supported for SAVED VIEW.")]
        [InlineData("CREATE OR REPLACE SAVED VIEW 'Default' FOR REPORT 'Daily Sales' PARAMETERS ();", "CREATE OR REPLACE is not supported for SAVED VIEW.")]
        public void UnsupportedCreateModes_AreRejectedInsteadOfIgnored(string sql, string expectedMessage)
        {
            var script = Parse(sql);

            var diagnostic = Assert.Single(script.Diagnostics);
            Assert.Equal("SYNTAX", diagnostic.Code);
            Assert.Contains(expectedMessage, diagnostic.Message, StringComparison.Ordinal);
            Assert.Empty(script.Statements);
        }

        [Theory]
        [InlineData("CREATE TABLE IF NOT EXISTS #t (id INT);")]
        [InlineData("CREATE OR REPLACE TABLE #t (id INT);")]
        [InlineData("CREATE OR ALTER CONNECTION c AS MOCKDB();")]
        [InlineData("CREATE OR REPLACE VIEW v AS SELECT 1 AS id;")]
        [InlineData("CREATE OR ALTER SCHEDULE Nightly ON '0 2 * * *';")]
        [InlineData("CREATE OR REPLACE NOTIFICATION Ops USING mail TO 'ops@example.com';")]
        [InlineData("CREATE OR REPLACE ALERT A FOR REPORT 'Daily Sales' WHEN VISUAL Failures > 0;")]
        public void SupportedCreateModes_StillParse(string sql)
        {
            var script = Parse(sql);

            Assert.Empty(script.Diagnostics);
            Assert.Single(script.Statements);
        }

        [Theory]
        [InlineData("ALTER INDEX ix ON #t (id);")]
        [InlineData("ALTER SETS !regions BEGIN @r = 'North' END;")]
        [InlineData("ALTER TAG FOR TABLE #t WITH (owner = 'Ops');")]
        [InlineData("ALTER LINEAGE FOR TABLE #t FROM 'lineage.json';")]
        [InlineData("ALTER GROUP 'Analysts' SET DESCRIPTION = 'Ops';")]
        [InlineData("ALTER SHARE LINK FOR REPORT 'Daily Sales';")]
        [InlineData("ALTER EMBED TOKEN FOR REPORT 'Daily Sales';")]
        [InlineData("ALTER SAVED VIEW 'Default' FOR REPORT 'Daily Sales' PARAMETERS ();")]
        public void UnsupportedAlterObjectKinds_AreRejected(string sql)
        {
            var script = Parse(sql);

            Assert.NotEmpty(script.Diagnostics);
            Assert.Empty(script.Statements);
        }

        [Theory]
        [InlineData("CREATE CONNECTION IF NOT EXISTS c AS MOCKDB();")]
        [InlineData("CREATE PROCEDURE IF NOT EXISTS p() AS PRINT('ok');")]
        [InlineData("CREATE FUNCTION IF NOT EXISTS f() RETURNS INT AS RETURN 1;")]
        [InlineData("CREATE VIEW IF NOT EXISTS v AS SELECT 1 AS id;")]
        [InlineData("CREATE JOB IF NOT EXISTS J FOR SCRIPT 'jobs/j.etlsql';")]
        [InlineData("CREATE SCHEDULE IF NOT EXISTS T ON '0 2 * * *';")]
        [InlineData("CREATE NOTIFICATION IF NOT EXISTS Ops USING mail TO 'ops@example.com';")]
        [InlineData("CREATE DIRECTORY IF NOT EXISTS 'out';")]
        [InlineData("CREATE INDEX IF NOT EXISTS ix ON #t (id);")]
        [InlineData("CREATE UNIQUE INDEX IF NOT EXISTS ix ON #t (id);")]
        [InlineData("CREATE SETS IF NOT EXISTS !regions BEGIN @r = 'North' END;")]
        [InlineData("CREATE TAG IF NOT EXISTS FOR TABLE #t WITH (owner = 'Ops');")]
        [InlineData("CREATE LINEAGE IF NOT EXISTS FOR TABLE #t FROM 'lineage.json';")]
        [InlineData("CREATE VISUAL IF NOT EXISTS V AS TABLE (SOURCE = (SELECT 1 AS id), MAPPINGS (id));")]
        [InlineData("CREATE PAGE IF NOT EXISTS P AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = V));")]
        [InlineData("CREATE DATASET IF NOT EXISTS &sales AS (SELECT 1 AS id);")]
        [InlineData("CREATE STYLE IF NOT EXISTS S AS (COLOR = 'red');")]
        [InlineData("CREATE CONTAINER IF NOT EXISTS C AS BOX (LAYOUT (STRUCTURE = 'A', MAP ('A' = V)));")]
        [InlineData("CREATE NAVIGATION IF NOT EXISTS N AS TAB (PAGES (P));")]
        [InlineData("CREATE BUTTON IF NOT EXISTS B AS (TITLE = 'Run', ACTIONS (ON_CLICK = APPLY_PARAMETERS));")]
        [InlineData("CREATE TEMPLATE IF NOT EXISTS T AS (TYPE = 'table');")]
        [InlineData("CREATE THEME IF NOT EXISTS Dark AS (BACKGROUND = '#000');")]
        [InlineData("CREATE USER IF NOT EXISTS 'alice' WITH (EMAIL='alice@example.com', PASSWORD='x');")]
        [InlineData("CREATE GROUP IF NOT EXISTS 'Analysts';")]
        [InlineData("CREATE FOLDER IF NOT EXISTS '/Finance';")]
        [InlineData("CREATE SUBSCRIPTION IF NOT EXISTS FOR REPORT 'Daily Sales' TO 'ops@example.com' SCHEDULE 'Daily';")]
        [InlineData("CREATE SHARE LINK IF NOT EXISTS FOR REPORT 'Daily Sales';")]
        [InlineData("CREATE EMBED TOKEN IF NOT EXISTS FOR REPORT 'Daily Sales';")]
        [InlineData("CREATE SAVED VIEW IF NOT EXISTS 'Default' FOR REPORT 'Daily Sales' PARAMETERS ();")]
        [InlineData("CREATE ALERT IF NOT EXISTS A FOR REPORT 'Daily Sales' WHEN VISUAL Failures > 0;")]
        public void UnsupportedCreateIfNotExistsObjectKinds_AreRejected(string sql)
        {
            var script = Parse(sql);

            Assert.NotEmpty(script.Diagnostics);
            Assert.Empty(script.Statements);
        }

        [Theory]
        [InlineData("CREATE OR REPLACE CONNECTION c AS MOCKDB();", "CREATE OR REPLACE CONNECTION")]
        [InlineData("CREATE OR REPLACE PROCEDURE p() AS PRINT('ok');", "CREATE OR REPLACE PROCEDURE")]
        [InlineData("CREATE OR REPLACE FUNCTION f() RETURNS INT AS RETURN 1;", "CREATE OR REPLACE FUNCTION")]
        [InlineData("CREATE OR REPLACE VIEW v AS SELECT 1 AS id;", "CREATE OR REPLACE VIEW")]
        [InlineData("CREATE OR REPLACE JOB J FOR SCRIPT 'jobs/j.etlsql';", "CREATE OR REPLACE JOB")]
        [InlineData("CREATE OR REPLACE SCHEDULE T ON '0 2 * * *';", "CREATE OR REPLACE SCHEDULE")]
        [InlineData("CREATE OR REPLACE NOTIFICATION Ops USING mail TO 'ops@example.com';", "CREATE OR REPLACE NOTIFICATION")]
        [InlineData("CREATE OR REPLACE ALERT A FOR REPORT 'Daily Sales' WHEN VISUAL Failures > 0;", "CREATE OR REPLACE ALERT")]
        [InlineData("CREATE OR REPLACE VISUAL V AS TABLE (SOURCE = (SELECT 1 AS id), MAPPINGS (id));", "CREATE OR REPLACE VISUAL")]
        [InlineData("CREATE OR REPLACE PAGE P AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = V));", "CREATE OR REPLACE PAGE")]
        [InlineData("CREATE OR REPLACE DATASET &sales AS (SELECT 1 AS id);", "CREATE OR REPLACE DATASET")]
        [InlineData("CREATE OR REPLACE CONTAINER C AS BOX (LAYOUT (STRUCTURE = 'A', MAP ('A' = V)));", "CREATE OR REPLACE CONTAINER")]
        [InlineData("CREATE OR REPLACE NAVIGATION N AS TAB (PAGES (P));", "CREATE OR REPLACE NAVIGATION")]
        [InlineData("CREATE OR REPLACE STYLE S AS (COLOR = 'red');", "CREATE OR REPLACE STYLE")]
        [InlineData("CREATE OR REPLACE BUTTON B AS (TITLE = 'Run', ACTIONS (ON_CLICK = APPLY_PARAMETERS));", "CREATE OR REPLACE BUTTON")]
        [InlineData("CREATE OR REPLACE TEMPLATE T AS (TYPE = 'table');", "CREATE OR REPLACE TEMPLATE")]
        [InlineData("CREATE OR REPLACE THEME Dark AS (BACKGROUND = '#000');", "CREATE OR REPLACE THEME")]
        public void CreateOrReplace_SerializesWithoutDroppingLifecycleMode(string sql, string expectedPrefix)
        {
            var statement = Assert.Single(Parse(sql).Statements);

            var serialized = statement.ToSql();

            Assert.StartsWith(expectedPrefix, serialized, StringComparison.Ordinal);
            Assert.DoesNotContain("UNKNOWN STATEMENT", serialized, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("CREATE OR ALTER CONNECTION c AS MOCKDB();")]
        [InlineData("CREATE OR ALTER VISUAL V AS TABLE (SOURCE = (SELECT 1 AS id), MAPPINGS (id));")]
        [InlineData("CREATE OR ALTER PAGE P AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = V));")]
        [InlineData("CREATE OR ALTER DATASET &sales AS (SELECT 1 AS id);")]
        [InlineData("CREATE OR ALTER CONTAINER C AS BOX (LAYOUT (STRUCTURE = 'A', MAP ('A' = V)));")]
        [InlineData("CREATE OR ALTER NAVIGATION N AS TAB (PAGES (P));")]
        [InlineData("CREATE OR ALTER STYLE S AS (COLOR = 'red');")]
        [InlineData("CREATE OR ALTER BUTTON B AS (TITLE = 'Run', ACTIONS (ON_CLICK = APPLY_PARAMETERS));")]
        [InlineData("CREATE OR ALTER TEMPLATE T AS (TYPE = 'table');")]
        [InlineData("CREATE OR ALTER THEME Dark AS (BACKGROUND = '#000');")]
        public void CreateOrAlter_ReportAndConnectionStatements_RoundTripThroughFormatter(string sql)
        {
            var serialized = Assert.Single(Parse(sql).Statements).ToSql();
            var reparsed = Parse(serialized);

            Assert.Empty(reparsed.Diagnostics);
            Assert.Equal(serialized, Assert.Single(reparsed.Statements).ToSql());
        }

        [Fact]
        public void CreateStyle_WithoutAs_ReportsSyntaxError()
        {
            var script = Parse("CREATE STYLE Panel (COLOR = 'red');");

            var diagnostic = Assert.Single(script.Diagnostics);
            Assert.Equal("SYNTAX", diagnostic.Code);
            Assert.Contains("Expected AS after style name", diagnostic.Message, StringComparison.Ordinal);
            Assert.Empty(script.Statements);
        }
    }
}
