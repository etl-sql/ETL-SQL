using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Renderers;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Reporting.Semantics.Runtime;
using Xunit;

namespace ETL_SQL.Tests;

public class LegendControlsTests
{
    private static CreateVisualStatement ParseVisual(string script)
    {
        var lexer = new Lexer(script);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var statements = new List<Statement>();
        while (parser.Current.Type != TokenType.EOF) statements.Add(parser.ParseStatement());
        return (CreateVisualStatement)statements[0];
    }

    private static VisualManifest CreateManifest(string name = "SalesChart")
    {
        return new VisualManifest
        {
            Name = name,
            VisualType = "BAR",
            Columns = ["Region", "Revenue"],
            Rows = [["North", "100"], ["South", "150"], ["East", "200"], ["West", "250"]]
        };
    }

    private static PlotPlan CreatePlan(params StyleToken[] styleTokens)
    {
        var s1 = new ResolvedSeries("s1", "Alpha", 0, "#3366cc");
        var s2 = new ResolvedSeries("s2", "Beta", 1, "#dc3912");
        var s3 = new ResolvedSeries("s3", "Gamma", 2, "#ff9900");
        var s4 = new ResolvedSeries("s4", "Delta", 3, "#109618");

        var p1 = new PaletteAssignment("s1", "#3366cc");
        var p2 = new PaletteAssignment("s2", "#dc3912");
        var p3 = new PaletteAssignment("s3", "#ff9900");
        var p4 = new PaletteAssignment("s4", "#109618");

        var l1 = new LegendEntry("s1", "Alpha", 0, "#3366cc");
        var l2 = new LegendEntry("s2", "Beta", 1, "#dc3912");
        var l3 = new LegendEntry("s3", "Gamma", 2, "#ff9900");
        var l4 = new LegendEntry("s4", "Delta", 3, "#109618");

        var markLayer = new ResolvedMarkLayer("layer1", MarkKind.Rect, 1, "s1", ImmutableArray<ResolvedDatum>.Empty)
        {
            Style = ImmutableArray<StyleToken>.Empty
        };

        return PlotPlan.Create(
            "test_spec",
            new PlotBounds(0, 0, 600, 400),
            ImmutableArray<ResolvedScale>.Empty,
            ImmutableArray.Create(s1, s2, s3, s4),
            ImmutableArray.Create(p1, p2, p3, p4),
            ImmutableArray.Create(l1, l2, l3, l4),
            ImmutableArray.Create(markLayer),
            new ResolvedNullPolicy(NullValuePolicy.Skip, ImmutableArray<FieldNullPolicy>.Empty, ImmutableArray<int>.Empty, ImmutableArray<int>.Empty),
            "accessible summary",
            new SemanticFallback(SemanticFallbackKind.Summary, "Fallback", ImmutableArray<SemanticFallbackItem>.Empty),
            style: styleTokens.ToImmutableArray());
    }

    [Fact]
    public void LegendTitle_RendersTitleInSvg()
    {
        var plan = CreatePlan(
            new StyleToken("LEGEND", "ON"),
            new StyleToken("LEGEND_TITLE", "Regions"));

        var svg = new SvgChartRenderer().Render(plan);
        var doc = XDocument.Parse(svg);
        var titleEl = doc.Descendants().FirstOrDefault(e => (string?)e.Attribute("class") == "plot-legend-title");

        Assert.NotNull(titleEl);
        Assert.Contains("Regions", titleEl.Value);
    }

    [Fact]
    public void LegendTitle_None_SuppressesTitle()
    {
        var plan = CreatePlan(
            new StyleToken("LEGEND", "ON"),
            new StyleToken("LEGEND_TITLE", "NONE"));

        var svg = new SvgChartRenderer().Render(plan);
        var doc = XDocument.Parse(svg);
        var titleEl = doc.Descendants().FirstOrDefault(e => (string?)e.Attribute("class") == "plot-legend-title");

        Assert.Null(titleEl);
    }

