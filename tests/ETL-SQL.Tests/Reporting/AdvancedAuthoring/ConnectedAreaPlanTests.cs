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

public sealed class ConnectedAreaPlanTests
{
    private const string Sql = """
        CREATE VISUAL Route AS CUSTOM (SOURCE = #prepared, CHART (
          COORDINATE (TYPE = CARTESIAN),
          LAYERS (route = AREA (NULL_HANDLING = GAP, AREA_BASELINE = ZERO,
            ENCODINGS (X = Distance (TYPE = QUANTITATIVE), Y = Estimate (TYPE = QUANTITATIVE)),
            STYLE (INTERPOLATION = 'LINEAR'),
            CONDITIONS (COLOR WHEN Distance < 2 THEN '#ff0000' ELSE '#0000ff',
              OPACITY WHEN Distance < 2 THEN 0.5 ELSE 1)))
        ));
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StripsShareCrossSectionsAndZeroBaseline(bool reverse)
    {
        var (spec, data) = Lower();
        spec = spec with { Scales = spec.Scales.Select(scale => scale with { Reverse = reverse }).ToImmutableArray() };
        var resolver = new PlotPlanResolver();
        var plan = resolver.Resolve(spec, data);
        Assert.Equal(3, spec.Version);
        Assert.Equal(5, plan.Version);
        Assert.True(plan.Scales.Single(scale => scale.Channel == FieldChannel.Y).IncludesZero);
        foreach (var candidate in new[] { plan, resolver.Relayout(spec, data, plan, new PlotBounds(0m, 0m, 850m, 500m)) })
        {
            var paths = Paths(candidate);
            Assert.Equal(2, paths.Length);
            Assert.Equal(new[] { "#ff0000", "#0000ff" }, paths.Select(path => (string?)path.Attribute("fill")));
            Assert.Equal(new[] { "0.5", "1" }, paths.Select(path => (string?)path.Attribute("opacity")));
            Assert.All(paths, path => Assert.Equal("none", (string?)path.Attribute("stroke")));
            var first = paths[0].Attribute("d")!.Value.Split(' ');
            var second = paths[1].Attribute("d")!.Value.Split(' ');
            Assert.Equal(first[4..6], second[1..3]);
            Assert.Equal(first[7..9], second[10..12]);
            Assert.Equal(first[8], first[11]);
            // Signed values lie on opposite sides of the same semantic zero baseline.
            decimal N(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
            Assert.True((N(first[2]) - N(first[8])) * (N(first[5]) - N(first[8])) < 0m);
            var terminal = TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(candidate), 180).NormalizedText;
            foreach (var connection in candidate.Layers[0].Connections)
            {
                var description = ConnectedMarkResolver.Describe(connection);
                Assert.Contains(description, terminal);
                Assert.Contains(paths, path => path.Value == description);
                Assert.Contains(candidate.Fallback.Items, item => item.Value == description);
            }
            Assert.Equal(ChartContractSerializer.Serialize(resolver.Resolve(spec, data, candidate.Bounds)), ChartContractSerializer.Serialize(candidate));
        }
    }

    [Fact]
    public void PositiveValuesIncludeZeroAndGapsBreakStrips()
    {
        var (spec, data) = Lower();
        data = data with
        {
            Columns = data.Columns.Select(column => column.Name == "Estimate"
            ? column with { Values = [ChartValue.From(2m), ChartValue.From(3m), ChartValue.From(4m)], DisplayValues = [] } : column).ToImmutableArray()
        };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Equal(0m, PlotPlanResolver.Number(plan.Scales.Single(scale => scale.Channel == FieldChannel.Y).Domain[0]));
        data = data with
        {
            Columns = data.Columns.Select(column => column.Name == "Estimate"
            ? column with { Values = column.Values.SetItem(1, ChartValue.Null()) } : column).ToImmutableArray()
        };
        plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Empty(plan.Layers[0].Connections);
        Assert.Empty(Paths(plan));
        Assert.DoesNotContain(plan.Fallback.Items, item => item.Detail == "outgoing connection");
    }

    [Fact]
    public async Task AuthoringContractsLineageAndPdfRoundTrip()
    {
        var statement = Parse(Sql);
        Assert.Equal(statement.ToSql(), Parse(statement.ToSql()).ToSql());
        var source = Sql + "CREATE PAGE Dashboard AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = Route)));";
        var design = new DesignerAnalysisService().Parse(source, 100);
        Assert.Null(design.Error);
        Assert.Equal(statement.ToSql(), Parse(new DesignerScriptPatcher().Patch(source, design.DesignState)).ToSql());
        var tracker = new LineageTracker(ETL_SQL.Common.NullLogger.Instance);
        new ETL_SQL.Analysis.Lineage.LineageAnalyzer(tracker).Analyze(new Parser(new Lexer(Sql).Tokenize(), Sql).Parse());
        foreach (var field in new[] { "Distance", "Estimate" })
            Assert.Contains(tracker.GetFullLineage(), entry => entry.TargetTable == "report:Route" && entry.SourceColumns.Contains(field));
        var (spec, data) = Lower();
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Equal(ChartContractSerializer.Serialize(spec), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializeChartSpec(ChartContractSerializer.Serialize(spec))));
        Assert.Equal(ChartContractSerializer.Serialize(plan), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializePlotPlan(ChartContractSerializer.Serialize(plan))));
        var report = new ReportManifest
        {
            Title = "Route",
            Source = "route.rptsql",
            Visuals = [new VisualManifest { Name = "Route", VisualType = "CUSTOM", PlotPlan = plan, NativeSvg = new SvgChartRenderer().Render(plan) }]
        };
        Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, (await new PdfExporter().ExportAsync(report))[..4]);
        Assert.Throws<InvalidDataException>(() => (spec with { Version = 2, Schema = ChartContractVersions.ChartSpecSchema }).Validate());
        Assert.Throws<InvalidDataException>(() => (plan with { Version = 3, Schema = ChartContractVersions.PlotPlanSchema }).Validate());
        Assert.Throws<InvalidDataException>(() => (plan with
        {
            Scales = plan.Scales.Select(scale => scale.Channel == FieldChannel.Y
            ? scale with { Domain = [ChartValue.From(1m), ChartValue.From(4m)] } : scale).ToImmutableArray()
        }).Validate());
        Assert.Throws<InvalidDataException>(() => (spec with
        {
            Layers = [spec.Layers[0] with
            { Style = spec.Layers[0].Style.Where(token => token.Name != "areaBaseline").ToImmutableArray() }]
        }).Validate());
    }

    [Theory]
    [InlineData("AREA_BASELINE = ZERO", "AREA_BASELINE = 2")]
    [InlineData("AREA_BASELINE = ZERO,", "")]
    [InlineData("NULL_HANDLING = GAP", "NULL_HANDLING = ZERO")]
    public void InvalidFormsRemainRejected(string before, string after) =>
        Assert.Contains(AdvancedChartSemanticValidator.Validate(Parse(Sql.Replace(before, after, StringComparison.Ordinal))),
            diagnostic => diagnostic.Message.Contains("Connected CONDITIONS", StringComparison.Ordinal));

    [Fact]
    public void Goldens()
    {
        var (spec, data) = Lower();
        var plan = new PlotPlanResolver().Resolve(spec, data);
        static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n", StringComparison.Ordinal))));
        var planHash = Hash(ChartContractSerializer.Serialize(plan));
        var svgHash = Hash(new SvgChartRenderer().Render(plan));
        Assert.True(planHash == "B6EE7941B936A8E1FA41CAF40E12DBBFB1E302DAE513CCF5AC4F6E7DA7AEA29A" && svgHash == "2DD8D712387F620ABE307C36E4FA5898056AACE993E29E758F1E33CD616479DA", $"Plan: {planHash}; SVG: {svgHash}");
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
        var manifest = new VisualManifest { Name = "Route", Columns = ["Distance", "Estimate"], Rows = [["1", "2"], ["2", "-1"], ["4", "3"]] };
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        return (spec, new VisualChartDataBuilder().Build(spec, manifest));
    }
}
