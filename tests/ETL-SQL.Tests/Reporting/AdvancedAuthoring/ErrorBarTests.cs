using System;
using System.Collections.Immutable;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Authoring;
using ETL_SQL.Reporting.Renderers;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Reporting.Semantics.Runtime;
using Moq;
using Spectre.Console;
using Xunit;

namespace ETL_SQL.Tests.Reporting.AdvancedAuthoring;

public sealed class ErrorBarTests
{
    [Fact]
    public void CustomPoint_ParsesAndRoundTripsErrorChannels()
    {
        const string sql = """
            CREATE VISUAL Experiment AS CUSTOM (
              SOURCE = #experiment,
              CHART (
                COORDINATE (TYPE = CARTESIAN),
                LAYERS (
                  points = POINT (
                    STYLE (ERROR_BAR_STYLE = 'CAPS'),
                    ENCODINGS (
                      X = Trial (TYPE = NOMINAL),
                      Y = Estimate (TYPE = QUANTITATIVE),
                      ERROR_LOW = LowerBound (TYPE = QUANTITATIVE),
                      ERROR_HIGH = UpperBound (TYPE = QUANTITATIVE)
                    )
                  )
                )
              )
            );
            """;

        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        var layer = Assert.Single(statement.AdvancedChart!.Layers);
        Assert.Contains(layer.Encodings, e => e.Channel == AdvancedChartChannel.ErrorLow);
        Assert.Contains(layer.Encodings, e => e.Channel == AdvancedChartChannel.ErrorHigh);

        var serialized = statement.ToSql();
        Assert.Contains("ERROR_LOW = LowerBound ( TYPE = QUANTITATIVE )", serialized);
        Assert.Contains("ERROR_HIGH = UpperBound ( TYPE = QUANTITATIVE )", serialized);

        var roundTrip = new Parser(new Lexer(serialized).Tokenize(), serialized).Parse();
        Assert.Empty(roundTrip.Diagnostics);
        var roundLayer = Assert.Single(Assert.Single(roundTrip.Statements.OfType<CreateVisualStatement>()).AdvancedChart!.Layers);
        Assert.Contains(roundLayer.Encodings, e => e.Channel == AdvancedChartChannel.ErrorLow);
        Assert.Contains(roundLayer.Encodings, e => e.Channel == AdvancedChartChannel.ErrorHigh);
    }

    [Fact]
    public void CustomRect_ParsesAndRoundTripsErrorChannels()
    {
        const string sql = """
            CREATE VISUAL BarsWithError AS CUSTOM (
              SOURCE = #bars,
              CHART (
                COORDINATE (TYPE = CARTESIAN),
                LAYERS (
                  bars = RECT (
                    STYLE (ERROR_BAR_STYLE = 'NO_CAPS'),
                    ENCODINGS (
                      X = Category (TYPE = NOMINAL),
                      Y = MeanValue (TYPE = QUANTITATIVE),
                      ERROR_LOW = MinBound (TYPE = QUANTITATIVE),
                      ERROR_HIGH = MaxBound (TYPE = QUANTITATIVE)
                    )
                  )
                )
              )
            );
            """;

        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        var layer = Assert.Single(statement.AdvancedChart!.Layers);
        Assert.Contains(layer.Encodings, e => e.Channel == AdvancedChartChannel.ErrorLow);
        Assert.Contains(layer.Encodings, e => e.Channel == AdvancedChartChannel.ErrorHigh);

        var serialized = statement.ToSql();
        Assert.Contains("ERROR_LOW = MinBound ( TYPE = QUANTITATIVE )", serialized);
        Assert.Contains("ERROR_HIGH = MaxBound ( TYPE = QUANTITATIVE )", serialized);

        var roundTrip = new Parser(new Lexer(serialized).Tokenize(), serialized).Parse();
        Assert.Empty(roundTrip.Diagnostics);
        var roundLayer = Assert.Single(Assert.Single(roundTrip.Statements.OfType<CreateVisualStatement>()).AdvancedChart!.Layers);
        Assert.Contains(roundLayer.Encodings, e => e.Channel == AdvancedChartChannel.ErrorLow);
        Assert.Contains(roundLayer.Encodings, e => e.Channel == AdvancedChartChannel.ErrorHigh);
    }

