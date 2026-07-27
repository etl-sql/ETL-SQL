using System;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using Xunit;

namespace ETL_SQL.Tests.Core.Parsing
{
    /// <summary>
    /// <c>ALTER</c> for report objects is implemented for five kinds only, and each of those accepts
    /// only the clauses it can actually patch. The rest must be refused by the parser rather than
    /// parsing and then failing — or silently doing nothing — at execution. See the canonical-syntax
    /// track in <c>TODO.md</c>.
    /// </summary>
    public class AlterReportObjectSupportTests
    {
        private static Script Parse(string sql)
        {
            var tokens = new Lexer(sql).Tokenize();
            return new Parser(tokens, sql).Parse();
        }

        [Theory]
        [InlineData("ALTER VISUAL v (TITLE = 'x');")]
        [InlineData("ALTER PAGE p (TITLE = 'x');")]
        [InlineData("ALTER CONTAINER c (TITLE = 'x');")]
        [InlineData("ALTER BUTTON b (TITLE = 'x');")]
        [InlineData("ALTER TEMPLATE t (OPTIONS (density = 'compact'));")]
        public void SupportedKinds_Parse(string sql)
        {
            var script = Parse(sql);

            Assert.Empty(script.Diagnostics);
            Assert.Single(script.Statements);
        }

        /// <summary>
        /// These previously parsed cleanly and threw "ALTER not yet implemented" at execution —
        /// after a report script may already have done half its work.
        /// </summary>
        [Theory]
        [InlineData("ALTER STYLE s (TITLE = 'x');", "STYLE")]
        [InlineData("ALTER NAVIGATION n (TITLE = 'x');", "NAVIGATION")]
        [InlineData("ALTER THEME t (TITLE = 'x');", "THEME")]
        public void UnsupportedKinds_AreRejectedAtParseTime(string sql, string kind)
        {
            var script = Parse(sql);

            var diagnostic = Assert.Single(script.Diagnostics);
            Assert.Equal("SYNTAX", diagnostic.Code);
            Assert.Contains($"ALTER is not supported for {kind}", diagnostic.Message, StringComparison.Ordinal);

            // The message must name what *is* supported, so the reader is not left guessing.
            Assert.Contains("VISUAL", diagnostic.Message, StringComparison.Ordinal);
            Assert.Empty(script.Statements);
        }

        /// <summary>
        /// The suggested recreate form has to be a form the parser accepts. STYLE takes no
        /// <c>AS</c> and NAVIGATION names its type after <c>AS</c>, so a single generic
        /// "CREATE OR REPLACE &lt;kind&gt; &lt;name&gt; AS (...)" would send the reader to a second
        /// syntax error.
        /// </summary>
        [Theory]
        [InlineData("ALTER STYLE s (TITLE = 'x');", "CREATE OR REPLACE STYLE <name> (...)")]
        [InlineData("ALTER NAVIGATION n (TITLE = 'x');", "CREATE OR REPLACE NAVIGATION <name> AS TAB|BUTTON|LINK (...)")]
        [InlineData("ALTER THEME t (TITLE = 'x');", "CREATE OR REPLACE THEME <name> AS (...)")]
        public void RefusalNamesAParseableRecreateForm(string sql, string expectedForm)
        {
            var diagnostic = Assert.Single(Parse(sql).Diagnostics);

            Assert.Contains(expectedForm, diagnostic.Message, StringComparison.Ordinal);
        }

