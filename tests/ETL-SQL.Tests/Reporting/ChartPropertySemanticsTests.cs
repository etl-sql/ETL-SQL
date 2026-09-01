using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Builders;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Reporting.Semantics.Runtime;
using Xunit;

namespace ETL_SQL.Tests.Reporting;

public sealed class ChartPropertySemanticsTests
{
    [Fact]
    public void NamedBar_BandSizeOptionControlsResolvedLayerWidth()
    {
        var statement = NamedVisual(VisualType.Bar,
            new VisualOption { Key = "BAND_SIZE", Value = "0.4" });
        var manifest = Manifest("BAR");

        var spec = new NamedVisualChartLowerer().Lower(statement, manifest);

        Assert.Equal(0.4m, Assert.Single(spec.Layers).BandSize);
    }

    [Fact]
    public void NamedBar_NormalizedStackAndOuterPaddingLowerToSharedSemantics()
    {
        var statement = NamedVisual(VisualType.Bar,
            new VisualOption { Key = "STACKED", Value = "100PCT" },
            new VisualOption { Key = "OUTER_PADDING", Value = "0.5" });
        var manifest = Manifest("BAR");
        manifest.Options["STACKED"] = "100PCT";
        manifest.Options["OUTER_PADDING"] = "0.5";

        var spec = new NamedVisualChartLowerer().Lower(statement, manifest);

        Assert.All(spec.Layers.SelectMany(layer => layer.Bindings).Where(binding => binding.Channel == FieldChannel.Y),
            binding => Assert.Equal(StackMode.Normalize, binding.Stack));
        Assert.Equal(0.5m, spec.Scales.Single(scale => scale.Channel == FieldChannel.X).OuterPadding);
    }

    [Fact]
    public void NamedBar_NormalizedStackingParsesWithoutQuotes()
    {
        const string sql = "CREATE VISUAL Chart AS BAR (SOURCE = #data, MAPPINGS (X = Category, Y = Value), OPTIONS (STACKED = 100PCT));";

        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();

        Assert.Empty(script.Diagnostics);
        Assert.Equal("100PCT", Assert.Single(Assert.Single(script.Statements.OfType<CreateVisualStatement>()).Options).Value);
    }

    [Fact]
    public void NamedBar_SeriesGapChangesGroupedBarGeometry()
    {
        var noGap = RenderGroupedBars("0");
        var fullGap = RenderGroupedBars("1");

        Assert.NotEqual(noGap, fullGap);
        Assert.Contains("data-row-index='0'", noGap);
        Assert.Contains("data-row-index='0'", fullGap);
    }

    [Fact]
    public void NamedCartesianAxisControlsReachResolvedScaleAndSvg()
    {
        var statement = NamedVisual(VisualType.Scatter);
        var manifest = Manifest("SCATTER");
        manifest.Options["axis:x:min"] = "0";
        manifest.Options["axis:x:max"] = "20";
        manifest.Options["axis:x:reverse"] = "ON";
        manifest.Options["axis:x:major_tick_count"] = "3";
        manifest.Options["axis:x:minor_ticks"] = "ON";
        manifest.Options["axis:x:label_rotation"] = "45";
        manifest.Options["axis:x:label_skip"] = "1";
        manifest.Options["axis:y:include_zero"] = "OFF";

        var spec = new NamedVisualChartLowerer().Lower(statement, manifest);
        var data = new VisualChartDataBuilder().Build(spec, manifest);
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var x = plan.Scales.Single(scale => scale.Channel == FieldChannel.X);
        var y = plan.Scales.Single(scale => scale.Channel == FieldChannel.Y);

        Assert.True(x.Reverse);
        Assert.True(x.MinorTicks);
        Assert.Equal("45", x.LabelRotation);
        Assert.Equal(1, x.LabelSkip);
        Assert.Equal(3, x.Ticks.Length);
        Assert.False(y.IncludesZero);
        var svg = new SvgChartRenderer().Render(plan);
        Assert.Contains("plot-minor-tick", svg);
        Assert.Contains("rotate(-45", svg);
    }

    [Fact]
    public void CartesianGridLines_CanBeHiddenInCode()
    {
        var svg = RenderLine(new StyleToken("GRID_LINES", "OFF"));

        Assert.DoesNotContain("stroke='#e5e7eb'", svg);
        Assert.Contains("stroke='#bbb'", svg);
    }

