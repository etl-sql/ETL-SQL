using System;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Engine;

/// <summary>
/// With <c>ORDER BY</c> and no frame clause the frame is <c>RANGE BETWEEN UNBOUNDED PRECEDING AND
/// CURRENT ROW</c>. <c>LAST_VALUE</c> read the whole partition instead. A <c>RANGE</c> frame with an
/// offset read the whole partition for a row whose order key is <c>NULL</c>.
/// </summary>
public sealed class WindowDefaultFrameTests
{
    // g = 'a': n = 1, 2, 2, 3      g = 'b': n = 1
    private const string Setup =
        "CREATE TABLE #w (g VARCHAR, n INT); INSERT INTO #w VALUES ('a', 2), ('a', 1), ('b', 1), ('a', 3), ('a', 2); ";

    [Theory]
    // The frame ends at the current row's last peer.
    [InlineData("LAST_VALUE(n) OVER (PARTITION BY g ORDER BY n)", "1,2,2,3,1")]
    [InlineData("LAST_VALUE(n) OVER (ORDER BY g, n)", "1,2,2,3,1")]
    [InlineData("LAST_VALUE(n * 10) OVER (PARTITION BY g ORDER BY n)", "10,20,20,30,10")]
    [InlineData("LAST_VALUE(n) OVER (PARTITION BY g ORDER BY n DESC)", "1,2,2,3,1")]
    // Peers share the frame: ordering by g alone makes the whole partition one peer group.
    [InlineData("MAX(n) OVER (PARTITION BY g ORDER BY g) - LAST_VALUE(n) OVER (PARTITION BY g ORDER BY g) + n", null)]
    // A frame the query writes is still followed.
    [InlineData("LAST_VALUE(n) OVER (PARTITION BY g ORDER BY n ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING)", "3,3,3,3,1")]
    [InlineData("LAST_VALUE(n) OVER (PARTITION BY g ORDER BY n RANGE BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING)", "3,3,3,3,1")]
    [InlineData("LAST_VALUE(n) OVER (PARTITION BY g ORDER BY n ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW)", "1,2,2,3,1")]
    // FIRST_VALUE and the running aggregates do not change.
    [InlineData("FIRST_VALUE(n) OVER (PARTITION BY g ORDER BY n)", "1,1,1,1,1")]
    [InlineData("FIRST_VALUE(n) OVER (PARTITION BY g ORDER BY n DESC)", "3,3,3,3,1")]
    [InlineData("SUM(n) OVER (PARTITION BY g ORDER BY n)", "1,5,5,8,1")]
    [InlineData("NTH_VALUE(n, 2) OVER (PARTITION BY g ORDER BY n)", "NULL,2,2,2,NULL")]
    public async Task LastValueReadsTheDefaultRunningFrame(string expression, string? expected)
    {
        var rows = await Run(Setup + $"SELECT g, n, {expression} AS v FROM #w ORDER BY g, n;");
        if (expected == null)
        {
            // LAST_VALUE over one peer group is some value of the partition: the result stays in range.
            Assert.All(rows, r => Assert.InRange(Convert.ToDecimal(r["v"]), 1m, 5m));
            return;
        }
        Assert.Equal(expected, string.Join(",", rows.Select(r => Text(r["v"]))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ASpilledPartitionGivesTheSameLastValue(bool ties)
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        evaluator.WindowSpillThreshold = 20;
        var values = string.Join(", ", Enumerable.Range(1, 600).Reverse().Select(n => $"({n % 3}, {n}, {(ties ? n / 4 : n)})"));
        await evaluator.Evaluate(new Lexer($"CREATE TABLE #big (g INT, n INT, k INT); INSERT INTO #big VALUES {values};").TokenizeToScript());
        await evaluator.Evaluate(new Lexer(
            "SELECT g, n, k, LAST_VALUE(k) OVER (PARTITION BY g ORDER BY k) AS lv, " +
            "FIRST_VALUE(k) OVER (PARTITION BY g ORDER BY k) AS fv FROM #big;").TokenizeToScript());

        var rows = evaluator.LastResult!.Rows.ToArray();
        Assert.Equal(600, rows.Length);
        var firsts = rows.GroupBy(r => Text(r["g"])).ToDictionary(g => g.Key, g => g.Min(r => Convert.ToDecimal(r["k"])));
        Assert.All(rows, r =>
        {
            Assert.Equal(Convert.ToDecimal(r["k"]), Convert.ToDecimal(r["lv"]));
            Assert.Equal(firsts[Text(r["g"])], Convert.ToDecimal(r["fv"]));
        });
    }

    // #r: k = NULL, NULL, 1, 2, 3      x = 1, 2, 10, 20, 30
    private const string RangeSetup =
        "CREATE TABLE #r (k INT, x INT); INSERT INTO #r VALUES (2, 20), (NULL, 1), (1, 10), (3, 30), (NULL, 2); ";

    [Theory]
    // A NULL key is a peer of NULL only.
    [InlineData("RANGE BETWEEN 1 PRECEDING AND 1 FOLLOWING", "3,3,30,60,50")]
    [InlineData("RANGE BETWEEN 1 PRECEDING AND CURRENT ROW", "3,3,10,30,50")]
    [InlineData("RANGE BETWEEN CURRENT ROW AND 1 FOLLOWING", "3,3,30,50,30")]
    // An unbounded end still reaches the end of the partition.
    [InlineData("RANGE BETWEEN UNBOUNDED PRECEDING AND 1 FOLLOWING", "3,3,33,63,63")]
    [InlineData("RANGE BETWEEN 1 PRECEDING AND UNBOUNDED FOLLOWING", "63,63,60,60,50")]
    [InlineData("RANGE BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING", "63,63,63,63,63")]
    [InlineData("RANGE BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW", "3,3,13,33,63")]
    public async Task ARangeFrameOnANullKeyCoversItsNullPeers(string frame, string expected)
    {
        var rows = await Run(RangeSetup + $"SELECT k, x, SUM(x) OVER (ORDER BY k {frame}) AS v FROM #r ORDER BY k, x;");
        Assert.Equal(expected, string.Join(",", rows.Select(r => Text(r["v"]))));
    }

    private static string Text(object? value) => value switch
    {
        null or DBNull => "NULL",
        string s => s,
        _ => Convert.ToDecimal(value).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
    };

    private static async Task<Row[]> Run(string sql)
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        await evaluator.Evaluate(new Lexer(sql).TokenizeToScript());
        return evaluator.LastResult!.Rows.ToArray();
    }
}
