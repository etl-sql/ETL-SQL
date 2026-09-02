using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Authoring;
using ETL_SQL.Reporting.Builders;
using ETL_SQL.Reporting.Contracts;
using ETL_SQL.Reporting.Renderers;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Reporting.Semantics.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Xunit;

namespace ETL_SQL.Tests.Reporting.AdvancedAuthoring;

public sealed class ReferenceLineOverlayTests
{
    [Fact]
    public void NamedLine_ParsesAndRoundTripsMinimalReferenceLine()
    {
        const string sql = """
            CREATE VISUAL TargetVisual AS LINE (
              SOURCE = #sales,
              MAPPINGS (
                X = Month,
                Y = Revenue
              ),
              OVERLAYS (
                REFERENCE_LINE (VALUE = 100)
              )
            );
            """;

        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        var overlay = Assert.Single(statement.Overlays);
        Assert.Equal(OverlayType.ReferenceLine, overlay.OverlayType);
        Assert.Equal(100.0, overlay.Parameter);
        Assert.Equal(OverlayLineStyle.Dashed, overlay.LineStyle);
        Assert.Null(overlay.Label);
        Assert.Null(overlay.Color);

        var serialized = statement.ToSql();
        Assert.Contains("REFERENCE_LINE (VALUE = 100, STYLE = DASHED)", serialized);
    }

    [Fact]
    public void NamedLine_ParsesAndRoundTripsFullReferenceLine()
    {
        const string sql = """
            CREATE VISUAL TargetVisual AS LINE (
              SOURCE = #sales,
              MAPPINGS (
                X = Month,
                Y = Revenue
              ),
              OVERLAYS (
                REFERENCE_LINE (
                  VALUE = 75000,
                  LABEL = 'Target',
                  STYLE = DASHED,
                  COLOR = '#dc2626'
                )
              )
            );
            """;

        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        var overlay = Assert.Single(statement.Overlays);
        Assert.Equal(OverlayType.ReferenceLine, overlay.OverlayType);
        Assert.Equal(75000.0, overlay.Parameter);
        Assert.Equal("Target", overlay.Label);
        Assert.Equal(OverlayLineStyle.Dashed, overlay.LineStyle);
        Assert.Equal("#dc2626", overlay.Color);

        var serialized = statement.ToSql();
        Assert.Contains("REFERENCE_LINE (VALUE = 75000, LABEL = 'Target', STYLE = DASHED, COLOR = '#dc2626')", serialized);
    }

    [Theory]
    [InlineData("VALUE = 0", 0.0)]
    [InlineData("VALUE = -15.5", -15.5)]
    [InlineData("VALUE = +42.75", 42.75)]
    [InlineData("VALUE = 123456.789", 123456.789)]
    public void NamedLine_ParsesNegativeZeroAndDecimalValues(string valueClause, double expectedValue)
    {
        var sql = $"""
            CREATE VISUAL TargetVisual AS LINE (
              SOURCE = #sales,
              MAPPINGS (X = Month, Y = Revenue),
              OVERLAYS (
                REFERENCE_LINE ({valueClause})
              )
            );
            """;

        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        var overlay = Assert.Single(statement.Overlays);
        Assert.Equal(expectedValue, overlay.Parameter);
    }

    [Fact]
    public void NamedLine_ParsesArbitraryPropertyOrder()
    {
        const string sql = """
            CREATE VISUAL TargetVisual AS LINE (
              SOURCE = #sales,
              MAPPINGS (X = Month, Y = Revenue),
              OVERLAYS (
                REFERENCE_LINE (
                  COLOR = '#22c55e',
                  STYLE = SOLID,
                  LABEL = 'Lower Bound',
                  VALUE = -10
                )
              )
            );
            """;

        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        var overlay = Assert.Single(statement.Overlays);
        Assert.Equal(-10.0, overlay.Parameter);
        Assert.Equal("Lower Bound", overlay.Label);
        Assert.Equal(OverlayLineStyle.Solid, overlay.LineStyle);
        Assert.Equal("#22c55e", overlay.Color);

        // Canonical serializer emits VALUE, LABEL, STYLE, COLOR
        var serialized = statement.ToSql();
        Assert.Contains("REFERENCE_LINE (VALUE = -10, LABEL = 'Lower Bound', STYLE = SOLID, COLOR = '#22c55e')", serialized);
    }

    [Fact]
    public void Parser_RejectsMissingValue_WithPositiveSourceLocation()
    {
        const string sql = """
            CREATE VISUAL TargetVisual AS LINE (
              SOURCE = #sales,
              MAPPINGS (X = Month, Y = Revenue),
              OVERLAYS (
                REFERENCE_LINE (LABEL = 'Missing Value')
              )
            );
            """;

        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        var diag = Assert.Single(script.Diagnostics);
        Assert.Contains("REFERENCE_LINE requires a VALUE property", diag.Message);
        Assert.True(diag.Line > 0, $"Expected positive line number, got {diag.Line}");
        Assert.True(diag.Column > 0, $"Expected positive column number, got {diag.Column}");
    }

