using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
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
        // The distinct engine took the disk-spill partition path. (Counts non-empty partitions, like
        // the join engine; the exact number depends on the hash distribution of the few distinct
        // values, so assert the path was used rather than an exact partition count.)
        Assert.True(evaluator.Telemetry.PartitionsCount >= 1);
    }

    [Fact]
    public async Task Distinct_HighCardinality_RecursivelyRepartitionsAndDedupsCorrectly()
    {
        // threshold 3 with only 2 partitions: each top-level partition holds far more than the
        // threshold, so the engine must recursively repartition (depth-salted hash) several
        // levels deep before each sub-partition fits the in-memory dedup bound. 40 distinct
        // values, each duplicated, must collapse to exactly 40 rows with none lost.
        var evaluator = CreateEvaluator();
        evaluator.JoinSpillThreshold = 3;
        evaluator.ExternalHashPartitions = 2;

        var values = Enumerable.Range(1, 40).ToList();
        var doubled = values.Concat(values).OrderBy(v => (v * 31 + 7) % 97); // interleave duplicates
        var inserts = string.Join(",", doubled.Select(v => $"({v})"));
        await TestHelpers.Execute(evaluator, $"CREATE TABLE #t (v INT); INSERT INTO #t VALUES {inserts};");

        var result = await Query(evaluator, "SELECT DISTINCT v FROM #t ORDER BY v;");

        Assert.Equal(values.Select(v => (decimal)v), result.Rows.Select(r => System.Convert.ToDecimal(r["v"])));
        // Recursion produced more partitions than the 2 top-level ones.
        Assert.True(evaluator.Telemetry.PartitionsCount > 2);
    }

    [Fact]
    public async Task Distinct_DuplicateHeavyPartition_FallsBackToInMemoryDedup()
    {
        // A single over-represented value (50 copies) lands entirely in one partition that
        // exceeds the threshold but cannot be split by repartitioning. The engine must fall
        // back to in-memory dedup rather than recurse forever, returning the single value.
        var evaluator = CreateEvaluator();
        evaluator.JoinSpillThreshold = 3;
        evaluator.ExternalHashPartitions = 2;

        var inserts = string.Join(",", Enumerable.Repeat("(7)", 50));
        await TestHelpers.Execute(evaluator, $"CREATE TABLE #t (v INT); INSERT INTO #t VALUES {inserts};");

        var result = await Query(evaluator, "SELECT DISTINCT v FROM #t;");

        Assert.Single(result.Rows);
        Assert.Equal(7m, System.Convert.ToDecimal(result.Rows[0]["v"]));
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

    // Note: there is intentionally no DISTINCT "SpillOrFail throws" governor test. Unlike the join
    // build side or holistic aggregates, DISTINCT only retains one row per distinct value, so an
    // unsplittable partition (all-identical rows) is O(1) memory and correctly never trips the
    // governor. High-cardinality DISTINCT always splits via recursive repartition (covered above).

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
