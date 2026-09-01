using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Authoring;
using ETL_SQL.Reporting.Contracts;
using ETL_SQL.Reporting.Renderers;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Reporting.Semantics.Runtime;
using Spectre.Console;
using Xunit;

namespace ETL_SQL.Tests.Reporting.AdvancedAuthoring;

public sealed class ForecastOverlayTests
{
    [Fact]
    public void NamedLine_ParsesAndRoundTripsForecastOverlay()
    {
        const string sql = """
            CREATE VISUAL SalesForecast AS LINE (
              SOURCE = #sales,
              MAPPINGS (
                X = Month,
                Y = Revenue
              ),
              OVERLAYS (
                FORECAST(ForecastRev) AS DASHED WITH (
                  CONFIDENCE_LOW = LowBound,
                  CONFIDENCE_HIGH = HighBound,
                  ANOMALY = AnomalyVal,
                  COLOR = '#2563eb',
                  LABEL = 'Projected Revenue'
                )
              )
            );
            """;

        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        var overlay = Assert.Single(statement.Overlays);
        Assert.Equal(OverlayType.Forecast, overlay.OverlayType);
        Assert.Equal("ForecastRev", overlay.ForecastField);
        Assert.Equal("LowBound", overlay.ConfidenceLowField);
        Assert.Equal("HighBound", overlay.ConfidenceHighField);
        Assert.Equal("AnomalyVal", overlay.AnomalyField);
        Assert.Equal("#2563eb", overlay.Color);
        Assert.Equal("Projected Revenue", overlay.Label);
        Assert.Equal(OverlayLineStyle.Dashed, overlay.LineStyle);

        var serialized = statement.ToSql();
        Assert.Contains("FORECAST(ForecastRev) AS DASHED", serialized);
        Assert.Contains("CONFIDENCE_LOW = LowBound", serialized);
        Assert.Contains("CONFIDENCE_HIGH = HighBound", serialized);
        Assert.Contains("ANOMALY = AnomalyVal", serialized);
        Assert.Contains("COLOR = '#2563eb'", serialized);
        Assert.Contains("LABEL = 'Projected Revenue'", serialized);

        var roundTrip = new Parser(new Lexer(serialized).Tokenize(), serialized).Parse();
        Assert.Empty(roundTrip.Diagnostics);
        var roundStmt = Assert.Single(roundTrip.Statements.OfType<CreateVisualStatement>());
        var roundOverlay = Assert.Single(roundStmt.Overlays);
        Assert.Equal(OverlayType.Forecast, roundOverlay.OverlayType);
        Assert.Equal("ForecastRev", roundOverlay.ForecastField);
        Assert.Equal("LowBound", roundOverlay.ConfidenceLowField);
        Assert.Equal("HighBound", roundOverlay.ConfidenceHighField);
        Assert.Equal("AnomalyVal", roundOverlay.AnomalyField);
        Assert.Equal("#2563eb", roundOverlay.Color);
        Assert.Equal("Projected Revenue", roundOverlay.Label);
    }

    [Fact]
    public void NamedLine_MinimalForecastOverlay()
    {
        const string sql = """
            CREATE VISUAL MinimalForecast AS LINE (
              SOURCE = #sales,
              MAPPINGS (
                X = Month,
                Y = Revenue
              ),
              OVERLAYS (
                FORECAST(ForecastRev) AS DASHED
              )
            );
            """;

        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        var overlay = Assert.Single(statement.Overlays);
        Assert.Equal(OverlayType.Forecast, overlay.OverlayType);
        Assert.Equal("ForecastRev", overlay.ForecastField);
        Assert.Null(overlay.ConfidenceLowField);
        Assert.Null(overlay.ConfidenceHighField);
        Assert.Null(overlay.AnomalyField);

        var serialized = statement.ToSql();
        Assert.Contains("FORECAST(ForecastRev) AS DASHED", serialized);

        var roundTrip = new Parser(new Lexer(serialized).Tokenize(), serialized).Parse();
        Assert.Empty(roundTrip.Diagnostics);
        var roundStmt = Assert.Single(roundTrip.Statements.OfType<CreateVisualStatement>());
        var roundOverlay = Assert.Single(roundStmt.Overlays);
        Assert.Equal("ForecastRev", roundOverlay.ForecastField);
    }

    [Fact]
    public void NamedCombo_ParsesAndRoundTripsForecastOverlay()
    {
        const string sql = """
            CREATE VISUAL ComboWithForecast AS COMBO (
              SOURCE = #data,
              MAPPINGS (
                X = Period,
                Y = Actuals
              ),
              SERIES (
                BAR Actuals
              ),
              OVERLAYS (
                FORECAST(Projected) AS DASHED WITH (
                  CONFIDENCE_LOW = BandLow,
                  CONFIDENCE_HIGH = BandHigh,
                  COLOR = '#10b981'
                )
              )
            );
            """;

        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        var overlay = Assert.Single(statement.Overlays);
        Assert.Equal(OverlayType.Forecast, overlay.OverlayType);
        Assert.Equal("Projected", overlay.ForecastField);
        Assert.Equal("BandLow", overlay.ConfidenceLowField);
        Assert.Equal("BandHigh", overlay.ConfidenceHighField);
    }

