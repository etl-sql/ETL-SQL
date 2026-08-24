using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Reporting;
using Xunit;

namespace ETL_SQL.Tests.Reporting
{
    /// <summary>
    /// Static-safety contract for detail surfaces (TOOLTIP clauses). Every numeric budget is
    /// asserted at limit-1, limit, and limit+1 so a boundary can never silently drift.
    /// </summary>
    public class DetailSurfaceResolverTests
    {
        // ── Fixtures ────────────────────────────────────────────────────────────

        private static LiteralExpression Lit(string value) =>
            new(value, ETL_SQL.Core.Parser.TokenType.STRING_LITERAL);

        private static CreateVisualStatement Visual(string name, TooltipDefinition? tooltip = null) =>
            new()
            {
                Name = name,
                VisualType = VisualType.Bar,
                Source = new VisualSourceExpression { TempTableName = "#detail" },
                Tooltip = tooltip
            };

        private static CreateContainerStatement Container(
            string name,
            IEnumerable<string> children,
            TooltipDefinition? tooltip = null)
        {
            var map = new Dictionary<string, string>();
            int slot = 0;
            foreach (var child in children)
                map[((char)('A' + slot++)).ToString()] = child;

            return new CreateContainerStatement
            {
                Name = name,
                ContainerType = "BOX",
                SlotMap = map,
                Tooltip = tooltip
            };
        }

        private static Dictionary<string, T> Map<T>(params (string Name, T Value)[] items)
        {
            var d = new Dictionary<string, T>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var (name, value) in items) d[name] = value;
            return d;
        }

        private static List<DetailSurfaceDiagnostic> ResolveOne(
            TooltipDefinition tooltip,
            Dictionary<string, CreateVisualStatement> visuals,
            Dictionary<string, CreateContainerStatement> containers,
            out ResolvedDetailSurface surface,
            string owner = "Owner")
        {
            var diagnostics = new List<DetailSurfaceDiagnostic>();
            surface = DetailSurfaceResolver.Resolve(owner, tooltip, visuals, containers, diagnostics);
            return diagnostics;
        }

        private static void AssertCode(IEnumerable<DetailSurfaceDiagnostic> diagnostics, string code)
            => Assert.Contains(diagnostics, d => d.Code == code && d.Severity == DetailSurfaceSeverity.Error);

        private static void AssertNoErrors(IEnumerable<DetailSurfaceDiagnostic> diagnostics)
        {
            var errors = diagnostics.Where(d => d.Severity == DetailSurfaceSeverity.Error).ToList();
            Assert.True(errors.Count == 0, "unexpected errors: " + string.Join("; ", errors));
        }

        // ── Kind contract: transient tooltip vs persistent popover ──────────────

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void TextTooltip_IsTransient()
        {
            var tooltip = TooltipDefinition.Text(Lit("Revenue for the month"));
            Assert.Equal(DetailSurfaceKind.Transient, tooltip.Kind);
        }

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void ContainerTooltip_IsPersistentPopover()
        {
            Assert.Equal(DetailSurfaceKind.Persistent, TooltipDefinition.Container("Box").Kind);
        }

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void InlineTooltip_WithVisuals_IsPersistentPopover()
        {
            var tooltip = TooltipDefinition.Inline("**Detail**", new List<string> { "Trend" });
            Assert.Equal(DetailSurfaceKind.Persistent, tooltip.Kind);
        }

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void InlineTooltip_MarkdownOnly_StaysTransient()
        {
            // Markdown with no visuals carries no interactive descendants, so it remains a
            // transient tooltip rather than becoming a focusable dialog.
            var tooltip = TooltipDefinition.Inline("**Detail**", new List<string>());
            Assert.Equal(DetailSurfaceKind.Transient, tooltip.Kind);
        }

        // ── Missing references ─────────────────────────────────────────────────

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void MissingContainer_ProducesActionableDiagnostic()
        {
            var diagnostics = ResolveOne(
                TooltipDefinition.Container("NoSuchBox"),
                Map<CreateVisualStatement>(),
                Map<CreateContainerStatement>(),
                out var surface);

            AssertCode(diagnostics, DetailSurfaceDiagnostics.MissingContainer);
            Assert.False(surface.IsValid);
            Assert.Contains("NoSuchBox", diagnostics[0].Message);
            Assert.Contains("CREATE CONTAINER", diagnostics[0].Message);
        }

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void MissingVisual_InInlineList_ProducesActionableDiagnostic()
        {
            var diagnostics = ResolveOne(
                TooltipDefinition.Inline(null, new List<string> { "Ghost" }),
                Map<CreateVisualStatement>(),
                Map<CreateContainerStatement>(),
                out var surface);

            AssertCode(diagnostics, DetailSurfaceDiagnostics.MissingVisual);
            Assert.False(surface.IsValid);
        }

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void MissingVisual_InReferencedContainer_ProducesDiagnostic()
        {
            var diagnostics = ResolveOne(
                TooltipDefinition.Container("Box"),
                Map<CreateVisualStatement>(),
                Map(("Box", Container("Box", new[] { "Ghost" }))),
                out _);

            AssertCode(diagnostics, DetailSurfaceDiagnostics.MissingVisual);
        }

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void EmptyInlineSurface_IsRejected()
        {
            var diagnostics = ResolveOne(
                TooltipDefinition.Inline(null, new List<string>()),
                Map<CreateVisualStatement>(),
                Map<CreateContainerStatement>(),
                out _);

            AssertCode(diagnostics, DetailSurfaceDiagnostics.EmptyInlineSurface);
        }