    [Fact]
    public void CartesianGridZeroLineAndAxisSpines_UseAuthoredPresentation()
    {
        var svg = RenderLine(
            new StyleToken("GRID_LINE_COLOR", "#123456"),
            new StyleToken("GRID_LINE_DASH", "DOTTED"),
            new StyleToken("GRID_LINE_WIDTH", "2"),
            new StyleToken("MINOR_GRID_LINES", "ON"),
            new StyleToken("ZERO_LINE", "ON"),
            new StyleToken("ZERO_LINE_COLOR", "#654321"),
            new StyleToken("ZERO_LINE_DASH", "DASHED"),
            new StyleToken("axis:y:axis_line", "OFF"));

        Assert.Contains("class='plot-grid-line'", svg);
        Assert.Contains("class='plot-minor-grid-line'", svg);
        Assert.Contains("stroke='#123456' stroke-width='2' stroke-dasharray='1 5'", svg);
        Assert.Contains("class='plot-zero-line'", svg);
        Assert.Contains("stroke='#654321' stroke-width='1.5' stroke-dasharray='7 5'", svg);
        Assert.Equal(1, svg.Split("class='plot-axis-line'", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void LineSymbols_CanBeHiddenInCode()
    {
        var svg = RenderLine(new StyleToken("SYMBOLS", "OFF"));

        Assert.DoesNotContain("<circle", svg);
        Assert.Contains("<path", svg);
    }

    [Fact]
    public void OverlayFormatting_PreservesTheAuthoredChartClause()
    {
        var statement = NamedVisual(VisualType.Line);
        statement = statement with
        {
            Overlays =
            [
                new VisualOverlay
                {
                    OverlayType = OverlayType.Goal,
                    Parameter = 42,
                    LineStyle = OverlayLineStyle.Dashed,
                    Color = "#dc2626",
                    Label = "Target"
                }
            ]
        };

        var sql = statement.ToSql();

        Assert.Contains("OVERLAYS ( GOAL(42) AS DASHED WITH (COLOR = '#dc2626', LABEL = 'Target') )", sql);
    }

    [Fact]
    public void NamedChartFormatting_AssignsRuleColorsToMatchingRows()
    {
        var plan = ResolveLine();

        var formatted = VisualBuilder.ApplyChartFormatting(plan, [null, "#dc2626"]);

        var first = formatted.Layers.Single().Data[0];
        var second = formatted.Layers.Single().Data[1];
        Assert.DoesNotContain(first.Encodings, item => item.Channel == ConditionalEncodingChannel.Color);
        Assert.Equal("#dc2626", second.Encodings.Single(item =>
            item.Channel == ConditionalEncodingChannel.Color).Value.Text);
    }

    private static CreateVisualStatement NamedVisual(VisualType type, params VisualOption[] options) => new()
    {
        Name = "Chart",
        VisualType = type,
        Source = new VisualSourceExpression { TempTableName = "#data" },
        Mappings =
        [
            new VisualMapping { Role = "X", Column = "Category" },
            new VisualMapping { Role = "Y", Column = "Value" }
        ],
        Options = options.ToList()
    };

    private static VisualManifest Manifest(string type) => new()
    {
        Name = "Chart",
        VisualType = type,
        Columns = ["Category", "Value"],
        Rows = [["A", "10"], ["B", "20"]],
        Options = new Dictionary<string, string>()
    };

    private static string RenderLine(params StyleToken[] style)
        => new SvgChartRenderer().Render(ResolveLine(style));

    private static PlotPlan ResolveLine(params StyleToken[] style)
    {
        var bindings = ImmutableArray.Create(
            new FieldBinding(FieldChannel.X, "Category", DataSemanticKind.Nominal, "x"),
            new FieldBinding(FieldChannel.Y, "Value", DataSemanticKind.Quantitative, "y"));
        var spec = ChartSpec.Create(
            "line",
            "#data",
            bindings,
            [new MarkLayerSpec("primary", MarkKind.Line, 0, bindings, [])],
            new CoordinateSpec(CoordinateKind.Cartesian),
            [
                new ScaleSpec("x", FieldChannel.X, ScaleKind.Band, false, []),
                new ScaleSpec("y", FieldChannel.Y, ScaleKind.Linear, true, [])
            ],
            new FormattingSpec("en-US", "UTC", "", []),
            new NullHandlingSpec(NullValuePolicy.Gap, []),
            new ThemeSpec("default", style.ToImmutableArray()),
            new AccessibilitySpec("Line", null, null, true));
        var data = ChartDataSet.Create("#data", 2,
        [
            new ChartColumn("Category", ChartValueKind.Text, DataSemanticKind.Nominal,
                [ChartValue.From("A"), ChartValue.From("B")], []),
            new ChartColumn("Value", ChartValueKind.Decimal, DataSemanticKind.Quantitative,
                [ChartValue.From(10m), ChartValue.From(20m)], [])
        ]);

        return new PlotPlanResolver().Resolve(spec, data);
    }

    private static string RenderGroupedBars(string gap)
    {
        var spec = CreateGroupedBarSpec(gap);
        var data = ChartDataSet.Create("#data", 1,
        [
            new ChartColumn("Category", ChartValueKind.Text, DataSemanticKind.Nominal, [ChartValue.From("A")], []),
            new ChartColumn("First", ChartValueKind.Decimal, DataSemanticKind.Quantitative, [ChartValue.From(10m)], []),
            new ChartColumn("Second", ChartValueKind.Decimal, DataSemanticKind.Quantitative, [ChartValue.From(20m)], [])
        ]);
        return new SvgChartRenderer().Render(new PlotPlanResolver().Resolve(spec, data));
    }

    private static ChartSpec CreateGroupedBarSpec(string gap)
    {
        var x = new FieldBinding(FieldChannel.X, "Category", DataSemanticKind.Nominal, "x");
        var first = new FieldBinding(FieldChannel.Y, "First", DataSemanticKind.Quantitative, "y");
        var second = new FieldBinding(FieldChannel.Y, "Second", DataSemanticKind.Quantitative, "y");
        return ChartSpec.Create("bars", "#data", [x, first, second],
            [
                new MarkLayerSpec("first", MarkKind.Rect, 0, [x, first], [new StyleToken("series", "First")]),
                new MarkLayerSpec("second", MarkKind.Rect, 1, [x, second], [new StyleToken("series", "Second")])
            ],
            new CoordinateSpec(CoordinateKind.Cartesian),
            [new ScaleSpec("x", FieldChannel.X, ScaleKind.Band, false, []), new ScaleSpec("y", FieldChannel.Y, ScaleKind.Linear, true, [])],
            new FormattingSpec("en-US", "UTC", "", []), new NullHandlingSpec(NullValuePolicy.Skip, []),
            new ThemeSpec("default", [new StyleToken("SERIES_GAP", gap)]),
            new AccessibilitySpec("Grouped bars", null, null, true));
    }
}
