using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Renderers;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Reporting.Semantics.Runtime;
using Xunit;

namespace ETL_SQL.Tests.Reporting.Radar;

public class RadarControlsTests
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

    private static (ChartSpec Spec, VisualManifest Manifest) ParseAndLower(
        string script,
        List<List<string>>? rows = null,
        List<string>? columns = null)
    {
        var statement = ParseVisual(script);
        var cols = columns ?? ["Model", "Speed", "Reliability", "Efficiency", "Coverage"];
        var defaultRows = rows ??
        [
            ["Model A", "80", "70", "90", "85"],
            ["Model B", "60", "90", "70", "65"]
        ];

        var manifest = new VisualManifest
        {
            Name = statement.Name,
            VisualType = statement.VisualType.ToString().ToUpperInvariant(),
            Columns = cols,
            Rows = defaultRows
        };
        foreach (var opt in statement.Options)
        {
            manifest.Options[opt.Key] = opt.Value;
        }

        var lowerer = new NamedVisualChartLowerer();
        var spec = lowerer.Lower(statement, manifest);
        return (spec, manifest);
    }

    private static string RenderToSvg(string script, List<List<string>>? rows = null, List<string>? columns = null)
    {
        var (spec, manifest) = ParseAndLower(script, rows, columns);
        var dataBuilder = new VisualChartDataBuilder();
        var data = dataBuilder.Build(spec, manifest);
        var plan = new PlotPlanResolver().Resolve(spec, data, new PlotBounds(0, 0, 800, 500));
        return new SvgChartRenderer().Render(plan);
    }

    [Fact]
    public void Radar_Shape_Circle_RendersConcentricCircles()
    {
        var script = @"CREATE VISUAL CircleRadar AS RADAR (
            SOURCE = #models,
            OPTIONS (
                SHAPE = CIRCLE
            )
        );";

        var svg = RenderToSvg(script);

        Assert.Contains("data-shape='circle'", svg);
        Assert.Contains("<circle cx=", svg);
        Assert.DoesNotContain("<polygon points=", svg.Substring(0, svg.IndexOf("<g class='plot-radar'", StringComparison.Ordinal) + 400));
    }

    [Fact]
    public void Radar_Shape_Polygon_RendersNestedPolygons()
    {
        var script = @"CREATE VISUAL PolyRadar AS RADAR (
            SOURCE = #models,
            OPTIONS (
                SHAPE = POLYGON
            )
        );";

        var svg = RenderToSvg(script);

        Assert.Contains("data-shape='polygon'", svg);
        Assert.Contains("<polygon points=", svg);
        Assert.DoesNotContain("<circle cx=", svg);
    }

    [Fact]
    public void Radar_FillOpacity_RendersCustomOpacity()
    {
        var script = @"CREATE VISUAL OpacityRadar AS RADAR (
            SOURCE = #models,
            OPTIONS (
                FILL_OPACITY = 0.42
            )
        );";

        var svg = RenderToSvg(script);

        Assert.Contains("fill-opacity='0.42'", svg);
    }

    [Fact]
    public void Radar_Fill_Off_RendersNoFill()
    {
        var script = @"CREATE VISUAL NoFillRadar AS RADAR (
            SOURCE = #models,
            OPTIONS (
                FILL = OFF
            )
        );";

        var svg = RenderToSvg(script);

        Assert.Contains("fill='none'", svg);
    }

    [Fact]
    public void Radar_IndependentAxes_On_AutoScalesPerDimension()
    {
        var script = @"CREATE VISUAL IndAxesRadar AS RADAR (
            SOURCE = #telemetry,
            OPTIONS (
                INDEPENDENT_AXES = ON
            )
        );";

        var cols = new List<string> { "Server", "RequestsSec", "LatencyMs", "UptimePct" };
        var rows = new List<List<string>>
        {
            new() { "Server 1", "1000", "20", "99" },
            new() { "Server 2", "2000", "40", "99.5" }
        };

        var svg = RenderToSvg(script, rows, cols);

        Assert.Contains("data-independent-axes='true'", svg);
        Assert.Contains("[0..2000]", svg);
        Assert.Contains("[0..40]", svg);
        Assert.Contains("[0..99.5]", svg);
    }

    [Theory]
    [InlineData("SHAPE", "TRIANGLE", "Invalid SHAPE 'TRIANGLE'. Valid values are POLYGON or CIRCLE.")]
    [InlineData("FILL_OPACITY", "1.5", "Invalid FILL_OPACITY '1.5'. Must be a number between 0.0 and 1.0.")]
    [InlineData("FILL_OPACITY", "-0.2", "Invalid FILL_OPACITY '-0.2'. Must be a number between 0.0 and 1.0.")]
    [InlineData("INDEPENDENT_AXES", "MAYBE", "Invalid INDEPENDENT_AXES 'MAYBE'. Valid values are ON or OFF.")]
    [InlineData("FILL", "SOMETIMES", "Invalid FILL 'SOMETIMES'. Valid values are ON or OFF.")]
    public void Radar_InvalidOptions_ThrowDescriptiveExceptions(string optKey, string optVal, string expectedMsg)
    {
        var script = $@"CREATE VISUAL BadRadar AS RADAR (
            SOURCE = #models,
            OPTIONS (
                {optKey} = '{optVal}'
            )
        );";

        var ex = Assert.Throws<InvalidOperationException>(() => ParseAndLower(script));
        Assert.Contains(expectedMsg, ex.Message);
    }
}
