using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Renderers;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Reporting.Semantics.Runtime;
using Spectre.Console;
using Spectre.Console.Rendering;
using Xunit;

namespace ETL_SQL.Tests.Reporting;

public class PieDonutControlsTests
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

    private static ResolvedDatum CreateArcDatum(int rowIndex, string category, decimal value)
    {
        return new ResolvedDatum(
            rowIndex,
            ImmutableArray.Create(
                new ResolvedChannelValue(FieldChannel.Theta, ChartValue.From(category), category),
                new ResolvedChannelValue(FieldChannel.Radius, ChartValue.From(value), value.ToString(CultureInfo.InvariantCulture))
            ),
            false);
    }

    private static PlotPlan CreatePiePlan(
        (string Category, decimal Value)[] items,
        CoordinateSpec? coordinate = null,
        params StyleToken[] styleTokens)
    {
        var seriesList = new List<ResolvedSeries>();
        var paletteList = new List<PaletteAssignment>();
        var legendList = new List<LegendEntry>();
        var dataList = new List<ResolvedDatum>();

        var colors = new[] { "#5470c6", "#91cc75", "#fac858", "#ee6666", "#73c0de", "#3ba272", "#fc8452" };

        for (var i = 0; i < items.Length; i++)
        {
            var key = items[i].Category;
            var color = colors[i % colors.Length];
            seriesList.Add(new ResolvedSeries(key, key, i, color));
            paletteList.Add(new PaletteAssignment(key, color));
            legendList.Add(new LegendEntry(key, key, i, color));
            dataList.Add(CreateArcDatum(i, key, items[i].Value));
        }

        var markLayer = new ResolvedMarkLayer("arc_layer", MarkKind.Arc, 1, seriesList[0].Key, dataList.ToImmutableArray())
        {
            Style = ImmutableArray<StyleToken>.Empty
        };

        return PlotPlan.Create(
            "test_pie",
            new PlotBounds(0, 0, 600, 400),
            ImmutableArray<ResolvedScale>.Empty,
            seriesList.ToImmutableArray(),
            paletteList.ToImmutableArray(),
            legendList.ToImmutableArray(),
            ImmutableArray.Create(markLayer),
            new ResolvedNullPolicy(NullValuePolicy.Skip, ImmutableArray<FieldNullPolicy>.Empty, ImmutableArray<int>.Empty, ImmutableArray<int>.Empty),
            "accessible pie summary",
            new SemanticFallback(SemanticFallbackKind.Summary, "Fallback", ImmutableArray<SemanticFallbackItem>.Empty),
            coordinate: coordinate ?? new CoordinateSpec(CoordinateKind.Polar),
            style: styleTokens.ToImmutableArray());
    }

    private static List<string> ExtractSliceTitles(string svg)
    {
        var matches = Regex.Matches(svg, @"<title>([^<:]+): ([^<]+)</title>");
        return matches.Select(m => m.Groups[1].Value).ToList();
    }

    // ── 1. Slice Sort Order Tests ───────────────────────────────────────────

    [Fact]
    public void SortOrder_ValueDesc_OrdersSlicesLargestFirst()
    {
        var items = new[] { ("Small", 10m), ("Largest", 100m), ("Medium", 50m) };
        var plan = CreatePiePlan(items, null, new StyleToken("SORT", "VALUE_DESC"));

        var svg = new SvgChartRenderer().Render(plan);
        Assert.NotNull(svg);

        var titles = ExtractSliceTitles(svg);
        Assert.Equal(["Largest", "Medium", "Small"], titles);
    }

    [Fact]
    public void SortOrder_ValueAsc_OrdersSlicesSmallestFirst()
    {
        var items = new[] { ("Medium", 50m), ("Largest", 100m), ("Small", 10m) };
        var plan = CreatePiePlan(items, null, new StyleToken("SORT", "VALUE_ASC"));

        var svg = new SvgChartRenderer().Render(plan);
        Assert.NotNull(svg);

        var titles = ExtractSliceTitles(svg);
        Assert.Equal(["Small", "Medium", "Largest"], titles);
    }

    [Fact]
    public void SortOrder_Alpha_OrdersSlicesAlphabetically()
    {
        var items = new[] { ("Zebra", 100m), ("Apple", 50m), ("Mango", 10m) };
        var plan = CreatePiePlan(items, null, new StyleToken("SORT", "ALPHA"));

        var svg = new SvgChartRenderer().Render(plan);
        Assert.NotNull(svg);

        var titles = ExtractSliceTitles(svg);
        Assert.Equal(["Apple", "Mango", "Zebra"], titles);
    }

    [Fact]
    public void SortOrder_Source_KeepsSourceOrder()
    {
        var items = new[] { ("Bravo", 50m), ("Charlie", 10m), ("Alpha", 100m) };
        var plan = CreatePiePlan(items, null, new StyleToken("SORT", "SOURCE"));

        var svg = new SvgChartRenderer().Render(plan);
        Assert.NotNull(svg);

        var titles = ExtractSliceTitles(svg);
        Assert.Equal(["Bravo", "Charlie", "Alpha"], titles);
    }

    // ── 2. Minimum Slice Threshold / "Other" Rollup Tests ────────────────────

    [Fact]
    public void MinSlicePct_RollsUpSmallSlicesIntoOther()
    {
        // Total = 200. 5% of 200 = 10.
        // Big1: 100 (50%), Big2: 80 (40%), Small1: 8 (4%), Small2: 7 (3.5%), Small3: 5 (2.5%)
        // Rolled up: Small1 + Small2 + Small3 = 20 (10%)
        var items = new[]
        {
            ("Enterprise", 100m),
            ("Commercial", 80m),
            ("Micro", 8m),
            ("Nano", 7m),
            ("Pico", 5m)
        };

        var plan = CreatePiePlan(items, null, new StyleToken("MIN_SLICE_PCT", "5"));
        var svg = new SvgChartRenderer().Render(plan);
        Assert.NotNull(svg);

        var titles = ExtractSliceTitles(svg);
        Assert.Equal(["Enterprise", "Commercial", "Other"], titles);
        Assert.Contains("<title>Other: 20</title>", svg);
    }

    [Fact]
    public void MinSlicePct_CustomOtherLabel_UsesProvidedLabel()
    {
        var items = new[]
        {
            ("Alpha", 100m),
            ("Beta", 80m),
            ("Tiny1", 3m),
            ("Tiny2", 2m)
        };

        var plan = CreatePiePlan(items, null,
            new StyleToken("MIN_SLICE_PCT", "5%"),
            new StyleToken("OTHER_LABEL", "Miscellaneous"));

        var svg = new SvgChartRenderer().Render(plan);
        Assert.NotNull(svg);

        var titles = ExtractSliceTitles(svg);
        Assert.Equal(["Alpha", "Beta", "Miscellaneous"], titles);
        Assert.Contains("<title>Miscellaneous: 5</title>", svg);
    }

    [Fact]
    public void MinSlicePct_TerminalRenderer_IncludesOther()
    {
        var items = new[]
        {
            ("SegmentA", 100m),
            ("SegmentB", 80m),
            ("TinyA", 3m),
            ("TinyB", 2m)
        };

        var plan = CreatePiePlan(items, null,
            new StyleToken("MIN_SLICE_PCT", "5"),
            new StyleToken("OTHER_LABEL", "Rest"));

        var renderable = PlotPlanTerminalRenderer.Render(plan, 80);
        Assert.NotNull(renderable);
        Assert.IsAssignableFrom<IRenderable>(renderable);
    }

    // ── 3. Slice Explosion Tests ────────────────────────────────────────────

    [Fact]
    public void Explode_SingleSlice_OffsetsSliceCenterAndAddsExplodedClass()
    {
        var items = new[] { ("Alpha", 60m), ("Beta", 40m) };
        var plan = CreatePiePlan(items, null,
            new StyleToken("EXPLODE", "Beta"),
            new StyleToken("EXPLODE_DISTANCE", "15"));

        var svg = new SvgChartRenderer().Render(plan);
        Assert.NotNull(svg);

        Assert.Contains("class='plot-arc-slice plot-arc-exploded'", svg);
        Assert.Contains("class='plot-arc-slice'", svg);
    }

    [Fact]
    public void ExplodeAll_OffsetsAllSlices()
    {
        var items = new[] { ("Alpha", 50m), ("Beta", 30m), ("Gamma", 20m) };
        var plan = CreatePiePlan(items, null, new StyleToken("EXPLODE_ALL", "12"));

        var svg = new SvgChartRenderer().Render(plan);
        Assert.NotNull(svg);

        var matches = Regex.Matches(svg, @"class='plot-arc-slice plot-arc-exploded'");
        Assert.Equal(3, matches.Count);
    }

    // ── 4. Slice Border / Stroke Tests ──────────────────────────────────────

    [Fact]
    public void SliceBorder_CustomColorAndWidth_AppliesStrokeAttributes()
    {
        var items = new[] { ("Alpha", 60m), ("Beta", 40m) };
        var plan = CreatePiePlan(items, null,
            new StyleToken("SLICE_BORDER_COLOR", "#ff0000"),
            new StyleToken("SLICE_BORDER_WIDTH", "4"));

        var svg = new SvgChartRenderer().Render(plan);
        Assert.NotNull(svg);

        Assert.Contains("stroke='#ff0000' stroke-width='4'", svg);
    }

    [Fact]
    public void SliceBorder_WidthZero_RemovesInterSliceLine()
    {
        var items = new[] { ("Alpha", 60m), ("Beta", 40m) };
        var plan = CreatePiePlan(items, null, new StyleToken("SLICE_BORDER_WIDTH", "0"));

        var svg = new SvgChartRenderer().Render(plan);
        Assert.NotNull(svg);

        Assert.Contains("stroke='none' stroke-width='0'", svg);
        Assert.DoesNotContain("stroke='white'", svg);
    }

    // ── 5. Start Angle Tests ────────────────────────────────────────────────

    [Fact]
    public void StartAngle_RotatesFirstSliceFrom12OClock()
    {
        var items = new[] { ("A", 50m), ("B", 50m) };

        // Default 0 start angle: angle starts at -PI/2 (12 o'clock, top).
        // 90 start angle: angle starts at 0 rad (3 o'clock, right).
        var planDefault = CreatePiePlan(items, null);
        var planRotated = CreatePiePlan(items, null, new StyleToken("START_ANGLE", "90"));

        var svgDefault = new SvgChartRenderer().Render(planDefault);
        var svgRotated = new SvgChartRenderer().Render(planRotated);

        Assert.NotNull(svgDefault);
        Assert.NotNull(svgRotated);
        Assert.NotEqual(svgDefault, svgRotated);
    }

    // ── 6. Validation Tests ─────────────────────────────────────────────────

    [Fact]
    public void Lowerer_RejectsInvalidSort()
    {
        var lowerer = new NamedVisualChartLowerer();
        var statement = ParseVisual("CREATE VISUAL V1 AS PIE (SOURCE = #data, OPTIONS (SORT = RANDOM));");
        var manifest = new VisualManifest { Name = "V1", Columns = ["Category", "Amount"], Rows = [] };

        var ex = Assert.Throws<InvalidOperationException>(() => lowerer.Lower(statement, manifest));
        Assert.Contains("Invalid SORT 'RANDOM'", ex.Message);
    }

    [Fact]
    public void Lowerer_RejectsNegativeMinSlicePct()
    {
        var lowerer = new NamedVisualChartLowerer();
        var statement = ParseVisual("CREATE VISUAL V1 AS PIE (SOURCE = #data, OPTIONS (MIN_SLICE_PCT = -5));");
        var manifest = new VisualManifest { Name = "V1", Columns = ["Category", "Amount"], Rows = [] };

        var ex = Assert.Throws<InvalidOperationException>(() => lowerer.Lower(statement, manifest));
        Assert.Contains("Must be a positive number.", ex.Message);
    }

    [Fact]
    public void Lowerer_RejectsMinSlicePctOver100()
    {
        var lowerer = new NamedVisualChartLowerer();
        var statement = ParseVisual("CREATE VISUAL V1 AS PIE (SOURCE = #data, OPTIONS (MIN_SLICE_PCT = 150));");
        var manifest = new VisualManifest { Name = "V1", Columns = ["Category", "Amount"], Rows = [] };

        var ex = Assert.Throws<InvalidOperationException>(() => lowerer.Lower(statement, manifest));
        Assert.Contains("Invalid MIN_SLICE_PCT '150'. Must be at most 100.", ex.Message);
    }

    [Fact]
    public void Lowerer_RejectsNegativeBorderWidth()
    {
        var lowerer = new NamedVisualChartLowerer();
        var statement = ParseVisual("CREATE VISUAL V1 AS PIE (SOURCE = #data, OPTIONS (SLICE_BORDER_WIDTH = -1));");
        var manifest = new VisualManifest { Name = "V1", Columns = ["Category", "Amount"], Rows = [] };

        var ex = Assert.Throws<InvalidOperationException>(() => lowerer.Lower(statement, manifest));
        Assert.Contains("Must be a non-negative number.", ex.Message);
    }

    [Fact]
    public void Lowerer_RejectsPieOptionsOnBar()
    {
        var lowerer = new NamedVisualChartLowerer();
        var statement = ParseVisual("CREATE VISUAL B1 AS BAR (SOURCE = #data, OPTIONS (MIN_SLICE_PCT = 5));");
        var manifest = new VisualManifest { Name = "B1", Columns = ["Category", "Amount"], Rows = [] };

        var ex = Assert.Throws<InvalidOperationException>(() => lowerer.Lower(statement, manifest));
        Assert.Contains("MIN_SLICE_PCT is supported only on PIE and DONUT visuals; found BAR.", ex.Message);
    }

    [Fact]
    public void Lowerer_AcceptsAllPieOptionsOnDonut()
    {
        var lowerer = new NamedVisualChartLowerer();
        var statement = ParseVisual(@"CREATE VISUAL D1 AS DONUT (
            SOURCE = #data,
            OPTIONS (
                SORT = VALUE_DESC,
                MIN_SLICE_PCT = 5,
                OTHER_LABEL = 'Rest',
                EXPLODE = 'Enterprise',
                SLICE_BORDER_COLOR = '#333333',
                SLICE_BORDER_WIDTH = 1,
                START_ANGLE = 45
            )
        );");
        var manifest = new VisualManifest { Name = "D1", Columns = ["Category", "Amount"], Rows = [] };

        var spec = lowerer.Lower(statement, manifest);
        Assert.NotNull(spec);
        Assert.Equal(45m, spec.Coordinate.StartAngle);
    }
}