    [Fact]
    public void LegendTitle_TerminalRenderer_IncludesTitle()
    {
        var plan = CreatePlan(
            new StyleToken("LEGEND", "ON"),
            new StyleToken("LEGEND_TITLE", "Products"));

        var renderable = PlotPlanTerminalRenderer.Render(plan, 80);
        Assert.NotNull(renderable);
    }

    [Fact]
    public void LegendTypography_AppliesFontSizeColorAndWeight()
    {
        var plan = CreatePlan(
            new StyleToken("LEGEND", "ON"),
            new StyleToken("LEGEND_FONT_SIZE", "12"),
            new StyleToken("LEGEND_FONT_COLOR", "#e11d48"),
            new StyleToken("LEGEND_FONT_WEIGHT", "BOLD"));

        var svg = new SvgChartRenderer().Render(plan);
        var doc = XDocument.Parse(svg);
        var legendTexts = doc.Descendants()
            .Where(e => e.Name.LocalName == "text" && (e.Value == "Alpha" || e.Value == "Beta"))
            .ToList();

        Assert.NotEmpty(legendTexts);
        foreach (var text in legendTexts)
        {
            Assert.Equal("12", (string?)text.Attribute("font-size"));
            Assert.Equal("#e11d48", (string?)text.Attribute("fill"));
            Assert.Equal("BOLD", (string?)text.Attribute("font-weight"));
        }
    }

    [Fact]
    public void LegendOrientation_VerticalAtBottom_StacksVertically()
    {
        var plan = CreatePlan(
            new StyleToken("LEGEND", "ON"),
            new StyleToken("LEGEND_POSITION", "BOTTOM"),
            new StyleToken("LEGEND_ORIENTATION", "VERTICAL"));

        var svg = new SvgChartRenderer().Render(plan);
        var doc = XDocument.Parse(svg);
        var legendTexts = doc.Descendants()
            .Where(e => e.Name.LocalName == "text" && (e.Value == "Alpha" || e.Value == "Beta"))
            .OrderBy(e => decimal.Parse(e.Attribute("y")!.Value, CultureInfo.InvariantCulture))
            .ToList();

        Assert.True(legendTexts.Count >= 2);
        var y1 = decimal.Parse(legendTexts[0].Attribute("y")!.Value, CultureInfo.InvariantCulture);
        var y2 = decimal.Parse(legendTexts[1].Attribute("y")!.Value, CultureInfo.InvariantCulture);
        Assert.True(y2 > y1, "Items in vertical legend should have increasing Y coordinates");
    }

    [Fact]
    public void LegendOrientation_HorizontalAtTop_ArrangesHorizontally()
    {
        var plan = CreatePlan(
            new StyleToken("LEGEND", "ON"),
            new StyleToken("LEGEND_POSITION", "TOP"),
            new StyleToken("LEGEND_ORIENTATION", "HORIZONTAL"));

        var svg = new SvgChartRenderer().Render(plan);
        var doc = XDocument.Parse(svg);
        var legendTexts = doc.Descendants()
            .Where(e => e.Name.LocalName == "text" && (e.Value == "Alpha" || e.Value == "Beta"))
            .OrderBy(e => decimal.Parse(e.Attribute("x")!.Value, CultureInfo.InvariantCulture))
            .ToList();

        Assert.True(legendTexts.Count >= 2);
        var x1 = decimal.Parse(legendTexts[0].Attribute("x")!.Value, CultureInfo.InvariantCulture);
        var x2 = decimal.Parse(legendTexts[1].Attribute("x")!.Value, CultureInfo.InvariantCulture);
        Assert.True(x2 > x1, "Items in horizontal legend at top should have increasing X coordinates");
    }