    [Fact]
    public void NamedVisualChartLowerer_LowersForecastToThreeLayers()
    {
        const string sql = """
            CREATE VISUAL SalesForecast AS LINE (
              SOURCE = #sales,
              MAPPINGS (
                X = Month,
                Y = Revenue
              ),
              OVERLAYS (
                FORECAST(ForecastRev) AS DASHED WITH (
                  CONFIDENCE_LOW = LowBound,
                  CONFIDENCE_HIGH = HighBound,
                  ANOMALY = AnomalyVal,
                  COLOR = '#2563eb',
                  LABEL = 'Outlook'
                )
              )
            );
            """;

        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        var manifest = new VisualManifest
        {
            Name = "SalesForecast",
            Columns = ["Month", "Revenue", "ForecastRev", "LowBound", "HighBound", "AnomalyVal"],
            Rows = [
                ["Jan", "100", null, null, null, null],
                ["Feb", "120", "120", "110", "130", null],
                ["Mar", null, "140", "125", "155", "140"]
            ]
        };

        var lowerer = new NamedVisualChartLowerer(new SystemExecutionContext());
        var spec = lowerer.Lower(statement, manifest);

        // Base line layer + 3 overlay layers: Area (band), Line (forecast), Point (anomaly)
        Assert.Equal(4, spec.Layers.Length);

        var bandLayer = spec.Layers.Single(l => l.Mark == MarkKind.Area);
        Assert.Equal(98, bandLayer.ZIndex);
        Assert.Contains(bandLayer.Bindings, b => b.Channel == FieldChannel.X && b.Field == "Month");
        Assert.Contains(bandLayer.Bindings, b => b.Channel == FieldChannel.ConfidenceLow && b.Field == "LowBound");
        Assert.Contains(bandLayer.Bindings, b => b.Channel == FieldChannel.ConfidenceHigh && b.Field == "HighBound");
        Assert.Equal("ForecastConfidence", bandLayer.Style.Single(s => s.Name == "overlayType").Value);

        var forecastLayer = spec.Layers.Single(l => l.Mark == MarkKind.Line && l.Style.Any(s => s.Name == "overlayType" && s.Value == "Forecast"));
        Assert.Equal(100, forecastLayer.ZIndex);
        Assert.Contains(forecastLayer.Bindings, b => b.Channel == FieldChannel.X && b.Field == "Month");
        Assert.Contains(forecastLayer.Bindings, b => b.Channel == FieldChannel.Y && b.Field == "ForecastRev");
        Assert.Equal("dashed", forecastLayer.Style.Single(s => s.Name == "lineStyle").Value);

        var anomalyLayer = spec.Layers.Single(l => l.Mark == MarkKind.Point && l.Style.Any(s => s.Name == "overlayType" && s.Value == "ForecastAnomaly"));
        Assert.Equal(102, anomalyLayer.ZIndex);
        Assert.Contains(anomalyLayer.Bindings, b => b.Channel == FieldChannel.X && b.Field == "Month");
        Assert.Contains(anomalyLayer.Bindings, b => b.Channel == FieldChannel.Y && b.Field == "AnomalyVal");
    }

    [Theory]
    [InlineData("BAR")]
    [InlineData("SCATTER")]
    [InlineData("PIE")]
    public void NamedVisualChartLowerer_RejectsUnsupportedVisualTypes(string visualType)
    {
        var sql = $"""
            CREATE VISUAL BadType AS {visualType} (
              SOURCE = #sales,
              MAPPINGS (
                X = Month,
                Y = Revenue
              ),
              OVERLAYS (
                FORECAST(ForecastRev) AS DASHED
              )
            );
            """;

        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        var manifest = new VisualManifest
        {
            Name = "BadType",
            Columns = ["Month", "Revenue", "ForecastRev"],
            Rows = [["Jan", "100", "110"]]
        };

        var lowerer = new NamedVisualChartLowerer(new SystemExecutionContext());
        var ex = Assert.Throws<InvalidOperationException>(() => lowerer.Lower(statement, manifest));
        Assert.Contains("FORECAST overlay is supported only on LINE and COMBO visuals", ex.Message);
    }

    [Fact]
    public void NamedVisualChartLowerer_RejectsUnpairedConfidence()
    {
        const string sqlLowOnly = """
            CREATE VISUAL UnpairedConf AS LINE (
              SOURCE = #sales,
              MAPPINGS (
                X = Month,
                Y = Revenue
              ),
              OVERLAYS (
                FORECAST(ForecastRev) AS DASHED WITH (
                  CONFIDENCE_LOW = LowBound
                )
              )
            );
            """;

        var script = new Parser(new Lexer(sqlLowOnly).Tokenize(), sqlLowOnly).Parse();
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        var manifest = new VisualManifest
        {
            Name = "UnpairedConf",
            Columns = ["Month", "Revenue", "ForecastRev", "LowBound"],
            Rows = [["Jan", "100", "110", "90"]]
        };

        var lowerer = new NamedVisualChartLowerer(new SystemExecutionContext());
        var ex = Assert.Throws<InvalidOperationException>(() => lowerer.Lower(statement, manifest));
        Assert.Contains("requires both CONFIDENCE_LOW and CONFIDENCE_HIGH as a pair", ex.Message);
    }

    [Fact]
    public void NamedVisualChartLowerer_RejectsMissingXOrY()
    {
        const string sqlNoX = """
            CREATE VISUAL NoX AS LINE (
              SOURCE = #sales,
              MAPPINGS (
                Y = Revenue
              ),
              OVERLAYS (
                FORECAST(ForecastRev) AS DASHED
              )
            );
            """;

        var script = new Parser(new Lexer(sqlNoX).Tokenize(), sqlNoX).Parse();
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        var manifest = new VisualManifest
        {
            Name = "NoX",
            Columns = ["Revenue", "ForecastRev"],
            Rows = [["100", "110"]]
        };

        var lowerer = new NamedVisualChartLowerer(new SystemExecutionContext());
        var ex = Assert.Throws<InvalidOperationException>(() => lowerer.Lower(statement, manifest));
        Assert.Contains("FORECAST overlay requires an X mapping", ex.Message);
    }

