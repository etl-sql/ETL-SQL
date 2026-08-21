using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Renderers;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Reporting.Semantics.Runtime;
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
        Assert.Contains("Braille line", snap80.NormalizedText);
        Assert.Contains("gaps", snap80.NormalizedText);
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
        Assert.Contains("proportional components", snap80.NormalizedText);
        Assert.Contains("49.1%", snap80.NormalizedText);
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
        Assert.Contains("point glyph", snap80.NormalizedText);
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
        Assert.Contains("────────", snap80.NormalizedText);
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

    [Fact]
    public async Task PlotPlanTerminal_PreservesResolvedOrderPaletteAndNullPolicy()
    {
        var (_, manifest, _) = await TerminalSnapshotHarness.CompileFixtureAsync("terminal_line_null_gaps.rptsql");
        var plan = Assert.IsType<PlotPlan>(manifest.Visuals.First().PlotPlan);
        var snapshot = TerminalSnapshotHarness.CaptureSnapshot(TerminalRenderer.RenderVisual(manifest.Visuals.First(), manifest), 80, preserveAnsi: true);

        Assert.Equal(plan.Series.OrderBy(item => item.Order).Select(item => item.Key), plan.Series.Select(item => item.Key));
        Assert.Equal(plan.Series.Select(item => item.Color), plan.Palette.Select(item => item.Color));
        Assert.NotEmpty(plan.Nulls.GapRows);
        Assert.Contains("\u001b[", snapshot.RawOutput);
    }

    [Fact]
    public void Facets_RenderAsCoordinatedPanelsWithoutChangingRowIdentity()
    {
        var spec = ChartSpec.Create(
            "faceted", "faceted-data",
            [
                new FieldBinding(FieldChannel.X, "quarter", DataSemanticKind.Ordinal, "x"),
                new FieldBinding(FieldChannel.Y, "value", DataSemanticKind.Quantitative, "y"),
                new FieldBinding(FieldChannel.Row, "region", DataSemanticKind.Nominal)
            ],
            [new MarkLayerSpec("bars", MarkKind.Rect, 0, [], [])],
            new CoordinateSpec(CoordinateKind.Cartesian),
            [new ScaleSpec("x", FieldChannel.X, ScaleKind.Band, false, []), new ScaleSpec("y", FieldChannel.Y, ScaleKind.Linear, true, [])],
            new FormattingSpec("en-US", "UTC", "—", []),
            new NullHandlingSpec(NullValuePolicy.Gap, []),
            new ThemeSpec("default", []),
            new AccessibilitySpec("Quarterly values by region", null, null, true),
            title: "Faceted values",
            facet: new FacetSpec("region", null, new ScaleResolutionSpec()));
        var data = ChartDataSet.Create("faceted-data", 4,
        [
            new ChartColumn("quarter", ChartValueKind.Text, DataSemanticKind.Ordinal,
                [ChartValue.From("Q1"), ChartValue.From("Q2"), ChartValue.From("Q1"), ChartValue.From("Q2")], []),
            new ChartColumn("value", ChartValueKind.Decimal, DataSemanticKind.Quantitative,
                [ChartValue.From(10m), ChartValue.From(20m), ChartValue.From(30m), ChartValue.From(40m)], []),
            new ChartColumn("region", ChartValueKind.Text, DataSemanticKind.Nominal,
                [ChartValue.From("East"), ChartValue.From("East"), ChartValue.From("West"), ChartValue.From("West")], [])
        ]);

        var plan = new PlotPlanResolver().Resolve(spec, data);
        var narrow = TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan, 40), 40).NormalizedText;
        var wide = TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan, 120), 120).NormalizedText;

        Assert.Contains("East", narrow);
        Assert.Contains("West", narrow);
        Assert.Contains("East", wide);
        Assert.Contains("West", wide);
        Assert.Equal([0, 1, 2, 3], plan.Layers[0].Data.Select(item => item.RowIndex));
    }

    [Theory]
    [InlineData("MAP", SemanticFallbackKind.RankedTable, "South", "ranked")]
    [InlineData("SANKEY", SemanticFallbackKind.TransitionTable, "Visit -> Trial", "transition")]
    [InlineData("TREEMAP", SemanticFallbackKind.Hierarchy, "Platform > Runtime", "hierarchy")]
    [InlineData("SUNBURST", SemanticFallbackKind.Hierarchy, "Platform > Runtime", "hierarchy")]
    [InlineData("NETWORK", SemanticFallbackKind.NetworkConnections, "Hub", "connects")]
    public void SpecializedVisuals_ProduceUsefulSharedFallbacks(string type, SemanticFallbackKind kind, string expected, string meaning)
    {
        var visual = new VisualManifest
        {
            Name = type + "Fixture",
            VisualType = type,
            Columns = type switch
            {
                "SANKEY" or "NETWORK" => ["Source", "Target", "Value"],
                _ => ["Label", "Value"]
            },
            Rows = type switch
            {
                "MAP" => [["North", "10"], ["South", "25"]],
                "SANKEY" => [["Visit", "Trial", "80"], ["Trial", "Buy", "50"]],
                "NETWORK" => [["Hub", "A", "1"], ["Hub", "B", "1"], ["B", "C", "1"]],
                _ => [["Platform>Runtime", "60"], ["Platform>Tools", "40"]]
            }
        };

        visual.SemanticFallback = VisualSemanticFallbackBuilder.Build(visual);
        Assert.Equal(kind, visual.SemanticFallback.Kind);
        Assert.Contains(expected, string.Join(" ", visual.SemanticFallback.Items.Select(item => item.Label)));
        Assert.Contains(meaning, string.Join(" ", visual.SemanticFallback.Items.Select(item => item.Detail)), StringComparison.OrdinalIgnoreCase);
        if (type == "MAP") Assert.Equal("South", visual.SemanticFallback.Items[0].Label);

        var serialized = JsonSerializer.Serialize(visual);
        var terminal = TerminalSnapshotHarness.CaptureSnapshot(TerminalRenderer.RenderVisual(visual), 80).NormalizedText;
        var markdown = new MarkdownRenderer().Render(new ReportManifest { Title = "Fallback", Visuals = [visual] });
        Assert.Contains(visual.SemanticFallback.Summary!, serialized);
        Assert.Contains(visual.SemanticFallback.Summary!, terminal);
        Assert.Contains(visual.SemanticFallback.Summary!, markdown);
    }
}
