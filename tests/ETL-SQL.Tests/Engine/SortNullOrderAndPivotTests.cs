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
/// The columnar sort put <c>NULL</c> first for <c>DESC</c> where the row sort puts it last.
/// <c>PIVOT</c> built its group key by joining values with <c>|</c>, so different rows shared a
/// group, and compared a <c>NULL</c> pivot value with <c>=</c>. <c>UNPIVOT</c> kept <c>NULL</c> cells.
/// </summary>
public sealed class SortNullOrderAndPivotTests
{
    private const string SortSetup =
        "CREATE TABLE #s (g INT, x INT, t VARCHAR, d DATE); " +
        "INSERT INTO #s VALUES (1, 2, 'b', '2026-01-02'), (1, NULL, NULL, NULL), (1, 3, 'c', '2026-01-03'), (1, 1, 'a', '2026-01-01'); ";

    [Theory]
    [InlineData("SELECT x AS v FROM #s ORDER BY x;", "NULL,1,2,3")]
    [InlineData("SELECT x AS v FROM #s ORDER BY x ASC;", "NULL,1,2,3")]
    [InlineData("SELECT x AS v FROM #s ORDER BY x DESC;", "3,2,1,NULL")]
    [InlineData("SELECT t AS v FROM #s ORDER BY t;", "NULL,a,b,c")]
    [InlineData("SELECT t AS v FROM #s ORDER BY t DESC;", "c,b,a,NULL")]
    [InlineData("SELECT x AS v, d FROM #s ORDER BY d;", "NULL,1,2,3")]
    [InlineData("SELECT x AS v, d FROM #s ORDER BY d DESC;", "3,2,1,NULL")]
    [InlineData("SELECT x AS v, g FROM #s ORDER BY g, x DESC;", "3,2,1,NULL")]
    [InlineData("SELECT x AS v, g FROM #s ORDER BY g DESC, x;", "NULL,1,2,3")]
    public async Task NullSortsLowestOnEveryPath(string sql, string expected)
    {
        foreach (var (columnar, spill) in new[] { (false, false), (true, false), (false, true), (true, true) })
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            evaluator.UseColumnarTempTables = columnar;
            if (spill) evaluator.ExternalSortChunkSize = 2;
            await evaluator.Evaluate(new Lexer(SortSetup + sql).TokenizeToScript());
            var actual = string.Join(",", evaluator.LastResult!.Rows.Select(r => Text(r["v"])));
            Assert.True(expected == actual, $"columnar={columnar} spill={spill}: expected {expected}, got {actual}");
        }
    }

    [Fact]
    public async Task PivotKeepsRowsApartWhenTheirJoinedTextIsTheSame()
    {
        var rows = await Run(
            "CREATE TABLE #p (a VARCHAR, b VARCHAR, k VARCHAR, v INT); " +
            "INSERT INTO #p VALUES ('x|y', 'z', 'q1', 1), ('x', 'y|z', 'q1', 2), (NULL, 'n', 'q1', 4), ('NULL', 'n', 'q1', 8); " +
            "SELECT a, b, q1 FROM #p PIVOT (SUM(v) FOR k IN ('q1')) AS pv;");

        Assert.Equal(new[] { "1", "2", "4", "8" }, rows.Select(r => Text(r["q1"])).OrderBy(v => v).ToArray());
    }

    [Fact]
    public async Task PivotStillGroupsEqualRows()
    {
        var rows = await Run(
            "CREATE TABLE #p (a VARCHAR, n INT, k VARCHAR, v INT); " +
            "INSERT INTO #p VALUES ('x', 1, 'q1', 1), ('x', 1, 'q2', 2), ('x', 1, 'q1', 4), (NULL, NULL, 'q1', 8), (NULL, NULL, 'q2', 16); " +
            "SELECT a, n, q1, q2 FROM #p PIVOT (SUM(v) FOR k IN ('q1', 'q2')) AS pv;");

        Assert.Equal(2, rows.Length);
        var x = rows.Single(r => Text(r["a"]) == "x");
        Assert.Equal("5", Text(x["q1"]));
        Assert.Equal("2", Text(x["q2"]));
        var none = rows.Single(r => Text(r["a"]) == "NULL");
        Assert.Equal("8", Text(none["q1"]));
        Assert.Equal("16", Text(none["q2"]));
    }

    [Fact]
    public async Task DynamicPivotKeepsValueCombinationsApart()
    {
        var rows = await Run(
            "PIVOT (VALUES ('N', 'ab', 'c', 1), ('N', 'a', 'bc', 2)) AS t(region, c1, c2, amount) " +
            "ON c1, c2 USING SUM(amount);");

        var row = Assert.Single(rows);
        Assert.Equal("1", Text(row["ab_c"]));
        Assert.Equal("2", Text(row["a_bc"]));
    }

    [Fact]
    public async Task DynamicPivotCountsANullPivotValue()
    {
        var rows = await Run(
            "PIVOT (VALUES ('N', 'Q1', 10), ('N', NULL, 5), ('N', NULL, 2)) AS t(region, quarter, amount) " +
            "ON quarter USING SUM(amount);");

        var row = Assert.Single(rows);
        Assert.Equal("10", Text(row["Q1"]));
        Assert.Equal("7", Text(row["NULL"]));
    }

    [Theory]
    [InlineData("'q1', 'q2'")]
    [InlineData("[q1], [q2]")]
    [InlineData("q1, q2")]
    public async Task ASpilledPivotReadsItsValueList(string values)
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        evaluator.JoinSpillThreshold = 50;
        var data = string.Join(", ", Enumerable.Range(1, 300).Select(n => $"({n % 10}, 'q{(n % 2) + 1}', {n})"));
        await evaluator.Evaluate(new Lexer($"CREATE TABLE #big (id INT, k VARCHAR, v INT); INSERT INTO #big VALUES {data};").TokenizeToScript());
        await evaluator.Evaluate(new Lexer($"SELECT id, q1, q2 FROM #big PIVOT (SUM(v) FOR k IN ({values})) AS pv;").TokenizeToScript());

        var rows = evaluator.LastResult!.Rows.ToArray();
        Assert.Equal(10, rows.Length);
        Assert.Equal(45150m, rows.Sum(r => Convert.ToDecimal(r["q1"]) + Convert.ToDecimal(r["q2"])));
    }

    [Theory]
    [InlineData("SELECT region, quarter, amount FROM #w UNPIVOT (amount FOR quarter IN (q1, q2, q3)) AS u;")]
    [InlineData("UNPIVOT #w ON q1, q2, q3 INTO NAME quarter VALUE amount;")]
    [InlineData("UNPIVOT #w ON COLUMNS(* EXCLUDE (region)) INTO NAME quarter VALUE amount;")]
    public async Task UnpivotLeavesOutNullCells(string sql)
    {
        var rows = await Run(
            "CREATE TABLE #w (region VARCHAR, q1 INT, q2 INT, q3 INT); " +
            "INSERT INTO #w VALUES ('N', 10, NULL, 30), ('S', NULL, NULL, NULL), ('E', 1, 2, 3); " + sql);

        Assert.Equal(5, rows.Length);
        Assert.DoesNotContain(rows, r => r["amount"] is null or DBNull);
        Assert.DoesNotContain(rows, r => Text(r["region"]) == "S");
        Assert.Equal(46m, rows.Sum(r => Convert.ToDecimal(r["amount"])));
    }

    private static string Text(object? value) => value switch
    {
        null or DBNull => "NULL",
        string s => s,
        IConvertible number and not DateTime => Convert.ToDecimal(number).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
        _ => TypeConverter.ToInvariantText(value) ?? "NULL"
    };

    private static async Task<Row[]> Run(string sql)
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        await evaluator.Evaluate(new Lexer(sql).TokenizeToScript());
        return evaluator.LastResult!.Rows.ToArray();
    }
}
