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
