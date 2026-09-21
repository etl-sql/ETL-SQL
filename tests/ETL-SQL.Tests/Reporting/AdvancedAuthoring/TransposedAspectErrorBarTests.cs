using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using ETL_SQL.Analysis.Lineage;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using ETL_SQL.Portal.Services;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Renderers;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Reporting.Semantics.Runtime;
using Spectre.Console;

namespace ETL_SQL.Tests.Reporting.AdvancedAuthoring;

public sealed class TransposedAspectErrorBarTests
{
    private const string Script = """
        CREATE VISUAL Measurement AS CUSTOM (
          SOURCE = #prepared,
          CHART (
            COORDINATE (TYPE = TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2),
            SCALES (
              distances = LINEAR (CHANNEL = X, MIN = 0, MAX = 4),
              estimates = LINEAR (CHANNEL = Y, INCLUDE_ZERO = ON)
            ),
            LAYERS (observations = POINT (
              STYLE (ERROR_BAR_STYLE = 'CAPS'),
              ENCODINGS (
                X = Distance (TYPE = QUANTITATIVE, SCALE = distances),
                Y = Estimate (TYPE = QUANTITATIVE, SCALE = estimates),
                ERROR_LOW = LowerBound (TYPE = QUANTITATIVE, SCALE = estimates),
                ERROR_HIGH = UpperBound (TYPE = QUANTITATIVE, SCALE = estimates)
              )
            ))
          )
        );
        """;

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ErrorBars_UseSemanticYScaleOnPhysicalHorizontalAxis(bool logarithmic, bool reverse)
    {
        var sql = Script;
        if (logarithmic)
            sql = sql.Replace("LINEAR (CHANNEL = Y, INCLUDE_ZERO = ON)",
                "LOGARITHMIC (CHANNEL = Y, INCLUDE_ZERO = OFF)", StringComparison.Ordinal);
        if (reverse)
            sql = sql.Replace("CHANNEL = Y,", "CHANNEL = Y, REVERSE = ON,", StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        var resolver = new PlotPlanResolver();
        var original = resolver.Resolve(spec, data);
        var resized = resolver.Relayout(spec, data, original, new PlotBounds(0m, 0m, 320m, 450m));
        foreach (var plan in new[] { original, resized })
        {
            var yScale = plan.Scales.Single(scale => scale.Channel == FieldChannel.Y);
            Assert.True(PlotPlanResolver.Number(yScale.Domain[^1]) >= 40m);
            var before = ChartContractSerializer.Serialize(plan);
            var svg = new SvgChartRenderer().Render(plan);
            Assert.Equal(before, ChartContractSerializer.Serialize(plan));
            Assert.Equal(svg, new SvgChartRenderer().Render(new VisualManifest { PlotPlan = plan }));
            var document = XDocument.Parse(svg);
            var points = Elements(document, "plot-point");
            var stems = Elements(document, "plot-error-bar-stem");
            var caps = Elements(document, "plot-error-bar-cap");
            Assert.Equal(2, points.Length);
            Assert.Equal(2, stems.Length);
            Assert.Equal(4, caps.Length);
            for (var index = 0; index < stems.Length; index++)
            {
                Assert.Equal(Read(points[index], "cy"), Read(stems[index], "y1"));
                Assert.Equal(Read(stems[index], "y1"), Read(stems[index], "y2"));
                Assert.Equal(reverse, Read(stems[index], "x1") > Read(stems[index], "x2"));
                Assert.InRange(Read(points[index], "cx"),
                    Math.Min(Read(stems[index], "x1"), Read(stems[index], "x2")),
                    Math.Max(Read(stems[index], "x1"), Read(stems[index], "x2")));
                Assert.Equal(Read(caps[index * 2], "x1"), Read(caps[index * 2], "x2"));
                Assert.Equal(Read(stems[index], "x1"), Read(caps[index * 2], "x1"));
                Assert.Equal(Read(stems[index], "x2"), Read(caps[index * 2 + 1], "x1"));
            }
            var semanticYSpan = logarithmic ? (decimal)Math.Log10(3d) : 20m;
            var physicalYUnit = Math.Abs(Read(points[1], "cx") - Read(points[0], "cx")) / semanticYSpan;
            var physicalXUnit = Math.Abs(Read(points[1], "cy") - Read(points[0], "cy")) / 2m;
            Assert.InRange(physicalYUnit / physicalXUnit, 1.995m, 2.005m);
            var errorSpan = logarithmic ? (decimal)Math.Log10(3d) : 10m;
            Assert.InRange(Math.Abs(Read(stems[0], "x2") - Read(stems[0], "x1")) / errorSpan / physicalYUnit, .995m, 1.005m);
        }
    }

    [Fact]
    public void MissingEndpoint_DoesNotInventAnInterval_AndNoCapsIsHonored()
    {
        var (spec, data) = Lower(Script.Replace("'CAPS'", "'NO_CAPS'", StringComparison.Ordinal), missingEndpoint: true);
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var svg = XDocument.Parse(new SvgChartRenderer().Render(plan));
        Assert.Single(Elements(svg, "plot-error-bar-stem"));
        Assert.Empty(Elements(svg, "plot-error-bar-cap"));
    }

    [Fact]
    public void Facets_IncludeEndpointsInEachIndependentDomain()
    {
        var sql = Script.Replace("SCALES (", "FACET (WRAP = Distance, COLUMNS = 2), RESOLVE (Y = INDEPENDENT), SCALES (", StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        var plan = new PlotPlanResolver().Resolve(spec, data, new PlotBounds(0m, 0m, 800m, 450m));
        Assert.Equal(2, plan.Facets.Length);
        Assert.Equal(15m, PlotPlanResolver.Number(plan.Facets[0].Scales.Single(scale => scale.Channel == FieldChannel.Y).Domain[^1]));
        Assert.Equal(40m, PlotPlanResolver.Number(plan.Facets[1].Scales.Single(scale => scale.Channel == FieldChannel.Y).Domain[^1]));
        Assert.Equal(2, Elements(XDocument.Parse(new SvgChartRenderer().Render(plan)), "plot-error-bar-stem").Length);
    }

    [Fact]
    public void AuthoringContractsLineageAndFallback_PreserveTheInterval()
    {
        var statement = Parse(Script);
        Assert.Equal(statement.ToSql(), Parse(statement.ToSql()).ToSql());
        var (spec, data) = Lower(Script);
        var specJson = ChartContractSerializer.Serialize(spec);
        Assert.Equal(specJson, ChartContractSerializer.Serialize(ChartContractSerializer.DeserializeChartSpec(specJson)));
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var planJson = ChartContractSerializer.Serialize(plan);
        Assert.Equal(planJson, ChartContractSerializer.Serialize(ChartContractSerializer.DeserializePlotPlan(planJson)));
        Assert.Contains("error 5 to 15", plan.Fallback.Items[0].Detail);
        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer)
        });
        console.Write(PlotPlanTerminalRenderer.Render(plan));
        Assert.Contains("error 5 to 15", writer.ToString());

