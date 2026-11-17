using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Authoring;
using ETL_SQL.Reporting.Builders;
using ETL_SQL.Reporting.Contracts;
using ETL_SQL.Reporting.Renderers;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Reporting.Semantics.Runtime;
using ETL_SQL.Tests.Reporting.TerminalSemantics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Reporting.AdvancedAuthoring;

public sealed class AnalyticalOverlayTests
{
    [Fact]
    public void Parser_RoundTripsReferenceBandAndTableCalculations()
    {
        const string sql = """
            CREATE VISUAL Analysis AS LINE (
              SOURCE = #data,
              MAPPINGS (X = Period, Y = Amount),
              OVERLAYS (
                REFERENCE_BAND (HIGH = 80, LABEL = 'Expected range', LOW = -10, COLOR = '#94a3b8'),
                RUNNING_TOTAL(CumulativeAmount) AS SOLID WITH (COLOR = '#2563eb', LABEL = 'Cumulative'),
                PERCENT_OF_TOTAL(AmountShare) AS DOTTED WITH (COLOR = '#dc2626', LABEL = 'Share')
              )
            );
            """;

        var statement = ParseVisual(sql);
        Assert.Collection(statement.Overlays,
            band =>
            {
                Assert.Equal(OverlayType.ReferenceBand, band.OverlayType);
                Assert.Equal(-10d, band.BandLow);
                Assert.Equal(80d, band.BandHigh);
            },
            running => Assert.Equal(OverlayType.RunningTotal, running.OverlayType),
            percent => Assert.Equal(OverlayType.PercentOfTotal, percent.OverlayType));

        var canonical = statement.ToSql();
        Assert.Contains("REFERENCE_BAND (LOW = -10, HIGH = 80, COLOR = '#94a3b8', LABEL = 'Expected range')", canonical);
        Assert.Contains("RUNNING_TOTAL(CumulativeAmount) AS SOLID WITH (COLOR = '#2563eb', LABEL = 'Cumulative')", canonical);
        Assert.Contains("PERCENT_OF_TOTAL(AmountShare) AS DOTTED WITH (COLOR = '#dc2626', LABEL = 'Share')", canonical);
        Assert.Empty(new Parser(new Lexer(canonical).Tokenize(), canonical).Parse().Diagnostics);
    }

    [Theory]
    [InlineData("REFERENCE_BAND (LOW = 10)", "requires both LOW and HIGH")]
    [InlineData("REFERENCE_BAND (LOW = 10, LOW = 20, HIGH = 30)", "Duplicate LOW")]
    [InlineData("REFERENCE_BAND (LOW = 10 HIGH = 30)", "Expected ',' or ')'")]
    [InlineData("REFERENCE_BAND (LOW = 10, HIGH = 30,)", "Trailing comma")]
    [InlineData("REFERENCE_BAND (LOW = 10, HIGH = 30, OPACITY = 0.2)", "Unknown property")]
    public void Parser_RejectsMalformedReferenceBand(string overlay, string expected)
    {
        var sql = $"CREATE VISUAL V AS LINE (SOURCE = #data, MAPPINGS (X = Period, Y = Amount), OVERLAYS ({overlay}));";
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        var diagnostic = Assert.Single(script.Diagnostics);
        Assert.Contains(expected, diagnostic.Message);
        Assert.True(diagnostic.Line > 0);
        Assert.True(diagnostic.Column > 0);
    }

    [Fact]
    public void Lowerer_ValidatesReferenceBandBoundsAndSupportedVisuals()
    {
        var manifest = Manifest("V", ["Period", "Amount"], [["A", "10"]]);
        var reversed = ParseVisual("CREATE VISUAL V AS LINE (SOURCE = #data, MAPPINGS (X = Period, Y = Amount), OVERLAYS (REFERENCE_BAND (LOW = 20, HIGH = 10))); ");
        var lowerer = new NamedVisualChartLowerer(new SystemExecutionContext());
        var boundsError = Assert.Throws<InvalidOperationException>(() => lowerer.Lower(reversed, manifest));
        Assert.Contains("LOW to be less than HIGH", boundsError.Message);

        var pie = ParseVisual("CREATE VISUAL V AS PIE (SOURCE = #data, MAPPINGS (LABEL = Period, VALUE = Amount), OVERLAYS (REFERENCE_BAND (LOW = 10, HIGH = 20))); ");
        var typeError = Assert.Throws<InvalidOperationException>(() => lowerer.Lower(pie, manifest));
        Assert.Contains("supported only on Cartesian charts", typeError.Message);
    }