    [Fact]
    public void LegendReverse_FlipsOrderInLegend()
    {
        var planNormal = CreatePlan(
            new StyleToken("LEGEND", "ON"),
            new StyleToken("LEGEND_REVERSE", "OFF"));
        var svgNormal = new SvgChartRenderer().Render(planNormal);
        var docNormal = XDocument.Parse(svgNormal);
        var normalLabels = docNormal.Descendants()
            .Where(e => e.Name.LocalName == "text" && (e.Value == "Alpha" || e.Value == "Beta" || e.Value == "Gamma" || e.Value == "Delta"))
            .Select(e => e.Value)
            .ToList();

        var planReverse = CreatePlan(
            new StyleToken("LEGEND", "ON"),
            new StyleToken("LEGEND_REVERSE", "ON"));
        var svgReverse = new SvgChartRenderer().Render(planReverse);
        var docReverse = XDocument.Parse(svgReverse);
        var reverseLabels = docReverse.Descendants()
            .Where(e => e.Name.LocalName == "text" && (e.Value == "Alpha" || e.Value == "Beta" || e.Value == "Gamma" || e.Value == "Delta"))
            .Select(e => e.Value)
            .ToList();

        Assert.Equal(new[] { "Alpha", "Beta", "Gamma", "Delta" }, normalLabels);
        Assert.Equal(new[] { "Delta", "Gamma", "Beta", "Alpha" }, reverseLabels);
    }

    [Fact]
    public void LegendInside_RendersOverlayBoxWithAnchor()
    {
        var plan = CreatePlan(
            new StyleToken("LEGEND", "ON"),
            new StyleToken("LEGEND_POSITION", "INSIDE"),
            new StyleToken("LEGEND_ANCHOR", "TOP_RIGHT"));

        var svg = new SvgChartRenderer().Render(plan);
        var doc = XDocument.Parse(svg);

        var insideGroup = doc.Descendants().FirstOrDefault(e => (string?)e.Attribute("class") == "plot-legend plot-legend-inside");
        Assert.NotNull(insideGroup);

        var bgRect = insideGroup.Descendants().FirstOrDefault(e => (string?)e.Attribute("class") == "plot-legend-bg");
        Assert.NotNull(bgRect);

        var x = decimal.Parse(bgRect.Attribute("x")!.Value, CultureInfo.InvariantCulture);
        var y = decimal.Parse(bgRect.Attribute("y")!.Value, CultureInfo.InvariantCulture);
        Assert.True(x > 300, $"Expected x > 300 for TOP_RIGHT, got {x}");
        Assert.True(y < 200, $"Expected y < 200 for TOP_RIGHT, got {y}");
    }

    [Fact]
    public void LegendInside_BottomLeft_PositionsInLowerLeft()
    {
        var plan = CreatePlan(
            new StyleToken("LEGEND", "ON"),
            new StyleToken("LEGEND_POSITION", "INSIDE"),
            new StyleToken("LEGEND_ANCHOR", "BOTTOM_LEFT"));

        var svg = new SvgChartRenderer().Render(plan);
        var doc = XDocument.Parse(svg);
        var bgRect = doc.Descendants().FirstOrDefault(e => (string?)e.Attribute("class") == "plot-legend-bg");
        Assert.NotNull(bgRect);

        var x = decimal.Parse(bgRect.Attribute("x")!.Value, CultureInfo.InvariantCulture);
        var y = decimal.Parse(bgRect.Attribute("y")!.Value, CultureInfo.InvariantCulture);
        Assert.True(x < 200, $"Expected x < 200 for BOTTOM_LEFT, got {x}");
        Assert.True(y > 200, $"Expected y > 200 for BOTTOM_LEFT, got {y}");
    }

    [Fact]
    public void LegendColumns_LaysOutInSpecifiedColumnCount()
    {
        var plan = CreatePlan(
            new StyleToken("LEGEND", "ON"),
            new StyleToken("LEGEND_POSITION", "BOTTOM"),
            new StyleToken("LEGEND_COLUMNS", "2"));

        var svg = new SvgChartRenderer().Render(plan);
        var doc = XDocument.Parse(svg);
        var legendTexts = doc.Descendants()
            .Where(e => e.Name.LocalName == "text" && (e.Value == "Alpha" || e.Value == "Beta" || e.Value == "Gamma" || e.Value == "Delta"))
            .ToList();

        Assert.Equal(4, legendTexts.Count);
        var distinctX = legendTexts.Select(e => decimal.Parse(e.Attribute("x")!.Value, CultureInfo.InvariantCulture)).Distinct().Count();
        Assert.Equal(2, distinctX);
    }

