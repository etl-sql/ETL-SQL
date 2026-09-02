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
        Assert.Contains("supported only on LINE and BAR", error.Message);
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
}