    [Fact]
    public void Parser_RejectsMissingCommaBetweenProperties()
    {
        const string sql = """
            CREATE VISUAL TargetVisual AS LINE (
              SOURCE = #sales,
              MAPPINGS (X = Month, Y = Revenue),
              OVERLAYS (
                REFERENCE_LINE (VALUE = 10 LABEL = 'Missing comma')
              )
            );
            """;

        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        var diag = Assert.Single(script.Diagnostics);
        Assert.Contains("Expected ',' or ')' after REFERENCE_LINE property", diag.Message);
        Assert.True(diag.Line > 0);
        Assert.True(diag.Column > 0);
    }

    [Fact]
    public void Parser_RejectsLeadingComma()
    {
        const string sql = """
            CREATE VISUAL TargetVisual AS LINE (
              SOURCE = #sales,
              MAPPINGS (X = Month, Y = Revenue),
              OVERLAYS (
                REFERENCE_LINE (, VALUE = 10)
              )
            );
            """;

        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        var diag = Assert.Single(script.Diagnostics);
        Assert.Contains("Unexpected ',' at start of REFERENCE_LINE", diag.Message);
        Assert.True(diag.Line > 0);
        Assert.True(diag.Column > 0);
    }

    [Fact]
    public void Parser_RejectsConsecutiveCommas()
    {
        const string sql = """
            CREATE VISUAL TargetVisual AS LINE (
              SOURCE = #sales,
              MAPPINGS (X = Month, Y = Revenue),
              OVERLAYS (
                REFERENCE_LINE (VALUE = 10,, LABEL = 'Double comma')
              )
            );
            """;

        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        var diag = Assert.Single(script.Diagnostics);
        Assert.Contains("Consecutive commas are not permitted in REFERENCE_LINE", diag.Message);
        Assert.True(diag.Line > 0);
        Assert.True(diag.Column > 0);
    }

    [Fact]
    public void Parser_RejectsTrailingComma()
    {
        const string sql = """
            CREATE VISUAL TargetVisual AS LINE (
              SOURCE = #sales,
              MAPPINGS (X = Month, Y = Revenue),
              OVERLAYS (
                REFERENCE_LINE (VALUE = 10,)
              )
            );
            """;

        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        var diag = Assert.Single(script.Diagnostics);
        Assert.Contains("Trailing comma before ')' is not permitted in REFERENCE_LINE", diag.Message);
        Assert.True(diag.Line > 0);
        Assert.True(diag.Column > 0);
    }

    [Fact]
    public void Parser_RejectsDuplicateValue()
    {
        const string sql = """
            CREATE VISUAL TargetVisual AS LINE (
              SOURCE = #sales,
              MAPPINGS (X = Month, Y = Revenue),
              OVERLAYS (
                REFERENCE_LINE (VALUE = 10, VALUE = 20)
              )
            );
            """;

        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.NotEmpty(script.Diagnostics);
        Assert.Contains(script.Diagnostics, d => d.Message.Contains("Duplicate VALUE property"));
    }

    [Fact]
    public void Parser_RejectsDuplicateOptionalProperties()
    {
        const string sql = """
            CREATE VISUAL TargetVisual AS LINE (
              SOURCE = #sales,
              MAPPINGS (X = Month, Y = Revenue),
              OVERLAYS (
                REFERENCE_LINE (VALUE = 10, LABEL = 'A', LABEL = 'B')
              )
            );
            """;

        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.NotEmpty(script.Diagnostics);
        Assert.Contains(script.Diagnostics, d => d.Message.Contains("Duplicate LABEL property"));
    }

    [Fact]
    public void Parser_RejectsUnknownProperty()
    {
        const string sql = """
            CREATE VISUAL TargetVisual AS LINE (
              SOURCE = #sales,
              MAPPINGS (X = Month, Y = Revenue),
              OVERLAYS (
                REFERENCE_LINE (VALUE = 10, UNKNOWN_PROP = 'val')
              )
            );
            """;

        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.NotEmpty(script.Diagnostics);
        Assert.Contains(script.Diagnostics, d => d.Message.Contains("Unknown property 'UNKNOWN_PROP' in REFERENCE_LINE"));
    }

