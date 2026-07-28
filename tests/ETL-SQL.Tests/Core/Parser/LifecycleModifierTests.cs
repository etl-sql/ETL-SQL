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
        [InlineData("CREATE OR REPLACE INDEX ix ON #t (id);", "CREATE OR REPLACE is not supported for INDEX.")]
        [InlineData("CREATE OR REPLACE UNIQUE INDEX ix ON #t (id);", "CREATE OR REPLACE is not supported for INDEX.")]
        [InlineData("CREATE OR ALTER DIRECTORY 'out';", "CREATE OR ALTER is not supported for DIRECTORY.")]
        [InlineData("CREATE OR REPLACE SSH_KEY_PAIR 'keys/id_rsa';", "CREATE OR REPLACE is not supported for SSH_KEY_PAIR.")]
        [InlineData("CREATE OR REPLACE PGP_KEY_PAIR 'keys/pgp.asc';", "CREATE OR REPLACE is not supported for PGP_KEY_PAIR.")]
        [InlineData("CREATE OR REPLACE SETS !regions BEGIN @r = 'North' END;", "CREATE OR REPLACE is not supported for SETS.")]
        [InlineData("CREATE OR REPLACE TAG FOR TABLE #t WITH (owner = 'Ops');", "CREATE OR REPLACE is not supported for TAG.")]
        [InlineData("CREATE OR REPLACE LINEAGE FOR TABLE #t FROM 'lineage.json';", "CREATE OR REPLACE is not supported for LINEAGE.")]
        [InlineData("CREATE OR ALTER USER 'alice' WITH (EMAIL='alice@example.com', PASSWORD='x');", "CREATE OR ALTER is not supported for USER.")]
        [InlineData("CREATE OR REPLACE GROUP 'Analysts';", "CREATE OR REPLACE is not supported for GROUP.")]
        [InlineData("CREATE OR REPLACE FOLDER '/Finance';", "CREATE OR REPLACE is not supported for FOLDER.")]
        [InlineData("CREATE OR ALTER SUBSCRIPTION FOR REPORT 'Daily Sales' TO 'ops@example.com' SCHEDULE 'Daily';", "CREATE OR ALTER is not supported for SUBSCRIPTION.")]
        [InlineData("CREATE OR REPLACE SHARE LINK FOR REPORT 'Daily Sales';", "CREATE OR REPLACE is not supported for SHARE LINK.")]
        [InlineData("CREATE OR REPLACE EMBED TOKEN FOR REPORT 'Daily Sales';", "CREATE OR REPLACE is not supported for EMBED TOKEN.")]
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
    }
}
