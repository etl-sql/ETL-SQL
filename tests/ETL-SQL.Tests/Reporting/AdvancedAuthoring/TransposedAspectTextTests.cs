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

public sealed class TransposedAspectTextTests
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
            ENCODINGS (
              X = Distance (TYPE = QUANTITATIVE, SCALE = distances),
              Y = Estimate (TYPE = QUANTITATIVE, SCALE = estimates)
            ),
            LAYERS (
              observations = POINT (
                POSITION = NUDGE(X = 0.5, Y = 0.5, UNIT = EM),
                ENCODINGS (
                  ERROR_LOW = LowerBound (TYPE = QUANTITATIVE, SCALE = estimates),
                  ERROR_HIGH = UpperBound (TYPE = QUANTITATIVE, SCALE = estimates)
                )
              ),
              labels = TEXT (
                POSITION = NUDGE(X = 0.5, Y = 0.5, UNIT = EM),
                ENCODINGS (TEXT = Caption (TYPE = NOMINAL))
              )
            )
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
    public void Labels_FollowTransposedPointsThroughFacetsAndRelayout(bool facets, bool reverse, bool logarithmic)
    {
        var sql = Script;
        if (facets) sql = sql.Replace("SCALES (", "FACET (WRAP = Distance, COLUMNS = 2), RESOLVE (Y = INDEPENDENT), SCALES (", StringComparison.Ordinal);
        if (reverse) sql = sql.Replace("CHANNEL = X,", "CHANNEL = X, REVERSE = ON,", StringComparison.Ordinal)
            .Replace("CHANNEL = Y,", "CHANNEL = Y, REVERSE = ON,", StringComparison.Ordinal);
        if (logarithmic) sql = sql.Replace("LINEAR (CHANNEL", "LOGARITHMIC (CHANNEL", StringComparison.Ordinal)
            .Replace("MIN = 0,", "INCLUDE_ZERO = OFF, MIN = 1,", StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        var resolver = new PlotPlanResolver();
        var original = resolver.Resolve(spec, data, new PlotBounds(0m, 0m, 1000m, 600m));
        foreach (var bounds in new[] { original.Bounds, new PlotBounds(0m, 0m, 800m, 650m) })
        {
            var plan = resolver.Relayout(spec, data, original, bounds);
            var svg = XDocument.Parse(new SvgChartRenderer().Render(plan));
            var points = Elements(svg, "plot-point");
            var labels = Elements(svg, "plot-smart-label");
            Assert.Equal(2, labels.Length);
            foreach (var label in labels)
            {
                var point = Assert.Single(points, point => point.Attribute("data-row-index")!.Value == label.Attribute("data-row-index")!.Value);
                Assert.Equal(Read(point, "cx"), Read(label, "x"));
                Assert.Equal(Read(point, "cy") - 7m, Read(label, "y"));
            }
            Assert.Equal(new[] { "<alpha>", "[beta]" }, labels.Select(label => label.Value).OrderBy(value => value, StringComparer.Ordinal));
            Assert.Equal(2, Elements(svg, "plot-error-bar-stem").Length);
            var terminal = TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 120).NormalizedText;
            Assert.Equal(1, terminal.Split("<alpha>", StringSplitOptions.None).Length - 1);
            Assert.Equal(1, terminal.Split("[beta]", StringSplitOptions.None).Length - 1);
            Assert.Equal(ChartContractSerializer.Serialize(resolver.Resolve(spec, data, bounds)), ChartContractSerializer.Serialize(plan));
            Assert.Equal(new[] { "<alpha>", "[beta]" }, plan.Fallback.Items.Skip(2).Select(item => item.Label));
        }
    }

    [Fact]
    public void ConditionalTextAndPresentation_AreRenderedAndEscaped()
    {
        var sql = Script.Replace("ENCODINGS (TEXT = Caption (TYPE = NOMINAL))", """
            ENCODINGS (TEXT = Caption (TYPE = NOMINAL)),
            CONDITIONS (
              TEXT WHEN Estimate > 5 THEN '<high>' ELSE '[low]',
              COLOR WHEN Estimate > 5 THEN '#112233' ELSE '#445566',
              SIZE WHEN Estimate > 5 THEN 14 ELSE 12,
              OPACITY WHEN Estimate > 5 THEN 0.5 ELSE 0.8
            )
            """, StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var labels = Elements(XDocument.Parse(new SvgChartRenderer().Render(plan)), "plot-smart-label");
        Assert.Equal(new[] { "[low]", "<high>" }, labels.Select(label => label.Value));
        Assert.Equal(new[] { "#445566", "#112233" }, labels.Select(label => (string?)label.Attribute("fill")));
        Assert.Equal(new[] { 12m, 14m }, labels.Select(label => Read(label, "font-size")));
        Assert.Equal(new[] { .8m, .5m }, labels.Select(label => Read(label, "opacity")));
        Assert.Equal(new[] { "[low]", "<high>" }, plan.Fallback.Items.Skip(2).Select(item => item.Label));
        var terminal = TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 100).NormalizedText;
        Assert.Contains("[low]", terminal);
        Assert.Contains("<high>", terminal);
    }

    [Fact]
    public void TextOnlyAndCrowdedLabels_RetainSemanticFallback()
    {
        var (spec, data) = Lower(Script);
        spec = spec with { Layers = [spec.Layers[1]] };
        var plan = new PlotPlanResolver().Resolve(spec, data, new PlotBounds(0m, 0m, 120m, 120m));
        var svg = XDocument.Parse(new SvgChartRenderer().Render(plan));
        Assert.NotEmpty(Elements(svg, "plot-smart-label-occluded"));
        Assert.Equal(2, Elements(svg, "plot-smart-label-occluded").Length + Elements(svg, "plot-smart-label").Length);
        Assert.Equal(new[] { "<alpha>", "[beta]" }, plan.Fallback.Items.Select(item => item.Label));
        var terminal = TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 100).NormalizedText;
        Assert.Equal(1, terminal.Split("<alpha>", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, terminal.Split("[beta]", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void MissingPositions_DoNotInventLabelLocations()
    {
        var (spec, data) = Lower(Script, missing: true);
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Empty(Elements(XDocument.Parse(new SvgChartRenderer().Render(plan)), "plot-smart-label"));
        Assert.Contains(plan.Fallback.Items, item => item.Label == "<alpha>" && item.Value == "gap");
    }

    [Theory]
    [InlineData("labels = TEXT", "labels = LINE")]
    public void UnsupportedCombinations_StillHavePositionedDiagnostics(string oldValue, string newValue)
    {
        var statement = Parse(Script.Replace(oldValue, newValue, StringComparison.Ordinal));
        Assert.Contains(AdvancedChartSemanticValidator.Validate(statement), diagnostic => diagnostic.Code == "RPT-CHART" &&
            diagnostic.Line > 0 && diagnostic.Column > 0 && diagnostic.Message.Contains("ASPECT_RATIO", StringComparison.Ordinal));
        var (spec, _) = Lower(Script);
        var layer = newValue == "labels = LINE" ? spec.Layers[1] with { Mark = MarkKind.Line }
            : spec.Layers[1] with
            {
                Position = new PositionAdjustmentSpec(PositionAdjustmentKind.Nudge, .5m, .5m,
                Unit: newValue == "UNIT = DATA" ? PositionAdjustmentUnit.Data : PositionAdjustmentUnit.Band)
            };
        var invalid = spec with { Layers = [layer] };
        Assert.Contains("ASPECT_RATIO", Assert.Throws<InvalidDataException>(invalid.Validate).Message);
    }

    [Fact]
    public async Task AuthoringContractsAndStaticExport_PreserveText()
    {
        var statement = Parse(Script);
        var tracker = new LineageTracker(ETL_SQL.Common.NullLogger.Instance);
        new ETL_SQL.Analysis.Lineage.LineageAnalyzer(tracker).Analyze(new Parser(new Lexer(Script).Tokenize(), Script).Parse());
        Assert.Contains(tracker.GetFullLineage(), entry => entry.TargetTable == "report:Measurement" && entry.SourceColumns.Contains("Caption"));
        Assert.Equal(statement.ToSql(), Parse(statement.ToSql()).ToSql());
        var source = Script + "CREATE PAGE Dashboard AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = Measurement)));";
        var parsed = new DesignerAnalysisService().Parse(source, 100);
        Assert.Null(parsed.Error);
        var patched = new DesignerScriptPatcher().Patch(source, parsed.DesignState);
        Assert.Equal(statement.ToSql(), Parse(patched).ToSql());
        var (spec, data) = Lower(Script);
        var json = ChartContractSerializer.Serialize(spec);
        Assert.Equal(json, ChartContractSerializer.Serialize(ChartContractSerializer.DeserializeChartSpec(json)));
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var planJson = ChartContractSerializer.Serialize(plan);
        Assert.Equal(planJson, ChartContractSerializer.Serialize(ChartContractSerializer.DeserializePlotPlan(planJson)));
        var svg = new SvgChartRenderer().Render(plan);
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
        Assert.True(planHash == "5EA7E992FF404D7A96AB8244A22359EFBED3244D621B8BC9C8BD91D3677B2684" && svgHash == "E49427C4AB7D7D7069A9D439C4CD52572018947F4EA889732B5FCC0EDFD823FD", $"Plan: {planHash}; SVG: {svgHash}");
    }

    private static XElement[] Elements(XDocument document, string name) => document.Descendants()
        .Where(element => (string?)element.Attribute("class") == name).ToArray();
    private static decimal Read(XElement element, string name) => decimal.Parse(element.Attribute(name)!.Value, CultureInfo.InvariantCulture);
    private static CreateVisualStatement Parse(string sql)
    {
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        return Assert.Single(script.Statements.OfType<CreateVisualStatement>());
    }
    private static (ChartSpec Spec, ChartDataSet Data) Lower(string sql, bool missing = false)
    {
        var statement = Parse(sql);
        Assert.Empty(AdvancedChartSemanticValidator.Validate(statement));
        var manifest = new VisualManifest
        {
            Name = "Measurement",
            Columns = ["Distance", "Estimate", "LowerBound", "UpperBound", "Caption"],
            Rows = missing ? [["3", null, "2", "4", "<alpha>"], [null, "7", "6", "8", "[beta]"]]
                : [["3", "3", "2", "4", "<alpha>"], ["7", "7", "6", "8", "[beta]"]]
        };
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        return (spec, new VisualChartDataBuilder().Build(spec, manifest));
    }
}
