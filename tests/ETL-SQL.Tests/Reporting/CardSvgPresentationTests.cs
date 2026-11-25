using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
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

    [Fact]
    public void RenderForCard_WideYAxisTicksWithTitle_ExpandsLeftMarginToAvoidCollision()
    {
        var spec = GrammarOfGraphics.GrammarOfGraphicsContractFixtures.ChartSpec() with { Title = "Revenue" };
        var data = GrammarOfGraphics.GrammarOfGraphicsContractFixtures.ChartData();
        var resolved = new PlotPlanResolver().Resolve(spec, data, new PlotBounds(0m, 0m, 720m, 260m));
        var widenedScales = resolved.Scales.Select(s => s.Channel == FieldChannel.Y
            ? s with { Ticks = s.Ticks.Select(t => t with { Label = "522195.24" }).ToImmutableArray() }
            : s).ToImmutableArray();
        var styleBuilder = resolved.Style.ToBuilder();
        styleBuilder.Add(new StyleToken("axis:y:title", "Revenue ($)"));
        var plan = resolved with { Scales = widenedScales, Style = styleBuilder.ToImmutable() };

        var card = new SvgChartRenderer().Render(plan, PlotSvgRenderOptions.Card);

        // Plot axis line x1 should be widened past the default 60px left margin so wide tick labels clear the Y title.
        Assert.DoesNotContain("class='plot-axis-line' x1='60'", card);
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
