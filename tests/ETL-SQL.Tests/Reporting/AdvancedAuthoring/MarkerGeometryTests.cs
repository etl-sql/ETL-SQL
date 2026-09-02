using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Renderers;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Reporting.Semantics.Runtime;
using Xunit;

namespace ETL_SQL.Tests.Reporting.AdvancedAuthoring;

public sealed class MarkerGeometryTests
{
    [Theory]
    [InlineData("CIRCLE", "circle")]
    [InlineData("SQUARE", "rect")]
    [InlineData("TRIANGLE", "polygon")]
    [InlineData("DIAMOND", "polygon")]
    [InlineData("CROSS", "polygon")]
    [InlineData("STAR", "polygon")]
    public void NamedLineAndScatter_RenderSymbolShape(string shape, string elementName)
    {
        foreach (var visualType in new[] { "LINE", "SCATTER" })
        {
            var sql = $"CREATE VISUAL V AS {visualType} (SOURCE = #data, MAPPINGS (X = XValue, Y = YValue), OPTIONS (SYMBOL_SHAPE = {shape}));";
            var plan = ResolveNamed(sql, Manifest(["XValue", "YValue"], [["1", "10"], ["2", "20"]]));
            var primary = Assert.Single(plan.Layers, layer => layer.Id == "primary");
            Assert.Contains(primary.Style, token => token.Name == "symbolShape" && token.Value == shape);

            var document = XDocument.Parse(new SvgChartRenderer().Render(plan));
            var symbols = document.Descendants()
                .Where(element => (string?)element.Attribute("data-symbol-shape") == shape)
                .ToList();
            Assert.Equal(2, symbols.Count);
            Assert.All(symbols, symbol => Assert.Equal(elementName, symbol.Name.LocalName));
        }
    }

    [Fact]
    public void CustomPoint_ShapeEncodingRendersDocumentedVocabulary()
    {
        const string sql = """
            CREATE VISUAL V AS CUSTOM (
              SOURCE = #data,
              CHART (
                COORDINATE (TYPE = CARTESIAN),
                LAYERS (
                  points = POINT (ENCODINGS (
                    X = XValue (TYPE = QUANTITATIVE),
                    Y = YValue (TYPE = QUANTITATIVE),
                    SHAPE = MarkerShape (TYPE = NOMINAL)
                  ))
                )
              )
            );
            """;
        var shapes = new[] { "CIRCLE", "SQUARE", "TRIANGLE", "DIAMOND", "CROSS", "STAR" };
        var rows = shapes.Select((shape, index) => new List<string?>
            { (index + 1).ToString(), (index + 1).ToString(), shape }).ToList();
        var plan = ResolveCustom(sql, Manifest(["XValue", "YValue", "MarkerShape"], rows));
        var document = XDocument.Parse(new SvgChartRenderer().Render(plan));

        foreach (var shape in shapes)
            Assert.Single(document.Descendants(), element => (string?)element.Attribute("data-symbol-shape") == shape);
    }

    [Fact]
    public void CustomPoint_InvalidRuntimeShapeFallsBackToCircle()
    {
        const string sql = """
            CREATE VISUAL V AS CUSTOM (
              SOURCE = #data,
              CHART (COORDINATE (TYPE = CARTESIAN), LAYERS (
                points = POINT (ENCODINGS (
                  X = XValue (TYPE = QUANTITATIVE), Y = YValue (TYPE = QUANTITATIVE),
                  SHAPE = MarkerShape (TYPE = NOMINAL)
                ))
              ))
            );
            """;
        var plan = ResolveCustom(sql, Manifest(["XValue", "YValue", "MarkerShape"], [["1", "2", "HEXAGON"]]));
        var document = XDocument.Parse(new SvgChartRenderer().Render(plan));
        var symbol = Assert.Single(document.Descendants(), element => element.Attribute("data-symbol-shape") is not null);
        Assert.Equal("circle", symbol.Name.LocalName);
        Assert.Equal("CIRCLE", (string?)symbol.Attribute("data-symbol-shape"));
    }

