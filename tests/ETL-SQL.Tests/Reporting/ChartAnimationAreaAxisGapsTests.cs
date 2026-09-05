using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Builders;
using ETL_SQL.Reporting.Contracts;
using ETL_SQL.Reporting.Renderers;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Reporting.Semantics.Runtime;
using Xunit;

namespace ETL_SQL.Tests.Reporting;

public sealed class ChartAnimationAreaAxisGapsTests
{
    private static Script Parse(string sql)
    {
        var tokens = new Lexer(sql).Tokenize();
        return new Parser(tokens, sql).Parse();
    }

    private static PlotPlan ResolveNamedPlan(CreateVisualStatement stmt, List<string> columns, List<List<string>> rows)
    {
        var manifest = new VisualManifest
        {
            Name = stmt.Name,
            VisualType = stmt.VisualType.ToString().ToUpperInvariant(),
            Columns = columns,
            Rows = rows,
            Options = stmt.Options.ToDictionary(o => o.Key, o => o.Value, StringComparer.OrdinalIgnoreCase)
        };
        var spec = new NamedVisualChartLowerer().Lower(stmt, manifest);
        var data = new VisualChartDataBuilder().Build(spec, manifest);
        return new PlotPlanResolver().Resolve(spec, data);
    }

    [Fact]
    public void Animation_Options_ParsesSerializesAndRendersSvg()
    {
        const string sql = """
CREATE VISUAL AnimatedBar AS BAR (
    SOURCE = #sales,
    MAPPINGS (X = Region, Y = Total),
    OPTIONS (
        ANIMATION = ON,
        ANIMATION_DURATION = 1200,
        ANIMATION_EASING = BOUNCE,
        UPDATE_ANIMATION = OFF
    )
);
""";
        var script = Parse(sql);
        Assert.Empty(script.Diagnostics);
        var stmt = script.Statements.OfType<CreateVisualStatement>().Single();

        Assert.Equal("True", stmt.Options.First(o => o.Key == "ANIMATION").Value, ignoreCase: true);
        Assert.Equal("1200", stmt.Options.First(o => o.Key == "ANIMATION_DURATION").Value);
        Assert.Equal("BOUNCE", stmt.Options.First(o => o.Key == "ANIMATION_EASING").Value);
        Assert.Equal("False", stmt.Options.First(o => o.Key == "UPDATE_ANIMATION").Value, ignoreCase: true);

        var serialized = stmt.ToSql();
        Assert.Contains("ANIMATION = ON", serialized);
        Assert.Contains("ANIMATION_DURATION = 1200", serialized);
        Assert.Contains("ANIMATION_EASING = BOUNCE", serialized);
        Assert.Contains("UPDATE_ANIMATION = OFF", serialized);

        var plan = ResolveNamedPlan(stmt, ["Region", "Total"], [["North", "100"], ["South", "200"]]);
        var svg = new SvgChartRenderer().Render(plan);

        Assert.Contains("data-animation='on'", svg);
        Assert.Contains("data-animation-duration='1200'", svg);
        Assert.Contains("data-animation-easing='bounce'", svg);
        Assert.Contains("data-update-animation='off'", svg);
    }

    [Fact]
    public void Area_Baseline_ParsesSerializesAndRendersSvg()
    {
        const string sql = """
CREATE VISUAL AreaZero AS LINE (
    SOURCE = #sales,
    MAPPINGS (X = Month, Y = Profit),
    OPTIONS (
        AREA = ON,
        AREA_BASELINE = ZERO
    )
);
""";
        var script = Parse(sql);
        Assert.Empty(script.Diagnostics);
        var stmt = script.Statements.OfType<CreateVisualStatement>().Single();

        Assert.Equal("True", stmt.Options.First(o => o.Key == "AREA").Value, ignoreCase: true);
        Assert.Equal("ZERO", stmt.Options.First(o => o.Key == "AREA_BASELINE").Value);

        var serialized = stmt.ToSql();
        Assert.Contains("AREA_BASELINE = ZERO", serialized);

        var plan = ResolveNamedPlan(stmt, ["Month", "Profit"], [["Jan", "-50"], ["Feb", "150"]]);
        var svg = new SvgChartRenderer().Render(plan);

        // SVG contains an area path fill
        Assert.Contains("fill-opacity='.2'", svg);
    }

