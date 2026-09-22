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

public sealed class TransposedAspectDataNudgeTests
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
              POSITION = NUDGE(X = 0.5, Y = -0.5, UNIT = DATA),
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
    public void DataNudge_MovesPointsAndIntervalsTogetherWithoutChangingValues(bool facets, bool reverse, bool logarithmic)
    {
        var sql = Script;
        if (facets) sql = sql.Replace("SCALES (", "FACET (WRAP = Distance, COLUMNS = 2), RESOLVE (Y = INDEPENDENT), SCALES (", StringComparison.Ordinal)
            .Replace("estimates = LINEAR (CHANNEL = Y, MIN = 0, MAX = 10)", "estimates = LINEAR (CHANNEL = Y)", StringComparison.Ordinal);
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
            VerifyMappedAnchors(plan);
            Assert.Equal(ChartContractSerializer.Serialize(resolver.Resolve(spec, data, bounds)), ChartContractSerializer.Serialize(plan));
            Assert.Equal(TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(baseline), 120).NormalizedText,
                TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 120).NormalizedText);
        }
    }

    [Theory]
    [InlineData("LEFT", false)]
    [InlineData("RIGHT", false)]
    [InlineData("BOTTOM", false)]
    [InlineData("OFF", false)]
    [InlineData("RIGHT", true)]
    public void LegendGuttersAndFacets_UseTheRenderedAxisDistance(string legend, bool facets)
    {
        var sql = Script.Replace("TEXT = Caption (TYPE = NOMINAL)",
            "TEXT = Caption (TYPE = NOMINAL), COLOR = Caption (TYPE = NOMINAL)", StringComparison.Ordinal);
        if (facets) sql = sql.Replace("SCALES (", "FACET (WRAP = Id, COLUMNS = 2), SCALES (", StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        spec = spec with { Theme = spec.Theme with { Tokens = [new StyleToken("LEGEND", legend)] } };
        var plan = new PlotPlanResolver().Resolve(spec, data, new PlotBounds(0m, 0m, 1000m, 650m));
        Assert.Equal(2, plan.Legend.Length);
        VerifyMappedAnchors(plan);
    }

    [Theory]
    [InlineData("NUDGE(X = -4, Y = 0, UNIT = DATA)", "positive logarithmic")]
    [InlineData("NUDGE(X = 0, Y = -4, UNIT = DATA)", "positive logarithmic")]
    public void NonpositiveLogTargets_FailBeforeRendering(string position, string message)
    {
        var sql = Script.Replace("NUDGE(X = 0.5, Y = -0.5, UNIT = DATA)", position, StringComparison.Ordinal)
            .Replace("LINEAR (CHANNEL", "LOGARITHMIC (CHANNEL", StringComparison.Ordinal)
            .Replace("MIN = 0,", "INCLUDE_ZERO = OFF, MIN = 1,", StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        Assert.Contains(message, Assert.Throws<InvalidOperationException>(() => new PlotPlanResolver().Resolve(spec, data)).Message);
    }

    [Fact]
    public void MissingPosition_FailsBeforeRendering()
    {
        var (spec, data) = Lower(Script);
        data = data with
        {
            Columns = data.Columns.Select(column => column.Name == "Distance" ? column with
            {
                Values = [ChartValue.Null(), ChartValue.From(7m)]
            } : column).ToImmutableArray()
        };
        Assert.Contains("numeric X and Y", Assert.Throws<InvalidOperationException>(() => new PlotPlanResolver().Resolve(spec, data)).Message);
    }

    [Fact]
    public void DataNudge_AddsToCategoricalOffsets()
    {
        var sql = Script.Replace("TEXT = Caption (TYPE = NOMINAL)",
            "TEXT = Caption (TYPE = NOMINAL), X_OFFSET = Caption (TYPE = NOMINAL)", StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        var resolver = new PlotPlanResolver();
        var combined = resolver.Resolve(spec, data);
        var groupOnly = resolver.Resolve(spec with { Layers = [spec.Layers[0] with { Position = null }] }, data);
        var plainSpec = spec with { Layers = [spec.Layers[0] with { Bindings = spec.Layers[0].Bindings.Where(binding => binding.Channel != FieldChannel.XOffset).ToImmutableArray() }] };
        var dataOnly = resolver.Resolve(plainSpec, data);
        for (var index = 0; index < combined.Layers[0].Data.Length; index++)
        {
            Assert.Equal(groupOnly.Layers[0].Data[index].DisplayOffsetX + dataOnly.Layers[0].Data[index].DisplayOffsetX, combined.Layers[0].Data[index].DisplayOffsetX);
            Assert.Equal(groupOnly.Layers[0].Data[index].DisplayOffsetY + dataOnly.Layers[0].Data[index].DisplayOffsetY, combined.Layers[0].Data[index].DisplayOffsetY);
        }
    }

    [Fact]
    public async Task TextAndAuthoringContracts_PreserveDataNudge()
    {
        var statement = Parse(Script);
        Assert.Equal(statement.ToSql(), Parse(statement.ToSql()).ToSql());
        var source = Script + "CREATE PAGE Dashboard AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = Measurement)));";
        var parsed = new DesignerAnalysisService().Parse(source, 100);
        Assert.Null(parsed.Error);
        Assert.Equal(statement.ToSql(), Parse(new DesignerScriptPatcher().Patch(source, parsed.DesignState)).ToSql());
        var tracker = new LineageTracker(ETL_SQL.Common.NullLogger.Instance);
        new ETL_SQL.Analysis.Lineage.LineageAnalyzer(tracker).Analyze(new Parser(new Lexer(Script).Tokenize(), Script).Parse());
        Assert.Contains(tracker.GetFullLineage(), entry => entry.TargetTable == "report:Measurement" && entry.SourceColumns.Contains("Estimate"));
        var (spec, data) = Lower(Script);
        var text = spec.Layers[0] with
        {
            Id = "labels",
            ZIndex = 1,
            Mark = MarkKind.Text,
            Bindings = spec.Layers[0].Bindings.Where(binding => binding.Channel is not (FieldChannel.ErrorLow or FieldChannel.ErrorHigh)).ToImmutableArray()
        };
        spec = spec with { Layers = [spec.Layers[0], text] };
        var json = ChartContractSerializer.Serialize(spec);
        Assert.Equal(json, ChartContractSerializer.Serialize(ChartContractSerializer.DeserializeChartSpec(json)));
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var baseline = new PlotPlanResolver().Resolve(spec with { Layers = spec.Layers.Select(layer => layer with { Position = null }).ToImmutableArray() }, data);
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
        Assert.True(planHash == "50709E437D49A2B20F7144706CE21B81B4288A481189305FF8307C84FF071DCE" && svgHash == "98F1D166D76A50D486C01A7926A197F29B04F91B5A72FBDCBDC129C93BB62BFD", $"Plan: {planHash}; SVG: {svgHash}");
    }

    private static void VerifyMappedAnchors(PlotPlan plan)
    {
        // Independent oracle: render the same scales with anchors moved in data units and no offsets.
        var mapped = plan with
        {
            Layers = plan.Layers.Select(layer => layer with
            {
                Data = layer.Data.Select(datum => datum with
                {
                    DisplayOffsetX = 0m,
                    DisplayOffsetY = 0m,
                    Channels = datum.Channels.Select(channel => channel.Channel is FieldChannel.X or FieldChannel.Y
                        ? channel with { Value = ChartValue.From(PlotPlanResolver.Number(channel.Value)!.Value + (channel.Channel == FieldChannel.X ? .5m : -.5m)) }
                        : channel).ToImmutableArray()
                }).ToImmutableArray()
            }).ToImmutableArray()
        };
        var actual = Elements(XDocument.Parse(new SvgChartRenderer().Render(plan)), "plot-point");
        var expected = Elements(XDocument.Parse(new SvgChartRenderer().Render(mapped)), "plot-point");
        Assert.NotEmpty(actual);
        Assert.Equal(expected.Length, actual.Length);
        for (var index = 0; index < actual.Length; index++)
        {
            Assert.InRange(Read(actual[index], "cx") - Read(expected[index], "cx"), -.001m, .001m);
            Assert.InRange(Read(actual[index], "cy") - Read(expected[index], "cy"), -.001m, .001m);
        }
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
            Columns = ["Id", "Distance", "Estimate", "LowerBound", "UpperBound", "Caption"],
            Rows = [["a", "3", "3", "2", "4", "alpha"], ["b", "7", "7", "6", "8", "beta"]]
        };
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        return (spec, new VisualChartDataBuilder().Build(spec, manifest));
    }
}