    [Fact]
    public void ReferenceBand_ExpandsDomainAndRendersHorizontalBand()
    {
        const string sql = """
            CREATE VISUAL V AS LINE (
              SOURCE = #data,
              MAPPINGS (X = Period, Y = Amount),
              OVERLAYS (REFERENCE_BAND (LOW = -20, HIGH = 80, COLOR = '#64748b', LABEL = 'Operating range'))
            );
            """;
        var plan = Resolve(sql, Manifest("V", ["Period", "Amount"], [["A", "10"], ["B", "20"]]));
        var band = Assert.Single(plan.Layers, layer => LayerStyle(layer, "overlayType") == "ReferenceBand");
        Assert.Equal(MarkKind.Rect, band.Mark);
        var yScale = Assert.Single(plan.Scales, scale => scale.Channel == FieldChannel.Y);
        Assert.True(PlotPlanResolver.Number(yScale.Domain[0]) <= -20m);
        Assert.True(PlotPlanResolver.Number(yScale.Domain[1]) >= 80m);

        var svg = new SvgChartRenderer().Render(plan);
        Assert.Contains("data-overlay-type='ReferenceBand'", svg);
        Assert.Contains("class='plot-reference-band'", svg);
        Assert.Contains("fill='#64748b'", svg);
        Assert.Contains("Operating range", svg);
        Assert.True(svg.IndexOf("class='plot-reference-band'", StringComparison.Ordinal) <
            svg.IndexOf("stroke-width='2'", StringComparison.Ordinal));
    }

    [Fact]
    public void ReferenceBand_RendersVerticallyForHorizontalBar()
    {
        const string sql = """
            CREATE VISUAL V AS HBAR (
              SOURCE = #data,
              MAPPINGS (X = Region, Y = Amount),
              OVERLAYS (REFERENCE_BAND (LOW = 20, HIGH = 40))
            );
            """;
        var plan = Resolve(sql, Manifest("V", ["Region", "Amount"], [["A", "10"], ["B", "50"]]));
        var svg = new SvgChartRenderer().Render(plan);
        var document = System.Xml.Linq.XDocument.Parse(svg);
        var band = Assert.Single(document.Descendants(), element => (string?)element.Attribute("class") == "plot-reference-band");
        var verticalAxis = Assert.Single(document.Descendants(), element =>
            (string?)element.Attribute("class") == "plot-axis-line" &&
            (string?)element.Attribute("x1") == (string?)element.Attribute("x2"));
        var bandTop = decimal.Parse(band.Attribute("y")!.Value, CultureInfo.InvariantCulture);
        var bandHeight = decimal.Parse(band.Attribute("height")!.Value, CultureInfo.InvariantCulture);
        Assert.Equal(decimal.Parse(verticalAxis.Attribute("y1")!.Value, CultureInfo.InvariantCulture), bandTop);
        Assert.Equal(decimal.Parse(verticalAxis.Attribute("y2")!.Value, CultureInfo.InvariantCulture), bandTop + bandHeight);
        Assert.True(decimal.Parse(band.Attribute("width")!.Value, CultureInfo.InvariantCulture) > 1m);
    }

    [Fact]
    public void Resolver_UsesPrecomputedRunningTotalFieldInSourceOrder()
    {
        const string sql = "CREATE VISUAL V AS LINE (SOURCE = #data, MAPPINGS (X = Period, Y = Amount), OVERLAYS (RUNNING_TOTAL(CumulativeAmount) AS SOLID));";
        var plan = Resolve(sql, Manifest("V", ["Period", "Amount", "CumulativeAmount"],
            [["A", "10", "10"], ["B", "20", "30"], ["C", "-5", "25"]]));
        var overlay = Assert.Single(plan.Layers, layer => LayerStyle(layer, "overlayType") == "RunningTotal");
        Assert.Equal([10m, 30m, 25m], Values(overlay));
        Assert.Contains("data-overlay-type='RunningTotal'", new SvgChartRenderer().Render(plan));
    }