    [Fact]
    public void AdvancedEnumBridge_ParityIncludesErrorChannels()
    {
        Assert.Equal(FieldChannel.ErrorLow, AdvancedChartEnumBridge.Channel(AdvancedChartChannel.ErrorLow));
        Assert.Equal(FieldChannel.ErrorHigh, AdvancedChartEnumBridge.Channel(AdvancedChartChannel.ErrorHigh));
        Assert.Equal(AdvancedChartChannel.ErrorLow, AdvancedChartEnumBridge.Channel(FieldChannel.ErrorLow));
        Assert.Equal(AdvancedChartChannel.ErrorHigh, AdvancedChartEnumBridge.Channel(FieldChannel.ErrorHigh));
    }

    [Fact]
    public void SemanticValidator_RejectsMissingEndpoint()
    {
        const string sql = """
            CREATE VISUAL Incomplete AS CUSTOM (
              SOURCE = #test,
              CHART (
                COORDINATE (TYPE = CARTESIAN),
                LAYERS (
                  points = POINT (
                    ENCODINGS (
                      X = Trial (TYPE = NOMINAL),
                      Y = Estimate (TYPE = QUANTITATIVE),
                      ERROR_LOW = LowerBound (TYPE = QUANTITATIVE)
                    )
                  )
                )
              )
            );
            """;

        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        var diagnostics = AdvancedChartSemanticValidator.Validate(statement);
        Assert.Contains(diagnostics, d => d.Code == "RPT-CHART" && d.Message.Contains("requires both ERROR_LOW and ERROR_HIGH as a pair"));
    }

    [Fact]
    public void SemanticValidator_RejectsNonQuantitativeEndpoint()
    {
        const string sql = """
            CREATE VISUAL NonQuantitative AS CUSTOM (
              SOURCE = #test,
              CHART (
                COORDINATE (TYPE = CARTESIAN),
                LAYERS (
                  points = POINT (
                    ENCODINGS (
                      X = Trial (TYPE = NOMINAL),
                      Y = Estimate (TYPE = QUANTITATIVE),
                      ERROR_LOW = LowerBound (TYPE = NOMINAL),
                      ERROR_HIGH = UpperBound (TYPE = QUANTITATIVE)
                    )
                  )
                )
              )
            );
            """;

        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        var diagnostics = AdvancedChartSemanticValidator.Validate(statement);
        Assert.Contains(diagnostics, d => d.Code == "RPT-CHART" && d.Message.Contains("channel ERROR_LOW requires QUANTITATIVE TYPE"));
    }

    [Fact]
    public void SemanticValidator_RejectsUnsupportedMarks()
    {
        const string sql = """
            CREATE VISUAL LineError AS CUSTOM (
              SOURCE = #test,
              CHART (
                COORDINATE (TYPE = CARTESIAN),
                LAYERS (
                  trend = LINE (
                    ENCODINGS (
                      X = Trial (TYPE = NOMINAL),
                      Y = Estimate (TYPE = QUANTITATIVE),
                      ERROR_LOW = LowerBound (TYPE = QUANTITATIVE),
                      ERROR_HIGH = UpperBound (TYPE = QUANTITATIVE)
                    )
                  )
                )
              )
            );
            """;

        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        var diagnostics = AdvancedChartSemanticValidator.Validate(statement);
        Assert.Contains(diagnostics, d => d.Code == "RPT-CHART" && d.Message.Contains("only POINT and RECT marks support error bars"));
    }

    [Fact]
    public void SemanticValidator_RejectsInvalidErrorBarStyle()
    {
        const string sql = """
            CREATE VISUAL BadStyle AS CUSTOM (
              SOURCE = #test,
              CHART (
                COORDINATE (TYPE = CARTESIAN),
                LAYERS (
                  points = POINT (
                    STYLE (ERROR_BAR_STYLE = 'WHISKERS'),
                    ENCODINGS (
                      X = Trial (TYPE = NOMINAL),
                      Y = Estimate (TYPE = QUANTITATIVE),
                      ERROR_LOW = LowerBound (TYPE = QUANTITATIVE),
                      ERROR_HIGH = UpperBound (TYPE = QUANTITATIVE)
                    )
                  )
                )
              )
            );
            """;

        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        var diagnostics = AdvancedChartSemanticValidator.Validate(statement);
        Assert.Contains(diagnostics, d => d.Code == "RPT-CHART" && d.Message.Contains("ERROR_BAR_STYLE accepts only CAPS or NO_CAPS; found 'WHISKERS'"));
    }

