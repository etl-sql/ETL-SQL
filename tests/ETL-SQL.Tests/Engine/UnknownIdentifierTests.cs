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

    [Theory]
    [InlineData("SELECT @nope AS v;")]
    [InlineData("SELECT @nope + 1 AS v;")]
    [InlineData("SELECT @nope AS v FROM (SELECT 1 AS a) t;")]
    [InlineData("SELECT a AS v FROM (SELECT 1 AS a) t WHERE a = @nope;")]
    [InlineData("DECLARE @x INT = 1; SET @x = @nope;")]
    public async Task UndeclaredVariableFails(string sql)
    {
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => Run(sql));
        Assert.Contains("Unknown variable '@nope'", failure.ToString());
    }

    [Theory]
    [InlineData("DECLARE @x INT = 5; SELECT @x AS v;", "5")]
    [InlineData("DECLARE @x INT; SELECT COALESCE(@x, 9) AS v;", "9")]
    [InlineData("DECLARE @y INT; SET @y = 7; SELECT @y AS v FROM (SELECT 1 AS a) t;", "7")]
    public async Task DeclaredVariableStillAnswers(string sql, string expected)
    {
        var rows = await Run(sql);
        Assert.StartsWith(expected, TypeConverter.ToInvariantText(Assert.Single(rows)["v"]));
    }

    // Every left row here is unmatched, so the misspelt name is only ever read on a row that
    // carries one side of the join.
    [Theory]
    [InlineData("SELECT o.id AS id, c.regoin AS v FROM (SELECT 2 AS id) o LEFT JOIN (SELECT 1 AS id, 'north' AS region) c ON o.id = c.id;", "c.regoin")]
    [InlineData("SELECT o.id AS id, regoin AS v FROM (SELECT 2 AS id) o LEFT JOIN (SELECT 1 AS id, 'north' AS region) c ON o.id = c.id;", "regoin")]
    [InlineData("SELECT o.id AS id FROM (SELECT 2 AS id) o LEFT JOIN (SELECT 1 AS id, 'north' AS region) c ON o.id = c.id WHERE c.regoin IS NULL;", "c.regoin")]
    [InlineData("SELECT o.id AS id, c.regoin AS v FROM (SELECT 2 AS id) o LEFT JOIN (SELECT 1 AS id, 'north' AS region) c ON o.id > c.id + 5;", "c.regoin")]
    [InlineData("SELECT c.id AS id, o.regoin AS v FROM (SELECT 1 AS id, 'north' AS region) o RIGHT JOIN (SELECT 2 AS id) c ON o.id = c.id;", "o.regoin")]
    [InlineData("SELECT l.idd AS v FROM (SELECT 1 AS id) l FULL OUTER JOIN (SELECT 2 AS id) r ON l.id = r.id;", "l.idd")]
    [InlineData("CREATE TABLE #lookup (id INT, region VARCHAR); SELECT o.id AS id, c.regoin AS v FROM (SELECT 1 AS id) o LEFT JOIN #lookup c ON o.id = c.id;", "c.regoin")]
    [InlineData("SELECT o.id AS id, c.regoin AS v FROM (SELECT 1 AS id) o LEFT JOIN (SELECT 1 AS id, 'north' AS region WHERE 1 = 0) c ON o.id = c.id;", "c.regoin")]
    [InlineData("SELECT o.id AS id, c.regoin AS v FROM (SELECT 2 AS id) o OUTER APPLY (SELECT 'north' AS region WHERE o.id = 1) c;", "c.regoin")]
    [InlineData("CREATE TABLE #lookup (id INT, region VARCHAR); SELECT o.id AS id, c.regoin AS v FROM (SELECT 1 AS id) o LEFT JOIN (SELECT * FROM #lookup) c ON o.id = c.id;", "c.regoin")]
    [InlineData("WITH c AS (SELECT 1 AS id, 'north' AS region WHERE 1 = 0) SELECT o.id AS id, c.regoin AS v FROM (SELECT 1 AS id) o LEFT JOIN c ON o.id = c.id;", "c.regoin")]
    public async Task UnknownNameOnAnUnmatchedOuterJoinRowFails(string sql, string name)
    {
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => Run(sql));
        Assert.Matches($"Unknown (column|identifier) '{System.Text.RegularExpressions.Regex.Escape(name)}'", failure.ToString());
    }

    [Theory]
    [InlineData("SELECT o.id AS id, c.region AS v FROM (SELECT 1 AS id) o LEFT JOIN (SELECT 1 AS id, 'north' AS region WHERE 1 = 0) c ON o.id = c.id;")]
    [InlineData("SELECT o.id AS id, region AS v FROM (SELECT 1 AS id) o LEFT JOIN (SELECT 1 AS id, 'north' AS region WHERE 1 = 0) c ON o.id = c.id;")]
    [InlineData("SELECT o.id AS id, c.region AS v FROM (SELECT 2 AS id) o OUTER APPLY (SELECT 'north' AS region WHERE o.id = 1) c;")]
    [InlineData("SELECT c.id AS id, o.region AS v FROM (SELECT 1 AS id, 'north' AS region WHERE 1 = 0) o RIGHT JOIN (SELECT 2 AS id) c ON o.id = c.id;")]
    [InlineData("CREATE TABLE #lookup (id INT, region VARCHAR); SELECT o.id AS id, c.region AS v FROM (SELECT 1 AS id) o LEFT JOIN (SELECT * FROM #lookup) c ON o.id = c.id;")]
    [InlineData("WITH c AS (SELECT 1 AS id, 'north' AS region WHERE 1 = 0) SELECT o.id AS id, c.region AS v FROM (SELECT 1 AS id) o LEFT JOIN c ON o.id = c.id;")]
    [InlineData("SELECT o.id AS id, c.value AS v FROM (SELECT 1 AS id) o LEFT JOIN (VALUES (1, 'x')) AS c(id, value) ON o.id = c.id + 5;")]
    public async Task OuterJoinAgainstAnEmptySubqueryReadsItsColumnsAsNull(string sql)
    {
        var rows = await Run(sql);
        Assert.True(Assert.Single(rows)["v"] is null or DBNull);
    }

    [Theory]
    [InlineData("SELECT n, bogus AS v FROM #t ORDER BY n DESC;", "bogus")]
    [InlineData("SELECT a.n AS n, b.bogus AS v FROM #t a LEFT JOIN (SELECT n, g FROM #t) b ON a.n = b.n + 100000;", "b.bogus")]
    [InlineData("SELECT g, SUM(bogus) AS v FROM (SELECT n, g FROM #t) s GROUP BY g;", "bogus")]
    [InlineData("SELECT DISTINCT g, bogus AS v FROM (SELECT n, g FROM #t) s;", "bogus")]
    public async Task UnknownNameOnASpilledRowFails(string query, string name)
    {
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => RunSpilled(query));
        Assert.Matches($"Unknown (column|identifier) '{System.Text.RegularExpressions.Regex.Escape(name)}'", failure.ToString());
    }

    [Fact]
    public async Task SpilledOuterJoinStillReadsTheMissingSideAsNull()
    {
        var rows = await RunSpilled(
            "SELECT a.n AS n, b.g AS v FROM (SELECT n, g FROM #t) a LEFT JOIN (SELECT n, g FROM #t WHERE n <= 300) b ON a.n = b.n ORDER BY a.n;");

        Assert.Equal(600, rows.Length);
        Assert.False(rows[0]["v"] is null or DBNull);
        Assert.True(rows[^1]["v"] is null or DBNull, "Row 600 has no match.");
    }

    [Theory]
    [InlineData("region", true)]
    [InlineData("c.region", true)]
    [InlineData("c.regoin", false)]
    public async Task OuterJoinAgainstAFileWithOnlyAHeaderKnowsItsColumns(string column, bool known)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"sq03-{Guid.NewGuid():N}.csv");
        try
        {
            await System.IO.File.WriteAllTextAsync(path, "id,region\n");
            var sql = $"CREATE CONNECTION f AS FLATFILE('{path}', HEADER=ON); " +
                $"SELECT o.id AS id, {column} AS v FROM (SELECT 1 AS id) o LEFT JOIN f.FILE c ON o.id = c.id;";

            if (known)
                Assert.True(Assert.Single(await Run(sql))["v"] is null or DBNull);
            else
                Assert.Contains("Unknown column 'c.regoin'", (await Assert.ThrowsAnyAsync<Exception>(() => Run(sql))).ToString());
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    private static async Task<Row[]> RunSpilled(string query)
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        evaluator.JoinSpillThreshold = 50;
        evaluator.ExternalSortChunkSize = 50;
        var values = string.Join(", ", Enumerable.Range(1, 600).Select(n => $"({n}, {n % 4})"));
        await evaluator.Evaluate(new Lexer($"CREATE TABLE #t (n INT, g INT); INSERT INTO #t VALUES {values};").TokenizeToScript());
        await evaluator.Evaluate(new Lexer(query).TokenizeToScript());
        return evaluator.LastResult!.Rows.ToArray();
    }

    private static async Task<Row[]> Run(string sql)
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        await evaluator.Evaluate(new Lexer(sql).TokenizeToScript());
        return evaluator.LastResult!.Rows.ToArray();
    }
}