    [Fact]
    public void CustomChart_AreaLayer_AreaBaseline_ValidationAndLowering()
    {
        const string validSql = """
CREATE VISUAL AdvArea AS CUSTOM (
    SOURCE = #metrics,
    CHART (
        COORDINATE (TYPE = CARTESIAN),
        SCALES (
            steps = BAND (CHANNEL = X),
            scores = LINEAR (CHANNEL = Y)
        ),
        LAYERS (
            areaLayer = AREA (
                ENCODINGS (
                    X = Step (TYPE = ORDINAL, SCALE = steps),
                    Y = Score (TYPE = QUANTITATIVE, SCALE = scores)
                ),
                AREA_BASELINE = 50
            )
        )
    )
);
""";
        var script = Parse(validSql);
        Assert.Empty(script.Diagnostics);
        var stmt = script.Statements.OfType<CreateVisualStatement>().Single();
        Assert.NotNull(stmt.AdvancedChart);
        var layer = stmt.AdvancedChart.Layers.Single();
        Assert.Equal("50", layer.AreaBaseline);

        var serialized = stmt.ToSql();
        Assert.Contains("AREA_BASELINE = 50", serialized);

        var results = AdvancedChartSemanticValidator.Validate(stmt);
        Assert.DoesNotContain(results, r => r.Severity == DiagnosticSeverity.Error);

        // Invalid baseline on non-area layer
        const string invalidLayerSql = """
CREATE VISUAL InvalidAdv AS CUSTOM (
    SOURCE = #metrics,
    CHART (
        COORDINATE (TYPE = CARTESIAN),
        SCALES (
            steps = BAND (CHANNEL = X),
            scores = LINEAR (CHANNEL = Y)
        ),
        LAYERS (
            lineLayer = LINE (
                ENCODINGS (
                    X = Step (TYPE = ORDINAL, SCALE = steps),
                    Y = Score (TYPE = QUANTITATIVE, SCALE = scores)
                ),
                AREA_BASELINE = 50
            )
        )
    )
);
""";
        var invalidScript = Parse(invalidLayerSql);
        var invalidStmt = invalidScript.Statements.OfType<CreateVisualStatement>().Single();
        var invalidResults = AdvancedChartSemanticValidator.Validate(invalidStmt);
        Assert.Contains(invalidResults, r => r.Message.Contains("AREA_BASELINE"));

        // Invalid non-numeric baseline
        const string invalidValSql = """
CREATE VISUAL InvalidVal AS CUSTOM (
    SOURCE = #metrics,
    CHART (
        COORDINATE (TYPE = CARTESIAN),
        SCALES (
            steps = BAND (CHANNEL = X),
            scores = LINEAR (CHANNEL = Y)
        ),
        LAYERS (
            areaLayer = AREA (
                ENCODINGS (
                    X = Step (TYPE = ORDINAL, SCALE = steps),
                    Y = Score (TYPE = QUANTITATIVE, SCALE = scores)
                ),
                AREA_BASELINE = INVALID_WORD
            )
        )
    )
);
""";
        var invalidValScript = Parse(invalidValSql);
        var invalidValStmt = invalidValScript.Statements.OfType<CreateVisualStatement>().Single();
        var invalidValResults = AdvancedChartSemanticValidator.Validate(invalidValStmt);
        Assert.Contains(invalidValResults, r => r.Message.Contains("AREA_BASELINE must be ZERO or a decimal number"));
    }