    [Fact]
    public void Parser_RejectsNonNumericValue()
    {
        const string sql = """
            CREATE VISUAL TargetVisual AS LINE (
              SOURCE = #sales,
              MAPPINGS (X = Month, Y = Revenue),
              OVERLAYS (
                REFERENCE_LINE (VALUE = 'NotANumber')
              )
            );
            """;

        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.NotEmpty(script.Diagnostics);
        Assert.Contains(script.Diagnostics, d => d.Message.Contains("Expected numeric value for VALUE in REFERENCE_LINE"));
    }

    [Fact]
    public void Parser_RejectsInvalidStyle()
    {
        const string sql = """
            CREATE VISUAL TargetVisual AS LINE (
              SOURCE = #sales,
              MAPPINGS (X = Month, Y = Revenue),
              OVERLAYS (
                REFERENCE_LINE (VALUE = 10, STYLE = WAVY)
              )
            );
            """;

        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.NotEmpty(script.Diagnostics);
        Assert.Contains(script.Diagnostics, d => d.Message.Contains("Expected SOLID, DASHED, or DOTTED for STYLE"));
    }

    [Theory]
    [InlineData("BAR", "X = Cat, Y = Val")]
    [InlineData("HBAR", "X = Cat, Y = Val")]
    [InlineData("LINE", "X = Cat, Y = Val")]
    [InlineData("COMBO", "X = Cat, Y = Val")]
    [InlineData("SCATTER", "X = Cat, Y = Val")]
    [InlineData("BUBBLE", "X = Cat, Y = Val, SIZE = SizeVal")]
    public void NamedVisualChartLowerer_SupportsAllCartesianNamedCharts(string visualType, string mappings)
    {
        var sql = $"""
            CREATE VISUAL Chart1 AS {visualType} (
              SOURCE = #data,
              MAPPINGS ({mappings}),
              OVERLAYS (
                REFERENCE_LINE (VALUE = 50, LABEL = 'Benchmark')
              )
            );
            """;

        var manifest = new VisualManifest
        {
            Name = "Chart1",
            Columns = ["Cat", "Val", "SizeVal"],
            Rows = [["A", "40", "10"], ["B", "60", "20"]]
        };

        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        var lowerer = new NamedVisualChartLowerer(new SystemExecutionContext());
        var spec = lowerer.Lower(statement, manifest);

        var ruleLayer = Assert.Single(spec.Layers.Where(l => l.Mark == MarkKind.Rule));
        Assert.Equal("rule-00-referenceline", ruleLayer.Id);
        Assert.Equal(100, ruleLayer.ZIndex);
        Assert.Equal("Benchmark", ruleLayer.LegendTitle);
    }

    [Fact]
    public void NamedVisualChartLowerer_OmitsLabelStyleToken_WhenLabelOmitted()
    {
        const string sql = """
            CREATE VISUAL UnlabeledLine AS LINE (
              SOURCE = #data,
              MAPPINGS (X = Cat, Y = Val),
              OVERLAYS (
                REFERENCE_LINE (VALUE = 100)
              )
            );
            """;

        var manifest = new VisualManifest
        {
            Name = "UnlabeledLine",
            Columns = ["Cat", "Val"],
            Rows = [["A", "40"]]
        };

        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        var lowerer = new NamedVisualChartLowerer(new SystemExecutionContext());
        var spec = lowerer.Lower(statement, manifest);

        var ruleLayer = Assert.Single(spec.Layers.Where(l => l.Mark == MarkKind.Rule));
        Assert.Null(ruleLayer.LegendTitle);
        Assert.False(ruleLayer.Style.Any(t => t.Name.Equals("label", StringComparison.OrdinalIgnoreCase)),
            "Layer must not contain a visual 'label' style token when LABEL is omitted");
    }

    [Theory]
    [InlineData("PIE")]
    [InlineData("DONUT")]
    [InlineData("RADAR")]
    [InlineData("GAUGE")]
    public void NamedVisualChartLowerer_RejectsPolarAndNonAxisVisuals(string visualType)
    {
        var sql = $"""
            CREATE VISUAL Chart1 AS {visualType} (
              SOURCE = #data,
              MAPPINGS (CATEGORY = Cat, VALUE = Val),
              OVERLAYS (
                REFERENCE_LINE (VALUE = 50)
              )
            );
            """;

        var manifest = new VisualManifest
        {
            Name = "Chart1",
            Columns = ["Cat", "Val"],
            Rows = [["A", "40"]]
        };

        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        var lowerer = new NamedVisualChartLowerer(new SystemExecutionContext());
        var ex = Assert.Throws<InvalidOperationException>(() => lowerer.Lower(statement, manifest));
        Assert.Contains("REFERENCE_LINE overlay is supported only on Cartesian charts", ex.Message);
    }

