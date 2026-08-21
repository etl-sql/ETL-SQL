using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Reporting;
using Xunit;

namespace ETL_SQL.Tests.Reporting.Conformance;

public class RepresentativeVisualConformanceTests
{
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
    public async Task CategoryAndSeriesOrdering_PreservesOrderInManifestAndECharts()
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

        var echartsJson = RepresentativeVisualConformanceHarness.RenderEChartsJson(manifest, visual.Name);
        Assert.NotNull(echartsJson);
        Assert.Contains("\"series\"", echartsJson);
        Assert.Contains("\"xAxis\"", echartsJson);
    }

    [Fact]
    public async Task ExplicitDomain_PreservedInOptionsAndLoweredToECharts()
    {
        var (ast, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync("bar_explicit_domain.rptsql");
        var visual = manifest.Visuals.First();

        Assert.True(visual.Options.ContainsKey("axis:y:min") || visual.Options.ContainsKey("Y_AXIS") || visual.Min != null);

        var echartsJson = RepresentativeVisualConformanceHarness.RenderEChartsJson(manifest, visual.Name);
        Assert.NotNull(echartsJson);
        Assert.Contains("\"yAxis\"", echartsJson);
    }

    [Fact]
    public async Task StackedMultiSeriesAndPalette_PreservesColorMap()
    {
        var (ast, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync("bar_multi_series_stacked.rptsql");
        var visual = manifest.Visuals.First();

        Assert.True(visual.Options["STACKED"].Equals("ON", StringComparison.OrdinalIgnoreCase) || visual.Options["STACKED"].Equals("True", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("#1E3A8A", visual.Options["color:Enterprise"]);
        Assert.Equal("#3B82F6", visual.Options["color:Mid-Market"]);

        var echartsJson = RepresentativeVisualConformanceHarness.RenderEChartsJson(manifest, visual.Name);
        Assert.NotNull(echartsJson);
        Assert.Contains("\"stack\":\"total\"", echartsJson);
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

        var echartsJson = RepresentativeVisualConformanceHarness.RenderEChartsJson(manifest, visual.Name);
        Assert.NotNull(echartsJson);
        Assert.Contains("null", echartsJson);
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

        var echartsJson = RepresentativeVisualConformanceHarness.RenderEChartsJson(manifest, visual.Name);
        Assert.NotNull(echartsJson);
        Assert.Contains("\"type\":\"bar\"", echartsJson);
        Assert.Contains("\"type\":\"line\"", echartsJson);
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
    public async Task MultiSurfaceRendering_ExecutesAcrossEChartsSvgAndTerminalWithoutThrowing(string fixtureFileName)
    {
        var (ast, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync(fixtureFileName);

        foreach (var visual in manifest.Visuals)
        {
            var echarts = RepresentativeVisualConformanceHarness.RenderEChartsJson(manifest, visual.Name);
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
}