    [Fact]
    public void Resolver_UsesPrecomputedPercentOfTotalField()
    {
        const string sql = "CREATE VISUAL V AS BAR (SOURCE = #data, MAPPINGS (X = Period, Y = Amount), OVERLAYS (PERCENT_OF_TOTAL(AmountShare) AS DASHED));";
        var plan = Resolve(sql, Manifest("V", ["Period", "Amount", "AmountShare"], [["A", "10", "25"], ["B", "30", "75"]]));
        var overlay = Assert.Single(plan.Layers, layer => LayerStyle(layer, "overlayType") == "PercentOfTotal");
        Assert.Equal([25m, 75m], Values(overlay));
    }

    [Theory]
    [InlineData("SCATTER", "RUNNING_TOTAL")]
    [InlineData("COMBO", "PERCENT_OF_TOTAL")]
    public void Lowerer_RejectsTableCalculationsOutsideLineAndBar(string visualType, string overlayType)
    {
        var statement = ParseVisual($"CREATE VISUAL V AS {visualType} (SOURCE = #data, MAPPINGS (X = Period, Y = Amount), OVERLAYS ({overlayType}(CalculatedAmount) AS SOLID));");
        var error = Assert.Throws<InvalidOperationException>(() =>
            new NamedVisualChartLowerer(new SystemExecutionContext()).Lower(statement,
                Manifest("V", ["Period", "Amount"], [["A", "10"]])));
        Assert.Contains("supported only on LINE, BAR, and HORIZONTALBAR", error.Message);
    }

    [Fact]
    public void TableCalculationOverlay_RequiresAVisiblePrecomputedField()
    {
        const string missingArgument = "CREATE VISUAL V AS LINE (SOURCE = #data, MAPPINGS (X = Period, Y = Amount), OVERLAYS (RUNNING_TOTAL AS SOLID));";
        var parseResult = new Parser(new Lexer(missingArgument).Tokenize(), missingArgument).Parse();
        Assert.Contains("Expected '(' after RUNNING_TOTAL", Assert.Single(parseResult.Diagnostics).Message);

        var statement = ParseVisual("CREATE VISUAL V AS LINE (SOURCE = #data, MAPPINGS (X = Period, Y = Amount), OVERLAYS (PERCENT_OF_TOTAL(MissingShare) AS SOLID));");
        var error = Assert.Throws<InvalidOperationException>(() =>
            new NamedVisualChartLowerer(new SystemExecutionContext()).Lower(statement,
                Manifest("V", ["Period", "Amount"], [["A", "10"]])));
        Assert.Contains("was not found in the visual source", error.Message);
    }

    [Fact]
    public void Manifest_PreservesReferenceBandBounds()
    {
        var manifest = new VisualManifest
        {
            Name = "V",
            Overlays = [new OverlayManifest { OverlayType = "ReferenceBand", BandLow = -5d, BandHigh = 15d, TableCalculationField = "CalculatedAmount" }]
        };
        var json = JsonSerializer.Serialize(manifest);
        var copy = JsonSerializer.Deserialize<VisualManifest>(json);
        var overlay = Assert.Single(copy!.Overlays!);
        Assert.Equal(-5d, overlay.BandLow);
        Assert.Equal(15d, overlay.BandHigh);
        Assert.Equal("CalculatedAmount", overlay.TableCalculationField);
    }