        // ── Cycles ─────────────────────────────────────────────────────────────

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void DirectCycle_IsDetected()
        {
            var diagnostics = ResolveOne(
                TooltipDefinition.Container("A"),
                Map<CreateVisualStatement>(),
                Map(("A", Container("A", new[] { "A" }))),
                out var surface);

            AssertCode(diagnostics, DetailSurfaceDiagnostics.Cycle);
            Assert.False(surface.IsValid);
        }

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void IndirectCycle_IsDetected()
        {
            var diagnostics = ResolveOne(
                TooltipDefinition.Container("A"),
                Map<CreateVisualStatement>(),
                Map(
                    ("A", Container("A", new[] { "B" })),
                    ("B", Container("B", new[] { "A" }))),
                out var surface);

            AssertCode(diagnostics, DetailSurfaceDiagnostics.Cycle);
            Assert.False(surface.IsValid);
        }

        // ── Nested detail surfaces ─────────────────────────────────────────────

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void ContainerInsideDetailSurface_DeclaringItsOwnTooltip_IsRejected()
        {
            var diagnostics = ResolveOne(
                TooltipDefinition.Container("Outer"),
                Map<CreateVisualStatement>(),
                Map(("Outer", Container("Outer", System.Array.Empty<string>(),
                    TooltipDefinition.Text(Lit("nested"))))),
                out _);

            AssertCode(diagnostics, DetailSurfaceDiagnostics.NestedDetailSurface);
        }

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void VisualInsideDetailSurface_DeclaringItsOwnTooltip_IsRejected()
        {
            var diagnostics = ResolveOne(
                TooltipDefinition.Container("Box"),
                Map(("Detail", Visual("Detail", TooltipDefinition.Text(Lit("nested"))))),
                Map(("Box", Container("Box", new[] { "Detail" }))),
                out _);

            AssertCode(diagnostics, DetailSurfaceDiagnostics.NestedDetailSurface);
        }

        // ── Nesting depth boundary ─────────────────────────────────────────────

        /// <summary>Builds a chain of containers C1 -> C2 -> ... -> Cn ending in a visual.</summary>
        private static Dictionary<string, CreateContainerStatement> Chain(int levels)
        {
            var containers = new Dictionary<string, CreateContainerStatement>(System.StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i <= levels; i++)
            {
                string child = i < levels ? $"C{i + 1}" : "Leaf";
                containers[$"C{i}"] = Container($"C{i}", new[] { child });
            }
            return containers;
        }

        [Theory]
        [Trait("Category", "Smoke.Reporting")]
        [InlineData(DetailSurfaceLimits.MaxNestingDepth - 1, false)]
        [InlineData(DetailSurfaceLimits.MaxNestingDepth, false)]
        [InlineData(DetailSurfaceLimits.MaxNestingDepth + 1, true)]
        public void NestingDepth_BoundaryIsEnforced(int levels, bool shouldFail)
        {
            var diagnostics = ResolveOne(
                TooltipDefinition.Container("C1"),
                Map(("Leaf", Visual("Leaf"))),
                Chain(levels),
                out var surface);

            if (shouldFail)
            {
                AssertCode(diagnostics, DetailSurfaceDiagnostics.DepthExceeded);
                Assert.False(surface.IsValid);
            }
            else
            {
                AssertNoErrors(diagnostics);
                Assert.Equal(levels, surface.Depth);
            }
        }

        // ── Visual-count boundary ──────────────────────────────────────────────

        [Theory]
        [Trait("Category", "Smoke.Reporting")]
        [InlineData(DetailSurfaceLimits.MaxVisuals - 1, false)]
        [InlineData(DetailSurfaceLimits.MaxVisuals, false)]
        [InlineData(DetailSurfaceLimits.MaxVisuals + 1, true)]
        public void VisualCount_BoundaryIsEnforced(int count, bool shouldFail)
        {
            var names = Enumerable.Range(0, count).Select(i => $"V{i}").ToList();
            var visuals = new Dictionary<string, CreateVisualStatement>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var n in names) visuals[n] = Visual(n);

