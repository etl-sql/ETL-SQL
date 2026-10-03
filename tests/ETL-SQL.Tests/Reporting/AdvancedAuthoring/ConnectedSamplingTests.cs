using System.Globalization;
using System.Xml.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Reporting.Semantics.Runtime;

namespace ETL_SQL.Tests.Reporting.AdvancedAuthoring;

[Trait("CompatBreak", "0.20.0")]
public sealed class ConnectedSamplingTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var area in new[] { false, true })
            foreach (var transposed in new[] { false, true })
                foreach (var mode in new[] { "LTTB", "AVERAGE", "MAX", "MIN" }) yield return [area, transposed, mode];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void DenseConnectionsRetainExactEndpointGeometryAndSourceIdentity(bool area, bool transposed, string mode)
    {
        var sql = $$"""
            CREATE VISUAL Routes AS CUSTOM (SOURCE = #prepared, CHART (
              COORDINATE (TYPE = {{(transposed ? "TRANSPOSED_CARTESIAN" : "CARTESIAN")}}),
              LAYERS (route = {{(area ? "AREA (AREA_BASELINE = ZERO," : "LINE (")}}
                NULL_HANDLING = GAP,
                ENCODINGS (X = Distance (TYPE = QUANTITATIVE), Y = Estimate (TYPE = QUANTITATIVE)),
                STYLE (INTERPOLATION = 'LINEAR'),
                CONDITIONS (COLOR WHEN Distance < 200 THEN '#ff0000' ELSE '#0000ff')))
            ));
            """;
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        var manifest = new VisualManifest
        {
            Name = "Routes",
            Columns = ["Distance", "Estimate"],
            Rows = Enumerable.Range(0, 400).Select(index => new List<string?>
                { index.ToString(CultureInfo.InvariantCulture), (index % 19).ToString(CultureInfo.InvariantCulture) }).ToList()
        };
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(Assert.Single(script.Statements.OfType<CreateVisualStatement>()), manifest);
        var plan = new PlotPlanResolver().Resolve(spec, new VisualChartDataBuilder().Build(spec, manifest), new PlotBounds(0m, 0m, 200m, 400m));
        var sampled = plan with { Style = plan.Style.Add(new("SAMPLING", mode)) };
        static XElement[] Paths(PlotPlan value) => XDocument.Parse(new SvgChartRenderer().Render(value)).Descendants()
            .Where(element => (string?)element.Attribute("class") is "plot-conditional-connection" or "plot-conditional-area").ToArray();
        var fullPaths = Paths(plan);
        var sampledPaths = Paths(sampled);
        Assert.Equal(399, sampledPaths.Length);
        Assert.Equal(fullPaths.Select(path => path.ToString()), sampledPaths.Select(path => path.ToString()));
        Assert.Equal(400, Assert.Single(sampled.Layers).Data.Length);
        Assert.Equal(Enumerable.Range(0, 399), sampledPaths.Select(path => int.Parse(path.Attribute("data-source-index")!.Value, CultureInfo.InvariantCulture)));
        Assert.Equal(Enumerable.Range(1, 399), sampledPaths.Select(path => int.Parse(path.Attribute("data-destination-index")!.Value, CultureInfo.InvariantCulture)));
        Assert.Contains("Row 399 to row 400", sampledPaths[^1].Value);
        Assert.Equal("#0000ff", sampledPaths[^1].Attribute(area ? "fill" : "stroke")!.Value);
        Assert.Equal(ChartContractSerializer.Serialize(sampled), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializePlotPlan(ChartContractSerializer.Serialize(sampled))));
    }
}