    [Fact]
    public void NamedVisualChartLowerer_RejectsMissingPrimaryQuantitativeMapping()
    {
        const string sql = """
            CREATE VISUAL Chart1 AS LINE (
              SOURCE = #data,
              MAPPINGS (X = Cat),
              OVERLAYS (
                REFERENCE_LINE (VALUE = 50)
              )
            );
            """;

        var manifest = new VisualManifest
        {
            Name = "Chart1",
            Columns = ["Cat"],
            Rows = [["A"]]
        };

        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        var lowerer = new NamedVisualChartLowerer(new SystemExecutionContext());
        var ex = Assert.Throws<InvalidOperationException>(() => lowerer.Lower(statement, manifest));
        Assert.Contains("requires a primary quantitative value mapping", ex.Message);
    }

    [Fact]
    public void Combo_BindsReferenceLineToPrimaryYScaleNotY2()
    {
        const string sql = """
            CREATE VISUAL ComboChart AS COMBO (
              SOURCE = #data,
              MAPPINGS (
                X = Period,
                Y = Rev1,
                Y2 = Rev2
              ),
              OVERLAYS (
                REFERENCE_LINE (VALUE = 150, LABEL = 'Primary Target')
              )
            );
            """;

        var manifest = new VisualManifest
        {
            Name = "ComboChart",
            Columns = ["Period", "Rev1", "Rev2"],
            Rows = [
                ["Jan", "100", "50"],
                ["Feb", "120", "60"]
            ]
        };

        var plan = ResolveNamed(sql, manifest);
        var yScale = plan.Scales.Single(s => s.Channel == FieldChannel.Y);
        var y2Scale = plan.Scales.FirstOrDefault(s => s.Channel == FieldChannel.Y2);

        var (yMin, yMax) = (PlotPlanResolver.Number(yScale.Domain[0])!.Value, PlotPlanResolver.Number(yScale.Domain[1])!.Value);
        Assert.True(yMax >= 150m, $"Primary Y scale domain must expand to include 150; max was {yMax}");

        if (y2Scale is not null)
        {
            var y2Max = PlotPlanResolver.Number(y2Scale.Domain[1])!.Value;
            Assert.True(y2Max < 150m, $"Y2 scale domain must not expand to 150; max was {y2Max}");
        }
    }

    [Fact]
    public void Coexistence_WithGoalAverageMovingAvgAndForecast()
    {
        const string sql = """
            CREATE VISUAL MultiOverlayChart AS LINE (
              SOURCE = #data,
              MAPPINGS (X = Period, Y = Revenue),
              OVERLAYS (
                GOAL(100) AS SOLID WITH (COLOR = '#10b981', LABEL = 'Goal 100'),
                AVERAGE AS DOTTED WITH (COLOR = '#6b7280', LABEL = 'Mean'),
                MOVING_AVG(2) AS DASHED WITH (COLOR = '#8b5cf6', LABEL = 'MA2'),
                REFERENCE_LINE (VALUE = 150, LABEL = 'Stretch Ref', STYLE = SOLID, COLOR = '#dc2626'),
                FORECAST(ForecastRev) AS DASHED WITH (COLOR = '#2563eb', LABEL = 'Forecast')
              )
            );
            """;

        var manifest = new VisualManifest
        {
            Name = "MultiOverlayChart",
            Columns = ["Period", "Revenue", "ForecastRev"],
            Rows = [
                ["Jan", "80", null],
                ["Feb", "90", "95"],
                ["Mar", "110", "120"]
            ]
        };

        var plan = ResolveNamed(sql, manifest);
        var ruleLayers = plan.Layers.Where(l => l.Mark == MarkKind.Rule).ToList();
        Assert.Equal(3, ruleLayers.Count);
        Assert.Equal("rule-00-goal", ruleLayers[0].Id);
        Assert.Equal("rule-01-average", ruleLayers[1].Id);
        Assert.Equal("rule-03-referenceline", ruleLayers[2].Id);

        Assert.Equal(100, ruleLayers[0].ZIndex);
        Assert.Equal(101, ruleLayers[1].ZIndex);
        Assert.Equal(103, ruleLayers[2].ZIndex);
    }

    [Fact]
    public void PlotPlanResolver_ExpandsPrimaryYDomainForOutlierReferenceLine()
    {
        const string sql = """
            CREATE VISUAL SalesTarget AS LINE (
              SOURCE = #data,
              MAPPINGS (X = Month, Y = Revenue),
              OVERLAYS (
                REFERENCE_LINE (VALUE = 300, LABEL = 'Outlier Target')
              )
            );
            """;

        var manifest = new VisualManifest
        {
            Name = "SalesTarget",
            Columns = ["Month", "Revenue"],
            Rows = [["Jan", "50"], ["Feb", "80"], ["Mar", "100"]]
        };

        var plan = ResolveNamed(sql, manifest);
        var yScale = plan.Scales.Single(s => s.Channel == FieldChannel.Y);
        var max = PlotPlanResolver.Number(yScale.Domain[1])!.Value;
        Assert.True(max >= 300m, $"Expected domain max >= 300 to retain visibility of reference line; was {max}");
    }