    [Fact]
    public void Series_HoverFocus_ParsesSerializesAndRendersSvg()
    {
        const string sql = """
CREATE VISUAL SeriesChart AS BAR (
    SOURCE = #sales,
    MAPPINGS (X = Region, Y = Total, SERIES = Channel),
    OPTIONS (
        HOVER_FOCUS = SERIES
    )
);
""";
        var script = Parse(sql);
        Assert.Empty(script.Diagnostics);
        var stmt = script.Statements.OfType<CreateVisualStatement>().Single();
        Assert.Equal("SERIES", stmt.Options.First(o => o.Key == "HOVER_FOCUS").Value);

        var serialized = stmt.ToSql();
        Assert.Contains("HOVER_FOCUS = SERIES", serialized);

        var plan = ResolveNamedPlan(stmt, ["Region", "Channel", "Total"], [["North", "Online", "100"], ["North", "Retail", "150"]]);
        var svg = new SvgChartRenderer().Render(plan);

        Assert.Contains("data-hover-focus='series'", svg);
        Assert.Contains("data-series=", svg);
    }

    [Fact]
    public void CustomChart_Layer_HoverFocus_ValidationAndLowering()
    {
        const string sql = """
CREATE VISUAL CustomHover AS CUSTOM (
    SOURCE = #data,
    CHART (
        COORDINATE (TYPE = CARTESIAN),
        SCALES (
            depts = BAND (CHANNEL = X),
            costs = LINEAR (CHANNEL = Y)
        ),
        LAYERS (
            bars = RECT (
                ENCODINGS (
                    X = Dept (TYPE = ORDINAL, SCALE = depts),
                    Y = Cost (TYPE = QUANTITATIVE, SCALE = costs)
                ),
                HOVER_FOCUS = SELF
            )
        )
    )
);
""";
        var script = Parse(sql);
        Assert.Empty(script.Diagnostics);
        var stmt = script.Statements.OfType<CreateVisualStatement>().Single();
        Assert.Equal("SELF", stmt.AdvancedChart!.Layers.Single().HoverFocus);

        var serialized = stmt.ToSql();
        Assert.Contains("HOVER_FOCUS = SELF", serialized);

        var results = AdvancedChartSemanticValidator.Validate(stmt);
        Assert.DoesNotContain(results, r => r.Severity == DiagnosticSeverity.Error);

        // Invalid HOVER_FOCUS
        const string invalidSql = """
CREATE VISUAL InvalidHover AS CUSTOM (
    SOURCE = #data,
    CHART (
        COORDINATE (TYPE = CARTESIAN),
        SCALES (
            depts = BAND (CHANNEL = X),
            costs = LINEAR (CHANNEL = Y)
        ),
        LAYERS (
            bars = RECT (
                ENCODINGS (
                    X = Dept (TYPE = ORDINAL, SCALE = depts),
                    Y = Cost (TYPE = QUANTITATIVE, SCALE = costs)
                ),
                HOVER_FOCUS = BAD_MODE
            )
        )
    )
);
""";
        var invalidScript = Parse(invalidSql);
        var invalidStmt = invalidScript.Statements.OfType<CreateVisualStatement>().Single();
        var invalidResults = AdvancedChartSemanticValidator.Validate(invalidStmt);
        Assert.Contains(invalidResults, r => r.Message.Contains("HOVER_FOCUS accepts only NONE, SELF, or SERIES"));
    }