            var diagnostics = ResolveOne(
                TooltipDefinition.Inline(null, names),
                visuals,
                Map<CreateContainerStatement>(),
                out var surface);

            if (shouldFail)
            {
                AssertCode(diagnostics, DetailSurfaceDiagnostics.VisualCountExceeded);
                Assert.False(surface.IsValid);
            }
            else
            {
                AssertNoErrors(diagnostics);
                Assert.Equal(count, surface.Visuals.Count);
            }
        }

        // ── Refresh-query boundary ─────────────────────────────────────────────

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void RefreshQueryCount_CountsEveryResolvedVisual()
        {
            var names = Enumerable.Range(0, 3).Select(i => $"V{i}").ToList();
            var visuals = new Dictionary<string, CreateVisualStatement>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var n in names) visuals[n] = Visual(n);

            ResolveOne(TooltipDefinition.Inline(null, names), visuals, Map<CreateContainerStatement>(),
                out var surface);

            Assert.Equal(3, surface.RefreshQueryCount);
        }

        // ── Aggregate per-report surface boundary ──────────────────────────────

        [Theory]
        [Trait("Category", "Smoke.Reporting")]
        [InlineData(DetailSurfaceLimits.MaxSurfacesPerReport - 1, false)]
        [InlineData(DetailSurfaceLimits.MaxSurfacesPerReport, false)]
        [InlineData(DetailSurfaceLimits.MaxSurfacesPerReport + 1, true)]
        public void ReportSurfaceCount_BoundaryIsEnforced(int count, bool shouldFail)
        {
            var visuals = new Dictionary<string, CreateVisualStatement>(System.StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < count; i++)
                visuals[$"V{i}"] = Visual($"V{i}", TooltipDefinition.Text(Lit("x")));

            var (surfaces, diagnostics) = DetailSurfaceResolver.ResolveReport(
                visuals, Map<CreateContainerStatement>());

            Assert.Equal(count, surfaces.Count);
            if (shouldFail)
                AssertCode(diagnostics, DetailSurfaceDiagnostics.SurfaceCountExceeded);
            else
                AssertNoErrors(diagnostics);
        }

        // ── Transient text-length boundary ─────────────────────────────────────

        [Theory]
        [Trait("Category", "Smoke.Reporting")]
        [InlineData(DetailSurfaceLimits.MaxTransientTextLength - 1, false)]
        [InlineData(DetailSurfaceLimits.MaxTransientTextLength, false)]
        [InlineData(DetailSurfaceLimits.MaxTransientTextLength + 1, true)]
        public void TransientTextLength_BoundaryIsEnforced(int length, bool shouldFail)
        {
            var diagnostics = ResolveOne(
                TooltipDefinition.Text(Lit(new string('x', length))),
                Map<CreateVisualStatement>(),
                Map<CreateContainerStatement>(),
                out var surface);

            if (shouldFail)
            {
                AssertCode(diagnostics, DetailSurfaceDiagnostics.TransientTextTooLong);
                Assert.False(surface.IsValid);
            }
            else
            {
                AssertNoErrors(diagnostics);
            }
        }

        // ── The real kitchen-sink shape ────────────────────────────────────────

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void KitchenSinkShape_BarWithTooltip_ResolvesCleanly()
        {
            // Mirrors samples/10_Kitchen_Sinks/01_BAR.rptsql:
            //   CREATE CONTAINER TooltipBox AS BOX (LAYOUT (MAP ('A' = MonthDetail)))
            //   CREATE VISUAL BarWithTooltip AS BAR (..., TOOLTIP = TooltipBox)
            var visuals = Map(
                ("MonthDetail", Visual("MonthDetail")),
                ("BarWithTooltip", Visual("BarWithTooltip", TooltipDefinition.Container("TooltipBox"))));
            var containers = Map(("TooltipBox", Container("TooltipBox", new[] { "MonthDetail" })));

            var (surfaces, diagnostics) = DetailSurfaceResolver.ResolveReport(visuals, containers);

            AssertNoErrors(diagnostics);
            var surface = Assert.Single(surfaces);
            Assert.Equal("BarWithTooltip", surface.OwnerObject);
            Assert.Equal(DetailSurfaceKind.Persistent, surface.Kind);
            Assert.Equal(new[] { "TooltipBox" }, surface.Containers);
            Assert.Equal(new[] { "MonthDetail" }, surface.Visuals);
            Assert.Equal(1, surface.Depth);
            Assert.Equal(2, surface.NodeCount);
            Assert.Equal(1, surface.RefreshQueryCount);
            Assert.True(surface.IsValid);
        }
    }
}