    [Fact]
    public void NamedScatter_AcceptsBothMappingsAndDefaultsToCaps()
    {
        const string sql = """
            CREATE VISUAL Scat AS SCATTER (
              SOURCE = #data,
              MAPPINGS (
                X = Estimate,
                Y = Actual,
                ERROR_LOW = LowerBound,
                ERROR_HIGH = UpperBound
              )
            );
            """;

        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        var manifest = new VisualManifest
        {
            Name = "Scat",
            Columns = ["Estimate", "Actual", "LowerBound", "UpperBound"],
            Rows = [["10", "20", "15", "25"]]
        };

        var lowerer = new NamedVisualChartLowerer(new SystemExecutionContext());
        var spec = lowerer.Lower(statement, manifest);

        Assert.Contains(spec.Bindings, b => b.Channel == FieldChannel.ErrorLow);
        Assert.Contains(spec.Bindings, b => b.Channel == FieldChannel.ErrorHigh);
        var layer = Assert.Single(spec.Layers);
        var style = layer.Style.FirstOrDefault(s => s.Name == "errorBarStyle");
        Assert.NotNull(style);
        Assert.Equal("CAPS", style.Value);
    }

    [Fact]
    public void NamedScatter_RejectsSingleMapping()
    {
        const string sql = """
            CREATE VISUAL ScatSingle AS SCATTER (
              SOURCE = #data,
              MAPPINGS (
                X = Estimate,
                Y = Actual,
                ERROR_LOW = LowerBound
              )
            );
            """;

        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        var manifest = new VisualManifest
        {
            Name = "ScatSingle",
            Columns = ["Estimate", "Actual", "LowerBound"],
            Rows = [["10", "20", "15"]]
        };

        var lowerer = new NamedVisualChartLowerer(new SystemExecutionContext());
        var ex = Assert.Throws<InvalidOperationException>(() => lowerer.Lower(statement, manifest));
        Assert.Contains("requires both ERROR_LOW and ERROR_HIGH mappings as a pair", ex.Message);
    }

    [Fact]
    public void NamedScatter_RejectsInvalidErrorBarStyleOption()
    {
        const string sql = """
            CREATE VISUAL ScatInvalidStyle AS SCATTER (
              SOURCE = #data,
              MAPPINGS (
                X = Estimate,
                Y = Actual,
                ERROR_LOW = LowerBound,
                ERROR_HIGH = UpperBound
              ),
              OPTIONS (
                ERROR_BAR_STYLE = FANCY
              )
            );
            """;

        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        var manifest = new VisualManifest
        {
            Name = "ScatInvalidStyle",
            Columns = ["Estimate", "Actual", "LowerBound", "UpperBound"],
            Rows = [["10", "20", "15", "25"]]
        };

        var lowerer = new NamedVisualChartLowerer(new SystemExecutionContext());
        var ex = Assert.Throws<InvalidOperationException>(() => lowerer.Lower(statement, manifest));
        Assert.Contains("Invalid ERROR_BAR_STYLE 'FANCY'. Valid values are CAPS or NO_CAPS", ex.Message);
    }

    [Fact]
    public void ErrorBars_LowHighValuesExpandResolvedYDomain()
    {
        const string sql = """
            CREATE VISUAL DomainCheck AS CUSTOM (
              SOURCE = #data,
              CHART (
                COORDINATE (TYPE = CARTESIAN),
                LAYERS (
                  points = POINT (
                    ENCODINGS (
                      X = Trial (TYPE = NOMINAL),
                      Y = Estimate (TYPE = QUANTITATIVE),
                      ERROR_LOW = Low (TYPE = QUANTITATIVE),
                      ERROR_HIGH = High (TYPE = QUANTITATIVE)
                    )
                  )
                )
              )
            );
            """;

        var manifest = new VisualManifest
        {
            Name = "DomainCheck",
            Columns = ["Trial", "Estimate", "Low", "High"],
            Rows = [
                ["A", "50", "10", "65"],
                ["B", "70", "55", "98"]
            ]
        };

        var plan = ResolveCustom(sql, manifest);
        var yScale = plan.Scales.Single(s => s.Channel == FieldChannel.Y);
        var domain = yScale.Domain.Select(PlotPlanResolver.Number).ToList();

        // Estimate is 50..70, but Low/High range is 10..98.
        Assert.True(domain[0] <= 10m);
        Assert.True(domain[^1] >= 98m);
    }