    [Fact]
    public void PlotPlanResolver_ExplicitMinMaxOverridesAutomaticDomain()
    {
        const string sql = """
            CREATE VISUAL SalesTarget AS LINE (
              SOURCE = #data,
              MAPPINGS (X = Month, Y = Revenue),
              OPTIONS (
                Y_AXIS (MIN = 0, MAX = 200)
              ),
              OVERLAYS (
                REFERENCE_LINE (VALUE = 300, LABEL = 'Outlier Target')
              )
            );
            """;

        var manifest = new VisualManifest
        {
            Name = "SalesTarget",
            Columns = ["Month", "Revenue"],
            Rows = [["Jan", "50"], ["Feb", "80"]]
        };

        var plan = ResolveNamed(sql, manifest);
        var yScale = plan.Scales.Single(s => s.Channel == FieldChannel.Y);
        var max = PlotPlanResolver.Number(yScale.Domain[1])!.Value;
        Assert.Equal(200m, max);
    }

    [Fact]
    public void SvgRenderer_CartesianHorizontalLineAndSemanticClass()
    {
        const string sql = """
            CREATE VISUAL TargetLine AS LINE (
              SOURCE = #data,
              MAPPINGS (X = Month, Y = Revenue),
              OVERLAYS (
                REFERENCE_LINE (
                  VALUE = 75,
                  LABEL = 'Goal Line',
                  STYLE = DASHED,
                  COLOR = '#dc2626'
                )
              )
            );
            """;

        var manifest = new VisualManifest
        {
            Name = "TargetLine",
            Columns = ["Month", "Revenue"],
            Rows = [["Jan", "50"], ["Feb", "90"]]
        };

        var plan = ResolveNamed(sql, manifest);
        var svg = new SvgChartRenderer().Render(plan);

        Assert.Contains("data-overlay-type='ReferenceLine'", svg);
        Assert.Contains("class='plot-reference-line'", svg);
        Assert.Contains("stroke='#dc2626'", svg);
        Assert.Contains("stroke-dasharray='7 5'", svg);

        var match = Regex.Match(svg, @"<line class='plot-reference-line' x1='(?<x1>[0-9.]+)' y1='(?<y1>[0-9.]+)' x2='(?<x2>[0-9.]+)' y2='(?<y2>[0-9.]+)'");
        Assert.True(match.Success, "Expected plot-reference-line SVG line match");
        Assert.Equal(match.Groups["y1"].Value, match.Groups["y2"].Value);
        Assert.NotEqual(match.Groups["x1"].Value, match.Groups["x2"].Value);
    }

    [Fact]
    public void SvgRenderer_UnlabeledLine_EmitsNoOverlayLabelLeaderOrBackground()
    {
        const string sql = """
            CREATE VISUAL MinimalLine AS LINE (
              SOURCE = #data,
              MAPPINGS (X = Month, Y = Revenue),
              OVERLAYS (
                REFERENCE_LINE (VALUE = 100)
              )
            );
            """;

        var manifest = new VisualManifest
        {
            Name = "MinimalLine",
            Columns = ["Month", "Revenue"],
            Rows = [["Jan", "50"], ["Feb", "90"]]
        };

        var plan = ResolveNamed(sql, manifest);
        var svg = new SvgChartRenderer().Render(plan);

        // SVG line must exist
        Assert.Contains("class='plot-reference-line'", svg);
        // But no overlay label, background rect, or leader line
        Assert.DoesNotContain("plot-overlay-label", svg);
        Assert.DoesNotContain("plot-overlay-label-leader", svg);
        Assert.DoesNotContain("plot-overlay-label-bg", svg);
        Assert.DoesNotContain("<text class='plot-overlay-label'", svg);
    }

    [Fact]
    public void SvgRenderer_TransposedHbarVerticalLine_DoesNotEmitTicksPerCategory()
    {
        const string sql = """
            CREATE VISUAL TargetHbar AS HBAR (
              SOURCE = #data,
              MAPPINGS (X = Region, Y = Revenue),
              OVERLAYS (
                REFERENCE_LINE (
                  VALUE = 60000,
                  LABEL = 'Quota',
                  STYLE = SOLID,
                  COLOR = '#10b981'
                )
              )
            );
            """;

        var manifest = new VisualManifest
        {
            Name = "TargetHbar",
            Columns = ["Region", "Revenue"],
            Rows = [
                ["North", "45000"],
                ["South", "55000"],
                ["East", "70000"],
                ["West", "80000"]
            ]
        };

        var plan = ResolveNamed(sql, manifest);
        var svg = new SvgChartRenderer().Render(plan);

        Assert.Contains("data-overlay-type='ReferenceLine'", svg);
        Assert.Contains("class='plot-reference-line'", svg);
        Assert.Contains("stroke='#10b981'", svg);

        var matches = Regex.Matches(svg, @"<line class='plot-reference-line' x1='(?<x1>[0-9.]+)' y1='(?<y1>[0-9.]+)' x2='(?<x2>[0-9.]+)' y2='(?<y2>[0-9.]+)'");
        Assert.Single(matches);

        var match = matches[0];
        Assert.Equal(match.Groups["x1"].Value, match.Groups["x2"].Value);
        Assert.NotEqual(match.Groups["y1"].Value, match.Groups["y2"].Value);
    }

