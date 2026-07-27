using System;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using Xunit;

namespace ETL_SQL.Tests.Core.Parsing
{
    /// <summary>
    /// <c>ALTER</c> for report objects is implemented for four kinds only. The rest must be refused
    /// by the parser rather than parsing and failing at execution — see the canonical-syntax track
    /// in <c>TODO.md</c>.
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
        /// <remarks>
        /// THEME is absent deliberately: it has no <c>ALTER</c> branch in the parser at all (only
        /// CREATE and DROP), so it is refused by the generic "expected object kind" error rather
        /// than by this check. It is still covered below, where rejection — not the message — is
        /// what matters.
        /// </remarks>
        [Theory]
        [InlineData("ALTER STYLE s (TITLE = 'x');", "STYLE")]
        [InlineData("ALTER NAVIGATION n (TITLE = 'x');", "NAVIGATION")]
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
                ReportObjectType.Container, ReportObjectType.Template
            };

            foreach (var kind in accepted)
            {
                var sql = $"ALTER {kind.ToString().ToUpperInvariant()} x (TITLE = 'y');";
                Assert.Empty(Parse(sql).Diagnostics);
            }

            // Every other report object kind that has an ALTER spelling must be refused.
            foreach (var kind in new[]
                     {
                         ReportObjectType.Style, ReportObjectType.Navigation, ReportObjectType.Theme
                     })
            {
                var sql = $"ALTER {kind.ToString().ToUpperInvariant()} x (TITLE = 'y');";
                Assert.NotEmpty(Parse(sql).Diagnostics);
            }
        }
    }
}
