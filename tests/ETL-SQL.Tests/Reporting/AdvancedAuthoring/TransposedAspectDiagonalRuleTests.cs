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
    public void DiagonalEndpoints_MapThroughFacetsAndResize(bool reverse, bool logarithmic, bool facets, string unit)
    {
        var sql = WithPosition(unit);
        if (reverse) sql = sql.Replace("MIN = 0,", "REVERSE = ON, MIN = 0,", StringComparison.Ordinal);
        if (logarithmic) sql = sql.Replace("LINEAR (CHANNEL", "LOGARITHMIC (CHANNEL", StringComparison.Ordinal).Replace("MIN = 0,", "INCLUDE_ZERO = OFF, MIN = 1,", StringComparison.Ordinal);
        if (facets) sql = sql.Replace("SCALES (", "FACET (WRAP = Cohort, COLUMNS = 2), RESOLVE (X = INDEPENDENT, Y = INDEPENDENT), SCALES (", StringComparison.Ordinal);
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
    [InlineData("StartX", "IDENTITY")]
    [InlineData("StartX", "EM")]
    [InlineData("StartX", "BAND")]
    [InlineData("StartX", "JITTER")]
    [InlineData("StartX", "DATA")]
    [InlineData("EndX", "IDENTITY")]
    [InlineData("EndX", "EM")]
    [InlineData("EndX", "BAND")]
    [InlineData("EndX", "JITTER")]
    [InlineData("EndX", "DATA")]
    [InlineData("LowerBound", "IDENTITY")]
    [InlineData("LowerBound", "EM")]
    [InlineData("LowerBound", "BAND")]
    [InlineData("LowerBound", "JITTER")]
    [InlineData("LowerBound", "DATA")]
    [InlineData("UpperBound", "IDENTITY")]
    [InlineData("UpperBound", "EM")]
    [InlineData("UpperBound", "BAND")]
    [InlineData("UpperBound", "JITTER")]
    [InlineData("UpperBound", "DATA")]
    public void MissingEndpoint_SkipsIncompleteRows(string field, string unit)
    {
        var (spec, data) = Lower(WithPosition(unit));
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
    [InlineData("CONDITIONS (COLOR WHEN Estimate > 0 THEN '#112233'),")]
    public void UnsupportedPresentation_HasPositionedDiagnostic(string option)
    {
        var statement = Parse(Script.Replace("Z_INDEX = 1,", "Z_INDEX = 1, " + option, StringComparison.Ordinal));
        Assert.Contains(AdvancedChartSemanticValidator.Validate(statement), diagnostic => diagnostic.Line > 0 && diagnostic.Column > 0 && diagnostic.Message.Contains("ASPECT_RATIO RULE", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("IDENTITY")]
    [InlineData("EM")]
    [InlineData("BAND")]
    [InlineData("JITTER")]
    [InlineData("DATA")]
    public void ConstantEndpoints_RenderWithoutPrimaryBindings(string unit)
    {
        var sql = WithPosition(unit).Replace("StartX (", "DATUM(2) (", StringComparison.Ordinal).Replace("EndX (", "DATUM(8) (", StringComparison.Ordinal)
            .Replace("LowerBound (", "DATUM(1) (", StringComparison.Ordinal).Replace("UpperBound (", "DATUM(4) (", StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        spec = spec with { Layers = [spec.Layers[1]] };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Equal(3, Elements(XDocument.Parse(new SvgChartRenderer().Render(plan)), "plot-range-rule").Length);
        Assert.Equal(3, plan.Fallback.Items.Length);
        Assert.All(plan.Fallback.Items, item => Assert.Equal("X = 2 to 8; Y = 1 to 4", item.Value));
    }

    [Theory]
    [InlineData("IDENTITY", "E602F4D95A3AE9ABF84FC248483BAACDCDD0D58288A877A3F84A4808BB3D340B", "97EE9502415AA41ED8FA063CC4B7AFBA685D96231445EBC0B3FB47BA7E9AEF22")]
    [InlineData("EM", "12836CC2F2DD311EE1F30B2A81BCBA812DD8AF7171676EC8312C1F87C1BBCD2D", "918EFCEBA4A5A867E06C54068E254CE9CCB6B46096DD55FCF6B417EC4ED83478")]
    [InlineData("BAND", "AF62BA80A2C263D1521F36A88D498A82D5034AF3EE122AB04C02CEAE52A41C33", "50C830165ED604149A0A2DCBE24EF52C0DF06D8351460384D291F86B3C7F8659")]
    [InlineData("DATA", "C3C217CA8CB5A804D25DB562880D47DB328F3F11B63068207686AAA5C25AF4D7", "EA6FE42566364D54169FAC28D052C718AA25D34DCEFBF4481AB01746A0BD9D1A")]
    [InlineData("JITTER", "FEF8D8F1F352A5D6E68B76BAB6CC18A451524052B881D2926D35B4E599D738C7", "11DF7AEA8072666D988A9A2A8E065047A0B77CB464F0A7112EA5AF9037699CA7")]
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

    [Fact]
    public void SegmentJitter_IsStableByKeyAcrossReorderRenameAndReversal()
    {
        var (spec, data) = Lower(WithPosition("JITTER"));
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
        var renamed = spec with { Layers = spec.Layers.Select(layer => layer.Mark == MarkKind.Rule ? layer with { Id = "renamed" } : layer).ToImmutableArray() };
        Assert.Equal(JitterOffsets(original, data), JitterOffsets(resolver.Resolve(renamed, reordered), reordered));
        var reseeded = spec with
        {
            Layers = spec.Layers.Select(layer => layer.Mark == MarkKind.Rule
            ? layer with { Position = layer.Position! with { Seed = 43 } } : layer).ToImmutableArray()
        };
        Assert.NotEqual(JitterOffsets(original, data), JitterOffsets(resolver.Resolve(reseeded, data), data));
        var reversed = spec with { Scales = spec.Scales.Select(scale => scale with { Reverse = true }).ToImmutableArray() };
        Assert.Equal(JitterOffsets(original, data), JitterOffsets(resolver.Resolve(reversed, data), data));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SegmentJitter_UsesSemanticAxisAndRescalesSameHash(bool xOnly)
    {
        var sql = WithPosition("JITTER").Replace(xOnly ? "Y = 0.03" : "X = 0.02", xOnly ? "Y = 0" : "X = 0", StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        var resolver = new PlotPlanResolver();
        var original = resolver.Resolve(spec, data, new PlotBounds(0m, 0m, 600m, 500m));
        var resized = resolver.Relayout(spec, data, original, new PlotBounds(0m, 0m, 1000m, 700m));
        var ratio = xOnly ? (resized.CartesianViewport!.Height - 100m) / (original.CartesianViewport!.Height - 100m)
            : (resized.CartesianViewport!.Width - 80m) / (original.CartesianViewport!.Width - 80m);
        foreach (var pair in JitterOffsets(original, data).Zip(JitterOffsets(resized, data)))
        {
            Assert.Equal(0m, xOnly ? pair.First.X : pair.First.Y);
            Assert.Equal(0m, xOnly ? pair.Second.X : pair.Second.Y);
            var before = xOnly ? pair.First.Y : pair.First.X;
            var after = xOnly ? pair.Second.Y : pair.Second.X;
            Assert.NotEqual(0m, before);
            Assert.InRange(after - before * ratio, -.000001m, .000001m);
        }
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("null")]
    [InlineData("missing")]
    public void SegmentJitter_RejectsInvalidKeysEvenOnIncompleteRows(string problem)
    {
        var (spec, data) = Lower(WithPosition("JITTER"));
        // Key validation covers the whole source, including rows that cannot draw a segment.
        data = data with
        {
            Columns = data.Columns.Select(column => column.Name == "EndX" ? column with
            {
                Values = [ChartValue.Null(), ChartValue.Null(), ChartValue.Null()],
                DisplayValues = []
            } : column.Name == "Distance" ? column with
            {
                Values = problem == "null" ? [ChartValue.Null(), ChartValue.From(5m), ChartValue.From(7m)]
                    : [ChartValue.From(3m), ChartValue.From(3m), ChartValue.From(7m)],
                DisplayValues = []
            } : column).ToImmutableArray()
        };
        if (problem == "missing") spec = spec with
        {
            Layers = spec.Layers.Select(layer => layer.Mark == MarkKind.Rule
            ? layer with { Position = layer.Position! with { StableKeyField = "Missing" } } : layer).ToImmutableArray()
        };
        Assert.Contains(problem == "missing" ? "does not exist" : problem == "null" ? "nulls" : "duplicate",
            Assert.Throws<InvalidOperationException>(() => new PlotPlanResolver().Resolve(spec, data)).Message);
    }

    [Theory]
    [InlineData("X = 0.02", "X = -0.1")]
    [InlineData("Y = 0.03", "Y = 1.1")]
    public void SegmentJitter_RejectsInvalidAmplitudesInAuthoringAndContracts(string before, string after)
    {
        var statement = Parse(WithPosition("JITTER").Replace(before, after, StringComparison.Ordinal));
        Assert.Contains(AdvancedChartSemanticValidator.Validate(statement), diagnostic => diagnostic.Line > 0 &&
            diagnostic.Column > 0 && diagnostic.Message.Contains("amplitudes", StringComparison.Ordinal));
        var (spec, _) = Lower(WithPosition("JITTER"));
        var invalid = spec with
        {
            Layers = spec.Layers.Select(layer => layer.Mark == MarkKind.Rule
            ? layer with { Position = layer.Position! with { X = -1m } } : layer).ToImmutableArray()
        };
        Assert.Contains("amplitudes", Assert.Throws<InvalidDataException>(invalid.Validate).Message);
    }

    [Fact]
    public void SegmentJitter_KeyOnlyFieldParticipatesInLineage()
    {
        var sql = WithPosition("JITTER").Replace("KEY = Distance", "KEY = StableId", StringComparison.Ordinal);
        var tracker = new LineageTracker(ETL_SQL.Common.NullLogger.Instance);
        new ETL_SQL.Analysis.Lineage.LineageAnalyzer(tracker).Analyze(new Parser(new Lexer(sql).Tokenize(), sql).Parse());
        Assert.Contains(tracker.GetFullLineage(), entry => entry.TargetTable == "report:Measurement" && entry.SourceColumns.Contains("StableId"));
    }

    private static (decimal Key, decimal X, decimal Y)[] JitterOffsets(PlotPlan plan, ChartDataSet data)
    {
        var keys = data.Columns.Single(column => column.Name == "Distance").Values;
        return plan.Layers.Single(layer => layer.Mark == MarkKind.Rule).Data.Select(datum =>
            (Key: PlotPlanResolver.Number(keys[datum.RowIndex])!.Value, X: datum.DisplayOffsetX, Y: datum.DisplayOffsetY))
            .OrderBy(item => item.Key).ToArray();
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
            Columns = ["Distance", "Estimate", "Cohort", "LowerBound", "UpperBound", "StartX", "EndX"],
            Rows = [["3", "3", "A", "1", "4", "2", "8"], ["5", "5", "A", "7", "2", "9", "1"], ["7", "7", "B", "6", "6", "4", "4"]]
        };
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        return (spec, new VisualChartDataBuilder().Build(spec, manifest));
    }
}