    [Fact]
    public void Axis_TickFormatAndTimeUnit_ParsesSerializesAndResolves()
    {
        const string sql = """
CREATE VISUAL TimeChart AS LINE (
    SOURCE = #timeseries,
    MAPPINGS (X = Ts, Y = Metric),
    OPTIONS (
        X_AXIS (
            TIME_UNIT = MONTH,
            TICK_FORMAT = 'MMM yyyy'
        ),
        Y_AXIS (
            TICK_FORMAT = 'C0'
        )
    )
);
""";
        var script = Parse(sql);
        Assert.Empty(script.Diagnostics);
        var stmt = script.Statements.OfType<CreateVisualStatement>().Single();

        var xAxis = stmt.AxisOptions.FirstOrDefault(a => a.Axis == "X");
        var yAxis = stmt.AxisOptions.FirstOrDefault(a => a.Axis == "Y");
        Assert.NotNull(xAxis);
        Assert.NotNull(yAxis);
        Assert.Equal("MONTH", xAxis.Options.First(o => o.Key == "TIME_UNIT").Value);
        Assert.Equal("MMM yyyy", xAxis.Options.First(o => o.Key == "TICK_FORMAT").Value);
        Assert.Equal("C0", yAxis.Options.First(o => o.Key == "TICK_FORMAT").Value);

        var serialized = stmt.ToSql();
        Assert.Contains("TIME_UNIT = MONTH", serialized);
        Assert.Contains("TICK_FORMAT = 'MMM yyyy'", serialized);
        Assert.Contains("TICK_FORMAT = 'C0'", serialized);

        var plan = ResolveNamedPlan(stmt, ["Ts", "Metric"], [["2026-01-15", "1200"], ["2026-02-15", "1800"]]);
        var yScale = plan.Scales.First(s => s.Channel == FieldChannel.Y);
        Assert.Equal("C0", yScale.TickFormat);

        // Y ticks should be formatted with currency symbol (¤ or $)
        Assert.All(yScale.Ticks, t => Assert.True(!string.IsNullOrEmpty(t.Label) && (t.Label.Contains("¤") || t.Label.Contains("$"))));
    }

    [Fact]
    public void CustomChart_Scales_TimeUnitAndTickFormat_ValidationAndLowering()
    {
        const string sql = """
CREATE VISUAL AdvTime AS CUSTOM (
    SOURCE = #data,
    CHART (
        COORDINATE (TYPE = CARTESIAN),
        SCALES (
            timeScale = BAND (
                CHANNEL = X,
                TIME_UNIT = DAY,
                TICK_FORMAT = 'yyyy-MM-dd'
            ),
            amtScale = LINEAR (CHANNEL = Y)
        ),
        LAYERS (
            lineLayer = LINE (
                ENCODINGS (
                    X = DateCol (TYPE = ORDINAL, SCALE = timeScale),
                    Y = Amt (TYPE = QUANTITATIVE, SCALE = amtScale)
                )
            )
        )
    )
);
""";
        var script = Parse(sql);
        Assert.Empty(script.Diagnostics);
        var stmt = script.Statements.OfType<CreateVisualStatement>().Single();
        var scale = stmt.AdvancedChart!.Scales.Single(s => s.Name == "timeScale");
        Assert.Equal("DAY", scale.TimeUnit);
        Assert.Equal("yyyy-MM-dd", scale.TickFormat);

        var results = AdvancedChartSemanticValidator.Validate(stmt);
        Assert.DoesNotContain(results, r => r.Severity == DiagnosticSeverity.Error);

        // Invalid TIME_UNIT
        const string invalidSql = """
CREATE VISUAL AdvTimeInvalid AS CUSTOM (
    SOURCE = #data,
    CHART (
        COORDINATE (TYPE = CARTESIAN),
        SCALES (
            timeScale = BAND (
                CHANNEL = X,
                TIME_UNIT = CENTURY
            ),
            amtScale = LINEAR (CHANNEL = Y)
        ),
        LAYERS (
            lineLayer = LINE (
                ENCODINGS (
                    X = DateCol (TYPE = ORDINAL, SCALE = timeScale),
                    Y = Amt (TYPE = QUANTITATIVE, SCALE = amtScale)
                )
            )
        )
    )
);
""";
        var invalidScript = Parse(invalidSql);
        var invalidStmt = invalidScript.Statements.OfType<CreateVisualStatement>().Single();
        var invalidResults = AdvancedChartSemanticValidator.Validate(invalidStmt);
        Assert.Contains(invalidResults, r => r.Message.Contains("TIME_UNIT accepts only"));
    }

