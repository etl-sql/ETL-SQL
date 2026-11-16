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
/// <c>SEMI JOIN</c> and <c>ANTI JOIN</c> matched a <c>NULL</c> key to a <c>NULL</c> key and ignored
/// every <c>ON</c> predicate that was not a key equality. A <c>WHERE</c> predicate on a joined
/// table was applied to the first table when both had a column of that name.
/// </summary>
public sealed class JoinSemanticsTests
{
    // #a: (1,10) (2,20) (NULL,30)      #b: (1,10,1) (2,99,0) (NULL,30,1)
    private const string Setup =
        "CREATE TABLE #a (id INT, x INT); INSERT INTO #a VALUES (1, 10), (2, 20), (NULL, 30); " +
        "CREATE TABLE #b (id INT, x INT, flag INT); INSERT INTO #b VALUES (1, 10, 1), (2, 99, 0), (NULL, 30, 1); ";

    [Theory]
    // A NULL key matches nothing.
    [InlineData("SELECT a.x AS v FROM #a a SEMI JOIN #b b ON a.id = b.id ORDER BY v;", "10,20")]
    [InlineData("SELECT a.x AS v FROM #a a LEFT SEMI JOIN #b b ON a.id = b.id ORDER BY v;", "10,20")]
    [InlineData("SELECT a.x AS v FROM #a a ANTI JOIN #b b ON a.id = b.id ORDER BY v;", "30")]
    [InlineData("SELECT a.x AS v FROM #a a LEFT ANTI JOIN #b b ON a.id = b.id ORDER BY v;", "30")]
    // The rest of the ON clause counts.
    [InlineData("SELECT a.x AS v FROM #a a SEMI JOIN #b b ON a.id = b.id AND b.flag = 1 ORDER BY v;", "10")]
    [InlineData("SELECT a.x AS v FROM #a a ANTI JOIN #b b ON a.id = b.id AND b.flag = 1 ORDER BY v;", "20,30")]
    [InlineData("SELECT a.x AS v FROM #a a SEMI JOIN #b b ON a.id = b.id AND a.x = b.x ORDER BY v;", "10")]
    [InlineData("SELECT a.x AS v FROM #a a ANTI JOIN #b b ON a.id = b.id AND a.x = b.x ORDER BY v;", "20,30")]
    [InlineData("SELECT a.x AS v FROM #a a SEMI JOIN #b b ON a.id = b.id AND a.x < b.x ORDER BY v;", "20")]
    public async Task SemiAndAntiJoinFollowTheWholeOnClause(string sql, string expected)
    {
        Assert.Equal(expected, await Values(Setup + sql));
    }

    [Theory]
    // b.x is not a.x: a predicate on the joined table must not filter the first table.
    [InlineData("SELECT a.id AS v FROM #a a JOIN #b b ON a.id = b.id WHERE b.x = 99;", "2")]
    [InlineData("SELECT a.id AS v FROM #a a INNER JOIN #b b ON a.id = b.id WHERE b.x = 99 ORDER BY v;", "2")]
    [InlineData("SELECT a.id AS v FROM #a a, #b b WHERE a.id = b.id AND b.x = 99;", "2")]
    [InlineData("SELECT a.id AS v FROM #a a CROSS JOIN #b b WHERE a.id = b.id AND b.x = 99;", "2")]
    [InlineData("SELECT a.id AS v FROM #a a JOIN #b b ON a.id = b.id WHERE a.x = 20;", "2")]
    [InlineData("SELECT a.id AS v FROM #a a JOIN #b b ON a.id = b.id WHERE a.x = 20 AND b.x = 99;", "2")]
    [InlineData("SELECT a.id AS v FROM #a a JOIN #b b ON a.id = b.id WHERE b.x = 20;", "")]
    [InlineData("SELECT a.id AS v FROM #a a JOIN #b b ON a.id = b.id JOIN #b c ON c.id = a.id WHERE c.x = 99 AND b.flag = 0;", "2")]
    [InlineData("SELECT COUNT(*) AS v FROM #a a JOIN #b b ON a.id = b.id WHERE b.x = 99;", "1")]
    public async Task AWherePredicateFiltersTheTableItNames(string sql, string expected)
    {
        Assert.Equal(expected, await Values(Setup + sql));
    }

    [Theory]
    [InlineData("INNER MERGE JOIN")]
    [InlineData("INNER HASH JOIN")]
    [InlineData("INNER LOOP JOIN")]
    [InlineData("INNER JOIN")]
    public async Task ACompositeKeyJoinMatchesOnEveryKey(string join)
    {
        const string setup =
            "CREATE TABLE #l (k1 INT, k2 INT, v INT); INSERT INTO #l VALUES (2, 1, 21), (1, 2, 12), (1, 1, 11), (2, 2, 22), (1, NULL, 19); " +
            "CREATE TABLE #r (k1 INT, k2 INT, w INT); INSERT INTO #r VALUES (2, 2, 220), (1, 1, 110), (1, 2, 120), (3, 1, 310), (1, NULL, 190); ";
        var sql = $"SELECT l.v + r.w AS v FROM #l l {join} #r r ON l.k1 = r.k1 AND l.k2 = r.k2 ORDER BY v;";
        Assert.Equal("121,132,242", await Values(setup + sql));
    }

    [Theory]
    // #t holds n = 1..600 and g = n % 4. The right side is large enough to spill.
    [InlineData("SEMI JOIN", 300)]
    [InlineData("LEFT SEMI JOIN", 300)]
    [InlineData("ANTI JOIN", 300)]
    [InlineData("LEFT ANTI JOIN", 300)]
    public async Task ASpilledSemiOrAntiJoinReturnsEachLeftRowAtMostOnce(string join, int expected)
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        evaluator.JoinSpillThreshold = 50;
        var values = string.Join(", ", Enumerable.Range(1, 600).Select(n => $"({n}, {n % 4})"));
        await evaluator.Evaluate(new Lexer($"CREATE TABLE #t (n INT, g INT); INSERT INTO #t VALUES {values};").TokenizeToScript());
        await evaluator.Evaluate(new Lexer(
            $"SELECT a.n AS v, a.g AS g FROM #t a {join} (SELECT g FROM #t WHERE g < 2) b ON a.g = b.g;").TokenizeToScript());

        var rows = evaluator.LastResult!.Rows.ToArray();
        Assert.Equal(expected, rows.Length);
        Assert.Equal(expected, rows.Select(r => Convert.ToInt32(r["v"])).Distinct().Count());
        bool semi = join.Contains("SEMI");
        Assert.All(rows, r => Assert.Equal(semi, Convert.ToInt32(r["g"]) < 2));
    }

    private static async Task<string> Values(string sql)
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        await evaluator.Evaluate(new Lexer(sql).TokenizeToScript());
        return string.Join(",", evaluator.LastResult!.Rows.Select(r => Convert.ToDecimal(r["v"]).ToString("0")));
    }
}
