using System;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
using Xunit;

namespace ETL_SQL.Tests.Core.Parsing
{
    /// <summary>
    /// Existence modifiers occupy one position for every object kind: <c>DROP &lt;kind&gt; IF EXISTS
    /// &lt;name&gt;</c>. The post-name spelling was accepted for six kinds and is now rejected —
    /// see the canonical-syntax track in <c>TODO.md</c>.
    /// </summary>
    public class DropExistenceModifierTests
    {
        private static Script Parse(string sql)
        {
            var tokens = new Lexer(sql).Tokenize();
            return new Parser(tokens, sql).Parse();
        }

        /// <summary>Every kind that accepts <c>IF EXISTS</c> accepts it before the name.</summary>
        [Theory]
        [InlineData("DROP TABLE IF EXISTS #t;")]
        [InlineData("DROP CONNECTION IF EXISTS c;")]
        [InlineData("DROP PROCEDURE IF EXISTS p;")]
        [InlineData("DROP FUNCTION IF EXISTS f;")]
        [InlineData("DROP VIEW IF EXISTS v;")]
        [InlineData("DROP INDEX IF EXISTS ix;")]
        [InlineData("DROP JOB IF EXISTS j;")]
        [InlineData("DROP SCHEDULE IF EXISTS s;")]
        [InlineData("DROP NOTIFICATION IF EXISTS n;")]
        [InlineData("DROP SETS IF EXISTS !s;")]
        [InlineData("DROP VISUAL IF EXISTS vis;")]
        [InlineData("DROP PAGE IF EXISTS pg;")]
        [InlineData("DROP CONTAINER IF EXISTS ctr;")]
        [InlineData("DROP STYLE IF EXISTS sty;")]
        [InlineData("DROP NAVIGATION IF EXISTS nav;")]
        [InlineData("DROP DATASET IF EXISTS ds;")]
        [InlineData("DROP TEMPLATE IF EXISTS tpl;")]
        [InlineData("DROP THEME IF EXISTS thm;")]
        [InlineData("DROP ALERT IF EXISTS a;")]
        public void CanonicalForm_PlacesIfExists_BeforeTheName(string sql)
        {
            var script = Parse(sql);

            Assert.Single(script.Statements);
            Assert.Empty(script.Diagnostics);
        }

        /// <summary>Portal operational objects do not support <c>DROP IF EXISTS</c>, except ALERT.</summary>
        [Theory]
        [InlineData("DROP USER IF EXISTS 'alice';")]
        [InlineData("DROP GROUP IF EXISTS 'Analysts';")]
        [InlineData("DROP FOLDER IF EXISTS '/Finance';")]
        [InlineData("DROP REPORT IF EXISTS 'Daily Sales';")]
        [InlineData("DROP DATASET IF EXISTS 'Sales' IN FOLDER '/Finance';")]
        [InlineData("DROP SUBSCRIPTION IF EXISTS 42;")]
        [InlineData("DROP SAVED VIEW IF EXISTS 'Default' FOR REPORT 'Daily Sales';")]
        public void PortalObjectsWithoutDropIfExists_RejectTheExistenceModifier(string sql)
        {
            var script = Parse(sql);

            Assert.NotEmpty(script.Diagnostics);
            Assert.Empty(script.Statements);
        }

        /// <summary>
        /// The retired post-name form is refused for every kind, including the ones that never
        /// accepted it — a single uniform diagnostic beats a per-kind accident of history.
        /// </summary>
        [Theory]
        [InlineData("DROP TABLE #t IF EXISTS;", "DROP TABLE IF EXISTS #t")]
        [InlineData("DROP CONNECTION c IF EXISTS;", "DROP CONNECTION IF EXISTS c")]
        [InlineData("DROP PROCEDURE p IF EXISTS;", "DROP PROCEDURE IF EXISTS p")]
        [InlineData("DROP FUNCTION f IF EXISTS;", "DROP FUNCTION IF EXISTS f")]
        [InlineData("DROP VIEW v IF EXISTS;", "DROP VIEW IF EXISTS v")]
        [InlineData("DROP INDEX ix IF EXISTS;", "DROP INDEX IF EXISTS ix")]
        [InlineData("DROP JOB j IF EXISTS;", "DROP JOB IF EXISTS j")]
        [InlineData("DROP SETS !s IF EXISTS;", "DROP SETS IF EXISTS !s")]
        [InlineData("DROP VISUAL vis IF EXISTS;", "DROP VISUAL IF EXISTS vis")]
        [InlineData("DROP PAGE pg IF EXISTS;", "DROP PAGE IF EXISTS pg")]
        [InlineData("DROP CONTAINER ctr IF EXISTS;", "DROP CONTAINER IF EXISTS ctr")]
        [InlineData("DROP STYLE sty IF EXISTS;", "DROP STYLE IF EXISTS sty")]
        [InlineData("DROP NAVIGATION nav IF EXISTS;", "DROP NAVIGATION IF EXISTS nav")]
        [InlineData("DROP DATASET ds IF EXISTS;", "DROP DATASET IF EXISTS ds")]
        [InlineData("DROP TEMPLATE tpl IF EXISTS;", "DROP TEMPLATE IF EXISTS tpl")]
        [InlineData("DROP THEME thm IF EXISTS;", "DROP THEME IF EXISTS thm")]
        public void RetiredPostNameForm_IsRejected_WithTheCanonicalSpelling(string sql, string expectedFix)
        {
            // Parse() converts a SyntaxException into a diagnostic and recovers to the next
            // terminator, so the rejection surfaces here rather than as a thrown exception.
            var script = Parse(sql);

            var diagnostic = Assert.Single(script.Diagnostics);
            Assert.Equal("SYNTAX", diagnostic.Code);

            // The message must carry the exact replacement, not merely name the offence.
            Assert.Contains(expectedFix, diagnostic.Message, StringComparison.Ordinal);

            // The malformed statement must not survive as a silently-accepted DROP.
            Assert.Empty(script.Statements);
        }

        /// <summary>
        /// The statement terminator is optional, so a bare DROP may legally be followed by a real
        /// IF statement. Only <c>IF EXISTS</c> is the retired form; rejecting on IF alone would
        /// break valid scripts.
        /// </summary>
        [Fact]
        public void BareDrop_FollowedByAnIfStatement_StillParses()
        {
            var script = Parse("DROP VIEW v\nIF 1 = 1 BEGIN PRINT 'x'; END");

            Assert.Empty(script.Diagnostics);
            Assert.Equal(2, script.Statements.Count);
        }
    }
}
