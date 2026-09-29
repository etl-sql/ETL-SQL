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

public sealed class TransposedAspectRangeRuleTests
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
                ENCODINGS (X = Distance (TYPE = QUANTITATIVE, SCALE = distances), Y_START = LowerBound (TYPE = QUANTITATIVE, SCALE = estimates), Y_END = UpperBound (TYPE = QUANTITATIVE, SCALE = estimates)),
                STYLE (LABEL = '<target>', COLOR = '#112233')
              )
            )
          )
        );
        """;

    [Theory]
    [InlineData(false, false, false, "IDENTITY")]
    [InlineData(false, false, false, "EM")]
    [InlineData(false, false, false, "BAND")]
    [InlineData(false, false, false, "JITTER")]
    [InlineData(true, false, false, "IDENTITY")]
    [InlineData(true, false, false, "EM")]
    [InlineData(true, false, false, "BAND")]
    [InlineData(true, false, false, "JITTER")]
    [InlineData(false, true, false, "IDENTITY")]
    [InlineData(false, true, false, "EM")]
    [InlineData(false, true, false, "BAND")]
    [InlineData(false, true, false, "JITTER")]
    [InlineData(true, true, false, "IDENTITY")]
    [InlineData(true, true, false, "EM")]
    [InlineData(true, true, false, "BAND")]
    [InlineData(true, true, false, "JITTER")]
    [InlineData(false, false, true, "IDENTITY")]
    [InlineData(false, false, true, "EM")]
    [InlineData(false, false, true, "BAND")]
    [InlineData(false, false, true, "JITTER")]
    [InlineData(true, false, true, "IDENTITY")]
    [InlineData(true, false, true, "EM")]
    [InlineData(true, false, true, "BAND")]
    [InlineData(true, false, true, "JITTER")]
    [InlineData(false, true, true, "IDENTITY")]
    [InlineData(false, true, true, "EM")]
    [InlineData(false, true, true, "BAND")]
    [InlineData(false, true, true, "JITTER")]
    [InlineData(true, true, true, "IDENTITY")]
    [InlineData(true, true, true, "EM")]
    [InlineData(true, true, true, "BAND")]
    [InlineData(true, true, true, "JITTER")]
    public void Segments_MapBothEndpointsThroughFacetsAndResize(bool xRange, bool reverseLog, bool facets, string unit)
    {
        var sql = WithPosition(unit);
        if (xRange) sql = sql.Replace("X = Distance (TYPE = QUANTITATIVE, SCALE = distances), Y_START = LowerBound (TYPE = QUANTITATIVE, SCALE = estimates), Y_END = UpperBound (TYPE = QUANTITATIVE, SCALE = estimates)",
            "Y = Estimate (TYPE = QUANTITATIVE, SCALE = estimates), X_START = LowerBound (TYPE = QUANTITATIVE, SCALE = distances), X_END = UpperBound (TYPE = QUANTITATIVE, SCALE = distances)", StringComparison.Ordinal);
        if (reverseLog) sql = sql.Replace("LINEAR (CHANNEL", "LOGARITHMIC (CHANNEL", StringComparison.Ordinal).Replace("MIN = 0,", "INCLUDE_ZERO = OFF, REVERSE = ON, MIN = 1,", StringComparison.Ordinal);
        if (facets) sql = sql.Replace("SCALES (", "FACET (WRAP = Cohort, COLUMNS = 2), RESOLVE (Y = INDEPENDENT), SCALES (", StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        var resolver = new PlotPlanResolver();
        var original = resolver.Resolve(spec, data, new PlotBounds(0m, 0m, 1000m, 650m));
        foreach (var bounds in new[] { original.Bounds, new PlotBounds(0m, 0m, 800m, 600m) })
        {
            var plan = resolver.Relayout(spec, data, original, bounds);
            var identitySpec = spec with { Layers = spec.Layers.Select(layer => layer with { Position = null }).ToImmutableArray() };
            var identity = resolver.Resolve(identitySpec, data, bounds);
            Assert.Equal(ChartContractSerializer.Serialize(plan), ChartContractSerializer.Serialize(plan with { Scales = identity.Scales, Facets = identity.Facets }));
            Assert.Equal(ChartContractSerializer.Serialize(plan), ChartContractSerializer.Serialize(plan with { Fallback = identity.Fallback }));
            Assert.Equal(TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(identity), 140).NormalizedText,
                TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 140).NormalizedText);
            var identitySvg = XDocument.Parse(new SvgChartRenderer().Render(identity));
            var svg = XDocument.Parse(new SvgChartRenderer().Render(plan));
            var baselineRules = Elements(identitySvg, "plot-range-rule");
            var shiftedRules = Elements(svg, "plot-range-rule");
            var labels = Elements(svg, "plot-reference-rule-label");
            var ruleData = plan.Layers.Single(layer => layer.Mark == MarkKind.Rule).Data;
            for (var row = 0; row < shiftedRules.Length; row++)
            {
                var datum = ruleData[row];
                var viewport = plan.Facets.FirstOrDefault(panel => panel.RowIndices.Contains(datum.RowIndex))?.CartesianViewport ?? plan.CartesianViewport!;
                var dx = unit == "JITTER" ? datum.DisplayOffsetX : unit == "EM" ? -0.36m : unit == "BAND" ? -0.03m * (viewport.Width - 80m) : 0m;
                var dy = unit == "JITTER" ? datum.DisplayOffsetY : unit == "EM" ? -0.24m : unit == "BAND" ? -0.02m * (viewport.Height - 100m) : 0m;
                if (unit == "JITTER")
                {
                    Assert.InRange(Math.Abs(dx), 0m, 0.03m * (viewport.Width - 80m));
                    Assert.InRange(Math.Abs(dy), 0m, 0.02m * (viewport.Height - 100m));
                    Assert.NotEqual(0m, dx);
                    Assert.NotEqual(0m, dy);
                }
                Assert.Equal(dx, datum.DisplayOffsetX);
                Assert.Equal(dy, datum.DisplayOffsetY);
                foreach (var endpoint in new[] { "1", "2" })
                {
                    Assert.InRange(Read(shiftedRules[row], "x" + endpoint) - Read(baselineRules[row], "x" + endpoint) - dx, -0.002m, 0.002m);
                    Assert.InRange(Read(shiftedRules[row], "y" + endpoint) - Read(baselineRules[row], "y" + endpoint) - dy, -0.002m, 0.002m);
                }
                Assert.Equal(Read(shiftedRules[row], "x2") + 4m, Read(labels[row], "x"));
                Assert.Equal(Read(shiftedRules[row], "y2") - 4m, Read(labels[row], "y"));
                Assert.Equal(identity.Layers.Single(layer => layer.Mark == MarkKind.Rule).Data[row].Channels.ToArray(), datum.Channels.ToArray());
            }
            var rules = Elements(svg, "plot-range-rule");
            Assert.Equal(3, rules.Length);
            // Render independent point layers at the two semantic endpoints on unchanged scales.
            foreach (var end in new[] { false, true })
            {
                var rangeLayer = Assert.Single(plan.Layers, layer => layer.Mark == MarkKind.Rule);
                var endpoint = xRange ? (end ? FieldChannel.XEnd : FieldChannel.XStart) : (end ? FieldChannel.YEnd : FieldChannel.YStart);
                var mapped = plan with
                {
                    Layers = [rangeLayer with
                {
                    Mark = MarkKind.Point,
                    Data = rangeLayer.Data.Select(datum => datum with { Channels = datum.Channels
                        .Where(channel => channel.Channel is FieldChannel.X or FieldChannel.Y || channel.Channel == endpoint)
                        .Select(channel => channel.Channel == endpoint ? channel with { Channel = xRange ? FieldChannel.X : FieldChannel.Y } : channel).ToImmutableArray() }).ToImmutableArray()
                }]
                };
                var points = Elements(XDocument.Parse(new SvgChartRenderer().Render(mapped)), "plot-point");
                Assert.Equal(3, points.Length);
                for (var i = 0; i < rules.Length; i++)
                {
                    Assert.Equal(Read(points[i], "cx"), Read(rules[i], end ? "x2" : "x1"));
                    Assert.Equal(Read(points[i], "cy"), Read(rules[i], end ? "y2" : "y1"));
                }
            }
            Assert.Equal(3, Elements(svg, "plot-reference-rule-label").Length);
            Assert.All(Elements(svg, "plot-reference-rule-label"), label => Assert.Equal("<target>", label.Value));
            var references = plan.Fallback.Items.Where(item => item.Group == "Reference").ToArray();
            Assert.Equal(3, references.Length);
            var terminal = TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 140).NormalizedText;
            foreach (var pair in rules.Zip(references))
            {
                Assert.Contains(pair.Second.Value, pair.First.Value);
                Assert.Contains(pair.Second.Value, terminal);
            }
            Assert.Contains(xRange ? "X = 1 to 4" : "Y = 1 to 4", references[0].Value);
            Assert.Contains(xRange ? "X = 7 to 2" : "Y = 7 to 2", references[1].Value);
            Assert.Equal(ChartContractSerializer.Serialize(resolver.Resolve(spec, data, bounds)), ChartContractSerializer.Serialize(plan));
        }
    }

    [Theory]
    [InlineData(false, "IDENTITY")]
    [InlineData(false, "EM")]
    [InlineData(false, "BAND")]
    [InlineData(false, "JITTER")]
    [InlineData(false, "DATA")]
    [InlineData(true, "IDENTITY")]
    [InlineData(true, "EM")]
    [InlineData(true, "BAND")]
    [InlineData(true, "JITTER")]
    [InlineData(true, "DATA")]
    public void MissingEndpoint_SkipsSegmentAndFallback(bool allMissing, string unit)
    {
        var (spec, data) = Lower(WithPosition(unit));
        data = data with
        {
            Columns = data.Columns.Select(column => column.Name == "LowerBound"
            ? column with { Values = allMissing ? Enumerable.Repeat(ChartValue.Null(), 3).ToImmutableArray() : column.Values.SetItem(1, ChartValue.Null()), DisplayValues = [] } : column).ToImmutableArray()
        };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Equal(allMissing ? 0 : 2, Elements(XDocument.Parse(new SvgChartRenderer().Render(plan)), "plot-range-rule").Length);
        Assert.Equal(allMissing ? 0 : 2, plan.Fallback.Items.Count(item => item.Group == "Reference"));
    }

    [Fact]
    public void Endpoints_ExpandGlobalAndIndependentFacetDomains()
    {
        var sql = Script.Replace(", MIN = 0, MAX = 10", "", StringComparison.Ordinal)
            .Replace("SCALES (", "FACET (WRAP = Cohort, COLUMNS = 2), RESOLVE (Y = INDEPENDENT), SCALES (", StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.True(PlotPlanResolver.Number(plan.Scales.Single(scale => scale.Channel == FieldChannel.Y).Domain[^1]) >= 9m);
        Assert.True(PlotPlanResolver.Number(plan.Facets[0].Scales.Single(scale => scale.Channel == FieldChannel.Y).Domain[^1]) >= 7m);
        Assert.True(PlotPlanResolver.Number(plan.Facets[1].Scales.Single(scale => scale.Channel == FieldChannel.Y).Domain[^1]) >= 9m);
    }

    [Theory]
    [InlineData("STYLE (LABEL", "CONDITIONS (COLOR WHEN Estimate > 0 THEN '#112233'), STYLE (LABEL")]
    [InlineData("X = Distance (TYPE = QUANTITATIVE, SCALE = distances), Y_START", "X = Distance (TYPE = QUANTITATIVE, SCALE = distances), X_START = Distance (TYPE = QUANTITATIVE, SCALE = distances), X_END = Estimate (TYPE = QUANTITATIVE, SCALE = distances), Y_START")]
    public void UnsupportedSegments_HavePositionedDiagnostics(string before, string after)
    {
        var statement = Parse(Script.Replace(before, after, StringComparison.Ordinal));
        Assert.Contains(AdvancedChartSemanticValidator.Validate(statement), diagnostic => diagnostic.Line > 0 && diagnostic.Column > 0 && diagnostic.Message.Contains("ASPECT_RATIO RULE", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("IDENTITY")]
    [InlineData("EM")]
    [InlineData("BAND")]
    [InlineData("JITTER")]
    [InlineData("DATA")]
    public async Task AuthoringContractsAndPdf_PreserveSegments(string unit)
    {
        var sql = WithPosition(unit);
        var statement = Parse(sql);
        Assert.Equal(statement.ToSql(), Parse(statement.ToSql()).ToSql());
        var source = sql + "CREATE PAGE Dashboard AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = Measurement)));";
        var parsed = new DesignerAnalysisService().Parse(source, 100);
        Assert.Null(parsed.Error);
        Assert.Equal(statement.ToSql(), Parse(new DesignerScriptPatcher().Patch(source, parsed.DesignState)).ToSql());
        var tracker = new LineageTracker(ETL_SQL.Common.NullLogger.Instance);
        new ETL_SQL.Analysis.Lineage.LineageAnalyzer(tracker).Analyze(new Parser(new Lexer(sql).Tokenize(), sql).Parse());
        Assert.Contains(tracker.GetFullLineage(), entry => entry.TargetTable == "report:Measurement" && entry.SourceColumns.Contains("LowerBound"));
        var (spec, data) = Lower(sql);
        Assert.Equal(ChartContractSerializer.Serialize(spec), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializeChartSpec(ChartContractSerializer.Serialize(spec))));
        var invalid = spec with { Layers = [spec.Layers[1] with { Position = new PositionAdjustmentSpec(PositionAdjustmentKind.Jitter, 2m, 0.1m, StableKeyField: "Distance") }] };
        Assert.Contains("amplitudes", Assert.Throws<InvalidDataException>(invalid.Validate).Message);
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

    [Theory]
    [InlineData("IDENTITY", "D35C92425116786F840DD2AC23211A282A200A9300E66CFB89036E191C5D71B1", "35133EEE9F906BF776960079620F1E4978B3FD25D0B5C3F368B8C189E371B3A5")]
    [InlineData("EM", "91EA8FEA70C24E64B7C0442E55EDFF5FF7A0E321F2D4DF0A59E4F67C142067A7", "1CA1C885FF5E86CF784F7F0C517ADE7497BD24E3248BB0F5D15B9FC5172E5DEC")]
    [InlineData("BAND", "6034A4E7D5CBD8324699C6CBB8B74AE25B65FEC8D9D38E6A76537D5B0FA22EE8", "99382418DA40B6BB9AE8908C97BCCB1C34595509CE0A7DA7DBF9EB93FD324313")]
    [InlineData("DATA", "C5AEAB6D1CC25D3E35B318BEA9DED1F3775003A5EC430416F8A6B0F5C6FE74BD", "6957C1CAE103AAD1BABC48347A227EE67E246673B81CDB824DF1BEBF7A78AC38")]
    [InlineData("JITTER", "F0E4B8B922AE7415BB4B1859AB356B54770DBF634075B12135D3672BFEB07E8E", "1C1AB8558E37E2F3F104EFD6515B7DCCA28A3B91D112B282DD27CDE7BEE8CC0E")]
    public void PlanAndSvg_MatchDeterministicGoldens(string unit, string expectedPlan, string expectedSvg)
    {
        var (spec, data) = Lower(WithPosition(unit));
        var plan = new PlotPlanResolver().Resolve(spec, data);
        static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Replace("\r\n", "\n", StringComparison.Ordinal))));
        var planHash = Hash(ChartContractSerializer.Serialize(plan));
        var svgHash = Hash(new SvgChartRenderer().Render(plan));
        Assert.True(planHash == expectedPlan && svgHash == expectedSvg, $"Unit: {unit}; Plan: {planHash}; SVG: {svgHash}");
    }

    [Theory]
    [InlineData("IDENTITY")]
    [InlineData("EM")]
    [InlineData("BAND")]
    [InlineData("JITTER")]
    [InlineData("DATA")]
    public void ConstantEndpoints_PreserveOneSegmentPerSourceRow(string unit)
    {
        var sql = WithPosition(unit).Replace("LowerBound (", "DATUM(2) (", StringComparison.Ordinal).Replace("UpperBound (", "DATUM(8) (", StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        spec = spec with { Layers = [spec.Layers[1]] };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Equal(3, Elements(XDocument.Parse(new SvgChartRenderer().Render(plan)), "plot-range-rule").Length);
        Assert.Equal(3, plan.Fallback.Items.Length);
        Assert.All(plan.Fallback.Items, item => Assert.Contains("Y = 2 to 8", item.Value));
    }

    private static string WithPosition(string unit) => unit == "IDENTITY" ? Script : unit == "JITTER" ? Script.Replace(
        "Z_INDEX = 1,", "Z_INDEX = 1, POSITION = JITTER(X = 0.02, Y = 0.03, KEY = Distance, SEED = 42),", StringComparison.Ordinal) : Script.Replace(
        "Z_INDEX = 1,", $"Z_INDEX = 1, POSITION = NUDGE(X = 0.02, Y = -0.03, UNIT = {unit}),", StringComparison.Ordinal);

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
            Columns = ["Distance", "Estimate", "Cohort", "LowerBound", "UpperBound"],
            Rows = [["3", "3", "A", "1", "4"], ["5", "5", "A", "7", "2"], ["7", "7", "B", "6", "9"]]
        };
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        return (spec, new VisualChartDataBuilder().Build(spec, manifest));
    }
}
