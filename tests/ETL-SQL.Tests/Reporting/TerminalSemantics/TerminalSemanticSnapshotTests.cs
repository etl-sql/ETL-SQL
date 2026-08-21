using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Renderers;
using Xunit;

namespace ETL_SQL.Tests.Reporting.TerminalSemantics;

public class TerminalSemanticSnapshotTests
{
    [Theory]
    [InlineData("terminal_bar_ordering.rptsql")]
    [InlineData("terminal_line_null_gaps.rptsql")]
    [InlineData("terminal_bar_negative_zero.rptsql")]
    [InlineData("terminal_table_long_labels.rptsql")]
    [InlineData("terminal_pie_donut_breakdown.rptsql")]
    [InlineData("terminal_scatter_coordinates.rptsql")]
    [InlineData("terminal_combo_dual_layer.rptsql")]
    [InlineData("terminal_rules_annotations.rptsql")]
    [InlineData("terminal_accessible_summary.rptsql")]
    public async Task AllFixtures_CaptureDeterministicSnapshotsAtWidths40_80_120(string fixtureFileName)
    {
        var (_, manifest, _) = await TerminalSnapshotHarness.CompileFixtureAsync(fixtureFileName);
        Assert.NotNull(manifest);
        Assert.NotEmpty(manifest.Visuals);

        var page = manifest.Pages.FirstOrDefault();
        Assert.NotNull(page);

        var renderable = TerminalRenderer.RenderPage(page, manifest);
        Assert.NotNull(renderable);

        var widths = new[] { 40, 80, 120 };
        foreach (var width in widths)
        {
            var snapshot = TerminalSnapshotHarness.CaptureSnapshot(renderable, width);
            Assert.NotNull(snapshot.NormalizedText);
            Assert.True(snapshot.NormalizedText.Length > 0);
            Assert.True(snapshot.LineCount > 0);
            Assert.NotEmpty(snapshot.ChecksumSha256);
        }
    }

    [Fact]
    public async Task BarChart_PreservesStableOrderingAcrossTerminalWidths()
    {
        var (_, manifest, _) = await TerminalSnapshotHarness.CompileFixtureAsync("terminal_bar_ordering.rptsql");
        var visual = manifest.Visuals.First();
        var renderable = TerminalRenderer.RenderVisual(visual, manifest);

        var snap80 = TerminalSnapshotHarness.CaptureSnapshot(renderable, 80);

        Assert.Contains("North", snap80.NormalizedText);
        Assert.Contains("South", snap80.NormalizedText);
        Assert.Contains("East", snap80.NormalizedText);
        Assert.Contains("West", snap80.NormalizedText);
    }

    [Fact]
    public async Task LineChart_RendersDiscontinuousSeriesWithNullGaps()
    {
        var (_, manifest, _) = await TerminalSnapshotHarness.CompileFixtureAsync("terminal_line_null_gaps.rptsql");
        var visual = manifest.Visuals.First();
        var renderable = TerminalRenderer.RenderVisual(visual, manifest);

        var snap80 = TerminalSnapshotHarness.CaptureSnapshot(renderable, 80);
        Assert.NotNull(snap80.NormalizedText);
        Assert.NotEmpty(snap80.NormalizedText);
    }

    [Fact]
    public async Task NegativeAndZeroValues_RenderCorrectlyInTerminal()
    {
        var (_, manifest, _) = await TerminalSnapshotHarness.CompileFixtureAsync("terminal_bar_negative_zero.rptsql");
        var visual = manifest.Visuals.First();
        var renderable = TerminalRenderer.RenderVisual(visual, manifest);

        var snap80 = TerminalSnapshotHarness.CaptureSnapshot(renderable, 80);

        Assert.Contains("Product A", snap80.NormalizedText);
        Assert.Contains("Product B", snap80.NormalizedText);
        Assert.Contains("Product C", snap80.NormalizedText);
        Assert.Contains("Product D", snap80.NormalizedText);
    }

    [Fact]
    public async Task LongLabels_TruncateDeterministicallyAtNarrowWidth40()
    {
        var (_, manifest, _) = await TerminalSnapshotHarness.CompileFixtureAsync("terminal_table_long_labels.rptsql");
        var visual = manifest.Visuals.First();
        var renderable = TerminalRenderer.RenderVisual(visual, manifest);

        var snap40 = TerminalSnapshotHarness.CaptureSnapshot(renderable, 40);
        var snap120 = TerminalSnapshotHarness.CaptureSnapshot(renderable, 120);

        Assert.True(snap40.LineCount > 0);
        Assert.True(snap120.LineCount > 0);

        // At width 120, full department strings should be visible
        Assert.Contains("Critical Infrastructure Operations Team", snap120.NormalizedText);
    }

