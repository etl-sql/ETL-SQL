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

public sealed class TransposedAspectRuleJitterTests
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
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void RuleJitter_PreservesSpanAndValuesThroughFacetsAndResize(bool xRule, bool field, bool facets)
    {
        var sql = Script;
        if (xRule) sql = sql.Replace("Y = DATUM(5) (TYPE = QUANTITATIVE, SCALE = estimates)", "X = DATUM(5) (TYPE = QUANTITATIVE, SCALE = distances)", StringComparison.Ordinal);
        if (field) sql = sql.Replace("DATUM(5)", xRule ? "Distance" : "Estimate", StringComparison.Ordinal);
        if (facets) sql = sql.Replace("SCALES (", "FACET (WRAP = Cohort, COLUMNS = 2), RESOLVE (Y = INDEPENDENT), SCALES (", StringComparison.Ordinal)
            .Replace("LINEAR (CHANNEL", "LOGARITHMIC (CHANNEL", StringComparison.Ordinal)
            .Replace("MIN = 0,", "INCLUDE_ZERO = OFF, REVERSE = ON, MIN = 1,", StringComparison.Ordinal);
        var x = xRule ? "0.02" : "0";
        var y = xRule ? "0" : "0.03";
        sql = sql.Replace("Z_INDEX = 1,", $"Z_INDEX = 1, POSITION = JITTER(X = {x}, Y = {y}, KEY = Id, SEED = 42),", StringComparison.Ordinal);
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
                    int[] selectedRows = field ? (facets ? [0, 1, 2, 3, 4, 5] : [0, 1, 2]) : (facets ? [0, 3] : [0]);
                    var datum = plan.Layers[1].Data.Single(datum => datum.RowIndex == selectedRows[i]);
                    var viewport = facets ? Assert.Single(plan.Facets, panel => panel.RowIndices.Contains(datum.RowIndex)).CartesianViewport! : plan.CartesianViewport!;
                    var dx = datum.DisplayOffsetX;
                    var dy = datum.DisplayOffsetY;
                    Assert.Equal(0m, xRule ? dx : dy);
                    Assert.NotEqual(0m, xRule ? dy : dx);
                    Assert.InRange(Math.Abs(xRule ? dy : dx), 0m, xRule ? .02m * (viewport.Height - 100m) : .03m * (viewport.Width - 80m));
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
    [InlineData("JITTER(X = 0.1, Y = 0, KEY = Id, SEED = 3)")]
    public void UnsupportedPlacements_HavePositionedDiagnostics(string position)
    {
        var statement = Parse(Script.Replace("Z_INDEX = 1,", $"Z_INDEX = 1, POSITION = {position},", StringComparison.Ordinal));
        Assert.Contains(AdvancedChartSemanticValidator.Validate(statement), diagnostic => diagnostic.Line > 0 && diagnostic.Column > 0 && diagnostic.Message.Contains("ASPECT_RATIO RULE", StringComparison.Ordinal));
    }

    [Fact]
    public void DirectContract_RejectsUnsupportedDisplacements()
    {
        var (spec, _) = Lower(Script);
        spec = spec with { Layers = [spec.Layers[0], spec.Layers[1] with { Position = new PositionAdjustmentSpec(PositionAdjustmentKind.Jitter, .1m, 0m, StableKeyField: "Id") }] };
        Assert.Contains("ASPECT_RATIO RULE", Assert.Throws<InvalidDataException>(spec.Validate).Message);
    }

    [Fact]
    public async Task AuthoringAndExport_PreserveRuleDisplacement()
    {
        var sql = JitterScript;
        var tracker = new LineageTracker(ETL_SQL.Common.NullLogger.Instance);
        new ETL_SQL.Analysis.Lineage.LineageAnalyzer(tracker).Analyze(new Parser(new Lexer(sql).Tokenize(), sql).Parse());
        Assert.Contains(tracker.GetFullLineage(), entry => entry.TargetTable == "report:Measurement" && entry.SourceColumns.Contains("Id"));
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
        var (spec, data) = Lower(JitterScript);
        var plan = new PlotPlanResolver().Resolve(spec, data);
        static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Replace("\r\n", "\n", StringComparison.Ordinal))));
        var planHash = Hash(ChartContractSerializer.Serialize(plan));
        var svgHash = Hash(new SvgChartRenderer().Render(plan));
        Assert.True(planHash == "898AA1158CB5682F9640079F514EDF910C298BBFDFE39BB534E7C3D64EDEB369" && svgHash == "4BEE87DE35329BE98B7A1F2479FC28AD9F84B59202332A688D626027D0A2AE64", $"Plan: {planHash}; SVG: {svgHash}");
    }

    [Fact]
    public void DuplicateThresholds_KeepFirstRowSelectionWhileOffsetsFollowStableKeys()
    {
        var (spec, data) = Lower(JitterScript.Replace("DATUM(5)", "Estimate", StringComparison.Ordinal));
        var resolver = new PlotPlanResolver();
        var original = resolver.Resolve(spec, data);
        var reordered = data with
        {
            Columns = data.Columns.Select(column => column with
            {
                Values = column.Values.Reverse().ToImmutableArray(),
                DisplayValues = column.DisplayValues.IsDefaultOrEmpty ? column.DisplayValues : column.DisplayValues.Reverse().ToImmutableArray()
            }).ToImmutableArray()
        };
        var renamed = spec with { Layers = [spec.Layers[0], spec.Layers[1] with { Id = "renamed" }] };
        var reversedRows = resolver.Resolve(renamed, reordered);
        Assert.Equal(Offsets(original, data), Offsets(reversedRows, reordered));
        var reseeded = spec with { Layers = [spec.Layers[0], spec.Layers[1] with { Position = spec.Layers[1].Position! with { Seed = 43 } }] };
        Assert.NotEqual(Offsets(original, data), Offsets(resolver.Resolve(reseeded, data), data));
        var reverseScales = spec with { Scales = spec.Scales.Select(scale => scale with { Reverse = true }).ToImmutableArray() };
        Assert.Equal(Offsets(original, data), Offsets(resolver.Resolve(reverseScales, data), data));
        foreach (var plan in new[] { original, reversedRows })
        {
            var rules = Elements(XDocument.Parse(new SvgChartRenderer().Render(plan)), "plot-reference-rule");
            var unshifted = plan with
            {
                Layers = plan.Layers.Select(layer => layer with
                {
                    Data = layer.Data.Select(datum => datum with { DisplayOffsetX = 0m, DisplayOffsetY = 0m }).ToImmutableArray()
                }).ToImmutableArray()
            };
            var baseline = Elements(XDocument.Parse(new SvgChartRenderer().Render(unshifted)), "plot-reference-rule");
            Assert.Equal(3, rules.Length);
            for (var i = 0; i < rules.Length; i++)
            {
                // The first three rows own the three distinct thresholds in either source order.
                var first = plan.Layers[1].Data[i];
                Assert.Contains($"Y = {PlotPlanResolver.Display(first.Channels.Single(channel => channel.Channel == FieldChannel.Y).Value)}", rules[i].Value);
                Assert.InRange(Read(rules[i], "x1") - Read(baseline[i], "x1") - first.DisplayOffsetX, -.002m, .002m);
                Assert.NotEqual(first.DisplayOffsetX, plan.Layers[1].Data[i + 3].DisplayOffsetX);
            }
        }
    }

    [Fact]
    public void Resize_RescalesSameJitterAndNullThresholdsRemainAbsent()
    {
        var (spec, data) = Lower(JitterScript.Replace("DATUM(5)", "Estimate", StringComparison.Ordinal));
        data = data with
        {
            Columns = data.Columns.Select(column => column.Name == "Estimate" ? column with
            {
                Values = [ChartValue.Null(), ChartValue.From(5m), ChartValue.Null(), ChartValue.From(3m), ChartValue.From(5m), ChartValue.Null()],
                DisplayValues = []
            } : column).ToImmutableArray()
        };
        var resolver = new PlotPlanResolver();
        var original = resolver.Resolve(spec, data, new PlotBounds(0m, 0m, 800m, 600m));
        var resized = resolver.Relayout(spec, data, original, new PlotBounds(0m, 0m, 1000m, 700m));
        var ratio = (resized.CartesianViewport!.Width - 80m) / (original.CartesianViewport!.Width - 80m);
        foreach (var pair in original.Layers[1].Data.Zip(resized.Layers[1].Data))
            Assert.InRange(pair.Second.DisplayOffsetX - pair.First.DisplayOffsetX * ratio, -.000001m, .000001m);
        foreach (var plan in new[] { original, resized })
        {
            Assert.Equal(2, Elements(XDocument.Parse(new SvgChartRenderer().Render(plan)), "plot-reference-rule").Length);
            Assert.Equal(new[] { "5", "3" }, plan.Fallback.Items.Where(item => item.Group == "Reference").Select(item => item.Value));
        }
    }

    [Theory]
    [InlineData("null")]
    [InlineData("duplicate")]
    [InlineData("missing")]
    public void InvalidKeys_FailEvenWhenThresholdsAreAllNull(string problem)
    {
        var (spec, data) = Lower(JitterScript.Replace("DATUM(5)", "Estimate", StringComparison.Ordinal));
        data = data with
        {
            Columns = data.Columns.Select(column => column.Name == "Estimate" ? column with
            {
                Values = Enumerable.Repeat(ChartValue.Null(), 6).ToImmutableArray(),
                DisplayValues = []
            } : column.Name == "Id" ? column with
            {
                Values = column.Values.SetItem(0, problem == "null" ? ChartValue.Null() : column.Values[1]),
                DisplayValues = []
            } : column).ToImmutableArray()
        };
        if (problem == "missing") spec = spec with
        {
            Layers = [spec.Layers[0], spec.Layers[1] with
        {
            Position = spec.Layers[1].Position! with { StableKeyField = "Missing" }
        }]
        };
        Assert.Contains(problem == "missing" ? "does not exist" : problem == "null" ? "nulls" : "duplicate",
            Assert.Throws<InvalidOperationException>(() => new PlotPlanResolver().Resolve(spec, data)).Message);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void InvalidAmplitudes_HaveAuthoringAndContractDiagnostics(double amplitude)
    {
        var sql = JitterScript.Replace("Y = 0.03", $"Y = {amplitude.ToString(CultureInfo.InvariantCulture)}", StringComparison.Ordinal);
        Assert.Contains(AdvancedChartSemanticValidator.Validate(Parse(sql)), diagnostic => diagnostic.Line > 0 && diagnostic.Column > 0 &&
            diagnostic.Message.Contains("amplitudes", StringComparison.Ordinal));
        var (spec, _) = Lower(JitterScript);
        spec = spec with { Layers = [spec.Layers[0], spec.Layers[1] with { Position = spec.Layers[1].Position! with { Y = (decimal)amplitude } }] };
        Assert.Contains("amplitudes", Assert.Throws<InvalidDataException>(spec.Validate).Message);
    }

    private static string JitterScript => Script.Replace("Z_INDEX = 1,", "Z_INDEX = 1, POSITION = JITTER(X = 0, Y = 0.03, KEY = Id, SEED = 42),", StringComparison.Ordinal);

    private static (string Key, decimal X, decimal Y)[] Offsets(PlotPlan plan, ChartDataSet data) => plan.Layers[1].Data.Select(datum =>
        (Key: PlotPlanResolver.Display(data.Columns.Single(column => column.Name == "Id").Values[datum.RowIndex]), X: datum.DisplayOffsetX, Y: datum.DisplayOffsetY))
        .OrderBy(item => item.Key, StringComparer.Ordinal).ToArray();

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
            Columns = ["Distance", "Estimate", "Cohort", "Id"],
            Rows = [["3", "3", "A", "a"], ["5", "5", "A", "b"], ["7", "7", "A", "c"], ["3", "3", "B", "d"], ["5", "5", "B", "e"], ["7", "7", "B", "f"]]
        };
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        return (spec, new VisualChartDataBuilder().Build(spec, manifest));
    }
}
