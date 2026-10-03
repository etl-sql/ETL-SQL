using System.Collections.Immutable;
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

[Trait("CompatBreak", "0.20.0")]
public sealed class RibbonScaleTests
{
    private const string Sql = """
        CREATE VISUAL Ranges AS CUSTOM (SOURCE = #prepared, CHART (
          COORDINATE (TYPE = CARTESIAN),
          SCALES (
            unused = LINEAR (CHANNEL = Y, MIN = 0, MAX = 10),
            bounds = LINEAR (CHANNEL = Y, MIN = 100, MAX = 200)
          ),
          LAYERS (ribbon = AREA (NULL_HANDLING = GAP,
            ENCODINGS (X = Distance (TYPE = QUANTITATIVE),
              Y_START = Lower (TYPE = QUANTITATIVE, SCALE = bounds),
              Y_END = Upper (TYPE = QUANTITATIVE, SCALE = bounds)),
            STYLE (INTERPOLATION = 'LINEAR'),
            CONDITIONS (COLOR WHEN Distance < 2 THEN '#ff0000' ELSE '#0000ff')))
        ));
        """;

    [Theory]
    [InlineData("unused")]
    [InlineData(null)]
    public void AuthoringRejectsDistinctEffectiveEndpointScales(string? endScale)
    {
        var sql = endScale is null
            ? Sql.Replace("Y_END = Upper (TYPE = QUANTITATIVE, SCALE = bounds)", "Y_END = Upper (TYPE = QUANTITATIVE)")
            : Sql.Replace("Y_END = Upper (TYPE = QUANTITATIVE, SCALE = bounds)", $"Y_END = Upper (TYPE = QUANTITATIVE, SCALE = {endScale})");
        var statement = Parse(sql);
        var diagnostic = Assert.Single(AdvancedChartSemanticValidator.Validate(statement));
        Assert.Equal("RPT-CHART", diagnostic.Code);
        Assert.Contains("same Y scale", diagnostic.Message);
        Assert.True(diagnostic.Line > statement.Line);
        Assert.Throws<AdvancedChartSemanticException>(() => new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, Manifest()));
    }

    [Fact]
    public void EndpointScaleIdsResolveCaseInsensitively()
    {
        var sql = Sql.Replace("bounds = LINEAR (CHANNEL = Y, MIN = 100, MAX = 200)", "bounds = LINEAR (CHANNEL = Y)")
            .Replace("Y_END = Upper (TYPE = QUANTITATIVE, SCALE = bounds)", "Y_END = Upper (TYPE = QUANTITATIVE, SCALE = BOUNDS)");
        var statement = Parse(sql);
        Assert.Empty(AdvancedChartSemanticValidator.Validate(statement));
        var manifest = Manifest();
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        var plan = new PlotPlanResolver().Resolve(spec, new VisualChartDataBuilder().Build(spec, manifest));
        var scale = plan.Scales.Single(scale => scale.Id == "bounds");
        Assert.True(PlotPlanResolver.Number(scale.Domain[^1]) >= 180m);
        Assert.Equal("bounds", plan.Layers[0].AreaRibbonScaleId);
    }

    [Fact]
    public void IndependentFacetDomainsIncludeMixedCaseRibbonBounds()
    {
        var (spec, _) = Lower();
        var resolution = new ScaleResolutionSpec(Y: ScaleResolutionMode.Independent);
        spec = spec with
        {
            Schema = ChartContractVersions.ChartSpecSchema,
            Version = 2,
            Scales = spec.Scales.Select(scale => scale.Id == "bounds" ? scale with { DomainMinimum = null, DomainMaximum = null } : scale).ToImmutableArray(),
            Bindings = spec.Bindings.Add(new FieldBinding(FieldChannel.Column, "Cohort", DataSemanticKind.Nominal)),
            Layers = [spec.Layers[0] with { Conditions = [], Bindings = spec.Layers[0].Bindings.Select(binding => binding.Channel == FieldChannel.YEnd ? binding with { ScaleId = "BOUNDS" } : binding).ToImmutableArray() }],
            Facet = new FacetSpec(null, "Cohort", resolution),
            ScaleResolution = resolution
        };
        var manifest = Manifest();
        manifest.Columns.Add("Cohort");
        for (var row = 0; row < manifest.Rows.Count; row++) manifest.Rows[row].Add(row < 2 ? "A" : "B");
        var plan = new PlotPlanResolver().Resolve(spec, new VisualChartDataBuilder().Build(spec, manifest));
        var facet = plan.Facets.Single(facet => facet.ColumnLabel == "B");
        Assert.True(PlotPlanResolver.Number(facet.Scales.Single(scale => scale.Id == "bounds").Domain[^1]) >= 180m);
        Assert.Contains("plot-ribbon", new SvgChartRenderer().Render(plan));
    }

    [Fact]
    public void ContractRejectsDistinctEndpointScales()
    {
        var (spec, _) = Lower();
        var layer = spec.Layers[0];
        var invalid = spec with
        {
            Layers = [layer with { Bindings = layer.Bindings.Select(binding => binding.Channel == FieldChannel.YEnd
                ? binding with { ScaleId = "unused" } : binding).ToImmutableArray() }]
        };
        Assert.Contains("same Y scale", Assert.Throws<InvalidDataException>(invalid.Validate).Message);
        Assert.Throws<InvalidDataException>(() => ChartContractSerializer.Serialize(invalid));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RenderingUsesSharedEndpointScaleEvenWhenDeclaredSecond(bool reverse)
    {
        var (spec, data) = Lower();
        spec = spec with { Scales = spec.Scales.Select(scale => scale.Id == "bounds" ? scale with { Reverse = reverse } : scale).ToImmutableArray() };
        var resolver = new PlotPlanResolver();
        var plan = resolver.Resolve(spec, data);
        var expectedSpec = spec with { Scales = spec.Scales.Where(scale => scale.Id != "unused").ToImmutableArray() };
        foreach (var candidate in new[] { plan, resolver.Relayout(spec, data, plan, new PlotBounds(0m, 0m, 850m, 500m)) })
        {
            var expected = resolver.Resolve(expectedSpec, data, candidate.Bounds);
            Assert.Equal(Geometry(expected), Geometry(candidate));
            Assert.Equal(AxisLabels(expected), AxisLabels(candidate));
            var json = ChartContractSerializer.Serialize(candidate);
            Assert.Equal(Geometry(expected), Geometry(ChartContractSerializer.DeserializePlotPlan(json)));
            Assert.Equal(ChartContractSerializer.Serialize(resolver.Resolve(spec, data, candidate.Bounds)), json);
            Assert.Equal(expected.Fallback.Items.ToArray(), candidate.Fallback.Items.ToArray());
            var oldMapping = candidate with
            {
                Schema = ChartContractVersions.ConnectedPlotPlanSchema,
                Version = ChartContractVersions.ConnectedPlotPlanVersion,
                Layers = [candidate.Layers[0] with { AreaRibbonScaleId = null }]
            };
            Assert.NotEqual(Geometry(expected)[0], Geometry(oldMapping)[0]);
        }
    }

    [Fact]
    public async Task WireGuardsAuthoringAndExportPreserveSelectedScale()
    {
        var (spec, data) = Lower();
        var statement = Parse(Sql);
        Assert.Equal(statement.ToSql(), Parse(statement.ToSql()).ToSql());
        var source = Sql + "CREATE PAGE Dashboard AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = Ranges)));";
        var design = new DesignerAnalysisService().Parse(source, 100);
        Assert.Null(design.Error);
        Assert.Equal(statement.ToSql(), Parse(new DesignerScriptPatcher().Patch(source, design.DesignState)).ToSql());
        var resolver = new PlotPlanResolver();
        var plan = resolver.Resolve(spec, data);
        Assert.Equal(6, plan.Version);
        Assert.Equal("bounds", plan.Layers[0].AreaRibbonScaleId);
        var tracker = new LineageTracker(ETL_SQL.Common.NullLogger.Instance);
        new ETL_SQL.Analysis.Lineage.LineageAnalyzer(tracker).Analyze(new Parser(new Lexer(Sql).Tokenize(), Sql).Parse());
        foreach (var field in new[] { "Distance", "Lower", "Upper" })
            Assert.Contains(tracker.GetFullLineage(), entry => entry.TargetTable == "report:Ranges" && entry.SourceColumns.Contains(field));
        Assert.Throws<InvalidDataException>(() => (plan with { Version = 5, Schema = ChartContractVersions.ConnectedPlotPlanSchema }).Validate());
        foreach (var scaleId in new[] { "missing", "inferred-cartesian-primary-x" })
            Assert.Throws<InvalidDataException>(() => (plan with { Layers = [plan.Layers[0] with { AreaRibbonScaleId = scaleId }] }).Validate());
        var terminal = TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 180).NormalizedText;
        Assert.Contains("Y 110 to 140", terminal);
        Assert.Contains(plan.Fallback.Items, item => item.Value == "X 1; Y 110 to 140");
        var report = new ReportManifest
        {
            Title = "Ranges",
            Source = "ranges.rptsql",
            Visuals =
            [new VisualManifest { Name = "Ranges", VisualType = "CUSTOM", PlotPlan = plan, NativeSvg = new SvgChartRenderer().Render(plan) }]
        };
        Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, (await new PdfExporter().ExportAsync(report))[..4]);
        var empty = data with { RowCount = 0, Columns = data.Columns.Select(column => column with { Values = [], DisplayValues = [] }).ToImmutableArray() };
        var emptyPlan = resolver.Resolve(spec, empty);
        Assert.Equal("bounds", emptyPlan.Layers[0].AreaRibbonScaleId);
        Assert.Empty(Geometry(ChartContractSerializer.DeserializePlotPlan(ChartContractSerializer.Serialize(emptyPlan))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnconditionalRibbonsAlsoUseSharedScale(bool grouped)
    {
        // Derive intent from the accepted AST so this also covers unconditioned contract consumers.
        var statement = Parse(Sql);
        var chart = statement.AdvancedChart!;
        var layer = chart.Layers[0] with { Conditions = [] };
        statement = statement with { AdvancedChart = chart with { Layers = [layer] } };
        var manifest = Manifest();
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        if (grouped)
        {
            var color = new FieldBinding(FieldChannel.Color, "Cohort", DataSemanticKind.Nominal);
            manifest.Columns.Add("Cohort");
            for (var row = 0; row < manifest.Rows.Count; row++) manifest.Rows[row].Add("control");
            spec = spec with { Bindings = spec.Bindings.Add(color), Layers = [spec.Layers[0] with { Bindings = spec.Layers[0].Bindings.Add(color) }] };
        }
        var data = new VisualChartDataBuilder().Build(spec, manifest);
        var resolver = new PlotPlanResolver();
        var plan = resolver.Resolve(spec, data);
        var expected = resolver.Resolve(spec with { Scales = spec.Scales.Where(scale => scale.Id != "unused").ToImmutableArray() }, data);
        string[] Paths(PlotPlan candidate) => XDocument.Parse(new SvgChartRenderer().Render(candidate)).Descendants()
            .Where(element => element.Name.LocalName == "path").Select(element => element.Attribute("d")!.Value).ToArray();
        Assert.NotEmpty(Paths(plan));
        Assert.Equal(Paths(expected), Paths(plan));
        Assert.Equal(AxisLabels(expected), AxisLabels(plan));
        Assert.Equal("bounds", Assert.Single(plan.Layers).AreaRibbonScaleId);
        Assert.Equal(6, plan.Version);
    }

    [Fact]
    public void ExplicitScalePlanAndSvgGoldens()
    {
        var (spec, data) = Lower();
        var plan = new PlotPlanResolver().Resolve(spec, data);
        static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n", StringComparison.Ordinal))));
        var planHash = Hash(ChartContractSerializer.Serialize(plan));
        var svgHash = Hash(new SvgChartRenderer().Render(plan));
        Assert.True(planHash == "5D54B5026AB41959586F7D34FB4277714D7DCE7050C3E65F846EDE27324AF0CA" && svgHash == "A58EEDFA4FE6954D57AB41298C950574161C018B58BBE0B107A547562CFDC2C3", $"Plan: {planHash}; SVG: {svgHash}");
    }

    [Fact]
    public void ConformanceDetectsDiscardedRibbonScale()
    {
        var (spec, data) = Lower();
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var report = PlotPlanConformanceHarness.Evaluate(plan, [new LostScaleBackend()]);
        Assert.Equal("layers", Assert.Single(report.Issues).SemanticArea);
    }

    private sealed record LostScaleBackend : IPlotPlanSemanticBackend
    {
        public string Name => "lost-ribbon-scale";
        public PlotSemanticProjection Project(PlotPlan plan)
        {
            var projection = PlotSemanticProjection.FromPlan(plan);
            return projection with { Layers = projection.Layers.Select(layer => layer with { AreaRibbonScaleId = null }).ToImmutableArray() };
        }
    }

    private static string[] Geometry(PlotPlan plan) => XDocument.Parse(new SvgChartRenderer().Render(plan)).Descendants()
        .Where(element => (string?)element.Attribute("class") == "plot-conditional-area")
        .Select(element => element.Attribute("d")!.Value).ToArray();

    private static string[] AxisLabels(PlotPlan plan) => XDocument.Parse(new SvgChartRenderer().Render(plan)).Descendants()
        .Where(element => element.Name.LocalName == "text").Select(element => element.Value).ToArray();

    private static CreateVisualStatement Parse(string sql)
    {
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        return Assert.Single(script.Statements.OfType<CreateVisualStatement>());
    }

    private static VisualManifest Manifest() => new()
    {
        Name = "Ranges",
        Columns = ["Distance", "Lower", "Upper"],
        Rows = [["1", "110", "140"], ["2", "150", "130"], ["4", "120", "180"]]
    };

    private static (ChartSpec Spec, ChartDataSet Data) Lower()
    {
        var statement = Parse(Sql);
        Assert.Empty(AdvancedChartSemanticValidator.Validate(statement));
        var manifest = Manifest();
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        return (spec, new VisualChartDataBuilder().Build(spec, manifest));
    }
}
