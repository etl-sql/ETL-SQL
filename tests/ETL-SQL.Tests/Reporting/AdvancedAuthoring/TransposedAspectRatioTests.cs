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

namespace ETL_SQL.Tests.Reporting.AdvancedAuthoring;

public sealed class TransposedAspectRatioTests
{
    private const string Script = """
        CREATE VISUAL PhysicalScatter AS CUSTOM (
          SOURCE = #prepared,
          CHART (
            COORDINATE (TYPE = TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2),
            SCALES (
              horizontal = LINEAR (CHANNEL = X, MIN = 0, MAX = 10),
              vertical = LINEAR (CHANNEL = Y, MIN = 0, MAX = 20)
            ),
            LAYERS (observations = POINT (ENCODINGS (
              X = Distance (TYPE = QUANTITATIVE, SCALE = horizontal),
              Y = Elevation (TYPE = QUANTITATIVE, SCALE = vertical)
            )))
          )
        );
        """;

    [Theory]
    [InlineData(600, 350)]
    [InlineData(300, 600)]
    public void PhysicalUnits_ArePreservedInPlanAndSvg(int width, int height)
    {
        var (spec, data) = Lower(Script);
        var plan = new PlotPlanResolver().Resolve(spec, data, new PlotBounds(0m, 0m, width, height));
        var viewport = Assert.IsType<PlotBounds>(plan.CartesianViewport);
        Assert.Equal(2m, Math.Round(((viewport.Width - 80m) / 20m) / ((viewport.Height - 100m) / 10m), 8));

        var serialized = ChartContractSerializer.Serialize(plan);
        var svg = new SvgChartRenderer().Render(plan);
        Assert.Equal(svg, new SvgChartRenderer().Render(ChartContractSerializer.DeserializePlotPlan(serialized)));
        Assert.Equal(serialized, ChartContractSerializer.Serialize(plan));
        Assert.Equal(svg, new SvgChartRenderer().Render(new VisualManifest { PlotPlan = plan }));
        var points = XDocument.Parse(svg).Descendants().Where(element =>
            element.Name.LocalName == "circle" && (string?)element.Attribute("class") == "plot-point").ToArray();
        Assert.Equal(3, points.Length);
        decimal Read(int index, string attribute) => decimal.Parse(points[index].Attribute(attribute)!.Value, CultureInfo.InvariantCulture);
        var yUnit = (Read(1, "cx") - Read(0, "cx")) / 10m;
        var xUnit = (Read(0, "cy") - Read(1, "cy")) / 5m;
        Assert.InRange(yUnit / xUnit, 1.999m, 2.001m);
        Assert.Equal(["0", "1", "2"], points.Select(point => (string?)point.Attribute("data-row-index")));
        Assert.NotNull(PlotPlanTerminalRenderer.Render(plan));
        Assert.Equal(3, plan.Fallback.Items.Length);
    }

    [Fact]
    public void AuthoringAndContractRoundTrips_PreserveTheCombination()
    {
        var (spec, _) = Lower(Script);
        var json = ChartContractSerializer.Serialize(spec);
        Assert.Equal(json, ChartContractSerializer.Serialize(ChartContractSerializer.DeserializeChartSpec(json)));
        var statement = Parse(Script);
        Assert.Equal(statement.ToSql(), Parse(statement.ToSql()).ToSql());
        var source = Script + "CREATE PAGE Dashboard AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = PhysicalScatter)));";
        var parsed = new DesignerAnalysisService().Parse(source, 100);
        Assert.Null(parsed.Error);
        var patched = new DesignerScriptPatcher().Patch(source, parsed.DesignState);
        Assert.Contains("TYPE = TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2", patched);
    }

    [Theory]
    [InlineData("POINT", "LINE")]
    [InlineData("TYPE = TRANSPOSED_CARTESIAN", "TYPE = POLAR")]
    [InlineData("ASPECT_RATIO = 2", "ASPECT_RATIO = 0")]
    public void UnsupportedCombinations_StillFailSemanticValidation(string oldText, string newText)
    {
        var statement = Parse(Script.Replace(oldText, newText, StringComparison.Ordinal));
        Assert.NotEmpty(AdvancedChartSemanticValidator.Validate(statement));
    }

    [Fact]
    public void ContractBackstop_RejectsUnsupportedTransposedMarks()
    {
        var (spec, _) = Lower(Script);
        var invalid = spec with { Layers = [spec.Layers[0] with { Mark = MarkKind.Area }] };
        Assert.Contains("supports POINT layers", Assert.Throws<InvalidDataException>(invalid.Validate).Message);
    }

