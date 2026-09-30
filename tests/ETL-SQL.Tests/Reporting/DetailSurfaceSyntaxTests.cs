using System.Collections.Generic;
using ETL_SQL.Core;
using ETL_SQL.Core.Formatting;
using ETL_SQL.Core.Parser;
using Xunit;

namespace ETL_SQL.Tests.Reporting
{
    /// <summary>
    /// Parser and formatter contract for the three canonical detail-surface forms. Each form
    /// must survive a parse → format → re-parse cycle unchanged, so the formatter can never
    /// quietly drop a clause the parser accepted — the failure mode that let inline
    /// <c>VISUALS</c> reach manifests that no renderer honoured.
    /// </summary>
    public class DetailSurfaceSyntaxTests
    {
        private static CreateVisualStatement Parse(string script)
        {
            var tokens = new Lexer(script).Tokenize();
            return (CreateVisualStatement)new Parser(tokens, script).ParseStatement();
        }

        private static string Visual(string tooltipClause) => $@"
            CREATE VISUAL BarWithTooltip AS BAR (
                SOURCE = (SELECT Month, Revenue FROM #sales),
                MAPPINGS (X = Month, Y = Revenue),
                {tooltipClause}
            );";

        /// <summary>Parses, formats, and re-parses, returning the second parse.</summary>
        private static (CreateVisualStatement First, CreateVisualStatement Second, string Formatted) RoundTrip(
            string tooltipClause)
        {
            var first = Parse(Visual(tooltipClause));
            var formatted = AstSerializer.Format(first);
            var second = Parse(formatted.TrimEnd(';', '\r', '\n', ' ') + ";");
            return (first, second, formatted);
        }

        // ── Text form ──────────────────────────────────────────────────────────

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void TextForm_ParsesAsATransientTooltip()
        {
            var visual = Parse(Visual("TOOLTIP = 'Revenue for the month'"));

            Assert.NotNull(visual.Tooltip);
            Assert.Equal(DetailSurfaceKind.Transient, visual.Tooltip!.Kind);
            Assert.False(visual.Tooltip.IsInline);
            Assert.Equal("'Revenue for the month'", visual.Tooltip.PlainText!.ToSql());
        }

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void TextForm_SurvivesAFormatterRoundTrip()
        {
            var (first, second, formatted) = RoundTrip("TOOLTIP = 'Revenue for the month'");

            Assert.Contains("TOOLTIP", formatted);
            Assert.Equal(first.Tooltip!.PlainText!.ToSql(), second.Tooltip!.PlainText!.ToSql());
            Assert.Equal(first.Tooltip.Kind, second.Tooltip.Kind);
        }

        // ── Referenced-container form ──────────────────────────────────────────

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void ContainerForm_ParsesAsAPersistentPopover()
        {
            var visual = Parse(Visual("TOOLTIP = TooltipBox"));

            Assert.Equal("TooltipBox", visual.Tooltip!.ContainerRef);
            Assert.Equal(DetailSurfaceKind.Persistent, visual.Tooltip.Kind);
        }

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void ContainerForm_SurvivesAFormatterRoundTrip()
        {
            var (first, second, formatted) = RoundTrip("TOOLTIP = TooltipBox");

            Assert.Contains("TooltipBox", formatted);
            Assert.Equal(first.Tooltip!.ContainerRef, second.Tooltip!.ContainerRef);
            Assert.Equal(DetailSurfaceKind.Persistent, second.Tooltip.Kind);
        }

        // ── Inline form ────────────────────────────────────────────────────────

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void InlineForm_WithVisuals_ParsesAsAPersistentPopover()
        {
            var visual = Parse(Visual("TOOLTIP ('**Detail**', VISUALS (MonthDetail, RegionDetail))"));

            var tooltip = visual.Tooltip!;
            Assert.True(tooltip.IsInline);
            Assert.Equal("**Detail**", tooltip.InlineMarkdown);
            Assert.Equal(new List<string> { "MonthDetail", "RegionDetail" }, tooltip.InlineVisuals);
            Assert.Equal(DetailSurfaceKind.Persistent, tooltip.Kind);
        }

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void InlineForm_SurvivesAFormatterRoundTrip()
        {
            var (first, second, formatted) = RoundTrip("TOOLTIP ('**Detail**', VISUALS (MonthDetail, RegionDetail))");

            // The visual list is the part that must not be dropped: it is what makes this a
            // popover rather than a text tooltip.
            Assert.Contains("VISUALS", formatted);
            Assert.Contains("MonthDetail", formatted);
            Assert.Contains("RegionDetail", formatted);
            Assert.Equal(first.Tooltip!.InlineVisuals, second.Tooltip!.InlineVisuals);
            Assert.Equal(first.Tooltip.InlineMarkdown, second.Tooltip.InlineMarkdown);
            Assert.Equal(DetailSurfaceKind.Persistent, second.Tooltip.Kind);
        }

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void InlineForm_MarkdownOnly_StaysTransientAcrossARoundTrip()
        {
            var (first, second, _) = RoundTrip("TOOLTIP ('**Detail**')");

            Assert.True(first.Tooltip!.IsInline);
            Assert.Equal(DetailSurfaceKind.Transient, first.Tooltip.Kind);
            Assert.True(second.Tooltip!.IsInline);
            Assert.Equal(DetailSurfaceKind.Transient, second.Tooltip.Kind);
        }

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void InlineForm_VisualsWithoutMarkdown_IsAccepted()
        {
            var visual = Parse(Visual("TOOLTIP (VISUALS (MonthDetail))"));

            Assert.Null(visual.Tooltip!.InlineMarkdown);
            Assert.Equal(new List<string> { "MonthDetail" }, visual.Tooltip.InlineVisuals);
            Assert.Equal(DetailSurfaceKind.Persistent, visual.Tooltip.Kind);
        }

        // ── Fields and visuals together ───────────────────────────────────────

        /// <summary>
        /// visual.md documents FIELDS before VISUALS, and the formatter writes VISUALS, FIELDS with a
        /// comma. The parser used to accept only VISUALS FIELDS with no comma, so neither form parsed.
        /// </summary>
        [Theory]
        [InlineData("TOOLTIP ('Details', FIELDS (Revenue FORMAT 'C0'), VISUALS (DetailChart))")]
        [InlineData("TOOLTIP ('Details', VISUALS (DetailChart), FIELDS (Revenue FORMAT 'C0'))")]
        [InlineData("TOOLTIP (FIELDS (Revenue FORMAT 'C0'), VISUALS (DetailChart))")]
        [InlineData("TOOLTIP ('Details', VISUALS (DetailChart) FIELDS (Revenue FORMAT 'C0'))")]
        public void InlineForm_FieldsAndVisualsParseInEitherOrderAndRoundTrip(string clause)
        {
            var (first, second, _) = RoundTrip(clause);

            foreach (var tooltip in new[] { first.Tooltip!, second.Tooltip! })
            {
                Assert.Equal(["DetailChart"], tooltip.InlineVisuals!);
                var field = Assert.Single(tooltip.Fields!);
                Assert.Equal(("Revenue", "C0"), (field.Name, field.Format));
            }
        }

        [Theory]
        [InlineData("TOOLTIP (FIELDS (Revenue), FIELDS (Month))")]
        [InlineData("TOOLTIP (VISUALS (A), VISUALS (B))")]
        public void InlineForm_RejectsARepeatedPart(string clause)
        {
            Assert.ThrowsAny<Exception>(() => Parse(Visual(clause)));
        }

        // ── The real sample ────────────────────────────────────────────────────

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void KitchenSinkSampleForm_RoundTrips()
        {
            // Exactly the clause in samples/10_Kitchen_Sinks/01_BAR.rptsql.
            var (first, second, _) = RoundTrip("TOOLTIP = TooltipBox");

            Assert.Equal("TooltipBox", first.Tooltip!.ContainerRef);
            Assert.Equal("TooltipBox", second.Tooltip!.ContainerRef);
        }
    }
}
