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
/// Reading a temp table or dataset nothing created must fail. Reads and writes shared one
/// resolution path that created an empty table for any unknown <c>#name</c> or <c>&amp;name</c>,
/// so a misspelt table returned no rows and the script succeeded.
/// </summary>
public sealed class UnknownSourceTests
{
    [Theory]
    [InlineData("SELECT * FROM #mistyped;", "#mistyped")]
    [InlineData("SELECT * FROM &mistyped;", "&mistyped")]
    [InlineData("SELECT COUNT(*) AS n FROM #mistyped;", "#mistyped")]
    [InlineData("SELECT a FROM #mistyped WHERE a > 1 ORDER BY a;", "#mistyped")]
    [InlineData("SELECT t.a FROM (SELECT 1 AS a) t JOIN #mistyped m ON t.a = m.a;", "#mistyped")]
    [InlineData("SELECT t.a FROM (SELECT 1 AS a) t LEFT JOIN #mistyped m ON t.a = m.a;", "#mistyped")]
    [InlineData("SELECT a FROM (SELECT * FROM #mistyped) s;", "#mistyped")]
    [InlineData("SELECT 1 AS a WHERE EXISTS (SELECT 1 FROM #mistyped);", "#mistyped")]
    [InlineData("CREATE TABLE #real (a INT); INSERT INTO #real SELECT a FROM #mistyped;", "#mistyped")]
    [InlineData("SELECT a INTO #copy FROM #mistyped;", "#mistyped")]
    [InlineData("CREATE TABLE #real (a INT); SELECT a FROM #real UNION ALL SELECT a FROM #mistyped;", "#mistyped")]
    [InlineData("TRUNCATE TABLE #mistyped;", "#mistyped")]
    [InlineData("ALTER TABLE #mistyped ADD b INT;", "#mistyped")]
    public async Task ReadingASourceNothingCreatedFails(string sql, string name)
    {
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => Run(sql));
        Assert.Contains($"Unknown source '{name}'", failure.ToString());
    }

    [Theory]
    [InlineData("DELETE FROM #mistyped WHERE a = 1;")]
    [InlineData("UPDATE #mistyped SET a = 1;")]
    public async Task ChangingRowsOfASourceNothingCreatedFails(string sql)
    {
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => Run(sql));
        Assert.Matches("Unknown.*#mistyped", failure.ToString());
    }

    [Fact]
    public async Task AFailedReadDoesNotCreateTheTable()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        await Assert.ThrowsAnyAsync<Exception>(() => evaluator.Evaluate(new Lexer("SELECT * FROM #mistyped;").TokenizeToScript()));
        await Assert.ThrowsAnyAsync<Exception>(() => evaluator.Evaluate(new Lexer("SELECT * FROM #mistyped;").TokenizeToScript()));
    }

    [Theory]
    [InlineData("SELECT 7 AS a INTO #made; SELECT a AS v FROM #made;")]
    [InlineData("CREATE TABLE #made (a INT); INSERT INTO #made VALUES (7); SELECT a AS v FROM #made;")]
    [InlineData("INSERT INTO #made SELECT 7 AS a; SELECT a AS v FROM #made;")]
    [InlineData("SELECT 7 AS a INTO &made; SELECT a AS v FROM &made;")]
    [InlineData("WITH made AS (SELECT 7 AS a) SELECT a AS v FROM made;")]
    [InlineData("EXEC ('SELECT 7 AS a') INTO #made; SELECT a AS v FROM #made;")]
    [InlineData("DECLARE @sql STRING = 'SELECT 7 AS a'; EXEC (@sql) INTO #made; SELECT a AS v FROM #made;")]
    public async Task ASourceTheScriptCreatedStillReads(string sql)
    {
        var rows = await Run(sql);
        Assert.StartsWith("7", TypeConverter.ToInvariantText(Assert.Single(rows)["v"]));
    }

    [Fact]
    public async Task ExecIntoFailsWhenTheSqlReturnsNothing()
    {
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => Run("EXEC ('DECLARE @x INT = 1;') INTO #made;"));
        Assert.Contains("returned no result set", failure.ToString());
    }

    [Fact]
    public async Task AnEmptyCreatedTableReadsAsNoRows()
    {
        Assert.Empty(await Run("CREATE TABLE #made (a INT); SELECT a AS v FROM #made;"));
    }

    private static async Task<Row[]> Run(string sql)
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        await evaluator.Evaluate(new Lexer(sql).TokenizeToScript());
        return evaluator.LastResult!.Rows.ToArray();
    }
}
