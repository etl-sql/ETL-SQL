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
/// A name nothing defines must fail. It read as NULL when a row was in scope and as its own name
/// when none was, so a misspelt column gave a column of NULLs and the script succeeded. The sources
/// here are subqueries on purpose: <c>SelectColumnBinder</c> already checks plain temp tables before
/// the statement runs, and these reach the row evaluator.
/// </summary>
public sealed class UnknownIdentifierTests
{
    [Theory]
    [InlineData("SELECT bogus AS v;", "bogus")]
    [InlineData("SELECT bogus + 1 AS v;", "bogus")]
    [InlineData("SELECT bogus AS v FROM (SELECT 1 AS a) t;", "bogus")]
    [InlineData("SELECT a AS v FROM (SELECT 1 AS a) t WHERE bogus = 1;", "bogus")]
    [InlineData("SELECT t.bogus AS v FROM (SELECT 1 AS a) t;", "t.bogus")]
    [InlineData("SELECT a AS v FROM (SELECT 1 AS a) t ORDER BY bogus;", "bogus")]
    [InlineData("SELECT o.id AS v FROM (SELECT 1 AS id) o LEFT JOIN (SELECT 1 AS id, 'x' AS region) c ON o.id = c.id WHERE c.regoin = 'x';", "c.regoin")]
    public async Task UnknownNameFails(string sql, string name)
    {
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => Run(sql));
        Assert.Matches($"Unknown (column|identifier) '{System.Text.RegularExpressions.Regex.Escape(name)}'", failure.ToString());
    }

    [Fact]
    public async Task UnknownColumnErrorListsTheColumnsInScope()
    {
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => Run("SELECT amuont AS v FROM (SELECT 1 AS amount, 2 AS tax) t;"));
        Assert.Contains("amount, tax", failure.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("SELECT DATEADD(day, 1, CAST('2026-01-01' AS DATETIME)) AS v;", "2026-01-02")]
    [InlineData("SELECT DATEPART(year, CAST('2026-03-04' AS DATETIME)) AS v;", "2026")]
    [InlineData("SELECT a AS v FROM (SELECT 7 AS a) t;", "7")]
    [InlineData("SELECT t.a AS v FROM (SELECT 7 AS a) t;", "7")]
    public async Task KnownNameStillAnswers(string sql, string expected)
    {
        var rows = await Run(sql);
        Assert.StartsWith(expected, TypeConverter.ToInvariantText(Assert.Single(rows)["v"]));
    }

    [Theory]
    [InlineData("c.region")]
    [InlineData("region")]
    public async Task OuterJoinRowWithNoMatchReadsTheOtherSideAsNull(string column)
    {
        var rows = await Run(
            $"SELECT o.id AS id, {column} AS v FROM (SELECT 1 AS id UNION ALL SELECT 2 AS id) o " +
            "LEFT JOIN (SELECT 1 AS id, 'north' AS region) c ON o.id = c.id ORDER BY o.id;");

        Assert.Equal(2, rows.Length);
        Assert.Equal("north", rows[0]["v"]);
        Assert.True(rows[1]["v"] is null or DBNull, "The unmatched row has no region.");
    }

    [Fact]
    public async Task OuterJoinAgainstAnEmptySideReadsItsColumnsAsNull()
    {
        var rows = await Run(
            "CREATE TABLE #lookup (id INT, region VARCHAR);" +
            "SELECT o.id AS id, c.region AS v FROM (SELECT 1 AS id) o LEFT JOIN #lookup c ON o.id = c.id;");

        Assert.True(Assert.Single(rows)["v"] is null or DBNull);
    }

    [Fact]
    public async Task FullOuterJoinReadsEachMissingSideAsNull()
    {
        var rows = await Run(
            "SELECT l.id AS lid, r.id AS rid FROM (SELECT 1 AS id) l " +
            "FULL OUTER JOIN (SELECT 2 AS id) r ON l.id = r.id;");

        Assert.Equal(2, rows.Length);
        Assert.Contains(rows, row => row["lid"] is null or DBNull);
        Assert.Contains(rows, row => row["rid"] is null or DBNull);
    }

    [Fact]
    public async Task PivotInListReadsABracketedValueByName()
    {
        var rows = await Run(
            "SELECT [1] AS one, [2] AS two FROM (SELECT 1 AS id, 'A' AS val UNION ALL SELECT 2 AS id, 'B' AS val) src " +
            "PIVOT (MAX(val) FOR id IN ([1], [2])) AS pvt;");

        var row = Assert.Single(rows);
        Assert.Equal("A", row["one"]);
        Assert.Equal("B", row["two"]);
    }

    [Fact]
    public async Task ConnectionOptionTakesABareKeyword()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"sq03-{Guid.NewGuid():N}.csv");
        try
        {
            await System.IO.File.WriteAllTextAsync(path, "id|name\n1|north\n");
            var rows = await Run(
                $"CREATE CONNECTION f AS FLATFILE('{path}', HEADER=ON, DELIMITER=PIPE); SELECT name AS v FROM f.FILE;");

            Assert.Equal("north", Assert.Single(rows)["v"]);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    private static async Task<Row[]> Run(string sql)
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        await evaluator.Evaluate(new Lexer(sql).TokenizeToScript());
        return evaluator.LastResult!.Rows.ToArray();
    }
}