    [Fact]
    public void Lowerer_RejectsInvalidLegendPosition()
    {
        var visual = ParseVisual("CREATE VISUAL V1 AS BAR (SOURCE = #data, MAPPINGS (X = Region, Y = Revenue), OPTIONS (LEGEND_POSITION = DIAGONAL));");
        var lowerer = new NamedVisualChartLowerer();
        Assert.Throws<InvalidOperationException>(() => lowerer.Lower(visual, CreateManifest()));
    }

    [Fact]
    public void Lowerer_RejectsAnchorWithoutInside()
    {
        var visual = ParseVisual("CREATE VISUAL V1 AS BAR (SOURCE = #data, MAPPINGS (X = Region, Y = Revenue), OPTIONS (LEGEND_ANCHOR = TOP_LEFT));");
        var lowerer = new NamedVisualChartLowerer();
        Assert.Throws<InvalidOperationException>(() => lowerer.Lower(visual, CreateManifest()));
    }

    [Fact]
    public void Lowerer_RejectsInvalidLegendOrientation()
    {
        var visual = ParseVisual("CREATE VISUAL V1 AS BAR (SOURCE = #data, MAPPINGS (X = Region, Y = Revenue), OPTIONS (LEGEND_ORIENTATION = SLANTED));");
        var lowerer = new NamedVisualChartLowerer();
        Assert.Throws<InvalidOperationException>(() => lowerer.Lower(visual, CreateManifest()));
    }

    [Fact]
    public void Lowerer_RejectsInvalidLegendColumns()
    {
        var visual = ParseVisual("CREATE VISUAL V1 AS BAR (SOURCE = #data, MAPPINGS (X = Region, Y = Revenue), OPTIONS (LEGEND_COLUMNS = -1));");
        var lowerer = new NamedVisualChartLowerer();
        Assert.Throws<InvalidOperationException>(() => lowerer.Lower(visual, CreateManifest()));
    }

    [Fact]
    public void Lowerer_RejectsLegendOnGauge()
    {
        var visual = ParseVisual("CREATE VISUAL G1 AS GAUGE (SOURCE = #data, MAPPINGS (VALUE = Revenue), OPTIONS (LEGEND = ON));");
        var lowerer = new NamedVisualChartLowerer();
        var manifest = new VisualManifest { Name = "G1", VisualType = "GAUGE", Columns = ["Revenue"], Rows = [["100"]] };
        Assert.Throws<InvalidOperationException>(() => lowerer.Lower(visual, manifest));
    }

    [Fact]
    public void Lowerer_AcceptsAllLegendOptionsOnBar()
    {
        var visual = ParseVisual(@"CREATE VISUAL V1 AS BAR (
            SOURCE = #data,
            MAPPINGS (X = Region, Y = Revenue),
            OPTIONS (
                LEGEND = ON,
                LEGEND_POSITION = INSIDE,
                LEGEND_ANCHOR = TOP_RIGHT,
                LEGEND_ORIENTATION = VERTICAL,
                LEGEND_REVERSE = ON,
                LEGEND_COLUMNS = 2,
                LEGEND_TITLE = 'Product Line',
                LEGEND_FONT_SIZE = 11,
                LEGEND_FONT_COLOR = '#1e3a8a',
                LEGEND_FONT_WEIGHT = BOLD
            )
        );");

        var lowerer = new NamedVisualChartLowerer();
        var spec = lowerer.Lower(visual, CreateManifest());
        Assert.NotNull(spec);
    }
}