        var source = Script + "CREATE PAGE Dashboard AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = Measurement)));";
        var parsed = new DesignerAnalysisService().Parse(source, 100);
        Assert.Null(parsed.Error);
        var patched = new DesignerScriptPatcher().Patch(source, parsed.DesignState);
        Assert.Contains("TYPE = TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2", patched);
        Assert.Contains("ERROR_LOW = LowerBound", patched);
        var tracker = new LineageTracker(NullLogger.Instance);
        new LineageAnalyzer(tracker).Analyze(new Parser(new Lexer(Script).Tokenize(), Script).Parse());
        var entries = tracker.GetFullLineage().Where(entry => entry.TargetTable == "report:Measurement");
        Assert.Contains(entries, entry => entry.SourceColumns.Contains("LowerBound"));
        Assert.Contains(entries, entry => entry.SourceColumns.Contains("UpperBound"));
    }

    [Theory]
    [InlineData("ERROR_HIGH = UpperBound (TYPE = QUANTITATIVE, SCALE = estimates)", "COLOR = Distance (TYPE = QUANTITATIVE)", "requires both ERROR_LOW and ERROR_HIGH")]
    [InlineData("ERROR_LOW = LowerBound (TYPE = QUANTITATIVE, SCALE = estimates)", "ERROR_LOW = LowerBound (TYPE = QUANTITATIVE, SCALE = distances)", "same scale as Y")]
    [InlineData("ERROR_LOW = LowerBound (TYPE = QUANTITATIVE, SCALE = estimates)", "ERROR_LOW = LowerBound (TYPE = QUANTITATIVE, SCALE = estimates, AXIS = SECONDARY)", "primary axis")]
    public void InvalidCombinations_KeepPositionedDiagnostics(string oldText, string replacement, string message)
    {
        var statement = Parse(Script.Replace(oldText, replacement, StringComparison.Ordinal));
        Assert.Contains(AdvancedChartSemanticValidator.Validate(statement), diagnostic =>
            diagnostic.Code == "RPT-CHART" && diagnostic.Message.Contains(message, StringComparison.Ordinal));
    }

    [Fact]
    public void ContractBackstop_RejectsAnUnpairedEndpoint()
    {
        var (spec, _) = Lower(Script);
        var layer = spec.Layers[0];
        var invalid = spec with { Layers = [layer with { Bindings = layer.Bindings.Where(binding => binding.Channel != FieldChannel.ErrorHigh).ToImmutableArray() }] };
        Assert.Contains("both ERROR_LOW and ERROR_HIGH", Assert.Throws<InvalidDataException>(invalid.Validate).Message);
    }

    [Fact]
    public void PlanAndSvg_MatchDeterministicGoldens()
    {
        var (spec, data) = Lower(Script);
        var plan = new PlotPlanResolver().Resolve(spec, data);
        static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Replace("\r\n", "\n", StringComparison.Ordinal))));
        var planHash = Hash(ChartContractSerializer.Serialize(plan));
        var svgHash = Hash(new SvgChartRenderer().Render(plan));
        Assert.True(planHash == "73EC509554B97AAEAC8E893BAEA168DD39923EE60B541752723BED22C36C9662" && svgHash == "B8A0B440EB9C202BE3A31BC125475F31F96CAFF832457C1FF0102C064C66E824", $"Plan: {planHash}; SVG: {svgHash}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StaticPdfExport_AcceptsResolvedAndPreRenderedGeometry(bool preRender)
    {
        var (spec, data) = Lower(Script);
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var report = new ReportManifest
        {
            Title = "Measurements",
            Source = "measurements.rptsql",
            Visuals = [new VisualManifest
            {
                Name = "Measurement", VisualType = "CUSTOM", PlotPlan = plan,
                NativeSvg = preRender ? new SvgChartRenderer().Render(plan) : null
            }]
        };
        var pdf = await new PdfExporter().ExportAsync(report);
        Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, pdf[..4]);
    }

    private static XElement[] Elements(XDocument document, string name) =>
        document.Descendants().Where(element => (string?)element.Attribute("class") == name).ToArray();

    private static decimal Read(XElement element, string name) => decimal.Parse(element.Attribute(name)!.Value, CultureInfo.InvariantCulture);

    private static CreateVisualStatement Parse(string sql)
    {
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        return Assert.Single(script.Statements.OfType<CreateVisualStatement>());
    }

    private static (ChartSpec Spec, ChartDataSet Data) Lower(string sql, bool missingEndpoint = false)
    {
        var statement = Parse(sql);
        Assert.Empty(AdvancedChartSemanticValidator.Validate(statement));
        var manifest = new VisualManifest
        {
            Name = "Measurement",
            Columns = ["Distance", "Estimate", "LowerBound", "UpperBound"],
            Rows = [["1", "10", "5", "15"], ["3", "30", missingEndpoint ? null : "20", "40"]]
        };
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        return (spec, new VisualChartDataBuilder().Build(spec, manifest));
    }
}