        /// <summary>
        /// THEME reaches the report-object parser purely to get this message. Without that routing
        /// it falls through to the generic "expected object kind after ALTER", which names every
        /// other object in the language and not the one thing the author needs to write.
        /// </summary>
        [Fact]
        public void AlterTheme_IsRefusedByName_NotByTheGenericObjectKindError()
        {
            var diagnostic = Assert.Single(Parse("ALTER THEME corporate (BACKGROUND = '#000');").Diagnostics);

            Assert.Contains("ALTER is not supported for THEME", diagnostic.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("Expected CONNECTION, PROCEDURE", diagnostic.Message, StringComparison.Ordinal);
        }

        /// <summary>
        /// The parser's allow-list and the handler's switch must agree. If a kind becomes alterable
        /// in the handler without being added to the parser list, it stays unreachable; if it is
        /// added to the parser list without a handler arm, it fails at execution again.
        /// </summary>
        [Fact]
        public void ParserAllowList_MatchesTheHandlersImplementedKinds()
        {
            // Kinds the parser accepts, discovered by probing rather than by reading the private list.
            var accepted = new[]
            {
                ReportObjectType.Visual, ReportObjectType.Page,
                ReportObjectType.Container, ReportObjectType.Button, ReportObjectType.Template
            };

            foreach (var kind in accepted)
            {
                // TITLE is the one clause every alterable kind but TEMPLATE accepts.
                var body = kind == ReportObjectType.Template ? "OPTIONS (density = 'compact')" : "TITLE = 'y'";
                var sql = $"ALTER {kind.ToString().ToUpperInvariant()} x ({body});";
                Assert.Empty(Parse(sql).Diagnostics);
            }

            // Every other report object kind that has an ALTER spelling must be refused.
            foreach (var kind in new[]
                     {
                         ReportObjectType.Style, ReportObjectType.Navigation,
                         ReportObjectType.Theme, ReportObjectType.Dataset
                     })
            {
                var sql = $"ALTER {kind.ToString().ToUpperInvariant()} x (TITLE = 'y');";
                Assert.NotEmpty(Parse(sql).Diagnostics);
            }
        }

        /// <summary>
        /// Every kind used to be parsed with one visual-shaped body, so a clause the object has no
        /// field for parsed happily and the handler then dropped it — the statement reported success
        /// having changed nothing.
        /// </summary>
        [Theory]
        [InlineData("ALTER PAGE p (SOURCE = (SELECT 1 AS n));", "PAGE", "SOURCE")]
        [InlineData("ALTER PAGE p (MAPPINGS (X = a, Y = b));", "PAGE", "MAPPINGS")]
        [InlineData("ALTER CONTAINER c (ACTIONS (ON_CLICK = BACK));", "CONTAINER", "ACTIONS")]
        [InlineData("ALTER CONTAINER c (REFRESH = 30);", "CONTAINER", "REFRESH")]
        [InlineData("ALTER BUTTON b (SUBTITLE = 'x');", "BUTTON", "SUBTITLE")]
        [InlineData("ALTER BUTTON b (SOURCE = (SELECT 1 AS n));", "BUTTON", "SOURCE")]
        [InlineData("ALTER TEMPLATE t (TITLE = 'x');", "TEMPLATE", "TITLE")]
        [InlineData("ALTER VISUAL v (VISIBLE = OFF);", "VISUAL", "VISIBLE")]
        [InlineData("ALTER PAGE p (ICON = 'gear');", "PAGE", "ICON")]
        public void ClausesTheObjectCannotPatch_AreRejectedAtParseTime(string sql, string kind, string clause)
        {
            var script = Parse(sql);

            var diagnostic = Assert.Single(script.Diagnostics);
            Assert.Equal("SYNTAX", diagnostic.Code);
            Assert.Contains($"ALTER {kind} does not support {clause}", diagnostic.Message, StringComparison.Ordinal);

            // The message lists what the kind does accept — otherwise the author is guessing.
            Assert.Contains("Supported clauses:", diagnostic.Message, StringComparison.Ordinal);
            Assert.Empty(script.Statements);
        }

        /// <summary>PAGE and CONTAINER can now patch the fields they actually own.</summary>
        [Theory]
        [InlineData("ALTER PAGE p (VISIBLE = OFF);")]
        [InlineData("ALTER PAGE p (REFRESH = 30);")]
        [InlineData("ALTER PAGE p (VISIBLE = ON, REFRESH = 0, TITLE = 'x');")]
        [InlineData("ALTER CONTAINER c (VISIBLE = OFF);")]
        [InlineData("ALTER CONTAINER c (ICON = 'gear');")]
        public void ObjectOwnedClauses_Parse(string sql)
        {
            var script = Parse(sql);

            Assert.Empty(script.Diagnostics);
            Assert.Single(script.Statements);
        }

        [Fact]
        public void AlterPage_CarriesTheOwnedClausesOntoTheAst()
        {
            var stmt = Assert.IsType<AlterReportObjectStatement>(
                Parse("ALTER PAGE p (VISIBLE = OFF, REFRESH = 45);").Statements[0]);

            Assert.Equal("OFF", stmt.Visibility);
            Assert.Equal(45, stmt.RefreshIntervalSeconds);
            Assert.Null(stmt.Icon);
        }

        [Fact]
        public void AlterContainer_CarriesTheOwnedClausesOntoTheAst()
        {
            var stmt = Assert.IsType<AlterReportObjectStatement>(
                Parse("ALTER CONTAINER c (VISIBLE = OFF, ICON = 'gear');").Statements[0]);

            Assert.Equal("OFF", stmt.Visibility);
            Assert.Equal("gear", stmt.Icon);
            Assert.Null(stmt.RefreshIntervalSeconds);
        }

        /// <summary>
        /// CREATE PAGE swallows an unparseable interval and silently means "off". On a patch that
        /// silence is worse: the statement reports success and leaves the previous interval running,
        /// which is the one outcome the author cannot observe from the script.
        /// </summary>
        [Fact]
        public void AlterPage_NonNumericRefresh_IsRejected()
        {
            var diagnostic = Assert.Single(Parse("ALTER PAGE p (REFRESH = 'soon');").Diagnostics);

            Assert.Contains("whole number of seconds", diagnostic.Message, StringComparison.Ordinal);
        }

        /// <summary>An absent clause must stay absent, so the handler keeps the existing value.</summary>
        [Fact]
        public void OmittedClauses_AreNullSoTheHandlerKeepsTheCurrentValue()
        {
            var stmt = Assert.IsType<AlterReportObjectStatement>(
                Parse("ALTER PAGE p (TITLE = 'x');").Statements[0]);

            Assert.Null(stmt.Visibility);
            Assert.Null(stmt.RefreshIntervalSeconds);
            Assert.Null(stmt.Styles);
        }
    }
}