    [Fact]
    public void SvgRendering_CapsAndNoCapsProduceDistinguishableSvg()
    {
        var manifest = new VisualManifest
        {
            Name = "Test",
            Columns = ["Trial", "Val", "Low", "High"],
            Rows = [["T1", "50", "30", "70"]]
        };

        const string sqlCaps = """
            CREATE VISUAL CapsVisual AS CUSTOM (
              SOURCE = #data,
              CHART (
                COORDINATE (TYPE = CARTESIAN),
                LAYERS (
                  p = POINT (
                    STYLE (ERROR_BAR_STYLE = 'CAPS'),
                    ENCODINGS (
                      X = Trial (TYPE = NOMINAL),
                      Y = Val (TYPE = QUANTITATIVE),
                      ERROR_LOW = Low (TYPE = QUANTITATIVE),
                      ERROR_HIGH = High (TYPE = QUANTITATIVE)
                    )
                  )
                )
              )
            );
            """;

        const string sqlNoCaps = """
            CREATE VISUAL NoCapsVisual AS CUSTOM (
              SOURCE = #data,
              CHART (
                COORDINATE (TYPE = CARTESIAN),
                LAYERS (
                  p = POINT (
                    STYLE (ERROR_BAR_STYLE = 'NO_CAPS'),
                    ENCODINGS (
                      X = Trial (TYPE = NOMINAL),
                      Y = Val (TYPE = QUANTITATIVE),
                      ERROR_LOW = Low (TYPE = QUANTITATIVE),
                      ERROR_HIGH = High (TYPE = QUANTITATIVE)
                    )
                  )
                )
              )
            );
            """;

        var planCaps = ResolveCustom(sqlCaps, manifest);
        var planNoCaps = ResolveCustom(sqlNoCaps, manifest);

        var svgCaps = new SvgChartRenderer().Render(planCaps);
        var svgNoCaps = new SvgChartRenderer().Render(planNoCaps);

        // Both contain the error bar stem
        Assert.Contains("class='plot-error-bar-stem'", svgCaps);
        Assert.Contains("class='plot-error-bar-stem'", svgNoCaps);

        // CAPS contains horizontal caps
        Assert.Contains("class='plot-error-bar-cap'", svgCaps);

        // NO_CAPS does NOT contain caps
        Assert.DoesNotContain("class='plot-error-bar-cap'", svgNoCaps);
    }

    [Fact]
    public void SvgRendering_CustomRectRendersStemAndCapsCenteredOnBar()
    {
        const string sql = """
            CREATE VISUAL BarError AS CUSTOM (
              SOURCE = #data,
              CHART (
                COORDINATE (TYPE = CARTESIAN),
                LAYERS (
                  bars = RECT (
                    ENCODINGS (
                      X = Category (TYPE = NOMINAL),
                      Y = Actual (TYPE = QUANTITATIVE),
                      ERROR_LOW = LowBound (TYPE = QUANTITATIVE),
                      ERROR_HIGH = HighBound (TYPE = QUANTITATIVE)
                    )
                  )
                )
              )
            );
            """;

        var manifest = new VisualManifest
        {
            Name = "BarError",
            Columns = ["Category", "Actual", "LowBound", "HighBound"],
            Rows = [["A", "50", "40", "65"]]
        };

        var plan = ResolveCustom(sql, manifest);
        var svg = new SvgChartRenderer().Render(plan);

        Assert.Contains("class='plot-error-bar-stem'", svg);
        Assert.Contains("class='plot-error-bar-cap'", svg);

        // Check paint order: <rect ...> must appear BEFORE <g class='plot-error-bar'
        var rectPos = svg.IndexOf("<rect");
        var errorBarPos = svg.IndexOf("class='plot-error-bar'");
        Assert.True(rectPos >= 0 && errorBarPos > rectPos, "Error bar should paint after rect so it remains visible");
    }

    [Fact]
    public void SvgRendering_TransposedCartesianRendersHorizontalStem()
    {
        const string sql = """
            CREATE VISUAL TransposedError AS CUSTOM (
              SOURCE = #data,
              CHART (
                COORDINATE (TYPE = TRANSPOSED_CARTESIAN),
                LAYERS (
                  bars = RECT (
                    ENCODINGS (
                      X = Category (TYPE = NOMINAL),
                      Y = Actual (TYPE = QUANTITATIVE),
                      ERROR_LOW = LowBound (TYPE = QUANTITATIVE),
                      ERROR_HIGH = HighBound (TYPE = QUANTITATIVE)
                    )
                  )
                )
              )
            );
            """;

        var manifest = new VisualManifest
        {
            Name = "TransposedError",
            Columns = ["Category", "Actual", "LowBound", "HighBound"],
            Rows = [["A", "50", "40", "65"]]
        };

        var plan = ResolveCustom(sql, manifest);
        var svg = new SvgChartRenderer().Render(plan);

        Assert.Contains("class='plot-error-bar-stem'", svg);
        Assert.Contains("class='plot-error-bar-cap'", svg);
    }

