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

public sealed class ConnectedRibbonTests
{
    private const string Sql = """
        CREATE VISUAL Route AS CUSTOM (SOURCE = #prepared, CHART (
          COORDINATE (TYPE = CARTESIAN),
          LAYERS (route = AREA (NULL_HANDLING = GAP,
            ENCODINGS (X = Distance (TYPE = QUANTITATIVE),
              Y_START = Lower (TYPE = QUANTITATIVE), Y_END = Upper (TYPE = QUANTITATIVE)),
            STYLE (INTERPOLATION = 'LINEAR'),
            CONDITIONS (COLOR WHEN Distance < 2 THEN '#ff0000' ELSE '#0000ff',
              OPACITY WHEN Distance < 2 THEN 0.5 ELSE 1)))
        ));
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StripsPreserveCrossingBoundsAndPhysicalX(bool reverse)
    {
        var (spec, data) = Lower();
        spec = spec with { Scales = spec.Scales.Select(scale => scale with { Reverse = reverse }).ToImmutableArray() };
        var resolver = new PlotPlanResolver();
        var plan = resolver.Resolve(spec, data);
        var y = plan.Scales.Single(scale => scale.Channel == FieldChannel.Y);
        Assert.False(y.IncludesZero);
        Assert.InRange(PlotPlanResolver.Number(y.Domain[0])!.Value, 0.01m, 2m);
        Assert.True(PlotPlanResolver.Number(y.Domain[^1]) >= 7m);
        foreach (var candidate in new[] { plan, resolver.Relayout(spec, data, plan, new PlotBounds(0m, 0m, 850m, 500m)) })
        {
            Assert.True(candidate.Layers[0].AreaRibbon);
            var paths = Paths(candidate);
            Assert.Equal(2, paths.Length);
            Assert.Equal(new[] { "#ff0000", "#0000ff" }, paths.Select(path => (string?)path.Attribute("fill")));
            Assert.Equal(new[] { "0.5", "1" }, paths.Select(path => (string?)path.Attribute("opacity")));
            Assert.All(paths, path => Assert.Equal("none", (string?)path.Attribute("stroke")));
            var first = paths[0].Attribute("d")!.Value.Split(' ');
            var second = paths[1].Attribute("d")!.Value.Split(' ');
            Assert.Equal(first[4..6], second[1..3]);
            Assert.Equal(first[7..9], second[10..12]);
            decimal N(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
            Assert.True((N(first[2]) - N(first[11])) * (N(first[5]) - N(first[8])) < 0m);
            Assert.InRange(Math.Abs((N(second[4]) - N(second[1])) / (N(first[4]) - N(first[1])) - 2m), 0m, .001m);
            var terminal = TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(candidate), 180).NormalizedText;
            foreach (var datum in candidate.Layers[0].Data)
            {
                var description = ConnectedMarkResolver.RibbonDescription(datum);
                Assert.Contains(description, terminal);
                Assert.Contains(candidate.Fallback.Items, item => item.Value == description);
            }
            foreach (var connection in candidate.Layers[0].Connections)
                Assert.Contains(paths, path => path.Value == ConnectedMarkResolver.Describe(connection));
            Assert.Equal(ChartContractSerializer.Serialize(resolver.Resolve(spec, data, candidate.Bounds)), ChartContractSerializer.Serialize(candidate));
        }
    }

    [Theory]
    [InlineData("Lower")]
    [InlineData("Upper")]
    public void EitherMissingBoundBreaksConnections(string field)
    {
        var (spec, data) = Lower();
        data = data with
        {
            Columns = data.Columns.Select(column => column.Name == field
            ? column with { Values = column.Values.SetItem(1, ChartValue.Null()) } : column).ToImmutableArray()
        };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Empty(plan.Layers[0].Connections);
        Assert.Empty(Paths(plan));
        Assert.Contains(plan.Fallback.Items, item => item.Value == "gap");
    }

    [Fact]
    public void EmptyRibbonRetainsGeometryAndRoundTrips()
    {
        var (spec, data) = Lower();
        data = data with { RowCount = 0, Columns = data.Columns.Select(column => column with { Values = [], DisplayValues = [] }).ToImmutableArray() };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.True(plan.Layers[0].AreaRibbon);
        Assert.Empty(plan.Layers[0].Data);
        Assert.Empty(Paths(plan));
        Assert.Contains("Route", TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 180).NormalizedText);
        Assert.Equal(ChartContractSerializer.Serialize(plan), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializePlotPlan(ChartContractSerializer.Serialize(plan))));
    }

    [Fact]
    public async Task AuthoringLineageCompatibilityAndPdf()
    {
        var statement = Parse(Sql);
        Assert.Equal(statement.ToSql(), Parse(statement.ToSql()).ToSql());
        var source = Sql + "CREATE PAGE Dashboard AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = Route)));";
        var design = new DesignerAnalysisService().Parse(source, 100);
        Assert.Null(design.Error);
        Assert.Equal(statement.ToSql(), Parse(new DesignerScriptPatcher().Patch(source, design.DesignState)).ToSql());
        var tracker = new LineageTracker(ETL_SQL.Common.NullLogger.Instance);
        new ETL_SQL.Analysis.Lineage.LineageAnalyzer(tracker).Analyze(new Parser(new Lexer(Sql).Tokenize(), Sql).Parse());
        foreach (var field in new[] { "Distance", "Lower", "Upper" })
            Assert.Contains(tracker.GetFullLineage(), entry => entry.TargetTable == "report:Route" && entry.SourceColumns.Contains(field));
        var (spec, data) = Lower();
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Equal(ChartContractSerializer.Serialize(spec), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializeChartSpec(ChartContractSerializer.Serialize(spec))));
        Assert.Equal(ChartContractSerializer.Serialize(plan), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializePlotPlan(ChartContractSerializer.Serialize(plan))));
        Assert.Throws<InvalidDataException>(() => (plan with { Layers = [plan.Layers[0] with { AreaRibbon = false }] }).Validate());
        Assert.Throws<InvalidDataException>(() => (plan with { Version = 3, Schema = ChartContractVersions.PlotPlanSchema }).Validate());
        var report = new ReportManifest
        {
            Title = "Route",
            Source = "route.rptsql",
            Visuals =
            [new VisualManifest { Name = "Route", VisualType = "CUSTOM", PlotPlan = plan, NativeSvg = new SvgChartRenderer().Render(plan) }]
        };
        Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, (await new PdfExporter().ExportAsync(report))[..4]);
        Assert.Contains(AdvancedChartSemanticValidator.Validate(Parse(Sql.Replace("NULL_HANDLING = GAP,", "NULL_HANDLING = GAP, AREA_BASELINE = ZERO,", StringComparison.Ordinal))),
            diagnostic => diagnostic.Message.Contains("Connected CONDITIONS", StringComparison.Ordinal));
        Assert.Throws<InvalidDataException>(() => (spec with { Layers = [spec.Layers[0] with { Style = spec.Layers[0].Style.Add(new("areaBaseline", "ZERO")) }] }).Validate());
    }

    [Fact]
    public void Goldens()
    {
        var (spec, data) = Lower();
        var plan = new PlotPlanResolver().Resolve(spec, data);
        static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n", StringComparison.Ordinal))));
        var planHash = Hash(ChartContractSerializer.Serialize(plan));
        var svgHash = Hash(new SvgChartRenderer().Render(plan));
        Assert.True(planHash == "754AB541621BF4A10514F410DD815D2A135F5222D304BD4314B4610CC7BA4C90" && svgHash == "C166265A71834C8CFE2F12A8F353A13A86B68A54311E9AAE6D9FEFA4AAB2C6E4", $"Plan: {planHash}; SVG: {svgHash}");
    }
    private static XElement[] Paths(PlotPlan plan) => XDocument.Parse(new SvgChartRenderer().Render(plan)).Descendants()
        .Where(element => (string?)element.Attribute("class") == "plot-conditional-area").ToArray();
    private static CreateVisualStatement Parse(string sql)
    {
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        return Assert.Single(script.Statements.OfType<CreateVisualStatement>());
    }
    private static (ChartSpec Spec, ChartDataSet Data) Lower()
    {
        var statement = Parse(Sql);
        Assert.Empty(AdvancedChartSemanticValidator.Validate(statement));
        var manifest = new VisualManifest { Name = "Route", Columns = ["Distance", "Lower", "Upper"], Rows = [["1", "2", "5"], ["2", "6", "3"], ["4", "4", "7"]] };
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        return (spec, new VisualChartDataBuilder().Build(spec, manifest));
    }
}
