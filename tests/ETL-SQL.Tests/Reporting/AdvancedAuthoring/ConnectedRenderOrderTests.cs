using System.Xml.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Semantics.Runtime;

namespace ETL_SQL.Tests.Reporting.AdvancedAuthoring;

[Trait("CompatBreak", "0.20.0")]
public sealed class ConnectedRenderOrderTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var form in new[] { "LINE", "AREA", "RIBBON" })
            foreach (var transposed in new[] { false, true })
                foreach (var facets in new[] { false, true })
                    foreach (var pointsInFront in new[] { false, true }) yield return [form, transposed, facets, pointsInFront];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void EveryPanelPaintsOrdinaryPointsInAuthoredPriorityOrder(string form, bool transposed, bool facets, bool pointsInFront)
    {
        var sql = $$"""
            CREATE VISUAL Routes AS CUSTOM (SOURCE = #prepared, CHART (
              COORDINATE (TYPE = {{(transposed ? "TRANSPOSED_CARTESIAN" : "CARTESIAN")}}),
              {{(facets ? "FACET (WRAP = Cohort, COLUMNS = 2)," : "")}}
              LAYERS (
                observations = POINT (Z_INDEX = {{(pointsInFront ? 5 : 0)}}, INHERIT_ENCODINGS = OFF,
                  ENCODINGS (X = Distance (TYPE = QUANTITATIVE), Y = Estimate (TYPE = QUANTITATIVE))),
                route = {{(form == "LINE" ? "LINE" : "AREA")}} (Z_INDEX = {{(pointsInFront ? 0 : 5)}}, INHERIT_ENCODINGS = OFF,
                  NULL_HANDLING = GAP, {{(form == "AREA" ? "AREA_BASELINE = ZERO," : "")}}
                  ENCODINGS (X = Distance (TYPE = QUANTITATIVE),
                    {{(form == "RIBBON" ? "Y_START = Lower (TYPE = QUANTITATIVE), Y_END = Upper (TYPE = QUANTITATIVE)" : "Y = Estimate (TYPE = QUANTITATIVE)")}}),
                  STYLE (INTERPOLATION = 'LINEAR'), CONDITIONS (OPACITY WHEN Distance > 0 THEN 0.5 ELSE 1)))
            ));
            """;
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        var manifest = new VisualManifest
        {
            Name = "Routes",
            Columns = ["Distance", "Estimate", "Lower", "Upper", "Cohort"],
            Rows = [["1", "2", "1", "3", "A"], ["2", "3", "2", "4", "A"], ["3", "4", "3", "5", "B"], ["4", "5", "4", "6", "B"]]
        };
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(Assert.Single(script.Statements.OfType<CreateVisualStatement>()), manifest);
        var plan = new PlotPlanResolver().Resolve(spec, new VisualChartDataBuilder().Build(spec, manifest));
        var root = XDocument.Parse(new SvgChartRenderer().Render(plan)).Root!;
        var panels = facets ? root.Descendants(root.Name).ToArray() : [root];
        Assert.Equal(facets ? 2 : 1, panels.Length);
        foreach (var panel in panels)
        {
            var glyphs = panel.Descendants().Where(element => (string?)element.Attribute("class") is "plot-point" or "plot-conditional-connection" or "plot-conditional-area").ToArray();
            var pointIndices = glyphs.Select((element, index) => (element, index)).Where(item => (string?)item.element.Attribute("class") == "plot-point").Select(item => item.index).ToArray();
            var connectionIndices = glyphs.Select((element, index) => (element, index)).Where(item => (string?)item.element.Attribute("class") != "plot-point").Select(item => item.index).ToArray();
            Assert.NotEmpty(pointIndices);
            Assert.NotEmpty(connectionIndices);
            Assert.True(pointsInFront ? pointIndices.Min() > connectionIndices.Max() : pointIndices.Max() < connectionIndices.Min());
        }
    }
}
