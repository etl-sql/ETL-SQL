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

namespace ETL_SQL.Tests.Reporting.BoxPlot;

public class BoxPlotControlsTests
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
        var cols = columns ?? (statement.Mappings.Count > 0 ? statement.Mappings.Select(m => m.Column).ToList() : ["Cohort", "Score"]);
        var defaultRows = rows ??
        [
            ["A", "10"], ["A", "15"], ["A", "20"], ["A", "25"], ["A", "30"], ["A", "35"], ["A", "40"],
            ["B", "12"], ["B", "18"], ["B", "22"], ["B", "28"], ["B", "32"], ["B", "38"], ["B", "50"]
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
    public void BoxPlot_Notched_RendersNotchedPolygonPath()
    {
        var script = @"CREATE VISUAL NotchedPlot AS BOXPLOT (
            SOURCE = #stats,
            MAPPINGS (X = Cat, LOW = LowVal, Q1 = Q1Val, MEDIAN = MedVal, Q3 = Q3Val, HIGH = HighVal),
            OPTIONS (
                NOTCHED = ON
            )
        );";

        var rows = new List<List<string>>
        {
            new() { "Group1", "10", "20", "30", "40", "50" }
        };
        var cols = new List<string> { "Cat", "LowVal", "Q1Val", "MedVal", "Q3Val", "HighVal" };

        var svg = RenderToSvg(script, rows, cols);

        Assert.Contains("data-notched='true'", svg);
        Assert.Contains("class='plot-boxplot-box'", svg);
    }

    [Fact]
    public void BoxPlot_ShowMean_Precalculated_RendersDiamondMarker()
    {
        var script = @"CREATE VISUAL MeanPlot AS BOXPLOT (
            SOURCE = #stats,
            MAPPINGS (X = Cat, LOW = LowVal, Q1 = Q1Val, MEDIAN = MedVal, MEAN = MeanVal, Q3 = Q3Val, HIGH = HighVal),
            OPTIONS (
                SHOW_MEAN = ON,
                MEAN_COLOR = '#f59e0b'
            )
        );";

        var rows = new List<List<string>>
        {
            new() { "Group1", "10", "20", "30", "32", "40", "50" }
        };
        var cols = new List<string> { "Cat", "LowVal", "Q1Val", "MedVal", "MeanVal", "Q3Val", "HighVal" };

        var svg = RenderToSvg(script, rows, cols);

        Assert.Contains("class='plot-boxplot-mean'", svg);
        Assert.Contains("fill='#f59e0b'", svg);
        Assert.Contains("data-mean='32'", svg);
    }

    [Fact]
    public void BoxPlot_ShowMean_RawObservations_ComputesAndRendersDiamondMarker()
    {
        var script = @"CREATE VISUAL RawMeanPlot AS BOXPLOT (
            SOURCE = #obs,
            MAPPINGS (X = Cohort, Y = Score),
            OPTIONS (
                SHOW_MEAN = ON
            )
        );";

        var svg = RenderToSvg(script);

        Assert.Contains("class='plot-boxplot-mean'", svg);
        Assert.Contains("data-mean=", svg);
    }

    [Fact]
    public void BoxPlot_ViolinDensity_Overlay_RendersViolinHullAndBox()
    {
        var script = @"CREATE VISUAL ViolinOverlayPlot AS BOXPLOT (
            SOURCE = #obs,
            MAPPINGS (X = Cohort, Y = Score),
            OPTIONS (
                BOX_STYLE = BOTH,
                SHOW_VIOLIN = ON,
                VIOLIN_COLOR = '#8b5cf6'
            )
        );";

        var svg = RenderToSvg(script);

        Assert.Contains("class='plot-violin'", svg);
        Assert.Contains("fill='#8b5cf6'", svg);
        Assert.Contains("class='plot-boxplot-box'", svg);
    }

    [Fact]
    public void BoxPlot_ViolinDensity_ViolinOnly_RendersViolinHullWithoutBox()
    {
        var script = @"CREATE VISUAL PureViolinPlot AS BOXPLOT (
            SOURCE = #obs,
            MAPPINGS (X = Cohort, Y = Score),
            OPTIONS (
                BOX_STYLE = VIOLIN
            )
        );";

        var svg = RenderToSvg(script);

        Assert.Contains("class='plot-violin'", svg);
        Assert.DoesNotContain("class='plot-boxplot-box'", svg);
    }

    [Fact]
    public void BoxPlot_Orientation_Horizontal_RendersHorizontalLayout()
    {
        var script = @"CREATE VISUAL HorizPlot AS BOXPLOT (
            SOURCE = #obs,
            MAPPINGS (X = Cohort, Y = Score),
            OPTIONS (
                ORIENTATION = HORIZONTAL,
                NOTCHED = ON,
                SHOW_MEAN = ON
            )
        );";

        var svg = RenderToSvg(script);

        Assert.Contains("data-notched='true'", svg);
        Assert.Contains("class='plot-boxplot-mean'", svg);
    }

    [Fact]
    public void BoxPlot_InvalidBoxStyle_ThrowsException()
    {
        var script = @"CREATE VISUAL BadStyle AS BOXPLOT (
            SOURCE = #obs,
            MAPPINGS (X = Cohort, Y = Score),
            OPTIONS (
                BOX_STYLE = HEXAGON
            )
        );";

        Assert.Throws<InvalidOperationException>(() => ParseAndLower(script));
    }

    [Fact]
    public void BoxPlot_InvalidOrientation_ThrowsException()
    {
        var script = @"CREATE VISUAL BadOrient AS BOXPLOT (
            SOURCE = #obs,
            MAPPINGS (X = Cohort, Y = Score),
            OPTIONS (
                ORIENTATION = DIAGONAL
            )
        );";

        Assert.Throws<InvalidOperationException>(() => ParseAndLower(script));
    }
}
