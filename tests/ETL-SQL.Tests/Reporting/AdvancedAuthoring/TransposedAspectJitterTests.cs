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

public sealed class TransposedAspectJitterTests
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
            LAYERS (observations = POINT (
              POSITION = JITTER(X = 0.02, Y = 0.03, KEY = Id, SEED = 42),
              ENCODINGS (
                X = Distance (TYPE = QUANTITATIVE, SCALE = distances),
                Y = Estimate (TYPE = QUANTITATIVE, SCALE = estimates),
                ERROR_LOW = LowerBound (TYPE = QUANTITATIVE, SCALE = estimates),
                ERROR_HIGH = UpperBound (TYPE = QUANTITATIVE, SCALE = estimates),
                TEXT = Caption (TYPE = NOMINAL)
              )
            ))
          )
        );
        """;

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void Jitter_MovesPointsAndIntervalsTogetherWithoutChangingValues(bool facets, bool reverse, bool logarithmic)
    {
        var sql = Script;
        if (facets) sql = sql.Replace("SCALES (", "FACET (WRAP = Distance, COLUMNS = 2), RESOLVE (Y = INDEPENDENT), SCALES (", StringComparison.Ordinal);
        if (reverse) sql = sql.Replace("CHANNEL = X,", "CHANNEL = X, REVERSE = ON,", StringComparison.Ordinal)
            .Replace("CHANNEL = Y,", "CHANNEL = Y, REVERSE = ON,", StringComparison.Ordinal);
        if (logarithmic) sql = sql.Replace("LINEAR (CHANNEL", "LOGARITHMIC (CHANNEL", StringComparison.Ordinal)
            .Replace("MIN = 0,", "INCLUDE_ZERO = OFF, MIN = 1,", StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        var baselineSpec = spec with { Layers = [spec.Layers[0] with { Position = null }] };
        var resolver = new PlotPlanResolver();
        var original = resolver.Resolve(spec, data, new PlotBounds(0m, 0m, 1000m, 600m));
        foreach (var bounds in new[] { original.Bounds, new PlotBounds(0m, 0m, 800m, 650m) })
        {
            var plan = resolver.Relayout(spec, data, original, bounds);
            var baseline = resolver.Resolve(baselineSpec, data, bounds);
            Assert.Equal(baseline.CartesianViewport, plan.CartesianViewport);
            Assert.Equal(baseline.Scales.SelectMany(scale => scale.Domain), plan.Scales.SelectMany(scale => scale.Domain));
            Assert.Equal(baseline.Fallback.Items.ToArray(), plan.Fallback.Items.ToArray());
            Assert.Equal(baseline.Layers[0].Data.SelectMany(datum => datum.Channels), plan.Layers[0].Data.SelectMany(datum => datum.Channels));
            var svg = XDocument.Parse(new SvgChartRenderer().Render(plan));
            var baseSvg = XDocument.Parse(new SvgChartRenderer().Render(baseline));
            foreach (var markClass in new[] { "plot-point", "plot-error-bar-stem", "plot-error-bar-cap" })
            {
                var actual = Elements(svg, markClass);
                var expected = Elements(baseSvg, markClass);
                Assert.NotEmpty(actual);
                Assert.Equal(expected.Length, actual.Length);
                var attributes = markClass == "plot-point" ? new[] { "cx", "cy" } : ["x1", "y1", "x2", "y2"];
                for (var index = 0; index < actual.Length; index++)
                {
                    var row = markClass == "plot-error-bar-cap" ? index / 2 : index;
                    var datum = Assert.Single(plan.Layers[0].Data, datum => datum.RowIndex == row);
                    foreach (var attribute in attributes)
                        Assert.InRange(Read(actual[index], attribute) - Read(expected[index], attribute) -
                            (attribute.Contains('x') ? datum.DisplayOffsetX : datum.DisplayOffsetY), -.002m, .002m);
                }
            }
            foreach (var datum in plan.Layers[0].Data)
            {
                var viewport = facets ? Assert.Single(plan.Facets, facet => facet.RowIndices.Contains(datum.RowIndex)).CartesianViewport! : plan.CartesianViewport!;
                Assert.InRange(Math.Abs(datum.DisplayOffsetX), 0m, .03m * (viewport.Width - 80m));
                Assert.InRange(Math.Abs(datum.DisplayOffsetY), 0m, .02m * (viewport.Height - 100m));
            }
            Assert.Equal(ChartContractSerializer.Serialize(resolver.Resolve(spec, data, bounds)), ChartContractSerializer.Serialize(plan));
            Assert.Equal(TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(baseline), 120).NormalizedText,
                TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 120).NormalizedText);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Amplitudes_FollowSemanticAxesAndScaleWithFittedViewport(bool xOnly)
    {
        var (spec, data) = Lower(Script.Replace(xOnly ? "Y = 0.03" : "X = 0.02", xOnly ? "Y = 0" : "X = 0", StringComparison.Ordinal));
        var resolver = new PlotPlanResolver();
        var original = resolver.Resolve(spec, data, new PlotBounds(0m, 0m, 600m, 500m));
        var resized = resolver.Relayout(spec, data, original, new PlotBounds(0m, 0m, 1000m, 700m));
        var ratio = xOnly ? (resized.CartesianViewport!.Height - 100m) / (original.CartesianViewport!.Height - 100m)
            : (resized.CartesianViewport!.Width - 80m) / (original.CartesianViewport!.Width - 80m);
        for (var index = 0; index < original.Layers[0].Data.Length; index++)
        {
            var datum = original.Layers[0].Data[index];
            var next = resized.Layers[0].Data[index];
            Assert.Equal(0m, xOnly ? datum.DisplayOffsetX : datum.DisplayOffsetY);
            Assert.NotEqual(0m, xOnly ? datum.DisplayOffsetY : datum.DisplayOffsetX);
            Assert.InRange((xOnly ? next.DisplayOffsetY - datum.DisplayOffsetY * ratio : next.DisplayOffsetX - datum.DisplayOffsetX * ratio), -.0000001m, .0000001m);
        }
    }

    [Fact]
    public void StableKeys_PreserveOffsetsAcrossRowOrderAndLayerRename()
    {
        var (spec, data) = Lower(Script);
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
        var renamed = spec with { Layers = [spec.Layers[0] with { Id = "renamed" }] };
        Assert.Equal(Offsets(original), Offsets(resolver.Resolve(renamed, reordered)));
        var reseeded = spec with { Layers = [spec.Layers[0] with { Position = spec.Layers[0].Position! with { Seed = 43 } }] };
        Assert.NotEqual(Offsets(original), Offsets(resolver.Resolve(reseeded, data)));
        var reversed = spec with { Scales = spec.Scales.Select(scale => scale with { Reverse = true }).ToImmutableArray() };
        Assert.Equal(Offsets(original), Offsets(resolver.Resolve(reversed, data)));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("null")]
    [InlineData("missing")]
    public void InvalidStableKeys_FailBeforeRendering(string problem)
    {
        var (spec, data) = Lower(Script);
        data = data with
        {
            Columns = data.Columns.Where(column => problem != "missing" || column.Name != "Id").Select(column => column.Name != "Id" ? column : column with
            {
                Values = problem == "null" ? [ChartValue.Null(), ChartValue.From("b")] : [ChartValue.From("a"), ChartValue.From("a")]
            }).ToImmutableArray()
        };
        Assert.Contains(problem == "missing" ? "does not exist" : problem == "null" ? "nulls" : "duplicate", Assert.Throws<InvalidOperationException>(() => new PlotPlanResolver().Resolve(spec, data)).Message);
    }

    [Theory]
    [InlineData("X = 0.02", "X = -0.1")]
    [InlineData("Y = 0.03", "Y = 1.1")]
    public void InvalidAmplitudes_HaveAuthoringAndContractDiagnostics(string before, string after)
    {
        Assert.Contains(AdvancedChartSemanticValidator.Validate(Parse(Script.Replace(before, after, StringComparison.Ordinal))), diagnostic =>
            diagnostic.Line > 0 && diagnostic.Column > 0 && diagnostic.Message.Contains("amplitudes", StringComparison.Ordinal));
        var (spec, _) = Lower(Script);
        var invalid = spec with { Layers = [spec.Layers[0] with { Position = spec.Layers[0].Position! with { X = -1m } }] };
        Assert.Contains("amplitudes", Assert.Throws<InvalidDataException>(invalid.Validate).Message);
    }

    [Fact]
    public async Task TextAndAuthoringContracts_PreserveJitter()
    {
        var statement = Parse(Script);
        Assert.Equal(statement.ToSql(), Parse(statement.ToSql()).ToSql());
        var source = Script + "CREATE PAGE Dashboard AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = Measurement)));";
        var parsed = new DesignerAnalysisService().Parse(source, 100);
        Assert.Null(parsed.Error);
        Assert.Equal(statement.ToSql(), Parse(new DesignerScriptPatcher().Patch(source, parsed.DesignState)).ToSql());
        var tracker = new LineageTracker(ETL_SQL.Common.NullLogger.Instance);
        new ETL_SQL.Analysis.Lineage.LineageAnalyzer(tracker).Analyze(new Parser(new Lexer(Script).Tokenize(), Script).Parse());
        Assert.Contains(tracker.GetFullLineage(), entry => entry.TargetTable == "report:Measurement" && entry.SourceColumns.Contains("Id"));
        var (spec, data) = Lower(Script);
        var text = spec.Layers[0] with
        {
            Id = "labels",
            Mark = MarkKind.Text,
            Bindings = spec.Layers[0].Bindings.Where(binding => binding.Channel is not (FieldChannel.ErrorLow or FieldChannel.ErrorHigh)).ToImmutableArray()
        };
        spec = spec with { Layers = [text] };
        var json = ChartContractSerializer.Serialize(spec);
        Assert.Equal(json, ChartContractSerializer.Serialize(ChartContractSerializer.DeserializeChartSpec(json)));
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var baseline = new PlotPlanResolver().Resolve(spec with { Layers = [text with { Position = null }] }, data);
        var svg = new SvgChartRenderer().Render(plan);
        var labels = Elements(XDocument.Parse(svg), "plot-smart-label");
        var baseLabels = Elements(XDocument.Parse(new SvgChartRenderer().Render(baseline)), "plot-smart-label");
        Assert.Equal(2, labels.Length);
        for (var index = 0; index < labels.Length; index++)
        {
            Assert.InRange(Read(labels[index], "x") - Read(baseLabels[index], "x") - plan.Layers[0].Data[index].DisplayOffsetX, -.002m, .002m);
            Assert.InRange(Read(labels[index], "y") - Read(baseLabels[index], "y") - plan.Layers[0].Data[index].DisplayOffsetY, -.002m, .002m);
        }
        var planJson = ChartContractSerializer.Serialize(plan);
        Assert.Equal(planJson, ChartContractSerializer.Serialize(ChartContractSerializer.DeserializePlotPlan(planJson)));
        Assert.Equal(TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(baseline), 100).NormalizedText,
            TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 100).NormalizedText);
        var report = new ReportManifest
        {
            Title = "Measurement",
            Source = "measurement.rptsql",
            Visuals = [new VisualManifest { Name = "Measurement", VisualType = "CUSTOM", PlotPlan = plan, NativeSvg = svg }]
        };
        Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, (await new PdfExporter().ExportAsync(report))[..4]);
    }

    [Fact]
    public void PlanAndSvg_MatchDeterministicGoldens()
    {
        var (spec, data) = Lower(Script);
        var plan = new PlotPlanResolver().Resolve(spec, data);
        static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Replace("\r\n", "\n", StringComparison.Ordinal))));
        var planHash = Hash(ChartContractSerializer.Serialize(plan));
        var svgHash = Hash(new SvgChartRenderer().Render(plan));
        Assert.True(planHash == "4BD89F7C9E74FB661DB00BD525A4622DED3795B8FE177232506DC1B803C8A8FE" && svgHash == "E8E07A089FF709B7741676095C2D2647C3C76B8F293130B2797E2A441B71B63E", $"Plan: {planHash}; SVG: {svgHash}");
    }

    private static (string Label, decimal X, decimal Y)[] Offsets(PlotPlan plan) => plan.Layers[0].Data.Select(datum =>
        (Label: datum.Channels.Single(channel => channel.Channel == FieldChannel.Text).DisplayValue!, X: datum.DisplayOffsetX, Y: datum.DisplayOffsetY))
        .OrderBy(item => item.Label, StringComparer.Ordinal).ToArray();
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
            Columns = ["Id", "Distance", "Estimate", "LowerBound", "UpperBound", "Caption"],
            Rows = [["a", "3", "3", "2", "4", "alpha"], ["b", "7", "7", "6", "8", "beta"]]
        };
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        return (spec, new VisualChartDataBuilder().Build(spec, manifest));
    }
}