    [Fact]
    public void CartesianBehavior_RemainsUnchanged()
    {
        var (spec, data) = Lower(Script.Replace("TRANSPOSED_CARTESIAN", "CARTESIAN", StringComparison.Ordinal));
        var plan = new PlotPlanResolver().Resolve(spec, data, new PlotBounds(0m, 0m, 600m, 350m));
        var viewport = Assert.IsType<PlotBounds>(plan.CartesianViewport);
        Assert.Equal(2m, ((viewport.Height - 100m) / 20m) / ((viewport.Width - 80m) / 10m));
    }

    [Fact]
    public void LogarithmicDomainsAndRelayout_PreserveRatioPerDecade()
    {
        var sql = Script.Replace("LINEAR (CHANNEL = X, MIN = 0, MAX = 10)",
            "LOGARITHMIC (CHANNEL = X, INCLUDE_ZERO = OFF, MIN = 1, MAX = 100)", StringComparison.Ordinal)
            .Replace("LINEAR (CHANNEL = Y, MIN = 0, MAX = 20)",
            "LOGARITHMIC (CHANNEL = Y, INCLUDE_ZERO = OFF, MIN = 1, MAX = 1000)", StringComparison.Ordinal);
        var (spec, data) = Lower(sql, positive: true);
        var resolver = new PlotPlanResolver();
        var plan = resolver.Resolve(spec, data);
        var resized = resolver.Relayout(spec, data, plan, new PlotBounds(0m, 0m, 300m, 400m));
        foreach (var candidate in new[] { plan, resized })
        {
            var viewport = Assert.IsType<PlotBounds>(candidate.CartesianViewport);
            Assert.Equal(2m, Math.Round(((viewport.Width - 80m) / 3m) / ((viewport.Height - 100m) / 2m), 8));
        }
        Assert.Equal(ChartContractSerializer.Serialize(resolver.Resolve(spec, data, resized.Bounds)),
            ChartContractSerializer.Serialize(resized));
    }

    [Fact]
    public void FacetViewports_AreResolvedIndividually()
    {
        var sql = Script.Replace("SCALES (", "FACET (WRAP = Distance, COLUMNS = 2), SCALES (", StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        var plan = new PlotPlanResolver().Resolve(spec, data, new PlotBounds(0m, 0m, 800m, 600m));
        Assert.Equal(3, plan.Facets.Length);
        foreach (var panel in plan.Facets)
        {
            var viewport = Assert.IsType<PlotBounds>(panel.CartesianViewport);
            Assert.Equal(2m, Math.Round(((viewport.Width - 80m) / 20m) / ((viewport.Height - 100m) / 10m), 8));
        }
        Assert.Contains("plot-aspect-viewport", new SvgChartRenderer().Render(plan));
    }

    [Fact]
    public void DeterministicPlanAndSvg_MatchGoldens()
    {
        var (spec, data) = Lower(Script);
        var plan = new PlotPlanResolver().Resolve(spec, data);
        static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Replace("\r\n", "\n", StringComparison.Ordinal))));
        var planHash = Hash(ChartContractSerializer.Serialize(plan));
        var svgHash = Hash(new SvgChartRenderer().Render(plan));
        Assert.True(planHash == "776504C9931F7A2FC4AA680147C28179BADB572F6CC20ADF317C174DAF28D773" && svgHash == "E672493C0B045684CEA8192ADC1CF6A7760325F2AA6C84201F82D57C7F9D6D74", $"Plan: {planHash}; SVG: {svgHash}");
    }

    private static CreateVisualStatement Parse(string sql)
    {
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        return Assert.Single(script.Statements.OfType<CreateVisualStatement>());
    }

    private static (ChartSpec Spec, ChartDataSet Data) Lower(string sql, bool positive = false)
    {
        var statement = Parse(sql);
        Assert.Empty(AdvancedChartSemanticValidator.Validate(statement));
        var manifest = new VisualManifest
        {
            Name = "PhysicalScatter",
            Columns = ["Distance", "Elevation"],
            Rows = positive ? [["1", "1"], ["10", "10"], ["100", "1000"]] : [["0", "0"], ["5", "10"], ["10", "20"]]
        };
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        return (spec, new VisualChartDataBuilder().Build(spec, manifest));
    }
}