    [Fact]
    public void BarMinHeight_ParsesSerializesAndEnforcesMinHeightInSvg()
    {
        const string sql = """
CREATE VISUAL TinyBar AS BAR (
    SOURCE = #sales,
    MAPPINGS (X = Region, Y = SmallAmount),
    OPTIONS (
        BAR_MIN_HEIGHT = 15
    )
);
""";
        var script = Parse(sql);
        Assert.Empty(script.Diagnostics);
        var stmt = script.Statements.OfType<CreateVisualStatement>().Single();
        Assert.Equal("15", stmt.Options.First(o => o.Key == "BAR_MIN_HEIGHT").Value);

        var serialized = stmt.ToSql();
        Assert.Contains("BAR_MIN_HEIGHT = 15", serialized);

        var plan = ResolveNamedPlan(stmt, ["Region", "SmallAmount"], [["North", "0.0001"], ["South", "1000"]]);
        var svg = new SvgChartRenderer().Render(plan);

        // The first bar should have clamped height of 15
        Assert.Contains("height='15'", svg);
    }

    [Fact]
    public void BarMinHeight_HBar_EnforcesMinHeightInSvg()
    {
        const string sql = """
CREATE VISUAL TinyHBar AS HBAR (
    SOURCE = #sales,
    MAPPINGS (X = Region, Y = SmallAmount),
    OPTIONS (
        BAR_MIN_HEIGHT = 12
    )
);
""";
        var script = Parse(sql);
        Assert.Empty(script.Diagnostics);
        var stmt = script.Statements.OfType<CreateVisualStatement>().Single();

        var plan = ResolveNamedPlan(stmt, ["Region", "SmallAmount"], [["North", "0.0001"], ["South", "1000"]]);
        var svg = new SvgChartRenderer().Render(plan);

        // In HBAR, the value dimension is width, clamped to 12
        Assert.Contains("width='12'", svg);
    }

    [Fact]
    public void BarMinHeight_Waterfall_EnforcesMinHeightInSvg()
    {
        const string sql = """
CREATE VISUAL TinyWaterfall AS WATERFALL (
    SOURCE = #sales,
    MAPPINGS (X = Step, Y = Delta),
    OPTIONS (
        BAR_MIN_HEIGHT = 10
    )
);
""";
        var script = Parse(sql);
        Assert.Empty(script.Diagnostics);
        var stmt = script.Statements.OfType<CreateVisualStatement>().Single();

        var plan = ResolveNamedPlan(stmt, ["Step", "Delta"], [["Start", "100"], ["SmallDelta", "0.001"]]);
        var svg = new SvgChartRenderer().Render(plan);

        // SmallDelta rect clamped to height 10
        Assert.Contains("height='10'", svg);
    }

