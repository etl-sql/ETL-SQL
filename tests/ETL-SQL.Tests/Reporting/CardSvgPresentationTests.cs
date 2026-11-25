using System.Collections.Generic;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Renderers;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Reporting.Semantics.Runtime;

namespace ETL_SQL.Tests.Reporting;

public sealed class CardSvgPresentationTests
{
    [Fact]
    public void RenderForCard_SuppressesDuplicateTitleAndWhiteBackground_WhileStandalonePreservesThem()
    {
        var visual = CreateBarVisual(theme: null, title: "Revenue by Region");

        var standalone = new SvgChartRenderer().Render(visual);
        var card = new SvgChartRenderer().RenderForCard(visual);

        Assert.Contains("fill='white'", standalone);
        Assert.Contains(">Revenue by Region</text>", standalone);

        Assert.DoesNotContain("fill='white'", card);
        Assert.DoesNotContain(">Revenue by Region</text>", card);
    }

    [Fact]
    public void RenderForCard_DarkTheme_UsesLightAxisAndTextTokens()
    {
        var visual = CreateBarVisual(theme: "dark", title: "Dark Revenue");

        var card = new SvgChartRenderer().RenderForCard(visual);

        Assert.DoesNotContain("fill='white'", card);
        Assert.Contains("stroke='#475569'", card);
        Assert.Contains("fill='#94a3b8'", card);
        Assert.DoesNotContain("stroke='#bbb'", card);
    }

    [Fact]
    public void RenderForCard_CompactCanvas_ReclaimsTopMarginAndPreservesReadableFontFloor()
    {
        var spec = GrammarOfGraphics.GrammarOfGraphicsContractFixtures.ChartSpec();
        var data = GrammarOfGraphics.GrammarOfGraphicsContractFixtures.ChartData();
        var plan = new PlotPlanResolver().Resolve(spec, data, new PlotBounds(0m, 0m, 480m, 240m));

        var card = new SvgChartRenderer().Render(plan, PlotSvgRenderOptions.Card);

        Assert.Contains("font-size='10'", card);
        Assert.DoesNotContain("fill='white'", card);
    }

    private static VisualManifest CreateBarVisual(string? theme, string title)
    {
        var spec = GrammarOfGraphics.GrammarOfGraphicsContractFixtures.ChartSpec() with { Title = title };
        var data = GrammarOfGraphics.GrammarOfGraphicsContractFixtures.ChartData();
        var resolved = new PlotPlanResolver().Resolve(spec, data);
        var styleBuilder = resolved.Style.ToBuilder();
        styleBuilder.Add(new StyleToken("TITLE", title));
        if (theme is not null)
            styleBuilder.Add(new StyleToken("THEME", theme));
        var plan = resolved with { Style = styleBuilder.ToImmutable() };

        return new VisualManifest
        {
            Name = "SalesByRegion",
            VisualType = "BAR",
            ChartSpec = spec,
            ChartData = data,
            PlotPlan = plan,
            Options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["TITLE"] = title },
            Styles = theme is not null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["THEME"] = theme }
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };
    }
}