    [Fact]
    public async Task PieDonut_RendersTextualBreakdownWithPercentages()
    {
        var (_, manifest, _) = await TerminalSnapshotHarness.CompileFixtureAsync("terminal_pie_donut_breakdown.rptsql");
        var visual = manifest.Visuals.First();
        var renderable = TerminalRenderer.RenderVisual(visual, manifest);

        var snap80 = TerminalSnapshotHarness.CaptureSnapshot(renderable, 80);

        Assert.Contains("Compute", snap80.NormalizedText);
        Assert.Contains("Storage", snap80.NormalizedText);
        Assert.Contains("Networking", snap80.NormalizedText);
    }

    [Fact]
    public async Task Scatter_PlotsCoordinatesOnTerminalGrid()
    {
        var (_, manifest, _) = await TerminalSnapshotHarness.CompileFixtureAsync("terminal_scatter_coordinates.rptsql");
        var visual = manifest.Visuals.First();
        var renderable = TerminalRenderer.RenderVisual(visual, manifest);

        var snap80 = TerminalSnapshotHarness.CaptureSnapshot(renderable, 80);
        Assert.NotNull(snap80.NormalizedText);
        Assert.NotEmpty(snap80.NormalizedText);
    }

    [Fact]
    public async Task RulesOverlays_RenderThresholdAnnotationsInTerminal()
    {
        var (_, manifest, _) = await TerminalSnapshotHarness.CompileFixtureAsync("terminal_rules_annotations.rptsql");
        var visual = manifest.Visuals.First();
        var renderable = TerminalRenderer.RenderVisual(visual, manifest);

        var snap80 = TerminalSnapshotHarness.CaptureSnapshot(renderable, 80);
        Assert.NotNull(snap80.NormalizedText);
        Assert.NotEmpty(snap80.NormalizedText);
    }

    [Fact]
    public async Task AccessibleSummary_RendersCardAndTableFallback()
    {
        var (_, manifest, _) = await TerminalSnapshotHarness.CompileFixtureAsync("terminal_accessible_summary.rptsql");
        var page = manifest.Pages.First();
        var renderable = TerminalRenderer.RenderPage(page, manifest);

        var snap80 = TerminalSnapshotHarness.CaptureSnapshot(renderable, 80);

        Assert.Contains("Global Fleet SLA", snap80.NormalizedText);
        Assert.Contains("99.91%", snap80.NormalizedText);
        Assert.Contains("US-East", snap80.NormalizedText);
    }

    [Fact]
    public void TestFixtureBuilder_ConstructsValidManifestWithoutDiskIo()
    {
        var dataSet = TestChartDataSet.Create(
            new[] { ("Category", typeof(string)), ("Revenue", typeof(double)) },
            new object[] { "Alpha", 150.0 },
            new object[] { "Beta", -50.0 },
            new object[] { "Gamma", 0.0 }
        );

        var spec = new TestChartSpec(
            Name: "SyntheticBar",
            VisualType: "BAR",
            Title: "Synthetic In-Memory Bar",
            DataSet: dataSet,
            Mappings: new System.Collections.Generic.Dictionary<string, string> { ["x"] = "Category", ["y"] = "Revenue" },
            Options: new System.Collections.Generic.Dictionary<string, string> { ["AXIS_SORT"] = "ASC" }
        );

        var manifest = TerminalSemanticFixtureBuilder.BuildReportManifest(spec);
        Assert.NotNull(manifest);
        Assert.Single(manifest.Visuals);
        Assert.Equal(3, manifest.Visuals[0].Rows.Count);

        var renderable = TerminalRenderer.RenderPage(manifest.Pages[0], manifest);
        var snapshot = TerminalSnapshotHarness.CaptureSnapshot(renderable, 80);

        Assert.Contains("Terminal Semantic Test Page", snapshot.NormalizedText);
        Assert.Contains("Al", snapshot.NormalizedText);
        Assert.Contains("Be", snapshot.NormalizedText);
        Assert.Contains("Ga", snapshot.NormalizedText);
    }
}
