using System.Collections.Immutable;
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
public sealed class IndependentFacetContractTests
{
    public static IEnumerable<object[]> BoundsCases()
    {
        foreach (var kind in new[] { "LINEAR", "LOGARITHMIC" })
            foreach (var bounds in new[] { "MIN = 1, MAX = 10", "MIN = 1", "MAX = 10" })
                foreach (var coordinate in new[] { "CARTESIAN", "TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2" }) yield return [kind, bounds, coordinate];
    }

    [Theory]
    [MemberData(nameof(BoundsCases))]
    public void NumericFacetsPreserveEachExplicitBound(string kind, string bounds, string coordinate)
    {
        var (spec, data) = Lower(kind, bounds, coordinate);
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var points = XDocument.Parse(new SvgChartRenderer().Render(plan)).Descendants().Where(element => (string?)element.Attribute("class") == "plot-point").ToArray();
        Assert.Equal(2, plan.Facets.Length);
        foreach (var facet in plan.Facets)
            foreach (var scale in facet.Scales)
            {
                var values = data.Columns.Single(column => column.Name == (scale.Channel == FieldChannel.X ? "Distance" : "Estimate")).Values;
                var minimum = bounds.Contains("MIN", StringComparison.Ordinal) ? 1m : facet.RowIndices.Select(row => values[row].Decimal!.Value).Min();
                var maximum = bounds.Contains("MAX", StringComparison.Ordinal) ? 10m : facet.RowIndices.Select(row => values[row].Decimal!.Value).Max();
                Assert.Equal(minimum, scale.Domain[0].Decimal);
                Assert.Equal(maximum, scale.Domain[^1].Decimal);
                Assert.Equal(minimum, scale.Ticks[0].Value.Decimal);
                Assert.Equal(maximum, scale.Ticks[^1].Value.Decimal);
            }
        foreach (var facet in plan.Facets)
            foreach (var row in facet.RowIndices)
            {
                var point = Assert.Single(points, point => (string?)point.Attribute("data-row-index") == row.ToString(CultureInfo.InvariantCulture));
                var frame = facet.CartesianViewport ?? facet.Bounds;
                decimal Map(FieldChannel channel, bool vertical)
                {
                    var scale = facet.Scales.Single(scale => scale.Channel == channel);
                    var value = data.Columns.Single(column => column.Name == (channel == FieldChannel.X ? "Distance" : "Estimate")).Values[row].Decimal!.Value;
                    var minimum = scale.Domain[0].Decimal!.Value;
                    var maximum = scale.Domain[^1].Decimal!.Value;
                    var ratio = kind == "LINEAR" ? (value - minimum) / (maximum - minimum)
                        : (decimal)((Math.Log10((double)value) - Math.Log10((double)minimum)) / (Math.Log10((double)maximum) - Math.Log10((double)minimum)));
                    return vertical ? 40m + (1m - ratio) * (frame.Height - 100m) : 60m + ratio * (frame.Width - 80m);
                }
                var transposed = coordinate.StartsWith("TRANSPOSED", StringComparison.Ordinal);
                Assert.InRange(Math.Abs(decimal.Parse(point.Attribute("cx")!.Value, CultureInfo.InvariantCulture) - Map(transposed ? FieldChannel.Y : FieldChannel.X, false)), 0m, 0.002m);
                Assert.InRange(Math.Abs(decimal.Parse(point.Attribute("cy")!.Value, CultureInfo.InvariantCulture) - Map(transposed ? FieldChannel.X : FieldChannel.Y, true)), 0m, 0.002m);
            }
        var resolver = new PlotPlanResolver();
        var resized = resolver.Relayout(spec, data, plan, new PlotBounds(0m, 0m, 900m, 600m));
        Assert.Equal(ChartContractSerializer.Serialize(resolver.Resolve(spec, data, resized.Bounds)), ChartContractSerializer.Serialize(resized));
    }

    [Fact]
    public void RoundTripFallbackPassesAndChangedContentFailsConformance()
    {
        var (spec, data) = Lower("LINEAR", "MIN = 1, MAX = 10", "CARTESIAN");
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Equal(3, plan.Version);
        var deserialized = ChartContractSerializer.DeserializePlotPlan(ChartContractSerializer.Serialize(plan));
        Assert.NotEqual(plan.Fallback, deserialized.Fallback);
        Assert.Empty(PlotPlanConformanceHarness.Evaluate(plan, [new Backend(deserialized.Fallback)]).Issues);
        var first = plan.Fallback.Items[0];
        foreach (var altered in new[] { plan.Fallback with { Heading = "lost" }, plan.Fallback with { Summary = "lost" },
            plan.Fallback with { Kind = SemanticFallbackKind.NetworkConnections },
            plan.Fallback with { Items = plan.Fallback.Items.SetItem(0, first with { Detail = "lost" }) },
            plan.Fallback with { Items = plan.Fallback.Items.Reverse().ToImmutableArray() } })
            Assert.Contains(PlotPlanConformanceHarness.Evaluate(plan, [new Backend(altered)]).Issues, issue => issue.SemanticArea == "fallback");
    }

    private sealed class Backend(SemanticFallback fallback) : IPlotPlanSemanticBackend
    {
        public string Name => "fallback";
        public PlotSemanticProjection Project(PlotPlan plan) => PlotSemanticProjection.FromPlan(plan) with { Fallback = fallback };
    }
    private static (ChartSpec Spec, ChartDataSet Data) Lower(string kind, string bounds, string coordinate)
    {
        var sql = $$"""
            CREATE VISUAL Route AS CUSTOM (SOURCE = #prepared, CHART (
              COORDINATE (TYPE = {{coordinate}}),
              SCALES (horizontal = {{kind}} (CHANNEL = X, INCLUDE_ZERO = OFF, {{bounds}}),
                vertical = {{kind}} (CHANNEL = Y, INCLUDE_ZERO = OFF, {{bounds}})),
              FACET (WRAP = Cohort, COLUMNS = 2), RESOLVE (X = INDEPENDENT, Y = INDEPENDENT),
              LAYERS (observations = POINT (ENCODINGS (X = Distance (TYPE = QUANTITATIVE, SCALE = horizontal),
                Y = Estimate (TYPE = QUANTITATIVE, SCALE = vertical))))
            ));
            """;
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        var manifest = new VisualManifest { Name = "Route", Columns = ["Distance", "Estimate", "Cohort"], Rows = [["5", "3", "A"], ["7", "4", "A"], ["1", "8", "B"], ["2", "9", "B"]] };
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(Assert.Single(script.Statements.OfType<CreateVisualStatement>()), manifest);
        return (spec, new VisualChartDataBuilder().Build(spec, manifest));
    }
}
