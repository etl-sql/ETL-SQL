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

[Trait("CompatBreak", "0.20")]
public sealed class TransposedAspectRuleDataNudgeTests
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
    [InlineData(false, false, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(false, false, true, true)]
    [InlineData(true, false, true, true)]
    [InlineData(false, true, true, true)]
    [InlineData(true, true, true, true)]
    [InlineData(false, true, true, false)]
    [InlineData(true, true, true, false)]
    public void DataNudge_MatchesShiftedThresholdOnOriginalScales(bool xRule, bool field, bool reverse, bool facets)
    {
        var sql = Script;
        if (xRule) sql = sql.Replace("Y = DATUM(5) (TYPE = QUANTITATIVE, SCALE = estimates)", "X = DATUM(5) (TYPE = QUANTITATIVE, SCALE = distances)", StringComparison.Ordinal);
        if (field) sql = sql.Replace("DATUM(5)", xRule ? "Distance" : "Estimate", StringComparison.Ordinal);
        if (reverse) sql = sql.Replace("CHANNEL = X,", "CHANNEL = X, REVERSE = ON,", StringComparison.Ordinal)
            .Replace("CHANNEL = Y,", "CHANNEL = Y, REVERSE = ON,", StringComparison.Ordinal);
        if (facets) sql = sql.Replace("SCALES (", "FACET (WRAP = Cohort, COLUMNS = 2), RESOLVE (Y = INDEPENDENT), SCALES (", StringComparison.Ordinal)
            .Replace("LINEAR (CHANNEL", "LOGARITHMIC (CHANNEL", StringComparison.Ordinal)
            .Replace("MIN = 0,", "INCLUDE_ZERO = OFF, MIN = 1,", StringComparison.Ordinal);
        sql = sql.Replace("observations = POINT (ENCODINGS (", "observations = POINT (ENCODINGS (COLOR = Cohort (TYPE = NOMINAL),", StringComparison.Ordinal);
        sql = sql.Replace("Z_INDEX = 1,", $"Z_INDEX = 1, POSITION = NUDGE(X = {(xRule ? "-0.5" : "0")}, Y = {(xRule ? "0" : "0.5")}, UNIT = DATA),", StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        spec = spec with { Theme = spec.Theme with { Tokens = [.. spec.Theme.Tokens, new StyleToken("LEGEND_POSITION", "LEFT")] } };
        var baselineSpec = spec with { Layers = [spec.Layers[0], spec.Layers[1] with { Position = null }] };
        var resolver = new PlotPlanResolver();
        var original = resolver.Resolve(spec, data, new PlotBounds(0m, 0m, 1000m, 650m));
        foreach (var bounds in new[] { original.Bounds, new PlotBounds(0m, 0m, 800m, 600m) })
        {
            var plan = resolver.Relayout(spec, data, original, bounds);
            var baseline = resolver.Resolve(baselineSpec, data, bounds);
            Assert.Single(baseline.Layers, layer => layer.Mark == MarkKind.Rule);
            Assert.Equal(2, baseline.Layers.Count(layer => layer.Mark == MarkKind.Point));
            Assert.Equal(field ? 3 : 1, baseline.Fallback.Items.Count(item => item.Group == "Reference"));
            Assert.Equal(baseline.CartesianViewport, plan.CartesianViewport);
            Assert.Equal(baseline.Scales.SelectMany(scale => scale.Domain), plan.Scales.SelectMany(scale => scale.Domain));
            Assert.Equal(baseline.Layers[^1].Data.SelectMany(datum => datum.Channels), plan.Layers[^1].Data.SelectMany(datum => datum.Channels));
            Assert.Equal(baseline.Fallback.Items.ToArray(), plan.Fallback.Items.ToArray());
            // Independent geometry oracle: shift raw thresholds on the same resolved scales, with no offsets.
            var mapped = plan with
            {
                Layers = plan.Layers.Select(layer => layer.Mark != MarkKind.Rule ? layer : layer with
                {
                    Data = layer.Data.Select(datum => datum with
                    {
                        DisplayOffsetX = 0m,
                        DisplayOffsetY = 0m,
                        Channels = datum.Channels.Select(channel => channel.Channel is FieldChannel.X or FieldChannel.Y
                            ? channel with { Value = ChartValue.From(PlotPlanResolver.Number(channel.Value)!.Value + (xRule ? -.5m : .5m)) } : channel).ToImmutableArray()
                    }).ToImmutableArray()
                }).ToImmutableArray()
            };
            var svg = XDocument.Parse(new SvgChartRenderer().Render(plan));
            var oracle = XDocument.Parse(new SvgChartRenderer().Render(mapped));
            foreach (var name in new[] { "plot-reference-rule", "plot-reference-rule-label" })
            {
                var actual = Elements(svg, name);
                var expected = Elements(oracle, name);
                Assert.Equal((field ? 3 : 1) * (facets ? 2 : 1), actual.Length);
                Assert.Equal(expected.Length, actual.Length);
                for (var i = 0; i < actual.Length; i++)
                    foreach (var attribute in name == "plot-reference-rule" ? new[] { "x1", "y1", "x2", "y2" } : ["x", "y"])
                        Assert.InRange(Read(actual[i], attribute) - Read(expected[i], attribute), -.002m, .002m);
            }
            Assert.Equal(ChartContractSerializer.Serialize(resolver.Resolve(spec, data, bounds)), ChartContractSerializer.Serialize(plan));
            Assert.Equal(TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(baseline), 120).NormalizedText,
                TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 120).NormalizedText);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LogarithmicRule_RejectsNonPositiveShiftedThreshold(bool xRule)
    {
        var sql = Script.Replace("LINEAR (CHANNEL", "LOGARITHMIC (CHANNEL", StringComparison.Ordinal)
            .Replace("MIN = 0,", "INCLUDE_ZERO = OFF, MIN = 1,", StringComparison.Ordinal);
        if (xRule) sql = sql.Replace("Y = DATUM(5) (TYPE = QUANTITATIVE, SCALE = estimates)", "X = DATUM(5) (TYPE = QUANTITATIVE, SCALE = distances)", StringComparison.Ordinal);
        sql = sql.Replace("Z_INDEX = 1,", $"Z_INDEX = 1, POSITION = NUDGE(X = {(xRule ? "-5" : "0")}, Y = {(xRule ? "0" : "-5")}, UNIT = DATA),", StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        var error = Assert.Throws<InvalidOperationException>(() => new PlotPlanResolver().Resolve(spec, data));
        Assert.Contains("positive logarithmic domain", error.Message);
    }

    [Fact]
    public void NullThresholds_DoNotRequireAnUnboundAnchor()
    {
        var sql = Script.Replace("DATUM(5)", "Estimate", StringComparison.Ordinal)
            .Replace("Z_INDEX = 1,", "Z_INDEX = 1, POSITION = NUDGE(X = 0, Y = 0.5, UNIT = DATA),", StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        data = data with
        {
            Columns = data.Columns.Select(column => column.Name == "Estimate" ? column with
            {
                Values = [ChartValue.Null(), ChartValue.From(5m), ChartValue.Null(), ChartValue.Null(), ChartValue.From(5m), ChartValue.Null()],
                DisplayValues = []
            } : column).ToImmutableArray()
        };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Single(Elements(XDocument.Parse(new SvgChartRenderer().Render(plan)), "plot-reference-rule"));
        Assert.Equal("5", Assert.Single(plan.Fallback.Items, item => item.Group == "Reference").Value);
        Assert.All(plan.Layers[^1].Data.Where(datum => datum.Channels.Any(channel => channel.Channel == FieldChannel.Y && channel.Value.Kind == ChartValueKind.Null)),
            datum => Assert.Equal(0m, datum.DisplayOffsetX));
    }

    [Fact]
    public void PlanAndSvg_MatchDeterministicGoldens()
    {
        var (spec, data) = Lower(Script.Replace("Z_INDEX = 1,", "Z_INDEX = 1, POSITION = NUDGE(X = 0, Y = 0.5, UNIT = DATA),", StringComparison.Ordinal));
        var plan = new PlotPlanResolver().Resolve(spec, data);
        static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Replace("\r\n", "\n", StringComparison.Ordinal))));
        var planHash = Hash(ChartContractSerializer.Serialize(plan));
        var svgHash = Hash(new SvgChartRenderer().Render(plan));
        Assert.True(planHash == "F4A47B76AAA7B65777F901D1F5E3B91C0A3181A24155DE4EB863BBF5317F72DB" && svgHash == "152D7D659A9064EB3A1CDC4E496170DCB7DD7C4187D0B1E020BEF3F7B7D67D5B", $"Plan: {planHash}; SVG: {svgHash}");
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