    [Fact]
    public async Task VisualBuilder_PreservesAnalyticalOverlayFields()
    {
        const string sql = """
            CREATE VISUAL V AS LINE (
              SOURCE = #data,
              MAPPINGS (X = Period, Y = Amount),
              OVERLAYS (
                REFERENCE_BAND (LOW = -5, HIGH = 15, COLOR = '#94a3b8', LABEL = 'Range'),
                RUNNING_TOTAL(CumulativeAmount) AS SOLID,
                PERCENT_OF_TOTAL(AmountShare) AS DOTTED
              )
            );
            """;
        var statement = ParseVisual(sql);
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        const string prep = "SELECT 'A' AS Period, 10 AS Amount, 10 AS CumulativeAmount, 100 AS AmountShare INTO #data;";
        await evaluator.Evaluate(new Parser(new Lexer(prep).Tokenize(), prep).Parse());

        var manifest = await new VisualBuilder(evaluator, new StyleBuilder(evaluator)).BuildAsync("V", statement);

        Assert.Collection(manifest.Overlays!,
            band =>
            {
                Assert.Equal(-5d, band.BandLow);
                Assert.Equal(15d, band.BandHigh);
            },
            running => Assert.Equal("CumulativeAmount", running.TableCalculationField),
            percent => Assert.Equal("AmountShare", percent.TableCalculationField));
    }

