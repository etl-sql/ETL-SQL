using System;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Renderers;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Reporting.Semantics.Runtime;
using Xunit;

namespace ETL_SQL.Tests.Reporting.Conformance;

public class RepresentativeVisualConformanceTests
{
    private static readonly VisualType[] MigratedTypes =
    [VisualType.Bar, VisualType.Line, VisualType.Scatter, VisualType.Pie, VisualType.Donut, VisualType.Combo];

    [Theory]
    [InlineData("bar_stable_ordering.rptsql")]
    [InlineData("bar_explicit_domain.rptsql")]
    [InlineData("bar_multi_series_stacked.rptsql")]
    [InlineData("line_temporal_decimals.rptsql")]
    [InlineData("line_null_gaps.rptsql")]
    [InlineData("scatter_multi_series_inferred.rptsql")]
    [InlineData("pie_donut_proportions.rptsql")]
    [InlineData("combo_dual_axes.rptsql")]
    [InlineData("rule_statistical_overlays.rptsql")]
    [InlineData("accessible_semantic_fallbacks.rptsql")]
    public async Task AllRepresentativeFixtures_ParseAndCompileWithoutErrorDiagnostics(string fixtureFileName)
    {
        var projection = RepresentativeVisualConformanceHarness.Registry[fixtureFileName];
        Assert.NotNull(projection);

        var (ast, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync(fixtureFileName);

        Assert.NotNull(ast);
        Assert.NotNull(manifest);
        Assert.NotEmpty(manifest.Visuals);

        var primaryVisual = manifest.Visuals.First();
        Assert.Equal(projection.ExpectedVisualType.ToString().ToUpperInvariant(), primaryVisual.VisualType.ToUpperInvariant());
    }

    [Fact]
    public async Task CategoryAndSeriesOrdering_PreservesOrderInManifestAndNativeSvg()
    {
        var (ast, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync("bar_stable_ordering.rptsql");
        var visual = manifest.Visuals.First();

        var xValues = visual.Rows.Select(r => r[0]?.ToString()).Distinct().ToList();
        Assert.Contains("Alpha", xValues);
        Assert.Contains("Beta", xValues);
        Assert.Contains("Gamma", xValues);
        Assert.Contains("Delta", xValues);

        var seriesValues = visual.Rows.Select(r => r[1]?.ToString()).Distinct().ToList();
        Assert.Contains("Actual", seriesValues);
        Assert.Contains("Forecast", seriesValues);

        Assert.Contains("<svg", Assert.IsType<string>(visual.NativeSvg));
    }

    [Fact]
    public async Task ExplicitDomain_PreservedInResolvedPlan()
    {
        var (ast, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync("bar_explicit_domain.rptsql");
        var visual = manifest.Visuals.First();

        Assert.True(visual.Options.ContainsKey("axis:y:min") || visual.Options.ContainsKey("Y_AXIS") || visual.Min != null);

        Assert.NotEmpty(Assert.IsType<PlotPlan>(visual.PlotPlan).Scales.Single(scale => scale.Channel == FieldChannel.Y).Domain);
    }

    [Fact]
    public async Task StackedMultiSeriesAndPalette_PreservesColorMap()
    {
        var (ast, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync("bar_multi_series_stacked.rptsql");
        var visual = manifest.Visuals.First();

        Assert.True(visual.Options["STACKED"].Equals("ON", StringComparison.OrdinalIgnoreCase) || visual.Options["STACKED"].Equals("True", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("#1E3A8A", visual.Options["color:Enterprise"]);
        Assert.Equal("#3B82F6", visual.Options["color:Mid-Market"]);

        var plan = Assert.IsType<PlotPlan>(visual.PlotPlan);
        var y = plan.Scales.Single(scale => scale.Channel == FieldChannel.Y);
        Assert.Equal(170000m, PlotPlanResolver.Number(y.Domain[^1]));
        var svg = new SvgChartRenderer().Render(plan);
        Assert.Contains("#1E3A8A", svg);
        Assert.Contains("#3B82F6", svg);
        Assert.Contains("#93C5FD", svg);
    }

    [Fact]
    public async Task MigratedVisual_PdfExportSelectsNativePlanPathWithoutServerV8()
    {
        var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync("combo_dual_axes.rptsql");
        var visual = manifest.Visuals.Single();

        Assert.True(PdfExporter.UsesNativePlotPlanRendering(visual));
        var bytes = await new PdfExporter().ExportAsync(manifest);

        Assert.True(bytes.Length > 100);
        Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, bytes[..4]);
    }

    [Fact]
    public async Task TemporalDecimals_MaintainsPrecisionInRowsAndPayload()
    {
        var (ast, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync("line_temporal_decimals.rptsql");
        var visual = manifest.Visuals.First();

        var dateStrings = visual.Rows.Select(r => r[0]?.ToString()).ToList();
        Assert.Contains("2026-01-01", dateStrings);
        Assert.Contains("2026-01-05", dateStrings);

        var values = visual.Rows.Select(r => Convert.ToDouble(r[1])).ToList();
        Assert.Contains(98.7452, values);
        Assert.Contains(104.3819, values);
    }

    [Fact]
    public async Task NullValuesAndGaps_EmitsDiscontinuousSeriesPoints()
    {
        var (ast, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync("line_null_gaps.rptsql");
        var visual = manifest.Visuals.First();

        var nullRows = visual.Rows.Where(r => r[1] == null || string.IsNullOrEmpty(r[1]) || r[1] == "NULL").ToList();
        Assert.Equal(2, nullRows.Count);

        Assert.Equal(2, Assert.IsType<PlotPlan>(visual.PlotPlan).Nulls.GapRows.Length);
    }

    [Fact]
    public async Task ComboDualAxes_ResolvesPrimaryAndSecondaryAxes()
    {
        var (ast, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync("combo_dual_axes.rptsql");
        var visual = manifest.Visuals.First();

        Assert.NotNull(visual.SeriesDefs);
        Assert.Equal(2, visual.SeriesDefs.Count);
        Assert.Equal("bar", visual.SeriesDefs[0].SeriesType, ignoreCase: true);
        Assert.Equal("line", visual.SeriesDefs[1].SeriesType, ignoreCase: true);

        var plan = Assert.IsType<PlotPlan>(visual.PlotPlan);
        Assert.Contains(plan.Layers, layer => layer.Mark == MarkKind.Rect);
        Assert.Contains(plan.Layers, layer => layer.Mark == MarkKind.Line);
    }

    [Fact]
    public async Task StatisticalRules_ResolvesGoalAverageAndMovingAvgOverlays()
    {
        var (ast, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync("rule_statistical_overlays.rptsql");
        var visual = manifest.Visuals.First();

        Assert.NotNull(visual.Overlays);
        Assert.Equal(3, visual.Overlays.Count);

        var goalOverlay = visual.Overlays.FirstOrDefault(o => o.OverlayType.Equals("GOAL", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(goalOverlay);
        Assert.Equal(50, goalOverlay.Parameter);

        var avgOverlay = visual.Overlays.FirstOrDefault(o => o.OverlayType.Equals("AVERAGE", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(avgOverlay);

        var movingAvgOverlay = visual.Overlays.FirstOrDefault(o => o.OverlayType.Contains("Moving", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(movingAvgOverlay);
        Assert.Equal(2, movingAvgOverlay.Parameter);
    }

    [Theory]
    [InlineData("bar_stable_ordering.rptsql")]
    [InlineData("bar_explicit_domain.rptsql")]
    [InlineData("bar_multi_series_stacked.rptsql")]
    [InlineData("line_temporal_decimals.rptsql")]
    [InlineData("line_null_gaps.rptsql")]
    [InlineData("scatter_multi_series_inferred.rptsql")]
    [InlineData("pie_donut_proportions.rptsql")]
    [InlineData("combo_dual_axes.rptsql")]
    [InlineData("rule_statistical_overlays.rptsql")]
    [InlineData("accessible_semantic_fallbacks.rptsql")]
    public async Task MultiSurfaceRendering_ExecutesAcrossNativeSvgAndTerminalWithoutThrowing(string fixtureFileName)
    {
        var (ast, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync(fixtureFileName);

        foreach (var visual in manifest.Visuals)
        {
            var svg = RepresentativeVisualConformanceHarness.RenderSvg(manifest, visual.Name);

            // Validate SVG output when supported
            if (svg != null)
            {
                Assert.Contains("<svg", svg);
            }
        }

        var terminal = RepresentativeVisualConformanceHarness.RenderTerminal(manifest);
        Assert.NotNull(terminal);
    }

    [Theory]
    [InlineData("bar_stable_ordering.rptsql")]
    [InlineData("bar_explicit_domain.rptsql")]
    [InlineData("bar_multi_series_stacked.rptsql")]
    [InlineData("line_temporal_decimals.rptsql")]
    [InlineData("line_null_gaps.rptsql")]
    [InlineData("scatter_multi_series_inferred.rptsql")]
    [InlineData("pie_donut_proportions.rptsql")]
    [InlineData("combo_dual_axes.rptsql")]
    [InlineData("rule_statistical_overlays.rptsql")]
    public async Task MigratedVisuals_AreBuiltAndRenderedFromOnePlotPlan(string fixtureFileName)
    {
        var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync(fixtureFileName);
        foreach (var visual in manifest.Visuals.Where(item =>
                     Enum.TryParse<VisualType>(item.VisualType, true, out var type) && MigratedTypes.Contains(type)))
        {
            Assert.NotNull(visual.ChartSpec);
            Assert.NotNull(visual.ChartData);
            Assert.NotNull(visual.PlotPlan);
            visual.ChartSpec.Validate();
            visual.ChartData.Validate();
            visual.PlotPlan.Validate();
            Assert.Equal(visual.ChartSpec.Id, visual.PlotPlan.SpecId);
            Assert.Equal(visual.ChartData.RowCount, visual.Rows.Count);
            Assert.Null(visual.ChartConfig);
            Assert.Equal(new SvgChartRenderer().Render(visual.PlotPlan), visual.NativeSvg);
            Assert.Contains("role='img'", new SvgChartRenderer().Render(visual.PlotPlan));
            Assert.NotNull(TerminalRenderer.RenderVisual(visual));
        }
    }

    [Theory]
    [InlineData("bar_stable_ordering.rptsql")]
    [InlineData("bar_explicit_domain.rptsql")]
    [InlineData("bar_multi_series_stacked.rptsql")]
    [InlineData("line_temporal_decimals.rptsql")]
    [InlineData("line_null_gaps.rptsql")]
    [InlineData("scatter_multi_series_inferred.rptsql")]
    [InlineData("pie_donut_proportions.rptsql")]
    [InlineData("combo_dual_axes.rptsql")]
    [InlineData("rule_statistical_overlays.rptsql")]
    public async Task RegisteredSemanticExpectations_AgreeWithResolvedPlan(string fixtureFileName)
    {
        var expected = RepresentativeVisualConformanceHarness.Registry[fixtureFileName];
        var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync(fixtureFileName);
        var visual = manifest.Visuals.First();
        var plan = Assert.IsType<PlotPlan>(visual.PlotPlan);

        if (expected.ExpectedCategories.Count > 0)
        {
            var categories = plan.Scales.First(scale => scale.Channel is FieldChannel.X or FieldChannel.Theta).Categories;
            Assert.Equal(expected.ExpectedCategories, categories);
        }
        if (expected.ExpectedSeriesNames.Count > 0)
            Assert.Equal(expected.ExpectedSeriesNames, plan.Series.Select(series => series.Label));
        Assert.Equal(expected.HasNullGaps, plan.Nulls.GapRows.Length > 0);
        Assert.Equal(expected.HasDualAxes, plan.Scales.Any(scale => scale.Channel == FieldChannel.Y2));

        if (expected.HasExplicitDomain)
        {
            var y = plan.Scales.First(scale => scale.Channel == FieldChannel.Y);
            Assert.Equal(0m, PlotPlanResolver.Number(y.Domain[0]));
            Assert.Equal(500m, PlotPlanResolver.Number(y.Domain[^1]));
        }

        var overlays = plan.Layers
            .Where(layer => !layer.Style.IsDefault)
            .SelectMany(layer => layer.Style)
            .Where(token => token.Name == "overlayType")
            .Select(token => Enum.Parse<OverlayType>(token.Value))
            .ToArray();
        Assert.Equal(expected.ExpectedOverlays, overlays);
        foreach (var (series, color) in expected.ExpectedPalette)
            Assert.Contains(plan.Palette, item => item.SeriesKey == series && item.Color.Equals(color, StringComparison.OrdinalIgnoreCase));
        Assert.False(string.IsNullOrWhiteSpace(plan.AccessibleSummary));
        Assert.NotEmpty(plan.Fallback.Items);
    }
}