    [Fact]
    public void CustomPoint_ConditionalShapeOverridesTheShapeField()
    {
        const string sql = """
            CREATE VISUAL V AS CUSTOM (
              SOURCE = #data,
              CHART (COORDINATE (TYPE = CARTESIAN), LAYERS (
                points = POINT (
                  ENCODINGS (
                    X = XValue (TYPE = QUANTITATIVE), Y = YValue (TYPE = QUANTITATIVE),
                    SHAPE = MarkerShape (TYPE = NOMINAL)
                  ),
                  CONDITIONS (SHAPE WHEN YValue > 0 THEN 'STAR' ELSE 'DIAMOND')
                )
              ))
            );
            """;
        var plan = ResolveCustom(sql, Manifest(["XValue", "YValue", "MarkerShape"],
            [["1", "2", "SQUARE"], ["2", "-1", "SQUARE"]]));
        var document = XDocument.Parse(new SvgChartRenderer().Render(plan));
        Assert.Single(document.Descendants(), element => (string?)element.Attribute("data-symbol-shape") == "STAR");
        Assert.Single(document.Descendants(), element => (string?)element.Attribute("data-symbol-shape") == "DIAMOND");
        Assert.DoesNotContain(document.Descendants(), element => (string?)element.Attribute("data-symbol-shape") == "SQUARE");
    }