    [Fact]
    public void TerminalRenderer_DescribesReferenceBandWithoutInternalLayerId()
    {
        const string sql = "CREATE VISUAL V AS LINE (SOURCE = #data, MAPPINGS (X = Period, Y = Amount), OVERLAYS (REFERENCE_BAND (LOW = 10, HIGH = 20)));";
        var plan = Resolve(sql, Manifest("V", ["Period", "Amount"], [["A", "15"]]));
        var output = TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 80).NormalizedText;
        Assert.Contains("Reference band", output);
        Assert.Contains("10 to 20", output);
        Assert.DoesNotContain("band-00-referenceband", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DesignerServices_PreserveAnalyticalOverlaySyntax()
    {
        const string sql = """
            CREATE VISUAL V AS LINE (
              SOURCE = #data,
              MAPPINGS (X = Period, Y = Amount),
              OVERLAYS (
                REFERENCE_BAND (LOW = 10, HIGH = 20, COLOR = '#cbd5e1', LABEL = 'Range'),
                RUNNING_TOTAL(CumulativeAmount) AS SOLID,
                PERCENT_OF_TOTAL(AmountShare) AS DOTTED
              )
            );
            """;
        var parsing = new DesignerScriptParsingService();
        var generated = new DesignerScriptGenerationService().Generate(parsing.Parse(sql));
        var visual = parsing.Parse(generated).Pages.SelectMany(page => page.Visuals).Single(item => item.Name == "V");
        var overlays = visual.Options["overlays"];
        Assert.Contains("REFERENCE_BAND (LOW = 10, HIGH = 20, COLOR = '#cbd5e1', LABEL = 'Range')", overlays);
        Assert.Contains("RUNNING_TOTAL(CumulativeAmount) AS SOLID", overlays);
        Assert.Contains("PERCENT_OF_TOTAL(AmountShare) AS DOTTED", overlays);
    }

    [Fact]
    public void ExponentialLogarithmicPower_FitLeastSquaresAndRejectInvalidDomains()
    {
        var expRows = new List<List<string?>>
        {
            new() { "1", "2" },
            new() { "2", "4" },
            new() { "3", "8" },
            new() { "4", "16" }
        };
        var expPlan = Resolve(
            "CREATE VISUAL V AS SCATTER (SOURCE = #d, MAPPINGS (X = XVal, Y = YVal), OVERLAYS (EXPONENTIAL AS SOLID));",
            Manifest("V", ["XVal", "YVal"], expRows));
        var expLayer = Assert.Single(expPlan.Layers, l => LayerStyle(l, "overlayType") == "Exponential");
        var expVals = Values(expLayer);
        Assert.Equal(4, expVals.Count);
        Assert.InRange(expVals[0], 1.99m, 2.01m);
        Assert.InRange(expVals[3], 15.99m, 16.01m);

        var logRows = Enumerable.Range(1, 4)
            .Select(x => new List<string?> { x.ToString(CultureInfo.InvariantCulture), (3d + 2d * Math.Log(x)).ToString("G12", CultureInfo.InvariantCulture) })
            .ToList();
        var logPlan = Resolve(
            "CREATE VISUAL V AS SCATTER (SOURCE = #d, MAPPINGS (X = XVal, Y = YVal), OVERLAYS (LOGARITHMIC AS DASHED));",
            Manifest("V", ["XVal", "YVal"], logRows));
        var logLayer = Assert.Single(logPlan.Layers, l => LayerStyle(l, "overlayType") == "Logarithmic");
        var logVals = Values(logLayer);
        Assert.InRange(logVals[0], 2.999m, 3.001m);
        Assert.InRange(logVals[3], 5.77m, 5.78m);

        var powRows = Enumerable.Range(1, 4)
            .Select(x => new List<string?> { x.ToString(CultureInfo.InvariantCulture), (2d * Math.Pow(x, 1.5)).ToString("G12", CultureInfo.InvariantCulture) })
            .ToList();
        var powPlan = Resolve(
            "CREATE VISUAL V AS SCATTER (SOURCE = #d, MAPPINGS (X = XVal, Y = YVal), OVERLAYS (POWER AS DOTTED));",
            Manifest("V", ["XVal", "YVal"], powRows));
        var powLayer = Assert.Single(powPlan.Layers, l => LayerStyle(l, "overlayType") == "Power");
        var powVals = Values(powLayer);
        Assert.InRange(powVals[0], 1.999m, 2.001m);
        Assert.InRange(powVals[3], 15.99m, 16.01m);

        // Domain guards: non-positive Y fails Exponential/Power; non-positive X fails Logarithmic/Power.
        Assert.Throws<InvalidOperationException>(() => Resolve(
            "CREATE VISUAL V AS SCATTER (SOURCE = #d, MAPPINGS (X = XVal, Y = YVal), OVERLAYS (EXPONENTIAL AS SOLID));",
            Manifest("V", ["XVal", "YVal"], [["1", "10"], ["2", "0"]])));
        Assert.Throws<InvalidOperationException>(() => Resolve(
            "CREATE VISUAL V AS SCATTER (SOURCE = #d, MAPPINGS (X = XVal, Y = YVal), OVERLAYS (LOGARITHMIC AS SOLID));",
            Manifest("V", ["XVal", "YVal"], [["0", "10"], ["2", "20"]])));
        Assert.Throws<InvalidOperationException>(() => Resolve(
            "CREATE VISUAL V AS SCATTER (SOURCE = #d, MAPPINGS (X = XVal, Y = YVal), OVERLAYS (POWER AS SOLID));",
            Manifest("V", ["XVal", "YVal"], [["0", "10"], ["2", "20"]])));
        Assert.Throws<InvalidOperationException>(() => Resolve(
            "CREATE VISUAL V AS SCATTER (SOURCE = #d, MAPPINGS (X = XVal, Y = YVal), OVERLAYS (POWER AS SOLID));",
            Manifest("V", ["XVal", "YVal"], [["1", "-5"], ["2", "20"]])));
    }

    [Fact]
    public void StatisticalOverlays_ExcludeNullPairsWithoutZeroCoercion()
    {
        var manifest = Manifest("V", ["XVal", "YVal"],
        [
            ["1", "10"],
            ["2", null],
            ["3", "30"],
            ["4", "50"]
        ]);
        const string sql = """
            CREATE VISUAL V AS SCATTER (
              SOURCE = #d,
              MAPPINGS (X = XVal, Y = YVal),
              OVERLAYS (
                AVERAGE AS DOTTED,
                MOVING_AVG(2) AS SOLID,
                LINEAR AS DASHED
              )
            );
            """;
        var plan = Resolve(sql, manifest);

        var avgLayer = Assert.Single(plan.Layers, l => LayerStyle(l, "overlayType") == "Average");
        Assert.All(Values(avgLayer), v => Assert.Equal(30m, v));

        var maLayer = Assert.Single(plan.Layers, l => LayerStyle(l, "overlayType") == "MovingAvg");
        Assert.Null(NullableValues(maLayer)[0]);
        Assert.Equal(10m, NullableValues(maLayer)[1]);
        Assert.Equal(30m, NullableValues(maLayer)[2]);
        Assert.Equal(40m, NullableValues(maLayer)[3]);

        // Linear points fitted exclusively through (1,10), (3,30), (4,50): not pulled toward (2, 0).
        var linLayer = Assert.Single(plan.Layers, l => LayerStyle(l, "overlayType") == "Linear");
        var linVals = Values(linLayer);
        Assert.True(linVals[1] > 18m && linVals[1] < 26m, $"Expected interpolated Y near 22, got {linVals[1]}");
    }

    [Fact]
    public void MultiSeriesTrendline_RequiresSeriesOwnershipAndPartitionsBySeries()
    {
        var manifest = Manifest("V", ["XVal", "YVal", "Region"],
        [
            ["1", "10", "North"],
            ["2", "20", "North"],
            ["3", "30", "North"],
            ["1", "100", "South"],
            ["2", "200", "South"],
            ["3", "300", "South"]
        ]);

        // Ambiguous statistical overlay without SERIES on multi-series visual fails validation.
        const string ambiguousSql = """
            CREATE VISUAL V AS LINE (
              SOURCE = #d,
              MAPPINGS (X = XVal, Y = YVal, SERIES = Region),
              OVERLAYS (LINEAR AS DASHED)
            );
            """;
        Assert.Throws<InvalidOperationException>(() => Resolve(ambiguousSql, manifest));

        // Explicit SERIES = 'North' computes regression solely over North rows and round-trips syntax.
        const string ownedSql = """
            CREATE VISUAL V AS LINE (
              SOURCE = #d,
              MAPPINGS (X = XVal, Y = YVal, SERIES = Region),
              OVERLAYS (LINEAR AS DASHED WITH (SERIES = 'North', COLOR = '#ef4444'))
            );
            """;
        var parsed = ParseVisual(ownedSql);
        Assert.Contains("WITH (SERIES = 'North', COLOR = '#ef4444')", parsed.ToSql());
        var plan = Resolve(ownedSql, manifest);
        var trend = Assert.Single(plan.Layers, l => LayerStyle(l, "overlayType") == "Linear");
        Assert.Equal("North", trend.SeriesKey);
        Assert.Equal([10m, 20m, 30m], Values(trend).Select(v => Math.Round(v, 2)).ToList());
    }

    [Fact]
    public void PolynomialRegression_HandlesLargeXWithoutOverflow_AndRejectsUnderdeterminedDegree()
    {
        // Large epoch-ms X values would overflow uncentered decimal sums at degree 2.
        var largeXManifest = Manifest("V", ["Ts", "YVal"],
        [
            ["1700000000000", "10"],
            ["1700100000000", "40"],
            ["1700200000000", "90"],
            ["1700300000000", "160"]
        ]);
        var polyPlan = Resolve(
            "CREATE VISUAL V AS SCATTER (SOURCE = #d, MAPPINGS (X = Ts, Y = YVal), OVERLAYS (POLYNOMIAL(2) AS SOLID));",
            largeXManifest);
        var polyLayer = Assert.Single(polyPlan.Layers, l => LayerStyle(l, "overlayType") == "Polynomial");
        var polyVals = Values(polyLayer);
        Assert.InRange(polyVals[0], 9.9m, 10.1m);
        Assert.InRange(polyVals[3], 159.9m, 160.1m);

        // Degree exceeding distinct X points minus 1 throws explicit InvalidOperationException.
        var fewPoints = Manifest("V", ["XVal", "YVal"], [["1", "10"], ["2", "20"], ["3", "30"]]);
        Assert.Throws<InvalidOperationException>(() => Resolve(
            "CREATE VISUAL V AS SCATTER (SOURCE = #d, MAPPINGS (X = XVal, Y = YVal), OVERLAYS (POLYNOMIAL(3) AS SOLID));",
            fewPoints));
    }

    [Fact]
    public void ResolveScales_PreservesUnstackedYInMixedStackedCharts_AndIncludesAnnotationCoord()
    {
        // Stacked layers sum to 30, while unstacked line FieldChannel.Y reaches 120 on primary Y axis -> max >= 120.
        const string mixedSql = """
            CREATE VISUAL Mixed AS CUSTOM (
              SOURCE = #mixed,
              CHART (
                COORDINATE (TYPE = CARTESIAN),
                SCALES (
                  periods = BAND (CHANNEL = X, ORDER = SOURCE),
                  y = LINEAR (CHANNEL = Y, INCLUDE_ZERO = ON)
                ),
                LAYERS (
                  bar_a = RECT (
                    Z_INDEX = 0,
                    ENCODINGS (
                      X = Period (TYPE = NOMINAL, SCALE = periods),
                      Y = A (TYPE = QUANTITATIVE, SCALE = y, STACK = ZERO)
                    )
                  ),
                  bar_b = RECT (
                    Z_INDEX = 1,
                    ENCODINGS (
                      X = Period (TYPE = NOMINAL, SCALE = periods),
                      Y = B (TYPE = QUANTITATIVE, SCALE = y, STACK = ZERO)
                    )
                  ),
                  unstacked_line = LINE (
                    Z_INDEX = 2,
                    ENCODINGS (
                      X = Period (TYPE = NOMINAL, SCALE = periods),
                      Y = Target (TYPE = QUANTITATIVE, SCALE = y)
                    )
                  )
                )
              )
            );
            """;
        var mixedManifest = Manifest("Mixed", ["Period", "A", "B", "Target"],
            [["Jan", "10", "20", "50"], ["Feb", "15", "15", "120"]]);
        var mixedStatement = ParseVisual(mixedSql);
        var mixedSpec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(mixedStatement, mixedManifest);
        var plan = new PlotPlanResolver().Resolve(mixedSpec, new VisualChartDataBuilder().Build(mixedSpec, mixedManifest));
        var yScale = Assert.Single(plan.Scales, s => s.Channel == FieldChannel.Y);
        Assert.True(PlotPlanResolver.Number(yScale.Domain[1]) >= 120m);

        // Explicit AnnotationPoint COORD widens continuous X and Y scale domains.
        var coordPlan = Resolve(
            """
            CREATE VISUAL V AS SCATTER (
              SOURCE = #d,
              MAPPINGS (X = XVal, Y = YVal),
              ANNOTATIONS (POINT (TYPE = COORD(50, 200), LABEL = 'Stretch'))
            );
            """,
            Manifest("V", ["XVal", "YVal"], [["1", "10"], ["10", "40"]]));
        var coordXScale = Assert.Single(coordPlan.Scales, s => s.Channel == FieldChannel.X);
        var coordYScale = Assert.Single(coordPlan.Scales, s => s.Channel == FieldChannel.Y);
        Assert.True(PlotPlanResolver.Number(coordXScale.Domain[1]) >= 50m);
        Assert.True(PlotPlanResolver.Number(coordYScale.Domain[1]) >= 200m);
    }

    [Fact]
    public void GoldenHarness_FailsClosedWhenVisualHasError()
    {
        var failedManifest = new ReportManifest
        {
            Visuals = [new VisualManifest { Name = "Broken", VisualType = "BAR", Error = "Boom" }]
        };
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Goldens.ReportingGoldenHarness.AssertManifestSucceeded("broken.rptsql", failedManifest));
        Assert.Contains("Broken", ex.Message, StringComparison.Ordinal);
    }

    private static CreateVisualStatement ParseVisual(string sql)
    {
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        return Assert.Single(script.Statements.OfType<CreateVisualStatement>());
    }

    private static VisualManifest Manifest(string name, List<string> columns, List<List<string?>> rows) => new()
    {
        Name = name,
        Columns = columns,
        Rows = rows
    };

    private static PlotPlan Resolve(string sql, VisualManifest manifest)
    {
        var statement = ParseVisual(sql);
        var spec = new NamedVisualChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        return new PlotPlanResolver().Resolve(spec, new VisualChartDataBuilder().Build(spec, manifest));
    }

    private static string? LayerStyle(ResolvedMarkLayer layer, string name) =>
        layer.Style.FirstOrDefault(token => token.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;

    private static List<decimal> Values(ResolvedMarkLayer layer) => layer.Data
        .Select(datum => datum.Channels.First(channel => channel.Channel == FieldChannel.Y).Value)
        .Select(value => PlotPlanResolver.Number(value)!.Value)
        .ToList();

    private static List<decimal?> NullableValues(ResolvedMarkLayer layer) => layer.Data
        .Select(datum => PlotPlanResolver.Number(datum.Channels.First(channel => channel.Channel == FieldChannel.Y).Value))
        .ToList();
}
