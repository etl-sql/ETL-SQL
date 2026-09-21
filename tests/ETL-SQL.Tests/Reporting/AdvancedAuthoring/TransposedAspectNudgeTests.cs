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

public sealed class TransposedAspectNudgeTests
{
    private const string Script = """
        CREATE VISUAL Measurement AS CUSTOM (
          SOURCE = #prepared,
          CHART (
            COORDINATE (TYPE = TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2),
            SCALES (
              distances = LINEAR (CHANNEL = X, MIN = 0, MAX = 4),
              estimates = LINEAR (CHANNEL = Y, MIN = 0, MAX = 40)
            ),
            LAYERS (observations = POINT (
              POSITION = NUDGE(X = 1, Y = -0.5, UNIT = EM),
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
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    public void EmNudge_TranslatesPointsAndErrorBarsTogether(bool facets, bool reverse, bool logarithmic)
    {
        var sql = Script;
        if (logarithmic)
            sql = sql.Replace("LINEAR (CHANNEL", "LOGARITHMIC (CHANNEL", StringComparison.Ordinal)
                .Replace("MIN = 0,", "INCLUDE_ZERO = OFF, MIN = 1,", StringComparison.Ordinal);
        if (facets)
            sql = sql.Replace("SCALES (", "FACET (WRAP = Distance, COLUMNS = 2), RESOLVE (Y = INDEPENDENT), SCALES (", StringComparison.Ordinal);
        if (reverse)
            sql = sql.Replace("CHANNEL = X,", "CHANNEL = X, REVERSE = ON,", StringComparison.Ordinal)
                .Replace("CHANNEL = Y,", "CHANNEL = Y, REVERSE = ON,", StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        var unshifted = spec with { Layers = [spec.Layers[0] with { Position = null }] };
        var resolver = new PlotPlanResolver();
        var original = resolver.Resolve(spec, data, new PlotBounds(0m, 0m, 800m, 450m));
        foreach (var bounds in new[] { original.Bounds, new PlotBounds(0m, 0m, 600m, 500m) })
        {
            var plan = resolver.Relayout(spec, data, original, bounds);
            var baseline = resolver.Resolve(unshifted, data, bounds);
            Assert.Equal(baseline.CartesianViewport, plan.CartesianViewport);
            Assert.Equal(baseline.Scales.SelectMany(scale => scale.Domain), plan.Scales.SelectMany(scale => scale.Domain));
            Assert.Equal(baseline.Fallback.Items.ToArray(), plan.Fallback.Items.ToArray());
            Assert.Equal(baseline.Layers[0].Data.SelectMany(datum => datum.Channels), plan.Layers[0].Data.SelectMany(datum => datum.Channels));
            Assert.All(plan.Layers[0].Data, datum =>
            {
                Assert.Equal(-6m, datum.DisplayOffsetX);
                Assert.Equal(-12m, datum.DisplayOffsetY);
            });
            var baseSvg = XDocument.Parse(new SvgChartRenderer().Render(baseline));
            var svg = XDocument.Parse(new SvgChartRenderer().Render(plan));
            foreach (var markClass in new[] { "plot-point", "plot-error-bar-stem", "plot-error-bar-cap" })
            {
                var oldMarks = Elements(baseSvg, markClass);
                var newMarks = Elements(svg, markClass);
                Assert.NotEmpty(newMarks);
                Assert.Equal(oldMarks.Length, newMarks.Length);
                var attributes = markClass == "plot-point" ? new[] { "cx", "cy" } : ["x1", "y1", "x2", "y2"];
                for (var index = 0; index < newMarks.Length; index++)
                    foreach (var attribute in attributes)
                        Assert.Equal(attribute.Contains('x') ? -6m : -12m,
                            Read(newMarks[index], attribute) - Read(oldMarks[index], attribute));
            }
            Assert.Equal(ChartContractSerializer.Serialize(resolver.Resolve(spec, data, bounds)), ChartContractSerializer.Serialize(plan));
        }
    }

    [Theory]
    [InlineData("CARTESIAN", 12, 6)]
    [InlineData("TRANSPOSED_CARTESIAN", -6, -12)]
    public void EmNudge_FollowsSemanticAxesWithoutChangingCartesianBehavior(string coordinate, int xOffset, int yOffset)
    {
        var (spec, data) = Lower(Script.Replace("TRANSPOSED_CARTESIAN", coordinate, StringComparison.Ordinal));
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.All(plan.Layers[0].Data, datum =>
        {
            Assert.Equal((decimal)xOffset, datum.DisplayOffsetX);
            Assert.Equal((decimal)yOffset, datum.DisplayOffsetY);
        });
    }

    [Theory]
    [InlineData("NUDGE(X = 1, Y = 1, UNIT = DATA)")]
    [InlineData("NUDGE(X = 0.1, Y = 0.1, UNIT = BAND)")]
    [InlineData("JITTER(X = 0.1, Y = 0.1, KEY = Distance, SEED = 1)")]
    public void UnsupportedUnitsAndJitter_StillFailAuthoringAndContractValidation(string position)
    {
        var invalid = Parse(Script.Replace("NUDGE(X = 1, Y = -0.5, UNIT = EM)", position, StringComparison.Ordinal));
        Assert.Contains(AdvancedChartSemanticValidator.Validate(invalid), diagnostic =>
            diagnostic.Code == "RPT-CHART" && diagnostic.Line > 0 && diagnostic.Column > 0 &&
            diagnostic.Message.Contains("NUDGE UNIT EM", StringComparison.Ordinal));
        var (spec, _) = Lower(Script);
        var unsupported = position.StartsWith("JITTER", StringComparison.Ordinal)
            ? new PositionAdjustmentSpec(PositionAdjustmentKind.Jitter, .1m, .1m, "Distance", 1)
            : new PositionAdjustmentSpec(PositionAdjustmentKind.Nudge, .1m, .1m,
                Unit: position.Contains("DATA", StringComparison.Ordinal) ? PositionAdjustmentUnit.Data : PositionAdjustmentUnit.Band);
        var contract = spec with { Layers = [spec.Layers[0] with { Position = unsupported }] };
        Assert.Contains("NUDGE UNIT EM", Assert.Throws<InvalidDataException>(contract.Validate).Message);
    }

    [Fact]
    public void AuthoringAndSerialization_PreserveNudgeAndRawFallback()
    {
        var statement = Parse(Script);
        Assert.Equal(statement.ToSql(), Parse(statement.ToSql()).ToSql());
        var source = Script + "CREATE PAGE Dashboard AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = Measurement)));";
        var parsed = new DesignerAnalysisService().Parse(source, 100);
        Assert.Null(parsed.Error);
        var patched = new DesignerScriptPatcher().Patch(source, parsed.DesignState);
        Assert.Contains("POSITION = NUDGE(X = 1, Y = -0.5, UNIT = EM)", patched);
        var (spec, data) = Lower(Script);
        var json = ChartContractSerializer.Serialize(spec);
        Assert.Equal(json, ChartContractSerializer.Serialize(ChartContractSerializer.DeserializeChartSpec(json)));
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var planJson = ChartContractSerializer.Serialize(plan);
        Assert.Equal(planJson, ChartContractSerializer.Serialize(ChartContractSerializer.DeserializePlotPlan(planJson)));
        var unshifted = spec with { Layers = [spec.Layers[0] with { Position = null }] };
        var baseline = new PlotPlanResolver().Resolve(unshifted, data);
        Assert.Equal(TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(baseline), 100).NormalizedText,
            TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 100).NormalizedText);
    }

    [Fact]
    public async Task StaticExport_ConsumesResolvedNudge()
    {
        var (spec, data) = Lower(Script);
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var svg = new SvgChartRenderer().Render(plan);
        var visual = new VisualManifest { Name = "Measurement", VisualType = "CUSTOM", PlotPlan = plan, NativeSvg = svg };
        Assert.Equal(svg, new SvgChartRenderer().Render(visual));
        var report = new ReportManifest { Title = "Measurement", Source = "measurement.rptsql", Visuals = [visual] };
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
        Assert.True(planHash == "CD745C0ED283FB74670F4E18E40A477406EF29FBE8FDD3DB7C7069E927E0BE5E" && svgHash == "65BF95F58D07562E2086F16DE56B73715E4D162EB7294B3FFB991AA769B9E7F2", $"Plan: {planHash}; SVG: {svgHash}");
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

    private static (ChartSpec Spec, ChartDataSet Data) Lower(string sql)
    {
        var statement = Parse(sql);
        Assert.Empty(AdvancedChartSemanticValidator.Validate(statement));
        var manifest = new VisualManifest
        {
            Name = "Measurement",
            Columns = ["Distance", "Estimate", "LowerBound", "UpperBound"],
            Rows = [["1", "10", "5", "15"], ["3", "30", "20", "40"]]
        };
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        return (spec, new VisualChartDataBuilder().Build(spec, manifest));
    }
}