    [Fact]
    public void CustomPoint_RejectsInvalidAuthoredShapeConstants()
    {
        const string sql = """
            CREATE VISUAL V AS CUSTOM (
              SOURCE = #data,
              CHART (COORDINATE (TYPE = CARTESIAN), LAYERS (
                points = POINT (
                  ENCODINGS (
                    X = XValue (TYPE = QUANTITATIVE), Y = YValue (TYPE = QUANTITATIVE),
                    SHAPE = VALUE('HEXAGON') (TYPE = NOMINAL)
                  ),
                  CONDITIONS (SHAPE WHEN YValue > 0 THEN 'STAR' ELSE 'OCTAGON')
                )
              ))
            );
            """;
        var statement = ParseVisual(sql);
        var diagnostics = AdvancedChartSemanticValidator.Validate(statement);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Message.Contains("found 'HEXAGON'", StringComparison.Ordinal));
        Assert.Contains(diagnostics, diagnostic => diagnostic.Message.Contains("found 'OCTAGON'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("LINE", "HEXAGON", "Invalid SYMBOL_SHAPE")]
    [InlineData("BAR", "STAR", "supported only on LINE and SCATTER")]
    public void NamedSymbolShape_RejectsInvalidValueOrVisual(string visualType, string shape, string expected)
    {
        var statement = ParseVisual($"CREATE VISUAL V AS {visualType} (SOURCE = #data, MAPPINGS (X = XValue, Y = YValue), OPTIONS (SYMBOL_SHAPE = {shape}));");
        var error = Assert.Throws<InvalidOperationException>(() =>
            new NamedVisualChartLowerer(new SystemExecutionContext()).Lower(statement,
                Manifest(["XValue", "YValue"], [["1", "2"]])));
        Assert.Contains(expected, error.Message);
    }

    [Fact]
    public void SymbolShape_RoundTripsAsAnOrdinaryVisualOption()
    {
        const string sql = "CREATE VISUAL V AS LINE (SOURCE = #data, MAPPINGS (X = XValue, Y = YValue), OPTIONS (SYMBOL_SHAPE = STAR));";
        var canonical = ParseVisual(sql).ToSql();
        Assert.Contains("SYMBOL_SHAPE = 'STAR'", canonical);
        Assert.Empty(new Parser(new Lexer(canonical).Tokenize(), canonical).Parse().Diagnostics);
    }

    [Theory]
    [InlineData("LINE")]
    [InlineData("SCATTER")]
    public void NamedPointStroke_RendersPortableColorAndWidth(string visualType)
    {
        var sql = $"CREATE VISUAL V AS {visualType} (SOURCE = #data, MAPPINGS (X = XValue, Y = YValue), OPTIONS (SYMBOL_STROKE_COLOR = '#123abc', SYMBOL_STROKE_WIDTH = 2.5));";
        var plan = ResolveNamed(sql, Manifest(["XValue", "YValue"], [["1", "10"]]));
        var symbol = Assert.Single(XDocument.Parse(new SvgChartRenderer().Render(plan)).Descendants(),
            element => (string?)element.Attribute("stroke") == "#123abc");
        Assert.Equal("2.5", (string?)symbol.Attribute("stroke-width"));
    }

    [Fact]
    public void CustomPointStroke_ColorAloneUsesOnePixelDefault()
    {
        const string sql = """
            CREATE VISUAL V AS CUSTOM (
              SOURCE = #data,
              CHART (COORDINATE (TYPE = CARTESIAN), LAYERS (
                points = POINT (
                  ENCODINGS (X = XValue (TYPE = QUANTITATIVE), Y = YValue (TYPE = QUANTITATIVE)),
                  STYLE (SYMBOL_STROKE_COLOR = '#abcdef')
                )
              ))
            );
            """;
        var plan = ResolveCustom(sql, Manifest(["XValue", "YValue"], [["1", "2"]]));
        var symbol = Assert.Single(XDocument.Parse(new SvgChartRenderer().Render(plan)).Descendants(),
            element => (string?)element.Attribute("stroke") == "#abcdef");
        Assert.Equal("1", (string?)symbol.Attribute("stroke-width"));
    }

    [Theory]
    [InlineData("LINE", "SYMBOL_STROKE_COLOR = 'red'", "Invalid SYMBOL_STROKE_COLOR")]
    [InlineData("SCATTER", "SYMBOL_STROKE_WIDTH = -1", "Invalid SYMBOL_STROKE_WIDTH")]
    [InlineData("BAR", "SYMBOL_STROKE_COLOR = '#123456'", "supported only on LINE and SCATTER")]
    public void NamedPointStroke_RejectsInvalidOptions(string visualType, string option, string expected)
    {
        var statement = ParseVisual($"CREATE VISUAL V AS {visualType} (SOURCE = #data, MAPPINGS (X = XValue, Y = YValue), OPTIONS ({option}));");
        var error = Assert.Throws<InvalidOperationException>(() =>
            new NamedVisualChartLowerer(new SystemExecutionContext()).Lower(statement,
                Manifest(["XValue", "YValue"], [["1", "2"]])));
        Assert.Contains(expected, error.Message);
    }

    [Fact]
    public void CustomPointStroke_RejectsInvalidOrNonPointStyles()
    {
        const string sql = """
            CREATE VISUAL V AS CUSTOM (
              SOURCE = #data,
              CHART (COORDINATE (TYPE = CARTESIAN), LAYERS (
                points = POINT (
                  ENCODINGS (X = XValue (TYPE = QUANTITATIVE), Y = YValue (TYPE = QUANTITATIVE)),
                  STYLE (SYMBOL_STROKE_COLOR = 'red', SYMBOL_STROKE_WIDTH = -1)
                ),
                line = LINE (
                  ENCODINGS (X = XValue (TYPE = QUANTITATIVE), Y = YValue (TYPE = QUANTITATIVE)),
                  STYLE (SYMBOL_STROKE_COLOR = '#123456')
                )
              ))
            );
            """;
        var diagnostics = AdvancedChartSemanticValidator.Validate(ParseVisual(sql));
        Assert.Contains(diagnostics, diagnostic => diagnostic.Message.Contains("portable #RRGGBB", StringComparison.Ordinal));
        Assert.Contains(diagnostics, diagnostic => diagnostic.Message.Contains("style 'SYMBOL_STROKE_WIDTH' must be a literal or parameter", StringComparison.Ordinal));
        Assert.Contains(diagnostics, diagnostic => diagnostic.Message.Contains("only on POINT marks", StringComparison.Ordinal));
    }

    [Fact]
    public void NamedLineWidth_RendersRequestedWidth()
    {
        const string sql = "CREATE VISUAL V AS LINE (SOURCE = #data, MAPPINGS (X = XValue, Y = YValue), OPTIONS (LINE_WIDTH = 4.5));";
        var plan = ResolveNamed(sql, Manifest(["XValue", "YValue"], [["1", "10"], ["2", "20"]]));
        var layer = Assert.Single(plan.Layers, item => item.Id == "primary");
        Assert.Contains(layer.Style, token => token.Name == "LINE_WIDTH" && token.Value == "4.5");
        Assert.Contains("stroke-width='4.5'", new SvgChartRenderer().Render(plan), StringComparison.Ordinal);
    }

    [Fact]
    public void NamedComboLineWidth_AppliesOnlyToLineSeries()
    {
        const string sql = "CREATE VISUAL V AS COMBO (SOURCE = #data, MAPPINGS (X = XValue), SERIES (BAR BarValue, LINE LineValue), OPTIONS (LINE_WIDTH = 3));";
        var plan = ResolveNamed(sql, Manifest(["XValue", "BarValue", "LineValue"], [["A", "10", "2"], ["B", "20", "4"]]));
        var line = Assert.Single(plan.Layers, layer => layer.Mark == MarkKind.Line);
        var bar = Assert.Single(plan.Layers, layer => layer.Mark == MarkKind.Rect);
        Assert.Contains(line.Style, token => token.Name == "LINE_WIDTH" && token.Value == "3");
        Assert.DoesNotContain(bar.Style, token => token.Name == "LINE_WIDTH");
        Assert.Contains("stroke-width='3'", new SvgChartRenderer().Render(plan), StringComparison.Ordinal);
    }

    [Fact]
    public void NamedComboAxisSort_OrdersTheSharedCategoryAxisAndBothSeries()
    {
        const string sql = "CREATE VISUAL V AS COMBO (SOURCE = #data, MAPPINGS (X = Category), SERIES (BAR Sales, LINE Margin), OPTIONS (AXIS_SORT = ASC));";
        var plan = ResolveNamed(sql, Manifest(
            ["Category", "Sales", "Margin"],
            [["C", "30", "3"], ["A", "10", "1"], ["B", "20", "2"]]));

        Assert.Equal(new[] { "A", "B", "C" }, plan.Scales.Single(scale => scale.Channel == FieldChannel.X).Categories.ToArray());
        Assert.All(plan.Layers, layer => Assert.Equal(
            new[] { "A", "B", "C" },
            layer.Data.Select(datum => PlotPlanResolver.Display(
                datum.Channels.Single(channel => channel.Channel == FieldChannel.X).Value)).ToArray()));
    }

    [Fact]
    public void CustomLineWidth_RendersAndRoundTrips()
    {
        const string sql = """
            CREATE VISUAL V AS CUSTOM (
              SOURCE = #data,
              CHART (COORDINATE (TYPE = CARTESIAN), LAYERS (
                trend = LINE (
                  ENCODINGS (X = XValue (TYPE = QUANTITATIVE), Y = YValue (TYPE = QUANTITATIVE)),
                  STYLE (LINE_WIDTH = 6)
                )
              ))
            );
            """;
        var statement = ParseVisual(sql);
        Assert.Contains("STYLE ( LINE_WIDTH = 6 )", statement.ToSql(), StringComparison.Ordinal);
        var plan = ResolveCustom(sql, Manifest(["XValue", "YValue"], [["1", "2"], ["2", "4"]]));
        Assert.Contains("stroke-width='6'", new SvgChartRenderer().Render(plan), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("LINE", "0", "Invalid LINE_WIDTH")]
    [InlineData("COMBO", "11", "Invalid LINE_WIDTH")]
    [InlineData("SCATTER", "2", "supported only on LINE and COMBO")]
    public void NamedLineWidth_RejectsInvalidValueOrVisual(string visualType, string width, string expected)
    {
        var statement = ParseVisual($"CREATE VISUAL V AS {visualType} (SOURCE = #data, MAPPINGS (X = XValue, Y = YValue), OPTIONS (LINE_WIDTH = {width}));");
        var error = Assert.Throws<InvalidOperationException>(() =>
            new NamedVisualChartLowerer(new SystemExecutionContext()).Lower(statement,
                Manifest(["XValue", "YValue"], [["1", "2"]])));
        Assert.Contains(expected, error.Message);
    }

    [Fact]
    public void CustomLineWidth_RejectsOutOfRangeAndNonLineStyles()
    {
        const string sql = """
            CREATE VISUAL V AS CUSTOM (
              SOURCE = #data,
              CHART (COORDINATE (TYPE = CARTESIAN), LAYERS (
                line = LINE (
                  ENCODINGS (X = XValue (TYPE = QUANTITATIVE), Y = YValue (TYPE = QUANTITATIVE)),
                  STYLE (LINE_WIDTH = 0)
                ),
                point = POINT (
                  ENCODINGS (X = XValue (TYPE = QUANTITATIVE), Y = YValue (TYPE = QUANTITATIVE)),
                  STYLE (LINE_WIDTH = 2)
                )
              ))
            );
            """;
        var diagnostics = AdvancedChartSemanticValidator.Validate(ParseVisual(sql));
        Assert.Contains(diagnostics, diagnostic => diagnostic.Message.Contains("LINE_WIDTH must be from", StringComparison.Ordinal));
        Assert.Contains(diagnostics, diagnostic => diagnostic.Message.Contains("only on LINE marks", StringComparison.Ordinal));
    }

    private static CreateVisualStatement ParseVisual(string sql)
    {
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        return Assert.Single(script.Statements.OfType<CreateVisualStatement>());
    }

    private static VisualManifest Manifest(List<string> columns, List<List<string?>> rows) => new()
    {
        Name = "V",
        Columns = columns,
        Rows = rows
    };

    private static PlotPlan ResolveNamed(string sql, VisualManifest manifest)
    {
        var statement = ParseVisual(sql);
        var spec = new NamedVisualChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        return new PlotPlanResolver().Resolve(spec, new VisualChartDataBuilder().Build(spec, manifest));
    }

    private static PlotPlan ResolveCustom(string sql, VisualManifest manifest)
    {
        var statement = ParseVisual(sql);
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        return new PlotPlanResolver().Resolve(spec, new VisualChartDataBuilder().Build(spec, manifest));
    }
}
