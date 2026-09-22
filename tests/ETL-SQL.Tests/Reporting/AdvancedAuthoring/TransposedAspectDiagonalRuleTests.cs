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

public sealed class TransposedAspectDiagonalRuleTests
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
                ENCODINGS (X_START = StartX (TYPE = QUANTITATIVE, SCALE = distances), X_END = EndX (TYPE = QUANTITATIVE, SCALE = distances), Y_START = LowerBound (TYPE = QUANTITATIVE, SCALE = estimates), Y_END = UpperBound (TYPE = QUANTITATIVE, SCALE = estimates)),
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
    public void DiagonalEndpoints_MapThroughFacetsAndResize(bool reverse, bool logarithmic, bool facets)
    {
        var sql = Script;
        if (reverse) sql = sql.Replace("MIN = 0,", "REVERSE = ON, MIN = 0,", StringComparison.Ordinal);
        if (logarithmic) sql = sql.Replace("LINEAR (CHANNEL", "LOGARITHMIC (CHANNEL", StringComparison.Ordinal).Replace("MIN = 0,", "INCLUDE_ZERO = OFF, MIN = 1,", StringComparison.Ordinal);
        if (facets) sql = sql.Replace("SCALES (", "FACET (WRAP = Cohort, COLUMNS = 2), RESOLVE (X = INDEPENDENT, Y = INDEPENDENT), SCALES (", StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        var resolver = new PlotPlanResolver();
        var original = resolver.Resolve(spec, data, new PlotBounds(0m, 0m, 1000m, 650m));
        foreach (var bounds in new[] { original.Bounds, new PlotBounds(0m, 0m, 800m, 600m) })
        {
            var plan = resolver.Relayout(spec, data, original, bounds);
            var svg = XDocument.Parse(new SvgChartRenderer().Render(plan));
            var rules = Elements(svg, "plot-range-rule");
            Assert.Equal(3, rules.Length);
            foreach (var end in new[] { false, true })
            {
                var rule = Assert.Single(plan.Layers, layer => layer.Mark == MarkKind.Rule);
                var x = end ? FieldChannel.XEnd : FieldChannel.XStart;
                var y = end ? FieldChannel.YEnd : FieldChannel.YStart;
                var mapped = plan with
                {
                    Layers = [rule with { Mark = MarkKind.Point,
                    Data = rule.Data.Select(datum => datum with { Channels = datum.Channels
                        .Where(channel => channel.Channel == x || channel.Channel == y)
                        .Select(channel => channel with { Channel = channel.Channel == x ? FieldChannel.X : FieldChannel.Y }).ToImmutableArray() }).ToImmutableArray()
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
            Assert.Equal(Read(rules[2], "x1"), Read(rules[2], "x2"));
            Assert.Equal(Read(rules[2], "y1"), Read(rules[2], "y2"));
            Assert.Equal(3, Elements(svg, "plot-reference-rule-label").Length);
            var references = plan.Fallback.Items.Where(item => item.Group == "Reference").ToArray();
            Assert.Equal(new[] { "X = 2 to 8; Y = 1 to 4", "X = 9 to 1; Y = 7 to 2", "X = 4 to 4; Y = 6 to 6" }, references.Select(item => item.Value));
            var terminal = TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 140).NormalizedText;
            foreach (var pair in rules.Zip(references))
            {
                Assert.Contains(pair.Second.Value, pair.First.Value);
                Assert.Contains(pair.Second.Value, terminal);
            }
            Assert.Equal(ChartContractSerializer.Serialize(resolver.Resolve(spec, data, bounds)), ChartContractSerializer.Serialize(plan));
        }
    }

    [Theory]
    [InlineData("StartX")]
    [InlineData("EndX")]
    [InlineData("LowerBound")]
    [InlineData("UpperBound")]
    public void MissingEndpoint_SkipsIncompleteRows(string field)
    {
        var (spec, data) = Lower(Script);
        data = data with
        {
            Columns = data.Columns.Select(column => column.Name == field
            ? column with { Values = column.Values.SetItem(1, ChartValue.Null()), DisplayValues = [] } : column).ToImmutableArray()
        };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Equal(2, Elements(XDocument.Parse(new SvgChartRenderer().Render(plan)), "plot-range-rule").Length);
        Assert.Equal(2, plan.Fallback.Items.Count(item => item.Group == "Reference"));
    }

    [Fact]
    public void Endpoints_ExpandBothIndependentDomains()
    {
        var sql = Script.Replace(", MIN = 0, MAX = 10", "", StringComparison.Ordinal)
            .Replace("SCALES (", "FACET (WRAP = Cohort, COLUMNS = 2), RESOLVE (X = INDEPENDENT, Y = INDEPENDENT), SCALES (", StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        data = data with
        {
            Columns = data.Columns.Select(column => column.Name is "EndX" or "UpperBound"
            ? column with { Values = column.Values.SetItem(0, ChartValue.From(column.Name == "EndX" ? 20m : 30m)), DisplayValues = [] } : column).ToImmutableArray()
        };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        foreach (var scales in new[] { plan.Scales, plan.Facets[0].Scales })
        {
            Assert.True(PlotPlanResolver.Number(scales.Single(scale => scale.Channel == FieldChannel.X).Domain[^1]) >= 20m);
            Assert.True(PlotPlanResolver.Number(scales.Single(scale => scale.Channel == FieldChannel.Y).Domain[^1]) >= 30m);
        }
    }

    [Theory]
    [InlineData("POSITION = NUDGE(X = 0, Y = 1, UNIT = EM),")]
    [InlineData("CONDITIONS (COLOR WHEN Estimate > 0 THEN '#112233'),")]
    [InlineData("POSITION = JITTER(X = 0.1, Y = 0, KEY = Distance, SEED = 3),")]
    public void UnsupportedPresentation_HasPositionedDiagnostic(string option)
    {
        var statement = Parse(Script.Replace("Z_INDEX = 1,", "Z_INDEX = 1, " + option, StringComparison.Ordinal));
        Assert.Contains(AdvancedChartSemanticValidator.Validate(statement), diagnostic => diagnostic.Line > 0 && diagnostic.Column > 0 && diagnostic.Message.Contains("ASPECT_RATIO RULE", StringComparison.Ordinal));
    }

    [Fact]
    public void ConstantEndpoints_RenderWithoutPrimaryBindings()
    {
        var sql = Script.Replace("StartX (", "DATUM(2) (", StringComparison.Ordinal).Replace("EndX (", "DATUM(8) (", StringComparison.Ordinal)
            .Replace("LowerBound (", "DATUM(1) (", StringComparison.Ordinal).Replace("UpperBound (", "DATUM(4) (", StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        spec = spec with { Layers = [spec.Layers[1]] };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Equal(3, Elements(XDocument.Parse(new SvgChartRenderer().Render(plan)), "plot-range-rule").Length);
        Assert.Equal(3, plan.Fallback.Items.Length);
        Assert.All(plan.Fallback.Items, item => Assert.Equal("X = 2 to 8; Y = 1 to 4", item.Value));
    }

    [Fact]
    public void PlanAndSvg_MatchDeterministicGoldens()
    {
        var (spec, data) = Lower(Script);
        var plan = new PlotPlanResolver().Resolve(spec, data);
        static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Replace("\r\n", "\n", StringComparison.Ordinal))));
        var planHash = Hash(ChartContractSerializer.Serialize(plan));
        var svgHash = Hash(new SvgChartRenderer().Render(plan));
        Assert.True(planHash == "E602F4D95A3AE9ABF84FC248483BAACDCDD0D58288A877A3F84A4808BB3D340B" && svgHash == "97EE9502415AA41ED8FA063CC4B7AFBA685D96231445EBC0B3FB47BA7E9AEF22", $"Plan: {planHash}; SVG: {svgHash}");
    }
    [Fact]
    public async Task AuthoringContractsAndPdf_PreserveSegments()
    {
        var statement = Parse(Script);
        Assert.Equal(statement.ToSql(), Parse(statement.ToSql()).ToSql());
        var source = Script + "CREATE PAGE Dashboard AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = Measurement)));";
        var parsed = new DesignerAnalysisService().Parse(source, 100);
        Assert.Null(parsed.Error);
        Assert.Equal(statement.ToSql(), Parse(new DesignerScriptPatcher().Patch(source, parsed.DesignState)).ToSql());
        var tracker = new LineageTracker(ETL_SQL.Common.NullLogger.Instance);
        new ETL_SQL.Analysis.Lineage.LineageAnalyzer(tracker).Analyze(new Parser(new Lexer(Script).Tokenize(), Script).Parse());
        Assert.Contains(tracker.GetFullLineage(), entry => entry.TargetTable == "report:Measurement" && entry.SourceColumns.Contains("LowerBound"));
        var (spec, data) = Lower(Script);
        Assert.Equal(ChartContractSerializer.Serialize(spec), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializeChartSpec(ChartContractSerializer.Serialize(spec))));
        var invalid = spec with { Layers = [spec.Layers[1] with { Position = new PositionAdjustmentSpec(PositionAdjustmentKind.Nudge, 0m, 1m) }] };
        Assert.Contains("ASPECT_RATIO RULE", Assert.Throws<InvalidDataException>(invalid.Validate).Message);
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
            Columns = ["Distance", "Estimate", "Cohort", "LowerBound", "UpperBound", "StartX", "EndX"],
            Rows = [["3", "3", "A", "1", "4", "2", "8"], ["5", "5", "A", "7", "2", "9", "1"], ["7", "7", "B", "6", "6", "4", "4"]]
        };
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        return (spec, new VisualChartDataBuilder().Build(spec, manifest));
    }
}
