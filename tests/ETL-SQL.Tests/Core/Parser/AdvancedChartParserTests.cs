using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Tests.Core.Parsing;

public sealed class AdvancedChartParserTests
{
    [Fact]
    public void CustomChart_ParsesCompleteLayerScaleConditionAndFacetGrammar()
    {
        var statement = ParseVisual(CompleteChart);
        var chart = Assert.IsType<AdvancedChartDefinition>(statement.AdvancedChart);

        Assert.Equal(AdvancedChartCoordinateKind.Cartesian, chart.Coordinate.Kind);
        Assert.Equal(3, chart.Scales.Length);
        Assert.Equal(["revenue_bars", "margin_points"], chart.Layers.Select(layer => layer.Name));
        Assert.Equal(AdvancedChartAxisRole.Secondary, chart.Layers[1].Encodings.Single(e => e.Channel == AdvancedChartChannel.Y2).Axis);
        Assert.Equal(2, chart.Layers[1].Conditions.Length);
        Assert.Equal("Region", chart.Facet!.RowField);
        Assert.Equal("Segment", chart.Facet.ColumnField);
        Assert.Equal(AdvancedChartResolutionMode.Independent, chart.Resolution.Y);
    }

    [Fact]
    public void CustomChart_CanonicalFormatterRoundTripsSemanticShape()
    {
        var original = ParseVisual(CompleteChart);
        var formatted = original.ToSql();
        var reparsed = ParseVisual(formatted);

        Assert.Contains("CREATE VISUAL RevenueMargin AS CUSTOM", formatted);
        Assert.Contains("CHART (", formatted);
        Assert.Contains("CONDITIONS (", formatted);
        Assert.Equal(
            original.AdvancedChart!.Scales.Select(scale => (scale.Name, scale.Kind, scale.Channel, scale.IncludeZero)),
            reparsed.AdvancedChart!.Scales.Select(scale => (scale.Name, scale.Kind, scale.Channel, scale.IncludeZero)));
        Assert.Equal(
            original.AdvancedChart.Layers.Select(layer => (layer.Name, layer.Mark, layer.ZIndex)),
            reparsed.AdvancedChart.Layers.Select(layer => (layer.Name, layer.Mark, layer.ZIndex)));
        Assert.Equal(original.AdvancedChart.Facet!.RowField, reparsed.AdvancedChart.Facet!.RowField);
        Assert.Equal(original.AdvancedChart.Facet.ColumnField, reparsed.AdvancedChart.Facet.ColumnField);
        Assert.Equal(
            (original.AdvancedChart.Resolution.X, original.AdvancedChart.Resolution.Y, original.AdvancedChart.Resolution.Color),
            (reparsed.AdvancedChart.Resolution.X, reparsed.AdvancedChart.Resolution.Y, reparsed.AdvancedChart.Resolution.Color));
        Assert.Equal(formatted, reparsed.ToSql());
    }

    [Theory]
    [InlineData("CREATE VISUAL Broken AS CUSTOM (SOURCE = #data);")]
    [InlineData("CREATE VISUAL Broken AS BAR (SOURCE = #data, CHART (COORDINATE (TYPE = CARTESIAN), SCALES (x = BAND (CHANNEL = X)), LAYERS (bars = RECT (ENCODINGS (X = Name (TYPE = NOMINAL, SCALE = x))))));")]
    [InlineData("CREATE VISUAL Broken AS CUSTOM (SOURCE = #data, MAPPINGS (X = Name), CHART (COORDINATE (TYPE = CARTESIAN), SCALES (x = BAND (CHANNEL = X)), LAYERS (bars = RECT (ENCODINGS (X = Name (TYPE = NOMINAL, SCALE = x))))));")]
    public void CustomChart_RejectsIncompleteOrMixedAuthoringForms(string sql)
    {
        var script = Parse(sql);
        Assert.Contains(script.Diagnostics, diagnostic => diagnostic.Severity == ETL_SQL.Core.Common.DiagnosticSeverity.Error);
    }

    private static CreateVisualStatement ParseVisual(string sql)
    {
        var script = Parse(sql);
        Assert.Empty(script.Diagnostics);
        return Assert.Single(script.Statements.OfType<CreateVisualStatement>());
    }

    private static Script Parse(string sql) => new Parser(new Lexer(sql).Tokenize(), sql).Parse();

    private const string CompleteChart = """
        CREATE VISUAL RevenueMargin AS CUSTOM (
          TITLE = 'Revenue and margin',
          SOURCE = #monthly,
          CHART (
            COORDINATE (TYPE = CARTESIAN),
            SCALES (
              months = BAND (CHANNEL = X, INCLUDE_ZERO = OFF, ORDER = SOURCE),
              revenue = LINEAR (CHANNEL = Y, INCLUDE_ZERO = ON, MIN = 0),
              margin = LINEAR (CHANNEL = Y2, INCLUDE_ZERO = OFF)
            ),
            LAYERS (
              revenue_bars = RECT (
                ENCODINGS (
                  X = Month (TYPE = ORDINAL, SCALE = months),
                  Y = Revenue (TYPE = QUANTITATIVE, SCALE = revenue, AXIS = PRIMARY)
                )
              ),
              margin_points = POINT (
                Z_INDEX = 1,
                ENCODINGS (
                  X = Month (TYPE = ORDINAL, SCALE = months),
                  Y2 = MarginPct (TYPE = QUANTITATIVE, SCALE = margin, AXIS = SECONDARY)
                ),
                CONDITIONS (
                  COLOR WHEN MarginPct < 0 THEN '#C0392B' ELSE '#2E86C1',
                  OPACITY WHEN IsForecast = TRUE THEN 0.45 ELSE 1
                )
              )
            ),
            FACET (ROW = Region, COLUMN = Segment),
            RESOLVE (X = SHARED, Y = INDEPENDENT, COLOR = SHARED)
          )
        );
        """;
}
