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
using Xunit;

namespace ETL_SQL.Tests.Reporting;

public class ScatterBubbleControlsTests
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

    // ── 1. COLOR Mapping on BUBBLE ───────────────────────────────────────────

    [Fact]
    public void Bubble_WithColorMapping_GeneratesSeriesAndColorsPoints()
    {
        var script = @"CREATE VISUAL MarketMap AS BUBBLE (
            SOURCE = #market,
            MAPPINGS (
                X = Price,
                Y = Margin,
                SIZE = Volume,
                COLOR = Segment
            )
        );";
        var statement = ParseVisual(script);
        var manifest = new VisualManifest
        {
            Name = "MarketMap",
            VisualType = "BUBBLE",
            Columns = ["Price", "Margin", "Volume", "Segment"],
            Rows =
            [
                ["10", "20", "100", "Enterprise"],
                ["30", "40", "200", "Consumer"],
                ["50", "60", "300", "Enterprise"]
            ]
        };

        var lowerer = new NamedVisualChartLowerer();
        var spec = lowerer.Lower(statement, manifest);

        Assert.NotNull(spec);
        Assert.Contains(spec.Bindings, b => b.Channel == FieldChannel.Color && b.Field == "Segment");

        var dataBuilder = new VisualChartDataBuilder();
        var data = dataBuilder.Build(spec, manifest);
        var plan = new PlotPlanResolver().Resolve(spec, data, new PlotBounds(0, 0, 600, 400));

        Assert.NotNull(plan);
        Assert.Equal(2, plan.Series.Length); // Enterprise, Consumer
        Assert.Contains(plan.Legend, l => l.Label == "Enterprise");
        Assert.Contains(plan.Legend, l => l.Label == "Consumer");

        var svg = new SvgChartRenderer().Render(plan);
        Assert.NotNull(svg);
        Assert.Contains("Enterprise", svg);
        Assert.Contains("Consumer", svg);
    }

    // ── 2. Bubble Size Range Tests ──────────────────────────────────────────

    [Fact]
    public void Bubble_SizeRange_ScalesPointsToSpecifiedRange()
    {
        var script = @"CREATE VISUAL SizedBubble AS BUBBLE (
            SOURCE = #market,
            MAPPINGS (X = Price, Y = Margin, SIZE = Volume),
            OPTIONS (SIZE_RANGE = (10, 40))
        );";
        var statement = ParseVisual(script);
        var manifest = new VisualManifest
        {
            Name = "SizedBubble",
            VisualType = "BUBBLE",
            Columns = ["Price", "Margin", "Volume"],
            Rows =
            [
                ["10", "20", "100"],
                ["50", "60", "500"]
            ]
        };

        var lowerer = new NamedVisualChartLowerer();
        var spec = lowerer.Lower(statement, manifest);

        var dataBuilder = new VisualChartDataBuilder();
        var data = dataBuilder.Build(spec, manifest);
        var plan = new PlotPlanResolver().Resolve(spec, data, new PlotBounds(0, 0, 600, 400));

        var svg = new SvgChartRenderer().Render(plan);
        Assert.NotNull(svg);

        // Min value (100) should have radius 10, Max value (500) should have radius 40
        Assert.Contains("r='10'", svg);
        Assert.Contains("r='40'", svg);
    }

    [Fact]
    public void Bubble_MinAndMaxBubbleSizeOptions_ScalePoints()
    {
        var script = @"CREATE VISUAL SizedBubble AS BUBBLE (
            SOURCE = #market,
            MAPPINGS (X = Price, Y = Margin, SIZE = Volume),
            OPTIONS (MIN_BUBBLE_SIZE = 8, MAX_BUBBLE_SIZE = 32)
        );";
        var statement = ParseVisual(script);
        var manifest = new VisualManifest
        {
            Name = "SizedBubble",
            VisualType = "BUBBLE",
            Columns = ["Price", "Margin", "Volume"],
            Rows =
            [
                ["10", "20", "100"],
                ["50", "60", "500"]
            ]
        };

        var lowerer = new NamedVisualChartLowerer();
        var spec = lowerer.Lower(statement, manifest);

        var dataBuilder = new VisualChartDataBuilder();
        var data = dataBuilder.Build(spec, manifest);
        var plan = new PlotPlanResolver().Resolve(spec, data, new PlotBounds(0, 0, 600, 400));

        var svg = new SvgChartRenderer().Render(plan);
        Assert.NotNull(svg);

        Assert.Contains("r='8'", svg);
        Assert.Contains("r='32'", svg);
    }

    // ── 3. Logarithmic Axis Scale Tests ─────────────────────────────────────

    [Fact]
    public void CartesianChart_LogScaleAxes_SetsLogarithmicScaleKind()
    {
        var script = @"CREATE VISUAL LogScatter AS SCATTER (
            SOURCE = #data,
            MAPPINGS (X = ValX, Y = ValY),
            OPTIONS (
                X_AXIS (SCALE = LOG),
                Y_AXIS (SCALE = LOGARITHMIC)
            )
        );";
        var statement = ParseVisual(script);
        var manifest = new VisualManifest
        {
            Name = "LogScatter",
            VisualType = "SCATTER",
            Columns = ["ValX", "ValY"],
            Rows =
            [
                ["1", "10"],
                ["10", "100"],
                ["100", "1000"]
            ]
        };

        var lowerer = new NamedVisualChartLowerer();
        var spec = lowerer.Lower(statement, manifest);

        var xScale = spec.Scales.First(s => s.Channel == FieldChannel.X);
        var yScale = spec.Scales.First(s => s.Channel == FieldChannel.Y);

        Assert.Equal(ScaleKind.Logarithmic, xScale.Kind);
        Assert.Equal(ScaleKind.Logarithmic, yScale.Kind);
        Assert.False(xScale.IncludeZero);
        Assert.False(yScale.IncludeZero);

        var dataBuilder = new VisualChartDataBuilder();
        var data = dataBuilder.Build(spec, manifest);
        var plan = new PlotPlanResolver().Resolve(spec, data, new PlotBounds(0, 0, 600, 400));

        var svg = new SvgChartRenderer().Render(plan);
        Assert.NotNull(svg);
    }

    [Fact]
    public void CartesianChart_LogScale_RejectsIncludeZero()
    {
        var script = @"CREATE VISUAL LogScatter AS SCATTER (
            SOURCE = #data,
            MAPPINGS (X = ValX, Y = ValY),
            OPTIONS (
                X_AXIS (SCALE = LOG, INCLUDE_ZERO = ON)
            )
        );";
        var statement = ParseVisual(script);
        var manifest = new VisualManifest
        {
            Name = "LogScatter",
            VisualType = "SCATTER",
            Columns = ["ValX", "ValY"],
            Rows = [["1", "10"]]
        };

        var lowerer = new NamedVisualChartLowerer();
        var ex = Assert.Throws<InvalidOperationException>(() => lowerer.Lower(statement, manifest));
        Assert.Contains("Logarithmic scale for axis 'X' cannot use INCLUDE_ZERO = ON", ex.Message);
    }

    [Fact]
    public void CartesianChart_LogScale_RejectsNonPositiveValues()
    {
        var script = @"CREATE VISUAL LogScatter AS SCATTER (
            SOURCE = #data,
            MAPPINGS (X = ValX, Y = ValY),
            OPTIONS (
                X_AXIS (SCALE = LOG)
            )
        );";
        var statement = ParseVisual(script);
        var manifest = new VisualManifest
        {
            Name = "LogScatter",
            VisualType = "SCATTER",
            Columns = ["ValX", "ValY"],
            Rows =
            [
                ["0", "10"],
                ["10", "100"]
            ]
        };

        var lowerer = new NamedVisualChartLowerer();
        var spec = lowerer.Lower(statement, manifest);

        var dataBuilder = new VisualChartDataBuilder();
        var data = dataBuilder.Build(spec, manifest);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new PlotPlanResolver().Resolve(spec, data, new PlotBounds(0, 0, 600, 400)));
        Assert.Contains("Logarithmic scale 'x' requires positive values and domain bounds", ex.Message);
    }

    // ── 4. Jitter on SCATTER Tests ──────────────────────────────────────────

    [Fact]
    public void Scatter_JitterOn_AppliesPositionAdjustmentSpec()
    {
        var script = @"CREATE VISUAL JitteredScatter AS SCATTER (
            SOURCE = #data,
            MAPPINGS (X = ScoreA, Y = ScoreB),
            OPTIONS (JITTER = ON)
        );";
        var statement = ParseVisual(script);
        var manifest = new VisualManifest
        {
            Name = "JitteredScatter",
            VisualType = "SCATTER",
            Columns = ["ScoreA", "ScoreB"],
            Rows =
            [
                ["5", "5"],
                ["5", "5"],
                ["5", "5"]
            ]
        };

        var lowerer = new NamedVisualChartLowerer();
        var spec = lowerer.Lower(statement, manifest);

        var layer = spec.Layers.First(l => l.Id == "primary");
        Assert.NotNull(layer.Position);
        Assert.Equal(PositionAdjustmentKind.Jitter, layer.Position.Kind);
        Assert.Equal(0.15m, layer.Position.X);
        Assert.Equal(0.15m, layer.Position.Y);

        var dataBuilder = new VisualChartDataBuilder();
        var data = dataBuilder.Build(spec, manifest);
        var plan = new PlotPlanResolver().Resolve(spec, data, new PlotBounds(0, 0, 600, 400));

        var resolvedLayer = plan.Layers.First(l => l.Mark == MarkKind.Point);
        // Because of jitter, overlapping points will have distinct display offsets!
        var xOffsets = resolvedLayer.Data.Select(d => d.DisplayOffsetX).ToList();
        var yOffsets = resolvedLayer.Data.Select(d => d.DisplayOffsetY).ToList();

        Assert.False(xOffsets.All(o => o == 0m));
        Assert.False(yOffsets.All(o => o == 0m));
        Assert.Equal(3, xOffsets.Distinct().Count());
    }

    [Fact]
    public void Scatter_JitterCustomWidthAndHeight_AppliesAmplitudes()
    {
        var script = @"CREATE VISUAL JitteredScatter AS SCATTER (
            SOURCE = #data,
            MAPPINGS (X = ScoreA, Y = ScoreB),
            OPTIONS (JITTER (WIDTH = 0.25, HEIGHT = 0.1))
        );";
        var statement = ParseVisual(script);
        var manifest = new VisualManifest
        {
            Name = "JitteredScatter",
            VisualType = "SCATTER",
            Columns = ["ScoreA", "ScoreB"],
            Rows = [["5", "5"]]
        };

        var lowerer = new NamedVisualChartLowerer();
        var spec = lowerer.Lower(statement, manifest);

        var layer = spec.Layers.First(l => l.Id == "primary");
        Assert.NotNull(layer.Position);
        Assert.Equal(PositionAdjustmentKind.Jitter, layer.Position.Kind);
        Assert.Equal(0.25m, layer.Position.X);
        Assert.Equal(0.10m, layer.Position.Y);
    }

    // ── 5. Validation Constraints ───────────────────────────────────────────

    [Fact]
    public void Lowerer_RejectsJitterOnBar()
    {
        var script = @"CREATE VISUAL BarChart AS BAR (
            SOURCE = #data,
            MAPPINGS (X = Category, Y = Amount),
            OPTIONS (JITTER = ON)
        );";
        var statement = ParseVisual(script);
        var manifest = new VisualManifest
        {
            Name = "BarChart",
            VisualType = "BAR",
            Columns = ["Category", "Amount"],
            Rows = []
        };

        var lowerer = new NamedVisualChartLowerer();
        var ex = Assert.Throws<InvalidOperationException>(() => lowerer.Lower(statement, manifest));
        Assert.Contains("JITTER is supported only on SCATTER visuals; found BAR", ex.Message);
    }

    [Fact]
    public void Lowerer_RejectsInvalidJitterAmplitude()
    {
        var script = @"CREATE VISUAL BadJitter AS SCATTER (
            SOURCE = #data,
            MAPPINGS (X = ScoreA, Y = ScoreB),
            OPTIONS (JITTER (WIDTH = 1.5))
        );";
        var statement = ParseVisual(script);
        var manifest = new VisualManifest
        {
            Name = "BadJitter",
            VisualType = "SCATTER",
            Columns = ["ScoreA", "ScoreB"],
            Rows = []
        };

        var lowerer = new NamedVisualChartLowerer();
        var ex = Assert.Throws<InvalidOperationException>(() => lowerer.Lower(statement, manifest));
        Assert.Contains("Invalid JITTER width '1.5'. Must be between 0 and 1", ex.Message);
    }

    [Fact]
    public void Lowerer_RejectsSizeRangeOnPie()
    {
        var script = @"CREATE VISUAL PieChart AS PIE (
            SOURCE = #data,
            MAPPINGS (NAME = Category, VALUE = Amount),
            OPTIONS (SIZE_RANGE = (5, 20))
        );";
        var statement = ParseVisual(script);
        var manifest = new VisualManifest
        {
            Name = "PieChart",
            VisualType = "PIE",
            Columns = ["Category", "Amount"],
            Rows = []
        };

        var lowerer = new NamedVisualChartLowerer();
        var ex = Assert.Throws<InvalidOperationException>(() => lowerer.Lower(statement, manifest));
        Assert.Contains("SIZE_RANGE, MIN_BUBBLE_SIZE, and MAX_BUBBLE_SIZE are supported only on BUBBLE and SCATTER visuals; found PIE", ex.Message);
    }

    [Fact]
    public void Lowerer_RejectsMinBubbleSizeGreaterThanMax()
    {
        var script = @"CREATE VISUAL BadBubble AS BUBBLE (
            SOURCE = #data,
            MAPPINGS (X = Price, Y = Margin, SIZE = Volume),
            OPTIONS (MIN_BUBBLE_SIZE = 50, MAX_BUBBLE_SIZE = 20)
        );";
        var statement = ParseVisual(script);
        var manifest = new VisualManifest
        {
            Name = "BadBubble",
            VisualType = "BUBBLE",
            Columns = ["Price", "Margin", "Volume"],
            Rows = []
        };

        var lowerer = new NamedVisualChartLowerer();
        var ex = Assert.Throws<InvalidOperationException>(() => lowerer.Lower(statement, manifest));
        Assert.Contains("Minimum bubble size (50) cannot exceed maximum bubble size (20)", ex.Message);
    }
}