    [Fact]
    public void SegmentStyle_Line_ParsesSerializesAndRendersSvg()
    {
        const string sql = """
CREATE VISUAL TrendForecast AS LINE (
    SOURCE = #sales,
    MAPPINGS (X = Month, Y = Revenue),
    OPTIONS (
        SEGMENT_STYLE (
            WHEN is_forecast = 1 THEN LINE_DASH = DASHED, COLOR = '#64748b'
        )
    )
);
""";
        var script = Parse(sql);
        Assert.Empty(script.Diagnostics);
        var stmt = script.Statements.OfType<CreateVisualStatement>().Single();

        Assert.Single(stmt.SegmentStyles);
        var rule = stmt.SegmentStyles[0];
        Assert.Equal("DASHED", rule.LineDash, ignoreCase: true);
        Assert.Equal("#64748b", rule.Color, ignoreCase: true);

        var formatted = stmt.ToSql();
        Assert.Contains("SEGMENT_STYLE", formatted);
        Assert.Contains("LINE_DASH = DASHED", formatted);
        Assert.Contains("COLOR = '#64748b'", formatted);

        var reParsed = Parse(formatted);
        Assert.Empty(reParsed.Diagnostics);
        var reStmt = reParsed.Statements.OfType<CreateVisualStatement>().Single();
        Assert.Single(reStmt.SegmentStyles);
        Assert.Equal("DASHED", reStmt.SegmentStyles[0].LineDash, ignoreCase: true);

        var plan = ResolveNamedPlan(stmt, ["Month", "Revenue", "is_forecast"], [
            ["Jan", "100", "0"],
            ["Feb", "120", "0"],
            ["Mar", "130", "1"],
            ["Apr", "150", "1"]
        ]);

        var segmentStyles = new List<SegmentStyleManifest?>
        {
            null,
            null,
            new() { LineDash = "DASHED", Color = "#64748b" },
            new() { LineDash = "DASHED", Color = "#64748b" }
        };

        var styledPlan = VisualBuilder.ApplySegmentStyles(plan, segmentStyles);
        var svg = new SvgChartRenderer().Render(styledPlan);

        // Path with dashed line and overridden color
        Assert.Contains("stroke-dasharray='7 5'", svg);
        Assert.Contains("stroke='#64748b'", svg);
    }

    [Fact]
    public void SegmentStyle_Validation_RejectsUnsupportedVisual()
    {
        const string sql = """
CREATE VISUAL BarWithSegment AS BAR (
    SOURCE = #sales,
    MAPPINGS (X = Category, Y = Total),
    OPTIONS (
        SEGMENT_STYLE (
            WHEN Total > 100 THEN LINE_DASH = DASHED
        )
    )
);
""";
        var script = Parse(sql);
        Assert.Empty(script.Diagnostics);
        var stmt = script.Statements.OfType<CreateVisualStatement>().Single();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ResolveNamedPlan(stmt, ["Category", "Total"], [["A", "10"], ["B", "200"]]));
        Assert.Contains("SEGMENT_STYLE is supported only on LINE and COMBO visuals", ex.Message);
    }

    [Fact]
    public void SegmentStyle_Combo_ParsesAndAppliesToLineSeries()
    {
        const string sql = """
CREATE VISUAL ComboForecast AS COMBO (
    SOURCE = #sales,
    MAPPINGS (X = Month, Y = Revenue, Y2 = Margin),
    OPTIONS (
        SEGMENT_STYLE (
            WHEN Margin < 0 THEN LINE_DASH = DOTTED, COLOR = '#ef4444'
        )
    )
);
""";
        var script = Parse(sql);
        Assert.Empty(script.Diagnostics);
        var stmt = script.Statements.OfType<CreateVisualStatement>().Single();

        Assert.Single(stmt.SegmentStyles);
        Assert.Equal("DOTTED", stmt.SegmentStyles[0].LineDash, ignoreCase: true);
        Assert.Equal("#ef4444", stmt.SegmentStyles[0].Color, ignoreCase: true);

        var plan = ResolveNamedPlan(stmt, ["Month", "Revenue", "Margin"], [
            ["Jan", "100", "15"],
            ["Feb", "120", "-5"],
            ["Mar", "130", "-10"]
        ]);

        var segmentStyles = new List<SegmentStyleManifest?>
        {
            null,
            new() { LineDash = "DOTTED", Color = "#ef4444" },
            new() { LineDash = "DOTTED", Color = "#ef4444" }
        };

        var styledPlan = VisualBuilder.ApplySegmentStyles(plan, segmentStyles);
        var svg = new SvgChartRenderer().Render(styledPlan);

        Assert.Contains("stroke-dasharray='1 5'", svg);
        Assert.Contains("stroke='#ef4444'", svg);
    }
}
