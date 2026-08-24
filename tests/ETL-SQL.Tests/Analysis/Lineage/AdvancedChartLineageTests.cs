using System.Linq;
using ETL_SQL.Analysis.Lineage;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Tests.Analysis.Lineage;

public sealed class AdvancedChartLineageTests
{
    [Fact]
    public void ChartEncodingsConditionsAndFacets_RecordReadLineage()
    {
        const string sql = """
            CREATE VISUAL Native AS CUSTOM (
              SOURCE = #prepared,
              CHART (
                COORDINATE (TYPE = CARTESIAN),
                SCALES (amount = LINEAR (CHANNEL = Y)),
                ENCODINGS (X = Category (TYPE = NOMINAL)),
                LAYERS (bars = RECT (
                  ENCODINGS (
                    Y = Revenue (TYPE = QUANTITATIVE, SCALE = amount),
                    TOOLTIP = DATUM(@Target) (TYPE = QUANTITATIVE),
                    COLOR = VALUE('#2563eb') (TYPE = NOMINAL)
                  ),
                  CONDITIONS (COLOR WHEN Profit < 0 THEN '#b91c1c')
                )),
                FACET (ROW = Region)
              )
            );
            """;
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        var tracker = new LineageTracker(NullLogger.Instance);

        new LineageAnalyzer(tracker).Analyze(script);

        var entries = tracker.GetFullLineage().Where(entry => entry.TargetTable == "report:Native").ToList();
        Assert.Contains(entries, entry => entry.TargetColumn == "bars.Y" && entry.SourceColumns.Contains("Revenue"));
        Assert.Contains(entries, entry => entry.TargetColumn == "CHART.X" && entry.SourceColumns.Contains("Category"));
        Assert.Contains(entries, entry => entry.TargetColumn == "bars.COLOR" && entry.SourceColumns.Contains("Profit"));
        Assert.Contains(entries, entry => entry.TargetColumn == "FACET.ROW" && entry.SourceColumns.Contains("Region"));
        var parameter = Assert.Single(entries, entry => entry.Operation == "CREATE VISUAL CHART PARAMETER");
        Assert.Equal("@Target", parameter.Metadata["parameter-dependency"]);
        Assert.Empty(parameter.SourceColumns);
        Assert.DoesNotContain(entries, entry => entry.SourceColumns.Contains("#2563eb"));
    }
}
