using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Tests.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Statements;

public class SelectSpillBoundaryTests
{
    [Fact]
    public async Task Distinct_SpillsProjectedRowsBeforeOrdering()
    {
        var evaluator = CreateEvaluator();
        evaluator.JoinSpillThreshold = 3;
        evaluator.ExternalHashPartitions = 4;
        await TestHelpers.Execute(evaluator, "CREATE TABLE #t (v INT); INSERT INTO #t VALUES (3),(1),(2),(1),(3),(4),(2),(5);");

        var result = await Query(evaluator, "SELECT DISTINCT v FROM #t ORDER BY v DESC;");

        Assert.Equal(new[] { 5m, 4m, 3m, 2m, 1m }, result.Rows.Select(r => System.Convert.ToDecimal(r["v"])));
        Assert.True(evaluator.Telemetry.PartitionsCount >= 4);
    }

    [Fact]
    public async Task TopPercent_CountsAndReplaysExternalSort()
    {
        var evaluator = CreateEvaluator();
        evaluator.ExternalSortChunkSize = 3;
        await TestHelpers.Execute(evaluator, "CREATE TABLE #t (v INT); INSERT INTO #t VALUES (10),(9),(8),(7),(6),(5),(4),(3),(2),(1);");

        var result = await Query(evaluator, "SELECT TOP 30 PERCENT v FROM #t ORDER BY v;");

        Assert.Equal(new[] { 1m, 2m, 3m }, result.Rows.Select(r => System.Convert.ToDecimal(r["v"])));
    }

    [Fact]
    public async Task WithTies_StreamsPastExternalSortBoundary()
    {
        var evaluator = CreateEvaluator();
        evaluator.ExternalSortChunkSize = 3;
        await TestHelpers.Execute(evaluator, "CREATE TABLE #t (id INT, score INT); INSERT INTO #t VALUES (1,100),(2,90),(3,90),(4,90),(5,80),(6,70);");

        var result = await Query(evaluator, "SELECT TOP 2 WITH TIES id, score FROM #t ORDER BY score DESC;");

        Assert.Equal(4, result.Rows.Count);
        Assert.Equal(100m, result.Rows[0]["score"]);
        Assert.All(result.Rows.Skip(1), row => Assert.Equal(90m, row["score"]));
    }

    private static Evaluator CreateEvaluator()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        evaluator.BatchSize = 2;
        return evaluator;
    }

    private static async Task<DataTable> Query(Evaluator evaluator, string sql)
    {
        var result = new DataTable();
        await foreach (var batch in evaluator.ExecuteQuery(TestHelpers.Parse(sql).Statements[0]))
        {
            if (result.ColumnNames.Count == 0) result.SetColumns(batch.ColumnNames);
            foreach (var row in batch.Rows) await result.AddRowAsync(row);
        }
        return result;
    }
}