    [Theory]
    [InlineData("SOLID", "")]
    [InlineData("DASHED", "stroke-dasharray='7 5'")]
    [InlineData("DOTTED", "stroke-dasharray='1 5'")]
    public void SvgRenderer_AppliesLineStylesCorrectly(string styleName, string expectedDash)
    {
        var sql = $"""
            CREATE VISUAL LineStyleTest AS LINE (
              SOURCE = #data,
              MAPPINGS (X = Month, Y = Revenue),
              OVERLAYS (
                REFERENCE_LINE (VALUE = 50, STYLE = {styleName})
              )
            );
            """;

        var manifest = new VisualManifest
        {
            Name = "LineStyleTest",
            Columns = ["Month", "Revenue"],
            Rows = [["Jan", "40"], ["Feb", "60"]]
        };

        var plan = ResolveNamed(sql, manifest);
        var svg = new SvgChartRenderer().Render(plan);

        if (string.IsNullOrEmpty(expectedDash))
            Assert.DoesNotContain("stroke-dasharray", svg);
        else
            Assert.Contains(expectedDash, svg);
    }

    [Fact]
    public void SvgRenderer_CollisionAwareOverlayLabel()
    {
        const string sql = """
            CREATE VISUAL LabeledLine AS LINE (
              SOURCE = #data,
              MAPPINGS (X = Month, Y = Revenue),
              OVERLAYS (
                REFERENCE_LINE (VALUE = 50, LABEL = 'Benchmark High', COLOR = '#e11d48')
              )
            );
            """;

        var manifest = new VisualManifest
        {
            Name = "LabeledLine",
            Columns = ["Month", "Revenue"],
            Rows = [["Jan", "40"], ["Feb", "60"]]
        };

        var plan = ResolveNamed(sql, manifest);
        var svg = new SvgChartRenderer().Render(plan);

        Assert.Contains("<text class='plot-overlay-label'", svg);
        Assert.Contains("Benchmark High", svg);
        Assert.Contains("class='plot-overlay-label-leader'", svg);
        Assert.Contains("class='plot-overlay-label-bg'", svg);
    }

