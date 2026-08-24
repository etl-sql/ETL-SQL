using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Formatting;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Reporting;
using Xunit;

namespace ETL_SQL.Tests.Reporting
{
    /// <summary>
    /// Regression tests for the three v0.19 detail-surface compatibility breaks recorded in
    /// <c>BREAKING_CHANGES.md</c>. Each asserts the new behaviour and states what the old one
    /// was, so a future change that quietly restores the old behaviour fails here.
    /// </summary>
    [Trait("CompatBreak", "0.19")]
    public class DetailSurfaceCompatBreakTests
    {
        private static LiteralExpression Lit(string value) =>
            new(value, TokenType.STRING_LITERAL);

        private static CreateVisualStatement Visual(
            string name,
            TooltipDefinition? tooltip = null,
            params (string Role, string Column)[] mappings) =>
            new()
            {
                Name = name,
                VisualType = VisualType.Bar,
                Source = new VisualSourceExpression { TempTableName = "#detail" },
                Tooltip = tooltip,
                Mappings = mappings.Select(m => new VisualMapping { Role = m.Role, Column = m.Column }).ToList()
            };

        private static Dictionary<string, T> Map<T>(params (string Name, T Value)[] items)
        {
            var d = new Dictionary<string, T>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var (name, value) in items) d[name] = value;
            return d;
        }

        // ── Break 1: invalid detail surfaces fail the build ────────────────────

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void UnresolvableTooltipTarget_NowFailsInsteadOfPublishingSilently()
        {
            // Old behaviour (<= v0.18): resolution produced a manifest that every renderer
            // ignored, so the report published and the tooltip simply never appeared.
            var owner = Visual("Bar", TooltipDefinition.Container("NoSuchBox"), ("X", "Month"));
            var diagnostics = new List<DetailSurfaceDiagnostic>();

            var resolved = DetailSurfaceResolver.Resolve(
                "Bar", owner.Tooltip!, Map(("Bar", owner)), Map<CreateContainerStatement>(),
                diagnostics, owner);

            Assert.False(resolved.IsValid);
            Assert.Contains(diagnostics, d =>
                d.Code == DetailSurfaceDiagnostics.MissingContainer &&
                d.Severity == DetailSurfaceSeverity.Error);
        }

        // ── Break 2: explicit, non-secret row context is required ──────────────

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void PopoverWithoutARowContextMapping_NowFailsInsteadOfUsingTheFirstColumn()
        {
            // Old behaviour (<= v0.18): the browser fell back to columns[0] when no
            // row-context role was mapped, disclosing whatever happened to be first.
            var detail = Visual("Detail", null, ("X", "Region"));
            var owner = Visual("Bar", TooltipDefinition.Container("Box"), ("SIZE", "Weight"));
            var containers = Map(("Box", new CreateContainerStatement
            {
                Name = "Box",
                ContainerType = "BOX",
                SlotMap = new Dictionary<string, string> { ["A"] = "Detail" }
            }));
            var diagnostics = new List<DetailSurfaceDiagnostic>();

            var resolved = DetailSurfaceResolver.Resolve(
                "Bar", owner.Tooltip!, Map(("Bar", owner), ("Detail", detail)), containers,
                diagnostics, owner);

            Assert.False(resolved.IsValid);
            Assert.Contains(diagnostics, d => d.Code == DetailSurfaceDiagnostics.MissingRowContext);
        }

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void PopoverOverASecretColumn_NowFailsInsteadOfDisclosingIt()
        {
            var detail = Visual("Detail", null, ("X", "Region"));
            var owner = Visual("Bar", TooltipDefinition.Container("Box"), ("X", "ApiKey"));
            var containers = Map(("Box", new CreateContainerStatement
            {
                Name = "Box",
                ContainerType = "BOX",
                SlotMap = new Dictionary<string, string> { ["A"] = "Detail" }
            }));
            var diagnostics = new List<DetailSurfaceDiagnostic>();

            var resolved = DetailSurfaceResolver.Resolve(
                "Bar", owner.Tooltip!, Map(("Bar", owner), ("Detail", detail)), containers,
                diagnostics, owner);

            Assert.False(resolved.IsValid);
            var secret = Assert.Single(diagnostics, d =>
                d.Code == DetailSurfaceDiagnostics.SecretDisclosure);
            Assert.Contains("ApiKey", secret.Message);
        }

        // ── Break 3: the formatter preserves a visual's TOOLTIP ────────────────

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void FormattingAVisual_NowPreservesItsTooltipInsteadOfDeletingIt()
        {
            // Old behaviour (<= v0.18): FormatCreateVisual omitted the clause entirely, so
            // formatting a report silently deleted the author's detail surface.
            const string script = """
                CREATE VISUAL BarWithTooltip AS BAR (
                    SOURCE = (SELECT Month, Revenue FROM #sales),
                    MAPPINGS (X = Month, Y = Revenue),
                    TOOLTIP = TooltipBox
                );
                """;

            var visual = (CreateVisualStatement)new Parser(new Lexer(script).Tokenize(), script).ParseStatement();
            var formatted = AstSerializer.Format(visual);

            Assert.Contains("TOOLTIP", formatted);
            Assert.Contains("TooltipBox", formatted);
        }

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void FormattingAVisualWithoutATooltip_IsUnchanged()
        {
            // The break is strictly additive: visuals with no TOOLTIP format exactly as before.
            const string script = """
                CREATE VISUAL PlainBar AS BAR (
                    SOURCE = (SELECT Month, Revenue FROM #sales),
                    MAPPINGS (X = Month, Y = Revenue)
                );
                """;

            var visual = (CreateVisualStatement)new Parser(new Lexer(script).Tokenize(), script).ParseStatement();

            Assert.DoesNotContain("TOOLTIP", AstSerializer.Format(visual));
        }
    }
}