    [Fact]
    public void CustomArea_ParsesAndRoundTripsConfidenceChannels()
    {
        const string sql = """
            CREATE VISUAL ForecastArea AS CUSTOM (
              SOURCE = #data,
              CHART (
                COORDINATE (TYPE = CARTESIAN),
                LAYERS (
                  confidence = AREA (
                    ENCODINGS (
                      X = Period (TYPE = NOMINAL),
                      CONFIDENCE_LOW = Low (TYPE = QUANTITATIVE),
                      CONFIDENCE_HIGH = High (TYPE = QUANTITATIVE)
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
        Assert.Contains(layer.Encodings, e => e.Channel == AdvancedChartChannel.ConfidenceLow);
        Assert.Contains(layer.Encodings, e => e.Channel == AdvancedChartChannel.ConfidenceHigh);

        var serialized = statement.ToSql();
        Assert.Contains("CONFIDENCE_LOW = Low ( TYPE = QUANTITATIVE )", serialized);
        Assert.Contains("CONFIDENCE_HIGH = High ( TYPE = QUANTITATIVE )", serialized);

        var roundTrip = new Parser(new Lexer(serialized).Tokenize(), serialized).Parse();
        Assert.Empty(roundTrip.Diagnostics);
        var roundLayer = Assert.Single(Assert.Single(roundTrip.Statements.OfType<CreateVisualStatement>()).AdvancedChart!.Layers);
        Assert.Contains(roundLayer.Encodings, e => e.Channel == AdvancedChartChannel.ConfidenceLow);
        Assert.Contains(roundLayer.Encodings, e => e.Channel == AdvancedChartChannel.ConfidenceHigh);
    }

    [Fact]
    public void EnumBridge_ConfidenceChannelsParity()
    {
        Assert.Equal(FieldChannel.ConfidenceLow, AdvancedChartEnumBridge.Channel(AdvancedChartChannel.ConfidenceLow));
        Assert.Equal(FieldChannel.ConfidenceHigh, AdvancedChartEnumBridge.Channel(AdvancedChartChannel.ConfidenceHigh));
        Assert.Equal(AdvancedChartChannel.ConfidenceLow, AdvancedChartEnumBridge.Channel(FieldChannel.ConfidenceLow));
        Assert.Equal(AdvancedChartChannel.ConfidenceHigh, AdvancedChartEnumBridge.Channel(FieldChannel.ConfidenceHigh));
    }

    [Fact]
    public void AdvancedChartSemanticValidator_AcceptsValidConfidenceChannels()
    {
        const string sql = """
            CREATE VISUAL ValidConfidence AS CUSTOM (
              SOURCE = #data,
              CHART (
                COORDINATE (TYPE = CARTESIAN),
                LAYERS (
                  band = AREA (
                    ENCODINGS (
                      X = Period (TYPE = NOMINAL),
                      CONFIDENCE_LOW = Low (TYPE = QUANTITATIVE),
                      CONFIDENCE_HIGH = High (TYPE = QUANTITATIVE)
                    )
                  )
                )
              )
            );
            """;

        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        var diagnostics = AdvancedChartSemanticValidator.Validate(statement);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void AdvancedChartSemanticValidator_RejectsNonAreaMark()
    {
        const string sql = """
            CREATE VISUAL PointConfidence AS CUSTOM (
              SOURCE = #data,
              CHART (
                COORDINATE (TYPE = CARTESIAN),
                LAYERS (
                  points = POINT (
                    ENCODINGS (
                      X = Period (TYPE = NOMINAL),
                      CONFIDENCE_LOW = Low (TYPE = QUANTITATIVE),
                      CONFIDENCE_HIGH = High (TYPE = QUANTITATIVE)
                    )
                  )
                )
              )
            );
            """;

        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        var diagnostics = AdvancedChartSemanticValidator.Validate(statement);
        Assert.Contains(diagnostics, d => d.Code == "RPT-CHART" && d.Message.Contains("only AREA marks support confidence channels"));
    }

    [Fact]
    public void AdvancedChartSemanticValidator_RejectsPolarCoordinates()
    {
        const string sql = """
            CREATE VISUAL PolarConfidence AS CUSTOM (
              SOURCE = #data,
              CHART (
                COORDINATE (TYPE = POLAR),
                LAYERS (
                  band = AREA (
                    ENCODINGS (
                      X = Period (TYPE = NOMINAL),
                      CONFIDENCE_LOW = Low (TYPE = QUANTITATIVE),
                      CONFIDENCE_HIGH = High (TYPE = QUANTITATIVE)
                    )
                  )
                )
              )
            );
            """;

        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        var diagnostics = AdvancedChartSemanticValidator.Validate(statement);
        Assert.Contains(diagnostics, d => d.Code == "RPT-CHART" && d.Message.Contains("require CARTESIAN or TRANSPOSED_CARTESIAN coordinates"));
    }

    [Fact]
    public void AdvancedChartSemanticValidator_RejectsUnpairedEndpoint()
    {
        const string sql = """
            CREATE VISUAL UnpairedConf AS CUSTOM (
              SOURCE = #data,
              CHART (
                COORDINATE (TYPE = CARTESIAN),
                LAYERS (
                  band = AREA (
                    ENCODINGS (
                      X = Period (TYPE = NOMINAL),
                      CONFIDENCE_LOW = Low (TYPE = QUANTITATIVE)
                    )
                  )
                )
              )
            );
            """;

        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        var diagnostics = AdvancedChartSemanticValidator.Validate(statement);
        Assert.Contains(diagnostics, d => d.Code == "RPT-CHART" && d.Message.Contains("requires both CONFIDENCE_LOW and CONFIDENCE_HIGH as a pair"));
    }

    [Fact]
    public void AdvancedChartSemanticValidator_RejectsNonQuantitativeType()
    {
        const string sql = """
            CREATE VISUAL NominalConf AS CUSTOM (
              SOURCE = #data,
              CHART (
                COORDINATE (TYPE = CARTESIAN),
                LAYERS (
                  band = AREA (
                    ENCODINGS (
                      X = Period (TYPE = NOMINAL),
                      CONFIDENCE_LOW = Low (TYPE = QUANTITATIVE),
                      CONFIDENCE_HIGH = High (TYPE = NOMINAL)
                    )
                  )
                )
              )
            );
            """;

        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        var diagnostics = AdvancedChartSemanticValidator.Validate(statement);
        Assert.Contains(diagnostics, d => d.Code == "RPT-CHART" && d.Message.Contains("requires QUANTITATIVE TYPE"));
    }

    [Fact]
    public void AdvancedChartSemanticValidator_RejectsMissingX()
    {
        const string sql = """
            CREATE VISUAL NoXConf AS CUSTOM (
              SOURCE = #data,
              CHART (
                COORDINATE (TYPE = CARTESIAN),
                LAYERS (
                  band = AREA (
                    ENCODINGS (
                      CONFIDENCE_LOW = Low (TYPE = QUANTITATIVE),
                      CONFIDENCE_HIGH = High (TYPE = QUANTITATIVE)
                    )
                  )
                )
              )
            );
            """;

        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        var diagnostics = AdvancedChartSemanticValidator.Validate(statement);
        Assert.Contains(diagnostics, d => d.Code == "RPT-CHART" && d.Message.Contains("requires an X encoding"));
    }

    [Fact]
    public void AdvancedChartSemanticValidator_RejectsConflictWithY()
    {
        const string sql = """
            CREATE VISUAL ConflictY AS CUSTOM (
              SOURCE = #data,
              CHART (
                COORDINATE (TYPE = CARTESIAN),
                LAYERS (
                  band = AREA (
                    ENCODINGS (
                      X = Period (TYPE = NOMINAL),
                      Y = Value (TYPE = QUANTITATIVE),
                      CONFIDENCE_LOW = Low (TYPE = QUANTITATIVE),
                      CONFIDENCE_HIGH = High (TYPE = QUANTITATIVE)
                    )
                  )
                )
              )
            );
            """;

        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        var diagnostics = AdvancedChartSemanticValidator.Validate(statement);
        Assert.Contains(diagnostics, d => d.Code == "RPT-CHART" && d.Message.Contains("cannot combine CONFIDENCE_LOW/CONFIDENCE_HIGH with Y, Y2, Y_START, or Y_END"));
    }

    [Fact]
    public void AdvancedChartSemanticValidator_RejectsSecondaryAxisOnConfidence()
    {
        const string sql = """
            CREATE VISUAL SecondaryConf AS CUSTOM (
              SOURCE = #data,
              CHART (
                COORDINATE (TYPE = CARTESIAN),
                LAYERS (
                  band = AREA (
                    ENCODINGS (
                      X = Period (TYPE = NOMINAL),
                      CONFIDENCE_LOW = Low (TYPE = QUANTITATIVE, AXIS = SECONDARY),
                      CONFIDENCE_HIGH = High (TYPE = QUANTITATIVE)
                    )
                  )
                )
              )
            );
            """;

        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        var diagnostics = AdvancedChartSemanticValidator.Validate(statement);
        Assert.Contains(diagnostics, d => d.Code == "RPT-CHART" && (d.Message.Contains("CONFIDENCE_LOW encoding must use the primary axis") || d.Message.Contains("AXIS=SECONDARY only on the Y2 channel")));
    }

    [Fact]
    public void AdvancedChartSemanticValidator_RejectsMismatchedScaleIds()
    {
        const string sql = """
            CREATE VISUAL MismatchedScales AS CUSTOM (
              SOURCE = #data,
              CHART (
                COORDINATE (TYPE = CARTESIAN),
                SCALES (
                  scaleA = LINEAR (CHANNEL = Y),
                  scaleB = LINEAR (CHANNEL = Y)
                ),
                LAYERS (
                  band = AREA (
                    ENCODINGS (
                      X = Period (TYPE = NOMINAL),
                      CONFIDENCE_LOW = Low (TYPE = QUANTITATIVE, SCALE = scaleA),
                      CONFIDENCE_HIGH = High (TYPE = QUANTITATIVE, SCALE = scaleB)
                    )
                  )
                )
              )
            );
            """;

        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        var diagnostics = AdvancedChartSemanticValidator.Validate(statement);
        Assert.Contains(diagnostics, d => d.Code == "RPT-CHART" && d.Message.Contains("CONFIDENCE_LOW and CONFIDENCE_HIGH must resolve to the same scale ID"));
    }

    [Fact]
    public void ChartSpec_Validate_RejectsDirectConstructionViolations()
    {
        // 1. Non-AREA mark with confidence channels
        var nonAreaSpec = ChartSpec.Create(
            "non-area",
            "#data",
            [
                new FieldBinding(FieldChannel.X, "Period", DataSemanticKind.Nominal, "x"),
                new FieldBinding(FieldChannel.ConfidenceLow, "Low", DataSemanticKind.Quantitative, "y"),
                new FieldBinding(FieldChannel.ConfidenceHigh, "High", DataSemanticKind.Quantitative, "y")
            ],
            [
                new MarkLayerSpec("layer1", MarkKind.Point, 1, [
                    new FieldBinding(FieldChannel.X, "Period", DataSemanticKind.Nominal, "x"),
                    new FieldBinding(FieldChannel.ConfidenceLow, "Low", DataSemanticKind.Quantitative, "y"),
                    new FieldBinding(FieldChannel.ConfidenceHigh, "High", DataSemanticKind.Quantitative, "y")
                ], [], "layer1")
            ],
            new CoordinateSpec(CoordinateKind.Cartesian),
            [
                new ScaleSpec("x", FieldChannel.X, ScaleKind.Band, false, []),
                new ScaleSpec("y", FieldChannel.Y, ScaleKind.Linear, false, [])
            ],
            new FormattingSpec("en-US", "UTC", "—", []),
            new NullHandlingSpec(NullValuePolicy.Gap, []),
            new ThemeSpec("default", []),
            new AccessibilitySpec("title", "summary", "{series}: {value}", true));

        var ex1 = Assert.Throws<InvalidDataException>(() => nonAreaSpec.Validate());
        Assert.Contains("does not support confidence channels", ex1.Message);

        // 2. Unpaired confidence channels
        var unpairedSpec = ChartSpec.Create(
            "unpaired",
            "#data",
            [
                new FieldBinding(FieldChannel.X, "Period", DataSemanticKind.Nominal, "x"),
                new FieldBinding(FieldChannel.ConfidenceLow, "Low", DataSemanticKind.Quantitative, "y")
            ],
            [
                new MarkLayerSpec("layer1", MarkKind.Area, 1, [
                    new FieldBinding(FieldChannel.X, "Period", DataSemanticKind.Nominal, "x"),
                    new FieldBinding(FieldChannel.ConfidenceLow, "Low", DataSemanticKind.Quantitative, "y")
                ], [], "layer1")
            ],
            new CoordinateSpec(CoordinateKind.Cartesian),
            [
                new ScaleSpec("x", FieldChannel.X, ScaleKind.Band, false, []),
                new ScaleSpec("y", FieldChannel.Y, ScaleKind.Linear, false, [])
            ],
            new FormattingSpec("en-US", "UTC", "—", []),
            new NullHandlingSpec(NullValuePolicy.Gap, []),
            new ThemeSpec("default", []),
            new AccessibilitySpec("title", "summary", "{series}: {value}", true));

        var ex2 = Assert.Throws<InvalidDataException>(() => unpairedSpec.Validate());
        Assert.Contains("requires both CONFIDENCE_LOW and CONFIDENCE_HIGH", ex2.Message);

        // 3. Mismatched scale IDs
        var mismatchedSpec = ChartSpec.Create(
            "mismatched",
            "#data",
            [
                new FieldBinding(FieldChannel.X, "Period", DataSemanticKind.Nominal, "x"),
                new FieldBinding(FieldChannel.ConfidenceLow, "Low", DataSemanticKind.Quantitative, "y1"),
                new FieldBinding(FieldChannel.ConfidenceHigh, "High", DataSemanticKind.Quantitative, "y2")
            ],
            [
                new MarkLayerSpec("layer1", MarkKind.Area, 1, [
                    new FieldBinding(FieldChannel.X, "Period", DataSemanticKind.Nominal, "x"),
                    new FieldBinding(FieldChannel.ConfidenceLow, "Low", DataSemanticKind.Quantitative, "y1"),
                    new FieldBinding(FieldChannel.ConfidenceHigh, "High", DataSemanticKind.Quantitative, "y2")
                ], [], "layer1")
            ],
            new CoordinateSpec(CoordinateKind.Cartesian),
            [
                new ScaleSpec("x", FieldChannel.X, ScaleKind.Band, false, []),
                new ScaleSpec("y1", FieldChannel.Y, ScaleKind.Linear, false, []),
                new ScaleSpec("y2", FieldChannel.Y, ScaleKind.Linear, false, [])
            ],
            new FormattingSpec("en-US", "UTC", "—", []),
            new NullHandlingSpec(NullValuePolicy.Gap, []),
            new ThemeSpec("default", []),
            new AccessibilitySpec("title", "summary", "{series}: {value}", true));

        var ex3 = Assert.Throws<InvalidDataException>(() => mismatchedSpec.Validate());
        Assert.Contains("must resolve to the same scale ID", ex3.Message);
    }

    [Fact]
    public void PlotPlanResolver_ExpandsYDomainWithConfidenceAndForecast()
    {
        const string sql = """
            CREATE VISUAL ForecastVisual AS LINE (
              SOURCE = #data,
              MAPPINGS (
                X = Period,
                Y = ActualRevenue
              ),
              OVERLAYS (
                FORECAST(ProjectedRevenue) AS DASHED WITH (
                  CONFIDENCE_LOW = LowRev,
                  CONFIDENCE_HIGH = HighRev
                )
              )
            );
            """;

        var manifest = new VisualManifest
        {
            Name = "ForecastVisual",
            Columns = ["Period", "ActualRevenue", "ProjectedRevenue", "LowRev", "HighRev"],
            Rows = [
                ["Jan", "50", null, null, null],
                ["Feb", "60", "60", "40", "80"],
                ["Mar", null, "75", "30", "110"]
            ]
        };

        var plan = ResolveNamed(sql, manifest);
        var yScale = plan.Scales.Single(s => s.Channel == FieldChannel.Y);

        var min = PlotPlanResolver.Number(yScale.Domain[0]);
        var max = PlotPlanResolver.Number(yScale.Domain[1]);

        // Minimum must cover LowRev (30 or lower with zero), Maximum must cover HighRev (110 or higher)
        Assert.True(min <= 30m, $"Expected domain min <= 30, was {min}");
        Assert.True(max >= 110m, $"Expected domain max >= 110, was {max}");
    }

    [Fact]
    public void PlotPlanResolver_HandlesGapsAndNullsInConfidenceAndForecast()
    {
        const string sql = """
            CREATE VISUAL ForecastVisual AS LINE (
              SOURCE = #data,
              MAPPINGS (
                X = Period,
                Y = ActualRevenue
              ),
              OVERLAYS (
                FORECAST(ProjectedRevenue) AS DASHED WITH (
                  CONFIDENCE_LOW = LowRev,
                  CONFIDENCE_HIGH = HighRev,
                  ANOMALY = AnomalyVal
                )
              )
            );
            """;

        var manifest = new VisualManifest
        {
            Name = "ForecastVisual",
            Columns = ["Period", "ActualRevenue", "ProjectedRevenue", "LowRev", "HighRev", "AnomalyVal"],
            Rows = [
                ["Jan", "50", null, null, null, null],
                ["Feb", "60", "60", "40", "80", null],
                ["Mar", null, "70", "50", "90", "95"],
                ["Apr", null, null, null, null, null]
            ]
        };

        var plan = ResolveNamed(sql, manifest);

        var bandLayer = plan.Layers.Single(l => l.Mark == MarkKind.Area);
        Assert.True(bandLayer.Data[0].IsGap, "Row 0 has null confidence bounds, must be marked gap");
        Assert.False(bandLayer.Data[1].IsGap, "Row 1 has valid confidence bounds, must not be gap");
        Assert.False(bandLayer.Data[2].IsGap, "Row 2 has valid confidence bounds, must not be gap");
        Assert.True(bandLayer.Data[3].IsGap, "Row 3 has null confidence bounds, must be marked gap");

        var forecastLine = plan.Layers.Single(l => l.Mark == MarkKind.Line && l.Style.Any(s => s.Name == "overlayType" && s.Value == "Forecast"));
        Assert.True(forecastLine.Data[0].IsGap, "Row 0 has null forecast, must be gap");
        Assert.False(forecastLine.Data[1].IsGap, "Row 1 has forecast, not gap");
        Assert.False(forecastLine.Data[2].IsGap, "Row 2 has forecast, not gap");
        Assert.True(forecastLine.Data[3].IsGap, "Row 3 has null forecast, must be gap");

        var anomalyLayer = plan.Layers.Single(l => l.Mark == MarkKind.Point && l.Style.Any(s => s.Name == "overlayType" && s.Value == "ForecastAnomaly"));
        Assert.True(anomalyLayer.Data[0].IsGap, "Row 0 has null anomaly, must be gap");
        Assert.True(anomalyLayer.Data[1].IsGap, "Row 1 has null anomaly, must be gap");
        Assert.False(anomalyLayer.Data[2].IsGap, "Row 2 has non-null anomaly, must not be gap");
    }

    [Fact]
    public void SvgRenderer_EmitsSemanticClassesAndDataAttributes()
    {
        const string sql = """
            CREATE VISUAL ForecastVisual AS LINE (
              SOURCE = #data,
              MAPPINGS (
                X = Period,
                Y = ActualRevenue
              ),
              OVERLAYS (
                FORECAST(ProjectedRevenue) AS DASHED WITH (
                  CONFIDENCE_LOW = LowRev,
                  CONFIDENCE_HIGH = HighRev,
                  ANOMALY = AnomalyVal,
                  COLOR = '#2563eb',
                  LABEL = 'Model Output'
                )
              )
            );
            """;

        var manifest = new VisualManifest
        {
            Name = "ForecastVisual",
            Columns = ["Period", "ActualRevenue", "ProjectedRevenue", "LowRev", "HighRev", "AnomalyVal"],
            Rows = [
                ["Jan", "50", null, null, null, null],
                ["Feb", "60", "60", "40", "80", null],
                ["Mar", null, "75", "50", "100", "75"]
            ]
        };

        var plan = ResolveNamed(sql, manifest);
        var svg = new SvgChartRenderer().Render(plan);

        // SVG group wrappers for overlays
        Assert.Contains("data-overlay-type='ForecastConfidence'", svg);
        Assert.Contains("data-overlay-type='Forecast'", svg);
        Assert.Contains("data-overlay-type='ForecastAnomaly'", svg);

        // Semantic CSS classes
        Assert.Contains("class='plot-confidence-band'", svg);
        Assert.Contains("class='plot-forecast-line'", svg);
        Assert.Contains("plot-anomaly-marker", svg);

        // Confidence ribbon styling
        Assert.Contains("fill-opacity='.2'", svg);
        Assert.Contains("stroke-width='1'", svg);
    }

    [Fact]
    public void TerminalRenderer_RendersConfidenceIntervalsAndForecast()
    {
        const string sql = """
            CREATE VISUAL ForecastVisual AS LINE (
              SOURCE = #data,
              MAPPINGS (
                X = Period,
                Y = ActualRevenue
              ),
              OVERLAYS (
                FORECAST(ProjectedRevenue) AS DASHED WITH (
                  CONFIDENCE_LOW = LowRev,
                  CONFIDENCE_HIGH = HighRev,
                  ANOMALY = AnomalyVal,
                  COLOR = '#2563eb',
                  LABEL = 'Model Output'
                )
              )
            );
            """;

        var manifest = new VisualManifest
        {
            Name = "ForecastVisual",
            Columns = ["Period", "ActualRevenue", "ProjectedRevenue", "LowRev", "HighRev", "AnomalyVal"],
            Rows = [
                ["Jan", "50", null, null, null, null],
                ["Feb", "60", "60", "40", "80", null],
                ["Mar", null, "75", "50", "100", "75"]
            ]
        };

        var plan = ResolveNamed(sql, manifest);

        // Semantic fallback includes confidence interval
        var fallback = plan.Fallback;
        Assert.NotEmpty(fallback.Items);
        Assert.Contains(fallback.Items, item => item.Detail != null && item.Detail.Contains("confidence 40 to 80"));
        Assert.Contains(fallback.Items, item => item.Detail == "forecast");
        Assert.Contains(fallback.Items, item => item.Detail == "anomaly");

        // Terminal renderer includes interval in output
        var terminal = PlotPlanTerminalRenderer.Render(plan, 80);
        var writer = new StringWriter();
        var testConsole = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer)
        });
        testConsole.Write(terminal);
        var text = writer.ToString();
        Assert.Contains("confidence 40 to 80", text);
    }

    [Fact]
    public void DesignerScriptParsing_PreservesForecastOverlay()
    {
        const string sql = """
            CREATE VISUAL SalesForecast AS LINE (
              SOURCE = #sales,
              MAPPINGS (
                X = Month,
                Y = Revenue
              ),
              OVERLAYS (
                FORECAST(ForecastRev) AS DASHED WITH (
                  CONFIDENCE_LOW = LowBound,
                  CONFIDENCE_HIGH = HighBound,
                  ANOMALY = AnomalyVal,
                  COLOR = '#2563eb',
                  LABEL = 'Forecast'
                )
              )
            );
            """;

        var service = new DesignerScriptParsingService();
        var state = service.Parse(sql);
        var visual = state.Pages.SelectMany(p => p.Visuals).Single(v => v.Name == "SalesForecast");

        Assert.True(visual.Options.ContainsKey("overlays"));
        var overlays = visual.Options["overlays"];
        Assert.Contains("FORECAST(ForecastRev) AS DASHED", overlays);
        Assert.Contains("CONFIDENCE_LOW = LowBound", overlays);
        Assert.Contains("CONFIDENCE_HIGH = HighBound", overlays);
        Assert.Contains("ANOMALY = AnomalyVal", overlays);
        Assert.Contains("COLOR = '#2563eb'", overlays);
        Assert.Contains("LABEL = 'Forecast'", overlays);
    }

    [Fact]
    public void SampleScript_ParsesAndValidatesCleanly()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ETL-SQL.slnx")))
            dir = dir.Parent;
        var repoRoot = dir?.FullName ?? AppContext.BaseDirectory;
        var path = Path.Combine(repoRoot, "samples", "08_Reporting", "forecast_overlay_anomaly_intervals.rptsql");
        Assert.True(File.Exists(path), $"Sample file not found at {path}");
        var sql = File.ReadAllText(path);
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        var customVisual = script.Statements.OfType<CreateVisualStatement>().Single(v => v.AdvancedChart != null);
        var diagnostics = AdvancedChartSemanticValidator.Validate(customVisual);
        Assert.Empty(diagnostics);

        // Assert that the custom chart contains all four expected layers
        var chart = customVisual.AdvancedChart!;
        Assert.Equal(4, chart.Layers.Length);
        Assert.Contains(chart.Layers, l => l.Mark == AdvancedChartMarkKind.Area && l.Name == "confidence_band");
        Assert.Contains(chart.Layers, l => l.Mark == AdvancedChartMarkKind.Line && l.Name == "actual_line");
        Assert.Contains(chart.Layers, l => l.Mark == AdvancedChartMarkKind.Line && l.Name == "forecast_line");

        // Assert that the anomaly POINT layer binds Y to Anomaly
        var anomalyLayer = Assert.Single(chart.Layers.Where(l => l.Mark == AdvancedChartMarkKind.Point && l.Name == "anomaly_points"));
        var yEncoding = Assert.Single(anomalyLayer.Encodings.Where(e => e.Channel == AdvancedChartChannel.Y));
        Assert.Equal("Anomaly", yEncoding.Source.Field);
    }

    [Fact]
    public void TransposedConfidenceRibbon_SplitsAcrossNullGap()
    {
        const string sql = """
            CREATE VISUAL TransposedForecast AS CUSTOM (
              SOURCE = #transposed_data,
              CHART (
                COORDINATE (TYPE = TRANSPOSED_CARTESIAN),
                SCALES (
                  cat_scale = BAND (CHANNEL = X, ORDER = SOURCE),
                  val_scale = LINEAR (CHANNEL = Y, INCLUDE_ZERO = ON)
                ),
                LAYERS (
                  confidence_band = AREA (
                    ENCODINGS (
                      X = Month (TYPE = NOMINAL, SCALE = cat_scale),
                      CONFIDENCE_LOW = Low (TYPE = QUANTITATIVE, SCALE = val_scale),
                      CONFIDENCE_HIGH = High (TYPE = QUANTITATIVE, SCALE = val_scale)
                    )
                  )
                )
              )
            );
            """;

        var manifest = new VisualManifest
        {
            Name = "TransposedForecast",
            Columns = ["Month", "Low", "High"],
            Rows = [
                ["M1", "10", "20"],
                ["M2", "12", "22"],
                ["M3", null, null],
                ["M4", "15", "25"],
                ["M5", "18", "28"]
            ]
        };

        var plan = ResolveCustom(sql, manifest);
        var svg = new SvgChartRenderer().Render(plan);

        // Data pattern: valid, valid, gap, valid, valid
        // Must emit exactly two confidence band paths separated by the missing interval, not one path bridging across it
        var bandMatches = Regex.Matches(svg, @"<path class='plot-confidence-band'");
        Assert.Equal(2, bandMatches.Count);
    }

    [Fact]
    public void OverlayManifest_JsonSerialization_PreservesAllForecastProperties()
    {
        var overlay = new OverlayManifest
        {
            OverlayType = "Forecast",
            LineStyle = "dashed",
            Color = "#2563eb",
            Label = "Forecast Model",
            ForecastField = "ForecastRev",
            ConfidenceLowField = "ConfLow",
            ConfidenceHighField = "ConfHigh",
            AnomalyField = "AnomalyPoint"
        };

        var json = JsonSerializer.Serialize(overlay);
        Assert.Contains("\"forecastField\":\"ForecastRev\"", json);
        Assert.Contains("\"confidenceLowField\":\"ConfLow\"", json);
        Assert.Contains("\"confidenceHighField\":\"ConfHigh\"", json);
        Assert.Contains("\"anomalyField\":\"AnomalyPoint\"", json);
        Assert.Contains("\"color\":\"#2563eb\"", json);
        Assert.Contains("\"label\":\"Forecast Model\"", json);
        Assert.Contains("\"lineStyle\":\"dashed\"", json);

        var deserialized = JsonSerializer.Deserialize<OverlayManifest>(json);
        Assert.NotNull(deserialized);
        Assert.Equal("Forecast", deserialized.OverlayType);
        Assert.Equal("dashed", deserialized.LineStyle);
        Assert.Equal("#2563eb", deserialized.Color);
        Assert.Equal("Forecast Model", deserialized.Label);
        Assert.Equal("ForecastRev", deserialized.ForecastField);
        Assert.Equal("ConfLow", deserialized.ConfidenceLowField);
        Assert.Equal("ConfHigh", deserialized.ConfidenceHighField);
        Assert.Equal("AnomalyPoint", deserialized.AnomalyField);
    }

    [Fact]
    public void Designer_ParseGenerateParse_PreservesCompleteForecastOverlay()
    {
        const string sql = """
            CREATE VISUAL SalesForecast AS LINE (
              SOURCE = #sales,
              MAPPINGS (X = Month, Y = Revenue),
              OVERLAYS (
                FORECAST(ForecastRev) AS DASHED WITH (
                  CONFIDENCE_LOW = LowBound,
                  CONFIDENCE_HIGH = HighBound,
                  ANOMALY = AnomalyVal,
                  COLOR = '#2563eb',
                  LABEL = 'Forecast'
                )
              )
            );
            """;

        var parsingService = new DesignerScriptParsingService();
        var generationService = new DesignerScriptGenerationService();

        var state1 = parsingService.Parse(sql);
        var generatedSql = generationService.Generate(state1);
        var state2 = parsingService.Parse(generatedSql);

        var visual2 = state2.Pages.SelectMany(p => p.Visuals).Single(v => v.Name == "SalesForecast");
        Assert.True(visual2.Options.ContainsKey("overlays"));
        var overlays = visual2.Options["overlays"];
        Assert.Contains("FORECAST(ForecastRev) AS DASHED", overlays);
        Assert.Contains("CONFIDENCE_LOW = LowBound", overlays);
        Assert.Contains("CONFIDENCE_HIGH = HighBound", overlays);
        Assert.Contains("ANOMALY = AnomalyVal", overlays);
        Assert.Contains("COLOR = '#2563eb'", overlays);
        Assert.Contains("LABEL = 'Forecast'", overlays);
    }

    [Fact]
    public void SvgRenderer_PaintOrder_ConfidenceBandBeforeForecastLineBeforeAnomalyMarkers()
    {
        const string sql = """
            CREATE VISUAL ForecastVisual AS LINE (
              SOURCE = #data,
              MAPPINGS (
                X = Period,
                Y = ActualRevenue
              ),
              OVERLAYS (
                FORECAST(ProjectedRevenue) AS DASHED WITH (
                  CONFIDENCE_LOW = LowRev,
                  CONFIDENCE_HIGH = HighRev,
                  ANOMALY = AnomalyVal,
                  COLOR = '#2563eb',
                  LABEL = 'Model Output'
                )
              )
            );
            """;

        var manifest = new VisualManifest
        {
            Name = "ForecastVisual",
            Columns = ["Period", "ActualRevenue", "ProjectedRevenue", "LowRev", "HighRev", "AnomalyVal"],
            Rows = [
                ["Jan", "50", null, null, null, null],
                ["Feb", "60", "60", "40", "80", null],
                ["Mar", null, "75", "50", "100", "75"]
            ]
        };

        var plan = ResolveNamed(sql, manifest);
        var svg = new SvgChartRenderer().Render(plan);

        var bandIndex = svg.IndexOf("class='plot-confidence-band'", StringComparison.Ordinal);
        var lineIndex = svg.IndexOf("class='plot-forecast-line'", StringComparison.Ordinal);
        var anomalyIndex = svg.IndexOf("plot-anomaly-marker", StringComparison.Ordinal);

        Assert.True(bandIndex >= 0, "Confidence band should be present in SVG");
        Assert.True(lineIndex >= 0, "Forecast line should be present in SVG");
        Assert.True(anomalyIndex >= 0, "Anomaly marker should be present in SVG");

        Assert.True(bandIndex < lineIndex, $"Confidence band (pos {bandIndex}) must paint before forecast line (pos {lineIndex})");
        Assert.True(lineIndex < anomalyIndex, $"Forecast line (pos {lineIndex}) must paint before anomaly marker (pos {anomalyIndex})");
    }

    private static PlotPlan ResolveNamed(string sql, VisualManifest manifest)
    {
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        var spec = new NamedVisualChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        return new PlotPlanResolver().Resolve(spec, new VisualChartDataBuilder().Build(spec, manifest));
    }

    private static PlotPlan ResolveCustom(string sql, VisualManifest manifest)
    {
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        return new PlotPlanResolver().Resolve(spec, new VisualChartDataBuilder().Build(spec, manifest));
    }
}
