using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using ETL_SQL.Portal.Services;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Renderers;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Reporting.Semantics.Runtime;
using ETL_SQL.Tests.Reporting.TerminalSemantics;

namespace ETL_SQL.Tests.Reporting.AdvancedAuthoring;

public sealed class TransposedAspectRuleNudgeTests
{
    private const string Script = """
        CREATE VISUAL Measurement AS CUSTOM (
          SOURCE = #prepared,
          CHART (
            COORDINATE (TYPE = TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2),
            SCALES (
              distances = LINEAR (CHANNEL = X, MIN = 0, MAX = 10),
              estimates = LINEAR (CHANNEL = Y, MIN = 0, MAX = 10)
            ),
            LAYERS (
              observations = POINT (ENCODINGS (
                X = Distance (TYPE = QUANTITATIVE, SCALE = distances),
                Y = Estimate (TYPE = QUANTITATIVE, SCALE = estimates)
              )),
              threshold = RULE (
                Z_INDEX = 1,
                INHERIT_ENCODINGS = OFF,
                ENCODINGS (Y = DATUM(5) (TYPE = QUANTITATIVE, SCALE = estimates)),
                STYLE (LABEL = '<target>', COLOR = '#112233')
              )
            )
          )
        );
        """;

    [Theory]
    [InlineData("EM", false, false, false)]
    [InlineData("EM", true, false, false)]
    [InlineData("EM", false, true, false)]
    [InlineData("EM", true, true, false)]
    [InlineData("EM", false, false, true)]
    [InlineData("EM", true, false, true)]
    [InlineData("EM", false, true, true)]
    [InlineData("EM", true, true, true)]
    [InlineData("BAND", false, false, false)]
    [InlineData("BAND", true, false, false)]
    [InlineData("BAND", false, true, false)]
    [InlineData("BAND", true, true, false)]
    [InlineData("BAND", false, false, true)]
    [InlineData("BAND", true, false, true)]
    [InlineData("BAND", false, true, true)]
    [InlineData("BAND", true, true, true)]
    public void RuleNudge_PreservesSpanAndValuesThroughFacetsAndResize(string unit, bool xRule, bool field, bool facets)
    {
        var sql = Script;
        if (xRule) sql = sql.Replace("Y = DATUM(5) (TYPE = QUANTITATIVE, SCALE = estimates)", "X = DATUM(5) (TYPE = QUANTITATIVE, SCALE = distances)", StringComparison.Ordinal);
        if (field) sql = sql.Replace("DATUM(5)", xRule ? "Distance" : "Estimate", StringComparison.Ordinal);
        if (facets) sql = sql.Replace("SCALES (", "FACET (WRAP = Cohort, COLUMNS = 2), RESOLVE (Y = INDEPENDENT), SCALES (", StringComparison.Ordinal)
            .Replace("LINEAR (CHANNEL", "LOGARITHMIC (CHANNEL", StringComparison.Ordinal)
            .Replace("MIN = 0,", "INCLUDE_ZERO = OFF, REVERSE = ON, MIN = 1,", StringComparison.Ordinal);
        var x = xRule ? "0.02" : "0";
        var y = xRule ? "0" : "-0.03";
        sql = sql.Replace("Z_INDEX = 1,", $"Z_INDEX = 1, POSITION = NUDGE(X = {x}, Y = {y}, UNIT = {unit}),", StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        var baselineSpec = spec with { Layers = [spec.Layers[0], spec.Layers[1] with { Position = null }] };
        var resolver = new PlotPlanResolver();
        var original = resolver.Resolve(spec, data, new PlotBounds(0m, 0m, 1000m, 650m));
        foreach (var bounds in new[] { original.Bounds, new PlotBounds(0m, 0m, 800m, 600m) })
        {
            var plan = resolver.Relayout(spec, data, original, bounds);
            var baseline = resolver.Resolve(baselineSpec, data, bounds);
            Assert.Equal(baseline.CartesianViewport, plan.CartesianViewport);
            Assert.Equal(baseline.Scales.SelectMany(scale => scale.Domain), plan.Scales.SelectMany(scale => scale.Domain));
            Assert.Equal(baseline.Layers[1].Data.SelectMany(datum => datum.Channels), plan.Layers[1].Data.SelectMany(datum => datum.Channels));
            Assert.Equal(baseline.Fallback.Items.ToArray(), plan.Fallback.Items.ToArray());
            var svg = XDocument.Parse(new SvgChartRenderer().Render(plan));
            var baseSvg = XDocument.Parse(new SvgChartRenderer().Render(baseline));
            foreach (var name in new[] { "plot-reference-rule", "plot-reference-rule-label" })
            {
                var actual = Elements(svg, name);
                var expected = Elements(baseSvg, name);
                Assert.Equal((field ? 3 : 1) * (facets ? 2 : 1), actual.Length);
                Assert.Equal(expected.Length, actual.Length);
                for (var i = 0; i < actual.Length; i++)
                {
                    var point = actual[i].Parent!.Descendants().First(element => (string?)element.Attribute("class") == "plot-point");
                    var row = int.Parse(point.Attribute("data-row-index")!.Value, CultureInfo.InvariantCulture);
                    var viewport = facets ? Assert.Single(plan.Facets, panel => panel.RowIndices.Contains(row)).CartesianViewport! : plan.CartesianViewport!;
                    var dx = xRule ? 0m : -.03m * (unit == "EM" ? 12m : viewport.Width - 80m);
                    var dy = xRule ? -.02m * (unit == "EM" ? 12m : viewport.Height - 100m) : 0m;
                    foreach (var attribute in name == "plot-reference-rule" ? new[] { "x1", "y1", "x2", "y2" } : ["x", "y"])
                        Assert.InRange(Read(actual[i], attribute) - Read(expected[i], attribute) - (attribute[0] == 'x' ? dx : dy), -.002m, .002m);
                    Assert.Equal(expected[i].Value, actual[i].Value);
                }
            }
            Assert.Equal(Elements(baseSvg, "plot-point").Select(element => element.ToString()), Elements(svg, "plot-point").Select(element => element.ToString()));
            Assert.Equal(ChartContractSerializer.Serialize(resolver.Resolve(spec, data, bounds)), ChartContractSerializer.Serialize(plan));
            Assert.Equal(TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(baseline), 120).NormalizedText,
                TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 120).NormalizedText);
        }
    }

    [Theory]
    [InlineData("NUDGE(X = 1, Y = 0, UNIT = EM)")]
    [InlineData("NUDGE(X = 1, Y = 0, UNIT = BAND)")]
    [InlineData("NUDGE(X = 1, Y = 0, UNIT = DATA)")]
    [InlineData("JITTER(X = 0.1, Y = 0, KEY = Distance, SEED = 3)")]
    public void UnsupportedPlacements_HavePositionedDiagnostics(string position)
    {
        var statement = Parse(Script.Replace("Z_INDEX = 1,", $"Z_INDEX = 1, POSITION = {position},", StringComparison.Ordinal));
        Assert.Contains(AdvancedChartSemanticValidator.Validate(statement), diagnostic => diagnostic.Line > 0 && diagnostic.Column > 0 && diagnostic.Message.Contains("ASPECT_RATIO RULE", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(PositionAdjustmentUnit.Em, 1, 0)]
    [InlineData(PositionAdjustmentUnit.Band, 1, 0)]
    [InlineData(PositionAdjustmentUnit.Data, 1, 0)]
    public void DirectContract_RejectsUnsupportedDisplacements(PositionAdjustmentUnit unit, int x, int y)
    {
        var (spec, _) = Lower(Script);
        spec = spec with { Layers = [spec.Layers[0], spec.Layers[1] with { Position = new PositionAdjustmentSpec(PositionAdjustmentKind.Nudge, x, y, Unit: unit) }] };
        Assert.Contains("ASPECT_RATIO RULE", Assert.Throws<InvalidDataException>(spec.Validate).Message);
    }

    [Theory]
    [InlineData("EM")]
    [InlineData("BAND")]
    [InlineData("DATA")]
    public async Task AuthoringAndExport_PreserveRuleDisplacement(string unit)
    {
        var sql = Script.Replace("Z_INDEX = 1,", $"Z_INDEX = 1, POSITION = NUDGE(X = 0, Y = 0.03, UNIT = {unit}),", StringComparison.Ordinal);
        var tracker = new LineageTracker(ETL_SQL.Common.NullLogger.Instance);
        new ETL_SQL.Analysis.Lineage.LineageAnalyzer(tracker).Analyze(new Parser(new Lexer(sql).Tokenize(), sql).Parse());
        Assert.Contains(tracker.GetFullLineage(), entry => entry.TargetTable == "report:Measurement" && entry.SourceColumns.Contains("Estimate"));
        var statement = Parse(sql);
        Assert.Equal(statement.ToSql(), Parse(statement.ToSql()).ToSql());
        var source = sql + "CREATE PAGE Dashboard AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = Measurement)));";
        var parsed = new DesignerAnalysisService().Parse(source, 100);
        Assert.Null(parsed.Error);
        Assert.Equal(statement.ToSql(), Parse(new DesignerScriptPatcher().Patch(source, parsed.DesignState)).ToSql());
        var (spec, data) = Lower(sql);
        Assert.Equal(ChartContractSerializer.Serialize(spec), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializeChartSpec(ChartContractSerializer.Serialize(spec))));
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Equal(ChartContractSerializer.Serialize(plan), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializePlotPlan(ChartContractSerializer.Serialize(plan))));
        var report = new ReportManifest
        {
            Title = "Measurement",
            Source = "measurement.rptsql",
            Visuals = [new VisualManifest { Name = "Measurement", VisualType = "CUSTOM", PlotPlan = plan, NativeSvg = new SvgChartRenderer().Render(plan) }]
        };
        Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, (await new PdfExporter().ExportAsync(report))[..4]);
    }

    [Fact]
    public void PlanAndSvg_MatchDeterministicGoldens()
    {
        var (spec, data) = Lower(Script.Replace("Z_INDEX = 1,", "Z_INDEX = 1, POSITION = NUDGE(X = 0, Y = 0.03, UNIT = BAND),", StringComparison.Ordinal));
        var plan = new PlotPlanResolver().Resolve(spec, data);
        static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Replace("\r\n", "\n", StringComparison.Ordinal))));
        var planHash = Hash(ChartContractSerializer.Serialize(plan));
        var svgHash = Hash(new SvgChartRenderer().Render(plan));
        Assert.True(planHash == "03B1701A68B3A3A361EA9F257DE5D0AF1BF9ADDCC37997EB9B002205B9B3C77F" && svgHash == "91CD776A10E08C2B890D9E8CB453974811232B678B85B7F6CF7B6695634E7DE3", $"Plan: {planHash}; SVG: {svgHash}");
    }

    private static XElement[] Elements(XDocument document, string name) => document.Descendants().Where(element => (string?)element.Attribute("class") == name).ToArray();
    private static decimal Read(XElement element, string name) => decimal.Parse(element.Attribute(name)!.Value, CultureInfo.InvariantCulture);
    private static CreateVisualStatement Parse(string sql)
    {
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        return Assert.Single(script.Statements.OfType<CreateVisualStatement>());
    }
    private static (ChartSpec Spec, ChartDataSet Data) Lower(string sql)
    {
        var statement = Parse(sql);
        Assert.Empty(AdvancedChartSemanticValidator.Validate(statement));
        var manifest = new VisualManifest
        {
            Name = "Measurement",
            Columns = ["Distance", "Estimate", "Cohort"],
            Rows = [["3", "3", "A"], ["5", "5", "A"], ["7", "7", "A"], ["3", "3", "B"], ["5", "5", "B"], ["7", "7", "B"]]
        };
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        return (spec, new VisualChartDataBuilder().Build(spec, manifest));
    }
}
