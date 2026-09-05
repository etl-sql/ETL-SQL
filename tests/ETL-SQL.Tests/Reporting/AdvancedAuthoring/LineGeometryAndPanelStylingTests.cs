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

/// <summary>
/// Covers the v0.19.0 chart-property gaps for line geometry (<c>INTERPOLATION</c>, <c>LINE_DASH</c>)
/// and panel styling (<c>PLOT_BACKGROUND</c>, <c>PLOT_BORDER</c>, axis typography).
/// </summary>
public sealed class LineGeometryAndPanelStylingTests
{
    private const string Line = "CREATE VISUAL V AS LINE (SOURCE = #data, MAPPINGS (X = XValue, Y = YValue), OPTIONS ({0}));";

    [Theory]
    [InlineData("STEP_BEFORE")]
    [InlineData("STEP_AFTER")]
    public void StepInterpolation_EmitsOrthogonalPathSegments(string mode)
    {
        var plan = ResolveNamed(string.Format(Line, $"INTERPOLATION = {mode}"), ThreePoints());
        var path = LinePath(plan);

        // A step path is a staircase: every segment is either purely horizontal or purely vertical.
        var points = ParsePoints(path);
        Assert.True(points.Count > 3, $"expected a staircase with extra vertices; got '{path}'");
        for (var index = 1; index < points.Count; index++)
        {
            var previous = points[index - 1];
            var current = points[index];
            Assert.True(previous.X == current.X || previous.Y == current.Y,
                $"segment {index} of '{path}' is diagonal, so no step was emitted");
        }

        // STEP_BEFORE turns vertically first, STEP_AFTER travels horizontally first.
        var firstIsVertical = points[0].X == points[1].X;
        Assert.Equal(mode == "STEP_BEFORE", firstIsVertical);
    }

    [Fact]
    public void LinearInterpolation_OverridesSmoothToggle()
    {
        var smoothed = LinePath(ResolveNamed(string.Format(Line, "SMOOTH = ON"), ThreePoints()));
        Assert.Contains(" C ", smoothed, StringComparison.Ordinal);

        var linear = LinePath(ResolveNamed(string.Format(Line, "SMOOTH = ON, INTERPOLATION = LINEAR"), ThreePoints()));
        Assert.DoesNotContain(" C ", linear, StringComparison.Ordinal);
    }

    [Fact]
    public void SmoothInterpolation_MatchesTheLegacySmoothToggle()
    {
        var legacy = LinePath(ResolveNamed(string.Format(Line, "SMOOTH = ON"), ThreePoints()));
        var explicitMode = LinePath(ResolveNamed(string.Format(Line, "INTERPOLATION = SMOOTH"), ThreePoints()));
        Assert.Equal(legacy, explicitMode);
    }

    [Theory]
    [InlineData("DASHED", "7 5")]
    [InlineData("DOTTED", "1 5")]
    public void LineDash_ReachesTheSeriesStroke(string dash, string expected)
    {
        var plan = ResolveNamed(string.Format(Line, $"LINE_DASH = {dash}"), ThreePoints());
        var document = XDocument.Parse(new SvgChartRenderer().Render(plan));
        var series = document.Descendants()
            .Where(element => element.Name.LocalName == "path" && (string?)element.Attribute("fill") == "none")
            .ToList();
        Assert.NotEmpty(series);
        Assert.Contains(series, element => (string?)element.Attribute("stroke-dasharray") == expected);
    }

    [Fact]
    public void LineDash_Solid_LeavesTheStrokeUndashed()
    {
        var plan = ResolveNamed(string.Format(Line, "LINE_DASH = SOLID"), ThreePoints());
        var document = XDocument.Parse(new SvgChartRenderer().Render(plan));
        Assert.DoesNotContain(document.Descendants(), element => element.Attribute("stroke-dasharray") is not null);
    }