    [Fact]
    public void TerminalRenderer_DisplaysAuthoredLabelAndFormattedValue()
    {
        const string sql = """
            CREATE VISUAL TerminalTest AS LINE (
              SOURCE = #data,
              MAPPINGS (X = Month, Y = Revenue),
              OVERLAYS (
                REFERENCE_LINE (VALUE = -12.5, LABEL = 'Critical Floor', COLOR = '#dc2626')
              )
            );
            """;

        var manifest = new VisualManifest
        {
            Name = "TerminalTest",
            Columns = ["Month", "Revenue"],
            Rows = [["Jan", "10"], ["Feb", "20"]]
        };

        var plan = ResolveNamed(sql, manifest);
        var terminal = PlotPlanTerminalRenderer.Render(plan, 80);
        var writer = new StringWriter();
        var testConsole = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer)
        });
        testConsole.Write(terminal);
        var output = writer.ToString();

        Assert.Contains("Critical Floor", output);
        Assert.Contains("-12.5", output);
    }

    [Fact]
    public void TerminalRenderer_UnlabeledLine_DisplaysStableDefaultReference_NeverInternalLayerId()
    {
        const string sql = """
            CREATE VISUAL TerminalUnlabeled AS LINE (
              SOURCE = #data,
              MAPPINGS (X = Month, Y = Revenue),
              OVERLAYS (
                REFERENCE_LINE (VALUE = 100)
              )
            );
            """;

        var manifest = new VisualManifest
        {
            Name = "TerminalUnlabeled",
            Columns = ["Month", "Revenue"],
            Rows = [["Jan", "10"], ["Feb", "20"]]
        };

        var plan = ResolveNamed(sql, manifest);
        var terminal = PlotPlanTerminalRenderer.Render(plan, 80);
        var writer = new StringWriter();
        var testConsole = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer)
        });
        testConsole.Write(terminal);
        var output = writer.ToString();

        Assert.Contains("Reference", output);
        Assert.DoesNotContain("rule-00-referenceline", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SemanticFallback_ProvidesAuthorReferenceLineDetail()
    {
        const string sql = """
            CREATE VISUAL FallbackTest AS LINE (
              SOURCE = #data,
              MAPPINGS (X = Month, Y = Revenue),
              OVERLAYS (
                REFERENCE_LINE (VALUE = 100, LABEL = 'My Target')
              )
            );
            """;

        var manifest = new VisualManifest
        {
            Name = "FallbackTest",
            Columns = ["Month", "Revenue"],
            Rows = [["Jan", "50"], ["Feb", "70"]]
        };

        var plan = ResolveNamed(sql, manifest);
        var fallbackItem = Assert.Single(plan.Fallback.Items.Where(i => i.Label == "My Target"));
        Assert.Equal("100", fallbackItem.Value);
        Assert.Equal("author reference line", fallbackItem.Detail);
        Assert.Equal("Reference", fallbackItem.Group);
    }

    [Fact]
    public void SemanticFallback_UnlabeledLine_UsesStableDefaultReference_NeverInternalLayerId()
    {
        const string sql = """
            CREATE VISUAL FallbackUnlabeled AS LINE (
              SOURCE = #data,
              MAPPINGS (X = Month, Y = Revenue),
              OVERLAYS (
                REFERENCE_LINE (VALUE = 100)
              )
            );
            """;

        var manifest = new VisualManifest
        {
            Name = "FallbackUnlabeled",
            Columns = ["Month", "Revenue"],
            Rows = [["Jan", "50"], ["Feb", "70"]]
        };

        var plan = ResolveNamed(sql, manifest);
        var fallbackItem = Assert.Single(plan.Fallback.Items.Where(i => i.Group == "Reference"));
        Assert.Equal("Reference", fallbackItem.Label);
        Assert.Equal("100", fallbackItem.Value);
        Assert.Equal("author reference line", fallbackItem.Detail);
        Assert.DoesNotContain("rule-00-referenceline", fallbackItem.Label, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VisualBuilder_ManifestMapping_AndJsonRoundTrip()
    {
        // Prove that a parsed REFERENCE_LINE flows through the actual VisualBuilder manifest mapping
        const string sql = """
            CREATE VISUAL FlowVisual AS LINE (
              SOURCE = #sales,
              MAPPINGS (X = Month, Y = Revenue),
              OVERLAYS (
                REFERENCE_LINE (
                  VALUE = 75000,
                  LABEL = 'Target',
                  STYLE = DASHED,
                  COLOR = '#dc2626'
                )
              )
            );
            """;

        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());

        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        var prepScript = new Parser(new Lexer("SELECT 'Jan' AS Month, 50000 AS Revenue INTO #sales;").Tokenize(), "prep").Parse();
        await evaluator.Evaluate(prepScript);

        var visualBuilder = new VisualBuilder(evaluator, new StyleBuilder(evaluator));
        var vm = await visualBuilder.BuildAsync("FlowVisual", statement);

        Assert.NotNull(vm.Overlays);
        var overlay = Assert.Single(vm.Overlays);
        Assert.Equal("ReferenceLine", overlay.OverlayType);
        Assert.Equal(75000.0, overlay.Parameter);
        Assert.Equal("dashed", overlay.LineStyle);
        Assert.Equal("#dc2626", overlay.Color);
        Assert.Equal("Target", overlay.Label);

        // Round-trip through JSON serializer/deserializer
        var json = JsonSerializer.Serialize(vm);
        var deserialized = JsonSerializer.Deserialize<VisualManifest>(json);

        Assert.NotNull(deserialized);
        var roundTrippedOverlay = Assert.Single(deserialized.Overlays);
        Assert.Equal("ReferenceLine", roundTrippedOverlay.OverlayType);
        Assert.Equal(75000.0, roundTrippedOverlay.Parameter);
        Assert.Equal("dashed", roundTrippedOverlay.LineStyle);
        Assert.Equal("#dc2626", roundTrippedOverlay.Color);
        Assert.Equal("Target", roundTrippedOverlay.Label);
    }

    [Fact]
    public void DesignerScriptService_ParseGenerateRoundTrip()
    {
        const string sql = """
            CREATE VISUAL SalesTarget AS LINE (
              SOURCE = #data,
              MAPPINGS (
                X = Month,
                Y = Revenue
              ),
              OVERLAYS (
                REFERENCE_LINE (VALUE = 80000, LABEL = 'Goal', STYLE = DASHED, COLOR = '#10b981')
              )
            );
            """;

        var parsingService = new DesignerScriptParsingService();
        var generationService = new DesignerScriptGenerationService();

        var state1 = parsingService.Parse(sql);
        var generatedSql = generationService.Generate(state1);
        var state2 = parsingService.Parse(generatedSql);

        var visual2 = state2.Pages.SelectMany(p => p.Visuals).Single(v => v.Name == "SalesTarget");
        Assert.True(visual2.Options.ContainsKey("overlays"));
        var overlays = visual2.Options["overlays"];
        Assert.Contains("REFERENCE_LINE (VALUE = 80000, LABEL = 'Goal', STYLE = DASHED, COLOR = '#10b981')", overlays);
    }

    [Fact]
    public void SampleScript_ParsesLowersResolvesAndRenders()
    {
        var samplePath = Path.Combine(AppContext.BaseDirectory, "../../../../../samples/08_Reporting/reference_lines_named_charts.rptsql");
        if (!File.Exists(samplePath))
        {
            samplePath = Path.GetFullPath("samples/08_Reporting/reference_lines_named_charts.rptsql");
        }
        Assert.True(File.Exists(samplePath), $"Sample file not found at {samplePath}");

        var sql = File.ReadAllText(samplePath);
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);

        var visuals = script.Statements.OfType<CreateVisualStatement>().ToList();
        Assert.Equal(2, visuals.Count);

        var salesTrendVisual = visuals.Single(v => v.Name == "SalesTrendReferenceLines");
        var quotaVisual = visuals.Single(v => v.Name == "RegionalSalesQuota");

        // 1. Validate SalesTrendReferenceLines (LINE)
        var salesManifest = new VisualManifest
        {
            Name = "SalesTrendReferenceLines",
            Columns = ["Month", "ActualRevenue"],
            Rows = [
                ["2026-01", "42000.0"],
                ["2026-02", "48000.0"],
                ["2026-03", "53000.0"],
                ["2026-04", "59000.0"],
                ["2026-05", "64000.0"],
                ["2026-06", "71000.0"]
            ]
        };

        var lowerer = new NamedVisualChartLowerer(new SystemExecutionContext());
        var salesSpec = lowerer.Lower(salesTrendVisual, salesManifest);
        var salesPlan = new PlotPlanResolver().Resolve(salesSpec, new VisualChartDataBuilder().Build(salesSpec, salesManifest));
        var salesSvg = new SvgChartRenderer().Render(salesPlan);

        Assert.Contains("class='plot-reference-line'", salesSvg);
        Assert.Contains("Midpoint Baseline", salesSvg);
        Assert.Contains("Stretch Target (Outside Range)", salesSvg);
        var salesYScale = salesPlan.Scales.Single(s => s.Channel == FieldChannel.Y);
        var salesMax = PlotPlanResolver.Number(salesYScale.Domain[1])!.Value;
        Assert.True(salesMax >= 85000m, $"Domain max must be >= 85000, was {salesMax}");

        // 2. Validate RegionalSalesQuota (HBAR)
        var quotaManifest = new VisualManifest
        {
            Name = "RegionalSalesQuota",
            Columns = ["Region", "RegionalSales"],
            Rows = [
                ["North", "85000.0"],
                ["South", "62000.0"],
                ["East", "94000.0"],
                ["West", "78000.0"]
            ]
        };

        var quotaSpec = lowerer.Lower(quotaVisual, quotaManifest);
        var quotaPlan = new PlotPlanResolver().Resolve(quotaSpec, new VisualChartDataBuilder().Build(quotaSpec, quotaManifest));
        var quotaSvg = new SvgChartRenderer().Render(quotaPlan);

        Assert.Contains("class='plot-reference-line'", quotaSvg);
        Assert.Contains("Regional Quota", quotaSvg);
        var match = Regex.Match(quotaSvg, @"<line class='plot-reference-line' x1='(?<x1>[0-9.]+)' y1='(?<y1>[0-9.]+)' x2='(?<x2>[0-9.]+)' y2='(?<y2>[0-9.]+)'");
        Assert.True(match.Success, "Expected plot-reference-line match in HBAR SVG");
        Assert.Equal(match.Groups["x1"].Value, match.Groups["x2"].Value);
    }

    private static PlotPlan ResolveNamed(string sql, VisualManifest manifest)
    {
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        foreach (var axis in statement.AxisOptions)
        {
            var prefix = "axis:" + axis.Axis.ToLowerInvariant() + ":";
            foreach (var opt in axis.Options)
                manifest.Options[prefix + opt.Key.ToLowerInvariant()] = opt.Value;
        }
        foreach (var opt in statement.Options)
        {
            manifest.Options[opt.Key.ToUpperInvariant()] = opt.Value;
        }
        var spec = new NamedVisualChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        return new PlotPlanResolver().Resolve(spec, new VisualChartDataBuilder().Build(spec, manifest));
    }
}
