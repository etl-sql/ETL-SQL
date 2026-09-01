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
    public void CartesianGridLines_CanBeHiddenInCode()
    {
        var svg = RenderLine(new StyleToken("GRID_LINES", "OFF"));

        Assert.DoesNotContain("stroke='#e5e7eb'", svg);
        Assert.Contains("stroke='#bbb'", svg);
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
}
