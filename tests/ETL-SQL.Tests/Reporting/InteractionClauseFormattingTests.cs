using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Formatting;
using ETL_SQL.Core.Parser;
using Xunit;

namespace ETL_SQL.Tests.Reporting
{
    /// <summary>
    /// Formatting a visual keeps its interaction clauses. A format pass that drops one deletes the
    /// author's cross-filter or drill-through without a diagnostic, and the report still builds.
    /// Old behaviour (&lt;= v0.19): all three clauses were omitted from formatted output.
    /// </summary>
    [Trait("CompatBreak", "0.20")]
    public class InteractionClauseFormattingTests
    {
        private static CreateVisualStatement ParseVisual(string script) =>
            (CreateVisualStatement)new Parser(new Lexer(script).Tokenize(), script).ParseStatement();

        [Fact]
        public void FormattingAVisual_KeepsWhoItsSelectionFilters()
        {
            var visual = ParseVisual("""
                CREATE VISUAL ByRegion AS BAR (
                    SOURCE = #sales,
                    MAPPINGS (X = Region, Y = Amount),
                    EMIT_FILTER (TARGETS = (Detail, Totals))
                );
                """);

            var formatted = AstSerializer.Format(visual);

            Assert.Contains("EMIT_FILTER (TARGETS = (Detail, Totals))", formatted);
            var reparsed = ParseVisual(formatted);
            Assert.Equal("Detail,Totals", Assert.Single(reparsed.Options, o => o.Key == "EMIT_FILTER:TARGETS").Value);
        }

        [Fact]
        public void FormattingAVisual_KeepsHowItReceivesASelection()
        {
            var visual = ParseVisual("""
                CREATE VISUAL Detail AS TABLE (
                    SOURCE = #sales,
                    INTERACTIONS (ON_SELECT = HIGHLIGHT, MATCHING = Region)
                );
                """);

            var reparsed = ParseVisual(AstSerializer.Format(visual));

            Assert.Equal(
                visual.Interactions.Select(i => (i.Key, i.Value)),
                reparsed.Interactions.Select(i => (i.Key, i.Value)));
            Assert.Equal(2, reparsed.Interactions.Count);
        }

        [Fact]
        public void FormattingATable_KeepsItsRowDetail()
        {
            var visual = ParseVisual("""
                CREATE VISUAL Totals AS TABLE (
                    SOURCE = #sales,
                    ROW_DETAIL (TARGET = Detail, BINDINGS (@region = Region, @year = SaleYear), LIMIT = 50)
                );
                """);

            var formatted = AstSerializer.Format(visual);

            Assert.Contains("ROW_DETAIL (TARGET = Detail, BINDINGS (@region = Region, @year = SaleYear), LIMIT = 50)", formatted);
            var detail = ParseVisual(formatted).RowDetail;
            Assert.NotNull(detail);
            Assert.Equal("Detail", detail.TargetName);
            Assert.Equal(50, detail.Limit);
            Assert.Equal(
                new[] { ("Region", "region"), ("SaleYear", "year") },
                detail.Bindings.Select(b => (b.ParentColumn, b.ChildParameter)));
        }

        [Fact]
        public void ARowDetailWithoutBindingsOrLimit_FormatsAsJustItsTarget()
        {
            var visual = ParseVisual("""
                CREATE VISUAL Totals AS TABLE (SOURCE = #sales, ROW_DETAIL (TARGET = Detail));
                """);

            Assert.Contains("ROW_DETAIL (TARGET = Detail)", AstSerializer.Format(visual));
        }
    }
}