    [Fact]
    public void FallbackAndTerminal_PreservesErrorInterval()
    {
        const string sql = """
            CREATE VISUAL IntervalVisual AS CUSTOM (
              SOURCE = #data,
              CHART (
                COORDINATE (TYPE = CARTESIAN),
                LAYERS (
                  points = POINT (
                    ENCODINGS (
                      X = Label (TYPE = NOMINAL),
                      Y = Val (TYPE = QUANTITATIVE),
                      ERROR_LOW = Low (TYPE = QUANTITATIVE),
                      ERROR_HIGH = High (TYPE = QUANTITATIVE)
                    )
                  )
                )
              )
            );
            """;

        var manifest = new VisualManifest
        {
            Name = "IntervalVisual",
            Columns = ["Label", "Val", "Low", "High"],
            Rows = [["Sample1", "50", "45", "55"]]
        };

        var plan = ResolveCustom(sql, manifest);

        // Semantic fallback includes error interval
        var fallback = plan.Fallback;
        Assert.NotEmpty(fallback.Items);
        var item = fallback.Items.First();
        Assert.Contains("error 45 to 55", item.Detail);

        // Terminal renderer includes interval in text
        var terminal = PlotPlanTerminalRenderer.Render(plan, 80);
        var writer = new System.IO.StringWriter();
        var testConsole = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer)
        });
        testConsole.Write(terminal);
        var text = writer.ToString();
        Assert.Contains("error 45 to 55", text);
    }

    [Fact]
    public void DesignerScriptParsing_PreservesErrorMappingsAndOptions()
    {
        const string sql = """
            CREATE VISUAL ScatDesigner AS SCATTER (
              SOURCE = #data,
              MAPPINGS (
                X = Estimate,
                Y = Actual,
                ERROR_LOW = LowerBound,
                ERROR_HIGH = UpperBound
              ),
              OPTIONS (
                ERROR_BAR_STYLE = NO_CAPS
              )
            );
            """;

        var service = new DesignerScriptParsingService();
        var state = service.Parse(sql);
        var visual = state.Pages.SelectMany(p => p.Visuals).Single(v => v.Name == "ScatDesigner");

        Assert.Equal("LowerBound", visual.Mappings["ERROR_LOW"]);
        Assert.Equal("UpperBound", visual.Mappings["ERROR_HIGH"]);
        Assert.Equal("NO_CAPS", visual.Options["ERROR_BAR_STYLE"]);
    }

    private static PlotPlan ResolveCustom(string sql, VisualManifest manifest)
    {
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        return new PlotPlanResolver().Resolve(spec, new VisualChartDataBuilder().Build(spec, manifest));
    }

    [Fact]
    public void SemanticValidator_RejectsY2WithoutY()
    {
        const string sql = """
            CREATE VISUAL Y2Only AS CUSTOM (
              SOURCE = #test,
              CHART (
                COORDINATE (TYPE = CARTESIAN),
                LAYERS (
                  points = POINT (
                    ENCODINGS (
                      X = Trial (TYPE = NOMINAL),
                      Y2 = Estimate (TYPE = QUANTITATIVE, AXIS = SECONDARY),
                      ERROR_LOW = LowerBound (TYPE = QUANTITATIVE),
                      ERROR_HIGH = UpperBound (TYPE = QUANTITATIVE)
                    )
                  )
                )
              )
            );
            """;

        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        var diagnostics = AdvancedChartSemanticValidator.Validate(statement);
        Assert.Contains(diagnostics, d => d.Code == "RPT-CHART" && d.Message.Contains("requires a quantitative Y encoding"));
    }

    [Fact]
    public void SemanticValidator_RejectsMismatchedScales()
    {
        const string sql = """
            CREATE VISUAL MismatchedScales AS CUSTOM (
              SOURCE = #test,
              CHART (
                COORDINATE (TYPE = CARTESIAN),
                SCALES (
                  scale_y = LINEAR (CHANNEL = Y),
                  scale_err = LINEAR (CHANNEL = Y)
                ),
                LAYERS (
                  points = POINT (
                    ENCODINGS (
                      X = Trial (TYPE = NOMINAL),
                      Y = Estimate (TYPE = QUANTITATIVE, SCALE = scale_y),
                      ERROR_LOW = LowerBound (TYPE = QUANTITATIVE, SCALE = scale_err),
                      ERROR_HIGH = UpperBound (TYPE = QUANTITATIVE, SCALE = scale_err)
                    )
                  )
                )
              )
            );
            """;

        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        var diagnostics = AdvancedChartSemanticValidator.Validate(statement);
        Assert.Contains(diagnostics, d => d.Code == "RPT-CHART" && d.Message.Contains("ERROR_LOW must resolve to the same scale as Y ('scale_y'); found 'scale_err'"));
        Assert.Contains(diagnostics, d => d.Code == "RPT-CHART" && d.Message.Contains("ERROR_HIGH must resolve to the same scale as Y ('scale_y'); found 'scale_err'"));
    }

    private static ChartSpec CreateTestSpec(MarkLayerSpec layer, ImmutableArray<ScaleSpec> scales) => ChartSpec.Create(
        "test", "#data", layer.Bindings,
        [layer],
        new CoordinateSpec(CoordinateKind.Cartesian),
        scales,
        new FormattingSpec("en-US", "UTC", "", []),
        new NullHandlingSpec(NullValuePolicy.Gap, []),
        new ThemeSpec("default", []),
        new AccessibilitySpec("Test", null, null, true));

    [Fact]
    public void ChartSpec_Validate_RejectsLackingPrimaryY()
    {
        var layer = new MarkLayerSpec("points", MarkKind.Point, 0,
            [
                new FieldBinding(FieldChannel.X, "Trial", DataSemanticKind.Nominal),
                new FieldBinding(FieldChannel.Y2, "Estimate", DataSemanticKind.Quantitative, Axis: AxisRole.Secondary),
                new FieldBinding(FieldChannel.ErrorLow, "LowerBound", DataSemanticKind.Quantitative),
                new FieldBinding(FieldChannel.ErrorHigh, "UpperBound", DataSemanticKind.Quantitative)
            ],
            []);

        var spec = CreateTestSpec(layer, [new ScaleSpec("y", FieldChannel.Y, ScaleKind.Linear, false, [])]);

        var ex = Assert.Throws<System.IO.InvalidDataException>(() => spec.Validate());
        Assert.Contains("requires a quantitative primary Y binding", ex.Message);
    }

    [Fact]
    public void ChartSpec_Validate_RejectsDifferentScales()
    {
        var layer = new MarkLayerSpec("points", MarkKind.Point, 0,
            [
                new FieldBinding(FieldChannel.X, "Trial", DataSemanticKind.Nominal),
                new FieldBinding(FieldChannel.Y, "Estimate", DataSemanticKind.Quantitative, ScaleId: "scale_y"),
                new FieldBinding(FieldChannel.ErrorLow, "LowerBound", DataSemanticKind.Quantitative, ScaleId: "scale_err"),
                new FieldBinding(FieldChannel.ErrorHigh, "UpperBound", DataSemanticKind.Quantitative, ScaleId: "scale_err")
            ],
            []);

        var spec = CreateTestSpec(layer,
            [
                new ScaleSpec("scale_y", FieldChannel.Y, ScaleKind.Linear, false, []),
                new ScaleSpec("scale_err", FieldChannel.Y, ScaleKind.Linear, false, [])
            ]);

        var ex = Assert.Throws<System.IO.InvalidDataException>(() => spec.Validate());
        Assert.Contains("ERROR_LOW, ERROR_HIGH, and Y must resolve to the same scale ID", ex.Message);
    }

    [Fact]
    public void ErrorBars_YRemainsInResolvedDomainWhenOutsideInterval()
    {
        const string sql = """
            CREATE VISUAL MalformedInterval AS CUSTOM (
              SOURCE = #data,
              CHART (
                COORDINATE (TYPE = CARTESIAN),
                LAYERS (
                  points = POINT (
                    ENCODINGS (
                      X = Trial (TYPE = NOMINAL),
                      Y = Estimate (TYPE = QUANTITATIVE),
                      ERROR_LOW = Low (TYPE = QUANTITATIVE),
                      ERROR_HIGH = High (TYPE = QUANTITATIVE)
                    )
                  )
                )
              )
            );
            """;

        var manifest = new VisualManifest
        {
            Name = "MalformedInterval",
            Columns = ["Trial", "Estimate", "Low", "High"],
            Rows = [
                ["A", "120", "10", "60"]
            ]
        };

        var plan = ResolveCustom(sql, manifest);
        var yScale = plan.Scales.Single(s => s.Channel == FieldChannel.Y);
        var domain = yScale.Domain.Select(PlotPlanResolver.Number).ToList();

        // Domain must contain both the error endpoints (10) AND Y itself (120)
        Assert.True(domain[0] <= 10m);
        Assert.True(domain[^1] >= 120m);
    }

    private static IExecutionContext CreateContextWithVariable(string name, string value)
    {
        var varContext = new Mock<IVariableContext>();
        varContext.Setup(v => v.ContainsVariable(It.IsAny<string>())).Returns(true);
        varContext.Setup(v => v.GetVariable(It.IsAny<string>())).Returns(value);
        varContext.Setup(v => v.GetVariablesWithMetadata()).Returns(new System.Collections.Generic.Dictionary<string, (object? Value, VariableMetadata Metadata)>());

        var context = new Mock<IExecutionContext>();
        context.Setup(c => c.VarContext).Returns(varContext.Object);
        return context.Object;
    }

    [Fact]
    public void ParameterizedStyle_NoCaps_SucceedsAndRendersWithoutCaps()
    {
        const string sql = """
            CREATE VISUAL ParamStyleVisual AS CUSTOM (
              SOURCE = #data,
              CHART (
                COORDINATE (TYPE = CARTESIAN),
                LAYERS (
                  p = POINT (
                    STYLE (ERROR_BAR_STYLE = @style),
                    ENCODINGS (
                      X = Trial (TYPE = NOMINAL),
                      Y = Val (TYPE = QUANTITATIVE),
                      ERROR_LOW = Low (TYPE = QUANTITATIVE),
                      ERROR_HIGH = High (TYPE = QUANTITATIVE)
                    )
                  )
                )
              )
            );
            """;

        var manifest = new VisualManifest
        {
            Name = "ParamStyleVisual",
            Columns = ["Trial", "Val", "Low", "High"],
            Rows = [["T1", "50", "30", "70"]]
        };

        var context = CreateContextWithVariable("style", "NO_CAPS");

        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());

        var spec = new AdvancedChartLowerer(context).Lower(statement, manifest);
        var plan = new PlotPlanResolver().Resolve(spec, new VisualChartDataBuilder().Build(spec, manifest));
        var svg = new SvgChartRenderer().Render(plan);

        Assert.Contains("class='plot-error-bar-stem'", svg);
        Assert.DoesNotContain("class='plot-error-bar-cap'", svg);
    }

    [Fact]
    public void ParameterizedStyle_Invalid_FailsWithPositionedDiagnostic()
    {
        const string sql = """
            CREATE VISUAL ParamStyleBad AS CUSTOM (
              SOURCE = #data,
              CHART (
                COORDINATE (TYPE = CARTESIAN),
                LAYERS (
                  p = POINT (
                    STYLE (ERROR_BAR_STYLE = @style),
                    ENCODINGS (
                      X = Trial (TYPE = NOMINAL),
                      Y = Val (TYPE = QUANTITATIVE),
                      ERROR_LOW = Low (TYPE = QUANTITATIVE),
                      ERROR_HIGH = High (TYPE = QUANTITATIVE)
                    )
                  )
                )
              )
            );
            """;

        var manifest = new VisualManifest
        {
            Name = "ParamStyleBad",
            Columns = ["Trial", "Val", "Low", "High"],
            Rows = [["T1", "50", "30", "70"]]
        };

        var context = CreateContextWithVariable("style", "WHISKERS");

        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());

        var ex = Assert.Throws<AdvancedChartSemanticException>(() => new AdvancedChartLowerer(context).Lower(statement, manifest));
        Assert.Single(ex.Diagnostics);
        var diag = ex.Diagnostics[0];
        Assert.Equal("RPT-CHART", diag.Code);
        Assert.True(diag.Line > 0);
        Assert.Contains("ERROR_BAR_STYLE accepts only CAPS or NO_CAPS; found 'WHISKERS'", diag.Message);
    }

}
