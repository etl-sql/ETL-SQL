using ETL_SQL.ReportHosting;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Reporting.Semantics.Runtime;

namespace ETL_SQL.Tests.Reporting;

public sealed class NativeChartLayoutTests
{
    [Theory]
    [InlineData(0, NativeChartLayoutTier.Compact)]
    [InlineData(479, NativeChartLayoutTier.Compact)]
    [InlineData(480, NativeChartLayoutTier.Standard)]
    [InlineData(959, NativeChartLayoutTier.Standard)]
    [InlineData(960, NativeChartLayoutTier.Wide)]
    [InlineData(10000, NativeChartLayoutTier.Wide)]
    public void DefaultProfile_ResolvesContiguousBoundaries(
        int containerWidth,
        NativeChartLayoutTier expected)
    {
        Assert.Equal(expected, NativeChartLayoutProfile.Default.Resolve(containerWidth));
    }

    /// <summary>
    /// The STANDARD stamp on a freshly built manifest has to describe the canvas the chart was
    /// actually drawn on.
    /// </summary>
    /// <remarks>
    /// <c>DashboardService_CachesByReportVisualTier</c> below asserted the tier string and stopped
    /// there, and the string was right for as long as the drawing was wrong: <c>VisualBuilder</c>
    /// resolves against the plot resolver's own 600x350 default, and the default stamp only wrote
    /// the label. The browser trusts the label — its resize observer asks the server for a
    /// re-layout only when the container's tier differs from the stamped one — so a chart in any
    /// tile between 480 and 959 pixels wide sat on a canvas 120 pixels narrower than the tier it
    /// claimed, and resizing anywhere inside that band changed nothing. Asserting the bounds and
    /// the SVG's own width rather than the word is what makes the stamp checkable.
    /// </remarks>
    [Fact]
    public async Task DefaultStamp_DrawsTheCanvasItClaims()
    {
        var scriptPath = GetSamplePath(Path.Combine(
            "samples", "08_Reporting", "custom_statistical_financial_layers.rptsql"));
        await using var service = new DashboardService(
            scriptPath,
            DashboardTestHelper.CreateMockScopeFactory());

        var manifest = await service.GetManifestAsync();
        var standard = NativeChartLayoutProfile.Default[NativeChartLayoutTier.Standard].Bounds;

        var stamped = manifest.Visuals
            .Where(visual => visual.Layout is not null && visual.PlotPlan is not null)
            .ToList();
        Assert.NotEmpty(stamped);

        foreach (var visual in stamped)
        {
            Assert.Equal("STANDARD", visual.Layout!.Tier);
            Assert.Equal(standard.Width, visual.Layout.Width);
            Assert.Equal(standard.Height, visual.Layout.Height);

            // The plan, not just the manifest: the manifest is what the browser reads, the plan is
            // what the renderer measured against, and the defect was the two disagreeing.
            Assert.Equal(standard.Width, visual.PlotPlan!.Bounds.Width);
            Assert.Equal(standard.Height, visual.PlotPlan.Bounds.Height);

            // And the delivered drawing, which is the only one of the three the author sees.
            Assert.Contains($"width='{standard.Width:0}' height='{standard.Height:0}'", visual.NativeSvg);
        }
    }

    [Fact]
    public async Task DashboardService_CachesByReportVisualTier_AndPreservesInteractionState()
    {
        var scriptPath = GetSamplePath(Path.Combine(
            "samples", "08_Reporting", "custom_statistical_financial_layers.rptsql"));
        await using var service = new DashboardService(
            scriptPath,
            DashboardTestHelper.CreateMockScopeFactory());

        var baseline = await service.GetManifestAsync();
        var source = baseline.Visuals.Single(visual => visual.Name == "PriceAndVolume");
        Assert.Equal("STANDARD", source.Layout?.Tier);

        var first = await service.ResolveVisualLayoutAsync(source.Name, NativeChartLayoutTier.Compact);
        var second = await service.ResolveVisualLayoutAsync(source.Name, NativeChartLayoutTier.Compact);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(1, service.NativeLayoutCacheEntryCount);
        Assert.Equal("COMPACT", first.Visuals.Single(visual => visual.Name == source.Name).Layout?.Tier);
        Assert.Equal(
            first.Visuals.Single(visual => visual.Name == source.Name).NativeSvg,
            second.Visuals.Single(visual => visual.Name == source.Name).NativeSvg);

        source.HighlightRows = [source.Rows[0]];
        source.Interaction = new InteractionManifest
        {
            Key = "Day",
            Select = "SINGLE",
            Effect = "HIGHLIGHT",
            Highlight = "CATEGORICAL"
        };
        baseline.IsInteraction = true;
        baseline.BuiltAt = baseline.BuiltAt.AddTicks(1);

        var refreshed = await service.ResolveVisualLayoutAsync(source.Name, NativeChartLayoutTier.Wide);
        var refreshedVisual = Assert.Single(refreshed!.Visuals, visual => visual.Name == source.Name);
        Assert.True(refreshed.IsInteraction);
        Assert.Equal(source.HighlightRows, refreshedVisual.HighlightRows);
        Assert.Equal("Day", refreshedVisual.Interaction?.Key);
        Assert.Equal("WIDE", refreshedVisual.Layout?.Tier);
        Assert.Equal(1200m, refreshedVisual.PlotPlan?.Bounds.Width);

        var browserJson = BrowserDeliveryProjection.Serialize(refreshed);
        Assert.DoesNotContain("\"plotPlan\"", browserJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"chartSpec\"", browserJson, StringComparison.Ordinal);
        Assert.Contains("\"layout\"", browserJson, StringComparison.Ordinal);

        var pdf = new PdfExporter().Export(refreshed);
        Assert.True(pdf.Length > 100);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public void Resolve_ReusesBoundsIndependentPlanState()
    {
        var spec = GrammarOfGraphics.GrammarOfGraphicsContractFixtures.ChartSpec();
        var data = GrammarOfGraphics.GrammarOfGraphicsContractFixtures.ChartData();
        var original = new PlotPlanResolver().Resolve(spec, data);
        var visual = new VisualManifest { Name = "Reusable", VisualType = "CUSTOM", ChartSpec = spec, ChartData = data, PlotPlan = original };

        NativeChartLayoutResolver.Resolve(visual, NativeChartLayoutTier.Wide);

        var relaid = Assert.IsType<PlotPlan>(visual.PlotPlan);
        Assert.Equal(1200m, relaid.Bounds.Width);
        Assert.True(original.Scales == relaid.Scales);
        Assert.True(original.Series == relaid.Series);
        Assert.True(original.Palette == relaid.Palette);
        Assert.True(original.Legend == relaid.Legend);
        Assert.Same(original.Fallback, relaid.Fallback);
    }

    private static string GetSamplePath(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")))
                return Path.Combine(current.FullName, relativePath);
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
