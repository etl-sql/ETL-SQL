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
/// A group key is normalized so that <c>'1'</c> and <c>1</c> group together, and the normalized
/// value was what the query returned: text <c>'123'</c> came back as a number and text
/// <c>'2026-01-05'</c> as a date. <c>HAVING</c> could not see a <c>SELECT</c> alias or a qualified
/// grouping column.
/// </summary>
public sealed class GroupKeyAndHavingTests
{
    private const string Setup =
        "CREATE TABLE #g (code VARCHAR, day VARCHAR, region VARCHAR, amount INT); " +
        "INSERT INTO #g VALUES ('123', '2026-01-05', 'east', 10), ('123', '2026-01-05', 'east', 5), " +
        "('00123', '2026-01-06', 'west', 7), ('abc', 'n/a', 'west', 1); ";

    [Theory]
    [InlineData("SELECT code AS v, SUM(amount) AS s FROM #g GROUP BY code ORDER BY s DESC;", "123")]
    [InlineData("SELECT day AS v, SUM(amount) AS s FROM #g GROUP BY day ORDER BY s DESC;", "2026-01-05")]
    [InlineData("SELECT code AS v, day, SUM(amount) AS s FROM #g GROUP BY code, day ORDER BY s DESC;", "123")]
    [InlineData("SELECT day AS v, code, region, SUM(amount) AS s FROM #g GROUP BY code, day, region ORDER BY s DESC;", "2026-01-05")]
    [InlineData("SELECT DISTINCT code AS v FROM #g WHERE amount = 10;", "123")]
    [InlineData("SELECT DISTINCT day AS v FROM #g WHERE amount = 10;", "2026-01-05")]
    [InlineData("SELECT code AS v, COUNT(*) AS s FROM #g GROUP BY code HAVING COUNT(*) > 1;", "123")]
    public async Task AGroupedTextColumnComesBackAsText(string sql, string expected)
    {
        var value = (await Run(Setup + sql))[0]["v"];
        Assert.IsType<string>(value);
        Assert.Equal(expected, value);
    }

    [Fact]
    public async Task ASpilledGroupKeepsItsTextToo()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        evaluator.ExternalSortChunkSize = 50;
        var values = string.Join(", ", Enumerable.Range(1, 600).Select(n => $"('{n}', '2026-01-{(n % 9) + 1:00}', {n})"));
        await evaluator.Evaluate(new Lexer($"CREATE TABLE #big (code VARCHAR, day VARCHAR, amount INT); INSERT INTO #big VALUES {values};").TokenizeToScript());
        await evaluator.Evaluate(new Lexer("SELECT code, day, SUM(amount) AS s FROM #big GROUP BY code, day;").TokenizeToScript());

        var rows = evaluator.LastResult!.Rows.ToArray();
        Assert.Equal(600, rows.Length);
        Assert.All(rows, r => { Assert.IsType<string>(r["code"]); Assert.IsType<string>(r["day"]); });
    }

    [Theory]
    // A SELECT alias of a grouping expression.
    [InlineData("SELECT UPPER(region) AS r, SUM(amount) AS v FROM #g GROUP BY UPPER(region) HAVING r = 'EAST';", "15")]
    [InlineData("SELECT region AS r, SUM(amount) AS v FROM #g GROUP BY region HAVING r = 'east';", "15")]
    // The grouping expression itself, bare and qualified.
    [InlineData("SELECT region, SUM(amount) AS v FROM #g GROUP BY region HAVING region = 'east';", "15")]
    [InlineData("SELECT t.region, SUM(t.amount) AS v FROM #g t GROUP BY t.region HAVING t.region = 'east';", "15")]
    [InlineData("SELECT UPPER(region) AS r, SUM(amount) AS v FROM #g GROUP BY UPPER(region) HAVING UPPER(region) = 'EAST';", "15")]
    // An alias of an aggregate, and an aggregate that is not selected.
    [InlineData("SELECT region, SUM(amount) AS v FROM #g GROUP BY region HAVING v > 10;", "15")]
    [InlineData("SELECT region, SUM(amount) AS v FROM #g GROUP BY region HAVING COUNT(*) = 2 AND MAX(amount) = 10;", "15")]
    // A grouping column that is not selected.
    [InlineData("SELECT SUM(amount) AS v FROM #g GROUP BY region HAVING region = 'east';", "15")]
    public async Task HavingSeesGroupingColumnsAndAliases(string sql, string expected)
    {
        var row = Assert.Single(await Run(Setup + sql));
        Assert.Equal(expected, Convert.ToDecimal(row["v"]).ToString("0"));
    }

    [Fact]
    public async Task HavingFailsOnANameNothingDefines()
    {
        var failure = await Assert.ThrowsAnyAsync<Exception>(() =>
            Run(Setup + "SELECT region, SUM(amount) AS v FROM #g GROUP BY region HAVING regoin = 'east';"));
        Assert.Contains("regoin", failure.ToString());
    }

    private static async Task<Row[]> Run(string sql)
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        await evaluator.Evaluate(new Lexer(sql).TokenizeToScript());
        return evaluator.LastResult!.Rows.ToArray();
    }
}