    [Fact]
    public void LineGeometryOptions_AreRejectedOnVisualsThatCannotHonourThem()
    {
        foreach (var option in new[] { "INTERPOLATION = STEP_AFTER", "LINE_DASH = DASHED" })
        {
            var sql = $"CREATE VISUAL V AS BAR (SOURCE = #data, MAPPINGS (X = XValue, Y = YValue), OPTIONS ({option}));";
            var error = Assert.Throws<InvalidOperationException>(() => ResolveNamed(sql, ThreePoints()));
            Assert.Contains("supported only on LINE and COMBO", error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void LineGeometryOptions_RejectUnknownValues()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            ResolveNamed(string.Format(Line, "INTERPOLATION = STEPWISE"), ThreePoints()));
        Assert.Contains("LINEAR, SMOOTH, STEP_BEFORE, or STEP_AFTER", error.Message, StringComparison.Ordinal);

        var dashError = Assert.Throws<InvalidOperationException>(() =>
            ResolveNamed(string.Format(Line, "LINE_DASH = SQUIGGLY"), ThreePoints()));
        Assert.Contains("SOLID, DASHED, or DOTTED", dashError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomChartLayer_AcceptsInterpolationAndRejectsTypos()
    {
        const string valid = """
            CREATE VISUAL V AS CUSTOM (
              SOURCE = #data,
              CHART (COORDINATE (TYPE = CARTESIAN), LAYERS (
                line = LINE (
                  ENCODINGS (X = XValue (TYPE = QUANTITATIVE), Y = YValue (TYPE = QUANTITATIVE)),
                  STYLE (INTERPOLATION = 'STEP_AFTER', LINE_DASH = 'DASHED')
                )
              ))
            );
            """;
        Assert.Empty(AdvancedChartSemanticValidator.Validate(ParseVisual(valid)));
        var plan = ResolveCustom(valid, ThreePoints());
        var document = XDocument.Parse(new SvgChartRenderer().Render(plan));
        Assert.Contains(document.Descendants(), element => (string?)element.Attribute("stroke-dasharray") == "7 5");

        const string invalid = """
            CREATE VISUAL V AS CUSTOM (
              SOURCE = #data,
              CHART (COORDINATE (TYPE = CARTESIAN), LAYERS (
                line = LINE (
                  ENCODINGS (X = XValue (TYPE = QUANTITATIVE), Y = YValue (TYPE = QUANTITATIVE)),
                  STYLE (INTERPOLATION = 'CURVY')
                )
              ))
            );
            """;
        Assert.Contains(AdvancedChartSemanticValidator.Validate(ParseVisual(invalid)),
            diagnostic => diagnostic.Message.Contains("INTERPOLATION accepts only", StringComparison.Ordinal));
    }

    [Fact]
    public void PlotBackgroundAndBorder_PaintOnlyTheRegionBoundedByTheAxes()
    {
        var plan = ResolveNamed(
            string.Format(Line, "PLOT_BACKGROUND = '#f8fafc', PLOT_BORDER = '2px dashed #99aabb'"),
            ThreePoints());
        var document = XDocument.Parse(new SvgChartRenderer().Render(plan));
        var panel = Assert.Single(document.Descendants(),
            element => (string?)element.Attribute("class") == "plot-panel");

        Assert.Equal("#f8fafc", (string?)panel.Attribute("fill"));
        Assert.Equal("#99aabb", (string?)panel.Attribute("stroke"));
        Assert.Equal("2", (string?)panel.Attribute("stroke-width"));
        Assert.Equal("7 5", (string?)panel.Attribute("stroke-dasharray"));

        // The panel must be inset from the visual card, which is what makes it a plot area and not
        // a card background: the axis line sits on its left edge.
        var axisLine = document.Descendants()
            .First(element => (string?)element.Attribute("class") == "plot-axis-line");
        Assert.Equal((string?)axisLine.Attribute("x1"), (string?)panel.Attribute("x"));
        Assert.True(decimal.Parse((string)panel.Attribute("x")!) > 0m);
    }

    [Fact]
    public void PlotPanel_IsAbsentWhenNeitherOptionIsDeclared()
    {
        var plan = ResolveNamed(string.Format(Line, "GRID_LINES = ON"), ThreePoints());
        var document = XDocument.Parse(new SvgChartRenderer().Render(plan));
        Assert.DoesNotContain(document.Descendants(), element => (string?)element.Attribute("class") == "plot-panel");
    }

    [Fact]
    public void PlotBackground_TransparentPaintsBorderOnly()
    {
        var plan = ResolveNamed(
            string.Format(Line, "PLOT_BACKGROUND = 'transparent', PLOT_BORDER = '1px solid #cccccc'"),
            ThreePoints());
        var document = XDocument.Parse(new SvgChartRenderer().Render(plan));
        var panel = Assert.Single(document.Descendants(),
            element => (string?)element.Attribute("class") == "plot-panel");
        Assert.Equal("none", (string?)panel.Attribute("fill"));
        Assert.Equal("#cccccc", (string?)panel.Attribute("stroke"));
    }

    [Fact]
    public void AxisTypography_AppliesToTickLabelsAndTitlesIndependently()
    {
        var manifest = ThreePoints();
        // Axis titles reach the manifest through VisualBuilder, not through the lowerer, so the
        // title is seeded the way the builder would seed it.
        manifest.Options["axis:x:label"] = "Period";
        var plan = ResolveNamed(
            string.Format(Line, "AXIS_FONT_SIZE = 13, AXIS_FONT_COLOR = '#334455', AXIS_TITLE_FONT_SIZE = 17"),
            manifest);
        var document = XDocument.Parse(new SvgChartRenderer().Render(plan));

        var tickLabels = document.Descendants()
            .Where(element => (string?)element.Attribute("class") == "plot-axis-label")
            .ToList();
        Assert.NotEmpty(tickLabels);
        Assert.All(tickLabels, label =>
        {
            Assert.Equal("13", (string?)label.Attribute("font-size"));
            Assert.Equal("#334455", (string?)label.Attribute("fill"));
        });

        var title = Assert.Single(document.Descendants(),
            element => element.Value == "Period" && element.Name.LocalName == "text");
        Assert.Equal("17", (string?)title.Attribute("font-size"));
        Assert.Equal("#334455", (string?)title.Attribute("fill"));
    }

    [Fact]
    public void AxisTypographyDefaults_LeaveTheHistoricalAttributesUntouched()
    {
        var manifest = ThreePoints();
        manifest.Options["axis:x:label"] = "Period";
        var plan = ResolveNamed(string.Format(Line, "GRID_LINES = ON"), manifest);
        var svg = new SvgChartRenderer().Render(plan);
        Assert.Contains("font-size='9' fill='#666'", svg, StringComparison.Ordinal);
        Assert.Contains("font-size='10' fill='#444'", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void PanelOptions_RejectNonPortableValues()
    {
        var color = Assert.Throws<InvalidOperationException>(() =>
            ResolveNamed(string.Format(Line, "PLOT_BACKGROUND = 'rebeccapurple'"), ThreePoints()));
        Assert.Contains("portable #RRGGBB color", color.Message, StringComparison.Ordinal);

        var size = Assert.Throws<InvalidOperationException>(() =>
            ResolveNamed(string.Format(Line, "AXIS_FONT_SIZE = 'large'"), ThreePoints()));
        Assert.Contains("positive point size", size.Message, StringComparison.Ordinal);
    }

    private static string LinePath(PlotPlan plan)
    {
        var document = XDocument.Parse(new SvgChartRenderer().Render(plan));
        var path = document.Descendants()
            .First(element => element.Name.LocalName == "path" && (string?)element.Attribute("fill") == "none");
        return (string)path.Attribute("d")!;
    }

    private static List<(decimal X, decimal Y)> ParsePoints(string path)
    {
        var tokens = path.Split([' '], StringSplitOptions.RemoveEmptyEntries);
        var points = new List<(decimal X, decimal Y)>();
        for (var index = 0; index < tokens.Length; index++)
        {
            if (tokens[index] is not ("M" or "L")) continue;
            points.Add((decimal.Parse(tokens[index + 1]), decimal.Parse(tokens[index + 2])));
        }
        return points;
    }

    private static VisualManifest ThreePoints() => new()
    {
        Name = "V",
        Columns = ["XValue", "YValue"],
        Rows = [["1", "10"], ["2", "30"], ["3", "20"]]
    };

    private static CreateVisualStatement ParseVisual(string sql)
    {
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        return Assert.Single(script.Statements.OfType<CreateVisualStatement>());
    }

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
